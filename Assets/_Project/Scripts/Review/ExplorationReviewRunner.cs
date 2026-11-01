#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public class ExplorationReviewRunner : MonoBehaviour
    {
        const string Output = "Logs/Exploration/Scenes";
        bool failed;
        void OnEnable() => Application.logMessageReceived += Log;
        void OnDisable() => Application.logMessageReceived -= Log;
        void Log(string text, string trace, LogType type)
        {
            if (failed || type is not (LogType.Exception or LogType.Error or LogType.Assert)) return;
            failed = true; File.WriteAllText("Logs/Exploration/error.txt", text + "\n" + trace); Finish(false);
        }
        static void Finish(bool ok) { SessionState.SetBool("Diverse.ExplorationCapture.OK", ok); EditorApplication.ExitPlaymode(); }
        bool Check(bool condition, string message)
        {
            if (condition) return true;
            Debug.LogError("[ExplorationReview] " + message); return false;
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 90; i++) yield return null;
            var game = Game.I;
            if (!Check(game != null, "No game")) yield break;
            game.Player.Level = 27; game.World.bossKills = 10; game.Player.invulnUntil = float.MaxValue;
            foreach (var def in DB.WorldRules.exploration)
            {
                game.World.zoneThemes.Clear(); game.World.zoneThemes.AddRange(Enumerable.Repeat(def.theme, ZoneMap.Count));
                game.World.flags.Clear(); game.World.discovered.Clear();
                World.I.ResetForRun(); World.I.Init(game.World.seed);
                var world = WorldStreamer.I; StructureSpec site = null;
                for (int y = -5; y <= 5; y++) for (int x = -5; x <= 5; x++)
                {
                    var s = world.Gen.Exploration.At(x, y);
                    if (s != null && s.exploration == def.id && s.center.magnitude > 85 &&
                        (site == null || s.center.magnitude < site.center.magnitude)) site = s;
                }
                if (!Check(site != null, "No actual site for " + def.id)) yield break;
                world.Warm(site.center);
                var entrance = world.NearestWalkable(site.center + new Vector2(0, -9), game.Player.radius, 4, false);
                game.Player.StopMoving(); game.Player.Pos = entrance;
                CameraRig.I.target = null; CameraRig.I.Snap(site.center);
                for (int i = 0; i < 12; i++) yield return null;
                var interactions = Interactable.All.Where(i => i != null && i.id.StartsWith(site.id + ":")).ToArray();
                foreach (var it in interactions)
                {
                    var destination = world.NearestWalkable(it.Pos, game.Player.radius, 2, false);
                    var path = PathFinder.Find(world, entrance, destination, 10000);
                    if (!Check(path != null && path.Count > 0 && Vector2.Distance(path[path.Count - 1], it.Pos) <= 1.5f,
                        def.id + " has unreachable actual interaction " + it.kind + " " + it.Pos)) yield break;
                }
                World.I.UpdateDiscovery(entrance);
                if (!Check(game.World.discovered.Contains(site.id), "Place not discovered")) yield break;
                ReferenceReviewRunner.Shot(def.id, false, Output, 360);
                ReferenceReviewRunner.Shot(def.id + "_hud", true, Output, 360);
                if (!ReferenceReviewRunner.CheckChunkSeams(site.center, def.id, "Logs/Exploration")) yield break;
                var record = interactions.First(i => i.kind == "exploration_record");
                Dialogue.Open(game, record); Dialogue.Current.options.First().act();
                if (!Check(game.World.Flag("investigate:" + site.id), "Investigation flag missing")) yield break;
                int chronicle = game.World.chronicle.Count;
                Dialogue.Open(game, record);
                if (!Check(Dialogue.Current.options.Count == 1 && game.World.chronicle.Count == chronicle, "Investigation repeats reward")) yield break;
                game.CloseOverlay(); Dialogue.Current = null;
                var chest = interactions.First(i => i.kind == "chest");
                game.Player.Pos = world.NearestWalkable(chest.Pos + Vector2.down, game.Player.radius, 3);
                CameraRig.I.Snap(chest.Pos + Vector2.up);
                if (def.id == "meadow_Hamlet") ReferenceReviewRunner.Shot("chest_closed", false, Output, 144);
                Dialogue.Open(game, chest);
                if (!Check(game.World.Flag("chest:" + chest.id) && chest.sr.sprite.name == "prop_chest_open", "Open chest did not persist")) yield break;
                if (def.id == "meadow_Hamlet") ReferenceReviewRunner.Shot("chest_open", false, Output, 144);
                string chestId = chest.id;
                var copy = JsonUtility.FromJson<WorldSave>(JsonUtility.ToJson(game.World));
                if (!Check(copy.Flag("investigate:" + site.id) && copy.Flag("chest:" + chestId), "World memory roundtrip lost state")) yield break;
                World.I.ResetForRun(); World.I.Init(game.World.seed); WorldStreamer.I.Warm(site.center);
                var restored = Interactable.All.FirstOrDefault(i => i != null && i.id == chestId);
                if (!Check(restored != null && restored.sr.sprite.name == "prop_chest_open", "Reloaded chest closed again")) yield break;
                File.AppendAllText("Logs/Exploration/interaction_checks.txt", def.id + ": actual paths, discovery, one-time investigation, chest open, world-memory roundtrip and chunk reload PASS\n");
                Debug.Log("[ExplorationReview] PASS " + def.id);
                for (int i = 0; i < 3; i++) yield return null;
            }
            // A normal five-theme world as well as the controlled per-theme fixtures above.
            game.World.zoneThemes.Clear(); World.I.ResetForRun(); World.I.Init(game.World.seed);
            var normal = WorldStreamer.I;
            if (!Check(normal.Gen.Zones.themes.Distinct().Count() == 5, "Normal world does not contain five unique themes")) yield break;
            foreach (var at in new[] { new Vector2(-11, 24), new Vector2(28, 83) })
            {
                normal.Warm(at); game.Player.Pos = normal.NearestWalkable(at, game.Player.radius, 8, false);
                CameraRig.I.Snap(at); for (int i = 0; i < 8; i++) yield return null;
                string label = at.y == 24 ? "normal_spawn_field" : "normal_border_field";
                ReferenceReviewRunner.Shot(label, false, Output);
                if (at.y == 24 && !Check(normal.Gen.ThemeAt(at.x, at.y) == DB.SpawnTheme, "Foreign tile at spawn repro point")) yield break;
            }
            File.WriteAllText("Logs/Exploration/result.txt", "PASS: all 18 generated exploration places, 18 chunk seam checks, normal five-theme world field captures, paths and persistent interactions tested through Game.unity using scratch saves.");
            Finish(true);
        }
    }
}
#endif
