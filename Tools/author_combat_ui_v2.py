"""Offline pixel asset authoring/import. Game only loads the resulting PNGs and registered library."""
from pathlib import Path
import sys, re, hashlib, shutil, json, math
sys.dont_write_bytecode=True
from PIL import Image, ImageDraw
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'ArtSource/CombatUiV2';UI=ROOT/'Assets/_Project/UI/Frames';FX=ROOT/'Assets/_Project/Resources/Sprites/Combat'

def write(path,text):
    path.parent.mkdir(parents=True,exist_ok=True)
    with path.open('w',encoding='utf-8',newline='\r\n') as f:f.write(text)
def guid(path):
    meta=Path(str(path)+'.meta')
    if meta.exists():return re.search(r'^guid: (\w+)',meta.read_text(encoding='utf-8'),re.M)[1]
    value=hashlib.md5(('CombatUiV2:'+str(path.relative_to(ROOT))).encode()).hexdigest()
    write(meta,'fileFormatVersion: 2\nguid: '+value+'\n');return value
def save(path,im,border=0,ppu=16):
    path.parent.mkdir(parents=True,exist_ok=True);im.save(path);key=guid(path)
    meta=(UI/'slot.png.meta').read_text(encoding='utf-8')
    for pattern,value in [(r'^guid: .*','guid: '+key),(r'spriteID: \w+','spriteID: '+hashlib.md5((key+'sprite').encode()).hexdigest()),(r'spriteBorder: .*',f'spriteBorder: {{x: {border}, y: {border}, z: {border}, w: {border}}}'),(r'spritePixelsToUnits: \d+','spritePixelsToUnits: '+str(ppu)),(r'isReadable: \d+','isReadable: 1'),(r'textureCompression: \d+','textureCompression: 0')]:meta=re.sub(pattern,value,meta,flags=re.M)
    write(Path(str(path)+'.meta'),meta);return key
def ui():
    palettes={'equipment':('#222937','#5c6b7a','#a0adbb'),'detail':('#302937','#766375','#b6a0a7'),'workshop':('#30262a','#735342','#bb9462'),'tray':('#262330','#52495e','#9b8aa8'),'satchel_panel':('#302937','#766375','#b6a0a7')}
    for name,(fill,edge,light) in palettes.items():
        im=Image.new('RGBA',(32,32));d=ImageDraw.Draw(im)
        d.polygon([(3,0),(28,0),(31,3),(31,28),(28,31),(3,31),(0,28),(0,3)],fill='#171622')
        d.rectangle((2,2,29,29),fill=edge);d.rectangle((3,3,28,28),fill=fill)
        d.line((4,2,27,2),fill=light)
        for x,y in [(3,3),(26,3),(3,26),(26,26)]:
            if name in ('equipment','satchel_panel'):d.rectangle((x,y,x+2,y+2),fill=edge);d.point((x,y),fill=light)
        if name=='detail':d.rectangle((26,2,28,7),fill='#bb9462')
        if name=='workshop':d.line((2,27,29,27),fill='#4c3734');d.point((4,4),fill='#c2a570')
        if name=='tray':d.line((3,27,28,27),fill='#191823')
        save(UI/(name+'.png'),im,8,100)
    # A single rim and inset face. No nested slot frames.
    im=Image.new('RGBA',(32,32));d=ImageDraw.Draw(im)
    d.rectangle((0,0,31,31),fill='#171724');d.rectangle((1,1,30,30),fill='#393746');d.rectangle((2,2,29,29),fill='#272636')
    d.line((2,1,29,1),fill='#696071');d.point((1,2),fill='#696071');save(UI/'satchel_slot.png',im,3,100)
    im=Image.new('RGBA',(32,32));d=ImageDraw.Draw(im)
    for x,y,sx,sy in [(1,1,1,1),(30,1,-1,1),(1,30,1,-1),(30,30,-1,-1)]:
        d.line((x,y,x+sx*5,y),fill='#ffdf9a',width=1);d.line((x,y,x,y+sy*5),fill='#ffdf9a',width=1)
    save(UI/'satchel_selected.png',im,7,100)
    # Keep the approved ImageGen satchel as the actual bag, not a procedural replacement frame.
    source=Image.open(ROOT/'ArtSource/InventoryPolish/satchel_imagegen.png').convert('RGBA')
    body=source.crop((104,196,1299,1073)).resize((128,96),Image.Resampling.NEAREST)
    alpha=body.getchannel('A').point(lambda a:255 if a>=128 else 0)
    body=body.convert('RGB').quantize(colors=14,method=Image.Quantize.MEDIANCUT).convert('RGBA');body.putalpha(alpha)
    ImageDraw.Draw(body).rectangle((9,17,118,87),fill='#292336')
    save(UI/'satchel_body.png',body,24,100)
    handle=source.crop((500,20,906,196)).resize((20,10),Image.Resampling.NEAREST)
    alpha=handle.getchannel('A').point(lambda a:255 if a>=128 else 0)
    handle=handle.convert('RGB').quantize(colors=10,method=Image.Quantize.MEDIANCUT).convert('RGBA');handle.putalpha(alpha)
    save(UI/'satchel_handle.png',handle,0,100)
    im=Image.new('RGBA',(32,20));d=ImageDraw.Draw(im);d.rounded_rectangle((0,0,31,19),radius=2,fill='#1d1925');d.rectangle((1,1,30,18),fill='#7e6570');d.rectangle((2,2,29,17),fill='#4b3d48');d.line((3,2,28,2),fill='#b89a95')
    save(UI/'satchel_button.png',im,4,100)

