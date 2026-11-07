"""Static visual review using serialized prefab geometry and imported sprites, never a Unity screenshot."""
from pathlib import Path
import json
import re
import sys
sys.dont_write_bytecode=True
from PIL import Image,ImageDraw,ImageFont
from patch_part_readability import Prefab,ROOT

OUT=ROOT/'ArtSource/InventoryPolish'
P=Prefab(ROOT/'Assets/_Project/Prefabs/UI/GameCanvas.prefab')
FRAMES=ROOT/'Assets/_Project/UI/Frames'
PARTS=ROOT/'Assets/_Project/Resources/Sprites/Parts'
S=3
def vec(v):return tuple(float(x) for x in re.findall(r'[xy]: (-?[\d.]+)',v))
def area(r):
    x,y=vec(P.value(r,'m_AnchoredPosition'));w,h=vec(P.value(r,'m_SizeDelta'));return x,-y,w,h

class Preview:
    def __init__(self):self.im=Image.new('RGBA',(640*S,360*S),'#201d2a');self.d=ImageDraw.Draw(self.im);self.d.fontmode='1'
    def skin(self,name,r,border=4):
        if name=='satchel_panel':border=6
        src=Image.open(FRAMES/(name+'.png')).convert('RGBA');x,y,w,h=map(round,r)
        if border:
            sw,sh=src.size;xs=[0,border,sw-border,sw];ys=[0,border,sh-border,sh];dx=[0,border,w-border,w];dy=[0,border,h-border,h]
            out=Image.new('RGBA',(w,h))
            for row in range(3):
                for col in range(3):
                    cell=src.crop((xs[col],ys[row],xs[col+1],ys[row+1])).resize((dx[col+1]-dx[col],dy[row+1]-dy[row]),Image.Resampling.NEAREST)
                    out.alpha_composite(cell,(dx[col],dy[row]))
        else:out=src.resize((w,h),Image.Resampling.NEAREST)
        self.im.alpha_composite(out.resize((w*S,h*S),Image.Resampling.NEAREST),(x*S,y*S))
    def text(self,xy,text,size=8,color='#dcd0e2'):
        font=ImageFont.truetype(str(ROOT/'Assets/_Project/Resources/Fonts/Galmuri11.ttf'),size*S)
        self.d.text(tuple(round(n*S) for n in xy),text,font=font,fill=color,spacing=2*S)
    def icon(self,name,r):
        path=PARTS/(name+'.png') if '_t' not in name else PARTS/'Evolutions'/(name+'.png')
        im=Image.open(path).convert('RGBA');x,y,w,h=map(round,r)
        self.im.alpha_composite(im.resize((w*S,h*S),Image.Resampling.NEAREST),(x*S,y*S))
    def save(self,name):self.im.convert('RGB').save(OUT/name)

def inventory(width,height):
    v=Preview();panel=P.find(P.host('InventoryView'),'Panel')
    def rect(path):
        x,y,w,h=area(P.find(panel,path));return x,y+4,w,h
    for path in ['HeaderBar','EquipmentBox','WorkshopBox','ItemBox','PendingBox']:v.skin('satchel_panel',rect(path))
    bx,by,bw,bh=rect('Bag');v.skin('satchel_body',(bx,by,bw,bh),18);v.skin('satchel_handle',(bx+151,by-16,40,18),0)
    v.text((134,8),'인벤토리',12);v.skin('satchel_slot',(386,6,72,20));v.text((398,12),'닫기 [I]',9)
    v.text((14,60),'장착 정보',10);v.text((14,84),'대검\n검 · 사용 중인 무기\n\n무기 1/4\n보유 골드 64G\n\nQWER 칸 · 스킬\n◆ 칸 · 고정 파츠\n\n◆ 흑요석 주사위\n◆ 바람 장화\n◆ 깃털 고리',8)
    v.skin('satchel_slot',(14,256,96,22));v.text((24,263),'파츠 공방 열기',9)
    v.text((14,288),'드래그로 이동\n빈 칸 클릭: 선택 해제\n유형별 외곽선\nQWER · 스킬 슬롯',8,color='#a89abc')
    v.text((bx+26,by+7),f'여행 가방 · {width}×{height}',8,color='#c8b292')
    gx=bx+12+(318-(width*32-2))/2;gy=by+18+(254-(height*32-2))/2
    assert width*32-2<=318 and height*32-2<=254
    for y in range(height):
        for x in range(width):v.skin('satchel_slot',(gx+x*32,gy+y*32,30,30))
    for x,y,name in [(1,0,'lucky_die'),(0,1,'wind_boot'),(2,1,'feather_ring')]:
        v.icon(name,(gx+x*32+3,gy+y*32+3,24,24));v.text((gx+x*32+18,gy+y*32+23),'T1',7,color='#ead7ad')
    v.skin('satchel_selected',(gx+32,gy,30,30))
    # A representative 9-cell weapon contour uses the same shared footprint geometry as the UI.
    cells={(1,0),(0,1),(1,1),(2,1),(1,2),(2,2),(1,3),(1,4)}
    for x,y in cells:
        x0=round((gx+x*32-1)*S);y0=round((gy+y*32-1)*S)
        for dx,dy,a,b in [(-1,0,(0,0),(0,32)),(1,0,(32,0),(32,32)),(0,-1,(0,0),(32,0)),(0,1,(0,32),(32,32))]:
            if (x+dx,y+dy) not in cells:v.d.line((x0+a[0]*S,y0+a[1]*S,x0+b[0]*S,y0+b[1]*S),fill='#d9a467',width=S)
    v.icon('lucky_die',(482,64,36,36));v.text((526,60),'흑요석\n주사위',10);v.text((526,98),'파츠 · T1',8,color='#a89abc')
    v.text((484,126),'고유 능력\n명중한 평타를 세어\n5번째마다 확정 치명타.\n그 타격의 치명타 피해\n+10%.',9)
    v.skin('satchel_slot',(482,326,144,20));v.text((538,331),'버리기',9)
    v.text((132,323),'수령 대기 · 끌어서 가방에 넣기',8)
    v.text((4,354),'정적 프리팹 배치·아트 미리보기 · 게임 실행 없음',6,color='#9e91af');v.save(f'inventory_{width}x{height}.png')

