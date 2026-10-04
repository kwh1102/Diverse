using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public enum GameState { Title, CharacterSelect, Playing, Evolving, Dialogue, Map, Paused, Dead, Settings }

    /// <summary>
    /// The game's single entry point. Drop it in an empty scene and it builds everything in code.
    /// Flow: Title → Character select (costume + weapon) → Run (start in town) → Death → Grave/record → Next life
    /// </summary>
    public partial class Game : MonoBehaviour
    {
        public static Game I { get; private set; }
        public static Settings Settings { get; private set; } = new Settings();

        public GameState State = GameState.Title;
        public WorldSave World;
        public Player Player;
        public string HeroName;
        public bool UiCapturingMouse;
        public UI Ui;

        Actor lastDamageSource;
        float discoverT, saveT;
        System.Random rnd = new System.Random();
        public bool Generating;            // AI generating
        public List<AbilityGraph> Offer;   // current evolution candidates
        public string OfferStatus;
        public Enemy Boss;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindFirstObjectByType<Game>() == null) new GameObject("Game").AddComponent<Game>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            GameTime.Reset();
            GameEvents.ClearAll();
            Settings = SaveSystem.LoadSettings();
            Sfx.Volume = Settings.sfx;
            Sfx.MusicVolume = Settings.music;
            Controls.Init(Settings);

            // Clean up the template objects in the scene
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (cam.GetComponent<CameraRig>() == null) Destroy(cam.gameObject);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) Destroy(l.gameObject);

            CameraRig.Create();
            Fx.Create();
            Sfx.Create();
            World = SaveSystem.LoadWorld();
            Diverse.World.Create(World.seed);
            Ui = UI.Create(this);
            GameEvents.Toast += Ui.Toast;
            GameEvents.WorldAction += OnWorldAction;
            GameEvents.Combat += OnCombat;

            // Show the town as the title backdrop
            WorldStreamer.I.Warm(Vector2.zero);
            CameraRig.I.Snap(new Vector2(0, 2));
            Sfx.PlayMusic(true);
        }

        void OnDestroy()
        {
            if (I == this) { I = null; GameEvents.ClearAll(); GameTime.Reset(); }
        }

        void Update()
        {
            GameTime.Tick();
            float dt = Time.deltaTime;

            if (State == GameState.Title || State == GameState.CharacterSelect)
            {
                // Title: camera slowly pans
                var cam = CameraRig.I;
                cam.target = null;
                cam.Snap(new Vector2(Mathf.Sin(Time.unscaledTime * 0.08f) * 6f, 2 + Mathf.Cos(Time.unscaledTime * 0.06f) * 3f));
                WorldStreamer.I.Tick(cam.transform.position);
                return;
            }

            if (Player != null) WorldStreamer.I.Tick(Player.Pos);

            if (State == GameState.Playing && Player != null && Player.Alive)
            {
                HandleGlobalKeys();
                discoverT -= dt;
                if (discoverT <= 0) { discoverT = 0.5f; Diverse.World.I.UpdateDiscovery(Player.Pos); UpdateQuests(); }
                if (Controls.Down(Act.Interact)) { var it = Interactable.Nearest(Player.Pos); if (it != null) Interact(it); }
                saveT -= Time.unscaledDeltaTime;
                if (saveT <= 0) { saveT = 20; SaveSystem.SaveWorld(World); }
                if (Player.PendingEvolutions > 0 && !Generating && Offer == null && Actor.Nearest(Player.Pos, 7, Team.Enemy) == null)
                    BeginEvolution();
            }
            else if (State == GameState.Map || State == GameState.Paused || State == GameState.Dialogue || State == GameState.Evolving || State == GameState.Settings)
            {
                if (Controls.Down(Act.Pause) && State != GameState.Evolving) CloseOverlay();
                if (State == GameState.Map && Controls.Down(Act.Map)) CloseOverlay();
            }
        }

        void HandleGlobalKeys()
        {
            if (Controls.Down(Act.Map)) OpenOverlay(GameState.Map);
            else if (Controls.Down(Act.Pause)) OpenOverlay(GameState.Paused);
            else if (Controls.Down(Act.Abilities))
            {
                if (Player.PendingEvolutions > 0 && !Generating && Offer == null) BeginEvolution();
                else OpenOverlay(GameState.Paused, "abilities");
            }
            else if (Controls.Down(Act.Chronicle)) OpenOverlay(GameState.Paused, "chronicle");
        }

        public void OpenOverlay(GameState s, string tab = null)
        {
            State = s;
            GameTime.Pause("overlay");
            Ui.OnOverlayOpened(s, tab);
            Sfx.Play("ui", 0.5f);
        }

        public void CloseOverlay()
        {
            if (State == GameState.Dialogue) Ui.CloseDialogue();
            State = GameState.Playing;
            GameTime.Resume("overlay");
            Sfx.Play("ui", 0.4f, 0.8f);
        }

        // ───────────────────────── Starting a run ─────────────────────────

        public void StartRun(CostumeDef costume, WeaponKind weapon)
        {
            World.lifeCount++;
            World.lastCostume = costume.id;
            World.lastWeapon = (int)weapon;
            HeroName = MakeHeroName(costume);
            Diverse.World.I.ResetForRun();
            Fx.I.ClearAll();

            // Start position: plaza (a little in front of the well)
            var w = DB.W(weapon);
            var weaponCopy = new WeaponDef
            {
                kind = w.kind, name = w.name, desc = w.desc, tags = w.tags, slashColor = w.slashColor, slashCore = w.slashCore, baseDamage = w.baseDamage,
                comboReset = w.comboReset, moveSpeedMul = w.moveSpeedMul, attackRange = w.attackRange, combo = w.combo, skills = w.skills, element = w.element,
                sfxSwing = w.sfxSwing, sfxHit = w.sfxHit,
            };
            if (Player != null) Destroy(Player.gameObject);
            WorldStreamer.I.Warm(new Vector2(0, -1.5f));
            Player = Player.Spawn(costume, weaponCopy, new Vector2(0, -1.5f));
            Player.HeroName = HeroName;
            Player.Gold = Mathf.Min(World.gold, 40 + World.lifeCount * 10);   // withdraw part of the shared vault's gold
            World.gold -= Player.Gold;
            CameraRig.I.target = Player.transform;
            CameraRig.I.Snap(Player.Pos);
            Offer = null; Generating = false; Boss = null;
            State = GameState.Playing;
            GameTime.ClearPauses();
            World.Log($"{World.lifeCount}번째 삶: {HeroName}({costume.name}, {w.name}) — 기억의 광장에서 눈을 떴다.", "life");
            if (World.lifeCount == 1)
                Ui.Toast("우클릭 이동/공격 · 좌클릭 공격 · QWER 스킬 · Space 대시 · F 상호작용 · Tab 지도");
            else
                Ui.Toast($"{World.lifeCount}번째 삶. 세계는 당신의 지난 삶을 기억한다.");
            EnsureQuests();
            SaveSystem.SaveWorld(World);
        }

        static readonly string[] namePre = { "하", "설", "봄", "달", "별", "바", "단", "루", "윤", "가", "나", "라", "모", "호", "소", "리", "토", "키" };
        static readonly string[] namePost = { "람", "이", "리", "울", "빛", "니", "아", "온", "잉", "롱", "운", "린", "키", "돌" };

        string MakeHeroName(CostumeDef c)
        {
            var r = new Rng((uint)(World.seed + World.lifeCount * 7919));
            return r.Pick(namePre) + r.Pick(namePost);
        }

        /// <summary>Legacy inherited from past lives (statues/recovered abilities) → passive stat bonus.</summary>
        public void ApplyLegacy(Stats s)
        {
            int statues = World.graves.Count(g => g.statue);
            if (statues > 0) s.Mul(StatId.Attack, 0.03f * statues);
            s.Add(StatId.MaxHp, World.towerFloor * 10);
        }

        // ───────────────────────── Events ─────────────────────────

        void OnCombat(CombatEvent e) { }

        void OnWorldAction(string kind, string detail)
        {
            Player?.Telemetry.RecordWorld(kind);
        }

        public void LastDamageSource(Actor a) => lastDamageSource = a;

        public void OnEnemyKilled(Enemy e)
        {
            var d = e.def;
            Pickup.Drop("xp", d.xp / Mathf.Max(1, Mathf.CeilToInt(d.xp / 8f)), e.Pos, Mathf.CeilToInt(d.xp / 8f));
            Pickup.Drop("gold", d.gold, e.Pos, Random.value < 0.6f ? 1 : 2);
            if (Random.value < 0.05f || d.boss) Pickup.Drop("heart", 20, e.Pos);
            if (d.boss) Pickup.Drop("shard", 1, e.Pos, 3);
            Diverse.World.I.OnCampEnemyKilled(e.campId);
            foreach (var q in World.quests)
                if (q.status == 1 && q.kind == "hunt" && q.enemy == d.id) { q.have++; if (q.have >= q.need) { q.status = 2; Ui.Toast($"의뢰 완료: {q.title} — 게시판에 보고하세요"); } }
            if (d.boss)
            {
                if (Boss == e) Boss = null;
                World.Log($"{Ko.I(HeroName)} {Ko.Eul(d.name)} 쓰러뜨렸다.", "deed");
            }
        }

        public void OnCampCleared(string id, StructureSpec spec)
        {
            foreach (var q in World.quests)
                if (q.status == 1 && q.targetId == id) { q.status = 2; Ui.Toast($"의뢰 완료: {q.title}"); }
            if (spec != null && spec.kind == StructKind.Lair) Pickup.Drop("shard", 2, Player.Pos);
        }

        public void ShowBoss(Enemy e) { Boss = e; }

        // ───────────────────────── Death ─────────────────────────

        public void OnPlayerDied(DamageInfo killer)
        {
            StartCoroutine(DeathSequence(killer));
        }

        IEnumerator DeathSequence(DamageInfo killer)
        {
            State = GameState.Dead;
            string cause = killer.source is Enemy en ? en.def.name : lastDamageSource is Enemy le ? le.def.name : "알 수 없는 원인";
            var p = Player;
            var grave = new GraveRecord
            {
                life = World.lifeCount, heroName = HeroName, costume = p.Costume.id, weapon = (int)p.Weapon.kind, level = p.Level, kills = p.Kills,
                x = p.Pos.x, y = p.Pos.y, cause = cause,
            };
            // Abilities carved on the grave (the 2 strongest) → the next life can visit the grave and recover them
            foreach (var g in p.Abilities.Owned.Where(a => a.kind == "trigger").OrderByDescending(a => a.cost).Take(2)) grave.abilities.Add(AbilityRecord.From(g));
            // Heroes who achieved great deeds get a statue
            grave.statue = p.Level >= 12 || World.towerFloor >= 2 && p.Kills > 80;
            grave.epitaph = $"{HeroName} — {Ko.Ro(cause)} 인해 잠들다. 레벨 {p.Level}, 처치한 몬스터 {p.Kills}마리.";
            World.graves.Add(grave);
            World.gold += p.Gold / 2;    // half the gold goes to the vault
            World.Log($"{Ko.I(HeroName)} {Ko.Ro(cause)} 인해 쓰러졌다 ({Diverse.World.I.Gen.RegionName(p.Pos)}). 무덤이 세워졌다.", "death");
            SaveSystem.SaveWorld(World);

            yield return new WaitForSecondsRealtime(1.6f);
            GameTime.ClearPauses();
            Ui.ShowDeath(grave);
            if (AiClient.Enabled) StartCoroutine(AiClient.Epitaph(grave, text => { if (!string.IsNullOrEmpty(text)) { grave.epitaph = text; SaveSystem.SaveWorld(World); } }));
        }

        public void ToCharacterSelect()
        {
            if (Player != null) { Destroy(Player.gameObject); Player = null; }
            Diverse.World.I.ResetForRun();
            WorldStreamer.I.Warm(Vector2.zero);
            State = GameState.CharacterSelect;
            GameTime.ClearPauses();
        }

        public void ResetWorld()
        {
            SaveSystem.DeleteWorld();
            World = SaveSystem.NewWorld();
            Destroy(WorldStreamer.I.gameObject);
            Destroy(Diverse.World.I.gameObject);
            Diverse.World.Create(World.seed);
            WorldStreamer.I.Warm(Vector2.zero);
            State = GameState.Title;
        }

        void OnApplicationQuit()
        {
            if (World != null) SaveSystem.SaveWorld(World);
        }
    }
}
