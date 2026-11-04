"""Compare this change to its pre-edit snapshot, preserving unrelated pending work."""
from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parents[2]
relative = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
feature = root / relative
before = root / 'Library/GarnishLiquidMotionOriginal/FluidGpuExperiment'
harness = root / 'Library/FluidModelDHarness' / relative
allowed = {
    'Runtime/FluidExperimentBody.cs', 'Runtime/FluidExperimentBody.Contents.cs',
    'Runtime/FluidExperimentWorld.cs', 'Runtime/FluidExperimentGpuLiquid.cs',
    'Runtime/FluidExperimentGpuLiquid.Accounting.cs', 'README.md', 'DEVELOPMENT_PLAN.md',
    'Runtime/FluidExperimentInteractor.cs',
}
added = {'Runtime/FluidExperimentBody.GarnishMotion.cs', 'Runtime/FluidExperimentBody.GarnishMotion.cs.meta',
         'Runtime/FluidExperimentGpuLiquid.Garnish.cs', 'Runtime/FluidExperimentGpuLiquid.Garnish.cs.meta'}
def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()
baseline = {p.relative_to(before).as_posix(): digest(p) for p in before.rglob('*') if p.is_file()}
current = {p.relative_to(feature).as_posix(): digest(p) for p in feature.rglob('*') if p.is_file()}
changed = sorted(p for p in baseline if current.get(p) != baseline[p])
new = sorted(current.keys() - baseline.keys())
missing = sorted(baseline.keys() - current.keys())
# The disposable project generates its own directory GUIDs. Asset GUIDs and source
# bytes must still match; no serialized reference in this feature uses folder GUIDs.
directory_metas = {p for p in current if p.endswith('.meta') and (feature / p[:-5]).is_dir()}
parity = sorted(p for p in current if p not in directory_metas
                and (not (harness / p).is_file() or digest(harness / p) != current[p]))
harness_sources = root / 'Library/FluidModelDHarness/Assets/Editor'
test_parity = sorted(p.name for p in (root / 'FluidGpuExperimentEvidence/HarnessSource').glob('*.cs')
                     if not (harness_sources / p.name).is_file() or digest(harness_sources / p.name) != digest(p))
protected = [p for p in baseline if p.startswith(('Shaders/', 'Art/', 'Data/', 'Prefabs/', 'Scenes/'))]
status = not missing and set(changed) <= allowed and set(new) == added and not parity and not test_parity
report = dict(passed=status, baselineFileCount=len(baseline), changed=changed, added=new,
              missing=missing, harnessMismatches=parity, protectedAssetCount=len(protected),
              harnessTestMismatches=test_parity,
              disposableDirectoryMetasExcluded=sorted(directory_metas),
              protectedAssetsUnchanged=all(current.get(p) == baseline[p] for p in protected),
              baselineSha256=baseline,
              scope='Feature-only comparison against pre-edit working copy; existing unrelated Git changes are not modified.')
out = root / 'FluidGpuExperimentEvidence/garnish-liquid-motion-preservation-audit.json'
out.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in report.items() if k != 'baselineSha256'}, indent=2))
raise SystemExit(0 if status and report['protectedAssetsUnchanged'] else 1)
