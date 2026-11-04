"""Compare actual OFF/ON replay frames; no generated motion or extra particles."""
from pathlib import Path
import hashlib,json
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
attempt=HERE.parent/'20261026-visible-splash-final-breakage'
status=json.loads((attempt/'exit.json').read_text(encoding='utf-8-sig'))
assert status['exitCode']==0 and not status['timedOut']
manifest={}
def paths(prefix):
    items=sorted(p for p in attempt.glob(prefix+'*.png') if p.stem[-3:].isdigit())
    assert len(items)==20
    return items
for mode in ('FCoherentLiquid','GExplosiveBreakage'):
    off,on=paths(mode+'-off-'),paths(mode+'-right-')
    frames=[]
    for a,b in zip(off,on):
        frame=Image.new('RGB',(1280,512),(16,20,24));draw=ImageDraw.Draw(frame)
        draw.text((16,8),'OFF: existing liquid physics',fill='white')
        draw.text((656,8),'ON: impact splash',fill='white')
        with Image.open(a) as im:frame.paste(im.resize((640,480)),(0,32))
        with Image.open(b) as im:frame.paste(im.resize((640,480)),(640,32))
        frames.append(frame)
    output=HERE/(mode[0].lower()+'-off-on.gif')
    frames[0].save(output,save_all=True,append_images=frames[1:],duration=40,loop=0,optimize=True)
    manifest[output.name]=dict(sourceAttempt=attempt.name,offFrames=[p.name for p in off],onFrames=[p.name for p in on],
        frames=len(frames),frameIntervalSeconds=.04,sha256=hashlib.sha256(output.read_bytes()).hexdigest())
for prefix,name in [('floor-splash-','floor.gif'),('FCoherentLiquid-right-','bottle-f.gif')]:
    items=paths(prefix);frames=[]
    for p in items:
        with Image.open(p) as im:frames.append(im.copy())
    output=HERE/name
    frames[0].save(output,save_all=True,append_images=frames[1:],duration=40,loop=0,optimize=True)
    manifest[name]=dict(sourceAttempt=attempt.name,sourceFrames=[p.name for p in items],
        frames=len(frames),frameIntervalSeconds=.04,sha256=hashlib.sha256(output.read_bytes()).hexdigest())
(HERE/'motion-previews.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print({name:info['frames'] for name,info in manifest.items()})
