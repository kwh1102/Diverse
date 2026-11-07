"""Author finite evolution definitions/art manifest. No art, API calls, Unity or tests."""
from pathlib import Path
from itertools import combinations_with_replacement
import csv
import json
import re

ROOT = Path(__file__).resolve().parents[1]
FAMILIES = [
    ('power', '위력', 'charge', '충전 입력 또는 R', {'damage': .07}, '응축된 수정·작은 도가니'),
    ('critical', '치명타', 'precision', '직선·관통·유도 단계', {'damage': .055}, '초승달·날카로운 홍옥'),
    ('reuse', '재사용', 'alternate', '6초 안에 직전과 다른 슬롯 시전', {'cooldown': .035}, '태엽·은빛 기어'),
    ('projectile', '투사체', 'projectile', '투사체 단계', {'damage': .045, 'range': .025}, '깃털·나선 발사구'),
    ('reach', '범위', 'distance', '2 이상 떨어진 위치 조준', {'range': .065}, '나침반·호박 거울'),
    ('control', '제어', 'control', '끌기·밀기·기절·표식 단계', {'control': .09}, '서리·닻·중력석'),
    ('guard', '방어', 'shield', '보호막 보유 또는 보호막·장벽 단계', {'shield': .10, 'heal': .008}, '버클·수호 성배'),
    ('movement', '이동', 'movement', '돌진·후퇴·점멸 단계', {'damage': .035, 'range': .025}, '깃털·바람 리본'),
]
NAMES = [
    ['응축의 심장','결정타의 렌즈','점화 태엽','파열의 노즐','지평의 수정','결박 도가니','가호의 렌즈','질풍의 핵'],
    ['운명의 월륜','기회의 톱니','매의 송곳니','별을 꿰는 핀','서리의 칼날','불굴의 홍옥','달그림자 고리'],
    ['되울림 태엽','연사의 기관','궤도의 시계','속박의 진자','수호의 종','순환의 날개'],
    ['매의 척추','지평의 조준경','닻의 발사관','보루의 화살깃','유성의 깃'],
    ['지평 나침반','인력의 천구','성역의 거울','바람길 지도'],
    ['속박의 추','서리의 봉인','폭풍의 갈고리'],
    ['수호의 성배','산들바람 방패'],
    ['질풍의 날개'],
]
KEYS = ['damage','range','control','shield','heal','cooldown']


def definitions():
    rows = []
    for i, j in combinations_with_replacement(range(8), 2):
        a, b = FAMILIES[i], FAMILIES[j]
        for tier, scale in zip(range(2, 6), [1, 1.6, 2.3, 3.2]):
            stem = f'{a[0]}_{b[0]}_t{tier}'
            # Mixed power traits reward their companion condition. Pure power rewards charging.
            gate_a = 'always' if i == 0 and j != i else a[2]
            gate_b = b[2]
            condition = b[3] if gate_a == 'always' or i == j else a[3] + ' + ' + b[3]
            fa, fb = sorted([a[1], b[1]])
            row = dict(id=stem, familyA=fa, familyB=fb, tier=tier,
                       name=['','각성한 ','찬란한 ','완성된 '][tier-2] + NAMES[i][j-i],
                       conditionA=gate_a, conditionB=gate_b, conditionText=condition + '일 때 수동 시전 강화',
                       sprite='Sprites/Parts/Evolutions/' + stem)
            for key in KEYS:
                row[key] = round((a[4].get(key,0) + b[4].get(key,0)) * .65 * scale, 6)
            row['artBrief'] = a[5] if i == j else a[5] + '와 ' + b[5] + '를 결합한 하나의 새 도구'
            rows.append(row)
    return rows


def main():
    rows = definitions()
    asset = ROOT/'Assets/_Project/Data/AbilityRules.asset'
    text = asset.read_text(encoding='utf-8')
    text = re.sub(r'    evolutions:\n.*?(?=    weaponShapes:)', '', text, flags=re.S)
    for key, value in dict(evolutionComboWindow=6,evolutionAimDistance=2,evolutionDamageCap=.35,
                           evolutionRangeCap=.2,evolutionControlCap=.3,evolutionCooldownCap=.2).items():
        if f'    {key}:' not in text: text = text.replace('    weaponShapes:',f'    {key}: {value}\n    weaponShapes:')
    lines = ['    evolutions:']
    for row in rows:
        for index, (key, value) in enumerate(row.items()):
            if key == 'artBrief': continue
            lines.append(('    - ' if index == 0 else '      ') + f'{key}: ' + (json.dumps(value,ensure_ascii=False) if isinstance(value,str) else str(value)))
    text = text.replace('    weaponShapes:', '\n'.join(lines) + '\n    weaponShapes:')
    asset.write_text(text,encoding='utf-8',newline='\n')
    # Preserve current T1 tuning: read the asset rather than silently resetting it.
    source = text.split('    parts:\n',1)[1].split('    evolutions:',1)[0]
    t1=[]
    for block in re.split(r'(?=^    - id:)',source,flags=re.M):
        if not block.strip() or not re.search(r'^    - id:',block,re.M): continue
        field=lambda k: re.search(rf'^\s+(?:- )?{k}: (.*)$',block,re.M).group(1).strip('"')
        t1.append(dict(id=field('id'), name=field('name'), family=field('family'),
                       path=f"Assets/_Project/Resources/Sprites/Parts/{field('id')}.png", tier=1,
                       brief=field('description')))
    manifest=ROOT/'ArtSource/Parts/image_manifest.csv'
    with manifest.open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.writer(f);writer.writerow(['id','name','tier','families','final_png','art_brief','status'])
        for r in t1: writer.writerow([r['id'],r['name'],1,r['family'],r['path'],r['brief'],'정교화 대기'])
        for r in rows: writer.writerow([r['id'],r['name'],r['tier'],r['familyA']+' + '+r['familyB'],
            'Assets/_Project/Resources/'+r['sprite']+'.png',r['artBrief']+' · T'+str(r['tier'])+' 단계의 성장 실루엣','신규 제작 대기'])
    with (ROOT/'Docs/PartEvolutions.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
    (ROOT/'ArtSource/Parts/evolutions.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (ROOT/'Assets/_Project/Resources/Sprites/Parts/Evolutions').mkdir(exist_ok=True)


if __name__ == '__main__': main()
