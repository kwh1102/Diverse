using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public static partial class UIBuilder
    {
        [MenuItem("Diverse/인벤토리 시스템 반영")]
        public static void InstallInventory()
        {
            font = Resources.Load<Font>("Fonts/Galmuri11");
            fontBold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
            BuildFrames();
            string path = "Assets/_Project/Prefabs/UI/GameCanvas.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var ui = root.GetComponent<GameUI>();
                var inventory = root.GetComponentInChildren<InventoryView>(true) ?? BuildInventory(root.transform);
                Wire(ui,"inventory",inventory); UIKit.Show(inventory,false);
                if (inventory.transform.Find("Panel/ActiveFrame")==null) AddActiveFrame(inventory);
                var hud=root.GetComponentInChildren<HudView>(true);
                var bagButton=hud.transform.Find("InventoryButton")?.GetComponent<Button>();
                if (bagButton==null) bagButton=Btn(hud.transform,"InventoryButton","가방 [I]",TR,new Vector2(-8,180),new Vector2(76,20),10);
                Wire(ui,"inventoryButton",bagButton);
                foreach (var card in root.GetComponentsInChildren<EvolveCard>(true))
                {
                    var image = card.transform.Find("PartIcon")?.GetComponent<Image>();
                    if (image == null)
                    {
                        image = Pic(card.transform,"PartIcon",TL,new Vector2(6,18),new Vector2(30,28),null);
                        image.raycastTarget = false;
                        var so = new SerializedObject(card);
                        var label = so.FindProperty("title").objectReferenceValue as Text;
                        if (label != null)
                        {
                            var rt=label.rectTransform; rt.anchoredPosition = new Vector2(40,rt.anchoredPosition.y);
                            rt.sizeDelta = new Vector2(((RectTransform)card.transform).rect.width-46,rt.sizeDelta.y);
                            label.resizeTextForBestFit=true; label.resizeTextMinSize=8; label.resizeTextMaxSize=11;
                        }
                    }
                    Wire(card,"partIcon",image);
                }
                var death = root.GetComponentInChildren<DeathView>(true);
                if (death != null && death.transform.Find("Panel/LegacyNext") == null)
                {
                    var panel = death.transform.Find("Panel") as RectTransform;
                    if (panel != null)
                    {
                        panel.sizeDelta = new Vector2(panel.sizeDelta.x,300);
                        var button=Btn(panel,"LegacyNext","다른 파츠를 유산으로 선택",TL,new Vector2(60,236),new Vector2(200,20),10);
                        UIKit.Label(button,"다른 파츠를 유산으로 선택"); Wire(death,"legacyNext",button);
                        var so = new SerializedObject(death);
                        var carried = so.FindProperty("carried").objectReferenceValue as Text;
                        carried.rectTransform.sizeDelta = new Vector2(288,108); carried.resizeTextForBestFit = true;
                        carried.resizeTextMinSize=7; carried.resizeTextMaxSize=9;
                        var next = so.FindProperty("nextLife").objectReferenceValue as Button;
                        next.GetComponent<RectTransform>().anchoredPosition = new Vector2(90,-268);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            string relic="Assets/_Project/Resources/Sprites/inventory_weapon_relic.png";
            if (!File.Exists(relic)) { File.WriteAllBytes(relic,PartArt.Relic().texture.EncodeToPNG()); ImportPicture(relic,Art.PPU); }
            var db = GameDatabase.Load(); db.inventory ??= new InventoryRules(); EditorUtility.SetDirty(db);
            foreach (string kind in new[] { "heal","xp","cleanse" })
            {
                string icon = "Assets/_Project/Resources/Sprites/inventory_"+kind+".png";
                if (File.Exists(icon)) continue;
                File.WriteAllBytes(icon,PartArt.For(PartArt.Potion(kind)).texture.EncodeToPNG());
                ImportPicture(icon,Art.PPU);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Inventory UI and consumable sprites installed. No play mode or smoke test was run.");
        }

        static void AddActiveFrame(InventoryView view)
        {
            var panel=view.transform.Find("Panel");
            var frame=Node(panel,"ActiveFrame",TL,new Vector2(12,54),new Vector2(88,88));
            void Edge(string name,Vector2 min,Vector2 max,Vector2 size)
            {
                var r=Stretch(frame,name); r.anchorMin=min; r.anchorMax=max; r.sizeDelta=size; r.anchoredPosition=Vector2.zero;
                var img=Box(r,whiteSpr,new Color(.36f,1,.53f)); img.raycastTarget=false;
            }
            Edge("Top",new Vector2(0,1),Vector2.one,new Vector2(0,2));
            Edge("Bottom",Vector2.zero,new Vector2(1,0),new Vector2(0,2));
            Edge("Left",Vector2.zero,new Vector2(0,1),new Vector2(2,0));
            Edge("Right",new Vector2(1,0),Vector2.one,new Vector2(2,0));
            Wire(view,"activeFrame",frame);
            // The frame draws one continuous perimeter over the grid's spacing.
            var template=view.GetComponentInChildren<InventoryCell>(true);
            foreach(string name in new[]{"Top","Bottom","Left","Right"}) template.transform.Find(name).GetComponent<Image>().color=Color.clear;
        }

        static InventoryView BuildInventory(Transform root)
        {
            var host = Stretch(root,"Inventory");
            var view = host.gameObject.AddComponent<InventoryView>();
            var panel = Panel(host,"Panel",620,340);
            var title=Label(panel,"Title","인벤토리",TL,new Vector2(12,8),new Vector2(540,20),12,color:UIKit.Highlight,bold:true);
            Label(panel,"Help","위 Q · 왼쪽 W · 오른쪽 E · 아래 R · 대각선 파츠 / 기타 아이템은 무기 영역 밖에 보관",TL,new Vector2(12,32),new Vector2(600,16),9,color:UIKit.Muted);
            var close=Btn(panel,"Close","닫기",TL,new Vector2(560,8),new Vector2(48,20),10);
            var gridNode=Node(panel,"Grid",TL,new Vector2(12,54),new Vector2(310,244));
            var grid=gridNode.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize=new Vector2(28,28); grid.spacing=new Vector2(2,2); grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount=7;
            var rt=Node(gridNode,"CellTemplate",TL,Vector2.zero,new Vector2(28,28));
            Box(rt,slotSpr); var button=rt.gameObject.AddComponent<Button>(); button.targetGraphic=rt.GetComponent<Image>();
            var cell=rt.gameObject.AddComponent<InventoryCell>();
            var selected=Box(Stretch(rt,"Selected",1,1,1,1),whiteSpr,new Color(.85f,.65f,1,.4f)); selected.raycastTarget=false;
            var icon=Pic(rt,"Icon",TL,new Vector2(2,2),new Vector2(24,24),null); icon.raycastTarget=false;
            var caption=Label(rt,"Caption","",TL,new Vector2(1,18),new Vector2(26,9),7,TextAnchor.LowerRight,color:UIKit.Highlight);
            GameObject Edge(string name,Vector2 pos,Vector2 size)
            { var edge=Box(Node(rt,name,TL,pos,size),whiteSpr,new Color(.36f,1,.53f)); edge.raycastTarget=false; return edge.gameObject; }
            Wire(cell,"icon",icon); Wire(cell,"selection",selected); Wire(cell,"caption",caption);
            Wire(cell,"top",Edge("Top",Vector2.zero,new Vector2(28,2)));
            Wire(cell,"bottom",Edge("Bottom",new Vector2(0,26),new Vector2(28,2)));
            Wire(cell,"left",Edge("Left",Vector2.zero,new Vector2(2,28)));
            Wire(cell,"right",Edge("Right",new Vector2(26,0),new Vector2(2,28)));
            UIKit.Show(cell,false);
            var preview=Pic(panel,"Preview",TL,new Vector2(330,54),new Vector2(34,34),null); preview.raycastTarget=false;
            var infoRect=Node(panel,"Details",TL,new Vector2(372,54),new Vector2(234,88));
            var details=ScrollText(panel,"DetailsScroll",infoRect,out _); details.fontSize=9;
            var equip=Btn(panel,"Equip","무기 사용",TL,new Vector2(330,148),new Vector2(88,20),9);
            var use=Btn(panel,"Use","아이템 사용",TL,new Vector2(424,148),new Vector2(88,20),9);
            var discard=Btn(panel,"Discard","버리기",TL,new Vector2(518,148),new Vector2(88,20),9);
            var a=Btn(panel,"MaterialA","재료 A 선택",TL,new Vector2(330,174),new Vector2(135,20),8);
            var b=Btn(panel,"MaterialB","재료 B 선택",TL,new Vector2(471,174),new Vector2(135,20),8);
            var fuse=Btn(panel,"Fuse","파츠 합성",TL,new Vector2(330,200),new Vector2(132,20),9);
            var confirm=Btn(panel,"Confirm","합성 확정",TL,new Vector2(468,200),new Vector2(86,20),9);
            var cancel=Btn(panel,"Cancel","취소",TL,new Vector2(558,200),new Vector2(48,20),9);
            var status=Label(panel,"Status","",TL,new Vector2(330,230),new Vector2(276,68),9,color:UIKit.Muted);
            status.resizeTextForBestFit=true; status.resizeTextMinSize=7; status.resizeTextMaxSize=9;
            var prompt=Node(panel,"WeaponPrompt",TL,new Vector2(328,228),new Vector2(280,70)); Box(prompt,slotSpr);
            Label(prompt,"Label","발견한 무기를 디자인해 줘",TL,new Vector2(4,2),new Vector2(270,12),9,color:UIKit.Highlight);
            var inputRt=Node(prompt,"Input",TL,new Vector2(4,18),new Vector2(270,22)); Box(inputRt,buttonSpr);
            var inputText=TextOn(Stretch(inputRt,"Text",4,0,4,0),"",9,TextAnchor.MiddleLeft); inputText.supportRichText=false;
            var placeholder=TextOn(Stretch(inputRt,"Placeholder",4,0,4,0),"예: 푸른 번개를 쏘는 우산",9,TextAnchor.MiddleLeft,UIKit.Muted);
            var input=inputRt.gameObject.AddComponent<InputField>(); input.textComponent=inputText; input.placeholder=placeholder; input.characterLimit=200;
            var generate=Btn(prompt,"Generate","무기 생성 / 수령",TL,new Vector2(4,45),new Vector2(270,20),9);
            var waitingLabel=Label(panel,"PendingLabel","수령 대기",TL,new Vector2(12,312),new Vector2(74,16),9,color:UIKit.Highlight);
            var scrollRt=Node(panel,"Pending",TL,new Vector2(88,308),new Vector2(518,24)); Box(scrollRt,slotSpr);
            scrollRt.gameObject.AddComponent<RectMask2D>();
            var scroll=scrollRt.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=true; scroll.vertical=false;
            var content=Node(scrollRt,"Content",TL,Vector2.zero,new Vector2(518,24));
            var layout=content.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing=4; layout.childControlWidth=false; layout.childControlHeight=false; layout.childForceExpandWidth=false; layout.childForceExpandHeight=false;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            scroll.content=content; scroll.viewport=scrollRt;
            var pending=Btn(content,"PendingTemplate","아이템",TL,Vector2.zero,new Vector2(100,22),8); UIKit.Show(pending,false);
            Wire(view,"grid",grid); Wire(view,"cellTemplate",cell); Wire(view,"pendingTemplate",pending);
            Wire(view,"title",title); Wire(view,"details",details); Wire(view,"status",status); Wire(view,"preview",preview);
            Wire(view,"equip",equip); Wire(view,"use",use); Wire(view,"discard",discard); Wire(view,"materialA",a); Wire(view,"materialB",b);
            Wire(view,"fuse",fuse); Wire(view,"confirm",confirm); Wire(view,"cancel",cancel); Wire(view,"close",close);
            Wire(view,"promptPanel",prompt.gameObject); Wire(view,"weaponPrompt",input); Wire(view,"generate",generate); Wire(view,"pendingLabel",waitingLabel);
            return view;
        }
    }
}
