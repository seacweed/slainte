"""Audit real Unity completions against this turn's preserved baseline."""
from pathlib import Path
import hashlib
import json
import subprocess

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
FEATURE='Assets/_Project/Features/Bartending/FluidGpuExperiment/'
PREFIX='20261021-hose-fracture-final-'
SUITES=('dispenser','breakage','runtime','auto-f','auto-g','input','contents','modes')
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
before=json.loads((HERE/'before-sha256.json').read_text(encoding='utf-8'))
changed=[p for p,h in before.items() if not (ROOT/p).is_file() or sha(ROOT/p)!=h]
allowed={FEATURE+'Runtime/'+f for f in (
    'FluidExperimentDispenser.cs','FluidExperimentDispenser.Motion.cs',
    'FluidExperimentBreakableGlass.cs','FluidExperimentGlassShard.cs','FluidExperimentBreakageProfile.cs')}
allowed.add(FEATURE+'README.md')
runs=[]
for suite in SUITES:
    folder=ROOT/'FluidGpuExperimentEvidence'/(PREFIX+suite)
    info=json.loads((folder/'exit.json').read_text(encoding='utf-8-sig'))
    command=json.loads((folder/'command.json').read_text(encoding='utf-8-sig'))
    manifest=json.loads((folder/'source-sha256.json').read_text(encoding='utf-8-sig'))
    mismatches=[p for p,h in manifest.items() if Path(p).suffix!='.md' and
                (not (ROOT/p).is_file() or sha(ROOT/p)!=h)]
    harness=json.loads((folder/'harness-sha256.json').read_text(encoding='utf-8-sig'))
    snapshot=ROOT/'FluidGpuExperimentEvidence/HarnessSnapshots'/sha(folder/'harness-sha256.json')
    harness_mismatches=[p for p,h in harness.items() if not (snapshot/p).is_file() or sha(snapshot/p)!=h]
    reports=list(folder.glob('*validation.txt')) or list(folder.glob('*diagnosis.txt'))
    lines=[l for f in reports for l in f.read_text(encoding='utf-8-sig').splitlines()]
    results=list(folder.glob('*result.txt'))
    result_pass=bool(results) and all(f.read_text(encoding='utf-8-sig').strip()=='PASS' for f in results)
    log=(folder/'Editor.log').read_text(encoding='utf-8-sig',errors='replace')
    item=dict(attempt=folder.name,entry=command['entryMethod'],**info,
              passedAssertions=sum(l.startswith('PASS ') for l in lines),
              failedAssertions=sum(l.startswith('FAIL ') for l in lines),resultPassed=result_pass,
              sourceMismatches=mismatches,harnessSnapshotMismatches=harness_mismatches,
              measures=[l for l in lines if l.startswith('MEASURE')],
              applicationErrors=(folder/'application-errors.txt').exists() or 'Shader error in' in log or 'Skipping dispatch' in log)
    item['passed']=info['exitCode']==0 and not info['timedOut'] and command.get('forceShaderImport') is True \
        and item['passedAssertions']>0 and item['failedAssertions']==0 and result_pass \
        and not mismatches and not harness_mismatches and not item['applicationErrors']
    runs.append(item)

audio_paths=['Assets/Resources/Core/Audio','Assets/_Project/Core/Runtime/Audio',
             'Assets/_Project/Features/Business/Audio','Packages']
audio_git_changes=subprocess.check_output(['git','diff','--name-only','HEAD','--',*audio_paths],cwd=ROOT,text=True).splitlines()
checks=dict(allRunsCompletedAndPassed=all(r['passed'] for r in runs),
            changesConfinedToExpectedPhysicsFiles=set(changed)<=allowed,
            serializedAssetsAndMetadataPreserved=all(Path(p).suffix in {'.cs','.md'} for p in changed),
            soundCodePlanningAndPackagesPreserved=not any(p in changed for p in before if not p.startswith(FEATURE)),
            trackedSoundAssetsAndPackagesUnchanged=not audio_git_changes)
audit=dict(passed=all(checks.values()),checks=checks,
           head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
           changedSinceTaskBaseline=changed,totalRuns=len(runs),
           totalPassingAssertions=sum(r['passedAssertions'] for r in runs),
           audioGitChanges=audio_git_changes,runs=runs)
(HERE/'audit.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in audit.items() if k not in {'runs','changedSinceTaskBaseline'}},ensure_ascii=False,indent=2))
raise SystemExit(0 if audit['passed'] else 1)
