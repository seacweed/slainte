"""Verify final source parity and real, shader-reimported Unity completions."""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess

root = Path(__file__).resolve().parents[2]
feature = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('attempts', nargs='+')
parser.add_argument('--output', default='ice-stability-audit.json')
args = parser.parse_args()
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
changed = subprocess.check_output(['git', '-c', 'core.safecrlf=false', 'diff', '--name-only'], cwd=root, text=True).splitlines()
allowed_runtime = {
    'FluidExperimentBody.cs', 'FluidExperimentBody.Contents.cs', 'FluidExperimentBody.GarnishMotion.cs',
    'FluidExperimentBody.IceStock.cs', 'FluidExperimentComparison.cs', 'FluidExperimentGpuLiquid.cs',
    'FluidExperimentBody.Geometry.cs',
    'FluidExperimentGpuLiquid.IceFlow.cs',
    'FluidExperimentGpuLiquid.Garnish.cs', 'FluidExperimentGpuLiquid.Buffers.cs',
    'FluidExperimentGpuLiquid.ImprovedSurface.cs', 'FluidExperimentGpuTypes.cs',
    'FluidExperimentLiquidSettings.cs', 'FluidExperimentRuntimeRig.cs', 'FluidExperimentWorld.cs',
}
allowed = {str(feature/'Runtime'/p).replace('\\','/') for p in allowed_runtime}
allowed.update(str(feature/p).replace('\\','/') for p in (
    'README.md', 'DEVELOPMENT_PLAN.md', 'Shaders/FluidExperimentLiquid.compute',
    'Shaders/FluidExperimentCohesiveContacts.hlsl'))
allowed.update(('FluidGpuExperimentEvidence/README.md', 'FluidGpuExperimentEvidence/HarnessSource/IceContactValidation.cs',
                'FluidGpuExperimentEvidence/HarnessSource/GarnishMotionValidation.cs'))
harness = root/'Library/FluidModelDHarness'
checked = 0
mismatches, folders = [], []
for source in (root/feature).rglob('*'):
    if not source.is_file():
        continue
    # Reports can be finalized after execution; runtime and serialized assets
    # must remain identical to the exact source used by the Unity run.
    if source.suffix == '.md':
        continue
    relative = source.relative_to(root)
    if source.suffix == '.meta' and source.with_suffix('').is_dir():
        folders.append(relative.as_posix())
        continue
    checked += 1
    target = harness/relative
    if not target.is_file() or sha(source) != sha(target):
        mismatches.append(relative.as_posix())
