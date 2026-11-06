"""Export a buildless local wiki from project assets/source. Standard library only; no game/tests/API calls."""
from pathlib import Path
from datetime import datetime, timezone
import csv
import json
import re

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'Docs/Wiki'


def decode(value):
    value = re.sub(r'\\u([0-9a-fA-F]{4})', lambda m: chr(int(m[1],16)), value)
    return value.strip().strip('"').replace('\\n','\n').replace('\\"','"')


def fields(text):
    return {m[1]:decode(m[2]) for m in re.finditer(r'^\s+(?:- )?(\w+): (.+)$',text,re.M)}


def blocks(text, start, end):
    section=text.split(start,1)[1].split(end,1)[0]
    return [b for b in re.split(r'(?=^    - (?:id|type):)',section,flags=re.M) if b.strip()]


def main():
    OUT.mkdir(exist_ok=True)
    entries=[]
    def add(id, category, name, summary, source, raw='', **extra):
        entries.append(dict(id=id,category=category,name=name,summary=summary,source=source,raw=decode(raw),**extra))
    ability=ROOT/'Assets/_Project/Data/AbilityRules.asset'; text=ability.read_text(encoding='utf-8')
    trait_caps={k:float(v) for k,v in fields(text.split('    partTraits:\n',1)[1].split('    parts:',1)[0]).items()}
    trait_source=(ROOT/'Assets/_Project/Scripts/Abilities/PartTraits.cs').read_text(encoding='utf-8')
    trait_names=[name.strip() for name in re.search(r'enum PartMechanic\s*\{([^}]+)\}',trait_source,re.S)[1].split(',')]
    for b in blocks(text,'    parts:\n','    evolutions:'):
        d=fields(b)
        if 'id' not in d: continue
        d['mechanicName']=trait_names[int(d['mechanic'])]
        image=d.get('sprite','')
        image='Assets/_Project/Resources/'+image+'.png' if image else ''
        effect=d.get('traitText',d.get('description',''))
        for token,key,mul in [('p','amount',100),('s','secondary',100),('v','amount',1),('t','threshold',100),('d','duration',1),('c','cooldown',1),('r','distance',1),('n','count',1)]:
            effect=effect.replace('{'+token+'}',f'{float(d.get(key,0))*mul:g}')
        effect=effect.replace('{limit}',f"{float(re.search(r'^      damageCap: (.+)$',text,re.M)[1])*100:g}")
        d['고유 능력']=effect; d['등장 시기']='초반부터' if d.get('requiresActive')=='0' else '5레벨부터'
        add('part:'+d['id'],'T1 파츠',d['name'],d['등장 시기']+' · '+effect,str(ability.relative_to(ROOT)),b,
            tier=1,family=d.get('family'),image=image,art='임시 기존 그림 · 정교화 대기',data=d)
    for b in blocks(text,'    evolutions:\n','    weaponShapes:'):
        d=fields(b); tier=int(d['tier']); image='Assets/_Project/Resources/'+d['sprite']+'.png'
        add('evolution:'+d['id'],'진화 파츠',d['name'],d['conditionText'],str(ability.relative_to(ROOT)),b,
            tier=tier,family=d['familyA']+' + '+d['familyB'],image=image if (ROOT/image).exists() else '',
            art='전용 진화 그림 준비 완료' if (ROOT/image).exists() else '전용 진화 그림 제작 대기',data=d)
    weapon_names=['검','활','총','봉','단검']
    shape_section=text.split('    weaponShapes:\n',1)[1]
    shapes=[]
    for b in re.split(r'(?=^    - type:)',shape_section,flags=re.M):
        if not b.strip(): continue
        d=fields(b); t=int(re.search(r'- type: (\d+)',b)[1]); cells=[]
        for part in ['skills','parts']:
            section=b.split('      '+part+':',1)[1] if '      '+part+':' in b else ''
            section=section.split('      parts:',1)[0] if part=='skills' else section
            cells.append([dict(x=int(x),y=int(y)) for x,y in re.findall(r'\{x: (-?\d+), y: (-?\d+)\}',section)])
        color=re.search(r'outlineColor: \{r: ([\d.]+), g: ([\d.]+), b: ([\d.]+)',b)
        tint='rgb('+','.join(str(round(float(x)*255)) for x in color.groups())+')' if color else '#c9b8ff'
        shape=dict(type=t,name=weapon_names[t],skills=cells[0],parts=cells[1],color=tint);shapes.append(shape)
        add('weapon-type:'+str(t),'무기 유형',weapon_names[t], '같은 유형·같은 QWER 슬롯끼리만 교환. 굵은 외곽선은 사용 중인 무기.',str(ability.relative_to(ROOT)),b,shape=shape,data=d)
    categories={'Weapons':'기본 무기','Enemies':'적','Costumes':'코스튬','CombatTypes':'기본 공격 유형','Themes':'월드 테마'}
    for p in sorted((ROOT/'Assets/_Project/Data').rglob('*.asset')):
        if p==ability: continue
        raw=p.read_text(encoding='utf-8'); content=raw.split('  def:\n',1)[-1]
        # The first name is the asset's own display name, not a nested list member.
        own=re.search(r'^    name: (.+)$',content,re.M)
        d=fields(content); name=decode(own[1]) if own else p.stem
        cat=categories.get(p.parent.name,'게임 규칙·데이터')
        summary=next((decode(m[1]) for m in [re.search(r'^    (?:desc|perk|description): (.+)$',content,re.M)] if m),p.stem+' 데이터 전체 필드')
        add('asset:'+str(p.relative_to(ROOT)),cat,name,summary,str(p.relative_to(ROOT)),content,data=d)
    db=ROOT/'Assets/_Project/Resources/GameDatabase.asset'
    add('database','게임 규칙·데이터','성장·인벤토리·보상','Q/W/E/R 획득 레벨 5/10/15/20 · 나머지 레벨 T1 파츠',str(db.relative_to(ROOT)),db.read_text(encoding='utf-8'))
    add('active-rules','액티브 언어','수치·상한·이미지 설정','실행 예산·쿨다운·최대 단계·선택 거리·이미지 모델과 품질',str(ability.relative_to(ROOT)),text.split('    active:\n',1)[1].split('    parts:',1)[0])
    grammar={
        'delivery':('전달 형태',['arc','line','projectile','nova','field','dash','retreat','blink','guard','trap','wall']),
        'pattern':('궤적',['single','fan','ring','chain','return','pierce','seek','curve','bounce','converge']),
        'effect':('효과',['damage','shield','heal','pull','push','stun','mark','block']),
        'condition':('대상 조건',['always','marked','burning','chilled','poisoned']),
        'input':('입력',['tap','charge','channel','recast']),
        'timing':('시간',['instant','delayed','repeat']),
        'origin':('기준 위치',['caster','aim','previous','impact']),
        'trigger':('연결 사건',['after','hit','arrive','expire','recast']),
    }
    translations=dict(arc='전방 베기',line='직선 타격',projectile='투사체',nova='주변 폭발',field='지면 영역',dash='돌진',retreat='후퇴',blink='점멸',guard='보호막 방어',trap='단발 지면 덫',wall='적 탄환 장벽',single='단일',fan='부채꼴',ring='방사',chain='연쇄 직선',return_='돌아오는 탄환',pierce='관통',seek='유도',curve='곡선',bounce='벽 반사 1회',converge='수렴',damage='피해',shield='보호막',heal='회복',pull='끌어당김',push='밀치기',stun='기절',mark='표식',block='적 탄환 차단',always='항상',marked='표식 대상',burning='화상 대상',chilled='냉기 대상',poisoned='중독 대상',tap='한 번 입력',charge='길게 누른 뒤 놓기',channel='누르는 동안 유지',recast='같은 키 재입력',instant='즉시',delayed='지연',repeat='반복',caster='시전자',aim='조준 위치',previous='앞 단계 위치',impact='앞 단계 명중 위치',after='즉시 연결',hit='앞 단계 첫 명중 후',arrive='앞 단계 자연 도착 후',expire='앞 단계 종료 후')
    translations['return']=translations.pop('return_')
    generator_source=(ROOT/'Assets/_Project/Scripts/Combat/ActiveSkillGenerator.cs').read_text(encoding='utf-8')
    for field,(label,values) in grammar.items():
        schema=re.search(rf"\b{field}:'([^']+)'",generator_source)
        if schema: values=schema[1].split('|')
        for value in values:
            add('grammar:'+field+':'+value,'액티브 언어',translations.get(value,value),f'{label} · {field} = {value}','Assets/_Project/Scripts/Combat/ActiveSkillGenerator.cs',family=label,data={'field':field,'token':value})
    for role, label in [('burst','순간 화력'),('precision','정밀 타격'),('sustain','지속 압박'),('control','제어·집결'),('trap','매복·차단'),('mobility','이동·각도'),('guard','방어·생존')]:
        add('role:'+role,'액티브 언어',label,'실행 단계에서 판정하는 전술 역할 · '+role,'Assets/_Project/Scripts/Combat/ActiveSkillGenerator.cs',family='전술 역할')
    for drawback,label in [('recovery','긴 시전 후딜'),('cooldown','긴 쿨다운'),('shortRange','짧은 사거리')]:
        add('drawback:'+drawback,'액티브 언어',label,'위력의 대가 · '+drawback,'Assets/_Project/Scripts/Combat/ActiveSkillGenerator.cs',family='대가')
    for p in sorted((ROOT/'Assets/_Project/Scripts').rglob('*.cs')):
        if 'Editor' in p.parts: continue
        code=p.read_text(encoding='utf-8-sig')
        for match in re.finditer(r'\benum\s+(\w+)\s*\{([^}]+)\}',code,re.S):
            body=re.sub(r'//[^\n]*','',match[2]); values=[]
            for value in body.split(','):
                name=value.strip().split('=')[0].strip()
                if re.fullmatch(r'\w+',name) and name!='Count': values.append(name)
            labels_source=re.search(r'(?:Name|Label)\s*\([^)]*\)\s*=>\s*\w+\s+switch\s*\{(.*?)\};',code,re.S)
            translated=dict(re.findall(rf'{match[1]}\.(\w+)\s*=>\s*"([^"]+)"',labels_source[1] if labels_source else ''))
            if match[1]=='WeaponType': translated.update(zip(['Sword','Bow','Gun','Staff','Dagger'],weapon_names))
            if match[1]=='ItemKind': translated.update(zip(['Weapon','Skill','Part','Consumable'],['무기','액티브 스킬','파츠','소비 아이템']))
            if match[1]=='TravelType': translated['Dash']='대시형'
            for index,name in enumerate(values):
                add('enum:'+p.stem+':'+match[1]+':'+name,'유형·열거 목록',translated.get(name,name),match[1]+'.'+name+' · 선언 순서 '+str(index),str(p.relative_to(ROOT)),match[0],family=match[1])
    for id,name,desc in [('heal','회복약','체력 비율 회복 · potionHealFraction'),('xp','경험의 물약','경험치 획득 · xpPotionAmount'),('cleanse','정화의 물약','상태 이상 제거')]:
        add('consumable:'+id,'소비 아이템',name,desc,'Assets/_Project/Scripts/Core/InventoryState.cs',data={'id':id},image='Assets/_Project/Resources/Sprites/inventory_'+id+'.png')
    for p in sorted((ROOT/'Docs').glob('*.md')):
        add('doc:'+p.stem,'시스템 문서',p.stem,p.read_text(encoding='utf-8').splitlines()[0].lstrip('# '),str(p.relative_to(ROOT)),p.read_text(encoding='utf-8'))
    prompt=ROOT/'ArtSource/Parts/IMAGEGEN_PROMPT.md'
    add('doc:PartImagePrompt','시스템 문서','파츠 이미지 생성 프롬프트','다른 세션에 전달할 고유 능력 반영 아트 제작 지침',str(prompt.relative_to(ROOT)),prompt.read_text(encoding='utf-8'))
    tier_power=json.loads(re.search(r'partTierPower: (\[[^\]]+\])',text)[1])
    data=dict(tierPower=tier_power,traitCaps=trait_caps,generated=datetime.now(timezone.utc).isoformat(timespec='seconds'),entries=entries,shapes=shapes)
    (OUT/'catalog.js').write_text('window.DIVERSE_WIKI = '+json.dumps(data,ensure_ascii=False,separators=(',',':'))+';\n',encoding='utf-8')


if __name__ == '__main__': main()
