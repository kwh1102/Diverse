using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class SkillUiReview
    {
        public const string Key = "Diverse.SkillUiReview";
        public static string Output => SessionState.GetString(Key + ".Output", "Logs/SkillUiReview");
        public static void RunBatch()
        {
            string output = Path.GetFullPath("Logs/SkillUiReview/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string scratch = output + "/Scratch";
            Directory.CreateDirectory(scratch + "/slots/normal_1"); Directory.CreateDirectory(scratch + "/designs/img");
            // Read only public skill art from the latest live run. Never copy settings, credentials or write live saves.
            string live = Application.persistentDataPath;
            var runs = Directory.Exists(live + "/slots") ? Directory.GetFiles(live + "/slots", "run.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime) : Enumerable.Empty<string>();
            ActiveSkillData fixture = null;
            foreach (string path in runs)
            {
                var run = JsonUtility.FromJson<RunSave>(File.ReadAllText(path));
                fixture = run?.inventory?.items?.Select(i => i.active).FirstOrDefault(a => a != null && a.weaponType == WeaponType.Sword && a.steps?.Count > 0);
                if (fixture != null) break;
            }
            if (fixture != null)
            {
                var ids = (fixture.effectImages ?? Array.Empty<string>()).Concat(new[] { fixture.iconImage, fixture.effectSourceImage });
                foreach (string id in ids.Where(id => id != null && Regex.IsMatch(id, "^[a-f0-9]{12}$")).Distinct())
                {
                    string from = Path.Combine(live, "designs/img", id + ".png");
                    if (File.Exists(from)) File.Copy(from, scratch + "/designs/img/" + id + ".png", true);
                }
                File.WriteAllText(scratch + "/fixture.json", JsonUtility.ToJson(fixture));
            }
            File.WriteAllText(scratch + "/settings.json", JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0, lastSlot = 1 }));
            File.WriteAllText(scratch + "/slots/normal_1/world.json", JsonUtility.ToJson(new WorldSave { seed = 12345 }));
            SessionState.SetString(Key + ".Output", output); SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".OK", false);
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", scratch);
            SkillPartsPrefabAuthoring.Install();
            EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            EditorApplication.playModeStateChanged += Changed; EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }
        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(Key, false)) return;
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", Output + "/Scratch");
            EditorApplication.playModeStateChanged -= Changed; EditorApplication.playModeStateChanged += Changed;
        }
        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) new GameObject("Skill UI review (editor only)").AddComponent<SkillUiReviewRunner>();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Key, false); Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
            EditorApplication.playModeStateChanged -= Changed;
            EditorApplication.Exit(SessionState.GetBool(Key + ".OK", false) ? 0 : 1);
        }
    }
}
