import copy
import hashlib
import json
import shutil
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch

import pandas as pd

from f1ml.features import build_features
from f1ml.models import LightModel
from f1ml.qualifying.features import attach, QUALIFYING_FEATURES
from f1ml.qualifying.source import normalize
from f1ml.qualifying.forecast import forecast
from f1ml.source import normalize as results_normalize
from f1ml.training import evaluate
from test_pipeline import race

ROOT = Path(__file__).resolve().parents[1]


def quali(records):
    return [{k:r[k] for k in ('season','round','driver','team','circuit','position')} for r in records]


class QualifyingTests(unittest.TestCase):
    def test_own_race_labels_do_not_enter_qualifying_features(self):
        records = race(2026,1)+race(2026,2)
        before = attach(build_features(records),quali(records))
        changed = copy.deepcopy(records)
        for row in changed[10:]:
            row['position']=999
            row['points']=999
            row['classified']=False
        after = attach(build_features(changed),quali(records))
        pd.testing.assert_frame_equal(before[QUALIFYING_FEATURES],after[QUALIFYING_FEATURES])

    def test_absent_ambiguous_and_conflicting_qualifying(self):
        records = race(2026,1)
        q = quali(records)
        q[0]['position_ambiguous']=True
        frame = attach(build_features(records),q[:-1])
        self.assertEqual(frame.qualifying_known.sum(),8)
        self.assertTrue(frame.loc[frame.qualifying_known.eq(0),'qualifying_position'].eq(.55).all())
        q[1]['team']='different'
        with self.assertRaises(ValueError):
            attach(build_features(records),q)

    def test_pagination_duplicates_and_totals(self):
        entries = [{'position':str(i+1),'Driver':{'driverId':f'd{i}'},'Constructor':{'constructorId':'team'}} for i in range(3)]
        def page(offset,items):
            return {'MRData':{'total':'3','offset':str(offset),'RaceTable':{'season':'2026','Races':[
                {'season':'2026','round':'1','Circuit':{'circuitId':'test'},'QualifyingResults':items}]}}}
        pages=[page(0,entries[:2]),page(2,entries[2:])]
        self.assertEqual(len(normalize(pages,2026)),3)
        with self.assertRaises(ValueError): normalize(pages[:1],2026)
        pages[1]['MRData']['RaceTable']['Races'][0]['QualifyingResults'][0]['Driver']['driverId']='d0'
        with self.assertRaises(ValueError): normalize(pages,2026)
        entries[2]['Driver']['driverId']='d2'
        entries[2]['position']='2'
        rows=normalize([page(0,entries)],2026)
        self.assertEqual(sum(r['position_ambiguous'] for r in rows),2)


@unittest.skipUnless((ROOT/'models/lightgbm-q1/metadata.json').exists(),'Train qualifying model first')
class QualifyingArtifactTests(unittest.TestCase):
    def test_saved_qualification_schema_and_audit_reproduce(self):
        records=[]
        qualifying=[]
        for year in range(2010,2026):
            records+=results_normalize(json.loads((ROOT/f'data/raw/results-{year}.json').read_text(encoding='utf-8'))['pages'],year)
            if year>=2014:
                qualifying+=normalize(json.loads((ROOT/f'data/qualifying/qualifying-{year}.json').read_text(encoding='utf-8'))['pages'],year)
        frame=attach(build_features(records),qualifying)
        directory=ROOT/'models/lightgbm-q1-audit-2025'
        metadata=json.loads((directory/'metadata.json').read_text(encoding='utf-8'))
        pace,risk=LightModel.load(directory/'pace.txt'),LightModel.load(directory/'nonclassification.txt')
        self.assertEqual(pace.features,QUALIFYING_FEATURES)
        actual=evaluate(frame[frame.season.eq(2025)],pace,risk,metadata['calibration'])
        for key in ['rank_mae','winner_accuracy','nonclass_brier']:
            self.assertAlmostEqual(actual[key],metadata['evaluation'][key],places=12)
        for name,digest in metadata['artifact_sha256'].items():
            self.assertEqual(hashlib.sha256((directory/name).read_bytes()).hexdigest(),digest)

    def test_future_forecast_waits_for_data_then_seals_before_race(self):
        cache=ROOT/'.cache/tmp'
        cache.mkdir(parents=True,exist_ok=True)
        with tempfile.TemporaryDirectory(dir=cache) as directory:
            root=Path(directory)
            shutil.copytree(ROOT/'models/lightgbm-q1',root/'models/lightgbm-q1')
            (root/'reports').mkdir()
            records=race(2026,17)
            q=quali([{**r,'round':18} for r in records])
            event={'round':'18','raceName':'Test Grand Prix','date':'2026-11-01','time':'20:00:00Z',
                   'Circuit':{'circuitId':'test'},'Qualifying':{'date':'2026-10-31','time':'20:00:00Z'}}
            snapshot={'season':2026,'through':17,'fetched_at':'2026-10-19T12:00:00+00:00',
                      'drivers':[{'id':r['driver'],'name':r['name'],'points':0,'position':r['position']} for r in records],
                      'teams':[{'id':f'team{i}','name':f'Team {i}','points':0,'position':i+1} for i in range(5)],'calendar':[event]}
            with patch('f1ml.qualifying.forecast.fetch_input',return_value=snapshot):
                waiting=forecast(root,records,q,datetime(2026,10,30,tzinfo=timezone.utc))
                self.assertEqual(waiting['status'],'waiting')
                partial=forecast(root,records,q[:-1],datetime(2026,10,31,22,tzinfo=timezone.utc))
                self.assertEqual(partial['status'],'waiting')
                ready=forecast(root,records,q,datetime(2026,10,31,22,tzinfo=timezone.utc))
                self.assertEqual(ready['status'],'ready')
                self.assertEqual(len(ready['forecast']['races']),1)
                self.assertEqual(len(list((root/'data/frozen-forecasts').glob('*.json'))),1)
                ended=forecast(root,records,q,datetime(2026,11,1,21,tzinfo=timezone.utc))
                self.assertEqual(ended['status'],'waiting')