def import_atlas():
    src=Path(r'C:\Users\woohy\.codex\generated_images\01a1b11c-f0af-7601-877e-64534005609e\exec-39bd6832-ff0d-499f-9757-4d6ae8de78da.png')
    dest=OUT/'combat_imagegen.png'
    if dest.exists():src=dest
    else:shutil.copy2(src,dest)
    sheet=Image.open(src).convert('RGBA');w,h=sheet.width//6,sheet.height//4
    names=['slash','contact','fire','earth'];result={}
    for row,name in enumerate(names):
        frames=[]
        for f in range(6):
            cell=sheet.crop((f*w,row*h,(f+1)*w,(row+1)*h));a=np.asarray(cell).copy();mask=a[:,:,3]>=200
            a[:,:,3]=np.where(mask,255,0);cell=Image.fromarray(a)
            # Coverage import, common registered canvas and palette for the entire row.
            coverageFloat=np.asarray(Image.fromarray(mask.astype('float32')).resize((64,64),Image.Resampling.BOX))
            rgbArray=np.zeros((64,64,3),dtype='uint8')
            for channel in range(3):
                weighted=np.asarray(Image.fromarray(a[:,:,channel].astype('float32')*mask).resize((64,64),Image.Resampling.BOX))
                rgbArray[:,:,channel]=np.clip(weighted/np.maximum(.001,coverageFloat),0,255).astype('uint8')
            rgb=Image.fromarray(rgbArray);rgb.putalpha(Image.fromarray(np.where(coverageFloat>=.18,255,0).astype('uint8')))
            if name=='earth':
                registered=Image.new('RGBA',(64,64));registered.alpha_composite(rgb,(0,-12));rgb=registered
            frames.append(rgb)
        colors=np.concatenate([np.asarray(im)[:,:,:3][np.asarray(im)[:,:,3]>0] for im in frames])
        joined=Image.fromarray(colors.reshape(1,-1,3))
        palette=joined.quantize(colors=14,method=Image.Quantize.MEDIANCUT)
        ids=[]
        for f,im in enumerate(frames):
            clean=im.convert('RGB').quantize(palette=palette,dither=Image.Dither.NONE).convert('RGBA');clean.putalpha(im.getchannel('A'))
            ids.append(save(FX/f'{name}_{f}.png',clean))
        result[name]=ids
    return result

