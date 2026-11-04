import hashlib
import json
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
TARGET=ROOT/'Library/FluidModelDHarness'
before=json.loads((HERE/'before-sha256.json').read_text(encoding='utf-8'))
prefix='Assets/_Project/Features/Bartending/FluidGpuExperiment/'
allowed={prefix+p for p in ('README.md','Runtime/FluidExperimentBody.cs','Runtime/FluidExperimentInteractor.cs',
    'Runtime/FluidExperimentInteractor.Pointer.cs','Runtime/FluidExperimentWorld.cs',
    'Runtime/FluidExperimentWorld.ShardContacts.cs','Runtime/FluidExperimentShakerPart.cs')}
changed=[p for p,h in before.items() if hashlib.sha256((ROOT/p).read_bytes()).hexdigest()!=h]
summary={'original_files':len(before),'unchanged':len(before)-len(changed),'changed':changed,
         'unexpected_changes':sorted(set(changed)-allowed),'suites':[],'source_mismatches':[],'metadata_errors':[]}
for name in ('02-input-native-final','03-stir-cocktails','04-input-regression','05-shards','06-runtime','07-garnish'):
    folder=HERE/name; status=json.loads((folder/'exit.json').read_text(encoding='utf-8-sig'))
    results=list(folder.glob('*result.txt'))
    checks=sum(sum(s.startswith('PASS ') for s in p.read_text(encoding='utf-8-sig').splitlines()) for p in folder.glob('*validation.txt'))
    ok=bool(results) and status['exitCode']==0 and not status['timedOut'] and all(p.read_text().strip()=='PASS' for p in results)
    summary['suites'].append({'attempt':name,'result':'PASS' if ok else 'FAIL','checks':checks})
    manifest=json.loads((folder/'source-sha256.json').read_text(encoding='utf-8-sig'))
    for relative,expected in manifest.items():
        for label,base in (('workspace',ROOT),('harness',TARGET)):
            if hashlib.sha256((base/relative).read_bytes()).hexdigest()!=expected:
                summary['source_mismatches'].append(name+':'+label+':'+relative)
guids={}
for path in (ROOT/prefix).rglob('*.meta'):
    match=re.search(r'^guid: (\S+)',path.read_text(encoding='utf-8-sig'),re.MULTILINE)
    if not match or not re.fullmatch('[0-9a-f]{32}',match[1]) or match[1] in guids:
        summary['metadata_errors'].append(str(path))
    else: guids[match[1]]=path
summary['passing_checks']=sum(p['checks'] for p in summary['suites'] if p['result']=='PASS')
summary['result']='PASS' if not any(summary[p] for p in ('unexpected_changes','source_mismatches','metadata_errors')) and all(
    s['result']=='PASS' for s in summary['suites']) else 'FAIL'
(HERE/'validation-summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2)); raise SystemExit(summary['result']!='PASS')
