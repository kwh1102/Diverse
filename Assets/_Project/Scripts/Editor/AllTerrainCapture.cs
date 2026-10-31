using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class AllTerrainCapture
    {
        const string Key = "Diverse.AllTerrainCapture";

        [MenuItem("Diverse/Art/Capture All Terrain Regions")]
        public static void RunBatch()
        {
            string dir = Path.GetFullPath("Logs/AllTerrainScratch");
            Directory.CreateDirectory(dir); Directory.CreateDirectory(Path.Combine(dir, "slots/normal_1"));
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0, lastMode = 0, lastSlot = 1 }));
            File.WriteAllText(Path.Combine(dir, "slots/normal_1/world.json"), JsonUtility.ToJson(new WorldSave { seed = 12345 }));
            Directory.CreateDirectory("Logs/AllTerrain"); File.WriteAllText("Logs/AllTerrain/terrain_checks.txt", "");
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".OK", false); SessionState.SetString(Key + ".Save", dir);
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(Key, false)) return;
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", SessionState.GetString(Key + ".Save", ""));
            EditorApplication.playModeStateChanged -= Changed; EditorApplication.playModeStateChanged += Changed;
        }

        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) new GameObject("Terrain region review (editor only)").AddComponent<AllTerrainRunner>();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Key, false); Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
            EditorApplication.playModeStateChanged -= Changed;
            if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Key + ".OK", false) ? 0 : 1);
        }
    }
}
