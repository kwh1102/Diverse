"""Static prefab geometry/PNG review, not a game screenshot or runtime test."""
from pathlib import Path
import json
import re
from PIL import Image, ImageDraw, ImageFont
from patch_part_readability import ROOT,OUT,Prefab

P=Prefab(ROOT/'Assets/_Project/Prefabs/UI/GameCanvas.prefab')
SCALE=3
FONT=ROOT/'Assets/_Project/Resources/Fonts/Galmuri11.ttf'
BOLD=ROOT/'Assets/_Project/Resources/Fonts/Galmuri11-Bold.ttf'
SPRITES=ROOT/'Assets/_Project/Resources/Sprites/Parts'

def vec(value):return tuple(float(x) for x in re.findall(r'[xy]: (-?[\d.]+)',value))

def rect(node):
    x,y=vec(P.value(node,'m_AnchoredPosition'));w,h=vec(P.value(node,'m_SizeDelta'));return x,-y,w,h

class Review:
    def __init__(self):
        self.im=Image.new('RGBA',(640*SCALE,360*SCALE),'#353541');self.d=ImageDraw.Draw(self.im);self.d.fontmode='1'
    def box(self,r,fill='#282936',border='#8d7c72',thick=2):
        x,y,w,h=r;self.d.rectangle((round(x*SCALE),round(y*SCALE),round((x+w)*SCALE-1),round((y+h)*SCALE-1)),fill=fill,outline=border,width=thick*SCALE)
    def text(self,xy,text,size=9,bold=False,color='#e9d8ae'):
        self.d.text(tuple(round(x*SCALE) for x in xy),text,font=ImageFont.truetype(str(BOLD if bold else FONT),size*SCALE),fill=color,spacing=2*SCALE)
    def icon(self,id,r):
        path=SPRITES/(id+'.png') if '_t' not in id else SPRITES/'Evolutions'/(id+'.png')
        im=Image.open(path).convert('RGBA');x,y,w,h=r;im=im.resize((round(w*SCALE),round(h*SCALE)),Image.Resampling.NEAREST)
        self.im.alpha_composite(im,(round(x*SCALE),round(y*SCALE)))
    def bar(self,r,horizontal=False):
        x,y,w,h=r
        if horizontal:self.box((x+4,y+h-6,w-8,4),'#181521','#181521',1);self.box((x+4,y+h-6,(w-8)*.38,4),'#baa5d6','#baa5d6',1)
        else:self.box((x+w-6,y+4,4,h-8),'#181521','#181521',1);self.box((x+w-6,y+4,4,(h-8)*.42),'#baa5d6','#baa5d6',1)
    def save(self,name):self.im.convert('RGB').save(OUT/name)

def inventory(width,height):
    v=Review();host=P.host('InventoryView');panel=P.find(host,'Panel')
    def area(path):
        x,y,w,h=rect(P.find(panel,path));return x,y+4,w,h
    for path in ['HeaderBar','EquipmentBox','WorkshopBox','Bag','ItemBox','PendingBox']:v.box(area(path),fill='#4d3342' if path=='Bag' else '#282936')
    hx,hy,hw,hh=area('HeaderBar');v.text((hx+10,hy+3),'인벤토리',12)
    v.box((hx+262,hy+2,72,20),'#69554d','#c5ab78');v.text((hx+268,hy+6),'닫기 [I]',9)
    ex,ey,ew,eh=area('EquipmentBox');v.text((ex+8,ey+8),'장착 정보',11)
    v.text((ex+8,ey+30),'대검\n검 · 사용 중인 무기\n\n무기 1/4\n보유 골드 64G\n\n표시된 QWER 칸\n같은 유형·같은 슬롯\n◆ 흑요석 주사위\n◆ 바람 장화\n◆ 깃털 고리',8)
    v.bar((ex+8,ey+29,96,153))
    wx,wy,ww,wh=area('WorkshopBox');v.box((wx+8,wy+8,96,22),'#69554d','#c5ab78');v.text((wx+12,wy+15),'파츠 공방 열기',9)
    v.text((wx+8,wy+40),'드래그로 이동\n빈 칸 클릭: 선택 해제\n유형별 색상\n두꺼운 외곽선:\n사용 중인 무기',8,color='#b6a5cb')
    bx,by,bw,bh=area('Bag');v.box((bx+151,by-12,40,16),'#4d3342','#a17d65');v.text((bx+12,by+6),f'가방 · {width}×{height}',9,bold=True)
    grid=P.find(panel,'Bag/GridBounds/Grid');cell=vec(P.value(P.component(grid,'GridLayoutGroup'),'m_CellSize'))[0]
    gx,gy=(bx+12+(318-(width*32-2))/2,by+18+(254-(height*32-2))/2)
    assert cell==30 and width*32-2<=318 and height*32-2<=254
    for y in range(height):
        for x in range(width):v.box((gx+x*32,gy+y*32,30,30),'#262734','#8d7c72')
    for x,y,id in [(1,0,'lucky_die'),(0,1,'wind_boot'),(2,1,'feather_ring')]:
        v.icon(id,(gx+x*32+3,gy+y*32+1,24,24));v.text((gx+x*32+18,gy+y*32+22),'T1',7)
    ix,iy,iw,ih=area('ItemBox');v.icon('lucky_die',(ix+8,iy+12,36,36));v.text((ix+50,iy+8),'흑요석\n주사위',10,bold=True);v.text((ix+50,iy+42),'파츠 · T1',8,color='#b6a5cb')
    v.text((ix+10,iy+68),'고유 능력\n명중한 평타를 세어\n5번째마다 확정 치명타.\n그 타격의 치명타 피해\n+10%.',9)
    v.bar((ix+8,iy+62,iw-16,190))
    v.box((ix+8,iy+ih-28,iw-16,20),'#69554d','#c5ab78');v.text((ix+44,iy+ih-23),'버리기',9)
    px,py,pw,ph=area('PendingBox');v.text((px+5,py+1),'수령 대기 5 · 끌어서 가방에 넣기',8);v.bar((px+4,py+11,pw-8,20),True)
    v.text((4,354),'정적 프리팹 배치 미리보기 · 게임 실행 없음',6,color='#c6bfd0');v.save(f'inventory_{width}x{height}.png')

