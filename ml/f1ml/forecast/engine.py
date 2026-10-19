"""Monte Carlo on ML pace and nonclassification risk, starting from official points."""

import json
import hashlib
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import pandas as pd
from ..models import LightModel, artifact_hashes

from ..features import FEATURES, build_features
from ..training import SEED, probability
from .source import fetch_input

RACE_POINTS = [25, 18, 15, 12, 10, 8, 6, 4, 2, 1]
SPRINT_POINTS = [8, 7, 6, 5, 4, 3, 2, 1]


def pace_positions(model, frame: pd.DataFrame) -> np.ndarray:
    scores = np.asarray(model.predict(frame))
    if model.kind == "regressor":
        return np.clip(scores * frame["field_size"].to_numpy(), 1, frame["field_size"].to_numpy())
    series = pd.Series(scores, index=frame.index)
    return series.groupby(frame["group"]).rank(ascending=False, method="average").to_numpy()


def residual_pools(model, frame: pd.DataFrame) -> tuple[np.ndarray, np.ndarray]:
    validation = frame[frame["season"].eq(2024)].copy()
    validation["residual"] = validation["position"] - pace_positions(model, validation)
    classified = validation[validation["classified"]].copy()
    if len(classified) < 100:
        raise ValueError("Not enough historical residuals for season uncertainty")
    grouped = classified.groupby(["group", "team"])["residual"].mean()
    team = grouped.to_numpy(dtype=float)
    individual = (classified["residual"] - classified.groupby(["group", "team"])["residual"].transform("mean")).to_numpy(dtype=float)
    return team - team.mean(), individual - individual.mean()


def championship_ranks(points: np.ndarray, counts: np.ndarray, rng) -> np.ndarray:
    keys = [rng.random(points.shape)] + [-counts[:, :, p] for p in reversed(range(40))] + [-points]
    order = np.lexsort(tuple(keys), axis=1)
    ranks = np.empty_like(order)
    np.put_along_axis(ranks, order, np.arange(1, points.shape[1] + 1)[None, :], axis=1)
    return ranks


def summarise(entries: list[dict], points: np.ndarray, ranks: np.ndarray, active: set[str]) -> list[dict]:
    rows = []
    for i, entry in enumerate(entries):
        rows.append({**entry, "active": entry["id"] in active,
                     "expected_points": float(points[:, i].mean()),
                     "points_low": float(np.quantile(points[:, i], .1)), "points_high": float(np.quantile(points[:, i], .9)),
                     "average_position": float(ranks[:, i].mean()),
                     "position_low": int(np.quantile(ranks[:, i], .1, method="nearest")),
                     "position_high": int(np.quantile(ranks[:, i], .9, method="nearest")),
                     "title_chance": float((ranks[:, i] == 1).mean() * 100),
                     "top3_chance": float((ranks[:, i] <= 3).mean() * 100)})
    return sorted(rows, key=lambda row: (-row["expected_points"], row["average_position"], row["id"]))


