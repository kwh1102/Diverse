using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Diverse.EditorTools
{
    public static class ExplorationChecks
    {
        static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void RunBatchWorld()
        {
            try { Run(); WorldOverview.RunBatch(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void RunBatchPreviews()
        {
            try { Run(); WorldPictureCapture.RunBatchOverwrite(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void Run()
        {
            Directory.CreateDirectory("Logs/Exploration"); var report = new StringBuilder();
            Require(DB.WorldRules.exploration.Count == 18, "Expected 18 place definitions");
            foreach (string id in WorldArtOverhaul.ExplorationThemes)
                Require(DB.WorldRules.exploration.Count(d => d.theme == id) == 2, "Missing place variants for " + id);
            foreach (var def in DB.WorldRules.exploration) CheckLayout(def, report);
            for (int seed = 0; seed < 32; seed++)
            {
                var zones = ZoneMap.ForWorld(null, seed);
                Require(zones.combat.Distinct().Count() == 5 && zones.themes.Distinct().Count() == 5, "Repeated fresh-world zone");
                for (int y = -44; y <= 44; y += 4) for (int x = -44; x <= 44; x += 4)
                    if (x * x + y * y <= DB.WorldRules.centerRadius * DB.WorldRules.centerRadius)
                        Require(zones.ThemeAt(x, y) == DB.SpawnTheme, "Foreign theme leaked into the neutral spawn");
            }
            report.AppendLine("32 seeds: five distinct zones from eight candidates; unconditional neutral spawn.");
            CheckThemePatches(report);
            foreach (int seed in new[] { 12345, 777, 4242 })
            {
                var a = new WorldGen(seed, ZoneMap.ForWorld(null, seed));
                var b = new WorldGen(seed, ZoneMap.ForWorld(null, seed));
                int places = 0, early = 0;
                for (int y = -6; y <= 6; y++) for (int x = -6; x <= 6; x++)
                {
                    var s = a.Exploration.At(x, y); var reverse = b.Exploration.At(-x, -y);
                    if (s != null) { places++; if (s.center.magnitude < 170) early++; }
                }
                for (int y = 6; y >= -6; y--) for (int x = 6; x >= -6; x--)
                {
                    var s = a.Exploration.At(x, y); var other = b.Exploration.At(x, y);
                    Require((s == null) == (other == null), "Place existence changed with order");
                    if (s != null) Require(s.id == other.id && s.center == other.center && s.exploration == other.exploration, "Place layout changed with order");
                }
                Require(early >= 2 && places >= 15, "Too few exploration places: seed " + seed);
                var rng = new System.Random(seed);
                for (int i = 0; i < 2048; i++)
                {
                    int x = rng.Next(-350, 350), y = rng.Next(-350, 350);
                    Require(a.GroundAt(x, y) == b.GroundAt(x, y), "Ground differs after reversed place loading");
                }
                report.AppendLine($"seed {seed}: {places} large places in 13x13 cells, {early} inside first 170 tiles; 2,048 ground/order checks match.");
            }
            CheckStates(report);
            WorldVisualChecks.Run();
            File.WriteAllText("Logs/Exploration/checks.txt", report.ToString()); Debug.Log("[ExplorationChecks] PASS\n" + report);
        }

        static void CheckLayout(ExplorationSiteDef d, StringBuilder report)
        {
            var theme = DB.Theme(d.theme); int half = Mathf.CeilToInt(d.halfSize.magnitude) + 2;
            var solids = d.props.Where(p => p.blocks).Select(p => new Solid(Footprint.Ellipse,
                p.position + Vector2.up * p.footprint.y * .5f, p.footprint * .5f)).ToArray();
            Ground G(int x, int y) => Mathf.Abs(x + .5f) > d.halfSize.x || Mathf.Abs(y + .5f) > d.halfSize.y ? theme.ground :
                ExplorationLayout.Sample(d, theme, new Vector2(x + .5f, y + .5f));
            bool Walk(int x, int y)
            {
                if (WorldGen.Impassable(G(x, y))) return false;
                for (int dy = 1; dy <= 2; dy++) if (WorldGen.Level(G(x, y - dy)) >= dy) return false;
                var p = new Vector2(x + .5f, y + .5f);
                return !solids.Any(s => s.Contains(p, .3f));
            }
            var start = new Vector2Int(0, -Mathf.FloorToInt(d.halfSize.y) + 2);
            Require(Walk(start.x, start.y), d.id + " entrance blocked");
            var reach = new HashSet<Vector2Int> { start }; var queue = new Queue<Vector2Int>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var step in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                {
                    var next = p + step;
                    if (Mathf.Abs(next.x) > half || Mathf.Abs(next.y) > half || reach.Contains(next) || !Walk(next.x, next.y)) continue;
                    reach.Add(next); queue.Enqueue(next);
                }
            }
            foreach (var prop in d.props)
            {
                Require(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/Sprites/" + prop.sprite + ".png") != null, "Missing prop " + prop.sprite);
                if (prop.role == ExplorationRole.Decor) continue;
                Require(reach.Any(p => Vector2.Distance(new Vector2(p.x + .5f, p.y + .5f), prop.position) <= 1.3f), d.id + " unreachable " + prop.role + " " + prop.position);
            }
            report.AppendLine(d.id + ": entrance, record, loot/NPC/herb accessible around physical footprints and projected cliffs.");
        }

        static void CheckStates(StringBuilder report)
        {
            var closed = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/Sprites/prop_chest.png");
            var open = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/Sprites/prop_chest_open.png");
            int Width(Sprite s)
            {
                int left = s.texture.width, right = -1; var pixels = s.texture.GetPixels32();
                for (int y = 0; y < s.texture.height; y++) for (int x = 0; x < s.texture.width; x++)
                    if (pixels[y * s.texture.width + x].a > 0) { left = Mathf.Min(left, x); right = Mathf.Max(right, x); }
                return right - left + 1;
            }
            Require(Width(closed) == 22 && Width(open) == 22, "Chest changes width when opened");
            Require(Mathf.Abs(closed.pivot.y - open.pivot.y) < .001f, "Chest baseline changes");
            int blink = 0;
            for (int i = 0; i < 4800; i++) if (Player.IdlePose(i / 1000f) == Pose.Idle1) blink++;
            Require(blink <= 100 && blink >= 50, "Blink should occupy about 80ms of 4.8 seconds");
            report.AppendLine($"Chest: both visible widths 22px, same bottom pivot. Blink: {blink}/4800ms; open eyes for over 97% of idle time.");
        }

        static void CheckThemePatches(StringBuilder report)
        {
            const int side = 320, offset = 160;
            var zones = ZoneMap.ForWorld(null, 12345); var labels = new ThemeDef[side * side]; var seen = new bool[labels.Length];
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) labels[y * side + x] = zones.ThemeAt(x - offset, y - offset);
            var queue = new Queue<Vector2Int>(); int fragments = 0, components = 0;
            var directions = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
            {
                int index = y * side + x; if (seen[index]) continue;
                var label = labels[index]; int count = 0; bool clipped = false;
                queue.Enqueue(new Vector2Int(x, y)); seen[index] = true;
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue(); count++;
                    if (p.x == 0 || p.y == 0 || p.x == side - 1 || p.y == side - 1) clipped = true;
                    foreach (var dir in directions)
                    {
                        var next = p + dir;
                        if ((uint)next.x >= side || (uint)next.y >= side) continue;
                        int n = next.y * side + next.x;
                        if (seen[n] || labels[n] != label) continue;
                        seen[n] = true; queue.Enqueue(next);
                    }
                }
                if (!clipped) { components++; if (count < 8) fragments++; }
            }
            Require(fragments == 0, "Tiny isolated theme fragments: " + fragments);
            report.AppendLine($"320x320 theme tiles: {components} internal components, zero isolated fragments smaller than 8 tiles.");
        }
    }
}
