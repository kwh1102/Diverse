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

        public void Init(System.Action onClick)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick());
        }

        /// <param name="lockK">0~1 while the appear-lock runs, &lt;0 when not shown.</param>
        public void Tick(AbilityGraph a, int index, bool locked, float lockK)
        {
            string src = a.source == "ai" ? "<color=#c9b8ff>✦ AI 생성</color>" : "<color=#8c7aa8>◇ 로컬</color>";
            string kind = a.kind == "modifier" ? "증폭" : a.kind == "stat" ? "기본" : "메커니즘";
            UIKit.Set(header, $"<color=#6b5c8a>{index + 1}</color>  {src}  <color=#8c7aa8>{kind}</color>");
            UIKit.Set(title, $"<b><color=#fff2b3>{a.name}</color></b>");
            UIKit.Set(desc, a.desc ?? a.Explain());
            UIKit.Show(reason, !string.IsNullOrEmpty(a.semanticReason));
            if (!string.IsNullOrEmpty(a.semanticReason)) UIKit.Set(reason, $"<color=#8c7aa8><i>\"{a.semanticReason}\"</i></color>");
            UIKit.Set(tags, $"<color=#6b5c8a>{string.Join(" ", a.tags.Take(4))}</color>");
            button.interactable = !locked;
            if (group != null) group.alpha = locked ? 0.55f + 0.45f * Mathf.Max(0, lockK) : 1;
            UIKit.Show(lockBar, lockK >= 0);
            if (lockK >= 0) UIKit.Fill(lockBar, lockK);
        }
    }
}
