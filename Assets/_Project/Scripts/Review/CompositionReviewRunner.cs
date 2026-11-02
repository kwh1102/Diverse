#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public class CompositionReviewRunner : MonoBehaviour
    {
        const string Output = "Logs/Composition/Scenes";
        bool failed;
        void OnEnable() => Application.logMessageReceived += Log;
        void OnDisable() => Application.logMessageReceived -= Log;
        void Log(string text, string trace, LogType type)
        {
            if (failed || type is not (LogType.Exception or LogType.Error or LogType.Assert)) return;
            failed = true; File.WriteAllText("Logs/Composition/capture_error.txt", text + "\n" + trace); Finish(false);
        }
        static void Finish(bool ok) { SessionState.SetBool("Diverse.CompositionCapture.OK", ok); EditorApplication.ExitPlaymode(); }

        IEnumerator Start()
        {
            for (int i = 0; i < 12; i++) yield return null;
            var game = Game.I;
            game.Player.Level = 27; game.World.bossKills = 10; game.Player.invulnUntil = float.MaxValue;
            File.WriteAllText("Logs/Composition/capture_checks.txt", "");
            string selected = SessionState.GetString("Diverse.CompositionCapture.Themes", "");
            var themes = string.IsNullOrEmpty(selected) ? new[] { "overgrowth", "volcano", "crystal", "glacier", "canyon", "fortress", "mirror", "swamp" } : selected.Split(',');
            foreach (string id in themes)
            {
                game.World.zoneThemes.Clear(); game.World.zoneThemes.AddRange(Enumerable.Repeat(id, ZoneMap.Count));
                World.I.ResetForRun(); World.I.Init(game.World.seed);
                var w = WorldStreamer.I; var theme = DB.Theme(id);
                Vector2 at = Vector2.zero; float best = float.MinValue;
                for (int y = -200; y <= 200; y += 8) for (int x = -200; x <= 200; x += 8)
                {
                    var p = new Vector2(x, y);
                    if (p.magnitude < 85 || WorldGen.Impassable(w.Gen.GroundAt(x, y))) continue;
                    float patch = SceneryHabitat.Sample(p, theme, w.Gen.seed);
                    float road = w.Gen.Terrain.RoadClearance(p);
                    if (patch < .25f || patch > .8f || road < 3 || w.Scenery.ReservedByGroup(p)) continue;
                    float contrast = Mathf.Abs(SceneryHabitat.Sample(p + Vector2.right * 9, theme, w.Gen.seed) -
                        SceneryHabitat.Sample(p - Vector2.right * 9, theme, w.Gen.seed));
                    float score = contrast * 4 + patch - Mathf.Abs(road - 5) * .04f;
                    foreach (var site in w.Gen.StructuresNear(p, 12)) score -= 20;
                    if (score > best) { best = score; at = p; }
                }
                if (best == float.MinValue) { Debug.LogError("No field capture for " + id); yield break; }
                w.Warm(at); game.Player.Pos = w.NearestWalkable(at, game.Player.radius, 8, false);
                CameraRig.I.target = null; CameraRig.I.Snap(at);
                for (int i = 0; i < 8; i++) yield return null;
                var roadGoal = w.NearestWalkable(w.Gen.Terrain.NearestRoadPoint(game.Player.Pos), game.Player.radius, 6, false);
                var fieldPath = PathFinder.Find(w, game.Player.Pos, roadGoal);
                if (fieldPath.Count == 0 || Vector2.Distance(fieldPath[fieldPath.Count - 1], roadGoal) > 1)
                { Debug.LogError(id + " field cannot reach its road around scenery footprints"); yield break; }
                ReferenceReviewRunner.Shot(id + "_field", true, Output);
                if (!ReferenceReviewRunner.CheckChunkSeams(at, id, "Logs/Composition")) { Finish(false); yield break; }
                if (id == "overgrowth")
                {
                    var blocker = FindObjectsByType<SceneryOcclusion>(FindObjectsSortMode.None).FirstOrDefault(o =>
                        Vector2.Distance(o.transform.position, at) < 9 && w.Walkable((Vector2)o.transform.position + Vector2.up * 1.5f));
                    if (blocker == null) { Debug.LogError("No canopy to verify occlusion"); yield break; }
                    game.Player.Pos = (Vector2)blocker.transform.position + Vector2.up * 1.5f;
                    for (int i = 0; i < 24; i++) yield return null;
                    var sr = blocker.GetComponent<SpriteRenderer>();
                    if (sr.color.a > .45f) { Debug.LogError("Canopy did not reveal the player"); yield break; }
                    ReferenceReviewRunner.Shot("overgrowth_occlusion", false, Output);
                    game.Player.Pos = w.NearestWalkable((Vector2)blocker.transform.position + Vector2.down * 2, game.Player.radius, 6, false);
                    for (int i = 0; i < 24; i++) yield return null;
                    if (sr.color.a < .95f) { Debug.LogError("Canopy did not restore opacity"); yield break; }
                    File.AppendAllText("Logs/Composition/capture_checks.txt", "Canopy occlusion: player revealed and opacity restored.\n");
                }
                SceneryLayout.Group group = null;
                for (int y = -5; y <= 5; y++) for (int x = -5; x <= 5; x++)
                {
                    var candidate = w.Scenery.GroupAt(x, y);
                    if (candidate != null && candidate.center.magnitude > 85 && (group == null || candidate.center.sqrMagnitude < group.center.sqrMagnitude)) group = candidate;
                }
                if (group == null) { Debug.LogError("No group capture for " + id); yield break; }
                w.Warm(group.center); game.Player.Pos = w.NearestWalkable(group.center + Vector2.down * 3, game.Player.radius, 8, false);
                CameraRig.I.Snap(group.center);
                for (int i = 0; i < 8; i++) yield return null;
                var path = PathFinder.Find(w, game.Player.Pos, group.center);
                if (path == null || path.Count == 0 || Vector2.Distance(path[path.Count - 1], group.center) > 1)
                { Debug.LogError(id + " group center inaccessible"); yield break; }
                ReferenceReviewRunner.Shot(id + "_group", false, Output);
                var landmark = w.Gen.Zones.sites.First(s => s.kind == StructKind.Landmark);
                w.Warm(landmark.center); game.Player.Pos = w.NearestWalkable(landmark.center + new Vector2(-4, -3), game.Player.radius, 8, false);
                CameraRig.I.Snap(landmark.center + Vector2.up * 5);
                for (int i = 0; i < 8; i++) yield return null;
                ReferenceReviewRunner.Shot(id + "_landmark", false, Output, 300);
                File.AppendAllText("Logs/Composition/capture_checks.txt", id + ": real field/group/landmark captured; chunk seams match; field-to-road path=" + fieldPath.Count + "; group center path=" + path.Count + "; field=" + at + "; group=" + group.center + "\n");
                Debug.Log("[CompositionCapture] " + id + " captured and verified");
            }
            File.WriteAllText("Logs/Composition/result.txt", "PASS: " + themes.Length + " regions captured through Game.unity (" + string.Join(", ", themes) + "), field seams match, group centers reachable. Scratch saves only.");
            Finish(true);
        }
    }
}
#endif
