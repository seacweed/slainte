"""Saved-file prefab/reference audit; never changes Unity assets."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

import yaml

ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
EVIDENCE = Path(__file__).resolve().parent


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def documents(path):
    text = path.read_text(encoding='utf-8-sig')
    text = re.sub(r'^%.*\n', '', text, flags=re.M)
    text = re.sub(r'^--- !u!(\d+) &(-?\d+)(?: stripped)?', r'---\n__class: \1\n__id: \2', text, flags=re.M)
    return [doc for doc in yaml.safe_load_all(text) if doc]


def components(path, suffix):
    return [d['MonoBehaviour'] for d in documents(path)
            if 'MonoBehaviour' in d and d['MonoBehaviour'].get('m_EditorClassIdentifier', '').endswith(suffix)]


def manifest():
    paths = list(FEATURE.rglob('*'))
    paths += list((ROOT / 'Assets/_Project/Features/Bartending/Runtime').rglob('*'))
    paths += list((ROOT / 'Assets/_Project/Features/Business/Runtime').rglob('*'))
    return {p.relative_to(ROOT).as_posix(): digest(p) for p in paths if p.is_file()}


def audit():
    meta_paths = subprocess.check_output(['rg', '--files', 'Assets', '-g', '*.meta'], cwd=ROOT, text=True, encoding='utf-8').splitlines()
    guids = {}
    for relative in meta_paths:
        path = ROOT / relative
        match = re.search(r'^guid: ([a-f0-9]{32})$', path.read_text(encoding='utf-8-sig'), re.M)
        if match:
            guids.setdefault(match[1], []).append(path.with_suffix(''))
    referenced_assets = []
    referenced_guids = set()
    for path in FEATURE.rglob('*'):
        if not path.is_file() or path.suffix not in ('.prefab', '.unity', '.asset', '.mat'):
            continue
        referenced_assets.append(path)
        referenced_guids.update(re.findall(r'guid: ([a-f0-9]{32})', path.read_text(encoding='utf-8-sig')))
    unknown = {g for g in referenced_guids if g not in guids and not g.startswith('0000000000000000')}
    if unknown:
        packages = subprocess.run(['rg', '-l', '--no-ignore', 'guid: (' + '|'.join(sorted(unknown)) + ')',
            'Library/PackageCache', '-g', '*.meta'], cwd=ROOT, text=True, encoding='utf-8', capture_output=True)
        if packages.returncode not in (0, 1):
            raise RuntimeError(packages.stderr)
        for relative in packages.stdout.splitlines():
            path = ROOT / relative
            guid = re.search(r'^guid: ([a-f0-9]{32})$', path.read_text(encoding='utf-8-sig'), re.M)[1]
            guids.setdefault(guid, []).append(path.with_suffix(''))
    errors, prefabs, missing_refs = [], [], []
    for path in referenced_assets:
        for guid in set(re.findall(r'guid: ([a-f0-9]{32})', path.read_text(encoding='utf-8-sig'))):
            if guid.startswith('0000000000000000'):
                continue
            if guid not in guids or not guids[guid][0].exists():
                missing_refs.append({'asset': path.relative_to(ROOT).as_posix(), 'guid': guid})
    errors.extend('Unresolved reference: ' + str(x) for x in missing_refs)
    catalog_path = FEATURE / 'Data/FluidExperimentCatalog.asset'
    catalog = documents(catalog_path)[0]['MonoBehaviour']['entries']
    seen_ids, seen_prefabs = set(), set()
    for entry in catalog:
        guid = entry['prefab'].get('guid')
        if entry['id'] in seen_ids or guid in seen_prefabs:
            errors.append('Duplicate catalog entry: ' + entry['id'])
        seen_ids.add(entry['id']); seen_prefabs.add(guid)
        if guid not in guids:
            continue
        path = guids[guid][0]
        bodies = components(path, '.FluidExperimentBody')
        if bodies and entry['ingredient'] != bodies[0].get('ingredient', {'fileID': 0}):
            errors.append('Catalog ingredient differs from prefab: ' + path.name)
    for path in sorted((FEATURE / 'Prefabs').glob('*.prefab')):
        body = next(iter(components(path, '.FluidExperimentBody')), None)
        source = next(iter(components(path, '.FluidExperimentGarnishSource')), None)
        meta = path.with_suffix(path.suffix + '.meta').read_text()
        guid = re.search(r'^guid: (\w+)', meta, re.M)[1]
        item = {'path': path.relative_to(ROOT).as_posix(), 'catalogued': guid in seen_prefabs}
        if body:
            item.update({k: body.get(k) for k in ('kind', 'displayName', 'ingredient', 'servingGlassId',
                'capacityMl', 'remainingMl', 'pourMlPerSecond', 'collisionProfile', 'icePrefab', 'shakerProfile')})
            if body['kind'] == 0 and not body.get('ingredient', {}).get('guid'):
                errors.append('Bottle missing ingredient: ' + path.name)
            if body['kind'] == 1 and not (body.get('servingGlassId') or '').strip():
                errors.append('Glass missing recipe identity: ' + path.name)
        if source:
            item['garnishPrefab'] = source.get('garnishPrefab')
        prefabs.append(item)
    overrides = []
    for path in sorted((FEATURE / 'Scenes').glob('*.unity')):
        for doc in documents(path):
            instance = doc.get('PrefabInstance')
            if not instance:
                continue
            guid = instance['m_SourcePrefab']['guid']
            source = guids.get(guid, [Path(guid)])[0]
            changes = instance['m_Modification'].get('m_Modifications', [])
            substantive = [c for c in changes if not c['propertyPath'].startswith(('m_Local', 'm_RootOrder', 'm_Name', 'm_Euler'))]
            overrides.append({'scene': path.name, 'source': source.name, 'changes': substantive})
    return {'errors': errors, 'prefabs': prefabs, 'catalog_count': len(catalog), 'scene_overrides': overrides,
            'missing_references': missing_refs}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=('baseline', 'audit'))
    args = parser.parse_args()
    if args.mode == 'baseline':
        target = EVIDENCE / 'before-sha256.json'
        if target.exists():
            print('Retaining existing baseline without overwriting it.')
        else:
            before = manifest()
            target.write_text(json.dumps(before, indent=2), encoding='utf-8')
            print(json.dumps({'baseline_files': len(before)}))
    report = audit()
    name = 'before-audit.json' if args.mode == 'baseline' else 'after-audit.json'
    (EVIDENCE / name).write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({'catalog_count': report['catalog_count'], 'prefab_count': len(report['prefabs']),
        'errors': report['errors'], 'substantive_override_count': sum(len(x['changes']) for x in report['scene_overrides'])}, ensure_ascii=False))
    if report['errors'] and args.mode == 'audit':
        raise SystemExit(1)


if __name__ == '__main__':
    main()
