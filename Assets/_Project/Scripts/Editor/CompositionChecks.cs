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
    public static class CompositionChecks
    {
        static readonly string[] Themes = { "overgrowth", "volcano", "crystal", "glacier", "canyon", "fortress", "mirror", "swamp" };

        static void Require(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }

        [MenuItem("Diverse/World/Check Regional Composition")]
        public static void Run()
        {
            Directory.CreateDirectory("Logs/Composition"); var report = new StringBuilder();
            Require(!DB.SpawnTheme.scenery.enabled, "Town/meadow must retain its composition");
            var go = new GameObject("Composition checks (temporary)");
            try
            {
                var world = go.AddComponent<WorldStreamer>(); world.EditorRegister();
                foreach (string id in Themes)
                {
                    var theme = DB.Theme(id);
                    Require(theme.scenery.enabled && theme.scenery.group.Count >= 4, id + " missing composition/group");
                    foreach (var entry in theme.decor.Where(d => !d.flat))
                    {
                        Require(entry.role != SceneryRole.Legacy && entry.visualScale > 0, id + " unclassified " + entry.sprite);
                        Require(entry.role != SceneryRole.Supply || entry.chance == 0, id + " scattered supplies");
                        if (entry.rareScale > 0)
                        {
                            Require(entry.visualScale < entry.mediumScale && entry.mediumScale < entry.rareScale &&
                                entry.spacingScale >= entry.rareScale, id + " invalid size/spacing hierarchy");
                            int giants = 0;
                            for (int sample = 0; sample < 1000; sample++)
                            {
                                float roll = (sample + .5f) / 1000;
                                if (entry.IndividualScale(.5f, roll, 1) == entry.rareScale) giants++;
                                Require(entry.IndividualScale(.5f, roll, 0) == entry.visualScale, id + " large individual outside a cluster");
                            }
                            Require(giants > 0 && giants <= 50, id + " giants must be rare");
                        }
                    }
                    Require(theme.SceneryScale("house_0_c2603e") >= 1.4f && theme.scenery.landmarkScale > 1, id + " missing architecture scale");
                    var save = new WorldSave { seed = 12345 }; save.zoneThemes.AddRange(Enumerable.Repeat(id, ZoneMap.Count));
                    world.EditorInit(new WorldGen(save.seed, ZoneMap.ForWorld(save, save.seed)));
                    var accepted = new List<SceneryLayout.Placement>();
                    for (int y = 35; y < 63; y++) for (int x = -75; x < -47; x++)
                    {
                        var p = world.Scenery.At(x, y);
                        if (p != null && world.Scenery.Accepted(x, y, p)) accepted.Add(p);
                    }
                    for (int i = 0; i < accepted.Count; i++) for (int j = 0; j < i; j++)
                        Require(!SceneryLayout.Conflicts(accepted[i], accepted[j]), id + " spacing conflict");
                    var reverse = new SceneryLayout(world); int reversedCount = 0;
                    for (int y = 62; y >= 35; y--) for (int x = -48; x >= -75; x--)
                    {
                        var p = reverse.At(x, y);
                        if (p == null || !reverse.Accepted(x, y, p)) continue;
                        Require(accepted.Any(a => a.position == p.position && a.sprite == p.sprite && a.scale == p.scale), id + " reverse-order mismatch");
                        Require(world.Gen.Terrain.RoadClearance(p.position) >= DB.WorldRules.sceneryRoadMargin || p.level > 0, id + " road blocked");
                        reversedCount++;
                    }
                    Require(accepted.Count == reversedCount, id + " reverse-order count");
                    Require(accepted.Count > 0, id + " no scenery");
                    int groups = 0;
                    for (int y = -6; y <= 6; y++) for (int x = -6; x <= 6; x++)
                    {
                        var group = world.Scenery.GroupAt(x, y); var other = reverse.GroupAt(x, y);
                        Require((group == null) == (other == null), id + " group order");
                        if (group == null) continue;
                        groups++;
                        Require(group.members.Count == theme.scenery.group.Count, id + " partial group");
                        for (int i = 0; i < group.members.Count; i++)
                        {
                            var p = group.members[i]; Require(p.position == other.members[i].position, id + " group position");
                            Require(world.Scenery.ReservedByGroup(p.position), id + " group not reserved");
                            Require(!WorldGen.Impassable(world.Gen.GroundAt(Mathf.FloorToInt(p.position.x), Mathf.FloorToInt(p.position.y))), id + " group on impassable terrain");
                        }
                    }
                    Require(groups > 0, id + " no small places");
                    report.AppendLine($"{id}: {theme.decor.Count} classified entries; {accepted.Count} accepted proposals, reverse-order identical; {groups} complete reserved groups; buildings/anchors/landmarks enlarged.");
                }
                for (int seed = 0; seed < 16; seed++) CheckRoads(seed, report);
                WorldVisualChecks.Run();
                File.WriteAllText("Logs/Composition/checks.txt", report.ToString());
                Debug.Log("[CompositionChecks] PASS\n" + report);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void CheckRoads(int seed, StringBuilder report)
        {
            var world = new WorldGen(seed, ZoneMap.ForWorld(null, seed)); var t = world.Terrain;
            Require(t.ExitCount == DB.WorldRules.roadExits, "Unexpected town exits");
            var reachable = new HashSet<int>();
            for (int i = 0; i < t.ExitCount; i++) reachable.Add(i * 2);
            bool changed;
            do
            {
                changed = false;
                foreach (var edge in t.Connections)
                    if (reachable.Contains(edge.x) || reachable.Contains(edge.y)) { changed |= reachable.Add(edge.x); changed |= reachable.Add(edge.y); }
            } while (changed);
            Require(reachable.Count == t.Junctions.Count, "Disconnected road graph: " + seed);
            for (int i = 0; i < t.ExitCount; i++) Require(t.Connections.Count(e => e.x == i * 2 || e.y == i * 2) == 1, "Town gate has extra spokes");
            foreach (var site in world.Zones.sites)
                Require(t.Junctions.Contains(site.center) && t.OnRoad(site.center), "Unconnected destination " + site.id);
            int approaches = 0;
            foreach (var site in world.Exploration.Near(Vector2.zero, 220))
            {
                var path = world.Exploration.Trail(site); var def = world.Exploration.Definition(site);
                Require(path.Length >= 2 && path.Length <= 5, "Invalid place approach");
                Require(path[0] == site.center + Vector2.down * (def.halfSize.y - .5f), "Approach misses entrance");
                for (int i = 1; i < path.Length; i++)
                    Require(world.Exploration.TrailAt(Vector2.Lerp(path[i - 1], path[i], .5f), out _), "Approach segment missing");
                approaches++;
            }
            Require(world.Zones.themes.Distinct().Count() == 5, "Zone selection changed");
            report.AppendLine($"seed {seed}: {t.ExitCount} town exits, {t.Connections.Count} shared road links; all 10 destinations connected; {approaches} single-entrance approaches; five distinct zones retained.");
        }
    }
}
