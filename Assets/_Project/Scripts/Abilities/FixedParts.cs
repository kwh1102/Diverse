using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    [Serializable]
    public class PartDef
    {
        public string id, name, description, sprite, family;
        public StatMod[] mods = Array.Empty<StatMod>();
        public float skillDamage, projectileDamage, meleeDamage, shieldOnCast, healOnHit, rangeBonus, controlBonus;
    }

    [Serializable]
    public class PartIngredient { public string id; public int count; }

    /// <summary>Saved part identity and upgrade rank. No generated programs or executable rules are stored.</summary>
    [Serializable]
    public class AbilityDef
    {
        public string id, catalogId, name, desc, partName, partImage, source = "catalog", semanticReason;
        public int tier = 1;
        public List<PartIngredient> ingredients = new List<PartIngredient>();
        public List<string> tags = new List<string>();
        public bool HasTag(string tag) => tags.Contains(tag);
        public string Explain() => PartCatalog.Explain(this);
        public string Signature() => PartCatalog.CompositionKey(this);
        public AbilityDef Clone() => JsonUtility.FromJson<AbilityDef>(JsonUtility.ToJson(this));
        public void EnsureLists() { tags ??= new List<string>(); }
    }

    [Serializable]
    public class AbilityRecord
    {
        public string json, name;
        public static AbilityRecord From(AbilityDef a) => new AbilityRecord { json = JsonUtility.ToJson(a), name = a.name };
        public AbilityDef ToAbility()
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var a = JsonUtility.FromJson<AbilityDef>(json);
                if (a == null) return null;
                return PartCatalog.Normalize(a, json);
            }
            catch { return null; }
        }
    }

    public static class PartCatalog
    {
        public static PartDef[] All => DB.Asset.abilityRules.def.parts;
        public static PartDef Find(string id) => All.FirstOrDefault(p => p.id == Alias(id));
        static string Alias(string id) => id switch { "scholar_quill" => "hourglass_bead", "mint_coin" => "star_map", "moss_brooch" => "vital_vial", _ => id };
        public static int Units(int tier) => 1 << (Mathf.Clamp(tier, 1, InventoryState.Rules.maxPartTier) - 1);
        public static AbilityDef Create(string id, int tier = 1)
        {
            var d = Find(id) ?? All[0];
            var a = new AbilityDef { id = Guid.NewGuid().ToString("N"), tier = Mathf.Clamp(tier, 1, InventoryState.Rules.maxPartTier),
                ingredients = new List<PartIngredient> { new PartIngredient { id = d.id, count = Units(tier) } } };
            Describe(a); return a;
        }
        public static AbilityDef Normalize(AbilityDef a, string seed = null)
        {
            a.tier = Mathf.Clamp(a.tier, 1, InventoryState.Rules.maxPartTier);
            if (a.ingredients == null || a.ingredients.Count == 0)
            {
                var d = Find(a.catalogId) ?? All[(int)(PartArt.Hash(a.id ?? seed) % (uint)All.Length)];
                a.ingredients = new List<PartIngredient> { new PartIngredient { id = d.id, count = Units(a.tier) } };
            }
            a.ingredients = a.ingredients.Where(i => i != null && i.count > 0 && Find(i.id) != null)
                .GroupBy(i => Find(i.id).id).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new PartIngredient { id = g.Key, count = Mathf.Clamp(g.Sum(i => i.count), 1, Units(a.tier)) }).ToList();
            // Corrupt or older compositions retain a bounded, valid amount of material.
            int remaining = Units(a.tier);
            foreach (var i in a.ingredients) { i.count = Mathf.Min(i.count, remaining); remaining -= i.count; }
            a.ingredients.RemoveAll(i => i.count <= 0);
            if (remaining > 0)
            {
                if (a.ingredients.Count == 0) a.ingredients.Add(new PartIngredient { id = All[0].id, count = 0 });
                a.ingredients[0].count += remaining;
            }
            if (string.IsNullOrEmpty(a.id)) a.id = Guid.NewGuid().ToString("N");
            Describe(a); return a;
        }
        public static string CompositionKey(AbilityDef a) => a?.ingredients == null || a.ingredients.Count == 0 ? a?.catalogId ?? "" :
            string.Join("|", a.ingredients.OrderBy(i => i.id, StringComparer.Ordinal).Select(i => i.id + "*" + i.count));
        public static IEnumerable<PartIngredient> Dominant(AbilityDef a) => a.ingredients.OrderByDescending(i => i.count).ThenBy(i => i.id, StringComparer.Ordinal);
        static void Describe(AbilityDef a)
        {
            var dominant = Dominant(a).ToList(); var d = Find(dominant[0].id);
            a.catalogId = a.tier == 1 ? d.id : "mix:" + CompositionKey(a);
            a.name = a.tier == 1 ? d.name : string.Join("·", dominant.Take(2).Select(i => Find(i.id).name)) + " 결합체";
            a.partName = a.name; a.partImage = d.sprite; a.source = "catalog";
            a.desc = a.tier == 1 ? d.description : "재료의 모든 능력을 비율에 따라 계승한 고정 규칙 합성 파츠.";
            a.tags = new List<string> { "PART", "T" + a.tier }; a.tags.AddRange(dominant.Select(i => Find(i.id).family).Distinct());
        }
        public static AbilityDef Fuse(AbilityDef a, AbilityDef b)
        {
            if (a == null || b == null || a.tier != b.tier || a.tier >= InventoryState.Rules.maxPartTier) return null;
            return Normalize(new AbilityDef { id = Guid.NewGuid().ToString("N"), tier = a.tier + 1,
                ingredients = a.ingredients.Concat(b.ingredients).Select(i => new PartIngredient { id = i.id, count = i.count }).ToList() });
        }
        public static float Rank(AbilityDef a) => DB.Asset.abilityRules.def.partTierPower[Mathf.Clamp(a.tier - 1, 0, DB.Asset.abilityRules.def.partTierPower.Length - 1)];
        public static PartDef Resolve(AbilityDef a)
        {
            var result = new PartDef { id = a.catalogId, name = a.name, description = a.desc, sprite = a.partImage };
            var mods = new List<StatMod>();
            foreach (var i in a.ingredients)
            {
                var d = Find(i.id); float k = i.count / (float)Units(a.tier) * Rank(a);
                result.skillDamage += d.skillDamage * k; result.projectileDamage += d.projectileDamage * k; result.meleeDamage += d.meleeDamage * k;
                result.shieldOnCast += d.shieldOnCast * k; result.healOnHit += d.healOnHit * k; result.rangeBonus += d.rangeBonus * k; result.controlBonus += d.controlBonus * k;
                foreach (var m in d.mods)
                {
                    int index = mods.FindIndex(x => x.stat == m.stat && x.mul == m.mul);
                    if (index < 0) mods.Add(new StatMod(m.stat, m.value * k, m.mul));
                    else { var old = mods[index]; old.value += m.value * k; mods[index] = old; }
                }
            }
            result.mods = mods.ToArray(); return result;
        }
        public static string Explain(AbilityDef a)
        {
            if (a == null) return "고정 파츠";
            var d = Resolve(a); float k = 1; var lines = new List<string> { d.description, "", "<b>능력</b>" };
            foreach (var m in d.mods) lines.Add($"{Stats.Label(m.stat)} {m.value * k * (m.mul || Stats.IsPercent(m.stat) ? 100 : 1):+0.#;-0.#;0}{(m.mul || Stats.IsPercent(m.stat) ? "%" : "")}");
            if (d.skillDamage != 0) lines.Add($"액티브 피해 +{d.skillDamage * k * 100:0.#}%");
            if (d.projectileDamage != 0) lines.Add($"투사체 액티브 피해 +{d.projectileDamage * k * 100:0.#}%");
            if (d.meleeDamage != 0) lines.Add($"근접 액티브 피해 +{d.meleeDamage * k * 100:0.#}%");
            if (d.shieldOnCast != 0) lines.Add($"액티브 시전 시 공격력의 {d.shieldOnCast * k * 100:0.#}% 보호막");
            if (d.healOnHit != 0) lines.Add($"액티브 적중 시 공격력의 {d.healOnHit * k * 100:0.#}% 회복 (시전당 1회)");
            if (d.rangeBonus != 0) lines.Add($"액티브 사거리 +{d.rangeBonus * 100:0.#}%");
            if (d.controlBonus != 0) lines.Add($"액티브 제어 지속시간 +{d.controlBonus * 100:0.#}%");
            lines.Add("\n<b>구성 재료</b>\n" + string.Join("\n", a.ingredients.Select(i => Find(i.id).name + " ×" + i.count)));
            lines.Add($"\n<b>합성 규칙</b>\nT{a.tier} · T1 재료 {Units(a.tier)}개" + (a.tier >= InventoryState.Rules.maxPartTier ? " · 최종 등급" : $"\n같은 등급 두 개 → T{a.tier + 1}\n같은 종류와 다른 종류 모두 가능"));
            return string.Join("\n", lines);
        }
        public static List<AbilityDef> Offer(System.Random random, int count) => All.OrderBy(_ => random.Next()).Take(count).Select(d => Create(d.id)).ToList();
    }
}
