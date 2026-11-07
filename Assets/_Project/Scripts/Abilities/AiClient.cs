using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Diverse
{
    /// <summary>Shared OpenAI-compatible text/vision transport for designs, active skills and epitaphs.</summary>
    public static class AiClient
    {
        public static string LastStatus = "";
        public static string LastPrompt = "";
        public static string LastResponse = "";
        /// <summary>The last call failed in a way retrying can't fix (bad key, unknown model, no credit).</summary>
        public static bool LastFatal;
        public static bool HasKey => !string.IsNullOrEmpty(SaveSystem.LoadApiKey());
        public static bool Enabled => Game.Settings.aiEnabled && HasKey;

        /// <summary>
        /// Selectable models. Classic chat models take temperature + max_tokens; reasoning models (IsReasoning) take
        /// max_completion_tokens + reasoning_effort instead — PostOnce sends whichever the model accepts.
        /// </summary>
        public static readonly (string id, string label, string desc)[] Models =
        {
            ("gpt-6-luna", "GPT-6 Luna", "최신 · 가장 저렴하고 똑똑함 (기본값)"),
            ("gpt-4o-mini", "GPT-4o mini", "이전 기본값, 빠름"),
            ("gpt-4.1-mini", "GPT-4.1 mini", "조금 더 똑똑함, 비용 약 2배"),
            ("gpt-4.1-nano", "GPT-4.1 nano", "가장 빠르고 저렴함, 품질은 낮음"),
            ("gpt-4.1", "GPT-4.1", "가장 높은 품질, 느리고 비쌈"),
        };

        /// <summary>USD per 1M tokens (input, output) for the cost estimate shown in the design studio. Unknown models → null.</summary>
        static readonly (string id, float input, float output)[] Prices =
        {
            ("gpt-6-luna", 0.10f, 0.50f), ("gpt-4o-mini", 0.15f, 0.60f), ("gpt-4.1-mini", 0.40f, 1.60f), ("gpt-4.1-nano", 0.10f, 0.40f), ("gpt-4.1", 2.00f, 8.00f),
        };

        /// <summary>Token usage of the last successful call (0 when the endpoint didn't report it).</summary>
        public static int LastPromptTokens, LastCompletionTokens;

        public static float? CostUsd(string model, int promptTokens, int completionTokens)
        {
            foreach (var p in Prices) if (p.id == model) return (promptTokens * p.input + completionTokens * p.output) / 1_000_000f;
            return null;
        }

        public static bool IsPresetModel(string id) => Array.Exists(Models, m => m.id == id);
        public static string ModelLabel(string id) { foreach (var m in Models) if (m.id == id) return m.label; return id; }

        [Serializable] class Msg { public string role; public string content; }
        [Serializable] class RespFmt { public string type = "json_object"; }
        [Serializable] class Req { public string model; public List<Msg> messages; public float temperature; public RespFmt response_format; public int max_tokens; }
        [Serializable] class ReasoningReq { public string model; public List<Msg> messages; public RespFmt response_format; public int max_completion_tokens; public string reasoning_effort = "low"; }

        /// <summary>gpt-5 / gpt-6 / o-series: no temperature, completion budget includes hidden reasoning tokens.</summary>
        static bool IsReasoning(string model) => model != null && (model.StartsWith("gpt-5") || model.StartsWith("gpt-6") || model.StartsWith("o1") || model.StartsWith("o3") || model.StartsWith("o4"));
        /// <summary>Models whose request format turned out to be the other one (a 400 "unsupported parameter" flips it once).</summary>
        static readonly HashSet<string> flippedFormat = new HashSet<string>();
        [Serializable] class Choice { public Msg message; public string finish_reason; }
        [Serializable] class Usage { public int prompt_tokens; public int completion_tokens; }
        [Serializable] class Resp { public List<Choice> choices; public Usage usage; }
        /// <summary>Storyteller: epitaph for a fallen hero (one line).</summary>
        public static IEnumerator Epitaph(GraveRecord g, Action<string> done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { done(null); yield break; }
            var abil = string.Join(", ", g.abilities.ConvertAll(a => a.name));
            string prompt = $"Write a one-sentence Korean epitaph (max 40 characters) for a hero ({DB.Costume(g.costume).name}). Name: {g.heroName}. Weapon: {DesignStore.WeaponName(g.costume, g.weapon, g.weaponType)}. Level {g.level}. Kills {g.kills}. Cause of death: {g.cause}. Abilities: {abil}. Return JSON {{\"text\":\"...\"}}";
            string json = null;
            yield return Post(key, "You are the Storyteller. Output JSON only.", prompt, 0.9f, 120, r => json = r);
            if (json == null) { done(null); yield break; }
            try { done(JsonUtility.FromJson<TextOnly>(json)?.text); } catch { done(null); }
        }

        [Serializable] class TextOnly { public string text; }

        /// <summary>
        /// Seconds an AI request may take. Generated skill answers can be long (the model only starts sending once the whole JSON is
        /// written), so this is far above the old 25 s; anything slower falls back to the local generator.
        /// </summary>
        public const int TimeoutSeconds = 75;

        /// <summary>One JSON-mode completion for other generators (design studio). Calls back with null on failure (see LastStatus).</summary>
        public static IEnumerator Complete(string system, string user, float temp, int maxTokens, Action<string> done)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { LastStatus = "API 키 없음"; done(null); yield break; }
            yield return Post(key, system, user, temp, maxTokens, done);
        }

        static IEnumerator Post(string key, string system, string user, float temp, int maxTokens, Action<string> done) =>
            PostMessages(key, new List<Msg> { new Msg { role = "system", content = system }, new Msg { role = "user", content = user } }, temp, maxTokens, done);

        const string ImageMarker = "__DIVERSE_IMAGE_CONTENT__";

        [Serializable] class Str { public string s; }
        static string JsonString(string s) { var j = JsonUtility.ToJson(new Str { s = s }); return j.Substring(5, j.Length - 6); }

        /// <summary>
        /// One JSON-mode question about a picture (low detail: a few hundred tokens). Used by the design studio to check which
        /// way generated sprites face. Calls back with null on failure.
        /// </summary>
        public static IEnumerator CompleteVision(string system, string question, byte[] png, int maxTokens, Action<string> done, string detail = "low")
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { LastStatus = "API 키 없음"; done(null); yield break; }
            var content = "[{\"type\":\"text\",\"text\":" + JsonString(question) + "},{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,"
                          + Convert.ToBase64String(png) + "\",\"detail\":\"" + detail + "\"}}]";
            var msgs = new List<Msg> { new Msg { role = "system", content = system }, new Msg { role = "user", content = ImageMarker } };
            string answer = null;
            yield return PostOnce(key, msgs, 0, maxTokens, (c, _) => answer = c, content);
            done(answer);
        }

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

        /// <param name="imageContent">Vision: JSON content array that replaces the user message marked ImageMarker.</param>
        static IEnumerator PostOnce(string key, List<Msg> messages, float temp, int maxTokens, Action<string, bool> done, string imageContent = null, bool retried = false)
        {
            float started = Time.realtimeSinceStartup;
            LastFatal = false;
            string model = Game.Settings.aiModel;
            bool reasoning = IsReasoning(model) != flippedFormat.Contains(model);
            // Reasoning models spend hidden tokens thinking before the answer: give the budget headroom (low effort keeps it small)
            var json = reasoning
                ? JsonUtility.ToJson(new ReasoningReq { model = model, messages = messages, response_format = new RespFmt(), max_completion_tokens = maxTokens + 1500 })
                : JsonUtility.ToJson(new Req { model = model, temperature = temp, max_tokens = maxTokens, response_format = new RespFmt(), messages = messages });
            if (imageContent != null) json = json.Replace("\"" + ImageMarker + "\"", imageContent);
            var body = Encoding.UTF8.GetBytes(json);
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
                // The model wants the other request format (e.g. no temperature / max_completion_tokens): remember and retry once
                if (www.responseCode == 400 && !retried && (msg.Contains("unsupported_parameter") || msg.Contains("Unsupported parameter") || msg.Contains("unsupported_value")))
                {
                    if (!flippedFormat.Add(model)) flippedFormat.Remove(model);
                    Debug.LogWarning($"[AI] {model} rejected the {(reasoning ? "reasoning" : "classic")} request format — retrying with the other one");
                    yield return PostOnce(key, messages, temp, maxTokens, done, imageContent, true);
                    yield break;
                }
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
                LastPromptTokens = resp?.usage?.prompt_tokens ?? 0;
                LastCompletionTokens = resp?.usage?.completion_tokens ?? 0;
                done(ch?.message?.content, ch?.finish_reason == "length");
            }
            catch (Exception e) { LastStatus = "AI 응답 해석 실패"; Debug.LogWarning(e); done(null, false); }
        }
    }
}
