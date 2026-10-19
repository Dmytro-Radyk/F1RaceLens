"""Reload actual LightGBM files and independently reproduce audit metrics."""
import hashlib
import json
import unittest
from pathlib import Path
from f1ml.models import LightModel
from f1ml.features import build_features
from f1ml.source import normalize
from f1ml.training import evaluate
ROOT = Path(__file__).resolve().parents[1]

@unittest.skipUnless((ROOT/'models/lightgbm-audit-2025/metadata.json').exists(), 'Train LightGBM first')
class ArtifactTests(unittest.TestCase):
    def test_saved_models_reproduce_report_and_hashes(self):
        directory = ROOT/'models/lightgbm-audit-2025'
        report = json.loads((directory/'metadata.json').read_text(encoding='utf-8'))
        records = []
        for year in range(2010,2026):
            raw = json.loads((ROOT/f'data/raw/results-{year}.json').read_text(encoding='utf-8'))
            records.extend(normalize(raw['pages'], year))
        frame = build_features(records)
        pace = LightModel.load(directory/'pace.txt')
        risk = LightModel.load(directory/'nonclassification.txt')
        actual = evaluate(frame[frame.season.eq(2025)], pace, risk, report['calibration'])
        for name in ['rank_mae','winner_accuracy','nonclass_brier','nonclass_logloss']:
            self.assertAlmostEqual(actual[name], report['evaluation'][name], places=12)
        for name, digest in report['artifact_sha256'].items():
            self.assertEqual(hashlib.sha256((directory/name).read_bytes()).hexdigest(),digest)

@unittest.skipUnless((ROOT / "reports/season-forecast-lightgbm-v1.json").exists(), "Generate season forecasts first")
class SeasonArtifactTests(unittest.TestCase):
    def test_forecasts_reference_unchanged_models_and_only_future_races(self):
        for version in ("lightgbm-v1", "lightgbm-v2", "lightgbm-v3", "lightgbm-v4"):
            if not (ROOT / f"reports/season-forecast-{version}.json").exists():
                continue
            report = json.loads((ROOT / f"reports/season-forecast-{version}.json").read_text(encoding="utf-8"))
            directory = ROOT / f"models/{version}"
            for name, expected_hash in report["artifact_sha256"].items():
                self.assertEqual(hashlib.sha256((directory / name).read_bytes()).hexdigest(), expected_hash)
            self.assertEqual(len(report["races"]), report["remaining_races"])
            self.assertTrue(all(r["round"] > report["through_round"] for r in report["races"]))
            self.assertEqual(len({r["round"] for r in report["races"]}), report["remaining_races"])

    def test_season_probabilities_and_future_team_points_are_consistent(self):
        for version in ("lightgbm-v1", "lightgbm-v2", "lightgbm-v3", "lightgbm-v4"):
            if not (ROOT / f"reports/season-forecast-{version}.json").exists():
                continue
            report = json.loads((ROOT / f"reports/season-forecast-{version}.json").read_text(encoding="utf-8"))
            for kind in ("drivers", "teams"):
                self.assertAlmostEqual(sum(r["title_chance"] for r in report[kind]), 100)
                self.assertTrue(all(r["expected_points"] >= r["points"] for r in report[kind]))
            driver_gain = sum(r["expected_points"] - r["points"] for r in report["drivers"])
            team_gain = sum(r["expected_points"] - r["points"] for r in report["teams"])
            self.assertAlmostEqual(driver_gain, team_gain)
            for race in report["races"]:
                self.assertAlmostEqual(sum(r["win_chance"] for r in race["drivers"]), 100)
