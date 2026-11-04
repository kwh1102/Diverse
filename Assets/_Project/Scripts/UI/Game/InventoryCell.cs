using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Diverse
{
    public class InventoryCell : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [SerializeField] Image icon, selection;
        [SerializeField] Text caption;
        [SerializeField] GameObject top, bottom, left, right;
        InventoryView view;
        int x, y;
        public void Bind(InventoryView owner, int px, int py, InventoryState bag, string selected)
        {
            view = owner; x = px; y = py;
            var item = bag.At(x,y); var w = bag.Owner(x,y);
            UIKit.Sprite(icon, PartArt.For(item));
            string text = item?.kind == ItemKind.Skill ? "QWER"[item.skillSlot].ToString() : item?.kind == ItemKind.Part ? "T" + item.complexity : item?.kind == ItemKind.Consumable ? item.count.ToString() : "";
            if (item == null && w != null)
            {
                int slot = System.Array.IndexOf(InventoryState.Slots, new Vector2Int(x-w.x,y-w.y));
                text = slot >= 0 ? "QWER"[slot].ToString() : "◇";
            }
            UIKit.Set(caption,text);
            UIKit.Show(selection, item != null && item.id == selected);
            bool active = w != null && w == bag.Active;
            UIKit.Show(top, active && y == w.y-1); UIKit.Show(bottom, active && y == w.y+1);
            UIKit.Show(left, active && x == w.x-1); UIKit.Show(right, active && x == w.x+1);
        }
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) view.QuickUse(x,y);
            else view.Click(x,y);
        }
        public void OnBeginDrag(PointerEventData e) => view.BeginDrag(x,y);
        public void OnDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) => view.EndDrag();
        public void OnDrop(PointerEventData e) => view.Drop(x,y);
    }
}
