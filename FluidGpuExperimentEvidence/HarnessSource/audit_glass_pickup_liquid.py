"""Verify pickup-only scope against the pre-edit working copy and the executed harness."""
from pathlib import Path
import hashlib
import json
import re

root = Path(__file__).resolve().parents[2]
relative = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
feature = root / relative
before = root / 'Library/GlassPickupLiquidOriginal/FluidGpuExperiment'
harness = root / 'Library/FluidModelDHarness' / relative
allowed = {
    'Runtime/FluidExperimentBody.cs', 'Runtime/FluidExperimentInteractor.cs',
    'Runtime/FluidExperimentGpuLiquid.cs', 'Runtime/FluidExperimentGpuLiquid.Buffers.cs',
    'Runtime/FluidExperimentGpuLiquid.ImprovedSurface.cs',
    'Shaders/FluidExperimentLiquid.compute', 'Shaders/FluidExperimentImprovedSurface.compute',
    'README.md', 'DEVELOPMENT_PLAN.md',
}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

baseline = {p.relative_to(before).as_posix(): digest(p) for p in before.rglob('*') if p.is_file()}
current = {p.relative_to(feature).as_posix(): digest(p) for p in feature.rglob('*') if p.is_file()}
changed = sorted(p for p in baseline if current.get(p) != baseline[p])
added = sorted(current.keys() - baseline.keys())
missing = sorted(baseline.keys() - current.keys())
directory_metas = {p for p in current if p.endswith('.meta') and (feature / p[:-5]).is_dir()}
parity = sorted(p for p in current if p not in directory_metas
                and (not (harness / p).is_file() or digest(harness / p) != current[p]))
harness_sources = root / 'Library/FluidModelDHarness/Assets/Editor'
test_parity = sorted(p.name for p in (root / 'FluidGpuExperimentEvidence/HarnessSource').glob('*.cs')
                    if not (harness_sources / p.name).is_file() or digest(harness_sources / p.name) != digest(p))
protected = [p for p in baseline if p.startswith(('Shaders/', 'Art/', 'Data/', 'Prefabs/', 'Scenes/')) and p not in allowed]
protected_ok = all(current.get(p) == baseline[p] for p in protected)

# The new compute entry point is separate: every previous physics kernel, including A-E,
# must remain identical to the working copy saved immediately before this request.
shader_path = 'Shaders/FluidExperimentLiquid.compute'
shader = (feature / shader_path).read_text(encoding='utf-8-sig')
without_new_kernel = shader.replace('#pragma kernel CarryPickupContents\n', '').replace(
    'float2 _PickupFrom, _PickupTo, _PickupRotation;\n', '')
without_new_kernel = re.sub(r'\[numthreads\(THREAD_GROUP_SIZE, 1, 1\)\]\nvoid CarryPickupContents\([^}]+}\n\n', '', without_new_kernel)
old_kernels_unchanged = without_new_kernel == (before / shader_path).read_text(encoding='utf-8-sig')
body = (feature / 'Runtime/FluidExperimentBody.cs').read_text(encoding='utf-8-sig')
interactor = (feature / 'Runtime/FluidExperimentInteractor.cs').read_text(encoding='utf-8-sig')
gates = ('kind != LabItemKind.Glass' in body and '!World.Liquid.CohesivePhysicsActive' in body
         and 'ice.UsesLiquidGarnishMotion' in body and interactor.count('.RestorePickupPose(') == 1
         and interactor.count('.RestoreHeldPose(') == 2)
status = (bool(baseline) and not missing and not added and set(changed) <= allowed
          and not parity and not test_parity and protected_ok and old_kernels_unchanged and gates)
report = dict(passed=status, baselineFileCount=len(baseline), changed=changed, added=added,
              missing=missing, harnessMismatches=parity, harnessTestMismatches=test_parity,
              protectedAssetCount=len(protected), protectedAssetsUnchanged=protected_ok,
              existingPhysicsKernelsUnchanged=old_kernels_unchanged, pickupOnlyFGates=gates,
              baselineSha256=baseline, currentSha256=current,
              scope='Glass pickup in F only; compare against pre-edit working copy, not Git HEAD.')
out = root / 'FluidGpuExperimentEvidence/glass-pickup-liquid-preservation-audit.json'
out.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in report.items() if k not in ('baselineSha256', 'currentSha256')}, indent=2))
raise SystemExit(0 if status else 1)
