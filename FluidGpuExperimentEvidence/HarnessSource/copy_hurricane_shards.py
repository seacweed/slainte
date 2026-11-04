"""Copy the supplied hurricane shard PNGs byte-for-byte; do not resample their canvases."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import shutil
import uuid
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('source', type=Path)
args = parser.parse_args()
destination = FEATURE / 'Art/Shards/Hurricane'
template = (ROOT / 'Assets/_Project/Features/Bartending/Art/Glasses/hurricane_front_line.png.meta').read_text()

def guid(path):
    return uuid.uuid5(uuid.NAMESPACE_URL, 'slainte-fluid-experiment/' + path.relative_to(ROOT).as_posix()).hex

for folder in [destination.parent, destination]:
    folder.mkdir(parents=True, exist_ok=True)
    meta = Path(str(folder) + '.meta')
    if not meta.exists():
        meta.write_text(f'fileFormatVersion: 2\nguid: {guid(folder)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')

report = []
for number in range(1, 17):
    name = f'hurricane_shard_{number:02d}.png'
    source = args.source / name
    target = destination / name
    original = source.read_bytes()
    with Image.open(source) as image:
        assert image.size == (310, 590), (name, image.size)
        alpha = image.convert('RGBA').getchannel('A')
        alpha_min, alpha_max = alpha.getextrema()
        bounds = alpha.point(lambda value: 255 if value > 8 else 0).getbbox()
    if target.exists() and target.read_bytes() != original:
        raise RuntimeError(f'Refusing to replace an existing changed asset: {target}')
    if not target.exists():
        shutil.copy2(source, target)
    meta = Path(str(target) + '.meta')
    if not meta.exists():
        meta.write_text(re.sub(r'^guid: [a-f0-9]{32}', 'guid: ' + guid(target), template, count=1, flags=re.M))
    report.append(dict(name=name, sha256=hashlib.sha256(original).hexdigest(),
                       width=310, height=590, alpha_max=alpha_max, bounds=bounds,
                       copied_byte_identically=target.read_bytes() == original))

report_path = ROOT / 'FluidGpuExperimentEvidence/hurricane-shard-source-manifest.json'
report_path.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
