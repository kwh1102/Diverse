using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public static partial class UIBuilder
    {
        // Asset authoring only: never enters play mode, executes checks, renders previews, or calls AI.
        [MenuItem("Diverse/인벤토리 시스템 반영")]
        public static void InstallInventory()
        {
            font=Resources.Load<Font>("Fonts/Galmuri11");fontBold=Resources.Load<Font>("Fonts/Galmuri11-Bold");BuildFrames();
            Patch(UIPrefabDir+"/GameCanvas.prefab",root=>
            {
                var ui=root.GetComponent<GameUI>();
                var inventory=root.GetComponentInChildren<InventoryView>(true);
                if(inventory==null) inventory=Stretch(root.transform,"Inventory").gameObject.AddComponent<InventoryView>();
                LayoutInventory(inventory);Wire(ui,"inventory",inventory);UIKit.Show(inventory,false);
                var fusion=root.GetComponentInChildren<PartFusionView>(true)??BuildPartFusion(root.transform);
                Wire(ui,"partFusion",fusion);UIKit.Show(fusion,false);
                var weapon=root.GetComponentInChildren<WeaponRewardView>(true)??BuildWeaponReward(root.transform);
                Wire(ui,"weaponReward",weapon);UIKit.Show(weapon,false);
                var hud=root.GetComponentInChildren<HudView>(true);
                var bagButton=hud.transform.Find("InventoryButton")?.GetComponent<Button>()??Btn(hud.transform,"InventoryButton","가방 [I]",TR,new Vector2(-8,180),new Vector2(76,20),10);
                Wire(ui,"inventoryButton",bagButton);
                LayoutPartCards(root);
                LayoutInventoryPolish(root);
                InstallLegacyChoice(root);
                AddStudioInventoryPreview(root);
                return true;
            });
            Patch(UIPrefabDir+"/MenuCanvas.prefab",root=>{AddStudioInventoryPreview(root);InstallHeroFacingButton(root);return true;});
            var db=GameDatabase.Load();db.inventory??=new InventoryRules();
            if(db.inventory.partIconPixels<=0) db.inventory.partIconPixels=32;
            if(db.inventory.partIconColors<=0) db.inventory.partIconColors=16;
            if(db.inventory.partImageParallel<=0) db.inventory.partImageParallel=3;
            EditorUtility.SetDirty(db);
            foreach(string kind in new[]{"heal","xp","cleanse"})
            {
                string icon="Assets/_Project/Resources/Sprites/inventory_"+kind+".png";
                if(!File.Exists(icon)) {File.WriteAllBytes(icon,PartArt.For(PartArt.Potion(kind)).texture.EncodeToPNG());ImportPicture(icon,Art.PPU);}
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Inventory, parts, fusion and weapon-studio prefab authoring saved. No tests or previews executed.");
        }

        static T InventoryFind<T>(Transform root,string name) where T:Component=>root.GetComponentsInChildren<T>(true).FirstOrDefault(c=>c.name==name);
        static T InventoryComponent<T>(GameObject obj) where T:Component
        {
            var component=obj.GetComponent<T>();
            return component!=null?component:obj.AddComponent<T>();
        }
        static void InventoryRect(RectTransform rt,Transform parent,float x,float y,float w,float h)
        {
            rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=rt.pivot=TL;rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);
        }
        static RectTransform InventoryBox(Transform parent,string name,float x,float y,float w,float h,Sprite sprite=null)
        {
            var rt=parent.Find(name) as RectTransform;
            if(rt==null) {rt=Node(parent,name,TL,new Vector2(x,y),new Vector2(w,h));Box(rt,sprite??panelSpr);}
            InventoryRect(rt,parent,x,y,w,h);return rt;
        }
        static Text InventoryText(Transform parent,string name,string value,float x,float y,float w,float h,int size=9,TextAnchor align=TextAnchor.UpperLeft)
        {
            var text=parent.Find(name)?.GetComponent<Text>()??Label(parent,name,value,TL,new Vector2(x,y),new Vector2(w,h),size,align);
            InventoryRect(text.rectTransform,parent,x,y,w,h);text.text=value;text.fontSize=size;text.alignment=align;
            text.resizeTextForBestFit=true;text.resizeTextMinSize=7;text.resizeTextMaxSize=size;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        static Button InventoryButton(Transform search,Transform parent,string name,string value,float x,float y,float w,float h)
        {
            var button=InventoryFind<Button>(search,name)??Btn(parent,name,value,TL,new Vector2(x,y),new Vector2(w,h),9);
            InventoryRect(button.GetComponent<RectTransform>(),parent,x,y,w,h);UIKit.Label(button,value);
            var text=button.GetComponentInChildren<Text>(true);text.resizeTextForBestFit=true;text.resizeTextMinSize=7;text.resizeTextMaxSize=9;text.horizontalOverflow=HorizontalWrapMode.Wrap;
            var styling=button.GetComponent<GameButtonStyle>();if(styling!=null) styling.enabled=false;
            UIKit.Show(button.transform.Find("Category Glow")?.gameObject,false);
            button.image.sprite=buttonSpr;button.image.overrideSprite=null;button.transition=Selectable.Transition.ColorTint;
            var colors=ColorBlock.defaultColorBlock;colors.disabledColor=new Color(.45f,.45f,.52f,.7f);button.colors=colors;
            var group=InventoryComponent<CanvasGroup>(button.gameObject);
            var state=InventoryComponent<ButtonVisualState>(button.gameObject);Wire(state,"button",button);Wire(state,"group",group);
            button.gameObject.SetActive(true);return button;
        }
        static Image InventoryPicture(Transform parent,string name,float x,float y,float w,float h)
        {
            var image=parent.Find(name)?.GetComponent<Image>()??Pic(parent,name,TL,new Vector2(x,y),new Vector2(w,h),null);
            InventoryRect(image.rectTransform,parent,x,y,w,h);image.raycastTarget=false;return image;
        }
        static Text InventoryScroll(Transform search,Transform parent,string name,float x,float y,float w,float h)
        {
            var scroll=InventoryFind<ScrollRect>(search,name);
            Text text;
            if(scroll==null) text=ScrollText(parent,name,Node(parent,name,TL,new Vector2(x,y),new Vector2(w,h)),out scroll);
            else text=scroll.content.GetComponent<Text>()??scroll.content.GetComponentInChildren<Text>(true);
            InventoryRect(scroll.GetComponent<RectTransform>(),parent,x,y,w,h);text.fontSize=9;return text;
        }
        static RectTransform InventoryOutline(Transform parent,string name,Color color)
        {
            var rt=parent.Find(name) as RectTransform??Node(parent,name,TL,Vector2.zero,new Vector2(24,24));
            var layout=rt.GetComponent<LayoutElement>()??rt.gameObject.AddComponent<LayoutElement>();layout.ignoreLayout=true;
            void Edge(string id,Vector2 min,Vector2 max,Vector2 size)
            {
                var edge=rt.Find(id) as RectTransform??Stretch(rt,id);
                edge.anchorMin=min;edge.anchorMax=max;edge.pivot=new Vector2(.5f,.5f);edge.sizeDelta=size;edge.anchoredPosition=Vector2.zero;
                var image=edge.GetComponent<Image>()??Box(edge,whiteSpr,color);image.color=color;image.raycastTarget=false;
            }
            Edge("Top",new Vector2(0,1),Vector2.one,new Vector2(0,2));Edge("Bottom",Vector2.zero,new Vector2(1,0),new Vector2(0,2));
            Edge("Left",Vector2.zero,new Vector2(0,1),new Vector2(2,0));Edge("Right",new Vector2(1,0),Vector2.one,new Vector2(2,0));
            return rt;
        }

        static void LayoutInventory(InventoryView view)
        {
            var host=view.transform;
            var panel=host.Find("Panel") as RectTransform??Panel(host,"Panel",640,340);
            panel.anchorMin=panel.anchorMax=panel.pivot=C;panel.anchoredPosition=Vector2.zero;panel.sizeDelta=new Vector2(640,340);
            var outer=panel.GetComponent<Image>();if(outer!=null) outer.enabled=false;
            foreach(string obsolete in new[]{"MaterialA","MaterialB","Fuse","Confirm","Cancel","WeaponPrompt","Help"}) UIKit.Show(InventoryFind<RectTransform>(host,obsolete),false);
            var inventoryHeading=panel.Find("Heading")?.GetComponent<Text>()??panel.Find("HeaderBar/Heading")?.GetComponent<Text>();
            if(inventoryHeading==null) InventoryText(panel,"Heading","인벤토리",8,5,400,20,15);
            var title=InventoryFind<Text>(host,"Title")??InventoryText(panel,"Title","대시형 · 7×7",146,30,304,15,10,TextAnchor.MiddleCenter);
            var close=InventoryButton(host,panel,"Close","닫기",572,6,60,20);
            var left=InventoryBox(panel,"EquipmentBox",6,60,132,180);
            InventoryText(left,"Heading","장착 정보",8,7,116,16,11);
            var summary=InventoryText(left,"Summary","사용 중인 무기",8,29,116,143,9);
            var tools=InventoryBox(panel,"WorkshopBox",6,246,132,90);
            var workshop=InventoryButton(host,tools,"OpenFusion","파츠 공방 열기",8,8,116,22);
            var status=InventoryFind<Text>(host,"Status")??InventoryText(tools,"Status","",8,38,116,45,8);
            InventoryRect(status.rectTransform,tools,8,38,116,45);status.fontSize=8;status.resizeTextForBestFit=true;status.resizeTextMinSize=7;status.resizeTextMaxSize=8;status.verticalOverflow=VerticalWrapMode.Truncate;
            var bagSprite=Frame("inventory_bag",Pal.Hex("4c303e"),Pal.Hex("745344"),Pal.Hex("ad8164"),255);
            var bag=InventoryBox(panel,"Bag",146,48,304,246,bagSprite);
            InventoryRect(title.rectTransform,bag,12,6,280,12);title.fontSize=9;
            string handlePath=UIDir+"/Frames/inventory_handle.png";
            if(!File.Exists(handlePath))
            {
                var pixels=new PixelCanvas(24,12);pixels.Rect(3,1,18,10,Pal.Hex("2a202c"));pixels.Rect(5,3,14,8,Pal.Hex("ad8164"));pixels.Rect(7,3,10,5,new Color32(0,0,0,0));
                pixels.Rect(2,0,5,3,Pal.Hex("bb9d83"));pixels.Rect(17,0,5,3,Pal.Hex("bb9d83"));
                var texture=pixels.ToTexture();File.WriteAllBytes(handlePath,texture.EncodeToPNG());Object.DestroyImmediate(texture);ImportPicture(handlePath,100);
            }
            var handle=InventoryPicture(bag,"Handle",132,-12,40,16);handle.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(handlePath);handle.enabled=true;
            for(int side=0;side<2;side++)
            {
                var clasp=InventoryPicture(bag,"Clasp"+side,side==0?-3:300,110,7,20);clasp.sprite=buttonSpr;clasp.type=Image.Type.Sliced;clasp.enabled=true;clasp.color=Pal.Hex("bb9d83");
            }
            var bounds=bag.Find("GridBounds") as RectTransform??Node(bag,"GridBounds",C,Vector2.zero,new Vector2(280,216),C);
            bounds.anchorMin=bounds.anchorMax=bounds.pivot=C;bounds.anchoredPosition=new Vector2(0,-3);bounds.sizeDelta=new Vector2(280,216);
            var grid=InventoryFind<GridLayoutGroup>(host,"Grid");
            if(grid==null) grid=Node(bounds,"Grid",C,Vector2.zero,new Vector2(180,180),C).gameObject.AddComponent<GridLayoutGroup>();
            var gridRt=(RectTransform)grid.transform;gridRt.SetParent(bounds,false);gridRt.anchorMin=gridRt.anchorMax=gridRt.pivot=C;gridRt.anchoredPosition=Vector2.zero;gridRt.sizeDelta=new Vector2(180,180);
            grid.cellSize=new Vector2(24,24);grid.spacing=new Vector2(2,2);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=7;grid.childAlignment=TextAnchor.UpperLeft;
            var cell=view.GetComponentInChildren<InventoryCell>(true);
            if(cell==null)
            {
                var rt=Node(gridRt,"CellTemplate",TL,Vector2.zero,new Vector2(24,24));Box(rt,slotSpr);rt.gameObject.AddComponent<Button>();cell=rt.gameObject.AddComponent<InventoryCell>();
            }
            cell.transform.SetParent(gridRt,false);
            var cellRt=(RectTransform)cell.transform;
            var selected=cell.transform.Find("Selected")?.GetComponent<Image>()??Box(Stretch(cellRt,"Selected",1,1,1,1),whiteSpr,new Color(.85f,.65f,1,.4f));selected.raycastTarget=false;
            var icon=InventoryPicture(cellRt,"Icon",2,2,20,20);
            var glyph=InventoryText(cellRt,"SkillGlyph","",2,2,20,20,15,TextAnchor.MiddleCenter);
            glyph.resizeTextMinSize=glyph.resizeTextMaxSize=15;
            var key=InventoryText(cellRt,"SkillKey","",1,0,10,8,7);key.color=UIKit.Highlight;
            var caption=InventoryText(cellRt,"Caption","",1,16,22,8,7,TextAnchor.LowerRight);caption.color=UIKit.Highlight;
            foreach(string edge in new[]{"Top","Bottom","Left","Right"}) UIKit.Show(cell.transform.Find(edge)?.gameObject,false);
            Wire(cell,"icon",icon);Wire(cell,"skillGlyph",glyph);Wire(cell,"skillKey",key);Wire(cell,"caption",caption);Wire(cell,"selection",selected);
            Wire(cell,"contents",InventoryComponent<CanvasGroup>(cell.gameObject));UIKit.Show(cell,false);
            var frame=InventoryFind<RectTransform>(host,"ActiveFrame")??InventoryOutline(gridRt,"ActiveFrame",new Color(.36f,1,.53f));
            frame.SetParent(gridRt,false);frame.anchorMin=frame.anchorMax=frame.pivot=TL;frame.anchoredPosition=Vector2.zero;frame.sizeDelta=new Vector2(76,76);
            var ignore=frame.GetComponent<LayoutElement>()??frame.gameObject.AddComponent<LayoutElement>();ignore.ignoreLayout=true;
            var marker=gridRt.Find("DropMarker") as RectTransform??Node(gridRt,"DropMarker",TL,Vector2.zero,new Vector2(24,24));
            (marker.GetComponent<LayoutElement>()??marker.gameObject.AddComponent<LayoutElement>()).ignoreLayout=true;
            var valid=InventoryOutline(marker,"Valid",Pal.Hex("69e99b"));var invalid=InventoryOutline(marker,"Invalid",Pal.Hex("ff657b"));
            foreach(var outline in new[]{valid,invalid}) {outline.anchorMin=Vector2.zero;outline.anchorMax=Vector2.one;outline.pivot=C;outline.offsetMin=outline.offsetMax=Vector2.zero;}
            var validFill=valid.GetComponent<Image>()??Box(valid,whiteSpr);validFill.color=new Color(.25f,1,.5f,.13f);validFill.raycastTarget=false;
            var invalidFill=invalid.GetComponent<Image>()??Box(invalid,whiteSpr);invalidFill.color=new Color(1,.2f,.3f,.2f);invalidFill.raycastTarget=false;
            UIKit.Show(marker,false);
            var right=InventoryBox(panel,"ItemBox",458,60,176,276);
            var preview=InventoryFind<Image>(host,"Preview")??InventoryPicture(right,"Preview",68,10,40,40);InventoryRect(preview.rectTransform,right,68,10,40,40);preview.raycastTarget=false;
            var previewGlyph=InventoryText(right,"PreviewGlyph","",68,10,40,40,24,TextAnchor.MiddleCenter);
            var details=InventoryScroll(host,right,"DetailsScroll",8,58,160,118);
            var actions=right.Find("Actions") as RectTransform??Node(right,"Actions",TL,new Vector2(10,180),new Vector2(156,86));
            var actionLayout=actions.GetComponent<VerticalLayoutGroup>()??actions.gameObject.AddComponent<VerticalLayoutGroup>();
            actionLayout.spacing=4;actionLayout.childControlWidth=false;actionLayout.childControlHeight=false;actionLayout.childForceExpandWidth=false;actionLayout.childForceExpandHeight=false;
            var equip=InventoryButton(host,actions,"Equip","이 무기 사용",0,0,156,22);
            var use=InventoryButton(host,actions,"Use","아이템 사용",0,0,156,22);
            var receive=InventoryButton(host,actions,"Receive","가방에 넣기",0,0,156,22);
            var redraw=InventoryButton(host,actions,"Redraw","스킬 그림 생성",0,0,156,22);
            var discard=InventoryButton(host,actions,"Discard","버리기",0,0,156,22);
            foreach(var b in new[]{equip,use,receive,redraw,discard}) UIKit.Show(b,false);
            var waiting=InventoryBox(panel,"PendingBox",146,300,304,36,slotSpr);
            var pendingLabel=InventoryFind<Text>(host,"PendingLabel")??InventoryText(waiting,"PendingLabel","수령 대기",5,1,294,11,8);
            InventoryRect(pendingLabel.rectTransform,waiting,5,1,294,11);pendingLabel.fontSize=8;
            var scroll=InventoryFind<ScrollRect>(host,"Pending");
            if(scroll==null)
            {
                var r=Node(waiting,"Pending",TL,new Vector2(4,14),new Vector2(296,20));Box(r,slotSpr);r.gameObject.AddComponent<RectMask2D>();scroll=r.gameObject.AddComponent<ScrollRect>();
                scroll.content=Node(r,"Content",TL,Vector2.zero,new Vector2(296,20));scroll.viewport=r;
                var layout=scroll.content.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=4;layout.childControlWidth=false;layout.childControlHeight=false;layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;
                scroll.content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            }
            InventoryRect((RectTransform)scroll.transform,waiting,4,14,296,20);scroll.horizontal=true;scroll.vertical=false;
            var pending=InventoryFind<Button>(host,"PendingTemplate")??Btn(scroll.content,"PendingTemplate","",TL,Vector2.zero,new Vector2(110,20),8);
            pending.transform.SetParent(scroll.content,false);
            pending.GetComponent<RectTransform>().sizeDelta=new Vector2(110,20);
            UIKit.Show(pending.transform.Find("Text")?.gameObject,false);
            var pendingView=pending.GetComponent<InventoryPendingItem>()??pending.gameObject.AddComponent<InventoryPendingItem>();
            var pendingIcon=InventoryPicture(pending.transform,"Icon",2,2,16,16);var pendingGlyph=InventoryText(pending.transform,"Glyph","",2,2,16,16,12,TextAnchor.MiddleCenter);
            var pendingName=InventoryText(pending.transform,"Name","",21,1,86,18,8,TextAnchor.MiddleLeft);
            Wire(pendingView,"icon",pendingIcon);Wire(pendingView,"glyph",pendingGlyph);Wire(pendingView,"label",pendingName);UIKit.Show(pending,false);
            var ghost=host.Find("DragGhost") as RectTransform??Node(host,"DragGhost",C,Vector2.zero,new Vector2(32,32),C);
            var dragImage=InventoryPicture(ghost,"Icon",0,0,32,32);var dragGlyph=InventoryText(ghost,"Glyph","",0,0,32,32,22,TextAnchor.MiddleCenter);
            var dragGroup=InventoryComponent<CanvasGroup>(ghost.gameObject);dragGroup.blocksRaycasts=false;dragGroup.interactable=false;UIKit.Show(ghost,false);
            Wire(view,"grid",grid);Wire(view,"cellTemplate",cell);Wire(view,"pendingTemplate",pending);Wire(view,"title",title);Wire(view,"details",details);Wire(view,"equipmentSummary",summary);Wire(view,"status",status);
            Wire(view,"preview",preview);Wire(view,"previewGlyph",previewGlyph);Wire(view,"equip",equip);Wire(view,"use",use);Wire(view,"discard",discard);Wire(view,"receive",receive);Wire(view,"redraw",redraw);Wire(view,"openFusion",workshop);Wire(view,"close",close);
            Wire(view,"activeFrame",frame);Wire(view,"pendingLabel",pendingLabel);Wire(view,"dragGhost",ghost);Wire(view,"dragImage",dragImage);Wire(view,"dragGlyph",dragGlyph);Wire(view,"dragGroup",dragGroup);
            Wire(view,"dropMarker",marker);Wire(view,"validDrop",valid.gameObject);Wire(view,"invalidDrop",invalid.gameObject);
            RepairInventoryDetails(view);
        }

        static PartFusionView BuildPartFusion(Transform root)
        {
            var host=Stretch(root,"PartFusion");var view=host.gameObject.AddComponent<PartFusionView>();var panel=Panel(host,"Panel",620,340);
            InventoryText(panel,"Heading","파츠 합성",12,8,596,22,17,TextAnchor.MiddleCenter);
            var listArea=Node(panel,"Parts",TL,new Vector2(12,42),new Vector2(184,246));Box(listArea,slotSpr);
            var viewport=Stretch(listArea,"Viewport",4,4,4,4);viewport.gameObject.AddComponent<RectMask2D>();
            var content=Node(viewport,"Content",TL,Vector2.zero,new Vector2(176,0));
            var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=4;layout.childControlWidth=true;layout.childControlHeight=false;layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=listArea.gameObject.AddComponent<ScrollRect>();scroll.content=content;scroll.viewport=viewport;scroll.horizontal=false;
            var row=InventoryButton(content,content,"PartTemplate","",0,0,176,38);UIKit.Show(row.transform.Find("Text").gameObject,false);
            var rowView=row.gameObject.AddComponent<PartListItem>();Wire(rowView,"icon",InventoryPicture(row.transform,"Icon",4,5,28,28));Wire(rowView,"nameLabel",InventoryText(row.transform,"Name","",37,3,131,32,8));UIKit.Show(rowView,false);
            var a=InventoryBox(panel,"MaterialA",208,42,170,106,slotSpr);var b=InventoryBox(panel,"MaterialB",208,158,170,106,slotSpr);
            var aIcon=InventoryPicture(a,"Icon",10,12,36,36);var bIcon=InventoryPicture(b,"Icon",10,12,36,36);
            var aText=InventoryText(a,"Description","재료 A",52,10,108,56,9);var bText=InventoryText(b,"Description","재료 B",52,10,108,56,9);
            var chooseA=InventoryButton(a,a,"ChooseA","재료 A 선택",10,74,150,22);var chooseB=InventoryButton(b,b,"ChooseB","재료 B 선택",10,74,150,22);
            var result=InventoryBox(panel,"Result",390,42,218,246,slotSpr);var resultIcon=InventoryPicture(result,"Icon",85,10,48,48);
            var resultText=InventoryScroll(result,result,"ResultDescription",8,66,202,172);
            var status=InventoryText(panel,"Status","",208,270,400,27,8);
            var back=InventoryButton(panel,panel,"Back","가방으로",12,306,90,22);var generate=InventoryButton(panel,panel,"Generate","합성안 만들기",208,306,170,22);
            var confirm=InventoryButton(panel,panel,"Confirm","합성 확정",390,306,126,22);var cancel=InventoryButton(panel,panel,"Cancel","안 취소",524,306,84,22);UIKit.Label(cancel,"결과 취소");UIKit.Show(confirm,false);UIKit.Show(cancel,false);
            Wire(view,"partTemplate",rowView);Wire(view,"firstIcon",aIcon);Wire(view,"secondIcon",bIcon);Wire(view,"resultIcon",resultIcon);Wire(view,"firstText",aText);Wire(view,"secondText",bText);Wire(view,"resultText",resultText);Wire(view,"status",status);
            Wire(view,"chooseFirst",chooseA);Wire(view,"chooseSecond",chooseB);Wire(view,"generate",generate);Wire(view,"confirm",confirm);Wire(view,"cancel",cancel);Wire(view,"back",back);return view;
        }

        static WeaponRewardView BuildWeaponReward(Transform root)
        {
            var host=Stretch(root,"WeaponReward");var view=host.gameObject.AddComponent<WeaponRewardView>();var panel=Panel(host,"Panel",620,340);
            InventoryText(panel,"Heading","어떤 무기를 깨울까요?",12,8,596,22,17,TextAnchor.MiddleCenter);
            var preview=InventoryBox(panel,"Preview",12,42,220,254,slotSpr);
            InventoryText(preview,"HeroLabel","지금의 영웅",8,4,96,12,8);InventoryText(preview,"WeaponLabel","새 무기 · 발사",112,4,100,12,8);
            var hero=InventoryPicture(preview,"Hero",8,20,92,96);var weapon=InventoryPicture(preview,"Weapon",110,25,100,38);var shot=InventoryPicture(preview,"Projectile",140,76,40,34);
            var shape=BuildShapePreview(preview,"InventoryPreview",10,126,200,120);
            InventoryText(panel,"PromptHint","원하는 무기의 외형과 공격 방식을 설명해 주세요.",246,43,362,19,10);
            var inputRt=InventoryBox(panel,"Prompt",246,68,362,86,slotSpr);
            var inputText=TextOn(Stretch(inputRt,"Text",6,4,6,4),"",10,TextAnchor.UpperLeft);inputText.supportRichText=false;
            var placeholder=TextOn(Stretch(inputRt,"Placeholder",6,4,6,4),"예) 손잡이가 용의 꼬리인 우산. 펼치면 번개를 쏜다.",9,TextAnchor.UpperLeft,UIKit.Muted);
            var input=inputRt.gameObject.AddComponent<InputField>();input.textComponent=inputText;input.placeholder=placeholder;input.characterLimit=DesignGuard.MaxPrompt;input.lineType=InputField.LineType.MultiLineNewline;
            var generate=InventoryButton(panel,panel,"Generate","요청 적용 / 다시 만들기",246,162,220,23);
            var status=InventoryText(panel,"Status","",246,189,362,24,8);
            var info=InventoryScroll(panel,panel,"Info",246,217,362,79);
            var cancel=InventoryButton(panel,panel,"Cancel","돌아가기",12,306,94,23);var claim=InventoryButton(panel,panel,"Claim","이 무기 받기",452,306,156,23);
            Wire(view,"prompt",input);Wire(view,"hero",hero);Wire(view,"weapon",weapon);Wire(view,"projectile",shot);Wire(view,"inventoryPreview",shape);Wire(view,"info",info);Wire(view,"status",status);Wire(view,"generate",generate);Wire(view,"claim",claim);Wire(view,"cancel",cancel);return view;
        }

        static InventoryShapePreview BuildShapePreview(Transform parent,string name,float x,float y,float w,float h)
        {
            var root=parent.Find(name) as RectTransform??Node(parent,name,TL,new Vector2(x,y),new Vector2(w,h));InventoryRect(root,parent,x,y,w,h);
            var view=root.GetComponent<InventoryShapePreview>()??root.gameObject.AddComponent<InventoryShapePreview>();
            var label=InventoryText(root,"ShapeLabel","유형별 가방",0,0,w,14,9,TextAnchor.MiddleCenter);
            var gridNode=root.Find("Grid") as RectTransform??Node(root,"Grid",TL,new Vector2(0,20),new Vector2(w,h-20));InventoryRect(gridNode,root,0,20,w,h-20);
            var grid=gridNode.GetComponent<GridLayoutGroup>()??gridNode.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(10,10);grid.spacing=Vector2.one;grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=7;grid.childAlignment=TextAnchor.UpperCenter;
            var cell=gridNode.Find("Cell")?.GetComponent<Image>()??Box(Node(gridNode,"Cell",TL,Vector2.zero,new Vector2(10,10)),slotSpr);cell.raycastTarget=false;UIKit.Show(cell,false);
            Wire(view,"grid",grid);Wire(view,"cellTemplate",cell);Wire(view,"label",label);return view;
        }
        static void AddStudioInventoryPreview(GameObject root)
        {
            foreach(var studio in root.GetComponentsInChildren<DesignStudioView>(true))
            {
                var preview=studio.transform.Find("Panel/Preview");if(preview==null) continue;
                Wire(studio,"inventoryPreview",BuildShapePreview(preview,"InventoryPreview",10,130,200,116));
            }
        }
        static void LayoutPartCards(GameObject root)
        {
            var evolve=root.GetComponentInChildren<EvolveView>(true);if(evolve==null) return;
            var heading=evolve.transform.Find("Panel/Heading")?.GetComponent<Text>();if(heading!=null) heading.text="성장 보상";
            var pipeline=evolve.transform.Find("Panel/Pipeline/Heading")?.GetComponent<Text>();if(pipeline!=null) pipeline.text="후보 준비 과정";
            foreach(var card in evolve.GetComponentsInChildren<EvolveCard>(true))
            {
                var so=new SerializedObject(card);var title=so.FindProperty("title").objectReferenceValue as Text;var header=so.FindProperty("header").objectReferenceValue as Text;var tags=so.FindProperty("tags").objectReferenceValue as Text;var body=so.FindProperty("desc").objectReferenceValue as Text;
                var rt=(RectTransform)card.transform;float width=rt.rect.width>0?rt.rect.width:rt.sizeDelta.x;float height=rt.rect.height>0?rt.rect.height:rt.sizeDelta.y;
                var icon=InventoryFind<Image>(card.transform,"PartIcon")??InventoryPicture(card.transform,"PartIcon",6,22,32,32);Wire(card,"partIcon",icon);
                InventoryRect(header.rectTransform,card.transform,6,4,width-63,12);header.fontSize=8;
                InventoryRect(title.rectTransform,card.transform,44,20,width-50,36);title.alignment=TextAnchor.MiddleLeft;title.resizeTextForBestFit=true;title.resizeTextMinSize=8;title.resizeTextMaxSize=11;title.verticalOverflow=VerticalWrapMode.Truncate;
                var scroll=body.GetComponentInParent<ScrollRect>();if(scroll!=null) InventoryRect(scroll.GetComponent<RectTransform>(),card.transform,6,62,width-12,height-96);
                else InventoryRect(body.rectTransform,card.transform,6,62,width-12,height-96);
                var complexity=InventoryText(card.transform,"Complexity","등급 T0",6,height-28,width-12,11,8);complexity.color=UIKit.Highlight;Wire(card,"complexityLabel",complexity);
                InventoryRect(tags.rectTransform,card.transform,6,height-14,width-12,10);tags.fontSize=7;tags.resizeTextForBestFit=true;tags.resizeTextMinSize=6;tags.resizeTextMaxSize=7;tags.verticalOverflow=VerticalWrapMode.Truncate;
            }
        }
        static void InstallLegacyChoice(GameObject root)
        {
            var death=root.GetComponentInChildren<DeathView>(true);if(death==null || death.transform.Find("Panel/LegacyNext")!=null) return;
            var panel=death.transform.Find("Panel") as RectTransform;if(panel==null) return;panel.sizeDelta=new Vector2(320,300);
            var button=InventoryButton(panel,panel,"LegacyNext","다른 파츠를 유산으로 선택",60,236,200,20);Wire(death,"legacyNext",button);
            var so=new SerializedObject(death);var carried=so.FindProperty("carried").objectReferenceValue as Text;carried.rectTransform.sizeDelta=new Vector2(288,108);carried.resizeTextForBestFit=true;carried.resizeTextMinSize=7;carried.resizeTextMaxSize=9;
            var next=so.FindProperty("nextLife").objectReferenceValue as Button;next.GetComponent<RectTransform>().anchoredPosition=new Vector2(90,-268);
        }
    }
}
