"""Audit remote-based dispenser integration; read-only except for a new JSON report.

Run after synchronizing the disposable harness and completing its validation.
Reuses the earlier audit's parsing helpers without modifying its baseline or report.
"""
from collections import defaultdict
from io import BytesIO
from pathlib import Path
from zipfile import ZipFile
import argparse
import json
import re
import sys

sys.dont_write_bytecode = True

from audit_dispenser import (ROOT, RELATIVE, FEATURE, ALLOWED_CHANGED, ALLOWED_ADDED,
                             WORLD_SCRIPT_GUID, digest, digest_bytes, normalized_content,
                             git, parse_unity, differences)


REMOTE_BASE = '415dcf0381f5bf53d64758eca6aab8f510f6c195'
SAVED_STASH = '4a471a56e733987b4a511315f6e1d289cd615ec3'
BACKUP = ROOT / 'Library/DispenserRemoteIntegration/20261018-163617'
BACKUP_ZIP_SHA = 'bcaae78d7bef3e179b84dfeb03195078504459bd2957070e61da6f8c55e3679b'
BACKUP_MANIFEST_SHA = '0fdef2d4209e53a8a9bdf1aa5c1671273a459037b41b1f2d829c00f032a006d4'
PNG_MANIFEST_SHA = '10f88ae360e2a340ab419f3fbce75a735a3e4f53f9f827cb45cb019d4e3d4af6'
ALLOWED_INTEGRATION = ALLOWED_CHANGED | {
    'Editor/FluidExperimentAssemblyBuilder.cs', 'Editor/FluidExperimentBuilder.cs',
    'Editor/FluidExperimentDesignerSession.cs', 'Prefabs/Runtime/FluidExperimentRuntime.prefab',
    'Runtime/FluidExperimentWorld.Environment.cs',
}
WIRING_FILES = ('Scenes/FluidGpuComparison.unity', 'Prefabs/Runtime/FluidExperimentRuntime.prefab')


def wiring_audit(before_bytes, after_bytes, expected_reference):
    before, after = parse_unity(before_bytes), parse_unity(after_bytes)
    worlds = [key for key, doc in before.items() if
              doc['data'].get('MonoBehaviour', {}).get('m_Script', {}).get('guid') == WORLD_SCRIPT_GUID]
    refs = []
    for key in worlds:
        current = after.get(key, {}).get('data', {}).get('MonoBehaviour', {})
        refs.append(current.pop('dispenserPrefab', None))
    unexpected = differences(before, after)
    return {'passed': len(worlds) == 1 and refs == [expected_reference] and not unexpected,
            'references': refs, 'unexpectedSemanticChanges': unexpected,
            'originalObjectCount': len(before), 'currentObjectCount': len(after)}


