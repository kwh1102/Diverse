"""Author deterministic T1 catalog/tuning/docs. Does not generate artwork or launch Unity."""
from pathlib import Path
import csv
import re

ROOT = Path(__file__).resolve().parents[1]
# id, Korean name, family, description, stat modifiers, active modifiers
PARTS = [
 ('focus_lens','집중 렌즈','위력','액티브 피해를 모으는 수정 렌즈.',[],{'skillDamage':.08}),
 ('sword_knot','검사의 매듭','위력','근접 액티브의 칼끝을 강화하는 매듭.',[],{'meleeDamage':.10}),
 ('ember_crucible','불씨 도가니','위력','원소 공격을 보조하는 작은 도가니.',[(12,.08,False)],{}),
 ('prism_core','분광 핵','위력','액티브 위력과 작은 시전 보호막을 제공한다.',[],{'skillDamage':.04,'shieldOnCast':.08}),
 ('moon_charm','초승달 부적','치명타','치명타 확률을 보조하는 은빛 부적.',[(4,.018,False)],{}),
 ('lucky_die','흑요석 주사위','치명타','치명타 피해를 보조하는 주사위.',[(5,.06,False)],{}),
 ('razor_gem','칼날 수정','치명타','치명타 확률과 근접 액티브 피해를 보조한다.',[(4,.009,False)],{'meleeDamage':.04}),
 ('ruby_pin','홍옥 핀','치명타','치명타 피해와 액티브 위력을 함께 보조한다.',[(5,.03,False)],{'skillDamage':.035}),
 ('clock_spring','태엽 시계','재사용','액티브 재사용 대기시간을 줄이는 시계.',[(6,.025,False)],{}),
 ('silver_gear','은빛 톱니','재사용','재사용 간격과 공격 흐름을 보조한다.',[(6,.012,False),(2,.025,True)],{}),
 ('echo_bell','메아리 방울','재사용','재사용 간격과 시전 보호막을 보조한다.',[(6,.012,False)],{'shieldOnCast':.08}),
 ('hourglass_bead','모래시계 구슬','재사용','재사용 간격과 성장 경험치를 보조한다.',[(6,.012,False),(8,.04,False)],{}),
 ('arrow_fletching','매깃 화살깃','투사체','투사체 액티브의 피해를 강화한다.',[],{'projectileDamage':.10}),
 ('falcon_sight','매눈 조준경','투사체','투사체 피해와 치명타 확률을 보조한다.',[(4,.009,False)],{'projectileDamage':.045}),
 ('rune_scope','룬 조준경','투사체','투사체 액티브의 사거리와 피해를 보조한다.',[],{'projectileDamage':.045,'rangeBonus':.035}),
 ('spiral_nozzle','나선 노즐','투사체','투사체 위력과 재사용 간격을 보조한다.',[(6,.012,False)],{'projectileDamage':.045}),
 ('trail_compass','길잡이 나침반','범위','액티브가 닿는 거리를 늘리는 나침반.',[],{'rangeBonus':.07}),
 ('star_map','별자리 지도','범위','사거리와 전리품 획득을 보조하는 지도.',[(9,.04,False)],{'rangeBonus':.035}),
 ('long_reed','갈대 관','범위','사거리와 투사체 피해를 보조한다.',[],{'rangeBonus':.035,'projectileDamage':.045}),
 ('amber_mirror','호박 거울','범위','사거리와 액티브 위력을 보조한다.',[],{'rangeBonus':.035,'skillDamage':.035}),
 ('frost_shard','서리 결정','제어','냉기와 기절 등 액티브 제어 시간을 늘린다.',[],{'controlBonus':.10}),
 ('anchor_hook','닻 갈고리','제어','제어 지속시간과 근접 액티브 위력을 보조한다.',[],{'controlBonus':.05,'meleeDamage':.045}),
 ('thunder_pin','뇌전 침','제어','제어 지속시간과 원소 공격을 보조한다.',[(12,.04,False)],{'controlBonus':.05}),
 ('gravity_stone','중력석','제어','제어 지속시간과 사거리를 보조한다.',[],{'controlBonus':.05,'rangeBonus':.035}),
 ('ward_buckle','수호 버클','방어','액티브를 시전하면 짧은 보호막을 얻는다.',[],{'shieldOnCast':.18}),
 ('vital_vial','생명의 약병','방어','액티브 적중 시 시전당 한 번 회복한다.',[],{'healOnHit':.025}),
 ('iron_plate','철갑 조각','방어','받는 피해를 줄이는 작은 갑옷 조각.',[(7,.025,False)],{}),
 ('heart_lock','심장 자물쇠','방어','최대 체력과 작은 시전 보호막을 보조한다.',[(0,6,False)],{'shieldOnCast':.08}),
 ('wind_boot','바람 장화','이동','이동 속도를 보조하는 가벼운 장화.',[(3,.035,True)],{}),
 ('silk_grip','비단 손잡이','이동','공격 속도와 이동 속도를 조금씩 보조한다.',[(2,.025,True),(3,.015,True)],{}),
 ('feather_ring','깃털 고리','이동','대시 재충전 시간을 줄이는 고리.',[(11,-.04,True)],{}),
 ('breeze_ribbon','산들바람 리본','이동','이동과 대시 재충전을 함께 보조한다.',[(3,.018,True),(11,-.02,True)],{}),
]
FALLBACKS = {
 'prism_core':'focus_lens','lucky_die':'mint_coin','razor_gem':'moon_charm','ruby_pin':'heart_lock',
 'silver_gear':'clock_spring','echo_bell':'ward_buckle','hourglass_bead':'scholar_quill','falcon_sight':'arrow_fletching',
 'rune_scope':'focus_lens','spiral_nozzle':'silk_grip','star_map':'trail_compass','long_reed':'scholar_quill',
 'amber_mirror':'focus_lens','frost_shard':'focus_lens','anchor_hook':'iron_plate','thunder_pin':'ember_crucible',
 'gravity_stone':'iron_plate','feather_ring':'arrow_fletching','breeze_ribbon':'sword_knot',
}

