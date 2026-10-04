using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// ④ Ability Graph analysis → ⑤ Stat preprocessing → ⑥ Generation Context → ⑦ Primitive Retrieval.
    /// Produces a compact snapshot of the player's state to hand to the AI.
    /// </summary>
    public class GenerationContext
    {
        public string weapon, weaponTags, costume, costumeTags;
        public List<string> activeSources = new List<string>();     // mechanics the player can already trigger
        public List<string> seededSources = new List<string>();     // modifiers exist, but no generator yet (e.g. clone damage with no clone)
        public List<Telemetry.Pattern> patterns;
        public List<string> unused;
        public Dictionary<Attr, int> attrs = new Dictionary<Attr, int>();
        public Dictionary<Attr, int> invest = new Dictionary<Attr, int>();
        public List<AbilityGraph> owned;
        public List<KeyValuePair<string, int>> intent;
        public List<Atom> retrieved = new List<Atom>();
        public List<Atom> exploration = new List<Atom>();
        public int tier;
        public string region;
        public HashSet<string> relevantTags = new HashSet<string>();

        public static GenerationContext Build(Player p, Telemetry tel, System.Random rnd)
        {
            var c = new GenerationContext
            {
                weapon = p.Weapon.name,
                weaponTags = p.Weapon.tags,
                costume = p.Costume.name,
                costumeTags = p.Costume.tags,
                owned = new List<AbilityGraph>(p.Abilities.Owned),
                tier = p.Level / 3,
                region = World.I != null ? World.I.Gen.RegionName(p.Pos) : "",
            };
            var f = tel.Extract();
            c.patterns = tel.Patterns(f);
            c.unused = tel.Unused(f);
            c.intent = tel.TopIntent(4).ToList();
            foreach (Attr a in System.Enum.GetValues(typeof(Attr)))
            {
                c.attrs[a] = p.Attrs[(int)a];
                c.invest[a] = tel.investRecent.TryGetValue(a, out var n) ? n : 0;
            }

            // ④ Current Ability Graph analysis: Active vs Seeded
            c.activeSources.AddRange(p.Weapon.tags.Split(' '));
            c.activeSources.Add("DODGE");
            var generators = new HashSet<string>();
            foreach (var g in c.owned)
            {
                if (g.kind != "trigger") continue;
                foreach (var e in g.effects)
                {
                    if (e.action == "Spawn" && !string.IsNullOrEmpty(e.entity)) generators.Add(e.entity.ToUpper());
                    if (!string.IsNullOrEmpty(e.element)) generators.Add(e.element.ToUpper());
                    if (e.action == "Projectile") generators.Add("PROJECTILE");
                    if (e.action == "ApplyStatus") generators.Add("STATUS");
                }
            }
            c.activeSources.AddRange(generators);
            foreach (var g in c.owned)
                if (g.kind == "modifier" && !generators.Contains(g.modTag) && !c.activeSources.Contains(g.modTag))
                    c.seededSources.Add(g.modTag);

            // Relevant tags = weapon + patterns + intent + seeded
            foreach (var t in p.Weapon.tags.Split(' ')) c.relevantTags.Add(t);
            foreach (var t in p.Costume.tags.Split(' ')) c.relevantTags.Add(t);
            foreach (var pt in c.patterns.Take(3)) foreach (var t in pt.tags.Split(' ')) c.relevantTags.Add(t);
            foreach (var kv in c.intent) c.relevantTags.Add(kv.Key);
            foreach (var s in c.seededSources) c.relevantTags.Add(s);
            foreach (var s in generators) c.relevantTags.Add(s);

            // ⑦ Primitive Retrieval: relevant atoms + 10–20% distant atoms (Exploration Pool)
            foreach (var a in AbilityLanguage.Atoms)
            {
                if (a.layer == "Rule" || a.layer == "Temporal" || a.layer == "Condition") { c.retrieved.Add(a); continue; }
                if (a.tags.Any(t => c.relevantTags.Contains(t))) c.retrieved.Add(a);
            }
            var far = AbilityLanguage.Atoms.Where(a => !c.retrieved.Contains(a) && a.layer != "Rule").OrderBy(_ => rnd.Next()).Take(Mathf.Max(3, c.retrieved.Count / 6)).ToList();
            c.exploration = far;
            return c;
        }

        static string Level(int v, int avg)
        {
            float r = (v - avg) / Mathf.Max(1f, avg);
            if (r > 0.6f) return "VERY_HIGH";
            if (r > 0.2f) return "HIGH";
            if (r < -0.6f) return "VERY_LOW";
            if (r < -0.2f) return "LOW";
            return "AVERAGE";
        }

        /// <summary>⑥ The player-state text sent to the LLM (not raw logs — a compressed representation).</summary>
        public string PlayerProfile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("EQUIPMENT");
            sb.AppendLine($"- {weapon} (tags: {weaponTags})");
            sb.AppendLine($"- Costume: {costume} (tags: {costumeTags})");
            sb.AppendLine("ACTIVE SOURCES: " + string.Join(", ", activeSources.Distinct()));
            if (seededSources.Count > 0)
                sb.AppendLine("SEEDED SOURCES (has modifier, no generator yet — player deliberately chose it): " + string.Join(", ", seededSources));
            sb.AppendLine("DOMINANT BEHAVIOR");
            if (patterns.Count == 0) sb.AppendLine("- (not enough data)");
            foreach (var p in patterns.Take(4)) sb.AppendLine($"- {p.id}: {p.desc} (strength {p.confidence:0.00})");
            if (unused.Count > 0) sb.AppendLine("UNUSED: " + string.Join(", ", unused));
            int avg = Mathf.Max(1, (int)attrs.Values.Average());
            sb.AppendLine("STATS: " + string.Join(", ", attrs.Select(kv => $"{kv.Key}={Level(kv.Value, avg)}")));
            var inv = invest.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ToList();
            if (inv.Count > 0) sb.AppendLine("STAT INVESTMENT INTENT: favoring " + string.Join(" > ", inv.Select(kv => kv.Key)));
            sb.AppendLine("CURRENT ABILITY GRAPH");
            if (owned.Count == 0) sb.AppendLine("- (none)");
            foreach (var g in owned) sb.AppendLine($"- {g.mechanic}: {g.Explain()} [tags: {string.Join(" ", g.tags)}]");
            if (intent.Count > 0) sb.AppendLine("PLAYER INTENT (tags picked before): " + string.Join(", ", intent.Select(kv => $"{kv.Key}x{kv.Value}")));
            sb.AppendLine($"EVOLUTION TIER: {tier}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// ⑩ Schema Validator → ⑪ World Rule Validator → ⑫ Synergy Resolver → ⑬ Evaluation → ⑭ Power Budget.
    /// Even if the AI produces a weird graph, the game engine filters/repairs it here. Numbers are decided here, not by the AI.
    /// </summary>
    public static class AbilityValidator
    {
        public static readonly string[] TriggerEvents =
            { "Attack", "Hit", "Crit", "Kill", "Dash", "DashEnd", "PerfectDodge", "Damaged", "SkillCast", "ComboFinish", "LowHealth", "Interval", "CloneSpawn", "CloneExpire", "StatusApplied", "Guard" };
        static readonly HashSet<string> Actions = new HashSet<string> { "Damage", "Projectile", "Nova", "Lightning", "Spawn", "Heal", "Shield", "Buff", "ApplyStatus", "Pull", "Push", "Blink", "ResetCooldown", "Store", "Release" };
        static readonly HashSet<string> Entities = new HashSet<string> { "Clone", "Turret", "Orb", "Zone", "PhantomWeapon" };
        static readonly HashSet<string> BuffStats = new HashSet<string> { "Attack", "AttackSpeed", "MoveSpeed", "CritChance", "Armor" };

        public class Result { public bool ok; public List<string> errors = new List<string>(); public List<string> repairs = new List<string>(); }

        public static Result Validate(AbilityGraph g, Player p)
        {
            var r = new Result { ok = true };
            void Fail(string e) { r.ok = false; r.errors.Add(e); }

            if (g == null) { Fail("비어 있음"); return r; }
            if (string.IsNullOrEmpty(g.kind)) g.kind = "trigger";

            // ── Schema ──
            if (g.kind == "modifier")
            {
                if (string.IsNullOrEmpty(g.modTag)) Fail("태그 없는 증폭");
                g.modMul = Mathf.Clamp(g.modMul <= 0 ? 1.2f : g.modMul, 1.05f, 1.5f);
                return Finalize(g, p, r);
            }
            if (g.kind == "stat")
            {
                if (!System.Enum.TryParse<StatId>(g.stat, out _)) Fail("알 수 없는 능력치 " + g.stat);
                return Finalize(g, p, r);
            }
            if (!TriggerEvents.Contains(g.trigger)) Fail("알 수 없는 트리거 " + g.trigger);
            if (g.effects == null || g.effects.Count == 0) Fail("효과 없음");
            if (g.effects != null && g.effects.Count > 3) { g.effects.RemoveRange(3, g.effects.Count - 3); r.repairs.Add("효과를 3개로 줄임"); }
            if (g.conditions == null) g.conditions = new List<CondNode>();
            g.conditions.RemoveAll(c => !AbilityLanguage.Exists(c.type, "Condition") && c.type != "EveryNth");

            if (g.effects != null)
                foreach (var e in g.effects)
                {
                    if (!Actions.Contains(e.action)) { Fail("알 수 없는 행동 " + e.action); continue; }
                    if (!string.IsNullOrEmpty(e.element) && !AbilityLanguage.Exists(e.element, "Element")) { r.repairs.Add("속성 제거 " + e.element); e.element = null; }
                    if (!string.IsNullOrEmpty(e.relation) && !AbilityLanguage.Exists(e.relation, "Relation")) { r.repairs.Add("관계 제거 " + e.relation); e.relation = null; }
                    if (!string.IsNullOrEmpty(e.form) && !AbilityLanguage.Exists(e.form, "Form")) e.form = null;
                    if (e.action == "Spawn")
                    {
                        if (string.IsNullOrEmpty(e.entity) || !Entities.Contains(e.entity)) Fail("유효한 소환체 없음");
                    }
                    // ── World Rules ──
                    // Grip: a clone cannot hold weapons → anything other than PhantomWeapon is repaired to Inherit
                    if (e.entity == "Clone" && e.relation == "Equip") { e.relation = "Inherit"; r.repairs.Add("분신 장착 불가 → Inherit"); }
                    if (e.action == "Buff" && !BuffStats.Contains(e.stat ?? "")) { e.stat = "Attack"; r.repairs.Add("버프 능력치 → Attack"); }
                    if (e.action == "ApplyStatus" && string.IsNullOrEmpty(e.element)) e.element = "Fire";
                }

            // Infinite-loop prevention: a CloneSpawn trigger that spawns another clone is rejected
            if (g.trigger == "CloneSpawn" && g.effects != null && g.effects.Any(e => e.action == "Spawn" && e.entity == "Clone")) Fail("분신 소환 무한 반복");
            if (g.trigger == "Interval" && !g.conditions.Any(c => c.type == "Cooldown")) g.conditions.Add(new CondNode { type = "Cooldown", value = 4f });
            // Abilities that fire very often (Hit/Attack) must carry a probability/cooldown condition
            if ((g.trigger == "Hit" || g.trigger == "Attack") && g.effects != null && g.effects.Any(e => e.action is "Spawn" or "Nova" or "Lightning" or "Projectile")
                && !g.conditions.Any(c => c.type is "Chance" or "Cooldown" or "EveryNth"))
            {
                g.conditions.Add(new CondNode { type = "Chance", value = 0.25f });
                r.repairs.Add("25% 확률 추가 (잦은 트리거)");
            }
            if (g.effects != null && g.effects.Any(e => e.relation == "Consume") && !g.conditions.Any(c => c.type == "TargetHasStatus"))
                g.conditions.Add(new CondNode { type = "TargetHasStatus", text = "burn" });

            return Finalize(g, p, r);
        }

        static Result Finalize(AbilityGraph g, Player p, Result r)
        {
            if (!r.ok) return r;
            // Duplicate check
            if (p != null)
            {
                string sig = g.Signature();
                foreach (var o in p.Abilities.Owned)
                    if (o.Signature() == sig) { r.ok = false; r.errors.Add("보유 능력과 중복 " + o.mechanic); return r; }
            }
            AutoTag(g);
            Balance(g, p);
            return r;
        }

        /// <summary>⑫ Synergy Resolver's foundation: tags derived automatically from the graph contents. Existing modifiers apply to matching tags automatically.</summary>
        public static void AutoTag(AbilityGraph g)
        {
            var set = new HashSet<string>(g.tags ?? new List<string>());
            if (g.kind == "modifier") set.Add(g.modTag);
            if (!string.IsNullOrEmpty(g.trigger))
            {
                if (g.trigger is "Dash" or "DashEnd" or "PerfectDodge") set.Add("DODGE");
                if (g.trigger is "Crit") set.Add("CRIT");
                if (g.trigger is "Kill") set.Add("KILL");
                if (g.trigger is "SkillCast") set.Add("SKILL");
                if (g.trigger is "Damaged" or "LowHealth" or "Guard") set.Add("DEFENSE");
                if (g.trigger is "CloneSpawn" or "CloneExpire") set.Add("CLONE");
            }
            foreach (var e in g.effects ?? new List<EffectNode>())
            {
                if (!string.IsNullOrEmpty(e.entity)) set.Add(e.entity == "PhantomWeapon" ? "CLONE" : e.entity.ToUpper());
                if (!string.IsNullOrEmpty(e.element)) set.Add(e.element.ToUpper());
                if (e.action == "Projectile") set.Add("PROJECTILE");
                if (e.action is "Nova" or "Release") set.Add("AREA");
                if (e.action is "Heal" or "Shield") set.Add("DEFENSE");
                if (e.action == "Blink") set.Add("MOBILITY");
                if (e.action == "ApplyStatus") set.Add("STATUS");
            }
            g.tags = set.Where(t => !string.IsNullOrEmpty(t)).ToList();
        }

        /// <summary>
        /// ⑭ Power Budget: total budget scales with evolution tier; each atom's cost is subtracted and the remainder is spread across the numbers.
        /// Even if the AI writes "damage 730%", it's ignored here.
        /// </summary>
        public static void Balance(AbilityGraph g, Player p)
        {
            int tier = p != null ? p.Level / 3 : 0;
            float budget = 100 + tier * 12;
            if (g.kind == "modifier") { g.cost = 40; g.modMul = Mathf.Round(Mathf.Lerp(1.15f, 1.3f, Mathf.Clamp01(tier / 5f)) * 20) / 20f; return; }
            if (g.kind == "stat") { g.cost = 30; g.statValue = StatBudget(AbilityGraph.ParseStat(g.stat), tier); return; }

            // Trigger frequency → frequent triggers have lower efficiency
            float freq = g.trigger switch
            {
                "Hit" or "Attack" => 0.45f, "Crit" => 0.85f, "Kill" => 0.9f, "Dash" or "DashEnd" => 1f, "PerfectDodge" => 1.6f,
                "Damaged" => 0.8f, "SkillCast" => 1.1f, "ComboFinish" => 1.0f, "LowHealth" => 1.8f, "Interval" => 1f,
                "CloneSpawn" or "CloneExpire" => 0.9f, "StatusApplied" => 0.6f, "Guard" => 1.5f, _ => 1f,
            };
            foreach (var c in g.conditions)
            {
                if (c.type == "Chance") freq *= Mathf.Lerp(2.2f, 1f, Mathf.Clamp01(c.value));
                if (c.type == "Cooldown") freq *= 1 + Mathf.Clamp(c.value, 1, 15) * 0.12f;
                if (c.type == "EveryNth") freq *= 1 + Mathf.Clamp(c.value, 2, 6) * 0.25f;
                if (c.type == "TargetHasStatus" || c.type == "HealthBelow") freq *= 1.3f;
                if (c.type == "Chance") c.value = Mathf.Clamp(c.value, 0.08f, 0.6f);
                if (c.type == "Cooldown") c.value = Mathf.Clamp(c.value, 0.5f, 20f);
                if (c.type == "EveryNth") c.value = Mathf.Clamp(Mathf.Round(c.value), 2, 6);
                if (c.type == "HealthBelow") c.value = Mathf.Clamp(c.value, 0.2f, 0.6f);
            }

            float spent = 0;
            foreach (var e in g.effects) spent += AbilityLanguage.ById.TryGetValue(e.action, out var a) ? a.cost : 30;
            foreach (var e in g.effects) if (!string.IsNullOrEmpty(e.relation) && AbilityLanguage.ById.TryGetValue(e.relation, out var rel)) spent += rel.cost;
            g.cost = spent;
            float remaining = Mathf.Max(20, budget - spent) * freq;
            float share = remaining / g.effects.Count;
            float k = share / 50f;   // 1.0 ≈ standard

            foreach (var e in g.effects)
            {
                bool hasEl = !string.IsNullOrEmpty(e.element);
                switch (e.action)
                {
                    case "Damage": e.power = R2(0.6f * k + 0.2f); e.radius = e.form is "AtSelf" or "Ring" or "DashOrigin" ? R1(1.4f + k * 0.4f) : 0; break;
                    case "Projectile": e.count = Mathf.Clamp(Mathf.RoundToInt(1 + k * 1.5f), 1, 6); e.power = R2(0.55f * k / Mathf.Sqrt(e.count) + 0.15f); break;
                    case "Nova": e.power = R2(0.55f * k + 0.15f); e.radius = R1(1.8f + k * 0.6f); break;
                    case "Lightning": e.power = R2(0.6f * k + 0.2f); e.count = e.relation == "Chain" ? Mathf.Clamp(Mathf.RoundToInt(2 + k), 2, 5) : 1; break;
                    case "Spawn":
                        e.count = Mathf.Clamp(e.entity == "Orb" ? Mathf.RoundToInt(1 + k) : 1, 1, 3);
                        e.duration = R1(Mathf.Clamp(1.2f + k * 1.2f, 1f, e.entity == "Orb" ? 12f : 6f));
                        e.power = R2(0.35f + k * 0.25f);
                        break;
                    case "Heal": e.power = R2(Mathf.Clamp(0.02f + k * 0.03f, 0.01f, 0.15f)); break;
                    case "Shield": e.power = R2(Mathf.Clamp(0.08f + k * 0.08f, 0.05f, 0.4f)); e.duration = R1(2.5f + k); break;
                    case "Buff": e.power = R2(Mathf.Clamp(0.1f + k * 0.12f, 0.05f, 0.6f)); e.duration = R1(2.5f + k * 1.5f); break;
                    case "ApplyStatus": e.duration = R1(2f + k); e.power = R2(0.3f + k * 0.2f); break;
                    case "Pull": e.radius = R1(2.5f + k * 0.8f); break;
                    case "Push": e.radius = R1(2f + k * 0.6f); e.power = R2(5 + k * 3); break;
                    case "Blink": e.radius = R1(2.5f + k); break;
                    case "ResetCooldown": e.power = R2(Mathf.Clamp(0.1f + k * 0.15f, 0.05f, 0.5f)); break;
                    case "Store": e.count = Mathf.Clamp(Mathf.RoundToInt(3 + k * 2), 3, 10); break;
                    case "Release": e.radius = R1(2f + k * 0.5f); e.power = R2(0.3f + k * 0.2f); break;
                }
                if (hasEl && e.action is "Damage" or "Nova" or "Projectile") e.power = R2(e.power * 0.9f);
                if (e.temporal == "Delayed" && e.delay <= 0) e.delay = 0.5f;
                e.delay = Mathf.Clamp(e.delay, 0, 2f);
                if (e.relation == "Repeat") e.delay = Mathf.Max(e.delay, 0.25f);
            }
        }

        static float R1(float v) => Mathf.Round(v * 10) / 10f;
        static float R2(float v) => Mathf.Round(v * 100) / 100f;

        public static float StatBudget(StatId s, int tier)
        {
            float t = 1 + tier * 0.15f;
            return s switch
            {
                StatId.MaxHp => Mathf.Round(18 * t),
                StatId.Attack => R2(0.1f * t),
                StatId.AttackSpeed => R2(0.1f * t),
                StatId.MoveSpeed => R2(0.07f * t),
                StatId.CritChance => R2(0.06f * t),
                StatId.CritDamage => R2(0.2f * t),
                StatId.CooldownReduction => R2(0.06f * t),
                StatId.Armor => R2(0.05f * t),
                StatId.Regen => R1(0.5f * t),
                StatId.XpGain => R2(0.15f * t),
                StatId.GoldGain => R2(0.2f * t),
                StatId.ElementPower => R2(0.15f * t),
                _ => 0.1f,
            };
        }

        /// <summary>⑬ Mechanic evaluation (behavior relevance, synergy, intent, novelty, complexity).</summary>
        public static float Score(AbilityGraph g, GenerationContext c)
        {
            float rel = 0, syn = 0, intent = 0, novelty = 1, complexity;
            foreach (var p in c.patterns.Take(3))
                foreach (var t in p.tags.Split(' '))
                    if (g.tags.Contains(t)) rel += p.confidence;
            foreach (var o in c.owned)
                foreach (var t in g.tags)
                    if (o.tags.Contains(t)) syn += 0.3f;
            foreach (var s in c.seededSources) if (g.tags.Contains(s)) syn += 1.2f;   // a generator appeared for a Seeded source → big synergy
            foreach (var kv in c.intent) if (g.tags.Contains(kv.Key)) intent += 0.2f * kv.Value;
            foreach (var o in c.owned) if (o.mechanic == g.mechanic) novelty -= 0.6f;
            complexity = g.effects?.Count ?? 0;
            foreach (var w in c.weaponTags.Split(' ')) if (g.tags.Contains(w)) rel += 0.3f;
            return rel * 1.0f + syn * 0.9f + intent * 0.7f + novelty * 0.6f - complexity * 0.12f + Random.Range(0, 0.4f);
        }
    }
}
