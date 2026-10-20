"""Explicit local update workflow. No scheduler, training or public write endpoint."""
import json
import os
import subprocess
import shutil
import sys
import uuid
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path
from .atomic import write_json


class UpdateBusy(Exception):
    pass


@contextmanager
def update_lock(path):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('a+b') as stream:
        stream.seek(0, os.SEEK_END)
        if not stream.tell():
            stream.write(b'0')
            stream.flush()
        stream.seek(0)
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as error:
            raise UpdateBusy('Another update is already running') from error
        try:
            yield
        finally:
            stream.seek(0)
            if os.name == 'nt':
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)


def stamp():
    return datetime.now(timezone.utc).isoformat()


def previous_success(path):
    try:
        value = json.loads(path.read_text(encoding='utf-8')).get('lastSuccessfulAt')
        time = datetime.fromisoformat(value)
        return time.astimezone(timezone.utc).isoformat() if time.tzinfo and time <= datetime.now(timezone.utc) else None
    except (OSError, ValueError, TypeError, AttributeError):
        return None


def run_update(project, runner=None):
    project = Path(project).resolve()
    local = project/'.local'
    status_file = local/'forecast-update.json'
    with update_lock(local/'forecast-update.lock'):
        state = {'state':'running', 'stage':'preflight', 'startedAt':stamp(), 'updatedAt':stamp(),
                 'finishedAt':None, 'lastSuccessfulAt':previous_success(status_file), 'error':None}
        write_json(status_file, state)
        run_id = uuid.uuid4().hex
        try:
            python = project/'ml/.venv/Scripts/python.exe' if os.name == 'nt' else project/'ml/.venv/bin/python'
            if not python.is_file() or not (project/'backend/F1RaceLens.Api/bin/Debug/net10.0/F1RaceLens.Api.dll').is_file():
                raise FileNotFoundError('Build the backend and set up the ML environment first')
            if datetime.now(timezone.utc).year != 2026:
                raise ValueError('Forecast points rules currently support 2026 only')
            # Run the DLL directly: a timeout kills the actual job, not a dotnet-run parent with an orphaned child.
            api = ['dotnet', str(project/'backend/F1RaceLens.Api/bin/Debug/net10.0/F1RaceLens.Api.dll')]
            api_directory = project/'backend/F1RaceLens.Api'
            powershell = 'pwsh' if shutil.which('pwsh') else 'powershell'
            steps = [('preflight', [powershell,'-NoProfile','-File',str(project/'scripts/database/Start.ps1')], project),
                     ('data', api+['--refresh-data'], api_directory),
                     ('ml', [str(python),'-u','-m','f1ml.cli','forecast','--model-version','lightgbm-v4','--refresh-current'], project/'ml'),
                     ('publish', api+['--publish-forecasts'], api_directory)]
            log_dir = local/'update-logs'
            log_dir.mkdir(parents=True, exist_ok=True)
            for stage, command, directory in steps:
                state.update(stage=stage, updatedAt=stamp())
                write_json(status_file, state)
                print(f'Updating: {stage}', flush=True)
                log_file = log_dir/f'{run_id}-{stage}.log'
                if runner:
                    runner(command, directory)
                else:
                    with log_file.open('w', encoding='utf-8') as log:
                        subprocess.run(command, cwd=directory, check=True, timeout=900, stdout=log, stderr=subprocess.STDOUT)
            state.update(state='succeeded', stage='complete', finishedAt=stamp(), updatedAt=stamp())
            state['lastSuccessfulAt'] = state['finishedAt']
            write_json(status_file, state)
            print('Update completed. Both forecasts and evaluations are published.', flush=True)
            return state
        except BaseException as error:
            # Public status deliberately contains no exception text, command output or credentials.
            code = 'source-busy' if isinstance(error, subprocess.CalledProcessError) and error.returncode == 3 else 'update-failed'
            state.update(state='failed', error=code, finishedAt=stamp(), updatedAt=stamp())
            write_json(status_file, state)
            raise


if __name__ == '__main__':
    try:
        run_update(Path(__file__).resolve().parents[2])
    except UpdateBusy:
        print('Another update is already running.', file=sys.stderr)
        sys.exit(2)
    except BaseException:
        print('Update failed. Previous published forecasts are preserved; see .local/update-logs for the failed stage.', file=sys.stderr)
        sys.exit(1)
