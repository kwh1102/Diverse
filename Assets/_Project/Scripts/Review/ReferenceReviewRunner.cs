#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public class ReferenceReviewRunner : MonoBehaviour
    {
        const string ReviewDir = "Logs/ReferenceReview";
        static void Finish(bool ok)
        {
            SessionState.SetBool("Diverse.ReferenceReview.OK", ok);
            EditorApplication.ExitPlaymode();
        }
        IEnumerator Start()
        {
            for (int i = 0; i < 90; i++) yield return null;
            if (Game.I == null || WorldStreamer.I == null) { Finish(false); yield break; }
            var game = Game.I;
            var gen = WorldStreamer.I.Gen;
            var spots = new[] { ("meadow", Vector2.zero), ("volcano", Vector2.zero) };
            foreach (var entry in spots)
            {
                Vector2 at = Vector2.zero;
                // Choose actual generated terrain with a visible south cliff or liquid nearby.
                int best = -1;
                int bestCliff = -1; Vector2 cliffAt = Vector2.zero;
                for (int y = -180; y <= 180; y += 6) for (int x = -180; x <= 180; x += 6)
                {
                    if (gen.ThemeAt(x, y).id != entry.Item1 || WorldGen.Impassable(gen.GroundAt(x, y))) continue;
                    bool pure = true;
                    for (int py = -8; py <= 8; py += 8) for (int px = -12; px <= 12; px += 12)
                        if (gen.ThemeAt(x + px, y + py).id != entry.Item1) pure = false;
                    if (!pure) continue;
                    int score = 0;
                    int upperRock = 0, lowerRock = 0, molten = 0;
                    for (int dy = -3; dy <= 7; dy++) for (int dx = -9; dx <= 9; dx++)
                    {
                        var g = gen.GroundAt(x + dx, y + dy);
                        if (WorldGen.Level(g) > 0) score += dy > 0 ? 4 : 1;
                        if (WorldGen.IsLiquid(g)) score += entry.Item1 == "volcano" ? 5 : -1;
                        if (WorldGen.Level(g) > 0) { if (dy > 0) upperRock++; else lowerRock++; }
                        if (g == Ground.Lava) molten++;
                    }
                    if (score > best && score < (entry.Item1 == "volcano" ? 600 : 420)) { best = score; at = new Vector2(x, y); }
                    int cliffScore = upperRock * 3 - lowerRock * 2 + molten * 2;
                    if (upperRock > 12 && upperRock < 130 && cliffScore > bestCliff)
                    { bestCliff = cliffScore; cliffAt = new Vector2(x, y); }
                }
                if (best < 0) { Debug.LogError("Review theme not found: " + entry.Item1); Finish(false); yield break; }
                game.Player.Pos = at; game.Player.invulnUntil = Time.time + 10000;
                WorldStreamer.I.Warm(at); CameraRig.I.Snap(at);
                for (int i = 0; i < 30; i++) yield return null;
                CameraRig.I.target = null; CameraRig.I.Snap(at);
                Shot(entry.Item1 + "_hud", true);
                Shot(entry.Item1 + "_world", false);
                if (entry.Item1 == "volcano")
                {
                    File.AppendAllText(ReviewDir + "/terrain_checks.txt", "Volcanic river regression (62,-140): " + gen.GroundAt(62, -140) + "\n");
                    if (gen.GroundAt(62, -140) != Ground.Lava)
                    { Debug.LogError("Water island remains in volcanic river"); Finish(false); yield break; }
                }
                if (!CheckChunkSeams(at, entry.Item1)) { Finish(false); yield break; }
                File.AppendAllText(ReviewDir + "/terrain_checks.txt", entry.Item1 + " camera: " + at + "\n");
                if (entry.Item1 == "volcano" && bestCliff >= 0)
                {
                    WorldStreamer.I.Warm(cliffAt);
                    game.Player.Pos = WorldStreamer.I.NearestWalkable(cliffAt, game.Player.radius, 8, false);
                    CameraRig.I.Snap(game.Player.Pos);
                    for (int i = 0; i < 8; i++) yield return null;
                    Shot("volcano_cliff", false);
                }
                if (entry.Item1 == "meadow")
                {
                    var tree = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                        .Where(r => r.sprite != null && r.sprite.name.StartsWith("tree_Meadow_") && r.sortingOrder > -30000)
                        .OrderBy(r => Vector2.Distance(r.transform.position, at)).FirstOrDefault();
                    if (tree != null)
                    {
                        var grove = WorldStreamer.I.NearestWalkable((Vector2)tree.transform.position + new Vector2(4, -3), game.Player.radius, 6, false);
                        game.Player.Pos = grove; WorldStreamer.I.Warm(grove); CameraRig.I.Snap(grove);
                        for (int i = 0; i < 8; i++) yield return null;
                        Shot("meadow_grove", true);
                    }
                }
            }
            game.OpenOverlay(GameState.Paused);
            for (int i = 0; i < 3; i++) yield return null;
            Shot("pause_ui", true);
            File.WriteAllText(ReviewDir + "/result.txt", "PASS: Game scene, meadow/volcano terrain, reference sprites, HUD and pause UI rendered using scratch saves.");
            Debug.Log("[ReferenceReview] PASS");
            Finish(true);
        }

        public static bool CheckChunkSeams(Vector2 at, string theme, string directory = ReviewDir)
        {
            int n = WorldGen.Chunk;
            var k = new Vector2Int(MathX.FloorDiv(Mathf.FloorToInt(at.x), n), MathX.FloorDiv(Mathf.FloorToInt(at.y), n));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var combined = TileBaker.BakeRegion(WorldStreamer.I, k * n, 2 * n);
            var expected = combined.GetPixels32(); int size = n * Art.PPU, mismatches = 0;
            // Bake in reverse order to detect both a border discontinuity and leaked scratch state from another region.
            for (int quadrant = 3; quadrant >= 0; quadrant--)
            {
                var offset = new Vector2Int(quadrant % 2, quadrant / 2);
                var chunk = TileBaker.Bake(WorldStreamer.I, k + offset); var actual = chunk.GetPixels32();
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    if (!actual[y * size + x].Equals(expected[(y + offset.y * size) * size * 2 + x + offset.x * size])) mismatches++;
                Destroy(chunk);
            }
            Destroy(combined);
            string result = theme + ": " + mismatches + " chunk/region pixel mismatches; " + watch.ElapsedMilliseconds + "ms for combined region + 4 chunks";
            File.AppendAllText(directory + "/terrain_checks.txt", result + "\n");
            if (mismatches > 0) { Debug.LogError(result); return false; }
            Debug.Log("[TerrainCheck] " + result);
            return true;
        }

        public static void Shot(string name, bool ui, string directory = ReviewDir)
        {
            const int w = 1440, h = 900;
            var cam = Camera.main;
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.enabled).ToArray();
            var modes = canvases.Select(c => c.renderMode).ToArray();
            var orders = canvases.Select(c => c.sortingOrder).ToArray();
            var scalers = FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None);
            foreach (var s in scalers) s.enabled = false;
            for (int i = 0; i < canvases.Length; i++)
            {
                var c = canvases[i]; c.enabled = ui;
                c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1;
                c.sortingOrder = 32767; c.scaleFactor = Mathf.Min(w / 640f, h / 360f);
            }
            Canvas.ForceUpdateCanvases();
            // The scratch player's permanent invulnerability blinks its body; capture the visible phase.
            foreach (var sr in Game.I.Player.GetComponentsInChildren<SpriteRenderer>())
                if (sr.sprite != null && sr.sprite.name.StartsWith("rabbit_")) sr.enabled = true;
            var rt = new RenderTexture(w, h, 24);
            float old = cam.orthographicSize;
            cam.orthographicSize = h / (2f * Art.PPU * 4); cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; cam.orthographicSize = old;
            RenderTexture.active = rt;
            var image = new Texture2D(w, h, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, w, h), 0, 0); image.Apply();
            File.WriteAllBytes(directory + "/" + name + ".png", image.EncodeToPNG());
            RenderTexture.active = null; Destroy(image); rt.Release(); Destroy(rt);
            for (int i = 0; i < canvases.Length; i++) { canvases[i].enabled = true; canvases[i].renderMode = modes[i]; canvases[i].sortingOrder = orders[i]; }
            foreach (var s in scalers) s.enabled = true;
        }
    }
}

#endif
