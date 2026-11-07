"""Patch existing serialized UI assets in place without launching Unity.

Retains all existing object IDs; new UI nodes clone working serialized UI components.
"""
from pathlib import Path
from copy import deepcopy
import hashlib
import json
import re
import sys

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'ArtSource/Parts/Readability'

class Prefab:
    def __init__(self,path):
        self.path=path;text=path.read_text(encoding='utf-8')
        self.prefix=text.split('--- !u!',1)[0]
        self.docs={};self.types={};self.order=[]
        for m in re.finditer(r'^--- !u!(\d+) &(\d+)\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S):
            id=int(m[2]);self.types[id]=int(m[1]);self.docs[id]=m[3];self.order.append(id)
        self.scripts={re.search(r'^guid: (.+)$',p.read_text(),re.M)[1]:p.name[:-8] for p in (ROOT/'Assets/_Project/Scripts').rglob('*.cs.meta')}
        self.initial_ids=set(self.docs)
    def value(self,id,key,default=None):
        m=re.search(r'^  '+re.escape(key)+r': (.*)$',self.docs[id],re.M)
        return m[1] if m else default
    def set(self,id,key,value):
        pattern=r'^  '+re.escape(key)+r': [^\n]*'
        line='  '+key+': '+str(value)
        if re.search(pattern,self.docs[id],re.M):self.docs[id]=re.sub(pattern,lambda _:line,self.docs[id],flags=re.M)
        else:self.docs[id]=self.docs[id].rstrip()+'\n'+line+'\n'
    def ref(self,id,key):
        value=self.value(id,key,'{fileID: 0}');return int(re.search(r'fileID: (\d+)',value)[1])
    def refs(self,id,key):
        m=re.search(r'^  '+key+r':\n((?:  -[^\n]*\n)*)',self.docs[id],re.M)
        return [int(x) for x in re.findall(r'fileID: (\d+)',m[1])] if m else []
    def set_refs(self,id,key,values):
        body='  '+key+':'+ ('\n'+''.join('  - {fileID: '+str(x)+'}\n' for x in values) if values else ' []\n')
        self.docs[id]=re.sub(r'^  '+key+r':(?: \[\]\n|\n(?:  -[^\n]*\n)*)',lambda _:body,self.docs[id],flags=re.M)
    def go(self,id):return id if self.types[id]==1 else self.ref(id,'m_GameObject')
    def rect(self,id):
        go=self.go(id)
        for x in self.components(go):
            if self.types[x]==224:return x
        raise ValueError('No RectTransform: '+str(id))
    def components(self,id):return [int(x) for x in re.findall(r'component: \{fileID: (\d+)\}',self.docs[self.go(id)])]
    def kind(self,id):
        d=self.docs[id]
        if self.types[id]!=114:return str(self.types[id])
        guid=re.search(r'm_Script: \{fileID: \d+, guid: ([^,]+)',d)
        if guid and guid[1] in self.scripts:return self.scripts[guid[1]]
        for key,name in [('m_VerticalScrollbar:','ScrollRect'),('m_NumberOfSteps:','Scrollbar'),('m_FontData:','Text'),('m_FillMethod:','Image'),('m_Softness:','RectMask2D'),('m_VerticalFit:','ContentSizeFitter'),('m_CellSize:','GridLayoutGroup'),('m_ChildControlHeight:','LayoutGroup'),('m_IgnoreLayout:','LayoutElement')]:
            if key in d:return name
        return 'Other'
    def component(self,id,kind):return next((x for x in self.components(id) if self.kind(x)==kind),None)
    def name(self,id):return self.value(self.go(id),'m_Name','')
    def children(self,id):return self.refs(self.rect(id),'m_Children')
    def find(self,id,path):
        for name in path.split('/'):
            id=next((x for x in self.children(id) if self.name(x)==name),None)
            if id is None:return None
        return self.rect(id)
    def host(self,kind):return self.rect(next(x for x in self.docs if self.kind(x)==kind))
    def hosts(self,kind):return [self.rect(x) for x in list(self.docs) if self.kind(x)==kind]
    def path_of(self,id):
        r=self.rect(id);p=self.ref(r,'m_Father')
        return (self.path_of(p)+'/' if p else '')+self.name(r)
    def new_id(self,label):
        id=int(hashlib.sha256(('PartReadability:'+label).encode()).hexdigest()[:15],16)
        while id in self.docs:id+=1
        return id
    def clone(self,source,parent,name,origin=None):
        original=origin or self
        nodes=[]
        def walk(r):
            nodes.append(original.go(r));nodes.extend(original.components(r))
            for child in original.children(r):walk(child)
        walk(original.rect(source));mapping={id:self.new_id(self.path_of(parent)+'/'+name+'/'+str(id)) for id in nodes}
        for old in nodes:
            id=mapping[old]
            self.docs[id]=re.sub(r'fileID: (\d+)',lambda m:'fileID: '+str(mapping.get(int(m[1]),int(m[1]))),original.docs[old])
            self.types[id]=original.types[old];self.order.append(id)
        r=mapping[original.rect(source)];self.set(self.go(r),'m_Name',name);self.set(r,'m_Father','{fileID: '+str(self.rect(parent))+'}')
        self.set_refs(self.rect(parent),'m_Children',self.children(parent)+[r])
        return r
    def reparent(self,node,parent):
        r=self.rect(node);old=self.ref(r,'m_Father')
        if old==self.rect(parent):return
        self.set_refs(old,'m_Children',[x for x in self.children(old) if x!=r])
        self.set_refs(self.rect(parent),'m_Children',self.children(parent)+[r])
        self.set(r,'m_Father','{fileID: '+str(self.rect(parent))+'}')
    def area(self,node,x,y,w,h,parent=None):
        if parent:self.reparent(node,parent)
        r=self.rect(node)
        for key,val in dict(m_AnchorMin='{x: 0, y: 1}',m_AnchorMax='{x: 0, y: 1}',m_Pivot='{x: 0, y: 1}',m_AnchoredPosition=f'{{x: {x}, y: {-y}}}',m_SizeDelta=f'{{x: {w}, y: {h}}}',m_LocalScale='{x: 1, y: 1, z: 1}').items():self.set(r,key,val)
    def stretch(self,node,left=0,top=0,right=0,bottom=0):
        r=self.rect(node)
        for k,v in dict(m_AnchorMin='{x: 0, y: 0}',m_AnchorMax='{x: 1, y: 1}',m_Pivot='{x: 0.5, y: 0.5}',m_AnchoredPosition=f'{{x: {(left-right)/2}, y: {(bottom-top)/2}}}',m_SizeDelta=f'{{x: {-left-right}, y: {-top-bottom}}}').items():self.set(r,k,v)
    def text_style(self,text,size=None):
        if size:
            self.docs[text]=re.sub(r'^(    m_FontSize: ).*$',lambda m:m[1]+str(size),self.docs[text],flags=re.M)
            self.docs[text]=re.sub(r'^(    m_MaxSize: ).*$',lambda m:m[1]+str(size),self.docs[text],flags=re.M)
        self.docs[text]=re.sub(r'^(    m_BestFit: ).*$',lambda m:m[1]+'0',self.docs[text],flags=re.M)
        self.docs[text]=re.sub(r'^(    m_HorizontalOverflow: ).*$',lambda m:m[1]+'0',self.docs[text],flags=re.M)
    def write(self):
        text=self.prefix+''.join(f'--- !u!{self.types[id]} &{id}\n'+self.docs[id] for id in self.order)
        with self.path.open('w',encoding='utf-8',newline='\r\n') as f:f.write(text)
    def validate(self):
        assert self.initial_ids.issubset(self.docs)
        for id in self.docs:
            if self.types[id]==224:
                for child in self.children(id):assert child in self.docs and self.ref(child,'m_Father')==id,(id,child)
            for match in re.finditer(r'\{fileID: (\d+)\}',self.docs[id]):
                ref=int(match[1]);assert ref==0 or ref in self.docs,(id,ref)

def describe(p):
    for kind in ['InventoryView','PartFusionView','EvolveCard']:
        for host in p.hosts(kind):
            print(kind,p.path_of(host))
            def walk(node,depth=0):
                print(' '*depth,p.name(node),node,p.value(node,'m_AnchoredPosition'),p.value(node,'m_SizeDelta'),[(p.kind(c),c) for c in p.components(node) if p.types[c]==114])
                if depth<4:
                    for child in p.children(node):walk(child,depth+1)
            walk(host)

def add_bars(p,scroll,template):
    area=p.rect(scroll);view=p.ref(scroll,'m_Viewport');content=p.ref(scroll,'m_Content')
    if not view or not content or p.value(scroll,'m_Enabled')=='0':return
    vertical=p.value(scroll,'m_Vertical')=='1';horizontal=p.value(scroll,'m_Horizontal')=='1'
    for enabled,key,name,direction in [(vertical,'m_VerticalScrollbar','Scrollbar',2),(horizontal,'m_HorizontalScrollbar','HorizontalScrollbar',0)]:
        if not enabled:continue
        bar=p.ref(scroll,key)
        if not bar:
            root=p.clone(template,area,name);bar=p.component(root,'Scrollbar');p.set(scroll,key,f'{{fileID: {bar}}}')
        rail=p.rect(bar);p.set(p.go(rail),'m_IsActive',1);p.set(bar,'m_Enabled',1);p.set(bar,'m_Direction',direction)
        for image in [p.component(rail,'Image'),p.component(p.ref(bar,'m_HandleRect'),'Image')]:
            if image:p.set(image,'m_RaycastTarget',1)
        p.set(p.component(rail,'Image'),'m_Color','{r: 0.12, g: 0.1, b: 0.16, a: 1}')
        thumb=p.ref(bar,'m_HandleRect');p.set(p.component(thumb,'Image'),'m_Color','{r: 0.75, g: 0.65, b: 0.86, a: 1}')
        p.stretch(thumb)
        for k,v in dict(m_AnchorMin='{x: 1, y: 0}' if vertical and key=='m_VerticalScrollbar' else '{x: 0, y: 0}',m_AnchorMax='{x: 1, y: 1}' if key=='m_VerticalScrollbar' else '{x: 1, y: 0}',m_Pivot='{x: 1, y: 0.5}' if key=='m_VerticalScrollbar' else '{x: 0.5, y: 0}',m_AnchoredPosition='{x: -2, y: 0}' if key=='m_VerticalScrollbar' else '{x: 0, y: 2}',m_SizeDelta='{x: 4, y: -8}' if key=='m_VerticalScrollbar' else '{x: -8, y: 4}').items():p.set(rail,k,v)
        p.set(scroll,'m_VerticalScrollbarVisibility' if key=='m_VerticalScrollbar' else 'm_HorizontalScrollbarVisibility',1)
        p.set(scroll,'m_VerticalScrollbarSpacing' if key=='m_VerticalScrollbar' else 'm_HorizontalScrollbarSpacing',2)
    if view!=area:p.stretch(view,2,2,10 if vertical else 2,8 if horizontal else 2)
    mask=p.component(view,'RectMask2D')
    if mask:p.set(mask,'m_Softness','{x: 0, y: 0}')
    p.set(scroll,'m_MovementType',2);p.set(scroll,'m_ScrollSensitivity',18)
    # Text content follows the viewport width, including newly reserved scrollbar padding.
    text=p.component(content,'Text')
    if vertical and not horizontal:
        p.set(content,'m_AnchorMin','{x: 0, y: 1}');p.set(content,'m_AnchorMax','{x: 1, y: 1}');p.set(content,'m_Pivot','{x: 0, y: 1}')
        p.set(content,'m_AnchoredPosition','{x: 0, y: 0}');p.set(content,'m_SizeDelta','{x: 0, y: 0}')
    if text:
        p.set(content,'m_AnchorMin','{x: 0, y: 1}');p.set(content,'m_AnchorMax','{x: 1, y: 1}');p.set(content,'m_Pivot','{x: 0, y: 1}')
        p.set(content,'m_AnchoredPosition','{x: 0, y: 0}');p.set(content,'m_SizeDelta','{x: 0, y: 0}')
        p.text_style(text);p.docs[text]=re.sub(r'^(    m_VerticalOverflow: ).*$',lambda m:m[1]+'1',p.docs[text],flags=re.M)

def apply(p):
    if not any(p.kind(x)=='ScrollRect' for x in p.docs):
        # Remove only this utility's unused prototype if a canvas has no scrollable surfaces.
        for node in [x for x in list(p.docs) if p.types[x]==224 and p.name(x)=='ScrollbarTemplate']:
            parent=p.ref(node,'m_Father');p.set_refs(parent,'m_Children',[x for x in p.children(parent) if x!=node])
            def remove(r):
                for child in p.children(r):remove(child)
                for id in p.components(r)+[p.go(r)]:p.docs.pop(id);p.types.pop(id);p.order.remove(id)
            remove(node)
        p.write();return dict(prefab=str(p.path.relative_to(ROOT)),scroll_regions=0,new_objects=0)
    template=next((x for x in p.docs if p.kind(x)=='Scrollbar'),None)
    if template:template_bar=p.rect(template)
    else:
        origin=Prefab(ROOT/'Assets/_Project/Prefabs/UI/GameCanvas.prefab')
        root=next(x for x in p.docs if p.types[x]==224 and p.ref(x,'m_Father')==0)
        template_bar=p.clone(origin.rect(next(x for x in origin.docs if origin.kind(x)=='Scrollbar')),root,'ScrollbarTemplate',origin)
        p.set(p.go(template_bar),'m_IsActive',0)
    if p.hosts('PartFusionView'):
        fusion=p.host('PartFusionView');panel=p.find(fusion,'Panel');view=p.component(fusion,'PartFusionView')
        original_scroll=p.find(panel,'Result/ResultDescription')
        # Material cards grow into their unused vertical gaps while the result remains a separate column.
        for letter,y,field in [('A',60,'firstTraits'),('B',180,'secondTraits')]:
            card=p.find(panel,'Material'+letter);p.area(card,208,y,170,114)
            p.area(p.find(card,'Icon'),10,8,32,32);p.area(p.find(card,'Description'),48,8,112,32)
            button=p.find(card,'Choose'+letter);p.area(button,10,88,150,18)
            scroll_area=p.find(card,'TraitsScroll') or p.clone(original_scroll,card,'TraitsScroll')
            p.area(scroll_area,8,44,154,39)
            scroll=p.component(scroll_area,'ScrollRect');body=p.component(p.ref(scroll,'m_Content'),'Text')
            p.text_style(body,8);p.set(body,'m_Text',json.dumps('선택한 재료의 고유 능력',ensure_ascii=True))
            p.set(view,field,f'{{fileID: {body}}}')
        p.area(p.find(panel,'Result/Icon'),85,8,48,48)
        p.area(original_scroll,8,64,202,162)
    if p.hosts('InventoryView'):
        inv=p.host('InventoryView');panel=p.find(inv,'Panel');view=p.component(inv,'InventoryView')
        p.set(panel,'m_SizeDelta','{x: 640, y: 352}')
        for path,rect in {
            'HeaderBar':(124,0,342,24),'HeaderBar/Heading':(10,1,242,22),'HeaderBar/Close':(262,2,72,20),
            'EquipmentBox':(6,48,112,190),'EquipmentBox/Heading':(8,7,96,16),
            'WorkshopBox':(6,244,112,108),'WorkshopBox/OpenFusion':(8,8,96,22),'WorkshopBox/Status':(8,38,96,62),
            'Bag':(124,38,342,276),'Bag/Title':(12,6,318,10),'Bag/Handle':(151,-12,40,16),
            'Bag/Clasp0':(-3,126,7,20),'Bag/Clasp1':(338,126,7,20),'Bag/GridBounds':(12,18,318,254),
            'ItemBox':(474,48,160,304),'ItemBox/ItemHeader/Name':(42,0,102,30),'ItemBox/ItemHeader/TypeTier':(42,32,102,14),
            'PendingBox':(124,318,342,34),'PendingBox/PendingLabel':(5,1,332,9),'PendingBox/Pending':(4,11,334,20),
        }.items():p.area(p.find(panel,path),*rect)
        grid=p.find(panel,'Bag/GridBounds/Grid');layout=p.component(grid,'GridLayoutGroup');p.set(layout,'m_CellSize','{x: 30, y: 30}')
        template=p.rect(p.ref(view,'cellTemplate'));p.set(template,'m_SizeDelta','{x: 30, y: 30}')
        p.area(p.find(template,'Icon'),3,1,24,24);p.area(p.find(template,'SkillGlyph'),3,1,24,24);p.area(p.find(template,'Caption'),1,22,28,8)
        ghost=p.find(inv,'DragGhost');p.set(ghost,'m_SizeDelta','{x: 36, y: 36}')
        for name in ['Icon','Glyph']:p.area(p.find(ghost,name),2,2,32,32)
        pending=p.rect(p.ref(view,'pendingTemplate'));p.set(pending,'m_SizeDelta','{x: 110, y: 16}')
        pending_content=p.find(panel,'PendingBox/Pending/Content');p.set(pending_content,'m_SizeDelta','{x: 518, y: 16}')
        for path,rect in {'Icon':(2,2,12,12),'Glyph':(2,2,12,12),'Name':(18,0,88,16)}.items():p.area(p.find(pending,path),*rect)
        # The equipment list can grow with attached parts; retain it in a bounded scroll area.
        box=p.find(panel,'EquipmentBox');scroll_area=p.find(box,'SummaryScroll')
        if not scroll_area:
            scroll_area=p.clone(p.find(p.host('PartFusionView'),'Panel/Result/ResultDescription'),box,'SummaryScroll')
        p.area(scroll_area,8,29,96,153);scroll=p.component(scroll_area,'ScrollRect');body=p.component(p.ref(scroll,'m_Content'),'Text')
        p.set(view,'equipmentSummary',f'{{fileID: {body}}}');p.text_style(body,8)
        p.set(p.go(p.find(box,'Summary')),'m_IsActive',0)
        # Vertical layout controls this column; clip neither long names nor action buttons.
        for node in [p.find(panel,'ItemBox/ItemHeader/Name'),p.find(panel,'ItemBox/ItemHeader/TypeTier')]:
            text=p.component(node,'Text');p.set(text,'m_RaycastTarget',0)
    for card in p.hosts('EvolveCard'):
        component=p.component(card,'EvolveCard');icon=p.ref(component,'partIcon');p.area(icon,4,4,28,28)
        body=p.ref(component,'desc');active=p.ref(p.rect(body),'m_Father')
        while active and not p.component(active,'ScrollRect'):active=p.ref(active,'m_Father')
        if active:
            # Old serialized empty scroll roots overlap the live one; deactivate them in place.
            for child in p.children(card):
                if p.component(child,'ScrollRect'):
                    enabled=child==active;p.set(p.component(child,'ScrollRect'),'m_Enabled',int(enabled));p.set(p.go(child),'m_IsActive',int(enabled))
            width=float(re.search(r'x: ([\d.]+)',p.value(card,'m_SizeDelta'))[1])-12
            p.area(active,6,64,width,70)
            content=p.ref(p.component(active,'ScrollRect'),'m_Content');p.set(content,'m_AnchoredPosition','{x: 0, y: 0}')
    bars=0
    for scroll in [x for x in list(p.docs) if p.kind(x)=='ScrollRect']:
        if p.value(scroll,'m_Enabled')=='0':continue
        add_bars(p,scroll,template_bar);bars+=1
    p.validate();p.write()
    return dict(prefab=str(p.path.relative_to(ROOT)),scroll_regions=bars,new_objects=len(p.docs)-len(p.initial_ids))

def audit(p):
    p.validate()
    before=Prefab(OUT/'Before/GameCanvas.prefab')
    assert set(before.docs).issubset(p.docs),'An existing prefab object was removed'
    inv=p.host('InventoryView');panel=p.find(inv,'Panel');grid=p.find(panel,'Bag/GridBounds/Grid')
    assert p.value(p.component(grid,'GridLayoutGroup'),'m_CellSize')=='{x: 30, y: 30}'
    for width,height in [(10,5),(7,7),(6,8),(8,6)]:assert width*32-2<=318 and height*32-2<=254
    for path in ['HeaderBar','EquipmentBox','WorkshopBox','Bag','ItemBox','PendingBox']:
        r=p.find(panel,path);x,y=map(float,re.findall(r'[xy]: (-?[\d.]+)',p.value(r,'m_AnchoredPosition')));w,h=map(float,re.findall(r'[xy]: (-?[\d.]+)',p.value(r,'m_SizeDelta')))
        assert x>=0 and -y>=0 and x+w<=640 and -y+h<=352,path
    fusion=p.component(p.host('PartFusionView'),'PartFusionView')
    for field in ['firstTraits','secondTraits']:
        target=p.ref(fusion,field);assert target and p.kind(target)=='Text',field
    active=[]
    for scroll in p.docs:
        if p.kind(scroll)!='ScrollRect' or p.value(scroll,'m_Enabled')!='1':continue
        assert p.ref(scroll,'m_Content') and p.ref(scroll,'m_Viewport')
        for flag,key in [('m_Vertical','m_VerticalScrollbar'),('m_Horizontal','m_HorizontalScrollbar')]:
            if p.value(scroll,flag)!='1':continue
            bar=p.ref(scroll,key);assert bar and p.kind(bar)=='Scrollbar',p.path_of(scroll)
            assert p.component(p.ref(bar,'m_HandleRect'),'Image'),p.path_of(scroll)
        active.append(p.path_of(scroll))
    result=dict(preserved_existing_objects=len(before.docs),new_objects=len(p.docs)-len(before.docs),scroll_regions=len(active),scroll_paths=active,inventory_cell=30,inventory_shapes=[[10,5],[7,7],[6,8],[8,6]],material_trait_references='connected',unity_started=False,compiled=False,tests_run=False)
    (OUT/'ui_validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='scroll_paths'}))

if __name__=='__main__':
    p=Prefab(ROOT/'Assets/_Project/Prefabs/UI/GameCanvas.prefab')
    if sys.argv[1]=='inspect':describe(p)
    elif sys.argv[1]=='apply':
        results=[apply(p)]
        menu=ROOT/'Assets/_Project/Prefabs/UI/MenuCanvas.prefab'
        if menu.exists():results.append(apply(Prefab(menu)))
        (OUT/'ui_patch.json').write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')
        print(json.dumps(results))
    elif sys.argv[1]=='audit':audit(p)
