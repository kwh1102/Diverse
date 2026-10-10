using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Rule IR — what an ability IS. Not "trigger → effect" any more but a tiny program:
    ///   Ability = State variables + Rule[] (+ engine-computed metadata)
    /// Each rule is one of six kinds (the Rule Language rule types of the design doc):
    ///   Trigger    : event → [conditions] → operations           (Effect / Accumulation / Threshold rules)
    ///   Continuous : [conditions] → passive modifiers/conversions  (Modifier / Scaling / Conversion / Spatial rules)
    ///   Replace    : an existing player action is replaced         (Replacement rules: Dash → Blink, …)
    ///   Constrain  : the player gives something up                  (Constraint rules: no potions, one dash, …) — always a price
    ///   Meta       : modifies OTHER owned abilities/rules selected by tag / element / trigger
    ///   System     : modifies game systems (evolution offers, rerolls, rewards, generation bias)
    /// Ops carry a Relation (how the effect links to things: Echo, Chain, Bounce, Return, Orbit…) and a Temporal
    /// (when/how long: Delay, Periodic, UntilHit, ForNextN…) — the two layers that make compositions combinatorial.
    /// Every primitive referenced here must exist in RuleLanguage; RuleValidator proves that before anything runs.
    /// JsonUtility-friendly on purpose (flat fields, no polymorphism) — this is also the format the LLM writes.
    /// </summary>
    [Serializable]
    public class AbilityDef
    {
        public string id;
        public string name;                  // display name (LLM #2 or local namer)
        public string desc;                  // display description
        public string concept;               // creative intent (LLM step A) — one sentence
        public string semanticReason;        // why it suits this player (shown on the card)
        public string source = "local";      // local | ai
        public List<StateDef> states = new List<StateDef>();
        public List<RuleDef> rules = new List<RuleDef>();

        // ── Engine-computed (the AI never sets these) ──
        public List<string> tags = new List<string>();
        public int tier;                     // complexity tier 0 numeric · 1 reactive · 2 stateful · 3 meta · 4 system
        public float power;                  // build-aware ΔPower when it was accepted (points)
        public string mechanic;              // machine-readable structure id

        public bool HasTag(string t) => tags != null && tags.Contains(t);
        public StateDef State(string name) => states?.FirstOrDefault(s => s.name == name);
        public string Explain() => RuleText.Explain(this);
        public AbilityDef Clone() => JsonUtility.FromJson<AbilityDef>(JsonUtility.ToJson(this));

        public void EnsureLists()
        {
            states ??= new List<StateDef>();
            rules ??= new List<RuleDef>();
            tags ??= new List<string>();
            foreach (var r in rules) { r.when ??= new List<CondDef>(); r.ops ??= new List<OpDef>(); }
        }

        /// <summary>Structure without numbers — two abilities with the same signature are the same mechanic.</summary>
        public string Signature()
        {
            var sb = new StringBuilder();
            foreach (var r in rules)
            {
                sb.Append('[').Append(r.kind).Append(':').Append(r.trigger);
                foreach (var c in r.when) sb.Append('?').Append(c.type).Append(c.a).Append(c.text);
                foreach (var o in r.ops)
                    sb.Append('|').Append(o.op).Append(':').Append(o.target).Append(':').Append(o.stat).Append(':').Append(o.element)
                      .Append(':').Append(o.status).Append(':').Append(o.entity).Append(':').Append(o.tag).Append(':').Append(o.from).Append(':').Append(o.to)
                      .Append(':').Append(o.scale).Append(':').Append(o.mode).Append(':').Append(string.IsNullOrEmpty(o.state) ? "" : "S")
                      .Append(':').Append(o.rel).Append(':').Append(o.time).Append(':').Append(Math.Sign(o.amount));
                sb.Append('#').Append(r.replaces).Append(r.select).Append(r.selectArg);
                sb.Append(']');
            }
            return sb.ToString();
        }

        /// <summary>The trigger of the first trigger rule (used for offer diversity / naming).</summary>
        public string PrimaryTrigger => rules?.FirstOrDefault(r => r.kind == RuleKind.Trigger)?.trigger;
    }

    public static class RuleKind
    {
        public const string Trigger = "Trigger", Continuous = "Continuous", Replace = "Replace", Constrain = "Constrain", Meta = "Meta", System = "System";
        public static readonly string[] All = { Trigger, Continuous, Replace, Constrain, Meta, System };
        /// <summary>Rules without a trigger/condition context.</summary>
        public static bool Static(string k) => k is Meta or System or Replace or Constrain;
    }

    [Serializable]
    public class StateDef
    {
        public string name;                  // identifier used by rules (e.g. "pain")
        public string label;                 // Korean display name (e.g. "고통") — optional
        public string type = "Counter";      // Counter | StoredValue | Charge | Flag | Timer | Position | Target
        public float max = 3;                // every state is bounded (NoUnboundedState)
        public float decay;                  // per-second decay toward 0 (0 = keeps its value). Timer: counts down by itself
        public float regen;                  // Charge: per-second refill toward max
    }

    [Serializable]
    public class RuleDef
    {
        public string kind;                  // RuleKind
        public string trigger;               // Trigger rules: an event primitive
        public float period;                 // "Every" trigger: seconds
        public string param;                 // trigger parameter (StateFull: state name, StatusApplied/Expired: status id)
        public List<CondDef> when = new List<CondDef>();
        public List<OpDef> ops = new List<OpDef>();
        public float cooldown;               // internal cooldown (seconds)
        public string replaces;              // Replace rules: the player action replaced (Dash | Potion | ComboFinisher)
        public string select;                // Meta rules: selector over abilities/rules (ByTag | ByElement | ByTrigger | Strongest | Newest | All)
        public string selectArg;             // tag / element / trigger for the selector
    }

    [Serializable]
    public class CondDef
    {
        public string type;                  // Chance | Compare | TargetHas | TargetLacks | EventTag | EveryNth
        public string a;                     // Compare: value reference (e.g. "self.hpPct", "state:pain", "build.tag:FIRE")
        public string cmp;                   // Compare: >= <= > < ==
        public float value;
        public string text;                  // status id / tag
    }

    [Serializable]
    public class OpDef
    {
        public string op;                    // operation primitive
        public string target;                // selector
        public string at;                    // position
        public string element, status, entity, stat, state, tag, from, to, mode;
        public string rel;                   // Relation: Echo Mirror Chain Bounce Return Split Pierce Seek Orbit Follow Attach Spread Cascade Alternate Copy Inherit Link
        public string time;                  // Temporal: Delay Periodic UntilHit UntilDamaged UntilNextAttack ForNextN Stack Refresh
        public int times;                    // Temporal parameter (ticks for Periodic, uses for ForNextN, jumps for Chain…)
        public float amount;                 // magnitude in the op's natural unit (engine-balanced)
        public string scale;                 // value reference added on top: magnitude = amount + per × value
        public float per;                    // engine-balanced
        public float radius, duration, delay;
        public int count;
    }

    /// <summary>An ability carved on a grave / saved in a run. Old saves (pre Rule IR) are converted on load.</summary>
    [Serializable]
    public class AbilityRecord
    {
        public string json;
        public string name;
        public static AbilityRecord From(AbilityDef g) => new AbilityRecord { json = JsonUtility.ToJson(g), name = g.name };

        public AbilityDef ToAbility()
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var a = JsonUtility.FromJson<AbilityDef>(json);
                if (a != null && a.rules != null && a.rules.Count > 0) { a.EnsureLists(); return a; }
                var legacy = JsonUtility.FromJson<LegacyGraph>(json);
                return legacy?.Convert();
            }
            catch { return null; }
        }
    }

    /// <summary>The pre-Rule-IR ability format (trigger + effects / modifier / stat). Only read, to migrate old saves.</summary>
    [Serializable]
    class LegacyGraph
    {
        public string id, name, desc, kind, trigger, semanticReason, source, modTag, stat;
        public float modMul, statValue;
        public List<LegacyCond> conditions;
        public List<LegacyEffect> effects;
        public List<string> tags;

        [Serializable] public class LegacyCond { public string type; public float value; public string text; }
        [Serializable] public class LegacyEffect { public string action, entity, form, element, relation, stat; public float power, radius, duration, delay; public int count; }

        public AbilityDef Convert()
        {
            var a = new AbilityDef { id = id, name = name, desc = desc, semanticReason = semanticReason, source = source ?? "local", tags = tags ?? new List<string>() };
            if (kind == "modifier")
            {
                a.rules.Add(new RuleDef { kind = RuleKind.Meta, ops = { new OpDef { op = "AmplifyTagged", tag = modTag, amount = Mathf.Max(0.05f, modMul - 1) } } });
                return a;
            }
            if (kind == "stat")
            {
                a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "ModifyStat", stat = stat, amount = statValue } } });
                return a;
            }
            var r = new RuleDef { kind = RuleKind.Trigger, trigger = trigger == "Interval" ? "Every" : trigger, period = 4 };
            foreach (var c in conditions ?? new List<LegacyCond>())
            {
                switch (c.type)
                {
                    case "Chance": r.when.Add(new CondDef { type = "Chance", value = c.value }); break;
                    case "Cooldown": r.cooldown = c.value; break;
                    case "EveryNth": r.when.Add(new CondDef { type = "EveryNth", value = c.value }); break;
                    case "HealthBelow": r.when.Add(new CondDef { type = "Compare", a = "self.hpPct", cmp = "<=", value = c.value }); break;
                    case "TargetHasStatus": r.when.Add(new CondDef { type = "TargetHas", text = c.text }); break;
                }
            }
            foreach (var e in effects ?? new List<LegacyEffect>())
            {
                string at = e.form switch { "AtSelf" or "Ring" => "Self", "DashOrigin" => "DashOrigin", "Forward" => "Self", _ => "EventPos" };
                var o = new OpDef { element = e.element, at = at, amount = e.power, radius = e.radius, duration = e.duration, delay = e.delay, count = e.count };
                switch (e.action)
                {
                    case "Damage": o.op = e.radius > 0 ? "Nova" : "Damage"; o.target = "EventTarget"; break;
                    case "Nova": case "Release": o.op = "Nova"; break;
                    case "Projectile": o.op = "Projectile"; break;
                    case "Lightning": o.op = "Chain"; o.target = "EventTarget"; break;
                    case "Spawn": o.op = "Spawn"; o.entity = e.entity; o.mode = e.relation == "Copy" && e.entity == "Clone" ? "Copy" : null; break;
                    case "Heal": o.op = "Heal"; break;
                    case "Shield": o.op = "Shield"; break;
                    case "Buff": o.op = "Buff"; o.stat = e.stat; break;
                    case "ApplyStatus": o.op = "ApplyStatus"; o.target = "EventTarget"; o.status = RuleLanguage.StatusOf(e.element) ?? "burn"; break;
                    case "Push": o.op = "Push"; break;
                    case "Pull": o.op = "Pull"; break;
                    case "Blink": o.op = "Blink"; o.target = "NearestEnemy"; break;
                    case "ResetCooldown": o.op = "ResetCooldowns"; break;
                    default: continue;
                }
                r.ops.Add(o);
            }
            if (r.ops.Count == 0) return null;
            a.rules.Add(r);
            return a;
        }
    }
}
