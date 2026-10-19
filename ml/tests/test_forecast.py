import copy
import unittest

import numpy as np

from f1ml.forecast.engine import championship_ranks, simulate
from f1ml.forecast.source import parse_standings


def inputs():
    drivers = [{"id": f"d{i}", "name": f"Driver {i}", "position": i + 1, "points": 0.0} for i in range(4)]
    teams = [{"id": f"t{i}", "name": f"Team {i}", "position": i + 1, "points": 0.0} for i in range(2)]
    snapshot = {"season": 2026, "through": 3, "drivers": drivers, "teams": teams}
    roster = [{"driver": f"d{i}", "name": f"Driver {i}", "team": f"t{i // 2}"} for i in range(4)]
    event = {"round": 4, "sprint": False, "name": "Next Grand Prix", "date": "2026-11-01",
             "pace": [1, 2, 3, 4], "risk": [0, 0, 0, 0]}
    return snapshot, roster, event


class SeasonForecastTests(unittest.TestCase):
    def test_official_points_are_starting_point_and_teams_receive_future_car_points(self):
        snapshot, roster, event = inputs()
        snapshot["drivers"][0]["points"] = 12
        snapshot["teams"][0]["points"] = 99  # Past team sanctions/transfers need not equal current pair's points.
        before = copy.deepcopy(snapshot)
        result = simulate(snapshot, [], roster, [event], [0], [0], simulations=20)
        drivers = {r["id"]: r for r in result["drivers"]}
        teams = {r["id"]: r for r in result["teams"]}
        self.assertEqual(drivers["d0"]["expected_points"], 37)
        self.assertEqual(teams["t0"]["expected_points"], 99 + 25 + 18)
        self.assertEqual(snapshot, before)

    def test_sprint_points_separate_and_not_counted_as_grand_prix(self):
        snapshot, roster, event = inputs()
        sprint = {**event, "sprint": True}
        result = simulate(snapshot, [], roster, [event, sprint], [0], [0], simulations=10)
        self.assertEqual(result["drivers"][0]["expected_points"], 33)
        self.assertEqual(result["remaining_races"], 1)
        self.assertEqual(result["remaining_sprints"], 1)
        self.assertEqual(len(result["races"]), 1)

    def test_nonclassification_does_not_award_points_or_race_win(self):
        snapshot, roster, event = inputs()
        event["risk"] = [1, 0, 0, 0]
        result = simulate(snapshot, [], roster, [event], [0], [0], simulations=10)
        d0 = next(r for r in result["drivers"] if r["id"] == "d0")
        winner = next(r for r in result["races"][0]["drivers"] if r["win_chance"] == 100)
        self.assertEqual(d0["expected_points"], 0)
        self.assertEqual(winner["driver"], "d1")

    def test_seed_reproducibility_and_championship_probabilities_sum_to_100(self):
        snapshot, roster, event = inputs()
        event["risk"] = [.2, .1, .1, .1]
        args = (snapshot, [], roster, [event], [-2, 0, 2], [-1, 0, 1])
        a = simulate(*args, simulations=300, seed=7)
        b = simulate(*args, simulations=300, seed=7)
        self.assertEqual(a, b)
        self.assertAlmostEqual(sum(r["title_chance"] for r in a["drivers"]), 100)
        self.assertAlmostEqual(sum(r["title_chance"] for r in a["teams"]), 100)

    def test_future_history_past_sessions_duplicate_sessions_and_invalid_risk_rejected(self):
        snapshot, roster, event = inputs()
        with self.assertRaises(ValueError):
            simulate(snapshot, [{"season": 2026, "round": 4}], roster, [event], [0], [0])
        with self.assertRaises(ValueError):
            simulate(snapshot, [], roster, [{**event, "round": 3}], [0], [0])
        with self.assertRaises(ValueError):
            simulate(snapshot, [], roster, [event, event], [0], [0])
        with self.assertRaises(ValueError):
            simulate(snapshot, [], roster, [{**event, "risk": [2, 0, 0, 0]}], [0], [0])

    def test_countback_prefers_race_wins_when_points_equal(self):
        points = np.array([[10., 10.]])
        counts = np.zeros((1, 2, 40))
        counts[0, 1, 0] = 1
        ranks = championship_ranks(points, counts, np.random.default_rng(1))
        self.assertEqual(ranks.tolist(), [[2, 1]])

    def test_completed_season_uses_official_order_without_new_points(self):
        snapshot, roster, _ = inputs()
        result = simulate(snapshot, [], roster, [], [0], [0], simulations=10)
        self.assertEqual(result["status"], "completed")
        self.assertEqual(result["drivers"][0]["title_chance"], 100)
        self.assertTrue(all(r["expected_points"] == 0 for r in result["drivers"]))


class ForecastSourceTests(unittest.TestCase):
    def test_standings_must_match_observed_round_and_not_be_truncated(self):
        page = {"MRData": {"offset": "0", "total": "1", "StandingsTable": {"season": "2026", "StandingsLists": [
            {"season": "2026", "round": "3", "DriverStandings": [{"position": "1", "positionText": "1", "points": "52",
             "Driver": {"driverId": "d0", "givenName": "Driver", "familyName": "Zero"}}]}]}}}
        rows = parse_standings(page, 2026, 3, False)
        self.assertEqual(rows[0]["points"], 52)
        with self.assertRaises(ValueError):
            parse_standings(page, 2026, 4, False)
        page["MRData"]["total"] = "2"
        with self.assertRaises(ValueError):
            parse_standings(page, 2026, 3, False)
