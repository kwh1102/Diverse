using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Finite equipment modifiers. Parts cannot spawn attacks, repeat skills, or run AI programs.</summary>
    public class AbilityRuntime
    {
        public readonly List<AbilityDef> Owned = new List<AbilityDef>();
        readonly Player player;
        public AbilityRuntime(Player owner) { player = owner; }
        public void Add(AbilityDef a) { Restore(a); Recompute(); }
        public void Restore(AbilityDef a) { if (a != null && !Owned.Contains(a)) Owned.Add(a); }
        public void AcquireOnce(AbilityDef a) { }
        public void Clear() => Owned.Clear();
        public void Remove(AbilityDef a, bool preserveState = false) { Owned.Remove(a); Recompute(); }
        public void Recompute() => player.RecalcStats();
        public void RefreshEquipmentStats() => Recompute();
        public int OfferCount => 3;
        public float RerollMul => 1;
        public float UpgradeOf(AbilityDef a) => PartCatalog.Rank(a);
        public float SynergyMul(string tags) => 1;
        public bool Forbids(string action) => false;
        public bool ReplaceDash(Vector2 direction) => false;
        public void ComboFinisher(Vector2 direction) { }
        public void Moved(float meters) { }
        public void Tick(float dt) { }
        public void Raise(string trigger, Vector2? pos = null, string param = null) { }
        public void Fire(CombatEvent ev) { }
        public float ClampStat(StatId stat, float value) => value;
        public float IncomingMultiplier(Actor attacker, float amount) => 1;
        public float FilterXp(float amount) => amount;
        public float FilterHeal(float amount, float missing) => amount;
        public IEnumerable<string> TagsOf(AbilityDef a) => a.tags;

        IEnumerable<AbilityDef> Equipped => Owned.Where(a => a.ingredients?.Count > 0)
            .GroupBy(a => a.Signature()).Select(g => g.OrderByDescending(a => a.tier).First());
        float Sum(System.Func<PartDef, float> value) => Equipped.Sum(a => value(PartCatalog.Resolve(a)));
        public float ActiveRange => 1 + Mathf.Clamp(Sum(d => d.rangeBonus), 0, DB.Asset.abilityRules.def.maxRangeBonus);
        public float ActiveControl => 1 + Mathf.Clamp(Sum(d => d.controlBonus), 0, DB.Asset.abilityRules.def.maxControlBonus);
        public void ApplyStats(Stats stats)
        {
            foreach (var a in Equipped) foreach (var m in PartCatalog.Resolve(a).mods)
                if (m.mul) stats.Mul(m.stat, m.value); else stats.Add(m.stat, m.value);
        }
        public float DamageMultiplier(Actor target, string tags)
        {
            if (tags == null || !tags.Split(' ').Contains("SKILL")) return 1;
            float bonus = Sum(d => d.skillDamage);
            if (tags.Split(' ').Contains("PROJECTILE")) bonus += Sum(d => d.projectileDamage);
            if (tags.Split(' ').Contains("MELEE")) bonus += Sum(d => d.meleeDamage);
            return 1 + Mathf.Clamp(bonus, 0, DB.Asset.abilityRules.def.active.passiveBonusCap);
        }
        public void OnActiveCast()
        {
            var cfg = DB.Asset.abilityRules.def;
            float shield = Mathf.Min(cfg.partMaxShield, Sum(d => d.shieldOnCast));
            if (shield > 0) player.AddShield(player.AttackPower * shield, cfg.partShieldDuration);
        }
        public void OnActiveHit()
        {
            float heal = Mathf.Min(DB.Asset.abilityRules.def.partMaxHeal, Sum(d => d.healOnHit));
            if (heal > 0) player.Heal(player.AttackPower * heal);
        }
        public static Color32 StatusColor(string status) => status switch {
            "burn" => Pal.Hex("ff986b"), "chill" => Pal.Hex("9edbff"), "poison" => Pal.Hex("96d989"), _ => Pal.Hex("c9b8ff") };
    }
}
