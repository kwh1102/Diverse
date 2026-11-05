using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Diverse
{
    /// <summary>
    /// OpenAI Images API (same key and base URL as AiClient). Low quality is enough: the picture is shrunk to a few dozen
    /// pixels by Pixelizer anyway, so detail above that is paid for and thrown away.
    /// </summary>
    public static class ImageClient
    {
        public const int TimeoutSeconds = 150;
        public static string LastStatus = "";
        [Serializable] public class TokenDetails { public int text_tokens, image_tokens; }
        [Serializable] public class ImageUsage
        {
            public int input_tokens, output_tokens, total_tokens;
            public TokenDetails input_tokens_details;
            public float? CostUsd(string model) => model is "gpt-image-2.5-flare" or "gpt-image-2.5-sunburst" or "gpt-image-2.6-flare"
                ? ((input_tokens_details?.text_tokens ?? input_tokens) * 5f + (input_tokens_details?.image_tokens ?? 0) * 8f + output_tokens * 30f) / 1000000f : null;
        }

        public static string Model => string.IsNullOrEmpty(Game.Settings.imageModel) ? "gpt-image-2.5-flare" : Game.Settings.imageModel;
        static string Quality => Game.Settings.imageQuality == "medium" ? "medium" : "low";

        /// <summary>
        /// Approximate USD per 1024x1024 picture at the current quality (for the studio's cost line; the API bills tokens).
        /// Wide sheets (1536x1024) cost about 1.5x. Unknown models → null.
        /// </summary>
        public static float? PriceUsd(string model, string quality = null)
        {
            float? low = model switch
            {
                "gpt-image-2.5-flare" or "gpt-image-2.5-sunburst" or "gpt-image-2" => 0.006f,
                "gpt-image-2.6-flare" => 0.02469f,
                "gpt-image-1-mini" => 0.005f,
                "gpt-image-1.5" or "gpt-image-1" => 0.011f,
                _ => null,
            };
            return low * ((quality ?? Quality) == "medium" ? 4 : 1);
        }

        /// <summary>A 3x2 sheet (6 poses) on a wide canvas: every cell is a full 512x512 instead of 341x512.</summary>
        public const string WideSize = "1536x1024";

        static string Endpoint
        {
            get
            {
                var chat = Game.Settings.aiEndpoint ?? "";
                int i = chat.IndexOf("/chat/completions", StringComparison.Ordinal);
                return i >= 0 ? chat.Substring(0, i) + "/images/generations" : "https://api.openai.com/v1/images/generations";
            }
        }

        [Serializable] class Req { public string model, prompt; public string size = "1024x1024", quality = "low", background = "transparent", output_format = "png"; public int n = 1; }
        [Serializable] class Item { public string b64_json; }
        [Serializable] class Resp { public List<Item> data; public ImageUsage usage; }

        /// <summary>One picture. Calls back with null on failure (LastStatus says why).</summary>
        /// <param name="transparent">false for full-bleed textures (ground swatches).</param>
        public static IEnumerator Generate(string prompt, Action<Texture2D> done, bool transparent = true, string size = "1024x1024", string model = null, string quality = null, Action<ImageUsage> usage = null)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { LastStatus = "API 키 없음"; done(null); yield break; }
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Req { model = model ?? Model, prompt = prompt, size = size, quality = quality ?? Quality, background = transparent ? "transparent" : "opaque" }));
            using var www = new UnityWebRequest(Endpoint, "POST");
            www.uploadHandler = new UploadHandlerRaw(body);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + key);
            www.timeout = TimeoutSeconds;
            float started = Time.realtimeSinceStartup;
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                string msg = www.downloadHandler?.text ?? "";
                LastStatus = www.responseCode switch
                {
                    401 => "API 키가 올바르지 않음(401)",
                    403 => "이미지 모델 권한 없음(403, 조직 인증 필요할 수 있음)",
                    429 => msg.Contains("insufficient_quota") ? "크레딧/결제 한도 없음(429)" : "요청 한도 초과(429)",
                    400 => msg.Contains("safety") || msg.Contains("moderation") ? "그림 요청이 안전 필터에 걸림" : "그림 요청 오류(400)",
                    0 => "그림 응답 시간 초과 또는 네트워크 실패",
                    _ => $"그림 생성 실패({www.responseCode})",
                };
                Debug.LogWarning("[AI image] " + LastStatus + " — " + www.error + "\n" + (msg.Length > 400 ? msg.Substring(0, 400) : msg));
                done(null);
                yield break;
            }
            Texture2D tex = null;
            ImageUsage measuredUsage = null;
            try
            {
                var resp = JsonUtility.FromJson<Resp>(www.downloadHandler.text);
                measuredUsage = resp?.usage;
                var b64 = resp?.data != null && resp.data.Count > 0 ? resp.data[0].b64_json : null;
                if (!string.IsNullOrEmpty(b64))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(Convert.FromBase64String(b64))) { UnityEngine.Object.Destroy(tex); tex = null; }
                }
            }
            catch (Exception e) { Debug.LogWarning("[AI image] parse failed: " + e.Message); if (tex != null) UnityEngine.Object.Destroy(tex); tex = null; }
            usage?.Invoke(measuredUsage);
            LastStatus = tex != null ? "그림 수신" : "그림 응답 형식 오류";
            Debug.Log($"[AI image] {Time.realtimeSinceStartup - started:0.0}s · {model ?? Model} · {(tex != null ? tex.width + "x" + tex.height : "none")}");
            done(tex);
        }
    }
}
