import hashlib
import json
from pathlib import Path
import sys
import re

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
paths = list(FEATURE.rglob('*'))
for name in ('Bartending/Runtime', 'Business/Runtime'):
    paths += list((ROOT / 'Assets/_Project/Features' / name).rglob('*'))
paths += list((ROOT / 'Assets/Resources/Bartending').rglob('*'))
current = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths if p.is_file()}
baseline = HERE / 'before-sha256.json'
if sys.argv[1] == 'baseline':
    if baseline.exists():
        raise SystemExit('Refusing to replace baseline')
    baseline.write_text(json.dumps(current, indent=2), encoding='utf-8')
    print('Recorded', len(current), 'original file hashes')
else:
    before = json.loads(baseline.read_text(encoding='utf-8'))
    changed = [p for p, digest in before.items() if current.get(p) != digest]
    allowed = {str(FEATURE.relative_to(ROOT).as_posix()) + '/' + name for name in (
        'Runtime/FluidExperimentWorld.cs', 'Runtime/FluidExperimentInteractor.Pointer.cs', 'Runtime/FluidExperimentComparison.cs',
        'Prefabs/Runtime/FluidExperimentRuntime.prefab', 'Scenes/FluidGpuComparison.unity',
        'Editor/FluidExperimentAssemblyBuilder.cs', 'Editor/FluidExperimentPrefabAudit.cs', 'README.md')}
    unexpected = [p for p in changed if p not in allowed]
    semantic = {}
    for name, identity in (('Scenes/FluidGpuComparison.unity', '379435371'),
                           ('Prefabs/Runtime/FluidExperimentRuntime.prefab', '4452693018939984929')):
        relative = FEATURE.relative_to(ROOT).as_posix() + '/' + name
        raw = (ROOT / relative).read_bytes().replace(b'\r\n', b'\n')
        raw = re.sub(rb'^  - component: \{fileID: ' + identity.encode() + rb'\}\n', b'', raw, flags=re.M)
        raw = re.sub(rb'^--- !u!114 &' + identity.encode() + rb'\n.*?(?=^--- !u!|\Z)', b'', raw, flags=re.M | re.S)
        semantic[name] = any(hashlib.sha256(value).hexdigest() == before[relative] for value in (raw, raw.replace(b'\n', b'\r\n')))
        if not semantic[name]: unexpected.append('Existing scene/runtime semantics changed: ' + name)
    report = {'original_files': len(before), 'unchanged': len(before) - len(changed), 'changed': changed,
              'unexpected': unexpected, 'scene_and_runtime_only_add_session': semantic,
              'added': [p for p in current if p not in before]}
    (HERE / 'preservation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    raise SystemExit(bool(unexpected))
