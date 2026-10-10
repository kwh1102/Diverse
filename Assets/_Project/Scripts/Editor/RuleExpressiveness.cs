using System.Collections.Generic;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Expressiveness test (design doc, turn 2): 30 abilities in the style of TFT augments and Sephiria artifacts, each picked
    /// for a structurally different mechanic. Written once with mechanic macros (what the LLM writes) and once as raw IR where
    /// the macros can't say it. If all of them compile + validate, the language covers that design space.
    /// "pattern" names the reference mechanic each one reproduces (no original names / numbers are copied).
    /// </summary>
    public static class RuleExpressiveness
    {
        public class Case
        {
            public string name, pattern;
            public AbilityIntent intent;
            public Case(string name, string pattern, AbilityIntent intent) { this.name = name; this.pattern = pattern; this.intent = intent; }
        }

        static AbilityIntent M(string concept, params MechanicIntent[] ms) { var i = new AbilityIntent { concept = concept }; i.mechanics.AddRange(ms); return i; }
        static AbilityIntent R(string concept, List<StateDef> states, params RuleDef[] rules) { var i = new AbilityIntent { concept = concept, states = states ?? new List<StateDef>() }; i.rules.AddRange(rules); return i; }
        static MechanicIntent Mk(string type) => new MechanicIntent { type = type };
        static RuleDef Trig(string t, params OpDef[] ops) { var r = new RuleDef { kind = RuleKind.Trigger, trigger = t }; r.ops.AddRange(ops); return r; }
        static RuleDef Cont(params OpDef[] ops) { var r = new RuleDef { kind = RuleKind.Continuous }; r.ops.AddRange(ops); return r; }

        public static List<Case> All() => new List<Case>
        {
            // ── TFT augment patterns ──
            new Case("한 걸음 늦은 메아리", "Spell echo (abilities cast twice at reduced power)",
                M("Your skills echo", new MechanicIntent { type = "Meta", select = "ByTag", tag = "SKILL", effect = "RepeatTagged" })),
            new Case("도박사의 선택", "Fewer choices, higher rarity (choice-system augment)",
                M("Fewer, stranger choices", new MechanicIntent { type = "System", effect = "OfferCount", count = -1 }, new MechanicIntent { type = "System", effect = "TierBias", count = 1 })),
            new Case("황금 알", "Delayed payout that hatches after a goal (golden egg)",
                R("An egg that hatches after enough kills", null, new RuleDef { kind = RuleKind.System, ops = { new OpDef { op = "DelayedReward", amount = 120, times = 50 } } })),
            new Case("사냥꾼의 박자", "Kill-based stacking attack speed that decays (hunter stacks)",
                M("Each kill quickens you, fading over time", new MechanicIntent { type = "Hunt", on = "Kill", stat = "AttackSpeed", count = 10 })),
            new Case("최후의 저항", "Bonus scaling with missing HP + safety shield at low HP (last stand)",
                M("The closer to death, the harder you hit", new MechanicIntent { type = "Scaling", stat = "Attack", scale = "missing hp" }, new MechanicIntent { type = "Effect", on = "LowHealth", effect = "Shield" })),
            new Case("거인 사냥꾼", "Bonus damage vs high-HP targets (giant slayer)",
                R("Big things fall harder", null, new RuleDef { kind = RuleKind.Continuous, when = { new CondDef { type = "Compare", a = "target.hpPct", cmp = ">=", value = 0.7f } }, ops = { new OpDef { op = "AmplifyDamage" } } })),
            new Case("처형인", "Execute below threshold + bonus vs low HP (executioner)",
                M("Finish what you started", new MechanicIntent { type = "Effect", on = "Hit", effect = "Execute", chance = 0.5f })),
            new Case("첫 일격", "First hit on each enemy is empowered (first strike)",
                M("The first cut is the deepest", new MechanicIntent { type = "Effect", on = "FirstHit", effect = "Damage", rel = "Echo" })),
            new Case("연쇄 폭발", "Kills explode and chain into more kills, bounded (cascade)",
                M("Deaths explode", new MechanicIntent { type = "Effect", on = "Kill", effect = "Nova", at = "EventPos", rel = "Cascade", chance = 0.35f })),
            new Case("방랑 상인", "Economy: gold income and cheaper rerolls (economy augment)",
                M("Trade on the road: every pickup pays, rerolls are cheap", new MechanicIntent { type = "System", effect = "RerollDiscount" }, new MechanicIntent { type = "Effect", on = "Kill", effect = "DropPickup", chance = 0.3f }, new MechanicIntent { type = "Scaling", stat = "GoldGain", scale = "run.level" })),
            new Case("관통 화살", "Projectiles pierce and split (archer trait-style)",
                M("Arrows that do not stop", new MechanicIntent { type = "Effect", on = "Attack", effect = "Projectile", rel = "Split", chance = 0.3f })),
            new Case("세 번째 타격", "Every third attack is special (Nth-hit passive)",
                R("The third blow thunders", null, new RuleDef { kind = RuleKind.Trigger, trigger = "Hit", when = { new CondDef { type = "EveryNth", value = 3 } }, ops = { new OpDef { op = "Chain" } } })),
            new Case("분신술", "Clones that copy you on a cadence (summon augment)",
                M("Afterimages fight beside you", new MechanicIntent { type = "Threshold", on = "Dash", effect = "Spawn", entity = "Clone", rel = "Copy", count = 3 })),
            new Case("보호막 과충전", "Shield size converts to damage (shield-scaling augment)",
                M("Your shield hums with power", new MechanicIntent { type = "Convert", from = "Shield", to = "Damage" }, new MechanicIntent { type = "Effect", on = "Guard", effect = "Shield" })),
            new Case("탐욕의 대가", "Gold scales offense, at a price (greed)",
                R("Wealth is a weapon", null, Cont(new OpDef { op = "ModifyStat", stat = "Attack", scale = "self.gold", per = 0.05f }, new OpDef { op = "ModifyStat", stat = "Armor", amount = -0.1f }))),

            // ── Sephiria artifact patterns ──
            new Case("타오르는 수집", "Count-of-tag scaling (artifact set bonus)",
                M("Every fire you own feeds the rest", new MechanicIntent { type = "Scaling", stat = "ElementPower", scale = "build.tag:FIRE" })),
            new Case("원소의 조화", "Distinct element count scaling (diversity bonus)",
                M("Harmony of many elements", new MechanicIntent { type = "Scaling", stat = "CritChance", scale = "build.elements" })),
            new Case("고통의 기억", "Store damage taken, release on an event (vengeance)",
                M("Pain is remembered and returned on your next dash", new MechanicIntent { type = "Accumulate", on = "Damaged", release = "Dash", effect = "Nova", at = "Self", label = "고통" })),
            new Case("피의 계약", "Trade max HP for scaling damage; low-HP healing becomes shield",
                M("Blood pact", new MechanicIntent { type = "Tradeoff", price = "MaxHp", stat = "Attack", scale = "missing hp" }, new MechanicIntent { type = "Convert", from = "Heal", to = "Shield", scale = "self.hpPct", chance = 0.3f })),
            new Case("부서진 방패", "Shield break triggers an effect (break bonus)",
                M("Blows you take build a frail shield; when it shatters, so does everything near", new MechanicIntent { type = "Effect", on = "ShieldBroken", effect = "Nova", at = "Self", element = "Frost" }, new MechanicIntent { type = "Effect", on = "Damaged", effect = "Shield", cooldown = 3 })),
            new Case("역병", "Status spreads on death (contagion)",
                R("Poison outlives its host", null, new RuleDef { kind = RuleKind.Trigger, trigger = "Kill", when = { new CondDef { type = "TargetHas", text = "poison" } }, ops = { new OpDef { op = "Spread", status = "poison" } } })),
            new Case("저주 터뜨리기", "Consume statuses for burst (detonate)",
                M("Curses ripen and burst", new MechanicIntent { type = "Effect", on = "Crit", effect = "Detonate", status = "burn" })),
            new Case("바람 걸음", "Movement distance charges the next attack (momentum)",
                M("Every step sharpens the next strike", new MechanicIntent { type = "Convert", from = "MoveDistance", to = "Damage" })),
            new Case("정지된 시간", "Bonus while standing still (stance)",
                R("Stillness is strength", null, new RuleDef { kind = RuleKind.Continuous, when = { new CondDef { type = "Compare", a = "self.stillTime", cmp = ">=", value = 1 } }, ops = { new OpDef { op = "ModifyStat", stat = "CritChance" } } })),
            new Case("그림자 표지", "Remember a place and return to it (beacon/teleport)",
                R("Your dash leaves a shadow; casting returns you to it in a burst of shadow", new List<StateDef> { new StateDef { name = "shade", label = "그림자", type = "Position" } },
                    Trig("Dash", new OpDef { op = "StorePosition", state = "shade", at = "DashOrigin" }),
                    Trig("SkillCast", new OpDef { op = "Blink", at = "Stored", state = "shade" }, new OpDef { op = "Nova", at = "Self", element = "Shadow" }))),
            new Case("사냥 표적", "Mark a target; later effects hunt it (marked prey)",
                M("The first enemy you strike becomes your prey", new MechanicIntent { type = "Anchor", on = "FirstHit", release = "Crit", effect = "Chain", target = "StoredTarget", label = "사냥감" })),
            new Case("순간 이동 대시", "Replace the dash with something else (kit change)",
                M("Your dash becomes a blink that leaves a frost burst behind", new MechanicIntent { type = "Replace", on = "Dash", effect = "Blink" }, new MechanicIntent { type = "Effect", on = "Dash", effect = "Nova", at = "DashOrigin", element = "Frost" })),
            new Case("금욕의 서약", "Give up potions for kill-based sustain (vow)",
                R("No potions; every kill mends you", null,
                    new RuleDef { kind = RuleKind.Constrain, ops = { new OpDef { op = "Forbid", mode = "Potion" } } },
                    Trig("Kill", new OpDef { op = "Heal" }))),
            new Case("뒤바뀐 박자", "Change the trigger of other abilities (rule rewrite)",
                R("Dash abilities wait for a perfect dodge — but then they strike thrice", null,
                    new RuleDef { kind = RuleKind.Meta, select = "ByTrigger", selectArg = "Dash", ops = { new OpDef { op = "RetriggerTagged", from = "Dash", to = "PerfectDodge" } } },
                    new RuleDef { kind = RuleKind.Meta, select = "ByTrigger", selectArg = "Dash", ops = { new OpDef { op = "AmplifyTagged" } } },
                    Trig("PerfectDodge", new OpDef { op = "ResetCooldowns" }, new OpDef { op = "GainDash" }))),
            new Case("교대하는 원소", "Alternate between two elements (alternating artifact)",
                M("Fire and frost take turns", new MechanicIntent { type = "Effect", on = "ComboFinish", effect = "Nova", element = "Fire", rel = "Alternate", at = "Self" })),
            new Case("지뢰밭", "Spawn traps that trigger another effect when they end (trap chain)",
                R("Mines that scatter blades", null,
                    Trig("DashEnd", new OpDef { op = "Spawn", entity = "Mine", at = "DashOrigin" }),
                    new RuleDef { kind = RuleKind.Trigger, trigger = "EntityExpired", param = "Mine", ops = { new OpDef { op = "Projectile", at = "EventPos", mode = "Ring", rel = "Bounce", times = 2 } } })),
            new Case("주기 장판", "Periodic temporal effect (ticking artifact)",
                M("Every few seconds the ground burns around you", new MechanicIntent { type = "Effect", on = "Every", period = 5, effect = "Nova", at = "Self", element = "Fire", time = "Periodic" })),
        };
    }
}
