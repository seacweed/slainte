"""Copy only experiment assets and read-only dependencies to an isolated Unity project."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import uuid

root = Path(__file__).resolve().parents[2]
feature = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
target = root / 'FluidGpuExperimentEvidence/HarnessProject'
target.mkdir(parents=True, exist_ok=True)

# Author new metadata only under the new feature. Never refresh the original project.
for path in [root / feature, *(root / feature).rglob('*')]:
    if path.suffix == '.meta':
        continue
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n'
                        + ('folderAsset: yes\n' if path.is_dir() else ''), encoding='utf-8')

index = {}
for meta in (root / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([a-f0-9]{32})', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
    if match:
        index[match[1]] = Path(str(meta)[:-5]).relative_to(root)
copied = set()

def copy_asset(relative):
    if relative in copied:
        return
    source = root / relative
    if not source.is_file():
        return
    copied.add(relative)
    destination = target / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    meta = Path(str(source) + '.meta')
    if meta.exists():
        shutil.copy2(meta, str(destination) + '.meta')
    if source.suffix in {'.unity', '.prefab', '.asset', '.mat'}:
        for guid in re.findall(r'guid: ([a-f0-9]{32})', source.read_text(encoding='utf-8-sig', errors='replace')):
            if guid in index:
                copy_asset(index[guid])

for path in (root / feature).rglob('*'):
    if path.is_file() and path.suffix != '.meta':
        copy_asset(path.relative_to(root))
for path in ('Interaction/ItemDef.cs', 'BartendingPointerAnchor.cs', 'BartendingViewport.cs'):
    copy_asset(Path('Assets/_Project/Features/Bartending/Runtime') / path)
for folder in ('Packages', 'ProjectSettings', 'UserSettings', 'Assets/Editor'):
    (target / folder).mkdir(parents=True, exist_ok=True)
dependencies = {'com.unity.ugui': '2.0.0', 'com.unity.render-pipelines.universal': '17.3.0', 'com.unity.inputsystem': '1.17.0'}
dependencies.update({'com.unity.modules.' + name: '1.0.0' for name in
    ('physics2d', 'physics', 'imgui', 'ui', 'imageconversion', 'jsonserialize', 'animation')})
(target / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}, indent=2))
(target / 'UserSettings/Search.settings').write_text('{"indexOnEditorStartup": false}\n')
for name in ('ProjectVersion.txt', 'ProjectSettings.asset', 'GraphicsSettings.asset', 'QualitySettings.asset', 'TimeManager.asset', 'TagManager.asset', 'Physics2DSettings.asset'):
    source = root / 'ProjectSettings' / name
    shutil.copy2(source, target / 'ProjectSettings' / name)
    for guid in re.findall(r'guid: ([a-f0-9]{32})', source.read_text(encoding='utf-8-sig', errors='replace')):
        if guid in index:
            copy_asset(index[guid])
for path in (root / 'FluidGpuExperimentEvidence/HarnessSource').glob('*.cs'):
    shutil.copy2(path, target / 'Assets/Editor' / path.name)
(target / 'source-sha256.json').write_text(json.dumps({str(p).replace('\\', '/'):
    hashlib.sha256((root / p).read_bytes()).hexdigest() for p in sorted(copied)}, indent=2))
print(f'Prepared {len(copied)} experiment/dependency assets at {target}')
