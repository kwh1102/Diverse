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
        static readonly Queue<string> recent = new Queue<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => recent.Clear();
        static readonly string[] deliveries = { "arc", "line", "projectile", "nova", "field", "dash", "retreat", "blink", "guard" };
        static readonly string[] patterns = { "single", "fan", "ring", "chain", "return", "pierce", "seek" };
        static readonly string[] effects = { "damage", "shield", "heal", "pull", "push", "stun", "mark" };
        static readonly string[] conditions = { "always", "marked", "burning", "chilled", "poisoned" };
        const string Prompt = @"Design NEW active skills for a top-down pixel action game. Never select finished skills from a list.
First decide a concrete tactical purpose, then compose supported operations. Korean name, English visual prompts.
Every skill requires player input and has exactly one primary purpose; use 1-3 steps, at most two special rules.
No passive triggers, automatic recasts, infinite loops, arbitrary code, invented operations or gameplay numbers.
Schema: {skills:[{slot:0,name:'Korean',intent:'tactic',drawback:'recovery|cooldown|shortRange',color:'hex',
steps:[{delivery:'arc|line|projectile|nova|field|dash|retreat|blink|guard',pattern:'single|fan|ring|chain|return|pierce|seek',
effect:'damage|shield|heal|pull|push|stun|mark',element:'None|Fire|Frost|Lightning|Shadow|Holy|Poison|Wind|Blood|Void|Arcane',
condition:'always|marked|burning|chilled|poisoned',timing:'instant|delayed|repeat',consume:false}],linkedMask:0,
iconPrompt:'distinct skill icon',projectilePrompt:'dedicated flying object facing right',effectPrompt:'distinct casting and impact motif'}]}.
Use valid JSON with double quotes. Slot indices Q=0 W=1 E=2 R=3; slots never move.
Most skills must work independently. Soft status synergies must include an unconditional useful step.
Hard dependencies: only a reciprocal two-slot group within requested slots, linkedMask includes both slots.
The producer marks targets; the companion must have a marked conditional step and consume it. Other conditions are soft.
Represent dependencies through mark operations and matching conditions, never promise unsupported anchors, copies or summons.
Projectile-only patterns (fan/ring/return/pierce/seek) apply to projectile delivery. Chain applies to line delivery only.
Non-damage effects use single pattern, instant timing, no element. guard is shield only. Movement uses single pattern and instant timing.
At most one movement step and one defensive step. Do not add a free extra effect. R can be powerful but has a long cooldown.
Match weapon type: Sword/Dagger use arcs, lines and movement; Bow/Gun use projectiles, lines and retreat; Staff uses any delivery.
Respect the player's requested concept only when expressible in this schema. Visual descriptions must match executable steps.
Never repeat a structural signature supplied in RECENT or EXISTING. Each skill needs its OWN icon, projectile and effects.";
        [Serializable] class Answer { public List<ActiveSkillData> skills; }

        public static WeaponType TypeOf(WeaponDef w) => w.type;
        public static string TypeName(WeaponType t) => t switch { WeaponType.Sword => "검", WeaponType.Bow => "활", WeaponType.Gun => "총", WeaponType.Staff => "봉", _ => "단검" };
        public static WeaponType TypeOf(InventoryItem w) => w?.design != null ? w.design.weapon.type : TypeOf(w.weapon);
        public static WeaponFootprint Shape(InventoryItem w) => DB.Asset.abilityRules.def.weaponShapes.First(s => s.type == TypeOf(w));

        public static IEnumerator Generate(Player owner, InventoryItem weapon, int[] slots, Action<List<ActiveSkillData>> done, Action<string> progress = null, string request = null)
        {
            List<ActiveSkillData> accepted = null;
            string failure = "";
            if (AiClient.Enabled)
            {
                for (int attempt = 0; attempt < Rules.generationAttempts && accepted == null; attempt++)
                {
                    progress?.Invoke("AI가 액티브의 조작과 효과를 설계하는 중…");
                    string context = $"WEAPON TYPE: {TypeOf(weapon)}; WEAPON: {weapon.name}; REQUESTED SLOTS: {string.Join(",", slots)}\n" +
                        $"PLAYER: {owner.Telemetry.Summary()}\nCONCEPT: {request ?? weapon.design?.prompts[1]}\n" +
                        "EXISTING: " + string.Join("\n", owner.Inventory.items.Where(i => i.active != null).Select(i => i.active.Signature())) +
                        "\nRECENT: " + string.Join("\n", recent) + "\nPREVIOUS REJECTION: " + failure;
                    string json = null;
                    yield return AiClient.Complete(Prompt, context, .9f, 2400 + slots.Length * 600, s => json = s);
                    if (json != null && Game.I?.World != null)
                    {
                        Game.I.World.designCalls++;
                        Game.I.World.designCostUsd += AiClient.CostUsd(Game.Settings.aiModel, AiClient.LastPromptTokens, AiClient.LastCompletionTokens) ?? 0;
                    }
                    Answer answer = null;
                    try { if (json != null) answer = JsonUtility.FromJson<Answer>(json); } catch (Exception) { }
                    if (answer?.skills != null && ValidateSet(answer.skills, TypeOf(weapon), slots, out failure))
                    {
                        var existing = owner.Inventory.items.Where(i => i.active != null).Select(i => i.active.Signature());
                        if (answer.skills.Any(a => recent.Contains(a.Signature()) || existing.Contains(a.Signature()))) { failure = "duplicate structure"; continue; }
                        accepted = answer.skills;
                        foreach (var a in accepted) a.source = "ai";
                    }
                    if (AiClient.LastFatal) break;
                }
            }
            accepted ??= slots.Select(slot => Local(TypeOf(weapon), slot)).ToList();
            foreach (var a in accepted) a.id = Guid.NewGuid().ToString("N");
            string batchId = Guid.NewGuid().ToString("N");
            foreach (var a in accepted)
            {
                if (a.linkedMask != 0) a.linkId = weapon.id + ":" + a.linkedMask + ":" + batchId;
                recent.Enqueue(a.Signature());
                while (recent.Count > Rules.recentSignatures) recent.Dequeue();
                if (AiClient.Enabled) yield return ActiveSkillArt.Prepare(a, progress);
            }
            done(accepted);
        }

        public static bool ValidateSet(List<ActiveSkillData> set, WeaponType type, int[] slots, out string why)
        {
            why = "slot set, unsupported operation, or invalid dependency";
            if (set == null || set.Count != slots.Length || set.Any(a => a == null) || !set.Select(a => a.slot).OrderBy(x => x).SequenceEqual(slots.OrderBy(x => x))) return false;
            int requested = slots.Aggregate(0, (m, s) => m | (1 << s));
            foreach (var a in set)
            {
                a.weaponType = type;
                if (a.steps == null || a.steps.Count < 1 || a.steps.Count > Rules.maxSteps) return false;
                if (!new[] { "recovery", "cooldown", "shortRange" }.Contains(a.drawback)) return false;
                if (!ColorUtility.TryParseHtmlString("#" + (a.color ?? "").TrimStart('#'), out _)) return false;
                a.color = a.color.TrimStart('#');
                a.name = DesignGuard.CleanName(a.name); if (string.IsNullOrWhiteSpace(a.name)) return false;
                a.intent = DesignGuard.CleanImagePrompt(a.intent);
                a.iconPrompt = DesignGuard.CleanImagePrompt(a.iconPrompt); a.projectilePrompt = DesignGuard.CleanImagePrompt(a.projectilePrompt); a.effectPrompt = DesignGuard.CleanImagePrompt(a.effectPrompt);
                if (a.iconPrompt == null || a.projectilePrompt == null || a.effectPrompt == null) return false;
                foreach (var s in a.steps)
                {
                    if (s == null || !deliveries.Contains(s.delivery) || !patterns.Contains(s.pattern) || !effects.Contains(s.effect) || !conditions.Contains(s.condition) ||
                        !new[] { "instant", "delayed", "repeat" }.Contains(s.timing) || !Enum.TryParse(s.element, out Element el) || !Enum.IsDefined(typeof(Element), el)) return false;
                    if (s.pattern != "single" && s.delivery != "projectile" && !(s.delivery == "line" && s.pattern == "chain")) return false;
                    if (s.effect != "damage" && (s.pattern != "single" || s.timing != "instant" || el != Element.None)) return false;
                    if (s.delivery == "guard" && s.effect != "shield") return false;
                    if ((s.effect is "shield" or "heal") && s.condition != "always") return false;
                    if (Moving(s) && (s.pattern != "single" || s.timing != "instant" || s.condition != "always" || s.effect != "damage")) return false;
                    if (s.consume && s.condition == "always") return false;
                    if ((type == WeaponType.Bow || type == WeaponType.Gun) && s.delivery == "arc") return false;
                    if ((type == WeaponType.Sword || type == WeaponType.Dagger) && s.delivery == "field") return false;
                }
                int special = a.steps.Count(s => s.pattern != "single") + a.steps.Count(s => s.timing != "instant") + a.steps.Count(s => s.condition != "always");
                if (special > 2 || a.steps.Count(Moving) > 1 || a.steps.Count(s => s.effect is "shield" or "heal") > 1) return false;
                if (a.linkedMask == 0 && !a.steps.Any(s => s.condition == "always" && (s.effect == "damage" || s.effect == "shield" || s.effect == "heal" || Moving(s)))) return false;
                if (a.linkedMask != 0)
                {
                    int members = Enumerable.Range(0, 4).Count(i => (a.linkedMask & (1 << i)) != 0);
                    if (members != 2 || members > Rules.maxLinkedSlots || (a.linkedMask & (1 << a.slot)) == 0 || (a.linkedMask & ~requested) != 0) return false;
                    var group = set.Where(s => (a.linkedMask & (1 << s.slot)) != 0).ToList();
                    if (group.Any(s => s.linkedMask != a.linkedMask || s.steps == null) || !group.Any(s => s.steps.Any(o => o.effect == "mark" && o.condition == "always")) ||
                        !group.Any(s => s.steps.Any(o => o.condition == "marked" && o.consume))) return false;
                    if (group.Count(s => s.steps.Any(o => o.condition == "always" && o.effect == "mark")) != 1) return false;
                }
            }
            if (set.Select(a => a.Signature()).Distinct().Count() != set.Count) return false;
            why = null; return true;
        }

        public static bool Moving(ActiveStep s) => s.delivery is "dash" or "retreat" or "blink";
        public static ActiveSkillData Local(WeaponType type, int slot)
        {
            string Pick(string[] xs) => xs[UnityEngine.Random.Range(0, xs.Length)];
            bool ranged = type is WeaponType.Bow or WeaponType.Gun;
            var a = new ActiveSkillData { weaponType = type, slot = slot, source = "local", drawback = Pick(new[] { "recovery", "cooldown", "shortRange" }),
                name = Pick(new[] { "반향", "황혼", "서리", "별빛", "잔영", "파동" }) + Pick(new[] { "의 일격", "의 궤적", "의 파열", "의 맹세" }),
                iconPrompt = "a distinct fantasy skill emblem", projectilePrompt = "a small luminous blade flying right", effectPrompt = "a crisp crescent of luminous sparks" };
            a.steps.Add(new ActiveStep { delivery = ranged ? "projectile" : Pick(new[] { "arc", "line", "nova" }), effect = "damage", element = Pick(new[] { "Fire", "Frost", "Wind", "Arcane", "Shadow" }),
                pattern = ranged ? Pick(new[] { "fan", "pierce", "return", "seek", "ring" }) : "single", timing = Pick(new[] { "instant", "delayed", "repeat" }) });
            if (UnityEngine.Random.value > .5f) a.steps.Add(new ActiveStep { delivery = "guard", effect = "shield" });
            return a;
        }

        public static SkillDef Definition(ActiveSkillData a)
        {
            return new SkillDef { id = a.id, name = a.name, desc = Describe(a), active = a, color = Pal.Hex(a.color), icon = "QWER"[a.slot].ToString(),
                cooldown = Mathf.Clamp((a.slot == 3 ? Rules.ultimateCooldown : Rules.baseCooldown) * (a.drawback == "cooldown" ? Rules.cooldownPenalty : 1), Rules.minCooldown, Rules.maxCooldown),
                lockTime = Rules.lockTime + (a.drawback == "recovery" ? Rules.recoveryPenalty : 0) };
        }
        public static float Cost(ActiveSkillData a) => a.steps.Sum(s => 1 + (s.pattern != "single" ? Rules.patternCost : 0) +
            (s.effect is "stun" or "pull" or "push" or "mark" ? Rules.controlCost : 0) + (Moving(s) ? Rules.movementCost : 0) +
            (s.delivery is "nova" or "field" ? Rules.areaCost : 0) + (s.delivery is "projectile" or "line" ? Rules.rangeCost : 0) +
            (s.timing != "instant" ? Rules.timingCost : 0) + (s.effect is "heal" or "shield" ? Rules.defenseCost : 0));
        public static float Magnitude(ActiveSkillData a) => Mathf.Min(Rules.maxDamage, (a.slot == 3 ? Rules.ultimatePower : Rules.basePower) / Mathf.Max(1, Cost(a)));
        public static float Range(ActiveSkillData a, ActiveStep s) => (s.delivery == "arc" ? Rules.meleeRange : Rules.range) * (a.drawback == "shortRange" ? Rules.shortRangeFactor : 1);
        public static string Describe(ActiveSkillData a)
        {
            string Ko(string s) => s switch { "arc" => "전방 베기", "line" => "직선 타격", "projectile" => "투사체", "nova" => "주변 폭발", "field" => "지면 영역", "dash" => "돌진", "retreat" => "후퇴", "blink" => "순간이동", "guard" => "방어", "damage" => "피해", "shield" => "보호막", "heal" => "회복", "pull" => "끌어당김", "push" => "밀치기", "stun" => "기절", "mark" => "표식", "fan" => "부채꼴", "ring" => "방사", "chain" => "연쇄", "return" => "회수", "pierce" => "관통", "seek" => "유도", "marked" => "표식", "burning" => "화상", "chilled" => "냉기", "poisoned" => "중독", _ => s };
            var lines = a.steps.Select(s => $"{Ko(s.delivery)} · {Ko(s.effect)}" + (s.element != "None" ? $" [{s.element}]" : "") +
                (s.pattern != "single" ? " · " + Ko(s.pattern) : "") + (s.condition != "always" ? $" · {Ko(s.condition)} 대상{(s.consume ? " 상태 소모" : "")}" : "") +
                (s.timing != "instant" ? s.timing == "repeat" ? " · 반복" : " · 지연" : ""));
            return string.Join("\n", lines) + "\n대가: " + (a.drawback == "cooldown" ? "긴 재사용 대기시간" : a.drawback == "shortRange" ? "짧은 사거리" : "시전 후 긴 경직") +
                (a.linkedMask != 0 ? $"\n종속 묶음 {a.Links} · 함께 교환" : "\n독립 스킬 · 같은 유형·같은 슬롯으로 교환");
        }
    }
}
