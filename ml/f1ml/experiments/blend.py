"""Select a statistical blend on earlier seasons, then audit without retuning."""

import hashlib
import json
import time
from datetime import datetime, timezone

import numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import brier_score_loss

from ..models import fit_model, artifact_hashes
from ..training import SEED, probability, rank_field, evaluate
from ..search import FOLD_YEARS, split_fold
from ..adaptation.train import continue_training

FOLDS = FOLD_YEARS+[(2022,2023,2024)]


def calibrated(risk, stop):
    raw = risk.predict(stop,raw_score=True)
    fitted = LogisticRegression(C=1,random_state=SEED).fit(raw.reshape(-1,1),(~stop.classified).astype(int))
    return {'slope':float(fitted.coef_[0,0]),'intercept':float(fitted.intercept_[0])}


def experiment(frame, root, source_hash, threads=4):
    started = time.perf_counter()
    reports = root/'reports/lightgbm-search-v4'
    if reports.exists() or (root/'models/lightgbm-v4').exists():
        raise FileExistsError('v4 already exists; preserve previous runs')
    reports.mkdir(parents=True)
    configs = [
        {'id':'rmse-d3','objective':'regression','depth':3,'leaf':40,'l2':12},
        {'id':'mae-d3','objective':'regression_l1','depth':3,'leaf':40,'l2':12},
        {'id':'rmse-conservative','objective':'regression','depth':2,'leaf':80,'l2':30},
    ]
    candidates = []
    for config in configs:
        pace_params = {'objective':config['objective'],'max_depth':config['depth'],
                       'num_leaves':2**config['depth']-1,'min_data_in_leaf':config['leaf'],'lambda_l2':config['l2']}
        risk_params = {'max_depth':3,'num_leaves':7,'min_data_in_leaf':30,'lambda_l2':8}
        outputs = []
        for years in FOLDS:
            train,stop,score = split_fold(frame,years,start=2014)
            good,valid = train[train.classified],stop[stop.classified]
            pace = fit_model(good,good.position/good.field_size,'regressor',threads=threads,
                             rounds=900,validation=(valid,valid.position/valid.field_size),settings=pace_params)
            risk = fit_model(train,(~train.classified).astype(int),'classifier',.9**(years[0]-train.season),threads,
                             rounds=900,validation=(stop,(~stop.classified).astype(int)),settings=risk_params)
            outputs.append((score,pace.predict(score),probability(risk,calibrated(risk,stop),score),
                            pace.tree_count_,risk.tree_count_))
        for pace_weight in (0,.25,.5,.75):
            for risk_weight in (0,.25,.5,.75,1):
                metrics = []
                for years,(score,pace,risk,pt,rt) in zip(FOLDS,outputs):
                    mixed_pace = (1-pace_weight)*pace+pace_weight*score.baseline_pace.to_numpy()
                    mixed_risk = (1-risk_weight)*risk+risk_weight*score.baseline_nonclass.to_numpy()
                    ranked = rank_field(score,-mixed_pace,mixed_risk)
                    metrics.append({'years':years,'mae':float(abs(ranked.predicted_position-ranked.position).mean()),
                                    'brier':float(brier_score_loss((~score.classified).astype(int),mixed_risk))})
                candidates.append({'id':f"{config['id']}-p{pace_weight}-r{risk_weight}",
                                   'mean_mae':float(np.mean([m['mae'] for m in metrics])),
                                   'mean_brier':float(np.mean([m['brier'] for m in metrics])), 'folds':metrics,
                                   'settings':{'pace':pace_params,'risk':risk_params,
                                               'pace_rounds':int(np.median([o[3] for o in outputs])),
                                               'risk_rounds':int(np.median([o[4] for o in outputs])),
                                               'year_decay':1.,'risk_year_decay':.9,
                                               'pace_baseline_weight':pace_weight,'risk_baseline_weight':risk_weight}})
        print(f"Blend search: {config['id']} fitted on 4 earlier folds",flush=True)
    # MAE is the declared primary objective; risk Brier breaks ties, then ID.
    selected = min(candidates,key=lambda c:(c['mean_mae'],c['mean_brier'],c['id']))
    settings = selected['settings']
    selection = {'version':'lightgbm-v4','created_at':datetime.now(timezone.utc).isoformat(),
                 'source_sha256':source_hash,'folds':FOLDS,'candidates':candidates,'selected':settings,
                 'audit_used_for_selection':False,'criterion':'Mean fold rank MAE; then Brier; then ID',
                 'limitations':['Repeated development on these earlier years is not independent validation.',
                                'Pace blend uses at least 25% learned model; no pure baseline candidate.',
                                'Classification rank MAE is distinct from season simulation calibration.']}
    path = reports/'selection.json'
    path.write_text(json.dumps(selection,indent=2),encoding='utf-8')
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    train,stop = frame[frame.season.between(2014,2023)],frame[frame.season.eq(2024)]
    good = train[train.classified]
    pace = fit_model(good,good.position/good.field_size,'regressor',threads=threads,
                     rounds=settings['pace_rounds'],settings=settings['pace'])
    pace.baseline_weight = settings['pace_baseline_weight']
    risk = fit_model(train,(~train.classified).astype(int),'classifier',.9**(2023-train.season),threads,
                     rounds=settings['risk_rounds'],settings=settings['risk'])
    calibration = {**calibrated(risk,stop),'baseline_weight':settings['risk_baseline_weight']}
    directory = root/'models/lightgbm-v4-audit-2025'
    directory.mkdir(parents=True)
    pace.save(directory/'pace.txt')
    risk.save(directory/'nonclassification.txt')
    audit = {'version':'lightgbm-v4-audit-2025','engine':'lightgbm','selection_sha256':digest,
             'calibration':calibration,'artifact_sha256':artifact_hashes(directory),
             'evaluation':evaluate(frame[frame.season.eq(2025)],pace,risk,calibration)}
    (directory/'metadata.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
    (reports/'audit-2025.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
    final = continue_training(frame,root,source_hash,threads,'lightgbm-v4',settings)
    final['selection_sha256'] = digest
    final['method'] = 'LightGBM plus past-only statistical baseline blend'
    for path in [root/'models/lightgbm-v4/metadata.json',root/'reports/lightgbm-v4/evaluation.json']:
        path.write_text(json.dumps(final,indent=2,default=int),encoding='utf-8')
    summary = {'settings':settings,'selection_sha256':digest,'selected_earlier_mae':selected['mean_mae'],
               'audit_2025':audit['evaluation'],'audit_2026':final['evaluation'],
               'seconds':round(time.perf_counter()-started,2),
               'limitations':['2025/2026 were inspected during previous development; not fresh independent tests.',
                              'No external forecasts used for tuning; no automatic promotion.']}
    (reports/'comparison.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    return summary
