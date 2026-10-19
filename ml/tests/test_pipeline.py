import copy
import unittest

import numpy as np
import pandas as pd

from f1ml.features import FEATURES, build_features
from f1ml.source import normalize
from f1ml.training import rank_field


def race(season: int, round_number: int) -> list[dict]:
    return [{"season": season, "round": round_number, "date": f"{season}-03-01",
             "race_name": "Test", "circuit": "test", "driver": f"driver{i}", "name": f"Driver {i}",
             "team": f"team{i // 2}", "position": i + 1, "classified": i != 9, "points": 0.0}
            for i in range(10)]


class FeatureTests(unittest.TestCase):
    def test_future_labels_do_not_change_earlier_features(self):
        records = race(2022, 1) + race(2022, 2) + race(2023, 1)
        before = build_features(records)
        changed = copy.deepcopy(records)
        for row in changed[20:]:
            row["position"] = 11 - row["position"]
            row["classified"] = not row["classified"]
            row["points"] = 999
        after = build_features(changed)
        pd.testing.assert_frame_equal(before[FEATURES], after[FEATURES])

    def test_current_race_result_does_not_enter_own_features(self):
        records = race(2022, 1)
        before = build_features(records)
        for row in records:
            row["position"] = 11 - row["position"]
            row["classified"] = False
        pd.testing.assert_frame_equal(before[FEATURES], build_features(records)[FEATURES])
        self.assertTrue(before["driver_starts"].eq(0).all())
        self.assertTrue(before["team_recent_pace"].eq(0.55).all())

    def test_state_advances_only_between_races_and_years(self):
        frame = build_features(race(2022, 1) + race(2022, 2) + race(2023, 1))
        self.assertTrue(frame.loc[frame["group"].eq(202202), "driver_starts"].eq(1).all())
        next_year = frame[frame["season"].eq(2023)]
        self.assertTrue(next_year["season_starts"].eq(0).all())
        self.assertTrue(next_year["driver_starts"].eq(2).all())
        self.assertTrue(next_year["track_samples"].eq(20).all())
        self.assertTrue(frame.loc[frame["season"].eq(2022), "track_samples"].eq(0).all())

    def test_result_columns_are_not_inputs(self):
        for forbidden in ["position", "classified", "points", "date", "group", "name", "season"]:
            self.assertNotIn(forbidden, FEATURES)
        self.assertFalse(build_features(race(2022, 1))[FEATURES].isna().any().any())

    def test_rank_output_unique_and_risk_changes_order(self):
        frame = build_features(race(2022, 1))
        ordered = rank_field(frame, np.arange(10.0, 0, -1), np.zeros(10))
        self.assertEqual(sorted(ordered["predicted_position"]), list(range(1, 11)))
        risk = np.zeros(10)
        risk[0] = 1
        changed = rank_field(frame, np.arange(10.0, 0, -1), risk)
        self.assertEqual(changed.iloc[-1]["driver"], "driver0")


class SourceTests(unittest.TestCase):
    def page(self):
        results = [{"position": str(i + 1), "positionText": str(i + 1), "points": "0",
                    "Driver": {"driverId": f"driver{i}", "givenName": "Driver", "familyName": str(i)},
                    "Constructor": {"constructorId": f"team{i // 2}"}} for i in range(10)]
        return {"MRData": {"total": "10", "offset": "0", "RaceTable": {"season": "2022", "Races": [
            {"season": "2022", "round": "1", "raceName": "Test", "date": "2022-03-01",
             "Circuit": {"circuitId": "test"}, "Results": results}]}}}

    def test_split_race_across_pages_is_reassembled(self):
        first = self.page()
        second = self.page()
        first["MRData"]["RaceTable"]["Races"][0]["Results"] = first["MRData"]["RaceTable"]["Races"][0]["Results"][:6]
        second["MRData"]["offset"] = "6"
        second["MRData"]["RaceTable"]["Races"][0]["Results"] = second["MRData"]["RaceTable"]["Races"][0]["Results"][6:]
        self.assertEqual(len(normalize([first, second], 2022)), 10)

    def test_duplicates_and_incomplete_snapshot_rejected(self):
        page = self.page()
        page["MRData"]["RaceTable"]["Races"][0]["Results"][-1]["Driver"]["driverId"] = "driver0"
        with self.assertRaises(ValueError):
            normalize([page], 2022)
        page = self.page()
        page["MRData"]["total"] = "11"
        with self.assertRaises(ValueError):
            normalize([page], 2022)

    def test_wrong_year_and_invalid_positions_rejected(self):
        with self.assertRaises(ValueError):
            normalize([self.page()], 2023)
        page = self.page()
        page["MRData"]["RaceTable"]["Races"][0]["Results"][0]["position"] = "2"
        with self.assertRaises(ValueError):
            normalize([page], 2022)


if __name__ == "__main__":
    unittest.main()
