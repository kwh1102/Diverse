using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// The LLM's creative output, before compilation (design doc #14 / #36: "AI does the creative part, the compiler the
    /// mechanical part"). Either a list of mechanic macros (the 12 rule types of the Rule Language) or a direct Rule IR
    /// draft — or both (macros first, then the draft's rules are appended).
    /// </summary>
    [Serializable]
    public class AbilityIntent
    {
        public string concept;                         // one creative sentence
        public string semanticReason;                  // Korean, shown on the card
        public List<MechanicIntent> mechanics = new List<MechanicIntent>();
        public List<StateDef> states = new List<StateDef>();
        public List<RuleDef> rules = new List<RuleDef>();
    }

    /// <summary>
    /// One mechanic macro. type = Effect | Accumulate | Threshold | Scaling | Tradeoff | Convert | Replace | Constrain | Charge |
    /// Window | Anchor | Hunt | Meta | System. Fields are read per type (see AbilityCompiler.Expand). Names may use aliases.
    /// </summary>
    [Serializable]
    public class MechanicIntent
    {
        public string type;
        public string on, release, until;              // triggers (aliases ok: "on kill", "perfect dodge"…)
        public string effect;                          // op (alias ok: "explosion" → Nova, "lifesteal" → Drain…)
        public string target, at, element, status, entity, stat, tag, from, to, rel, time, select;
        public string scale;                           // value reference (alias ok: "missing hp" → self.missingHpPct)
        public string price;                           // Tradeoff/Constrain: the cost (stat name or action)
        public string label;                           // Korean state label
        public int count;                              // thresholds, stacks, ticks
        public float chance, cooldown, period, window;
    }

    /// <summary>
    /// Ability Compiler: Intent → Rule IR. Resolves aliases to primitive ids and expands mechanic macros into states + rules,
    /// using ONLY registered primitives. The result still goes through RuleValidator (the compiler never decides numbers).
    /// </summary>
    public static class AbilityCompiler
    {
        public class Output { public AbilityDef ability; public readonly List<string> notes = new List<string>(); }

        public static Output Compile(AbilityIntent intent)
        {
            var o = new Output();
            var a = new AbilityDef { concept = intent.concept, semanticReason = intent.semanticReason, source = "ai" };
            var n = new int[1];
            foreach (var m in intent.mechanics ?? new List<MechanicIntent>())
            {
                try { Expand(m, a, o.notes, n); }
                catch (Exception e) { o.notes.Add($"mechanic {m?.type}: {e.Message}"); }
            }
            foreach (var s in intent.states ?? new List<StateDef>()) if (a.State(s.name) == null) a.states.Add(s);
            foreach (var r in intent.rules ?? new List<RuleDef>()) a.rules.Add(r);
            Resolve(a, o.notes);
            a.EnsureLists();
            o.ability = a;
            return o;
        }

        /// <summary>Alias resolution over an existing IR (also used on direct LLM drafts).</summary>
        public static void Resolve(AbilityDef a, List<string> notes)
        {
            a.EnsureLists();
            foreach (var r in a.rules)
            {
                r.kind = Canon(RuleKind.All, r.kind) ?? r.kind;
                if (r.kind == RuleKind.Trigger) r.trigger = Trigger(r.trigger, notes) ?? r.trigger;
                r.replaces = Canon(RuleLanguage.Actions, r.replaces) ?? r.replaces;
                r.select = Canon(RuleLanguage.AbilitySelectors, r.select) ?? r.select;
                foreach (var c in r.when)
                {
                    c.type = Canon(RuleLanguage.Cat("Condition").Select(p => p.id.Substring(5)), c.type) ?? c.type;
                    if (c.type == "Compare") c.a = Value(c.a) ?? c.a;
                    if (c.type is "TargetHas" or "TargetLacks") c.text = Status(c.text) ?? c.text;
                    if (c.type == "WithinTime") c.text = Trigger(c.text, notes) ?? c.text;
                    if (c.type == "EventTag") c.text = c.text?.ToUpperInvariant();
                    if (c.type == "Chance" && c.value > 1) c.value /= 100f;
                }
                // Trigger slot holding something else: a stateful selector/op name → the event that naturally feeds it
                if (r.kind == RuleKind.Trigger && !RuleLanguage.Is(r.trigger, "Trigger"))
                {
                    string guess = Norm(r.trigger) switch
                    {
                        "storedtarget" or "marked" or "mark" => "Hit",
                        "gainshield" or "shield" or "shielded" => "Guard",
                        "spawn" or "summon" => "CloneSpawn",
                        "stateful" or "full" or "charged" => "StateFull",
                        "status" or "applystatus" => "StatusApplied",
                        _ => null,
                    };
                    if (guess != null) { notes.Add($"trigger '{r.trigger}' → {guess}"); r.trigger = guess; if (guess == "StateFull" && string.IsNullOrEmpty(r.param)) r.param = a.states.FirstOrDefault()?.name; }
                }
                foreach (var op in r.ops)
                {
                    op.op = Op(op.op, notes) ?? op.op;
                    // A stat change inside a trigger rule is a timed buff ("for 5 s after X, +speed")
                    if (r.kind == RuleKind.Trigger && op.op == "ModifyStat")
                    {
                        op.op = "Buff";
                        if (!RuleLanguage.BuffStats.Contains(Stat(op.stat) ?? "")) op.stat = "Attack";
                        notes.Add("ModifyStat in a trigger rule → Buff");
                    }
                    // Temporal written as a duration ("5s", "3 seconds") → Duration semantics (the op's own duration)
                    if (!string.IsNullOrEmpty(op.time) && !RuleLanguage.IsTemporal(op.time))
                    {
                        var mt = System.Text.RegularExpressions.Regex.Match(op.time, @"([d.]+)s*(s|sec|second|초)?");
                        if (mt.Success && float.TryParse(mt.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
                        { notes.Add($"time '{op.time}' → duration {secs}s"); op.duration = secs; op.time = null; }
                        else { var tt = Canon(RuleLanguage.Temporals.Keys, op.time) ?? TemporalAlias(op.time); if (tt != null) op.time = tt; }
                    }
                    // Relation that isn't one (a temporal/op word) → drop it rather than reject the card
                    if (!string.IsNullOrEmpty(op.rel) && !RuleLanguage.IsRelation(op.rel) && Canon(RuleLanguage.Relations.Keys, op.rel) == null) { notes.Add($"rel '{op.rel}' dropped"); op.rel = null; }
                    op.target = Canon(RuleLanguage.Cat("Selector").Select(p => p.id.Substring(4)), op.target) ?? op.target;
                    op.at = Canon(RuleLanguage.Cat("Position").Select(p => p.id.Substring(4)), op.at) ?? op.at;
                    op.element = Element(op.element) ?? op.element;
                    op.status = Status(op.status) ?? op.status;
                    op.entity = Canon(RuleLanguage.Entities, op.entity) ?? op.entity;
                    op.stat = Stat(op.stat) ?? op.stat;
                    op.rel = Canon(RuleLanguage.Relations.Keys, op.rel) ?? op.rel;
                    op.time = Canon(RuleLanguage.Temporals.Keys, op.time) ?? op.time;
                    op.scale = Value(op.scale) ?? op.scale;
                    if (!string.IsNullOrEmpty(op.tag)) op.tag = op.tag.ToUpperInvariant();
                    if (op.status == null && op.op is "ApplyStatus" or "Detonate" or "Spread") op.status = RuleLanguage.StatusOf(op.element);
                }
            }
        }

        // ───────────── Macro expansion (the Rule Language rule types) ─────────────

        static readonly string[] Macros = { "Effect", "Accumulate", "Threshold", "Scaling", "Tradeoff", "Convert", "Replace", "Constrain", "Charge", "Window", "Anchor", "Hunt", "Meta", "System" };

        /// <summary>
        /// Tolerant macro resolution — LLMs often put something else in "type": a rule kind ("Trigger", "Continuous"…),
        /// a trigger ("Guard", "Every"), an op ("StoreTarget", "ExtendEntities"), a synonym or a typo ("Accumlate").
        /// Each is mapped to the macro that means the same thing; the original word fills the matching slot when empty.
        /// </summary>
        static string MacroType(MechanicIntent m, List<string> notes)
        {
            string t = Canon(Macros, m.type);
            if (t != null) return t;
            string n = Norm(m.type);
            string mapped = n switch
            {
                "trigger" or "reactive" or "onevent" or "proc" or "when" => "Effect",
                "continuous" or "passive" or "modifier" or "stat" or "statmod" or "spatial" => "Scaling",
                "conversion" or "redefine" => "Convert",
                "replacement" or "swap" => "Replace",
                "constraint" or "price" or "cost" or "forbid" or "vow" => "Constrain",
                "accumulation" or "store" or "stack" or "release" => "Accumulate",
                "generation" or "choice" or "economy" => "System",
                "timer" or "buffwindow" => "Window",
                "resource" or "charges" => "Charge",
                "stacks" or "momentum" => "Hunt",
                "mark" or "beacon" or "remember" => "Anchor",
                "counter" or "everynth" or "nth" => "Threshold",
                _ => null,
            };
            // State-ish ops with a release trigger are really the stateful macros
            if (mapped == null && n is "storetarget" or "storeposition" && !string.IsNullOrEmpty(m.release)) mapped = "Anchor";
            if (mapped == null && n is "addstate" or "setstate" && !string.IsNullOrEmpty(m.release)) mapped = "Accumulate";
            // A trigger in the type slot → an Effect on that trigger; an op → an Effect/Meta/System doing that op
            if (mapped == null && Trigger(m.type) is string trig) { mapped = "Effect"; if (string.IsNullOrEmpty(m.on)) m.on = trig; }
            if (mapped == null && Op(m.type) is string op)
            {
                var p = RuleLanguage.Get(op);
                mapped = p != null && p.kinds.Contains('M') ? "Meta" : p != null && p.kinds.Contains('S') ? "System" : p != null && p.kinds == "C" ? "Scaling" : "Effect";
                if (string.IsNullOrEmpty(m.effect)) m.effect = op;
            }
            // A typo: the closest macro name by edit distance (at most 2 edits)
            if (mapped == null && n.Length >= 4)
            {
                var best = Macros.Select(x => (x, d: Distance(Norm(x), n))).OrderBy(x => x.d).First();
                if (best.d <= 2) mapped = best.x;
            }
            // Nothing in the type at all: infer from the fields that were filled
            if (mapped == null)
                mapped = !string.IsNullOrEmpty(m.release) ? "Accumulate" : !string.IsNullOrEmpty(m.from) && !string.IsNullOrEmpty(m.to) ? "Convert"
                    : !string.IsNullOrEmpty(m.price) ? "Tradeoff" : !string.IsNullOrEmpty(m.select) ? "Meta" : !string.IsNullOrEmpty(m.scale) && string.IsNullOrEmpty(m.on) ? "Scaling"
                    : !string.IsNullOrEmpty(m.on) || !string.IsNullOrEmpty(m.effect) ? "Effect" : null;
            if (mapped != null) notes.Add($"mechanic type '{m.type}' → {mapped}");
            return mapped;
        }

        /// <summary>Typo tolerance: the closest id within 2 edits (only for words of 5+ letters, to avoid wild guesses).</summary>
        static string Closest(IEnumerable<string> set, string s)
        {
            string n = Norm(s);
            if (n.Length < 5) return null;
            var best = set.Select(x => (x, d: Distance(Norm(x), n))).OrderBy(x => x.d).FirstOrDefault();
            return best.x != null && best.d <= 2 ? best.x : null;
        }

/// <summary>
        /// Salvage what an LLM tends to write outside the closed language, BEFORE expansion:
        ///   pseudo-triggers that are really temporals ("NextAttack" → Hit + time UntilNextAttack, "NextHit" → UntilHit…),
        ///   invented entities ("FireBolt", "IceSpike") → their element + the matching op (Projectile/Nova/Zone…),
        ///   percent / multiplier literals in value slots ("300%", "x2") → dropped (numbers are the engine's),
        ///   unknown effect words that name an element ("fire") → element + Nova.
        /// Every change is noted so the pipeline log shows what was reinterpreted.
        /// </summary>
        static void Salvage(MechanicIntent m, List<string> notes)
        {
            (string trig, string time)? Deferred(string s) => Norm(s) switch
            {
                "nextattack" or "nextstrike" or "nextswing" => ("Hit", "UntilNextAttack"),
                "nexthit" or "onnexthit" => ("Hit", "UntilHit"),
                "nextdamage" or "nexttimehit" or "whenhitnext" => ("Damaged", "UntilDamaged"),
                _ => ((string, string)?)null,
            };
            if (Trigger(m.on) == null && Deferred(m.on) is var (t1, tm1)) { notes.Add($"on '{m.on}' → {t1} + {tm1}"); m.on = t1; m.time ??= tm1; }
            if (!string.IsNullOrEmpty(m.release) && Trigger(m.release) == null && Deferred(m.release) is var (t2, _)) { notes.Add($"release '{m.release}' → {t2}"); m.release = t2; }

            // Invented entity / effect names: find an element word and a shape word inside them
            foreach (var slot in new[] { "entity", "effect" })
            {
                string v = slot == "entity" ? m.entity : m.effect;
                if (string.IsNullOrEmpty(v)) continue;
                if (slot == "entity" && Canon(RuleLanguage.Entities, v) != null) continue;
                if (slot == "effect" && Op(v) != null) continue;
                if (Canon(Macros, v) != null || Canon(RuleKind.All, v) != null) continue;
                string n = Norm(v);
                var words = System.Text.RegularExpressions.Regex.Split(v, @"(?<=[a-z])(?=[A-Z])|[^A-Za-z]+").Select(Norm).Where(w => w.Length > 0).ToList();
                string el = RuleLanguage.Elements.FirstOrDefault(e => words.Contains(Norm(e)))
                            ?? (n.Contains("ice") ? "Frost" : n.Contains("flame") || n.Contains("burn") ? "Fire" : n.Contains("thunder") || n.Contains("spark") ? "Lightning"
                              : n.Contains("dark") ? "Shadow" : n.Contains("light") || n.Contains("holy") ? "Holy" : n.Contains("venom") || n.Contains("toxic") ? "Poison" : null);
                string shape = n.Contains("bolt") || n.Contains("arrow") || n.Contains("spike") || n.Contains("shard") || n.Contains("missile") ? "Projectile"
                    : n.Contains("wave") || n.Contains("burst") || n.Contains("blast") || n.Contains("explo") || n.Contains("nova") ? "Nova"
                    : n.Contains("field") || n.Contains("pool") || n.Contains("ground") || n.Contains("area") ? "Spawn"
                    : n.Contains("chain") ? "Chain" : n.Contains("slash") || n.Contains("blade") ? "Slash" : null;
                if (el == null && shape == null) continue;
                if (el != null && string.IsNullOrEmpty(m.element)) m.element = el;
                if (slot == "entity")
                {
                    m.entity = shape == "Spawn" ? "Zone" : null;
                    if (shape != null && shape != "Spawn" && (string.IsNullOrEmpty(m.effect) || m.effect is "Spawn" or "Chain" or "Replace")) m.effect = shape;
                }
                else { m.effect = shape ?? "Nova"; if (shape == "Spawn") m.entity ??= "Zone"; }
                notes.Add($"{slot} '{v}' → element {m.element ?? "-"}, effect {m.effect}{(m.entity != null ? ", entity " + m.entity : "")}");
            }

            // Number literals in value slots: the engine decides numbers
            if (!string.IsNullOrEmpty(m.scale) && Value(m.scale) == null && System.Text.RegularExpressions.Regex.IsMatch(m.scale, @"^[x×]?s*[d.]+s*%?$"))
            { notes.Add($"scale '{m.scale}' dropped (numbers are the engine's)"); m.scale = null; }
            // Positions/targets that are actually selectors or vice versa are fixed later by the validator; unknown ones are dropped here
            if (!string.IsNullOrEmpty(m.at) && RuleLanguage.Position(m.at) == null && RuleLanguage.Selector(m.at) != null) { if (string.IsNullOrEmpty(m.target)) m.target = m.at; m.at = null; }
            if (!string.IsNullOrEmpty(m.target) && RuleLanguage.Selector(m.target) == null && RuleLanguage.Position(m.target) != null) { if (string.IsNullOrEmpty(m.at)) m.at = m.target; m.target = null; }
        }

static string TemporalAlias(string s) => Norm(s) switch
        {
            "nextattack" => "UntilNextAttack", "nexthit" or "onhit" => "UntilHit", "whendamaged" or "nextdamage" => "UntilDamaged",
            "overtime" or "tick" or "ticks" or "periodic" or "dot" => "Periodic", "later" or "delayed" or "after" => "Delay", "stacking" or "stacks" => "Stack",
            _ => null,
        };

        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        static void Expand(MechanicIntent m, AbilityDef a, List<string> notes, int[] n)
        {
            string type = MacroType(m, notes);
            Salvage(m, notes);
            // A replacement of something that is not a replaceable action is really "when X, also do Y"
            if (type == "Replace" && Canon(RuleLanguage.Actions, m.on) == null && Trigger(m.on) != null)
            { notes.Add($"Replace on '{m.on}' → Effect"); type = "Effect"; }
            string S(string baseName) { string s = baseName + (n[0] == 0 ? "" : n[0].ToString()); n[0]++; return s; }
            RuleDef T(string trig) => new RuleDef { kind = RuleKind.Trigger, trigger = Trigger(trig, notes) ?? trig, period = m.period > 0 ? m.period : 4, cooldown = m.cooldown };
            OpDef Pay() => new OpDef { op = Op(m.effect, notes) ?? "Nova", target = m.target, at = m.at, element = Element(m.element), status = Status(m.status), entity = Canon(RuleLanguage.Entities, m.entity), stat = Stat(m.stat), rel = m.rel, time = m.time, scale = Value(m.scale) };
            void Throttle(RuleDef r) { if (m.chance > 0) r.when.Add(new CondDef { type = "Chance", value = m.chance > 1 ? m.chance / 100 : m.chance }); }

            switch (type)
            {
                case "Effect":                 // Effect rule: when X → do Y
                {
                    var r = T(m.on); Throttle(r); r.ops.Add(Pay()); a.rules.Add(r);
                    break;
                }
                case "Accumulate":             // store a quantity from one event, release it (scaled) on another
                {
                    string s = S("store");
                    bool quantity = Trigger(m.on, notes) is "Damaged" or "Hit" or "Healed" or "Overkill";
                    a.states.Add(new StateDef { name = s, label = m.label ?? "축적", type = quantity ? "StoredValue" : "Counter", max = m.count > 0 ? m.count : quantity ? 8 : 5 });
                    var fill = T(m.on);
                    fill.ops.Add(quantity ? new OpDef { op = "AddState", state = s, scale = "event.amount", per = 1 } : new OpDef { op = "AddState", state = s, amount = 1 });
                    var use = T(m.release ?? "PerfectDodge");
                    use.when.Add(new CondDef { type = "Compare", a = "state:" + s, cmp = ">", value = 0 });
                    var p = Pay(); p.scale = "state:" + s; use.ops.Add(p);
                    use.ops.Add(new OpDef { op = "SetState", state = s, amount = 0 });
                    a.rules.Add(fill); a.rules.Add(use);
                    break;
                }
                case "Threshold":              // count N events → payoff → reset (StateFull)
                {
                    string s = S("count");
                    a.states.Add(new StateDef { name = s, label = m.label ?? "누적", type = "Counter", max = Mathf.Max(2, m.count > 0 ? m.count : 3) });
                    var fill = T(m.on); Throttle(fill); fill.ops.Add(new OpDef { op = "AddState", state = s, amount = 1 });
                    var full = new RuleDef { kind = RuleKind.Trigger, trigger = "StateFull", param = s };
                    var p = Pay(); if (p.target == "EventTarget") p.target = "NearestEnemy"; full.ops.Add(p);
                    full.ops.Add(new OpDef { op = "SetState", state = s, amount = 0 });
                    a.rules.Add(fill); a.rules.Add(full);
                    break;
                }
                case "Hunt":                   // stacks that grow while killing and decay over time (TFT "hunter" style)
                {
                    string s = S("hunt");
                    a.states.Add(new StateDef { name = s, label = m.label ?? "사냥감", type = "Counter", max = m.count > 0 ? m.count : 10, decay = 0.2f });
                    var fill = T(m.on ?? "Kill"); fill.ops.Add(new OpDef { op = "AddState", state = s, amount = 1 });
                    var pass = new RuleDef { kind = RuleKind.Continuous };
                    pass.ops.Add(new OpDef { op = "ModifyStat", stat = Stat(m.stat) ?? "AttackSpeed", scale = "state:" + s, per = 0.02f });
                    a.rules.Add(fill); a.rules.Add(pass);
                    break;
                }
                case "Charge":                 // a regenerating resource spent by a trigger
                {
                    string s = S("charge");
                    a.states.Add(new StateDef { name = s, label = m.label ?? "충전", type = "Charge", max = m.count > 0 ? m.count : 3, regen = 0.25f });
                    var use = T(m.on); Throttle(use);
                    use.when.Add(new CondDef { type = "Compare", a = "state:" + s, cmp = ">=", value = 1 });
                    use.ops.Add(Pay());
                    use.ops.Add(new OpDef { op = "AddState", state = s, amount = -1 });
                    a.rules.Add(use);
                    break;
                }
                case "Window":                 // after X, for a few seconds Y is empowered (Timer state)
                {
                    string s = S("window");
                    a.states.Add(new StateDef { name = s, label = m.label ?? "기회", type = "Timer", max = m.window > 0 ? m.window : 3 });
                    var open = T(m.on); open.ops.Add(new OpDef { op = "SetState", state = s, amount = m.window > 0 ? m.window : 3 });
                    var pass = new RuleDef { kind = RuleKind.Continuous };
                    pass.when.Add(new CondDef { type = "Compare", a = "state:" + s, cmp = ">", value = 0 });
                    var p = Pay();
                    if (!(p.op is "ModifyStat" or "AmplifyDamage" or "Resist")) { p = new OpDef { op = "ModifyStat", stat = Stat(m.stat) ?? "AttackSpeed" }; }
                    pass.ops.Add(p);
                    a.rules.Add(open); a.rules.Add(pass);
                    break;
                }
                case "Anchor":                 // remember a place / an enemy, act on it later
                {
                    string s = S("anchor");
                    bool enemy = m.target == "StoredTarget";
                    a.states.Add(new StateDef { name = s, label = m.label ?? (enemy ? "표적" : "표지"), type = enemy ? "Target" : "Position" });
                    var store = T(m.on);
                    store.ops.Add(enemy ? new OpDef { op = "StoreTarget", state = s, target = "EventTarget" } : new OpDef { op = "StorePosition", state = s, at = m.at ?? "EventPos" });
                    var use = T(m.release ?? "SkillCast");
                    var p = Pay(); if (enemy) { p.target = "StoredTarget"; p.state = s; } else { p.at = "Stored"; p.state = s; }
                    use.ops.Add(p);
                    a.rules.Add(store); a.rules.Add(use);
                    break;
                }
                case "Scaling":                // a stat grows with some value (missing HP, build tag count, enemies near…)
                {
                    var r = new RuleDef { kind = RuleKind.Continuous };
                    var p = Pay();
                    if (!(p.op is "ModifyStat" or "AmplifyDamage" or "Resist")) p = new OpDef { op = "ModifyStat", stat = Stat(m.stat) ?? "Attack" };
                    p.scale = Value(m.scale) ?? "self.missingHpPct"; p.per = 0.1f;
                    r.ops.Add(p);
                    a.rules.Add(r);
                    break;
                }
                case "Tradeoff":               // give something up for something else (both in one continuous rule)
                {
                    var r = new RuleDef { kind = RuleKind.Continuous };
                    string price = Stat(m.price);
                    if (price != null) r.ops.Add(new OpDef { op = "ModifyStat", stat = price, amount = -0.2f });
                    var gain = Pay();
                    if (gain.op is not ("ModifyStat" or "AmplifyDamage" or "Resist" or "Aura" or "StatusPotency")) gain = new OpDef { op = "ModifyStat", stat = Stat(m.stat) ?? "Attack", amount = 0.2f, scale = Value(m.scale) };
                    r.ops.Add(gain);
                    a.rules.Add(r);
                    if (price == null && Canon(RuleLanguage.Actions, m.price) is string act)
                        a.rules.Add(new RuleDef { kind = RuleKind.Constrain, ops = { new OpDef { op = "Forbid", mode = act } } });
                    break;
                }
                case "Constrain":
                {
                    string act = Canon(RuleLanguage.Actions, m.price ?? m.effect);
                    string stat = Stat(m.price ?? m.stat);
                    var r = new RuleDef { kind = RuleKind.Constrain };
                    if (act != null) r.ops.Add(new OpDef { op = "Forbid", mode = act });
                    else if (stat != null) r.ops.Add(new OpDef { op = "CapStat", stat = stat, amount = -0.5f });
                    a.rules.Add(r);
                    break;
                }
                case "Convert":
                {
                    var r = new RuleDef { kind = RuleKind.Continuous };
                    if (m.chance > 0 || !string.IsNullOrEmpty(m.scale)) r.when.Add(new CondDef { type = "Compare", a = Value(m.scale) ?? "self.hpPct", cmp = "<=", value = m.chance > 0 ? m.chance : 0.3f });
                    var (pf, pt) = ConvertPair(m.from, m.to);
                    r.ops.Add(new OpDef { op = "Convert", from = pf, to = pt });
                    a.rules.Add(r);
                    break;
                }
                case "Replace":
                {
                    var r = new RuleDef { kind = RuleKind.Replace, replaces = Canon(RuleLanguage.Actions, m.on) ?? "Dash" };
                    r.ops.Add(new OpDef { op = "ReplaceWith", mode = Canon(new[] { "Blink", "Nova", "Shield", "Spawn", "Projectile" }, m.effect) ?? "Blink", element = Element(m.element), entity = Canon(RuleLanguage.Entities, m.entity) });
                    a.rules.Add(r);
                    break;
                }
                case "Meta":
                {
                    var r = new RuleDef { kind = RuleKind.Meta, select = Canon(RuleLanguage.AbilitySelectors, m.select) ?? "ByTag", selectArg = m.tag?.ToUpperInvariant() ?? Element(m.element) ?? Trigger(m.on, notes) };
                    var op = new OpDef { op = Op(m.effect, notes) ?? "AmplifyTagged", tag = m.tag?.ToUpperInvariant(), element = Element(m.element), from = Trigger(m.from, notes), to = Trigger(m.to, notes) };
                    if (r.select == "ByElement") r.selectArg = Element(m.element) ?? Element(m.tag);
                    r.ops.Add(op);
                    a.rules.Add(r);
                    break;
                }
                case "System":
                {
                    var r = new RuleDef { kind = RuleKind.System };
                    r.ops.Add(new OpDef { op = Op(m.effect, notes) ?? "TierBias", tag = m.tag?.ToUpperInvariant(), amount = m.count != 0 ? m.count : 0, times = m.count });
                    a.rules.Add(r);
                    break;
                }
                default:
                    notes.Add($"unknown mechanic type '{m.type}'");
                    break;
            }
        }

        // ───────────── Alias resolution ─────────────

        static string Norm(string s) => string.IsNullOrEmpty(s) ? "" : new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>Exact (case/space-insensitive) match against a closed set.</summary>
        static string Canon(IEnumerable<string> set, string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string n = Norm(s);
            return set.FirstOrDefault(x => Norm(x) == n);
        }

        static readonly Dictionary<string, string> TriggerAlias = new Dictionary<string, string>
        {
            ["onhit"] = "Hit", ["whenhit"] = "Hit", ["strike"] = "Hit", ["onattack"] = "Attack", ["onkill"] = "Kill", ["takedown"] = "Kill", ["execute"] = "Kill",
            ["oncrit"] = "Crit", ["critical"] = "Crit", ["criticalhit"] = "Crit", ["ondash"] = "Dash", ["dodge"] = "Dash", ["roll"] = "Dash", ["evade"] = "PerfectDodge",
            ["perfectdodge"] = "PerfectDodge", ["justdodge"] = "PerfectDodge", ["parry"] = "Guard", ["block"] = "Guard", ["ondamaged"] = "Damaged", ["takedamage"] = "Damaged",
            ["hurt"] = "Damaged", ["lowhp"] = "LowHealth", ["lowhealth"] = "LowHealth", ["onheal"] = "Healed", ["spell"] = "SkillCast", ["cast"] = "SkillCast", ["skill"] = "SkillCast",
            ["combo"] = "ComboFinish", ["finisher"] = "ComboFinish", ["timer"] = "Every", ["periodic"] = "Every", ["interval"] = "Every", ["eachsecond"] = "Every",
            ["walk"] = "Moved", ["move"] = "Moved", ["firsthit"] = "FirstHit", ["overkill"] = "Overkill", ["elitekill"] = "EliteKill", ["bosskill"] = "BossKilled",
            ["pickup"] = "Pickup", ["loot"] = "Pickup", ["shieldbreak"] = "ShieldBroken", ["shieldbroken"] = "ShieldBroken", ["combatstart"] = "CombatStart",
            ["fightstart"] = "CombatStart", ["combatend"] = "CombatEnd", ["fightend"] = "CombatEnd", ["levelup"] = "LevelUp", ["clonespawn"] = "CloneSpawn",
            ["cloneexpire"] = "CloneExpire", ["clonedeath"] = "CloneExpire", ["summonexpire"] = "EntityExpired", ["projectileend"] = "ProjectileEnd", ["impact"] = "ProjectileEnd",
            ["statusapplied"] = "StatusApplied", ["statusexpired"] = "StatusExpired", ["full"] = "StateFull", ["empty"] = "StateEmpty",
        };

        static readonly Dictionary<string, string> OpAlias = new Dictionary<string, string>
        {
            ["explosion"] = "Nova", ["explode"] = "Nova", ["shockwave"] = "Nova", ["aoe"] = "Nova", ["blast"] = "Nova", ["hit"] = "Damage", ["strike"] = "Damage",
            ["lifesteal"] = "Drain", ["vampire"] = "Drain", ["leech"] = "Drain", ["bolt"] = "Projectile", ["missile"] = "Projectile", ["shoot"] = "Projectile",
            ["lightning"] = "Chain", ["chainlightning"] = "Chain", ["burn"] = "ApplyStatus", ["ignite"] = "ApplyStatus", ["poison"] = "ApplyStatus", ["inflict"] = "ApplyStatus",
            ["freeze"] = "Stun", ["stun"] = "Stun", ["slow"] = "Slow", ["knockback"] = "Push", ["vortex"] = "Pull", ["suck"] = "Pull", ["blackhole"] = "Pull",
            ["heal"] = "Heal", ["regen"] = "Heal", ["barrier"] = "Shield", ["shield"] = "Shield", ["buff"] = "Buff", ["empower"] = "EmpowerNext",
            ["summon"] = "Spawn", ["spawn"] = "Spawn", ["teleport"] = "Blink", ["blink"] = "Blink", ["dashto"] = "Leap", ["swap"] = "Swap", ["cooldownreset"] = "ResetCooldowns",
            ["refund"] = "ResetCooldowns", ["gold"] = "GainGold", ["xp"] = "GainXp", ["experience"] = "GainXp", ["potion"] = "GainPotion", ["consume"] = "Detonate",
            ["pop"] = "Detonate", ["contagion"] = "Spread", ["plague"] = "Spread", ["sweep"] = "Slash", ["laser"] = "Beam", ["ray"] = "Beam",
            ["amplify"] = "AmplifyTagged", ["repeat"] = "RepeatTagged", ["echo"] = "RepeatTagged", ["doublecast"] = "RepeatTagged", ["haste"] = "HasteTagged",
            ["infuse"] = "InfuseTagged", ["extend"] = "ExtendTagged", ["enlarge"] = "EnlargeTagged", ["multiply"] = "MultiplyTagged", ["retag"] = "RetagAbilities",
            ["unchain"] = "UnchainTagged", ["retrigger"] = "RetriggerTagged", ["fewerchoices"] = "OfferCount", ["choices"] = "OfferCount", ["rarity"] = "TierBias",
            ["reroll"] = "RerollDiscount", ["goldenegg"] = "DelayedReward", ["extraevolution"] = "ExtraEvolution", ["upgrade"] = "UpgradeRandom",
        };

        static readonly Dictionary<string, string> ValueAlias = new Dictionary<string, string>
        {
            ["missinghp"] = "self.missingHpPct", ["missinghealth"] = "self.missingHpPct", ["hp"] = "self.hpPct", ["health"] = "self.hpPct",
            ["shield"] = "self.shieldPct", ["enemiesnear"] = "enemies.near", ["nearbyenemies"] = "enemies.near", ["damagetaken"] = "event.amount",
            ["damage"] = "event.amount", ["amount"] = "event.amount", ["targethp"] = "target.hpPct", ["distance"] = "target.distance",
            ["level"] = "run.level", ["kills"] = "run.kills", ["gold"] = "self.gold", ["elements"] = "build.elements", ["abilities"] = "build.abilities",
            ["stilltime"] = "self.stillTime", ["standing"] = "self.stillTime", ["movement"] = "self.moveDistance", ["summons"] = "entities.count",
        };

        public static string Trigger(string s, List<string> notes = null)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var c = Canon(RuleLanguage.Cat("Trigger").Select(p => p.id), s);
            if (c != null) return c;
            if (TriggerAlias.TryGetValue(Norm(s), out var t)) return t;
            return Closest(RuleLanguage.Cat("Trigger").Select(p => p.id), s);
        }

        public static string Op(string s, List<string> notes = null)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var c = Canon(RuleLanguage.Cat("Op").Select(p => p.id), s);
            if (c != null) return c;
            if (OpAlias.TryGetValue(Norm(s), out var t)) return t;
            return Closest(RuleLanguage.Cat("Op").Select(p => p.id), s);
        }

        public static string Value(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Contains(':')) return s;
            var c = Canon(RuleLanguage.Cat("Value").Select(p => p.id.Substring(4)), s);
            if (c != null) return c;
            return ValueAlias.TryGetValue(Norm(s), out var t) ? t : null;
        }

        public static string Element(string s) => Canon(RuleLanguage.Elements, s) ?? (Norm(s) switch { "ice" => "Frost", "cold" => "Frost", "thunder" => "Lightning", "dark" => "Shadow", "light" => "Holy", "toxic" => "Poison", "air" => "Wind", _ => null });

        public static string Status(string s) => Canon(RuleLanguage.Statuses, s) ?? (Norm(s) switch { "fire" => "burn", "ignite" => "burn", "frost" => "chill", "slow" => "chill", "toxic" => "poison", "venom" => "poison", "bleeding" => "bleed", "vulnerable" => "mark", "weak" => "weaken", _ => null });

        public static string Stat(string s) => Canon(RuleLanguage.StatInfo.Keys, s) ?? (Norm(s) switch
        {
            "hp" or "health" or "maxhealth" => "MaxHp", "damage" or "attackpower" or "atk" => "Attack", "speed" or "movespeed" => "MoveSpeed",
            "aspd" or "attackspeed" => "AttackSpeed", "crit" or "critrate" => "CritChance", "critdmg" => "CritDamage", "cdr" or "cooldown" => "CooldownReduction",
            "defense" or "armor" => "Armor", "regen" or "regeneration" => "Regen", _ => null,
        });

        /// <summary>Resolve a conversion against the whitelist first (so "Shield→Damage" stays Damage, "Damage→Heal" becomes DamageDealt).</summary>
        static (string, string) ConvertPair(string from, string to)
        {
            foreach (var key in RuleLanguage.ConvertPairs.Keys)
            {
                var p = key.Split('>');
                if (Norm(p[0]) == Norm(from) && Norm(p[1]) == Norm(to)) return (p[0], p[1]);
            }
            string f = Pair(from), t = Pair(to);
            foreach (var key in RuleLanguage.ConvertPairs.Keys)
            {
                var p = key.Split('>');
                if ((p[0] == f || Norm(p[0]) == Norm(from)) && (p[1] == t || Norm(p[1]) == Norm(to) || p[1] == "Damage" && t == "DamageDealt")) return (p[0], p[1]);
            }
            return (f, t);
        }

        static string Pair(string s) => Norm(s) switch
        {
            "heal" or "healing" => "Heal", "shield" => "Shield", "damagedealt" or "damage" => "DamageDealt", "damagetaken" => "DamageTaken", "overheal" => "Overheal",
            "gold" => "Gold", "crit" => "Crit", "overkill" => "Overkill", "nova" or "explosion" => "Nova", "movedistance" or "movement" => "MoveDistance", "xp" => "Xp",
            _ => s,
        };
    }
}
