"""Audit hurricane breakage against the saved pre-edit feature and the executed harness."""
from pathlib import Path
from collections import defaultdict
import hashlib
import json
import re

root = Path(__file__).resolve().parents[2]
relative = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
feature = root / relative
before = root / 'Library/GlassBreakageOriginal/FluidGpuExperiment'
harness_project = root / 'Library/FluidModelDHarness'
harness = harness_project / relative
allowed_changed = {
    'Prefabs/Glass_hurricane.prefab',
    'Runtime/FluidExperimentWorld.cs',
    'Runtime/FluidExperimentGpuLiquid.cs',
    'Runtime/FluidExperimentShakerPart.cs',
    'README.md', 'DEVELOPMENT_PLAN.md',
}
new_assets = {
    'Data/HurricaneBreakage.asset',
    'Data/HurricaneShard.physicsMaterial2D',
    'Runtime/FluidExperimentBreakableGlass.cs',
    'Runtime/FluidExperimentBreakageProfile.cs',
    'Runtime/FluidExperimentGlassShard.cs',
    'Runtime/FluidExperimentWorld.Breakage.cs',
    'Editor/FluidExperimentBreakageBuilder.cs',
    *(f'Art/Shards/Hurricane/hurricane_shard_{i:02d}.png' for i in range(1, 17)),
}
new_folders = {'Art/Shards', 'Art/Shards/Hurricane'}
allowed_added = new_assets | {p + '.meta' for p in new_assets | new_folders}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

baseline = {p.relative_to(before).as_posix(): digest(p) for p in before.rglob('*') if p.is_file()}
current = {p.relative_to(feature).as_posix(): digest(p) for p in feature.rglob('*') if p.is_file()}
changed = sorted(p for p in baseline if current.get(p) != baseline[p])
added = sorted(current.keys() - baseline.keys())
missing = sorted(baseline.keys() - current.keys())
unexpected_changed = sorted(set(changed) - allowed_changed)
unexpected_added = sorted(set(added) - allowed_added)
missing_expected_assets = sorted(allowed_added - current.keys())

# Folder GUIDs are owned by the disposable harness. Asset and source metadata are compared.
directory_metas = {p for p in current if p.endswith('.meta') and (feature / p[:-5]).is_dir()}
parity = sorted(p for p in current if p not in directory_metas
                and (not (harness / p).is_file() or digest(harness / p) != current[p]))
harness_editor = harness_project / 'Assets/Editor'
test_parity = sorted(p.name for p in (root / 'FluidGpuExperimentEvidence/HarnessSource').glob('*.cs')
                    if not (harness_editor / p.name).is_file() or digest(harness_editor / p.name) != digest(p))

protected = sorted(p for p in baseline
                   if p.startswith(('Shaders/', 'Art/', 'Data/', 'Prefabs/', 'Scenes/'))
                   and p not in allowed_changed)
protected_changed = sorted(p for p in protected if current.get(p) != baseline[p])
protected_groups = {
    category: dict(count=sum(p.startswith(prefix) for p in protected),
                   unchanged=all(current.get(p) == baseline[p] for p in protected if p.startswith(prefix)))
    for category, prefix in [('shaders', 'Shaders/'), ('scene', 'Scenes/'), ('originalArt', 'Art/'),
                             ('profilesAndSettings', 'Data/'), ('otherPrefabs', 'Prefabs/')]
}

# Every new asset and folder must be accompanied by a stable Unity GUID.
missing_meta = sorted(p for p in new_assets | new_folders if not (feature / (p + '.meta')).is_file())
orphan_meta = sorted(p for p in added if p.endswith('.meta') and not (feature / p[:-5]).exists())
invalid_meta = []
for path in new_assets | new_folders:
    metadata = feature / (path + '.meta')
    if metadata.is_file() and not re.search(r'^guid: [a-f0-9]{32}$', metadata.read_text(encoding='utf-8-sig'), re.M):
        invalid_meta.append(path + '.meta')

# Scan the actual Assets tree, so a new asset cannot silently reuse a GUID outside this feature.
guid_paths = defaultdict(list)
for meta in (root / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([a-f0-9]{32})$', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
    if match:
        guid_paths[match.group(1)].append(meta.relative_to(root).as_posix())
duplicate_guids = {guid: sorted(paths) for guid, paths in guid_paths.items() if len(paths) > 1}

manifest_path = root / 'FluidGpuExperimentEvidence/hurricane-shard-source-manifest.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
expected_names = {f'hurricane_shard_{i:02d}.png' for i in range(1, 17)}
manifest_names = [item['name'] for item in manifest]
manifest_complete = len(manifest_names) == 16 and set(manifest_names) == expected_names
source_mismatches = []
for entry in manifest:
    asset = feature / 'Art/Shards/Hurricane' / entry['name']
    if not asset.is_file() or digest(asset) != entry['sha256']:
        source_mismatches.append(entry['name'])
actual_png_names = {p.name for p in (feature / 'Art/Shards/Hurricane').glob('*.png')}
png_set_matches = actual_png_names == expected_names

status = (len(baseline) == 250 and not missing and not unexpected_changed and not unexpected_added
          and not missing_expected_assets and not parity and not test_parity and not protected_changed
          and not missing_meta and not orphan_meta and not invalid_meta and not duplicate_guids
          and manifest_complete and not source_mismatches and png_set_matches)
report = dict(
    passed=status, baselineFileCount=len(baseline), currentFileCount=len(current), changed=changed,
    added=added, missing=missing, unexpectedChanged=unexpected_changed, unexpectedAdded=unexpected_added,
    missingExpectedNewAssets=missing_expected_assets, harnessMismatches=parity, harnessTestMismatches=test_parity,
    protectedAssetCount=len(protected), protectedAssetsUnchanged=not protected_changed,
    protectedAssetMismatches=protected_changed, protectedGroups=protected_groups,
    newAssetsMissingMeta=missing_meta, newOrphanMetas=orphan_meta, invalidNewMetas=sorted(invalid_meta),
    repositoryAssetGuidCount=len(guid_paths), duplicateAssetGuids=duplicate_guids,
    sourceManifestComplete=manifest_complete, suppliedPngHashMismatches=source_mismatches,
    exactly16ShardPngs=png_set_matches, disposableDirectoryMetasExcluded=sorted(directory_metas),
    baselineSha256=baseline, currentSha256=current,
    scope='F hurricane breakage only; compare the pre-request working copy, original PNG hashes, and harness bytes. '
          'Protected settings are the feature Data assets present in that snapshot; no baseline claim about external ProjectSettings.')
out = root / 'FluidGpuExperimentEvidence/glass-breakage-preservation-audit.json'
out.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps({k: v for k, v in report.items() if k not in ('baselineSha256', 'currentSha256')}, indent=2))
raise SystemExit(0 if status else 1)