def cards():
    v=Review();v.box((80,42,480,272),'#353541','#c5ab78');v.text((260,52),'성장 보상',16,bold=True)
    v.text((96,82),'초반 파츠 선택 · 평타와 이동·방어로 활용하는 파츠',9,color='#b6a5cb')
    for i,(id,name,body) in enumerate([('lucky_die','흑요석 주사위','명중한 평타를 세어\n5번째마다 확정 치명타.\n그 타격의 치명타 피해\n+10%.\n\n같은 등급끼리 합성'),('iron_plate','철갑 조각','제자리에서 0.8초 버티면\n다음에 받는 공격 피해\n20% 감소.\n재발동 대기 6초.\n\n같은 등급끼리 합성'),('wind_boot','바람 장화','3칸 이동하면 바람 충전.\n4초 안에 다음 평타\n첫 적중 피해 +30%.\n\n같은 등급끼리 합성')]):
        x=92+i*152;y=112;v.box((x,y,143,170));v.text((x+6,y+4),f'{i+1} ◆ 고정 목록',8,color='#b6a5cb')
        v.box((x+6,y+21,36,36));v.icon(id,(x+10,y+25,28,28));v.text((x+48,y+29),name,9,bold=True)
        v.text((x+8,y+69),'고유 능력\n'+body.split('\n\n')[0],8);v.bar((x+6,y+64,131,70))
        v.text((x+6,y+140),'등급 T1 · 고정 파츠',8);v.text((x+6,y+156),'PART T1',7,color='#b6a5cb')
    v.text((80,326),'정적 프리팹 배치 미리보기 · 게임 실행 없음',7);v.save('reward_cards.png')

def fusion():
    v=Review();v.box((10,10,620,340),'#353541','#c5ab78');v.text((274,18),'파츠 합성',16,bold=True)
    v.text((205,43),'합성 가능 · 확정할 때만 재료와 골드 소모',8,color='#b6a5cb')
    v.box((22,70,184,234));v.text((30,80),'합성할 파츠',10,bold=True)
    for i,(id,name) in enumerate([('feather_ring','깃털 고리'),('lucky_die','흑요석 주사위'),('wind_boot','바람 장화')]):
        y=102+i*42;v.box((28,y,166,38));v.icon(id,(30,y+4,28,28));v.text((65,y+8),name+'\n등급 T1',8)
    v.bar((22,70,184,234))
    for y,id,name,text,letter in [(70,'feather_ring','깃털 고리','완벽한 회피에 성공하면\nSpace 이동 충전 시간을\n1.2초 회복. 재발동 대기 4초.','A'),(190,'lucky_die','흑요석 주사위','명중한 평타를 세어 5번째마다\n확정 치명타. 그 타격의\n치명타 피해 +10%.','B')]:
        v.box((218,y,170,114));v.icon(id,(228,y+8,32,32));v.text((266,y+10),name+'\n등급 T1',9)
        v.text((228,y+46),text,8);v.bar((226,y+44,154,39));v.box((228,y+88,150,18),'#69554d','#c5ab78');v.text((258,y+92),f'재료 {letter} 바꾸기',8)
    v.box((400,70,218,234));v.icon('critical_movement_t2',(485,78,48,48));v.text((410,138),'달그림자 고리',11,bold=True)
    v.text((410,160),'고유 능력 · 재료에서 계승',9,bold=True)
    v.text((410,176),'깃털 고리\n완벽한 회피에 성공하면 이동 충전 회복.\n\n흑요석 주사위\n5번째 명중한 평타는 확정 치명타.\n\n진화 효과 · 발동 조건\n조건을 모두 만족할 때 수동 시전 강화.',8)
    v.bar((408,134,202,162))
    for x,w,text in [(22,90,'가방으로'),(400,126,'합성 확정'),(534,84,'결과 취소')]:v.box((x,316,w,22),'#69554d','#c5ab78');v.text((x+12,322),text,9)
    v.text((10,352),'정적 프리팹 배치 미리보기 · 게임 실행 없음',6);v.save('fusion.png')

if __name__=='__main__':
    for dims in [(10,5),(7,7),(6,8),(8,6)]:inventory(*dims)
    cards();fusion()
    print('Saved 4 inventory shapes, reward and fusion static layout previews from serialized sizes.')
