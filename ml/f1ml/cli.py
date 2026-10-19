"""Local LightGBM training and read-only forecasting commands."""
import argparse
import json
import sys
from pathlib import Path
from .source import fetch_season, snapshot_hash
ROOT = Path(__file__).resolve().parents[1]

def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description='F1 Race Lens / local LightGBM')
    parser.add_argument('command', choices=['download','train','search','continue','forecast','predict','view','qualifying-download','qualifying-train','qualifying-forecast','freeze','assess'])
    parser.add_argument('--threads', type=int, default=4)
    parser.add_argument('--model-version', choices=['lightgbm-v1','lightgbm-v2','lightgbm-v3','lightgbm-v4'], default='lightgbm-v1')
    parser.add_argument('--search-version', choices=['v2','v3','v4'], default='v2')
    parser.add_argument('--port', type=int, default=4300)
    parser.add_argument('--refresh-current', action='store_true')
    args = parser.parse_args()
    if not 1 <= args.threads <= 8:
        parser.error('Use 1-8 CPU threads')
    if args.command == 'view':
        if not 1024 <= args.port <= 65535:
            parser.error('Use port 1024-65535')
        from .viewer import serve
        serve(ROOT, args.port)
        return
    raw = ROOT/'data/raw'
    records = [row for year in range(2010,2027) for row in fetch_season(year, raw, args.refresh_current and year==2026)]
    if args.command == 'download':
        return
    if args.command == 'search':
        from .features import build_features
        from .search import experiment
        if args.search_version == 'v4':
            from .experiments.blend import experiment as blend_experiment
            report = blend_experiment(build_features(records),ROOT,snapshot_hash(raw),args.threads)
        else:
            report = experiment(build_features(records),ROOT,snapshot_hash(raw),args.threads,args.search_version)
        print(json.dumps({key:({k:v for k,v in value.items() if k!='per_race'} if isinstance(value,dict) else value)
                          for key,value in report.items()},indent=2))
        return
    if args.command in ('train','continue'):
        from .features import build_features
        from .training import train
        from .adaptation.train import continue_training
        frame = build_features(records)
        if args.command == 'train':
            audit = train(frame, ROOT/'models/lightgbm-audit-2025', ROOT/'reports/lightgbm-audit-2025', snapshot_hash(raw), args.threads)
            print(json.dumps({'audit_2025':audit['evaluation']}, default=int), flush=True)
        report = continue_training(frame, ROOT, snapshot_hash(raw), args.threads)
        print(json.dumps({'seconds':report['training_seconds'], 'evaluation':report['evaluation']}, indent=2, default=int))
        return
    if args.command.startswith('qualifying-'):
        from .qualifying.source import fetch, source_hash
        qualifying = [row for year in range(2014,2027) for row in fetch(year,ROOT/'data/qualifying',args.refresh_current and year==2026)]
        if args.command == 'qualifying-download':
            return
        if args.command == 'qualifying-train':
            from .qualifying.train import train as train_qualifying
            import hashlib
            combined_hash = hashlib.sha256((snapshot_hash(raw)+':'+source_hash(ROOT/'data/qualifying')).encode()).hexdigest()
            report = train_qualifying(ROOT,records,qualifying,combined_hash,args.threads)
            print(json.dumps({key:({k:v for k,v in value.items() if k!='per_race'} if isinstance(value,dict) else value) for key,value in report.items()},indent=2))
        else:
            from .qualifying.forecast import forecast as forecast_qualifying
            result = forecast_qualifying(ROOT,records,qualifying,refresh=args.refresh_current)
            print(json.dumps({'status':result['status'],'message':result['message']},ensure_ascii=False))
        return
    if args.command == 'assess':
        from .forecast.ledger import assess
        result = assess(ROOT,records)
        print(json.dumps(result['groups'],indent=2))
        return
    if args.command == 'freeze':
        from .forecast.source import fetch_input
        from .forecast.ledger import freeze
        path = ROOT/f'reports/season-forecast-{args.model_version}.json'
        saved = json.loads(path.read_text(encoding='utf-8'))
        snapshot = fetch_input(ROOT,saved['season'],saved['through_round'])
        print(json.dumps(freeze(ROOT,saved,snapshot['calendar']),indent=2))
        return
    from .forecast.engine import forecast
    result = forecast(ROOT, records, args.model_version, args.threads, args.refresh_current)
    output = result['races'][:1] if args.command == 'predict' else {'model':result['model'],'through_round':result['through_round'],'remaining_races':result['remaining_races'],'drivers':result['drivers'][:3]}
    print(json.dumps(output, indent=2, ensure_ascii=False))

if __name__ == '__main__':
    main()
