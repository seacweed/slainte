"""Audit delivered floating-solid sources, isolated harness parity and completed runs."""
from pathlib import Path
import hashlib
import json
import subprocess

root = Path(__file__).resolve().parents[2]
feature = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
harness = root / 'Library/FluidModelDHarness'
attempts = (
    '20261021-ice-mode-variants-02',
    '20261021-floating-contents-v2',
    '20261021-solid-rest-v2',
    '20261021-garnish-render-lemon',
    '20261021-garnish-render-nananga',
    '20261021-floating-garnish-v2',
    '20261021-floating-pickup-v2',
    '20261021-floating-input-v2',
    '20261021-floating-dispenser-v2',
    '20261021-floating-breakage-v2',
)
allowed = {
    str(feature / name).replace('\\', '/') for name in (
        'README.md', 'DEVELOPMENT_PLAN.md',
        'Runtime/FluidExperimentBody.cs', 'Runtime/FluidExperimentBody.Contents.cs',
        'Runtime/FluidExperimentBody.GarnishMotion.cs', 'Runtime/FluidExperimentGpuLiquid.cs',
        'Runtime/FluidExperimentBody.IceStock.cs', 'Runtime/FluidExperimentComparison.cs',
        'Runtime/FluidExperimentGpuLiquid.ImprovedSurface.cs', 'Runtime/FluidExperimentRuntimeRig.cs',
        'Runtime/FluidExperimentGpuLiquid.Garnish.cs', 'Runtime/FluidExperimentLiquidSettings.cs',
        'Runtime/FluidExperimentWorld.cs', 'Shaders/FluidExperimentLiquid.compute',
    )
}
allowed.add('FluidGpuExperimentEvidence/README.md')
changed = subprocess.check_output(
    ['git', '-c', 'core.safecrlf=false', 'diff', '--name-only'], cwd=root, text=True).splitlines()
head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
mismatches = []
folder_metadata = []
checked = 0
for source in (root / feature).rglob('*'):
    if not source.is_file():
        continue
    relative = source.relative_to(root)
    # prepare_experiment copies assets and their metadata, but Unity creates the
    # disposable project's directory metadata. These are not executable assets.
    if source.suffix == '.meta' and source.with_suffix('').is_dir():
        folder_metadata.append(str(relative).replace('\\', '/'))
        continue
    target = harness / relative
    checked += 1
    if not target.is_file() or hashlib.sha256(source.read_bytes()).digest() != hashlib.sha256(target.read_bytes()).digest():
        mismatches.append(str(relative).replace('\\', '/'))
suites = []
for attempt in attempts:
    folder = root / 'FluidGpuExperimentEvidence' / attempt
    exit_path = folder / 'exit.json'
    info = json.loads(exit_path.read_text(encoding='utf-8-sig')) if exit_path.exists() else {}
    reports = list(folder.glob('*validation.txt')) or list(folder.glob('*diagnosis.txt'))
    lines = reports[0].read_text(encoding='utf-8-sig').splitlines() if reports else []
    results = list(folder.glob('*result.txt'))
    suites.append({
        'attempt': attempt, 'exitCode': info.get('exitCode'), 'timedOut': info.get('timedOut'),
        'passedAssertions': sum(line.startswith('PASS ') for line in lines),
        'failedAssertions': sum(line.startswith('FAIL ') for line in lines),
        'resultPassed': bool(results) and all(p.read_text(encoding='utf-8-sig').strip() == 'PASS' for p in results),
    })
checks = {
    'trackedChangesWhitelisted': set(changed) <= allowed,
    'noScenePrefabProfileMetadataOrProjectSettingsChanges': all(Path(p).suffix in {'.cs', '.compute', '.md'} for p in changed),
    'allDeliveredAssetFilesMatchTestedHarness': not mismatches,
    'allSelectedRunsCompletedSuccessfully': all(s['exitCode'] == 0 and s['timedOut'] is False and
        s['passedAssertions'] > 0 and s['failedAssertions'] == 0 and s['resultPassed'] for s in suites),
}
audit = {'passed': all(checks.values()), 'baseHead': head, 'checks': checks,
         'changedTrackedFiles': changed, 'harnessCheckedFiles': checked, 'harnessMismatches': mismatches,
         'folderMetadataNotCopiedByHarness': folder_metadata,
         'totalPassingAssertions': sum(s['passedAssertions'] for s in suites), 'suites': suites}
destination = root / 'FluidGpuExperimentEvidence/floating-contents-audit.json'
destination.write_text(json.dumps(audit, indent=2) + '\n', encoding='utf-8')
print(json.dumps(audit, indent=2))
raise SystemExit(0 if audit['passed'] else 1)
