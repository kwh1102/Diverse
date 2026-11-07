using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    public class InventoryShapePreview : MonoBehaviour
    {
        [SerializeField] GridLayoutGroup grid;
        [SerializeField] Image cellTemplate;
        [SerializeField] Text label;
        readonly List<Image> cells=new List<Image>();
        TravelType shownType;
        bool shown,initialized;
        public void Show(TravelType type,bool selected=true)
        {
            if(initialized && shownType==type && shown==selected) return;
            initialized=true; shownType=type; shown=selected;
            var shape=InventoryState.Rules.Shape(type);
            UIKit.Set(label,selected?$"{TravelTypes.Name(type)} 가방 · {shape.width}×{shape.height}":"유형을 고르면 가방이 보여요");
            grid.constraintCount=shape.width;
            UIKit.Pool(cellTemplate,cells,selected?shape.width*shape.height:0);
        }
    }
}
