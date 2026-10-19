"""Local first-write forecast snapshots and derived, reproducible scoring reports."""

import hashlib
import json
import math
from datetime import datetime, timezone

from ..models import artifact_hashes


def utc(value):
    stamp = datetime.fromisoformat(value.replace('Z','+00:00'))
    if stamp.tzinfo is None:
        raise ValueError('UTC offset required')
    return stamp


def race_start(race):
    if not race.get('time'):
        raise ValueError('Race start time unknown; snapshot cannot be sealed')
    return utc(race['date']+'T'+race['time'])


def seal(payload):
    encoded = json.dumps(payload,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode('utf-8')
    return hashlib.sha256(encoded).hexdigest()


def read(path):
    record = json.loads(path.read_text(encoding='utf-8'))
    if record['sha256'] != seal(record['payload']):
        raise ValueError(f'Forecast snapshot changed: {path.name}')
    return record['payload']


def freeze(root, report, calendar, stage='pre-weekend', now=None):
    now = now or datetime.now(timezone.utc)
    if stage not in ('pre-weekend','post-qualifying'):
        raise ValueError('Invalid forecast stage')
    version = report['model'].replace('lightgbm-race-','lightgbm-')
    if version not in ('lightgbm-v1','lightgbm-v2','lightgbm-v3','lightgbm-v4','lightgbm-q1'):
        raise ValueError('Unsupported snapshot model')
    if artifact_hashes(root/f'models/{version}') != report['artifact_sha256']:
        raise ValueError('Forecast weights changed before snapshot')
    if utc(report['generated']) > now:
        raise ValueError('Forecast generated in future')
    directory = root/'data/frozen-forecasts'
    directory.mkdir(parents=True,exist_ok=True)
    saved = []
    for forecast in report['races']:
        races = [r for r in calendar if int(r['round']) == forecast['round']]
        if len(races) != 1 or forecast['round'] <= report['through_round']:
            raise ValueError('Snapshot cutoff/calendar inconsistent')
        start = race_start(races[0])
        if now >= start:
            continue
        drivers = forecast['drivers']
        if len({r['driver'] for r in drivers}) != len(drivers) or len(drivers) < 2:
            raise ValueError('Invalid forecast participants')
        if any(not math.isfinite(r['win_chance']) or not 0 <= r['win_chance'] <= 100 for r in drivers):
            raise ValueError('Invalid win probabilities')
        if not math.isclose(sum(r['win_chance'] for r in drivers),100,abs_tol=1e-6):
            raise ValueError('Incomplete winner distribution')
        payload = {'schema':1,'season':report['season'],'round':forecast['round'], 'name':forecast['name'],
                   'model':report['model'],'stage':stage,'frozen_at':now.isoformat(),'race_start':start.isoformat(),
                   'forecast_generated':report['generated'],'through_round':report['through_round'],
                   'artifact_sha256':report['artifact_sha256'],'simulations':report['simulations'],
                   'forecast':forecast,
                   'rank_definition':'Order by expected race points; not mean finish position',
                   'source_updated':report['source_updated']}
        path = directory/f"{report['season']}-{forecast['round']:02d}-{stage}-{version}.json"
        if path.exists():
            read(path)  # Never silently replace even with a newer calculation.
        else:
            with path.open('x',encoding='utf-8') as stream:
                json.dump({'sha256':seal(payload),'payload':payload},stream,indent=2,ensure_ascii=False)
        saved.append(path.name)
    return saved


def assess(root, results, now=None):
    now = now or datetime.now(timezone.utc)
    rows = []
    snapshots = [read(path) for path in sorted((root/'data/frozen-forecasts').glob('*.json'))]
    for saved in snapshots:
        if not utc(saved['forecast_generated']) <= utc(saved['frozen_at']) < utc(saved['race_start']):
            raise ValueError('Invalid snapshot chronology')
        actual = [r for r in results if (r['season'],r['round']) == (saved['season'],saved['round'])]
        row = {key:saved[key] for key in ('season','round','name','model','stage','frozen_at')}
        if now < utc(saved['race_start']) or not actual:
            rows.append({**row,'status':'pending'})
            continue
        predicted = saved['forecast']['drivers']
        actual_ids = {r['driver'] for r in actual}
        if actual_ids != {r['driver'] for r in predicted}:
            rows.append({**row,'status':'roster-mismatch','message':'Entrants differ; not scored on a partial field'})
            continue
        positions = {r['driver']:r['position'] for r in actual}
        winners = [r['driver'] for r in actual if r['position']==1]
        if len(winners) != 1 or len({r['position'] for r in actual}) != len(actual):
            raise ValueError('Ambiguous actual classification')
        winner = winners[0]
        chance = {r['driver']:r['win_chance']/100 for r in predicted}
        favourite = max(predicted,key=lambda r:r['win_chance'])['driver']
        # Winner log loss is clipped at half a simulated outcome, declared in report.
        floor = 1/(2*saved['simulations'])
        rows.append({**row,'status':'scored','actual_winner':winner,'favourite':favourite,
                     'winner_hit':favourite==winner,'winner_probability':chance[winner],
                     'rank_mae':sum(abs(i+1-positions[r['driver']]) for i,r in enumerate(predicted))/len(predicted),
                     'winner_brier_multiclass':sum((p-int(d==winner))**2 for d,p in chance.items()),
                     'winner_logloss':-math.log(max(chance[winner],floor)),
                     'logloss_probability_floor':floor})
    groups = []
    for key in sorted({(r['model'],r['stage']) for r in rows}):
        scored = [r for r in rows if (r['model'],r['stage'])==key and r['status']=='scored']
        bins = []
        for low in range(0,100,10):
            observations = []
            for payload in snapshots:
                if (payload['model'],payload['stage']) != key:
                    continue
                match = next((r for r in scored if r['season']==payload['season'] and r['round']==payload['round']),None)
                if match:
                    for driver in payload['forecast']['drivers']:
                        p = driver['win_chance']/100
                        if low/100 <= p < (low+10)/100 or (low==90 and p==1):
                            observations.append((p,int(driver['driver']==match['actual_winner'])))
            if observations:
                bins.append({'lower':low/100,'upper':(low+10)/100,'entries':len(observations),
                             'mean_probability':sum(x[0] for x in observations)/len(observations),
                             'observed_win_rate':sum(x[1] for x in observations)/len(observations)})
        groups.append({'model':key[0],'stage':key[1],'races':len(scored),'calibration_bins':bins,
                       'winner_accuracy':sum(r['winner_hit'] for r in scored)/len(scored) if scored else None,
                       'rank_mae':sum(r['rank_mae'] for r in scored)/len(scored) if scored else None})
    report = {'generated':now.isoformat(),'rows':rows,'groups':groups,
              'limitations':['Local timestamps/hash checks detect accidental changes, not external notarization.',
                             'Scores use current official results and can change after revisions.',
                             'Calibration needs many future races; pending forecasts do not establish accuracy.']}
    (root/'reports').mkdir(parents=True,exist_ok=True)
    (root/'reports/prospective-evaluation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    return report
