import unittest

import numpy as np

from f1ml.features import build_features
from f1ml.search import FOLD_YEARS, choose, split_fold
from test_pipeline import race


class SearchTests(unittest.TestCase):
    def test_training_stopping_and_scoring_use_separate_years(self):
        frame = build_features([r for year in range(2010, 2027) for r in race(year, 1)])
        for years in FOLD_YEARS:
            train, stop, score = split_fold(frame, years)
            self.assertLess(train["season"].max(), stop["season"].min())
            self.assertLess(stop["season"].max(), score["season"].min())
            self.assertLess(score["season"].max(), 2025)

    def test_test_years_cannot_be_selected_as_folds(self):
        frame = build_features(race(2023, 1) + race(2024, 1) + race(2025, 1))
        for years in [(2023, 2024, 2025), (2023, 2023, 2024), (2025, 2024, 2023)]:
            with self.assertRaises(ValueError):
                split_fold(frame, years)

    def test_selection_uses_metric_and_deterministic_ties(self):
        rows = [{"id": "b", "mean_mae": 2.0}, {"id": "a", "mean_mae": 2.0}, {"id": "c", "mean_mae": 3.0}]
        self.assertEqual(choose(rows, "mean_mae")["id"], "a")
        self.assertEqual(choose(list(reversed(rows)), "mean_mae")["id"], "a")

    def test_invalid_selection_metrics_rejected(self):
        for rows in [[], [{"id": "a", "metric": np.nan}], [{"id": "a", "metric": np.inf}]]:
            with self.assertRaises(ValueError):
                choose(rows, "metric")
