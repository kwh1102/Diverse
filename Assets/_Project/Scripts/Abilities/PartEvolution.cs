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
            if (d.damage > 0) lines.Add($"이 시전 피해 +{d.damage * k * 100:0.#}%");
            if (d.range > 0) lines.Add($"이 시전 사거리 +{d.range * k * 100:0.#}%");
            if (d.control > 0) lines.Add($"이 시전 제어 시간 +{d.control * k * 100:0.#}%");
            if (d.shield > 0) lines.Add($"시전 보호막 +공격력의 {d.shield * k * 100:0.#}%");
            if (d.heal > 0) lines.Add($"첫 적중 회복 +공격력의 {d.heal * k * 100:0.#}%");
            if (d.cooldown > 0) lines.Add($"이 시전 남은 쿨다운 {d.cooldown * k * 100:0.#}% 환급");
            return "<b>진화 특성 · " + d.name + "</b>\n" + d.conditionText + "\n" + string.Join(" · ", lines) + "\n시전 시작에 조건 확정 · 자동 공격 없음";
        }
        public static bool Matches(string gate, Player p, ActiveSkillData a, Vector2 aim, int previousSlot, float previousTime)
        {
            var cfg = DB.Asset.abilityRules.def;
            return gate switch {
                "charge" => a.input == "charge" || a.slot == 3,
                "precision" => a.steps.Any(s => s.delivery == "line" || s.pattern == "pierce" || s.pattern == "seek"),
                "alternate" => previousSlot >= 0 && previousSlot != a.slot && Time.time - previousTime <= cfg.evolutionComboWindow,
                "projectile" => a.steps.Any(s => s.delivery == "projectile"),
                "distance" => Vector2.Distance(p.Pos, aim) >= cfg.evolutionAimDistance,
                "control" => a.steps.Any(s => s.effect == "pull" || s.effect == "push" || s.effect == "stun" || s.effect == "mark"),
                "shield" => p.Shield > 0 || a.steps.Any(s => s.effect == "shield" || s.effect == "block"),
                "movement" => a.steps.Any(ActiveSkillGenerator.Moving),
                _ => true
            };
        }
    }
}
