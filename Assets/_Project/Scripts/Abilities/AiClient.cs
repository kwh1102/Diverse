using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Diverse
{
    /// <summary>
    /// OpenAI-compatible Chat Completions client.
    /// - LLM #1 (Mechanic Generator): Generation Context + atomic language → returns only AbilityGraph JSON
    /// - LLM #2 (Name/Flavor): validated & balanced mechanic → Korean name/description
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
        [Serializable] class Choice { public Msg message; }
        [Serializable] class Resp { public List<Choice> choices; }
        [Serializable] class GraphList { public List<AbilityGraph> abilities; }
        [Serializable] class NameItem { public string id; public string name; public string desc; }
        [Serializable] class NameList { public List<NameItem> names; }
        [Serializable] class ChatResp { public string reply; public List<AbilityGraph> abilities; }

        const string SystemPrompt =
@"You generate atomic abilities for a pixel-art action roguelite where abilities EVOLVE from how the player actually plays.

GOAL: Invent NEW mechanics that reflect the player's behavior, current build, equipment and apparent intent.

RULES:
- Do NOT pick from predefined skills. Construct new Ability Graphs from the provided primitives ONLY.
- Prefer simple single mechanics that COMBINE with existing abilities (small pieces -> complex build).
- Do not duplicate effects the player already owns.
- Existing modifiers automatically affect matching tags (the engine resolves synergy; you don't need to).
- Each ability introduces at most ONE major new mechanic.
- If a SEEDED SOURCE exists (player owns a modifier but nothing generates it), at least one candidate should give it a generator.
- Abilities must be causally coherent: the effect should feel like a natural consequence of the trigger.
- Do NOT output numbers for balance (power/duration/radius are decided by the engine). Only structure.
- World rules must be respected unless you explicitly justify breaking one in semanticReason.
- ""action"" must be one of the listed Action atoms. Relation atoms (Copy, Chain, SwapPosition, Orbit...) go ONLY in ""relation"", never in ""action"".
- ""trigger"" must be an Event atom. Spawn needs an ""entity"" (Clone, Turret, Orb, Zone, PhantomWeapon).

OUTPUT JSON SCHEMA:
{""abilities"":[
 {""mechanic"":""snake_case_id"",
  ""kind"":""trigger"",               // trigger | modifier | stat
  ""trigger"":""<Event atom>"",
  ""conditions"":[{""type"":""<Condition atom or EveryNth>"",""value"":0.3,""text"":""burn""}],
  ""effects"":[{""action"":""<Damage|Projectile|Nova|Lightning|Spawn|Heal|Shield|Buff|ApplyStatus|Pull|Push|Blink|ResetCooldown|Store|Release>"",""entity"":""<Entity or empty>"",""form"":""<Form or empty>"",""element"":""<Element or empty>"",""relation"":""<Relation or empty>"",""temporal"":""<Temporal or empty>"",""stat"":""<for Buff: Attack|AttackSpeed|MoveSpeed|CritChance|Armor>""}],
  ""modTag"":""<for modifier: tag to amplify>"",
  ""stat"":""<for stat kind: StatId>"",
  ""semanticReason"":""short Korean sentence: why this fits the player""}
]}";

        /// <summary>LLM #1: generate candidates. Calls back with null on failure.</summary>
        public static IEnumerator Generate(GenerationContext ctx, int count, Action<List<AbilityGraph>> done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { LastStatus = "API 키 없음 → 로컬 생성"; done(null); yield break; }

            var user = new StringBuilder();
            user.AppendLine("PLAYER:");
            user.AppendLine(ctx.PlayerProfile());
            user.AppendLine("AVAILABLE LANGUAGE (retrieved primitives):");
            user.AppendLine(AbilityLanguage.Describe(ctx.retrieved));
            user.AppendLine("EXPLORATION POOL (distant primitives — use in at most one candidate to surprise):");
            user.AppendLine(AbilityLanguage.Describe(ctx.exploration));
            user.AppendLine($"Generate {count} DIFFERENT candidates: 1 that develops the dominant behavior, 1 that builds synergy with the current graph/seeded sources, 1 exploratory.");
            LastPrompt = user.ToString();

            string json = null;
            yield return Post(key, SystemPrompt, LastPrompt, 0.9f, 1200, r => json = r);
            if (json == null) { done(null); yield break; }
            LastResponse = json;
            try
            {
                var list = JsonUtility.FromJson<GraphList>(json);
                if (list?.abilities == null || list.abilities.Count == 0) { LastStatus = "AI 응답 형식 오류"; done(null); yield break; }
                foreach (var g in list.abilities) Sanitize(g);
                LastStatus = $"AI 후보 {list.abilities.Count}개 수신";
                done(list.abilities);
            }
            catch (Exception e)
            {
                LastStatus = "AI JSON 파싱 실패 (" + e.Message + ")";
                done(null);
            }
        }

        static void Sanitize(AbilityGraph g)
        {
            g.source = "ai";
            g.conditions ??= new List<CondNode>();
            g.effects ??= new List<EffectNode>();
            g.tags ??= new List<string>();
        }

        const string ChatAddendum =
@"

YOU ARE NOW TALKING WITH THE PLAYER on the evolution screen. They see the 3 CURRENT CANDIDATES below and wrote you a message (Korean).
- Reply in Korean as the world's Storyteller: warm, 1~3 short sentences. Explain what you changed and why, or answer their question.
- Then output the REVISED list of exactly 3 candidates. Keep candidates the player didn't complain about unchanged (copy them as-is).
- Only change what the player asked for. If they only asked a question, return the same 3 candidates.
- Every rule above still applies (primitives only, no balance numbers — the engine decides numbers).
OUTPUT JSON: {""reply"":""..."",""abilities"":[ ...same ability schema as above... ]}";

        /// <summary>
        /// Evolution chat: the player talks to the AI about the current candidates; the AI answers and may revise them.
        /// Calls back (reply, revised) — both null on failure.
        /// </summary>
        public static IEnumerator Chat(GenerationContext ctx, List<AbilityGraph> offer, List<(bool player, string text)> history, Action<string, List<AbilityGraph>> done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { LastStatus = "API 키 없음"; done(null, null); yield break; }

            var first = new StringBuilder();
            first.AppendLine("PLAYER:");
            first.AppendLine(ctx.PlayerProfile());
            first.AppendLine("AVAILABLE LANGUAGE:");
            first.AppendLine(AbilityLanguage.Describe(ctx.retrieved));
            first.AppendLine(AbilityLanguage.Describe(ctx.exploration));
            first.AppendLine("CURRENT CANDIDATES (shown to the player as cards 1, 2, 3):");
            for (int i = 0; i < offer.Count; i++)
            {
                var g = offer[i];
                first.AppendLine($"card {i + 1}: \"{g.name}\" = {g.Explain()}");
                first.AppendLine("  structure: " + StructureJson(g));
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
                for (int i = 0; i < offer.Count; i++) cur.AppendLine($"card {i + 1}: \"{offer[i].name}\" = {offer[i].Explain()} | structure: {StructureJson(offer[i])}");
                last.content = cur + "\nPLAYER MESSAGE: " + last.content;
            }

            string json = null;
            yield return PostMessages(key, msgs, 0.8f, 1400, r => json = r);
            if (json == null) { done(null, null); yield break; }
            LastResponse = json;
            try
            {
                var resp = JsonUtility.FromJson<ChatResp>(json);
                if (resp == null || string.IsNullOrWhiteSpace(resp.reply)) { LastStatus = "AI 대화 응답 형식 오류"; done(null, null); yield break; }
                if (resp.abilities != null) foreach (var g in resp.abilities) Sanitize(g);
                LastStatus = "AI 대화 응답 수신";
                done(resp.reply.Trim(), resp.abilities ?? new List<AbilityGraph>());
            }
            catch (Exception e)
            {
                LastStatus = "AI 대화 응답 해석 실패 (" + e.Message + ")";
                done(null, null);
            }
        }

        /// <summary>Ability structure without engine-decided numbers or display text (what the AI is allowed to author).</summary>
        static string StructureJson(AbilityGraph g)
        {
            var c = g.Clone();
            c.id = null; c.name = null; c.desc = null; c.tags = new List<string>(); c.cost = 0; c.statValue = 0; c.modMul = 0; c.source = null;
            foreach (var e in c.effects) { e.power = 0; e.radius = 0; e.duration = 0; e.delay = 0; e.count = 0; }
            return JsonUtility.ToJson(c);
        }

        /// <summary>LLM #2: name/description for finalized mechanics.</summary>
        public static IEnumerator NameAll(List<AbilityGraph> graphs, string worldFlavor, Action done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { done(); yield break; }
            var sb = new StringBuilder();
            sb.AppendLine("World: a gentle pixel fantasy where rabbit wanderers live, die, and are remembered by the world. Region: " + worldFlavor);
            sb.AppendLine("For each FINALIZED mechanic below, write a short evocative Korean name (2~4 words) and a one-line Korean description that states the exact numbers given.");
            for (int i = 0; i < graphs.Count; i++) sb.AppendLine($"id={i}: {graphs[i].Explain()} | tags: {string.Join(" ", graphs[i].tags)}");
            sb.AppendLine("Return JSON: {\"names\":[{\"id\":\"0\",\"name\":\"...\",\"desc\":\"...\"}]}");

            string json = null;
            yield return Post(key, "You are the Storyteller of a roguelite world. You name abilities. Output JSON only.", sb.ToString(), 0.8f, 500, r => json = r);
            if (json == null) { done(); yield break; }
            try
            {
                var list = JsonUtility.FromJson<NameList>(json);
                if (list?.names != null)
                    foreach (var n in list.names)
                        if (int.TryParse(n.id, out int i) && i >= 0 && i < graphs.Count && !string.IsNullOrWhiteSpace(n.name))
                        {
                            graphs[i].name = n.name.Trim();
                            if (!string.IsNullOrWhiteSpace(n.desc)) graphs[i].desc = n.desc.Trim();
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

        static IEnumerator Post(string key, string system, string user, float temp, int maxTokens, Action<string> done) =>
            PostMessages(key, new List<Msg> { new Msg { role = "system", content = system }, new Msg { role = "user", content = user } }, temp, maxTokens, done);

        static IEnumerator PostMessages(string key, List<Msg> messages, float temp, int maxTokens, Action<string> done)
        {
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
            www.timeout = 25;
            LastStatus = "AI 호출 중…";
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                string msg = www.downloadHandler?.text ?? "";
                bool quota = msg.Contains("insufficient_quota");
                LastFatal = www.responseCode == 401 || www.responseCode == 404 || quota;
                LastStatus = www.responseCode switch
                {
                    401 => "API 키가 올바르지 않음(401)",
                    429 => quota ? "크레딧/결제 한도 없음(429 quota)" : "요청 한도 초과(429)",
                    404 => "모델을 사용할 수 없음(404)",
                    0 => "네트워크 연결 실패",
                    _ => $"AI 호출 실패({www.responseCode})",
                };
                Debug.LogWarning("[AI] " + LastStatus + "\n" + msg);
                done(null);
                yield break;
            }
            try
            {
                var resp = JsonUtility.FromJson<Resp>(www.downloadHandler.text);
                done(resp?.choices != null && resp.choices.Count > 0 ? resp.choices[0].message.content : null);
            }
            catch (Exception e) { LastStatus = "AI 응답 해석 실패"; Debug.LogWarning(e); done(null); }
        }
    }
}
