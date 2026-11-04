"""Encode actual Unity camera frames; retain their source and timing."""
from pathlib import Path
import hashlib
import json
from PIL import Image

HERE = Path(__file__).resolve().parent
attempt = HERE.parent/'20261022-cord-verified-dispenser'
status = json.loads((attempt/'exit.json').read_text(encoding='utf-8-sig'))
assert status['exitCode']==0 and not status['timedOut']
manifest = {}
for prefix, target in [('cord-sag-','slack-preview.gif'),('cord-held-','held-preview.gif'),
                       ('reel-','reel-preview.gif'),('hang-','hanging-preview.gif')]:
    paths = sorted(attempt.glob(prefix+'*.png'))
    assert len(paths)>10
    images = []
    for p in paths:
        with Image.open(p) as im: images.append(im.copy())
    output = HERE/target
    images[0].save(output,save_all=True,append_images=images[1:],duration=100,loop=0,optimize=True)
    manifest[target] = dict(sourceAttempt=attempt.name,sourceFrames=[p.name for p in paths],
                            frames=len(paths),frameIntervalSeconds=.1,sha256=hashlib.sha256(output.read_bytes()).hexdigest())
    print(target,len(paths),'frames',output.stat().st_size,'bytes')
(HERE/'motion-previews.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
