"""Read-only project audit against the pre-request Git archive; writes an evidence report only.

Run after the hidden author/validation project has been synchronized back to the workspace.
Requires PyYAML for semantic Unity scene comparison (available in the validation Python).
"""
from collections import defaultdict
from pathlib import Path
from zipfile import ZipFile
import argparse
import hashlib
import json
import re
import subprocess
import yaml


ROOT = Path(__file__).resolve().parents[2]
RELATIVE = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
FEATURE = ROOT / RELATIVE
ALLOWED_CHANGED = {
    'Runtime/FluidExperimentWorld.cs',
    'Runtime/FluidExperimentBody.cs',
    'Runtime/FluidExperimentInteractor.cs',
    'Runtime/FluidExperimentInteractor.Pointer.cs',
    'Runtime/FluidExperimentComparison.cs',
    'Runtime/FluidExperimentGpuLiquid.Streams.cs',
    'Scenes/FluidGpuComparison.unity',
    'README.md', 'DEVELOPMENT_PLAN.md',
}
NEW_ASSETS = {
    'Editor/FluidExperimentDispenserBuilder.cs',
    'Runtime/FluidExperimentDispenser.cs',
    'Runtime/FluidExperimentDispenser.Motion.cs',
    'Runtime/FluidExperimentDispenser.Emission.cs',
    'Runtime/FluidExperimentInteractor.Dispenser.cs',
    'Runtime/FluidExperimentWorld.Dispenser.cs',
    'Data/CollisionProfiles/Dispenser.asset',
    'Data/DispenserHotWater.asset',
    'Data/DispenserHose.mat',
    'Data/DispenserSurface.physicsMaterial2D',
    'Prefabs/Dispenser.prefab',
    *(f'Art/Dispenser/dispenser_{mode}.png' for mode in ('None', 'Ice', 'Hot')),
}
NEW_FOLDERS = {'Art/Dispenser'}
ALLOWED_ADDED = NEW_ASSETS | {p + '.meta' for p in NEW_ASSETS | NEW_FOLDERS}
SCENE = 'Scenes/FluidGpuComparison.unity'
WORLD_SCRIPT_GUID = 'eb1755b6f8984d2e881788ee4134bbf8'
TEXT_SUFFIXES = {'.cs', '.meta', '.asset', '.mat', '.prefab', '.unity', '.shader',
                 '.hlsl', '.compute', '.md', '.physicsmaterial2d'}


def digest_bytes(content):
    return hashlib.sha256(content).hexdigest()


def digest(path):
    return digest_bytes(path.read_bytes())


def normalized_content(path, content):
    """Only normalize CRLF in validated UTF-8 text; binary assets remain byte-exact.

    Git archives and Windows checkouts can have different line endings even when
    git reports a clean worktree. Do not normalize whitespace, BOMs or lone CRs.
    """
    if Path(path).suffix.lower() not in TEXT_SUFFIXES or b'\0' in content:
        return content
    try:
        content.decode('utf-8')
    except UnicodeDecodeError:
        return content
    return content.replace(b'\r\n', b'\n')


def git(*args):
    # Capture Git's checkout-newline notices; the audit reports those explicitly.
    result = subprocess.run(['git', *args], cwd=ROOT, capture_output=True)
    if result.returncode:
        raise RuntimeError('Git audit query failed: ' + result.stderr.decode('utf-8', errors='replace'))
    return result.stdout


def parse_unity(content):
    text = content.decode('utf-8-sig')
    matches = list(re.finditer(r'^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$', text, re.M))
    documents = {}
    for i, match in enumerate(matches):
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        key = match.group(2)
        if key in documents:
            raise ValueError('Duplicate Unity object file ID: ' + key)
        documents[key] = {'unityClass': int(match.group(1)),
                          'data': yaml.safe_load(text[match.end():end])}
    return documents


def canonical(value, path='', ignored=None):
    if isinstance(value, dict):
        result = {}
        for key, child in value.items():
            here = path + '/' + str(key)
            # Unity may rewrite editor type labels or serialization format versions.
            # All gameplay fields, references, object IDs, transforms and list order remain compared.
            if key in ('m_EditorClassIdentifier', 'serializedVersion'):
                if ignored is not None:
                    ignored.append(here)
            else:
                result[key] = canonical(child, here, ignored)
        return result
    if isinstance(value, list):
        return [canonical(child, path + '/' + str(i), ignored) for i, child in enumerate(value)]
    return value


