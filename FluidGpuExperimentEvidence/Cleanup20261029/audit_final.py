"""Audit cleanup against the exact pre-edit workspace rather than against Git HEAD."""
from pathlib import Path
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
PREFIX = 'Assets/_Project/Features/Bartending/FluidGpuExperiment/'
feature = ROOT / PREFIX
before = json.loads((HERE / 'before-manifest.json').read_text(encoding='utf-8'))
protected = json.loads((HERE / 'protected-before-sha256.json').read_text(encoding='utf-8'))
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
protected_changed = [name for name, value in protected.items() if not (ROOT / name).is_file() or digest(ROOT / name) != value]
original = {name: value for name,value in before.items() if name.startswith(PREFIX)}
current = {path.relative_to(ROOT).as_posix(): {'sha256': digest(path), 'bytes':path.stat().st_size}
           for path in feature.rglob('*') if path.is_file()}
removed = sorted(set(original)-set(current))
added = sorted(set(current)-set(original))
changed = sorted(name for name in original.keys() & current.keys() if original[name]['sha256'] != current[name]['sha256'])
meta_changed = [name for name in original.keys() & current.keys() if name.endswith('.meta') and original[name]['sha256'] != current[name]['sha256']]
old_prefabs = sorted(name for name in original if name.endswith('.prefab'))
new_prefabs = sorted(name for name in current if name.endswith('.prefab'))
removed_identifiers = r'\b(?:FluidExperimentMode|FluidExperimentScenario|FluidExperimentSolver|useImprovedPhysics|useCohesivePhysics|useReferenceSurface|useImprovedSurface|garnishLiquidMotionTransfer|FluidExperimentEffects)\b'
stale = []
for path in feature.rglob('*'):
    if path.suffix in ('.cs', '.prefab', '.asset', '.unity'):
        if re.search(removed_identifiers, path.read_text(encoding='utf-8-sig')):
            stale.append(path.relative_to(ROOT).as_posix())
results = json.loads((HERE / '03-suite-results.json').read_text(encoding='utf-8'))
for result in results:
    if result['name'] in ('authoring','runtime'):
        attempt = HERE / ('04-' + result['name'])
        exit_result = json.loads((attempt / 'exit.json').read_text(encoding='utf-8-sig'))
        result = dict(result, attempt=attempt.relative_to(ROOT / 'FluidGpuExperimentEvidence').as_posix(),
                      exit_code=exit_result['exitCode'])
    if result['name'] == 'prefabs' and (HERE / '05-final-prefabs/exit.json').exists():
        attempt = HERE / '05-final-prefabs'
        exit_result = json.loads((attempt / 'exit.json').read_text(encoding='utf-8-sig'))
        result = dict(result, attempt=attempt.relative_to(ROOT / 'FluidGpuExperimentEvidence').as_posix(),
                      exit_code=exit_result['exitCode'])
    attempt = ROOT / 'FluidGpuExperimentEvidence' / result['attempt']
    result['pass_checks'] = sum(sum(line.startswith('PASS ') for line in path.read_text(encoding='utf-8-sig').splitlines())
                                for path in attempt.glob('*validation.txt'))
    results[results.index(next(x for x in results if x['name']==result['name']))] = result
current_tests = {path.name:digest(path) for path in (HERE / 'Validation').glob('*.cs')}
harness_manifest = json.loads((ROOT / 'Library/FluidModelDHarness/source-sha256.json').read_text(encoding='utf-8'))
harness_mismatches = [name for name,value in current.items() if harness_manifest.get(name) != value['sha256']]
final = {
    'protected_files':len(protected), 'protected_changed':protected_changed,
    'feature_files_before':len(original), 'feature_files_after':len(current),
    'feature_nonmeta_before':sum(not n.endswith('.meta') for n in original),
    'feature_nonmeta_after':sum(not n.endswith('.meta') for n in current),
    'preserved_metadata':sum(n.endswith('.meta') for n in original.keys() & current.keys()),
    'changed_metadata':meta_changed, 'prefabs_before':len(old_prefabs),'prefabs_after':len(new_prefabs),
    'prefab_paths_preserved':old_prefabs==new_prefabs, 'stale_removed_identifiers':stale,
    'final_harness_source_mismatches':harness_mismatches,
    'removed':removed,'added':added,'changed':changed,'suites':results,
    'passed_checks':sum(x['pass_checks'] for x in results),
    'all_current_suites_passed':all(x['exit_code']==0 for x in results),
    'human_play_performed':False, 'long_trajectory_exact_parity':False,
}
(HERE / 'final-audit.json').write_text(json.dumps(final,indent=2),encoding='utf-8')
(HERE / 'final-source-sha256.json').write_text(json.dumps({n:v['sha256'] for n,v in current.items()},indent=2),encoding='utf-8')
(HERE / 'final-validation-sha256.json').write_text(json.dumps(current_tests,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in final.items() if k not in ('removed','added','changed','suites')},indent=2))
if protected_changed or meta_changed or old_prefabs != new_prefabs or stale or harness_mismatches or not final['all_current_suites_passed']:
    raise SystemExit(1)
