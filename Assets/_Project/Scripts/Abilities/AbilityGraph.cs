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

        /// <summary>Human-readable mechanic summary (for debugging/UI).</summary>
        public string Explain()
        {
            if (kind == "modifier") return $"[{modTag}] 효과 ×{modMul:0.##}";
            if (kind == "stat") { var sid = ParseStat(stat); bool pct = Stats.IsPercent(sid) || AbilityRuntime.IsMultiplicative(sid); return $"{Stats.Label(sid)} {(pct ? $"+{statValue * 100:0}%" : $"+{statValue:0.#}")}"; }
            var sb = new StringBuilder();
            sb.Append(Trigger.Label(trigger));
            foreach (var c in conditions) sb.Append(" · ").Append(c.Label());
            sb.Append(" → ");
            for (int i = 0; i < effects.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(effects[i].Label());
            }
            return sb.ToString();
        }

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
            "Chance" => $"{value * 100:0}% 확률",
            "TargetHasStatus" => $"대상이 {StatusLabel(text)} 상태",
            "HealthBelow" => $"체력 ≤ {value * 100:0}%",
            "WithinRange" => $"{value:0.#}m 이내",
            "Cooldown" => $"{value:0.#}초마다",
            "EveryNth" => $"{value:0}번째마다",
            _ => type,
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

        public string Label()
        {
            string el = string.IsNullOrEmpty(element) ? "" : ElementLabel(element) + " ";
            string where = form switch
            {
                "DashOrigin" => "대시 시작 지점에 ", "DashPath" => "대시 경로를 따라 ", "AtSelf" => "자신 주변에 ", "Ring" => "자신 둘레에 원형으로 ", "Forward" => "전방으로 ", _ => "",
            };
            string rel = relation switch
            {
                "Copy" => " (행동 복제)", "Inherit" => " (효과 계승)", "Repeat" => " (한 번 더 반복)", "Chain" => " (연쇄)", "Consume" => " (상태 소모해 증폭)",
                "Orbit" => " (공전)", "Mark" => " (표식)", "SwapPosition" => " (위치 교환)", _ => "",
            };
            string delayS = delay > 0.01f ? $"{delay:0.#}초 후 " : "";
            switch (action)
            {
                case "Damage": return $"{delayS}{where}{el}피해 {power * 100:0}%{(radius > 0 ? $" (반경 {radius:0.#})" : "")}{rel}";
                case "Projectile": return $"{delayS}{where}{el}투사체 {count}개 ({power * 100:0}%){rel}";
                case "Nova": return $"{delayS}{where}{el}충격파 {power * 100:0}% (반경 {radius:0.#}){rel}";
                case "Lightning": return $"{delayS}번개 {power * 100:0}%{(count > 1 ? $" ×{count}연쇄" : "")}{rel}";
                case "Spawn": return $"{delayS}{where}{EntityLabel(entity)} {count}개 소환 ({duration:0.#}초){rel}";
                case "Heal": return $"체력 {power * 100:0.#}% 회복";
                case "Shield": return $"보호막 {power * 100:0}% ({duration:0.#}초)";
                case "Buff": return $"{Stats.Label(AbilityGraph.ParseStat(stat))} +{power * 100:0}% ({duration:0.#}초)";
                case "ApplyStatus": return $"{el}상태이상 부여 ({duration:0.#}초)";
                case "Pull": return $"{where}적 끌어당기기 (반경 {radius:0.#})";
                case "Push": return $"{where}적 밀쳐내기 (반경 {radius:0.#})";
                case "Blink": return $"{radius:0.#}m 순간이동";
                case "ResetCooldown": return $"재사용 대기시간 -{power * 100:0}%";
                case "Store": return $"충전 축적 (최대 {count})";
                case "Release": return $"축적한 충전 방출 (반경 {radius:0.#})";
            }
            return action;
        }

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
        public static string Label(string t) => t switch
        {
            "Attack" => "공격 시", "Hit" => "적중 시", "Crit" => "치명타 시", "Kill" => "처치 시",
            "Dash" => "대시 시", "DashEnd" => "대시 종료 시", "PerfectDodge" => "완벽 회피 시", "Damaged" => "피격 시",
            "SkillCast" => "스킬 사용 시", "ComboFinish" => "콤보 마무리 시", "LowHealth" => "체력이 낮을 때", "Interval" => "주기적으로",
            "CloneSpawn" => "분신 소환 시", "CloneExpire" => "분신 소멸 시", "StatusApplied" => "상태이상 부여 시", "Guard" => "방어 성공 시",
            _ => t,
        };

        public static Trig Parse(string t) => Enum.TryParse<Trig>(t, out var x) ? x : Trig.None;
    }
}
