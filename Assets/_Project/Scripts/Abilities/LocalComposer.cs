using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 오프라인(키 없음/실패) 시 쓰는 로컬 생성기.
    /// 완성 스킬 목록이 아니라 "원자 조합 규칙"으로 그래프를 조립한다 — AI와 같은 출력 형식이라 뒤 파이프라인을 그대로 탄다.
    /// 플레이어 패턴/태그에 맞는 트리거·효과에 가중치를 주고, 탐색용으로 가끔 먼 조합도 섞는다.
    /// </summary>
    public static class LocalComposer
    {
        static readonly string[] Elements = { "Fire", "Frost", "Lightning", "Shadow", "Holy", "Poison", "Wind" };

        public static List<AbilityGraph> Compose(GenerationContext c, int count, System.Random rnd)
        {
            var results = new List<AbilityGraph>();
            int guard = 0;
            while (results.Count < count * 3 && guard++ < 60)
            {
                var g = rnd.NextDouble() < 0.18 ? ComposeSimple(c, rnd) : ComposeTrigger(c, rnd);
                if (g == null) continue;
                g.source = "local";
                if (results.Any(r => r.mechanic == g.mechanic)) continue;
                results.Add(g);
            }
            return results;
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

        static AbilityGraph ComposeTrigger(GenerationContext c, System.Random r)
        {
            // 1) 트리거: 플레이어 패턴과 관련된 것에 가중치
            var trig = Weighted(r, new List<(string, float)>
            {
                ("Dash", Rel(c, "DODGE MOBILITY") * 1.2f), ("PerfectDodge", Rel(c, "DODGE") * 0.6f), ("Hit", Rel(c, "MELEE RANGED ATTACK")),
                ("Crit", Rel(c, "CRIT")), ("Kill", Rel(c, "KILL") * 1.1f), ("Damaged", Rel(c, "DEFENSE GUARD")), ("SkillCast", Rel(c, "SKILL MAGIC")),
                ("ComboFinish", Rel(c, "MELEE COMBO")), ("LowHealth", Rel(c, "DEFENSE") * 0.5f), ("Interval", 0.5f),
                ("CloneSpawn", c.relevantTags.Contains("CLONE") ? 1.4f : 0f), ("StatusApplied", c.relevantTags.Contains("STATUS") ? 1f : 0.1f),
                ("Guard", c.relevantTags.Contains("SHIELD") ? 1.6f : 0f), ("DashEnd", Rel(c, "DODGE") * 0.5f),
            });

            var g = new AbilityGraph { trigger = trig, kind = "trigger" };

            // 2) 효과: Seeded 소스가 있으면 그 생성자를 우선 (예: 분신 피해 보유 → 분신 생성)
            var e = new EffectNode();
            bool seededClone = c.seededSources.Contains("CLONE");
            bool seededOrb = c.seededSources.Contains("ORB");
            string action = Weighted(r, new List<(string, float)>
            {
                ("Damage", 1f), ("Nova", Rel(c, "AREA MELEE")), ("Projectile", Rel(c, "RANGED PROJECTILE")), ("Lightning", Rel(c, "LIGHTNING MAGIC") * 0.8f),
                ("Spawn", (seededClone || seededOrb ? 4f : 0.9f) * (trig is "CloneSpawn" ? 0 : 1)), ("Heal", trig is "Damaged" or "Kill" or "LowHealth" ? 1.2f : 0.3f),
                ("Shield", trig is "Damaged" or "Dash" or "Guard" or "LowHealth" ? 1.2f : 0.2f), ("Buff", 0.8f), ("ApplyStatus", Rel(c, "STATUS") * 0.7f),
                ("Pull", 0.3f), ("Push", trig is "Damaged" or "Guard" ? 0.8f : 0.2f), ("Blink", trig is "Kill" or "PerfectDodge" ? 0.5f : 0.1f),
                ("ResetCooldown", trig is "Kill" or "PerfectDodge" or "Crit" ? 0.8f : 0.1f),
            });
            e.action = action;

            switch (action)
            {
                case "Spawn":
                    e.entity = seededClone ? "Clone" : seededOrb ? "Orb" : Pick(r, new[] { "Clone", "Turret", "Orb", "Zone", "PhantomWeapon" });
                    if (e.entity == "Clone") e.relation = r.NextDouble() < 0.6 ? "Copy" : "Inherit";
                    if (e.entity == "Orb") e.relation = "Orbit";
                    if (e.entity == "Zone") e.element = Pick(r, Elements);
                    e.form = trig is "Dash" or "PerfectDodge" ? "DashOrigin" : trig is "Kill" or "Hit" or "Crit" ? "AtTarget" : "AtSelf";
                    break;
                case "Damage":
                    e.form = trig is "Dash" or "DashEnd" ? (r.NextDouble() < 0.5 ? "DashPath" : "DashOrigin") : trig is "Kill" ? "AtTarget" : "AtTarget";
                    if (r.NextDouble() < 0.55) e.element = Pick(r, Elements);
                    if (r.NextDouble() < 0.25) e.relation = "Repeat";
                    break;
                case "Nova":
                    e.form = trig is "Kill" or "Hit" or "Crit" ? "AtTarget" : trig is "Dash" ? "DashOrigin" : "AtSelf";
                    e.element = Pick(r, Elements);
                    if (trig == "StatusApplied" || r.NextDouble() < 0.15) e.relation = "Consume";
                    break;
                case "Projectile":
                    e.form = trig is "Kill" or "Hit" ? "AtTarget" : r.NextDouble() < 0.5 ? "Ring" : "Forward";
                    if (r.NextDouble() < 0.5) e.element = Pick(r, Elements);
                    break;
                case "Lightning":
                    e.element = "Lightning";
                    e.relation = r.NextDouble() < 0.6 ? "Chain" : null;
                    break;
                case "Buff":
                    e.stat = Pick(r, new[] { "Attack", "AttackSpeed", "MoveSpeed", "CritChance", "Armor" });
                    if (c.relevantTags.Contains("CRIT") && r.NextDouble() < 0.5) e.stat = "CritChance";
                    break;
                case "ApplyStatus":
                    e.element = Pick(r, new[] { "Fire", "Frost", "Poison", "Shadow" });
                    e.relation = r.NextDouble() < 0.3 ? "Mark" : null;
                    break;
                case "Pull": case "Push": e.form = trig is "Kill" ? "AtTarget" : "AtSelf"; break;
            }
            g.effects.Add(e);

            // 3) 가끔 두 번째 효과 (작은 보조 효과)
            if (r.NextDouble() < 0.25 && action is not "Spawn")
            {
                var e2 = new EffectNode { action = Pick(r, new[] { "ApplyStatus", "Push", "Heal", "Buff" }) };
                if (e2.action == "ApplyStatus") e2.element = e.element ?? Pick(r, new[] { "Fire", "Frost", "Poison" });
                if (e2.action == "Buff") e2.stat = "MoveSpeed";
                if (e2.action == "Push") e2.form = e.form ?? "AtSelf";
                if (!(e2.action == e.action && e2.stat == e.stat)) g.effects.Add(e2);
            }

            // 4) 조건
            if (trig is "Hit" or "Attack" or "StatusApplied") g.conditions.Add(new CondNode { type = "Chance", value = r.NextDouble() < 0.5 ? 0.2f : 0.3f });
            else if (trig is "Crit" && r.NextDouble() < 0.3) g.conditions.Add(new CondNode { type = "Cooldown", value = 1.5f });
            else if (trig is "Interval") g.conditions.Add(new CondNode { type = "Cooldown", value = 3 + (float)r.NextDouble() * 3 });
            else if (trig is "ComboFinish" && r.NextDouble() < 0.4) g.conditions.Add(new CondNode { type = "EveryNth", value = 2 });
            if (e.relation == "Consume") g.conditions.Add(new CondNode { type = "TargetHasStatus", text = e.element == "Frost" ? "chill" : e.element == "Poison" ? "poison" : "burn" });

            g.mechanic = Mechanic(g);
            g.semanticReason = Reason(c, g);
            return g;
        }

        static AbilityGraph ComposeSimple(GenerationContext c, System.Random r)
        {
            // Seeded 생성 또는 기존 태그 강화(modifier) / 스탯
            var ownedTags = c.owned.SelectMany(o => o.tags).Distinct().ToList();
            if (r.NextDouble() < 0.6 && ownedTags.Count > 0)
            {
                string tag = Pick(r, ownedTags);
                if (c.owned.Any(o => o.kind == "modifier" && o.modTag == tag)) return null;
                return new AbilityGraph { kind = "modifier", modTag = tag, modMul = 1.25f, mechanic = "amp_" + tag.ToLower(), tags = new List<string> { tag }, semanticReason = $"이미 가진 [{tag}] 효과들을 한꺼번에 강화" };
            }
            // 처음엔 Seed가 될 modifier를 제시 (플레이어가 고르면 Intent 신호가 됨)
            if (r.NextDouble() < 0.4)
            {
                string seed = Pick(r, new[] { "CLONE", "ORB", "FIRE", "FROST", "LIGHTNING", "PROJECTILE" });
                if (c.owned.Any(o => o.kind == "modifier" && o.modTag == seed)) return null;
                return new AbilityGraph { kind = "modifier", modTag = seed, modMul = 1.25f, mechanic = "amp_" + seed.ToLower(), tags = new List<string> { seed }, semanticReason = $"아직 [{seed}] 원천이 없다. 고르면 그 방향의 진화가 열린다." };
            }
            var stat = Pick(r, new[] { StatId.MaxHp, StatId.Attack, StatId.AttackSpeed, StatId.MoveSpeed, StatId.CritChance, StatId.CritDamage, StatId.CooldownReduction, StatId.Regen });
            return new AbilityGraph { kind = "stat", stat = stat.ToString(), mechanic = "stat_" + stat.ToString().ToLower(), tags = new List<string> { "STAT" }, semanticReason = "기본기를 다진다" };
        }

        public static string Mechanic(AbilityGraph g)
        {
            var e = g.effects[0];
            string what = e.action.ToLower() + (string.IsNullOrEmpty(e.entity) ? "" : "_" + e.entity.ToLower()) + (string.IsNullOrEmpty(e.element) ? "" : "_" + e.element.ToLower());
            return $"{what}_on_{g.trigger.ToLower()}";
        }

        static string Reason(GenerationContext c, AbilityGraph g)
        {
            var p = c.patterns.FirstOrDefault(x => x.tags.Split(' ').Any(t => g.tags.Contains(t) || (g.trigger is "Dash" or "PerfectDodge" && t == "DODGE") || (g.trigger is "Kill" && t == "KILL") || (g.trigger is "Crit" && t == "CRIT")));
            if (p != null) return $"{p.desc} 패턴이 자주 보인다 → 그 순간을 능력으로";
            if (c.seededSources.Count > 0 && g.effects.Any(e => e.entity != null && c.seededSources.Contains(e.entity.ToUpper())))
                return $"이미 고른 [{string.Join(",", c.seededSources)}] 강화에 원천을 붙인다";
            return "새로운 방향을 탐색";
        }

        // ───────────── 이름 (LLM #2 의 로컬 대체) ─────────────

        public static void Name(AbilityGraph g)
        {
            if (!string.IsNullOrEmpty(g.name)) return;
            if (g.kind == "modifier")
            {
                g.name = g.modTag switch
                {
                    "CLONE" => "겹쳐진 그림자", "ORB" => "공명하는 별", "FIRE" => "타오르는 심장", "FROST" => "얼어붙은 숨결", "LIGHTNING" => "번개의 맥박",
                    "PROJECTILE" => "날 선 깃털", "DODGE" => "바람의 발걸음", "SWORD" => "벼린 칼날", "KILL" => "사냥꾼의 직감", "AREA" => "넓어지는 파문",
                    "DEFENSE" => "단단한 껍질", "CRIT" => "급소 읽기", "SHADOW" => "깊어지는 밤", "STATUS" => "스며드는 독기", _ => $"{g.modTag} 증폭",
                };
                g.desc = $"[{g.modTag}] 태그가 붙은 모든 효과의 위력 ×{g.modMul:0.##}";
                return;
            }
            if (g.kind == "stat")
            {
                var s = AbilityGraph.ParseStat(g.stat);
                g.name = s switch
                {
                    StatId.MaxHp => "튼튼한 다리", StatId.Attack => "힘줄 강화", StatId.AttackSpeed => "가벼운 손목", StatId.MoveSpeed => "토끼의 뜀박질",
                    StatId.CritChance => "날카로운 눈", StatId.CritDamage => "급소 찌르기", StatId.CooldownReduction => "맑은 정신", StatId.Regen => "따뜻한 숨",
                    _ => Stats.Label(s) + " 증가",
                };
                g.desc = g.Explain();
                return;
            }
            var e = g.effects[0];
            string el = e.element switch
            {
                "Fire" => "불꽃", "Frost" => "서리", "Lightning" => "번개", "Shadow" => "그림자", "Holy" => "빛", "Poison" => "독", "Wind" => "바람", _ => null,
            };
            string noun = e.action switch
            {
                "Spawn" => e.entity switch { "Clone" => "잔영", "Turret" => "파수꾼", "Orb" => "위성", "Zone" => "장막", _ => "환영검" },
                "Nova" => "파문", "Projectile" => "깃", "Lightning" => "낙뢰", "Heal" => "숨결", "Shield" => "보호막", "Buff" => "고양",
                "ApplyStatus" => "낙인", "Pull" => "소용돌이", "Push" => "충격", "Blink" => "도약", "ResetCooldown" => "가속", "Damage" => "일격", _ => "기술",
            };
            string verb = g.trigger switch
            {
                "Dash" => "남겨진", "DashEnd" => "멈춘 자리의", "PerfectDodge" => "아슬아슬한", "Kill" => "사냥꾼의", "Crit" => "급소의", "Hit" => "스치는",
                "Damaged" => "되받아치는", "SkillCast" => "공명하는", "ComboFinish" => "마무리", "LowHealth" => "벼랑 끝", "Interval" => "고요한",
                "CloneSpawn" => "겹치는", "StatusApplied" => "번지는", "Guard" => "막아선", _ => "",
            };
            g.name = (verb + " " + (el != null ? el + " " : "") + noun).Trim();
            g.desc = g.Explain();
        }
    }
}
