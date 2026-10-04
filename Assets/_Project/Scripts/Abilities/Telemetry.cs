using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// ① Raw Gameplay 수집 → ② Feature 추출 → ③ Pattern 분석.
    /// AI는 여기 개입하지 않는다. 결정적인 일반 코드가 "사실"만 기록하고 통계로 압축한다.
    /// </summary>
    public class Telemetry
    {
        public struct Rec { public float t; public string a; }

        readonly List<Rec> log = new List<Rec>();       // 최근 행동 (시간창)
        const float Window = 180f;                       // 최근 3분
        public readonly Dictionary<string, int> totals = new Dictionary<string, int>();
        public readonly Dictionary<string, int> world = new Dictionary<string, int>();    // 전투 밖 행동 (탐험·구조·조사)
        public readonly Dictionary<string, int> intent = new Dictionary<string, int>();   // 플레이어가 고른 능력 태그
        public readonly Dictionary<Attr, int> investRecent = new Dictionary<Attr, int>();
        float distSum; int distSamples;
        float t;

        public void Record(string action)
        {
            log.Add(new Rec { t = t, a = action });
            totals[action] = totals.TryGetValue(action, out var n) ? n + 1 : 1;
        }

        public void RecordWorld(string kind)
        {
            world[kind] = world.TryGetValue(kind, out var n) ? n + 1 : 1;
        }

        public void RecordChoice(AbilityGraph g)
        {
            foreach (var tag in g.tags) intent[tag] = (intent.TryGetValue(tag, out var n) ? n : 0) + 1;
        }

        public void RecordInvest(Attr a) => investRecent[a] = (investRecent.TryGetValue(a, out var n) ? n : 0) + 1;

        public void Tick(float dt, float nearestEnemyDist)
        {
            t += dt;
            if (nearestEnemyDist < 12) { distSum += nearestEnemyDist; distSamples++; }
            while (log.Count > 0 && t - log[0].t > Window) log.RemoveAt(0);
        }

        int Count(string a) { int n = 0; foreach (var r in log) if (r.a == a) n++; return n; }

        /// <summary>A 이후 within초 안에 B가 일어난 비율.</summary>
        float Seq(string a, string b, float within)
        {
            int na = 0, follow = 0;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].a != a) continue;
                na++;
                for (int j = i + 1; j < log.Count && log[j].t - log[i].t <= within; j++)
                    if (log[j].a == b) { follow++; break; }
            }
            return na == 0 ? 0 : follow / (float)na;
        }

        // ───────────── ② Feature Extraction ─────────────

        public class Features
        {
            public int attacks, hits, crits, kills, dashes, perfectDodges, skills, damaged, guards, combos;
            public float critRate, dashFreq, dodgeToAttack, attackToDash, killChain, avgDist, skillRatio, guardRate;
            public float meleeRatio;
        }

        public Features Extract()
        {
            var f = new Features
            {
                attacks = Count("Attack"), hits = Count("Hit"), crits = Count("Crit"), kills = Count("Kill"),
                dashes = Count("Dash"), perfectDodges = Count("PerfectDodge"), skills = Count("SkillCast"),
                damaged = Count("Damaged"), guards = Count("Guard"), combos = Count("ComboFinish"),
            };
            int actions = Mathf.Max(1, f.attacks + f.dashes + f.skills);
            f.critRate = f.hits == 0 ? 0 : f.crits / (float)f.hits;
            f.dashFreq = f.dashes / (float)actions;
            f.skillRatio = f.skills / (float)actions;
            f.dodgeToAttack = Seq("Dash", "Attack", 0.8f);
            f.attackToDash = Seq("Attack", "Dash", 0.6f);
            f.killChain = Seq("Kill", "Kill", 2.5f);
            f.avgDist = distSamples == 0 ? 3 : distSum / distSamples;
            f.guardRate = f.damaged + f.guards == 0 ? 0 : f.guards / (float)(f.damaged + f.guards);
            int ranged = Count("RangedAttack");
            f.meleeRatio = f.attacks == 0 ? 0.5f : 1f - ranged / (float)f.attacks;
            return f;
        }

        // ───────────── ③ Pattern Analyzer ─────────────

        public class Pattern { public string id, desc, tags; public float confidence; }

        public List<Pattern> Patterns(Features f)
        {
            var list = new List<Pattern>();
            void P(string id, string desc, string tags, float c) { if (c > 0.25f) list.Add(new Pattern { id = id, desc = desc, tags = tags, confidence = Mathf.Clamp01(c) }); }

            P("DodgeThenStrike", "회피 직후 공격 (Dash → Attack)", "DODGE MELEE", f.dodgeToAttack * Mathf.Clamp01(f.dashes / 6f));
            P("HitAndRun", "치고 빠지기 (Attack → Dash)", "DODGE MOBILITY", f.attackToDash * Mathf.Clamp01(f.dashes / 6f));
            P("KillStreak", "연속 처치", "KILL", f.killChain * Mathf.Clamp01(f.kills / 6f));
            P("CritFisher", "치명타 위주", "CRIT", f.critRate * 2.2f);
            P("Brawler", "근접 난전", "MELEE AREA", Mathf.Clamp01((3.2f - f.avgDist) / 2.2f) * Mathf.Clamp01(f.attacks / 15f));
            P("Kiter", "거리 유지", "RANGED", Mathf.Clamp01((f.avgDist - 3.5f) / 3f));
            P("Caster", "스킬 위주", "SKILL MAGIC", f.skillRatio * 3f);
            P("PerfectDodger", "아슬아슬한 회피", "DODGE", Mathf.Clamp01(f.perfectDodges / 4f));
            P("Tank", "피해를 받아내며 싸움", "DEFENSE GUARD", Mathf.Clamp01(f.damaged / 12f) * Mathf.Clamp01(f.attacks / 10f));
            P("Comboist", "콤보 마무리", "MELEE COMBO", Mathf.Clamp01(f.combos / 8f));
            P("Guardian", "막고 반격", "SHIELD GUARD", f.guardRate * 1.5f);

            // 전투 밖 행동 (탐험·구조·조사)도 성장 방향에 반영
            int explore = world.TryGetValue("explore", out var e) ? e : 0;
            int rescue = world.TryGetValue("rescue", out var r) ? r : 0;
            int investigate = world.TryGetValue("investigate", out var iv) ? iv : 0;
            P("Explorer", "구석구석 탐험", "MOBILITY", Mathf.Clamp01(explore / 6f));
            P("Savior", "사람을 구함", "DEFENSE HOLY", Mathf.Clamp01(rescue / 2f));
            P("Scholar", "유적·제단 조사", "MAGIC", Mathf.Clamp01(investigate / 3f));

            list.Sort((a, b) => b.confidence.CompareTo(a.confidence));
            return list;
        }

        public List<string> Unused(Features f)
        {
            var u = new List<string>();
            if (f.dashes < 2) u.Add("Dodge");
            if (f.skills < 2) u.Add("Skill");
            if (f.guards == 0) u.Add("Guard/Parry");
            if (f.crits == 0) u.Add("Crit");
            return u;
        }

        public string Summary()
        {
            var f = Extract();
            var sb = new StringBuilder();
            sb.Append($"공격 {f.attacks} · 처치 {f.kills} · 대시 {f.dashes} · 스킬 {f.skills}");
            return sb.ToString();
        }

        public IEnumerable<KeyValuePair<string, int>> TopIntent(int n) => intent.OrderByDescending(kv => kv.Value).Take(n);
    }
}