def accents(result):
    # Authored into PNGs offline: the running game never rasterizes these curves or particles.
    for name in ['cut','thrust','ring','ice','arcane','shards','trail','bolt']:
        ids=[]
        for f in range(6):
            im=Image.new('RGBA',(64,64));d=ImageDraw.Draw(im);t=f/5
            if name=='cut':
                length=[3,14,27,21,10,2][f];head=36+round(t*12);d.line((head-length,26+f,head,26+f),fill='#eee9d5',width=1);d.line((head-length+3,28+f,head-2,28+f),fill='#9aa6bd',width=1)
            elif name=='thrust':
                end=[12,32,56,58,58,58][f];tail=[6,6,6,22,42,55][f]
                if end>tail:d.polygon([(tail,30),(end-3,30),(end,32),(end-3,33),(tail,33)],fill='#acaec0');d.line((tail,31,end,31),fill='#fff1cf')
            elif name=='trail':
                d.line((12+f*3,31,31,31),fill='#c7dce0',width=1)
                if f<4:d.line((18+f*2,33,28,33),fill='#7b9baa',width=1)
            elif name=='bolt':
                # A small flying body, not a badge, at the shared centre. Convert to a 16px native cell below.
                d.polygon([(23,29),(33,27),(43,31),(33,35),(23,33),(29,31)],fill='#658599')
                d.polygon([(28,30),(35,29),(41,31),(34,33),(28,32)],fill='#d8eef0')
            elif name=='ring':
                r=6+f*4;d.arc((32-r,32-r*.45,32+r,32+r*.45),5,174,fill='#b2a5b8',width=1);d.arc((32-r,32-r*.45,32+r,32+r*.45),202,332,fill='#e3d6ca',width=1)
            elif name=='shards':
                for i in range(5):
                    ang=i*1.25+.4;dist=3+f*4;x=32+round(math.cos(ang)*dist);y=32+round(math.sin(ang)*dist*.6);size=max(1,4-f//2)
                    d.polygon([(x,y-size),(x+size,y),(x,y+size),(x-1,y)],fill='#b5a4a7' if i%2 else '#ddd0bc')
            else:
                colors=('#d3f2f3','#82bbd7','#5b789e') if name=='ice' else ('#ece0ff','#b092d0','#6a5489')
                for i in range(5):
                    ang=i*math.tau/5;dist=3+f*3;x=32+round(math.cos(ang)*dist);y=32+round(math.sin(ang)*dist*.7);r=max(1,7-abs(f-2)*2)
                    if name=='ice':d.polygon([(x,y-r),(x+r//2,y),(x,y+r),(x-r//2,y)],fill=colors[i%3])
                    else:d.arc((x-r,y-r,x+r,y+r),30+f*15,290+f*10,fill=colors[i%3],width=2)
            if name=='bolt':im=im.resize((16,16),Image.Resampling.NEAREST)
            ids.append(save(FX/f'{name}_{f}.png',im))
        result[name]=ids

def walls(result):
    src=Path(r'C:\Users\woohy\.codex\generated_images\01a1b11c-f0af-7601-877e-64534005609e\exec-d210eb6d-7a83-42e0-a37e-f06f4e23b12c.png')
    dest=OUT/'barriers_imagegen.png'
    if dest.exists():src=dest
    else:shutil.copy2(src,dest)
    sheet=Image.open(src).convert('RGBA');w,h=sheet.width//2,sheet.height//2
    ids=[]
    for i in range(4):
        cell=sheet.crop((i%2*w,i//2*h,(i%2+1)*w,(i//2+1)*h));a=np.asarray(cell).copy();a[:,:,3]=np.where(a[:,:,3]>=210,255,0);cell=Image.fromarray(a);box=cell.getbbox();cell=cell.crop(box)
        nw=26 if i==1 else 56 if i>=2 else 72;nh=72 if i==1 else round(cell.height*nw/cell.width)
        cell=cell.resize((nw,nh),Image.Resampling.NEAREST)
        # Register the ground line to the sprite centre. Upright height is never rotated sideways.
        ground=2 if i==0 else 27 if i==1 else 11
        im=Image.new('RGBA',(96,96));ox=(96-nw)//2;oy=48-(nh-ground)
        if i>=2:
            for x in range(nw):
                # Project the baked diagonal's ground line onto a true 45-degree line, retaining upright posts.
                shift=round((x-nw/2)*(.57 if i==3 else -.57))
                im.alpha_composite(cell.crop((x,0,x+1,nh)),(ox+x,oy+shift))
        else:im.alpha_composite(cell,(ox,oy))
        alpha=im.getchannel('A');im=im.convert('RGB').quantize(colors=18,method=Image.Quantize.MEDIANCUT).convert('RGBA');im.putalpha(alpha)
        ids.append(save(FX/f'barrier_{i}.png',im))
    result['barriers']=ids

def library(result):
    path=ROOT/'Assets/_Project/Scripts/Combat/CombatVisualLibrary.cs';script=guid(path)
    data='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: '+script+', type: 3}\n  m_Name: CombatVisualLibrary\n  m_EditorClassIdentifier: \n'
    for name,ids in result.items():data+='  '+name+':\n'+''.join('  - {fileID: 21300000, guid: '+v+', type: 3}\n' for v in ids)
    data+='  framesPerSecond: 30\n  contactScale: 0.55\n  accentScale: 0.8\n  slashReferenceRadius: 24\n  trailStartDistance: 1.25\n  barrierReferenceLengths:\n  - 64\n  - 52\n  - 68\n  - 68\n'
    asset=ROOT/'Assets/_Project/Resources/CombatVisualLibrary.asset'
    if asset.exists():
        # Preserve the skill and combat-cue fields authored outside this basic-art pass.
        retained = r'(?:skill\w+|cutHit|bluntHit|daggerHit|gunFlash|crystalHit|(?:arrow|bullet|magic|blade)Wake\w+|muzzleSeconds|cutSeconds|cutScale|basicSweep\w+|basicStrokeWeight|basicProjectileScale|basicMuzzlePixels|weaponContactScale|placement\w+)'
        data+=''.join(re.findall(r'^  '+retained+r':.*(?:\n  -[^\n]*)*\n',asset.read_text(encoding='utf-8'),re.M))
    write(asset,data);key=guid(asset)
    db=ROOT/'Assets/_Project/Resources/GameDatabase.asset';s=db.read_text(encoding='utf-8')
    line='  combatVisuals: {fileID: 11400000, guid: '+key+', type: 2}\n'
    if '  combatVisuals:' not in s:s+=line
    write(db,s)
    write(OUT/'asset_manifest.json',json.dumps(result,indent=2)+'\n')

if __name__=='__main__':
    OUT.mkdir(parents=True,exist_ok=True);ui();result=import_atlas();accents(result);walls(result);library(result)
    print('Authored 5 distinct UI panels/slots and registered native combat PNG sequences; original ImageGen sheets retained.')
