using System.Collections.Generic;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Hand-written Rule IR abilities that are structurally different from each other (design doc #29–34 + Blood Covenant),
    /// plus samples the validator MUST reject. Used by Diverse/능력 파이프라인 검증 and the smoke test:
    /// if all of these run on the same runtime with no special cases, the architecture holds.
    /// </summary>
    public static class RuleSamples
    {
        static OpDef Op(string op) => new OpDef { op = op };

        public static List<AbilityDef> Accepted() => new List<AbilityDef>
        {
            // 잔영 반격: perfect dodge ×3 → a clone that copies your attacks
            new AbilityDef
            {
                name = "잔영 반격", concept = "Perfect dodges pile up afterimages; the third one steps out and copies your strikes",
                states = { new StateDef { name = "echo", label = "잔상", type = "Counter", max = 3 } },
                rules =
                {
                    new RuleDef { kind = RuleKind.Trigger, trigger = "PerfectDodge", ops = { new OpDef { op = "AddState", state = "echo", amount = 1 } } },
                    new RuleDef { kind = RuleKind.Trigger, trigger = "StateFull", param = "echo", ops =
                    {
                        new OpDef { op = "Spawn", entity = "Clone", rel = "Copy", at = "Self" },
                        new OpDef { op = "SetState", state = "echo", amount = 0 },
                    } },
                },
            },
            // 역병: a poisoned enemy's death spreads the poison
            new AbilityDef
            {
                name = "역병", concept = "Poison outlives its host",
                rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Kill", when = { new CondDef { type = "TargetHas", text = "poison" } }, ops = { new OpDef { op = "Spread", status = "poison", count = 3 } } } },
            },
            // 고통의 기억: store damage taken, release it on a perfect dodge
            new AbilityDef
            {
                name = "고통의 기억", concept = "Pain is remembered and returned on your next dash",
                states = { new StateDef { name = "pain", label = "고통", type = "StoredValue", max = 8 } },
                rules =
                {
                    new RuleDef { kind = RuleKind.Trigger, trigger = "Damaged", ops = { new OpDef { op = "AddState", state = "pain", scale = "event.amount", per = 1 } } },
                    new RuleDef { kind = RuleKind.Trigger, trigger = "Dash", when = { new CondDef { type = "Compare", a = "state:pain", cmp = ">", value = 0 } }, ops =
                    {
                        new OpDef { op = "Nova", at = "Self", scale = "state:pain" },
                        new OpDef { op = "SetState", state = "pain", amount = 0 },
                    } },
                },
            },
            // 원소 집착: scale with the build — every FIRE ability raises element power
            new AbilityDef
            {
                name = "원소 집착", concept = "Each fire ability you own feeds every other",
                rules = { new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "ModifyStat", stat = "ElementPower", scale = "build.tag:FIRE", per = 0.08f } } } },
            },
            // 메아리치는 불꽃: meta — other FIRE abilities fire again at reduced power
            new AbilityDef
            {
                name = "메아리치는 불꽃", concept = "Fire answers itself",
                rules = { new RuleDef { kind = RuleKind.Meta, ops = { new OpDef { op = "RepeatTagged", tag = "FIRE", amount = 0.4f } } } },
            },
            // 도박사의 선택: system — fewer evolution cards, but stranger ones
            new AbilityDef
            {
                name = "도박사의 선택", concept = "Fewer choices, wilder ones",
                rules = { new RuleDef { kind = RuleKind.System, ops = { new OpDef { op = "OfferCount", amount = -1 }, new OpDef { op = "TierBias", amount = 1 } } } },
            },
            // 피의 계약: trade-off + scaling + rule redefinition
            new AbilityDef
            {
                name = "피의 계약", concept = "Give up health; every missing drop sharpens the blade; near death, healing hardens into a shield",
                rules =
                {
                    new RuleDef { kind = RuleKind.Continuous, ops =
                    {
                        new OpDef { op = "ModifyStat", stat = "MaxHp", amount = -0.3f },
                        new OpDef { op = "ModifyStat", stat = "Attack", scale = "self.missingHpPct", per = 0.5f },
                    } },
                    new RuleDef { kind = RuleKind.Continuous, when = { new CondDef { type = "Compare", a = "self.hpPct", cmp = "<=", value = 0.3f } }, ops = { new OpDef { op = "Convert", from = "Heal", to = "Shield", amount = 1 } } },
                },
            },
        };

        /// <summary>A FIRE source so the meta/build samples have something to act on.</summary>
        public static AbilityDef FireSource() => new AbilityDef
        {
            name = "불씨", concept = "Combos ignite",
            rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "ComboFinish", ops = { new OpDef { op = "Nova", at = "Self", element = "Fire" } } } },
        };

        /// <summary>Owned by the hostile-test build: closes a Kill → Hit loop with any Hit → area-damage candidate.</summary>
        public static AbilityDef LoopPartner() => new AbilityDef
        {
            name = "사방 칼날",
            rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Kill", ops = { new OpDef { op = "Projectile", at = "EventPos", mode = "Ring", count = 5 } } } },
        };

        public static AbilityDef DashSource() => new AbilityDef
        {
            name = "남겨진 바람", concept = "Dashes leave a gust that cuts",
            rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Dash", ops = { new OpDef { op = "Nova", at = "DashOrigin", element = "Wind" } } } },
        };

        public static AbilityDef SkillSource() => new AbilityDef
        {
            name = "공명하는 주문", concept = "Skills ring out",
            rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "SkillCast", ops = { new OpDef { op = "Nova", at = "Self", element = "Arcane" } } } },
        };

        public static AbilityDef FrostSource() => new AbilityDef
        {
            name = "서리 낙인", concept = "Combos chill",
            rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Kill", ops = { new OpDef { op = "Nova", at = "EventPos", element = "Frost" } } } },
        };

        public static AbilityDef PoisonSource() => new AbilityDef
        {
            name = "독니", concept = "Hits sometimes poison",
            rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Hit", when = { new CondDef { type = "Chance", value = 0.3f } }, ops = { new OpDef { op = "ApplyStatus", target = "EventTarget", status = "poison" } } } },
        };

        public static AbilityDef CloneSource() => new AbilityDef
        {
            name = "흩어지는 잔상", concept = "Dashes leave fighting afterimages",
            rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Dash", cooldown = 3, ops = { new OpDef { op = "Spawn", entity = "Clone", rel = "Copy", at = "DashOrigin" } } } },
        };

        public static AbilityDef FireSource2() => new AbilityDef
        {
            name = "타는 낙인", concept = "Crits set enemies ablaze",
            rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Crit", ops = { new OpDef { op = "ApplyStatus", target = "EventTarget", status = "burn" } } } },
        };

        /// <summary>(sample, expected outcome): "reject" must fail; "repair" must pass only after a repair with this code.</summary>
        public static List<(AbilityDef a, string expect, string code)> Hostile() => new List<(AbilityDef, string, string)>
        {
            // with LoopPartner() owned: Hit → Nova (kills) → Kill → projectiles (hits) → Hit …
            (new AbilityDef { name = "무한 연쇄 폭발", rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Hit", ops = { new OpDef { op = "Nova", at = "EventPos" } } } } }, "repair", "FEEDBACK_LOOP"),
            (new AbilityDef { name = "대상 없는 일격", rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "PerfectDodge", ops = { new OpDef { op = "Damage", target = "EventTarget" } } } } }, "repair", "TARGET_UNAVAILABLE"),
            (new AbilityDef { name = "검을 쥔 분신", rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Dash", ops = { new OpDef { op = "Spawn", entity = "Clone", mode = "Equip", at = "Self" } } } } }, "reject", "GRIP"),
            (new AbilityDef { name = "없는 상태", rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Hit", when = { new CondDef { type = "Compare", a = "state:ghost", cmp = ">=", value = 3 } }, ops = { new OpDef { op = "Nova", at = "Self" } } } } }, "reject", "UNKNOWN_STATE"),
            (new AbilityDef { name = "시간 역행", rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "TimeReverse", ops = { Op("Rewind") } } } }, "reject", "UNKNOWN_TRIGGER"),
            (new AbilityDef { name = "임의 변환", rules = { new RuleDef { kind = RuleKind.Continuous, ops = { new OpDef { op = "Convert", from = "Damage", to = "Gold" } } } } }, "reject", "CONVERT_PAIR"),
            (new AbilityDef { name = "쌓기만 하는 상태", states = { new StateDef { name = "s", type = "Counter", max = 3 } }, rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Dash", when = { new CondDef { type = "Compare", a = "state:s", cmp = ">=", value = 3 } }, ops = { new OpDef { op = "Nova", at = "Self" } } } } }, "reject", "STATE_NEVER_WRITTEN"),
        };
    }
}
