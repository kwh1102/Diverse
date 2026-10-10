using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>The player's build as the power model sees it. Built from the live player, or bare for edit-mode tests.</summary>
    public class BuildContext
    {
        public List<AbilityDef> owned = new List<AbilityDef>();
        public string[] weaponTags = new string[0];
        public string weaponElement;
        public bool ranged, shieldWeapon;
        public int level = 1;
        public Telemetry telemetry;
        public float budgetMul = 1;
        public int tierBias;
        public AbilityRulesDef cfg = RuleConfig.Def;

        public static BuildContext Of(Player p, int level = -1)
        {
            var b = new BuildContext
            {
                owned = new List<AbilityDef>(p.Abilities.Owned),
                weaponTags = (p.Weapon.tags ?? "").Split(' '),
                weaponElement = p.Weapon.element == Element.None ? null : p.Weapon.element.ToString(),
                level = level >= 0 ? level : p.Level,
                telemetry = p.Telemetry,
                budgetMul = p.Abilities.BudgetMul,
                tierBias = p.Abilities.TierBias,
            };
            b.ranged = b.weaponTags.Contains("RANGED");
            b.shieldWeapon = b.weaponTags.Contains("SHIELD");
            return b;
        }

        public static BuildContext Bare(int level, string weaponTags = "SWORD MELEE")
        {
            var b = new BuildContext { level = level, weaponTags = weaponTags.Split(' ') };
            b.ranged = b.weaponTags.Contains("RANGED");
            b.shieldWeapon = b.weaponTags.Contains("SHIELD");
            return b;
        }

        public int MaxTier => Mathf.Min(4, cfg.MaxTierAt(level) + tierBias);
        public float Budget(int tier) => cfg.Budget(level, tier) * budgetMul;
    }

    public class PowerReport
    {
        public readonly float[] dims = new float[PowerModel.Dims.Length];
        public float Total;
        public readonly Dictionary<RuleDef, float> freq = new Dictionary<RuleDef, float>();
        public readonly Dictionary<RuleDef, float> cost = new Dictionary<RuleDef, float>();          // runtime cost / s
        public readonly Dictionary<RuleDef, float> liveSpawns = new Dictionary<RuleDef, float>();    // expected alive entities
        public readonly Dictionary<AbilityDef, float> byAbility = new Dictionary<AbilityDef, float>();
        /// <summary>Event rates after the fixed point (base + what owned abilities produce) — the simulator replays these.</summary>
        public readonly Dictionary<string, float> eventRates = new Dictionary<string, float>();
        /// <summary>Share of enemies carrying each status from the build (not counting the candidate).</summary>
        public readonly Dictionary<string, float> statusPresence = new Dictionary<string, float>();

        public string Vector() => string.Join(" ", PowerModel.Dims.Select((d, i) => (d, v: dims[i])).Where(x => Mathf.Abs(x.v) >= 0.5f).Select(x => $"{x.d}={x.v:0}"));
    }

    /// <summary>
    /// Deterministic power model. Points: 100 ≈ the weapon's basic-attack damage output.
    ///   RulePower = OpPower(per execution) × TriggerFrequency × ConditionAvailability × Relation × Temporal × meta multipliers
    /// Trigger frequency is build-aware: the player's observed rates (telemetry) blended with generic rates, plus events
    /// produced by other owned abilities (kills from explosions, clone spawns, statuses…), iterated to a fixed point.
    /// Stateful rules are rate-limited by their state inflow (accumulate N → release ⇒ fires inflow/N times per second).
    /// Meta/system/replace/constraint abilities are valued by what they change in the CURRENT build:
    ///   ΔPower = Power(build + A) − Power(build)
    /// </summary>
    public static class PowerModel
    {
        public static readonly string[] Dims = { "Offense", "Defense", "Sustain", "Mobility", "Control", "Utility", "Economy", "Scaling" };
        static readonly float[] Weights = { 1, 1, 1, 0.8f, 0.9f, 0.8f, 0.5f, 1 };
        const int Off = 0, Def = 1, Sus = 2, Mob = 3, Con = 4, Uti = 5, Eco = 6, Sca = 7;
        const float U = 100f / 1.5f;                 // points per 1× attack power of damage
        public const float Baseline = 100;          // basic attacks

        public static float Delta(AbilityDef cand, BuildContext b, PowerReport before = null)
        {
            before ??= Evaluate(b.owned, b);
            var after = Evaluate(new List<AbilityDef>(b.owned) { cand }, b);
            return after.Total - before.Total;
        }

        public static PowerReport Evaluate(IList<AbilityDef> abilities, BuildContext b) => new Model(abilities, b).Run();

        public static int CountTag(BuildContext b, IEnumerable<AbilityDef> abilities, string tag) =>
            abilities.Count(a => RuleLanguage.TagMatches(a.tags, tag) || RetaggedAs(abilities, a, tag)) + (RuleLanguage.TagMatches(b.weaponTags, tag) ? 1 : 0);

        static bool RetaggedAs(IEnumerable<AbilityDef> all, AbilityDef a, string tag) =>
            all.Any(src => src.rules.Any(r => r.kind == RuleKind.Meta && r.ops.Any(o => o.op == "RetagAbilities" && o.tag == tag
                && AbilityRuntime.SelectAbilities(r, o, all.ToList(), src, 99).Contains(a))));

        public static int DistinctElements(IEnumerable<AbilityDef> abilities) =>
            abilities.SelectMany(a => a.rules).SelectMany(r => r.ops).Select(o => o.element).Where(RuleLanguage.IsElement).Distinct().Count();

        class MetaMods { public float amp = 1, repeat, haste, extend, enlarge, gate; public int multiply; public bool infuse, unchain; public string retriggerFrom, retriggerTo; }

        class Model
        {
            readonly IList<AbilityDef> abs;
            readonly BuildContext b;
            readonly PowerReport rep = new PowerReport();
            readonly Dictionary<string, float> baseRate = new Dictionary<string, float>();
            Dictionary<string, Dictionary<AbilityDef, float>> produced = new Dictionary<string, Dictionary<AbilityDef, float>>();
            readonly Dictionary<AbilityDef, MetaMods> meta = new Dictionary<AbilityDef, MetaMods>();
            float totalOffense = Baseline, sustainSources;
            readonly HashSet<string> forbidden = new HashSet<string>();
            readonly Dictionary<string, (AbilityDef a, RuleDef r)> replaced = new Dictionary<string, (AbilityDef, RuleDef)>();

            public Model(IList<AbilityDef> abilities, BuildContext build) { abs = abilities; b = build; }

            public PowerReport Run()
            {
                foreach (var a in abs)
                    foreach (var r in a.rules)
                    {
                        if (r.kind == RuleKind.Constrain) foreach (var o in r.ops.Where(o => o.op == "Forbid")) forbidden.Add(o.mode);
                        if (r.kind == RuleKind.Replace && !string.IsNullOrEmpty(r.replaces)) replaced[r.replaces] = (a, r);
                    }
                BaseRates();
                Meta();
                // Fixed point: frequencies → produced events → frequencies (bounded by proc depth; cycles were validated first)
                for (int it = 0; it <= b.cfg.maxProcDepth; it++)
                {
                    var next = new Dictionary<string, Dictionary<AbilityDef, float>>();
                    foreach (var a in abs)
                        foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger))
                        {
                            float f = Freq(a, r);
                            rep.freq[r] = f;
                            foreach (var o in r.ops) Produce(next, a, o, f * TemporalMul(o) * RelationExtra(o));
                        }
                    produced = next;
                }

                // Basic attacks (+ tag-selected AmplifyTagged on the weapon)
                float baseAmp = 1;
                foreach (var a in abs) foreach (var r in a.rules.Where(r => r.kind == RuleKind.Meta && (string.IsNullOrEmpty(r.select) || r.select == "ByTag"))) foreach (var o in r.ops)
                    if (o.op == "AmplifyTagged" && RuleLanguage.TagMatches(b.weaponTags.Append("ATTACK"), string.IsNullOrEmpty(r.selectArg) ? o.tag : r.selectArg)) baseAmp *= 1 + o.amount;
                rep.dims[Off] += Baseline * baseAmp;

                // Trigger rules
                foreach (var a in abs)
                {
                    float before = Sum();
                    var m = MetaOf(a);
                    foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger))
                    {
                        float f = rep.freq[r];
                        float mul = m.amp * (1 + m.repeat) * (m.gate > 0 ? 1.6f * (1 - m.gate * 0.6f) : 1);
                        // RetriggerTagged: a rarer, harder trigger (Dash → PerfectDodge) lands at better moments — worth more per fire
                        if (m.retriggerFrom != null && r.trigger == m.retriggerFrom) mul *= RetriggerQuality(m.retriggerFrom, m.retriggerTo);
                        foreach (var o in r.ops) TriggerOp(a, r, o, f, mul, m);
                    }
                    rep.byAbility[a] = Sum() - before;
                }
                totalOffense = rep.dims[Off];
                sustainSources = rep.dims[Sus];

                // Continuous, replacement, constraint and system rules (valued against the totals above)
                foreach (var a in abs)
                {
                    float before = Sum();
                    foreach (var r in a.rules.Where(r => r.kind == RuleKind.Continuous))
                    {
                        float avail = 1;
                        bool perHit = r.ops.All(o => o.op is "AmplifyDamage" or "Resist");
                        foreach (var c in r.when) avail *= Avail(a, r, c, perHit);
                        foreach (var o in r.ops) ContinuousOp(a, r, o, avail);
                    }
                    foreach (var r in a.rules.Where(r => r.kind == RuleKind.Replace)) ReplaceRule(a, r);
                    foreach (var r in a.rules.Where(r => r.kind == RuleKind.Constrain)) foreach (var o in r.ops) ConstrainOp(o);
                    foreach (var r in a.rules.Where(r => r.kind == RuleKind.System)) foreach (var o in r.ops) SystemOp(o);
                    rep.byAbility[a] = (rep.byAbility.TryGetValue(a, out var v) ? v : 0) + Sum() - before;
                }
                rep.Total = Sum();
                foreach (var key in baseRate.Keys.Concat(produced.Keys).Distinct()) rep.eventRates[key] = Rate(key, null);
                foreach (var s in RuleLanguage.Statuses) rep.statusPresence[s] = Presence(s);
                return rep;
            }

            float Sum() { float s = 0; for (int i = 0; i < rep.dims.Length; i++) s += rep.dims[i] * Weights[i]; return s; }

            // ───────────── Frequencies ─────────────

            void BaseRates()
            {
                foreach (var t in RuleLanguage.Cat("Trigger")) baseRate[t.id] = t.freq;
                if (!b.shieldWeapon) baseRate["Guard"] = 0.005f;
                baseRate["ProjectileEnd"] = b.ranged ? 1.4f : 0.05f;
                baseRate["CloneSpawn"] = baseRate["CloneExpire"] = baseRate["EntityExpired"] = 0;
                foreach (var s in RuleLanguage.Statuses) { baseRate["StatusApplied|" + s] = 0.02f; baseRate["StatusExpired|" + s] = 0.015f; }
                baseRate["StatusApplied"] = 0.08f; baseRate["StatusExpired"] = 0.06f;
                if (!string.IsNullOrEmpty(b.weaponElement))
                {
                    string ws = RuleLanguage.StatusOf(b.weaponElement);
                    if (ws != null) { baseRate["StatusApplied|" + ws] += 0.3f; baseRate["StatusExpired|" + ws] += 0.2f; }
                }

                // Build-aware: the player's own observed rates replace the generic ones as evidence accumulates
                if (b.telemetry != null)
                    foreach (var name in new[] { "Attack", "Hit", "Crit", "Kill", "Dash", "PerfectDodge", "Damaged", "Guard", "SkillCast", "ComboFinish" })
                    {
                        float obs = b.telemetry.ObservedRate(name, b.cfg, out float trust);
                        if (trust > 0) baseRate[name] = Mathf.Lerp(baseRate[name], obs, trust);
                    }
                baseRate["DashEnd"] = baseRate["Dash"];
                baseRate["FirstHit"] = Mathf.Min(baseRate["Hit"], baseRate["Kill"] * 1.2f);
                baseRate["Overkill"] = baseRate["Kill"] * 0.25f;
                baseRate["EliteKill"] = baseRate["Kill"] * 0.06f;

                // Constraints remove the events of forbidden actions; replacements keep the dash event
                if (forbidden.Contains("Dash")) { baseRate["Dash"] = baseRate["DashEnd"] = baseRate["PerfectDodge"] = 0; }
                if (forbidden.Contains("Skill")) baseRate["SkillCast"] = 0;
                if (replaced.ContainsKey("Dash")) baseRate["PerfectDodge"] *= 0.6f;
            }

            static string Key(RuleDef r) =>
                (r.trigger == "StatusApplied" || r.trigger == "StatusExpired") && !string.IsNullOrEmpty(r.param) ? r.trigger + "|" + r.param : r.trigger;

            float Rate(string key, AbilityDef self)
            {
                float r = baseRate.TryGetValue(key, out var v) ? v : 0;
                if (produced.TryGetValue(key, out var bySrc)) foreach (var kv in bySrc) if (kv.Key != self) r += kv.Value;
                // "any status" listens to every specific status too
                if (key == "StatusApplied" || key == "StatusExpired")
                    foreach (var s in RuleLanguage.Statuses)
                        if (produced.TryGetValue(key + "|" + s, out var bs)) foreach (var kv in bs) if (kv.Key != self) r += kv.Value;
                return r;
            }

            void Add(Dictionary<string, Dictionary<AbilityDef, float>> d, string key, AbilityDef src, float v)
            {
                if (v <= 0) return;
                if (!d.TryGetValue(key, out var m)) d[key] = m = new Dictionary<AbilityDef, float>();
                m[src] = (m.TryGetValue(src, out var x) ? x : 0) + v;
            }

            public float Freq(AbilityDef a, RuleDef r)
            {
                var m = MetaOf(a);
                string trig = m.retriggerFrom != null && r.trigger == m.retriggerFrom ? m.retriggerTo : r.trigger;
                float f;
                if (trig == "Every") f = 1f / Mathf.Max(1f, r.period * (1 - m.haste));
                else if (trig is "StateFull" or "StateEmpty")
                {
                    var s = a.State(r.param);
                    if (s == null) f = 0;
                    else if (s.type == "Timer") f = TimerStarts(a, r.param);
                    else if (s.type == "Charge" && s.regen > 0) f = s.regen / Mathf.Max(0.5f, s.max);
                    else f = Consumed(a, r.param) ? Inflow(a, r.param) / Mathf.Max(0.5f, s.max) : 0.01f;
                }
                else f = Rate(Key(new RuleDef { trigger = trig, param = r.param }), a);

                foreach (var c in r.when)
                {
                    if (StateGate(a, r, c, out float cap)) { f = Mathf.Min(f, cap); continue; }
                    if (m.unchain && c.type is "Chance" or "EveryNth") continue;
                    f *= Avail(a, r, c, false);
                }
                float cd = Mathf.Max(b.cfg.minInternalCooldown, r.cooldown * (1 - m.haste));
                return Mathf.Min(f, 1f / cd);
            }

            float TimerStarts(AbilityDef a, string s)
            {
                float v = 0;
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger))
                    if (r.ops.Any(o => o.op == "SetState" && o.state == s && o.amount > 0))
                        v += rep.freq.TryGetValue(r, out var x) ? x : Rate(Key(r), a) * 0.5f;
                return v;
            }

            /// <summary>"state ≥ k" on a rule that consumes the state: the rule fires at most inflow/k per second.</summary>
            bool StateGate(AbilityDef a, RuleDef r, CondDef c, out float cap)
            {
                cap = 0;
                if (c.type != "Compare" || c.a == null || !c.a.StartsWith("state:") || (c.cmp != ">=" && c.cmp != ">")) return false;
                string s = c.a.Substring(6);
                if (!r.ops.Any(o => Consumes(o, s))) return false;
                var st = a.State(s);
                float inflow = Inflow(a, s) + (st != null && st.type == "Charge" ? st.regen : 0);
                // "> 0" style gates: one fill event is enough → the gate fires at the fill-event rate, not inflow/threshold
                float perFill = FillsPerSecond(a, s);
                float need = Mathf.Max(c.value, 1e-3f);
                cap = c.value <= 0.01f ? perFill : inflow / need;
                return true;
            }

            static bool Consumes(OpDef o, string s) => o.state == s && (o.op == "SetState" || o.op == "AddState" && o.amount < 0);
            static bool Consumed(AbilityDef a, string s) => a.rules.Any(r => r.ops.Any(o => Consumes(o, s)));

            /// <summary>How many times per second the state receives anything (one fill event = one possible release).</summary>
            float FillsPerSecond(AbilityDef a, string s)
            {
                float v = 0;
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger))
                    if (r.ops.Any(o => o.op == "AddState" && o.state == s))
                        v += rep.freq.TryGetValue(r, out var x) ? x : Rate(Key(r), a) * 0.5f;
                var st = a.State(s);
                if (st != null && st.type == "Charge" && st.regen > 0) v += st.regen;
                return v;
            }

            float Inflow(AbilityDef a, string s)
            {
                float v = 0;
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger))
                {
                    float f = rep.freq.TryGetValue(r, out var x) ? x : Rate(Key(r), a) * 0.5f;
                    foreach (var o in r.ops)
                        if (o.op == "AddState" && o.state == s) v += f * Mathf.Max(0, o.amount + o.per * Expected(a, r, o.scale));
                }
                return v;
            }

            /// <summary>Expected value of a state where it is read: the amount accumulated between two uses, or its steady level.</summary>
            float StateValue(AbilityDef a, RuleDef reader, string s)
            {
                var st = a.State(s);
                if (st == null) return 0;
                float inflow = Inflow(a, s) + (st.type == "Charge" ? st.regen : 0);
                if (reader != null && reader.ops.Any(o => Consumes(o, s)))
                {
                    float f = rep.freq.TryGetValue(reader, out var x) ? x : 0.1f;
                    return Mathf.Min(st.max, inflow / Mathf.Max(0.01f, f));
                }
                if (st.type == "Timer") return st.max * Mathf.Min(1, TimerStarts(a, s) * st.max);
                if (st.decay > 0) return Mathf.Min(st.max, inflow / st.decay);
                return inflow > 0 ? st.max * 0.8f : 0;
            }

            // ───────────── Value references ─────────────

            public float Expected(AbilityDef a, RuleDef r, string reference)
            {
                if (string.IsNullOrEmpty(reference)) return 0;
                if (reference.StartsWith("state:")) return StateValue(a, r, reference.Substring(6));
                if (reference.StartsWith("build.") || reference.StartsWith("run.")) return BuildValue(reference);
                if (reference == "event.amount")
                    return r?.trigger switch { "Hit" or "Crit" or "Kill" or "FirstHit" => 1.1f, "Overkill" => 0.6f, "Damaged" => 0.08f, "Healed" => 0.05f, _ => 0 };
                if (reference == "entities.count") return Mathf.Min(b.cfg.maxSpawnPerOwner, rep.liveSpawns.Values.Sum());
                return RuleLanguage.Value(reference)?.expected ?? 0;
            }

            float BuildValue(string reference)
            {
                if (reference.StartsWith("build.tag:")) return CountTag(b, abs, reference.Substring(10));
                if (reference.StartsWith("build.element:"))
                {
                    string el = reference.Substring(14);
                    return abs.Count(a => a.rules.SelectMany(r => r.ops).Any(o => o.element == el)) + (b.weaponElement == el ? 1 : 0);
                }
                if (reference.StartsWith("build.tier:") && int.TryParse(reference.Substring(11), out var t)) return abs.Count(a => a.tier == t);
                return reference switch
                {
                    "build.elements" => DistinctElements(abs),
                    "build.abilities" => abs.Count,
                    "run.level" => b.level,
                    "run.kills" => b.level * 0.3f,
                    "run.bossKills" => b.level / 6f,
                    _ => 0,
                };
            }

            /// <summary>Share of enemies carrying a status — from OTHER sources (an ability never feeds its own condition; NoFreeRecursion).</summary>
            float Presence(string status, AbilityDef self = null)
            {
                float rate = Rate("StatusApplied|" + status, self);
                return Mathf.Clamp(0.05f + rate * 0.75f, 0.05f, 0.85f);
            }

            float TagShare(string tag)
            {
                if (RuleLanguage.TagMatches(b.weaponTags, tag)) return 0.9f;
                if (tag == "CLONE" || tag == "SUMMON")
                {
                    float clone = Rate("CloneSpawn", null);
                    return Mathf.Clamp(clone * 2f, 0.02f, 0.6f);
                }
                return abs.Any(a => RuleLanguage.TagMatches(a.tags, tag)) ? 0.25f : 0.05f;
            }

            float EntityPresence(string entity, AbilityDef self = null)
            {
                float live = rep.liveSpawns.Where(kv => self == null || !self.rules.Contains(kv.Key)).Sum(kv => kv.Value);
                if (live <= 0) return abs.Any(a => a != self && a.rules.Any(r => r.ops.Any(o => o.op == "Spawn" && (string.IsNullOrEmpty(entity) || o.entity == entity)))) ? 0.4f : 0.02f;
                return Mathf.Clamp01(live * 0.5f);
            }

            public float Avail(AbilityDef a, RuleDef r, CondDef c, bool perHit)
            {
                var m = MetaOf(a);
                switch (c.type)
                {
                    case "Chance": return Mathf.Min(0.95f, c.value * (1 + m.haste));
                    case "EveryNth": return 1f / Mathf.Max(2, c.value);
                    case "TargetHas": return Presence(c.text, a);
                    case "TargetLacks": return 1 - Presence(c.text, a);
                    case "TargetIs": return c.text switch { "Elite" => 0.08f, "Boss" => 0.03f, "Normal" => 0.9f, "Ranged" => 0.35f, _ => 0.65f };
                    case "EventTag": return TagShare(c.text);
                    case "HasEntity": return EntityPresence(c.text);
                    case "NoEntity": return 1 - EntityPresence(c.text);
                    case "WithinTime": return Mathf.Clamp01(1 - Mathf.Exp(-Rate(c.text, null) * c.value));
                    case "SameTarget": return 0.55f;
                    case "DifferentTarget": return 0.45f;
                    case "DominantElement": return DominantElement() == c.text ? 1 : 0.15f;
                    case "Compare": return CompareProb(a, r, c);
                }
                return 1;
            }

            string DominantElement() =>
                abs.SelectMany(x => x.rules).SelectMany(r => r.ops).Select(o => o.element).Where(RuleLanguage.IsElement)
                    .GroupBy(e => e).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? b.weaponElement;

            float CompareProb(AbilityDef a, RuleDef r, CondDef c)
            {
                string reference = c.a ?? "";
                float v = c.value, pGe;
                if (reference.StartsWith("build.") || reference.StartsWith("run."))
                {
                    float actual = BuildValue(reference);
                    return Holds(actual, c.cmp, v) ? 1 : 0.3f;     // not yet true → potential value
                }
                if (c.cmp == "==") return reference == "self.moving" ? (v > 0.5f ? 0.6f : 0.4f) : 0.3f;
                switch (reference)
                {
                    case "self.hpPct": pGe = 1 - Mathf.Pow(Mathf.Clamp01(v), 1.5f); break;
                    case "self.missingHpPct": pGe = Mathf.Pow(Mathf.Clamp01(1 - v), 1.5f); break;
                    case "time.sinceDamaged": case "time.sinceKill": pGe = Mathf.Exp(-v / 8f); break;
                    case "time.inCombat": pGe = Mathf.Exp(-v / 15f); break;
                    case "self.stillTime": pGe = Mathf.Exp(-v / 1.5f); break;
                    case "enemies.near": case "enemies.far": pGe = Mathf.Clamp01(1 - (v - 1) / 5f); break;
                    case "self.moving": pGe = v > 0.5f ? 0.6f : 1; break;
                    default:
                    {
                        float e = Expected(a, r, reference);
                        if (reference.StartsWith("state:")) { pGe = e >= v ? 1 : (v <= 0 ? 1 : e / v); break; }
                        pGe = e <= 0 ? 0 : Mathf.Clamp01(1 - v / (2 * e));
                        break;
                    }
                }
                return c.cmp == "<=" || c.cmp == "<" ? 1 - pGe : pGe;
            }

            public static bool Holds(float x, string cmp, float v) => cmp switch
            {
                ">=" => x >= v, ">" => x > v, "<=" => x <= v, "<" => x < v, "==" => Mathf.Abs(x - v) < 0.001f, _ => false,
            };

            // ───────────── Meta ─────────────

            void Meta()
            {
                foreach (var a in abs) meta[a] = new MetaMods();
                foreach (var src in abs)
                    foreach (var r in src.rules.Where(r => r.kind == RuleKind.Meta))
                        foreach (var o in r.ops)
                            foreach (var a in AbilityRuntime.SelectAbilities(r, o, abs, src, b.cfg.maxMetaTargets))
                            {
                                var m = meta[a];
                                switch (o.op)
                                {
                                    case "AmplifyTagged": m.amp *= 1 + o.amount; break;
                                    case "RepeatTagged": m.repeat = Mathf.Min(1.2f, m.repeat + o.amount); break;
                                    case "HasteTagged": m.haste = Mathf.Min(0.6f, m.haste + o.amount); break;
                                    case "ExtendTagged": m.extend = Mathf.Min(1.5f, m.extend + o.amount); break;
                                    case "EnlargeTagged": m.enlarge = Mathf.Min(1f, m.enlarge + o.amount); break;
                                    case "MultiplyTagged": m.multiply = Mathf.Min(3, m.multiply + Mathf.Max(1, o.count)); break;
                                    case "InfuseTagged": m.infuse = true; break;
                                    case "GateTagged": m.gate = Mathf.Max(m.gate, o.amount); break;
                                    case "UnchainTagged": m.unchain = true; break;
                                    case "RetriggerTagged": m.retriggerFrom = o.from; m.retriggerTo = o.to; break;
                                }
                            }
            }

            MetaMods MetaOf(AbilityDef a) => meta.TryGetValue(a, out var m) ? m : new MetaMods();

            /// <summary>Per-fire value of moving a rule from one trigger to another (rarer + skill-based = each fire matters more).</summary>
            float RetriggerQuality(string from, string to)
            {
                float rf = Mathf.Max(0.01f, baseRate.TryGetValue(from, out var x) ? x : 0.1f), rt = Mathf.Max(0.01f, baseRate.TryGetValue(to, out var y) ? y : 0.1f);
                return Mathf.Clamp(Mathf.Sqrt(rf / rt) * 1.4f, 1, 4);
            }

            // ───────────── Relation & temporal multipliers ─────────────

            static float RelationExtra(OpDef o) => RuleLanguage.Relations.TryGetValue(o.rel ?? "", out var r)
                ? 1 + r.powerMul * (o.rel is "Chain" or "Bounce" or "Pierce" or "Spread" or "Link" ? Mathf.Max(1, o.times) / 2f : o.rel == "Cascade" ? 0.5f : 1)
                : 1;

            /// <summary>Periodic: ticks; deferred temporals: probability the trigger condition arrives in time × ForNextN uses.</summary>
            float TemporalMul(OpDef o) => o.time switch
            {
                "Periodic" => Mathf.Max(2, o.times),
                "UntilHit" or "UntilNextAttack" => 0.95f,
                "UntilDamaged" => Mathf.Clamp01(1 - Mathf.Exp(-Rate("Damaged", null) * Mathf.Max(1, o.duration))),
                "ForNextN" => Mathf.Max(1, o.times) * 0.9f,
                "Stack" => 1.25f,
                _ => 1,
            };

            // ───────────── Operations ─────────────

            float Mag(AbilityDef a, RuleDef r, OpDef o) => o.amount + o.per * Expected(a, r, o.scale);

            /// <summary>
            /// Overkill: a hit of M (attack-power units) on an enemy with remaining HP ~ uniform on [0, H] only deals
            /// E[min(hp, M)] = M − M²/2H (M ≤ H), H/2 beyond. H ≈ 5 basic hits of HP. Big single hits lose part of their value.
            /// </summary>
            static float Effective(float m) { const float H = 5f; return m <= H ? m - m * m / (2 * H) : H / 2; }

            static float AreaTargets(float radius) => Mathf.Clamp(1 + 0.35f * radius * radius, 1, 5);

            static float PosFactor(string at) => at switch
            {
                "Self" or "Ring" => 0.8f, "DashOrigin" => 0.6f, "DashPath" => 0.9f, "Cursor" or "Forward" => 0.8f, "Behind" or "LastPosition" => 0.4f,
                "Stored" => 0.5f, "RandomNear" => 0.45f, "BehindTarget" => 0.9f, "EntityPos" => 0.7f, _ => 1f,
            };

            float Targets(OpDef o) => o.target switch
            {
                "EnemiesNear" => Mathf.Min(AreaTargets(o.radius > 0 ? o.radius : 3) * 0.8f, b.cfg.maxTargetsPerProc),
                "EnemiesInCone" => Mathf.Min(1 + o.radius * 0.5f, b.cfg.maxTargetsPerProc),
                "EnemiesInLine" => Mathf.Min(1 + o.radius * 0.3f, b.cfg.maxTargetsPerProc),
                "MarkedEnemies" => 1 + Presence("mark") * 2,
                "StatusedEnemies" => 1 + (string.IsNullOrEmpty(o.status) ? 0.6f : Presence(o.status)) * 2,
                "StoredTarget" => 0.7f,
                "LastHitTarget" or "LastAttacker" => 0.85f,
                _ => 1,
            };

            static float SpawnDps(string entity, string rel) => entity switch
            {
                "Clone" => rel == "Copy" ? 1.5f : 1.0f,
                "PhantomWeapon" => 1.05f,
                "Turret" => 0.8f,
                "Orb" => 0.35f,
                "Zone" => 0.7f,
                "Mine" => 0.5f,
                "Totem" => 0.45f,
                _ => 0.15f,
            };

            void Produce(Dictionary<string, Dictionary<AbilityDef, float>> d, AbilityDef a, OpDef o, float f)
            {
                float t = Targets(o);
                switch (o.op)
                {
                    case "Damage": case "Detonate": case "Execute": case "Chain": case "Nova": case "Projectile": case "Slash": case "Beam": case "Drain": case "DetonateEntities":
                    {
                        float hits = o.op switch
                        {
                            "Nova" => AreaTargets(o.radius) * PosFactor(o.at), "Projectile" => Mathf.Max(1, o.count) * 0.6f,
                            "Chain" => Mathf.Max(1, o.count), "Slash" => 1.6f, "Beam" => 1 + o.radius * 0.25f, "DetonateEntities" => 1.5f, _ => t,
                        };
                        // kills scale with how hard the effect hits (a 0.2× poke rarely finishes anything)
                        float lethal = Mathf.Clamp(o.amount <= 0 ? 0.6f : o.amount, 0.1f, 2f) * 0.12f;
                        Add(d, "Hit", a, f * hits); Add(d, "Crit", a, f * hits * 0.12f); Add(d, "Kill", a, f * hits * lethal);
                        if (o.op == "Projectile") Add(d, "ProjectileEnd", a, f * Mathf.Max(1, o.count));
                        if (o.op == "Drain") Add(d, "Healed", a, f);
                        if (o.op == "DetonateEntities") { Add(d, "EntityExpired", a, f); Add(d, "CloneExpire", a, f * 0.5f); }
                        break;
                    }
                    case "ApplyStatus": case "Slow":
                    {
                        string st = o.op == "Slow" ? "chill" : o.status;
                        Add(d, "StatusApplied|" + st, a, f * t); Add(d, "StatusExpired|" + st, a, f * t * 0.6f); break;
                    }
                    case "Spread": case "Transfer":
                        Add(d, "StatusApplied|" + (o.status ?? "burn"), a, f * Mathf.Max(1, o.count)); Add(d, "StatusExpired|" + (o.status ?? "burn"), a, f * o.count * 0.6f); break;
                    case "Spawn":
                    {
                        float n = f * Mathf.Max(1, o.count);
                        if (o.entity == "Clone" || o.entity == "PhantomWeapon") { Add(d, "CloneSpawn", a, n); Add(d, "CloneExpire", a, n); }
                        else Add(d, "EntityExpired", a, n);
                        if (o.entity is "Clone" or "PhantomWeapon" or "Turret" or "Orb" or "Mine") { Add(d, "Hit", a, n * o.duration * 1.2f); Add(d, "Kill", a, n * o.duration * 0.12f); }
                        if (o.entity == "Turret") Add(d, "ProjectileEnd", a, n * o.duration * 1.5f);
                        break;
                    }
                    case "Heal": Add(d, "Healed", a, f); break;
                    case "Shield": Add(d, "ShieldBroken", a, Mathf.Min(f, 1f / Mathf.Max(1, o.duration)) * 0.4f); break;
                    case "DropPickup": Add(d, "Pickup", a, f); break;
                }
            }

            void Put(int dim, float v) => rep.dims[dim] += v;

            float StatPoints(string stat, float m)
            {
                if (!RuleLanguage.StatInfo.TryGetValue(stat ?? "", out var si)) return 0;
                return stat switch
                {
                    "Attack" => m * totalOffense,
                    "AttackSpeed" => m * Baseline * 1.1f,
                    "CritChance" => m * totalOffense * 1.5f,
                    "CritDamage" => m * totalOffense * 0.15f,
                    "ElementPower" => m * (20 + totalOffense * 0.25f),
                    _ => m * si.points,
                };
            }

            int StatDim(string stat) => RuleLanguage.StatInfo.TryGetValue(stat ?? "", out var si) ? System.Array.IndexOf(Dims, si.dim) : Uti;

            void TriggerOp(AbilityDef a, RuleDef r, OpDef o, float f, float mul, MetaMods m)
            {
                float M = Mag(a, r, o);
                float dur = o.duration * (1 + m.extend * 0.7f);
                float rad = o.radius * (1 + m.enlarge);
                int cnt = Mathf.Max(1, o.count) + m.multiply;
                float t = Targets(o);
                float el = m.infuse && !RuleLanguage.IsElement(o.element) ? 1.15f : 1;
                float k = mul * TemporalMul(o) * RelationExtra(o);
                float cost = 0;
                switch (o.op)
                {
                    case "Damage": Put(Off, Effective(M) * t * U * f * k * el); cost = t; break;
                    case "Drain": Put(Off, Effective(M) * t * U * f * k); Put(Sus, M * t * 0.4f * 250 * 0.15f * f * k); cost = t; break;
                    case "Nova": { float n = o.at == "DashPath" ? 1.4f : AreaTargets(rad) * PosFactor(o.at); Put(Off, Effective(M) * n * U * f * k * el); cost = 3; break; }
                    case "Slash": Put(Off, Effective(M) * Mathf.Min(1 + rad * 0.4f, 4) * U * f * k * el); cost = 2; break;
                    case "Beam": Put(Off, Effective(M) * Mathf.Min(1 + rad * 0.25f, 4) * U * f * k * el); cost = 3; break;
                    case "Projectile": Put(Off, Effective(M) * cnt * (o.mode == "Seek" || o.rel == "Seek" ? 0.8f : 0.6f) * U * f * k * el); cost = 2 * cnt; break;
                    case "Chain": { float jumps = Mathf.Min(cnt - 1, 1.5f); Put(Off, Effective(M) * (1 + 0.75f * jumps) * U * f * k); cost = cnt; break; }
                    case "ApplyStatus":
                        if (o.status is "burn" or "poison" or "bleed") Put(Off, M * dur * 0.7f * t * U * f * k);
                        else if (o.status == "chill") Put(Con, 6 * dur * t * f * k);
                        else Put(Off, 8 * dur * t * f * k * (o.status == "weaken" ? 0.6f : 1));
                        cost = t; break;
                    case "Slow": Put(Con, 8 * M * t * f * k); cost = t; break;
                    case "Detonate":
                    {
                        float presence = r.when.Any(c => c.type == "TargetHas" && c.text == o.status) ? 1 : Presence(o.status, a);
                        Put(Off, Effective(M * 1.3f) * t * presence * U * f * k); cost = 2 * t; break;
                    }
                    case "Spread": case "Transfer":
                    {
                        float presence = r.when.Any(c => c.type == "TargetHas" && c.text == o.status) ? 1 : Presence(o.status ?? "burn", a);
                        float sv = o.status is "burn" or "poison" or "bleed" ? 20 : o.status == "chill" ? 10 : 15;
                        Put(o.status == "chill" ? Con : Off, Mathf.Max(1, o.count) * sv * presence * f * k * (o.op == "Transfer" ? 0.8f : 1)); cost = 2; break;
                    }
                    // P(target hp ≤ θ) × E[remaining | ≤ θ] = θ · θH/2 with remaining HP ~ U[0,H], H ≈ 5 hits (same as overkill)
                    case "Execute": Put(Off, M * M * 5f / 2f * t * U * f * k); cost = t; break;
                    case "Stun": Put(Con, 25 * M * t * f * k); cost = t; break;
                    case "Taunt": Put(Def, 12 * M * AreaTargets(rad) * f * k); Put(Con, 6 * M * f * k); cost = 2; break;
                    case "Push": case "Pull": Put(Con, 0.6f * M * AreaTargets(rad) * PosFactor(o.at) * f * k); cost = 2; break;
                    case "Cleanse": Put(Def, 90 * M * f * k); cost = 1; break;
                    case "Heal": Put(Sus, 250 * M * f * k + 4); cost = 1; break;
                    case "Shield": Put(Def, 160 * M * Mathf.Min(1, dur / 3f) * Mathf.Min(f * TemporalMul(o), 1f / Mathf.Max(1, dur)) * mul); cost = 1; break;
                    case "Buff":
                    {
                        float uptime = Mathf.Min(1, f * TemporalMul(o) * dur);
                        Put(StatDim(o.stat), StatPoints(o.stat, M) * uptime * mul); cost = 1; break;
                    }
                    case "Blink": case "Leap": Put(Mob, 6 + 60 * f); cost = 1; break;
                    case "Swap": Put(Mob, 10 * f); Put(Def, 6 * f); cost = 1; break;
                    case "MoveEntity": Put(Uti, 8 * f * Mathf.Min(1, rep.liveSpawns.Values.Sum())); cost = 1; break;
                    case "ResetCooldowns": Put(Uti, 60 * M * f * k); cost = 1; break;
                    case "GainDash": Put(Mob, 10 * cnt * f); cost = 1; break;
                    case "Spawn":
                    {
                        float live = f * cnt * Mathf.Min(dur, b.cfg.maxSpawnDuration);
                        rep.liveSpawns[r] = (rep.liveSpawns.TryGetValue(r, out var ls) ? ls : 0) + live;
                        float capF = Mathf.Min(1, b.cfg.maxSpawnPerOwner / Mathf.Max(1, live));
                        Put(Off, SpawnDps(o.entity, o.rel ?? o.mode) * M * U * live * capF * mul * el);
                        if (o.entity == "Totem") Put(Sus, 2.5f * M * live * capF);
                        if (o.entity == "Barrier") Put(Def, 18 * live * capF);
                        if (o.entity == "Decoy") Put(Def, 22 * live * capF);
                        cost = 6 * cnt;
                        break;
                    }
                    case "DetonateEntities":
                    {
                        float avail = Mathf.Min(b.cfg.maxSpawnPerOwner, rep.liveSpawns.Values.Sum());
                        Put(Off, M * Mathf.Max(0.3f, avail) * 1.5f * U * f * k); cost = 4; break;
                    }
                    case "ExtendEntities": Put(Off, 0.08f * M * rep.liveSpawns.Values.Sum() * U * f); cost = 1; break;
                    case "EmpowerNext":
                    {
                        float attacks = Mathf.Min(f * cnt, 1.5f);
                        Put(Off, Effective(M * 1.3f) * U * attacks * k); cost = 1; break;
                    }
                    case "GainGold": Put(Eco, 0.8f * M * f * 60); cost = 1; break;     // per minute-ish worth
                    case "GainXp": Put(Eco, 1.2f * M * f * 60); cost = 1; break;
                    case "GainPotion": Put(Sus, 900 * cnt * f); cost = 1; break;
                    case "DropPickup": Put(o.mode == "heart" ? Sus : Eco, (o.mode == "heart" ? 8 : 0.8f) * M * f * 60 / (o.mode == "heart" ? 100 : 1)); cost = 1; break;
                }
                rep.cost[r] = (rep.cost.TryGetValue(r, out var c) ? c : 0) + cost * f * TemporalMul(o) * RelationExtra(o);
            }

            void ContinuousOp(AbilityDef a, RuleDef r, OpDef o, float avail)
            {
                float M = Mag(a, r, o);
                switch (o.op)
                {
                    case "ModifyStat":
                        switch (o.mode)
                        {
                            case "Min": Put(StatDim(o.stat), StatPoints(o.stat, Mathf.Max(0, o.amount)) * 0.35f * avail); break;
                            case "Max": Put(StatDim(o.stat), -StatPoints(o.stat, Mathf.Abs(o.amount)) * 0.3f); break;
                            case "Override": Put(StatDim(o.stat), StatPoints(o.stat, o.amount) * avail); break;
                            default: Put(StatDim(o.stat), StatPoints(o.stat, M) * avail); break;
                        }
                        break;
                    case "AmplifyDamage": Put(Off, M * totalOffense * avail); break;
                    case "Resist": Put(Def, 220 * Mathf.Clamp(M, 0, 0.6f) * avail); break;
                    case "Aura": Put(Off, M * AreaTargets(o.radius) * 0.6f * U * avail); rep.cost[r] = (rep.cost.TryGetValue(r, out var c) ? c : 0) + 2 * AreaTargets(o.radius); break;
                    case "StatusPotency":
                    {
                        float statusShare = abs.Sum(x => x.rules.SelectMany(rr => rr.ops).Count(op => op.op is "ApplyStatus" or "Spread" or "Detonate" or "Aura")) * 15 + (string.IsNullOrEmpty(b.weaponElement) ? 0 : 20);
                        Put(Off, statusShare * M * avail); break;
                    }
                    case "Convert":
                        switch (o.from + ">" + o.to)
                        {
                            case "Heal>Shield": Put(Def, (4 + 0.6f * sustainSources) * o.amount * avail); Put(Sus, -sustainSources * 0.5f * avail); break;
                            case "Overheal>Shield": Put(Def, (6 + 0.4f * sustainSources) * o.amount * avail); break;
                            case "DamageDealt>Heal": Put(Sus, 250 * o.amount * (totalOffense / U) * 0.15f * avail); break;
                            case "DamageTaken>Shield": Put(Def, 200 * o.amount * Rate("Damaged", null) * avail); break;
                            case "Gold>Heal": Put(Sus, 30 * o.amount * Rate("Pickup", null) * avail); break;
                            case "Crit>Heal": Put(Sus, 250 * o.amount * Rate("Crit", null) * 1.5f * avail); break;
                            case "Overkill>Nova": Put(Off, 0.5f * o.amount * Rate("Overkill", null) * 2.5f * U * avail); break;
                            case "Shield>Damage": Put(Off, o.amount * Mathf.Max(0.05f, ShieldUptime()) * totalOffense * avail); break;
                            // ~6 m walked between hits × ratio per metre, applied to basic attacks only
                            case "MoveDistance>Damage": Put(Off, o.amount * 6 * Baseline * 0.35f * avail); break;
                            case "Xp>Gold": Put(Eco, 20 * o.amount * avail); Put(Sca, -8 * o.amount); break;
                        }
                        break;
                }
            }

            /// <summary>Average shield / max HP the build keeps up (shield ops, conversions, guard weapon).</summary>
            float ShieldUptime() => Mathf.Clamp(rep.dims[Def] / 400f + (b.shieldWeapon ? 0.05f : 0), 0, 0.5f);

            /// <summary>Replacement: value of the new action minus the value of what it replaces.</summary>
            void ReplaceRule(AbilityDef a, RuleDef r)
            {
                float rate = r.replaces switch { "Dash" => Rate("Dash", null), "Potion" => 1f / 60, "ComboFinisher" => Rate("ComboFinish", null), _ => 0 };
                foreach (var o in r.ops)
                {
                    float gain = o.mode switch
                    {
                        "Blink" => 14 + 30 * rate,                               // longer reach and a free reposition, minus the dash i-frames
                        "Nova" => o.amount * AreaTargets(2.2f) * 0.8f * U * rate,
                        "Shield" => 160 * o.amount * Mathf.Min(rate, 0.33f),
                        "Spawn" => SpawnDps(o.entity, null) * o.amount * U * rate * Mathf.Min(3, o.duration),
                        "Projectile" => o.amount * Mathf.Max(1, o.count) * 0.6f * U * rate,
                        _ => 0,
                    };
                    float lost = r.replaces switch { "Dash" => 10, "Potion" => 12, _ => 0 };      // the old action's own value (i-frames / heal)
                    Put(r.replaces == "Potion" ? Sus : Off, gain - lost);
                }
            }

            /// <summary>Constraints are prices: they subtract the value of what the player gives up.</summary>
            void ConstrainOp(OpDef o)
            {
                switch (o.op)
                {
                    case "Forbid":
                        // the price is what the action is actually worth to this build (potions: ~2 × 40% HP per life;
                        // regen: the build's regen stat; dash: i-frames + mobility; skills: their share of damage)
                        Put(o.mode switch { "Dash" => Mob, "Potion" or "Regen" => Sus, _ => Uti }, -(o.mode switch
                        {
                            "Dash" => 35f, "Potion" => 4f, "Regen" => 6f * 0.3f + 3f, "Skill" => 40f, _ => 10f,
                        }));
                        break;
                    case "CapStat":
                        if (RuleLanguage.StatInfo.TryGetValue(o.stat ?? "", out var si)) Put(StatDim(o.stat), -Mathf.Abs(StatPoints(o.stat, -o.amount)) * 0.8f);
                        break;
                }
            }

            void SystemOp(OpDef o)
            {
                switch (o.op)
                {
                    case "OfferCount": Put(Uti, 12 * o.amount); break;
                    case "TierBias": Put(Sca, 10 * o.amount); break;
                    case "BudgetBias": Put(Sca, 70 * o.amount); break;
                    case "RerollDiscount": Put(Eco, 15 * o.amount); break;
                    case "TagBias": Put(Sca, 4 * o.amount); break;
                    case "ExtraEvolution": Put(Sca, 45); break;
                    case "UpgradeRandom": Put(Sca, o.amount * (abs.Count > 1 ? abs.Where(x => x.power > 0).Select(x => x.power).DefaultIfEmpty(20).Average() : 0)); break;
                    case "DelayedReward": Put(Sca, 30 + o.amount * 0.1f); break;
                }
            }
        }
    }

    /// <summary>The live tuning (Data/AbilityRules.asset), or code defaults if the asset is missing.</summary>
    public static class RuleConfig
    {
        static AbilityRulesDef fallback, applied;

        /// <summary>Re-apply the asset tuning after it was edited in the Inspector.</summary>
        public static void Reload() => applied = null;

        public static AbilityRulesDef Def
        {
            get
            {
                try
                {
                    var d = DB.Asset != null && DB.Asset.abilityRules != null ? DB.Asset.abilityRules.def : null;
                    if (d != null) { if (applied != d) { applied = d; RuleLanguage.ApplyTuning(d.prims); } return d; }
                }
                catch { }
                return fallback ??= new AbilityRulesDef();
            }
        }
    }
}
