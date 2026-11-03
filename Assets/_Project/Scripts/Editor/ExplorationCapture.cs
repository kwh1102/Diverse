using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class ExplorationCapture
    {
        const string Key = "Diverse.ExplorationCapture";
        public static void RunBatch()
        {
            string path = Path.GetFullPath("Logs/Exploration/Scratch"); Directory.CreateDirectory(path);
            Directory.CreateDirectory(path + "/slots/normal_1");
            File.WriteAllText(path + "/settings.json", JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0, lastSlot = 1 }));
            File.WriteAllText(path + "/slots/normal_1/world.json", JsonUtility.ToJson(new WorldSave { seed = 12345 }));
            Directory.CreateDirectory("Logs/Exploration/Scenes"); File.WriteAllText("Logs/Exploration/interaction_checks.txt", "");
            File.WriteAllText("Logs/Exploration/terrain_checks.txt", "");
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".OK", false); SessionState.SetString(Key + ".Save", path);
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", path);
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
            if (state == PlayModeStateChange.EnteredPlayMode) new GameObject("Exploration capture (editor only)").AddComponent<ExplorationReviewRunner>();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Key, false); Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
            EditorApplication.playModeStateChanged -= Changed;
            EditorApplication.Exit(SessionState.GetBool(Key + ".OK", false) ? 0 : 1);
        }
    }
}
