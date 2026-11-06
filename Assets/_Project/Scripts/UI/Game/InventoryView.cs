using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Diverse
{
    public class InventoryView : MonoBehaviour
    {
        [SerializeField] GridLayoutGroup grid;
        [SerializeField] RectTransform panel;
        Canvas uiCanvas;
        [SerializeField] InventoryCell cellTemplate;
        [SerializeField] Button pendingTemplate;
        [SerializeField] Text title,details,status,pendingLabel,equipmentSummary;
        [SerializeField] Image preview;
        [SerializeField] Text itemHeading, itemMeta;
        [SerializeField] ScrollRect detailsScroll;
        string detailsItem;
        [SerializeField] Text previewGlyph;
        [SerializeField] Button equip,use,discard,receive,openFusion,redraw,close;
        [SerializeField] RectTransform activeFrame,dragGhost;
        [SerializeField] Image dragImage;
        [SerializeField] Text dragGlyph;
        [SerializeField] CanvasGroup dragGroup;
        [SerializeField] RectTransform dropMarker;
        [SerializeField] InventoryFootprintGraphic footprintView;
        [SerializeField] GameObject validDrop,invalidDrop;
        [SerializeField] float ghostFollow=28,ghostTravelSeconds=.14f;
        readonly List<InventoryCell> cells=new List<InventoryCell>();
        readonly List<Button> pending=new List<Button>();
        Game g;
        string selected,dragged,message;
        int revision=-1;
        InventoryState shown;
        RectTransform dragSource;
        Coroutine settling;
        string hoverMessage;
        public string Dragged=>dragged;
        public bool Typing=>false;
        public bool Busy=>g!=null && g.InventoryBusy;
        public bool CapturesWorldPointer
        {
            get
            {
                if(!isActiveAndEnabled)return false;
                if(dragged!=null || settling!=null)return true;
                var camera=uiCanvas!=null && uiCanvas.renderMode!=RenderMode.ScreenSpaceOverlay?uiCanvas.worldCamera:null;
                return panel!=null && RectTransformUtility.RectangleContainsScreenPoint(panel,Controls.MouseScreen,camera);
            }
        }
        InventoryState Bag=>g.Player.Inventory;

        public void Init(Game game)
        {
            g=game;
            uiCanvas=GetComponentInParent<Canvas>();
            UIKit.Bind(close,()=>g.CloseOverlay());
            UIKit.Bind(equip,()=>{ if(!Busy && Bag.Select(selected)) Changed(); });
            UIKit.Bind(use,()=>{ if(!Busy && g.Player.UseItem(selected)) Changed(); else message="지금은 사용할 필요가 없어."; });
            UIKit.Bind(discard,()=> { if(Busy) return; if(Bag.Discard(selected,out string why)) { selected=null; Changed(); } else message=why; });
            UIKit.Bind(receive,()=> { var item=Bag.Find(selected); if(item==null || Busy) return; message=Bag.PlaceFirst(item)?null:"배치할 공간이 없어. 가방을 정리해 줘."; g.InventoryChanged(); revision=-1; });
            UIKit.Bind(openFusion,()=>g.OpenFusion(selected));
            UIKit.Bind(redraw,()=>g.RequestPartVisuals(selected));
        }
        public void Opened()
        {
            selected=null; message=null; revision=-1;
            if(g!=null) g.RequestPartVisuals();
        }
        void Changed() { message=null; g.InventoryChanged(); revision=-1; }
        public void SelectItem(string id) { if(Busy) return; selected=id; message=null; revision=-1; }
        public void Click(int x,int y) { if(!Busy) SelectItem(Bag.At(x,y)?.id); }
        public void QuickUse(int x,int y)
        {
            if(Busy) return;
            var item=Bag.At(x,y); if(item==null) return;
            selected=item.id;
            if(item.kind==ItemKind.Weapon) Bag.Select(item.id);
            else if(item.kind==ItemKind.Consumable) g.Player.UseItem(item.id);
            Changed();
        }
        public void BeginDrag(int x,int y,PointerEventData e)
        {
            var item=Bag.At(x,y); if(item==null) return;
            BeginDragItem(item.id,cells[y*Bag.width+x].GetComponent<RectTransform>(),e);
            Hover(x,y);
        }
        public void BeginDragItem(string id,RectTransform source,PointerEventData e)
        {
            if(Busy || Bag.Find(id)==null) return;
            if(settling!=null) { StopCoroutine(settling); settling=null; }
            dragged=id; selected=id; dragSource=source; revision=-1;
            ItemIcons.Bind(dragImage,dragGlyph,Bag.Find(id));
            dragGhost.gameObject.SetActive(true); dragGhost.SetAsLastSibling(); dragGroup.alpha=.95f;
            dragGhost.localScale=Vector3.one*1.12f;
            MoveGhost(e.position,e.pressEventCamera,true);
        }
        void MoveGhost(Vector2 screen,Camera camera,bool immediate)
        {
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)dragGhost.parent,screen,camera,out var point)) return;
            dragGhost.anchoredPosition=immediate?point:Vector2.Lerp(dragGhost.anchoredPosition,point,1-Mathf.Exp(-ghostFollow*Time.unscaledDeltaTime));
        }
        public void Drag(PointerEventData e) { if(dragged!=null) MoveGhost(e.position,e.pressEventCamera,false); }
        public void Hover(int x,int y)
        {
            if(dragged==null) return;
            bool valid=Bag.CanMove(dragged,x,y,out string why);
            footprintView?.Preview(Bag.Find(dragged),x,y,valid);
            UIKit.Show(dropMarker,false);
            hoverMessage=valid?why??"여기에 놓을 수 있어.":why;
        }
        public void LeaveHover() {if(dragged==null)return;footprintView?.ClearPreview();UIKit.Show(dropMarker,false);hoverMessage="가방 안의 칸으로 옮겨 줘.";}
        public void EndDrag() { if(dragged!=null) FinishDrag(dragSource,false); }
        public void Drop(int x,int y)
        {
            if(Busy || dragged==null) return;
            bool moved=Bag.Move(dragged,x,y,out string why);
            if(moved) Changed(); else message=why;
            FinishDrag(moved?cells[y*Bag.width+x].GetComponent<RectTransform>():dragSource,moved);
        }
        void FinishDrag(RectTransform destination,bool accepted)
        {
            dragged=null; revision=-1;
            footprintView?.ClearPreview();
            UIKit.Show(dropMarker,false);hoverMessage=null;
            if(destination==null) { UIKit.Show(dragGhost,false); return; }
            Vector2 end=((RectTransform)dragGhost.parent).InverseTransformPoint(destination.TransformPoint(destination.rect.center));
            settling=StartCoroutine(Settle(end,accepted));
        }
        IEnumerator Settle(Vector2 end,bool accepted)
        {
            Vector2 start=dragGhost.anchoredPosition; float elapsed=0;
            while(elapsed<ghostTravelSeconds)
            {
                elapsed+=Time.unscaledDeltaTime; float t=Mathf.Clamp01(elapsed/ghostTravelSeconds); float ease=1-Mathf.Pow(1-t,3);
                dragGhost.anchoredPosition=Vector2.Lerp(start,end,ease);
                dragGhost.localScale=Vector3.one*Mathf.Lerp(1.12f,accepted ? .85f : 1,ease);
                dragGroup.alpha=1-t*.6f; yield return null;
            }
            UIKit.Show(dragGhost,false); settling=null;
        }
        void OnDisable() { dragged=null; if(settling!=null) StopCoroutine(settling); settling=null; footprintView?.ClearPreview();UIKit.Show(dragGhost,false);UIKit.Show(dropMarker,false); }
        void Update()
        {
            if(g?.Player?.Inventory==null) return;
            if(shown!=Bag || revision!=Bag.revision)
            {
                shown=Bag; revision=Bag.revision; grid.constraintCount=Bag.width;
                UIKit.Pool(cellTemplate,cells,Bag.width*Bag.height);
                for(int i=0;i<Bag.width*Bag.height;i++) cells[i].Bind(this,i%Bag.width,i/Bag.width,Bag,selected);
                var w=Bag.Active;
                var pitch=grid.cellSize+grid.spacing;
                ((RectTransform)grid.transform).sizeDelta=new Vector2(Bag.width*pitch.x-grid.spacing.x,Bag.height*pitch.y-grid.spacing.y);
                UIKit.Show(activeFrame,false); // Per-cell borders trace the actual weapon footprint.
                footprintView?.Bind(Bag);
                if (footprintView != null) footprintView.transform.SetAsLastSibling();
                var waiting=Bag.Pending.ToList(); UIKit.Pool(pendingTemplate,pending,waiting.Count);
                for(int i=0;i<waiting.Count;i++) pending[i].GetComponent<InventoryPendingItem>().Bind(this,waiting[i]);
                UIKit.Set(pendingLabel,$"수령 대기 {waiting.Count}");
            }
            var item=Bag.Find(selected); var active=Bag.Active;
            UIKit.Set(title,$"{TravelTypes.Name(g.Player.MovementType)} · {Bag.width}×{Bag.height}");
            UIKit.Set(equipmentSummary,active==null?"사용 중인 무기가 없어.":$"<b>{active.name}</b>\n{ActiveSkillGenerator.TypeName(ActiveSkillGenerator.TypeOf(active))} · 사용 중인 무기\n\n무기 {Bag.items.Count(i=>i.kind==ItemKind.Weapon)}/{InventoryState.Rules.maxWeapons}\n보유 골드 {g.Player.Gold}G\n\n표시된 QWER 칸: 액티브\n◇ 칸: 고정 파츠\n같은 유형·같은 슬롯 교환\n종속 스킬은 함께 이동\n\n{string.Join("\n",Bag.Attached(active).Where(i=>i.kind==ItemKind.Part).Select(i=>"◆ "+i.name))}");
            ItemIcons.Bind(preview,previewGlyph,item);
            UIKit.Set(itemHeading,item?.name ?? "아이템 선택");
            UIKit.Set(itemMeta,item == null ? "" : item.kind == ItemKind.Part ? $"파츠 · T{item.complexity}" : item.kind == ItemKind.Skill && item.active != null ? ActiveSkillGenerator.TypeName(item.active.weaponType) + " · " + ActiveSkillGenerator.SlotName(item.skillSlot) : item.kind == ItemKind.Weapon ? ActiveSkillGenerator.TypeName(ActiveSkillGenerator.TypeOf(item)) : "소모품");
            string description = Description(item);
            if (itemHeading != null && item != null) description = description.Replace($"<b>{item.name}</b>\n\n", "");
            UIKit.Set(details,description);
            if (detailsItem != selected) { detailsItem = selected; if (detailsScroll != null) { Canvas.ForceUpdateCanvases(); detailsScroll.StopMovement(); detailsScroll.verticalNormalizedPosition = 1; } }
            UIKit.Set(status,(dragged!=null?hoverMessage:null)??message??(g.PartVisualBusy?"전용 그림 준비 중…":g.Player.ActiveGenerationStatus??"드래그: 이동\n빈 칸: 선택 해제\n굵은 테두리:\n사용 중인 무기"));
            bool mutable=!Busy;
            UIKit.Show(equip,item?.kind==ItemKind.Weapon && item.Placed); equip.interactable=mutable && item?.id!=Bag.activeWeapon;
            UIKit.Label(equip,item?.id==Bag.activeWeapon?"사용 중인 무기":"이 무기 사용");
            UIKit.Show(use,item?.kind==ItemKind.Consumable && item.Placed); use.interactable=mutable;
            UIKit.Show(receive,item!=null && !item.Placed); receive.interactable=mutable;
            UIKit.Show(discard,item!=null); discard.interactable=mutable && !(item?.kind==ItemKind.Weapon && item.Placed && Bag.items.Count(i=>i.kind==ItemKind.Weapon&&i.Placed)==1);
            UIKit.Show(redraw,item?.kind==ItemKind.Skill && item.active!=null && !item.active.ArtReady); redraw.interactable=mutable && item?.generatingActive!=true && !g.PartVisualBusy && AiClient.Enabled;
            UIKit.Label(redraw,"스킬 그림 생성");
            openFusion.interactable=mutable; close.interactable=mutable;
        }
        public static string Description(InventoryItem item)
        {
            if(item==null) return "아이템을 선택하면\n이곳에 설명이 나타나요.";
            string desc=item.kind switch {
                ItemKind.Part=>item.Ability?.Explain(),
                ItemKind.Skill=>$"{ "QWER"[item.skillSlot]} 스킬\n\n{SkillDB.Get(item)?.desc}"+(item.active==null?"":"\n\n<b>교환 규칙</b>\n"+ActiveSkillGenerator.ExchangeRule(item.active)),
                ItemKind.Weapon=>$"{ActiveSkillGenerator.TypeName(ActiveSkillGenerator.TypeOf(item))} · 유형별 주변 공간\n\n{item.weapon.desc}",
                _=>item.consumable=="xp"?$"경험치 +{InventoryState.Rules.xpPotionAmount}":item.consumable=="cleanse"?"상태 이상 제거":$"체력 {InventoryState.Rules.potionHealFraction*100:0}% 회복"
            };
            return $"<b>{item.name}</b>\n\n{desc}"+(string.IsNullOrEmpty(item.ancestor)?"":"\n\n유산: "+item.ancestor);
        }
    }
}
