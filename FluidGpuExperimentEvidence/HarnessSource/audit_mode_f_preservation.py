"""Audit preserved A-E sources and the user-authorized promotion of F to default."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
EVIDENCE = ROOT / 'FluidGpuExperimentEvidence'
ALLOWED = {
    'Runtime/FluidExperimentComparison.cs',
    'Runtime/FluidExperimentGpuLiquid.Reference.cs',
    'Runtime/FluidExperimentGpuLiquid.Buffers.cs',
    'Runtime/FluidExperimentLiquidSettings.cs',
    'Editor/FluidExperimentBuilder.cs',
    'Scenes/FluidGpuComparison.unity',
    'Shaders/FluidExperimentLiquid.compute',
    'README.md',
    'DEVELOPMENT_PLAN.md',
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main():
    original = json.loads((EVIDENCE / 'mode-f-original-sha256.json').read_text(encoding='utf-8-sig'))
    changed = sorted(name for name, value in original.items()
                     if not (FEATURE / name).is_file() or sha(FEATURE / name) != value)
    unexpected = [name for name in changed if name not in ALLOWED]
    copy = ROOT / 'Library/FluidModeFOriginal/FluidGpuExperiment'
    snapshot_errors = [name for name, value in original.items()
                       if not (copy / name).is_file() or sha(copy / name) != value]
    # Existing compute kernel bodies must be identical; F only registers and includes new kernels.
    def legacy_compute(path):
        return '\n'.join(line for line in path.read_text(encoding='utf-8-sig').splitlines()
                         if 'Cohesive' not in line and 'Coherent' not in line)
    kernels_unchanged = legacy_compute(copy / 'Shaders/FluidExperimentLiquid.compute') == legacy_compute(
        FEATURE / 'Shaders/FluidExperimentLiquid.compute')
    comparison = (FEATURE / 'Runtime/FluidExperimentComparison.cs').read_text(encoding='utf-8-sig')
    builder = (FEATURE / 'Editor/FluidExperimentBuilder.cs').read_text(encoding='utf-8-sig')
    scene = (FEATURE / 'Scenes/FluidGpuComparison.unity').read_text(encoding='utf-8-sig')
    original_scene = (copy / 'Scenes/FluidGpuComparison.unity').read_text(encoding='utf-8-sig')
    default_f = 'initialMode = FluidExperimentMode.FCoherentLiquid;' in comparison
    builder_f = 'comparison.initialMode = FluidExperimentMode.FCoherentLiquid;' in builder
    scene_f = '  initialMode: 5\n' in scene
    only_scene_default_changed = scene.replace('  initialMode: 5\n', '  initialMode: 3\n') == original_scene
    enum_order = 'ACurrent, BReferencePhysics, CReferenceSurface, DImprovedSurface, ECalibratedLiquid, FCoherentLiquid' in comparison
    passed = (not unexpected and not snapshot_errors and kernels_unchanged and default_f
              and builder_f and scene_f and only_scene_default_changed and enum_order)
    report = {
        'passed': passed,
        'audited_at_utc': datetime.now(timezone.utc).isoformat(),
        'basis': 'Exact pre-F working-tree snapshot, including the uncommitted D interaction fixes; not HEAD.',
        'original_file_count': len(original),
        'unchanged_original_file_count': len(original) - len(changed),
        'changed_original_files': changed,
        'unexpected_changes': unexpected,
        'snapshot_errors': snapshot_errors,
        'original_compute_kernel_bodies_unchanged': kernels_unchanged,
        'authorized_default_mode': 'FCoherentLiquid',
        'runtime_default_f': default_f,
        'builder_default_f': builder_f,
        'scene_default_f': scene_f,
        'only_scene_initial_mode_changed': only_scene_default_changed,
        'original_serialized_mode_indices_preserved': enum_order,
        'snapshot_directory': str(copy),
        'limits': 'Static scope/source preservation only. Actual A-E GPU probes are compared in the mode-F runtime evidence.'
    }
    output = EVIDENCE / 'mode-f-default-preservation-audit.json'
    output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
