"""Explicit 32-part trait authoring/migration. No images, API calls or Unity execution.
Only run when intentionally reauthoring parts; ordinary wiki refresh uses export_wiki.py.
"""
from pathlib import Path
import csv
import json
import re

ROOT = Path(__file__).resolve().parents[1]
# id, early reward, amount, secondary, threshold, duration, cooldown, distance, count, effect, art motif
TRAITS = [
('focus_lens',False,.30,0,0,.8,0,0,1,'제자리에서 {d}초 기다린 뒤 사용한 액티브의 첫 적중 피해 +{p}%.','조준선을 한 점에 모으는 수정 렌즈'),
('sword_knot',True,.25,0,0,0,0,0,1,'평타 연계의 마지막 공격 피해 +{p}%.','검 손잡이를 묶고 칼끝을 당기는 단단한 매듭'),
('ember_crucible',False,.12,0,0,2,0,0,1,'원소 액티브의 첫 적중에 불씨를 붙여 {d}초 동안 매초 공격력의 {p}% 화상 피해.','불씨 하나를 담은 작은 도가니'),
('prism_core',False,.22,0,0,6,0,0,1,'{d}초 안에 직전과 다른 원소의 액티브를 사용하면 그 스킬 피해 +{p}%. 무속성 스킬은 연결을 끊습니다.','서로 다른 두 빛을 교차시키는 분광 수정'),
('moon_charm',True,.30,0,-.25,0,0,0,1,'적의 등 뒤에서 맞히는 평타의 치명타 확률 +{p}%p.','등 뒤를 비추는 초승달 은 부적'),
('lucky_die',True,.10,0,0,0,0,0,5,'명중한 평타를 세어 {n}번째마다 확정 치명타. 그 타격의 치명타 피해 +{p}%.','다섯 번째 눈이 빛나는 흑요석 주사위'),
('razor_gem',True,.35,0,.30,0,0,0,1,'체력이 {t}% 이하인 적에게 평타 피해 +{p}%.','금이 간 적을 마무리하는 얇은 칼날 수정'),
('ruby_pin',False,.25,0,0,5,0,0,1,'치명타를 내면 홍옥이 충전됩니다. {d}초 안에 다음 액티브를 쓰면 그 스킬의 치명타 확률 +{p}%p.','치명타 에너지를 저장하는 홍옥 핀'),
('clock_spring',False,.15,0,0,4,0,0,1,'액티브를 {d}초 쉬었다가 사용하면 그 스킬의 남은 대기시간 {p}% 감소. 첫 사용에도 적용.','멈췄다가 다시 감기는 태엽 시계'),
('silver_gear',False,.18,0,0,0,0,0,3,'평타를 {n}회 맞히면 톱니가 맞물립니다. 다음 액티브의 남은 대기시간 {p}% 감소 후 횟수 초기화.','평타와 스킬을 잇는 세 개 이빨의 은빛 톱니'),
('echo_bell',False,.18,0,0,6,2,0,1,'{d}초 안에 다른 QWER을 이어 쓰면 직전에 쓴 슬롯의 남은 대기시간 {p}% 감소. 재발동 대기 {c}초.','앞선 동작으로 소리를 되돌리는 메아리 방울'),
('hourglass_bead',False,.25,0,0,0,0,0,1,'공격 액티브가 끝날 때 아무 적도 맞히지 못했다면 그 슬롯의 남은 대기시간 {p}% 회수. 중단·무기 교체에는 미적용.','빗나간 시간을 되돌리는 모래시계 구슬'),
('arrow_fletching',False,0,0,0,0,0,0,1,'각 액티브에서 처음 발사한 투사체가 적 {n}명을 추가 관통합니다.','한 줄로 꿰뚫는 날카로운 화살깃'),
('falcon_sight',False,1.5,0,0,0,0,0,1,'액티브 투사체가 가까운 적을 향해 완만하게 방향을 보정합니다.','적을 쫓는 매의 눈 모양 조준경'),
('rune_scope',False,.25,0,0,0,0,0,1,'한 번 사용한 액티브의 투사체로 같은 적을 다시 맞히면 후속 탄의 피해 +{p}%.','같은 표적 위에 겹쳐지는 두 개의 룬 조준선'),
('spiral_nozzle',False,12,.04,0,0,0,0,1,'액티브 투사체가 작은 나선 궤적으로 휘어 날아가며 명중 폭이 넓어집니다.','나선 홈이 파인 작은 발사 노즐'),
('trail_compass',False,.20,0,0,0,0,3,1,'자신에게서 {r}칸 이상 떨어진 곳을 조준해 사용한 액티브의 사거리 +{p}%.','먼 조준점을 가리키는 길잡이 나침반'),
('star_map',False,.15,.20,0,5,0,.8,1,'스킬로 첫 피해를 준 위치를 {d}초 기억합니다. 그 지점 {r}칸 안을 다시 조준하면 다음 액티브 사거리 +{p}%, 첫 적중 피해 +{s}%.','두 적중 지점을 별자리로 잇는 작은 지도'),
('long_reed',False,.30,0,0,0,0,2,1,'{r}칸 이상 날아간 액티브 투사체의 피해 +{p}%. 가까운 적에게는 보너스가 없습니다.','긴 비행 거리를 만드는 속 빈 갈대 관'),
('amber_mirror',False,.35,0,0,4,0,0,1,'받은 체력 피해의 {p}%를 저장합니다(최대 공격력의 {limit}%). {d}초 안에 다음 액티브를 쓰면 첫 적중에 저장한 피해를 추가.','공격 흔적을 품었다 돌려주는 호박 거울'),
('frost_shard',False,.30,0,0,0,3,0,1,'액티브로 끌기·밀기·기절·표식을 주는 첫 대상을 {v}초 얼려 멈춥니다. 재발동 대기 {c}초.','제어의 순간 대상을 얼리는 서리 결정'),
('anchor_hook',False,.45,0,0,1,0,0,1,'액티브로 적을 밀거나 당긴 뒤 {d}초 동안 이동 속도 {p}% 감소.','움직인 적의 발을 붙잡는 닻 갈고리'),
('thunder_pin',False,.25,0,0,0,3,0,1,'이미 표식이 있는 적에게 액티브의 첫 피해를 주면 {v}초 기절. 재발동 대기 {c}초.','표식을 찌르면 번쩍이는 뇌전 침'),
('gravity_stone',False,.45,0,0,0,0,0,1,'액티브의 끌어당기는 힘 +{p}%. 밀쳐내기에는 적용되지 않습니다.','안쪽으로 파편을 끌어모으는 중력석'),
('ward_buckle',True,.35,0,0,3,6,0,1,'완벽한 회피에 성공하면 공격력의 {p}% 보호막을 {d}초 획득. 재발동 대기 {c}초.','공격을 흘려 방패를 여는 수호 버클'),
('vital_vial',True,.04,0,0,0,2,0,1,'평타를 맞히면 잃은 체력의 {p}% 회복. 재발동 대기 {c}초.','빠진 체력을 채우는 작은 생명의 약병'),
('iron_plate',True,.20,0,0,.8,6,0,1,'제자리에서 {d}초 버티면 다음에 받는 공격 피해 {p}% 감소. 재발동 대기 {c}초.','가만히 버틸 때 겹쳐지는 철갑 조각'),
('heart_lock',True,.45,0,.35,3,12,0,1,'체력이 {t}% 이하가 될 공격을 받으면 공격력의 {p}% 보호막을 {d}초 획득. 재발동 대기 {c}초.','위험할 때 심장을 감싸 잠기는 자물쇠'),
('wind_boot',True,.30,0,0,4,0,3,1,'{r}칸 이동하면 바람을 충전합니다. {d}초 안에 다음 평타를 맞히면 첫 적중 피해 +{p}%.','걸음을 모아 다음 일격으로 보내는 바람 장화'),
('silk_grip',True,.06,0,0,.4,0,0,3,'제자리에서 {d}초 멈춘 뒤 평타를 맞힐 때마다 공격 속도 +{p}%, 최대 {n}중첩. 움직이면 초기화.','손에 감을수록 단단해지는 비단 손잡이'),
('feather_ring',True,1.2,0,0,0,4,0,1,'완벽한 회피에 성공하면 Space 이동 충전 시간을 {v}초 회복. 재발동 대기 {c}초.','회피의 순간 가벼워지는 깃털 고리'),
('breeze_ribbon',True,.25,0,0,2,3,0,1,'Space 이동이 끝난 뒤 {d}초 동안 이동 속도 +{p}%. 재발동 대기 {c}초.','이동이 끝나도 바람이 이어지는 리본'),
]

