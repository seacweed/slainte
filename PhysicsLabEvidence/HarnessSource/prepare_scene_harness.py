"""Copy the authored PhysicsLab scene and its asset dependencies for an independent Unity run.
Never starts, stops, or writes to the user's running Unity project settings.
"""
from pathlib import Path
import hashlib
import json
import re
import shutil

root = Path(__file__).resolve().parents[2]
target = root / 'PhysicsLabEvidence/SceneHarnessProject'
lab = Path('Assets/_Project/Features/Bartending/PhysicsLab')
target.mkdir(parents=True, exist_ok=True)
guid_index = {}
for meta in (root / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([a-f0-9]{32})', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
    if match:
        guid_index[match[1]] = Path(str(meta)[:-5]).relative_to(root)
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
    if source.suffix.lower() in {'.asset', '.prefab', '.unity', '.mat', '.controller'}:
        text = source.read_text(encoding='utf-8-sig', errors='replace')
        for guid in re.findall(r'guid: ([a-f0-9]{32})', text):
            if guid in guid_index:
                copy_asset(guid_index[guid])

for directory in ('Runtime', 'Shaders', 'Prefabs', 'Scenes', 'Data'):
    for source in (root / lab / directory).rglob('*'):
        if source.is_file() and source.suffix != '.meta':
            copy_asset(source.relative_to(root))
copy_asset(Path('Assets/_Project/Features/Bartending/Runtime/Interaction/ItemDef.cs'))
copy_asset(Path('Assets/_Project/Features/Bartending/Runtime/BartendingPointerAnchor.cs'))
copy_asset(Path('Assets/_Project/Features/Bartending/Runtime/BartendingViewport.cs'))
(target / 'Packages').mkdir(exist_ok=True)
manifest = {'dependencies': {'com.unity.ugui':'2.0.0', 'com.unity.render-pipelines.universal':'17.3.0', 'com.unity.inputsystem':'1.17.0',
    **{'com.unity.modules.'+name:'1.0.0' for name in ('physics2d','physics','imgui','ui','imageconversion','jsonserialize','animation')}}}
(target / 'Packages/manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
(target / 'ProjectSettings').mkdir(exist_ok=True)
for name in ('ProjectVersion.txt','GraphicsSettings.asset','QualitySettings.asset','TimeManager.asset','TagManager.asset','Physics2DSettings.asset'):
    source=root/'ProjectSettings'/name
    shutil.copy2(source,target/'ProjectSettings'/name)
    for guid in re.findall(r'guid: ([a-f0-9]{32})',source.read_text(encoding='utf-8-sig',errors='replace')):
        if guid in guid_index:copy_asset(guid_index[guid])
editor = target / lab / 'Editor'
editor.mkdir(exist_ok=True)
source = (root / lab / 'Editor/PhysicsLabValidator.cs').read_text(encoding='utf-8-sig')
lines=[]
for line in source.splitlines():
    if 'Require(GpuLiquidSystem.Instance' in line or 'Require(FindObjectsByType<SlotController>' in line:
        lines.append('            results.Add("SKIP: Legacy-system presence is not tested in the isolated scene-copy project.");')
    else:lines.append(line)
(editor/'PhysicsLabValidator.cs').write_text('\n'.join(lines)+'\n',encoding='utf-8')
for partial in (root / lab / 'Editor').glob('PhysicsLabValidator.*.cs'):
    shutil.copy2(partial, editor / partial.name)
(editor/'PhysicsLabBuilder.cs').write_text('''namespace Slainte.Bartending.PhysicsLab.Editor {
public static class PhysicsLabBuilder {
public const string Root = "Assets/_Project/Features/Bartending/PhysicsLab";
public const string ScenePath = Root + "/Scenes/BartendingPhysicsSandbox.unity";
public static void Build() { throw new System.InvalidOperationException("Harness uses the copied scene, never regenerates it."); }
}}
''',encoding='utf-8')
inventory={str(p).replace('\\','/'):hashlib.sha256((target/p).read_bytes()).hexdigest() for p in sorted(copied)}
(target/'source-sha256.json').write_text(json.dumps(inventory,indent=2),encoding='utf-8')
print(f'Prepared authored scene with {len(copied)} assets at {target}')