def backup_audit():
    archive_path, manifest_path = BACKUP / 'local-worktree.zip', BACKUP / 'manifest.json'
    report = {'path': str(BACKUP), 'exists': archive_path.is_file() and manifest_path.is_file()}
    if not report['exists']:
        return dict(report, passed=False)
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    expected = manifest['sha256']
    with ZipFile(archive_path) as archive:
        actual = {entry.filename: digest_bytes(archive.read(entry))
                  for entry in archive.infolist() if not entry.is_dir()}
    mismatches = sorted(name for name in set(expected) | set(actual) if expected.get(name) != actual.get(name))
    archive_sha, manifest_sha = digest(archive_path), digest(manifest_path)
    return dict(report, passed=len(expected) == len(actual) == 140 and not mismatches
                and archive_sha == BACKUP_ZIP_SHA and manifest_sha == BACKUP_MANIFEST_SHA,
                originalHead=manifest['head'], manifestFileCount=len(expected), archiveFileCount=len(actual),
                archiveSha256=archive_sha, manifestSha256=manifest_sha, archiveHashMismatches=mismatches)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--harness', type=Path, default=ROOT / 'Library/FluidModelDHarness')
    parser.add_argument('--output', type=Path, default=ROOT / 'FluidGpuExperimentEvidence/remote-integration-audit.json')
    args = parser.parse_args()
    prefix = RELATIVE.as_posix() + '/'
    with ZipFile(BytesIO(git('archive', '--format=zip', REMOTE_BASE, '--', RELATIVE.as_posix()))) as archive:
        baseline = {entry.filename[len(prefix):]: archive.read(entry)
                    for entry in archive.infolist() if not entry.is_dir() and entry.filename.startswith(prefix)}
    current = {path.relative_to(FEATURE).as_posix(): path.read_bytes() for path in FEATURE.rglob('*') if path.is_file()}
    changed = sorted(path for path, data in baseline.items()
                     if normalized_content(path, current.get(path, b'')) != normalized_content(path, data))
    newline_only = sorted(path for path in baseline if path in current
                          and current[path] != baseline[path] and path not in changed)
    added, missing = sorted(current.keys() - baseline.keys()), sorted(baseline.keys() - current.keys())
    unexpected_changed = sorted(set(changed) - ALLOWED_INTEGRATION)
    unexpected_added = sorted(set(added) - ALLOWED_ADDED)
    missing_new = sorted(ALLOWED_ADDED - current.keys())
    protected = {path for path in baseline if path.startswith(('Shaders/', 'Data/', 'Art/', 'Prefabs/'))
                 and path != WIRING_FILES[1]}
    protected_mismatches = sorted(protected.intersection(changed))

    try:
        prefab_meta = FEATURE / 'Prefabs/Dispenser.prefab.meta'
        prefab_guid = re.search(r'^guid: ([a-f0-9]{32})$', prefab_meta.read_text(), re.M).group(1)
        script_guid = re.search(r'^guid: ([a-f0-9]{32})$',
                               (FEATURE / 'Runtime/FluidExperimentDispenser.cs.meta').read_text(), re.M).group(1)
        prefab_documents = parse_unity((FEATURE / 'Prefabs/Dispenser.prefab').read_bytes())
        controller_ids = [key for key, doc in prefab_documents.items()
                          if doc['data'].get('MonoBehaviour', {}).get('m_Script', {}).get('guid') == script_guid]
        expected_reference = {'fileID': int(controller_ids[0]), 'guid': prefab_guid, 'type': 3} if len(controller_ids) == 1 else None
        wiring = {path: wiring_audit(baseline[path], current[path], expected_reference) for path in WIRING_FILES}
    except (OSError, AttributeError, KeyError, ValueError) as error:
        wiring = {'unavailable': {'passed': False, 'error': str(error)}}

    directory_metas = {path for path in current if path.endswith('.meta') and (FEATURE / path[:-5]).is_dir()}
    harness_feature = args.harness / RELATIVE
    parity = sorted(path for path in current if path not in directory_metas
                    and (not (harness_feature / path).is_file() or digest(harness_feature / path) != digest_bytes(current[path])))
    harness_tests = sorted(path.name for path in (ROOT / 'FluidGpuExperimentEvidence/HarnessSource').glob('*.cs')
                           if not (args.harness / 'Assets/Editor' / path.name).is_file()
                           or digest(path) != digest(args.harness / 'Assets/Editor' / path.name))
    missing_meta = sorted(path for path in current if not path.endswith('.meta') and path + '.meta' not in current)
    orphan_meta = sorted(path for path in current if path.endswith('.meta') and not (FEATURE / path[:-5]).exists())
    invalid_meta, guid_paths = [], defaultdict(list)
    for meta in (ROOT / 'Assets').rglob('*.meta'):
        match = re.search(r'^guid: ([a-f0-9]{32})$', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
        if match:
            guid_paths[match[1]].append(meta.relative_to(ROOT).as_posix())
        elif meta.is_relative_to(FEATURE):
            invalid_meta.append(meta.relative_to(FEATURE).as_posix())
    duplicate_guids = {guid: paths for guid, paths in guid_paths.items() if len(paths) > 1}

    manifest_path = ROOT / 'FluidGpuExperimentEvidence/dispenser-source-manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    expected_names = {f'dispenser_{mode}.png' for mode in ('None', 'Ice', 'Hot')}
    png_mismatches = [entry['name'] for entry in manifest['files']
                      if not (FEATURE / 'Art/Dispenser' / entry['name']).is_file()
                      or digest(FEATURE / 'Art/Dispenser' / entry['name']) != entry['sha256']
                      or (FEATURE / 'Art/Dispenser' / entry['name']).stat().st_size != entry['sizeBytes']]
    png_complete = len(manifest['files']) == 3 and {entry['name'] for entry in manifest['files']} == expected_names
    png_complete &= {path.name for path in (FEATURE / 'Art/Dispenser').glob('*.png')} == expected_names
    tracked = git('diff', '--name-only', '-z', REMOTE_BASE, '--').decode().split('\0')
    untracked = git('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0')
    external = sorted(path for path in set(tracked + untracked) if path
                      and not path.startswith((prefix, 'FluidGpuExperimentEvidence/')))
    unmerged = git('diff', '--name-only', '--diff-filter=U', '-z').decode().strip('\0').split('\0')
    unmerged = [path for path in unmerged if path]
    head = git('rev-parse', 'HEAD').decode().strip()
    stash_preserved = SAVED_STASH in git('stash', 'list', '--format=%H').decode().splitlines()
    backup = backup_audit()
    checks = {'headMatchesRemoteBase': head == REMOTE_BASE, 'baselineFileCountIs348': len(baseline) == 348,
              'originalFilesPresent': not missing, 'changesWhitelisted': not unexpected_changed,
              'additionsWhitelisted': not unexpected_added, 'expectedNewFilesPresent': not missing_new,
              'remoteAssetsPreserved': not protected_mismatches, 'wiringOnly': all(v['passed'] for v in wiring.values()),
              'harnessSourceMatchesExactly': not parity, 'harnessTestsMatchExactly': not harness_tests,
              'metadataValid': not missing_meta and not orphan_meta and not invalid_meta and not duplicate_guids,
              'sourceManifestPreserved': digest(manifest_path) == PNG_MANIFEST_SHA,
              'originalPngBytesMatch': png_complete and not png_mismatches, 'repositoryScopePreserved': not external,
              'noUnmergedPaths': not unmerged, 'stashPreserved': stash_preserved, 'backupPreserved': backup['passed']}
    report = dict(passed=all(checks.values()), checks=checks, remoteBase=REMOTE_BASE, currentHEAD=head,
                  stash=SAVED_STASH, backupPath=str(BACKUP), backup=backup,
                  baselineFileCount=len(baseline), currentFileCount=len(current), changed=changed, added=added,
                  missing=missing, unexpectedChanged=unexpected_changed, unexpectedAdded=unexpected_added,
                  missingExpectedNewFiles=missing_new, newlineOnlyDifferences=newline_only,
                  protectedAssetCount=len(protected), protectedMismatches=protected_mismatches,
                  sceneAndRuntimePrefabWiring=wiring, harnessMismatches=parity, harnessTestMismatches=harness_tests,
                  directoryMetasExcludedFromHarnessParity=sorted(directory_metas), missingMetas=missing_meta,
                  orphanMetas=orphan_meta, invalidMetas=invalid_meta, duplicateGuids=duplicate_guids,
                  pngHashMismatches=png_mismatches, outsideScopeChanges=external, unmergedPaths=unmerged,
                  currentSha256={path: digest_bytes(data) for path, data in current.items()},
                  baselineSha256={path: digest_bytes(data) for path, data in baseline.items()},
                  scope='415dcf0 baseline; only 14 named existing feature files and 29 dispenser additions allowed. '
                        'Protected UTF-8 text differs only by CRLF/LF; PNGs and harness files compare byte-exact. '
                        'Scene/runtime prefab permits only the exact dispenser controller reference. '
                        'Original local work is checked in its immutable 140-file ZIP and retained stash.')
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(json.dumps({'passed': report['passed'], 'checks': checks, 'changedCount': len(changed),
                      'addedCount': len(added), 'protectedAssetCount': len(protected), 'report': str(args.output)}, indent=2))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
