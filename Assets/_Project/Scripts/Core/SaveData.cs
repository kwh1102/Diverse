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
        public int bossKills;                 // bosses defeated across all lives (opens farther exploration rings)
        public List<string> zones = new List<string>();        // normal mode: the 5 zones' build types (CombatType names), counter-clockwise; chosen once per world
        public List<string> zoneThemes = new List<string>();   // optional theme id per zone (empty = the build type's default theme)
        public List<string> flags = new List<string>();          // per-structure state ("chest:12:-3:0")
        public List<string> clearedCamps = new List<string>();
        public List<string> discovered = new List<string>();     // discovered structure ids (shown on the map)
        public List<GraveRecord> graves = new List<GraveRecord>();
        public List<ChronicleEntry> chronicle = new List<ChronicleEntry>();
        public List<QuestState> quests = new List<QuestState>();
        public List<string> exploredChunks = new List<string>();
        public List<AbilityRecord> legacyAbilities = new List<AbilityRecord>(); // abilities recovered from graves
        public List<WarpStone> warpStones = new List<WarpStone>();                // placed warp stones (shared by every life)
        public string lastCostume = "white";
        public int lastWeapon;
        public string designId;               // this world's look (designs/<id>.json); empty = the built-in art
        public int designCalls;               // AI design generations in this world (shown in the studio)
        public float designCostUsd;           // their estimated API cost

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
        public float xp, xpToNext = 60;
        public int gold, kills;
        public int[] attrs = new int[4];
        public int unspentAttr, pendingEvolutions;
        public float hp;
        public int dashMax = 2;
        public float x, y;
        public int warpStones;                                // warp stones carried (not yet placed)
        public int skillsLearned = -1;                        // QWER slots learned, in order (-1 = old save: all 4)
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

    /// <summary>A warp stone set up in the world. Travel between any two from the map while standing near one.</summary>
    [Serializable]
    public class WarpStone
    {
        public string id;
        public string name;
        public float x, y;
        public Vector2 Pos => new Vector2(x, y);
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
        public int rewardGold;
        public string enemy;
    }

    /// <summary>Normal: the hand-made world and enemies, only the hero and weapon are designed. Custom: everything can be designed.</summary>
    public enum GameMode { Normal, Custom }

    /// <summary>A save slot as the slot screen sees it.</summary>
    public class SlotInfo
    {
        public WorldSave world;
        public bool hasRun;
        public DateTime lastPlayed;
    }

    [Serializable]
    public class Settings
    {
        public float sfx = 0.6f, music = 0.35f;
        public bool screenShake = true;
        public float shakeScale = 1f;
        public bool damageNumbers = true;
        // Graphics (설정 → 그래픽, GraphicsQuality.Apply)
        public int fpsCap;                  // 0 = follow the monitor (VSync), else a frame cap without VSync (30 / 60 / 120)
        public int effects = 2;             // 0 low · 1 medium · 2 high: particles, afterimages, ambient specks
        public int postFx = 2;              // 0 off · 1 light (no bloom) · 2 full
        public bool showFps;
        public List<string> bindings = new List<string>();   // "action=path"
        public string aiEndpoint = "https://api.openai.com/v1/chat/completions";
        public string aiModel = "gpt-6-luna";
        public string imageModel = "gpt-image-2.5-flare";   // design studio pictures (Images API)
        public string imageQuality = "low";                 // low | medium: pictures are shrunk to a few dozen pixels, low is usually enough
        public int modelsVersion;                           // settings migrations (2 = gpt-6-luna / gpt-image-2.5)
        public int lastMode = (int)GameMode.Custom, lastSlot = 1;   // the save slot opened last (the Game scene opened from the editor uses it)
        public bool aiEnabled = true;
    }

    public static class SaveSystem
    {
        /// <summary>Tests set DIVERSE_SAVE_DIR to a scratch folder so they never touch the player's saves.</summary>
        static string Dir => System.Environment.GetEnvironmentVariable("DIVERSE_SAVE_DIR") is { Length: > 0 } d ? d : Application.persistentDataPath;
        /// <summary>Design packs (DesignStore). Kept when a world is reset so graves of older worlds' heroes still draw.</summary>
        public static string DesignDir => Path.Combine(Dir, "designs");
        static string SettingsPath => Path.Combine(Dir, "settings.json");

        // ───────── Save slots: each mode keeps up to SlotCount worlds (slots/normal_1 … custom_3) ─────────
        public const int SlotCount = 3;
        /// <summary>The slot that LoadWorld / SaveRun… read and write (Session.OpenSlot sets it).</summary>
        public static GameMode Mode = GameMode.Custom;
        public static int Slot = 1;

        static string SlotDir(GameMode m, int slot) => Path.Combine(Dir, "slots", (m == GameMode.Normal ? "normal_" : "custom_") + slot);
        static string WorldPath => Path.Combine(SlotDir(Mode, Slot), "world.json");
        static string RunPath => Path.Combine(SlotDir(Mode, Slot), "run.json");
        static void EnsureSlotDir() { try { Directory.CreateDirectory(SlotDir(Mode, Slot)); } catch { } }

        /// <summary>What the slot screen shows about a slot (null = empty).</summary>
        public static SlotInfo Peek(GameMode m, int slot)
        {
            var path = Path.Combine(SlotDir(m, slot), "world.json");
            try
            {
                if (!File.Exists(path)) return null;
                var w = JsonUtility.FromJson<WorldSave>(File.ReadAllText(path));
                if (w == null || w.seed == 0) return null;
                return new SlotInfo
                {
                    world = w, hasRun = File.Exists(Path.Combine(SlotDir(m, slot), "run.json")),
                    lastPlayed = File.GetLastWriteTime(path),
                };
            }
            catch { return null; }
        }

        public static void DeleteSlot(GameMode m, int slot)
        {
            try { var d = SlotDir(m, slot); if (Directory.Exists(d)) Directory.Delete(d, true); }
            catch (Exception e) { Debug.LogWarning("Slot delete failed: " + e.Message); }
        }

        /// <summary>Saves from before slots (world.json / run.json in the save root) become custom slot 1 — they could already use designs.</summary>
        public static void MigrateLegacySaves()
        {
            try
            {
                var oldWorld = Path.Combine(Dir, "world.json");
                if (!File.Exists(oldWorld)) return;
                var target = SlotDir(GameMode.Custom, 1);
                if (File.Exists(Path.Combine(target, "world.json"))) return;
                Directory.CreateDirectory(target);
                File.Move(oldWorld, Path.Combine(target, "world.json"));
                var oldRun = Path.Combine(Dir, "run.json");
                if (File.Exists(oldRun)) File.Move(oldRun, Path.Combine(target, "run.json"));
                Debug.Log("[Save] moved the old world into custom slot 1");
            }
            catch (Exception e) { Debug.LogWarning("Save migration failed: " + e.Message); }
        }

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
            w.Log("세계가 깨어났다.", "world");
            return w;
        }

        public static void SaveWorld(WorldSave w)
        {
            EnsureSlotDir();
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
            EnsureSlotDir();
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
            // One-time move off the old defaults (gpt-image-1-mini is being retired; gpt-6-luna is cheaper and smarter)
            if (s.modelsVersion < 2)
            {
                if (s.aiModel == "gpt-4o-mini") s.aiModel = "gpt-6-luna";
                if (string.IsNullOrEmpty(s.imageModel) || s.imageModel == "gpt-image-1-mini") s.imageModel = "gpt-image-2.5-flare";
                s.modelsVersion = 2;
            }
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
