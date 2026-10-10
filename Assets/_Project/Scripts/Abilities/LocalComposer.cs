using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Offline generator (no key / AI failed). Not a list of finished skills: it composes Rule IR from STRUCTURAL shapes
    /// (reactive, stateful accumulate→release, threshold, trade-off, build-scaling, conversion, meta, system) and fills each
    /// slot with primitives weighted by the player's patterns. Same output format as the AI → same validator/balancer.
    /// </summary>
    public static class LocalComposer
    {
        static readonly string[] Elements = RuleLanguage.Elements;

        public static List<AbilityDef> Compose(GenerationContext c, int count, System.Random rnd)
        {
            var results = new List<AbilityDef>();
            int guard = 0;
            while (results.Count < count * 3 && guard++ < 80)
            {
                var shape = Weighted(rnd, Shapes(c));
                var g = shape(c, rnd);
                if (g == null) continue;
                g.source = "local";
                g.EnsureLists();
                if (results.Any(r => r.Signature() == g.Signature())) continue;
                results.Add(g);
            }
            return results;
        }

        delegate AbilityDef Shape(GenerationContext c, System.Random r);

        static List<(Shape, float)> Shapes(GenerationContext c)
        {
            int t = c.maxTier;
            var list = new List<(Shape, float)>
            {
                (Reactive, 3f),
                (Numeric, 0.6f),
            };
            if (t >= 2)
            {
                list.Add((Accumulate, 1.6f));
                list.Add((Threshold, 1.0f));
                list.Add((TradeOff, 0.9f));
                list.Add((BuildScaling, 0.8f));
                list.Add((Conditional, 1.0f));
                list.Add((Conversion, 0.35f));
                list.Add((Anchor, 0.6f));
                list.Add((Layered, 1.4f));
                list.Add((MacroShape, 1.4f));
                list.Add((Price, 0.6f));
            }
            if (t >= 3) list.Add((Replacement, 0.5f));
            if (t >= 3 && c.owned.Count >= 2) list.Add((MetaShape, 1.2f));
            if (t >= 4) list.Add((SystemShape, 0.5f));
            return list;
        }

        static T Pick<T>(System.Random r, IList<T> list) => list[r.Next(list.Count)];

        static T Weighted<T>(System.Random r, IList<(T v, float w)> list)
        {
            float total = list.Sum(x => Mathf.Max(0, x.w));
            float x0 = (float)r.NextDouble() * total;
            foreach (var it in list) { x0 -= Mathf.Max(0, it.w); if (x0 <= 0) return it.v; }
            return list[list.Count - 1].v;
        }

        static float Rel(GenerationContext c, string tags)
        {
            float w = 0.4f;
            foreach (var t in tags.Split(' ')) if (c.relevantTags.Contains(t)) w += 1f;
            return w;
        }

        static bool Chance(System.Random r, double p) => r.NextDouble() < p;

        // ───────────── Slot fillers ─────────────

        static string Trigger(GenerationContext c, System.Random r, bool frequentOk = true)
        {
            var list = new List<(string, float)>
            {
                ("Dash", Rel(c, "DODGE MOBILITY")), ("PerfectDodge", Rel(c, "DODGE") * 0.8f), ("Kill", Rel(c, "KILL") * 1.1f),
                ("Crit", Rel(c, "CRIT")), ("Damaged", Rel(c, "DEFENSE GUARD")), ("SkillCast", Rel(c, "SKILL MAGIC")),
                ("ComboFinish", Rel(c, "MELEE COMBO")), ("DashEnd", Rel(c, "DODGE") * 0.5f),
                ("CloneSpawn", c.relevantTags.Contains("CLONE") ? 1.2f : 0), ("CloneExpire", c.relevantTags.Contains("CLONE") ? 1.2f : 0),
                ("StatusApplied", c.relevantTags.Contains("STATUS") ? 1f : 0.1f), ("StatusExpired", c.relevantTags.Contains("STATUS") ? 0.8f : 0.05f),
                ("Guard", c.relevantTags.Contains("SHIELD") ? 1.6f : 0), ("ProjectileEnd", c.relevantTags.Contains("PROJECTILE") ? 1.2f : 0.05f),
                ("Every", 0.4f), ("ShieldBroken", c.owned.Any(o => o.HasTag("SHIELD")) ? 0.8f : 0.05f),
            };
            if (frequentOk) { list.Add(("Hit", Rel(c, "MELEE RANGED ATTACK") * 0.8f)); list.Add(("Attack", Rel(c, "ATTACK") * 0.4f)); }
            return Weighted(r, list);
        }

        static RuleDef Rule(string trigger) => new RuleDef { kind = RuleKind.Trigger, trigger = trigger, period = 4 };

        static bool HasTarget(string trig) => (RuleLanguage.Get(trig)?.provides & Ctx.Target) != 0;
        static bool HasPlace(string trig) => (RuleLanguage.Get(trig)?.provides & (Ctx.Target | Ctx.Corpse)) != 0 || trig is "CloneExpire" or "CloneSpawn" or "ProjectileEnd";

        static string StatusFor(string element) => RuleLanguage.StatusOf(element) ?? "burn";

        /// <summary>A payoff op that makes sense after the trigger (offense / defense / control / summon).</summary>
        static OpDef Payoff(GenerationContext c, System.Random r, string trig, bool allowSpawn = true)
        {
            string el = Chance(r, 0.55) ? Pick(r, Elements) : null;
            bool place = HasPlace(trig), target = HasTarget(trig);
            var list = new List<(string, float)>
            {
                ("Nova", Rel(c, "AREA MELEE") * (place ? 1.2f : 0.8f)),
                ("Damage", target ? 1f : 0),
                ("Projectile", Rel(c, "RANGED PROJECTILE")),
                ("Chain", Rel(c, "LIGHTNING MAGIC") * 0.7f),
                ("Spawn", allowSpawn && trig is not "CloneSpawn" ? (c.seededSources.Contains("CLONE") || c.seededSources.Contains("ORB") ? 3f : 0.9f) : 0),
                ("Shield", trig is "Damaged" or "Dash" or "Guard" or "PerfectDodge" ? 1.2f : 0.2f),
                ("Heal", trig is "Kill" or "Damaged" ? 0.8f : 0.15f),
                ("Buff", 0.6f),
                ("ApplyStatus", target ? Rel(c, "STATUS") * 0.8f : 0),
                ("Stun", target ? 0.4f : 0),
                ("Pull", 0.25f), ("Push", trig is "Damaged" or "Guard" ? 0.8f : 0.15f),
                ("EmpowerNext", trig is "Dash" or "PerfectDodge" or "Guard" or "DashEnd" ? 1.2f : 0.3f),
                ("ResetCooldowns", trig is "Kill" or "PerfectDodge" or "Crit" ? 0.6f : 0.05f),
                ("Blink", trig is "Kill" or "PerfectDodge" ? 0.3f : 0),
                ("Detonate", target && c.relevantTags.Contains("STATUS") ? 0.8f : 0),
            };
            string op = Weighted(r, list);
            var o = new OpDef { op = op, element = el };
            switch (op)
            {
                case "Nova": o.at = trig is "Dash" or "PerfectDodge" ? (Chance(r, 0.5) ? "DashOrigin" : "Self") : place ? "EventPos" : "Self"; break;
                case "Damage": case "Stun": case "Chain": o.target = target ? "EventTarget" : "NearestEnemy"; if (op == "Chain") o.element = "Lightning"; break;
                case "Projectile": o.at = place ? "EventPos" : "Self"; o.mode = Pick(r, new[] { "Seek", "Ring", "Fan" }); break;
                case "Spawn":
                    o.entity = c.seededSources.Contains("CLONE") ? "Clone" : c.seededSources.Contains("ORB") ? "Orb" : Pick(r, RuleLanguage.Entities);
                    if (o.entity == "Clone" && Chance(r, 0.6)) o.rel = "Copy";
                    if (o.entity == "Zone") o.element ??= Pick(r, Elements);
                    o.at = trig is "Dash" or "PerfectDodge" ? "DashOrigin" : place ? "EventPos" : "Self";
                    break;
                case "Buff": o.stat = Pick(r, RuleLanguage.BuffStats); if (c.relevantTags.Contains("CRIT") && Chance(r, 0.5)) o.stat = "CritChance"; break;
                case "ApplyStatus": o.target = "EventTarget"; o.status = StatusFor(el ?? Pick(r, new[] { "Fire", "Frost", "Poison", "Shadow" })); break;
                case "Detonate": o.target = "EventTarget"; o.status = Pick(r, RuleLanguage.Statuses); break;
                case "Pull": case "Push": o.at = place ? "EventPos" : "Self"; break;
                case "Blink": o.target = "NearestEnemy"; break;
            }
            return o;
        }

        static void Throttle(RuleDef rule, System.Random r)
        {
            if (rule.trigger is "Hit" or "Attack" or "StatusApplied" or "ProjectileEnd")
            {
                if (Chance(r, 0.5)) rule.when.Add(new CondDef { type = "Chance", value = Chance(r, 0.5) ? 0.2f : 0.3f });
                else if (Chance(r, 0.5)) rule.when.Add(new CondDef { type = "EveryNth", value = r.Next(3, 6) });
                else rule.cooldown = 1.5f;
            }
            if (rule.trigger == "Every") rule.period = 3 + r.Next(0, 4);
        }

        // ───────────── Shapes ─────────────

        /// <summary>Tier 1: event → effect (the old grammar, still the backbone).</summary>
        static AbilityDef Reactive(GenerationContext c, System.Random r)
        {
            string trig = Trigger(c, r);
            var rule = Rule(trig);
            if (trig is "StatusApplied" or "StatusExpired" && Chance(r, 0.7)) rule.param = Pick(r, RuleLanguage.Statuses);
            rule.ops.Add(Payoff(c, r, trig));
            if (Chance(r, 0.25) && rule.ops[0].op != "Spawn")
            {
                var o2 = Payoff(c, r, trig, false);
                if (o2.op != rule.ops[0].op) rule.ops.Add(o2);
            }
            if (rule.ops.Any(o => o.op == "Detonate")) rule.when.Add(new CondDef { type = "TargetHas", text = rule.ops.First(o => o.op == "Detonate").status });
            Throttle(rule, r);
            if (trig is "Kill" && Chance(r, 0.35))
            {
                // Spread a status from the corpse (needs the status to be there)
                string st = Pick(r, new[] { "burn", "poison", "chill", "mark" });
                rule.ops.Clear();
                rule.when.Clear();
                rule.when.Add(new CondDef { type = "TargetHas", text = st });
                rule.ops.Add(new OpDef { op = "Spread", status = st });
            }
            return Finish(new AbilityDef { rules = { rule } }, c);
        }

        /// <summary>Tier 0: a plain stat (at most one of these per offer).</summary>
        static AbilityDef Numeric(GenerationContext c, System.Random r)
        {
            var stat = Pick(r, new[] { "MaxHp", "Attack", "AttackSpeed", "MoveSpeed", "CritChance", "CritDamage", "CooldownReduction", "Regen" });
            return Finish(new AbilityDef { rules = { new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "ModifyStat", stat = stat, amount = 0.1f } } } } }, c);
        }

        /// <summary>Tier 2: accumulate a value from one event, release it on another (스킬 「고통의 기억」 같은 구조).</summary>
        static AbilityDef Accumulate(GenerationContext c, System.Random r)
        {
            var (label, src) = Pick(r, new[] { ("고통", "Damaged"), ("잔향", "Hit"), ("사냥", "Kill"), ("발걸음", "Dash"), ("주문", "SkillCast") });
            string release = Trigger(c, r, false);
            if (release == src) release = src == "PerfectDodge" ? "Kill" : "PerfectDodge";
            var a = new AbilityDef { states = { new StateDef { name = "s", label = label, type = src is "Damaged" or "Hit" ? "StoredValue" : "Counter", max = src is "Damaged" or "Hit" ? 8 : 5 } } };
            var fill = Rule(src);
            fill.ops.Add(src is "Damaged" or "Hit"
                ? new OpDef { op = "AddState", state = "s", scale = "event.amount", per = 1 }
                : new OpDef { op = "AddState", state = "s", amount = 1 });
            var use = Rule(release);
            use.when.Add(new CondDef { type = "Compare", a = "state:s", cmp = ">=", value = src is "Damaged" ? 0.15f : 1 });
            var pay = Payoff(c, r, release, false);
            if (pay.op is "Blink" or "ResetCooldowns") pay = new OpDef { op = "Nova", at = "Self", element = Pick(r, Elements) };
            pay.scale = "state:s";
            use.ops.Add(pay);
            use.ops.Add(new OpDef { op = "SetState", state = "s", amount = 0 });
            a.rules.Add(fill); a.rules.Add(use);
            return Finish(a, c);
        }

        /// <summary>Tier 2: count to N → a big payoff (StateFull trigger), e.g. "완벽 회피 3번 → 다음 공격을 분신이 복제".</summary>
        static AbilityDef Threshold(GenerationContext c, System.Random r)
        {
            string src = Trigger(c, r);
            int n = src is "Hit" or "Attack" ? r.Next(8, 15) : src is "PerfectDodge" or "Damaged" or "Guard" ? 3 : r.Next(3, 6);
            var a = new AbilityDef { states = { new StateDef { name = "s", label = Pick(r, new[] { "결의", "잔상", "불씨", "맥박", "기억" }), type = "Counter", max = n } } };
            var fill = Rule(src);
            fill.ops.Add(new OpDef { op = "AddState", state = "s", amount = 1 });
            var full = new RuleDef { kind = RuleKind.Trigger, trigger = "StateFull", param = "s" };
            var pay = Payoff(c, r, "StateFull");
            if (pay.op is "Damage" or "Stun" or "Chain") pay.target = "NearestEnemy";
            full.ops.Add(pay);
            if (Chance(r, 0.4) && pay.op != "EmpowerNext") full.ops.Add(new OpDef { op = "EmpowerNext", element = pay.element });
            full.ops.Add(new OpDef { op = "SetState", state = "s", amount = 0 });
            a.rules.Add(fill); a.rules.Add(full);
            return Finish(a, c);
        }

        /// <summary>Tier 2: a real trade-off — a permanent cost paid for a scaling gain (「피의 계약」 구조).</summary>
        static AbilityDef TradeOff(GenerationContext c, System.Random r)
        {
            var (costStat, gainStat, scale) = Pick(r, new[]
            {
                ("MaxHp", "Attack", "self.missingHpPct"),
                ("Armor", "AttackSpeed", "enemies.near"),
                ("MoveSpeed", "Armor", "enemies.near"),
                ("MaxHp", "CritChance", "self.missingHpPct"),
                ("Regen", "Attack", "time.sinceDamaged"),
            });
            var a = new AbilityDef();
            a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops =
            {
                new OpDef { op = "ModifyStat", stat = costStat, amount = -0.2f },
                new OpDef { op = "ModifyStat", stat = gainStat, scale = scale, per = 0.1f },
            } });
            if (costStat == "MaxHp" && Chance(r, 0.5))
                a.rules.Add(new RuleDef { kind = RuleKind.Continuous, when = { new CondDef { type = "Compare", a = "self.hpPct", cmp = "<=", value = 0.3f } }, ops = { new OpDef { op = "Convert", from = "Heal", to = "Shield", amount = 1 } } });
            return Finish(a, c);
        }

        /// <summary>Tier 2: scale with the build itself (세피리아 식 — "보유한 X 능력 하나당").</summary>
        static AbilityDef BuildScaling(GenerationContext c, System.Random r)
        {
            var ownedTags = c.owned.SelectMany(o => o.tags).Concat(c.weaponTags.Split(' ')).Where(RuleLanguage.KnownTag).Distinct().ToList();
            if (ownedTags.Count == 0) return null;
            string tag = Pick(r, ownedTags);
            var a = new AbilityDef();
            if (Chance(r, 0.5))
                a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "ModifyStat", stat = Pick(r, new[] { "MoveSpeed", "CritChance", "Attack", "CooldownReduction", "Armor" }), scale = "build.tag:" + tag, per = 0.05f } } });
            else
                a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "ModifyStat", stat = Pick(r, new[] { "CritChance", "ElementPower", "AttackSpeed" }), scale = "build.elements", per = 0.05f } } });
            return Finish(a, c);
        }

        /// <summary>Tier 2: conditional passive (per-hit amplification or a situational stat).</summary>
        static AbilityDef Conditional(GenerationContext c, System.Random r)
        {
            var a = new AbilityDef();
            var choice = r.Next(4);
            switch (choice)
            {
                case 0: a.rules.Add(new RuleDef { kind = RuleKind.Continuous, when = { new CondDef { type = "TargetHas", text = Pick(r, RuleLanguage.Statuses) } }, ops = { new OpDef { op = "AmplifyDamage", amount = 0.3f } } }); break;
                case 1: a.rules.Add(new RuleDef { kind = RuleKind.Continuous, when = { new CondDef { type = "Compare", a = "target.hpPct", cmp = Chance(r, 0.5) ? ">=" : "<=", value = Chance(r, 0.5) ? 0.8f : 0.35f } }, ops = { new OpDef { op = "AmplifyDamage", amount = 0.3f } } }); break;
                case 2: a.rules.Add(new RuleDef { kind = RuleKind.Continuous, when = { new CondDef { type = "Compare", a = "self.moving", cmp = "==", value = Chance(r, 0.5) ? 1 : 0 } }, ops = { new OpDef { op = "ModifyStat", stat = Pick(r, new[] { "Armor", "AttackSpeed", "CritChance", "Regen" }), amount = 0.1f } } }); break;
                default: a.rules.Add(new RuleDef { kind = RuleKind.Continuous, when = { new CondDef { type = "Compare", a = "enemies.near", cmp = ">=", value = 3 } }, ops = { new OpDef { op = "ModifyStat", stat = Pick(r, new[] { "Armor", "Attack", "AttackSpeed" }), amount = 0.1f } } }); break;
            }
            return Finish(a, c);
        }

        /// <summary>Tier 2: rule redefinition (whitelisted conversion) + a compensating rule.</summary>
        static AbilityDef Conversion(GenerationContext c, System.Random r)
        {
            var a = new AbilityDef();
            string pair = Pick(r, RuleLanguage.ConvertPairs.Keys.ToList());
            var parts = pair.Split('>');
            a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "Convert", from = parts[0], to = parts[1] } } });
            if (pair == "Heal>Shield")
            {
                var shieldTrig = Rule("ShieldBroken");
                shieldTrig.ops.Add(new OpDef { op = "Nova", at = "Self", element = Pick(r, Elements) });
                a.rules.Add(shieldTrig);
            }
            return Finish(a, c);
        }

        /// <summary>Tier 2: remember a place, act on it later (Beacon 구조).</summary>
        static AbilityDef Anchor(GenerationContext c, System.Random r)
        {
            var a = new AbilityDef { states = { new StateDef { name = "mark", label = "표지", type = "Position" } } };
            string store = Pick(r, new[] { "Dash", "Kill", "CloneExpire", "Guard" });
            string use = Pick(r, new[] { "PerfectDodge", "SkillCast", "Damaged", "Every" }.Where(x => x != store).ToList());
            var s = Rule(store); s.ops.Add(new OpDef { op = "StorePosition", state = "mark", at = store == "Dash" ? "DashOrigin" : "EventPos" });
            var u = Rule(use);
            var pay = Pick(r, new[] { "Nova", "Blink", "Pull", "Spawn" });
            var o = new OpDef { op = pay, at = "Stored", state = "mark", element = Pick(r, Elements) };
            if (pay == "Spawn") { o.entity = Pick(r, new[] { "Zone", "Turret", "PhantomWeapon" }); }
            u.ops.Add(o);
            if (use == "Every") u.period = 5;
            a.rules.Add(s); a.rules.Add(u);
            return Finish(a, c);
        }

        /// <summary>Tier 3: modify other owned abilities by tag.</summary>
        static AbilityDef MetaShape(GenerationContext c, System.Random r)
        {
            var tags = c.owned.SelectMany(o => o.tags).Where(t => t != "STATE" && RuleLanguage.KnownTag(t)).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(5).ToList();
            if (tags.Count == 0) return null;
            string tag = Pick(r, tags);
            string op = Weighted(r, new List<(string, float)> { ("AmplifyTagged", 1.2f), ("RepeatTagged", 1f), ("HasteTagged", 0.8f), ("InfuseTagged", 0.6f), ("ExtendTagged", 0.4f) });
            var o = new OpDef { op = op, tag = tag };
            if (op == "InfuseTagged") o.element = Pick(r, Elements.Where(e => e.ToUpper() != tag).ToList());
            var a = new AbilityDef { rules = { new RuleDef { kind = RuleKind.Meta, ops = { o } } } };
            // Strong meta with a price: the amplified group gets stronger, everything else pays
            if (op == "RepeatTagged" && Chance(r, 0.4))
                a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "ModifyStat", stat = "AttackSpeed", amount = -0.1f } } });
            return Finish(a, c);
        }

        /// <summary>Tier 4: change the game's systems (TFT 증강 식).</summary>
        static AbilityDef SystemShape(GenerationContext c, System.Random r)
        {
            var a = new AbilityDef();
            switch (r.Next(4))
            {
                case 0:
                    a.rules.Add(new RuleDef { kind = RuleKind.System, ops = { new OpDef { op = "OfferCount", amount = -1 }, new OpDef { op = "TierBias", amount = 1 } } });
                    break;
                case 1:
                    a.rules.Add(new RuleDef { kind = RuleKind.System, ops = { new OpDef { op = "BudgetBias", amount = 0.25f } } });
                    a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "ModifyStat", stat = "GoldGain", amount = -0.3f } } });
                    break;
                case 2:
                {
                    var tag = c.relevantTags.Where(RuleLanguage.KnownTag).DefaultIfEmpty("ATTACK").ToList();
                    a.rules.Add(new RuleDef { kind = RuleKind.System, ops = { new OpDef { op = "TagBias", tag = Pick(r, tag), amount = 1 } } });
                    var k = Rule("Kill"); k.when.Add(new CondDef { type = "Chance", value = 0.15f }); k.ops.Add(new OpDef { op = "GainGold", amount = 3 });
                    a.rules.Add(k);
                    break;
                }
                default:
                    a.rules.Add(new RuleDef { kind = RuleKind.System, ops = { new OpDef { op = "RerollDiscount", amount = 0.5f } } });
                    var camp = Rule("CampCleared"); camp.ops.Add(new OpDef { op = "GainXp", amount = 10 });
                    a.rules.Add(camp);
                    break;
            }
            return Finish(a, c);
        }

        // ───────────── Relation / temporal / macro shapes (the final language) ─────────────

        static readonly string[] DamageRelations = { "Echo", "Mirror", "Chain", "Cascade", "Alternate" };
        static readonly string[] ProjectileRelations = { "Bounce", "Return", "Split", "Pierce", "Seek", "Echo" };

        /// <summary>A reactive ability whose payoff is shaped by a relation and/or a temporal (Echo, Chain, Bounce, UntilHit, Periodic…).</summary>
        static AbilityDef Layered(GenerationContext c, System.Random r)
        {
            string trig = Trigger(c, r);
            var rule = Rule(trig);
            var o = Payoff(c, r, trig, false);
            var prim = RuleLanguage.Get(o.op);
            if (o.op == "Projectile") { o.rel = Pick(r, ProjectileRelations); o.times = r.Next(2, 4); }
            else if (prim != null && !string.IsNullOrEmpty(prim.relations))
            {
                var rels = prim.relations.Split(' ');
                o.rel = Pick(r, rels);
                o.times = r.Next(2, 4);
            }
            if (prim != null && Chance(r, 0.45))
            {
                var times = prim.temporals.Split(' ').Where(x => x is not ("Immediate" or "Delay" or "Refresh")).ToList();
                if (times.Count > 0) { o.time = Pick(r, times); o.times = r.Next(2, 4); }
            }
            if (o.op == "Spawn")
            {
                o.entity = Pick(r, new[] { "Zone", "Mine", "Totem", "Turret", "Decoy", "Barrier", "Clone" });
                o.rel = o.entity switch { "Zone" => Chance(r, 0.5) ? "Follow" : HasTarget(trig) ? "Attach" : "Follow", "Clone" => "Copy", "Orb" => "Orbit", _ => null };
            }
            rule.ops.Add(o);
            Throttle(rule, r);
            return Finish(new AbilityDef { rules = { rule } }, c);
        }

        /// <summary>One of the compiler macros (Hunt / Charge / Window / Anchor / Accumulate / Threshold) with player-relevant slots.</summary>
        static AbilityDef MacroShape(GenerationContext c, System.Random r)
        {
            string type = Pick(r, new[] { "Hunt", "Charge", "Window", "Anchor", "Accumulate", "Threshold" });
            var m = new MechanicIntent { type = type, on = Trigger(c, r, type != "Charge"), release = Trigger(c, r, false) };
            var pay = Payoff(c, r, m.release ?? m.on, false);
            m.effect = pay.op; m.at = pay.at; m.element = pay.element; m.status = pay.status; m.entity = pay.entity; m.stat = pay.stat;
            if (type == "Hunt") m.stat = Pick(r, new[] { "AttackSpeed", "MoveSpeed", "CritChance", "Attack" });
            if (type == "Window") m.stat = Pick(r, new[] { "AttackSpeed", "Armor", "CritChance", "MoveSpeed" });
            if (type == "Anchor" && Chance(r, 0.4)) { m.target = "StoredTarget"; m.effect = Pick(r, new[] { "Damage", "Chain", "Execute", "Blink" }); m.on = "Hit"; }
            if (type == "Threshold") m.count = m.on is "Hit" or "Attack" ? r.Next(8, 14) : r.Next(3, 6);
            var intent = new AbilityIntent { mechanics = { m } };
            var a = AbilityCompiler.Compile(intent).ability;
            if (a.rules.Count == 0) return null;
            return Finish(a, c);
        }

        /// <summary>A real price: forbid an action or cap a stat, paid with a strong conditional benefit.</summary>
        static AbilityDef Price(GenerationContext c, System.Random r)
        {
            var a = new AbilityDef();
            switch (r.Next(3))
            {
                case 0:   // no potions → strong regen-like sustain from kills
                    a.rules.Add(new RuleDef { kind = RuleKind.Constrain, ops = { new OpDef { op = "Forbid", mode = "Potion" } } });
                    a.rules.Add(new RuleDef { kind = RuleKind.Trigger, trigger = "Kill", ops = { new OpDef { op = "Heal" } } });
                    break;
                case 1:   // no natural regen → damage taken returns as shield
                    a.rules.Add(new RuleDef { kind = RuleKind.Constrain, ops = { new OpDef { op = "Forbid", mode = "Regen" } } });
                    a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "Convert", from = "DamageTaken", to = "Shield" } } });
                    break;
                default:  // glass cannon: max HP capped, damage up
                    a.rules.Add(new RuleDef { kind = RuleKind.Constrain, ops = { new OpDef { op = "CapStat", stat = "MaxHp", amount = -0.3f } } });
                    a.rules.Add(new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "AmplifyDamage", amount = 0.3f } } });
                    break;
            }
            return Finish(a, c);
        }

        /// <summary>Tier 3: a player action is replaced (dash → blink/nova, combo finisher → +projectiles, potion → shield).</summary>
        static AbilityDef Replacement(GenerationContext c, System.Random r)
        {
            string action = Weighted(r, new List<(string, float)> { ("Dash", Rel(c, "DODGE MOBILITY")), ("ComboFinisher", Rel(c, "MELEE COMBO")), ("Potion", 0.4f) });
            string with = action switch
            {
                "Dash" => Pick(r, new[] { "Blink", "Nova", "Shield" }),
                "ComboFinisher" => Pick(r, new[] { "Nova", "Projectile", "Spawn" }),
                _ => Pick(r, new[] { "Shield", "Nova" }),
            };
            var a = new AbilityDef { rules = { new RuleDef { kind = RuleKind.Replace, replaces = action, ops = { new OpDef { op = "ReplaceWith", mode = with, element = Chance(r, 0.6) ? Pick(r, Elements) : null, entity = with == "Spawn" ? Pick(r, new[] { "PhantomWeapon", "Zone", "Mine" }) : null, count = 3 } } } } };
            return Finish(a, c);
        }

        static AbilityDef Finish(AbilityDef a, GenerationContext c)
        {
            a.EnsureLists();
            RuleValidator.Tag(a);
            a.semanticReason = Reason(c, a);
            return a;
        }

        static string Reason(GenerationContext c, AbilityDef a)
        {
            var p = c.patterns.FirstOrDefault(x => x.tags.Split(' ').Any(t => RuleLanguage.TagMatches(a.tags, t)));
            if (a.tier >= 4) return "세계의 규칙 자체를 조금 비튼다";
            if (a.tier == 3) return "이미 가진 능력들을 엮어 하나의 빌드로";
            if (p != null) return $"{p.desc} 패턴이 자주 보인다 → 그 순간을 능력으로";
            if (c.seededSources.Count > 0 && a.tags.Any(t => c.seededSources.Contains(t))) return $"이미 고른 [{string.Join(",", c.seededSources)}] 강화에 원천을 붙인다";
            if (a.rules.Any(r => r.ops.Any(o => o.amount < 0 && o.op == "ModifyStat"))) return "대가를 치르고 얻는 힘";
            return "새로운 방향을 탐색";
        }

        // ───────────── Names (local stand-in for LLM #2) ─────────────

        public static void Name(AbilityDef g)
        {
            if (!string.IsNullOrEmpty(g.name)) { g.desc ??= g.Explain(); return; }
            g.desc = g.Explain();
            var firstOp = g.rules.SelectMany(r => r.ops).FirstOrDefault(o => o.op is not ("AddState" or "SetState" or "StorePosition")) ?? g.rules[0].ops[0];
            string el = firstOp.element switch
            {
                "Fire" => "불꽃", "Frost" => "서리", "Lightning" => "번개", "Shadow" => "그림자", "Holy" => "빛", "Poison" => "독", "Wind" => "바람", _ => null,
            };
            string noun = firstOp.op switch
            {
                "Spawn" => firstOp.entity switch { "Clone" => "잔영", "Turret" => "파수꾼", "Orb" => "위성", "Zone" => "장막", _ => "환영검" },
                "Nova" => "파문", "Projectile" => "깃", "Chain" => "낙뢰", "Heal" => "숨결", "Shield" => "보호막", "Buff" => "고양",
                "ApplyStatus" => "낙인", "Pull" => "소용돌이", "Push" => "충격", "Blink" => "도약", "ResetCooldowns" => "가속", "Damage" => "일격",
                "Detonate" => "기폭", "Spread" => "역병", "Execute" => "단죄", "Stun" => "굉음", "EmpowerNext" => "일섬",
                "Drain" => "갈증", "Slash" => "참월", "Beam" => "광휘", "Slow" => "서릿발", "Taunt" => "허상", "Transfer" => "전이", "Leap" => "도약",
                "Swap" => "자리바꿈", "DetonateEntities" => "불꽃놀이", "Resist" => "갑주", "Aura" => "후광", "StatusPotency" => "독기", "Forbid" or "CapStat" => "서약",
                "ReplaceWith" => "변신", "EnlargeTagged" => "확장", "MultiplyTagged" => "증식", "RetagAbilities" => "이름 붙이기", "GateTagged" => "벼랑",
                "UnchainTagged" => "해방", "RetriggerTagged" => "뒤바뀐 박자", "ExtraEvolution" => "선물", "UpgradeRandom" => "벼림", "DelayedReward" => "황금알",
                "ModifyStat" => g.rules.Any(r => r.ops.Any(o => o.amount < 0)) ? "계약" : "단련", "AmplifyDamage" => "급소",
                "Convert" => "변환", "AmplifyTagged" => "공명", "RepeatTagged" => "메아리", "HasteTagged" => "가속", "InfuseTagged" => "물들임",
                "ExtendTagged" => "여운", "OfferCount" or "TierBias" => "도박", "BudgetBias" => "축복", "RerollDiscount" => "행운", "TagBias" => "집착",
                "GainGold" => "횡재", "GainXp" => "깨달음", _ => "기술",
            };
            string trig = g.PrimaryTrigger;
            string verb = g.states.Count > 0 ? (g.states[0].label ?? "") + "의" : trig switch
            {
                "Dash" => "남겨진", "DashEnd" => "멈춘 자리의", "PerfectDodge" => "아슬아슬한", "Kill" => "사냥꾼의", "Crit" => "급소의", "Hit" => "스치는",
                "Damaged" => "되받아치는", "SkillCast" => "공명하는", "ComboFinish" => "마무리", "LowHealth" => "벼랑 끝", "Every" => "고요한",
                "CloneSpawn" => "겹치는", "CloneExpire" => "스러지는", "StatusApplied" => "번지는", "StatusExpired" => "꺼져가는", "Guard" => "막아선",
                "ProjectileEnd" => "떨어진", "ShieldBroken" => "깨어진", _ => g.tier >= 3 ? "엮인" : "",
            };
            g.name = (verb + " " + (el != null ? el + " " : "") + noun).Trim();
        }
    }
}
