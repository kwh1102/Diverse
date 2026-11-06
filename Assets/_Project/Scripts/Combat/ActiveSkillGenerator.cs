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
        static readonly string[] deliveries = { "arc", "line", "projectile", "nova", "field", "dash", "retreat", "blink", "guard", "trap", "wall" };
        static readonly string[] patterns = { "single", "fan", "ring", "chain", "return", "pierce", "seek", "curve", "bounce", "converge" };
        static readonly string[] effects = { "damage", "shield", "heal", "pull", "push", "stun", "mark", "block" };
        static readonly string[] conditions = { "always", "marked", "burning", "chilled", "poisoned" };
        const string Prompt = @"Design NEW active skills for a top-down pixel action game. Never select finished skills from a list.
First decide a concrete tactical purpose, then compose supported operations. Korean name, English visual prompts.
Every skill requires player input and has exactly one primary purpose; use 1-4 causally connected steps.
No passive triggers, automatic recasts, infinite loops, arbitrary code, invented operations or gameplay numbers.
Schema: {skills:[{slot:0,name:'Korean',intent:'tactic',role:'burst|precision|sustain|control|trap|mobility|guard',designReason:'one short Korean sentence explaining tactical purpose from the supported operations, not internal reasoning',input:'tap|charge|channel|recast',drawback:'recovery|cooldown|shortRange',color:'hex',
steps:[{delivery:'arc|line|projectile|nova|field|dash|retreat|blink|guard|trap|wall',pattern:'single|fan|ring|chain|return|pierce|seek|curve|bounce|converge',
effect:'damage|shield|heal|pull|push|stun|mark|block',element:'None|Fire|Frost|Lightning|Shadow|Holy|Poison|Wind|Blood|Void|Arcane',
condition:'always|marked|burning|chilled|poisoned',timing:'instant|delayed|repeat',origin:'caster|aim|previous|impact',trigger:'after|hit|arrive|expire|recast',consume:false}],linkedMask:0,
iconPrompt:'one distinct isolated skill symbol matching executable operations'}]}.
Use valid JSON with double quotes. Slot indices Q=0 W=1 E=2 R=3; slots never move.
Most skills must work independently. Soft status synergies must include an unconditional useful step.
Hard dependencies: only a reciprocal two-slot group within requested slots, linkedMask includes both slots.
The producer marks targets; the companion must have a marked conditional step and consume it. Other conditions are soft.
Each stage may reference the PREVIOUS stage position or first impact. hit waits for its first enemy hit; arrive waits for projectile natural arrival or movement completion; expire waits for its completion. recast waits for a second player press.
The first stage uses caster/aim origin and after trigger. Movement always starts at caster; a recast movement with previous origin returns to the prior movement START position.
Charge scales power with held time; channel repeats only while held; recast MUST have exactly one later stage with recast trigger. No stage may reference a future stage.
field is a bounded pulsing ground anchor; trap waits for an enemy then bursts once; wall blocks enemy projectiles along a short line for a bounded duration. No terrain creation or summons.
Projectile-only patterns (fan/ring/return/pierce/seek/curve/bounce/converge) apply to projectile delivery. Chain applies to line delivery only.
Non-damage effects use single pattern, instant timing, no element. guard is shield only. Movement uses single pattern and instant timing.
Shield and heal are self effects: caster origin, always condition, guard/nova/field delivery (guard is shield only). wall always blocks unconditionally. Chain is line only.
wall MUST use block effect, single pattern, instant timing, no element. block is wall only. At most two movement steps and one defensive step. R can be powerful but has a long cooldown.
Match weapon type: Sword/Dagger use arcs, lines and movement; Bow/Gun use projectiles, lines and retreat; Staff uses any delivery.
For physical weapon slashes use arc with caster origin, including after movement. Use aim origin only for an explicitly projected or remote slash and describe that distinction in intent and iconPrompt.
Respect the player's requested concept only when expressible in this schema. Visual descriptions must match executable steps.
Never repeat a structural signature supplied in RECENT or EXISTING. Different element/colors/names do NOT count as different behavior. Avoid generic damage-plus-free-shield designs.
Visuals: restrained cute chunky pixel art, one clear symbol, 3-4 tones, no ornate emblems, excessive flames, particle storms, gradients or photorealistic materials. Combat effects are rendered from EACH executable step; do not invent a visual action that the graph cannot perform.
Required role semantics (the engine infers these in this priority): mobility has movement; guard has shield/heal/block and no movement; trap has trap delivery and no movement/defense; control has pull/push/stun without the prior features; sustain has channel, field or repeat without the prior features; precision has line/pierce/seek and is not charge; burst is remaining damage-only shapes. Honor the assigned role while varying origins, events, patterns and input styles.";
        [Serializable] class Answer { public List<ActiveSkillData> skills; }

        public static WeaponType TypeOf(WeaponDef w) => w.type;
        public static string TypeName(WeaponType t) => t switch { WeaponType.Sword => "검", WeaponType.Bow => "활", WeaponType.Gun => "총", WeaponType.Staff => "봉", _ => "단검" };
        public static WeaponType TypeOf(InventoryItem w) => w?.design != null ? w.design.weapon.type : TypeOf(w.weapon);
        public static WeaponFootprint Shape(InventoryItem w) => DB.Asset.abilityRules.def.weaponShapes.First(s => s.type == TypeOf(w));
        public static string SlotName(int slot) => slot >= 0 && slot < 4 ? "QWER"[slot].ToString() : "?";

        public static IEnumerator GenerateChoices(Player owner, int slot, string request, List<ActiveSkillData> old, bool[] pinned, Action<List<ActiveSkillData>> done, Action<string> trace = null, bool allowAi = true)
        {
            var owned = owner.Inventory.items.Where(i => i.kind == ItemKind.Weapon).Select(TypeOf).Distinct().ToList();
            var current = TypeOf(owner.Inventory.Active); owned.Remove(current); owned.Insert(0, current);
            var requested = owned.OrderByDescending(t => TypeName(t).Length).FirstOrDefault(t => !string.IsNullOrEmpty(request) && (request.IndexOf(TypeName(t), StringComparison.OrdinalIgnoreCase) >= 0 || request.IndexOf(t.ToString(), StringComparison.OrdinalIgnoreCase) >= 0));
            bool explicitType = !string.IsNullOrEmpty(request) && owned.Any(t => request.IndexOf(TypeName(t), StringComparison.OrdinalIgnoreCase) >= 0 || request.IndexOf(t.ToString(), StringComparison.OrdinalIgnoreCase) >= 0);
            var types = Enumerable.Range(0, 3).Select(i => explicitType ? requested : owned[i % owned.Count]).ToArray();
            var result = new ActiveSkillData[3];
            for (int i = 0; i < 3; i++) if (pinned[i] && old != null && i < old.Count) { result[i] = old[i]; types[i] = old[i].weaponType; }
            var constraint = ActiveSkillRequest.Read(request);
            if (allowAi) yield return constraint.Interpret(request, trace);
            if (constraint.unsupported != null) { trace?.Invoke(constraint.unsupported); done(null); yield break; }
            var roles = PlanRoles(result);
            if (constraint.Constrained) for (int i = 0; i < 3; i++) if (result[i] == null) roles[i] = constraint.Role;
            trace?.Invoke(constraint.Constrained ? "필수 실행 조건: " + constraint.Summary + " · 고정하지 않은 모든 후보에서 검사" : "명시 동작 조건 없음 · 자유 요청을 설계에 전달 (반영 내용은 후보 실행 단계에서 확인)");
            trace?.Invoke("요청: " + (string.IsNullOrWhiteSpace(request) ? "새로운 전술 후보" : request));
            for (int i = 0; i < 3; i++)
            {
                trace?.Invoke($"후보 {i + 1}: {TypeName(types[i])} · {RoleName(roles[i])}{(result[i] != null ? " · 고정 유지" : "")}");
                if (result[i] != null) trace?.Invoke($"[{result[i].name}] 유지 · {result[i].designReason}\n실행: {VisualSummary(result[i])}");
            }
            if (!allowAi || !AiClient.Enabled) trace?.Invoke("AI 요청 보류/연결 없음 · 실행 조건을 검사한 로컬 설계 후보 준비");
            string rejection = "";
            if (allowAi && AiClient.Enabled)
            {
                for (int attempt = 0; attempt < Rules.generationAttempts && result.Any(a => a == null); attempt++)
                {
                    string context = $"Create exactly THREE independent alternatives for slot {slot}. This is a selection screen, NOT a QWER set. linkedMask MUST be 0.\n" +
                        "Index weapon types and role assignments: " + string.Join(",", Enumerable.Range(0,3).Select(i => constraint.Constrained ? $"{i}={types[i]} (role unrestricted; infer from mandatory operations)" : $"{i}={types[i]}/{roles[i]}")) +
                        $"\nPLAYER: {owner.Telemetry.Summary()}\nREQUEST: {request}\n" +
                        "Pinned signatures to avoid: " + string.Join(";", result.Where(a => a != null).Select(a => a.Signature())) +
                        "\nEXISTING: " + string.Join(";", owner.Inventory.items.Where(i => i.active != null).Select(i => i.active.Signature())) +
                        "\nRECENT: " + string.Join(";", recent) + "\nREJECTION: " + rejection +
                        "\nMANDATORY executable constraints for EVERY unpinned alternative: " + constraint.MachineSummary +
                        "\nRequested constraints take priority over role variety. A dash request MUST contain dash, never substitute blink/retreat. Vary input styles, stage dependencies and followups within these constraints. Never change weapon type outside this list.";
                    trace?.Invoke($"AI 설계 요청 {attempt + 1}/{Rules.generationAttempts} · 필수 요청 조건과 실행 그래프 작성");
                    string json = null; yield return AiClient.Complete(Prompt, context, .9f, 4800, s => json = s);
                    if (json != null && Game.I?.World != null)
                    {
                        Game.I.World.designCalls++;
                        Game.I.World.designCostUsd += AiClient.CostUsd(Game.Settings.aiModel, AiClient.LastPromptTokens, AiClient.LastCompletionTokens) ?? 0;
                    }
                    Answer answer = null; try { if (json != null) answer = JsonUtility.FromJson<Answer>(json); } catch (Exception) { }
                    if (answer?.skills?.Count == 3)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            if (result[i] != null) continue;
                            var a = answer.skills[i];
                            if (a == null || a.linkedMask != 0 || !ValidateSet(new List<ActiveSkillData> { a }, types[i], new[] { slot }, out rejection))
                            { trace?.Invoke($"후보 {i + 1} 제외 · 실행 규칙/독립성 검사 실패 ({rejection})"); continue; }
                            a.role = InferRole(a);
                            if (!constraint.Matches(a)) { rejection = "missing requested executable operations: " + constraint.Summary; trace?.Invoke($"후보 {i + 1} 제외 · 필수 동작 부족 ({constraint.Summary})"); continue; }
                            if (!constraint.Constrained && a.role != roles[i]) { rejection = "role mismatch: " + roles[i]; trace?.Invoke($"후보 {i + 1} 제외 · 요청한 {RoleName(roles[i])} 동작 부족"); continue; }
                            if (result.Any(x => x != null && Distance(x, a) < Rules.minChoiceDistance) || recent.Contains(a.Signature()) || owner.Inventory.items.Any(x => x.active?.Signature() == a.Signature()))
                            { rejection = "too similar in geometry, input or operation graph"; trace?.Invoke($"후보 {i + 1} 제외 · 기존 동작과 지나치게 유사"); continue; }
                            a.source = "ai"; a.id = Guid.NewGuid().ToString("N"); result[i] = a;
                            if (string.IsNullOrWhiteSpace(a.designReason)) a.designReason = RoleName(a.role) + " 목적의 실행 단계를 연결했습니다.";
                            trace?.Invoke($"후보 {i + 1} [{a.name}] 채택 · {a.designReason}\n실행: {VisualSummary(a)}");
                        }
                    }
                    else trace?.Invoke(json == null ? "AI 요청 실패 · " + AiClient.LastStatus : "응답 검사 실패 · 후보 3개의 JSON 형식이 아님");
                    if (AiClient.LastFatal) break;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                if (result[i] == null)
                {
                    ActiveSkillData a = null;
                    for (int attempt = 0; attempt < 32; attempt++)
                    {
                        var proposed = constraint.Constrained ? constraint.Local(types[i], slot, i + attempt) : Local(types[i], slot, roles[i]);
                        if (!constraint.Matches(proposed) || !ValidateSet(new List<ActiveSkillData> { proposed }, types[i], new[] { slot }, out _) ||
                            result.Any(x => x != null && Distance(x, proposed) < Rules.minChoiceDistance) ||
                            recent.Contains(proposed.Signature()) || owner.Inventory.items.Any(x => x.active?.Signature() == proposed.Signature())) continue;
                        a = proposed; break;
                    }
                    if (a == null)
                    { trace?.Invoke("요청을 만족하면서 기존 동작과 구분되는 유효한 후보를 완성하지 못함 · 이전 후보 유지. 조건을 줄이거나 다른 조작을 요청해 주세요."); done(null); yield break; }
                    result[i] = a;
                    trace?.Invoke($"후보 {i + 1} 로컬 대체 · {RoleName(a.role)} · {a.designReason}\n실행: {VisualSummary(a)}");
                }
            }
            foreach (var a in result) { recent.Enqueue(a.Signature()); while (recent.Count > Rules.recentSignatures) recent.Dequeue(); }
            trace?.Invoke("완료: " + string.Join(" / ", result.Select((a, i) => $"{i + 1} {a.name}: {a.input} · {string.Join("→", a.steps.Select(s => s.delivery))} ({a.source})")));
            done(result.ToList());
        }

        static string[] PlanRoles(ActiveSkillData[] kept)
        {
            var groups = new[] { new[] { "burst", "precision", "sustain" }, new[] { "control", "trap" }, new[] { "mobility", "guard" } };
            var used = new HashSet<string>(kept.Where(a => a != null).Select(InferRole));
            var available = groups.OrderBy(g => g.Any(used.Contains) ? 1 : 0).ThenBy(_ => UnityEngine.Random.value).ToList();
            var roles = new string[3];
            for (int i = 0; i < roles.Length; i++)
            {
                if (kept[i] != null) { roles[i] = InferRole(kept[i]); continue; }
                var group = available[0]; available.RemoveAt(0);
                roles[i] = group.Where(r => !used.Contains(r)).OrderBy(_ => UnityEngine.Random.value).FirstOrDefault() ?? group[0];
                used.Add(roles[i]);
            }
            return roles;
        }
        public static string InferRole(ActiveSkillData a)
        {
            if (a.steps.Any(Moving)) return "mobility";
            if (a.steps.Any(s => s.effect is "shield" or "heal" or "block")) return "guard";
            if (a.steps.Any(s => s.delivery == "trap")) return "trap";
            if (a.steps.Any(s => s.effect is "pull" or "push" or "stun")) return "control";
            if (a.input == "channel" || a.steps.Any(s => s.delivery == "field" || s.timing == "repeat")) return "sustain";
            if (a.steps.Any(s => s.pattern is "pierce" or "seek" || s.delivery == "line") && a.input != "charge") return "precision";
            return "burst";
        }
        public static string RoleName(string role) => role switch { "mobility" => "이동·각도", "guard" => "방어·생존", "control" => "제어·집결", "trap" => "매복·차단", "sustain" => "지속 압박", "precision" => "정밀 타격", _ => "순간 화력" };
        public static float Distance(ActiveSkillData a, ActiveSkillData b)
        {
            HashSet<string> Features(ActiveSkillData x) => new HashSet<string>(x.steps.SelectMany((s, i) => new[] {
                $"{i}:shape:{s.delivery}", $"{i}:effect:{s.effect}", $"{i}:path:{s.pattern}", $"{i}:anchor:{s.origin}/{s.trigger}", $"{i}:time:{s.timing}/{s.condition}/{s.consume}" }));
            var left = Features(a); var right = Features(b);
            float graph = 1 - left.Intersect(right).Count() / (float)Mathf.Max(1, left.Union(right).Count());
            return graph * (1 - Rules.inputDistanceWeight - Rules.roleDistanceWeight) +
                (a.input != b.input ? Rules.inputDistanceWeight : 0) + (InferRole(a) != InferRole(b) ? Rules.roleDistanceWeight : 0);
        }

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
                a.input = string.IsNullOrEmpty(a.input) ? "tap" : a.input;
                if (!new[] { "tap", "charge", "channel", "recast" }.Contains(a.input)) return false;
                if (a.steps == null || a.steps.Count < 1 || a.steps.Count > Rules.maxSteps) return false;
                if (!new[] { "recovery", "cooldown", "shortRange" }.Contains(a.drawback)) return false;
                if (!ColorUtility.TryParseHtmlString("#" + (a.color ?? "").TrimStart('#'), out _)) return false;
                a.color = a.color.TrimStart('#');
                a.name = DesignGuard.CleanName(a.name); if (string.IsNullOrWhiteSpace(a.name)) return false;
                a.intent = DesignGuard.CleanImagePrompt(a.intent);
                a.designReason = DesignGuard.CleanImagePrompt(a.designReason);
                if (a.designReason?.Length > 140) a.designReason = a.designReason.Substring(0, 140);
                a.iconPrompt = DesignGuard.CleanImagePrompt(a.iconPrompt); a.projectilePrompt = DesignGuard.CleanImagePrompt(a.projectilePrompt); a.effectPrompt = DesignGuard.CleanImagePrompt(a.effectPrompt);
                if (a.iconPrompt == null) return false;
                foreach (var s in a.steps)
                {
                    if (s == null) return false;
                    s.origin = string.IsNullOrEmpty(s.origin) ? "caster" : s.origin;
                    s.trigger = string.IsNullOrEmpty(s.trigger) ? "after" : s.trigger;
                    int index = a.steps.IndexOf(s);
                    if (!new[] { "caster", "aim", "previous", "impact" }.Contains(s.origin) || !new[] { "after", "hit", "arrive", "expire", "recast" }.Contains(s.trigger)) return false;
                    if (index == 0 && (s.origin is "previous" or "impact" || s.trigger != "after")) return false;
                    if (s.origin == "impact" && s.trigger != "hit") return false;
                    if (s.trigger == "recast" && a.input != "recast") return false;
                    if (index > 0 && s.trigger == "hit" && (a.steps[index - 1].effect is "shield" or "heal" or "block")) return false;
                    if (index > 0 && s.trigger == "arrive" && a.steps[index - 1].delivery != "projectile" && !Moving(a.steps[index - 1])) return false;
                    if (s == null || !deliveries.Contains(s.delivery) || !patterns.Contains(s.pattern) || !effects.Contains(s.effect) || !conditions.Contains(s.condition) ||
                        !new[] { "instant", "delayed", "repeat" }.Contains(s.timing) || !Enum.TryParse(s.element, out Element el) || !Enum.IsDefined(typeof(Element), el)) return false;
                    if (s.pattern != "single" && s.delivery != "projectile" && !(s.delivery == "line" && s.pattern == "chain")) return false;
                    if (s.pattern == "chain" && s.delivery != "line") return false;
                    if (s.effect != "damage" && (s.pattern != "single" || s.timing != "instant" || el != Element.None)) return false;
                    if (s.delivery == "guard" && s.effect != "shield") return false;
                    if ((s.delivery == "wall") != (s.effect == "block")) return false;
                    if ((s.effect is "shield" or "heal") && (s.condition != "always" || s.origin != "caster" || !(s.delivery is "guard" or "nova" or "field"))) return false;
                    if (s.effect == "block" && s.condition != "always") return false;
                    if (Moving(s) && (s.pattern != "single" || s.timing != "instant" || s.condition != "always" || s.effect != "damage")) return false;
                    if (s.consume && s.condition == "always") return false;
                    if ((type == WeaponType.Bow || type == WeaponType.Gun) && s.delivery == "arc") return false;
                    if ((type == WeaponType.Sword || type == WeaponType.Dagger) && s.delivery == "field") return false;
                }
                int special = a.steps.Count(s => s.pattern != "single") + a.steps.Count(s => s.timing != "instant") + a.steps.Count(s => s.condition != "always");
                if (special > 4 || a.steps.Count(Moving) > 2 || a.steps.Count(s => s.effect is "shield" or "heal" or "block") > 1) return false;
                if ((a.input == "recast") != (a.steps.Count(s => s.trigger == "recast") == 1)) return false;
                if (a.input == "channel" && a.steps.Any(s => Moving(s) || s.trigger != "after" || s.delivery == "trap" || s.delivery == "wall")) return false;
                if (a.linkedMask == 0 && !a.steps.Any(s => s.condition == "always" && (s.effect is "damage" or "shield" or "heal" or "block" or "stun" or "pull" or "push"))) return false;
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
        public static ActiveSkillData Local(WeaponType type, int slot, string role = null)
        {
            string Pick(string[] xs) => xs[UnityEngine.Random.Range(0, xs.Length)];
            bool ranged = type is WeaponType.Bow or WeaponType.Gun;
            var a = new ActiveSkillData { weaponType = type, slot = slot, source = "local", drawback = Pick(new[] { "recovery", "cooldown", "shortRange" }),
                color = Pick(new[] { "c9b8ff", "b5cfa6", "edb18d", "99c9d2", "e5b4c7" }),
                iconPrompt = "one simple chunky fantasy tool symbol, 3 tone shading", projectilePrompt = "one small readable object flying right", effectPrompt = "a compact soft crescent, sparse sparks, 3 tones" };
            role ??= Pick(new[] { "burst", "precision", "sustain", "control", "trap", "mobility", "guard" });
            string element = Pick(new[] { "Fire", "Frost", "Wind", "Arcane", "Shadow" });
            ActiveStep Op(string delivery, string effect = "damage", string pattern = "single", string origin = "caster", string trigger = "after") =>
                new ActiveStep { delivery = delivery, effect = effect, pattern = pattern, origin = origin, trigger = trigger, element = effect == "damage" ? element : "None" };
            switch (role)
            {
                case "precision":
                    a.steps.Add(Op(ranged ? "projectile" : "line", pattern: ranged ? Pick(new[] { "pierce", "seek" }) : Pick(new[] { "single", "chain" })));
                    if (UnityEngine.Random.value > .5f) a.steps.Add(Op("nova", origin: "impact", trigger: "hit"));
                    a.name = "꿰뚫는 궤적"; a.designReason = "한 줄의 적을 노려 정확한 공격 각도를 보상합니다."; break;
                case "sustain":
                    a.input = "channel";
                    a.steps.Add(Op(ranged || type == WeaponType.Staff ? "projectile" : Pick(new[] { "arc", "line", "nova" }), pattern: ranged ? Pick(new[] { "curve", "fan", "single" }) : "single"));
                    a.name = "이어지는 맥동"; a.designReason = "키를 유지하며 같은 공간을 제한된 횟수로 압박합니다."; break;
                case "control":
                    a.steps.Add(Op(ranged ? "projectile" : Pick(new[] { "nova", "line" }), "pull", origin: "aim"));
                    a.steps.Add(Op("nova", origin: "impact", trigger: "hit"));
                    a.name = "모여드는 파열"; a.designReason = "적을 끌어당긴 위치에 후속 폭발을 연결합니다."; break;
                case "trap":
                    a.steps.Add(Op("trap", origin: "aim"));
                    if (UnityEngine.Random.value > .5f) a.steps[0].timing = "delayed";
                    a.steps.Add(Op("nova", "stun", origin: "impact", trigger: "hit"));
                    a.name = "발밑의 매복"; a.designReason = "조준한 곳에 덫을 놓고 접근한 적을 묶습니다."; break;
                case "mobility":
                    if (UnityEngine.Random.value > .5f)
                    {
                        a.input = "recast";
                        a.steps.Add(Op(ranged ? "retreat" : "dash"));
                        a.steps.Add(Op("dash", origin: "previous", trigger: "recast"));
                        a.name = "되짚는 발걸음"; a.designReason = "이동으로 각도를 만든 뒤 재입력해 출발점으로 돌아갑니다.";
                    }
                    else
                    {
                        a.steps.Add(Op(ranged ? "retreat" : "blink"));
                        a.steps.Add(Op(ranged ? "projectile" : "arc", origin: "caster", trigger: "arrive"));
                        a.name = "틈새의 궤적"; a.designReason = "위치를 바꾼 직후 새 각도에서 공격합니다.";
                    }
                    break;
                case "guard":
                    a.steps.Add(Op(Pick(new[] { "wall", "guard", "nova" }), "shield"));
                    if (a.steps[0].delivery == "wall") a.steps[0].effect = "block";
                    else if (a.steps[0].delivery == "nova") a.steps[0].effect = "heal";
                    a.name = a.steps[0].delivery == "wall" ? "가로막는 경계" : "위기의 가호";
                    a.designReason = "위험한 순간 직접 사용해 적의 공격을 견디는 생존 도구입니다."; break;
                default:
                    a.input = "charge";
                    a.steps.Add(Op(ranged ? "projectile" : "arc", pattern: ranged ? Pick(new[] { "fan", "ring", "converge", "return" }) : "single"));
                    a.steps.Add(Op("nova", origin: "impact", trigger: "hit"));
                    a.name = "응축된 파열"; a.designReason = "충전 중 빈틈을 감수하고 명중 위치에 화력을 집중합니다."; break;
            }
            a.role = InferRole(a); a.intent = a.designReason;
            a.iconPrompt = $"one chunky pixel symbol for {a.role}, {a.steps[0].delivery}, {element}; no frame or background";
            a.effectPrompt = VisualSummary(a);
            return a;
        }

        public static SkillDef Definition(ActiveSkillData a)
        {
            return new SkillDef { id = a.id, name = a.name, desc = Describe(a), active = a, color = Pal.Hex(a.color), icon = "QWER"[a.slot].ToString(),
                cooldown = Cooldown(a),
                lockTime = Rules.lockTime + (a.drawback == "recovery" ? Rules.recoveryPenalty : 0) };
        }
        public static float Cost(ActiveSkillData a) => a.steps.Sum(s => 1 + (s.pattern != "single" ? Rules.patternCost : 0) +
            (s.effect is "stun" or "pull" or "push" or "mark" ? Rules.controlCost : 0) + (Moving(s) ? Rules.movementCost : 0) +
            (s.delivery is "nova" or "field" ? Rules.areaCost : 0) + (s.delivery is "projectile" or "line" ? Rules.rangeCost : 0) +
            (s.timing != "instant" ? Rules.timingCost : 0) + (s.effect is "heal" or "shield" or "block" ? Rules.defenseCost : 0));
        public static float Magnitude(ActiveSkillData a) => Mathf.Min(Rules.maxDamage, (a.slot == 3 ? Rules.ultimatePower : Rules.basePower) / Mathf.Max(1, Cost(a)));
        public static float Range(ActiveSkillData a, ActiveStep s) => (s.delivery == "arc" ? Rules.meleeRange : Rules.range) * (a.drawback == "shortRange" ? Rules.shortRangeFactor : 1);
        public static float Cooldown(ActiveSkillData a)
        {
            float role = InferRole(a) switch { "guard" => Rules.guardCooldown, "control" => Rules.controlCooldown, "trap" => Rules.trapCooldown, "mobility" => Rules.mobilityCooldown, _ => 1 };
            return Mathf.Clamp((a.slot == 3 ? Rules.ultimateCooldown : Rules.baseCooldown) * role * (a.drawback == "cooldown" ? Rules.cooldownPenalty : 1), Rules.minCooldown, Rules.maxCooldown);
        }
        public static string VisualSummary(ActiveSkillData a) => a.input + ": " + string.Join(" -> ", a.steps.Select(s => $"{s.trigger}@{s.origin} {s.delivery}/{s.pattern} {s.effect}/{s.element}/{s.timing}"));
        public static string ExchangeRule(ActiveSkillData a) => a.linkedMask != 0 ? $"종속 묶음 {a.Links} · 함께 교환" : "독립 · 같은 유형/슬롯끼리 교환";
        public static string Describe(ActiveSkillData a)
        {
            string Ko(string s) => s switch { "trap" => "지면 덫", "wall" => "투사체 장벽", "block" => "적 탄환 차단", "curve" => "곡선", "bounce" => "벽 반사 1회", "converge" => "수렴", "caster" => "시전자", "aim" => "조준 위치", "previous" => "이전 단계 위치", "impact" => "명중 위치", "after" => "즉시 연결", "hit" => "앞 단계 명중 후", "arrive" => "앞 단계 도착 후", "expire" => "앞 단계 종료 후", "recast" => "같은 키 재입력 후", "arc" => "전방 베기", "line" => "직선 타격", "projectile" => "투사체", "nova" => "주변 폭발", "field" => "지면 영역", "dash" => "돌진", "retreat" => "후퇴", "blink" => "순간이동", "guard" => "방어", "damage" => "피해", "shield" => "보호막", "heal" => "회복", "pull" => "끌어당김", "push" => "밀치기", "stun" => "기절", "mark" => "표식", "fan" => "부채꼴", "ring" => "방사", "chain" => "연쇄", "return" => "회수", "pierce" => "관통", "seek" => "유도", "marked" => "표식", "burning" => "화상", "chilled" => "냉기", "poisoned" => "중독", _ => s };
            string input = a.input switch { "charge" => $"길게 누른 뒤 놓기 (최대 {Rules.chargeDuration:0.#}초)", "channel" => "누르는 동안 유지", "recast" => $"후속 대기 중 {Rules.recastWindow:0.#}초 내 재입력", _ => "한 번 누르기" };
            string ElementName(string s) => s switch { "Fire" => "불", "Frost" => "냉기", "Lightning" => "번개", "Shadow" => "어둠", "Holy" => "신성", "Poison" => "독", "Wind" => "바람", "Blood" => "혈기", "Void" => "공허", "Arcane" => "비전", _ => "" };
            var lines = a.steps.Select((s, i) => $"{i + 1}. " + (s.trigger != "after" ? Ko(s.trigger) + ": " : "") +
                (s.origin != "caster" ? Ko(s.origin) + "에 " : "") +
                (s.pattern != "single" ? Ko(s.pattern) + " " : "") + Ko(s.delivery) + " → " +
                (s.element != "None" ? ElementName(s.element) + " " : "") + Ko(s.effect) +
                (s.condition != "always" ? $" ({Ko(s.condition)} 대상{(s.consume ? "·소모" : "")})" : "") +
                (s.timing != "instant" ? s.timing == "repeat" ? " · 반복" : " · 지연" : ""));
            return $"<b>{RoleName(InferRole(a))}</b>\n" + string.Join("\n", lines) +
                $"\n\n<b>조작</b>  {input}\n<b>쿨다운</b>  {Cooldown(a):0.#}초\n<b>위력</b>  단계 예산 {Magnitude(a):0.##}배 (다발·반복 분할)\n<b>대가</b>  " +
                (a.drawback == "cooldown" ? "긴 재사용 대기시간" : a.drawback == "shortRange" ? "짧은 사거리" : "시전 후 긴 경직");
        }
    }
}
