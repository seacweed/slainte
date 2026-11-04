"""Audit phase-one native/GPU completions and preserved physics/material scope."""
from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
FEATURE='Assets/_Project/Features/Bartending/FluidGpuExperiment/'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
before=json.loads((HERE/'before-sha256.json').read_text(encoding='utf-8'))
changed=[p for p,h in before.items() if not (ROOT/p).is_file() or sha(ROOT/p)!=h]
allowed={FEATURE+p for p in ['README.md','Scenes/FluidGpuComparison.unity',
    'Runtime/FluidExperimentGpuLiquid.Shards.cs','Shaders/FluidExperimentLiquid.compute']}
runs=[]
for suite in ('contacts','splash-kernel','input','garnish','ice-modes'):
    folder=HERE.parent/('20261026-contact-final-'+suite)
    info=json.loads((folder/'exit.json').read_text(encoding='utf-8-sig'))
    command=json.loads((folder/'command.json').read_text(encoding='utf-8-sig'))
    manifest=json.loads((folder/'source-sha256.json').read_text(encoding='utf-8-sig'))
    source_bad=[p for p,h in manifest.items() if Path(p).suffix!='.md' and (not (ROOT/p).is_file() or sha(ROOT/p)!=h)]
    harness=json.loads((folder/'harness-sha256.json').read_text(encoding='utf-8-sig'))
    snapshot=HERE.parent/'HarnessSnapshots'/sha(folder/'harness-sha256.json')
    harness_bad=[p for p,h in harness.items() if not (snapshot/p).is_file() or sha(snapshot/p)!=h
                 or sha(HERE.parent/'HarnessSource'/p)!=h]
    reports=list(folder.glob('*validation.txt')) or list(folder.glob('*diagnosis.txt'))
    lines=[l for f in reports for l in f.read_text(encoding='utf-8-sig').splitlines()]
    results=list(folder.glob('*result.txt'))
    result_pass=bool(results) and all(f.read_text(encoding='utf-8-sig').strip()=='PASS' for f in results)
    log=(folder/'Editor.log').read_text(encoding='utf-8-sig',errors='replace')
    errors=((folder/'application-errors.txt').exists() and bool((folder/'application-errors.txt').read_text(encoding='utf-8-sig').strip())) \
        or 'Shader error in' in log or 'Skipping dispatch' in log
    run=dict(attempt=folder.name,entry=command['entryMethod'],**info,
             passedAssertions=sum(l.startswith('PASS ') for l in lines),failedAssertions=sum(l.startswith('FAIL ') for l in lines),
             resultPassed=result_pass,sourceMismatches=source_bad,harnessMismatches=harness_bad,applicationErrors=errors,
             measures=[l for l in lines if l.startswith('MEASURE')])
    run['passed']=info['exitCode']==0 and not info['timedOut'] and command.get('forceShaderImport') is True \
        and run['passedAssertions']>0 and run['failedAssertions']==0 and result_pass and not source_bad and not harness_bad and not errors
    runs.append(run)
checks=dict(allRunsCompletedAndPassed=all(r['passed'] for r in runs),changesConfinedToContactsAndReadme=set(changed)<=allowed,
            oldFanProfilesAndMaterialTimeSettingsPreserved=not any('/Data/' in p for p in changed),
            nativeShardCreationIceGarnishHoseAndPrefabsPreserved=not any(p not in allowed for p in changed if p.startswith(FEATURE)),
            soundPlanningAndPackagesPreserved=not any(not p.startswith(FEATURE) for p in changed))
audit=dict(passed=all(checks.values()),checks=checks,changedSinceTaskBaseline=changed,
           totalRuns=len(runs),totalPassingAssertions=sum(r['passedAssertions'] for r in runs),runs=runs)
(HERE/'audit.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in audit.items() if k!='runs'},ensure_ascii=False,indent=2))
raise SystemExit(0 if audit['passed'] else 1)
