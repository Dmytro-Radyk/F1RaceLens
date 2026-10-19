"""LightGBM artifacts with a training-only categorical vocabulary; no pickle loading."""

import json
from pathlib import Path

import lightgbm as lgb
import numpy as np
import pandas as pd

from .features import CATEGORICAL, FEATURES


class LightModel:
    def __init__(self, booster: lgb.Booster, categories: dict, kind: str):
        self.booster = booster
        self.categories = categories
        self.kind = kind
        self.features = FEATURES
        self.baseline_weight = 0.0
        self.tree_count_ = booster.num_trees()

    def encode(self, frame: pd.DataFrame) -> pd.DataFrame:
        result = frame[self.features].copy()
        for name in CATEGORICAL:
            # Unknown drivers/teams/tracks are missing values, never newly fitted categories.
            values = result[name].where(result[name].isin(self.categories[name]))
            result[name] = pd.Categorical(values, categories=self.categories[name])
        return result

    def predict(self, frame: pd.DataFrame, *, raw_score: bool = False):
        result = np.asarray(self.booster.predict(self.encode(frame), raw_score=raw_score, num_threads=4))
        if self.kind == 'regressor' and self.baseline_weight:
            baseline = .5*(frame.driver_season_pace.to_numpy()+frame.driver_recent_pace.to_numpy())
            result = (1-self.baseline_weight)*result+self.baseline_weight*baseline
        return result

    def predict_proba(self, frame: pd.DataFrame):
        p = self.predict(frame)
        return np.column_stack([1-p, p])

    def save(self, path: Path):
        # Python handles Unicode Windows paths; the native file API does not.
        path.write_text(self.booster.model_to_string(), encoding='utf-8')
        path.with_suffix('.schema.json').write_text(
            json.dumps({'kind': self.kind, 'categories': self.categories, 'features': self.features,
                        'baseline_weight':self.baseline_weight}), encoding='utf-8')

    @classmethod
    def load(cls, path: Path):
        schema = json.loads(path.with_suffix('.schema.json').read_text(encoding='utf-8'))
        allowed_features = [FEATURES, FEATURES+['qualifying_position','qualifying_known']]
        if schema['features'] not in allowed_features or schema['kind'] not in ('regressor', 'classifier'):
            raise ValueError('Incompatible LightGBM artifact schema')
        model = cls(lgb.Booster(model_str=path.read_text(encoding='utf-8')), schema['categories'], schema['kind'])
        model.features = schema['features']
        model.baseline_weight = float(schema.get('baseline_weight',0))
        if not 0 <= model.baseline_weight <= 1:
            raise ValueError('Invalid baseline blend')
        return model

    def get_all_params(self):
        return {'depth': self.booster.params.get('max_depth', 4),
                'learning_rate': self.booster.params.get('learning_rate', .035)}


def fit_model(frame: pd.DataFrame, target, kind: str, weights=None, threads: int = 4,
              rounds: int = 300, validation=None, settings: dict | None = None, feature_names=None) -> LightModel:
    categories = {name: sorted(frame[name].unique().tolist()) for name in CATEGORICAL}
    depth = 4 if kind == 'regressor' else 3
    params = dict(objective='regression' if kind == 'regressor' else 'binary',
                  metric='rmse' if kind == 'regressor' else 'binary_logloss',
                  max_depth=depth, num_leaves=2**depth-1, learning_rate=.035,
                  min_data_in_leaf=30, lambda_l2=8, num_threads=threads,
                  seed=260019, deterministic=True, force_col_wise=True, verbosity=-1)
    if settings:
        allowed = {'max_depth', 'num_leaves', 'min_data_in_leaf', 'lambda_l2', 'learning_rate', 'objective'}
        if set(settings) - allowed:
            raise ValueError('Unsupported LightGBM setting')
        if 'objective' in settings and (kind != 'regressor' or settings['objective'] not in ('regression','regression_l1','huber')):
            raise ValueError('Unsupported regression objective')
        params.update(settings)
    encoder = LightModel.__new__(LightModel)
    encoder.categories = categories
    encoder.features = feature_names or FEATURES
    train = lgb.Dataset(encoder.encode(frame), label=np.asarray(target), weight=weights,
                        categorical_feature=CATEGORICAL, free_raw_data=False)
    kwargs = {}
    if validation is not None:
        valid_frame, valid_target = validation
        kwargs = dict(valid_sets=[lgb.Dataset(encoder.encode(valid_frame), label=np.asarray(valid_target),
                                             reference=train, categorical_feature=CATEGORICAL)],
                      callbacks=[lgb.early_stopping(60, verbose=False)])
    booster = lgb.train(params, train, num_boost_round=rounds, **kwargs)
    model = LightModel(booster, categories, kind)
    model.features = encoder.features
    return model


def artifact_hashes(directory: Path) -> dict:
    import hashlib
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(directory.iterdir()) if p.suffix == '.txt' or p.name.endswith('.schema.json')}
