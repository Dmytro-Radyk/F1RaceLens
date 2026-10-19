import json
import unittest
from pathlib import Path

import numpy as np

from f1ml.adaptation.train import known_before, risk_adjustment
from f1ml.features import build_features
from test_pipeline import race

ROOT = Path(__file__).resolve().parents[1]


class AdaptationTests(unittest.TestCase):
    def test_cutoff_excludes_current_race_and_all_future_labels(self):
        frame = build_features(race(2025, 1)+race(2026, 1)+race(2026, 2)+race(2026, 3))
        known = known_before(frame, 2026, 2)
        self.assertEqual(len(known), 20)
        self.assertEqual(known.loc[known.season.eq(2026),'round'].max(),1)
        self.assertFalse(((known.season.eq(2026)) & (known['round']>=2)).any())

    def test_rate_adapts_to_past_nonclassification_and_stays_finite(self):
        frame = build_features(race(2025, 1)+race(2026, 1))
        early = risk_adjustment(frame[frame.season.eq(2025)], 2026, np.full(10,.1))
        changed = frame.copy()
        changed.loc[changed.season.eq(2026),'classified'] = False
        later = risk_adjustment(changed, 2026, np.full(10,.1))
        self.assertGreater(later['intercept'],early['intercept'])
        self.assertEqual(later['observed_entries'],10)
        self.assertTrue(np.isfinite(later['intercept']))


@unittest.skipUnless((ROOT/'models/lightgbm-v1/metadata.json').exists(),'Train LightGBM first')
class AdaptationArtifactTests(unittest.TestCase):
    def test_each_evaluated_race_precedes_its_training_update(self):
        metadata=json.loads((ROOT/'models/lightgbm-v1/metadata.json').read_text(encoding='utf-8'))
        for cutoff in metadata['cutoffs']:
            self.assertLess(cutoff['latest_training_round_in_current_season'],cutoff['forecast_round'])
        self.assertEqual(metadata['evaluation']['races'],len(metadata['cutoffs']))
        self.assertEqual(metadata['training_cutoff_round'],max(r['forecast_round'] for r in metadata['cutoffs']))
        self.assertGreater(metadata['training_rows'],metadata['cutoffs'][-1]['training_rows'])
