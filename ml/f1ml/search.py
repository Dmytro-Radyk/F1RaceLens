"""Chronological split helpers retained for future LightGBM searches."""
import numpy as np
import pandas as pd
FOLD_YEARS = [(2019, 2020, 2021), (2020, 2021, 2022), (2021, 2022, 2023)]

def split_fold(frame: pd.DataFrame, years: tuple[int, int, int], start: int = 2010):
    train_end, stop_year, score_year = years
    if not start <= train_end < stop_year < score_year <= 2024:
        raise ValueError("Fold must be chronological and precede audit seasons")
    train = frame[frame["season"].between(start, train_end)].copy()
    stop = frame[frame["season"].eq(stop_year)].copy()
    score = frame[frame["season"].eq(score_year)].copy()
    if min(len(train), len(stop), len(score)) == 0:
        raise ValueError("Empty chronological fold")
    return train, stop, score


def choose(candidates: list[dict], key: str) -> dict:
    if not candidates or any(not np.isfinite(row[key]) for row in candidates):
        raise ValueError("Invalid candidate metrics")
    return min(candidates, key=lambda row: (row[key], row["id"]))


def experiment(frame, root, source_hash, threads=4, version='v2'):
    """Bounded chronological search before 2025, then separate retrospective audits."""
    import hashlib
    import json
    import time
    from datetime import datetime, timezone
    from sklearn.linear_model import LogisticRegression
    from sklearn.metrics import brier_score_loss
    from .models import fit_model, artifact_hashes
    from .training import evaluate, pace_scores, probability, rank_field, SEED
    from .adaptation.train import continue_training

    started = time.perf_counter()
    if version not in ('v2','v3'):
        raise ValueError('Unsupported search version')
    folds_years = FOLD_YEARS + ([(2022,2023,2024)] if version == 'v3' else [])
    reports = root/f'reports/lightgbm-search-{version}'
    if reports.exists() or (root/f'models/lightgbm-{version}').exists():
        raise FileExistsError('Artifacts exist; use a new explicit version')
    reports.mkdir(parents=True)
    pace_candidates = [
        {'id':'standard', 'depth':4, 'leaf':30, 'l2':8, 'decay':.90},
        {'id':'shallow', 'depth':3, 'leaf':50, 'l2':12, 'decay':.90},
        {'id':'regularized', 'depth':4, 'leaf':70, 'l2':20, 'decay':.90},
        {'id':'recent', 'depth':4, 'leaf':30, 'l2':8, 'decay':.75},
        {'id':'deeper', 'depth':5, 'leaf':40, 'l2':12, 'decay':.90},
        {'id':'long-history', 'depth':3, 'leaf':40, 'l2':12, 'decay':1.0},
    ]
    risk_candidates = [
        {'id':'risk-shallow','depth':2,'leaf':60,'l2':12,'decay':.90},
        {'id':'risk-standard','depth':3,'leaf':30,'l2':8,'decay':.90},
        {'id':'risk-regularized','depth':4,'leaf':70,'l2':20,'decay':.90},
    ]
    if version == 'v3':
        pace_candidates += [
            {'id':f'{objective}-d{depth}', 'depth':depth,'leaf':40,'l2':12,'decay':decay,'objective':objective}
            for objective in ('regression_l1','huber') for depth,decay in ((2,1.0),(3,1.0),(4,.9))
        ]
        risk_candidates += [
            {'id':'risk-simple','depth':2,'leaf':100,'l2':30,'decay':1.0},
            {'id':'risk-recent','depth':3,'leaf':50,'l2':12,'decay':.75},
        ]
    def params(candidate):
        result = {'max_depth':candidate['depth'], 'num_leaves':2**candidate['depth']-1,
                'min_data_in_leaf':candidate['leaf'], 'lambda_l2':candidate['l2']}
        if 'objective' in candidate:
            result['objective'] = candidate['objective']
        return result

    results = {'pace':[], 'risk':[]}
    for kind, candidates in [('pace',pace_candidates),('risk',risk_candidates)]:
        for candidate in candidates:
            folds = []
            for years in folds_years:
                train, stop, score = split_fold(frame, years, start=2014)
                is_pace = kind == 'pace'
                if is_pace:
                    train, stop = train[train.classified], stop[stop.classified]
                target = lambda f: f.position/f.field_size if is_pace else (~f.classified).astype(int)
                weights = candidate['decay']**(years[0]-train.season)
                model = fit_model(train, target(train), 'regressor' if is_pace else 'classifier',
                                  weights, threads, rounds=900, validation=(stop,target(stop)),
                                  settings=params(candidate))
                if is_pace:
                    ranked = rank_field(score, pace_scores(model,score),score.baseline_nonclass.to_numpy())
                    metric = float(abs(ranked.predicted_position-ranked.position).mean())
                else:
                    raw = model.predict(stop,raw_score=True)
                    calibrator = LogisticRegression(C=1,random_state=SEED).fit(raw.reshape(-1,1),target(stop))
                    calibration = {'slope':float(calibrator.coef_[0,0]),'intercept':float(calibrator.intercept_[0])}
                    metric = float(brier_score_loss(target(score),probability(model,calibration,score)))
                folds.append({'years':years,'metric':metric,'trees':model.tree_count_})
            result = {**candidate, 'folds':folds,'mean_metric':float(np.mean([f['metric'] for f in folds])),
                      'rounds':int(np.median([f['trees'] for f in folds]))}
            results[kind].append(result)
            print(f"LightGBM search / {candidate['id']}: {result['mean_metric']:.5f}",flush=True)
    pace_config = choose(results['pace'],'mean_metric')
    risk_config = choose(results['risk'],'mean_metric')
    settings = {'pace':params(pace_config), 'risk':params(risk_config),
                'pace_rounds':pace_config['rounds'],'risk_rounds':risk_config['rounds'],
                'year_decay':pace_config['decay'], 'risk_year_decay':risk_config['decay']}
    selection = {'version':f'lightgbm-{version}','created_at':datetime.now(timezone.utc).isoformat(),
                 'source_sha256':source_hash,'folds':folds_years,'candidates':results,'selected':settings,
                 'audit_used_for_selection':False,
                 'pace_metric':'Mean fold classification rank MAE with fixed baseline nonclassification risk',
                 'risk_metric':'Mean fold Brier with validation-only sigmoid calibration',
                 'rounds_rule':'Median early-stopped trees across chronological folds; no 2025/2026 selection'}
    selection_path = reports/'selection.json'
    selection_path.write_text(json.dumps(selection,indent=2),encoding='utf-8')
    selection_hash = hashlib.sha256(selection_path.read_bytes()).hexdigest()
    # Selection is persisted BEFORE the audits; neither audit can influence candidates.
    train = frame[frame.season.between(2014,2023)]
    stop = frame[frame.season.eq(2024)]
    good = train[train.classified]
    pace = fit_model(good,good.position/good.field_size,'regressor',
                     pace_config['decay']**(2023-good.season),threads,rounds=settings['pace_rounds'],
                     settings=settings['pace'])
    risk = fit_model(train,(~train.classified).astype(int),'classifier',
                     risk_config['decay']**(2023-train.season),threads,rounds=settings['risk_rounds'],
                     settings=settings['risk'])
    raw = risk.predict(stop,raw_score=True)
    calibrator = LogisticRegression(C=1,random_state=SEED).fit(raw.reshape(-1,1),(~stop.classified).astype(int))
    calibration = {'slope':float(calibrator.coef_[0,0]),'intercept':float(calibrator.intercept_[0])}
    audit_dir = root/f'models/lightgbm-{version}-audit-2025'
    audit_dir.mkdir(parents=True)
    pace.save(audit_dir/'pace.txt')
    risk.save(audit_dir/'nonclassification.txt')
    audit = {'version':f'lightgbm-{version}-audit-2025', 'engine':'lightgbm', 'calibration':calibration,
             'selection_sha256':selection_hash, 'artifact_sha256':artifact_hashes(audit_dir),
             'split':{'train':[2014,2023],'calibration':[2024,2024],'audit':[2025,2025]},
             'evaluation':evaluate(frame[frame.season.eq(2025)],pace,risk,calibration)}
    (audit_dir/'metadata.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
    (reports/'audit-2025.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
    adaptive = continue_training(frame,root,source_hash,threads,f'lightgbm-{version}',settings)
    adaptive['selection_sha256'] = selection_hash
    for path in [root/f'models/lightgbm-{version}/metadata.json',root/f'reports/lightgbm-{version}/evaluation.json']:
        path.write_text(json.dumps(adaptive,indent=2,default=int),encoding='utf-8')
    summary = {'selection_sha256':selection_hash,'settings':settings,'audit_2025':audit['evaluation'],
               'audit_2026':adaptive['evaluation'],'seconds':round(time.perf_counter()-started,2),
               'limitations':['Both audit seasons were inspected in earlier development; not fresh independent tests.',
                              'No weather, qualifying, upgrades or future roster changes modeled.',
                              'No automatic default promotion using these audit outcomes.']}
    (reports/'comparison.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    return summary
