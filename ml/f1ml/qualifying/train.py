import hashlib
import json
from pathlib import Path

from sklearn.linear_model import LogisticRegression

from ..features import build_features
from ..models import fit_model, artifact_hashes
from ..training import evaluate, SEED
from ..adaptation.train import continue_training
from .features import attach, QUALIFYING_FEATURES


def qualification_baseline(frame):
    baseline = frame.copy()
    baseline['ordering'] = baseline.qualifying_position.where(baseline.qualifying_known.eq(1),baseline.baseline_pace)
    baseline = baseline.sort_values(['group','ordering','driver'])
    baseline['prediction'] = baseline.groupby('group').cumcount()+1
    return {'rank_mae':float(abs(baseline.prediction-baseline.position).mean()),
            'winner_accuracy':float(baseline.loc[baseline.prediction.eq(1),'position'].eq(1).mean()),
            'missing_qualifying_entries':int(baseline.qualifying_known.eq(0).sum()),
            'method':'Qualification order; missing/ambiguous positions fall back to known historical pace'}


def train(root: Path, records, qualifying, source_hash, threads=4):
    reports = root/'reports/qualifying-q1'
    directory = root/'models/lightgbm-q1-audit-2025'
    if reports.exists() or directory.exists() or (root/'models/lightgbm-q1').exists():
        raise FileExistsError('Preserve existing qualifying training run')
    frame = attach(build_features(records),qualifying)
    training = frame[frame.season.between(2014,2023)]
    validation = frame[frame.season.eq(2024)]
    good,stop = training[training.classified],validation[validation.classified]
    pace = fit_model(good,good.position/good.field_size,'regressor',threads=threads,rounds=600,
                     validation=(stop,stop.position/stop.field_size),feature_names=QUALIFYING_FEATURES)
    risk = fit_model(training,(~training.classified).astype(int),'classifier',threads=threads,rounds=400,
                     validation=(validation,(~validation.classified).astype(int)),feature_names=QUALIFYING_FEATURES)
    raw = risk.predict(validation,raw_score=True)
    calibrator = LogisticRegression(C=1,random_state=SEED).fit(raw.reshape(-1,1),(~validation.classified).astype(int))
    calibration = {'slope':float(calibrator.coef_[0,0]),'intercept':float(calibrator.intercept_[0])}
    settings = {'pace_rounds':pace.tree_count_,'risk_rounds':risk.tree_count_,
                'feature_names':QUALIFYING_FEATURES,'year_decay':.9,'risk_year_decay':.9}
    reports.mkdir(parents=True)
    selection_path = reports/'settings.json'
    selection_path.write_text(json.dumps({'settings':settings,'train':[2014,2023],
                                          'stop_and_calibration':[2024,2024],
                                          'audit_used_for_selection':False},indent=2),encoding='utf-8')
    digest = hashlib.sha256(selection_path.read_bytes()).hexdigest()
    directory.mkdir(parents=True)
    pace.save(directory/'pace.txt')
    risk.save(directory/'nonclassification.txt')
    audit = {'version':'lightgbm-q1-audit-2025','engine':'lightgbm','features':QUALIFYING_FEATURES,
             'calibration':calibration,'settings_sha256':digest,'artifact_sha256':artifact_hashes(directory),
             'evaluation':evaluate(frame[frame.season.eq(2025)],pace,risk,calibration),
             'same_stage_baseline':qualification_baseline(frame[frame.season.eq(2025)]),
             'coverage':{'known':int(frame.qualifying_known.sum()),'entries':len(frame)},
             'limitations':['Post-qualifying inputs, not comparable as an equal-information pre-weekend test.',
                            'Historical qualifying classifications can be revised; not contemporaneous snapshots.',
                            'Unknown/ambiguous qualification positions are missing, never fabricated grid places.']}
    for path in [directory/'metadata.json',reports/'audit-2025.json']:
        path.write_text(json.dumps(audit,indent=2),encoding='utf-8')
    final = continue_training(frame,root,source_hash,threads,'lightgbm-q1',settings)
    final.update({'settings_sha256':digest,'stage':'post-qualifying',
                  'same_stage_baseline':qualification_baseline(frame[frame.season.eq(2026)]),
                  'limitations':audit['limitations']+final['limitations']})
    for path in [root/'models/lightgbm-q1/metadata.json',root/'reports/lightgbm-q1/evaluation.json']:
        path.write_text(json.dumps(final,indent=2,default=int),encoding='utf-8')
    return {'audit_2025':audit['evaluation'],'audit_2026':final['evaluation'],'settings':settings}
