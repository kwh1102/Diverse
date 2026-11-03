#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public class EnvironmentReviewRunner : MonoBehaviour
    {
        const string Output = "Logs/Environment/Scenes";
        bool failed;
        void OnEnable() => Application.logMessageReceived += Log;
        void OnDisable() => Application.logMessageReceived -= Log;
        void Log(string message, string trace, LogType type)
        {
            if (failed || type is not (LogType.Exception or LogType.Error or LogType.Assert)) return;
            failed = true; File.WriteAllText("Logs/Environment/error.txt", message + "\n" + trace); Finish(false);
        }
        static void Finish(bool ok) { SessionState.SetBool("Diverse.EnvironmentCapture.OK", ok); EditorApplication.ExitPlaymode(); }
        bool Check(bool valid, string why) { if (!valid) Debug.LogError("[EnvironmentReview] " + why); return valid; }

        IEnumerator Start()
        {
            for (int i = 0; i < 12; i++) yield return null;
            var game = Game.I; game.Player.Level = 27; game.World.bossKills = 10; game.Player.invulnUntil = float.MaxValue;
            var spawner = World.I.GetComponent<EnemySpawner>(); if (spawner != null) spawner.enabled = false;
            File.WriteAllText("Logs/Environment/capture_checks.txt", "");
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            string selected = SessionState.GetString("Diverse.EnvironmentCapture.Themes", "");
            var themes = string.IsNullOrEmpty(selected) ? new[] { "overgrowth", "volcano", "crystal", "glacier", "canyon", "fortress", "mirror", "swamp" } : selected.Split(',');
            foreach (string id in themes)
            {
                game.World.zoneThemes.Clear(); game.World.zoneThemes.AddRange(Enumerable.Repeat(id, ZoneMap.Count));
                World.I.ResetForRun(); World.I.Init(game.World.seed);
                var w = WorldStreamer.I; var environment = w.Environment;
                environment.ReviewTime = 0;
                SceneryLayout.Group group = null;
                for (int y = -5; y <= 5; y++) for (int x = -5; x <= 5; x++)
                {
                    var candidate = w.Scenery.GroupAt(x, y);
                    if (candidate != null && candidate.theme.id == id && candidate.center.magnitude > 85 &&
                        (group == null || candidate.center.sqrMagnitude < group.center.sqrMagnitude)) group = candidate;
                }
                if (!Check(group != null, id + " has no representative place")) yield break;
                var at = group.center; w.Warm(at);
                game.Player.Pos = w.NearestWalkable(at + Vector2.down * 3, game.Player.radius, 8, false); game.Player.StopMoving();
                CameraRig.I.target = null; CameraRig.I.Snap(at);
                foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) enemy.enabled = false;
                var chunk = w.ChunkAt(at);
                var fire = chunk.AddDecor(Art.Prop("campfire"), at + new Vector2(4.5f, -3), false, 0, 0, name: "review campfire");
                var warp = Interactable.Create(chunk.transform, "warpstone", "environment-review", "검토용 워프 스톤", at + new Vector2(-4.5f, -3), Art.Prop("warpstone"), chunk);
                chunk.AddDecor(Art.Prop("banner"), at + new Vector2(4.5f, 1), false, 0, 0, name: "review flag");
                for (int i = 0; i < 12; i++) yield return null;
                var sources = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Where(sr => sr.sprite != null &&
                    (sr.sprite.name.StartsWith("tree_") || sr.sprite.name.StartsWith("explore_"))).ToArray();
                var sprites = sources.Select(sr => sr.sprite).ToArray(); var positions = sources.Select(sr => sr.transform.position).ToArray();
                var first = new Color32[0]; int differences = 0;
                int frames = id is "overgrowth" or "volcano" ? 24 : 4;
                for (int frame = 0; frame < frames; frame++)
                {
                    environment.ReviewTime = frame * (frames > 4 ? .125f : .625f);
                    for (int i = 0; i < 2; i++) yield return null;
                    var pixels = Shot(id + "_" + frame);
                    if (frame == 0) first = pixels;
                    else for (int i = 0; i < pixels.Length; i++) if (!pixels[i].Equals(first[i])) differences++;
                }
                if (!Check(differences > 100, id + " rendered frames did not animate")) yield break;
                if (id is "overgrowth" or "volcano")
                {
                    double off = FixedRender(false), on = FixedRender(true);
                    File.AppendAllText("Logs/Environment/render_benchmark.txt", id + ": fixed 960x600 camera, median render+GPU readback (20 samples after 4 warmups): frozen=" + off.ToString("0.00") + " ms, animated=" + on.ToString("0.00") + " ms. Includes CPU/readback; not standalone GPU frame time.\n");
                }
                for (int i = 0; i < sources.Length; i++)
                    if (!Check(sources[i].sprite == sprites[i] && sources[i].transform.position == positions[i], id + " source sprite or base moved")) yield break;
                var goal = w.NearestWalkable(at, game.Player.radius, 6, false);
                var path = PathFinder.Find(w, game.Player.Pos, goal);
                if (!Check(path.Count > 0 && Vector2.Distance(path[path.Count - 1], goal) <= 1, id + " animated scenery blocked the place")) yield break;
                if (!Check(fire.sprite == Art.Prop("campfire") && warp.sr.sprite == Art.Prop("warpstone"), "Overlay replaced the existing object identity")) yield break;
                environment.ReviewTime = -1;
                int maximum = 0;
                for (int i = 0; i < 100; i++)
                {
                    maximum = Mathf.Max(maximum, environment.ParticleCount);
                    if (!Check(maximum <= DB.WorldRules.environment.maxParticles, "Particle budget exceeded")) yield break;
                    yield return null;
                }
                float clock = environment.Clock;
                GameTime.Pause("environment review");
                for (int i = 0; i < 5; i++) yield return null;
                if (!Check(environment.Clock == clock, "Environment clock advanced while paused")) yield break;
                GameTime.Resume("environment review");
                Shot(id + "_live");
                File.AppendAllText("Logs/Environment/capture_checks.txt", id + ": " + frames + " GPU frames changed " + differences +
                    " pixels; source sprites/positions and path preserved; pause passed; peak particles=" + maximum + "; shared materials=" + environment.MaterialCount + "\n");
                Debug.Log("[EnvironmentReview] " + id + " PASS");
            }
            // Adversarial density: many visible emitters with long-lived smoke must still respect the global cap.
            {
                var w = WorldStreamer.I; var environment = w.Environment; var s = DB.WorldRules.environment;
                float life = s.smokeLife; s.smokeLife = 10;
                game.Player.PendingEvolutions = 0; game.State = GameState.Playing; GameTime.ClearPauses(); GameTime.Tick();
                float startClock = environment.Clock;
                var at = game.Player.Pos; var chunk = w.ChunkAt(at);
                for (int i = 0; i < 40; i++) chunk.AddDecor(Art.Prop("campfire"), at + new Vector2((i % 8 - 4) * .25f, (i / 8) * .2f), false, 0, 0, name: "stress emitter");
                Camera.main.Render(); // Batch mode has no continuously repainted Game view; establish visibility explicitly.
                int peak = 0;
                for (int i = 0; i < 600 && peak < s.maxParticles; i++)
                {
                    peak = Mathf.Max(peak, environment.ParticleCount);
                    if (!Check(peak <= s.maxParticles, "Stress case exceeded particle cap")) yield break;
                    yield return null;
                }
                s.smokeLife = life;
                if (!Check(peak >= s.maxParticles - 2, "Stress emitters did not exercise the cap: peak=" + peak + ", elapsed=" + (environment.Clock - startClock) + ", emitters=" + environment.EmitterCount + ", paused=" + GameTime.Paused)) yield break;
                var rates = DB.Themes.Select(t => t.environment.weatherRate).ToArray();
                foreach (var theme in DB.Themes) theme.environment.weatherRate = 0;
                foreach (var pool in w.GetComponentsInChildren<ParticleSystem>()) pool.Clear();
                CameraRig.I.Snap(at + new Vector2(1000, 1000));
                for (int i = 0; i < 30; i++) yield return null;
                bool culled = environment.ParticleCount == 0;
                for (int i = 0; i < rates.Length; i++) DB.Themes[i].environment.weatherRate = rates[i];
                if (!Check(culled, "Offscreen emitters kept producing particles")) yield break;
                File.AppendAllText("Logs/Environment/capture_checks.txt", "Stress: 40 extra emitters, peak=" + peak + "/" + s.maxParticles + "; offscreen emissions=0.\n");
            }
            File.WriteAllText("Logs/Environment/result.txt", "PASS: " + themes.Length + " themes animate through Game.unity (" + string.Join(", ", themes) + "), original sprite identity/base positions and place paths preserved; pause and native particle caps verified. Scratch saves only.");
            Finish(true);
        }

        Color32[] Shot(string name)
        {
            foreach (var sr in Game.I.Player.GetComponentsInChildren<SpriteRenderer>())
                if (sr.sprite != null && sr.sprite.name.StartsWith("rabbit_")) sr.enabled = true;
            var camera = Camera.main; float size = camera.orthographicSize; camera.orthographicSize = 225f / (2 * Art.PPU);
            var target = new RenderTexture(960, 600, 24); camera.targetTexture = target; camera.Render(); camera.targetTexture = null;
            RenderTexture.active = target;
            var image = new Texture2D(960, 600, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            RenderTexture.active = null; Destroy(image); target.Release(); Destroy(target); camera.orthographicSize = size;
            return pixels;
        }

        double FixedRender(bool motion)
        {
            var camera = Camera.main; var target = new RenderTexture(960, 600, 24);
            var image = new Texture2D(960, 600, TextureFormat.RGB24, false);
            var times = new List<double>(); var watch = new System.Diagnostics.Stopwatch();
            Shader.SetGlobalFloat("_EnvironmentEnabled", motion ? 1 : 0);
            camera.targetTexture = target;
            for (int sample = 0; sample < 24; sample++)
            {
                watch.Restart(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); image.Apply();
                if (sample >= 4) times.Add(watch.Elapsed.TotalMilliseconds);
            }
            camera.targetTexture = null; RenderTexture.active = null; Shader.SetGlobalFloat("_EnvironmentEnabled", 1);
            Destroy(image); target.Release(); Destroy(target); times.Sort(); return times[times.Count / 2];
        }
    }
}
#endif
