using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public static partial class UIBuilder
    {
        [MenuItem("Diverse/파츠 가독성 및 스크롤바 반영")]
        public static void ApplyPartReadability()
        {
            font=Resources.Load<Font>("Fonts/Galmuri11");fontBold=Resources.Load<Font>("Fonts/Galmuri11-Bold");BuildFrames();
            foreach(var name in new[]{"GameCanvas","MenuCanvas"})
                Patch(UIPrefabDir+"/"+name+".prefab",root=>{LayoutPartReadability(root);return true;});
            AssetDatabase.SaveAssets();
            Debug.Log("Part readability prefab edits saved. No play mode, compile request or tests executed.");
        }

        static Text ReadabilityDescription(Transform parent,string name,float x,float y,float w,float h,int size=8)
        {
            var area=parent.Find(name) as RectTransform;
            ScrollRect scroll;
            Text body;
            if(area==null) body=ScrollText(parent,name,Node(parent,name,TL,new Vector2(x,y),new Vector2(w,h)),out scroll);
            else {scroll=area.GetComponent<ScrollRect>();body=scroll.content.GetComponentInChildren<Text>(true);}
            InventoryRect(scroll.GetComponent<RectTransform>(),parent,x,y,w,h);
            body.fontSize=size;body.resizeTextForBestFit=false;body.horizontalOverflow=HorizontalWrapMode.Wrap;body.verticalOverflow=VerticalWrapMode.Overflow;
            EnsureReadabilityScrollbars(scroll);return body;
        }

        static void LayoutPartReadability(GameObject root)
        {
            var inventory=root.GetComponentInChildren<InventoryView>(true);
            if(inventory!=null)
            {
                var panel=inventory.transform.Find("Panel") as RectTransform;panel.sizeDelta=new Vector2(640,352);
                void Area(string path,float x,float y,float w,float h)
                {var r=panel.Find(path) as RectTransform;if(r!=null) InventoryRect(r,r.parent,x,y,w,h);}
                Area("HeaderBar",124,0,342,24);Area("HeaderBar/Heading",10,1,242,22);Area("HeaderBar/Close",262,2,72,20);
                Area("EquipmentBox",6,48,112,190);Area("EquipmentBox/Heading",8,7,96,16);
                Area("WorkshopBox",6,244,112,108);Area("WorkshopBox/OpenFusion",8,8,96,22);Area("WorkshopBox/Status",8,38,96,62);
                Area("Bag",124,38,342,276);Area("Bag/Title",12,6,318,10);Area("Bag/Handle",151,-12,40,16);
                Area("Bag/Clasp0",-3,126,7,20);Area("Bag/Clasp1",338,126,7,20);Area("Bag/GridBounds",12,18,318,254);
                Area("ItemBox",474,48,160,304);Area("ItemBox/ItemHeader/Name",42,0,102,30);Area("ItemBox/ItemHeader/TypeTier",42,32,102,14);
                Area("PendingBox",124,318,342,34);Area("PendingBox/PendingLabel",5,1,332,9);Area("PendingBox/Pending",4,11,334,20);
                var data=new SerializedObject(inventory);
                var grid=data.FindProperty("grid").objectReferenceValue as GridLayoutGroup;grid.cellSize=new Vector2(30,30);
                var cell=data.FindProperty("cellTemplate").objectReferenceValue as InventoryCell;
                ((RectTransform)cell.transform).sizeDelta=new Vector2(30,30);
                foreach(var n in new[]{"Icon","SkillGlyph"}) InventoryRect(cell.transform.Find(n) as RectTransform,cell.transform,3,1,24,24);
                InventoryRect(cell.transform.Find("Caption") as RectTransform,cell.transform,1,22,28,8);
                var ghost=data.FindProperty("dragGhost").objectReferenceValue as RectTransform;ghost.sizeDelta=new Vector2(36,36);
                foreach(var n in new[]{"Icon","Glyph"}) InventoryRect(ghost.Find(n) as RectTransform,ghost,2,2,32,32);
                var pending=data.FindProperty("pendingTemplate").objectReferenceValue as Button;
                pending.GetComponent<RectTransform>().sizeDelta=new Vector2(110,16);
                var waiting=panel.Find("PendingBox/Pending/Content") as RectTransform;if(waiting!=null) waiting.sizeDelta=new Vector2(waiting.sizeDelta.x,16);
                foreach(var n in new[]{"Icon","Glyph"}) InventoryRect(pending.transform.Find(n) as RectTransform,pending.transform,2,2,12,12);
                InventoryRect(pending.transform.Find("Name") as RectTransform,pending.transform,18,0,88,16);
                var equipment=panel.Find("EquipmentBox");
                Wire(inventory,"equipmentSummary",ReadabilityDescription(equipment,"SummaryScroll",8,29,96,153));
                UIKit.Show(equipment.Find("Summary")?.gameObject,false);
            }
            var fusion=root.GetComponentInChildren<PartFusionView>(true);
            if(fusion!=null)
            {
                var panel=fusion.transform.Find("Panel");
                for(int i=0;i<2;i++)
                {
                    string letter=i==0?"A":"B";var card=panel.Find("Material"+letter);
                    InventoryRect(card as RectTransform,panel,208,i==0?60:180,170,114);
                    InventoryRect(card.Find("Icon") as RectTransform,card,10,8,32,32);
                    InventoryRect(card.Find("Description") as RectTransform,card,48,8,112,32);
                    InventoryRect(card.Find("Choose"+letter) as RectTransform,card,10,88,150,18);
                    Wire(fusion,i==0?"firstTraits":"secondTraits",ReadabilityDescription(card,"TraitsScroll",8,44,154,39));
                }
                var result=panel.Find("Result");InventoryRect(result.Find("Icon") as RectTransform,result,85,8,48,48);
                InventoryRect(result.Find("ResultDescription") as RectTransform,result,8,64,202,162);
            }
            foreach(var card in root.GetComponentsInChildren<EvolveCard>(true))
            {
                var data=new SerializedObject(card);var icon=data.FindProperty("partIcon").objectReferenceValue as Image;
                if(icon!=null) {InventoryRect(icon.rectTransform,icon.transform.parent,4,4,28,28);icon.preserveAspect=true;}
                RepairPartCardScroll(card);
            }
            foreach(var scroll in root.GetComponentsInChildren<ScrollRect>(true))
                if(scroll.enabled) EnsureReadabilityScrollbars(scroll);
            LayoutInventoryArtwork(root);
        }

        static void EnsureReadabilityScrollbars(ScrollRect scroll)
        {
            if(scroll==null || scroll.content==null || scroll.viewport==null) return;
            var area=(RectTransform)scroll.transform;
            Scrollbar Bar(string name,bool vertical)
            {
                var bar=vertical?scroll.verticalScrollbar:scroll.horizontalScrollbar;
                if(bar==null)
                {
                    var rail=Node(area,name,TL,Vector2.zero,new Vector2(4,80));Box(rail,whiteSpr,new Color(.12f,.1f,.16f,1));
                    bar=rail.gameObject.AddComponent<Scrollbar>();
                    var handle=Stretch(rail,"Handle");var image=Box(handle,whiteSpr,new Color(.75f,.65f,.86f,1));
                    bar.handleRect=handle;bar.targetGraphic=image;
                }
                var r=(RectTransform)bar.transform;r.SetParent(area,false);r.localScale=Vector3.one;
                r.anchorMin=vertical?new Vector2(1,0):Vector2.zero;r.anchorMax=vertical?Vector2.one:new Vector2(1,0);
                r.pivot=vertical?new Vector2(1,.5f):new Vector2(.5f,0);
                r.anchoredPosition=vertical?new Vector2(-2,0):new Vector2(0,2);r.sizeDelta=vertical?new Vector2(4,-8):new Vector2(-8,4);
                bar.gameObject.SetActive(true);bar.enabled=true;bar.direction=vertical?Scrollbar.Direction.BottomToTop:Scrollbar.Direction.LeftToRight;
                if(bar.handleRect!=null) {var h=bar.handleRect;h.anchorMin=Vector2.zero;h.anchorMax=Vector2.one;h.sizeDelta=h.anchoredPosition=Vector2.zero;}
                return bar;
            }
            if(scroll.vertical) {scroll.verticalScrollbar=Bar("Scrollbar",true);scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;scroll.verticalScrollbarSpacing=2;}
            if(scroll.horizontal) {scroll.horizontalScrollbar=Bar("HorizontalScrollbar",false);scroll.horizontalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;scroll.horizontalScrollbarSpacing=2;}
            if(scroll.viewport!=area)
            {
                var v=scroll.viewport;v.anchorMin=Vector2.zero;v.anchorMax=Vector2.one;
                v.offsetMin=new Vector2(2,scroll.horizontal?8:2);v.offsetMax=new Vector2(scroll.vertical?-10:-2,-2);
            }
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=18;
            if(scroll.vertical && !scroll.horizontal)
            {
                var content=scroll.content;content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(0,1);
                content.anchoredPosition=Vector2.zero;content.sizeDelta=Vector2.zero;
            }
        }
    }
}