def main():
    asset=ROOT/'Assets/_Project/Data/AbilityRules.asset'
    source=asset.read_text(encoding='utf-8')
    section=source.split('    parts:\n',1)[1].split('    evolutions:',1)[0]
    old={re.search(r'    - id: (\S+)',b)[1].strip('"'):b for b in re.split(r'(?=^    - id:)',section,flags=re.M) if re.match(r'    - id:',b)}
    lines=['    parts:']; catalog=[]
    for index,row in enumerate(TRAITS,1):
        id,early,amount,secondary,threshold,duration,cooldown,distance,count,effect,art=row
        def field(key): return re.search(rf'^      {key}: (.+)$',old[id],re.M)[1].strip('"')
        data=dict(id=id,name=field('name'),family=field('family'),description=art+'。',sprite=field('sprite'),mods=[],
            skillDamage=0,projectileDamage=0,meleeDamage=0,shieldOnCast=0,healOnHit=0,rangeBonus=0,controlBonus=0,
            mechanic=index,requiresActive=0 if early else 1,traitText=effect,artBrief=art,
            amount=amount,secondary=secondary,threshold=threshold,duration=duration,cooldown=cooldown,distance=distance,count=count)
        data['description']=art+'.'
        for j,(key,value) in enumerate(data.items()):
            encoded=json.dumps(value,ensure_ascii=False) if isinstance(value,(str,list)) else str(value)
            lines.append(('    - ' if j==0 else '      ')+key+': '+encoded)
        catalog.append(data)
    rules=dict(maxStrength=2,damageCap=.65,critChanceCap=.5,critDamageCap=.5,reductionCap=.5,speedCap=.35,
        refundCap=.3,rangeCap=.5,healCap=.08,shieldCap=.75,stationaryTolerance=.03,slowCap=.6,homingCap=3,spiralFrequency=8,procDamageCap=.3)
    rule_text='    partTraits:\n'+''.join('      '+k+': '+str(v)+'\n' for k,v in rules.items())
    source=re.sub(r'    parts:\n.*?(?=    evolutions:)', '\n'.join(lines)+'\n',source,flags=re.S)
    source=re.sub(r'    partTraits:\n.*?(?=    parts:)', '',source,flags=re.S)
    source=source.replace('    parts:\n',rule_text+'    parts:\n',1)
    asset.write_text(source,encoding='utf-8',newline='\n')
    # Refresh art intent, keeping all existing IDs/paths and the 144 evolution images.
    manifest=ROOT/'ArtSource/Parts/image_manifest.csv'
    with manifest.open(encoding='utf-8-sig',newline='') as f: art_rows=list(csv.DictReader(f))
    by_id={d['id']:d for d in catalog}
    for row in art_rows:
        if row['id'] in by_id: row['art_brief']=by_id[row['id']]['artBrief']+' · 고유 능력 모티프가 읽히는 하나의 도구'
    with manifest.open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=list(art_rows[0]));writer.writeheader();writer.writerows(art_rows)
    def display(d):
        return d['traitText'].replace('{p}',f"{d['amount']*100:g}").replace('{s}',f"{d['secondary']*100:g}")\
            .replace('{v}',f"{d['amount']:g}").replace('{t}',f"{d['threshold']*100:g}")\
            .replace('{d}',f"{d['duration']:g}").replace('{c}',f"{d['cooldown']:g}").replace('{r}',f"{d['distance']:g}").replace('{n}',str(d['count'])).replace('{limit}','65')
    with (ROOT/'Docs/PartsCatalog.csv').open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.writer(f);w.writerow(['ID','이름','계열','등급','초반 등장','고유 능력','아트 모티프'])
        for d in catalog:w.writerow([d['id'],d['name'],d['family'],'T1','가능' if not d['requiresActive'] else '5레벨부터',display(d),d['artBrief']])
    head=['# 파츠 도감과 합성 규칙','','T1 32종은 각자 고정된 고유 능력을 가진다. 5레벨 전 보상에는 평타·방어·Space 이동으로 활용하는 12종만 나온다. 5레벨부터 전체 32종이 등장하며, 5/10/15/20레벨 보상은 기존대로 Q/W/E/R이다.','','| 이름 | 계열 | 등장 | 고유 능력 |','|---|---|---|---|']
    head += [f"| {d['name']} | {d['family']} | {'초반부터' if not d['requiresActive'] else '5레벨부터'} | {display(d)} |" for d in catalog]
    head += ['','## 합성과 계승','','같은 등급 두 개를 합치면 T2~T5로 오른다. 같은 종류도 가능하다. 모든 T1 재료 ID와 개수를 보존하며 합성 순서가 달라도 재료 구성이 같으면 결과가 같다. 재료의 고유 동작을 계승하고 수치의 기여는 `재료 수 / 전체 재료 수 × 등급 예산`으로 계산한다. 고정 횟수·발동 조건은 유지한다. 관통 횟수는 소량의 재료도 최소 1회를 계승하며 강화 상한을 적용한다.','','등급 예산은 1 / 1.8 / 3 / 4.5 / 6.4이며, 동일 고유 능력은 여러 파츠에 나뉘어 있어도 강도 합계 2배까지다. 추가 피해·치명타·대기시간 회수·회복·보호막·이동에는 별도 총합 상한이 있다. 같은 전체 재료 구성의 중복 장착은 하나만 적용한다.','','합성의 계열 쌍 36종 × 4등급 = 144개 이름·아트와 조건부 진화 보너스를 유지한다. 같은 외형도 구성 재료에 따라 계승 동작이 다르며, 144개가 서로 다른 독립 스킬 144개라는 뜻은 아니다.','','전투 중 충전·중첩·임시 준비 상태는 삶/저장 복원 시 초기화된다. 파츠가 스스로 새 스킬을 시전하거나 공격을 무한 반복하지 않는다. 투사체 보정·화상은 직접 사용한 액티브에만 붙는다.','','## 편집과 제작','','`AbilityRules.asset / parts`에서 고유 능력·초반 등장 여부·수치·문구·아트 모티프, `partTraits`에서 상한을 편집한다. 기존 재료 ID는 보존하므로 세이브의 합성 구성은 새 능력을 계승한다. 이미지 제작은 보류 상태다. [제작 프롬프트](../ArtSource/Parts/IMAGEGEN_PROMPT.md), [파츠 진화](PartEvolution.md), [위키](Wiki/index.html)를 참고한다.','','`author_part_catalog.py`는 이전 수치형 파츠의 초기 저작 도구로 현재 데이터에 실행하면 안 된다. `author_part_traits.py`도 의도적으로 능력을 재저작할 때만 사용한다. 도감 갱신은 `export_wiki.py`다.']
    (ROOT/'Docs/Parts.md').write_text('\n'.join(head)+'\n',encoding='utf-8')

if __name__=='__main__': main()
