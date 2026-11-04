using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    public class PartListItem : MonoBehaviour
    {
        [SerializeField] Image icon;
        [SerializeField] Text nameLabel;
        public void Bind(InventoryItem item,bool chosen,System.Action click)
        {
            UIKit.Sprite(icon,PartArt.For(item));
            UIKit.Set(nameLabel,(chosen?"◆ ":"")+item.name+$"\n복잡도 T{item.complexity}");
            UIKit.Bind(GetComponent<Button>(),click);
        }
        public void SetInteractable(bool value)=>GetComponent<Button>().interactable=value;
    }
}
