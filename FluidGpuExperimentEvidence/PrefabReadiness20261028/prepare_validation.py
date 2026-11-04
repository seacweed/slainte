"""Synchronize only the disposable validation checkout, retaining its Unity cache."""
import hashlib
import json
from pathlib import Path
import argparse
import re
import shutil

ROOT = Path(__file__).resolve().parents[2]
FEATURE = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
HARNESS = ROOT / 'Library/FluidModelDHarness'
parser = argparse.ArgumentParser()
parser.add_argument('--without-glass-ids', action='store_true')
args = parser.parse_args()
if not (HARNESS / 'ProjectSettings/ProjectVersion.txt').is_file():
    raise SystemExit('Existing disposable Unity harness is required')
manifest = {}
for source in (ROOT / FEATURE).rglob('*'):
    if not source.is_file():
        continue
    relative = source.relative_to(ROOT)
    target = HARNESS / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)
    if args.without_glass_ids and source.parent.name == 'Prefabs' and source.name.startswith('Glass_'):
        original_hash = json.loads(Path(__file__).with_name('before-sha256.json').read_text(encoding='utf-8'))[relative.as_posix()]
        raw = re.sub(rb'^  servingGlassId: [^\r\n]*\r?\n', b'', target.read_bytes(), flags=re.M)
        lf = raw.replace(b'\r\n', b'\n')
        candidates = (raw, lf, lf.replace(b'\n', b'\r\n'))
        original = next((value for value in candidates if hashlib.sha256(value).hexdigest() == original_hash), None)
        if original is None:
            raise SystemExit('Could not reconstruct original glass prefab: ' + str(source))
        target.write_bytes(original)
    manifest[relative.as_posix()] = hashlib.sha256(target.read_bytes()).hexdigest()
for source in (ROOT / 'FluidGpuExperimentEvidence/HarnessSource').glob('*.cs'):
    shutil.copyfile(source, HARNESS / 'Assets/Editor' / source.name)
shutil.copyfile(Path(__file__).with_name('PrefabReadinessValidation.cs'), HARNESS / 'Assets/Editor/PrefabReadinessValidation.cs')
(HARNESS / 'source-sha256.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(json.dumps({'feature_files_synchronized': len(manifest), 'project': str(HARNESS), 'original_glass_prefabs': args.without_glass_ids}))
