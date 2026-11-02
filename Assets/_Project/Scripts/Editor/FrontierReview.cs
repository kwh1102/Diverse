#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class FrontierReview
    {
        const string Key = "Diverse.FrontierReview";
        public const string DirectoryPath = "Logs/FrontierReview";

        [MenuItem("Diverse/Checks/Residents, Frontier and Map Performance")]
        public static void RunBatch()
        {
            string save = Path.GetFullPath(DirectoryPath + "/ScratchSave");
            Directory.CreateDirectory(save + "/slots/normal_1");
            File.WriteAllText(save + "/settings.json", JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0, lastSlot = 1 }));
            var world = new WorldSave { seed = 12345 };
            world.zoneThemes.AddRange(new[] { "meadow", "volcano", "overgrowth", "crystal", "glacier" });
            File.WriteAllText(save + "/slots/normal_1/world.json", JsonUtility.ToJson(world));
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", save);
            SessionState.SetString(Key + ".Save", save); SessionState.SetBool(Key, true);
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
            if (state == PlayModeStateChange.EnteredPlayMode)
                new GameObject("Frontier review (editor only)").AddComponent<FrontierReviewRunner>();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Key, false); Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
            EditorApplication.playModeStateChanged -= Changed;
            if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Key + ".OK", false) ? 0 : 1);
        }

        public static void Finish(bool ok)
        {
            SessionState.SetBool(Key + ".OK", ok); EditorApplication.ExitPlaymode();
        }
    }
}
#endif
