"""Author/import native manual-skill motion. No Unity, API requests or gameplay data changes."""
from pathlib import Path
import sys, math, json, re
sys.dont_write_bytecode = True
from PIL import Image, ImageDraw
import numpy as np
from author_combat_ui_v2 import save, write, guid, ROOT, FX

OUT = ROOT / 'ArtSource/SkillFxV3'
N = 64
INK = '#352c48'
SHADE = '#635674'
MID = '#aa95bc'
LIGHT = '#dfd3e9'
WHITE = '#fff5e6'

def point(x, y, r, a, flat=1):
    return round(x+math.cos(a)*r), round(y+math.sin(a)*r*flat)

def stroke(d, pts, width=2, color=LIGHT):
    pts=[(round(x),round(y)) for x,y in pts]
    d.line(pts,fill=INK,width=width+2,joint='curve')
    d.line(pts,fill=color,width=width,joint='curve')

def arc(d, r, start, end, width=2, color=LIGHT, at=(32,32), flat=1):
    pts=[point(*at,r,math.radians(start+(end-start)*i/32),flat) for i in range(33)]
    stroke(d,pts,width,color)

def facet(d, x, y, r, a=0, color=MID):
    # Long bevelled fragments read as a material rather than floating dot particles.
    pts=[point(x,y,r,a),point(x,y,r*.46,a+1.5),point(x,y,r*.65,a+math.pi),point(x,y,r*.46,a-1.5)]
    d.polygon(pts,fill=INK);d.polygon([(x,y),pts[0],pts[1]],fill=color)
    d.polygon([(x,y),pts[0],pts[3]],fill=LIGHT);d.line([pts[0],(x,y)],fill=WHITE,width=1)

