import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
TARGET = ROOT / 'Library/FluidModelDHarness'
previous = ROOT / 'FluidGpuExperimentEvidence/CocktailIntegration20261028/10-authoring/source-sha256.json'
manifest = json.loads(previous.read_text(encoding='utf-8-sig'))
for relative, digest in manifest.items():
    source = ROOT / relative
    if hashlib.sha256(source.read_bytes()).hexdigest() != digest:
        raise SystemExit('Dependency changed since the verified integration: ' + relative)
    destination = TARGET / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
for source in HERE.glob('*Validation.cs'):
    shutil.copyfile(source, TARGET / 'Assets/Editor' / source.name)
(TARGET / 'source-sha256.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print('Prepared unchanged verified sources:', len(manifest))
