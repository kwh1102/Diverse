using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum PartMechanic
    {
        None, FocusLens, SwordKnot, EmberCrucible, PrismCore,
        MoonCharm, LuckyDie, RazorGem, RubyPin,
        ClockSpring, SilverGear, EchoBell, HourglassBead,
        ArrowFletching, FalconSight, RuneScope, SpiralNozzle,
        TrailCompass, StarMap, LongReed, AmberMirror,
        FrostShard, AnchorHook, ThunderPin, GravityStone,
        WardBuckle, VitalVial, IronPlate, HeartLock,
        WindBoot, SilkGrip, FeatherRing, BreezeRibbon
    }

    [Serializable]
    public class PartTraitRules
    {
        public float maxStrength = 2, damageCap = .65f, critChanceCap = .5f, critDamageCap = .5f;
        public float reductionCap = .5f, speedCap = .35f, refundCap = .3f, rangeCap = .5f;
        public float healCap = .08f, shieldCap = .75f, stationaryTolerance = .03f;
        public float slowCap = .6f, homingCap = 3, spiralFrequency = 8;
        public float procDamageCap = .3f;
    }

    /// <summary>One manual attack/cast owns its snapshot. Delayed shots cannot spend another cast's first-hit reward.</summary>
    public class PartAttackState
    {
        public bool active, finisher, canMiss, closed;
        public int slot, hits, controlHits;
        public float firstDamage, damage, flatFirst, critChance, critDamage, range;
        public float[] strengths;
        public readonly Dictionary<int, int> targetHits = new Dictionary<int, int>();
    }

    public static class PartTraits
    {
        public static string Text(PartDef d, float strength)
        {
            string N(float n) => n.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            var caps = DB.Asset.abilityRules.def.partTraits;
            float amount = d.amount * strength;
            float limit = d.mechanic switch {
                PartMechanic.FocusLens or PartMechanic.SwordKnot or PartMechanic.PrismCore or PartMechanic.RazorGem or PartMechanic.RuneScope or PartMechanic.LongReed => caps.damageCap,
                PartMechanic.RubyPin or PartMechanic.MoonCharm => caps.critChanceCap,
                PartMechanic.ClockSpring or PartMechanic.SilverGear or PartMechanic.EchoBell or PartMechanic.HourglassBead => caps.refundCap,
                PartMechanic.TrailCompass or PartMechanic.StarMap or PartMechanic.GravityStone => caps.rangeCap,
                PartMechanic.WardBuckle or PartMechanic.HeartLock => caps.shieldCap,
                PartMechanic.BreezeRibbon => caps.speedCap,
                PartMechanic.AnchorHook => caps.slowCap,
                _ => float.MaxValue };
            amount = Mathf.Min(limit, amount);
            int count = d.mechanic == PartMechanic.ArrowFletching ? Mathf.Max(1, Mathf.RoundToInt(d.count * strength)) : d.count;
            return (d.traitText ?? d.description ?? "")
                .Replace("{p}", N(amount * 100)).Replace("{s}", N(d.secondary * strength * 100))
                .Replace("{v}", N(amount)).Replace("{t}", N(d.threshold * 100)).Replace("{limit}", N(caps.damageCap * 100))
                .Replace("{d}", N(d.duration)).Replace("{c}", N(d.cooldown))
                .Replace("{r}", N(d.distance)).Replace("{n}", count.ToString());
        }
    }
}
