"""Actual native/GPU replay camera frames, no generated intermediate images."""
from pathlib import Path
import hashlib,json
from PIL import Image
HERE=Path(__file__).resolve().parent
attempt=HERE.parent/'20261026-splash-final-breakage'
info=json.loads((attempt/'exit.json').read_text(encoding='utf-8-sig'))
assert info['exitCode']==0 and not info['timedOut']
manifest={}
for prefix,name in [('FCoherentLiquid-right-','bottle-f.gif'),('GExplosiveBreakage-right-','bottle-g.gif'),('floor-splash-','floor.gif')]:
    paths=sorted(p for p in attempt.glob(prefix+'*.png') if p.stem[-3:].isdigit())
    assert len(paths)==20,len(paths)
    frames=[]
    for p in paths:
        with Image.open(p) as im:frames.append(im.copy())
    output=HERE/name
    frames[0].save(output,save_all=True,append_images=frames[1:],duration=40,loop=0,optimize=True)
    manifest[name]=dict(sourceAttempt=attempt.name,sourceFrames=[p.name for p in paths],
                        frames=len(paths),frameIntervalSeconds=.04,sha256=hashlib.sha256(output.read_bytes()).hexdigest())
    print(name,len(paths),'frames',output.stat().st_size,'bytes')
(HERE/'motion-previews.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
