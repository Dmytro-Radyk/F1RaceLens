"""Chronological training, held-out evaluation, portable artifacts and metadata."""

import hashlib
import json
import platform
import time
from datetime import datetime, timezone
from pathlib import Path

import lightgbm
import numpy as np
import pandas as pd
from .models import LightModel, fit_model, artifact_hashes
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import brier_score_loss, log_loss

from . import VERSION
from .features import CATEGORICAL, FEATURES

SEED = 260019


def probability(classifier: LightModel, calibration: dict, frame: pd.DataFrame) -> np.ndarray:
    raw = np.asarray(classifier.predict(frame, raw_score=True))
    logit = calibration["slope"] * raw + calibration["intercept"]
    predicted = 1 / (1 + np.exp(-np.clip(logit, -30, 30)))
    weight = float(calibration.get('baseline_weight',0))
    if not 0 <= weight <= 1:
        raise ValueError('Invalid risk blend')
    baseline = .5*(frame.driver_season_nonclass.to_numpy()+frame.driver_recent_nonclass.to_numpy())
    return (1-weight)*predicted+weight*baseline


def rank_field(frame: pd.DataFrame, scores: np.ndarray, risk: np.ndarray) -> pd.DataFrame:
    ranked = frame.copy()
    ranked["pace_score"] = scores
    ranked["nonclassification_probability"] = risk
    # Ranker scores are relative, not positions or probabilities. Convert within each field.
    ranked["pace_order"] = ranked.groupby("group")["pace_score"].rank(ascending=False, method="average")
    ranked["ordering_score"] = (
        (1 - ranked["nonclassification_probability"]) * ranked["pace_order"] / ranked["field_size"]
        + ranked["nonclassification_probability"] * 1.1
    )
    ranked = ranked.sort_values(["group", "ordering_score", "driver"], kind="stable")
    ranked["predicted_position"] = ranked.groupby("group").cumcount() + 1
    return ranked


def pace_scores(model, frame: pd.DataFrame) -> np.ndarray:
    """A larger score always means faster, including regression artifacts."""
    values = np.asarray(model.predict(frame))
    return -values


def evaluate(frame: pd.DataFrame, ranker: LightModel, classifier: LightModel, calibration: dict) -> dict:
    scores = pace_scores(ranker, frame)
    risk = probability(classifier, calibration, frame)
    predicted = rank_field(frame, scores, risk)
    baseline = frame.copy()
    baseline["ordering_score"] = (1 - baseline["baseline_nonclass"]) * baseline["baseline_pace"] + baseline["baseline_nonclass"] * 1.1
    baseline = baseline.sort_values(["group", "ordering_score", "driver"], kind="stable")
    baseline["predicted_position"] = baseline.groupby("group").cumcount() + 1
    y = (~frame["classified"]).astype(int)
    per_race = []
    for group, field in predicted.groupby("group", sort=True):
        other = baseline[baseline["group"] == group]
        actual_winner = field.loc[field["position"].eq(1), "driver"].iloc[0]
        top = field.loc[field["predicted_position"].eq(1), "driver"].iloc[0]
        base_top = other.loc[other["predicted_position"].eq(1), "driver"].iloc[0]
        per_race.append({
            "season": int(field["season"].iloc[0]), "round": int(field["round"].iloc[0]),
            "race": field["race_name"].iloc[0], "participants": len(field),
            "rank_mae": float((field["predicted_position"] - field["position"]).abs().mean()),
            "baseline_rank_mae": float((other["predicted_position"] - other["position"]).abs().mean()),
            "predicted_winner": top, "baseline_winner": base_top, "actual_winner": actual_winner,
        })
    return {
        "entries": len(frame), "races": len(per_race),
        "rank_mae": float((predicted["predicted_position"] - predicted["position"]).abs().mean()),
        "baseline_rank_mae": float((baseline["predicted_position"] - baseline["position"]).abs().mean()),
        "winner_accuracy": sum(r["predicted_winner"] == r["actual_winner"] for r in per_race) / len(per_race),
        "baseline_winner_accuracy": sum(r["baseline_winner"] == r["actual_winner"] for r in per_race) / len(per_race),
        "nonclass_brier": float(brier_score_loss(y, risk)),
        "baseline_nonclass_brier": float(brier_score_loss(y, frame["baseline_nonclass"])),
        "nonclass_logloss": float(log_loss(y, risk, labels=[0, 1])),
        "nonclass_observed_rate": float(y.mean()), "nonclass_predicted_rate": float(risk.mean()),
        "per_race": per_race,
    }


def train(frame: pd.DataFrame, output: Path, reports: Path, source_hash: str, threads: int = 4) -> dict:
    """Fixed configuration; 2025 audited only after fitting on earlier years."""
    started = time.perf_counter()
    if output.exists() and any(output.iterdir()):
        raise FileExistsError("Audit artifacts exist; choose a new version before retraining")
    training = frame[frame.season.between(2010, 2022)]
    validation = frame[frame.season.between(2023, 2024)]
    test = frame[frame.season.eq(2025)]
    if min(len(training), len(validation), len(test)) == 0 or test['round'].nunique() != 24:
        raise ValueError("Complete chronological partitions required")
    good, stop = training[training.classified], validation[validation.classified]
    pace = fit_model(good, good.position/good.field_size, 'regressor', threads=threads,
                     validation=(stop, stop.position/stop.field_size))
    risk = fit_model(training, (~training.classified).astype(int), 'classifier', threads=threads,
                     validation=(validation, (~validation.classified).astype(int)))
    raw = risk.predict(validation, raw_score=True)
    calibrator = LogisticRegression(C=1, random_state=SEED).fit(raw.reshape(-1,1), (~validation.classified).astype(int))
    calibration = {'slope': float(calibrator.coef_[0,0]), 'intercept': float(calibrator.intercept_[0])}
    output.mkdir(parents=True, exist_ok=True)
    reports.mkdir(parents=True, exist_ok=True)
    pace.save(output/'pace.txt')
    risk.save(output/'nonclassification.txt')
    report = {'version': 'lightgbm-audit-2025', 'engine': 'lightgbm', 'lightgbm': lightgbm.__version__,
              'created_at': datetime.now(timezone.utc).isoformat(), 'features': FEATURES,
              'source_sha256': source_hash, 'seed': SEED, 'threads': threads,
              'split': {'train':[2010,2022], 'stop_and_calibration':[2023,2024], 'audit':[2025,2025]},
              'pace_trees': pace.tree_count_, 'risk_trees': risk.tree_count_, 'calibration':calibration,
              'evaluation': evaluate(test, pace, risk, calibration),
              'artifact_sha256': artifact_hashes(output), 'training_seconds':round(time.perf_counter()-started,2),
              'limitations':['2025 was inspected during earlier development; this is not a fresh independent holdout.',
                             'No parameter search using 2025/2026 outcomes or external forecasts.',
                             'Entry lists conditional on observed race rosters; no current-race qualifying/weather features.']}
    for target in [output/'metadata.json', reports/'evaluation.json']:
        target.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    return report
