#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public class AllTerrainRunner : MonoBehaviour
    {
        const string Dir = "Logs/AllTerrain";
        int errors;
        void OnEnable() => Application.logMessageReceived += Log;
        void OnDisable() => Application.logMessageReceived -= Log;
        void Log(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            File.AppendAllText(Dir + "/errors.txt", message + "\n" + stack + "\n");
            if (errors++ == 0) Finish(false);
        }
        static void Finish(bool ok)
        {
            SessionState.SetBool("Diverse.AllTerrainCapture.OK", ok); EditorApplication.ExitPlaymode();
        }
        IEnumerator Start()
        {
            for (int i = 0; i < 90; i++) yield return null;
            var game = Game.I;
            if (game == null) { Finish(false); yield break; }
            foreach (string id in new[] { "overgrowth", "swamp", "glacier", "canyon", "crystal", "fortress", "mirror", "meadow", "volcano" })
            {
                game.World.zoneThemes.Clear(); game.World.zoneThemes.AddRange(Enumerable.Repeat(id, ZoneMap.Count));
                World.I.ResetForRun(); World.I.Init(game.World.seed);
                for (int i = 0; i < 2; i++) yield return null;
                var world = WorldStreamer.I; var gen = world.Gen;
                int best = int.MinValue; Vector2 at = Vector2.zero;
                var expected = DB.Themes.First(t => t.id == id).ground;
                for (int y = -200; y <= 200; y += 8) for (int x = -200; x <= 200; x += 8)
                {
                    if (x * x + y * y < 90 * 90 || gen.ThemeAt(x, y).id != id || WorldGen.Impassable(gen.GroundAt(x, y))) continue;
                    int top = 0, foreground = 0, material = 0, liquid = 0;
                    for (int dy = -4; dy <= 7; dy++) for (int dx = -10; dx <= 10; dx++)
                    {
                        var g = gen.GroundAt(x + dx, y + dy);
                        if (WorldGen.Level(g) > 0) { if (dy > 1) top++; else foreground++; }
                        if (g == expected) material++;
                        if (WorldGen.IsLiquid(g) || g == Ground.Ice) liquid++;
                    }
                    if (top < 12 || top > 120 || foreground > 25) continue;
                    int score = top * 3 + material + Mathf.Min(liquid, 40) - foreground * 4;
                    if (score > best) { best = score; at = new Vector2(x, y); }
                }
                if (best == int.MinValue) { Debug.LogError("No review terrain for " + id); yield break; }
                game.Player.invulnUntil = Time.time + 10000;
                world.Warm(at); game.Player.Pos = world.NearestWalkable(at, game.Player.radius, 8, false);
                CameraRig.I.target = null; CameraRig.I.Snap(at);
                for (int i = 0; i < 15; i++) yield return null;
                ReferenceReviewRunner.Shot(id, false, Dir);
                ReferenceReviewRunner.Shot(id + "_hud", true, Dir);
                if (!ReferenceReviewRunner.CheckChunkSeams(at, id, Dir)) { Finish(false); yield break; }
                File.AppendAllText(Dir + "/terrain_checks.txt", id + " camera=" + at + " ground=" + expected + "\n");
                Debug.Log("[AllTerrain] Captured and verified " + id);
            }
            File.WriteAllText(Dir + "/result.txt", "PASS: all 9 base regions rendered through Game.unity; 9 full-region vs separate-chunk checks passed. Scratch saves only.");
            Debug.Log("[AllTerrain] PASS"); Finish(errors == 0);
        }
    }
}
#endif
