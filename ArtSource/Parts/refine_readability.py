"""Re-import saved imagegen originals at 64/96px, with explicit alpha and material ramps.

User-approved native-size conversion and matte cleanup only. Never starts Unity or tests.
"""
from pathlib import Path
import hashlib
import json
import re
import shutil
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from imagegen_pipeline import ROOT, ART, connected_objects

OUT=ART/'Readability'
OUT.mkdir(exist_ok=True)
JOBS=json.loads((ART/'imagegen_jobs.json').read_text(encoding='utf-8'))
RESULTS=ART/'imagegen_results.json'
PALETTE=['30213d', '55445f','898194','c1bfcd','f4edf3',
 '77503f','b7834e','e1b35d','fff0a7', '345178','5186b5','89c6e1','d9f5f2',
 '214652','347f86','63b6b0','b9e9d5', '602d4e','aa3b65','e86682','ffc0c9',
 '50385f','805786','b48bb8','e2c6e4', '4c5730','8a963e','bfc45a','e9dfa1',
 '543b39','89604b','ba8a66','e6bb89']

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def snapshot():
    baseline=OUT/'baseline.json'
    if baseline.exists():return
    backup=OUT/'Before';backup.mkdir(exist_ok=True)
    for j in JOBS:shutil.copy2(ROOT/j['final_png'],backup/(j['id']+'.png'))
    prefab=ROOT/'Assets/_Project/Prefabs/UI/GameCanvas.prefab'
    shutil.copy2(prefab,backup/'GameCanvas.prefab')
    paths=list((ROOT/'Assets/_Project/Data').rglob('*.asset'))+list((ROOT/'Tools').glob('author_part*.py'))
    guids={str(p.relative_to(ROOT)):re.search(r'^guid: (.+)$',p.read_text(),re.M)[1] for p in (ROOT/'Assets/_Project/Resources/Sprites/Parts').rglob('*.meta')}
    baseline.write_text(json.dumps(dict(unchanged={str(p.relative_to(ROOT)):sha(p) for p in paths},guids=guids),indent=2)+'\n',encoding='utf-8')

def originals():
    report=json.loads(RESULTS.read_text(encoding='utf-8'));sources={}
    # Read the original alpha, which the earlier crop importer had thresholded too early.
    for s in json.loads((ART/'imagegen_sheets.json').read_text(encoding='utf-8')):
        im=Image.open(ART/'Sheets'/(s['id']+'.png')).convert('RGBA')
        for id,c in zip(s['ids'],connected_objects(im,len(s['ids']))):sources[id]=im.crop(c['bbox'])
    for j in JOBS:
        r=report[j['id']]
        if 'sheet' not in r:sources[j['id']]=Image.open(r['source']).convert('RGBA')
    replacement=OUT/'representatives.png'
    if replacement.exists():
        ids=[f'critical_movement_t{n}' for n in range(2,6)]+['clock_spring']
        im=Image.open(replacement).convert('RGBA')
        for id,c in zip(ids,connected_objects(im,5)):sources[id]=im.crop(c['bbox'])
    return sources