def fusion_rows():
    v=Preview();v.skin('satchel_panel',(10,10,620,340));v.text((272,18),'파츠 합성',16)
    v.skin('satchel_slot',(22,70,184,234));v.text((214,45),'재료의 고유 능력을 확인하고 합성하세요',9)
    for i,(name,label) in enumerate([('critical_movement_t2','달그림자 고리'),('lucky_die','흑요석 주사위'),('wind_boot','바람 장화'),('feather_ring','깃털 고리')]):
        y=76+i*46;v.skin('satchel_panel',(28,y,164,42));v.icon(name,(36,y+7,28,28));v.text((70,y+11),label+'\n등급 T'+('2' if '_t' in name else '1'),8)
    for y,name,label in [(70,'feather_ring','깃털 고리'),(190,'lucky_die','흑요석 주사위')]:
        v.skin('satchel_slot',(218,y,170,114));v.icon(name,(228,y+8,32,32));v.text((266,y+10),label+'\n등급 T1',9)
        v.text((228,y+47),'고유 능력\n재료의 원래 특성 표시',8);v.skin('satchel_slot',(228,y+88,150,18));v.text((264,y+93),'재료 바꾸기',8)
    v.skin('satchel_slot',(400,70,218,234));v.icon('critical_movement_t2',(485,78,48,48));v.text((410,138),'달그림자 고리',11)
    v.text((410,165),'고유 능력 · 재료에서 계승\n\n깃털 고리\n완벽한 회피에 성공하면 충전 회복\n\n흑요석 주사위\n5번째 명중한 평타는 확정 치명타\n\n진화 효과 · 발동 조건',8)
    v.text((10,354),'정적 목록 배치 미리보기 · 게임 실행 없음',6);v.save('fusion_rows.png')

def inspect():
    P.validate();report={'mode':'static asset/prefab inspection; no Unity/compile/tests','canvases':[],'icons':[]}
    for name in ['GameCanvas','MenuCanvas']:
        p=Prefab(ROOT/f'Assets/_Project/Prefabs/UI/{name}.prefab');p.validate()
        before=Prefab(OUT/(name+'.before.prefab'))
        assert set(p.docs)==set(before.docs)
        report['canvases'].append({'name':name,'object_ids_preserved':True})
    for c in P.hosts('InventoryCell'):
        for name in ['Icon','SkillGlyph']:
            r=P.find(c,name)
            assert vec(P.value(r,'m_AnchoredPosition'))==(0,0)
            assert vec(P.value(r,'m_AnchorMin'))==(.5,.5)
    for c in P.hosts('SkillSlot'):
        r=P.rect(P.ref(P.component(c,'SkillSlot'),'artwork'))
        assert vec(P.value(r,'m_SizeDelta'))==(-10,-10)
    for path in FRAMES.glob('satchel_*.png'):
        im=Image.open(path).convert('RGBA');a=im.getchannel('A');meta=Path(str(path)+'.meta').read_text()
        assert set(a.tobytes())<={0,255};assert 'filterMode: 0' in meta and 'isReadable: 1' in meta
        assert 'textureCompression: 1' not in meta
        report['icons'].append({'file':str(path.relative_to(ROOT)),'size':im.size,'alpha':'binary transparent'})
    (OUT/'static_review.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=True))

if __name__=='__main__':
    for dims in [(10,5),(7,7),(6,8),(8,6)]:inventory(*dims)
    fusion_rows();inspect()
