using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>One QWER slot on the HUD: icon glyph, cooldown shade + seconds, key label, hover tooltip.</summary>
    public class SkillSlot : MonoBehaviour
    {
        [SerializeField] Text icon, cooldown, key;
        [SerializeField] Image cooldownShade;      // Filled image, vertical, shrinks as the skill recharges
        [SerializeField] Image artwork;
        int shownCd = -1;
        Color keyColor;

        void Awake()
        {
            if (key != null) keyColor = key.color;
            UIKit.Sprite(artwork, null);
            UIKit.Show(icon, false);
            UIKit.Show(cooldownShade, false);
            UIKit.Show(cooldown, false);
        }

        public void Tick(Player p, SkillState s, string keyName, TooltipView tooltip, string lockedHint = null)
        {
            UIKit.Set(key, keyName);
            bool has = s != null && s.def != null;
            bool linked = has && s.Ready && ActiveSkillSynergy.Ready(p, s.def.active);
            if (key != null) key.color = linked ? new Color(1, .82f, .46f) : keyColor;
            var sprite = has && s.def.active != null ? ActiveSkillArt.Icon(s.def.active) : null;
            UIKit.Sprite(artwork, sprite);
            UIKit.Show(icon, has && sprite == null);
            if (!has)
            {
                UIKit.Show(cooldownShade, false); UIKit.Show(cooldown, false);
                if (tooltip != null && lockedHint != null && UIKit.Hovered((RectTransform)transform))
                    tooltip.Show("<b>아직 익히지 않은 기술</b>", lockedHint);
                return;
            }
            UIKit.Set(icon, s.def.icon);
            icon.color = s.def.color;
            bool cd = !s.Ready;
            int capacity = s.Capacity;
            UIKit.Show(cooldownShade, cd);
            UIKit.Show(cooldown, cd || capacity > 0);
            if (cd)
            {
                UIKit.Fill(cooldownShade, s.cd / (s.def.cooldown * (1 - p.Stats[StatId.CooldownReduction])));
                // Shown in tenths below 1 s, whole seconds above: only build a new string when that number changes
                int shown = s.cd < 1 ? Mathf.RoundToInt(s.cd * 10) : 1000 + Mathf.RoundToInt(s.cd);
                if (shown != shownCd) { shownCd = shown; UIKit.Set(cooldown, s.cd.ToString(s.cd < 1 ? "0.0" : "0")); }
            }
            else if (capacity > 0)
            {
                int remaining = capacity - s.spentCharges;
                if (shownCd != -2 - remaining) { shownCd = -2 - remaining; UIKit.Set(cooldown, remaining.ToString()); }
            }
            if (tooltip != null && UIKit.Hovered((RectTransform)transform))
                tooltip.Show($"<b>{s.def.name}</b> <color=#8c7aa8>({s.def.cooldown}초)</color>",
                    linked ? s.def.desc + "\n<color=#ffd175>연계 발동 가능</color>" : s.def.desc);
        }
    }
}
