using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public static class ActiveSkillGenerator
    {
        public static ActiveRules Rules => DB.Asset.abilityRules.def.active;
        static readonly Queue<Technique> recent = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => recent.Clear();

        [Serializable] class Answer { public List<Proposal> skills; }
        [Serializable] class Proposal
        {
            public string technique, name, color, iconPrompt, effectPrompt;
        }

        public static WeaponType TypeOf(WeaponDef w) => w.type;
        public static string TypeName(WeaponType t) => t switch { WeaponType.Sword => "검", WeaponType.Bow => "활", WeaponType.Gun => "총", WeaponType.Staff => "봉", _ => "단검" };
        public static WeaponType TypeOf(InventoryItem w) => TypeOf(w.weapon);
        public static WeaponFootprint Shape(InventoryItem w) => DB.Asset.abilityRules.def.weaponShapes.First(s => s.type == TypeOf(w));
        public static string SlotName(int slot) => slot >= 0 && slot < 4 ? "QWER"[slot].ToString() : "?";
        public static string InputLabel(string input) => input switch {
            "charge" => "충전", "channel" => "유지 입력", "recast" => "재입력", _ => "한 번 입력"
        };

        public static IEnumerator GenerateChoices(Player owner, int slot, string request, List<ActiveSkillData> old, bool[] pinned,
            Action<List<ActiveSkillData>> done, Action<string> trace = null, bool allowAi = true)
        {
            var owned = owner.Inventory.items.Where(i => i.kind == ItemKind.Weapon).Select(TypeOf).Distinct().ToList();
            var current = TypeOf(owner.Inventory.Active);
            var types = owned.OrderBy(t => t == current ? 0 : 1).ToArray();
            var requestedType = types.Where(t => (request ?? "").StartsWith(TypeName(t) + " ", StringComparison.Ordinal)).DefaultIfEmpty(current).First();
            var explicitRule = ActiveTechniques.All.FirstOrDefault(r => (request ?? "").Contains(r.Name));
            var result = new ActiveSkillData[3];
            for (int i = 0; i < result.Length; i++)
                if (pinned[i] && old != null && i < old.Count && old[i].HasTechnique)
                    result[i] = old[i];

            var available = ActiveTechniques.All.Where(r => Allowed(r, requestedType) && result.All(a => a?.technique != r.Id)).ToList();
            var options = new ActiveTechniques.Rule[3][];
            var remaining = available.OrderBy(_ => UnityEngine.Random.value).ToList();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] != null) { options[i] = Array.Empty<ActiveTechniques.Rule>(); continue; }
                var preferred = remaining.Contains(explicitRule) ? new[] { explicitRule } : remaining
                    .OrderBy(r => r.Weapon == requestedType ? 0 : 1).ThenBy(r => recent.Contains(r.Id) ? 1 : 0).Take(5).ToArray();
                options[i] = preferred;
                remaining.RemoveAll(r => preferred.Contains(r));
            }

            Answer answer = null;
            if (allowAi && AiClient.Enabled)
            {
                string context = $"Slot {SlotName(slot)}, held weapon {requestedType}. Request: {request ?? "different tactical choices"}\n" +
                    string.Join("\n", options.Select((set, index) => $"{index}: " + string.Join(" | ", set.Select(r => $"{r.Id} ({r.Read}; visual motif {r.Motif})"))));
                string json = null;
                trace?.Invoke("서로 다른 전투 규칙과 각 규칙의 아트 테마를 설계하는 중");
                yield return AiClient.Complete(
                    "Return JSON {\"skills\":[{\"technique\":\"exact allowed id\",\"name\":\"Korean\",\"color\":\"six hex digits\",\"iconPrompt\":\"English isolated material motif\",\"effectPrompt\":\"English compact material burst\"}]}. " +
                    "Return three array positions. Choose one technique from each index's allowed set. Empty set means null. Keep the executable rule intact and vary the art motif. " +
                    "Names must suggest the chosen behavior. Never invent damage, rules, stages, teleport points, or visual anchors. The engine supplies placement, weapon contact, trajectory and timing.", context, .8f, 2000, s => json = s);
                if (json != null && Game.I?.World != null)
                {
                    Game.I.World.designCalls++;
                    Game.I.World.designCostUsd += AiClient.CostUsd(Game.Settings.aiModel, AiClient.LastPromptTokens, AiClient.LastCompletionTokens) ?? 0;
                }
                try { if (json != null) answer = JsonUtility.FromJson<Answer>(json); } catch (ArgumentException) { }
            }
            else trace?.Invoke("전용 전투 규칙으로 로컬 후보 준비");

            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] != null) continue;
                var p = answer?.skills?.Count == 3 ? answer.skills[i] : null;
                var rule = options[i].FirstOrDefault(r => r.Id.ToString() == p?.technique);
                result[i] = Make(rule ?? options[i][0], requestedType, slot, rule == null ? null : p);
            }
            if (slot > 0 && !result.Any(a => a.HasSynergy))
            {
                var link = ActiveSkillSynergy.Earlier(owner, requestedType, slot)
                    .SelectMany(s => ActiveSkillSynergy.Events.Where(e => ActiveSkillSynergy.Provides(s, e)).Select(e => (s, e)))
                    .FirstOrDefault();
                int index = Array.FindIndex(result, a => a.weaponType == requestedType && !(old != null && old.Contains(a)));
                if (link.s != null && index >= 0)
                {
                    result[index].linkSlot = link.s.slot;
                    result[index].linkEvent = link.e;
                    result[index].linkAction = ActiveTechniques.AcceptsLinkedPoint(result[index].technique) ? "relay" : "tempo";
                }
            }
            foreach (var a in result)
            {
                recent.Enqueue(a.technique);
                while (recent.Count > Rules.recentSignatures) recent.Dequeue();
                trace?.Invoke($"[{a.name}] {VisualSummary(a)}");
            }
            done(result.ToList());
        }

        public static string InferRole(ActiveSkillData a)
        {
            if (a.HasTechnique) return ActiveTechniques.Get(a.technique).Role;
            if (a.steps.Any(Moving)) return "mobility";
            if (a.steps.Any(s => s.effect is "shield" or "heal" or "block")) return "guard";
            if (a.steps.Any(s => s.delivery == "trap")) return "trap";
            if (a.steps.Any(s => s.effect is "pull" or "push" or "stun")) return "control";
            if (a.input == "channel" || a.steps.Any(s => s.delivery == "field" || s.timing == "repeat")) return "sustain";
            if (a.steps.Any(s => s.pattern is "pierce" or "seek" || s.delivery == "line") && a.input != "charge") return "precision";
            return "burst";
        }
        public static string RoleName(string role) => role switch { "mobility" => "이동·각도", "guard" => "방어·생존", "control" => "제어·집결", "trap" => "매복·차단", "sustain" => "지속 압박", "precision" => "정밀 타격", _ => "순간 화력" };
        internal static bool StandaloneAction(ActiveStep step) =>
            step?.condition == "always" && step.effect is "damage" or "shield" or "heal" or "block" or "stun" or "pull" or "push";

        public static bool ValidateCandidate(ActiveSkillData a, WeaponType type, int slot, out string why)
        {
            why = "지원하는 전투 규칙과 해금 슬롯을 확인해 주세요.";
            return a?.HasTechnique == true && !a.HasLegacyBundle && a.steps?.Count == 0 &&
                a.slot == slot && slot >= 0 && slot < 4 && a.weaponType == type && Enum.IsDefined(typeof(WeaponType), type) &&
                a.input == ActiveTechniques.Get(a.technique).Input && Allowed(ActiveTechniques.Get(a.technique), type) && ActiveSkillSynergy.ValidShape(a);
        }

        public static bool Moving(ActiveStep s) => s.delivery is "dash" or "retreat" or "blink";

        static bool Allowed(ActiveTechniques.Rule rule, WeaponType type) =>
            type == WeaponType.Gun || rule.Id is not (Technique.CaptureRound or Technique.IgniteCasings);

        static ActiveSkillData Make(ActiveTechniques.Rule rule, WeaponType type, int slot, Proposal proposal = null)
        {
            string color = proposal?.color?.TrimStart('#');
            if (!ColorUtility.TryParseHtmlString("#" + color, out _))
                color = type switch { WeaponType.Bow => "9cce98", WeaponType.Gun => "e8ac73", WeaponType.Staff => "a0c7ed",
                    WeaponType.Dagger => "c99de2", _ => "d8bf8d" };
            return new ActiveSkillData {
                technique = rule.Id, weaponType = type, slot = slot, role = rule.Role, input = rule.Input,
                source = proposal == null ? "local" : "ai", drawback = "", color = color,
                name = DesignGuard.CleanName(proposal?.name) ?? rule.Name, intent = rule.Read, designReason = rule.Read,
                iconPrompt = DesignGuard.CleanImagePrompt(proposal?.iconPrompt) ?? rule.Motif,
                effectPrompt = DesignGuard.CleanImagePrompt(proposal?.effectPrompt) ?? rule.Motif,
            };
        }

        public static ActiveSkillData Local(WeaponType type, int slot, string role = null)
        {
            var pool = ActiveTechniques.All.Where(r => Allowed(r, type) && (role == null || r.Role == role)).ToArray();
            if (pool.Length == 0) pool = ActiveTechniques.All;
            var candidates = pool.Where(r => r.Weapon == type).ToArray();
            if (candidates.Length == 0) candidates = pool;
            return Make(candidates[UnityEngine.Random.Range(0, candidates.Length)], type, slot);
        }

        public static SkillDef Definition(ActiveSkillData a)
        {
            return new SkillDef { id = a.id, name = a.name, desc = Describe(a), active = a, color = Pal.Hex(a.color), icon = "QWER"[a.slot].ToString(),
                cooldown = Cooldown(a),
                lockTime = Rules.lockTime + (a.drawback == "recovery" ? Rules.recoveryPenalty : 0) };
        }
        public static float Cost(ActiveSkillData a) => a.steps.Sum(s => (s.effect == "damage" ? 1f : Rules.utilityStepCost) + (s.pattern != "single" ? Rules.patternCost : 0) +
            (s.effect is "stun" or "pull" or "push" or "mark" ? Rules.controlCost : 0) + (Moving(s) ? Rules.movementCost : 0) +
            (s.delivery is "nova" or "field" ? Rules.areaCost : 0) + (s.delivery is "projectile" or "line" ? Rules.rangeCost : 0) +
            (s.timing != "instant" ? Rules.timingCost : 0) + (s.effect is "heal" or "shield" or "block" ? Rules.defenseCost : 0));
        public static float Magnitude(ActiveSkillData a) => a.HasTechnique ? ActiveTechniques.Get(a.technique).Tuning.strength *
            (a.slot == 3 ? Rules.ultimatePower / Rules.basePower : 1) : Mathf.Min(Rules.maxDamage, (a.slot == 3 ? Rules.ultimatePower : Rules.basePower) / Mathf.Max(1, Cost(a)));
        public static float Range(ActiveSkillData a, ActiveStep s) => (s.delivery == "arc" ? Rules.meleeRange : Rules.range) * (a.drawback == "shortRange" ? Rules.shortRangeFactor : 1);
        public static float Cooldown(ActiveSkillData a)
        {
            if (UsesTrapCharges(a)) return Rules.trapRecharge * (a.HasTechnique && a.slot == 3 || !a.HasTechnique && a.drawback == "cooldown" ? Rules.cooldownPenalty : 1);
            float role = InferRole(a) switch { "guard" => Rules.guardCooldown, "control" => Rules.controlCooldown, "trap" => Rules.trapCooldown, "mobility" => Rules.mobilityCooldown, _ => 1 };
            return Mathf.Clamp((a.slot == 3 ? Rules.ultimateCooldown : Rules.baseCooldown) * role * (!a.HasTechnique && a.drawback == "cooldown" ? Rules.cooldownPenalty : 1), Rules.minCooldown, Rules.maxCooldown);
        }
        public static string VisualSummary(ActiveSkillData a) => a.HasTechnique ? ActiveTechniques.Get(a.technique).Read : a.input + ": " + string.Join(" -> ", a.steps.Select(s => $"{s.trigger}@{s.origin} {s.delivery}/{s.pattern} {s.effect}/{s.element}/{s.timing}"));
        public static bool UsesTrapCharges(ActiveSkillData a) => a?.technique == Technique.ThresholdTrap || a?.steps?.Count > 0 &&
            a.steps[0].delivery == "trap" && a.input == "tap";
        public static string Describe(ActiveSkillData a)
        {
            if (a.HasTechnique)
            {
                var rule = ActiveTechniques.Get(a.technique);
                string inputRule = rule.Input == "recast" ? $"{Rules.techniqueRecastSeconds:0.#}초 안에 같은 키 재입력" : "한 번 입력";
                string link = a.HasSynergy ? $"\n{ActiveSkillSynergy.Describe(a)}" : "";
                string charges = UsesTrapCharges(a) ? $"\n{Rules.trapCharges}회 충전 · 최대 {Rules.trapLimit}개" : "";
                return $"{rule.Read}\n\n<b>조작</b>  {inputRule}\n<b>{(UsesTrapCharges(a) ? "충전" : "재사용")}</b>  {Cooldown(a):0.#}초{charges}{link}";
            }
            string Ko(string s) => s switch { "trap" => "지면 덫", "wall" => "투사체 장벽", "block" => "적 탄환 차단", "curve" => "곡선", "bounce" => "벽 반사 1회", "converge" => "수렴", "caster" => "시전자", "aim" => "조준 위치", "previous" => "이전 단계 위치", "impact" => "명중 위치", "after" => "즉시 연결", "hit" => "앞 단계 명중 후", "arrive" => "앞 단계 도착 후", "expire" => "앞 단계 종료 후", "recast" => "같은 키 재입력 후", "arc" => "전방 베기", "line" => "직선 타격", "projectile" => "투사체", "nova" => "주변 폭발", "field" => "지면 영역", "dash" => "돌진", "retreat" => "후퇴", "blink" => "순간이동", "guard" => "방어", "damage" => "피해", "shield" => "보호막", "heal" => "회복", "pull" => "끌어당김", "push" => "밀치기", "stun" => "기절", "mark" => "표식", "fan" => "부채꼴", "ring" => "방사", "chain" => "연쇄", "return" => "회수", "pierce" => "관통", "seek" => "유도", "marked" => "표식", "burning" => "화상", "chilled" => "냉기", "poisoned" => "중독", _ => s };
            string input = a.input switch { "charge" => $"길게 누른 뒤 놓기 (최대 {Rules.chargeDuration:0.#}초)", "channel" => "누르는 동안 유지", "recast" => $"후속 대기 중 {Rules.recastWindow:0.#}초 내 재입력", _ => "한 번 누르기" };
            string ElementName(string s) => s switch { "Fire" => "불", "Frost" => "냉기", "Lightning" => "번개", "Shadow" => "어둠", "Holy" => "신성", "Poison" => "독", "Wind" => "바람", "Blood" => "혈기", "Void" => "공허", "Arcane" => "비전", _ => "" };
            int trap = a.steps.FindIndex(s => s.delivery == "trap");
            var visibleSteps = trap >= 0 ? a.steps.Take(trap + 1) : a.steps;
            var lines = visibleSteps.Select((s, i) => $"{i + 1}. " + (s.trigger != "after" ? Ko(s.trigger) + ": " : "") +
                (s.delivery is "arc" or "line" || s.delivery == "projectile" && s.origin == "aim" ? "무기에서 " :
                    s.origin != "caster" ? Ko(s.origin) + "에 " : "") +
                (s.pattern != "single" ? Ko(s.pattern) + " " : "") + Ko(s.delivery) + " → " +
                (s.element != "None" ? ElementName(s.element) + " " : "") + Ko(s.effect) +
                (s.condition != "always" ? $" ({Ko(s.condition)} 대상{(s.consume ? "·소모" : "")})" : "") +
                (s.timing == "repeat" ? " · 반복" : s.timing == "delayed" && (i > 0 || a.input != "tap") ? " · 지연" : ""));
            return $"<b>{RoleName(InferRole(a))}</b>\n" + string.Join("\n", lines) +
                (trap >= 0 ? "\n발동 시 설치 지점에서만 폭발" : "") +
                (a.SeekingRupture ? "\n명중 지점에서 파열이 퍼져 주변 적에게 추가 피해" : "") +
                (a.HasSynergy ? $"\n<b>연계</b>  {ActiveSkillSynergy.Describe(a)}" : "") +
                (UsesTrapCharges(a) ? $"\n<b>덫</b>  {Rules.trapCharges}회 충전 · 최대 {Rules.trapLimit}개 · {Rules.trapDuration:0.#}초 유지" : "") +
                $"\n\n<b>조작</b>  {input}\n<b>{(UsesTrapCharges(a) ? "충전" : "쿨다운")}</b>  {Cooldown(a):0.#}초\n<b>위력</b>  단계 예산 {Magnitude(a):0.##}배 (다발·반복 분할)\n<b>대가</b>  " +
                (a.drawback == "cooldown" ? "긴 재사용 대기시간" : a.drawback == "shortRange" ? "짧은 사거리" : "시전 후 긴 경직");
        }
    }
}
