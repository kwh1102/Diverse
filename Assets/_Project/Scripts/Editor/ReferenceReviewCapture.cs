#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>Captures the first art slice through the actual Game scene, in a dedicated scratch save.</summary>
    public static class ReferenceReviewCapture
    {
        const string Key = "Diverse.ReferenceReview";
        public const string OutDir = "Logs/ReferenceReview";

        [MenuItem("Diverse/Art/Capture Reference Review")]
        public static void RunBatch()
        {
            string dir = Path.GetFullPath("Logs/ReferenceReviewSave");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "slots/normal_1"));
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0, lastMode = 0, lastSlot = 1 }));
            var world = new WorldSave { seed = 12345 };
            world.zoneThemes.AddRange(new[] { "meadow", "volcano", "overgrowth", "crystal", "glacier" });
            File.WriteAllText(Path.Combine(dir, "slots/normal_1/world.json"), JsonUtility.ToJson(world));
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/terrain_checks.txt", "");
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            SessionState.SetString(Key + ".Save", dir); SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(Key, false)) return;
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", SessionState.GetString(Key + ".Save", ""));
            EditorApplication.playModeStateChanged -= Changed;
            EditorApplication.playModeStateChanged += Changed;
        }

        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                new GameObject("Reference review capture (editor only)").AddComponent<ReferenceReviewRunner>();
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
