"""Audit the original working-tree bytes, GUID separation and validation copy."""
from pathlib import Path
import hashlib
import json
import re

root = Path(__file__).resolve().parents[2]
evidence = root / 'FluidGpuExperimentEvidence'
feature = root / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
original = json.loads((evidence / 'original-tracked-sha256.json').read_text(encoding='utf-8-sig'))
changed = [name for name, expected in original.items()
           if not (root / name).is_file() or hashlib.sha256((root / name).read_bytes()).hexdigest() != expected]
old_guids = set(json.loads((evidence / 'clone-guid-map.json').read_text()))
original_guids = set()
for name in original:
    if name.endswith('.meta') and (root / name).is_file():
        match = re.search(r'^guid: ([a-f0-9]{32})', (root / name).read_text(encoding='utf-8-sig', errors='replace'), re.M)
        if match:
            original_guids.add(match.group(1))
new_guids = {}
missing_meta, duplicate_guids, old_references, stale_harness = [], [], [], []
for path in [feature, *feature.rglob('*')]:
    if path.suffix == '.meta':
        continue
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        missing_meta.append(str(path.relative_to(root)))
        continue
    guid = re.search(r'^guid: ([a-f0-9]{32})', meta.read_text(), re.M).group(1)
    if guid in new_guids or guid in original_guids:
        duplicate_guids.append(str(path.relative_to(root)))
    new_guids[guid] = str(path.relative_to(root))
    if path.suffix in {'.unity', '.prefab', '.asset', '.mat'}:
        refs = set(re.findall(r'guid: ([a-f0-9]{32})', path.read_text(encoding='utf-8-sig')))
        if refs & old_guids:
            old_references.append(str(path.relative_to(root)))
    if path.is_file() and path.suffix in {'.cs', '.compute', '.hlsl', '.shader', '.unity', '.asset', '.prefab', '.mat'}:
        validated = evidence / 'HarnessProject' / path.relative_to(root)
        if not validated.exists() or path.read_bytes() != validated.read_bytes():
            stale_harness.append(str(path.relative_to(root)))

result = dict(original_file_count=len(original), changed_original_files=changed,
              experiment_guid_count=len(new_guids), duplicate_or_reused_guids=duplicate_guids,
              missing_metadata=missing_meta, references_to_original_forked_assets=old_references,
              source_differs_from_validation_copy=stale_harness)
result['passed'] = not any((changed, duplicate_guids, missing_meta, old_references, stale_harness))
(evidence / 'isolation-audit.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
raise SystemExit(0 if result['passed'] else 1)
