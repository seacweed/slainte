import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
TARGET = ROOT / 'Library/FluidModelDHarness'
before = json.loads((HERE / 'before-sha256.json').read_text(encoding='utf-8'))
changed = [p for p, h in before.items() if hashlib.sha256((ROOT / p).read_bytes()).hexdigest() != h]
allowed = {'Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Spoon.prefab',
           'Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentWorld.Rendering.cs'}
summary = {'preserved_original_files': len(before)-len(changed), 'original_files': len(before),
           'changed': changed, 'unexpected_changes': sorted(set(changed)-allowed), 'suites': [], 'source_mismatches': []}
for name, count in [('01-orientation',46), ('02-stir-cocktails',61), ('03-prefabs',7)]:
    folder = HERE / name
    status = json.loads((folder / 'exit.json').read_text(encoding='utf-8-sig'))
    results = list(folder.glob('*result.txt'))
    checks = sum(sum(s.startswith('PASS ') for s in p.read_text(encoding='utf-8-sig').splitlines())
                 for p in folder.glob('*validation.txt'))
    ok = status['exitCode'] == 0 and not status['timedOut'] and checks == count and bool(results)
    ok &= all(p.read_text().strip() == 'PASS' for p in results)
    summary['suites'].append({'attempt': name, 'result': 'PASS' if ok else 'FAIL', 'checks': checks})
    source = json.loads((folder / 'source-sha256.json').read_text(encoding='utf-8-sig'))
    for relative, expected in source.items():
        for label, base in [('workspace', ROOT), ('harness', TARGET)]:
            if hashlib.sha256((base / relative).read_bytes()).hexdigest() != expected:
                summary['source_mismatches'].append(name + ':' + label + ':' + relative)
summary['passing_checks'] = sum(p['checks'] for p in summary['suites'] if p['result'] == 'PASS')
summary['result'] = 'PASS' if not summary['unexpected_changes'] and not summary['source_mismatches'] and all(
    s['result'] == 'PASS' for s in summary['suites']) else 'FAIL'
(HERE / 'validation-summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps(summary, indent=2))
raise SystemExit(summary['result'] != 'PASS')
