using UnityEngine;

namespace Diverse
{
    public static class ActiveSkillPresentation
    {
        public static Sprite[] Effect(ActiveSkillData skill, ActiveStep step, int kind, int pixels)
        {
            ActiveSkillStyle.Ensure(skill);
            if (kind == 2 && step.delivery == "projectile") return ActiveProjectileArt.Frames(skill, step);
            var generated = ActiveEffectArt.Frames(skill, step, kind);
            if (generated != null) return ActiveSkillPalette.Frames(skill, generated, generated: true);
            var authored = CombatVisuals.Frames(skill, step, kind);
            return authored != null ? ActiveSkillPalette.Frames(skill, authored) : ActiveSkillVisuals.Effect(skill, step, kind, pixels);
        }
    }
}
