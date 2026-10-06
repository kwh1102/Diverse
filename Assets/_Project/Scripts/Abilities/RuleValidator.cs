using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Deterministic validation of a Rule IR ability. The LLM only proposes; this decides. Stages run in order and a hard
    /// failure stops the pipeline (later stages assume earlier ones passed) — the order of the design doc (#38):
    ///   1 Schema · 2 Type (event context ↔ what ops need; relation/temporal type constraints)
    ///   3 Reference (states, selectors, values, pairs) · 4 Capability (entity capabilities, World rule Grip)
    ///   5 Bounds (state/parameter clamps, world invariants) · 6 Cycle (event → op → event feedback loops, with gain)
    ///   7 Duplicate · 8 Tier (complexity unlocks) · 9 Power (build-aware ΔPower vs budget, numbers rebalanced to fit)
    ///   10 Risk (risk vector caps — separate from power) · 11 Runtime cost (spawn & proc budget) · 12 Simulation (optional)
    /// Every failure carries a machine-readable code so the LLM repair step (or the local repairer) knows what to change.
    /// </summary>
    public static class RuleValidator
    {
        public class Issue
        {
            public string stage, code, where, fix;
            public bool repaired;
            public override string ToString() => $"{stage}/{code} @{where}{(repaired ? " (수리됨: " + fix + ")" : string.IsNullOrEmpty(fix) ? "" : " → " + fix)}";
        }

        public class Result
        {
            public bool ok = true;
            public string failedStage;
            public readonly List<Issue> issues = new List<Issue>();
            public float delta, budget;
            public string vector, risk, sim;
            public IEnumerable<Issue> Errors => issues.Where(i => !i.repaired);
            public IEnumerable<Issue> Repairs => issues.Where(i => i.repaired);
            public string Summary() => ok ? $"ΔPower {delta:0} / 예산 {budget:0}" : $"[{failedStage}] {string.Join("; ", Errors.Select(e => e.code + "@" + e.where))}";
            /// <summary>For the LLM repair prompt.</summary>
            public string Machine() => string.Join("\n", Errors.Select(e => $"- stage={e.stage} code={e.code} at={e.where} required_fix={e.fix}"));
        }

        static readonly string[] Cmps = { ">=", "<=", ">", "<", "==" };
        static readonly string[] TargetKinds = { "Elite", "Boss", "Normal", "Ranged", "Melee" };

        /// <param name="simulate">Also run the headless simulation stage (slower; used for final offers and tests).</param>
        public static Result Validate(AbilityDef a, BuildContext b, bool rebalance = true, bool simulate = false)
        {
            var r = new Result();
            if (a == null) { r.ok = false; r.failedStage = "Schema"; r.issues.Add(new Issue { stage = "Schema", code = "EMPTY", where = "ability" }); return r; }
            a.EnsureLists();
            var cfg = b.cfg;

            bool Stage(string name, System.Action<System.Action<string, string, string>, System.Action<string, string, string>> body)
            {
                body((code, where, fix) => r.issues.Add(new Issue { stage = name, code = code, where = where, fix = fix }),
                     (code, where, fix) => r.issues.Add(new Issue { stage = name, code = code, where = where, fix = fix, repaired = true }));
                if (r.issues.Any(i => i.stage == name && !i.repaired)) { r.ok = false; r.failedStage = name; return false; }
                return true;
            }

            if (!Stage("Schema", (fail, fix) => Schema(a, cfg, fail, fix))) return r;
            if (!Stage("Type", (fail, fix) => Types(a, fail, fix))) return r;
            if (!Stage("Reference", (fail, fix) => References(a, b, fail, fix))) return r;
            if (!Stage("Capability", (fail, fix) => Capability(a, fail, fix))) return r;
            if (!Stage("Bounds", (fail, fix) => Bounds(a, cfg, fail, fix))) return r;
            Tag(a);
            if (!Stage("Cycle", (fail, fix) => Cycles(a, b, fail, fix))) return r;
            if (!Stage("Duplicate", (fail, fix) => Duplicate(a, b, fail))) return r;
            if (!Stage("Tier", (fail, fix) => Tier(a, b, fail))) return r;
            if (!Stage("Power", (fail, fix) => Power(a, b, r, rebalance, fail, fix))) return r;
            // The balancer may have relaxed cooldowns/chances: re-run the loop check on the final numbers
            if (!Stage("Cycle2", (fail, fix) => Cycles(a, b, fail, fix))) return r;
            if (!Stage("Risk", (fail, fix) => Risk(a, b, r, fail))) return r;
            if (!Stage("RuntimeCost", (fail, fix) => RuntimeCost(a, b, fail, fix))) return r;
            if (simulate && !Stage("Simulation", (fail, fix) => Simulation(a, b, r, fail))) return r;
            a.mechanic = Mechanic(a);
            return r;
        }

        // ───────────── 1 Schema ─────────────

        static void Schema(AbilityDef a, AbilityRulesDef cfg, System.Action<string, string, string> fail, System.Action<string, string, string> fix)
        {
            if (a.rules.Count == 0) { fail("NO_RULES", "ability", "add at least one rule"); return; }
            if (a.rules.Count > cfg.maxRulesPerAbility) { a.rules.RemoveRange(cfg.maxRulesPerAbility, a.rules.Count - cfg.maxRulesPerAbility); fix("TOO_MANY_RULES", "ability", $"kept {cfg.maxRulesPerAbility}"); }
            if (a.states.Count > cfg.maxStatesPerAbility) { a.states.RemoveRange(cfg.maxStatesPerAbility, a.states.Count - cfg.maxStatesPerAbility); fix("TOO_MANY_STATES", "ability", $"kept {cfg.maxStatesPerAbility}"); }
            var names = new HashSet<string>();
            foreach (var s in a.states)
            {
                if (string.IsNullOrEmpty(s.name)) { fail("STATE_NO_NAME", "state", "give every state a name"); continue; }
                if (!names.Add(s.name)) fail("STATE_DUPLICATE", s.name, "state names must be unique");
                if (!RuleLanguage.StateTypes.Contains(s.type)) { s.type = "Counter"; fix("STATE_TYPE", s.name, "type → Counter"); }
            }
            for (int i = 0; i < a.rules.Count; i++)
            {
                var rule = a.rules[i];
                string w = $"rule{i}";
                if (!RuleKind.All.Contains(rule.kind)) { fail("RULE_KIND", w, "kind must be " + string.Join("|", RuleKind.All)); continue; }
                if (rule.ops.Count == 0) fail("NO_OPS", w, "a rule needs at least one op");
                if (rule.ops.Count > cfg.maxOpsPerRule) { rule.ops.RemoveRange(cfg.maxOpsPerRule, rule.ops.Count - cfg.maxOpsPerRule); fix("TOO_MANY_OPS", w, $"kept {cfg.maxOpsPerRule}"); }
                if (rule.when.Count > cfg.maxConditionsPerRule) { rule.when.RemoveRange(cfg.maxConditionsPerRule, rule.when.Count - cfg.maxConditionsPerRule); fix("TOO_MANY_CONDITIONS", w, $"kept {cfg.maxConditionsPerRule}"); }
                if (rule.kind == RuleKind.Trigger)
                {
                    if (!RuleLanguage.Is(rule.trigger, "Trigger")) fail("UNKNOWN_TRIGGER", w + ":" + rule.trigger, "use a Trigger primitive");
                }
                else if (!string.IsNullOrEmpty(rule.trigger)) { rule.trigger = null; fix("TRIGGER_IGNORED", w, $"{rule.kind} rules have no trigger"); }
                if (RuleKind.Static(rule.kind) && rule.when.Count > 0) { rule.when.Clear(); fix("CONDITIONS_IGNORED", w, $"{rule.kind} rules are unconditional"); }
                if (rule.kind == RuleKind.Replace && !(rule.replaces is "Dash" or "Potion" or "ComboFinisher")) fail("REPLACE_ACTION", w + ":" + rule.replaces, "replaces must be Dash|Potion|ComboFinisher");
                if (rule.kind == RuleKind.Meta)
                {
                    if (string.IsNullOrEmpty(rule.select)) rule.select = "ByTag";
                    if (!RuleLanguage.AbilitySelectors.Contains(rule.select)) fail("META_SELECTOR", w + ":" + rule.select, string.Join("|", RuleLanguage.AbilitySelectors));
                }
                for (int j = 0; j < rule.ops.Count; j++)
                {
                    var o = rule.ops[j];
                    var p = RuleLanguage.Get(o.op);
                    if (p == null || p.cat != "Op") { fail("UNKNOWN_OP", $"{w}.op{j}:{o.op}", "use an Op primitive"); continue; }
                    if (!RuleLanguage.KindAllows(p, rule.kind)) fail("OP_KIND", $"{w}.op{j}:{o.op}", $"{o.op} is only allowed in {p.kinds} rules (T C R K M S)");
                    if (!string.IsNullOrEmpty(o.rel) && !RuleLanguage.IsRelation(o.rel)) fail("UNKNOWN_RELATION", $"{w}.op{j}:{o.rel}", string.Join("|", RuleLanguage.Relations.Keys));
                    if (!string.IsNullOrEmpty(o.time) && !RuleLanguage.IsTemporal(o.time)) fail("UNKNOWN_TEMPORAL", $"{w}.op{j}:{o.time}", string.Join("|", RuleLanguage.Temporals.Keys));
                }
                foreach (var c in rule.when)
                    if (!RuleLanguage.Is("cond:" + c.type, "Condition")) fail("UNKNOWN_CONDITION", $"{w}:{c.type}", "use a Condition primitive");
            }
            // A Constrain rule alone is pure loss; it must pay for something else in the same ability
            if (a.rules.All(r => r.kind == RuleKind.Constrain)) fail("CONSTRAINT_ONLY", "ability", "a constraint is a price — pair it with a benefit rule");
        }

        // ───────────── 2 Type ─────────────

        /// <summary>The context an op runs in: what its trigger provides (other rule kinds provide nothing).</summary>
        public static Ctx Provides(RuleDef r) => r.kind == RuleKind.Trigger ? RuleLanguage.Get(r.trigger)?.provides ?? Ctx.None : Ctx.None;

        static void Types(AbilityDef a, System.Action<string, string, string> fail, System.Action<string, string, string> fix)
        {
            for (int i = 0; i < a.rules.Count; i++)
            {
                var rule = a.rules[i];
                var ctx = Provides(rule);
                string w = $"rule{i}";
                bool perHit = rule.kind == RuleKind.Continuous && rule.ops.All(o => o.op is "AmplifyDamage" or "Resist");
                foreach (var c in rule.when)
                {
                    bool needsTarget = c.type is "TargetHas" or "TargetLacks" or "TargetIs" or "SameTarget" or "DifferentTarget";
                    if (needsTarget && (ctx & (Ctx.Target | Ctx.Corpse)) == 0 && !perHit)
                        fail("CONDITION_NEEDS_TARGET", $"{w}:{c.type}", $"trigger {rule.trigger ?? rule.kind} provides no target — use Hit/Crit/Kill/Damaged/StatusApplied or remove the condition");
                    if (c.type == "Compare")
                    {
                        var v = RuleLanguage.Value(c.a);
                        if (v == null) continue;     // reference stage reports it
                        if ((v.requires & Ctx.Target) != 0 && (ctx & Ctx.Target) == 0 && !perHit)
                            fail("VALUE_NEEDS_TARGET", $"{w}:{c.a}", "this value needs a live event target");
                        if ((v.requires & Ctx.Amount) != 0 && (ctx & Ctx.Amount) == 0)
                            fail("VALUE_NEEDS_AMOUNT", $"{w}:{c.a}", "event.amount needs Hit/Crit/Kill/Overkill/Damaged/Healed");
                    }
                    if (c.type == "EventTag" && rule.kind == RuleKind.Continuous && !perHit)
                        fail("EVENTTAG_NEEDS_EVENT", $"{w}:{c.text}", "EventTag needs a trigger (or an AmplifyDamage/Resist-only continuous rule)");
                }
                for (int j = 0; j < rule.ops.Count; j++)
                {
                    var o = rule.ops[j];
                    var p = RuleLanguage.Get(o.op);
                    string ow = $"{w}.op{j}:{o.op}";
                    bool needsTarget = p.Needs("target");
                    bool entitySel = o.op is "MoveEntity" or "DetonateEntities" or "ExtendEntities" || o.op == "Swap" && o.target is "OwnClones" or "OwnSummons" or "LastSpawned";
                    if (needsTarget && string.IsNullOrEmpty(o.target))
                    {
                        o.target = entitySel ? "OwnSummons" : (ctx & Ctx.Target) != 0 ? "EventTarget" : "NearestEnemy";
                        fix("TARGET_DEFAULT", ow, "target → " + o.target);
                    }
                    if (!string.IsNullOrEmpty(o.target))
                    {
                        var sel = RuleLanguage.Selector(o.target);
                        if (sel == null) fail("UNKNOWN_SELECTOR", ow + ":" + o.target, "use a Selector primitive");
                        else if ((sel.requires & Ctx.Target) != 0 && (ctx & Ctx.Target) == 0)
                        {
                            if (o.op is "Spread" && (ctx & Ctx.Corpse) != 0) { }
                            else { o.target = "NearestEnemy"; fix("TARGET_UNAVAILABLE", ow, $"{rule.trigger ?? rule.kind} has no live target → NearestEnemy"); }
                        }
                        bool isEntitySel = o.target is "OwnClones" or "OwnSummons" or "LastSpawned";
                        if (sel != null && entitySel && !isEntitySel && o.op != "Swap") fail("TARGET_NOT_ENTITY", ow + ":" + o.target, "this op needs OwnClones|OwnSummons|LastSpawned");
                        if (sel != null && !entitySel && isEntitySel && o.at != "EntityPos") fail("TARGET_NOT_ENEMY", ow + ":" + o.target, "entity selectors only work with entity ops or at=EntityPos");
                        if (sel != null && !sel.enemy && !isEntitySel && needsTarget && o.op is not ("Blink" or "Swap")) fail("TARGET_NOT_ENEMY", ow + ":" + o.target, "this op needs an enemy selector");
                    }
                    if (o.at == "EntityPos" && !(o.target is "OwnClones" or "OwnSummons" or "LastSpawned")) { o.target = "OwnSummons"; fix("ENTITYPOS_TARGET", ow, "at=EntityPos needs an entity selector → OwnSummons"); }
                    if (p.Needs("at") && string.IsNullOrEmpty(o.at)) { o.at = (ctx & (Ctx.Target | Ctx.Corpse | Ctx.Entity)) != 0 ? "EventPos" : "Self"; fix("POSITION_DEFAULT", ow, "at → " + o.at); }
                    if (!string.IsNullOrEmpty(o.at) && RuleLanguage.Position(o.at) == null) fail("UNKNOWN_POSITION", ow + ":" + o.at, "use a Position primitive");
                    if (o.at == "DashPath" && o.op != "Nova") { o.at = "DashOrigin"; fix("DASHPATH_ONLY_NOVA", ow, "only Nova can follow the dash path → DashOrigin"); }
                    if (o.op is "Spread" && (ctx & (Ctx.Target | Ctx.Corpse)) == 0) fail("SPREAD_NEEDS_SOURCE", ow, "Spread copies a status from the event target (Hit/Kill/StatusApplied…)");
                    if (!string.IsNullOrEmpty(o.scale))
                    {
                        var v = RuleLanguage.Value(o.scale);
                        if (v != null && (v.requires & Ctx.Amount) != 0 && (ctx & Ctx.Amount) == 0) fail("SCALE_NEEDS_AMOUNT", ow + ":" + o.scale, "event.amount is not provided by this trigger");
                        if (v != null && (v.requires & Ctx.Target) != 0 && (ctx & Ctx.Target) == 0) fail("SCALE_NEEDS_TARGET", ow + ":" + o.scale, "target value without a live target");
                    }
                    // Relation / temporal type constraints (the Relation DB: "Orbit: source MovableEntity", "Bounce: source Projectile"…)
                    if (!string.IsNullOrEmpty(o.rel) && RuleLanguage.IsRelation(o.rel) && !p.Accepts(o.rel))
                    {
                        if (o.rel == "Copy" && o.op == "Spawn" && o.entity != "Clone") { o.rel = null; fix("RELATION_TYPE", ow, "Copy needs a Clone"); }
                        else fail("RELATION_TYPE", ow + ":" + o.rel, $"{o.op} accepts relations: {(string.IsNullOrEmpty(p.relations) ? "none" : p.relations)}");
                    }
                    if (o.op == "Spawn" && !string.IsNullOrEmpty(o.rel))
                    {
                        string need = o.rel switch { "Copy" => "Copy", "Orbit" => "Orbit", "Follow" => "Follow", "Attach" => "Attach", "Inherit" => "Inherit", _ => null };
                        var caps = RuleLanguage.Capabilities.TryGetValue(o.entity ?? "", out var cc) ? cc : "";
                        if (need != null && !caps.Split(' ').Contains(need) && !(need == "Follow" && o.entity == "Orb")) fail("CAPABILITY", ow + $":{o.entity}.{o.rel}", $"{o.entity} capabilities: {caps}");
                        if (o.rel == "Attach" && (ctx & Ctx.Target) == 0) fail("ATTACH_NEEDS_TARGET", ow, "Attach needs a trigger with a live target");
                    }
                    if (!string.IsNullOrEmpty(o.time) && RuleLanguage.IsTemporal(o.time) && o.time != "Immediate" && !p.AcceptsTime(o.time))
                        fail("TEMPORAL_TYPE", ow + ":" + o.time, $"{o.op} accepts: {p.temporals}");
                    if (o.time is "UntilHit" or "UntilNextAttack" or "ForNextN" && p.Needs("target") && o.target != "EventTarget")
                    { o.target = "EventTarget"; fix("DEFERRED_TARGET", ow, "deferred effects land on the enemy of the moment they fire"); }
                }
            }
        }

        // ───────────── 3 Reference ─────────────

        static void References(AbilityDef a, BuildContext b, System.Action<string, string, string> fail, System.Action<string, string, string> fix)
        {
            bool StateOk(string name, string w, params string[] types)
            {
                var s = a.State(name);
                if (s == null) { fail("UNKNOWN_STATE", w + ":" + name, "declare the state in states[]"); return false; }
                if (types.Length > 0 && !types.Contains(s.type)) { fail("STATE_TYPE_MISMATCH", w + ":" + name, $"needs a {string.Join("/", types)} state"); return false; }
                return true;
            }
            void ValueOk(string reference, string w)
            {
                var v = RuleLanguage.Value(reference);
                if (v == null) { fail("UNKNOWN_VALUE", w + ":" + reference, "use a Value reference"); return; }
                if (reference.StartsWith("state:")) StateOk(reference.Substring(6), w);
                if (reference.StartsWith("build.tag:") && !RuleLanguage.KnownTag(reference.Substring(10))) fail("UNKNOWN_TAG", w + ":" + reference, "use a known tag");
                if (reference.StartsWith("build.element:") && !RuleLanguage.IsElement(reference.Substring(14))) fail("UNKNOWN_ELEMENT", w + ":" + reference, string.Join("|", RuleLanguage.Elements));
                if (reference.StartsWith("build.tier:") && !int.TryParse(reference.Substring(11), out _)) fail("UNKNOWN_VALUE", w + ":" + reference, "build.tier:<0..4>");
            }
            const string NUMERIC = "Counter StoredValue Charge Flag Timer";
            string[] numeric = NUMERIC.Split(' ');

            var written = new HashSet<string>();
            var read = new HashSet<string>();
            for (int i = 0; i < a.rules.Count; i++)
            {
                var rule = a.rules[i];
                string w = $"rule{i}";
                if (rule.trigger is "StateFull" or "StateEmpty") { if (StateOk(rule.param, w, numeric)) read.Add(rule.param); }
                if (rule.trigger is "StatusApplied" or "StatusExpired" && !string.IsNullOrEmpty(rule.param) && !RuleLanguage.Statuses.Contains(rule.param))
                    fail("UNKNOWN_STATUS", w + ":" + rule.param, string.Join("|", RuleLanguage.Statuses));
                if (rule.trigger == "EntityExpired" && !string.IsNullOrEmpty(rule.param) && !RuleLanguage.Entities.Contains(rule.param))
                    fail("UNKNOWN_ENTITY", w + ":" + rule.param, string.Join("|", RuleLanguage.Entities));
                if (rule.trigger == "Pickup" && !string.IsNullOrEmpty(rule.param) && !(rule.param is "gold" or "xp" or "heart" or "shard"))
                    fail("UNKNOWN_PICKUP", w + ":" + rule.param, "gold|xp|heart|shard");
                if (rule.trigger == "Every" && rule.period <= 0) { rule.period = 4; fix("PERIOD_DEFAULT", w, "period → 4s"); }
                if (rule.kind == RuleKind.Meta && rule.select is "ByTag" or "ByElement" or "ByTrigger")
                {
                    string arg = !string.IsNullOrEmpty(rule.selectArg) ? rule.selectArg : rule.ops.FirstOrDefault()?.tag;
                    if (rule.select == "ByTag" && !RuleLanguage.KnownTag(arg)) fail("UNKNOWN_TAG", w + ":" + arg, "selectArg (or op.tag) must be a known tag");
                    if (rule.select == "ByElement" && !RuleLanguage.IsElement(arg)) fail("UNKNOWN_ELEMENT", w + ":" + arg, string.Join("|", RuleLanguage.Elements));
                    if (rule.select == "ByTrigger" && !RuleLanguage.Is(arg, "Trigger")) fail("UNKNOWN_TRIGGER", w + ":" + arg, "selectArg must be a Trigger");
                    if (string.IsNullOrEmpty(rule.selectArg)) rule.selectArg = arg;
                }
                foreach (var c in rule.when)
                {
                    if (c.type == "Compare")
                    {
                        ValueOk(c.a, w);
                        if (!Cmps.Contains(c.cmp)) fail("BAD_COMPARATOR", w + ":" + c.cmp, ">= <= > < ==");
                        if (c.a != null && c.a.StartsWith("state:")) read.Add(c.a.Substring(6));
                    }
                    if (c.type is "TargetHas" or "TargetLacks" && !RuleLanguage.Statuses.Contains(c.text)) fail("UNKNOWN_STATUS", w + ":" + c.text, string.Join("|", RuleLanguage.Statuses));
                    if (c.type == "TargetIs" && !TargetKinds.Contains(c.text)) fail("UNKNOWN_TARGET_KIND", w + ":" + c.text, string.Join("|", TargetKinds));
                    if (c.type == "EventTag" && !RuleLanguage.KnownTag(c.text)) fail("UNKNOWN_TAG", w + ":" + c.text, "use a known tag");
                    if (c.type is "HasEntity" or "NoEntity" && !string.IsNullOrEmpty(c.text) && !RuleLanguage.Entities.Contains(c.text)) fail("UNKNOWN_ENTITY", w + ":" + c.text, string.Join("|", RuleLanguage.Entities));
                    if (c.type == "WithinTime") { if (!RuleLanguage.Is(c.text, "Trigger")) fail("UNKNOWN_TRIGGER", w + ":" + c.text, "WithinTime.text must be a Trigger"); c.value = Mathf.Clamp(c.value <= 0 ? 2 : c.value, 0.3f, 10); }
                    if (c.type == "DominantElement" && !RuleLanguage.IsElement(c.text)) fail("UNKNOWN_ELEMENT", w + ":" + c.text, string.Join("|", RuleLanguage.Elements));
                }
                for (int j = 0; j < rule.ops.Count; j++)
                {
                    var o = rule.ops[j];
                    var p = RuleLanguage.Get(o.op);
                    string ow = $"{w}.op{j}:{o.op}";
                    if (!string.IsNullOrEmpty(o.scale)) { ValueOk(o.scale, ow); if (o.scale.StartsWith("state:")) read.Add(o.scale.Substring(6)); }
                    if (p.Needs("status") && !RuleLanguage.Statuses.Contains(o.status))
                    {
                        string s = RuleLanguage.StatusOf(o.element);
                        if (s != null) { o.status = s; fix("STATUS_FROM_ELEMENT", ow, "status → " + s); }
                        else fail("UNKNOWN_STATUS", ow + ":" + o.status, string.Join("|", RuleLanguage.Statuses));
                    }
                    if (o.op is "Aura" or "StatusPotency" or "StatusedEnemies" && !string.IsNullOrEmpty(o.status) && !RuleLanguage.Statuses.Contains(o.status)) fail("UNKNOWN_STATUS", ow + ":" + o.status, string.Join("|", RuleLanguage.Statuses));
                    if (p.Needs("entity") && !RuleLanguage.Entities.Contains(o.entity)) fail("UNKNOWN_ENTITY", ow + ":" + o.entity, string.Join("|", RuleLanguage.Entities));
                    if (o.op == "Buff" && !RuleLanguage.BuffStats.Contains(o.stat)) { o.stat = "Attack"; fix("BUFF_STAT", ow, "stat → Attack"); }
                    if (o.op is "ModifyStat" or "CapStat" && !RuleLanguage.StatInfo.ContainsKey(o.stat ?? "")) fail("UNKNOWN_STAT", ow + ":" + o.stat, string.Join("|", RuleLanguage.StatInfo.Keys));
                    if (o.op == "ModifyStat" && !string.IsNullOrEmpty(o.mode) && !RuleLanguage.StatModes.Contains(o.mode)) { o.mode = "Add"; fix("STAT_MODE", ow, "mode → Add"); }
                    if (p.Needs("state"))
                    {
                        string[] types = o.op switch { "StorePosition" => new[] { "Position" }, "StoreTarget" => new[] { "Target" }, _ => numeric };
                        if (StateOk(o.state, ow, types)) written.Add(o.state);
                    }
                    if (o.at == "Stored" && !a.states.Any(s => s.type == "Position")) { o.at = "EventPos"; o.state = o.op is "StorePosition" ? o.state : null; fix("STORED_NO_STATE", ow, "no Position state → at EventPos"); }
                    if (o.at == "Stored")
                    {
                        if (string.IsNullOrEmpty(o.state) || o.op == "StorePosition") { var ps = a.states.FirstOrDefault(s => s.type == "Position"); if (ps != null && o.op != "StorePosition") { o.state = ps.name; fix("STORED_STATE", ow, "state → " + ps.name); } }
                        if (o.op != "StorePosition" && StateOk(o.state, ow, "Position")) read.Add(o.state);
                    }
                    if (o.target == "StoredTarget")
                    {
                        if (string.IsNullOrEmpty(o.state)) { var ts = a.states.FirstOrDefault(s => s.type == "Target"); if (ts != null) { o.state = ts.name; fix("STORED_STATE", ow, "state → " + ts.name); } }
                        if (o.op != "StoreTarget" && StateOk(o.state, ow, "Target")) read.Add(o.state);
                    }
                    if (p.Needs("tag") && !RuleLanguage.KnownTag(o.tag)) fail("UNKNOWN_TAG", ow + ":" + o.tag, "use a known tag");
                    if (p.Needs("element") && !RuleLanguage.IsElement(o.element)) fail("UNKNOWN_ELEMENT", ow + ":" + o.element, string.Join("|", RuleLanguage.Elements));
                    if (!string.IsNullOrEmpty(o.element) && !RuleLanguage.IsElement(o.element)) { o.element = null; fix("ELEMENT_REMOVED", ow, "unknown element dropped"); }
                    if (o.op == "Convert" && !RuleLanguage.ConvertPairs.ContainsKey(o.from + ">" + o.to)) fail("CONVERT_PAIR", ow + $":{o.from}>{o.to}", "allowed: " + string.Join(", ", RuleLanguage.ConvertPairs.Keys));
                    if (o.op == "RetriggerTagged" && !RuleLanguage.RetriggerPairs.Contains(o.from + ">" + o.to)) fail("RETRIGGER_PAIR", ow + $":{o.from}>{o.to}", "allowed: " + string.Join(", ", RuleLanguage.RetriggerPairs));
                    if (o.op == "InfuseTagged" && rule.selectArg == RuleLanguage.ParseElement(o.element).ToString().ToUpper()) fail("INFUSE_SELF", ow, "infusing an element into itself does nothing");
                    if (o.op == "Forbid" && !(o.mode is "Dash" or "Potion" or "Regen" or "Skill")) fail("FORBID_ACTION", ow + ":" + o.mode, "Dash|Potion|Regen|Skill");
                    if (o.op == "ReplaceWith" && !(o.mode is "Blink" or "Nova" or "Shield" or "Spawn" or "Projectile")) fail("REPLACE_WITH", ow + ":" + o.mode, "Blink|Nova|Shield|Spawn|Projectile");
                    if (o.op == "ReplaceWith" && o.mode == "Spawn" && !RuleLanguage.Entities.Contains(o.entity)) fail("UNKNOWN_ENTITY", ow + ":" + o.entity, string.Join("|", RuleLanguage.Entities));
                    if (o.op == "DropPickup" && !(o.mode is "heart" or "gold" or "xp")) { o.mode = "heart"; fix("PICKUP_KIND", ow, "mode → heart"); }
                }
            }
            // Store/Release validity: a state that is read must be written somewhere (ResourceConservation / StoredValue rules)
            foreach (var s in read) if (!written.Contains(s) && a.State(s)?.type != "Charge") fail("STATE_NEVER_WRITTEN", s, "add an AddState/SetState/StorePosition/StoreTarget that fills this state");
            foreach (var s in a.states) if (!written.Contains(s.name) && !read.Contains(s.name)) fail("STATE_UNUSED", s.name, "remove the state or use it");
            foreach (var s in a.states.Where(s => s.type == "Charge" && s.regen <= 0 && !written.Contains(s.name))) fail("CHARGE_NEVER_FILLS", s.name, "a Charge needs regen > 0 or an AddState");
        }

        // ───────────── 4 Capability ─────────────

        static void Capability(AbilityDef a, System.Action<string, string, string> fail, System.Action<string, string, string> fix)
        {
            foreach (var o in a.rules.SelectMany(r => r.ops))
            {
                if (o.op != "Spawn") continue;
                // legacy "mode: Copy" → relation Copy
                if (o.mode == "Copy") { o.rel ??= "Copy"; o.mode = null; }
                if (o.mode == "Equip") fail("GRIP", o.entity, "World rule Grip: clones have 0 grip — use PhantomWeapon or relation Copy");
                if (!string.IsNullOrEmpty(o.mode)) { o.mode = null; fix("SPAWN_MODE", o.entity, "mode dropped"); }
            }
            foreach (var o in a.rules.SelectMany(r => r.ops).Where(o => o.op == "Projectile"))
                if (!string.IsNullOrEmpty(o.mode) && o.mode is not ("Seek" or "Ring" or "Fan")) { o.mode = "Fan"; fix("PROJECTILE_MODE", "Projectile", "mode → Fan"); }
        }

        // ───────────── 5 Bounds ─────────────

        static void Bounds(AbilityDef a, AbilityRulesDef cfg, System.Action<string, string, string> fail, System.Action<string, string, string> fix)
        {
            foreach (var s in a.states)
            {
                float max = s.type switch { "Flag" => 1, "Position" => 1, "Target" => 1, "Timer" => Mathf.Clamp(s.max <= 0 ? 5 : s.max, 1, 30), _ => Mathf.Clamp(s.max <= 0 ? 3 : s.max, 1, cfg.maxStateValue) };
                if (!Mathf.Approximately(max, s.max)) { s.max = max; fix("STATE_BOUND", s.name, "max → " + max); }
                s.decay = Mathf.Clamp(s.decay, 0, Mathf.Max(1, s.max));
                s.regen = s.type == "Charge" ? Mathf.Clamp(s.regen, 0, s.max) : 0;
            }
            for (int i = 0; i < a.rules.Count; i++)
            {
                var rule = a.rules[i];
                string w = $"rule{i}";
                rule.cooldown = Mathf.Clamp(rule.cooldown, 0, 30);
                if (rule.trigger == "Every") rule.period = Mathf.Clamp(rule.period, 1, 30);
                foreach (var c in rule.when)
                {
                    if (c.type == "Chance") c.value = Mathf.Clamp(c.value <= 0 ? 0.25f : c.value, 0.05f, 0.9f);
                    if (c.type == "EveryNth") c.value = Mathf.Clamp(Mathf.Round(c.value <= 0 ? 3 : c.value), 2, 10);
                    if (c.type == "Compare" && c.a != null)
                    {
                        if (c.a.EndsWith("Pct")) c.value = Mathf.Clamp01(c.value);
                        if (c.a.StartsWith("state:")) { var s = a.State(c.a.Substring(6)); if (s != null && c.value > s.max) { fail("CONDITION_UNREACHABLE", w + ":" + c.a, $"state max is {s.max}"); } }
                    }
                }
                foreach (var o in rule.ops)
                {
                    var p = RuleLanguage.Get(o.op);
                    o.delay = Mathf.Clamp(o.delay, 0, 2);
                    o.radius = Mathf.Clamp(o.radius <= 0 ? p.defRadius : o.radius, 0, o.op is "Beam" ? 9 : 5);
                    if (p.Needs("status") || o.op is "Shield" or "Buff" or "Spawn" or "ApplyStatus") o.duration = Mathf.Clamp(o.duration <= 0 ? p.defDuration : o.duration, 0.5f, o.op == "Spawn" ? cfg.maxSpawnDuration : 10);
                    if (o.time is "Periodic" or "UntilHit" or "UntilDamaged" or "UntilNextAttack" or "ForNextN") o.duration = Mathf.Clamp(o.duration <= 0 ? 4 : o.duration, 1, o.time == "Periodic" ? 8 : cfg.maxArmedSeconds);
                    if (o.time is "Periodic" or "ForNextN" || o.rel is "Chain" or "Bounce" or "Pierce" or "Split" or "Spread" or "Link" or "Cascade")
                        o.times = Mathf.Clamp(o.times <= 0 ? (o.time == "Periodic" ? 3 : 2) : o.times, 1, o.rel == "Cascade" ? cfg.maxCascade : cfg.maxTemporalTicks);
                    o.count = Mathf.Clamp(o.count <= 0 ? Mathf.RoundToInt(p.defCount) : o.count, 1, o.op == "Projectile" ? 8 : o.op == "Spawn" ? 3 : 6);
                    if (o.op == "Spawn" && o.entity is "Clone" or "PhantomWeapon" && o.count > 2) o.count = 2;
                    if (o.op is "Nova" or "Push" or "Pull" or "Taunt" && o.radius < 1) o.radius = Mathf.Max(1, p.defRadius);
                    if (o.target is "EnemiesNear" or "EnemiesInCone" or "EnemiesInLine") o.radius = Mathf.Clamp(o.radius <= 0 ? 3 : o.radius, 1.5f, o.target == "EnemiesInLine" ? 8 : 5);
                    if (o.op == "AddState")
                    {
                        var s = a.State(o.state);
                        if (s != null && s.type == "Flag") o.amount = o.amount >= 0 ? 1 : -1;
                        if (o.amount == 0 && string.IsNullOrEmpty(o.scale)) o.amount = 1;
                    }
                    if (o.op == "SetState") { var s = a.State(o.state); if (s != null) o.amount = Mathf.Clamp(s.type == "Timer" && o.amount <= 0 ? s.max : o.amount, 0, s.max); }
                    if (o.op == "OfferCount") o.amount = o.amount > 0 ? 1 : -1;
                    if (o.op == "TierBias") o.amount = 1;
                    if (o.op == "ExtraEvolution") o.count = 1;
                    if (o.op == "DelayedReward") o.times = Mathf.Clamp(o.times <= 0 ? 40 : o.times, 20, 150);
                    if (o.op == "ModifyStat" && RuleLanguage.StatInfo.TryGetValue(o.stat ?? "", out var si))
                    {
                        if (string.IsNullOrEmpty(o.scale)) { if (o.per != 0) o.per = 0; }
                        // trade-off rules: a negative part must stay within the stat's floor
                        if (o.amount < si.min) { o.amount = si.min; fix("STAT_FLOOR", o.stat, "clamped to " + si.min); }
                        if (o.mode is "Min" or "Max" or "Override" && !string.IsNullOrEmpty(o.scale)) { o.scale = null; o.per = 0; fix("STAT_MODE_SCALE", o.stat, $"mode {o.mode} takes a fixed value"); }
                    }
                    if (o.op == "CapStat" && RuleLanguage.StatInfo.TryGetValue(o.stat ?? "", out var cs)) o.amount = Mathf.Clamp(o.amount, cs.min, 0);
                    if (string.IsNullOrEmpty(o.scale)) o.per = 0;
                    if (o.op == "Convert" && RuleLanguage.ConvertPairs.TryGetValue(o.from + ">" + o.to, out var cp)) o.amount = Mathf.Clamp(o.amount <= 0 ? cp.def : o.amount, cp.min, cp.max);
                }
            }
            // System invariants across the ability
            if (a.rules.SelectMany(r => r.ops).Count(o => o.op == "OfferCount") > 1) fail("OFFER_COUNT_TWICE", "ability", "at most one OfferCount");
            if (a.rules.Count(r => r.kind == RuleKind.Replace) > 1) fail("REPLACE_TWICE", "ability", "at most one Replace rule per ability");
        }

        // ───────────── Tags / tier (engine-derived) ─────────────

        public static void Tag(AbilityDef a)
        {
            var set = new HashSet<string>();
            foreach (var r in a.rules)
            {
                var t = RuleLanguage.Get(r.trigger);
                if (t != null) foreach (var x in t.tags) set.Add(x);
                foreach (var c in r.when.Where(c => c.type == "EventTag")) set.Add(c.text);
                foreach (var o in r.ops)
                {
                    var p = RuleLanguage.Get(o.op);
                    if (p == null) continue;
                    if (r.kind != RuleKind.Meta) foreach (var x in p.tags) set.Add(x);
                    if (RuleLanguage.IsElement(o.element)) set.Add(o.element.ToUpper());
                    if (!string.IsNullOrEmpty(o.status)) set.Add(o.status.ToUpper());
                    if (!string.IsNullOrEmpty(o.entity)) set.Add(o.entity.ToUpper());
                    if (o.op == "ModifyStat" || o.op == "Buff") set.Add("STAT");
                    if (o.op == "ApplyStatus" && o.status != null && RuleLanguage.Statuses.Contains(o.status))
                        set.Add(o.status switch { "burn" => "FIRE", "chill" => "FROST", "poison" => "POISON", "bleed" => "BLOOD", "weaken" => "VOID", _ => "SHADOW" });
                    if (r.kind == RuleKind.Replace && o.mode == "Blink") set.Add("BLINK");
                }
                if (r.kind == RuleKind.Replace && r.replaces == "Dash") set.Add("DODGE");
                if (r.kind == RuleKind.Meta && r.select == "ByTag" && !string.IsNullOrEmpty(r.selectArg)) set.Add(r.selectArg);
                if (r.kind == RuleKind.System) set.Add("SYSTEM");
            }
            if (a.states.Count > 0) set.Add("STATE");
            a.tags = set.Where(s => !string.IsNullOrEmpty(s)).ToList();
            a.tier = ComputeTier(a);
        }

        public static int ComputeTier(AbilityDef a)
        {
            int t = 0;
            foreach (var r in a.rules)
            {
                if (r.kind == RuleKind.System) t = Mathf.Max(t, 4);
                else if (r.kind == RuleKind.Meta) t = Mathf.Max(t, r.ops.Max(o => RuleLanguage.Get(o.op)?.tier ?? 3));
                else if (r.kind is RuleKind.Replace) t = Mathf.Max(t, 3);
                else if (r.kind is RuleKind.Constrain) t = Mathf.Max(t, 2);
                else if (r.kind == RuleKind.Trigger) t = Mathf.Max(t, 1);
                foreach (var o in r.ops)
                {
                    t = Mathf.Max(t, Mathf.Min(RuleLanguage.Get(o.op)?.tier ?? 1, r.kind == RuleKind.Meta ? 4 : 3));
                    if (RuleLanguage.Relations.TryGetValue(o.rel ?? "", out var rel)) t = Mathf.Max(t, rel.tier);
                    if (RuleLanguage.Temporals.TryGetValue(o.time ?? "", out var tm)) t = Mathf.Max(t, tm.tier);
                    if (o.op == "Convert" && RuleLanguage.ConvertPairs.TryGetValue(o.from + ">" + o.to, out var cp)) t = Mathf.Max(t, cp.tier);
                    if (o.op == "ModifyStat" && o.mode == "Override") t = Mathf.Max(t, 4);
                }
                if (r.when.Any(c => c.type == "Compare" && c.a != null && (c.a.StartsWith("state:") || c.a.StartsWith("build.")))) t = Mathf.Max(t, 2);
                if (r.ops.Any(o => !string.IsNullOrEmpty(o.scale))) t = Mathf.Max(t, 2);
            }
            if (a.states.Count > 0) t = Mathf.Max(t, 2);
            if (a.rules.Count >= 3) t = Mathf.Max(t, 2);
            return t;
        }

        // ───────────── 6 Cycle ─────────────

        /// <summary>
        /// Event graph over the whole build (+ candidate): trigger --ops--> events it can raise. A cycle is a feedback loop.
        /// Loops are allowed if they converge: gain per loop (frequency-weighted chance) below cfg.maxCycleGain, or broken by a
        /// cooldown. Otherwise the repairer adds an internal cooldown to the candidate's rule on the loop.
        /// </summary>
        static void Cycles(AbilityDef a, BuildContext b, System.Action<string, string, string> fail, System.Action<string, string, string> fix)
        {
            var all = new List<AbilityDef>(b.owned) { a };
            var edges = new List<(string from, string to, AbilityDef ab, RuleDef r, float gain)>();
            foreach (var ab in all)
                foreach (var r in ab.rules.Where(r => r.kind == RuleKind.Trigger))
                {
                    float gain = 1;
                    foreach (var c in r.when)
                        gain *= c.type switch { "Chance" => c.value, "EveryNth" => 1f / Mathf.Max(2, c.value), "TargetHas" => 0.5f, "Compare" => 0.6f, "TargetIs" => 0.3f, _ => 1 };
                    if (r.cooldown >= 0.5f) gain *= 0.3f;
                    float repeat = 0;
                    foreach (var src in all)
                        foreach (var mr in src.rules.Where(x => x.kind == RuleKind.Meta))
                            foreach (var mo in mr.ops.Where(x => x.op == "RepeatTagged"))
                                if (AbilityRuntime.SelectAbilities(mr, mo, all, src, b.cfg.maxMetaTargets).Contains(ab)) repeat += mo.amount;
                    gain *= 1 + repeat;
                    foreach (var o in r.ops)
                    {
                        var p = RuleLanguage.Get(o.op);
                        if (p == null) continue;
                        // expected targets per execution: how many follow-up events one proc produces
                        float g = gain * o.op switch
                        {
                            "Nova" => Mathf.Clamp(1 + 0.35f * o.radius * o.radius, 1, 5) * 0.8f,
                            "Projectile" => Mathf.Max(1, o.count) * 0.6f,
                            "Chain" => Mathf.Max(1, o.count),
                            "Spawn" => 2f,
                            "Spread" => Mathf.Max(1, o.count),
                            _ => 1f,
                        };
                        if (RuleLanguage.Relations.TryGetValue(o.rel ?? "", out var rel)) g *= 1 + rel.powerMul;
                        if (o.time == "Periodic") g *= Mathf.Max(2, o.times);
                        foreach (var ev in p.causes.Split(' ').Where(x => x.Length > 0))
                            edges.Add((r.trigger, ev, ab, r, g * (ev is "Kill" ? 0.3f : ev is "Crit" ? 0.2f : 1)));
                        if (o.op is "AddState" && o.state != null) edges.Add((r.trigger, "StateFull", ab, r, g));
                    }
                }
            foreach (var cyc in FindCycles(edges.Select(e => (e.from, e.to)).Distinct().ToList()))
            {
                var onCycle = edges.Where(e => cyc.Contains(e.from) && cyc.Contains(e.to)).ToList();
                float gain = 1;
                foreach (var node in cyc) { var outs = onCycle.Where(e => e.from == node).ToList(); if (outs.Count > 0) gain *= outs.Max(e => e.gain); }
                if (!onCycle.Any(e => e.ab == a)) continue;   // existing build already accepted
                // An ability never re-triggers itself at runtime (ABILITY:id tag), so only loops through 2+ abilities can run away
                if (onCycle.Select(e => e.ab).Distinct().Count() < 2) continue;
                if (gain < b.cfg.maxCycleGain) continue;
                var mine = onCycle.Where(e => e.ab == a).Select(e => e.r).Distinct().ToList();
                foreach (var r in mine) r.cooldown = Mathf.Max(r.cooldown, b.cfg.cycleRepairCooldown);
                fix("FEEDBACK_LOOP", string.Join("→", cyc) + "→" + cyc[0], $"gain {gain:0.##} ≥ {b.cfg.maxCycleGain} → internal cooldown {b.cfg.cycleRepairCooldown}s");
            }
        }

        static List<List<string>> FindCycles(List<(string from, string to)> edges)
        {
            var adj = edges.GroupBy(e => e.from).ToDictionary(g => g.Key, g => g.Select(e => e.to).Distinct().ToList());
            var cycles = new List<List<string>>();
            var seen = new HashSet<string>();
            foreach (var start in adj.Keys)
            {
                var stack = new List<string>();
                void Dfs(string n, int depth)
                {
                    if (depth > 6 || cycles.Count > 20) return;
                    stack.Add(n);
                    if (adj.TryGetValue(n, out var next))
                        foreach (var m in next)
                        {
                            if (m == start)
                            {
                                var key = string.Join(">", stack.OrderBy(x => x));
                                if (seen.Add(key)) cycles.Add(new List<string>(stack));
                            }
                            else if (!stack.Contains(m) && string.CompareOrdinal(m, start) > 0) Dfs(m, depth + 1);
                        }
                    stack.RemoveAt(stack.Count - 1);
                }
                Dfs(start, 0);
            }
            return cycles;
        }

        // ───────────── 7 Duplicate / 8 Tier ─────────────

        static void Duplicate(AbilityDef a, BuildContext b, System.Action<string, string, string> fail)
        {
            string sig = a.Signature();
            foreach (var o in b.owned) if (o.Signature() == sig) fail("DUPLICATE", o.name ?? o.mechanic, "already owned — change the structure");
            // System abilities stack only once each
            foreach (var op in a.rules.Where(r => r.kind == RuleKind.System).SelectMany(r => r.ops))
                if (b.owned.Any(o => o.rules.Where(r => r.kind == RuleKind.System).SelectMany(r => r.ops).Any(x => x.op == op.op && (op.op != "TagBias" || x.tag == op.tag))))
                    fail("SYSTEM_STACK", op.op, "this system rule is already owned");
            // One replacement per action across the build
            foreach (var rr in a.rules.Where(r => r.kind == RuleKind.Replace))
                if (b.owned.Any(o => o.rules.Any(x => x.kind == RuleKind.Replace && x.replaces == rr.replaces)))
                    fail("REPLACE_CONFLICT", rr.replaces, "this action is already replaced by an owned ability");
        }

        static void Tier(AbilityDef a, BuildContext b, System.Action<string, string, string> fail)
        {
            if (a.tier > b.MaxTier) fail("TIER_LOCKED", RuleText.TierLabel(a.tier), $"complexity tier {a.tier} unlocks later (max {b.MaxTier} at level {b.level})");
        }

        // ───────────── 9 Power ─────────────

        static void Power(AbilityDef a, BuildContext b, Result res, bool rebalance, System.Action<string, string, string> fail, System.Action<string, string, string> fix)
        {
            var cfg = b.cfg;
            float budget = b.Budget(a.tier);
            res.budget = budget;
            var before = PowerModel.Evaluate(b.owned, b);

            // Trade-offs: negative parts are the price — the balancer scales only the positive parts
            float Delta() => PowerModel.Delta(a, b, before);
            float d = Delta();
            if (rebalance)
            {
                // Each scalable number starts at its primitive default (the AI's numbers are ignored), then all scale together
                foreach (var r in a.rules) foreach (var o in r.ops) Defaults(a, r, o);
                d = Delta();
                for (int it = 0; it < 6 && Mathf.Abs(d - budget) > budget * 0.08f; it++)
                {
                    float k = d <= 0.01f ? 2.5f : Mathf.Clamp(budget / d, 0.25f, 4f);
                    if (!Scale(a, k)) break;
                    d = Delta();
                }
                // Magnitudes at their primitive caps but still short (or over): turn the frequency levers instead —
                // cooldowns, chances, counts and periods are numbers the balancer owns too (the AI only chose that they exist)
                for (int it = 0; it < 6 && (d < budget * 0.92f || d > budget * 1.08f); it++)
                {
                    float k = d <= 0.01f ? 2f : Mathf.Clamp(budget / d, 0.5f, 2f);
                    if (!Frequency(a, k)) break;
                    d = Delta();
                }
            }
            res.delta = d;
            res.vector = PowerModel.Evaluate(new List<AbilityDef>(b.owned) { a }, b).Vector();
            a.power = d;
            // Pure system abilities (choices, rarity, rerolls) are valued by the player, not by combat power: only the ceiling applies
            bool systemOnly = a.rules.All(r => r.kind is RuleKind.System or RuleKind.Constrain);
            if (d < budget * cfg.acceptMin && !systemOnly)
            {
                // A dead ability is a validator rejection, not a balance one: no state, target or trigger ever makes it fire
                if (d <= 0.5f) fail("NO_EFFECT", "ability", "in the current build this ability does nothing (conditions never hold / meta matches nothing)");
                else fail("TOO_WEAK", $"Δ{d:0}", $"budget {budget:0}: raise trigger frequency, remove a restrictive condition or add a payoff");
            }
            if (d > budget * cfg.acceptMax) fail("TOO_STRONG", $"Δ{d:0}", $"budget {budget:0}: add a condition/cooldown, a trade-off, or narrow the meta selector");
        }

        static void Defaults(AbilityDef a, RuleDef r, OpDef o)
        {
            var p = RuleLanguage.Get(o.op);
            if (p == null) return;
            if (r.kind == RuleKind.Replace) { o.amount = p.def; return; }
            // Conversions are sized by their pair (whitelist table), never by the generic primitive default
            if (o.op == "Convert") { if (RuleLanguage.ConvertPairs.TryGetValue(o.from + ">" + o.to, out var cp)) o.amount = cp.def; return; }
            switch (p.scalable)
            {
                case "amount":
                    if (o.op == "ModifyStat")
                    {
                        if (!RuleLanguage.StatInfo.TryGetValue(o.stat ?? "", out var si)) return;
                        if (o.mode is "Min" or "Max" or "Override") return;      // authored values: floors/caps/overrides are structure
                        // keep the sign the author chose (trade-offs), default magnitude by stat
                        float mag = si.max * 0.25f;
                        if (!string.IsNullOrEmpty(o.scale)) { o.per = Mathf.Sign(o.per == 0 ? 1 : o.per) * mag * 0.25f; o.amount = o.amount < 0 ? -mag : 0; }
                        else o.amount = o.amount < 0 ? Mathf.Max(si.min * 0.5f, -mag) : mag;
                        return;
                    }
                    if (!string.IsNullOrEmpty(o.scale))
                    {
                        float e = Mathf.Max(0.05f, ExpectedScale(a, r, o.scale));
                        o.per = p.def / e * 0.7f;
                        o.amount = p.def * 0.3f;
                        if (o.op == "AddState") { o.amount = 0; o.per = 1; }
                    }
                    else if (o.op != "AddState") o.amount = p.def;
                    break;
                case "count": o.count = Mathf.RoundToInt(p.def); break;
                case "duration": o.amount = p.def; break;
            }
        }

        /// <summary>Typical value of a state when it is read: what one fill brings in (event.amount fills are small fractions of max HP).</summary>
        static float StateTypical(AbilityDef a, string state)
        {
            var s = a.State(state);
            if (s == null) return 1;
            bool fractional = a.rules.Any(r => r.ops.Any(o => o.op == "AddState" && o.state == state && o.scale == "event.amount" && r.trigger is "Damaged" or "Healed"));
            if (fractional) return Mathf.Min(s.max, 0.25f);
            // decaying stacks settle where inflow = decay; the fill rate is the trigger's generic frequency
            if (s.decay > 0)
            {
                float inflow = a.rules.Where(r => r.kind == RuleKind.Trigger && r.ops.Any(o => o.op == "AddState" && o.state == state))
                    .Sum(r => (RuleLanguage.Get(r.trigger)?.freq ?? 0.2f) * r.ops.Where(o => o.op == "AddState" && o.state == state).Sum(o => Mathf.Max(0.5f, o.amount)));
                return Mathf.Clamp(inflow / s.decay, 0.5f, s.max);
            }
            return Mathf.Max(1, s.max * 0.5f);
        }

        static float ExpectedScale(AbilityDef a, RuleDef r, string reference)
        {
            var v = RuleLanguage.Value(reference);
            if (reference.StartsWith("state:")) return StateTypical(a, reference.Substring(6));
            if (reference.StartsWith("build.")) return 2;
            if (reference == "event.amount") return r.trigger == "Damaged" || r.trigger == "Healed" ? 0.08f : 1;
            return v?.expected > 0 ? v.expected : 1;
        }

        /// <summary>Scale every positive scalable number by k (within primitive bounds). Returns false if nothing could move.</summary>
        /// <summary>Frequency levers: cooldown ÷k, Chance ×k, EveryNth ÷k, Every period ÷k, state thresholds ÷k (within bounds).</summary>
        static bool Frequency(AbilityDef a, float k)
        {
            bool moved = false;
            foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger))
            {
                if (r.cooldown > 0.2f) { float n = Mathf.Clamp(r.cooldown / k, 0.3f, 30); moved |= !Mathf.Approximately(n, r.cooldown); r.cooldown = Mathf.Round(n * 10) / 10f; }
                if (r.trigger == "Every") { float n = Mathf.Clamp(r.period / k, 1, 30); moved |= !Mathf.Approximately(n, r.period); r.period = Mathf.Round(n * 10) / 10f; }
                foreach (var c in r.when)
                {
                    if (c.type == "Chance") { float n = Mathf.Clamp(c.value * k, 0.05f, 0.9f); moved |= !Mathf.Approximately(n, c.value); c.value = Mathf.Round(n * 100) / 100f; }
                    if (c.type == "EveryNth") { float n = Mathf.Clamp(Mathf.Round(c.value / k), 2, 10); moved |= !Mathf.Approximately(n, c.value); c.value = n; }
                }
                if (r.trigger == "StateFull")
                {
                    var s = a.State(r.param);
                    if (s != null && s.type == "Counter") { float n = Mathf.Clamp(Mathf.Round(s.max / k), 2, RuleConfig.Def.maxStateValue); moved |= !Mathf.Approximately(n, s.max); s.max = n; }
                }
                // Cooldown added by a cycle/spawn repair is a safety bound — never relax it below the repair value
            }
            // When over budget and no lever exists yet, add an internal cooldown to the most frequent trigger rule
            if (!moved && k < 1)
            {
                var hot = a.rules.FirstOrDefault(r => r.kind == RuleKind.Trigger && r.trigger != "Every");
                if (hot != null) { hot.cooldown = Mathf.Max(hot.cooldown, 0.5f) / k; moved = true; }
            }
            return moved;
        }

        static bool Scale(AbilityDef a, float k)
        {
            bool moved = false;
            foreach (var r in a.rules)
                foreach (var o in r.ops)
                {
                    var p = RuleLanguage.Get(o.op);
                    if (p == null) continue;
                    if (o.op == "Convert")
                    {
                        // a conversion ratio is a number the balancer owns too — within its whitelisted range
                        if (RuleLanguage.ConvertPairs.TryGetValue(o.from + ">" + o.to, out var cpr)) { float n = Mathf.Clamp(o.amount * k, cpr.min, cpr.max); moved |= !Mathf.Approximately(n, o.amount); o.amount = Mathf.Round(n * 1000) / 1000f; }
                        continue;
                    }
                    if (p.scalable == "none" || o.op is "CapStat") continue;
                    if (o.op == "ModifyStat")
                    {
                        if (o.mode is "Min" or "Max" or "Override") continue;
                        if (!RuleLanguage.StatInfo.TryGetValue(o.stat ?? "", out var si)) continue;
                        if (o.amount > 0) { float n = Mathf.Clamp(o.amount * k, 0.01f, si.max); moved |= !Mathf.Approximately(n, o.amount); o.amount = n; }
                        if (o.per > 0) { float n = Mathf.Min(o.per * k, si.max / Mathf.Max(0.05f, ExpectedScale(a, r, o.scale) * 2)); moved |= !Mathf.Approximately(n, o.per); o.per = n; }
                        continue;
                    }
                    switch (p.scalable)
                    {
                        case "amount":
                        {
                            if (o.amount > 0) { float n = Mathf.Clamp(o.amount * k, p.min, p.max); moved |= !Mathf.Approximately(n, o.amount); o.amount = n; }
                            // the runtime clamps the final magnitude to the primitive max; the coefficient only has to keep the typical value in range
                            if (o.per > 0) { float n = Mathf.Min(o.per * k, p.max / Mathf.Max(0.05f, ExpectedScale(a, r, o.scale))); moved |= !Mathf.Approximately(n, o.per); o.per = n; }
                            if (o.op is "Shield" or "Buff" or "Spawn" or "ApplyStatus" && k > 1.05f && o.amount >= p.max * 0.99f)
                            { float n = Mathf.Min(o.duration * Mathf.Sqrt(k), o.op == "Spawn" ? RuleConfig.Def.maxSpawnDuration : 10); moved |= !Mathf.Approximately(n, o.duration); o.duration = n; }
                            break;
                        }
                        case "count":
                        {
                            int n = Mathf.Clamp(Mathf.RoundToInt(o.count * k), Mathf.RoundToInt(p.min), Mathf.RoundToInt(p.max));
                            moved |= n != o.count; o.count = n; break;
                        }
                        case "duration":
                        {
                            float n = Mathf.Clamp(o.amount * k, p.min, p.max);
                            moved |= !Mathf.Approximately(n, o.amount); o.amount = n; break;
                        }
                    }
                }
            foreach (var r in a.rules) foreach (var o in r.ops) Round(o);
            return moved;
        }

        static void Round(OpDef o)
        {
            o.amount = o.op is "GainGold" or "GainXp" or "Push" or "Pull" or "DropPickup" or "DelayedReward" ? Mathf.Round(o.amount)
                : o.op == "Convert" ? Mathf.Round(o.amount * 1000) / 1000f : Mathf.Round(o.amount * 100) / 100f;
            o.per = Mathf.Round(o.per * 1000) / 1000f;
            o.duration = Mathf.Round(o.duration * 10) / 10f;
        }

        // ───────────── 10 Risk ─────────────

        /// <summary>Risk is not power: a weak ability can still be dangerous (unbounded recursion, entity floods, meta stacking).</summary>
        static void Risk(AbilityDef a, BuildContext b, Result res, System.Action<string, string, string> fail)
        {
            var rv = RiskModel.Evaluate(a, b);
            res.risk = rv.ToString();
            var cfg = b.cfg;
            for (int i = 0; i < RiskModel.Axes.Length; i++)
            {
                float v = rv.axes[i];
                if (v > cfg.riskCap) fail("RISK_" + RiskModel.Axes[i].ToUpper(), $"{v:0.00}", RiskModel.Advice(i));
                // High (not capped) risk is allowed only for the complexity tiers that are meant to be strange
                else if (v > cfg.riskWarn && a.tier < RiskModel.MinTierFor(i)) fail("RISK_TIER_" + RiskModel.Axes[i].ToUpper(), $"{v:0.00}", $"this much {RiskModel.Axes[i]} needs complexity tier ≥ {RiskModel.MinTierFor(i)}");
            }
        }

        // ───────────── 11 Runtime cost ─────────────

        static void RuntimeCost(AbilityDef a, BuildContext b, System.Action<string, string, string> fail, System.Action<string, string, string> fix)
        {
            var rep = PowerModel.Evaluate(new List<AbilityDef>(b.owned) { a }, b);
            float live = 0;
            foreach (var r in a.rules) if (rep.liveSpawns.TryGetValue(r, out var v)) live += v;
            if (live > b.cfg.maxExpectedLiveSpawns)
            {
                // Repair once: add a cooldown that brings the expected population under the limit
                foreach (var r in a.rules.Where(r => r.ops.Any(o => o.op == "Spawn")))
                {
                    float dur = r.ops.Where(o => o.op == "Spawn").Max(o => o.duration * o.count);
                    r.cooldown = Mathf.Max(r.cooldown, Mathf.Ceil(dur * 2 / b.cfg.maxExpectedLiveSpawns * 10) / 10f);
                }
                fix("SPAWN_POPULATION", $"{live:0.#} live", $"cooldown added (limit {b.cfg.maxExpectedLiveSpawns})");
            }
            float procs = a.rules.Where(r => rep.freq.ContainsKey(r)).Sum(r => rep.freq[r]);
            float cost = a.rules.Where(r => rep.cost.ContainsKey(r)).Sum(r => rep.cost[r]);
            if (procs > 20) fail("PROC_RATE", $"{procs:0.#}/s", "fires too often — add Chance/EveryNth/cooldown");
            if (cost > 60) fail("RUNTIME_COST", $"{cost:0}/s", "too many hit checks/spawns per second");
        }

        // ───────────── 12 Simulation ─────────────

        static void Simulation(AbilityDef a, BuildContext b, Result res, System.Action<string, string, string> fail)
        {
            var sim = RuleSimulator.Run(a, b);
            res.sim = sim.Summary();
            var cfg = b.cfg;
            if (sim.peakProcsPerSecond > cfg.simMaxProcsPerSecond) fail("SIM_PROC_STORM", $"{sim.peakProcsPerSecond:0.#}/s", "the simulated fight shows a proc storm — throttle the trigger");
            if (sim.peakEntities > cfg.maxSpawnPerOwner) fail("SIM_ENTITY_FLOOD", $"{sim.peakEntities}", "too many entities alive at once");
            if (sim.hitDepthCap) fail("SIM_DEPTH", "proc depth", "the simulated chain hit the proc-depth limit every time — the effect feeds itself");
            if (!sim.simulated) return;
            // Compare like with like: the realised trigger part vs the static estimate of the same trigger part.
            // Small parts (< 8 points) are noise-dominated and only checked for storms/floods above.
            float est = sim.staticTrigger, got = sim.power;
            if (Mathf.Max(est, got) >= 8)
            {
                float hi = Mathf.Max(Mathf.Max(est, got), 1), lo = Mathf.Max(Mathf.Min(est, got), 1);
                if (hi / lo > cfg.simMaxDivergence)
                    fail("SIM_DIVERGENCE", $"static {est:0} vs sim {got:0}", "the static estimate and the simulation disagree a lot — the power model misreads this structure");
            }
        }

        // ───────────── Mechanic id ─────────────

        public static string Mechanic(AbilityDef a)
        {
            var parts = new List<string>();
            foreach (var r in a.rules)
            {
                string head = r.kind == RuleKind.Trigger ? r.trigger : r.kind;
                parts.Add((head + "_" + string.Join("_", r.ops.Select(o => o.op + (string.IsNullOrEmpty(o.rel) ? "" : "-" + o.rel)))).ToLower());
            }
            return string.Join("__", parts);
        }
    }
}
