using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Risk vector (design doc #26): power and risk are different things. An ability can be weak and still dangerous
    /// (unbounded recursion, entity floods, meta stacking) or strong and perfectly safe (a plain +30% damage).
    /// Each axis is 0..1. RuleValidator rejects anything above cfg.riskCap, and anything above cfg.riskWarn whose complexity
    /// tier is too low for that kind of risk ("weird abilities are a rarity").
    /// </summary>
    public class RiskVector
    {
        public readonly float[] axes = new float[RiskModel.Axes.Length];
        public float Max => axes.Max();
        public override string ToString() =>
            string.Join(" ", RiskModel.Axes.Select((n, i) => (n, v: axes[i])).Where(x => x.v >= 0.05f).Select(x => $"{x.n}={x.v:0.00}"));
    }

    public static class RiskModel
    {
        public static readonly string[] Axes = { "Recursion", "EntityExplosion", "ProcExplosion", "Multiplicative", "Permanent", "Meta", "Redefine", "RuntimeCost" };

        /// <summary>The lowest complexity tier allowed to carry a "warn"-level amount of each risk.</summary>
        public static int MinTierFor(int axis) => axis switch { 0 => 3, 1 => 2, 2 => 2, 3 => 3, 4 => 4, 5 => 3, 6 => 2, _ => 2 };

        public static string Advice(int axis) => axis switch
        {
            0 => "break the self-feeding chain (cooldown, no Cascade, or a different trigger)",
            1 => "fewer/shorter spawns or a cooldown on the spawning rule",
            2 => "add Chance/EveryNth/cooldown or use a rarer trigger",
            3 => "narrow the meta selector or lower the multiplier",
            4 => "permanent growth must be bounded — use a state with a max",
            5 => "affect fewer abilities (ByTag with a narrow tag, or Strongest)",
            6 => "fewer rule redefinitions in one ability",
            _ => "fewer hit checks per second",
        };

        public static RiskVector Evaluate(AbilityDef a, BuildContext b)
        {
            var rv = new RiskVector();
            var all = b.owned.Concat(new[] { a }).ToList();
            var rep = PowerModel.Evaluate(all, b);
            var ops = a.rules.SelectMany(r => r.ops).ToList();

            // Recursion: relations/ops that can re-fire themselves + depth through other abilities
            float rec = 0;
            foreach (var o in ops)
            {
                if (o.rel == "Cascade") rec += 0.3f * Mathf.Max(1, o.times);
                if (o.rel == "Echo") rec += 0.15f;
                if (o.op == "RepeatTagged") rec += 0.35f + o.amount * 0.3f;
            }
            foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger))
            {
                var trig = RuleLanguage.Get(r.trigger);
                bool selfish = r.ops.Any(o => RuleLanguage.Get(o.op)?.causes.Split(' ').Contains(r.trigger) == true);
                if (selfish && r.cooldown < 0.5f && !r.when.Any(c => c.type is "Chance" or "EveryNth")) rec += 0.25f;
            }
            rv.axes[0] = Mathf.Clamp01(rec);

            // Entity explosion: expected live entities from this ability (vs the cap)
            float live = a.rules.Where(r => rep.liveSpawns.ContainsKey(r)).Sum(r => rep.liveSpawns[r]);
            live += ops.Where(o => o.op == "MultiplyTagged").Sum(o => o.count) * all.Count(x => x.rules.Any(r => r.ops.Any(q => q.op == "Spawn")));
            rv.axes[1] = Mathf.Clamp01(live / Mathf.Max(1, b.cfg.maxSpawnPerOwner));

            // Proc explosion: executions per second of this ability's rules
            float procs = a.rules.Where(r => rep.freq.ContainsKey(r)).Sum(r => rep.freq[r]);
            procs *= 1 + ops.Where(o => o.time == "Periodic").Sum(o => Mathf.Max(2, o.times)) * 0.3f;
            rv.axes[2] = Mathf.Clamp01(procs / 12f);

            // Multiplicative: meta amplification compounding with what the build already has
            float mult = 0;
            foreach (var r in a.rules.Where(r => r.kind == RuleKind.Meta))
                foreach (var o in r.ops)
                {
                    int n = AbilityRuntime.SelectAbilities(r, o, all, a, b.cfg.maxMetaTargets).Count;
                    if (o.op is "AmplifyTagged" or "RepeatTagged" or "MultiplyTagged" or "UnchainTagged") mult += (o.op == "UnchainTagged" ? 0.5f : Mathf.Max(o.amount, o.count * 0.25f)) * Mathf.Sqrt(n);
                }
            int stacked = b.owned.Count(x => x.rules.Any(r => r.kind == RuleKind.Meta && r.ops.Any(o => o.op is "AmplifyTagged" or "RepeatTagged")));
            rv.axes[3] = Mathf.Clamp01(mult * (1 + 0.3f * stacked));

            // Permanent scaling: things that grow forever (run-wide values, permanent upgrades)
            float perm = 0;
            foreach (var o in ops)
            {
                if (o.scale is "run.level" or "run.kills" or "run.bossKills" or "build.abilities") perm += 0.35f;
                if (o.op == "UpgradeRandom") perm += 0.5f;
                if (o.op is "GainPotion" or "GainXp" && a.rules.Any(r => r.trigger is "Hit" or "Attack")) perm += 0.3f;
            }
            rv.axes[4] = Mathf.Clamp01(perm);

            // Meta: how much of the build this ability rewrites
            float meta = 0;
            foreach (var r in a.rules.Where(r => r.kind == RuleKind.Meta))
                foreach (var o in r.ops)
                    meta += 0.15f * AbilityRuntime.SelectAbilities(r, o, all, a, b.cfg.maxMetaTargets).Count + (RuleLanguage.Get(o.op)?.tier >= 4 ? 0.3f : 0);
            rv.axes[5] = Mathf.Clamp01(meta);

            // Redefinition: conversions, replacements, constraints, overrides in one ability
            float redef = ops.Count(o => o.op is "Convert" or "ReplaceWith" or "Forbid" or "CapStat" or "RetriggerTagged" || o.op == "ModifyStat" && o.mode == "Override") * 0.3f;
            rv.axes[6] = Mathf.Clamp01(redef);

            // Runtime cost: hit checks per second
            float cost = a.rules.Where(r => rep.cost.ContainsKey(r)).Sum(r => rep.cost[r]);
            rv.axes[7] = Mathf.Clamp01(cost / 60f);
            return rv;
        }
    }
}
