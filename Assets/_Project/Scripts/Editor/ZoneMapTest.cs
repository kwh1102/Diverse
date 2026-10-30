using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Checks the five-zone layout without entering play mode, and draws it from above:
    /// Logs/ZoneShots/surface_<seed>.png (+ deep layer) — terrain colors, ★ zone bosses, ◆ border mid-bosses.
    /// Batch: Unity -batchmode -nographics -executeMethod Diverse.EditorTools.ZoneMapTest.RunBatch (exit code 0 = pass)
    /// </summary>
    public static class ZoneMapTest
    {
        [MenuItem("Diverse/오방 지도 그려 보기", priority = 90)]
        public static void RunMenu()
        {
            var fail = Run(out var report);
            Debug.Log(report);
            EditorUtility.DisplayDialog("오방 지도", fail ?? "통과 — Logs/ZoneShots 폴더에 지도가 저장되었습니다.", "확인");
        }

        public static void RunBatch()
        {
            var fail = Run(out var report);
            Debug.Log(report);
            EditorApplication.Exit(fail == null ? 0 : 1);
        }

        static string Run(out string report)
        {
            var sb = new StringBuilder("[ZoneMapTest]\n");
            string fail = null;
            void Fail(string why) { fail ??= why; sb.AppendLine("FAIL: " + why); }
            if (DB.Zones.Count < 8) Fail($"only {DB.Zones.Count} zone types registered in GameDatabase");
            Directory.CreateDirectory("Logs/ZoneShots");
            // Every zone type should come up about as often (5 of 8 per world)
            var seen = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < 400; i++)
            {
                var w = new WorldSave { seed = 1000 + i * 7919 };
                ZoneMap.EnsureZones(w);
                foreach (var z in w.zones) seen[z] = seen.TryGetValue(z, out var n) ? n + 1 : 1;
            }
            sb.AppendLine("zone picks over 400 worlds: " + string.Join(", ", seen.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
            if (seen.Count < 8 || seen.Values.Min() < 170) Fail("zone types are not picked evenly");
            foreach (int seed in new[] { 12345, 777 })
            {
                var w = new WorldSave { seed = seed };
                var map = ZoneMap.ForWorld(w);
                if (map == null) { Fail("no zone map"); break; }
                sb.AppendLine($"seed {seed}: rotation {map.rotation:0}°, zones {string.Join(", ", map.zones.Select(z => z.name))}");
                if (w.zones.Distinct().Count() != ZoneMap.Count) Fail($"seed {seed}: zones repeat ({string.Join(",", w.zones)})");
                if (ZoneMap.ForWorld(w).rotation != map.rotation) Fail("the layout is not stable for the same seed");
                for (int i = 0; i < ZoneMap.Count; i++)
                {
                    var zb = map.bosses.First(b => b.id == "zoneboss" + i);
                    var s = map.At(zb.center);
                    if (map.zones[s.a] != zb.zone) Fail($"zone boss {i} ({zb.zone.name}) stands in {map.zones[s.a].name}");
                    if (s.t > 0.3f) Fail($"zone boss {i} is too close to a border (t {s.t:0.00})");
                    var bb = map.bosses.First(b => b.id == "borderboss" + i);
                    var t = map.At(bb.center).t;
                    sb.AppendLine($"  {zb.name} @{zb.center.magnitude:0} (t {s.t:0.00}) · {bb.name} @{bb.center.magnitude:0} (t {t:0.00})");
                    if (t < 0.2f) Fail($"border boss {i} is not near its border (t {t:0.00})");
                }
                // Mechanics grow with distance: nothing at the spawn, both traits far out
                var inner = DB.ScaledEnemy("fox", 0); map.Apply(inner, map.bosses[0].center.normalized * 50, 0);
                var outer = DB.ScaledEnemy("fox", 6); map.Apply(outer, map.bosses[0].center.normalized * 380, 6);
                sb.AppendLine($"  near spawn: {Describe(inner.mods)} | far out: {Describe(outer.mods)} (speed {outer.speed:0.0}, cd {outer.attackCooldown:0.00})");
                Draw(new WorldGen(seed, map), map, $"Logs/ZoneShots/surface_{seed}.png");
                var deepSave = new WorldSave { seed = seed, zones = w.zones, layer = 1, deepZone = w.zones[0] };
                var deep = ZoneMap.ForWorld(deepSave);
                if (deep == null || !deep.deep || deep.zones.Any(z => z != map.zones[0])) Fail("deep layer is not its zone everywhere");
                else Draw(new WorldGen(seed, deep), deep, $"Logs/ZoneShots/deep_{seed}.png");
            }
            report = sb.ToString();
            return fail;
        }

        static string Describe(ZoneMods m)
        {
            if (m == null) return "-";
            var parts = new System.Collections.Generic.List<string>();
            if (m.armor > 0) parts.Add($"armor {m.armor:0.000}");
            if (m.shieldHits > 0) parts.Add($"shield {m.shieldHits}");
            if (m.deflect > 0) parts.Add($"deflect {m.deflect:0.00}");
            if (m.thorns > 0) parts.Add($"thorns {m.thorns:0.00}");
            if (m.deathBurst > 0) parts.Add($"burst {m.deathBurst:0.00}");
            if (m.skittish > 0) parts.Add($"skittish {m.skittish:0.00}");
            if (m.miasma > 0) parts.Add($"miasma {m.miasma:0.00}");
            if (m.barrage > 0) parts.Add($"barrage {m.barrage:0.00}");
            if (m.brood > 0) parts.Add($"brood {m.brood}");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        /// <summary>Top-down picture: 1 pixel = 2 tiles, ±440 tiles around the spawn.</summary>
        static void Draw(WorldGen gen, ZoneMap map, string path)
        {
            const int Size = 440, Step = 2, N = Size * 2 / Step;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int tx = x * Step - Size, ty = y * Step - Size;
                    Art.GroundColors(gen.GroundAt(tx, ty), out var a, out _, out _);
                    px[y * N + x] = a;
                }
            void Dot(Vector2 p, Color32 c, int r)
            {
                int cx = Mathf.RoundToInt((p.x + Size) / Step), cy = Mathf.RoundToInt((p.y + Size) / Step);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= N || y >= N || dx * dx + dy * dy > r * r) continue;
                        px[y * N + x] = Mathf.Abs(dx) == r || Mathf.Abs(dy) == r ? new Color32(20, 16, 28, 255) : c;
                    }
            }
            foreach (var b in map.bosses) Dot(b.center, b.kind == StructKind.ZoneBoss ? b.zone.color : new Color32(255, 80, 90, 255), b.kind == StructKind.ZoneBoss ? 6 : 4);
            Dot(Vector2.zero, new Color32(255, 255, 255, 255), 5);
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
