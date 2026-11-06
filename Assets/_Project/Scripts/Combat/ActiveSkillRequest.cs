using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Diverse
{
    /// <summary>Observable request constraints, never inferred from a skill's display name.</summary>
    public sealed class ActiveSkillRequest
    {
        public readonly List<string> deliveries = new List<string>(), effects = new List<string>(), patterns = new List<string>(), elements = new List<string>();
        public string input, unsupported;
        public bool Constrained => deliveries.Count + effects.Count + patterns.Count + elements.Count > 0 || input != null;
        [Serializable] class Meaning
        {
            public string[] deliveries, effects, patterns, elements;
            public string input, unsupported;
        }
        public IEnumerator Interpret(string text, Action<string> trace)
        {
            if (string.IsNullOrWhiteSpace(text) || unsupported != null) yield break;
            if (!AiClient.Enabled)
            {
                if (!Constrained) unsupported = "AI 연결이 없어 요청의 동작을 해석하지 못했어. 대시·보호막·관통·충전 같은 지원 동작으로 요청해 줘. 이전 후보는 유지할게.";
                else trace?.Invoke("AI 해석 연결 없음 · 명시한 지원 동작만 필수 조건으로 검사");
                yield break;
            }
            string json = null;
            yield return AiClient.Complete("Extract ONLY explicitly requested executable requirements, never invent extra requirements. JSON only: {\"deliveries\":[],\"effects\":[],\"patterns\":[],\"elements\":[],\"input\":\"\",\"unsupported\":\"\"}. " +
                "delivery: arc,line,projectile,nova,field,dash,retreat,blink,guard,trap,wall; effects: damage,shield,heal,pull,push,stun,mark,block; patterns: single,fan,ring,chain,return,pierce,seek,curve,bounce,converge; " +
                "elements: Fire,Frost,Lightning,Shadow,Holy,Poison,Wind,Blood,Void,Arcane; input: tap,charge,channel,recast. Every extracted value is mandatory for all changed candidates. " +
                "대시/돌진 means dash, never blink/retreat. For alternatives expressed with OR choose one feasible interpretation, not both. Return short Korean unsupported explanation for requests impossible with this language (summons, time stop, terrain, passive casts, negative exclusions). Cosmetic requests need no constraints. No gameplay numbers or internal reasoning.", text, .1f, 700, s => json = s);
            if (json != null && Game.I?.World != null)
            {
                Game.I.World.designCalls++;
                Game.I.World.designCostUsd += AiClient.CostUsd(Game.Settings.aiModel, AiClient.LastPromptTokens, AiClient.LastCompletionTokens) ?? 0;
            }
            Meaning meaning = null; try { if (json != null) meaning = UnityEngine.JsonUtility.FromJson<Meaning>(json); } catch (Exception) { }
            if (meaning == null)
            {
                if (!Constrained) unsupported = "요청 해석 응답을 읽지 못했어. 이전 후보를 유지할게. 원하는 실제 동작을 짧게 다시 적어 줘.";
                else trace?.Invoke("AI 요청 해석 실패 · 직접 확인한 명시 동작으로 검사 계속");
                yield break;
            }
            if (!string.IsNullOrWhiteSpace(meaning.unsupported)) { unsupported = DesignGuard.CleanImagePrompt(meaning.unsupported) + " 이전 후보는 유지할게."; yield break; }
            bool valid = true;
            void Merge(List<string> into, string[] values, string allowed)
            {
                foreach (var v in values ?? Array.Empty<string>())
                    if (allowed.Split(',').Contains(v)) { if (!into.Contains(v)) into.Add(v); } else valid = false;
            }
            Merge(deliveries, meaning.deliveries, "arc,line,projectile,nova,field,dash,retreat,blink,guard,trap,wall");
            Merge(effects, meaning.effects, "damage,shield,heal,pull,push,stun,mark,block");
            Merge(patterns, meaning.patterns, "single,fan,ring,chain,return,pierce,seek,curve,bounce,converge");
            Merge(elements, meaning.elements, "Fire,Frost,Lightning,Shadow,Holy,Poison,Wind,Blood,Void,Arcane");
            if (!string.IsNullOrEmpty(meaning.input))
            {
                if (!new[] { "tap", "charge", "channel", "recast" }.Contains(meaning.input) || input != null && input != meaning.input) valid = false;
                else input = meaning.input;
            }
            if (!valid) unsupported = "지원하지 않는 동작 또는 서로 충돌하는 입력 해석이 있어. 이전 후보를 유지할게. 요청을 나누어 다시 적어 줘.";
        }
        public static ActiveSkillRequest Read(string text)
        {
            var r = new ActiveSkillRequest(); text = (text ?? "").ToLowerInvariant();
            bool Has(string words) => words.Split('|').Any(w => text.Contains(w));
            void Need(List<string> list, string token, string words) { if (Has(words)) list.Add(token); }
            Need(r.deliveries, "dash", "대시|대쉬|돌진|dash");
            Need(r.deliveries, "blink", "순간이동|순간 이동|점멸|텔레포트|blink");
            Need(r.deliveries, "retreat", "후퇴|백스텝|retreat");
            Need(r.deliveries, "trap", "덫|함정|trap"); Need(r.deliveries, "wall", "장벽|탄환 차단|wall");
            Need(r.effects, "pull", "끌어|끌어당|집결|한곳에 모|한 곳에 모|pull");
            Need(r.effects, "push", "밀치|넉백|push"); Need(r.effects, "stun", "기절|스턴|stun");
            Need(r.effects, "shield", "보호막|shield"); Need(r.effects, "heal", "회복|치유|heal");
            Need(r.patterns, "pierce", "관통|pierce"); Need(r.patterns, "seek", "유도|추적|seek");
            Need(r.patterns, "bounce", "도탄|반사탄|bounce"); Need(r.patterns, "fan", "부채꼴|fan");
            Need(r.elements, "Fire", "화염|불꽃|fire"); Need(r.elements, "Frost", "얼음|냉기|frost");
            Need(r.elements, "Lightning", "번개|뇌전|lightning"); Need(r.elements, "Poison", "중독|독성|poison");
            if (Has("재입력|돌아오|복귀|recast")) r.input = "recast";
            else if (Has("충전|차지|charge")) r.input = "charge";
            else if (Has("누르는 동안|유지형|채널|channel")) r.input = "channel";
            if (Has("소환|분신|시간 정지|시간정지|지형 생성|무한|자동 시전|자동시전"))
                r.unsupported = "소환·시간 정지·지형 생성·무한/자동 시전은 현재 실행 언어가 지원하지 않아. 이동·투사체·영역·제어·방어와 직접 입력을 조합해 요청해 줘.";
            if (Regex.IsMatch(text, "(대시|대쉬|돌진|보호막|회복|기절|관통).{0,5}(말고|없이|제외|빼고|않)"))
                r.unsupported = "제외 조건은 아직 정확하게 검증할 수 없어. 원하는 동작을 긍정형으로 적어 줘. 기존 후보는 유지할게.";
            if (Regex.IsMatch(text, "(종속\\s*스킬|종속형|연계\\s*묶음|hard dependenc|linked skills)(?!\\s*(말고|없이|제외|빼고))"))
                r.unsupported = "현재 성장 보상·추가 의뢰는 한 슬롯의 독립 스킬만 생성해. Q의 표식이 있어야 W를 쓸 수 있는 종속 묶음은 아직 획득 경로가 없어. 한 스킬 안의 재입력 연계나, 다른 스킬 없이도 쓸 수 있는 상태 시너지는 요청할 수 있어. 기존 후보는 유지할게.";
            return r;
        }
        public bool Matches(ActiveSkillData a) => a != null && a.steps != null &&
            deliveries.All(x => a.steps.Any(s => s.delivery == x)) && effects.All(x => a.steps.Any(s => s.effect == x)) &&
            patterns.All(x => a.steps.Any(s => s.pattern == x)) && elements.All(x => a.steps.Any(s => s.element == x)) && (input == null || a.input == input);
        IEnumerable<string> Tokens => deliveries.Concat(effects).Concat(patterns).Concat(elements).Concat(input == null ? Array.Empty<string>() : new[] { input });
        public string MachineSummary => string.Join(", ", Tokens);
        public string Summary => string.Join(", ", Tokens.Select(Label));
        public static string Label(string token) => token switch {
            "dash" => "돌진", "blink" => "점멸", "retreat" => "후퇴", "trap" => "덫", "wall" => "탄환 장벽", "guard" => "방어",
            "arc" => "베기", "line" => "직선", "projectile" => "투사체", "nova" => "주변 폭발", "field" => "지면 영역",
            "damage" => "피해", "shield" => "보호막", "heal" => "회복", "pull" => "끌어당김", "push" => "밀치기", "stun" => "기절", "mark" => "표식", "block" => "탄환 차단",
            "pierce" => "관통", "seek" => "유도", "bounce" => "벽 반사", "fan" => "부채꼴", "single" => "단일", "ring" => "방사", "chain" => "연쇄", "return" => "회수", "curve" => "곡선", "converge" => "수렴",
            "tap" => "한 번 입력", "charge" => "충전", "channel" => "유지 입력", "recast" => "재입력",
            "Fire" => "화염", "Frost" => "냉기", "Lightning" => "번개", "Poison" => "독", "Wind" => "바람", "Blood" => "혈기", "Arcane" => "비전", "Shadow" => "어둠", "Holy" => "신성", "Void" => "공허", _ => token };
        public string Role => deliveries.Any(x => x == "dash" || x == "retreat" || x == "blink") ? "mobility" :
            effects.Any(x => x == "shield" || x == "heal") || deliveries.Contains("wall") ? "guard" : deliveries.Contains("trap") ? "trap" :
            effects.Any(x => x == "pull" || x == "push" || x == "stun" || x == "mark") ? "control" : input == "channel" ? "sustain" : patterns.Count > 0 && input != "charge" ? "precision" : "burst";

        public ActiveSkillData Local(WeaponType type, int slot, int variant)
        {
            int flavor = variant / 3 % 4; variant %= 3;
            var a = ActiveSkillGenerator.Local(type, slot, Role);
            a.steps.Clear(); a.input = input ?? (variant == 1 && deliveries.Count > 0 ? "recast" : variant == 2 ? "charge" : "tap");
            ActiveStep Op(string d, string e = "damage") => new ActiveStep { delivery = d, effect = e, element = e == "damage" ? elements.FirstOrDefault() ?? "None" : "None" };
            foreach (var d in deliveries) a.steps.Add(Op(d, d == "wall" ? "block" : d == "guard" ? "shield" : "damage"));
            foreach (var e in effects) if (!a.steps.Any(s => s.effect == e)) a.steps.Add(Op(e == "shield" ? "guard" : e == "block" ? "wall" : "nova", e));
            if (patterns.Count > 0)
                foreach (var p in patterns) { var s = Op(p == "chain" ? "line" : "projectile"); s.pattern = p; a.steps.Add(s); }
            if (a.steps.Count == 0 || a.steps.All(s => s.effect != "damage") && elements.Count > 0)
                a.steps.Add(Op(type == WeaponType.Bow || type == WeaponType.Gun ? "projectile" : "arc"));
            if (a.input == "recast")
            {
                var s = Op(deliveries.Contains("dash") ? "dash" : "nova"); s.trigger = "recast";
                s.origin = s.delivery == "dash" ? "previous" : "caster"; a.steps.Add(s);
            }
            else if (variant == 0 && a.steps.Count < ActiveSkillGenerator.Rules.maxSteps)
            { var s = Op("nova"); s.trigger = ActiveSkillGenerator.Moving(a.steps.Last()) ? "arrive" : "hit"; s.origin = ActiveSkillGenerator.Moving(a.steps.Last()) ? "caster" : a.steps.Last().effect == "damage" ? "impact" : "caster";
                if (a.steps.Last().effect != "damage") s.trigger = "after"; a.steps.Add(s); }
            if (flavor > 0 && a.steps.Count < ActiveSkillGenerator.Rules.maxSteps)
            {
                bool ranged = type == WeaponType.Bow || type == WeaponType.Gun;
                var follow = Op(ranged ? "projectile" : flavor == 1 ? "arc" : "line");
                if (ranged) follow.pattern = flavor == 1 ? "fan" : flavor == 2 ? "curve" : "pierce";
                follow.timing = flavor == 3 ? "delayed" : "instant"; a.steps.Add(follow);
            }
            a.name = (deliveries.Contains("dash") ? "돌진" : "요청의 궤적") + " · " + Label(a.input);
            a.role = ActiveSkillGenerator.InferRole(a); a.intent = a.designReason = "요청한 실행 조건을 유지합니다. " + Label(a.input) + ": " + string.Join(" → ", a.steps.Select(s => Label(s.delivery)));
            a.iconPrompt = "one isolated chunky pixel symbol: " + ActiveSkillGenerator.VisualSummary(a);
            return a;
        }
    }
}
