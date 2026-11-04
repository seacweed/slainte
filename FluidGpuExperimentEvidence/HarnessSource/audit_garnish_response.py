"""Preserve all unrelated feature state while tuning F garnish motion response."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
BEFORE = ROOT / 'Library/GarnishResponseOriginal/FluidGpuExperiment'
EVIDENCE = ROOT / 'FluidGpuExperimentEvidence'
ALLOWED = {'Runtime/FluidExperimentBody.cs', 'Runtime/FluidExperimentBody.Contents.cs',
           'Runtime/FluidExperimentGpuLiquid.cs', 'Editor/FluidExperimentGarnishBuilder.cs',
           'Prefabs/synthLemonPeel_Garnish.prefab', 'Prefabs/nanangaPeel_Garnish.prefab',
           'README.md', 'DEVELOPMENT_PLAN.md'}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper() if path.is_file() else None

def main():
    manifest = json.loads((EVIDENCE / 'garnish-response-original-sha256.json').read_text(encoding='utf-8-sig'))
    snapshot_errors = [n for n, sha in manifest.items() if digest(BEFORE / n) != sha]
    changes = [n for n, sha in manifest.items() if digest(FEATURE / n) != sha]
    unexpected = sorted(set(changes) - ALLOWED)
    immutable = [n for n in manifest if n.startswith(('Shaders/', 'Data/', 'Scenes/', 'Art/'))]
    prefab_errors = []
    for key in ('synthLemonPeel', 'nanangaPeel'):
        name = f'Prefabs/{key}_Garnish.prefab'
        # Only the coefficient and peel damping change; mass, gravity,
        # colliders, rendering and sprite references remain identical.
        current = (FEATURE / name).read_text()
        original = (BEFORE / name).read_text()
        restored = current.replace('  garnishLiquidMotionTransfer: 0.2\n', '')
        restored = restored.replace('  m_LinearDamping: 4\n', '  m_LinearDamping: 0.05\n')
        restored = restored.replace('  m_AngularDamping: 4\n', '  m_AngularDamping: 0.1\n')
        if restored != original:
            prefab_errors.append(name)
    gpu = (FEATURE / 'Runtime/FluidExperimentGpuLiquid.cs').read_text()
    gated = 'if (useCohesivePhysics && item.kind == LabItemKind.Garnish && item.garnishLiquidMotionTransfer < 1f)' in gpu
    result = dict(snapshot_count=len(manifest), snapshot_errors=snapshot_errors,
                  changed_files=changes, unexpected_changes=unexpected,
                  protected_count=len(immutable), protected_unchanged=all(n not in changes for n in immutable),
                  garnish_prefab_non_response_changes=prefab_errors, f_garnish_only_gate=gated)
    result['passed'] = not snapshot_errors and not unexpected and not prefab_errors and result['protected_unchanged'] and gated
    (EVIDENCE / 'garnish-response-preservation-audit.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result, indent=2))
    return 0 if result['passed'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
