import importlib.util
import tempfile
import unittest
from pathlib import Path

import numpy as np

from f1ml.features import FEATURES, build_features
from f1ml.models import LightModel, fit_model
from f1ml.training import probability
from test_pipeline import race


class LightModelTests(unittest.TestCase):
    def test_unicode_artifacts_roundtrip_unknown_categories_and_row_order(self):
        frame = build_features([r for i in range(1, 13) for r in race(2025, i)])
        model = fit_model(frame, frame.position/frame.field_size, 'regressor', rounds=8)
        future = frame.iloc[:10].copy()
        future.loc[:, 'driver'] = 'new-driver'
        expected = model.predict(future)
        self.assertTrue(np.isfinite(expected).all())
        self.assertTrue(model.encode(future).driver.isna().all())
        np.testing.assert_allclose(model.predict(future.iloc[::-1])[::-1], expected)
        temp_root = Path(__file__).resolve().parents[1]/'.cache/tmp'
        temp_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temp_root) as directory:
            path = Path(directory)/'модель-темпу.txt'
            model.save(path)
            loaded = LightModel.load(path)
            np.testing.assert_array_equal(loaded.predict(future), expected)
            self.assertEqual(loaded.kind, 'regressor')

    def test_unsupported_engine_not_installed(self):
        self.assertIsNone(importlib.util.find_spec('catboost'))

    def test_saved_blend_uses_only_pre_race_features_and_preserves_outputs(self):
        frame = build_features([r for i in range(1,13) for r in race(2025,i)])
        pace = fit_model(frame,frame.position/frame.field_size,'regressor',rounds=8)
        raw = pace.predict(frame)
        pace.baseline_weight = .25
        expected = .75*raw+.25*frame.baseline_pace.to_numpy()
        np.testing.assert_allclose(pace.predict(frame),expected)
        temp_root = Path(__file__).resolve().parents[1]/'.cache/tmp'
        with tempfile.TemporaryDirectory(dir=temp_root) as directory:
            path = Path(directory)/'blend.txt'
            pace.save(path)
            restored = LightModel.load(path)
            np.testing.assert_array_equal(restored.predict(frame),pace.predict(frame))
            changed = frame.copy()
            changed['position'] = 999
            changed['classified'] = False
            changed['points'] = 999
            np.testing.assert_array_equal(restored.predict(changed),restored.predict(frame))
        risk = fit_model(frame,(~frame.classified).astype(int),'classifier',rounds=8)
        calibration = {'slope':1.,'intercept':0.}
        original = probability(risk,calibration,frame)
        mixed = probability(risk,{**calibration,'baseline_weight':.25},frame)
        np.testing.assert_allclose(mixed,.75*original+.25*frame.baseline_nonclass.to_numpy())
        with self.assertRaises(ValueError):
            probability(risk,{**calibration,'baseline_weight':1.5},frame)

    def test_robust_objective_survives_save_and_cannot_replace_binary_target(self):
        frame = build_features([r for i in range(1,13) for r in race(2025,i)])
        with self.assertRaises(ValueError):
            fit_model(frame,(~frame.classified).astype(int),'classifier',settings={'objective':'regression_l1'})
        model = fit_model(frame,frame.position/frame.field_size,'regressor',rounds=8,
                          settings={'objective':'regression_l1'})
        temp_root = Path(__file__).resolve().parents[1]/'.cache/tmp'
        with tempfile.TemporaryDirectory(dir=temp_root) as directory:
            path = Path(directory)/'robust.txt'
            model.save(path)
            loaded = LightModel.load(path)
            self.assertEqual(loaded.booster.params['objective'],'regression_l1')
            np.testing.assert_array_equal(loaded.predict(frame),model.predict(frame))
