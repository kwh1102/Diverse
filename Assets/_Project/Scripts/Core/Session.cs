using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diverse
{
    /// <summary>
    /// State shared by the MainMenu and Game scenes: settings, the world save, the life in progress,
    /// and what the menu asked the game scene to do when it loads.
    /// </summary>
    public static class Session
    {
        public const string MenuScene = "MainMenu";
        public const string GameScene = "Game";

        public enum StartKind { None, NewLife, Continue }

        public static Settings Settings { get; private set; } = new Settings();
        public static WorldSave World { get; private set; }
        public static RunSave PendingRun;

        // Menu → game scene hand-off
        public static StartKind Start;
        public static CostumeDef StartCostume;
        public static WeaponKind StartWeapon;
        /// <summary>Game → menu: open the character select right away (after a death).</summary>
        public static bool OpenCharacterSelect;

        /// <summary>The open save slot's mode. Normal keeps the hand-made world and enemies (DesignRuntime enforces it).</summary>
        public static GameMode Mode => SaveSystem.Mode;
        public static int Slot => SaveSystem.Slot;
        /// <summary>A slot was picked on the slot screen in this play session (the menu opens on that screen until then).</summary>
        public static bool SlotChosen { get; private set; }

        // Enter Play Mode without a domain reload keeps statics alive between plays: start clean every time.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            World = null; PendingRun = null; SlotChosen = false;
            Start = StartKind.None; StartCostume = null; OpenCharacterSelect = false;
        }

        /// <summary>
        /// Load settings, controls and the last used save slot once per play session (whichever scene comes first).
        /// The Game scene opened straight from the editor plays that slot; the menu asks for a slot first.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (World != null) return;
            Settings = SaveSystem.LoadSettings();
            Sfx.Volume = Settings.sfx;
            Sfx.MusicVolume = Settings.music;
            Controls.Init(Settings);
            SaveSystem.MigrateLegacySaves();
            LoadSlot((GameMode)Mathf.Clamp(Settings.lastMode, 0, 1), Mathf.Clamp(Settings.lastSlot, 1, SaveSystem.SlotCount));
        }

        static void LoadSlot(GameMode mode, int slot)
        {
            SaveSystem.Mode = mode;
            SaveSystem.Slot = slot;
            World = SaveSystem.LoadWorld();
            PendingRun = SaveSystem.LoadRun();
            if (PendingRun != null && PendingRun.life != World.lifeCount) { PendingRun = null; SaveSystem.DeleteRun(); }
        }

        /// <summary>Slot screen: play this slot (an empty slot starts a new world there, saved right away).</summary>
        public static void OpenSlot(GameMode mode, int slot)
        {
            bool empty = SaveSystem.Peek(mode, slot) == null;
            LoadSlot(mode, slot);
            if (empty) SaveSystem.SaveWorld(World);
            Settings.lastMode = (int)mode;
            Settings.lastSlot = slot;
            SaveSystem.SaveSettings(Settings);
            SlotChosen = true;
            DesignRuntime.Activate(null);   // nothing of another slot's look stays on screen
        }

        public static bool HasRunToContinue => PendingRun != null && World != null && PendingRun.life == World.lifeCount;

        public static void NewLife(CostumeDef costume, WeaponKind weapon)
        {
            Start = StartKind.NewLife; StartCostume = costume; StartWeapon = weapon;
            SceneManager.LoadScene(GameScene);
        }

        public static void ContinueLife()
        {
            Start = StartKind.Continue;
            SceneManager.LoadScene(GameScene);
        }

        public static void ToMenu(bool characterSelect)
        {
            OpenCharacterSelect = characterSelect;
            SceneManager.LoadScene(MenuScene);
        }

        /// <summary>Give up the saved life from the title: it ends like a death (grave + chronicle) and the next life can begin.</summary>
        public static void AbandonRun()
        {
            var r = PendingRun;
            if (r == null) return;
            var grave = new GraveRecord
            {
                life = r.life, heroName = r.heroName, costume = r.costume, weapon = r.weapon, level = r.level, kills = r.kills,
                x = r.x, y = r.y, cause = "긴 방랑",
            };
            foreach (var rec in r.abilities.Select(a => (rec: a, g: a.ToAbility())).Where(t => t.g != null && t.g.tier >= 1).OrderByDescending(t => t.g.power).Take(2))
                grave.abilities.Add(rec.rec);
            grave.epitaph = $"{r.heroName} — 길 위에서 사라지다. 레벨 {r.level}, 처치한 몬스터 {r.kills}마리.";
            World.graves.Add(grave);
            World.gold += r.gold / 2;
            World.Log($"{Ko.I(r.heroName)} 방랑 끝에 자취를 감췄다. 그 자리에 무덤이 세워졌다.", "death");
            PendingRun = null;
            SaveSystem.DeleteRun();
            SaveSystem.SaveWorld(World);
        }

#if UNITY_EDITOR
        /// <summary>Editor tools: build world objects in edit mode against a throwaway save (null restores normal loading).</summary>
        public static void UseWorldForBake(WorldSave w) => World = w;
#endif

        public static void ResetWorld()
        {
            SaveSystem.DeleteWorld();
            SaveSystem.DeleteRun();
            PendingRun = null;
            World = SaveSystem.NewWorld();
            SaveSystem.SaveWorld(World);
        }
    }
}
