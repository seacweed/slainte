import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
paths = list(FEATURE.rglob('*'))
for relative in ('Assets/_Project/Features/Bartending/Runtime', 'Assets/_Project/Features/Business/Runtime',
                 'Assets/Resources/Bartending', 'ProjectSettings'):
    paths += list((ROOT / relative).rglob('*'))
current = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths if p.is_file()}
before = HERE / 'before-sha256.json'
if sys.argv[1] == 'baseline':
    if before.exists(): raise SystemExit('Refusing to replace baseline')
    before.write_text(json.dumps(current, indent=2), encoding='utf-8')
    print('Baseline files:', len(current))
else:
    original = json.loads(before.read_text(encoding='utf-8'))
    changed = [p for p, value in original.items() if current.get(p) != value]
    prefix = FEATURE.relative_to(ROOT).as_posix() + '/'
    allowed = {prefix + p for p in (
        'Runtime/FluidExperimentWorld.cs', 'Runtime/FluidExperimentBody.GarnishMotion.cs',
        'Runtime/FluidExperimentGpuLiquid.Rendering.cs', 'Runtime/FluidExperimentGpuLiquid.cs',
        'Runtime/FluidExperimentInteractor.cs', 'Shaders/FluidExperimentImprovedAccumulation.shader', 'README.md')}
    unexpected = [p for p in changed if p not in allowed]
    report = {'original_files': len(original), 'unchanged': len(original) - len(changed), 'changed': changed,
              'unexpected': unexpected, 'added': [p for p in current if p not in original]}
    (HERE / 'preservation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    raise SystemExit(bool(unexpected))
