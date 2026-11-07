using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public static partial class UIBuilder
    {
        [MenuItem("Diverse/가방 및 전투 아트 V2 반영")]
        public static void ApplyCombatUiV2() { AssetDatabase.Refresh(); ApplyPartReadability(); }

        // Authored in the prefab. Runtime views only supply content and inventory dimensions.
        static void LayoutInventoryArtwork(GameObject root)
        {
            Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Frames/"+name+".png");
            void Skin(Transform node,string name,bool sliced=true)
            {
                var image=node!=null?node.GetComponent<Image>():null;var sprite=Load(name);
                if(image==null || sprite==null)return;
                image.sprite=sprite;image.overrideSprite=null;image.color=Color.white;
                image.type=sliced?Image.Type.Sliced:Image.Type.Simple;
            }
            void Area(Transform parent,string name,float x,float y,float w,float h)
            {var r=parent.Find(name) as RectTransform;if(r!=null)InventoryRect(r,r.parent,x,y,w,h);}
            void Label(Transform parent,string path,int size,TextAnchor align=TextAnchor.MiddleLeft)
            {var t=parent.Find(path)?.GetComponent<Text>();if(t!=null){t.fontSize=size;t.resizeTextForBestFit=false;t.alignment=align;}}
            foreach(var inv in root.GetComponentsInChildren<InventoryView>(true))
            {
                var data=new SerializedObject(inv);var cell=data.FindProperty("cellTemplate").objectReferenceValue as InventoryCell;
                var grid=data.FindProperty("grid").objectReferenceValue as GridLayoutGroup;
                grid.cellSize=new Vector2(32,32);grid.spacing=Vector2.one;
                ((RectTransform)cell.transform).sizeDelta=new Vector2(32,32);
                Skin(cell.transform,"satchel_slot");
                foreach(var name in new[]{"Icon","SkillGlyph"})
                {
                    var r=cell.transform.Find(name) as RectTransform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);
                    r.anchoredPosition=Vector2.zero;r.sizeDelta=new Vector2(24,24);
                }
                var cellData=new SerializedObject(cell);var selection=cellData.FindProperty("selection").objectReferenceValue as Image;
                Skin(selection.transform,"satchel_selected");
                var caption=cellData.FindProperty("caption").objectReferenceValue as Text;
                InventoryRect(caption.rectTransform,cell.transform,2,21,28,10);caption.fontSize=7;caption.resizeTextForBestFit=false;caption.alignment=TextAnchor.MiddleRight;
                var key=cellData.FindProperty("skillKey").objectReferenceValue as Text;
                if(key!=null)
                {
                    InventoryRect(key.rectTransform,cell.transform,3,2,12,11);key.fontSize=8;key.resizeTextForBestFit=false;
                    key.alignment=TextAnchor.UpperLeft;key.raycastTarget=false;key.transform.SetAsLastSibling();
                    var outline=key.GetComponent<Outline>()??key.gameObject.AddComponent<Outline>();
                    outline.effectColor=new Color(.12f,.09f,.17f,1);outline.effectDistance=new Vector2(1,-1);
                    UIKit.Show(key,true);
                }
                var panel=inv.transform.Find("Panel");
                ((RectTransform)panel).sizeDelta=new Vector2(680,376);
                panel.localScale=new Vector3(.85f,.85f,1);
                Wire(inv,"panel",panel);
                var ghost=data.FindProperty("dragGhost").objectReferenceValue as RectTransform;
                ghost.sizeDelta=new Vector2(26,26);
                foreach(var name in new[]{"Icon","Glyph"})Area(ghost,name,2,2,22,22);
                Area(panel,"HeaderBar",126,0,378,24);Area(panel,"HeaderBar/Heading",10,3,278,18);Area(panel,"HeaderBar/Close",302,3,68,18);
                Label(panel,"HeaderBar/Heading",12);Label(panel,"HeaderBar/Close/Text",9,TextAnchor.MiddleCenter);
                Area(panel,"EquipmentBox",6,38,112,210);Area(panel,"EquipmentBox/Heading",10,9,92,14);Area(panel,"EquipmentBox/SummaryScroll",10,32,92,168);
                Label(panel,"EquipmentBox/Heading",10);Area(panel,"WorkshopBox",6,256,112,120);
                Area(panel,"WorkshopBox/OpenFusion",10,12,92,22);Area(panel,"WorkshopBox/Status",10,45,92,65);Label(panel,"WorkshopBox/Status",8,TextAnchor.UpperLeft);
                Area(panel,"Bag",126,36,378,312);Area(panel,"Bag/GridBounds",22,31,334,263);
                Skin(panel.Find("Bag"),"satchel_body");Skin(panel.Find("Bag/Handle"),"satchel_handle",false);
                Area(panel,"Bag/Handle",179,-7,20,10);Area(panel,"Bag/Title",30,10,318,12);Label(panel,"Bag/Title",9);
                foreach(var name in new[]{"Clasp0","Clasp1"})UIKit.Show(panel.Find("Bag/"+name)?.gameObject,false);
                Skin(panel.Find("HeaderBar"),"equipment");Skin(panel.Find("EquipmentBox"),"equipment");
                Skin(panel.Find("WorkshopBox"),"workshop");Skin(panel.Find("ItemBox"),"detail");Skin(panel.Find("PendingBox"),"tray");
                Area(panel,"ItemBox",512,38,162,338);
                var item=panel.Find("ItemBox");var stack=item.GetComponent<VerticalLayoutGroup>();
                if(stack!=null){stack.padding=new RectOffset(10,10,10,10);stack.spacing=8;}
                Area(item,"ItemHeader/Name",40,0,102,30);Area(item,"ItemHeader/TypeTier",40,32,102,14);
                var preview=data.FindProperty("preview").objectReferenceValue as Image;
                InventoryRect(preview.rectTransform,item.Find("ItemHeader"),0,6,32,32);
                var glyph=data.FindProperty("previewGlyph").objectReferenceValue as Text;
                if(glyph!=null)InventoryRect(glyph.rectTransform,item.Find("ItemHeader"),0,6,32,32);
                Area(panel,"PendingBox",126,352,378,24);Area(panel,"PendingBox/PendingLabel",9,7,88,10);Label(panel,"PendingBox/PendingLabel",8);
                Area(panel,"PendingBox/Pending",100,4,268,17);
                foreach(var scroll in inv.GetComponentsInChildren<ScrollRect>(true))
                {var bg=scroll.GetComponent<Image>();if(bg!=null)bg.color=new Color(1,1,1,0);}
                foreach(var button in inv.GetComponentsInChildren<Button>(true))
                {
                    if(button.GetComponent<InventoryCell>()!=null)continue;
                    Skin(button.transform,"satchel_button");var style=button.GetComponent<GameButtonStyle>();if(style!=null)style.enabled=false;
                    var colors=ColorBlock.defaultColorBlock;colors.highlightedColor=new Color(1,.93f,.83f);colors.pressedColor=new Color(.76f,.69f,.76f);button.colors=colors;
                    var t=button.GetComponentInChildren<Text>(true);if(t!=null){t.resizeTextForBestFit=false;t.fontSize=8;var r=t.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(5,2);r.offsetMax=new Vector2(-5,-2);t.alignment=TextAnchor.MiddleCenter;}
                }
                foreach(var path in new[]{"EquipmentBox","WorkshopBox"})
                {
                    var parent=panel.Find(path);var line=parent.Find("HeadingRule") as RectTransform??Node(parent,"HeadingRule",TL,Vector2.zero,Vector2.one);
                    InventoryRect(line,parent,10,path=="EquipmentBox"?27:40,92,1);
                    var image=line.GetComponent<Image>()??line.gameObject.AddComponent<Image>();image.sprite=whiteSpr;image.color=new Color(.43f,.39f,.48f);image.raycastTarget=false;
                }
                foreach(var footprint in inv.GetComponentsInChildren<InventoryFootprintGraphic>(true))
                {var so=new SerializedObject(footprint);so.FindProperty("activeWidth").floatValue=2;so.FindProperty("idleWidth").floatValue=1;so.FindProperty("previewWidth").floatValue=1;so.ApplyModifiedPropertiesWithoutUndo();}
            }
            foreach(var row in root.GetComponentsInChildren<PartListItem>(true))
            {
                var rect=(RectTransform)row.transform;rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(160,42);
                var data=new SerializedObject(row);var icon=data.FindProperty("icon").objectReferenceValue as Image;
                var text=data.FindProperty("nameLabel").objectReferenceValue as Text;
                InventoryRect(icon.rectTransform,row.transform,8,7,28,28);
                var t=text.rectTransform;t.anchorMin=Vector2.zero;t.anchorMax=Vector2.one;t.offsetMin=new Vector2(42,4);t.offsetMax=new Vector2(-8,-4);
                text.alignment=TextAnchor.MiddleLeft;text.fontSize=8;text.resizeTextForBestFit=false;
                var content=(RectTransform)rect.parent;content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(0,1);content.sizeDelta=content.anchoredPosition=Vector2.zero;
                var group=content.GetComponent<VerticalLayoutGroup>();
                if(group!=null){group.padding=new RectOffset(4,4,4,4);group.spacing=4;group.childAlignment=TextAnchor.UpperLeft;group.childControlWidth=true;group.childControlHeight=false;group.childForceExpandWidth=true;group.childForceExpandHeight=false;}
                Skin(row.transform,"satchel_panel");
            }
            foreach(var slot in root.GetComponentsInChildren<SkillSlot>(true))
            {
                var data=new SerializedObject(slot);var art=data.FindProperty("artwork").objectReferenceValue as Image;
                if(art==null)continue;var r=art.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(5,5);r.offsetMax=new Vector2(-5,-5);
            }
        }
    }
}