def generated():
    sheet=Image.open(OUT/'skill_motion_source.png').convert('RGBA')
    cw,ch=sheet.width//6,sheet.height//4
    result={}
    for row,name in enumerate(['skillSlash','skillSpear','skillArcane','skillFire']):
        cells=[]
        for f in range(6):
            cell=sheet.crop((f*cw,row*ch,(f+1)*cw,(row+1)*ch))
            pixels=np.array(cell);pixels[:,:,3]=np.where(pixels[:,:,3]>=190,255,0)
            cells.append(Image.fromarray(pixels))
        # Common row bounds and scale retain anticipation, follow-through and registered flight.
        boxes=[c.getbbox() for c in cells if c.getbbox()]
        left=min(b[0] for b in boxes);top=min(b[1] for b in boxes)
        right=max(b[2] for b in boxes);bottom=max(b[3] for b in boxes)
        pad=4;extent=max(right-left,bottom-top);scale=(N-pad*2)/extent
        w,h=round((right-left)*scale),round((bottom-top)*scale)
        frames=[]
        for cell in cells:
            cell=cell.crop((left,top,right,bottom)).resize((w,h),Image.Resampling.NEAREST)
            im=Image.new('RGBA',(N,N));im.alpha_composite(cell,((N-w)//2,(N-h)//2));frames.append(im)
        # Native integer pixels and a shared palette remove source anti-alias grain without flattening the material.
        colors=np.concatenate([np.asarray(im)[:,:,:3][np.asarray(im)[:,:,3]>0] for im in frames])
        palette=Image.fromarray(colors.reshape(1,-1,3)).quantize(colors=14,method=Image.Quantize.MEDIANCUT)
        result[name]=[]
        for im in frames:
            alpha=im.getchannel('A');im=im.convert('RGB').quantize(palette=palette,dither=Image.Dither.NONE).convert('RGBA');im.putalpha(alpha)
            result[name].append(im)
    return result

def authored(name,f):
    im=Image.new('RGBA',(N,N));d=ImageDraw.Draw(im);t=f/7
    strength=[.2,.65,1,1,.9,.65,.35,.12][f]
    if name in ['skillArrow','skillBullet','skillOrb','skillBlade']:
        # Flying rows loop with a fully present solid head in EVERY frame.
        bob=[0,0,1,1,0,0,-1,-1][f]
        if name=='skillArrow':
            stroke(d,[(15,32),(41,32)],2,MID)
            d.polygon([(50,32),(39,25),(35,32),(39,39)],fill=INK)
            d.polygon([(47,32),(39,27),(40,32)],fill=WHITE);d.polygon([(47,32),(40,32),(39,37)],fill=MID)
            d.polygon([(22,31),(14,26),(17,31),(13,34),(22,34)],fill=LIGHT)
            stroke(d,[(8,29+bob),(22,29)],1,SHADE)
        elif name=='skillBullet':
            d.polygon([(49,32),(42,27),(27,28),(23,32),(27,36),(42,37)],fill=INK)
            d.polygon([(47,32),(41,29),(28,30),(26,32)],fill=LIGHT)
            d.polygon([(47,32),(26,32),(29,34),(41,35)],fill=MID)
            stroke(d,[(12+f%3,31),(23,31)],2,LIGHT);d.line((29,30,42,30),fill=WHITE,width=1)
        elif name=='skillBlade':
            d.polygon([(50,32),(34,23),(18,27),(23,32),(18,37),(34,41)],fill=INK)
            d.polygon([(48,32),(33,25),(23,27),(28,32)],fill=LIGHT)
            d.polygon([(48,32),(28,32),(23,36),(33,39)],fill=MID)
            d.line((30,31,46,32),fill=WHITE,width=1);arc(d,21,130+f*3,225+f*3,1,SHADE)
        else:
            arc(d,13,-140+f*12,120+f*12,3,MID)
            d.polygon([(44,32),(37,22),(26,23),(20,32),(26,41),(37,41)],fill=INK)
            d.polygon([(42,31),(35,24),(27,25),(24,31)],fill=LIGHT)
            d.polygon([(42,32),(24,32),(28,39),(36,38)],fill=MID)
            facet(d,33,30,7,0,LIGHT);stroke(d,[(7,34+bob),(17,33)],2,SHADE)
    elif name=='skillThrust':
        head=[12,29,54,59,59,58,55,50][f];tail=[6,6,6,16,30,40,48,49][f]
        if head>tail:
            d.polygon([(tail,29),(head-6,29),(head,32),(head-6,35),(tail,35)],fill=INK)
            d.polygon([(tail,30),(head-5,30),(head-1,32),(tail,32)],fill=WHITE)
            d.polygon([(tail,33),(head-4,32),(head-6,34),(tail,34)],fill=MID)
            if head-tail>14:
                stroke(d,[(tail+3,26),(head-4,27)],1,LIGHT)
                stroke(d,[(tail+7,38),(head-8,37)],1,SHADE)
    elif name in ['skillMuzzle','skillImpact']:
        r=3+19*strength
        axes=[-.8,-.25,.3,.9] if name=='skillMuzzle' else [i*math.tau/7+.15 for i in range(7)]
        for i,a in enumerate(axes):
            end=point(32,32,r*(1 if i%2 else .78),a)
            start=point(32,32,r*(t*.5),a)
            vx,vy=end[0]-start[0],end[1]-start[1];nx,ny=-math.sin(a),math.cos(a)
            w=3*strength+1
            d.polygon([start,(start[0]+nx*w,start[1]+ny*w),end,(start[0]-nx*w,start[1]-ny*w)],fill=INK)
            d.polygon([start,(start[0]+nx*(w-1),start[1]+ny*(w-1)),end],fill=LIGHT if i%2 else MID)
            if f<5:d.line([start,end],fill=WHITE,width=1)
        if f<4:facet(d,32,32,2+f,0,LIGHT)
    elif name in ['skillShock','skillField','skillTrap','skillPull']:
        inward=name=='skillPull';r=(27-19*t) if inward else (5+22*t if name=='skillShock' else 20+2*math.sin(t*math.tau))
        for offset in [0,180]:arc(d,r,12+f*8+offset,140+f*8+offset,2,LIGHT if offset else MID,flat=.48)
        if name!='skillShock':
            for i in range(4 if name=='skillTrap' else 6):
                a=i*math.tau/(4 if name=='skillTrap' else 6)+(.15*t if name=='skillField' else 0)
                x,y=point(32,32,r,a,.48);facet(d,x,y,3 if name=='skillTrap' else 2,a,LIGHT)
                if name=='skillTrap':stroke(d,[(x,y),(x-3*math.cos(a),y-3*math.sin(a))],1,WHITE)
        if name=='skillField':
            arc(d,r-5,70+f*12,175+f*12,1,SHADE,flat=.48)
            arc(d,r-5,250+f*12,355+f*12,1,MID,flat=.48)
    elif name=='skillGuard':
        r=17+round(3*strength)
        for start,end in [(-78,-15),(15,78),(102,165),(195,258)]:
            arc(d,r,start+f*2,end+f*2,3,MID,flat=1.05)
            arc(d,r-2,start+5+f*2,end-10+f*2,1,LIGHT,flat=1.05)
        for i in range(3):facet(d,*point(32,32,r,i*2.1+t),3,i,LIGHT)
    elif name=='skillHeal':
        for i in range(3):
            x=20+i*12;y=48-t*27+i%2*3
            stroke(d,[(x-3,y+4),(x,y),(x-2,y-5),(x+2,y-9)],2,MID if i%2 else LIGHT)
        for i in range(2):facet(d,23+i*19,43-round(t*23),3,math.pi/2,LIGHT)
    elif name in ['skillDash','skillTrail']:
        head=52 if name=='skillDash' else 32
        for i in range(3):
            y=27+i*5;tail=head-(18 if name=='skillTrail' else 33)+f*2
            if tail<head-2:stroke(d,[(tail,y+i),(head-5,y),(head,y-1)],2 if i==1 else 1,LIGHT if i==1 else MID)
    elif name in ['skillIce','skillEarth']:
        for i in range(6):
            a=i*math.tau/6+.3;dist=6+13*t
            x,y=point(32,38,dist,a,.5);r=(6 if name=='skillEarth' else 9)*strength+1
            if name=='skillIce':facet(d,x,y-r*.3,r,a-.8,LIGHT if i%2 else MID)
            else:
                d.polygon([(x-4,y+3),(x-5,y-r*.2),(x-1,y-r),(x+4,y-r*.5),(x+4,y+2)],fill=INK)
                d.polygon([(x-3,y+1),(x-3,y-r*.2),(x-1,y-r+2),(x+1,y-r*.4),(x+1,y+1)],fill=MID)
                d.polygon([(x+1,y+1),(x+1,y-r*.4),(x+3,y-r*.4),(x+3,y)],fill=SHADE)
        arc(d,8+16*t,15,170,1,MID,at=(32,42),flat=.4)
    elif name=='skillLightning':
        for i in range(4):
            a=i*math.pi/2+.2+(f%2)*.15;r=5+22*strength
            pts=[(32,32),point(32,32,r*.4,a-.2),point(32,32,r*.48,a+.2),point(32,32,r,a)]
            stroke(d,pts,2,LIGHT)
            stroke(d,[pts[1],point(32,32,r*.7,a-.45)],1,MID)
        if f<4:facet(d,32,32,5,0,WHITE)
    elif name=='skillWind':
        for i in range(3):arc(d,12+i*6,15+f*15+i*100,110+f*15+i*100,3 if i==1 else 2,LIGHT if i==1 else MID,flat=.65)
    elif name=='skillPoison':
        r=6+15*strength
        for i in range(5):
            a=i*math.tau/5-.5;x,y=point(32,35,r*t,a,.7);size=round(2+6*strength)
            d.ellipse((x-size,y-size,x+size,y+size),fill=INK)
            d.ellipse((x-size+1,y-size+1,x+size-1,y+size-1),fill=MID)
            d.arc((x-size+2,y-size+2,x+size-2,y+size-2),180,280,fill=LIGHT,width=2)
        arc(d,9+16*t,15,165,2,SHADE,at=(32,42),flat=.4)
    elif name=='skillHoly':
        for i in range(4):facet(d,*point(32,32,12*t,i*math.pi/2),4+6*strength,i*math.pi/2,LIGHT)
        arc(d,12+13*t,15,150,1,MID,flat=.5);arc(d,12+13*t,195,330,1,LIGHT,flat=.5)
    return im

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    frames=generated()
    names=['skillThrust','skillMuzzle','skillImpact','skillShock','skillIce','skillLightning','skillEarth','skillWind','skillPoison','skillHoly','skillPull','skillGuard','skillHeal','skillField','skillTrap','skillDash','skillArrow','skillBullet','skillOrb','skillBlade','skillTrail']
    for name in names:frames[name]=[authored(name,f) for f in range(8)]
    result={name:[save(FX/f'{name}_{i}.png',im) for i,im in enumerate(row)] for name,row in frames.items()}
    asset=ROOT/'Assets/_Project/Resources/CombatVisualLibrary.asset';data=asset.read_text(encoding='utf-8')
    for name,ids in result.items():
        entry='  '+name+':\n'+''.join('  - {fileID: 21300000, guid: '+g+', type: 3}\n' for g in ids)
        pattern=r'^  '+name+r':\n(?:  -[^\n]*\n)*'
        data=re.sub(pattern,lambda _:entry,data,flags=re.M) if re.search(pattern,data,re.M) else data+entry
    values={'skillMainSeconds':.38,'skillAfterSeconds':.24,'skillImpactPixels':30,'skillProjectilePixels':26,'skillProjectileMinThickness':5,'skillProjectileMaxPixels':40,'skillMuzzlePixels':28,'skillTrailPixels':20,'skillTrailStartDistance':.3,'skillTrailInterval':.055,'skillUltimateScale':1.15}
    for key,val in values.items():
        line=f'  {key}: {val}\n';pattern=r'^  '+key+r':[^\n]*\n'
        data=re.sub(pattern,line,data,flags=re.M) if re.search(pattern,data,re.M) else data+line
    write(asset,data)
    for name in ['ActiveProjectileArt','ActiveSkillPalette','ActiveEffectPrompt']:
        guid(ROOT/f'Assets/_Project/Scripts/Design/{name}.cs')
    write(OUT/'asset_manifest.json',json.dumps(result,indent=2)+'\n')
    # An honest native sprite preview, not an in-game screenshot or a test run.
    preview=Image.new('RGBA',(8*72,25*80),'#292735');d=ImageDraw.Draw(preview)
    for y,(name,row) in enumerate(frames.items()):
        for x,im in enumerate(row):preview.alpha_composite(im,(x*72,y*80+12))
        d.text((2,y*80),name,fill='#c9bdd8')
    preview.save(OUT/'native_motion_preview.png')
    demo=[]
    for f in range(24):
        im=Image.new('RGBA',(5*80,5*80),'#292735')
        for i,row in enumerate(frames.values()):im.alpha_composite(row[(f//2)%len(row)],(i%5*80+8,i//5*80+8))
        demo.append(im.convert('RGB').resize((800,800),Image.Resampling.NEAREST))
    demo[0].save(OUT/'motion_families.gif',save_all=True,append_images=demo[1:],duration=45,loop=0,disposal=2)
    print(f'Saved {len(frames)} skill motion families / {sum(map(len,frames.values()))} native frames; GUIDs retained.')

if __name__=='__main__': main()
