using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class CompositionCapture
    {
        const string Key = "Diverse.CompositionCapture";
        public static void RunBatch() => StartCapture("");
        public static void RunBatchNaturalSizes() => StartCapture("canyon,crystal");
        static void StartCapture(string themes)
        {
            SessionState.SetString(Key + ".Themes", themes);
            string path = Path.GetFullPath("Logs/Composition/Scratch");
            Directory.CreateDirectory(path + "/slots/normal_1"); Directory.CreateDirectory("Logs/Composition/Scenes");
            File.WriteAllText(path + "/settings.json", JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0, lastSlot = 1 }));
            File.WriteAllText(path + "/slots/normal_1/world.json", JsonUtility.ToJson(new WorldSave { seed = 12345 }));
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
            if (state == PlayModeStateChange.EnteredPlayMode) new GameObject("Regional composition capture (editor only)").AddComponent<CompositionReviewRunner>();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Key, false); Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
            EditorApplication.playModeStateChanged -= Changed;
            EditorApplication.Exit(SessionState.GetBool(Key + ".OK", false) ? 0 : 1);
        }
    }
}
