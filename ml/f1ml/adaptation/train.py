import hashlib
import json
import time
from datetime import datetime, timezone
from pathlib import Path

import lightgbm
import numpy as np
import pandas as pd
from ..models import fit_model, artifact_hashes
from sklearn.metrics import brier_score_loss, log_loss

from ..features import CATEGORICAL, FEATURES
from ..training import SEED, pace_scores, probability, rank_field

VERSION = "lightgbm-race-v1"


def known_before(frame: pd.DataFrame, season: int, round_number: int) -> pd.DataFrame:
    return frame[(frame["season"] >= 2014) & ((frame["season"] < season) |
                 ((frame["season"] == season) & (frame["round"] < round_number)))].copy()


def fit_known(known: pd.DataFrame, season: int, threads: int, settings: dict | None = None):
    config = settings or {}
    weights = config.get('year_decay', .90) ** (season - known["season"]) * np.where(known["season"].eq(season), 2., 1.)
    risk_weights = config.get('risk_year_decay', .90) ** (season - known["season"]) * np.where(known["season"].eq(season), 2., 1.)
    classified = known["classified"]
    pace = fit_model(known.loc[classified],
                     known.loc[classified, "position"] / known.loc[classified, "field_size"],
                     'regressor', weights[classified], threads, rounds=config.get('pace_rounds', 300),
                     settings=config.get('pace'), feature_names=config.get('feature_names'))
    pace.baseline_weight = config.get('pace_baseline_weight',0)
    risk = fit_model(known, (~known.classified).astype(int), 'classifier', risk_weights, threads,
                     rounds=config.get('risk_rounds', 250), settings=config.get('risk'), feature_names=config.get('feature_names'))
    return pace, risk


def risk_adjustment(known: pd.DataFrame, season: int, raw_risk: np.ndarray) -> dict:
    current = known[known["season"].eq(season)]
    previous = known[known["season"].eq(season - 1)]
    prior = float((~previous["classified"]).mean()) if len(previous) else .15
    # Fixed 40-entry prior; only outcomes available BEFORE the forecast enter the update.
    observed = (float((~current["classified"]).sum()) + 40 * prior) / (len(current) + 40)
    logit = lambda x: np.log(np.clip(x, .001, .999) / (1 - np.clip(x, .001, .999)))
    shift = float(np.clip(logit(observed) - logit(float(raw_risk.mean())), -2, 2))
    return {"slope": 1., "intercept": shift, "method": "season-rate-logit-shift",
            "prior_entries": 40, "observed_entries": len(current), "shrunk_season_rate": observed}


