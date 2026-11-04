"""Compare the user's existing checkout with the pre-edit byte-hash baseline."""
import hashlib
import json
from pathlib import Path
import re

EVIDENCE = Path(__file__).resolve().parent
ROOT = EVIDENCE.parents[1]
FEATURE = 'Assets/_Project/Features/Bartending/FluidGpuExperiment/'
before = json.loads((EVIDENCE / 'before-sha256.json').read_text(encoding='utf-8'))
approved = {FEATURE + p for p in (
    'Prefabs/Glass_highball.prefab', 'Prefabs/Glass_hurricane.prefab',
    'Prefabs/Glass_martini.prefab', 'Prefabs/Glass_rock.prefab',
    'Editor/FluidExperimentDesignerWindow.cs', 'Editor/FluidExperimentDesignerSession.cs', 'README.md')}
changed, deleted, errors = [], [], []
for relative, expected in before.items():
    path = ROOT / relative
    if not path.exists():
        deleted.append(relative)
    elif hashlib.sha256(path.read_bytes()).hexdigest() != expected:
        changed.append(relative)
errors += ['Unexpected change: ' + p for p in changed if p not in approved]
errors += ['Deleted original: ' + p for p in deleted]
glass_physics = {}
for relative in sorted(approved):
    if not relative.endswith('.prefab'):
        continue
    without_identity = re.sub(rb'^  servingGlassId: [^\r\n]*\r?\n', b'', (ROOT / relative).read_bytes(), flags=re.M)
    lf = without_identity.replace(b'\r\n', b'\n')
    matches = any(hashlib.sha256(candidate).hexdigest() == before[relative]
                  for candidate in (without_identity, lf, lf.replace(b'\n', b'\r\n')))
    glass_physics[relative] = matches
    if not matches:
        errors.append('Glass prefab changed beyond its identity: ' + relative)
prior_audit = json.loads((EVIDENCE / 'before-audit.json').read_text(encoding='utf-8'))
after_audit = json.loads((EVIDENCE / 'after-audit.json').read_text(encoding='utf-8'))
overrides_preserved = prior_audit['scene_overrides'] == after_audit['scene_overrides']
if not overrides_preserved:
    errors.append('Saved scene overrides changed')
errors.extend(after_audit['errors'])
report = {'result': 'PASS' if not errors else 'FAIL', 'baseline_file_count': len(before),
    'unchanged_original_file_count': len(before) - len(changed) - len(deleted),
    'changed_original_files': changed, 'deleted_original_files': deleted,
    'glass_prefabs_only_add_identity': glass_physics, 'scene_overrides_preserved': overrides_preserved,
    'catalog_count': after_audit['catalog_count'], 'reference_errors': after_audit['errors'], 'errors': errors}
(EVIDENCE / 'preservation.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps(report, indent=2, ensure_ascii=True))
raise SystemExit(0 if not errors else 1)
