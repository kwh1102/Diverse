using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>One QWER slot on the HUD: icon glyph, cooldown shade + seconds, key label, hover tooltip.</summary>
    public class SkillSlot : MonoBehaviour
    {
        [SerializeField] Text icon, cooldown, key;
        [SerializeField] Image cooldownShade;      // Filled image, vertical, shrinks as the skill recharges

        public void Tick(Player p, SkillState s, string keyName, TooltipView tooltip, string lockedHint = null)
        {
            UIKit.Set(key, keyName);
            bool has = s != null && s.def != null;
            UIKit.Show(icon, has);
            if (!has)
            {
                UIKit.Show(cooldownShade, false); UIKit.Show(cooldown, false);
                if (tooltip != null && lockedHint != null && UIKit.Hovered((RectTransform)transform))
                    tooltip.Show("<b>아직 익히지 않은 기술</b>", lockedHint);
                return;
            }
            UIKit.Set(icon, s.def.icon);
            icon.color = s.def.color;
            bool cd = s.cd > 0;
            UIKit.Show(cooldownShade, cd);
            UIKit.Show(cooldown, cd);
            if (cd)
            {
                UIKit.Fill(cooldownShade, s.cd / (s.def.cooldown * (1 - p.Stats[StatId.CooldownReduction])));
                UIKit.Set(cooldown, s.cd.ToString(s.cd < 1 ? "0.0" : "0"));
            }
            if (tooltip != null && UIKit.Hovered((RectTransform)transform))
                tooltip.Show($"<b>{s.def.name}</b> <color=#8c7aa8>({s.def.cooldown}초)</color>", s.def.desc);
        }
    }
}
