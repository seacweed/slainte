"""Audit the held garnish pass-through change against its pre-edit working-copy snapshot."""
from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parents[2]
relative = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
feature = root / relative
before = root / 'Library/GarnishHeldPassThroughOriginal/FluidGpuExperiment'
harness = root / 'Library/FluidModelDHarness' / relative
allowed = {
    'Runtime/FluidExperimentBody.GarnishMotion.cs',
    'Runtime/FluidExperimentBody.cs', 'Runtime/FluidExperimentInteractor.cs',
    'Runtime/FluidExperimentWorld.cs', 'README.md', 'DEVELOPMENT_PLAN.md',
}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

baseline = {p.relative_to(before).as_posix(): digest(p) for p in before.rglob('*') if p.is_file()}
current = {p.relative_to(feature).as_posix(): digest(p) for p in feature.rglob('*') if p.is_file()}
changed = sorted(p for p in baseline if current.get(p) != baseline[p])
added = sorted(current.keys() - baseline.keys())
missing = sorted(baseline.keys() - current.keys())
# The disposable project owns directory GUIDs; all actual asset GUIDs are checked.
directory_metas = {p for p in current if p.endswith('.meta') and (feature / p[:-5]).is_dir()}
parity = sorted(p for p in current if p not in directory_metas
                and (not (harness / p).is_file() or digest(harness / p) != current[p]))
harness_sources = root / 'Library/FluidModelDHarness/Assets/Editor'
test_parity = sorted(p.name for p in (root / 'FluidGpuExperimentEvidence/HarnessSource').glob('*.cs')
                    if not (harness_sources / p.name).is_file() or digest(harness_sources / p.name) != digest(p))
protected = [p for p in baseline if p.startswith(('Shaders/', 'Art/', 'Data/', 'Prefabs/', 'Scenes/'))]
protected_ok = all(current.get(p) == baseline[p] for p in protected)
status = (bool(baseline) and not missing and not added and set(changed) <= allowed
          and not parity and not test_parity and protected_ok)
report = dict(passed=status, baselineFileCount=len(baseline), changed=changed, added=added,
              missing=missing, harnessMismatches=parity, harnessTestMismatches=test_parity,
              protectedAssetCount=len(protected), protectedAssetsUnchanged=protected_ok,
              disposableDirectoryMetasExcluded=sorted(directory_metas),
              baselineSha256=baseline, currentSha256=current,
              scope='Feature-only comparison against the pre-held-pass-through working copy, preserving existing pending work.')
out = root / 'FluidGpuExperimentEvidence/garnish-held-pass-through-preservation-audit.json'
out.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in report.items() if k not in ('baselineSha256', 'currentSha256')}, indent=2))
raise SystemExit(0 if status else 1)
