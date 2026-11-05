using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    [Serializable]
    public class PartDef
    {
        public string id, name, description, sprite;
        public StatMod[] mods = Array.Empty<StatMod>();
        public float skillDamage, projectileDamage, meleeDamage, shieldOnCast, healOnHit;
    }

    /// <summary>Saved part identity and upgrade rank. No generated programs or executable rules are stored.</summary>
    [Serializable]
    public class AbilityDef
    {
        public string id, catalogId, name, desc, partName, partImage, source = "catalog", semanticReason;
        public int tier = 1;
        public List<string> tags = new List<string>();
        public bool HasTag(string tag) => tags.Contains(tag);
        public string Explain() => PartCatalog.Explain(this);
        public string Signature() => catalogId ?? id;
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
                // Old passive programs become bounded catalog parts. Their JSON is never executed.
                var def = PartCatalog.Find(a.catalogId) ?? PartCatalog.All[(int)(PartArt.Hash(a.id ?? json) % (uint)PartCatalog.All.Length)];
                var result = PartCatalog.Create(def.id, Mathf.Clamp(a.tier, 1, InventoryState.Rules.maxPartTier));
                result.id = string.IsNullOrEmpty(a.id) ? Guid.NewGuid().ToString("N") : a.id;
                return result;
            }
            catch { return null; }
        }
    }

    public static class PartCatalog
    {
        public static PartDef[] All => DB.Asset.abilityRules.def.parts;
        public static PartDef Find(string id) => All.FirstOrDefault(p => p.id == id);
        public static AbilityDef Create(string id, int tier = 1)
        {
            var d = Find(id) ?? All[0];
            return new AbilityDef { id = Guid.NewGuid().ToString("N"), catalogId = d.id, name = d.name, partName = d.name,
                desc = d.description, partImage = d.sprite, tier = Mathf.Clamp(tier, 1, InventoryState.Rules.maxPartTier), tags = new List<string> { "PART", d.id.ToUpperInvariant() } };
        }
        public static float Rank(AbilityDef a) => 1 + (Mathf.Clamp(a.tier, 1, InventoryState.Rules.maxPartTier) - 1) * DB.Asset.abilityRules.def.partRankBonus;
        public static string Explain(AbilityDef a)
        {
            var d = Find(a?.catalogId); if (d == null) return "고정 파츠";
            float k = Rank(a); var lines = new List<string> { d.description };
            foreach (var m in d.mods) lines.Add($"{Stats.Label(m.stat)} {m.value * k * (m.mul || Stats.IsPercent(m.stat) ? 100 : 1):+0.#;-0.#;0}{(m.mul || Stats.IsPercent(m.stat) ? "%" : "")}");
            if (d.skillDamage != 0) lines.Add($"액티브 피해 +{d.skillDamage * k * 100:0.#}%");
            if (d.projectileDamage != 0) lines.Add($"투사체 액티브 피해 +{d.projectileDamage * k * 100:0.#}%");
            if (d.meleeDamage != 0) lines.Add($"근접 액티브 피해 +{d.meleeDamage * k * 100:0.#}%");
            if (d.shieldOnCast != 0) lines.Add($"액티브 시전 시 공격력의 {d.shieldOnCast * k * 100:0.#}% 보호막");
            if (d.healOnHit != 0) lines.Add($"액티브 적중 시 공격력의 {d.healOnHit * k * 100:0.#}% 회복 (시전당 1회)");
            return string.Join("\n", lines);
        }
        public static List<AbilityDef> Offer(System.Random random, int count) => All.OrderBy(_ => random.Next()).Take(count).Select(d => Create(d.id)).ToList();
    }
}
