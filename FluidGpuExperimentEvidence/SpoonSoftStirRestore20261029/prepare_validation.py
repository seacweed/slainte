import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
FEATURE = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
TARGET = ROOT / 'Library/FluidModelDHarness'
manifest = json.loads((HERE.parent / 'CocktailIntegration20261028/10-authoring/source-sha256.json').read_text(encoding='utf-8-sig'))
final = {}
for relative, digest in manifest.items():
    if relative.startswith(FEATURE.as_posix() + '/'): continue
    source = ROOT / relative
    if hashlib.sha256(source.read_bytes()).hexdigest() != digest: raise SystemExit('Shared source changed: ' + relative)
    destination = TARGET / relative
    destination.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, destination)
    final[relative] = digest
for source in (ROOT / FEATURE).rglob('*'):
    if not source.is_file(): continue
    relative = source.relative_to(ROOT)
    destination = TARGET / relative
    destination.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, destination)
    final[relative.as_posix()] = hashlib.sha256(source.read_bytes()).hexdigest()
for source in HERE.glob('*Validation.cs'):
    shutil.copyfile(source, TARGET / 'Assets/Editor' / source.name)
(TARGET / 'source-sha256.json').write_text(json.dumps(final, indent=2), encoding='utf-8')
print('Synchronized final files:', len(final))
