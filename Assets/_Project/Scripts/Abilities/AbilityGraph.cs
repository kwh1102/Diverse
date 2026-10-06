using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Ability Graph = Trigger(Event) → [Condition…] → Effect node chain.
    /// The structure the AI outputs (JSON). After validation, the balance system fills in the numbers and it's compiled.
    /// Modifier-type abilities (e.g. Clone damage ×1.25) attach to tags and apply to any matching effect automatically (Synergy Resolver).
    /// </summary>
    [Serializable]
    public class AbilityGraph
    {
        public string id;
        public string mechanic;              // machine-readable mechanic id (e.g. spawn_clone_after_dodge)
        public string name;                  // display name (generated in LLM #2)
        public string desc;                  // display description
        public string kind = "trigger";      // trigger | modifier | stat
        public string trigger;               // Event atom
        public List<CondNode> conditions = new List<CondNode>();
        public List<EffectNode> effects = new List<EffectNode>();
        public List<string> tags = new List<string>();      // synergy tags (CLONE DODGE…)
        public string semanticReason;        // why this ability suits the player (shown in UI)
        public string source = "local";      // local | ai
        public int tier;

        // modifier type
        public string modTag;                // which tag to amplify
        public float modMul;                 // multiplier (1.25 → ×1.25)
        // stat type
        public string stat;
        public float statValue;

        public float cost;                   // calculated budget cost

        public string Signature()
        {
            var sb = new StringBuilder(kind).Append('|').Append(trigger);
            foreach (var e in effects) sb.Append('|').Append(e.action).Append(':').Append(e.entity).Append(':').Append(e.element).Append(':').Append(e.relation);
            if (kind == "modifier") sb.Append('|').Append(modTag);
            if (kind == "stat") sb.Append('|').Append(stat);
            return sb.ToString();
        }

        public bool HasTag(string t) => tags.Contains(t);

        /// <summary>
        /// Player-facing description: when (whose action), under which condition (whose state, exact numbers), what happens
        /// (to whom, how much). Short, but no guessing needed: "내 체력이 30% 이하일 때" instead of "체력이 낮을 때".
        /// Damage is always relative to "공격력" (the hero's attack stat).
        /// </summary>
        public string Explain()
        {
            if (kind == "modifier") return $"[{TagLabel(modTag)}] 효과의 위력이 {(modMul - 1) * 100:0}% 증가한다";
            if (kind == "stat") { var sid = ParseStat(stat); bool pct = Stats.IsPercent(sid) || AbilityRuntime.IsMultiplicative(sid); return $"내 {Stats.Label(sid)} {(pct ? $"+{statValue * 100:0}%" : $"+{statValue:0.#}")} (영구)"; }
            var sb = new StringBuilder();
            var conds = new List<CondNode>(conditions);
            // "Interval" is really "every N seconds" (its cooldown): say that instead of "일정 시간마다, 최대 N초에 한 번"
            var every = trigger == "Interval" ? conds.Find(c => c.type == "Cooldown") : null;
            if (every != null) { sb.Append($"{every.value:0.#}초마다"); conds.Remove(every); }
            else sb.Append(Trigger.Label(trigger));
            // Conditions in reading order: who/what first, then chance, then rate limits
            conds.Sort((a, b) => CondNode.Order(a.type).CompareTo(CondNode.Order(b.type)));
            foreach (var c in conds) sb.Append(", ").Append(c.Label());
            sb.Append(": ");
            for (int i = 0; i < effects.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(effects[i].Label());
            }
            return sb.ToString();
        }

        /// <summary>Korean label for a synergy tag (falls back to the tag itself).</summary>
        public static string TagLabel(string tag) => tag switch
        {
            "CLONE" => "분신", "ORB" => "구슬", "TURRET" => "포탑", "ZONE" => "장판", "FIRE" => "화염", "FROST" => "냉기", "LIGHTNING" => "번개",
            "SHADOW" => "그림자", "HOLY" => "신성", "POISON" => "독", "WIND" => "바람", "PROJECTILE" => "투사체", "DODGE" => "회피",
            "SWORD" => "검", "KILL" => "처치", "AREA" => "범위", "DEFENSE" => "방어", "CRIT" => "치명타", "STATUS" => "상태이상",
            "MOBILITY" => "기동", "SKILL" => "기술", "MELEE" => "근접", "RANGED" => "원거리", _ => tag,
        };

        public static StatId ParseStat(string s) => Enum.TryParse<StatId>(s, out var id) ? id : StatId.Attack;

        public AbilityGraph Clone() => JsonUtility.FromJson<AbilityGraph>(JsonUtility.ToJson(this));
    }

    [Serializable]
    public class CondNode
    {
        public string type;          // Chance, TargetHasStatus, HealthBelow, WithinRange, Cooldown, EveryNth
        public float value;
        public string text;          // status id, etc.

        public string Label() => type switch
        {
            "Chance" => $"{value * 100:0}% 확률로",
            "TargetHasStatus" => $"대상이 {StatusLabel(text)} 상태라면",
            "HealthBelow" => $"내 체력이 {value * 100:0}% 이하일 때",
            "WithinRange" => $"대상이 {value:0.#}m 이내라면",
            "Cooldown" => $"최대 {value:0.#}초에 한 번",
            "EveryNth" => $"{value:0}번째마다",
            _ => type,
        };

        /// <summary>Reading order of conditions in a description.</summary>
        public static int Order(string type) => type switch
        {
            "HealthBelow" => 0, "TargetHasStatus" => 1, "WithinRange" => 2, "EveryNth" => 3, "Chance" => 4, "Cooldown" => 5, _ => 6,
        };

        public static string StatusLabel(string s) => s switch
        {
            "burn" => "화상", "chill" => "빙결", "poison" => "중독", "mark" => "표식", _ => s,
        };
    }

    [Serializable]
    public class EffectNode
    {
        public string action;        // Action atom
        public string entity;        // Clone/Turret/Orb/Zone/PhantomWeapon
        public string form;          // AtTarget/AtSelf/DashOrigin/DashPath/Forward/Ring
        public string element;       // Fire/Frost/...
        public string relation;      // Copy/Inherit/Repeat/Chain/Consume/Orbit/Mark/...
        public string temporal;      // Immediate/Delayed/Duration
        public string stat;          // for Buff
        // Numbers decided by the Balance Resolver (the AI's values are ignored)
        public float power;          // damage multiplier relative to base attack, or heal/shield %
        public float radius;
        public float duration;
        public float delay;
        public int count;

        /// <summary>One effect as a short sentence with exact numbers. "공격력의 N%" = damage relative to the hero's attack.</summary>
        public string Label()
        {
            string el = string.IsNullOrEmpty(element) ? "" : ElementLabel(element) + " ";
            string where = form switch
            {
                "DashOrigin" => "대시 시작 지점에 ", "DashPath" => "대시로 지나간 길의 적에게 ", "AtSelf" => "내 주변에 ", "Ring" => "내 둘레 사방으로 ",
                "Forward" => "커서 방향으로 ", "AtTarget" => "대상 위치에 ", _ => "",
            };
            string rel = relation switch
            {
                "Copy" => " — 분신이 내 공격을 따라 한다", "Inherit" => " — 분신이 내 강화 효과를 이어받는다", "Repeat" => " (잠시 후 60% 위력으로 한 번 더)",
                "Chain" => " (주변 적에게 튄다)", "Consume" => " — 대상의 상태이상을 없애고 위력 ×1.8", "Orbit" => " — 내 주위를 돈다",
                "Mark" => $" + 표식({duration:0.#}초 동안 받는 피해 +25%)", "SwapPosition" => " (위치 교환)", "Transfer" => " (적 2명 관통)", _ => "",
            };
            string delayS = delay > 0.01f ? $"{delay:0.#}초 뒤 " : "";
            string pow = $"공격력의 {power * 100:0}%";
            switch (action)
            {
                case "Damage":
                    return radius > 0 ? $"{delayS}{where}반경 {radius:0.#}m {el}피해 ({pow}){rel}"
                                      : $"{delayS}{where}{el}피해 ({pow}){rel}";
                case "Projectile": return $"{delayS}{where}{el}투사체 {count}발 발사 (각 {pow}){rel}";
                case "Nova": return $"{delayS}{where}반경 {radius:0.#}m {el}충격파 ({pow}){rel}";
                case "Lightning": return $"{delayS}대상에게 번개 ({pow}{(count > 1 ? $", 최대 {count}명까지 연쇄" : "")}){rel}";
                case "Spawn": return $"{delayS}{where}{EntityLabel(entity)} {count}개를 {duration:0.#}초 동안 소환 (공격 위력 {power * 100:0}%){rel}";
                case "Heal": return $"내 최대 체력의 {power * 100:0.#}% 회복";
                case "Shield": return $"{duration:0.#}초 동안 최대 체력 {power * 100:0}%만큼의 보호막";
                case "Buff": return $"{duration:0.#}초 동안 내 {Stats.Label(AbilityGraph.ParseStat(stat))} +{power * 100:0}%";
                case "ApplyStatus": return $"대상에게 {el}{StatusOf(element)} {duration:0.#}초{rel}";
                case "Pull": return $"{where}반경 {radius:0.#}m의 적을 끌어당긴다";
                case "Push": return $"{where}반경 {radius:0.#}m의 적을 밀쳐낸다";
                case "Blink": return $"대상 곁으로 순간이동 (최대 {radius:0.#}m)";
                case "ResetCooldown": return $"내 기술 재사용 대기시간 {power * 100:0}% 감소 + 대시 1회 충전";
                case "Store": return $"충전 1 축적 (최대 {count})";
                case "Release": return $"모은 충전을 방출해 내 주변 반경 {radius:0.#}m 피해 ({pow}, 충전 1당 +35%)";
            }
            return action;
        }

        static string StatusOf(string element) => element switch
        {
            "Frost" => "빙결(이동 둔화)", "Poison" => "중독(지속 피해)", "Shadow" => "표식(받는 피해 +25%)", _ => "화상(지속 피해)",
        };

        public static string EntityLabel(string e) => e switch
        {
            "Clone" => "잔상 분신", "Turret" => "포탑", "Orb" => "빛의 구슬", "Zone" => "장판", "PhantomWeapon" => "환영 무기", _ => e,
        };

        public static string ElementLabel(string e) => e switch
        {
            "Fire" => "화염", "Frost" => "냉기", "Lightning" => "번개", "Shadow" => "그림자", "Holy" => "신성", "Poison" => "독", "Wind" => "바람", _ => e,
        };

        public static Element ParseElement(string e) => Enum.TryParse<Element>(e, out var el) ? el : Element.None;
    }

    public static class Trigger
    {
        /// <summary>Whose action fires the ability, in plain words ("내가 적을 처치하면").</summary>
        public static string Label(string t) => t switch
        {
            "Attack" => "내가 기본 공격을 하면", "Hit" => "내 공격이 적에게 맞으면", "Crit" => "내 공격이 치명타로 맞으면", "Kill" => "내가 적을 처치하면",
            "Dash" => "내가 대시하면", "DashEnd" => "내 대시가 끝나면", "PerfectDodge" => "적의 공격을 아슬아슬하게 피하면(완벽 회피)", "Damaged" => "내가 피해를 받으면",
            "SkillCast" => "내가 기술(QWER)을 쓰면", "ComboFinish" => "기본 공격 연타의 마지막 타가 나가면", "LowHealth" => "내 체력이 30% 이하로 떨어지면", "Interval" => "일정 시간마다",
            "CloneSpawn" => "내 분신이 생기면", "CloneExpire" => "내 분신이 사라지면", "StatusApplied" => "내가 적에게 상태이상을 걸면", "Guard" => "내가 공격을 막아내면",
            _ => t,
        };

        public static Trig Parse(string t) => Enum.TryParse<Trig>(t, out var x) ? x : Trig.None;
    }
}
