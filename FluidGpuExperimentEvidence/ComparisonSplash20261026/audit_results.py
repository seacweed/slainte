"""Audit completed GPU tests and this task's narrow scene/profile change."""
from pathlib import Path
import hashlib
import json

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
FEATURE='Assets/_Project/Features/Bartending/FluidGpuExperiment/'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
before=json.loads((HERE/'before-sha256.json').read_text(encoding='utf-8'))
changed=[p for p,h in before.items() if not (ROOT/p).is_file() or sha(ROOT/p)!=h]
new=[p.relative_to(ROOT).as_posix() for p in (ROOT/FEATURE).rglob('*') if p.is_file() and p.relative_to(ROOT).as_posix() not in before]
allowed={FEATURE+'README.md',FEATURE+'Scenes/FluidGpuComparison.unity'}
allowed_new={FEATURE+'Data/ComparisonBreakageSplash.asset',FEATURE+'Data/ComparisonBreakageSplash.asset.meta'}
runs=[]
for suite in ('breakage','kernel'):
    folder=ROOT/'FluidGpuExperimentEvidence'/('20261026-splash-final-'+suite)
    info=json.loads((folder/'exit.json').read_text(encoding='utf-8-sig'))
    command=json.loads((folder/'command.json').read_text(encoding='utf-8-sig'))
    manifest=json.loads((folder/'source-sha256.json').read_text(encoding='utf-8-sig'))
    mismatch=[p for p,h in manifest.items() if Path(p).suffix!='.md' and (not (ROOT/p).is_file() or sha(ROOT/p)!=h)]
    harness=json.loads((folder/'harness-sha256.json').read_text(encoding='utf-8-sig'))
    snapshot=ROOT/'FluidGpuExperimentEvidence/HarnessSnapshots'/sha(folder/'harness-sha256.json')
    harness_bad=[p for p,h in harness.items() if not (snapshot/p).is_file() or sha(snapshot/p)!=h
                 or sha(ROOT/'FluidGpuExperimentEvidence/HarnessSource'/p)!=h]
    lines=[l for f in folder.glob('*validation.txt') for l in f.read_text(encoding='utf-8-sig').splitlines()]
    results=list(folder.glob('*result.txt'))
    result_pass=bool(results) and all(f.read_text(encoding='utf-8-sig').strip()=='PASS' for f in results)
    log=(folder/'Editor.log').read_text(encoding='utf-8-sig',errors='replace')
    errors=(folder/'application-errors.txt').exists() or 'Shader error in' in log or 'Skipping dispatch' in log
    run=dict(attempt=folder.name,entry=command['entryMethod'],**info,
             passedAssertions=sum(l.startswith('PASS ') for l in lines),failedAssertions=sum(l.startswith('FAIL ') for l in lines),
             resultPassed=result_pass,sourceMismatches=mismatch,harnessMismatches=harness_bad,applicationErrors=errors,
             measures=[l for l in lines if l.startswith('MEASURE')])
    run['passed']=info['exitCode']==0 and not info['timedOut'] and command.get('forceShaderImport') is True \
        and run['passedAssertions']>0 and run['failedAssertions']==0 and result_pass and not mismatch and not harness_bad and not errors
    runs.append(run)
scene=(ROOT/(FEATURE+'Scenes/FluidGpuComparison.unity')).read_text(encoding='utf-8-sig')
block=('    - target: {fileID: 2351671212039727122, guid: cdd4cf3ddb1645cbba5bf15a3c86a667, type: 3}\n'
       '      propertyPath: splashProfile\n      value:\n'
       '      objectReference: {fileID: 11400000, guid: 08647d3582e14b498e37d895b82fcc6d, type: 2}\n')
scene_exact=scene.count(block)==1 and scene.replace(block,'')==(HERE/'comparison-before.unity.txt').read_text(encoding='utf-8-sig')
checks=dict(allRunsCompletedAndPassed=all(r['passed'] for r in runs),
            scopeLimitedToComparisonSceneProfileAndReadme=set(changed)<=allowed and set(new)==allowed_new,
            sceneOnlyAddsTheHurricaneSplashReference=scene_exact,
            allRuntimeCodeShadersPrefabsExistingProfilesAndMetadataPreserved=not any(p not in allowed for p in changed if p.startswith(FEATURE)),
            soundPlanningAndPackagesPreserved=not any(not p.startswith(FEATURE) for p in changed))
audit=dict(passed=all(checks.values()),checks=checks,changedSinceTaskBaseline=changed,newFeatureFiles=new,
           totalRuns=len(runs),totalPassingAssertions=sum(r['passedAssertions'] for r in runs),runs=runs)
(HERE/'audit.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in audit.items() if k!='runs'},ensure_ascii=False,indent=2))
raise SystemExit(0 if audit['passed'] else 1)
