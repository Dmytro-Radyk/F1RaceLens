import copy
import json
import math
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

from f1ml.models import artifact_hashes
from f1ml.forecast.ledger import freeze,assess,read

ROOT=Path(__file__).resolve().parents[1]


class LedgerTests(unittest.TestCase):
    def setUp(self):
        cache=ROOT/'.cache/tmp'
        cache.mkdir(parents=True,exist_ok=True)
        self.temp=tempfile.TemporaryDirectory(dir=cache)
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        models=self.root/'models/lightgbm-v4'
        models.mkdir(parents=True)
        (models/'pace.txt').write_text('test',encoding='utf-8')
        self.now=datetime(2026,10,19,tzinfo=timezone.utc)
        self.calendar=[{'round':'18','date':'2026-10-25','time':'20:00:00Z'}]
        self.report={'season':2026,'through_round':17,'model':'lightgbm-race-v4','simulations':4000,
                     'generated':'2026-10-18T00:00:00+00:00','source_updated':'2026-10-17T00:00:00+00:00',
                     'artifact_sha256':artifact_hashes(models),
                     'races':[{'round':18,'name':'Test','drivers':[{'driver':'a','win_chance':60},{'driver':'b','win_chance':40}]}]}

    def test_first_snapshot_not_overwritten_and_scoring_uses_it(self):
        names=freeze(self.root,self.report,self.calendar,now=self.now)
        original=(self.root/'data/frozen-forecasts'/names[0]).read_bytes()
        changed=copy.deepcopy(self.report)
        changed['races'][0]['drivers'].reverse()
        freeze(self.root,changed,self.calendar,now=self.now)
        self.assertEqual(original,(self.root/'data/frozen-forecasts'/names[0]).read_bytes())
        pending=assess(self.root,[],self.now)
        self.assertEqual(pending['groups'][0]['races'],0)
        actual=[{'season':2026,'round':18,'driver':'b','position':1},{'season':2026,'round':18,'driver':'a','position':2}]
        scored=assess(self.root,actual,datetime(2026,10,26,tzinfo=timezone.utc))['rows'][0]
        self.assertFalse(scored['winner_hit'])
        self.assertEqual(scored['rank_mae'],1)
        self.assertAlmostEqual(scored['winner_brier_multiclass'],.72)
        self.assertAlmostEqual(scored['winner_logloss'],-math.log(.4))
        groups=assess(self.root,actual,datetime(2026,10,26,tzinfo=timezone.utc))['groups']
        self.assertEqual(sum(b['entries'] for b in groups[0]['calibration_bins']),2)

    def test_late_unknown_start_and_changed_weights_rejected(self):
        self.assertEqual(freeze(self.root,self.report,self.calendar,now=datetime(2026,10,26,tzinfo=timezone.utc)),[])
        with self.assertRaises(ValueError): freeze(self.root,self.report,[{'round':'18','date':'2026-10-25'}],now=self.now)
        (self.root/'models/lightgbm-v4/pace.txt').write_text('changed',encoding='utf-8')
        with self.assertRaises(ValueError): freeze(self.root,self.report,self.calendar,now=self.now)

    def test_tampering_and_partial_actual_results_never_silently_score(self):
        name=freeze(self.root,self.report,self.calendar,now=self.now)[0]
        actual=[{'season':2026,'round':18,'driver':'b','position':1}]
        assessed=assess(self.root,actual,datetime(2026,10,26,tzinfo=timezone.utc))
        self.assertEqual(assessed['rows'][0]['status'],'roster-mismatch')
        path=self.root/'data/frozen-forecasts'/name
        record=json.loads(path.read_text(encoding='utf-8'))
        record['payload']['forecast']['drivers'][0]['win_chance']=99
        path.write_text(json.dumps(record),encoding='utf-8')
        with self.assertRaises(ValueError): read(path)
