"""Package actual rendered physics frames into 10fps previews; no synthetic frames."""
from pathlib import Path
import hashlib
import json
from PIL import Image

here=Path(__file__).resolve().parent
attempt=here.parent/'20261022-tether-verified-dispenser'
exit_info=json.loads((attempt/'exit.json').read_text(encoding='utf-8-sig'))
assert exit_info['exitCode']==0 and not exit_info['timedOut']
manifest={}
for prefix,name in [('reel-','reel-preview.gif'),('hang-','hanging-preview.gif')]:
    paths=sorted(attempt.glob(prefix+'*.png'))
    assert len(paths)>=12,paths
    images=[]
    for p in paths:
        with Image.open(p) as im:images.append(im.copy())
    target=here/name
    images[0].save(target,save_all=True,append_images=images[1:],duration=100,loop=0,optimize=True)
    manifest[name]={'frames':len(paths),'frameIntervalSeconds':.1,
                    'sourceAttempt':attempt.name,'sourceFrames':[p.name for p in paths],
                    'sha256':hashlib.sha256(target.read_bytes()).hexdigest()}
    print(name,len(paths),'frames',target.stat().st_size,'bytes')
(here/'motion-previews.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