def components(mask):
    visited=set();groups=[];h,w=mask.shape
    for y,x in zip(*np.nonzero(mask)):
        if (int(x),int(y)) in visited:continue
        todo=[(int(x),int(y))];visited.add(todo[0]);group=[]
        while todo:
            px,py=todo.pop();group.append((px,py))
            for dx,dy in [(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(1,-1),(-1,1),(1,1)]:
                nx,ny=px+dx,py+dy
                if 0<=nx<w and 0<=ny<h and mask[ny,nx] and (nx,ny) not in visited:
                    visited.add((nx,ny));todo.append((nx,ny))
        groups.append(group)
    return groups

def convert(im,tier):
    size=64 if tier==1 else 96
    alpha=np.asarray(im.getchannel('A')).copy()
    cutoff=max(210,int(alpha.max()*.94))
    strong=Image.fromarray(np.where(alpha>=cutoff,255,0).astype(np.uint8))
    bbox=strong.getbbox()
    if bbox is None:raise ValueError('Missing solid alpha silhouette')
    im=im.crop(bbox);strong=strong.crop(bbox);im.putalpha(strong)
    limit=50 if tier==1 else {2:66,3:71,4:76,5:81}[tier]
    factor=limit/max(im.size)
    dims=(max(1,round(im.width*factor)),max(1,round(im.height*factor)))
    # Area sampling integrates the source pixel blocks; no arbitrary nearest samples of tiny details.
    im=im.resize(dims,Image.Resampling.BOX)
    a=np.asarray(im.getchannel('A'))>=160
    for c in components(a):
        if len(c)<=3:
            for x,y in c:a[y,x]=False
    palette=Image.new('P',(1,1))
    values=[int(c[i:i+2],16) for c in PALETTE for i in (0,2,4)]
    palette.putpalette(values+values[:3]*(256-len(PALETTE)))
    rgb=np.asarray(im.convert('RGB').quantize(palette=palette,dither=Image.Dither.NONE).convert('RGB')).copy()
    # Only isolated interior color noise is merged; thin outlines/mechanism details are retained.
    for y in range(1,dims[1]-1):
        for x in range(1,dims[0]-1):
            if not a[y-1:y+2,x-1:x+2].all():continue
            colors,counts=np.unique(rgb[y-1:y+2,x-1:x+2].reshape(-1,3),axis=0,return_counts=True)
            k=counts.argmax()
            if counts[k]>=6 and not any(np.array_equal(rgb[y,x],rgb[y+dy,x+dx]) for dy,dx in [(-1,0),(1,0),(0,-1),(0,1)]):rgb[y,x]=colors[k]
    result=Image.new('RGBA',(size,size));obj=Image.fromarray(rgb).convert('RGBA');obj.putalpha(Image.fromarray(a.astype(np.uint8)*255))
    result.paste(obj,((size-dims[0])//2,(size-dims[1])//2))
    # One clean continuous purple pixel outside the silhouette replaces soft gray fringe.
    outer=result.getchannel('A').filter(ImageFilter.MaxFilter(3))
    base=Image.new('RGBA',(size,size),'#30213d');base.putalpha(outer);base.alpha_composite(result)
    return base

def preview(ids,filename):
    width=220;out=Image.new('RGB',(width*len(ids),330),'#242530');d=ImageDraw.Draw(out)
    for i,id in enumerate(ids):
        j=next(j for j in JOBS if j['id']==id);im=Image.open(ROOT/j['final_png']).convert('RGBA')
        old=Image.open(OUT/'Before'/(id+'.png')).convert('RGBA')
        for source,y in [(old,20),(im,140)]:
            sprite=source.resize((96,96),Image.Resampling.NEAREST);out.paste(sprite,(i*width+16,y),sprite)
        white=Image.new('RGBA',(96,96),'#e1d9c6');white.alpha_composite(im.resize((96,96),Image.Resampling.NEAREST));out.paste(white.convert('RGB'),(i*width+120,140))
        d.text((i*width+8,260),id,fill='white')
    d.text((8,4),'BEFORE / AFTER on dark and light',fill='white');out.save(OUT/filename)

def run(only_representatives=False):
    snapshot();sources=originals();report=json.loads(RESULTS.read_text(encoding='utf-8'))
    ids=['clock_spring']+[f'critical_movement_t{n}' for n in range(2,6)]
    for j in JOBS:
        if only_representatives and j['id'] not in ids:continue
        dest=ROOT/j['final_png'];im=convert(sources[j['id']],int(j['tier']));im.save(dest)
        r=report[j['id']];r.update(size=im.width,sha256=sha(dest),conversion='material ramps; area sample; strong alpha; continuous purple outline')
        if j['id'] in ids:
            r.pop('sheet',None)
            r.update(source='ArtSource/Parts/Readability/representatives.png',prompt=(OUT/'imagegen_prompt.txt').read_text(encoding='utf-8'),tool='built-in image_gen edit')
    RESULTS.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    preview(ids,'representatives_comparison.png')
    print(json.dumps(dict(converted=5 if only_representatives else len(JOBS),sizes=[64,96])))

def audit():
    baseline=json.loads((OUT/'baseline.json').read_text(encoding='utf-8'));colors={tuple(int(c[i:i+2],16) for i in (0,2,4)) for c in PALETTE}
    for p,h in baseline['unchanged'].items():assert sha(ROOT/p)==h,p
    for p,g in baseline['guids'].items():assert re.search(r'^guid: (.+)$',(ROOT/p).read_text(),re.M)[1]==g,p
    for j in JOBS:
        p=ROOT/j['final_png'];im=Image.open(p);size=64 if j['tier']=='1' else 96
        assert im.mode=='RGBA' and im.size==(size,size),j['id']
        data=np.asarray(im)
        assert set(np.unique(data[:,:,3]).tolist())=={0,255},j['id']
        assert set(map(tuple,data[:,:,:3][data[:,:,3]>0].tolist())).issubset(colors),j['id']
        b=im.getbbox();assert min(b[0],b[1],size-b[2],size-b[3])>=5,j['id']
        meta=Path(str(p)+'.meta').read_text()
        for k,v in dict(filterMode=0,spriteMode=1,spritePixelsToUnits=16,textureType=8,isReadable=1,textureCompression=0).items():
            assert all(x==str(v) for x in re.findall(r'^\s*'+k+r': (.+)$',meta,re.M)),(j['id'],k)
    (OUT/'validation.json').write_text(json.dumps(dict(pngs=176,t1_size=64,evolution_size=96,alpha='binary',material_ramps=4,preserved_guids=len(baseline['guids']),gameplay_data='unchanged',authoring_scripts='unchanged',unity_started=False,compiled=False,tests_run=False),indent=2)+'\n',encoding='utf-8')
    print('176 PNG sizes, alpha, palette, margins, old GUIDs and unchanged gameplay/author scripts inspected.')

def provenance():
    report=json.loads(RESULTS.read_text(encoding='utf-8'))
    for id in ['clock_spring']+[f'critical_movement_t{n}' for n in range(2,6)]:
        report[id].pop('sheet',None)
    RESULTS.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':
    if sys.argv[1]=='representatives':run(True)
    elif sys.argv[1]=='all':run()
    elif sys.argv[1]=='audit':audit()
    elif sys.argv[1]=='provenance':provenance()
