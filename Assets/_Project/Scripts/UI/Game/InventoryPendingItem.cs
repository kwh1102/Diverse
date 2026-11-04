using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Diverse
{
    public class InventoryPendingItem : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler
    {
        [SerializeField] Image icon;
        [SerializeField] Text glyph,label;
        InventoryView view;
        string id;
        public void Bind(InventoryView owner,InventoryItem item)
        {
            view=owner; id=item.id; ItemIcons.Bind(icon,glyph,item); UIKit.Set(label,item.name);
            UIKit.Bind(GetComponent<Button>(),()=>view.SelectItem(id));
        }
        public void OnBeginDrag(PointerEventData e)=>view.BeginDragItem(id,(RectTransform)transform,e);
        public void OnDrag(PointerEventData e)=>view.Drag(e);
        public void OnEndDrag(PointerEventData e)=>view.EndDrag();
    }
}
