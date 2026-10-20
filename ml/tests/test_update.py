import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from datetime import datetime, timezone
from f1ml.source import retry_delay
from f1ml.atomic import write_json
from f1ml.update import run_update, update_lock, UpdateBusy


class UpdateTests(unittest.TestCase):
    def test_retry_after_seconds_and_http_dates_are_respected(self):
        now = datetime(2026,10,20,12,0,0,tzinfo=timezone.utc)
        self.assertEqual(retry_delay('3600',0,now),3600)
        self.assertEqual(retry_delay('Tue, 20 Oct 2026 12:00:30 GMT',0,now),30)
        self.assertEqual(retry_delay('Tue, 20 Oct 2026 11:00:00 GMT',0,now),0)
        self.assertEqual(retry_delay('invalid',1,now),4)

    def test_rate_limit_has_a_safe_public_error_code(self):
        import subprocess
        with tempfile.TemporaryDirectory() as directory:
            root = self.project(directory)
            def busy(*_): raise subprocess.CalledProcessError(3,['private-command'])
            with self.assertRaises(subprocess.CalledProcessError): run_update(root,runner=busy)
            state=json.loads((root/'.local/forecast-update.json').read_text(encoding='utf-8'))
            self.assertEqual(state['error'],'source-busy')

    def project(self, directory):
        root = Path(directory)
        import os
        python = root/('ml/.venv/Scripts/python.exe' if os.name == 'nt' else 'ml/.venv/bin/python')
        python.parent.mkdir(parents=True)
        python.touch()
        api = root/'backend/F1RaceLens.Api/bin/Debug/net10.0/F1RaceLens.Api.dll'
        api.parent.mkdir(parents=True)
        api.touch()
        return root

    def test_success_runs_only_expected_commands_and_publishes_timestamp(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.project(directory)
            calls = []
            result = run_update(root, runner=lambda command, cwd: calls.append(command))
            self.assertEqual(result['state'], 'succeeded')
            self.assertEqual(len(calls), 4)
            self.assertIn('Start.ps1', calls[0][-1])
            self.assertEqual(calls[1][-1], '--refresh-data')
            self.assertIn('--refresh-current', calls[2])
            self.assertEqual(calls[3][-1], '--publish-forecasts')
            self.assertEqual(result['lastSuccessfulAt'], result['finishedAt'])

    def test_failure_stops_following_stages_and_retains_previous_success_without_leaking_errors(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.project(directory)
            success = run_update(root, runner=lambda *_: None)['lastSuccessfulAt']
            calls = []
            def fail(command, cwd):
                calls.append(command)
                if len(calls) == 3:
                    raise RuntimeError('password=DO-NOT-PUBLISH')
            with self.assertRaises(RuntimeError):
                run_update(root, runner=fail)
            text = (root/'.local/forecast-update.json').read_text(encoding='utf-8')
            result = json.loads(text)
            self.assertEqual(result['state'], 'failed')
            self.assertEqual(result['stage'], 'ml')
            self.assertEqual(result['lastSuccessfulAt'], success)
            self.assertEqual(len(calls), 3)
            self.assertNotIn('DO-NOT-PUBLISH', text)
            self.assertEqual(run_update(root, runner=lambda *_: None)['state'], 'succeeded')

    def test_parallel_run_refused_without_modifying_status_and_os_lock_released(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.project(directory)
            path = root/'.local/forecast-update.json'
            write_json(path, {'state':'running'})
            before = path.read_bytes()
            with update_lock(root/'.local/forecast-update.lock'):
                with self.assertRaises(UpdateBusy):
                    run_update(root, runner=lambda *_: self.fail('Must not run'))
            self.assertEqual(before, path.read_bytes())
            with update_lock(root/'.local/forecast-update.lock'):
                pass

    def test_failed_atomic_replace_preserves_previous_artifact(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'forecast.json'
            write_json(path, {'valid':True})
            before = path.read_bytes()
            with patch('f1ml.atomic.os.replace', side_effect=OSError('locked')):
                with self.assertRaises(OSError):
                    write_json(path, {'valid':False})
            self.assertEqual(before, path.read_bytes())
            self.assertEqual([p.name for p in path.parent.iterdir()], ['forecast.json'])

    def test_missing_dependencies_mark_failure_before_running_commands(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                run_update(Path(directory), runner=lambda *_: self.fail('Must not run'))
            self.assertEqual(json.loads((Path(directory)/'.local/forecast-update.json').read_text())['stage'], 'preflight')
