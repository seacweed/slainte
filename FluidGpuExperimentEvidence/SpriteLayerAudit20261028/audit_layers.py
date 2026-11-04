"""Read-only serialized sprite layer inventory. Writes evidence only."""
import csv
import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
spec = importlib.util.spec_from_file_location('prefab_reader', HERE.parent / 'PrefabReadiness20261028/audit_prefabs.py')
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
paths = [p for p in FEATURE.rglob('*') if p.is_file()]
paths += [p for p in (ROOT / 'ProjectSettings').rglob('*') if p.is_file()]
manifest = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
before = HERE / 'before-sha256.json'
if not before.exists():
    before.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
else:
    original = json.loads(before.read_text(encoding='utf-8'))
    changed = [p for p, value in original.items() if manifest.get(p) != value]
    (HERE / 'preservation.json').write_text(json.dumps({'checked': len(original), 'changed': changed}, indent=2), encoding='utf-8')
    if changed: raise SystemExit('Original project files changed during audit: ' + str(changed))

rows, groups, scene_overrides = [], [], []
for path in sorted((FEATURE / 'Prefabs').rglob('*.prefab')):
    docs = reader.documents(path)
    objects = {d['__id']: d['GameObject'] for d in docs if 'GameObject' in d}
    transforms = {d['__id']: d['Transform'] for d in docs if 'Transform' in d}
    by_object = {t['m_GameObject']['fileID']: identity for identity, t in transforms.items()}
    def hierarchy(transform_id):
        t = transforms.get(transform_id)
        if not t: return ''
        label = objects[t['m_GameObject']['fileID']]['m_Name']
        parent = t['m_Father']['fileID']
        return (hierarchy(parent) + '/' if parent else '') + label
    def world_z(transform_id):
        t = transforms.get(transform_id)
        if not t: return 0
        parent = t['m_Father']['fileID']
        return world_z(parent) + float(t['m_LocalPosition']['z'])
    for doc in docs:
        if doc.get('__class') == 210:
            groups.append(path.relative_to(ROOT).as_posix())
        for renderer_type in ('SpriteRenderer', 'LineRenderer', 'MeshRenderer'):
            renderer = doc.get(renderer_type)
            if not renderer: continue
            transform_id = by_object.get(renderer['m_GameObject']['fileID'], 0)
            rows.append({'prefab': path.name, 'renderer': hierarchy(transform_id), 'type': renderer_type,
                'enabled': renderer.get('m_Enabled'), 'sortingLayerID': renderer.get('m_SortingLayerID', 0),
                'sortingOrder': renderer.get('m_SortingOrder', 0), 'z': world_z(transform_id),
                'sprite': renderer.get('m_Sprite'), 'materials': renderer.get('m_Materials'),
                'color': renderer.get('m_Color')})
for path in sorted((FEATURE / 'Scenes').glob('*.unity')):
    for doc in reader.documents(path):
        for change in doc.get('PrefabInstance', {}).get('m_Modification', {}).get('m_Modifications', []):
            if change['propertyPath'] in ('m_SortingOrder', 'm_SortingLayerID', 'm_LocalPosition.z'):
                scene_overrides.append({'scene': path.name, **change})
summary = {'prefab_count': len(list((FEATURE / 'Prefabs').rglob('*.prefab'))), 'renderer_count': len(rows),
           'sorting_groups': sorted(set(groups)), 'sorting_layer_ids': sorted(set(r['sortingLayerID'] for r in rows)),
           'world_z_values': sorted(set(r['z'] for r in rows)), 'scene_sorting_overrides': scene_overrides,
           'renderers': rows}
(HERE / 'serialized-inventory.json').write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding='utf-8')
with (HERE / 'serialized-inventory.csv').open('w', encoding='utf-8-sig', newline='') as file:
    writer = csv.DictWriter(file, fieldnames=['prefab', 'renderer', 'type', 'enabled', 'sortingLayerID', 'sortingOrder', 'z'])
    writer.writeheader()
    writer.writerows({k: row[k] for k in writer.fieldnames} for row in rows)
for name in sorted(set(row['prefab'] for row in rows)):
    print(name + ': ' + ', '.join(row['renderer'].split('/')[-1] + '=' + str(row['sortingOrder']) for row in rows if row['prefab'] == name))
print(json.dumps({key: value for key, value in summary.items() if key != 'renderers'}))
