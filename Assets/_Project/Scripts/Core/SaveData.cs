using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// "Things the world remembers." These persist even when a character dies.
    /// Things that belong to a single life (HP, abilities, level) live in RunState.
    /// </summary>
    [Serializable]
    public class WorldSave
    {
        public int version = 1;
        public int seed;
        public int lifeCount;                 // which life this is
        public int gold;                      // shared coffer (inherited through the town bank)
        public int memoryShards;              // tower-opening currency
        public int towerFloor;                // highest tower floor reached
        public int bossKills;                 // bosses defeated across all lives (opens farther exploration rings)
        public List<string> flags = new List<string>();          // per-structure state ("chest:12:-3:0")
        public List<string> clearedCamps = new List<string>();
        public List<string> discovered = new List<string>();     // discovered structure ids (shown on the map)
        public List<GraveRecord> graves = new List<GraveRecord>();
        public List<ChronicleEntry> chronicle = new List<ChronicleEntry>();
        public List<QuestState> quests = new List<QuestState>();
        public List<string> exploredChunks = new List<string>();
        public List<AbilityRecord> legacyAbilities = new List<AbilityRecord>(); // abilities recovered from graves
        public string lastCostume = "white";
        public int lastWeapon;

        public bool Flag(string key) => flags.Contains(key);
        public void SetFlag(string key) { if (!flags.Contains(key)) flags.Add(key); }

        public void Log(string text, string kind = "event")
        {
            chronicle.Add(new ChronicleEntry { life = lifeCount, text = text, kind = kind, time = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
            if (chronicle.Count > 200) chronicle.RemoveAt(0);
        }
    }

    /// <summary>
    /// The life currently in progress. Saved when returning to the title / quitting so the same life can be continued.
    /// Deleted when the hero dies.
    /// </summary>
    [Serializable]
    public class RunSave
    {
        public int life;
        public string heroName, costume;
        public int weapon;
        public float weaponDamage;
        public int level = 1;
        public float xp, xpToNext = 30;
        public int gold, kills, potions;
        public int[] attrs = new int[4];
        public int unspentAttr, pendingEvolutions;
        public float hp;
        public int dashMax = 2;
        public float x, y;
        public bool raft;
        public List<AbilityRecord> abilities = new List<AbilityRecord>();
        public List<string> intent = new List<string>();      // "TAG=count"
    }

    [Serializable]
    public class GraveRecord
    {
        public int life;
        public string heroName;
        public string costume;
        public int weapon;
        public int level;
        public float x, y;
        public string cause;
        public List<AbilityRecord> abilities = new List<AbilityRecord>();
        public bool recovered;
        public bool statue;           // a hero who earned a statue
        public int kills;
        public string epitaph;        // written by the storyteller
        public string forgottenBy;    // whether the storyteller recorded or forgot them
    }

    [Serializable]
    public class ChronicleEntry
    {
        public int life;
        public string text, kind, time;
    }

    [Serializable]
    public class QuestState
    {
        public string id;
        public string title, desc;
        public string kind;           // hunt / explore / deliver / rescue
        public string targetId;       // target structure id
        public float tx, ty;          // target coordinates
        public int need, have;
        public int status;            // 0 offered, 1 in progress, 2 done (ready to report), 3 complete
        public int rewardGold, rewardShards;
        public string enemy;
    }

    [Serializable]
    public class Settings
    {
        public float sfx = 0.6f, music = 0.35f;
        public bool screenShake = true;
        public float shakeScale = 1f;
        public bool damageNumbers = true;
        public List<string> bindings = new List<string>();   // "action=path"
        public string aiEndpoint = "https://api.openai.com/v1/chat/completions";
        public string aiModel = "gpt-4o-mini";
        public bool aiEnabled = true;
    }

    public static class SaveSystem
    {
        static string Dir => Application.persistentDataPath;
        static string WorldPath => Path.Combine(Dir, "world.json");
        static string SettingsPath => Path.Combine(Dir, "settings.json");
        static string RunPath => Path.Combine(Dir, "run.json");

        public static WorldSave LoadWorld()
        {
            try
            {
                if (File.Exists(WorldPath))
                {
                    var w = JsonUtility.FromJson<WorldSave>(File.ReadAllText(WorldPath));
                    if (w != null && w.seed != 0) return w;
                }
            }
            catch (Exception e) { Debug.LogWarning("World save load failed: " + e.Message); }
            return NewWorld();
        }

        public static WorldSave NewWorld()
        {
            var w = new WorldSave { seed = UnityEngine.Random.Range(1000, int.MaxValue / 4) };
            w.Log("세계가 깨어났다. 멀리 기억의 탑이 희미하게 빛난다.", "world");
            return w;
        }

        public static void SaveWorld(WorldSave w)
        {
            try { File.WriteAllText(WorldPath, JsonUtility.ToJson(w, true)); }
            catch (Exception e) { Debug.LogWarning("World save failed: " + e.Message); }
        }

        public static void DeleteWorld()
        {
            try { if (File.Exists(WorldPath)) File.Delete(WorldPath); } catch { }
        }

        public static RunSave LoadRun()
        {
            try
            {
                if (File.Exists(RunPath)) return JsonUtility.FromJson<RunSave>(File.ReadAllText(RunPath));
            }
            catch (Exception e) { Debug.LogWarning("Run save load failed: " + e.Message); }
            return null;
        }

        public static void SaveRun(RunSave r)
        {
            try { File.WriteAllText(RunPath, JsonUtility.ToJson(r, true)); }
            catch (Exception e) { Debug.LogWarning("Run save failed: " + e.Message); }
        }

        public static void DeleteRun()
        {
            try { if (File.Exists(RunPath)) File.Delete(RunPath); } catch { }
        }

        public static Settings LoadSettings()
        {
            Settings s = null;
            try
            {
                if (File.Exists(SettingsPath)) s = JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath));
            }
            catch { }
            s ??= new Settings();
            if (!AiClient.IsPresetModel(s.aiModel)) s.aiModel = AiClient.Models[0].id;   // only preset models can be selected
            return s;
        }

        public static void SaveSettings(Settings s)
        {
            try { File.WriteAllText(SettingsPath, JsonUtility.ToJson(s, true)); } catch { }
        }

        /// <summary>
        /// API key lookup order: OPENAI_API_KEY env var → persistentDataPath/openai_key.txt → project root/openai_key.txt.
        /// The key is never put in the game save or the code.
        /// </summary>
        public static string LoadApiKey()
        {
            var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            foreach (var p in new[] { Path.Combine(Dir, "openai_key.txt"), Path.Combine(Directory.GetCurrentDirectory(), "openai_key.txt") })
            {
                try { if (File.Exists(p)) { var k = File.ReadAllText(p).Trim(); if (k.Length > 10) return k; } } catch { }
            }
            return null;
        }

        public static string KeyFileHint => Path.Combine(Dir, "openai_key.txt");
    }
}
