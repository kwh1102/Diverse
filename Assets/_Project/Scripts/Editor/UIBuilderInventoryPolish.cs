using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public static partial class UIBuilder
    {
        [MenuItem("Diverse/아이템 UI 여백 정리")]
        public static void ApplyInventoryPolish()
        {
            font=Resources.Load<Font>("Fonts/Galmuri11");fontBold=Resources.Load<Font>("Fonts/Galmuri11-Bold");BuildFrames();
            Patch(UIPrefabDir+"/GameCanvas.prefab",root=>{LayoutInventoryPolish(root);return true;});
            Patch(UIPrefabDir+"/MenuCanvas.prefab",root=>{InstallHeroFacingButton(root);return true;});
            AssetDatabase.SaveAssets();Debug.Log("Item UI layout edits saved. No tests, play mode or previews executed.");
        }

        // Persist only the requested layout edits. No play mode, tests, capture or AI calls.
        static void LayoutInventoryPolish(GameObject root)
        {
            InstallHeroFacingButton(root);
            var inventory=root.GetComponentInChildren<InventoryView>(true);
            if(inventory!=null)
            {
                var panel=inventory.transform.Find("Panel");
                var header=InventoryBox(panel,"HeaderBar",146,7,304,28,slotSpr);
                var heading=panel.Find("Heading")?.GetComponent<Text>();
                if(heading==null) heading=header.Find("Heading")?.GetComponent<Text>();
                if(heading!=null)
                {
                    InventoryRect(heading.rectTransform,header,10,3,192,22);
                    heading.fontSize=12;heading.resizeTextMaxSize=12;heading.color=UIKit.Highlight;
                    heading.alignment=TextAnchor.MiddleLeft;
                }
                var so=new SerializedObject(inventory);
                var close=so.FindProperty("close").objectReferenceValue as Button;
                InventoryRect(close.GetComponent<RectTransform>(),header,224,4,72,20);
                UIKit.Label(close,"닫기 [I]");
                var key=inventory.GetComponentInChildren<InventoryCell>(true)?.transform.Find("SkillKey");
                if(key!=null) key.gameObject.SetActive(false);
            }
            var fusion=root.GetComponentInChildren<PartFusionView>(true);
            if(fusion!=null)
            {
                var panel=fusion.transform.Find("Panel");
                var parts=panel.Find("Parts") as RectTransform;
                if(parts!=null) InventoryRect(parts,panel,12,60,184,234);
                var result=panel.Find("Result") as RectTransform;
                if(result!=null) InventoryRect(result,panel,390,60,218,234);
                var materialA=panel.Find("MaterialA") as RectTransform;
                var materialB=panel.Find("MaterialB") as RectTransform;
                if(materialA!=null) InventoryRect(materialA,panel,208,60,170,106);
                if(materialB!=null) InventoryRect(materialB,panel,208,178,170,106);
                var resultScroll=result!=null?result.Find("ResultDescription") as RectTransform:null;
                if(resultScroll!=null) resultScroll.sizeDelta=new Vector2(202,160);
                var status=new SerializedObject(fusion).FindProperty("status").objectReferenceValue as Text;
                InventoryRect(status.rectTransform,panel,12,31,596,22);
                status.alignment=TextAnchor.MiddleCenter;status.horizontalOverflow=HorizontalWrapMode.Wrap;status.color=UIKit.Muted;
                status.verticalOverflow=VerticalWrapMode.Truncate;status.resizeTextForBestFit=true;status.resizeTextMinSize=7;status.resizeTextMaxSize=8;
                UIKit.Show(panel.Find("Footer")?.gameObject,false);
            }
            var evolve=root.GetComponentInChildren<EvolveView>(true);
            if(evolve==null) return;
            var view=new SerializedObject(evolve);
            view.FindProperty("heightWithChat").floatValue=350;view.FindProperty("heightWithoutChat").floatValue=266;
            view.ApplyModifiedPropertiesWithoutUndo();
            var evolvePanel=evolve.transform.Find("Panel") as RectTransform;
            evolvePanel.sizeDelta=new Vector2(evolvePanel.sizeDelta.x,350);
            var chat=evolvePanel.Find("Chat") as RectTransform;
            if(chat!=null) chat.anchoredPosition=new Vector2(chat.anchoredPosition.x,-230);
            var pipeline=evolvePanel.Find("Pipeline") as RectTransform;
            if(pipeline!=null) pipeline.sizeDelta=new Vector2(pipeline.sizeDelta.x,260);
            foreach(var card in evolve.GetComponentsInChildren<EvolveCard>(true))
            {
                var rt=(RectTransform)card.transform;rt.sizeDelta=new Vector2(rt.sizeDelta.x,170);
                float width=rt.rect.width;
                var fields=new SerializedObject(card);
                var title=fields.FindProperty("title").objectReferenceValue as Text;
                var tier=fields.FindProperty("complexityLabel").objectReferenceValue as Text;
                var tags=fields.FindProperty("tags").objectReferenceValue as Text;
                var icon=fields.FindProperty("partIcon").objectReferenceValue as Image;
                var slot=InventoryBox(card.transform,"IconSlot",6,21,36,36,slotSpr);
                InventoryRect(icon.rectTransform,slot,3,3,30,30);icon.preserveAspect=true;
                InventoryRect(title.rectTransform,card.transform,48,21,width-54,36);
                title.alignment=TextAnchor.MiddleLeft;title.verticalOverflow=VerticalWrapMode.Truncate;
                RepairPartCardScroll(card);
                InventoryRect(tier.rectTransform,card.transform,6,140,width-12,12);
                InventoryRect(tags.rectTransform,card.transform,6,156,width-12,10);
            }
        }

        internal static void RepairPartCardScroll(EvolveCard card)
        {
            var body=new SerializedObject(card).FindProperty("desc").objectReferenceValue as Text;
            float width=((RectTransform)card.transform).rect.width;
            var scroll=body.GetComponentInParent<ScrollRect>(true);
            if(scroll==null)
            {
                var area=Node(card.transform,"DescriptionScroll",TL,new Vector2(6,64),new Vector2(width-12,70));
                scroll=area.gameObject.AddComponent<ScrollRect>();scroll.content=body.rectTransform;
            }
            foreach(var old in card.GetComponentsInChildren<ScrollRect>(true))
            {
                if(old==scroll) continue;
                if(scroll.verticalScrollbar==null && old.verticalScrollbar!=null)
                {
                    scroll.verticalScrollbar=old.verticalScrollbar;
                    old.verticalScrollbar=null;
                }
                else if(old.verticalScrollbar!=null && old.verticalScrollbar!=scroll.verticalScrollbar)
                    old.verticalScrollbar.gameObject.SetActive(false);
                old.enabled=false;
            }
            InventoryRect((RectTransform)scroll.transform,card.transform,6,64,width-12,70);
            BoundPartScroll(scroll,body);
        }

        static void BoundPartScroll(ScrollRect scroll,Text body)
        {
            var area=(RectTransform)scroll.transform;
            var viewport=scroll.viewport;
            if(viewport==null || viewport==body.rectTransform) viewport=area.Find("Viewport") as RectTransform;
            if(viewport==null) viewport=Stretch(area,"Viewport");
            viewport.SetParent(area,false);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;
            viewport.pivot=new Vector2(0,1);viewport.offsetMin=new Vector2(2,2);viewport.offsetMax=new Vector2(-8,-2);
            var mask=InventoryComponent<RectMask2D>(viewport.gameObject);mask.enabled=true;mask.padding=Vector4.zero;mask.softness=Vector2Int.zero;
            var hit=InventoryComponent<Image>(viewport.gameObject);hit.color=Color.clear;hit.raycastTarget=true;
            var content=viewport.Find("PaddedContent") as RectTransform;
            if(content==null) content=Node(viewport,"PaddedContent",TL,Vector2.zero,Vector2.zero);
            content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(0,1);
            content.anchoredPosition=Vector2.zero;content.sizeDelta=Vector2.zero;
            var layout=InventoryComponent<VerticalLayoutGroup>(content.gameObject);
            layout.padding=new RectOffset(1,1,6,6);layout.spacing=0;
            layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            var textRect=body.rectTransform;textRect.SetParent(content,false);
            textRect.anchorMin=textRect.anchorMax=textRect.pivot=TL;textRect.anchoredPosition=Vector2.zero;textRect.sizeDelta=Vector2.zero;
            var textFitter=body.GetComponent<ContentSizeFitter>();if(textFitter!=null) textFitter.enabled=false;
            var fitter=InventoryComponent<ContentSizeFitter>(content.gameObject);
            fitter.horizontalFit=ContentSizeFitter.FitMode.Unconstrained;fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            body.maskable=true;body.alignment=TextAnchor.UpperLeft;body.horizontalOverflow=HorizontalWrapMode.Wrap;
            body.verticalOverflow=VerticalWrapMode.Overflow;body.resizeTextForBestFit=false;
            foreach(var canvas in content.GetComponentsInChildren<Canvas>(true)) canvas.overrideSorting=false;
            scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;
            if(scroll.verticalScrollbar!=null)
            {
                var bar=scroll.verticalScrollbar;
                var rail=(RectTransform)bar.transform;rail.SetParent(area,false);
                rail.anchorMin=new Vector2(1,0);rail.anchorMax=Vector2.one;rail.pivot=Vector2.one;
                // Match the visible text lane, including the same top/bottom padding as its content.
                rail.offsetMin=new Vector2(-4,viewport.offsetMin.y+layout.padding.bottom);
                rail.offsetMax=new Vector2(-1,viewport.offsetMax.y-layout.padding.top);
                rail.localScale=Vector3.one;
                if(bar.handleRect!=null)
                {
                    var handle=bar.handleRect;
                    handle.SetParent(rail,false);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;
                    handle.pivot=new Vector2(.5f,.5f);handle.sizeDelta=Vector2.zero;handle.anchoredPosition=Vector2.zero;handle.localScale=Vector3.one;
                }
                bar.direction=Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbarSpacing=2;
                scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            }
            scroll.StopMovement();scroll.verticalNormalizedPosition=1;
        }

        static void InstallHeroFacingButton(GameObject root)
        {
            foreach(var studio in root.GetComponentsInChildren<DesignStudioView>(true))
            {
                var preview=studio.transform.Find("Panel/Preview");if(preview==null) continue;
                var button=InventoryButton(preview,preview,"FlipHero","캐릭터 좌우 반전",6,113,96,13);
                Wire(studio,"flipHero",button);
            }
        }
    }
}
