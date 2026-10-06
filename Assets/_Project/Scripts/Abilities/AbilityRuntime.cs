using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Rule IR executor. Every primitive in RuleLanguage has a handler here and nothing else can run — the runtime is closed.
    ///   Trigger    : subscribe to GameEvents, check conditions, run ops (proc depth / proc budget / cooldown)
    ///   Continuous : re-evaluated every 0.25s into stat modifiers; per-hit ones (AmplifyDamage/Resist) are checked per hit
    ///   Replace    : intercepts player actions (Dash / Potion / combo finisher)
    ///   Constrain  : forbids actions / caps stats (the price side of an ability)
    ///   Meta       : resolved per ability from selectors (tag / element / trigger / strongest / newest / all)
    ///   System     : read by the evolution pipeline; one-shot system ops run when the ability is acquired
    /// Ops are wrapped by the Relation layer (Echo, Chain, Bounce, Cascade…) and the Temporal layer (Delay, Periodic,
    /// UntilHit, ForNextN…). Ability state lives here (bounded, per owned ability), never in the IR.
    /// </summary>
    public class AbilityRuntime
    {
        public readonly List<AbilityDef> Owned = new List<AbilityDef>();
        readonly Player p;
        readonly Dictionary<AbilityDef, Dictionary<string, float>> states = new Dictionary<AbilityDef, Dictionary<string, float>>();
        readonly Dictionary<AbilityDef, Dictionary<string, Vector2>> positions = new Dictionary<AbilityDef, Dictionary<string, Vector2>>();
        readonly Dictionary<AbilityDef, Dictionary<string, Actor>> targets = new Dictionary<AbilityDef, Dictionary<string, Actor>>();
        readonly Dictionary<RuleDef, float> lastFire = new Dictionary<RuleDef, float>();
        readonly Dictionary<RuleDef, float> nextPeriodic = new Dictionary<RuleDef, float>();
        readonly Dictionary<RuleDef, int> nth = new Dictionary<RuleDef, int>();
        readonly Dictionary<OpDef, int> alternate = new Dictionary<OpDef, int>();
        readonly Dictionary<AbilityDef, MetaMods> meta = new Dictionary<AbilityDef, MetaMods>();
        readonly Dictionary<AbilityDef, Summon> lastSummon = new Dictionary<AbilityDef, Summon>();
        readonly Dictionary<AbilityDef, Clone> lastClone = new Dictionary<AbilityDef, Clone>();
        readonly Dictionary<AbilityDef, float> upgrades = new Dictionary<AbilityDef, float>();
        readonly List<Armed> armed = new List<Armed>();
        readonly List<(float bonus, Element el, string tags, int left)> empower = new List<(float, Element, string, int)>();
        readonly Dictionary<string, float> continuousStats = new Dictionary<string, float>();
        readonly HashSet<Actor> hitOnce = new HashSet<Actor>();
        readonly Queue<(float t, Vector2 pos)> trail = new Queue<(float, Vector2)>();
        Actor lastHit, lastAttacker, lastEventTarget;
        float continuousT, lastDamagedAt = -99, lastKillAt = -99, combatStartAt = -99, noEnemyT, stillT, moveAcc;
        bool inCombat;
        int frame = -1, procsThisFrame, delayedKills;
        readonly Dictionary<string, float> lastTrigger = new Dictionary<string, float>();
        public int fireCount;
        AbilityRulesDef Cfg => RuleConfig.Def;

        class MetaMods { public float amp = 1, repeat, haste, extend, enlarge, gate; public int multiply; public Element infuse; public bool unchain; public string retriggerFrom, retriggerTo; public readonly List<string> extraTags = new List<string>(); }

        /// <summary>A deferred op (UntilHit / UntilDamaged / UntilNextAttack / ForNextN).</summary>
        class Armed { public AbilityDef a; public RuleDef r; public OpDef o; public float scale, until; public int uses; public string on; }

        public AbilityRuntime(Player owner) { p = owner; }

        public void Add(AbilityDef g)
        {
            g.EnsureLists();
            Owned.Add(g);
            RebuildMeta();
            Recompute();
            Raise("AbilityAcquired");
            RunOneShotSystem(g);
        }

        /// <summary>Restore without firing acquisition triggers or one-shot system ops (continue / load).</summary>
        public void Restore(AbilityDef g) { g.EnsureLists(); Owned.Add(g); RebuildMeta(); }

        public void Clear()
        {
            Owned.Clear(); states.Clear(); positions.Clear(); targets.Clear(); lastFire.Clear(); nth.Clear(); meta.Clear(); empower.Clear();
            continuousStats.Clear(); armed.Clear(); upgrades.Clear(); alternate.Clear();
        }

        public void Recompute() => p.RecalcStats();

        // ───────────────────────── System rules ─────────────────────────

        IEnumerable<OpDef> SystemOps => Owned.SelectMany(a => a.rules).Where(r => r.kind == RuleKind.System).SelectMany(r => r.ops);
        public int OfferCount => Mathf.Clamp(3 + Mathf.RoundToInt(SystemOps.Where(o => o.op == "OfferCount").Sum(o => o.amount)), 2, 4);
        public int TierBias => Mathf.RoundToInt(SystemOps.Where(o => o.op == "TierBias").Sum(o => o.amount));
        public float BudgetMul => 1 + SystemOps.Where(o => o.op == "BudgetBias").Sum(o => o.amount);
        public float RerollMul => Mathf.Clamp01(1 - SystemOps.Where(o => o.op == "RerollDiscount").Sum(o => o.amount));
        public float TagWeight(IEnumerable<string> tags) => 1 + SystemOps.Where(o => o.op == "TagBias" && RuleLanguage.TagMatches(tags, o.tag)).Sum(o => o.amount);
        public float UpgradeOf(AbilityDef a) => upgrades.TryGetValue(a, out var u) ? u : 1;

        void RunOneShotSystem(AbilityDef g)
        {
            foreach (var o in g.rules.Where(r => r.kind == RuleKind.System).SelectMany(r => r.ops))
            {
                switch (o.op)
                {
                    case "ExtraEvolution": p.PendingEvolutions += Mathf.Max(1, o.count); GameEvents.Notify("추가 진화를 얻었다"); break;
                    case "UpgradeRandom":
                    {
                        var pool = Owned.Where(x => x != g && x.rules.Any(r => r.kind == RuleKind.Trigger || r.kind == RuleKind.Continuous)).ToList();
                        if (pool.Count == 0) break;
                        var pick = pool[Random.Range(0, pool.Count)];
                        upgrades[pick] = UpgradeOf(pick) * (1 + o.amount);
                        GameEvents.Notify($"[{pick.name}] 강화 ×{1 + o.amount:0.##}");
                        break;
                    }
                    case "DelayedReward": delayedKills = Mathf.Max(1, o.times); break;
                }
            }
        }

        // ───────────────────────── Meta rules ─────────────────────────

        /// <summary>The abilities a meta rule selects (never more than cfg.maxMetaTargets).</summary>
        public static List<AbilityDef> SelectAbilities(RuleDef r, OpDef o, IList<AbilityDef> owned, AbilityDef self, int cap)
        {
            string sel = string.IsNullOrEmpty(r.select) ? "ByTag" : r.select;
            string arg = !string.IsNullOrEmpty(r.selectArg) ? r.selectArg : o.tag;
            IEnumerable<AbilityDef> pool = owned.Where(x => x != self);
            switch (sel)
            {
                case "ByTag": pool = pool.Where(x => RuleLanguage.TagMatches(x.tags, arg)); break;
                case "ByElement": pool = pool.Where(x => x.rules.SelectMany(rr => rr.ops).Any(op => op.element == arg)); break;
                case "ByTrigger": pool = pool.Where(x => x.rules.Any(rr => rr.trigger == arg)); break;
                case "Strongest": pool = pool.OrderByDescending(x => x.power).Take(1); break;
                case "Newest": pool = pool.Reverse().Take(1); break;
                case "All": break;
            }
            return pool.Take(cap).ToList();
        }

        void RebuildMeta()
        {
            meta.Clear();
            foreach (var a in Owned) meta[a] = new MetaMods();
            foreach (var src in Owned)
                foreach (var r in src.rules.Where(r => r.kind == RuleKind.Meta))
                    foreach (var o in r.ops)
                        foreach (var a in SelectAbilities(r, o, Owned, src, Cfg.maxMetaTargets))
                        {
                            var m = meta[a];
                            switch (o.op)
                            {
                                case "AmplifyTagged": m.amp *= 1 + o.amount; break;
                                case "RepeatTagged": m.repeat = Mathf.Max(m.repeat, o.amount); break;
                                case "HasteTagged": m.haste = Mathf.Min(0.6f, m.haste + o.amount); break;
                                case "ExtendTagged": m.extend = Mathf.Min(1.5f, m.extend + o.amount); break;
                                case "EnlargeTagged": m.enlarge = Mathf.Min(1f, m.enlarge + o.amount); break;
                                case "MultiplyTagged": m.multiply = Mathf.Min(3, m.multiply + Mathf.Max(1, o.count)); break;
                                case "InfuseTagged": m.infuse = RuleLanguage.ParseElement(o.element); break;
                                case "RetagAbilities": if (!string.IsNullOrEmpty(o.tag)) m.extraTags.Add(o.tag); break;
                                case "GateTagged": m.gate = Mathf.Max(m.gate, o.amount); break;
                                case "UnchainTagged": m.unchain = true; break;
                                case "RetriggerTagged": m.retriggerFrom = o.from; m.retriggerTo = o.to; break;
                            }
                        }
        }

        MetaMods Meta(AbilityDef a) => meta.TryGetValue(a, out var m) ? m : new MetaMods();

        /// <summary>Tags of an ability including tags given by RetagAbilities (used by build.tag and other meta).</summary>
        public IEnumerable<string> TagsOf(AbilityDef a) => a.tags.Concat(Meta(a).extraTags);

        /// <summary>Damage multiplier for an effect carrying these tags (AmplifyTagged by tag also applies to weapon basic attacks).</summary>
        public float SynergyMul(string tagsSpaceSeparated)
        {
            if (string.IsNullOrEmpty(tagsSpaceSeparated)) return 1;
            var tags = tagsSpaceSeparated.Split(' ');
            float m = 1;
            // Ability effects: the per-ability meta (any selector); weapon/other effects: tag-selected AmplifyTagged only
            string id = tags.FirstOrDefault(t => t.StartsWith("ABILITY:"));
            var self = id != null ? Owned.FirstOrDefault(x => "ABILITY:" + x.id == id) : null;
            if (self != null) return Meta(self).amp * UpgradeOf(self);
            foreach (var a in Owned)
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Meta && (string.IsNullOrEmpty(r.select) || r.select == "ByTag")))
                    foreach (var o in r.ops)
                        if (o.op == "AmplifyTagged" && RuleLanguage.TagMatches(tags, string.IsNullOrEmpty(r.selectArg) ? o.tag : r.selectArg)) m *= 1 + o.amount;
            return m;
        }

        // ───────────────────────── Replace / Constrain ─────────────────────────

        IEnumerable<(AbilityDef a, RuleDef r)> RulesOf(string kind) => Owned.SelectMany(a => a.rules.Where(r => r.kind == kind).Select(r => (a, r)));

        /// <summary>Constraint: is this action forbidden ("Dash" | "Potion" | "Regen" | "Skill")?</summary>
        public bool Forbids(string action) => RulesOf(RuleKind.Constrain).Any(x => x.r.ops.Any(o => o.op == "Forbid" && o.mode == action));

        /// <summary>Replacement of the dash. Returns true when the input was consumed.</summary>
        public bool ReplaceDash(Vector2 dir) => Replace("Dash", new CombatEvent { type = Trig.Dash, source = p, position = p.Pos, direction = dir });

        public bool ReplacePotion() => Replace("Potion", new CombatEvent { source = p, position = p.Pos });

        bool Replace(string action, CombatEvent ev)
        {
            var hit = RulesOf(RuleKind.Replace).FirstOrDefault(x => x.r.replaces == action);
            if (hit.r == null) return false;
            foreach (var o in hit.r.ops) ReplaceWith(hit.a, o, ev);
            Dispatch(action == "Dash" ? "Dash" : "Healed", ev);     // the replaced action still counts as that event
            return true;
        }

        void ReplaceWith(AbilityDef a, OpDef o, CombatEvent ev)
        {
            var dir = ev.direction == Vector2.zero ? p.Facing : ev.direction;
            var proxy = new OpDef { op = o.mode, amount = o.amount, element = o.element, entity = o.entity, radius = o.radius > 0 ? o.radius : 2.2f, duration = o.duration > 0 ? o.duration : 3, count = Mathf.Max(1, o.count), at = "Self", mode = o.mode == "Projectile" ? "Fan" : null };
            switch (o.mode)
            {
                case "Blink": p.LastDashOrigin = p.Pos; p.BlinkTo(p.Pos + dir * 4f); break;
                case "Nova": case "Shield": case "Spawn": case "Projectile": Apply(a, null, proxy, ev, 1); break;
            }
        }

        /// <summary>Called by the combo finisher: a Replace rule on "ComboFinisher" adds its ops to the last hit.</summary>
        public void ComboFinisher(Vector2 dir)
        {
            var ev = new CombatEvent { source = p, position = p.Pos, direction = dir };
            foreach (var (a, r) in RulesOf(RuleKind.Replace).Where(x => x.r.replaces == "ComboFinisher").ToList())
                foreach (var o in r.ops) ReplaceWith(a, o, ev);
        }

        // ───────────────────────── State ─────────────────────────

        Dictionary<string, float> S(AbilityDef a)
        {
            if (!states.TryGetValue(a, out var d)) states[a] = d = new Dictionary<string, float>();
            return d;
        }

        public float GetState(AbilityDef a, string name) => S(a).TryGetValue(name, out var v) ? v : 0;

        void SetState(AbilityDef a, string name, float v, int depth)
        {
            var def = a.State(name);
            if (def == null) return;
            float before = GetState(a, name);
            v = Mathf.Clamp(v, 0, def.max);
            S(a)[name] = v;
            if (before < def.max && v >= def.max) FireStateRules(a, "StateFull", name, depth);
            if (before > 0 && v <= 0) FireStateRules(a, "StateEmpty", name, depth);
        }

        void FireStateRules(AbilityDef a, string trigger, string name, int depth)
        {
            foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger && r.trigger == trigger && r.param == name).ToList())
                TryRun(a, r, new CombatEvent { position = p.Pos, depth = depth + 1 });
        }

        // ───────────────────────── Tick / continuous / ambient triggers ─────────────────────────

        public void Moved(float meters)
        {
            moveAcc += meters;
            stillT = 0;
            if (moveAcc >= 6) { moveAcc -= 6; Dispatch("Moved", new CombatEvent { position = p.Pos, tags = "" }); }
        }

        public void Tick(float dt)
        {
            // State dynamics: decay, charge regen, timers
            foreach (var a in Owned)
                foreach (var s in a.states)
                {
                    float v = GetState(a, s.name);
                    if (s.type == "Timer") { if (v > 0) SetState(a, s.name, v - dt, 0); continue; }
                    if (s.type == "Charge" && s.regen > 0 && v < s.max) SetState(a, s.name, v + s.regen * dt, 0);
                    else if (s.decay > 0 && v > 0) SetState(a, s.name, v - s.decay * dt, 0);
                }

            // Periodic triggers
            foreach (var a in Owned)
                foreach (var r in a.rules)
                {
                    if (r.kind != RuleKind.Trigger || r.trigger != "Every") continue;
                    float period = Mathf.Max(1, r.period * (1 - Meta(a).haste));
                    if (!nextPeriodic.TryGetValue(r, out var next)) { nextPeriodic[r] = Time.time + period; continue; }
                    if (Time.time < next) continue;
                    nextPeriodic[r] = Time.time + period;
                    TryRun(a, r, new CombatEvent { position = p.Pos });
                }

            // Combat start / end, stillness, position trail
            bool enemyNear = Actor.Nearest(p.Pos, inCombat ? 10 : 8, Team.Enemy) != null;
            if (enemyNear) { noEnemyT = 0; if (!inCombat) { inCombat = true; combatStartAt = Time.time; hitOnce.Clear(); Raise("CombatStart"); } }
            else if (inCombat && (noEnemyT += dt) >= 3) { inCombat = false; Raise("CombatEnd"); }
            if (!p.Moving && !p.IsDashing) stillT += dt;
            trail.Enqueue((Time.time, p.Pos));
            while (trail.Count > 0 && Time.time - trail.Peek().t > 2.05f) trail.Dequeue();

            // Armed temporals expire
            armed.RemoveAll(x => Time.time > x.until);

            // Continuous rules: conditions change over time (HP, moving, enemies near…) → recompute stats when the result changes
            continuousT -= dt;
            if (continuousT <= 0)
            {
                continuousT = 0.25f;
                var now = EvalContinuousStats();
                if (!SameStats(now, continuousStats))
                {
                    continuousStats.Clear();
                    foreach (var kv in now) continuousStats[kv.Key] = kv.Value;
                    p.RecalcStats();
                }
                // Auras tick here too (2× per second at half strength)
                AuraTick(0.5f);
            }
        }

        float auraAcc;
        void AuraTick(float step)
        {
            auraAcc += 0.25f;
            if (auraAcc < step) return;
            auraAcc = 0;
            foreach (var a in Owned)
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Continuous))
                {
                    if (!r.ops.Any(o => o.op == "Aura")) continue;
                    var ev = new CombatEvent { position = p.Pos };
                    if (!CheckConditions(a, r, ev, false)) continue;
                    foreach (var o in r.ops.Where(o => o.op == "Aura"))
                    {
                        var el = RuleLanguage.ParseElement(o.element);
                        var col = el != Element.None ? Combat.ElementColor(el) : Pal.White;
                        float rad = o.radius * (1 + Meta(a).enlarge);
                        foreach (var t in Actor.InRadius(p.Pos, rad, Team.Enemy).Take(Cfg.maxTargetsPerProc).ToList())
                        {
                            var d = new DamageInfo { amount = p.AttackPower * o.amount * step * UpgradeOf(a), source = p, direction = t.Pos - p.Pos, element = el, tags = string.Join(" ", a.tags) + " ABILITY:" + a.id, fromAbility = true, depth = 1, noEvents = true, fxColor = col };
                            Combat.Hit(t, d, col);
                            if (!string.IsNullOrEmpty(o.status)) ApplyStatus(t, o.status, o.amount * 0.5f, 1.5f, false);
                        }
                    }
                }
        }

        Dictionary<string, float> EvalContinuousStats()
        {
            var d = new Dictionary<string, float>();
            var mins = new Dictionary<string, float>();
            var caps = new Dictionary<string, float>();
            foreach (var a in Owned)
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Continuous))
                {
                    if (!r.ops.Any(o => o.op == "ModifyStat")) continue;
                    var ev = new CombatEvent { position = p.Pos };
                    if (!CheckConditions(a, r, ev, false)) continue;
                    foreach (var o in r.ops.Where(o => o.op == "ModifyStat"))
                    {
                        float v = o.amount + o.per * Value(a, o.scale, ev);
                        if (RuleLanguage.StatInfo.TryGetValue(o.stat, out var si)) v = Mathf.Clamp(v, si.min, si.max);
                        switch (o.mode)
                        {
                            case "Min": mins[o.stat] = Mathf.Max(mins.TryGetValue(o.stat, out var m0) ? m0 : float.MinValue, v); break;
                            case "Max": caps[o.stat] = Mathf.Min(caps.TryGetValue(o.stat, out var c0) ? c0 : float.MaxValue, v); break;
                            default: d[o.stat] = (d.TryGetValue(o.stat, out var x) ? x : 0) + v; break;
                        }
                    }
                }
            foreach (var kv in mins) d["min:" + kv.Key] = kv.Value;
            foreach (var kv in caps) d["max:" + kv.Key] = kv.Value;
            return d;
        }

        static bool SameStats(Dictionary<string, float> a, Dictionary<string, float> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a) if (!b.TryGetValue(kv.Key, out var v) || Mathf.Abs(v - kv.Value) > 0.004f) return false;
            return true;
        }

        public void ApplyStats(Stats s)
        {
            foreach (var kv in continuousStats)
            {
                if (kv.Key.StartsWith("min:") || kv.Key.StartsWith("max:")) continue;
                var id = RuleText.ParseStat(kv.Key);
                bool mul = RuleLanguage.StatInfo.TryGetValue(kv.Key, out var si) && si.mul;
                if (mul) s.Mul(id, kv.Value); else s.Add(id, kv.Value);
            }
        }

        /// <summary>After all modifiers: floors (mode Min) / caps (mode Max, CapStat constraints) / overrides.</summary>
        public float ClampStat(StatId id, float value)
        {
            string key = id.ToString();
            if (continuousStats.TryGetValue("min:" + key, out var lo)) value = Mathf.Max(value, BaseRelative(id, lo));
            if (continuousStats.TryGetValue("max:" + key, out var hi)) value = Mathf.Min(value, BaseRelative(id, hi));
            foreach (var (a, r) in RulesOf(RuleKind.Constrain))
                foreach (var o in r.ops.Where(o => o.op == "CapStat" && o.stat == key)) value = Mathf.Min(value, BaseRelative(id, o.amount));
            foreach (var (a, r) in RulesOf(RuleKind.Continuous))
                foreach (var o in r.ops.Where(o => o.op == "ModifyStat" && o.mode == "Override" && o.stat == key)) value = BaseRelative(id, o.amount);
            return value;
        }

        /// <summary>Multiplicative stats express floors/caps as a fraction of the base (MaxHp 0.5 = half of base max HP).</summary>
        float BaseRelative(StatId id, float v) =>
            RuleLanguage.StatInfo.TryGetValue(id.ToString(), out var si) && si.mul ? p.Stats.GetBase(id) * (1 + v) : v;

        /// <summary>Player.DamageMultiplierAgainst: tag synergy × AmplifyDamage rules that hold for this target.</summary>
        public float DamageMultiplier(Actor target, string tags)
        {
            float m = SynergyMul(tags);
            var hitTags = string.IsNullOrEmpty(tags) ? new string[0] : tags.Split(' ');
            foreach (var a in Owned)
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Continuous && r.ops.Any(o => o.op == "AmplifyDamage" || o.op == "Convert" && o.from == "Shield")))
                {
                    var ev = new CombatEvent { target = target, position = target != null ? target.Pos : p.Pos, tags = tags };
                    if (!CheckConditions(a, r, ev, false, hitTags)) continue;
                    foreach (var o in r.ops.Where(o => o.op == "AmplifyDamage")) m *= 1 + Mathf.Max(0, o.amount + o.per * Value(a, o.scale, ev));
                    foreach (var o in r.ops.Where(o => o.op == "Convert" && o.from == "Shield" && o.to == "Damage")) m *= 1 + o.amount * Value(a, "self.shieldPct", ev);
                }
            float status = StatusPotency(null);
            if (status > 0 && hitTags.Contains("STATUS")) m *= 1 + status;
            // MoveDistance→Damage: basic attacks (not ability effects) gain ratio × metres walked in the last 3 s
            float move = ConvertRatio("MoveDistance>Damage");
            if (move > 0 && !hitTags.Any(t => t.StartsWith("ABILITY:"))) m *= 1 + move * Mathf.Min(20, PathLength());
            return m;
        }

        /// <summary>Called by Player.TakeDamage: Resist rules (damage taken ×(1-amount)) and DamageTaken→Shield.</summary>
        public float IncomingMultiplier(Actor attacker, float amount)
        {
            float m = 1;
            foreach (var a in Owned)
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Continuous && r.ops.Any(o => o.op == "Resist")))
                {
                    var ev = new CombatEvent { target = attacker, position = p.Pos };
                    if (!CheckConditions(a, r, ev, false)) continue;
                    foreach (var o in r.ops.Where(o => o.op == "Resist")) m *= 1 - Mathf.Clamp(o.amount + o.per * Value(a, o.scale, ev), 0, 0.6f);
                }
            float back = ConvertRatio("DamageTaken>Shield");
            if (back > 0) p.AddShield(Mathf.Min(p.maxHp * 0.5f, p.Shield + amount * m * back), 5, false);
            return m;
        }

        float StatusPotency(string status)
        {
            float v = 0;
            foreach (var (a, r) in RulesOf(RuleKind.Continuous))
                foreach (var o in r.ops.Where(o => o.op == "StatusPotency" && (string.IsNullOrEmpty(o.status) || o.status == status || status == null)))
                    v += o.amount;
            return v;
        }

        // ───────────────────────── Conversions (rule redefinition hooks) ─────────────────────────

        float ConvertRatio(string pair)
        {
            float r = 0;
            foreach (var a in Owned)
                foreach (var rule in a.rules.Where(x => x.kind == RuleKind.Continuous))
                    foreach (var o in rule.ops)
                        if (o.op == "Convert" && o.from + ">" + o.to == pair && CheckConditions(a, rule, new CombatEvent { position = p.Pos }, false))
                            r += o.amount;
            return r;
        }

        /// <summary>Called by Player.GainXp: Xp→Gold converts part of the experience into gold. Returns the XP kept.</summary>
        public float FilterXp(float amount)
        {
            float r = Mathf.Clamp01(ConvertRatio("Xp>Gold"));
            if (r <= 0) return amount;
            p.GainGold(Mathf.RoundToInt(amount * r));
            return amount * (1 - r * 0.5f);
        }

        /// <summary>Called by Actor.Heal for the player. Returns the HP that should actually be restored.</summary>
        public float FilterHeal(float amount, float missing)
        {
            float toShield = ConvertRatio("Heal>Shield");
            if (toShield > 0) { p.AddShield(Mathf.Min(p.maxHp, p.Shield + amount * toShield), 6, false); return 0; }
            float over = ConvertRatio("Overheal>Shield");
            if (over > 0 && amount > missing) p.AddShield(Mathf.Min(p.maxHp * 0.5f, p.Shield + (amount - missing) * over), 6, false);
            return amount;
        }

        // ───────────────────────── Events ─────────────────────────

        public void Raise(string trigger, Vector2? pos = null, string param = null)
        {
            if (trigger == "Pickup" && param == "gold") { float g = ConvertRatio("Gold>Heal"); if (g > 0) p.Heal(p.maxHp * 0.01f * g * 3); }
            Dispatch(trigger, new CombatEvent { position = pos ?? p.Pos, tags = param ?? "" });
        }

        /// <summary>Entry from GameEvents (the player's own events and its clones').</summary>
        public void Fire(CombatEvent ev)
        {
            if (ev.depth > Cfg.maxProcDepth) return;
            switch (ev.type)
            {
                case Trig.Damaged:
                    lastDamagedAt = Time.time;
                    if (ev.target != null) lastAttacker = ev.target;
                    FireArmed("UntilDamaged", ev);
                    break;
                case Trig.Attack: FireArmed("UntilNextAttack", ev); break;
                case Trig.Hit:
                {
                    float heal = ConvertRatio("DamageDealt>Heal");
                    if (heal > 0 && ev.amount > 0) p.Heal(ev.amount * heal);
                    if (ev.target != null) lastHit = ev.target;
                    bool own = ev.source == p && ev.tags != null && !ev.tags.Contains("ABILITY:");
                    if (own) { ConsumeEmpower(ev); FireArmed("UntilHit", ev); FireArmed("ForNextN", ev); }
                    if (ev.target != null && hitOnce.Add(ev.target)) { var first = ev; first.type = Trig.Hit; Dispatch("FirstHit", first); }
                    break;
                }
                case Trig.Crit:
                {
                    float heal = ConvertRatio("Crit>Heal");
                    if (heal > 0 && ev.amount > 0) p.Heal(ev.amount * heal);
                    break;
                }
                case Trig.Kill:
                {
                    lastKillAt = Time.time;
                    if (delayedKills > 0 && --delayedKills == 0) PayDelayedReward();
                    bool elite = ev.target is Enemy e && e.def != null && (e.def.elite || e.def.boss);
                    if (elite) Dispatch("EliteKill", ev);
                    float over = ev.target != null ? -ev.target.hp : 0;       // hp is clamped to 0, so use amount vs maxHp instead
                    if (ev.target != null && ev.amount > ev.target.maxHp * 0.5f && ev.amount > p.AttackPower * 0.5f)
                    {
                        var ok = ev; ok.amount = ev.amount; Dispatch("Overkill", ok);
                        float nova = ConvertRatio("Overkill>Nova");
                        if (nova > 0) ConvertNova(ev.position, ev.amount * 0.5f * nova, ev.depth);
                    }
                    break;
                }
            }
            if (ev.type is Trig.Interval or Trig.None) return;
            lastTrigger[ev.type.ToString()] = Time.time;
            Dispatch(ev.type.ToString(), ev);
            if (ev.target != null) lastEventTarget = ev.target;
        }

        void ConvertNova(Vector2 at, float amount, int depth)
        {
            Fx.I?.Play(Art.Shockwave(Pal.Blood, 30), at, 0, 22, 1, null, true);
            foreach (var t in Actor.InRadius(at, 2f, Team.Enemy).Take(Cfg.maxTargetsPerProc).ToList())
                Combat.Hit(t, new DamageInfo { amount = amount, source = p, direction = t.Pos - at, knockback = 2, canCrit = false, tags = "AREA", depth = depth + 1, fromAbility = true }, Pal.Blood, "hit");
        }

        void PayDelayedReward()
        {
            foreach (var o in SystemOps.Where(o => o.op == "DelayedReward"))
            {
                p.GainGold(Mathf.RoundToInt(o.amount));
                p.PendingEvolutions++;
                GameEvents.Notify("기다린 보상이 부화했다!");
            }
        }

        void Dispatch(string trigger, CombatEvent ev)
        {
            for (int i = 0; i < Owned.Count; i++)
            {
                var a = Owned[i];
                var m = Meta(a);
                foreach (var r in a.rules)
                {
                    if (r.kind != RuleKind.Trigger) continue;
                    // RetriggerTagged: this ability's rules listening to `from` now listen to `to`
                    string listens = m.retriggerFrom != null && r.trigger == m.retriggerFrom ? m.retriggerTo : r.trigger;
                    if (listens != trigger) continue;
                    if (!string.IsNullOrEmpty(r.param) && trigger is "StatusApplied" or "StatusExpired" or "EntityExpired" or "Pickup"
                        && !string.Equals(ev.tags, r.param, System.StringComparison.OrdinalIgnoreCase)) continue;
                    // An ability never re-triggers itself through its own effects (NoFreeRecursion)
                    if (ev.tags != null && ev.tags.Contains("ABILITY:" + a.id)) continue;
                    TryRun(a, r, ev);
                }
            }
        }

        void TryRun(AbilityDef a, RuleDef r, CombatEvent ev)
        {
            if (ev.depth > Cfg.maxProcDepth) return;
            if (Time.frameCount != frame) { frame = Time.frameCount; procsThisFrame = 0; }
            if (procsThisFrame >= Cfg.maxProcsPerFrame) return;            // ProcBudget
            var m = Meta(a);
            if (m.gate > 0 && p.hp / Mathf.Max(1, p.maxHp) < m.gate) return;
            float cd = Mathf.Max(Cfg.minInternalCooldown, r.cooldown * (1 - m.haste));
            if (lastFire.TryGetValue(r, out var last) && Time.time - last < cd) return;
            if (!CheckConditions(a, r, ev, true)) return;
            lastFire[r] = Time.time;
            procsThisFrame++;
            fireCount++;
            float scale = UpgradeOf(a) * (m.gate > 0 ? 1.6f : 1);
            p.StartCoroutine(Run(a, r, ev, scale));
            if (m.repeat > 0) p.StartCoroutine(Run(a, r, ev, scale * m.repeat, 0.3f));
        }

        // ───────────────────────── Conditions & values ─────────────────────────

        bool CheckConditions(AbilityDef a, RuleDef r, CombatEvent ev, bool consumesRandom, string[] hitTags = null)
        {
            var m = Meta(a);
            foreach (var c in r.when)
            {
                switch (c.type)
                {
                    case "Chance":
                        if (m.unchain) break;
                        if (!consumesRandom || Random.value > Mathf.Min(0.95f, c.value * (1 + m.haste))) return false;
                        break;
                    case "EveryNth":
                        if (m.unchain) break;
                        if (!consumesRandom) return false;
                        nth[r] = (nth.TryGetValue(r, out var n) ? n : 0) + 1;
                        if (nth[r] % Mathf.Max(2, (int)c.value) != 0) return false;
                        break;
                    case "TargetHas": if (ev.target == null || !ev.target.HasStatus(c.text)) return false; break;
                    case "TargetLacks": if (ev.target == null || ev.target.HasStatus(c.text)) return false; break;
                    case "TargetIs": if (!TargetIs(ev.target, c.text)) return false; break;
                    case "EventTag":
                        if (hitTags != null) { if (!RuleLanguage.TagMatches(hitTags, c.text)) return false; }
                        else if (string.IsNullOrEmpty(ev.tags) || !RuleLanguage.TagMatches(ev.tags.Split(' '), c.text)) return false;
                        break;
                    case "HasEntity": if (!HasEntity(c.text)) return false; break;
                    case "NoEntity": if (HasEntity(c.text)) return false; break;
                    case "WithinTime": if (!lastTrigger.TryGetValue(c.text ?? "", out var lt) || Time.time - lt > c.value) return false; break;
                    case "SameTarget": if (ev.target == null || ev.target != lastEventTarget) return false; break;
                    case "DifferentTarget": if (ev.target == null || ev.target == lastEventTarget) return false; break;
                    case "DominantElement": if (DominantElement() != c.text) return false; break;
                    case "Compare":
                        if ((c.a ?? "").StartsWith("target.") && ev.target == null) return false;
                        if (!Holds(Value(a, c.a, ev), c.cmp, c.value)) return false;
                        break;
                }
            }
            return true;
        }

        static bool TargetIs(Actor t, string what)
        {
            if (!(t is Enemy e) || e.def == null) return false;
            return what switch
            {
                "Elite" => e.def.elite, "Boss" => e.def.boss, "Normal" => !e.def.elite && !e.def.boss,
                "Ranged" => e.def.brain is EnemyBrain.Ranged or EnemyBrain.Caster, "Melee" => e.def.brain is not (EnemyBrain.Ranged or EnemyBrain.Caster),
                _ => false,
            };
        }

        bool HasEntity(string entity)
        {
            if (string.IsNullOrEmpty(entity)) return LiveSpawns > 0;
            if (entity is "Clone" or "PhantomWeapon") return Clone.Active.Any(c => c != null && c.name == entity);
            return Summon.Active.Any(s => s != null && s.KindName == entity);
        }

        string DominantElement() =>
            Owned.SelectMany(x => x.rules).SelectMany(r => r.ops).Select(o => o.element).Where(RuleLanguage.IsElement)
                .GroupBy(e => e).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? p.Weapon.element.ToString();

        static bool Holds(float x, string cmp, float v) => cmp switch
        {
            ">=" => x >= v - 1e-4f, ">" => x > v, "<=" => x <= v + 1e-4f, "<" => x < v, "==" => Mathf.Abs(x - v) < 0.01f, _ => false,
        };

        float Value(AbilityDef a, string reference, CombatEvent ev)
        {
            if (string.IsNullOrEmpty(reference)) return 0;
            if (reference.StartsWith("state:")) return GetState(a, reference.Substring(6));
            if (reference.StartsWith("build.tag:"))
            {
                string tag = reference.Substring(10);
                return Owned.Count(x => RuleLanguage.TagMatches(TagsOf(x), tag)) + (RuleLanguage.TagMatches((p.Weapon.tags ?? "").Split(' '), tag) ? 1 : 0);
            }
            if (reference.StartsWith("build.element:"))
            {
                string el = reference.Substring(14);
                return Owned.Count(x => x.rules.SelectMany(r => r.ops).Any(o => o.element == el)) + (p.Weapon.element.ToString() == el ? 1 : 0);
            }
            if (reference.StartsWith("build.tier:") && int.TryParse(reference.Substring(11), out var tier)) return Owned.Count(x => x.tier == tier);
            switch (reference)
            {
                case "self.hpPct": return p.maxHp > 0 ? p.hp / p.maxHp : 1;
                case "self.missingHpPct": return p.maxHp > 0 ? 1 - p.hp / p.maxHp : 0;
                case "self.shieldPct": return p.maxHp > 0 ? p.Shield / p.maxHp : 0;
                case "self.moving": return p.Moving || p.IsDashing ? 1 : 0;
                case "self.stillTime": return Mathf.Min(10, stillT);
                case "self.moveDistance": return trail.Count < 2 ? 0 : Mathf.Min(20, PathLength());
                case "self.dashCharges": return p.DashCharges;
                case "self.gold": return p.Gold / 100f;
                case "self.potions": return p.Potions;
                case "self.comboStep": return p.InCombo ? 1 : 0;
                case "enemies.near": return Actor.InRadius(p.Pos, 4, Team.Enemy).Count();
                case "enemies.far": return Actor.InRadius(p.Pos, 9, Team.Enemy).Count(x => (x.Pos - p.Pos).sqrMagnitude > 16);
                case "target.hpPct": return ev.target != null && ev.target.maxHp > 0 ? ev.target.hp / ev.target.maxHp : 1;
                case "target.distance": return ev.target != null ? Vector2.Distance(ev.target.Pos, p.Pos) : 0;
                case "target.statuses": return ev.target != null ? ev.target.statuses.Count : 0;
                case "event.amount":
                    return ev.type switch
                    {
                        Trig.Damaged => p.maxHp > 0 ? ev.amount / p.maxHp : 0,
                        Trig.Healed => p.maxHp > 0 ? ev.amount / p.maxHp : 0,
                        _ => p.AttackPower > 0 ? ev.amount / p.AttackPower : 0,
                    };
                case "time.sinceDamaged": return Mathf.Min(30, Time.time - lastDamagedAt);
                case "time.sinceKill": return Mathf.Min(30, Time.time - lastKillAt);
                case "time.inCombat": return inCombat ? Mathf.Min(60, Time.time - combatStartAt) : 0;
                case "entities.count": return LiveSpawns;
                case "build.elements": return PowerModel.DistinctElements(Owned);
                case "build.abilities": return Owned.Count;
                case "run.level": return p.Level;
                case "run.kills": return p.Kills / 100f;
                case "run.bossKills": return Game.I != null && Game.I.World != null ? Game.I.World.bossKills : 0;
            }
            return 0;
        }

        float PathLength()
        {
            float d = 0; Vector2? prev = null;
            foreach (var (t, pos) in trail) { if (Time.time - t > 3) continue; if (prev.HasValue) d += Vector2.Distance(prev.Value, pos); prev = pos; }
            return d;
        }

        // ───────────────────────── Execution: temporal layer ─────────────────────────

        IEnumerator Run(AbilityDef a, RuleDef r, CombatEvent ev, float scale, float extraDelay = 0)
        {
            if (extraDelay > 0) yield return new WaitForSeconds(extraDelay);
            foreach (var o in r.ops)
            {
                if (o.delay > 0) yield return new WaitForSeconds(o.delay);
                if (p == null || !p.Alive) yield break;
                switch (o.time)
                {
                    case "UntilHit": case "UntilDamaged": case "UntilNextAttack": case "ForNextN":
                        armed.Add(new Armed { a = a, r = r, o = o, scale = scale, on = o.time, uses = o.time == "ForNextN" ? Mathf.Max(1, o.times) : 1, until = Time.time + Mathf.Clamp(o.duration > 0 ? o.duration : 6, 1, Cfg.maxArmedSeconds) });
                        Fx.I?.Burst(p.Pos + Vector2.up * 0.6f, Pal.Gold, 5, 2, 0.4f, -2);
                        continue;
                    case "Periodic":
                        p.StartCoroutine(Periodic(a, r, o, ev, scale));
                        continue;
                }
                Apply(a, r, o, ev, scale);
            }
        }

        IEnumerator Periodic(AbilityDef a, RuleDef r, OpDef o, CombatEvent ev, float scale)
        {
            int ticks = Mathf.Clamp(o.times <= 0 ? 3 : o.times, 2, Cfg.maxTemporalTicks);
            float gap = Mathf.Max(0.25f, (o.duration > 0 ? o.duration : 3) / ticks);
            for (int i = 0; i < ticks; i++)
            {
                if (p == null || !p.Alive) yield break;
                Apply(a, r, o, ev, scale);
                yield return new WaitForSeconds(gap);
            }
        }

        void FireArmed(string on, CombatEvent ev)
        {
            for (int i = armed.Count - 1; i >= 0; i--)
            {
                var x = armed[i];
                if (x.on != on) continue;
                Apply(x.a, x.r, x.o, ev, x.scale);
                if (--x.uses <= 0) armed.RemoveAt(i);
            }
        }

        // ───────────────────────── Execution: positions & selectors ─────────────────────────

        Vector2 At(AbilityDef a, OpDef o, CombatEvent ev)
        {
            switch (o.at)
            {
                case "Self": case "Ring": return p.Pos;
                case "TargetPos": return ev.target != null ? ev.target.Pos : (Actor.Nearest(p.Pos, 7, Team.Enemy)?.Pos ?? p.Pos);
                case "BehindTarget": return ev.target != null ? (Vector2)ev.target.Pos + ((Vector2)ev.target.Pos - p.Pos).SafeNormal(p.Facing) : p.Pos + p.Facing * 2;
                case "DashOrigin": return p.LastDashOrigin;
                case "DashPath": return (p.LastDashOrigin + p.Pos) * 0.5f;
                case "Cursor": return p.AimPoint;
                case "Forward": return p.Pos + (p.AimPoint - p.Pos).SafeNormal(p.Facing) * 2;
                case "Behind": return p.Pos - p.Facing * 1.5f;
                case "RandomNear": return p.Pos + Random.insideUnitCircle * 4;
                case "LastPosition": return trail.Count > 0 ? trail.Peek().pos : p.Pos;
                case "Stored": return positions.TryGetValue(a, out var d) && o.state != null && d.TryGetValue(o.state, out var v) ? v : p.Pos;
                default: return ev.position != Vector2.zero ? ev.position : ev.target != null ? ev.target.Pos : p.Pos;
            }
        }

        List<Actor> Select(AbilityDef a, OpDef o, CombatEvent ev, Vector2 at)
        {
            int cap = Cfg.maxTargetsPerProc;
            var list = new List<Actor>();
            IEnumerable<Actor> near(float r) => Actor.InRadius(at, r, Team.Enemy);
            void One(Actor x) { if (x != null && x.Alive && x.team == Team.Enemy) list.Add(x); }
            Vector2 aim = (p.AimPoint - p.Pos).SafeNormal(p.Facing);
            float len = o.radius > 0 ? o.radius : 4;
            switch (o.target)
            {
                case "EventTarget": One(ev.target); break;
                case "LastHitTarget": One(lastHit); break;
                case "LastAttacker": One(lastAttacker); break;
                case "NearestEnemy": One(Actor.Nearest(at, 6, Team.Enemy)); break;
                case "FarthestEnemy": One(near(9).OrderByDescending(x => (x.Pos - at).sqrMagnitude).FirstOrDefault()); break;
                case "RandomEnemy": { var all = near(6).ToList(); if (all.Count > 0) list.Add(all[Random.Range(0, all.Count)]); break; }
                case "LowestHpEnemy": One(near(7).OrderBy(x => x.hp / Mathf.Max(1, x.maxHp)).FirstOrDefault()); break;
                case "HighestHpEnemy": One(near(7).OrderByDescending(x => x.hp).FirstOrDefault()); break;
                case "EnemiesNear": list.AddRange(near(o.radius > 0 ? o.radius : 3).OrderBy(x => (x.Pos - at).sqrMagnitude).Take(cap)); break;
                case "EnemiesInCone": list.AddRange(new List<Actor>(Combat.Arc(p.Pos, aim, len, 90, Team.Enemy)).Take(cap)); break;
                case "EnemiesInLine": list.AddRange(new List<Actor>(Combat.Line(p.Pos, p.Pos + aim * len, 0.6f, Team.Enemy)).Take(cap)); break;
                case "MarkedEnemies": list.AddRange(near(8).Where(x => x.HasStatus("mark")).Take(cap)); break;
                case "StatusedEnemies": list.AddRange(near(8).Where(x => string.IsNullOrEmpty(o.status) ? x.statuses.Count > 0 : x.HasStatus(o.status)).Take(cap)); break;
                case "StoredTarget": if (targets.TryGetValue(a, out var d) && o.state != null && d.TryGetValue(o.state, out var t)) One(t); break;
            }
            return list;
        }

        IEnumerable<Component> Entities(AbilityDef a, string selector)
        {
            switch (selector)
            {
                case "OwnClones": return Clone.Active.Where(c => c != null).Cast<Component>().ToList();
                case "OwnSummons": return Summon.Active.Where(s => s != null).Cast<Component>().ToList();
                case "LastSpawned":
                    var l = new List<Component>();
                    if (lastSummon.TryGetValue(a, out var s0) && s0 != null) l.Add(s0);
                    if (lastClone.TryGetValue(a, out var c0) && c0 != null) l.Add(c0);
                    return l;
            }
            return new List<Component>();
        }

        int LiveSpawns => Clone.Active.Count(c => c != null) + Summon.Active.Count(s => s != null);

        // ───────────────────────── Execution: ops + relation layer ─────────────────────────

        void Apply(AbilityDef a, RuleDef r, OpDef o, CombatEvent ev, float scale, int cascade = 0)
        {
            var m = Meta(a);
            var el = RuleLanguage.ParseElement(o.element);
            if (el == Element.None && m.infuse != Element.None) el = m.infuse;
            if (o.rel == "Alternate")
            {
                int k = alternate.TryGetValue(o, out var n) ? n + 1 : 0;
                alternate[o] = k;
                if (k % 2 == 1) el = Opposite(el);
            }
            Color32 col = el != Element.None ? Combat.ElementColor(el) : p.Weapon.slashColor;
            string tags = string.Join(" ", a.tags) + " ABILITY:" + a.id;
            // Tag amplification (meta) is applied by Combat.Hit through the effect's tags, so damage uses only `scale` here;
            // everything that does not go through a hit (heal, shield, buff, statuses…) gets the meta amp directly.
            float baseMag = o.amount + o.per * Value(a, o.scale, ev);
            var prim = RuleLanguage.Get(o.op);
            if (!string.IsNullOrEmpty(o.scale) && prim != null && prim.scalable == "amount" && prim.max > 0) baseMag = Mathf.Min(baseMag, prim.max * 1.5f);
            float mag = baseMag * m.amp * scale, magD = baseMag * scale;
            float dur = o.duration * (1 + m.extend);
            float rad = o.radius * (1 + m.enlarge);
            int cnt = Mathf.Max(1, o.count) + m.multiply;
            float atk = p.AttackPower;
            int depth = ev.depth + 1;
            Vector2 at = At(a, o, ev);
            if (o.at == "EntityPos" && !string.IsNullOrEmpty(o.target))
            {
                // Area ops centred on each of your entities
                var ents = Entities(a, o.target).ToList();
                if (ents.Count > 0 && o.op is "Nova" or "Pull" or "Push" or "Projectile" or "Taunt")
                {
                    var o2 = CopyOp(o); o2.at = "EventPos"; o2.target = null;
                    foreach (var e in ents.Take(Cfg.maxSpawnPerOwner)) { var e2 = ev; e2.position = e.transform.position; Apply(a, r, o2, e2, scale, cascade); }
                    return;
                }
            }

            var killed = new List<Actor>();
            DamageInfo D(float mult, Vector2 dir) => new DamageInfo
            {
                amount = atk * mult, source = p, direction = dir, knockback = 2.5f, hitstop = 0.03f, canCrit = true,
                element = el, tags = tags, depth = depth, fromAbility = true,
            };
            void HitOne(Actor t, float mult, string sfx = "hit_slice")
            {
                Combat.Hit(t, D(mult, t.Pos - at), col, sfx, 0.1f, 1.5f);
                Combat.ApplyElement(t, el, atk * mult, p, depth);
                if (!t.Alive) killed.Add(t);
            }
            List<Actor> damaged = new List<Actor>();

            switch (o.op)
            {
                case "Damage":
                    foreach (var t in Select(a, o, ev, at))
                    {
                        Fx.I?.Play(Art.Slash(col, Pal.White, 14, 200, 1), t.Pos + Vector2.up * 0.4f, Random.Range(0, 360f), 30, 1, null, true);
                        HitOne(t, magD); damaged.Add(t);
                    }
                    break;
                case "Drain":
                    foreach (var t in Select(a, o, ev, at))
                    {
                        float dealt = Combat.Hit(t, D(magD, t.Pos - p.Pos), Pal.Blood, "hit_slice", 0.08f, 1.5f);
                        p.Heal(dealt * 0.4f);
                        Fx.I?.Burst(t.Pos + Vector2.up * 0.4f, Pal.Blood, 6, 3, 0.4f);
                        damaged.Add(t);
                        if (!t.Alive) killed.Add(t);
                    }
                    break;
                case "Nova":
                    if (o.at == "DashPath")
                    {
                        foreach (var t in new List<Actor>(Combat.Line(p.LastDashOrigin, p.Pos, Mathf.Max(0.6f, rad * 0.4f), Team.Enemy)).Take(Cfg.maxTargetsPerProc)) { HitOne(t, magD, "hit"); damaged.Add(t); }
                        Fx.I?.Play(Art.Thrust(col, Pal.White, Mathf.Max(8, Mathf.RoundToInt(Vector2.Distance(p.LastDashOrigin, p.Pos) * Art.PPU))), p.LastDashOrigin, (p.Pos - p.LastDashOrigin).Angle(), 30, 1, null, true);
                        break;
                    }
                    Fx.I?.Play(Art.Shockwave(col, Mathf.RoundToInt(rad * Art.PPU)), at, 0, 22, 1, null, true);
                    Fx.I?.Burst(at, col, 16, 9, 0.45f);
                    CameraRig.I?.Shake(0.18f);
                    Sfx.Play("explode", 0.5f);
                    foreach (var t in Actor.InRadius(at, rad, Team.Enemy).Take(Cfg.maxTargetsPerProc).ToList()) { HitOne(t, magD, "hit"); damaged.Add(t); }
                    break;
                case "Slash":
                {
                    Vector2 aim = (p.AimPoint - p.Pos).SafeNormal(p.Facing);
                    Fx.I?.Play(Art.Slash(col, Pal.White, Mathf.RoundToInt(rad * Art.PPU * 0.85f), 150, 1), p.Pos + Vector2.up * 0.45f, aim.Angle(), 30, 1, null, true);
                    foreach (var t in new List<Actor>(Combat.Arc(p.Pos + Vector2.up * 0.45f, aim, rad, 150, Team.Enemy)).Take(Cfg.maxTargetsPerProc)) { HitOne(t, magD); damaged.Add(t); }
                    break;
                }
                case "Beam":
                {
                    Vector2 aim = (p.AimPoint - p.Pos).SafeNormal(p.Facing);
                    Vector2 from = p.Pos + Vector2.up * 0.45f, to = from + aim * rad;
                    Fx.I?.Play(Art.Thrust(col, Pal.White, Mathf.RoundToInt(rad * Art.PPU)), from, aim.Angle(), 30, 1, null, true);
                    Sfx.Play("thunder", 0.35f, 1.4f);
                    foreach (var t in new List<Actor>(Combat.Line(from, to, 0.6f, Team.Enemy)).Take(Cfg.maxTargetsPerProc)) { HitOne(t, magD, "hit"); damaged.Add(t); }
                    break;
                }
                case "Projectile":
                {
                    int n = cnt;
                    Vector2 origin = at + Vector2.up * 0.45f;
                    Vector2 aim = (p.AimPoint - origin).SafeNormal(p.Facing);
                    bool ring = o.mode == "Ring" || o.at == "Ring";
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 dir = ring ? MathX.Dir(360f / n * i + Random.Range(0, 20f)) : aim.Rotate((i - (n - 1) / 2f) * 12f);
                        if (o.rel == "Mirror" && i % 2 == 1) dir = -dir;
                        var pr = Projectile.Spawn(el != Element.None ? Art.Orb(Pal.White, col, 8) : Art.Knife(), origin, dir * 12f, Team.Enemy, p, D(magD, dir), el != Element.None);
                        pr.color = col; pr.life = 0.9f; pr.spin = el == Element.None;
                        pr.homing = o.mode == "Seek" || o.rel == "Seek" ? 6 : 1.5f;
                        if (o.rel == "Pierce") pr.pierce = Mathf.Clamp(o.times <= 0 ? 2 : o.times, 1, Cfg.maxTemporalTicks);
                        if (o.rel == "Bounce") Bounce(pr, Mathf.Clamp(o.times <= 0 ? 2 : o.times, 1, Cfg.maxTemporalTicks));
                        if (o.rel == "Return") ReturnHome(pr);
                        if (o.rel == "Split") SplitOnHit(pr, Mathf.Clamp(o.times <= 0 ? 3 : o.times, 2, 5), col, magD * 0.4f, tags, depth);
                        pr.onEnd += x => p.Abilities?.Raise("ProjectileEnd", x.transform.position);
                    }
                    Sfx.Play("shoot", 0.4f);
                    break;
                }
                case "Chain":
                {
                    var t = Select(a, o, ev, at).FirstOrDefault();
                    var hit = new HashSet<Actor>();
                    for (int i = 0; i < cnt && t != null; i++)
                    {
                        hit.Add(t);
                        Fx.I?.Play(Art.LightningBolt(64), t.Pos, 0, 22, 1, null, true);
                        Combat.Hit(t, D(magD * (i == 0 ? 1 : 0.75f), Vector2.down), Pal.Lightning, "thunder", 0.12f, 1.5f);
                        if (!t.Alive) killed.Add(t);
                        Actor next = null; float bd = 16;
                        foreach (var x in Actor.All) if (x != null && x.Alive && x.team == Team.Enemy && !hit.Contains(x)) { float d = (x.Pos - t.Pos).sqrMagnitude; if (d < bd) { bd = d; next = x; } }
                        t = next;
                    }
                    break;
                }
                case "ApplyStatus":
                {
                    string st = o.rel == "Alternate" && alternate.TryGetValue(o, out var alt) && alt % 2 == 1 ? OppositeStatus(o.status) : o.status;
                    foreach (var t in Select(a, o, ev, at)) { ApplyStatus(t, st, mag, dur, o.time == "Stack"); damaged.Add(t); }
                    break;
                }
                case "Slow":
                    foreach (var t in Select(a, o, ev, at)) { t.AddStatus("chill", mag, 0, 0.6f, p, Pal.Frost); damaged.Add(t); }
                    break;
                case "Detonate":
                    foreach (var t in Select(a, o, ev, at))
                    {
                        var st = t.statuses.FirstOrDefault(s => s.id == o.status);
                        if (st == null) continue;
                        float bonus = 1 + Mathf.Max(0, st.time) * 0.2f;
                        t.statuses.Remove(st);
                        Fx.I?.Play(Art.Shockwave(StatusColor(o.status), 18), t.Pos, 0, 24, 1, null, true);
                        Sfx.Play("explode", 0.4f, 1.3f);
                        HitOne(t, magD * bonus, "hit_heavy"); damaged.Add(t);
                    }
                    break;
                case "Spread":
                {
                    var src = ev.target;
                    if (src == null) break;
                    var st = src.statuses.FirstOrDefault(s => s.id == o.status);
                    if (st == null) break;
                    foreach (var t in Nearby(src, Mathf.Max(2.5f, rad), cnt))
                    {
                        t.AddStatus(st.id, Mathf.Max(st.time, 2f), st.dps * m.amp * scale, st.slow, p, st.color);
                        Fx.I?.Burst(t.Pos + Vector2.up * 0.4f, st.color, 6, 3, 0.3f);
                    }
                    break;
                }
                case "Transfer":
                {
                    var src = ev.target ?? Select(a, new OpDef { target = "NearestEnemy" }, ev, at).FirstOrDefault();
                    if (src == null) break;
                    var mine = src.statuses.Where(s => s.source == p).ToList();
                    if (mine.Count == 0) break;
                    foreach (var t in Nearby(src, Mathf.Max(3, rad), cnt))
                        foreach (var s in mine) t.AddStatus(s.id, Mathf.Max(s.time, 1.5f), s.dps, s.slow, p, s.color);
                    foreach (var s in mine) src.statuses.Remove(s);
                    Fx.I?.Burst(src.Pos + Vector2.up * 0.4f, Pal.ShadowEl, 10, 4, 0.4f);
                    break;
                }
                case "Execute":
                    foreach (var t in Select(a, o, ev, at))
                    {
                        bool boss = t is Enemy e && e.def != null && e.def.boss;
                        if (!boss && t.hp / Mathf.Max(1, t.maxHp) <= o.amount)
                        {
                            Fx.I?.Number(t.Pos + Vector2.up * 1.1f, "처형", new Color(1f, 0.4f, 0.5f), 1.1f);
                            Combat.Hit(t, D(999, t.Pos - p.Pos), Pal.Blood, "hit_heavy", 0.25f, 3);
                            if (!t.Alive) killed.Add(t);
                        }
                        else if (boss) HitOne(t, 1.5f * scale, "hit_heavy");
                        damaged.Add(t);
                    }
                    break;
                case "Stun":
                    foreach (var t in Select(a, o, ev, at)) { if (t is Enemy e) e.Stun(mag); damaged.Add(t); }
                    break;
                case "Taunt":
                    Fx.I?.Play(Art.Shockwave(Pal.Gold, Mathf.RoundToInt(rad * Art.PPU)), at, 0, 16, 1, null, true);
                    foreach (var t in Actor.InRadius(at, rad, Team.Enemy).ToList()) if (t is Enemy e) e.Taunt(at, mag);
                    break;
                case "Pull":
                    Fx.I?.Play(Art.Shockwave(col, Mathf.RoundToInt(rad * Art.PPU)), at, 0, 18, 1, null, true);
                    foreach (var t in Actor.InRadius(at, rad, Team.Enemy).ToList()) t.Knockback(at - t.Pos, mag);
                    break;
                case "Push":
                    Fx.I?.Play(Art.Shockwave(Pal.White, Mathf.RoundToInt(rad * Art.PPU)), at, 0, 26, 1, null, true);
                    foreach (var t in Actor.InRadius(at, rad, Team.Enemy).ToList()) { t.Knockback(t.Pos - at, mag); Combat.Hit(t, D(0.15f, t.Pos - at), Pal.White, "hit_light", 0.06f, 1); }
                    break;
                case "Cleanse":
                    p.invulnUntil = Mathf.Max(p.invulnUntil, Time.time + Mathf.Clamp(mag, 0.1f, 1.2f));
                    p.statuses.Clear();
                    Fx.I?.Play(Art.Shockwave(Pal.Holy, 18), p.Pos, 0, 22, 1, null, true);
                    break;
                case "Heal":
                    p.Heal(p.maxHp * mag);
                    Fx.I?.Burst(p.Pos + Vector2.up * 0.5f, Pal.Hex("8bff9a"), 8, 3, 0.6f, -2);
                    break;
                case "Shield":
                    p.AddShield(o.time == "Stack" ? Mathf.Min(p.maxHp, p.Shield + p.maxHp * mag) : p.maxHp * mag, dur);
                    break;
                case "Buff": p.AddBuff(RuleText.ParseStat(o.stat), mag, dur); break;
                case "Blink":
                {
                    var t = string.IsNullOrEmpty(o.target) ? null : Select(a, o, ev, at).FirstOrDefault();
                    Vector2 dst = t != null ? (Vector2)t.Pos - (t.Pos - p.Pos).normalized * 0.8f : at;
                    p.BlinkTo(dst);
                    break;
                }
                case "Leap":
                {
                    Vector2 to = at - p.Pos;
                    if (to.sqrMagnitude < 0.25f) break;
                    p.SetDash(to.normalized * 18f, Mathf.Min(0.3f, to.magnitude / 18f));
                    break;
                }
                case "Swap":
                {
                    Vector2 me = p.Pos;
                    Component other = Entities(a, o.target).FirstOrDefault();
                    Actor foe = other == null ? Select(a, o, ev, at).FirstOrDefault() : null;
                    if (other != null) { var there = (Vector2)other.transform.position; other.transform.position = me; p.BlinkTo(there); }
                    else if (foe != null) { var there = foe.Pos; foe.Pos = me; p.BlinkTo(there); }
                    break;
                }
                case "MoveEntity":
                    foreach (var e in Entities(a, o.target)) e.transform.position = at;
                    Fx.I?.Burst(at + Vector2.up * 0.3f, Pal.ShadowEl, 8, 3, 0.3f);
                    break;
                case "ResetCooldowns": p.ReduceCooldowns(Mathf.Clamp01(mag)); break;
                case "GainDash": p.DashCharges = Mathf.Min(p.DashChargesMax, p.DashCharges + cnt); break;
                case "Spawn": SpawnEntities(a, o, ev, at, magD, dur, cnt, el, tags, depth); break;
                case "DetonateEntities":
                    foreach (var e in Entities(a, o.target).ToList())
                    {
                        if (e is Summon s) s.Burst(magD, Mathf.Max(1.5f, rad), "ABILITY:" + a.id);
                        else if (e is Clone c) c.Burst(magD, Mathf.Max(1.5f, rad), "ABILITY:" + a.id);
                    }
                    break;
                case "ExtendEntities":
                    foreach (var e in Entities(a, o.target)) { if (e is Summon s) s.Extend(mag); else if (e is Clone c) c.Extend(mag); }
                    break;
                case "EmpowerNext":
                    empower.Add((magD, el, tags, cnt));
                    Fx.I?.Burst(p.Pos + Vector2.up * 0.5f, col, 10, 3, 0.4f, -2);
                    Sfx.Play("charge", 0.4f, 1.4f);
                    break;
                case "AddState":
                {
                    float v = o.amount + o.per * Value(a, o.scale, ev);
                    SetState(a, o.state, GetState(a, o.state) + v, ev.depth);
                    break;
                }
                case "SetState": SetState(a, o.state, o.amount, ev.depth); break;
                case "StorePosition":
                {
                    if (!positions.TryGetValue(a, out var d)) positions[a] = d = new Dictionary<string, Vector2>();
                    var pos = string.IsNullOrEmpty(o.at) || o.at == "Stored" ? (ev.position != Vector2.zero ? ev.position : p.Pos) : at;
                    d[o.state] = pos;
                    S(a)[o.state] = 1;
                    Fx.I?.Burst(pos + Vector2.up * 0.2f, Pal.ShadowEl, 6, 2, 0.4f);
                    break;
                }
                case "StoreTarget":
                {
                    var t = Select(a, o, ev, at).FirstOrDefault();
                    if (t == null) break;
                    if (!targets.TryGetValue(a, out var d)) targets[a] = d = new Dictionary<string, Actor>();
                    d[o.state] = t;
                    S(a)[o.state] = 1;
                    t.AddStatus("mark", 4, 0, 0, p, Pal.ShadowEl);
                    break;
                }
                case "GainGold": p.GainGold(Mathf.RoundToInt(mag)); break;
                case "GainXp": p.GainXp(mag); break;
                case "GainPotion": p.Potions = Mathf.Min(5, p.Potions + cnt); break;
                case "DropPickup": Pickup.Drop(o.mode is "gold" or "xp" ? o.mode : "heart", mag, at); break;
            }

            // ── Relation layer (after the base effect) ──
            if (cascade >= Cfg.maxCascade) return;
            switch (o.rel)
            {
                case "Echo":
                    if (scale > 0.3f) p.StartCoroutine(Later(0.5f, () => Apply(a, r, o, ev, scale * 0.5f, cascade + Cfg.maxCascade)));
                    break;
                case "Mirror":
                    if (o.op is "Nova" or "Push" or "Pull" or "Spawn" && scale > 0.3f)
                    {
                        var e2 = ev; e2.position = p.Pos * 2 - at;
                        var o2 = CopyOp(o); o2.at = "EventPos"; o2.rel = null;
                        Apply(a, r, o2, e2, scale * 0.8f, Cfg.maxCascade);
                    }
                    break;
                case "Chain":
                {
                    if (damaged.Count == 0) break;
                    var from = damaged[0];
                    var hit = new HashSet<Actor>(damaged);
                    int jumps = Mathf.Clamp(o.times <= 0 ? 2 : o.times, 1, Cfg.maxTemporalTicks);
                    float k = 0.75f;
                    for (int i = 0; i < jumps; i++)
                    {
                        var next = Actor.InRadius(from.Pos, 4, Team.Enemy).Where(x => !hit.Contains(x)).OrderBy(x => (x.Pos - from.Pos).sqrMagnitude).FirstOrDefault();
                        if (next == null) break;
                        hit.Add(next);
                        var e2 = ev; e2.target = next; e2.position = next.Pos;
                        var o2 = CopyOp(o); o2.rel = null; o2.target = "EventTarget"; o2.at = "EventPos";
                        Apply(a, r, o2, e2, scale * k, Cfg.maxCascade);
                        k *= 0.75f; from = next;
                    }
                    break;
                }
                case "Spread":
                    if (o.op == "ApplyStatus")
                        foreach (var t in damaged.ToList())
                            foreach (var n in Nearby(t, 3, Mathf.Clamp(o.times <= 0 ? 2 : o.times, 1, Cfg.maxTemporalTicks)))
                                ApplyStatus(n, o.status, mag * 0.7f, dur * 0.7f, false);
                    break;
                case "Link":
                    foreach (var t in damaged.ToList())
                        foreach (var n in Nearby(t, 4, Mathf.Clamp(o.times <= 0 ? 2 : o.times, 1, Cfg.maxTemporalTicks)))
                        {
                            Fx.I?.Play(Art.LightningBolt(32), n.Pos, 0, 22, 0.5f, null, true);
                            Combat.Hit(n, new DamageInfo { amount = atk * magD * 0.5f, source = p, direction = n.Pos - t.Pos, element = el, tags = tags, depth = depth + 1, fromAbility = true, noEvents = true, fxColor = Pal.ShadowEl }, Pal.ShadowEl);
                        }
                    break;
                case "Cascade":
                    foreach (var dead in killed.Take(Mathf.Max(1, o.times <= 0 ? 2 : o.times)))
                    {
                        var e2 = ev; e2.position = dead.Pos; e2.target = null; e2.depth = depth;
                        var o2 = CopyOp(o); o2.at = "EventPos"; if (o2.target == "EventTarget") o2.target = "NearestEnemy";
                        Apply(a, r, o2, e2, scale * 0.7f, cascade + 1);
                    }
                    break;
            }
        }

        static OpDef CopyOp(OpDef o) => JsonUtility.FromJson<OpDef>(JsonUtility.ToJson(o));

        IEnumerator Later(float t, System.Action act) { yield return new WaitForSeconds(t); if (p != null && p.Alive) act(); }

        static IEnumerable<Actor> Nearby(Actor src, float radius, int n) =>
            Actor.InRadius(src.Pos, radius, Team.Enemy).Where(x => x != src).OrderBy(x => (x.Pos - src.Pos).sqrMagnitude).Take(n).ToList();

        void SpawnEntities(AbilityDef a, OpDef o, CombatEvent ev, Vector2 at, float power, float dur, int cnt, Element el, string tags, int depth)
        {
            dur = Mathf.Min(dur, Cfg.maxSpawnDuration);
            for (int i = 0; i < cnt; i++)
            {
                if (LiveSpawns >= Cfg.maxSpawnPerOwner) break;           // SpawnCap
                Vector2 sp = at + (cnt > 1 ? MathX.Dir(i * 360f / cnt) * 0.6f : Vector2.zero);
                Summon s = null;
                switch (o.entity)
                {
                    case "Clone":
                    case "PhantomWeapon":
                        lastClone[a] = Diverse.Clone.Spawn(p, sp, dur, power, o.rel == "Copy" || o.mode == "Copy", o.entity == "PhantomWeapon", el, tags, depth);
                        break;
                    case "Turret": s = Summon.Turret(p, sp, dur, power, el, tags); break;
                    case "Orb": s = Summon.Orb(p, dur, power, el, i, cnt, tags); break;
                    case "Zone": s = Summon.Zone(p, sp, dur, power, el, tags); break;
                    case "Mine": s = Summon.Mine(p, sp, dur, power, el, tags); break;
                    case "Totem": s = Summon.Totem(p, sp, dur, power, el, tags); break;
                    case "Barrier": s = Summon.Barrier(p, sp, dur, power, el, tags); break;
                    case "Decoy": s = Summon.Decoy(p, sp, dur, power, el, tags); break;
                }
                if (s == null) continue;
                lastSummon[a] = s;
                if (o.rel == "Follow") s.Follow();
                if (o.rel == "Attach" && ev.target != null && ev.target.Alive) s.Attach(ev.target);
            }
        }

        // ── Projectile relations ──

        void Bounce(Projectile pr, int times)
        {
            int left = times;
            pr.onHit += (x, hitActor) =>
            {
                if (left-- <= 0) return;
                var next = Actor.InRadius(hitActor.Pos, 5, Team.Enemy).Where(e => e != hitActor).OrderBy(e => (e.Pos - hitActor.Pos).sqrMagnitude).FirstOrDefault();
                if (next != null) { x.vel = ((Vector2)next.Pos - (Vector2)x.transform.position).normalized * x.vel.magnitude; x.pierce++; x.life += 0.4f; }
            };
        }

        void ReturnHome(Projectile pr)
        {
            bool back = false;
            pr.life += 0.9f;
            pr.pierce += 3;
            p.StartCoroutine(Boomerang(pr, () => back = true));
            pr.onEnd += _ => { if (!back) { } };
        }

        IEnumerator Boomerang(Projectile pr, System.Action turned)
        {
            yield return new WaitForSeconds(0.45f);
            if (pr == null) yield break;
            turned();
            pr.homing = 0;
            while (pr != null && p != null)
            {
                Vector2 to = p.Pos + Vector2.up * 0.45f - (Vector2)pr.transform.position;
                if (to.sqrMagnitude < 0.4f) { pr.Block(); yield break; }
                pr.vel = to.normalized * 13f;
                yield return null;
            }
        }

        void SplitOnHit(Projectile pr, int pieces, Color32 col, float mult, string tags, int depth)
        {
            bool done = false;
            pr.onHit += (x, hitActor) =>
            {
                if (done) return;
                done = true;
                Vector2 from = x.transform.position;
                for (int i = 0; i < pieces; i++)
                {
                    var dir = MathX.Dir(360f / pieces * i + 20);
                    var d = new DamageInfo { amount = p.AttackPower * mult, source = p, direction = dir, knockback = 1, hitstop = 0.01f, canCrit = true, tags = tags, depth = depth + 1, fromAbility = true };
                    var child = Projectile.Spawn(Art.Orb(Pal.White, col, 6), from, dir * 10f, Team.Enemy, p, d, true);
                    child.color = col; child.life = 0.4f;
                }
            };
        }

        // ── Empower ──

        /// <summary>The next basic attack (the first Hit from the player's own weapon) consumes one empower charge.</summary>
        void ConsumeEmpower(CombatEvent ev)
        {
            if (empower.Count == 0 || ev.target == null || !ev.target.Alive) return;
            var e = empower[0];
            e.left--;
            if (e.left <= 0) empower.RemoveAt(0); else empower[0] = e;
            var col = e.el != Element.None ? Combat.ElementColor(e.el) : Pal.Gold;
            var d = new DamageInfo { amount = p.AttackPower * e.bonus, source = p, direction = ev.direction, knockback = 3, hitstop = 0.06f, canCrit = true, element = e.el, tags = e.tags, depth = ev.depth + 1, fromAbility = true };
            Fx.I?.Play(Art.Shockwave(col, 16), ev.target.Pos, 0, 26, 1, null, true);
            Combat.Hit(ev.target, d, col, "hit_heavy", 0.2f, 2.5f);
            Combat.ApplyElement(ev.target, e.el, d.amount, p, ev.depth + 1);
        }

        // ── Statuses ──

        public static Color32 StatusColor(string s) => s switch
        {
            "burn" => Pal.Fire, "chill" => Pal.Frost, "poison" => Pal.Hex("b6f06b"), "mark" => Pal.ShadowEl, "bleed" => Pal.Blood, "weaken" => Pal.Hex("6b4fa8"), _ => Pal.White,
        };

        static Element Opposite(Element e) => e switch
        {
            Element.Fire => Element.Frost, Element.Frost => Element.Fire, Element.Holy => Element.Shadow, Element.Shadow => Element.Holy,
            Element.Lightning => Element.Wind, Element.Wind => Element.Lightning, Element.Blood => Element.Void, Element.Void => Element.Blood,
            Element.Poison => Element.Arcane, Element.Arcane => Element.Poison, _ => Element.Arcane,
        };

        static string OppositeStatus(string s) => s switch
        {
            "burn" => "chill", "chill" => "burn", "poison" => "bleed", "bleed" => "poison", "mark" => "weaken", "weaken" => "mark", _ => s,
        };

        void ApplyStatus(Actor t, string status, float mag, float dur, bool stack)
        {
            float atk = p.AttackPower * (1 + StatusPotency(status));
            dur *= 1 + StatusPotency(status) * 0.5f;
            if (stack)
            {
                var cur = t.statuses.FirstOrDefault(s => s.id == status);
                if (cur != null) { cur.time = Mathf.Min(cur.time + dur, dur * 3); cur.dps = Mathf.Min(cur.dps + atk * mag * 0.5f, atk * mag * 3); return; }
            }
            switch (status)
            {
                case "burn": t.AddStatus("burn", dur, atk * mag, 0, p, Pal.Fire); break;
                case "poison": t.AddStatus("poison", dur, atk * mag * 0.8f, 0.1f, p, Pal.Hex("b6f06b")); break;
                case "bleed": t.AddStatus("bleed", dur, atk * mag * 0.9f, 0, p, Pal.Blood); break;
                case "chill": t.AddStatus("chill", dur, 0, Mathf.Clamp(0.3f + mag * 0.3f, 0.3f, 0.7f), p, Pal.Frost); break;
                case "mark": t.AddStatus("mark", dur, 0, 0, p, Pal.ShadowEl); break;
                case "weaken": t.AddStatus("weaken", dur, 0, 0, p, Pal.Hex("6b4fa8")); break;
            }
        }
    }
}
