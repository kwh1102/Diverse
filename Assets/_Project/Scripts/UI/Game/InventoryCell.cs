using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Diverse
{
    public class InventoryCell : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,IPointerEnterHandler,IPointerExitHandler
    {
        [SerializeField] Image icon, selection;
        [SerializeField] Text caption;
        [SerializeField] Text skillGlyph;
        [SerializeField] Text skillKey;
        [SerializeField] CanvasGroup contents;
        [SerializeField] GameObject top, bottom, left, right;
        InventoryView view;
        int x, y;
        public void Bind(InventoryView owner, int px, int py, InventoryState bag, string selected)
        {
            view = owner; x = px; y = py;
            var item = bag.At(x,y); var w = bag.Owner(x,y);
            ItemIcons.Bind(icon,skillGlyph,item);
            if(contents!=null) contents.alpha=owner!=null && item!=null && owner.Dragged==item.id ? .25f : 1;
            UIKit.Show(skillKey,false);
            string text = item?.kind==ItemKind.Skill ? "QWER"[item.skillSlot].ToString() : item?.kind == ItemKind.Part ? "T" + item.complexity : item?.kind == ItemKind.Consumable ? item.count.ToString() : "";
            if (item == null && w != null)
            {
                int slot = System.Array.IndexOf(ActiveSkillGenerator.Shape(w).skills, new Vector2Int(x-w.x,y-w.y));
                text = slot >= 0 ? "QWER"[slot].ToString() : "◇";
            }
            UIKit.Set(caption,text);
            UIKit.Show(selection, item != null && item.id == selected);
            // One shared prefab graphic draws continuous thin/thick type-colored contours over the grid gaps.
            UIKit.Show(top, false); UIKit.Show(bottom, false); UIKit.Show(left, false); UIKit.Show(right, false);
        }
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) view.QuickUse(x,y);
            else view.Click(x,y);
        }
        public void OnBeginDrag(PointerEventData e) { if(e.button==PointerEventData.InputButton.Left) view.BeginDrag(x,y,e); }
        public void OnDrag(PointerEventData e) => view.Drag(e);
        public void OnEndDrag(PointerEventData e) => view.EndDrag();
        public void OnDrop(PointerEventData e) => view.Drop(x,y);
        public void OnPointerEnter(PointerEventData e)=>view?.Hover(x,y);
        public void OnPointerExit(PointerEventData e)=>view?.LeaveHover();
    }
}
