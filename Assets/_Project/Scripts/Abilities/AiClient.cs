using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Diverse
{
    /// <summary>
    /// OpenAI-compatible Chat Completions client.
    /// - LLM #1 (Rule Generator): Generation Context + retrieved primitives → AbilityIntent (concept + mechanic macros / rule draft)
    ///   → AbilityCompiler → Rule IR (AbilityDef)
    /// - Repair: the validator's machine-readable errors go back to LLM #1 for a corrected structure
    /// - LLM #2 (Name/Flavor): validated & balanced ability → Korean name/description
    /// Even when the API fails, the game is unaffected (falls back to LocalComposer).
    /// </summary>
    public static class AiClient
    {
        public static string LastStatus = "";
        public static string LastPrompt = "";
        public static string LastResponse = "";
        /// <summary>The last call failed in a way retrying can't fix (bad key, unknown model, no credit).</summary>
        public static bool LastFatal;
        public static bool HasKey => !string.IsNullOrEmpty(SaveSystem.LoadApiKey());
        public static bool Enabled => Game.Settings.aiEnabled && HasKey;

        /// <summary>Selectable models. All of them accept the request format below (temperature + max_tokens + json_object).</summary>
        public static readonly (string id, string label, string desc)[] Models =
        {
            ("gpt-4o-mini", "GPT-4o mini", "빠르고 저렴함 (기본값)"),
            ("gpt-4.1-mini", "GPT-4.1 mini", "조금 더 똑똑함, 비용 약 2배"),
            ("gpt-4.1-nano", "GPT-4.1 nano", "가장 빠르고 저렴함, 품질은 낮음"),
            ("gpt-4.1", "GPT-4.1", "가장 높은 품질, 느리고 비쌈"),
        };

        public static bool IsPresetModel(string id) => Array.Exists(Models, m => m.id == id);
        public static string ModelLabel(string id) { foreach (var m in Models) if (m.id == id) return m.label; return id; }

        [Serializable] class Msg { public string role; public string content; }
        [Serializable] class RespFmt { public string type = "json_object"; }
        [Serializable] class Req { public string model; public List<Msg> messages; public float temperature; public RespFmt response_format; public int max_tokens; }
        [Serializable] class Choice { public Msg message; public string finish_reason; }
        [Serializable] class Resp { public List<Choice> choices; }
        [Serializable] class IntentList { public List<AbilityIntent> abilities; }
        [Serializable] class NameItem { public string id; public string name; public string desc; }
        [Serializable] class NameList { public List<NameItem> names; }
        [Serializable] class CardChange : AbilityIntent { public int card; }
        [Serializable] class ChatResp { public string reply; public List<CardChange> changes; }

        const string SystemPrompt =
@"You design abilities for a pixel-art action roguelite where abilities EVOLVE from how the player actually plays.
An ability is a tiny PROGRAM in a closed rule language — never a finished named skill. Think TFT augments / Sephiria
artifacts: trade-offs, things that accumulate and get released, conversions that redefine a game rule, replacing a
player action, giving something up, scaling with the build, modifying the player's OTHER abilities, or the evolution system.

YOU WRITE AN INTENT; THE ENGINE COMPILES, TYPE-CHECKS, BALANCES AND SIMULATES IT.
Each candidate: ""concept"" (one creative sentence) + ""semanticReason"" (short Korean, why it fits THIS player) +
EITHER ""mechanics"" (macros, preferred — easier to get right) OR a direct ""states""/""rules"" draft (for structures
the macros can't express) — or both.

MECHANIC MACROS — type MUST be one of these 14 words (not a rule kind, trigger or op name): Effect Accumulate Threshold Hunt
Charge Window Anchor Scaling Tradeoff Constrain Convert Replace Meta System. For a plain when X do Y use type Effect.
- Effect:     on, effect (+target/at/element/status/entity/stat/rel/time), chance, cooldown — ""when X, do Y""
- Accumulate: on, release, effect, label, count — store a quantity from X (damage taken, damage dealt…) and release it scaled on Y
- Threshold:  on, effect, count, label — every N times X happens, a payoff (state fills → resets)
- Hunt:       on, stat, count — stacks from X that decay over time and raise a stat
- Charge:     on, effect, count — a regenerating resource spent each time X fires
- Window:     on, effect|stat, window — after X, for a few seconds a passive bonus is active
- Anchor:     on, release, effect, target(StoredTarget for an enemy) — remember a place/enemy at X, act on it at Y
- Scaling:    effect|stat, scale — a stat that grows with a value (missing hp, enemies near, build.tag:FIRE…)
- Tradeoff:   price (a stat to lower, or an action to give up: Dash|Potion|Regen|Skill), effect|stat (+scale) — a real cost for a gain
- Constrain:  price — the player can no longer do something (only as part of a bigger ability)
- Convert:    from, to (whitelisted pairs), scale+chance (optional hp condition) — redefine a game rule
- Replace:    on (Dash|Potion|ComboFinisher), effect (Blink|Nova|Shield|Spawn|Projectile), entity/element — the action does something else
- Meta:       select (ByTag|ByElement|ByTrigger|Strongest|Newest|All), tag/element/on, effect (AmplifyTagged|RepeatTagged|HasteTagged|
              InfuseTagged|ExtendTagged|EnlargeTagged|MultiplyTagged|RetagAbilities|GateTagged|UnchainTagged|RetriggerTagged), element, from/to
- System:     effect (OfferCount|TierBias|BudgetBias|RerollDiscount|TagBias|ExtraEvolution|UpgradeRandom|DelayedReward), tag, count
Names may be ids or plain words (""explosion"", ""lifesteal"", ""missing hp"", ""on kill"") — the compiler resolves them.

DIRECT RULE DRAFT (when you need it)
rules[].kind = Trigger|Continuous|Replace|Constrain|Meta|System; Trigger rules have ""trigger"" (+param), ""when"" conditions, ""ops"".
ops[] fields: op, target, at, element, status, entity, stat, state, tag, from, to, mode, scale, rel (Relation layer), time (Temporal layer), times.
Ops may only use what the trigger provides (EventTarget needs Target; event.amount needs Amount). Every state must be written and read.

RULES
- Use ONLY primitives listed in AVAILABLE LANGUAGE / EXPLORATION POOL. Unknown words are rejected by the engine.
- Numbers are decided by the engine (power budget). You only choose structure, conditions, thresholds, and the SIGN of a trade-off.
- Prefer abilities that COMBINE with what the player already owns. Respect the complexity tiers allowed for this level.
- Relations (Echo, Chain, Bounce, Cascade, Orbit…) and temporals (Periodic, UntilHit, ForNextN…) are what make abilities
  feel new — use them. Element affinities are hints, not laws.
- Avoid self-feeding loops unless throttled (chance/cooldown).

OUTPUT JSON: {""abilities"":[ {""concept"":"""",""semanticReason"":"""",""mechanics"":[{""type"":"""", ...}],""states"":[],""rules"":[]} ]}

EXAMPLES
{""concept"":""Pain is remembered and returned on a perfect dodge"",""semanticReason"":""자주 맞고 아슬아슬하게 피하는 당신에게"",
 ""mechanics"":[{""type"":""Accumulate"",""on"":""Damaged"",""release"":""PerfectDodge"",""effect"":""Nova"",""at"":""Self"",""label"":""고통""}]}
{""concept"":""A blood pact: less health, more fury, and near death healing hardens into a shield"",""semanticReason"":""위험을 즐기는 당신에게"",
 ""mechanics"":[{""type"":""Tradeoff"",""price"":""MaxHp"",""stat"":""Attack"",""scale"":""missing hp""},
              {""type"":""Convert"",""from"":""Heal"",""to"":""Shield"",""scale"":""self.hpPct"",""chance"":0.3}]}
{""concept"":""Your dash leaves a mine; mines you leave make your fire abilities echo"",""semanticReason"":""대시를 자주 쓰는 당신에게"",
 ""rules"":[{""kind"":""Trigger"",""trigger"":""Dash"",""ops"":[{""op"":""Spawn"",""entity"":""Mine"",""at"":""DashOrigin""}]},
          {""kind"":""Trigger"",""trigger"":""EntityExpired"",""param"":""Mine"",""ops"":[{""op"":""Projectile"",""at"":""EventPos"",""mode"":""Ring"",""rel"":""Bounce"",""times"":2}]}]}";

        static string Request(GenerationContext ctx)
        {
            var user = new StringBuilder();
            user.AppendLine("PLAYER:");
            user.AppendLine(ctx.PlayerProfile());
            user.AppendLine("AVAILABLE LANGUAGE (retrieved primitives):");
            user.AppendLine(RuleLanguage.Describe(ctx.retrieved));
            user.AppendLine("EXPLORATION POOL (distant primitives — use in at most one candidate to surprise):");
            user.AppendLine(RuleLanguage.Describe(ctx.exploration, false));
            return user.ToString();
        }

        /// <summary>LLM #1: generate candidates (concept → Rule IR). Calls back with null on failure.</summary>
        public static IEnumerator Generate(GenerationContext ctx, int count, Action<List<AbilityDef>> done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { LastStatus = "API 키 없음 → 로컬 생성"; done(null); yield break; }

            LastPrompt = Request(ctx) +
                $"Generate {count} STRUCTURALLY DIFFERENT candidates (develop the dominant behavior / synergy with owned abilities / stateful, trade-off or exploratory). " +
                $"Use complexity up to tier {ctx.maxTier}. The \"abilities\" array MUST contain exactly {count} candidates. " +
                "Keep each one compact (1~2 mechanic macros, omit empty fields) but never skip candidates.";

            string json = null;
            yield return Post(key, SystemPrompt, LastPrompt, 0.9f, 1600, r => json = r);
            if (json == null) { done(null); yield break; }
            LastResponse = json;
            var list = Parse(json);
            if (list == null || list.Count == 0) { LastStatus = "AI 응답 형식 오류"; done(null); yield break; }
            LastStatus = $"AI 후보 {list.Count}개 수신";
            done(list);
        }

        /// <summary>
        /// Repair step: the validator's machine-readable errors go back to the LLM, which returns a corrected structure.
        /// Calls back with null on failure (the candidate is then dropped).
        /// </summary>
        public static IEnumerator Repair(GenerationContext ctx, List<(AbilityDef a, RuleValidator.Result r)> broken, Action<List<AbilityDef>> done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key) || broken.Count == 0) { done(null); yield break; }
            var sb = new StringBuilder(Request(ctx));
            sb.AppendLine("The engine's deterministic validator REJECTED these candidates. Fix each one so it passes, keeping its concept.");
            sb.AppendLine("Change the structure (trigger, conditions, ops, states, relation, temporal) — numbers are rebalanced by the engine anyway.");
            sb.AppendLine("Answer with direct rules drafts (states + rules) for the fixed candidates, keeping each concept.");
            for (int i = 0; i < broken.Count; i++)
            {
                sb.AppendLine($"candidate {i}: {StructureJson(broken[i].a)}");
                sb.AppendLine("errors:");
                sb.AppendLine(broken[i].r.Machine());
            }
            sb.AppendLine("Return {\"abilities\":[...]} with the fixed candidates in the same order.");
            string json = null;
            yield return Post(key, SystemPrompt, sb.ToString(), 0.4f, 1400, r => json = r);
            if (json == null) { done(null); yield break; }
            done(Parse(json));
        }

        static List<AbilityDef> Parse(string json)
        {
            try
            {
                var list = JsonUtility.FromJson<IntentList>(json)?.abilities;
                if (list == null) return null;
                return list.Where(i => i != null).Select(Compile).Where(a => a != null).ToList();
            }
            catch (Exception e)
            {
                LastStatus = "AI JSON 파싱 실패 (" + e.Message + ")";
                Debug.LogWarning("[AI] unparsable answer: " + (json.Length > 600 ? json.Substring(0, 600) + "…" : json));
                return null;
            }
        }

        /// <summary>Intent → Rule IR through the AbilityCompiler (aliases + macros). Compiler notes go to the debug log.</summary>
        static AbilityDef Compile(AbilityIntent intent)
        {
            var o = AbilityCompiler.Compile(intent);
            if (o.notes.Count > 0) Debug.Log("[AI compile] " + intent.concept + "\n  " + string.Join("\n  ", o.notes));
            var g = o.ability;
            if (g.rules.Count == 0) return null;
            Sanitize(g);
            return g;
        }

        static void Sanitize(AbilityDef g)
        {
            g.source = "ai";
            g.id = null; g.name = null; g.desc = null; g.tags = new List<string>(); g.power = 0; g.tier = 0;
            g.EnsureLists();
        }

        const string ChatAddendum =
@"

YOU ARE NOW TALKING WITH THE PLAYER on the evolution screen. They see the CURRENT CANDIDATES below and wrote you a message (Korean).
- Reply in Korean as the world's Storyteller: warm, 1~3 short sentences. Explain what you changed and why, or answer their question.
- Then output ONLY THE CARDS YOU CHANGE, each with ""card"" = its number (1-based). Do NOT repeat unchanged cards.
- Only change what the player asked for. If they only asked a question, return an empty list.
- Every rule above still applies (primitives only, the engine decides numbers). Keep each new card compact (1~2 macros).
OUTPUT JSON: {""reply"":""..."",""changes"":[ {""card"":2, ...same intent schema as above...} ]}";

        /// <summary>
        /// Evolution chat: the player talks to the AI about the current candidates; the AI answers and may revise them.
        /// Calls back (reply, revised) — both null on failure.
        /// </summary>
        public static IEnumerator Chat(GenerationContext ctx, List<AbilityDef> offer, List<(bool player, string text)> history, Action<string, List<AbilityDef>> done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { LastStatus = "API 키 없음"; done(null, null); yield break; }

            var first = new StringBuilder(Request(ctx));
            first.AppendLine("CURRENT CANDIDATES (shown to the player as numbered cards):");
            for (int i = 0; i < offer.Count; i++)
            {
                var g = offer[i];
                first.AppendLine($"card {i + 1}: \"{g.name}\" [tier {g.tier}] = {g.Explain().Replace("\n", " / ")}");
            }

            var msgs = new List<Msg> { new Msg { role = "system", content = SystemPrompt + ChatAddendum } };
            // The last few turns, oldest first. The candidate snapshot goes in front of the first player turn.
            int start = Mathf.Max(0, history.Count - 7);
            bool first_ = true;
            for (int i = start; i < history.Count; i++)
            {
                var (pl, text) = history[i];
                if (!pl && first_) continue;    // never start with an assistant turn
                string content = pl && first_ ? first + "\nPLAYER MESSAGE: " + text : text;
                msgs.Add(new Msg { role = pl ? "user" : "assistant", content = content });
                first_ = false;
            }
            if (first_) { done(null, null); yield break; }
            // Older turns referred to older cards: restate the current cards right before the latest message
            if (msgs.Count > 2)
            {
                var last = msgs[msgs.Count - 1];
                var cur = new StringBuilder("CURRENT CANDIDATES NOW:\n");
                for (int i = 0; i < offer.Count; i++) cur.AppendLine($"card {i + 1}: \"{offer[i].name}\" [tier {offer[i].tier}] = {offer[i].Explain().Replace("\n", " / ")}");
                last.content = cur + "\nPLAYER MESSAGE: " + last.content;
            }

            string json = null;
            yield return PostMessages(key, msgs, 0.8f, 1200, r => json = r);
            if (json == null) { done(null, null); yield break; }
            LastResponse = json;
            try
            {
                var resp = JsonUtility.FromJson<ChatResp>(json);
                if (resp == null || string.IsNullOrWhiteSpace(resp.reply)) { LastStatus = "AI 대화 응답 형식 오류"; done(null, null); yield break; }
                // Position-aligned list: null = keep that card (the model only sends what it changed)
                var compiled = new List<AbilityDef>();
                for (int i = 0; i < offer.Count; i++) compiled.Add(null);
                foreach (var ch in resp.changes ?? new List<CardChange>())
                    if (ch != null && ch.card >= 1 && ch.card <= offer.Count) compiled[ch.card - 1] = Compile(ch);
                LastStatus = "AI 대화 응답 수신";
                done(resp.reply.Trim(), compiled);
            }
            catch (Exception e)
            {
                LastStatus = "AI 대화 응답 해석 실패 (" + e.Message + ")";
                done(null, null);
            }
        }

        /// <summary>Ability structure without engine-decided numbers or display text (what the AI is allowed to author).</summary>
        public static string StructureJson(AbilityDef g)
        {
            var c = g.Clone();
            c.id = null; c.name = null; c.desc = null; c.tags = new List<string>(); c.power = 0; c.tier = 0; c.source = null; c.mechanic = null;
            foreach (var r in c.rules)
                foreach (var o in r.ops)
                {
                    if (o.op is "AddState" or "SetState") continue;
                    if (o.op == "ModifyStat") { o.amount = Mathf.Sign(o.amount); o.per = Mathf.Sign(o.per); }
                    else { o.amount = 0; o.per = 0; }
                    o.duration = 0; o.radius = 0; o.count = 0; o.delay = 0;
                }
            return JsonUtility.ToJson(c);
        }

        /// <summary>LLM #2: name/description for finalized abilities.</summary>
        public static IEnumerator NameAll(List<AbilityDef> abilities, string worldFlavor, Action done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { done(); yield break; }
            var sb = new StringBuilder();
            sb.AppendLine("World: a gentle pixel fantasy where rabbit wanderers live, die, and are remembered by the world. Region: " + worldFlavor);
            sb.AppendLine("For each FINALIZED ability below, write a short evocative Korean name (2~4 words) and a one-line Korean flavor sentence (max 30 characters, no numbers — the exact rules are shown separately).");
            for (int i = 0; i < abilities.Count; i++)
                sb.AppendLine($"id={i}: concept: {abilities[i].concept} | rules: {abilities[i].Explain().Replace("\n", " / ")} | tags: {string.Join(" ", abilities[i].tags)}");
            sb.AppendLine("Return JSON: {\"names\":[{\"id\":\"0\",\"name\":\"...\",\"desc\":\"...\"}]}");

            string json = null;
            yield return Post(key, "You are the Storyteller of a roguelite world. You name abilities. Output JSON only.", sb.ToString(), 0.8f, 700, r => json = r);
            if (json == null) { done(); yield break; }
            try
            {
                var list = JsonUtility.FromJson<NameList>(json);
                if (list?.names != null)
                    foreach (var n in list.names)
                        if (int.TryParse(n.id, out int i) && i >= 0 && i < abilities.Count && !string.IsNullOrWhiteSpace(n.name))
                        {
                            abilities[i].name = n.name.Trim();
                            if (!string.IsNullOrWhiteSpace(n.desc)) abilities[i].desc = n.desc.Trim();
                        }
            }
            catch { }
            done();
        }

        /// <summary>Storyteller: epitaph for a fallen hero (one line).</summary>
        public static IEnumerator Epitaph(GraveRecord g, Action<string> done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { done(null); yield break; }
            var abil = string.Join(", ", g.abilities.ConvertAll(a => a.name));
            string prompt = $"Write a one-sentence Korean epitaph (max 40 characters) for a rabbit hero. Name: {g.heroName}. Weapon: {DB.W((WeaponKind)g.weapon).name}. Level {g.level}. Kills {g.kills}. Cause of death: {g.cause}. Abilities: {abil}. Return JSON {{\"text\":\"...\"}}";
            string json = null;
            yield return Post(key, "You are the Storyteller. Output JSON only.", prompt, 0.9f, 120, r => json = r);
            if (json == null) { done(null); yield break; }
            try { done(JsonUtility.FromJson<TextOnly>(json)?.text); } catch { done(null); }
        }

        [Serializable] class TextOnly { public string text; }

        /// <summary>
        /// Seconds an AI request may take. Rule IR answers are long (the model only starts sending once the whole JSON is
        /// written), so this is far above the old 25 s; anything slower falls back to the local generator.
        /// </summary>
        public const int TimeoutSeconds = 75;

        static IEnumerator Post(string key, string system, string user, float temp, int maxTokens, Action<string> done) =>
            PostMessages(key, new List<Msg> { new Msg { role = "system", content = system }, new Msg { role = "user", content = user } }, temp, maxTokens, done);

        static IEnumerator PostMessages(string key, List<Msg> messages, float temp, int maxTokens, Action<string> done)
        {
            string content = null; bool truncated = false;
            yield return PostOnce(key, messages, temp, maxTokens, (c, t) => { content = c; truncated = t; });
            if (truncated)
            {
                Debug.LogWarning($"[AI] answer cut at {maxTokens} tokens — retrying once with {maxTokens * 2}");
                yield return PostOnce(key, messages, temp, maxTokens * 2, (c, t) => { content = c; truncated = t; });
                if (truncated) { LastStatus = "AI 응답이 너무 길어 잘림"; content = null; }
            }
            done(content);
        }

        static IEnumerator PostOnce(string key, List<Msg> messages, float temp, int maxTokens, Action<string, bool> done)
        {
            float started = Time.realtimeSinceStartup;
            LastFatal = false;
            var req = new Req
            {
                model = Game.Settings.aiModel,
                temperature = temp,
                max_tokens = maxTokens,
                response_format = new RespFmt(),
                messages = messages,
            };
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(req));
            using var www = new UnityWebRequest(Game.Settings.aiEndpoint, "POST");
            www.uploadHandler = new UploadHandlerRaw(body);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + key);
            www.timeout = TimeoutSeconds;
            LastStatus = "AI 호출 중…";
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                string msg = www.downloadHandler?.text ?? "";
                bool quota = msg.Contains("insufficient_quota");
                // UnityWebRequest reports a timeout as a connection error with code 0 — tell them apart by the elapsed time
                bool timedOut = www.responseCode == 0 && (www.error?.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                                                          || Time.realtimeSinceStartup - started >= TimeoutSeconds - 1);
                LastFatal = www.responseCode == 401 || www.responseCode == 404 || quota;
                LastStatus = www.responseCode switch
                {
                    401 => "API 키가 올바르지 않음(401)",
                    429 => quota ? "크레딧/결제 한도 없음(429 quota)" : "요청 한도 초과(429)",
                    404 => "모델을 사용할 수 없음(404)",
                    0 => timedOut ? $"AI 응답 시간 초과({TimeoutSeconds}초)" : "네트워크 연결 실패",
                    _ => $"AI 호출 실패({www.responseCode})",
                };
                Debug.LogWarning("[AI] " + LastStatus + " — " + www.error + "\n" + msg);
                done(null, false);
                yield break;
            }
            Debug.Log($"[AI] {(Time.realtimeSinceStartup - started):0.0}s · {body.Length / 1024}KB prompt · {www.downloadHandler.text.Length / 1024}KB response");
            try
            {
                var resp = JsonUtility.FromJson<Resp>(www.downloadHandler.text);
                var ch = resp?.choices != null && resp.choices.Count > 0 ? resp.choices[0] : null;
                done(ch?.message?.content, ch?.finish_reason == "length");
            }
            catch (Exception e) { LastStatus = "AI 응답 해석 실패"; Debug.LogWarning(e); done(null, false); }
        }
    }
}