def differences(before, after, path=''):
    result = []
    if isinstance(before, dict) and isinstance(after, dict):
        for key in sorted(set(before) | set(after), key=str):
            here = path + '/' + str(key)
            if key not in before:
                result.append({'path': here, 'kind': 'added', 'after': after[key]})
            elif key not in after:
                result.append({'path': here, 'kind': 'removed', 'before': before[key]})
            else:
                result.extend(differences(before[key], after[key], here))
    elif isinstance(before, list) and isinstance(after, list):
        if len(before) != len(after):
            result.append({'path': path, 'kind': 'listLength', 'before': len(before), 'after': len(after)})
        else:
            for i, (left, right) in enumerate(zip(before, after)):
                result.extend(differences(left, right, path + '/' + str(i)))
    elif before != after:
        result.append({'path': path, 'kind': 'changed', 'before': before, 'after': after})
    return result


def audit_scene(before_bytes, current_bytes):
    before, after = parse_unity(before_bytes), parse_unity(current_bytes)
    ignored = []
    world_ids = [key for key, doc in before.items()
                 if doc['data'].get('MonoBehaviour', {}).get('m_Script', {}).get('guid') == WORLD_SCRIPT_GUID]
    wiring = []
    prefab_meta = FEATURE / 'Prefabs/Dispenser.prefab.meta'
    prefab_guid = re.search(r'^guid: ([a-f0-9]{32})$', prefab_meta.read_text(), re.M).group(1) if prefab_meta.is_file() else None
    for key in world_ids:
        original = before[key]['data']['MonoBehaviour']
        current = after.get(key, {}).get('data', {}).get('MonoBehaviour', {})
        reference = current.pop('dispenserPrefab', None)
        original.pop('dispenserPrefab', None)
        wiring.append({'worldFileId': key, 'reference': reference,
                       'valid': bool(reference and reference.get('guid') == prefab_guid
                                     and reference.get('fileID', 0) != 0 and reference.get('type') == 3)})
    left = canonical(before, ignored=ignored)
    right = canonical(after, ignored=ignored)
    changes = differences(left, right)
    return {'passed': len(world_ids) == 1 and all(item['valid'] for item in wiring) and not changes,
            'worldWiring': wiring, 'unexpectedSemanticChanges': changes,
            'ignoredSerializationMetadata': sorted(set(ignored)),
            'originalObjectCount': len(before), 'currentObjectCount': len(after)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--harness', type=Path, default=ROOT / 'Library/FluidModelDHarness')
    parser.add_argument('--output', type=Path, default=ROOT / 'FluidGpuExperimentEvidence/dispenser-preservation-audit.json')
    args = parser.parse_args()
    archive_path = ROOT / 'Library/DispenserOriginal/feature-head.zip'
    with ZipFile(archive_path) as archive:
        prefix = RELATIVE.as_posix() + '/'
        baseline_bytes = {entry.filename[len(prefix):]: archive.read(entry)
                          for entry in archive.infolist() if not entry.is_dir() and entry.filename.startswith(prefix)}
        baseline_commit = archive.comment.decode('ascii').strip()
    baseline = {path: digest_bytes(data) for path, data in baseline_bytes.items()}
    current_bytes = {path.relative_to(FEATURE).as_posix(): path.read_bytes()
                     for path in FEATURE.rglob('*') if path.is_file()}
    current = {path: digest_bytes(data) for path, data in current_bytes.items()}
    baseline_normalized = {path: digest_bytes(normalized_content(path, data))
                           for path, data in baseline_bytes.items()}
    current_normalized = {path: digest_bytes(normalized_content(path, data))
                          for path, data in current_bytes.items()}
    raw_changed = sorted(path for path in baseline if current.get(path) != baseline[path])
    changed = sorted(path for path in baseline if current_normalized.get(path) != baseline_normalized[path])
    newline_only = sorted(set(raw_changed) - set(changed))
    added = sorted(current.keys() - baseline.keys())
    missing = sorted(baseline.keys() - current.keys())
    unexpected_changed = sorted(set(changed) - ALLOWED_CHANGED)
    unexpected_added = sorted(set(added) - ALLOWED_ADDED)
    missing_new = sorted(ALLOWED_ADDED - current.keys())

    protected = sorted(path for path in baseline
                       if path.startswith(('Shaders/', 'Art/', 'Data/', 'Prefabs/')))
    protected_changed = sorted(path for path in protected
                               if current_normalized.get(path) != baseline_normalized[path])
    protected_raw_changed = sorted(path for path in protected if current.get(path) != baseline[path])
    protected_groups = {
        category: {'count': sum(path.startswith(prefix) for path in protected),
                   'unchanged': all(current_normalized.get(path) == baseline_normalized[path]
                                    for path in protected if path.startswith(prefix)),
                   'rawBytesUnchanged': all(current.get(path) == baseline[path]
                                            for path in protected if path.startswith(prefix)),
                   'newlineOnlyDifferenceCount': sum(path.startswith(prefix) for path in newline_only
                                                     if path in protected)}
        for category, prefix in [('shaders', 'Shaders/'), ('originalArt', 'Art/'),
                                  ('profilesAndSettings', 'Data/'), ('originalPrefabs', 'Prefabs/')]
    }

    directory_metas = {path for path in current if path.endswith('.meta') and (FEATURE / path[:-5]).is_dir()}
    harness = args.harness / RELATIVE
    parity = sorted(path for path in current if path not in directory_metas
                    and (not (harness / path).is_file() or digest(harness / path) != current[path]))
    test = ROOT / 'FluidGpuExperimentEvidence/HarnessSource/DispenserValidation.cs'
    harness_test = args.harness / 'Assets/Editor/DispenserValidation.cs'
    test_parity = test.is_file() and harness_test.is_file() and digest(test) == digest(harness_test)

    missing_meta = sorted(path for path in NEW_ASSETS | NEW_FOLDERS if not (FEATURE / (path + '.meta')).is_file())
    orphan_meta = sorted(path for path in added if path.endswith('.meta') and not (FEATURE / path[:-5]).exists())
    invalid_meta = []
    for path in NEW_ASSETS | NEW_FOLDERS:
        meta = FEATURE / (path + '.meta')
        if meta.is_file() and not re.search(r'^guid: [a-f0-9]{32}$', meta.read_text(encoding='utf-8-sig'), re.M):
            invalid_meta.append(path + '.meta')
    guid_paths = defaultdict(list)
    for meta in (ROOT / 'Assets').rglob('*.meta'):
        match = re.search(r'^guid: ([a-f0-9]{32})$', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
        if match:
            guid_paths[match.group(1)].append(meta.relative_to(ROOT).as_posix())
    duplicate_guids = {guid: sorted(paths) for guid, paths in guid_paths.items() if len(paths) > 1}

    manifest = json.loads((ROOT / 'FluidGpuExperimentEvidence/dispenser-source-manifest.json').read_text(encoding='utf-8-sig'))
    expected_names = {f'dispenser_{mode}.png' for mode in ('None', 'Ice', 'Hot')}
    manifest_names = [entry['name'] for entry in manifest['files']]
    manifest_complete = len(manifest_names) == 3 and set(manifest_names) == expected_names
    source_mismatches = []
    for entry in manifest['files']:
        path = FEATURE / 'Art/Dispenser' / entry['name']
        if not path.is_file() or digest(path) != entry['sha256'] or path.stat().st_size != entry['sizeBytes']:
            source_mismatches.append(entry['name'])
    actual_png_names = {path.name for path in (FEATURE / 'Art/Dispenser').glob('*.png')}

    # Compare every tracked path with the same commit embedded in the original git archive.
    # The evidence directory is explicitly allowed; production assets, ProjectSettings and Packages are protected.
    tracked_changes = sorted(filter(None, git('diff', '--name-only', '-z', baseline_commit, '--').decode('utf-8').split('\0')))
    untracked = sorted(filter(None, git('ls-files', '--others', '--exclude-standard', '-z').decode('utf-8').split('\0')))
    def allowed_repository_path(path):
        return path.startswith(RELATIVE.as_posix() + '/') or path.startswith('FluidGpuExperimentEvidence/')
    external_changes = sorted(path for path in tracked_changes if not allowed_repository_path(path))
    external_additions = sorted(path for path in untracked if not allowed_repository_path(path))
    scene = audit_scene(baseline_bytes[SCENE], (FEATURE / SCENE).read_bytes())

    passed = (len(baseline) == 298 and not missing and not unexpected_changed and not unexpected_added
              and not missing_new and not protected_changed and not parity and test_parity
              and not missing_meta and not orphan_meta and not invalid_meta and not duplicate_guids
              and manifest_complete and not source_mismatches and actual_png_names == expected_names
              and not external_changes and not external_additions and scene['passed'])
    report = dict(passed=passed, baselineCommit=baseline_commit, baselineArchiveSha256=digest(archive_path),
                  baselineFileCount=len(baseline), currentFileCount=len(current), changed=changed,
                  rawChanged=raw_changed, newlineOnlyDifferences=newline_only, added=added,
                  missing=missing, unexpectedChanged=unexpected_changed, unexpectedAdded=unexpected_added,
                  missingExpectedNewAssets=missing_new, protectedAssetCount=len(protected),
                  protectedAssetMismatches=protected_changed, protectedRawByteDifferences=protected_raw_changed,
                  protectedGroups=protected_groups,
                  sceneSemanticAudit=scene, harnessMismatches=parity, dispenserHarnessTestMatches=test_parity,
                  newAssetsMissingMeta=missing_meta, newOrphanMetas=orphan_meta, invalidNewMetas=invalid_meta,
                  repositoryAssetGuidCount=len(guid_paths), duplicateAssetGuids=duplicate_guids,
                  sourceManifestComplete=manifest_complete, suppliedPngHashMismatches=source_mismatches,
                  exactlyThreeDispenserPngs=actual_png_names == expected_names,
                  externalTrackedChanges=external_changes, externalUntrackedPaths=external_additions,
                  productionArtItemDefinitionsSettingsUnchanged=not external_changes,
                  disposableDirectoryMetasExcluded=sorted(directory_metas),
                  baselineSha256=baseline, currentSha256=current,
                  baselineNormalizedContentSha256=baseline_normalized,
                  currentNormalizedContentSha256=current_normalized,
                  scope='F dispenser additions and explicit integration files only; existing UTF-8 text content preserved '
                        'after CRLF-to-LF normalization only, with raw hashes and newline-only differences reported separately; '
                        'binary assets and original PNGs compared byte-exact; '
                        'scene semantics permit only the dispenserPrefab reference and serialization metadata; '
                        'original downloaded PNG SHA256, full-repository tracked scope and Unity GUID uniqueness checked.')
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    errors = {key: value for key, value in {
        'missing': missing, 'unexpectedChanged': unexpected_changed, 'unexpectedAdded': unexpected_added,
        'missingExpectedNewAssets': missing_new, 'protectedAssetMismatches': protected_changed,
        'harnessMismatches': parity, 'newAssetsMissingMeta': missing_meta, 'newOrphanMetas': orphan_meta,
        'invalidNewMetas': invalid_meta, 'duplicateAssetGuids': duplicate_guids,
        'suppliedPngHashMismatches': source_mismatches, 'externalTrackedChanges': external_changes,
        'externalUntrackedPaths': external_additions, 'unexpectedSceneChanges': scene['unexpectedSemanticChanges'],
    }.items() if value}
    if not scene['passed']:
        errors['sceneWiring'] = scene['worldWiring']
    if not test_parity:
        errors['dispenserHarnessTestMatches'] = False
    if not manifest_complete or actual_png_names != expected_names:
        errors['sourceManifestComplete'] = manifest_complete
        errors['actualPngNames'] = sorted(actual_png_names)
    if len(baseline) != 298:
        errors['unexpectedBaselineFileCount'] = len(baseline)
    compact = dict(passed=passed, baselineFileCount=len(baseline), currentFileCount=len(current),
                   addedFileCount=len(added), changedContentCount=len(changed),
                   newlineOnlyDifferenceCount=len(newline_only), protectedAssetCount=len(protected),
                   protectedContentMismatches=len(protected_changed), sourcePngHashesMatch=not source_mismatches,
                   sceneSemanticPassed=scene['passed'], harnessMismatches=len(parity),
                   errors=errors, report=str(args.output))
    print(json.dumps(compact, indent=2, ensure_ascii=False))
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