def sprite_for(id):
    path=ROOT/f'Assets/_Project/Resources/Sprites/Parts/{id}.png'
    return id if path.exists() else FALLBACKS.get(id,'focus_lens')

def main():
    asset = ROOT/'Assets/_Project/Data/AbilityRules.asset'
    text = asset.read_text(encoding='utf-8')
    text = re.sub(r'    parts:\n.*?(?=    evolutions:|    weaponShapes:)', '', text, flags=re.S)
    rows=['    parts:']
    keys=['skillDamage','projectileDamage','meleeDamage','shieldOnCast','healOnHit','rangeBonus','controlBonus']
    for id,name,family,desc,mods,bonuses in PARTS:
        rows += [f'    - id: {id}',f'      name: {name}',f'      family: {family}',f'      description: {desc}',f'      sprite: Sprites/Parts/{sprite_for(id)}']
        rows += ['      mods:' if mods else '      mods: []']
        for stat,value,mul in mods: rows += [f'      - stat: {stat}',f'        value: {value}',f'        mul: {int(mul)}']
        rows += [f'      {key}: {bonuses.get(key,0)}' for key in keys]
    target = '    evolutions:' if '    evolutions:' in text else '    weaponShapes:'
    text=text.replace(target, '\n'.join(rows)+'\n'+target, 1)
    if '    partTierPower:' not in text:
        text=text.replace('    partRankBonus: 0.12','    partRankBonus: 0.12\n    partTierPower: [1, 1.8, 3, 4.5, 6.4]\n    maxRangeBonus: 0.5\n    maxControlBonus: 0.5')
    text=text.replace('      maxSteps: 3','      maxSteps: 4')
    if '      chargeDuration:' not in text:
        text=text.replace('      maxSteps: 4','      maxSteps: 4\n      chargeDuration: 1.1\n      chargeBonus: 0.5\n      channelDuration: 1.5\n      recastWindow: 3\n      curveStrength: 65\n      maxCastDuration: 8\n      anchorWidth: 0.6')
    colors=[(0.95,.55,.25),(.4,.8,.4),(.25,.8,.85),(.7,.5,.95),(.95,.5,.72)]
    if '      iconQuality:' not in text:
        text=text.replace('      imageModel: gpt-image-2.5-flare', '      imageModel: gpt-image-2.5-flare\n' + '\n'.join('      '+line for line in [
            'iconQuality: low','visualFrames: 6','maxVisualPixels: 128','visualCacheLimit: 192','iconPaletteColors: 12',
            'minChoiceDistance: 0.38','inputDistanceWeight: 0.15','roleDistanceWeight: 0.25','iconBorderLimit: 0.08','iconSolidLimit: 0.92',
            'iconMatteTolerance: 70','iconMatteBorderShare: 0.12','controlCooldown: 1.1','guardCooldown: 1.3','mobilityCooldown: 0.85','trapCooldown: 1.15']))
    prefix, shapes=text.split('    weaponShapes:',1)
    for index,(r,g,b) in enumerate(colors):
        shapes=re.sub(rf'(    - type: {index}\n)(?:      outlineColor:.*\n)?',rf'\1      outlineColor: {{r: {r}, g: {g}, b: {b}, a: 1}}\n',shapes)
    asset.write_text(prefix+'    weaponShapes:'+shapes,encoding='utf-8',newline='\n')
    db=ROOT/'Assets/_Project/Resources/GameDatabase.asset'
    text=db.read_text(encoding='utf-8').replace('maxPartTier: 8','maxPartTier: 5').replace('evolveEvery: 2','evolveEvery: 1')
    text=re.sub(r'(    skillLevels: ).*',r'\g<1>050000000a0000000f00000014000000',text)
    if '    skillCommissionCost:' not in text: text=text.replace('    maxPartTier: 5','    maxPartTier: 5\n    skillCommissionCost: 75')
    db.write_text(text,encoding='utf-8',newline='\n')
    out=ROOT/'Docs/PartsCatalog.csv'
    with out.open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.writer(f);w.writerow(['ID','이름','계열','등급','설명','스탯','액티브 보조'])
        for id,name,family,desc,mods,bonuses in PARTS:w.writerow([id,name,family,'T1',desc,str(mods),str(bonuses)])
    rows=['# 파츠 도감과 합성 규칙','', '선택 보상에는 아래 T1 32종만 등장한다. T2~T5는 합성으로만 얻는다.','',
          '| 그림 | 이름 | 계열 | 능력 |','|---|---|---|---|']
    labels={0:'최대 체력',2:'공격 속도',3:'이동 속도',4:'치명타 확률',5:'치명타 피해',6:'재사용 감소',7:'피해 감소',8:'경험치',9:'골드',11:'대시 재충전',12:'원소 위력'}
    active={'skillDamage':'액티브 피해','meleeDamage':'근접 액티브 피해','projectileDamage':'투사체 액티브 피해','shieldOnCast':'시전 보호막/공격력','healOnHit':'시전당 1회 적중 회복/공격력','rangeBonus':'액티브 사거리','controlBonus':'제어 지속시간'}
    for id,name,family,desc,mods,bonuses in PARTS:
        effects=[f'{labels[stat]} {value*100 if stat!=0 else value:+g}{"%" if stat!=0 else ""}' for stat,value,mul in mods]
        effects += [f'{active[k]} +{v*100:g}%' for k,v in bonuses.items()]
        rows.append(f'| ![{name}](../Assets/_Project/Resources/Sprites/Parts/{sprite_for(id)}.png) | {name} | {family} | {" · ".join(effects)} |')
    rows += ['', '**아트 상태:** 사용자 요청으로 이번 구현에서는 이미지 생성을 보류했다. 신규 원본이 없는 파츠는 기존 PNG를 임시 공유한다. 도감의 그림도 임시 이미지이며, 능력과 재료 ID는 32종 모두 독립적이다.']
    rows += ['', '## 합성', '', '같은 등급의 파츠 2개를 합쳐 등급을 1 올린다. 종류는 달라도 된다. T5는 합성할 수 없다. 확정 전에는 재료와 골드를 소비하지 않는다.', '',
      '| 등급 | T1 재료 수 | 위력 예산 | 이론적 재료 구성 수 |','|---|---:|---:|---:|',
      '| T1 | 1 | 1 | 32 |','| T2 | 2 | 1.8 | 528 |','| T3 | 4 | 3 | 52,360 |','| T4 | 8 | 4.5 | 61,523,748 |','| T5 | 16 | 6.4 | 1,503,232,609,098 |','',
      '모든 결과를 별도 PNG나 레시피 행으로 열거하지 않는다. 합성 규칙과 T1 정의가 모든 조합의 결과를 결정하며 런타임 AI는 사용하지 않는다.', '',
      '## 결과를 읽는 방법', '',
      '재료별 기여 = 해당 T1 능력 × 해당 재료 개수 ÷ 전체 재료 수 × 현재 등급의 위력 예산. 같은 스탯은 합산하며 서로 다른 능력도 모두 보존한다.', '',
      '집중 렌즈 + 수호 버클 = T2: 액티브 피해 7.2%, 시전 보호막/공격력 16.2%. 렌즈 2개 = T2: 액티브 피해 14.4%.', '',
      '(렌즈+렌즈)+(버클+시계)와 (렌즈+버클)+(렌즈+시계)는 모두 같은 T3이다. 정렬된 재료 ID·개수만으로 이름·그림·능력을 결정한다.', '',
      'T2~T5는 계열 조합·등급별 전용 진화 그림과 조건부 액티브 보조 특성을 사용한다. 자세한 정의는 [파츠 진화](PartEvolution.md)와 [로컬 위키](Wiki/index.html)를 본다. 이미지 생성은 보류되어 있다.', '',
      '같은 재료 구성의 파츠를 중복 장착하면 하나만 적용한다. 다른 구성은 함께 적용하되 액티브 피해·사거리·제어·보호막·회복은 데이터의 전체 상한을 따른다.', '',
      '## 편집 위치', '', '기본 파츠·위력 예산·전체 상한: `Assets/_Project/Data/AbilityRules.asset`. 최고 등급·비용: `Assets/_Project/Resources/GameDatabase.asset / Inventory`.', '',
      'CSV 원본: [PartsCatalog.csv](PartsCatalog.csv). 원본 데이터 제작 도구: `Tools/author_part_catalog.py`. 기존 세이브의 고정 파츠는 등급에 맞는 동일 T1 재료 개수로 이관한다.']
    (ROOT/'Docs/Parts.md').write_text('\n'.join(rows)+'\n',encoding='utf-8')
    from author_part_evolutions import main as author_evolutions
    author_evolutions()

if __name__=='__main__': main()