def simulate(snapshot: dict, history: list[dict], roster: list[dict], events: list[dict],
             team_residuals, individual_residuals, simulations: int = 4000, seed: int = SEED) -> dict:
    if simulations < 1 or len(roster) < 2 or any(e["round"] <= snapshot["through"] for e in events):
        raise ValueError("Invalid future simulation request")
    if any(r["season"] != snapshot["season"] or r["round"] > snapshot["through"] for r in history):
        raise ValueError("Future or different-season observations in championship history")
    if len({(e["round"], e["sprint"]) for e in events}) != len(events):
        raise ValueError("Duplicate future session")
    if any(not 1 <= r["position"] <= 40 for r in history):
        raise ValueError("Invalid historical classification")
    drivers, teams = snapshot["drivers"], snapshot["teams"]
    dindex = {r["id"]: i for i, r in enumerate(drivers)}
    tindex = {r["id"]: i for i, r in enumerate(teams)}
    if len(dindex) != len(drivers) or len(tindex) != len(teams) or len({r['driver'] for r in roster}) != len(roster):
        raise ValueError("Duplicated simulation participants")
    if any(r["driver"] not in dindex or r["team"] not in tindex for r in roster):
        raise ValueError("Roster differs from standings")
    n = len(roster)
    if any(len(e["pace"]) != n or len(e["risk"]) != n or not np.isfinite(e["pace"]).all()
           or not np.isfinite(e["risk"]).all() or np.any(np.asarray(e["risk"]) < 0) or np.any(np.asarray(e["risk"]) > 1) for e in events):
        raise ValueError("Invalid model output")
    for pool in (team_residuals, individual_residuals):
        if len(pool) == 0 or not np.isfinite(pool).all():
            raise ValueError("Invalid uncertainty pool")
    rng = np.random.default_rng(seed)
    dp = np.tile([r["points"] for r in drivers], (simulations, 1)).astype(float)
    tp = np.tile([r["points"] for r in teams], (simulations, 1)).astype(float)
    dc = np.zeros((simulations, len(drivers), 40), dtype=np.int16)
    tc = np.zeros((simulations, len(teams), 40), dtype=np.int16)
    for r in history:
        if r["classified"]:
            if r["driver"] in dindex:
                dc[:, dindex[r["driver"]], r["position"] - 1] += 1
            if r["team"] in tindex:
                tc[:, tindex[r["team"]], r["position"] - 1] += 1
    ids = np.array([dindex[r["driver"]] for r in roster])
    team_ids = np.array([tindex[r["team"]] for r in roster])
    # A modest persistent team component represents shared remaining-season uncertainty.
    # Correlation .25 is a fixed assumption, not a learned reliability guarantee.
    persistent = rng.choice(team_residuals, size=(simulations, len(teams))) * .5
    sim_ids = np.arange(simulations)
    race_forecasts = []
    for event in sorted(events, key=lambda e: (e["round"], not e["sprint"])):
        shock = persistent + rng.choice(team_residuals, size=(simulations, len(teams))) * np.sqrt(.75)
        scores = np.asarray(event["pace"])[None, :] + shock[:, team_ids] + rng.choice(individual_residuals, size=(simulations, n))
        failed = rng.random((simulations, n)) < np.asarray(event["risk"])[None, :]
        order = np.argsort(scores + failed * 1000, axis=1, kind="stable")
        ranks = np.empty_like(order)
        np.put_along_axis(ranks, order, np.arange(1, n + 1)[None, :], axis=1)
        scoring = np.zeros(n)
        scale = SPRINT_POINTS if event["sprint"] else RACE_POINTS
        scoring[:min(n, len(scale))] = scale[:n]
        earned = scoring[ranks - 1] * ~failed
        for i in range(n):
            dp[:, ids[i]] += earned[:, i]
            tp[:, team_ids[i]] += earned[:, i]
            if not event["sprint"]:
                dc[sim_ids, ids[i], ranks[:, i] - 1] += (~failed[:, i]).astype(np.int16)
                tc[sim_ids, team_ids[i], ranks[:, i] - 1] += (~failed[:, i]).astype(np.int16)
        if not event["sprint"]:
            rows = [{"driver": r["driver"], "name": r["name"], "team": r["team"],
                     "average_position": float(ranks[:, i].mean()), "expected_points": float(earned[:, i].mean()),
                     "win_chance": float(((ranks[:, i] == 1) & ~failed[:, i]).mean() * 100),
                     "podium_chance": float(((ranks[:, i] <= 3) & ~failed[:, i]).mean() * 100),
                     "risk": float(event["risk"][i]),
                     "position_low": int(np.quantile(ranks[:, i], .1, method="nearest")),
                     "position_high": int(np.quantile(ranks[:, i], .9, method="nearest"))} for i, r in enumerate(roster)]
            race_forecasts.append({"round": event["round"], "name": event["name"], "date": event["date"],
                                   "drivers": sorted(rows, key=lambda r: (-r["expected_points"], r["average_position"], r["driver"]))})
    if events:
        dr = championship_ranks(dp, dc, rng)
        tr = championship_ranks(tp, tc, rng)
    else:
        dr = np.tile([r["position"] for r in drivers], (simulations, 1))
        tr = np.tile([r["position"] for r in teams], (simulations, 1))
    return {"status": "ready" if events else "completed", "simulations": simulations, "seed": seed,
            "drivers": summarise(drivers, dp, dr, {r["driver"] for r in roster}),
            "teams": summarise(teams, tp, tr, {r["team"] for r in roster}), "races": race_forecasts,
            "remaining_races": sum(not e["sprint"] for e in events), "remaining_sprints": sum(e["sprint"] for e in events)}


