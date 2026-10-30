using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Review aid for the large landforms: a normal-mode world (scratch save, seed 12345), the hero dropped at the foot of
    /// a mountain range, in a canyon, on a river bridge and a ford, in a ruined keep and an abandoned village, plus the
    /// world map → Logs/TerrainShots/*.png (1280x720, game camera).
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.TerrainScreenshots.RunBatch
    /// </summary>
    public static class TerrainScreenshots
    {
        const string RunningKey = "Diverse.TerrainShots.Running";
        const string BatchKey = "Diverse.TerrainShots.Batch";
        const string DirKey = "Diverse.TerrainShots.Dir";
        const int W = 1280, H = 720;
        static string OutDir => Path.GetFullPath("Logs/TerrainShots");

        [MenuItem("Diverse/지형 스크린샷 찍기 (Logs/TerrainShots)", priority = 94)]
        public static void RunMenu() => Begin(false);
        public static void RunBatch() => Begin(true);

        static void Begin(bool batch)
        {
            var dir = Path.Combine(Path.GetTempPath(), "DiverseTerrainShots");
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
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Shooter>() == null) new GameObject("TerrainShots").AddComponent<Shooter>();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                if (Object.FindFirstObjectByType<Shooter>() == null) new GameObject("TerrainShots").AddComponent<Shooter>();
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
                Debug.Log("[Diverse] 지형 스크린샷: " + OutDir);
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
                // A 3x point-scaled crop of the middle, to judge the tile art pixel by pixel
                int cw = 320, ch = 180, cx0 = W / 2 - cw / 2, cy0 = H / 2 - ch / 2;
                var zoom = new Texture2D(cw * 3, ch * 3, TextureFormat.RGB24, false);
                var src = tex.GetPixels32();
                var dst = new Color32[cw * 3 * ch * 3];
                for (int y = 0; y < ch * 3; y++)
                    for (int x = 0; x < cw * 3; x++)
                        dst[y * cw * 3 + x] = src[(cy0 + y / 3) * W + cx0 + x / 3];
                zoom.SetPixels32(dst);
                zoom.Apply();
                File.WriteAllBytes(Path.Combine(OutDir, name + "_zoom.png"), zoom.EncodeToPNG());
                Destroy(zoom);
                Destroy(tex);
                rt.Release();
            }

            static IEnumerator F(int n) { for (int i = 0; i < n; i++) yield return null; }

            /// <summary>The nearest tile to the spawn whose ground passes the test, searched in rings (null if none within 360).</summary>
            static Vector2? Find(WorldGen gen, System.Func<int, int, bool> test)
            {
                for (int r = 20; r < 360; r += 4)
                    for (int i = 0; i < 48; i++)
                    {
                        var p = MathX.Dir(i * 7.5f) * r;
                        int tx = Mathf.FloorToInt(p.x), ty = Mathf.FloorToInt(p.y);
                        if (test(tx, ty)) return new Vector2(tx + 0.5f, ty + 0.5f);
                    }
                return null;
            }

            IEnumerator Start()
            {
                while (Game.I == null || Game.I.Player == null) yield return null;
                yield return F(10);
                var g = Game.I;
                var gen = Diverse.World.I.Gen;
                g.Player.invulnUntil = Time.time + 9999;
                g.Player.Level = 40;    // every frontier ring open

                // A range seen from the south (the cliff faces show), and a canyon through it
                var rock = Find(gen, (x, y) => gen.GroundAt(x, y) == Ground.Rock && !WorldGen.IsSolid(gen.GroundAt(x, y - 1)) && !WorldGen.IsSolid(gen.GroundAt(x, y - 4)));
                if (rock.HasValue) { yield return Visit(g, rock.Value + new Vector2(0, -2.2f)); Shot("01_mountain_foot"); }
                var canyon = Find(gen, (x, y) => gen.FeatureAt(x, y) == Feature.Canyon && WorldGen.IsSolid(gen.GroundAt(x - 3, y)) && WorldGen.IsSolid(gen.GroundAt(x + 3, y)));
                if (canyon.HasValue) { yield return Visit(g, canyon.Value); Shot("02_canyon"); }
                // A bridge and a ford
                var bridge = Find(gen, (x, y) => gen.GroundAt(x, y) == Ground.Wood);
                if (bridge.HasValue) { yield return Visit(g, bridge.Value); Shot("03_bridge"); }
                var ford = Find(gen, (x, y) => gen.GroundAt(x, y) == Ground.Ford);
                if (ford.HasValue) { yield return Visit(g, ford.Value); Shot("04_ford"); }
                var wet = Find(gen, (x, y) => gen.FeatureAt(x, y) == Feature.Wetland);
                if (wet.HasValue) { yield return Visit(g, wet.Value); Shot("05_wetland"); }
                // The keep: the south gate from outside, then the yard
                var keep = gen.FindNearest(Vector2.zero, StructKind.Keep, 0, 700);
                if (keep != null)
                {
                    yield return Visit(g, keep.center + new Vector2(0, -keep.site.hh - 2.5f)); Shot("06_keep_gate");
                    yield return Visit(g, keep.center + new Vector2(0, 2)); Shot("07_keep_yard");
                }
                var village = gen.FindNearest(Vector2.zero, StructKind.Village, 0, 700);
                if (village != null) { yield return Visit(g, village.center + new Vector2(0, -2)); Shot("08_village"); }
                var forest = Find(gen, (x, y) => gen.ForestAt(x, y, gen.BiomeAt(x + 0.5f, y + 0.5f)) && gen.GroundAt(x, y) == Ground.DarkGrass);
                if (forest.HasValue) { yield return Visit(g, forest.Value); Shot("09_forest"); }
                // The world map with everything near the spawn discovered
                foreach (var s in gen.StructuresNear(Vector2.zero, 400)) if (s.landmark) Diverse.World.I.Discover(s.id);
                yield return Visit(g, keep != null ? keep.center : Vector2.zero);
                g.OpenOverlay(GameState.Map);
                yield return F(4);
                var map = Object.FindFirstObjectByType<MapView>();
                if (map != null) { map.SendMessage("Repaint", SendMessageOptions.DontRequireReceiver); }
                yield return F(4);
                ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, "10_map.png"));
                yield return F(4);
                g.CloseOverlay();
                yield return F(2);
                EditorApplication.ExitPlaymode();
            }

            IEnumerator Visit(Game g, Vector2 at)
            {
                var p = g.Player;
                foreach (var a in Actor.All.ToArray()) if (a is Enemy e) Destroy(e.gameObject);
                p.invulnUntil = Time.time + 9999;
                WorldStreamer.I.Warm(at);
                p.Pos = WorldStreamer.I.NearestWalkable(at, p.radius, 10);
                CameraRig.I.Snap(p.Pos);
                yield return F(25);
                foreach (var a in Actor.All.ToArray()) if (a is Enemy e) Destroy(e.gameObject);
                yield return F(2);
            }
        }
    }
}
