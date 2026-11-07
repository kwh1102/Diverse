using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class CombatUiReview
    {
        public const string Key="Diverse.CombatUiV2.Review";
        public static string Output=>SessionState.GetString(Key+".Output","Logs/CombatUiV2");
        public static void RunBatch()=>Begin(false);
        public static void RunFocus()=>Begin(true);
        public static void RunEffects()=>Begin(false, true);
        static void Begin(bool focus, bool effects = false)
        {
            string output=Path.GetFullPath("Logs/CombatUiV2/"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string scratch=output+"/Scratch";Directory.CreateDirectory(scratch+"/slots/normal_1");Directory.CreateDirectory(scratch+"/designs/img");
            // Public inventory/art fixture only; credentials/settings and live saves are never changed.
            string live=Application.persistentDataPath;
            if (Directory.Exists(live + "/slots"))
            {
                var saves = Directory.GetFiles(live + "/slots", "run.json", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTime)
                    .Select(path => JsonUtility.FromJson<RunSave>(File.ReadAllText(path))).Where(r => r?.inventory?.Active != null);
                var run = effects ? saves.OrderByDescending(r => r.inventory.items.Any(i => i.kind == ItemKind.Skill &&
                    i.active?.steps?.Count == 1 && i.active.steps[0].delivery == "projectile" &&
                    !string.IsNullOrEmpty(i.active.effectSourceImage))).FirstOrDefault() : saves.FirstOrDefault();
                if (run != null)
                {
                    string fixture = JsonUtility.ToJson(run.inventory);
                    File.WriteAllText(scratch + "/inventory.json", fixture);
                    foreach(Match match in Regex.Matches(fixture,"\\\"([a-f0-9]{12})\\\""))
                    {string id=match.Groups[1].Value;string source=live+"/designs/img/"+id+".png";if(File.Exists(source))File.Copy(source,scratch+"/designs/img/"+id+".png",true);}
                }
            }
            File.WriteAllText(scratch+"/settings.json",JsonUtility.ToJson(new Settings{aiEnabled=false,sfx=0,music=0,lastSlot=1}));
            File.WriteAllText(scratch+"/slots/normal_1/world.json",JsonUtility.ToJson(new WorldSave{seed=12345}));
            SessionState.SetString(Key+".Output",output);SessionState.SetBool(Key+".OK",false);SessionState.SetBool(Key,true);
            SessionState.SetBool(Key+".Focus",focus);
            SessionState.SetBool(Key+".Effects",effects);
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR",scratch);
            if (!effects) UIBuilder.ApplyCombatUiV2();
            EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            EditorApplication.playModeStateChanged-=Changed;EditorApplication.playModeStateChanged+=Changed;
            EditorApplication.delayCall+=EditorApplication.EnterPlaymode;
        }
        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if(!SessionState.GetBool(Key,false))return;
            Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR",Output+"/Scratch");
            EditorApplication.playModeStateChanged-=Changed;EditorApplication.playModeStateChanged+=Changed;
        }
        static void Changed(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)new GameObject("Offline combat and UI review (editor only)").AddComponent<CombatUiReviewRunner>();
            if(state!=PlayModeStateChange.EnteredEditMode)return;
            SessionState.SetBool(Key,false);Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR",null);
            EditorApplication.playModeStateChanged-=Changed;EditorApplication.Exit(SessionState.GetBool(Key+".OK",false)?0:1);
        }
    }
}
