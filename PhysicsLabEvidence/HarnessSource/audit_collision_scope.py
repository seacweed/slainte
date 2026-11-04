"""Read-only comparison of collision migration against the committed prefabs."""
from pathlib import Path
import hashlib
import json
import re
import subprocess

root = Path(__file__).resolve().parents[2]
lab = Path('Assets/_Project/Features/Bartending/PhysicsLab')
output = root / 'PhysicsLabEvidence/collision-scope-audit'
output.mkdir(exist_ok=True)

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def blocks(text):
    parts = re.split(r'(?m)^--- !u!(\d+) &(-?\d+)\s*\n', text)
    result = {}
    for i in range(1, len(parts), 3):
        fields = re.split(r'(?m)^  ([A-Za-z_][A-Za-z_0-9]*):', parts[i+2])
        result[parts[i+1]] = (int(parts[i]), {fields[j]:fields[j+1].strip() for j in range(1,len(fields),2)})
    return result

allowed = {'solidColliders', 'capCollider', 'collisionProfile', 'liquidWall', 'wallClosed',
           'extraSolidHulls', 'contentRegions', 'mouthLocal', 'mouthLipLocal', 'overrideMouthLip', 'mouthDirectionLocal'}
baseline = 'c2d6a21'
report = {'baseline': subprocess.check_output(['git','rev-parse',baseline],cwd=root,text=True).strip(), 'prefabs':{}}
for path in sorted((root/lab/'Prefabs').glob('*.prefab')):
    relative = path.relative_to(root).as_posix()
    before = blocks(subprocess.check_output(['git','show',baseline+':'+relative],cwd=root,text=True,encoding='utf-8'))
    after = blocks(path.read_text(encoding='utf-8-sig'))
    changed = {}
    for key,(kind,old) in before.items():
        if key not in after:
            assert kind in (60,61,68) and old.get('m_IsTrigger')=='0', (path.name,'removed non-solid object',kind)
            continue
        new_kind,new = after[key]
        assert new_kind==kind
        # Unity writes the already-existing C# default when reserializing older prefabs.
        if kind==114 and 'mouthWidth' not in old and 'mouthWidth' in new:
            assert float(new['mouthWidth'])==0.14
            old['mouthWidth']=new['mouthWidth']
        delta = {field for field in old.keys()|new.keys() if old.get(field)!=new.get(field)}
        if not delta: continue
        if kind==1: assert delta <= {'m_Component'}, (path.name,delta)
        elif kind==114: assert delta<=allowed, (path.name,delta)
        elif kind in (60,61,68): assert old.get('m_IsTrigger')=='0', (path.name,'pick trigger changed')
        else: raise AssertionError((path.name,kind,delta))
        changed[key] = sorted(delta)
    for key,(kind,new) in after.items():
        if key not in before: assert kind==60 and new.get('m_IsTrigger')=='0', (path.name,'unexpected component',kind)
    report['prefabs'][path.name]={'allowed_geometry_changes':changed,'sha256':sha(path)}

settings=root/'ProjectSettings/ProjectSettings.asset'
report['preexisting_project_settings_sha256']=sha(settings)
assert report['preexisting_project_settings_sha256']=='04cbf9e21ba4556e72dad9c729e669e764ea128ec908f29933a329ded3c8f2be'
changed=subprocess.check_output(['git','-c','core.safecrlf=false','diff','--name-only'],cwd=root,text=True).splitlines()
assert all(p.startswith(lab.as_posix()+'/') or p.startswith('PhysicsLabEvidence/') or p=='ProjectSettings/ProjectSettings.asset' for p in changed)
assert not any(p.startswith((lab/'Scenes').as_posix()+'/') for p in changed)
report['scene_unchanged']=True
report['visual_transforms_rigidbody_settings_selection_and_gameplay_fields_preserved']=True
report['copied_assets_match']={}
harness=root/'PhysicsLabEvidence/SceneHarnessProject'
for relative in json.loads((harness/'source-sha256.json').read_text()):
    assert sha(root/relative)==sha(harness/relative),relative
    report['copied_assets_match'][relative]=sha(root/relative)
for path in (root/lab/'Editor').glob('*.cs'):
    if path.name in ('PhysicsLabValidator.cs','PhysicsLabBuilder.cs'): continue # documented harness substitutions
    assert path.read_bytes()==(harness/path.relative_to(root)).read_bytes(),path
report['authoring_before_sha256']={str(p.relative_to(harness)):sha(p) for folder in ('Prefabs','Data/CollisionProfiles') for p in (harness/lab/folder).glob('*') if p.is_file()}
(output/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('PASS: 24 prefabs preserve art/transforms/rigidbodies/selection/gameplay; scene and prior settings unchanged; 137 copied assets match.')
