using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Review aid for the zone art: a normal-mode world (scratch save), the hero dropped inside every zone type and on a
    /// border, the zone's monsters and master lined up around them → Logs/ZoneShots/zone_*.png (1280x720, game camera).
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.ZoneScreenshots.RunBatch
    /// </summary>
    public static class ZoneScreenshots
    {
        const string RunningKey = "Diverse.ZoneShots.Running";
        const string BatchKey = "Diverse.ZoneShots.Batch";
        const string DirKey = "Diverse.ZoneShots.Dir";
        const int W = 1280, H = 720;
        static string OutDir => Path.GetFullPath("Logs/ZoneShots");

        [MenuItem("Diverse/구역 스크린샷 찍기 (Logs/ZoneShots)", priority = 93)]
        public static void RunMenu() => Begin(false);
        public static void RunBatch() => Begin(true);

        static void Begin(bool batch)
        {
            var dir = Path.Combine(Path.GetTempPath(), "DiverseZoneShots");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(Path.Combine(dir, "slots", "normal_1"));
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0, lastMode = (int)GameMode.Normal, lastSlot = 1 }));
            File.WriteAllText(Path.Combine(dir, "slots", "normal_1", "world.json"), JsonUtility.ToJson(new WorldSave { seed = 12345 }));
            Directory.CreateDirectory(OutDir);
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", SessionState.GetString(DirKey, null));
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Shooter>() == null) new GameObject("ZoneShots").AddComponent<Shooter>();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                if (Object.FindFirstObjectByType<Shooter>() == null) new GameObject("ZoneShots").AddComponent<Shooter>();
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
                Debug.Log("[Diverse] 구역 스크린샷: " + OutDir);
                if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(0);
            }
        }

        class Shooter : MonoBehaviour
        {
            void Awake() => DontDestroyOnLoad(gameObject);

            static void Shot(string name)
            {
                var cam = Camera.main;
                var rt = new RenderTexture(W, H, 24);
                var old = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = old;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                rt.Release();
            }

            static IEnumerator F(int n) { for (int i = 0; i < n; i++) yield return null; }

            IEnumerator Start()
            {
                while (Game.I == null || Game.I.Player == null) yield return null;
                yield return F(10);
                var g = Game.I;
                // First the world's own five zones, then the three it didn't pick (set by hand)
                yield return ShootWorld(g);
                var rest = System.Enum.GetValues(typeof(BuildStyle)).Cast<BuildStyle>().Select(x => x.ToString()).Except(g.World.zones).ToList();
                g.World.zones = rest.Concat(g.World.zones).Take(ZoneMap.Count).ToList();
                Diverse.World.I.ResetForRun();
                Diverse.World.I.Init(g.World.seed);
                yield return ShootWorld(g);
                EditorApplication.ExitPlaymode();
            }

            IEnumerator ShootWorld(Game g)
            {
                var map = Diverse.World.I.Gen.Zones;
                var p = g.Player;
                for (int i = 0; i < ZoneMap.Count; i++)
                {
                    var z = map.zones[i];
                    var at = MathX.Dir(map.rotation + i * 72 + 36) * 125;
                    yield return Visit(g, at);
                    // The zone's monsters and master around the hero, standing still for the picture
                    var spawned = z.enemies.Append(z.boss).Select((id, k) =>
                    {
                        var d = DB.ScaledEnemy(id, 1);
                        d.aggroRange = 0.01f;
                        var spot = p.Pos + (k < 3 ? new Vector2(-4.5f + k * 3f, -2.6f) : new Vector2(5.5f, 0.5f));
                        return Enemy.Spawn(d, WorldStreamer.I.NearestWalkable(spot, d.radius), null);
                    }).ToList();
                    yield return F(30);
                    p.invulnUntil = 0; yield return F(2);   // no invulnerability blink in the picture
                    Shot($"zone_{z.style}");
                    foreach (var e in spawned) if (e != null) Destroy(e.gameObject);
                    // A border with its neighbour
                    yield return Visit(g, MathX.Dir(map.rotation + (i + 1) * 72) * 95);
                    yield return F(10);
                    Shot($"border_{z.style}_{map.zones[(i + 1) % ZoneMap.Count].style}");
                }
            }

            IEnumerator Visit(Game g, Vector2 at)
            {
                var p = g.Player;
                foreach (var a in Actor.All.ToArray()) if (a is Enemy e) Destroy(e.gameObject);
                p.invulnUntil = Time.time + 999;
                WorldStreamer.I.Warm(at);
                p.Pos = WorldStreamer.I.NearestWalkable(at, p.radius);
                CameraRig.I.Snap(p.Pos);
                yield return F(20);
                foreach (var a in Actor.All.ToArray()) if (a is Enemy e) Destroy(e.gameObject);
                yield return F(2);
            }
        }
    }
}
