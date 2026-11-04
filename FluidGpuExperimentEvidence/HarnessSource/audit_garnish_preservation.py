"""Compare this garnish change with the saved, already modified F-default source."""
from collections import Counter
from pathlib import Path
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
BEFORE = ROOT / 'Library/GarnishOriginal/FluidGpuExperiment'
EVIDENCE = ROOT / 'FluidGpuExperimentEvidence'
ALLOWED = {'README.md', 'DEVELOPMENT_PLAN.md', 'Scenes/FluidGpuComparison.unity',
           'Runtime/FluidExperimentBody.cs', 'Runtime/FluidExperimentBody.Contents.cs',
           'Runtime/FluidExperimentWorld.cs', 'Runtime/FluidExperimentInteractor.cs',
           'Runtime/FluidExperimentGpuLiquid.cs', 'Runtime/FluidExperimentGpuLiquid.ImprovedSurface.cs',
           'Runtime/FluidExperimentComparison.cs'}

def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest().upper() if p.is_file() else None

def scene_blocks(text):
    blocks = re.split(r'(?=^--- !u!)', text, flags=re.M)
    return {block.splitlines()[0]: block for block in blocks}

def main():
    manifest = json.loads((EVIDENCE / 'garnish-original-sha256.json').read_text(encoding='utf-8-sig'))
    snapshot_errors = [n for n, sha in manifest.items() if digest(BEFORE / n) != sha]
    changes = [n for n, sha in manifest.items() if digest(FEATURE / n) != sha]
    old = scene_blocks((BEFORE / 'Scenes/FluidGpuComparison.unity').read_text())
    new = scene_blocks((FEATURE / 'Scenes/FluidGpuComparison.unity').read_text())
    scene_losses = [key for key, block in old.items() if key not in new
                    or Counter(block.splitlines()) - Counter(new[key].splitlines())]
    shaders = [n for n in manifest if n.startswith('Shaders/')]
    protected = [n for n in manifest if n.startswith(('Data/', 'Prefabs/', 'Shaders/'))]
    pngs = []
    source = Path('C:/Users/boguk/Downloads/01_도구-20261009T090957Z-1-001/01_도구/1008_가니쉬')
    for png in sorted((FEATURE / 'Art/Garnishes').glob('*.png')):
        pngs.append({'name': png.name, 'sha256': digest(png), 'original_matches': digest(png) == digest(source / png.name)})
    runtime = (FEATURE / 'Runtime/FluidExperimentBody.cs').read_text()
    enums = re.search(r'enum LabItemKind\s*{([^}]+)}', runtime)[1].split(',')
    enum_ok = [e.strip() for e in enums] == ['Bottle','Glass','Jigger','Shaker','Spoon','IceBucket','Ice','Garnish']
    prefabs = [FEATURE / 'Prefabs' / (key + suffix) for key in ('synthLemonPeel','nanangaPeel')
               for suffix in ('_Garnish.prefab','_Source.prefab')]
    sprite_refs_ok = all('m_Sprite: {fileID: 0}' not in p.read_text() and 'm_Sprite: {fileID: 21300000' in p.read_text() for p in prefabs)
    report = dict(snapshot_count=len(manifest), snapshot_errors=snapshot_errors,
                  changed_original_files=changes, unexpected_changes=sorted(set(changes)-ALLOWED),
                  original_scene_field_losses=scene_losses, added_scene_object_count=len(new)-len(old),
                  protected_asset_count=len(protected), protected_assets_unchanged=all(n not in changes for n in protected),
                  shader_file_count=len(shaders), shaders_byte_identical=all(n not in changes for n in shaders),
                  existing_enum_indices_preserved=enum_ok, authored_default_f='initialMode: 5' in new[next(k for k in new if '&379435370' in k)],
                  original_pngs=pngs, prefab_sprite_references_non_null=sprite_refs_ok,
                  interpretation='Source/asset preservation only; runtime evidence is reported separately. Existing scene fields may be reordered and previously implicit defaults serialized by Unity.')
    report['passed'] = (not snapshot_errors and not report['unexpected_changes'] and not scene_losses
                        and report['protected_assets_unchanged'] and enum_ok and report['authored_default_f']
                        and len(pngs)==4 and all(p['original_matches'] for p in pngs) and sprite_refs_ok)
    path=EVIDENCE / 'garnish-preservation-audit.json'
    path.write_text(json.dumps(report, indent=2)+'\n')
    print(json.dumps(report, indent=2))
    return 0 if report['passed'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
