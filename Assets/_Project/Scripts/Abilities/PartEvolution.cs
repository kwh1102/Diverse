using System;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    [Serializable]
    public class PartEvolutionDef
    {
        public string id, familyA, familyB, name, conditionA, conditionB, conditionText, sprite;
        public int tier;
        public float damage, range, control, shield, heal, cooldown;
    }
    public struct PartCastBonus
    {
        public float damage, range, control, shield, heal, cooldown;
    }
    public static class PartEvolution
    {
        public static PartEvolutionDef Find(AbilityDef a)
        {
            if (a == null || a.tier < 2) return null;
            var families = a.ingredients.GroupBy(i => PartCatalog.Find(i.id).family)
                .OrderByDescending(g => g.Sum(i => i.count)).ThenBy(g => g.Key, StringComparer.Ordinal).Take(2).Select(g => g.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            string first = families[0], second = families.Length > 1 ? families[1] : first;
            return DB.Asset.abilityRules.def.evolutions.FirstOrDefault(e => e.tier == a.tier && e.familyA == first && e.familyB == second);
        }
        public static float Share(AbilityDef a, PartEvolutionDef d) => a.ingredients.Where(i => PartCatalog.Find(i.id).family == d.familyA || PartCatalog.Find(i.id).family == d.familyB).Sum(i => i.count) / (float)PartCatalog.Units(a.tier);
        public static string Explain(AbilityDef a)
        {
            var d = Find(a); if (d == null) return "";
            float k = Share(a, d); var lines = new System.Collections.Generic.List<string>();
            if (d.damage > 0) lines.Add($"· 사용한 스킬 피해 +{d.damage * k * 100:0.#}%");
            if (d.range > 0) lines.Add($"· 사용한 스킬 사거리 +{d.range * k * 100:0.#}%");
            if (d.control > 0) lines.Add($"· 기절·표식 등 지속시간 +{d.control * k * 100:0.#}%");
            if (d.shield > 0) lines.Add($"· 추가 보호막: 공격력의 {d.shield * k * 100:0.#}%");
            if (d.heal > 0) lines.Add($"· 첫 적중 시 추가 회복: 공격력의 {d.heal * k * 100:0.#}%");
            if (d.cooldown > 0) lines.Add($"· 사용한 스킬의 남은 대기시간 {d.cooldown * k * 100:0.#}% 감소");
            var conditions = new[] { d.conditionA, d.conditionB }.Where(g => g != "always" && !string.IsNullOrEmpty(g)).Distinct().Select(Condition).ToArray();
            string when = conditions.Length == 0 ? "스킬을 사용할 때마다" : string.Join("\n", conditions.Select(c => "· " + c));
            return "<b>진화 효과 · 발동 조건</b>\n" + (conditions.Length > 1 ? "아래 조건을 모두 만족할 때\n" : "") + when +
                "\n\n<b>발동하면 추가되는 효과</b>\n" + string.Join("\n", lines);
        }
        static string Condition(string gate)
        {
            var cfg = DB.Asset.abilityRules.def;
            return gate switch {
                "charge" => "충전형 스킬 또는 R을 사용할 때",
                "precision" => "직선·관통·유도 중 하나가 있는 스킬을 사용할 때",
                "alternate" => $"직전과 다른 QWER을 {cfg.evolutionComboWindow:0.#}초 안에 사용할 때",
                "projectile" => "투사체를 발사하는 스킬을 사용할 때",
                "distance" => $"조준점이 자신에게서 {cfg.evolutionAimDistance:0.#}칸 이상 떨어져 있을 때",
                "control" => "끌기·밀기·기절·표식 중 하나를 주는 스킬을 사용할 때",
                "shield" => "보호막이 있거나, 보호막·장벽 스킬을 사용할 때",
                "movement" => "돌진·후퇴·순간이동 중 하나가 있는 스킬을 사용할 때",
                _ => "스킬을 사용할 때마다"
            };
        }
        public static bool Matches(string gate, Player p, ActiveSkillData a, Vector2 aim, int previousSlot, float previousTime)
        {
            var cfg = DB.Asset.abilityRules.def;
            return gate switch {
                "charge" => a.input == "charge" || a.slot == 3,
                "precision" => a.HasTechnique ? ActiveSkillGenerator.InferRole(a) == "precision" : a.steps.Any(s => s.delivery == "line" || s.pattern == "pierce" || s.pattern == "seek"),
                "alternate" => previousSlot >= 0 && previousSlot != a.slot && Time.time - previousTime <= cfg.evolutionComboWindow,
                "projectile" => a.HasTechnique ? ActiveTechniques.UsesShot(a.technique) : a.steps.Any(s => s.delivery == "projectile"),
                "distance" => Vector2.Distance(p.Pos, aim) >= cfg.evolutionAimDistance,
                "control" => a.HasTechnique ? ActiveSkillGenerator.InferRole(a) == "control" : a.steps.Any(s => s.effect == "pull" || s.effect == "push" || s.effect == "stun" || s.effect == "mark"),
                "shield" => p.Shield > 0 || (a.HasTechnique ? ActiveSkillGenerator.InferRole(a) == "guard" : a.steps.Any(s => s.effect == "shield" || s.effect == "block")),
                "movement" => a.HasTechnique ? ActiveSkillGenerator.InferRole(a) == "mobility" : a.steps.Any(ActiveSkillGenerator.Moving),
                _ => true
            };
        }
    }
}
