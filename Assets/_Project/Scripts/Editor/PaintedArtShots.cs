using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Screenshots of the painted world at the normal play zoom: the town, one spot per biome and a river bank.
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.PaintedArtShots.RunBatch → Logs/PaintedShots/*.png
    /// Uses a scratch save (fixed seed), never the player's.
    /// </summary>
    public static class PaintedArtShots
    {
        const string RunningKey = "Diverse.PaintedShots.Running";
        const string OutDir = "Logs/PaintedShots";
        const int W = 1600, H = 900, PxPerUnit = 64;      // CameraRig's default zoom (4 × 16)

        [MenuItem("Diverse/Art/Painted World Screenshots")]
        public static void RunMenu() => Begin();

        public static void RunBatch() => Begin();

        static void Begin()
        {
            var dir = Path.Combine(Path.GetTempPath(), "DiversePaintedShots");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            // Normal mode (the five zones), slot 1, fixed seed
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0, lastMode = (int)GameMode.Normal, lastSlot = 1 }));
            Directory.CreateDirectory(Path.Combine(dir, "slots", "normal_1"));
            File.WriteAllText(Path.Combine(dir, "slots", "normal_1", "world.json"), JsonUtility.ToJson(new WorldSave { seed = 12345 }));
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            SessionState.SetString("Diverse.PaintedShots.Dir", dir);
            SessionState.SetBool(RunningKey, true);
            Directory.CreateDirectory(OutDir);
            EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", SessionState.GetString("Diverse.PaintedShots.Dir", null));
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Shooter>() == null) new GameObject("Shots").AddComponent<Shooter>();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && Object.FindFirstObjectByType<Shooter>() == null) new GameObject("Shots").AddComponent<Shooter>();
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        }

        class Shooter : MonoBehaviour
        {
            IEnumerator Start()
            {
                for (int i = 0; i < 90; i++) yield return null;
                var g = Game.I;
                var gen = Diverse.World.I.Gen;
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;

                var spots = new List<(string, Vector2)> { ("town", new Vector2(0, 1)) };
                if (gen.Zones != null)
                {
                    // Each zone's landmark (standing in its arena) and one border site
                    foreach (var s in gen.Zones.sites)
                        if (s.kind == StructKind.Landmark) spots.Add(("landmark_" + s.theme.id, s.center + Vector2.up * 5));
                    var b = gen.Zones.sites.Find(s => s.kind == StructKind.Border);
                    if (b != null) spots.Add(("border", b.center + Vector2.down * 2));
                }
                // A cliff seen from the open ground south of it, then a river bank
                // A south edge (wall) of a cliff at least lvl high, 5 tiles wide, open ground below it
                bool SouthFace(Vector2 p, int lvl)
                {
                    int x = (int)p.x, y = (int)p.y;
                    for (int dx = -2; dx <= 2; dx++)
                        if (WorldGen.Level(gen.GroundAt(x + dx, y)) < lvl || WorldGen.Level(gen.GroundAt(x + dx, y - 1)) != 0 || WorldGen.Impassable(gen.GroundAt(x + dx, y - 3))) return false;
                    return true;
                }
                // Cliffs seen from the open ground south of them (a wide wall, then one with the upper tier)
                if (Find(p => { for (int k = 0; k < 3; k++) if (SouthFace(p - new Vector2(0, k), 1)) return true; return false; }, out var cliff, 3)) spots.Add(("cliff", cliff + Vector2.down * 3));
                if (Find(p => { for (int k = 0; k < 3; k++) if (SouthFace(p - new Vector2(0, k), 2)) return true; return false; }, out var peak, 3)) spots.Add(("peak", peak + Vector2.down * 3));
                if (Find(p => WorldGen.IsWater(gen.GroundAt((int)p.x, (int)p.y)) && !WorldGen.Impassable(gen.GroundAt((int)p.x, (int)p.y + 1)), out var shore))
                    spots.Add(("shore", shore + new Vector2(0, 1)));

                var rig = CameraRig.I;
                var cam = rig.Cam;
                foreach (var (name, at) in spots)
                {
                    g.Player.Pos = at;
                    WorldStreamer.I.Warm(at);
                    rig.target = g.Player.transform;
                    rig.Snap(at);
                    for (int i = 0; i < 40; i++) yield return null;
                    rig.Snap(g.Player.Pos);
                    yield return null;
                    var rt = new RenderTexture(W, H, 24);
                    float old = cam.orthographicSize;
                    cam.orthographicSize = H / (2f * PxPerUnit);
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                    cam.orthographicSize = old;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    rt.Release();
                    File.WriteAllBytes($"{OutDir}/{name}.png", tex.EncodeToPNG());
                    Destroy(tex);
                    Debug.Log($"[PaintedShots] {name} at {at}");
                }
                EditorApplication.ExitPlaymode();
            }

            static bool Find(System.Func<Vector2, bool> ok, out Vector2 at, int step = 12)
            {
                for (int r = 30; r < 600; r += step)
                    for (int a = 0; a < 360; a += step < 12 ? 4 : 10)
                    {
                        var p = new Vector2(Mathf.Round(Mathf.Cos(a * Mathf.Deg2Rad) * r) + 0.5f, Mathf.Round(Mathf.Sin(a * Mathf.Deg2Rad) * r) + 0.5f);
                        if (ok(p)) { at = p; return true; }
                    }
                at = default;
                return false;
            }
        }
    }
}