suites = []
for attempt in args.attempts:
    folder = root/'FluidGpuExperimentEvidence'/attempt
    info = json.loads((folder/'exit.json').read_text(encoding='utf-8-sig'))
    command = json.loads((folder/'command.json').read_text(encoding='utf-8-sig'))
    manifest = json.loads((folder/'source-sha256.json').read_text(encoding='utf-8-sig'))
    # Documentation is allowed to describe results after execution. Every code,
    # shader, scene, prefab, profile and binary asset must match the tested snapshot.
    source_mismatches = [p for p, digest in manifest.items() if Path(p).suffix != '.md'
                         and (not (root/p).is_file() or sha(root/p) != digest)]
    harness_manifest = json.loads((folder/'harness-sha256.json').read_text(encoding='utf-8-sig'))
    snapshot = root/'FluidGpuExperimentEvidence/HarnessSnapshots'/sha(folder/'harness-sha256.json')
    harness_mismatches = [p for p, digest in harness_manifest.items()
                          if not (snapshot/p).is_file() or sha(snapshot/p) != digest]
    entry_files = {
        'CrowdedIceValidation':'CrowdedIceValidation.cs',
        'IceBuoyancyValidation':'IceBuoyancyValidation.cs',
        'IceFirstPourValidation':'IceFirstPourValidation.cs',
        'HeldLiquidOnlyValidation':'HeldLiquidOnlyValidation.cs',
        'CrowdedIcePlayLoopValidation':'CrowdedIcePlayLoopValidation.cs',
        'FloatingContentsValidation':'FloatingContentsValidation.cs',
        'ExperimentIceContactValidation':'IceContactValidation.cs',
        'FloatingContentsPlayLoopValidation':'FloatingContentsPlayLoopValidation.cs',
        'ExperimentGarnishMotionValidation':'GarnishMotionValidation.cs',
        'MixedFloatSettleValidation':'MixedFloatSettleValidation.cs',
        'ExperimentGlassPickupLiquidValidation':'GlassPickupLiquidValidation.cs',
        'ExperimentInputShakerValidation':'InputShakerValidation.cs',
        'ExperimentDispenserValidation':'DispenserValidation.cs',
        'ExperimentGlassBreakageValidation':'GlassBreakageValidation.cs',
    }
    entry_file = entry_files[command['entryMethod'].rsplit('.',1)[0]]
    entry_current = all(harness_manifest.get(p)==sha(root/'FluidGpuExperimentEvidence/HarnessSource'/p)
                        for p in (entry_file,'PhysicsLabValidationDispatch.cs'))
    reports = list(folder.glob('*validation.txt')) or list(folder.glob('*diagnosis.txt'))
    lines = [line for report in reports for line in report.read_text(encoding='utf-8-sig').splitlines()]
    results = list(folder.glob('*result.txt'))
    # These newer harnesses write their terminal success to the assertion log,
    # then exit Unity with zero; they do not emit a separate result.txt file.
    terminal_log_entries = {'IceBuoyancyValidation', 'IceFirstPourValidation',
                            'HeldLiquidOnlyValidation'}
    terminal_log_pass = (command['entryMethod'].rsplit('.',1)[0] in terminal_log_entries
                         and bool(lines) and lines[-1] == 'PASS No Unity application errors')
    result_passed = (all(p.read_text(encoding='utf-8-sig').strip()=='PASS' for p in results)
                     if results else terminal_log_pass)
    log = (folder/'Editor.log').read_text(encoding='utf-8-sig', errors='replace')
    suites.append(dict(
        attempt=attempt, entryMethod=command.get('entryMethod'), exitCode=info.get('exitCode'),
        timedOut=info.get('timedOut'), forceShaderImport=command.get('forceShaderImport') is True,
        passedAssertions=sum(line.startswith('PASS ') for line in lines),
        failedAssertions=sum(line.startswith('FAIL ') for line in lines),
        resultPassed=result_passed,
        resultEvidence='result.txt' if results else 'terminal assertion log',
        sourceMismatches=source_mismatches,
        harnessSourceMismatches=harness_mismatches, harnessSourceFiles=len(harness_manifest),
        harnessSnapshot=snapshot.relative_to(root).as_posix(), currentEntryAndDispatcherMatch=entry_current,
        applicationErrors=(folder/'application-errors.txt').exists() or "Shader error in" in log or "Skipping dispatch" in log,
    ))
checks = dict(
    scopedTrackedChanges=set(changed) <= allowed,
    protectedSerializedAssetsUnchanged=all(Path(p).suffix in {'.cs','.compute','.hlsl','.md'} for p in changed),
    deliveredHarnessParity=not mismatches,
    allRunsPassed=all(s['exitCode']==0 and s['timedOut'] is False and s['forceShaderImport']
                     and s['passedAssertions']>0 and s['failedAssertions']==0 and s['resultPassed']
                     and not s['sourceMismatches'] and not s['harnessSourceMismatches']
                     and s['harnessSourceFiles']>0 and s['currentEntryAndDispatcherMatch']
                     and not s['applicationErrors'] for s in suites),
)
audit = dict(passed=all(checks.values()), checks=checks,
             baseHead=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),
             changedTrackedFiles=changed, harnessCheckedFiles=checked, harnessMismatches=mismatches,
             generatedFolderMetadataExclusions=folders,
             totalPassingAssertions=sum(s['passedAssertions'] for s in suites), suites=suites)
(root/'FluidGpuExperimentEvidence'/args.output).write_text(json.dumps(audit,indent=2)+'\n',encoding='utf-8')
print(json.dumps(audit,indent=2))
raise SystemExit(0 if audit['passed'] else 1)
