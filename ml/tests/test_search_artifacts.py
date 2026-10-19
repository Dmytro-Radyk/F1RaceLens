import hashlib
import json
import unittest
from pathlib import Path

from f1ml.models import LightModel
from f1ml.features import build_features
from f1ml.source import normalize
from f1ml.training import evaluate

ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless((ROOT/'models/lightgbm-v2/metadata.json').exists(), 'Train v2 first')
class SearchArtifactTests(unittest.TestCase):
    version = "v2"
    def test_selection_precedes_audits_and_matches_final_artifacts(self):
        path = ROOT/f'reports/lightgbm-search-{self.version}/selection.json'
        selection = json.loads(path.read_text(encoding='utf-8'))
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        self.assertFalse(selection['audit_used_for_selection'])
        for folds in selection['folds']:
            self.assertLess(folds[0],folds[1])
            self.assertLess(folds[1],folds[2])
            self.assertLess(folds[2],2025)
        metadata = json.loads((ROOT/f'models/lightgbm-{self.version}/metadata.json').read_text(encoding='utf-8'))
        self.assertEqual(metadata['selection_sha256'],digest)
        self.assertEqual(metadata['settings'],selection['selected'])
        for cutoff in metadata['cutoffs']:
            self.assertLess(cutoff['latest_training_round_in_current_season'],cutoff['forecast_round'])
        pace = LightModel.load(ROOT/f'models/lightgbm-{self.version}/pace.txt')
        risk = LightModel.load(ROOT/f'models/lightgbm-{self.version}/nonclassification.txt')
        self.assertEqual(pace.tree_count_,selection['selected']['pace_rounds'])
        self.assertEqual(risk.tree_count_,selection['selected']['risk_rounds'])

    def test_audit_reloads_with_matching_hashes_and_metrics(self):
        directory = ROOT/f'models/lightgbm-{self.version}-audit-2025'
        metadata = json.loads((directory/'metadata.json').read_text(encoding='utf-8'))
        records = []
        for year in range(2010,2026):
            raw = json.loads((ROOT/f'data/raw/results-{year}.json').read_text(encoding='utf-8'))
            records.extend(normalize(raw['pages'],year))
        frame = build_features(records)
        pace = LightModel.load(directory/'pace.txt')
        risk = LightModel.load(directory/'nonclassification.txt')
        metrics = evaluate(frame[frame.season.eq(2025)],pace,risk,metadata['calibration'])
        for key in ['rank_mae','winner_accuracy','nonclass_brier','nonclass_logloss']:
            self.assertAlmostEqual(metrics[key],metadata['evaluation'][key],places=12)
        for name,digest in metadata['artifact_sha256'].items():
            self.assertEqual(hashlib.sha256((directory/name).read_bytes()).hexdigest(),digest)


@unittest.skipUnless((ROOT/"models/lightgbm-v3/metadata.json").exists(), "Train v3 first")
class SearchV3ArtifactTests(SearchArtifactTests):
    version = "v3"


@unittest.skipUnless((ROOT/"models/lightgbm-v4/metadata.json").exists(), "Train v4 first")
class SearchV4ArtifactTests(SearchArtifactTests):
    version = "v4"