def continue_training(frame: pd.DataFrame, root: Path, source_hash: str, threads: int = 4,
                      version: str = 'lightgbm-v1', settings: dict | None = None) -> dict:
    started = time.perf_counter()
    if version not in ('lightgbm-v1', 'lightgbm-v2', 'lightgbm-v3', 'lightgbm-v4', 'lightgbm-q1'):
        raise ValueError('Unsupported artifact version')
    out = root / f"models/{version}"
    reports = root / f"reports/{version}"
    if out.exists() or reports.exists():
        raise FileExistsError("LightGBM artifacts already exist; archive explicitly before retraining")
    out.mkdir(parents=True)
    reports.mkdir(parents=True)
    season = int(frame["season"].max())
    current = frame[frame["season"].eq(season)]
    predicted = []
    cutoffs = []
    for round_number in sorted(current["round"].unique()):
        known = known_before(frame, season, int(round_number))
        field = current[current["round"].eq(round_number)]
        pace, risk = fit_known(known, season, threads, settings)
        calibration = risk_adjustment(known, season, risk.predict_proba(field)[:, 1])
        if settings and 'risk_baseline_weight' in settings:
            calibration['baseline_weight'] = settings['risk_baseline_weight']
        ranked = rank_field(field, pace_scores(pace, field), probability(risk, calibration, field))
        ranked["pace_position"] = np.clip(pace.predict(ranked) * ranked["field_size"], 1, ranked["field_size"])
        predicted.append(ranked)
        cutoffs.append({"forecast_round": int(round_number), "latest_training_season": int(known["season"].max()),
                        "latest_training_round_in_current_season": int(known.loc[known["season"].eq(season), "round"].max()) if known["season"].eq(season).any() else 0,
                        "training_rows": len(known), "risk_adjustment": calibration})
        print(f"LightGBM / round {round_number}: trained on {len(known)} earlier entries, MAE {abs(ranked.predicted_position-ranked.position).mean():.3f}", flush=True)
    result = pd.concat(predicted).sort_values(["group", "predicted_position"])
    baseline = result.sort_values(["group", "baseline_pace", "driver"]).copy()
    baseline["ordering_score"] = (1-baseline["baseline_nonclass"])*baseline["baseline_pace"]+baseline["baseline_nonclass"]*1.1
    baseline = baseline.sort_values(["group", "ordering_score", "driver"])
    baseline["baseline_position"] = baseline.groupby("group").cumcount()+1
    winners = result[result["predicted_position"].eq(1)]
    base_winners = baseline[baseline["baseline_position"].eq(1)]
    y = (~result["classified"]).astype(int)
    metrics = {"entries": len(result), "races": result["group"].nunique(),
               "rank_mae": float(abs(result.predicted_position-result.position).mean()),
               "baseline_rank_mae": float(abs(baseline.baseline_position-baseline.position).mean()),
               "winner_accuracy": float(winners.position.eq(1).mean()),
               "baseline_winner_accuracy": float(base_winners.position.eq(1).mean()),
               "nonclass_brier": float(brier_score_loss(y, result.nonclassification_probability)),
               "baseline_nonclass_brier": float(brier_score_loss(y, result.baseline_nonclass)),
               "nonclass_logloss": float(log_loss(y, result.nonclassification_probability, labels=[0, 1])),
               "nonclass_observed_rate": float(y.mean()), "nonclass_predicted_rate": float(result.nonclassification_probability.mean())}
    result.to_csv(reports / "prequential-predictions.csv", index=False)
    # One final refit includes all completed races. It is used ONLY for future events.
    through = int(current["round"].max())
    known = known_before(frame, season, through+1)
    pace, risk = fit_known(known, season, threads, settings)
    last_field = current[current["round"].eq(through)]
    calibration = risk_adjustment(known, season, risk.predict_proba(last_field)[:, 1])
    if settings and 'risk_baseline_weight' in settings:
        calibration['baseline_weight'] = settings['risk_baseline_weight']
    pace.save(out / "pace.txt")
    risk.save(out / "nonclassification.txt")
    errors = result[result.classified].copy()
    errors["residual"] = errors.position-errors.pace_position
    team_errors = errors.groupby(["group", "team"])["residual"].mean().to_numpy()
    individual_errors = (errors.residual-errors.groupby(["group", "team"])["residual"].transform("mean")).to_numpy()
    metadata = {"version": version.replace('lightgbm-', 'lightgbm-race-'), "pace_kind": "regressor", "features": (settings or {}).get("feature_names",FEATURES), "categorical_features": CATEGORICAL,
                "created_at": datetime.now(timezone.utc).isoformat(), "engine": "lightgbm", "lightgbm": lightgbm.__version__, "threads": threads,
                "seed": SEED, "source_sha256": source_hash, "final_train_through": season,
                "training_cutoff_round": through, "training_rows": len(known), "calibration": calibration,
                "uncertainty_pools": {"team": (team_errors-team_errors.mean()).tolist(),
                                      "individual": (individual_errors-individual_errors.mean()).tolist()},
                "evaluation": metrics, "cutoffs": cutoffs,
                "settings": settings or {"pace_iterations":300,"pace_depth":4,"risk_iterations":250,"risk_depth":3,
                             "learning_rate":.035,"l2_leaf_reg":8,"year_decay":.90,"current_season_weight":2},
                "limitations": ["Retrospective development evaluation; 2026 was inspected with the previous engine, not a fresh independent test.",
                                "Each race predicted before including its labels; final refit includes all completed races and is not evaluated on their outcomes.",
                                "Settings fixed before this run, no tuning against external predictions. Logit risk adaptation is a statistical update, not proven calibration.",
                                "No current-race weather, qualifying or upgrades; future lineup frozen at last observed race."],
                "artifact_sha256": artifact_hashes(out),
                "training_seconds": round(time.perf_counter()-started,2)}
    for target in [out/'metadata.json', reports/'evaluation.json']:
        target.write_text(json.dumps(metadata, indent=2, ensure_ascii=False, default=int), encoding='utf-8')
    return metadata
