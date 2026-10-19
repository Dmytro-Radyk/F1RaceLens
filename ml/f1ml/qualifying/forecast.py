import json
from datetime import datetime, timezone

import numpy as np

from ..features import build_features
from ..models import LightModel, artifact_hashes
from ..training import probability
from ..forecast.engine import simulate, pace_positions
from ..forecast.source import fetch_input
from ..forecast.ledger import race_start, utc, freeze
from .features import attach


def forecast(root, records, qualifying, now=None, refresh=False):
    now = now or datetime.now(timezone.utc)
    season,through = max((r['season'],r['round']) for r in records)
    if season != 2026:
        raise ValueError('Post-qualifying points rules verified for 2026 only')
    snapshot = fetch_input(root,season,through,refresh)
    future = sorted([r for r in snapshot['calendar'] if int(r['round'])>through],key=lambda r:int(r['round']))
    directory = root/'models/lightgbm-q1'
    metadata = json.loads((directory/'metadata.json').read_text(encoding='utf-8'))
    if metadata['engine'] != 'lightgbm' or metadata['version'] != 'lightgbm-race-q1':
        raise ValueError('Unsupported qualifying model')
    if (metadata['final_train_through'],metadata['training_cutoff_round']) > (season,through):
        raise ValueError('Qualifying model trained beyond known race cutoff')
    pace,risk = LightModel.load(directory/'pace.txt'),LightModel.load(directory/'nonclassification.txt')
    if pace.features != metadata['features'] or risk.features != metadata['features']:
        raise ValueError('Qualifying feature schema differs from metadata')
    model = {'version':metadata['version'],'pace_kind':'regressor','pace_objective':pace.booster.params['objective'],
             'pace_trees':pace.tree_count_,'risk_trees':risk.tree_count_,
             'pace_depth':pace.get_all_params()['depth'],'risk_depth':risk.get_all_params()['depth'],
             'learning_rate':.035,'train_through':season,'features':len(pace.features),
             'pace_baseline_weight':0,'risk_baseline_weight':0,'evaluation':metadata['evaluation'],
             'same_stage_baseline':metadata.get('same_stage_baseline')}
    output = {'generated':now.isoformat(),'status':'waiting','model':model,'forecast':None,
              'message':'Майбутніх Гран-прі немає.'}
    if future:
        race = future[0]
        output.update({'round':int(race['round']),'name':race['raceName'],
                       'qualifying':race.get('Qualifying'),'race_start':race_start(race).isoformat()})
        q = race.get('Qualifying',{})
        if now >= race_start(race):
            output['message'] = 'Гонка вже стартувала або кеш результатів застарів. Новий передгоночний прогноз не створюється.'
        elif not q.get('time') or now < utc(q['date']+'T'+q['time']):
            output['message'] = 'Очікуємо кваліфікацію найближчого Гран-прі. Майбутні позиції не прогнозуються як відомі факти.'
        else:
            known = [r for r in records if r['season']==season]
            roster = sorted([r for r in known if r['round']==through],key=lambda r:r['driver'])
            current_q = [r for r in qualifying if (r['season'],r['round'])==(season,int(race['round']))]
            roster_teams = {r['driver']:r['team'] for r in roster}
            if ({r['driver'] for r in current_q} != set(roster_teams)
                or any(r.get('position_ambiguous') or r['team'] != roster_teams.get(r['driver']) for r in current_q)):
                output['message'] = 'Кваліфікаційні дані неповні, неоднозначні або змінився склад. Оновіть дані; уточнений прогноз поки недоступний.'
            else:
                hypothetical = [{**r,'round':int(race['round']),'date':race['date'],'race_name':race['raceName'],
                                  'circuit':race['Circuit']['circuitId'],'position':i+1,'points':0,'classified':True}
                                 for i,r in enumerate(roster)]
                frame = attach(build_features(records+hypothetical),qualifying)
                field = frame[frame.group.eq(season*100+int(race['round']))].sort_values('driver')
                event = {'round':int(race['round']),'name':race['raceName'],'date':race['date'],'sprint':False,
                         'pace':pace_positions(pace,field).tolist(),'risk':probability(risk,metadata['calibration'],field).tolist()}
                pools = metadata['uncertainty_pools']
                result = simulate(snapshot,known,roster,[event],np.array(pools['team']),np.array(pools['individual']))
                # This is a single-race forecast; never present its partial standings as the full championship.
                result.update({'model':metadata['version'],'forecast_version':'post-qualifying-race-v1',
                               'season':season,'through_round':through,'generated':now.isoformat(),
                               'source_updated':snapshot['fetched_at'],'artifact_sha256':artifact_hashes(directory),
                               'uncertainty':{'residual_year':season}})
                freeze(root,result,snapshot['calendar'],'post-qualifying',now)
                output.update({'status':'ready','forecast':result,
                               'message':'Використано фактичну кваліфікацію. Стартова решітка зі штрафами не підтверджена й не підміняється кваліфікаційними місцями.'})
    (root/'reports/post-qualifying.json').write_text(json.dumps(output,indent=2,ensure_ascii=False),encoding='utf-8')
    return output