def forecast(root: Path, records: list[dict], version: str, threads: int = 4, refresh: bool = False) -> dict:
    season, through = max((r["season"], r["round"]) for r in records)
    if season != 2026:
        raise ValueError("Season points rules verified for 2026 only")
    snapshot = fetch_input(root, season, through, refresh)
    known = [r for r in records if r["season"] == season]
    roster = sorted([r for r in known if r["round"] == through], key=lambda r: r["driver"])
    future = sorted([r for r in snapshot["calendar"] if int(r["round"]) > through], key=lambda r: int(r["round"]))
    if any(r["date"] < datetime.now(timezone.utc).date().isoformat() for r in future):
        raise ValueError("Cached race results miss an already elapsed event; refresh current data")
    if version not in ('lightgbm-v1', 'lightgbm-v2', 'lightgbm-v3', 'lightgbm-v4'):
        raise ValueError('Only LightGBM models are supported')
    directory = root / f"models/{version}"
    metadata = json.loads((directory / "metadata.json").read_text(encoding="utf-8"))
    if metadata["features"] != FEATURES:
        raise ValueError("Incompatible model feature schema")
    if metadata.get('engine') != 'lightgbm':
        raise ValueError('Unsupported model engine')
    pace = LightModel.load(directory / 'pace.txt')
    risk = LightModel.load(directory / 'nonclassification.txt')
    pools = ((np.asarray(metadata["uncertainty_pools"]["team"]), np.asarray(metadata["uncertainty_pools"]["individual"]))
             if "uncertainty_pools" in metadata else residual_pools(pace, build_features(records)))
    events = []
    for race in future:
        hypothetical = [{**r, "round": int(race["round"]), "date": race["date"], "race_name": race["raceName"],
                         "circuit": race["Circuit"]["circuitId"], "position": i + 1, "points": 0,
                         "classified": True} for i, r in enumerate(roster)]
        frame = build_features(records + hypothetical)
        field = frame[frame["group"].eq(season * 100 + int(race["round"]))].sort_values("driver")
        event = {"round": int(race["round"]), "name": race["raceName"], "date": race["date"], "sprint": False,
                 "pace": pace_positions(pace, field).tolist(), "risk": probability(risk, metadata["calibration"], field).tolist()}
        events.append(event)
        if "Sprint" in race:
            events.append({**event, "sprint": True, "date": race["Sprint"]["date"]})
    result = simulate(snapshot, known, roster, events, *pools)
    result.update({"model": metadata["version"], "forecast_version": "lightgbm-season-v1", "season": season,
                   "through_round": through, "source_updated": snapshot["fetched_at"],
                   "generated": datetime.now(timezone.utc).isoformat(),
                   "artifact_sha256": artifact_hashes(directory),
                   "uncertainty": {"residual_year": season if "uncertainty_pools" in metadata else 2024, "team_sd": float(np.std(pools[0])),
                                   "individual_sd": float(np.std(pools[1])), "persistent_team_correlation": .25},
                   "limitations": [
                       "Experimental Monte Carlo frequencies, not calibrated probabilities; race rank audit does not validate championship chances.",
                       "Official points at the observed round; roster frozen from last race. Future form is frozen at known cutoff, not fabricated from predictions.",
                       ("Prequential current-season residual bootstrap" if "uncertainty_pools" in metadata else "Historical 2024 residual bootstrap") + "; fixed .25 persistent team correlation. Independence assumptions within race reliability.",
                       "Past-only season-rate logit adjustment is experimental, not proven probability calibration.",
                       "Sprint uses race pace and risk proxy; only explicitly scheduled future sprints count. Full distance, no fastest lap bonus.",
                       "No future weather, qualifying, upgrades, penalties, cancellations, roster changes or qualifying tie-break modeled.",
                       "Equal points use race first/second/etc counts; remaining ties randomly resolved. Already completed points are never re-simulated.",
                   ]})
    target = root / f"reports/season-forecast-{version}.json"
    target.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    return result
