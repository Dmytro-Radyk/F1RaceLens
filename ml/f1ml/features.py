"""Every feature is calculated before appending any result from its race."""

from collections import defaultdict
from statistics import mean

import pandas as pd

CATEGORICAL = ["driver", "team", "circuit", "rules_era"]
NUMERIC = [
    "round", "field_size", "driver_starts", "driver_recent_pace", "driver_season_pace",
    "driver_recent_nonclass", "driver_season_nonclass", "team_recent_pace", "team_recent_nonclass",
    "driver_team_pace", "driver_team_starts", "season_starts", "track_nonclass", "track_samples",
    "driver_track_residual", "driver_track_samples", "form_change", "previous_season_pace",
]
FEATURES = CATEGORICAL + NUMERIC


def pace(rows: list[dict], fallback: float = 0.55) -> float:
    values = [r["normalized_position"] for r in rows if r["classified"]]
    return mean(values) if values else fallback


def nonclass(rows: list[dict], fallback: float = 0.15) -> float:
    # Shrink small samples towards a fixed prior, not a future global mean.
    return (sum(not r["classified"] for r in rows) + 5 * fallback) / (len(rows) + 5)


def build_features(records: list[dict]) -> pd.DataFrame:
    races = defaultdict(list)
    for row in records:
        races[(row["season"], row["round"])].append(dict(row))
    driver_history = defaultdict(list)
    team_history = defaultdict(list)
    circuit_history = defaultdict(list)
    output = []
    for (season, round_number), field in sorted(races.items()):
        # All rows in a race see precisely the same historical cutoff.
        for row in sorted(field, key=lambda r: r["driver"]):
            driver = driver_history[row["driver"]]
            team = team_history[row["team"]]
            recent = driver[-5:]
            team_recent = team[-6:]
            current = [r for r in driver if r["season"] == season]
            own_team = [r for r in driver if r["team"] == row["team"]][-10:]
            previous = [r for r in driver if r["season"] == season - 1]
            track = [r for r in circuit_history[row["circuit"]] if season - 2 <= r["season"] < season]
            own_track = [r for r in track if r["driver"] == row["driver"] and r["classified"]]
            team_pace = pace(team_recent)
            driver_pace = pace(recent, team_pace)
            season_pace = pace(current, driver_pace)
            # Stored residuals use the team pace known BEFORE their historical event.
            residual = sum(r["track_residual"] for r in own_track) / (len(own_track) + 5)
            row_features = {
                **row, "group": season * 100 + round_number, "field_size": len(field),
                "rules_era": "ground-effect" if season >= 2022 else "hybrid" if season >= 2014 else "v8",
                "driver_starts": len(driver), "driver_recent_pace": driver_pace,
                "driver_season_pace": season_pace, "driver_recent_nonclass": nonclass(recent),
                "driver_season_nonclass": nonclass(current), "team_recent_pace": team_pace,
                "team_recent_nonclass": nonclass(team_recent), "driver_team_pace": pace(own_team, team_pace),
                "driver_team_starts": len(own_team), "season_starts": len(current),
                "track_nonclass": nonclass(track), "track_samples": len(track),
                "driver_track_residual": residual, "driver_track_samples": len(own_track),
                "form_change": driver_pace - season_pace, "previous_season_pace": pace(previous, team_pace),
                "baseline_pace": 0.5 * season_pace + 0.5 * driver_pace,
                "baseline_nonclass": 0.5 * nonclass(current) + 0.5 * nonclass(recent),
            }
            output.append(row_features)
        # Atomic state advance only AFTER the whole field has been featurized.
        for row in field:
            row["normalized_position"] = row["position"] / len(field)
            row["track_residual"] = row["normalized_position"] - pace(team_history[row["team"]][-6:])
        for row in field:
            driver_history[row["driver"]].append(row)
            team_history[row["team"]].append(row)
            circuit_history[row["circuit"]].append(row)
    return pd.DataFrame(output)
