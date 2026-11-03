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
            var regions = SessionState.GetBool("Diverse.AllTerrainCapture.SitesOnly", false) ? System.Array.Empty<string>() :
                new[] { "overgrowth", "swamp", "glacier", "canyon", "crystal", "fortress", "mirror", "meadow", "volcano" };
            foreach (string id in regions)
            {
                game.World.zoneThemes.Clear(); game.World.zoneThemes.AddRange(Enumerable.Repeat(id, ZoneMap.Count));
                World.I.ResetForRun(); World.I.Init(game.World.seed);
                Fx.I.ClearAll();
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
            // Real river crossings explicitly exercise shallow water, banks, land transitions and wooden decks.
            if (!SessionState.GetBool("Diverse.AllTerrainCapture.SitesOnly", false))
                foreach (string id in new[] { "meadow", "canyon" })
                {
                    game.World.zoneThemes.Clear(); game.World.zoneThemes.AddRange(Enumerable.Repeat(id, ZoneMap.Count));
                    World.I.ResetForRun(); World.I.Init(game.World.seed);
                    var world = WorldStreamer.I; var gen = world.Gen;
                    int best = int.MinValue; Vector2 at = Vector2.zero;
                    for (int y = -210; y <= 210; y += 3) for (int x = -210; x <= 210; x += 3)
                    {
                        if (x * x + y * y < 90 * 90 || gen.GroundAt(x, y) != Ground.Wood) continue;
                        int water = 0, land = 0, raised = 0, deck = 0;
                        for (int dy = -6; dy <= 6; dy += 2) for (int dx = -10; dx <= 10; dx += 2)
                        {
                            var m = gen.GroundAt(x + dx, y + dy);
                            if (WorldGen.IsWater(m)) water++;
                            else if (m == Ground.Wood) deck++;
                            else if (WorldGen.Level(m) > 0) raised++;
                            else land++;
                        }
                        if (water < 12 || land < 8) continue;
                        int score = Mathf.Min(water, 35) * 3 + Mathf.Min(land, 25) * 2 + deck - raised * 3;
                        if (score > best) { best = score; at = new Vector2(x, y); }
                    }
                    if (best == int.MinValue) { Debug.LogError("No bridge review location for " + id); yield break; }
                    world.Warm(at); game.Player.Pos = world.NearestWalkable(at, game.Player.radius, 8, false);
                    CameraRig.I.target = null; CameraRig.I.Snap(at);
                    for (int i = 0; i < 10; i++) yield return null;
                    ReferenceReviewRunner.Shot(id + "_water_bridge", false, Dir);
                    ReferenceReviewRunner.Shot(id + "_water_bridge_hud", true, Dir);
                    if (!ReferenceReviewRunner.CheckChunkSeams(at, id + " bridge", Dir)) { Finish(false); yield break; }
                }
            // Capture the authored town and the actual generated boss sites, in addition to open terrain.
            foreach (string id in new[] { "meadow", "overgrowth", "swamp", "glacier", "canyon", "crystal", "fortress", "mirror", "volcano" })
            {
                game.World.zoneThemes.Clear(); game.World.zoneThemes.AddRange(Enumerable.Repeat(id, ZoneMap.Count));
                World.I.ResetForRun(); World.I.Init(game.World.seed);
                var world = WorldStreamer.I;
                var site = world.Gen.Zones.sites.First(s => s.kind == StructKind.Landmark);
                Vector2 at = id == "meadow" ? Vector2.zero : site.center + Vector2.up * 3;
                world.Warm(at);
                game.Player.Pos = world.NearestWalkable(id == "meadow" ? new Vector2(0, -2) : site.center + new Vector2(-3, -2), game.Player.radius, 8, false);
                CameraRig.I.target = null; CameraRig.I.Snap(at);
                for (int i = 0; i < 8; i++) yield return null;
                string label = id == "meadow" ? "town" : id + "_landmark";
                ReferenceReviewRunner.Shot(label, false, Dir, 360);
                ReferenceReviewRunner.Shot(label + "_hud", true, Dir, 360);
            }
            File.WriteAllText(Dir + "/result.txt", "PASS: terrain regions, real river/wooden-bridge crossings, town and actual generated landmarks rendered through Game.unity; captured terrain/bridge full-region vs separate-chunk checks passed. Scratch saves only.");
            Debug.Log("[AllTerrain] PASS"); Finish(errors == 0);
        }
    }
}
#endif
