"""Author the finite equipment catalog and native 32px item sprites. Does not launch Unity or run tests."""
from pathlib import Path
import uuid
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'Assets/_Project/Resources/Sprites/Parts'
OUT.mkdir(parents=True,exist_ok=True)
SRC = ROOT/'ArtSource/Parts'
SRC.mkdir(parents=True,exist_ok=True)
catalog = [
 ('focus_lens','집중 렌즈','액티브의 위력을 모으는 수정 렌즈.',[],{'skillDamage':.12}),
 ('arrow_fletching','매깃 화살깃','투사체 액티브의 비행을 안정시키는 깃.',[],{'projectileDamage':.15}),
 ('sword_knot','검사의 매듭','근접 액티브의 힘을 칼끝에 모으는 매듭.',[],{'meleeDamage':.15}),
 ('ward_buckle','수호 버클','액티브를 사용할 때 짧은 보호막을 두르는 버클.',[],{'shieldOnCast':.25}),
 ('vital_vial','생명의 약병','액티브 적중 시 체력을 되찾는 작은 약병.',[],{'healOnHit':.035}),
 ('clock_spring','태엽 시계','액티브를 다시 준비하는 시간을 줄이는 시계.',[(6,.04,False)],{}),
 ('wind_boot','바람 장화','공격 위치를 잡기 쉽게 해 주는 가벼운 장화.',[(3,.06,True)],{}),
 ('iron_plate','철갑 조각','받는 피해를 조금 줄이는 갑옷 조각.',[(7,.04,False)],{}),
 ('heart_lock','심장 자물쇠','최대 체력을 보조하는 붉은 자물쇠.',[(0,12,False)],{}),
 ('trail_compass','길잡이 나침반','대시 충전 간격을 줄이는 나침반.',[(11,-.06,True)],{}),
 ('scholar_quill','학자의 깃펜','전투에서 얻는 경험치를 늘리는 깃펜.',[(8,.08,False)],{}),
 ('mint_coin','행운 주화','획득하는 골드를 늘리는 오래된 주화.',[(9,.08,False)],{}),
 ('ember_crucible','불씨 도가니','원소 공격을 보조하는 작은 도가니.',[(12,.1,False)],{}),
 ('moon_charm','초승달 부적','치명타 확률을 조금 올리는 부적.',[(4,.025,False)],{}),
 ('moss_brooch','이끼 브로치','시간에 따라 체력을 조금 회복하는 브로치.',[(10,.12,False)],{}),
 ('silk_grip','비단 손잡이','기본 공격의 흐름을 매끄럽게 하는 손잡이.',[(2,.05,True)],{}),
]
ink='#302638'; shade='#6b526b'; metal='#a37c67'; gold='#d7ac70'; light='#f8e6bd'; blue='#89c7d5'; purple='#aa91cf'; red='#dc8790'; green='#97b58b'
def draw_icon(index):
    im=Image.new('RGBA',(32,32)); d=ImageDraw.Draw(im)
    def rect(box,col): d.rectangle(box,fill=col)
    def poly(points,col): d.polygon(points,fill=col)
    def line(points,col,w=1): d.line(points,fill=col,width=w)
    def ellipse(box,col): d.ellipse(box,fill=col)
    if index==0:
        ellipse((6,5,25,24),metal);ellipse((8,6,23,21),gold);ellipse((10,8,22,20),shade);ellipse((11,9,21,19),blue)
        poly([(12,10),(17,9),(20,13),(16,17),(12,16)],'#c5e9e4');rect((13,23,18,27),metal);rect((14,24,16,26),light)
    elif index==1:
        line([(7,26),(24,6)],metal,3);poly([(10,21),(8,14),(14,8),(25,5),(23,14),(16,22)],green)
        poly([(12,18),(14,10),(23,6),(19,14)],light);line([(7,26),(23,7)],gold,1);line([(15,15),(10,13)],shade);line([(18,11),(22,12)],shade)
    elif index==2:
        rect((13,5,18,11),gold);rect((11,9,20,13),metal);ellipse((9,11,21,21),red);ellipse((12,13,18,18),shade)
        poly([(12,18),(15,20),(11,28),(7,27)],red);poly([(17,19),(20,18),(25,25),(21,28)],red);line([(10,26),(14,21)],light);line([(20,21),(23,25)],light)
    elif index==3:
        poly([(7,8),(24,8),(24,20),(16,27),(7,20)],metal);poly([(9,10),(22,10),(22,19),(16,24),(9,19)],gold)
        poly([(12,12),(19,12),(19,19),(16,21),(12,18)],blue);rect((14,10,16,23),light);rect((11,15,20,17),light)
    elif index==4:
        rect((12,5,19,8),metal);rect((13,8,18,12),light);poly([(12,11),(19,11),(24,17),(23,25),(20,27),(10,27),(7,24),(7,17)],'#9cafbd')
        poly([(9,19),(22,18),(21,25),(11,25),(9,23)],red);rect((10,15,12,20),light);rect((14,13,16,14),light);ellipse((14,20,17,23),'#f2b4af')
    elif index==5:
        ellipse((12,3,19,9),metal);ellipse((6,7,25,27),metal);ellipse((8,8,23,25),gold);ellipse((10,10,22,23),light)
        line([(16,12),(16,17),(20,19)],shade,2);rect((10,15,11,17),metal);rect((20,14,21,16),metal);rect((15,22,16,23),metal)
    elif index==6:
        poly([(12,5),(22,5),(21,21),(26,23),(26,26),(8,26),(7,23),(11,20)],shade);poly([(13,7),(20,7),(19,21),(23,23),(10,23),(13,19)],'#8eaaa4')
        rect((11,8,22,11),light);rect((9,25,26,27),metal);line([(14,14),(19,15)],gold,2);line([(14,18),(18,19)],gold,2)
    elif index==7:
        poly([(8,6),(16,4),(24,9),(23,23),(15,28),(6,22)],shade);poly([(9,8),(16,6),(22,10),(21,22),(15,25),(8,21)],'#9dabb5')
        poly([(10,9),(15,8),(15,23),(10,20)],'#c8d0cb');rect((18,11,19,12),gold);rect((17,21,18,22),gold);line([(9,17),(21,15)],shade)
    elif index==8:
        ellipse((10,4,22,18),gold);ellipse((13,7,19,15),shade);rect((7,13,24,26),metal);rect((9,14,22,24),red)
        ellipse((11,16,16,21),light);ellipse((16,16,20,21),light);poly([(11,19),(20,19),(16,24)],light);rect((15,20,17,21),shade)
    elif index==9:
        ellipse((12,3,19,9),metal);ellipse((5,7,26,28),metal);ellipse((7,9,24,26),gold);ellipse((9,11,22,24),shade)
        poly([(16,11),(20,21),(16,19),(12,22)],red);poly([(16,14),(16,19),(12,22)],light);ellipse((15,17,17,19),gold)
    elif index==10:
        rect((6,25,20,27),metal);rect((7,23,19,25),gold);poly([(11,22),(11,16),(17,7),(25,4),(25,12),(19,21)],purple)
        poly([(12,20),(17,10),(23,6),(20,14)],light);line([(10,25),(23,7)],gold);line([(18,13),(24,12)],shade)
    elif index==11:
        ellipse((6,6,26,27),metal);ellipse((7,5,25,24),gold);ellipse((10,8,22,21),'#c38e5e');ellipse((11,9,21,20),gold)
        poly([(16,10),(18,14),(22,15),(19,18),(19,21),(16,19),(12,21),(13,17),(10,14),(14,14)],light)
    elif index==12:
        rect((8,17,24,20),gold);poly([(9,20),(23,20),(21,27),(11,27)],metal);rect((12,25,14,28),shade);rect((19,25,21,28),shade)
        poly([(10,16),(12,10),(15,13),(18,4),(21,11),(21,16)],'#d17b65');poly([(13,16),(16,10),(19,16)],light)
    elif index==13:
        line([(10,6),(14,11),(22,7)],gold,2);ellipse((8,11,24,27),gold);ellipse((13,10,25,22),(0,0,0,0))
        line([(10,20),(12,24),(17,25)],light,2);rect((23,23,24,26),purple);rect((21,24,26,25),purple)
    elif index==14:
        ellipse((6,9,26,26),metal);ellipse((8,11,24,24),shade);poly([(10,20),(12,11),(17,9),(21,13),(22,20),(17,24)],green)
        poly([(10,19),(14,12),(17,12),(13,20)],light);line([(16,23),(17,13),(20,15)],'#527d72',2);ellipse((6,8,11,13),purple)
    else:
        poly([(8,23),(21,6),(25,9),(12,27)],metal);poly([(10,22),(21,8),(23,10),(12,24)],red)
        for x,y in [(12,20),(15,16),(18,12)]: line([(x,y),(x+4,y+2)],light,2)
        poly([(10,23),(14,26),(12,28),(8,25)],gold);poly([(21,6),(24,5),(27,8),(25,10)],gold)
    # One-pixel dark contour follows the actual silhouette, preserving deliberate holes.
    src=im.copy(); pixels=src.load(); out=Image.new('RGBA',im.size); o=out.load()
    for y in range(32):
        for x in range(32):
            if pixels[x,y][3]: continue
            if any(0<=x+dx<32 and 0<=y+dy<32 and pixels[x+dx,y+dy][3] for dx,dy in [(-1,0),(1,0),(0,-1),(0,1)]): o[x,y]=(48,38,56,255)
    out.alpha_composite(im); return out

