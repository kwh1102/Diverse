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

        public static string Model => string.IsNullOrEmpty(Game.Settings.imageModel) ? "gpt-image-1-mini" : Game.Settings.imageModel;

        /// <summary>USD per 1024x1024 low-quality image (for the studio's cost line). Unknown models → null.</summary>
        public static float? PriceUsd(string model) => model switch
        {
            "gpt-image-1-mini" => 0.005f,
            "gpt-image-1" => 0.011f,
            _ => null,
        };

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
        [Serializable] class Resp { public List<Item> data; }

        /// <summary>One picture. Calls back with null on failure (LastStatus says why).</summary>
        /// <param name="transparent">false for full-bleed textures (ground swatches).</param>
        public static IEnumerator Generate(string prompt, Action<Texture2D> done, bool transparent = true)
        {
            var key = SaveSystem.LoadApiKey();
            if (string.IsNullOrEmpty(key)) { LastStatus = "API 키 없음"; done(null); yield break; }
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Req { model = Model, prompt = prompt, background = transparent ? "transparent" : "opaque" }));
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
            try
            {
                var resp = JsonUtility.FromJson<Resp>(www.downloadHandler.text);
                var b64 = resp?.data != null && resp.data.Count > 0 ? resp.data[0].b64_json : null;
                if (!string.IsNullOrEmpty(b64))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(Convert.FromBase64String(b64))) { UnityEngine.Object.Destroy(tex); tex = null; }
                }
            }
            catch (Exception e) { Debug.LogWarning("[AI image] parse failed: " + e.Message); tex = null; }
            LastStatus = tex != null ? "그림 수신" : "그림 응답 형식 오류";
            Debug.Log($"[AI image] {Time.realtimeSinceStartup - started:0.0}s · {Model} · {(tex != null ? tex.width + "x" + tex.height : "none")}");
            done(tex);
        }
    }
}
