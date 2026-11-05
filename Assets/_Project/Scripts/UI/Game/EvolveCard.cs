using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>One evolution candidate card.</summary>
    public class EvolveCard : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] Text header, title, desc, reason, tags;
        [SerializeField] Image lockBar;           // Filled horizontal: fills while the click lock wears off
        [SerializeField] CanvasGroup group;
        [SerializeField] Image partIcon;
        [SerializeField] Text complexityLabel;
        AbilityDef shownAbility;
        InventoryItem shownPart;
        string shownVisual;
        bool resetDescription;
        ScrollRect descriptionScroll;
        void OnEnable() => resetDescription=true;

        public void Init(System.Action onClick)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick());
        }

        /// <param name="lockK">0~1 while the appear-lock runs, &lt;0 when not shown.</param>
        public void Tick(AbilityDef a, int index, bool locked, float lockK)
        {
            string visual=a.partName+"|"+a.partImage;
            if (shownAbility != a || shownVisual!=visual) { shownAbility = a; shownVisual=visual; shownPart = PartArt.Preview(a); }
            UIKit.Sprite(partIcon, PartArt.For(shownPart));
            string src = a.source == "ai" ? "<color=#c9b8ff>✦ AI 생성</color>" : "<color=#8c7aa8>◆ 고정 목록</color>";
            string kind = "고정 파츠";
            UIKit.Set(header, $"<color=#6b5c8a>{index + 1}</color>  {src}");
            UIKit.Set(complexityLabel,$"등급 T{a.tier} · {kind}");
            UIKit.Set(title, $"<b><color=#fff2b3>{shownPart.name}</color></b>");
            // Exact rule text always comes from the graph (AI flavor text, if any, is shown in italics after it)
            string rule = a.Explain();
            bool flavor = a.source == "ai" && !string.IsNullOrEmpty(a.desc) && a.desc != rule;
            string details = flavor ? $"{rule}\n\n<color=#8c7aa8><i>{a.desc}</i></color>" : rule;
            if (!string.IsNullOrEmpty(a.semanticReason)) details += $"\n\n<color=#8c7aa8><i>\"{a.semanticReason}\"</i></color>";
            if (desc != null && desc.text != details) resetDescription=true;
            UIKit.Set(desc, details);
            UIKit.Show(reason, false);
            UIKit.Set(tags, $"<color=#6b5c8a>{string.Join(" ", a.tags.Take(4))}</color>");
            button.interactable = !locked;
            if (group != null) group.alpha = locked ? 0.55f + 0.45f * Mathf.Max(0, lockK) : 1;
            UIKit.Show(lockBar, lockK >= 0);
            if (lockK >= 0) UIKit.Fill(lockBar, lockK);
        }

        void LateUpdate()
        {
            if(!resetDescription || desc==null) return;
            if(descriptionScroll==null) descriptionScroll=desc.GetComponentInParent<ScrollRect>();
            if(descriptionScroll==null || descriptionScroll.content==null) return;
            resetDescription=false;
            // Text and its padding must finish layout before ScrollRect calculates the top bound.
            LayoutRebuilder.ForceRebuildLayoutImmediate(descriptionScroll.content);
            Canvas.ForceUpdateCanvases();
            descriptionScroll.StopMovement();descriptionScroll.verticalNormalizedPosition=1;
        }
    }
}
