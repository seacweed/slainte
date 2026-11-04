"""Prepare the current F-only harness without restoring retired comparison code."""
from pathlib import Path
import argparse
import hashlib
import json
import shutil

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
FEATURE = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--target', type=Path, default=ROOT / 'Library/FluidModelDHarness')
args = parser.parse_args()
target = args.target.resolve()
if not target.is_relative_to((ROOT / 'Library').resolve()) or target == (ROOT / 'Library').resolve():
    raise SystemExit('Disposable target must be a child of this workspace Library.')
target.mkdir(parents=True, exist_ok=True)
for relative in (FEATURE, Path('Assets/Editor'), Path('Assets/DevelopmentClient')):
    destination = (target / relative).resolve()
    if not destination.is_relative_to(target):
        raise SystemExit('Invalid disposable subfolder: ' + str(destination))
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
final = {}
dependencies = json.loads((HERE / 'shared-dependencies-sha256.json').read_text(encoding='utf-8'))
for relative, digest in dependencies.items():
    source = ROOT / relative
    if hashlib.sha256(source.read_bytes()).hexdigest() != digest:
        raise SystemExit('Shared dependency changed: ' + relative)
    destination = target / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    final[relative] = digest
for source in (ROOT / FEATURE).rglob('*'):
    if source.is_file():
        relative = source.relative_to(ROOT)
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        final[relative.as_posix()] = hashlib.sha256(source.read_bytes()).hexdigest()
for source in (HERE / 'Validation').glob('*.cs'):
    shutil.copy2(source, target / 'Assets/Editor' / source.name)
for source in (HERE.parent / 'DevelopmentClient').glob('*.cs'):
    shutil.copy2(source, target / 'Assets/DevelopmentClient' / source.name)
(target / 'Packages').mkdir(exist_ok=True)
shutil.copy2(HERE / 'Validation/manifest.json', target / 'Packages/manifest.json')
(target / 'ProjectSettings').mkdir(exist_ok=True)
for name in ('ProjectVersion.txt', 'ProjectSettings.asset', 'GraphicsSettings.asset', 'QualitySettings.asset',
             'TimeManager.asset', 'TagManager.asset', 'Physics2DSettings.asset'):
    shutil.copy2(ROOT / 'ProjectSettings' / name, target / 'ProjectSettings' / name)
(target / 'UserSettings').mkdir(exist_ok=True)
(target / 'UserSettings/Search.settings').write_text('{"indexOnEditorStartup":false}\n')
(target / 'source-sha256.json').write_text(json.dumps(final, indent=2), encoding='utf-8')
print('Prepared current F harness:', len(final), 'source/dependency files at', target)
