using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    public class ButtonVisualState : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] CanvasGroup group;
        [SerializeField,Range(0,1)] float disabledAlpha=.38f;
        void LateUpdate() { if(button!=null && group!=null) group.alpha=button.interactable?1:disabledAlpha; }
    }
}
