"""Verify rendering/pickup scope against the saved gentle-response implementation."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
BEFORE = ROOT / 'Library/GarnishMaskPickupOriginal/FluidGpuExperiment'
EVIDENCE = ROOT / 'FluidGpuExperimentEvidence'
ALLOWED = {'Runtime/FluidExperimentBody.cs', 'Runtime/FluidExperimentInteractor.cs',
           'Runtime/FluidExperimentWorld.cs', 'Runtime/FluidExperimentComparison.cs',
           'Runtime/FluidExperimentGpuLiquid.ImprovedSurface.cs', 'README.md', 'DEVELOPMENT_PLAN.md'}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper() if path.is_file() else None

def main():
    manifest = json.loads((EVIDENCE / 'garnish-mask-pickup-original-sha256.json').read_text(encoding='utf-8-sig'))
    snapshot_errors = [n for n, sha in manifest.items() if digest(BEFORE / n) != sha]
    changes = [n for n, sha in manifest.items() if digest(FEATURE / n) != sha]
    protected = [n for n in manifest if n.startswith(('Shaders/', 'Data/', 'Scenes/', 'Art/', 'Prefabs/'))]
    physical = [n for n in manifest if n.startswith('Runtime/FluidExperimentGpuLiquid') and 'ImprovedSurface' not in n]
    surface = (FEATURE / 'Runtime/FluidExperimentGpuLiquid.ImprovedSurface.cs').read_text()
    result = dict(snapshot_count=len(manifest), snapshot_errors=snapshot_errors, changed_files=changes,
                  unexpected_changes=sorted(set(changes)-ALLOWED), protected_count=len(protected),
                  protected_unchanged=all(n not in changes for n in protected),
                  physical_gpu_sources_unchanged=all(n not in changes for n in physical),
                  f_garnish_mask_only='if (useCohesivePhysics && body.kind == LabItemKind.Garnish) continue;' in surface)
    result['passed'] = not snapshot_errors and not result['unexpected_changes'] and result['protected_unchanged'] and result['physical_gpu_sources_unchanged'] and result['f_garnish_mask_only']
    (EVIDENCE / 'garnish-mask-pickup-preservation-audit.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result, indent=2))
    return 0 if result['passed'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
