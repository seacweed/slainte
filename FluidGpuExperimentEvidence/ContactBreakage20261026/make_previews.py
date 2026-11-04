"""Package actual native/GPU contact replay; no synthetic particles or frames."""
from pathlib import Path
import hashlib,json
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
attempt=HERE.parent/'20261026-contact-final-contacts'
info=json.loads((attempt/'exit.json').read_text(encoding='utf-8-sig'))
assert info['exitCode']==0 and not info['timedOut']
manifest={}
for prefix,name in [('pool-','pool.gif'),('break-','break.gif'),('intact-','intact.gif'),('bottle-right-','bottle-right.gif')]:
    paths=sorted(attempt.glob(prefix+'*.png'));assert len(paths)>=20
    frames=[]
    for p in paths:
        with Image.open(p) as im:frames.append(im.copy())
    output=HERE/name
    frames[0].save(output,save_all=True,append_images=frames[1:],duration=40,loop=0,optimize=True)
    manifest[name]=dict(sourceAttempt=attempt.name,sourceFrames=[p.name for p in paths],frames=len(paths),
                        frameIntervalSeconds=.04,sha256=hashlib.sha256(output.read_bytes()).hexdigest())
intact=sorted(attempt.glob('intact-*.png'));broken=sorted(attempt.glob('break-*.png'))
assert len(intact)==len(broken)
frames=[]
for a,b in zip(intact,broken):
    frame=Image.new('RGB',(1280,512),(16,20,24));draw=ImageDraw.Draw(frame)
    draw.text((16,8),'INTACT control',fill='white');draw.text((656,8),'BREAKABLE: physical contacts, no fan',fill='white')
    with Image.open(a) as im:frame.paste(im.resize((640,480)),(0,32))
    with Image.open(b) as im:frame.paste(im.resize((640,480)),(640,32))
    frames.append(frame)
output=HERE/'intact-vs-break.gif'
frames[0].save(output,save_all=True,append_images=frames[1:],duration=40,loop=0,optimize=True)
manifest[output.name]=dict(sourceAttempt=attempt.name,intactFrames=[p.name for p in intact],brokenFrames=[p.name for p in broken],
    frames=len(frames),frameIntervalSeconds=.04,sha256=hashlib.sha256(output.read_bytes()).hexdigest())
(HERE/'motion-previews.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print({name:value['frames'] for name,value in manifest.items()})