def meta(path):
    path=Path(str(path)+'.meta')
    if not path.exists(): path.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='utf-8')

sheet=Image.new('RGBA',(4*80,4*80),(45,39,54,255))
for i,(pid,*_) in enumerate(catalog):
    image=draw_icon(i); target=OUT/(pid+'.png');image.save(target)
    m=Path(str(target)+'.meta')
    guid=uuid.uuid4().hex if not m.exists() else m.read_text().split('guid: ')[1].splitlines()[0]
    template=(ROOT/'Assets/_Project/Resources/Sprites/inventory_weapon_relic.png.meta').read_text()
    import re
    settings=re.sub(r'^guid: \w+', 'guid: '+guid,template,flags=re.M).replace('  isReadable: 0','  isReadable: 1')
    m.write_text(settings,encoding='utf-8',newline='\n')
    sheet.alpha_composite(image.resize((64,64),Image.Resampling.NEAREST),((i%4)*80+8,(i//4)*80+8))
sheet.save(SRC/'catalog.png')
meta(OUT);meta(ROOT/'Assets/_Project/Scripts/Combat/ActiveSkillData.cs');meta(ROOT/'Assets/_Project/Scripts/Combat/ActiveSkillGenerator.cs');meta(ROOT/'Assets/_Project/Scripts/Combat/ActiveSkillRuntime.cs');meta(ROOT/'Assets/_Project/Scripts/Design/ActiveSkillArt.cs')

asset=ROOT/'Assets/_Project/Data/AbilityRules.asset'
header=asset.read_text(encoding='utf-8-sig').split('  def:')[0]
lines=[header+'  def:', '    partRankBonus: 0.12','    partShieldDuration: 2','    partMaxShield: 0.75','    partMaxHeal: 0.15','    telemetryFullTrust: 25','    telemetryMinSeconds: 30','    active:']
active={'maxSteps':3,'maxLinkedSlots':2,'maxTargets':8,'maxProjectiles':5,'maxPulses':3,'maxConcurrentCasts':4,'generationAttempts':3,'recentSignatures':32,
        'iconPixels':32,'projectilePixels':16,'effectPixels':48,'artParallel':2,'baseCooldown':7,'ultimateCooldown':22,'minCooldown':4,'maxCooldown':35,
        'basePower':3.8,'ultimatePower':8.5,'maxDamage':12,'range':5,'meleeRange':2.5,'radius':2,'projectileSpeed':11,'lockTime':.25,'recoveryPenalty':.3,'cooldownPenalty':1.3,
        'shortRangeFactor':.55,'effectDuration':2,'statusDuration':3,'stunDuration':.65,'pulseInterval':.45,'delay':.6,'movementDistance':3,'movementDuration':.2,
        'knockback':4,'shieldScale':5,'healScale':2,'conditionalBonus':.35,'patternCost':.45,'controlCost':.65,'movementCost':.45,'areaCost':.35,'rangeCost':.3,'timingCost':.35,
        'defenseCost':.65,'markBonus':.25,'passiveBonusCap':.65,'projectileRadius':.2,'guardInvulnerability':.25,'imageModel':'gpt-image-2.5-flare'}
for k,v in active.items(): lines.append(f'      {k}: {v}')
lines.append('    parts:')
for pid,name,desc,mods,bonuses in catalog:
    lines += [f'    - id: {pid}',f'      name: {name}',f'      description: {desc}',f'      sprite: Sprites/Parts/{pid}']
    if mods:
        lines.append('      mods:')
        for stat,value,mul in mods: lines += [f'      - stat: {stat}',f'        value: {value}',f'        mul: {int(mul)}']
    else: lines.append('      mods: []')
    for key in ['skillDamage','projectileDamage','meleeDamage','shieldOnCast','healOnHit']: lines.append(f'      {key}: {bonuses.get(key,0)}')
shapes=[(0,[(0,-1),(-1,0),(1,0),(0,1)],[(-1,-1),(1,-1),(0,-2),(0,2)]),
        (1,[(-1,-1),(-2,0),(2,0),(1,1)],[(-1,0),(1,0),(-1,1),(1,-1)]),
        (2,[(0,-1),(-1,0),(2,0),(0,1)],[(1,0),(1,-1),(1,1),(-1,1)]),
        (3,[(0,-2),(-1,0),(1,0),(0,2)],[(0,-1),(0,1),(-1,-1),(1,1)]),
        (4,[(0,-1),(-1,0),(1,0),(0,1)],[(-1,-1),(1,-1),(-1,1),(1,1)])]
lines.append('    weaponShapes:')
for kind,skills,parts in shapes:
    lines += [f'    - type: {kind}','      skills:']
    for x,y in skills: lines.append(f'      - {{x: {x}, y: {y}}}')
    lines.append('      parts:')
    for x,y in parts: lines.append(f'      - {{x: {x}, y: {y}}}')
asset.write_text('\n'.join(lines)+'\n',encoding='utf-8',newline='\n')
(SRC/'README.md').write_text('''# 고정 파츠 도트 원본

`catalog.png`는 고정 파츠 16종의 도트 미리보기입니다. 왼쪽부터 행 순서로 AbilityRules.asset의 목록과 대응합니다.
각 파츠는 32×32 투명 PNG, 16 PPU, Point 필터로 저장합니다. 게임에서는 Resources/Sprites/Parts의 개별 PNG를 사용합니다.
프로젝트의 기존 코드 기반 도트 아트 방식에 맞춰 직접 제작했습니다. 외부 이미지 생성 API는 사용하지 않았습니다.
재제작: 번들 Python으로 Tools/author_fixed_parts.py 실행. 데이터 에셋도 함께 갱신하므로 Inspector 수정 후에는 주의하세요.
''',encoding='utf-8')
