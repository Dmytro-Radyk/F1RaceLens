import hashlib
import json
from datetime import datetime, timezone

from ..source import request_json, BASE_URL


def normalize(pages, season):
    rows, seen, expected = [], set(), None
    for page in pages:
        data = page['MRData']
        total = int(data['total'])
        if not 0 <= total <= 2000 or int(data['offset']) != len(rows) or (expected is not None and expected != total):
            raise ValueError('Invalid qualifying pagination')
        expected = total
        table = data['RaceTable']
        if int(table['season']) != season:
            raise ValueError('Unexpected qualifying season')
        for race in table['Races']:
            if int(race['season']) != season or not 1 <= int(race['round']) <= 40:
                raise ValueError('Invalid qualifying race')
            for entry in race['QualifyingResults']:
                key = (season,int(race['round']),entry['Driver']['driverId'])
                position = int(entry['position'])
                if key in seen or not 1 <= position <= 40:
                    raise ValueError('Duplicated or invalid qualifying result')
                seen.add(key)
                rows.append({'season':season,'round':key[1],'driver':key[2],
                             'position':position,'team':entry['Constructor']['constructorId'],
                             'circuit':race['Circuit']['circuitId']})
    if expected is None or len(rows) != expected:
        raise ValueError('Incomplete qualifying snapshot')
    groups = {}
    for row in rows:
        positions = groups.setdefault(row['round'],{})
        positions[row['position']] = positions.get(row['position'],0)+1
    # Some revised classifications share positions (e.g. Britain 2023).
    # Keep the source, but do not teach the model a fabricated ordering.
    for row in rows:
        row['position_ambiguous'] = groups[row['round']][row['position']] > 1
    return rows


def fetch(season, destination, refresh=False):
    destination.mkdir(parents=True,exist_ok=True)
    path = destination/f'qualifying-{season}.json'
    if path.exists() and not refresh:
        return normalize(json.loads(path.read_text(encoding='utf-8'))['pages'],season)
    pages,offset = [],0
    while True:
        page = request_json(f'{season}/qualifying.json?limit=100&offset={offset}')
        data = page['MRData']
        count = sum(len(r['QualifyingResults']) for r in data['RaceTable']['Races'])
        if int(data['offset']) != offset or (count == 0 and offset < int(data['total'])):
            raise ValueError('Invalid qualifying page')
        pages.append(page)
        offset += count
        if offset >= int(data['total']):
            break
    rows = normalize(pages,season)
    payload = {'source':BASE_URL,'fetched_at':datetime.now(timezone.utc).isoformat(),'pages':pages}
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(payload),encoding='utf-8')
    temporary.replace(path)
    print(f'Qualifying {season}: {len(rows)} entries',flush=True)
    return rows


def source_hash(directory):
    digest = hashlib.sha256()
    for path in sorted(directory.glob('qualifying-*.json')):
        digest.update(path.name.encode())
        digest.update(path.read_bytes())
    return digest.hexdigest()
