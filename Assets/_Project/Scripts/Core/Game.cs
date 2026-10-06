using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public enum GameState { Playing, Evolving, Dialogue, Map, Paused, Dead }

    /// <summary>
    /// Game scene entry point. Game.unity holds the camera, Fx, Sfx, UI and world objects; Game wires them up with save data
    /// and starts the life the MainMenu scene asked for (Session.Start).
    /// Flow: MainMenu (title → character select) → Game (run) → death → MainMenu character select → next life
    /// </summary>
    [DefaultExecutionOrder(100)]
    public partial class Game : MonoBehaviour
    {
        public static Game I { get; private set; }
        public static Settings Settings => Session.Settings;

        [System.NonSerialized] public GameState State = GameState.Playing;
        public WorldSave World => Session.World;
        [System.NonSerialized] public Player Player;
        [System.NonSerialized] public string HeroName;
        [System.NonSerialized] public bool UiCapturingMouse;
        public GameUI Ui;
        [SerializeField] Diverse.World worldManager;

        Actor lastDamageSource;
        float discoverT, saveT;
        System.Random rnd = new System.Random();
        [System.NonSerialized] public bool Generating;            // AI generating
        [System.NonSerialized] public List<AbilityDef> Offer;     // current evolution candidates (2~3, System rules can change it)
        [System.NonSerialized] public string OfferStatus;
        [System.NonSerialized] public Enemy Boss;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            GameTime.Reset();
            GameEvents.ClearAll();
            Session.EnsureLoaded();

            // CameraRig, Fx, Sfx, UI and WorldManager are placed in Game.unity and register themselves in their own Awake.
            // Game runs after them (DefaultExecutionOrder) and hands out the data they need.
            worldManager.Init(World.seed);
            Ui.Init(this);
            GameEvents.Toast += Ui.Toast;
            GameEvents.WorldAction += OnWorldAction;
            GameEvents.Combat += OnCombat;
            Sfx.PlayMusic(true);
        }

        void Start()
        {
            // Opened straight from the editor (no menu request): continue the saved life, or start a default one
            var kind = Session.Start;
            Session.Start = Session.StartKind.None;
            if (kind == Session.StartKind.None) kind = Session.HasRunToContinue ? Session.StartKind.Continue : Session.StartKind.NewLife;
            if (kind == Session.StartKind.Continue && Session.HasRunToContinue) ContinueRun();
            else
            {
                var costume = Session.StartCostume ?? DB.Costume(World.lastCostume);
                var weapon = Session.StartCostume != null ? Session.StartWeapon : (WeaponKind)World.lastWeapon;
                StartRun(costume, weapon);
            }
        }

        void OnDestroy()
        {
            if (I == this) { I = null; GameEvents.ClearAll(); GameTime.Reset(); }
        }

        void Update()
        {
            GameTime.Tick();
            float dt = Time.deltaTime;

            if (Player != null) WorldStreamer.I.Tick(Player.Pos);

            if (State == GameState.Playing && Player != null && Player.Alive)
            {
                HandleGlobalKeys();
                discoverT -= dt;
                if (discoverT <= 0) { discoverT = 0.5f; Diverse.World.I.UpdateDiscovery(Player.Pos); UpdateQuests(); }
                if (Controls.Down(Act.Interact)) { var it = Interactable.Nearest(Player.Pos); if (it != null) Interact(it); }
                saveT -= Time.unscaledDeltaTime;
                if (saveT <= 0) { saveT = 20; SaveSystem.SaveWorld(World); SaveRun(); }
                UpdateFrontier(dt);
                if (Player.PendingEvolutions > 0 && !Generating && Offer == null && Actor.Nearest(Player.Pos, 7, Team.Enemy) == null)
                    BeginEvolution();
            }
            else if (State == GameState.Map || State == GameState.Paused || State == GameState.Dialogue || State == GameState.Evolving)
            {
                if (Controls.Down(Act.Pause) && State != GameState.Evolving && !Ui.ConsumesEscape) CloseOverlay();
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
            var weaponCopy = w.Clone();
            if (Player != null) Destroy(Player.gameObject);
            WorldStreamer.I.Warm(new Vector2(0, -1.5f));
            Player = Player.Spawn(costume, weaponCopy, new Vector2(0, -1.5f));
            Player.HeroName = HeroName;
            Player.Gold = Mathf.Min(World.gold, 40 + World.lifeCount * 10);   // withdraw part of the shared vault's gold
            World.gold -= Player.Gold;
            CameraRig.I.target = Player.transform;
            CameraRig.I.Snap(Player.Pos);
            Offer = null; Generating = false; Boss = null; ChatBusy = false;
            ResetPrefetch();
            State = GameState.Playing;
            GameTime.ClearPauses();
            World.Log($"{World.lifeCount}번째 삶: {HeroName}({costume.name}, {w.name}) — 기억의 광장에서 눈을 떴다.", "life");
            if (World.lifeCount == 1)
                Ui.Toast("우클릭 이동/공격 · 좌클릭 공격 · QWER 스킬 · Space 대시 · F 상호작용 · Tab 지도");
            else
                Ui.Toast($"{World.lifeCount}번째 삶. 세계는 당신의 지난 삶을 기억한다.");
            EnsureQuests();
            SaveSystem.SaveWorld(World);
            SaveRun();
            RequestPrefetch();
        }

        // ───────────────────────── Continuing a life ─────────────────────────


        /// <summary>Snapshot the current life so it can be continued later.</summary>
        public void SaveRun()
        {
            var p = Player;
            if (p == null || !p.Alive || State == GameState.Dead) return;
            var r = new RunSave
            {
                life = World.lifeCount, heroName = HeroName, costume = p.Costume.id, weapon = (int)p.Weapon.kind, weaponDamage = p.Weapon.baseDamage,
                level = p.Level, xp = p.Xp, xpToNext = p.XpToNext, gold = p.Gold, kills = p.Kills, potions = p.Potions,
                attrs = (int[])p.Attrs.Clone(), unspentAttr = p.UnspentAttr, pendingEvolutions = p.PendingEvolutions,
                hp = p.hp, dashMax = p.DashChargesMax, x = p.Pos.x, y = p.Pos.y, raft = p.HasRaft,
            };
            foreach (var a in p.Abilities.Owned) r.abilities.Add(AbilityRecord.From(a));
            foreach (var kv in p.Telemetry.intent) r.intent.Add(kv.Key + "=" + kv.Value);
            Session.PendingRun = r;
            SaveSystem.SaveRun(r);
        }

        /// <summary>Resume the saved life exactly where it was left.</summary>
        public void ContinueRun()
        {
            var r = Session.PendingRun;
            if (r == null) return;
            var costume = DB.Costumes.FirstOrDefault(c => c.id == r.costume) ?? DB.Costumes[0];
            var weapon = (WeaponKind)r.weapon;
            HeroName = r.heroName;
            Diverse.World.I.ResetForRun();
            Fx.I.ClearAll();
            var w = DB.W(weapon);
            var weaponCopy = w.Clone();
            if (r.weaponDamage > 0) weaponCopy.baseDamage = r.weaponDamage;
            if (Player != null) Destroy(Player.gameObject);
            var pos = new Vector2(r.x, r.y);
            WorldStreamer.I.Warm(pos);
            Player = Player.Spawn(costume, weaponCopy, pos);
            var p = Player;
            p.HeroName = HeroName;
            p.Level = r.level; p.Xp = r.xp; p.XpToNext = r.xpToNext;
            p.Gold = r.gold; p.Kills = r.kills; p.Potions = r.potions;
            if (r.attrs != null && r.attrs.Length == 4) p.Attrs = (int[])r.attrs.Clone();
            p.UnspentAttr = r.unspentAttr; p.PendingEvolutions = r.pendingEvolutions;
            p.DashChargesMax = p.DashCharges = Mathf.Max(2, r.dashMax);
            p.HasRaft = r.raft;
            foreach (var rec in r.abilities) { var g = rec.ToAbility(); if (g != null) p.Abilities.Restore(g); }
            foreach (var s in r.intent) { var kv = s.Split('='); if (kv.Length == 2 && int.TryParse(kv[1], out var n)) p.Telemetry.intent[kv[0]] = n; }
            p.RecalcStats();
            p.hp = Mathf.Clamp(r.hp, 1, p.maxHp);
            p.Pos = WorldStreamer.I.NearestWalkable(pos, p.radius, 8, p.HasRaft);
            CameraRig.I.target = p.transform;
            CameraRig.I.Snap(p.Pos);
            Offer = null; Generating = false; Boss = null; ChatBusy = false;
            ResetPrefetch();
            State = GameState.Playing;
            GameTime.ClearPauses();
            Ui.Toast($"{Ko.I(HeroName)} 다시 길을 나선다. (Lv.{p.Level})");
            EnsureQuests();
            RequestPrefetch();
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
            if (d.boss) Player?.Abilities?.Raise("BossKilled", e.Pos);
            if (d.boss && e.campId != null)
            {
                World.bossKills++;
                Ui.Toast($"보스 처치 {World.bossKills}회 — {FrontierHint()}");
            }
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
            Player?.Abilities?.Raise("CampCleared");
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
            foreach (var g in p.Abilities.Owned.Where(a => a.tier >= 1).OrderByDescending(a => a.power).Take(2)) grave.abilities.Add(AbilityRecord.From(g));
            // Heroes who achieved great deeds get a statue
            grave.statue = p.Level >= 12 || World.towerFloor >= 2 && p.Kills > 80;
            grave.epitaph = $"{HeroName} — {Ko.Ro(cause)} 인해 잠들다. 레벨 {p.Level}, 처치한 몬스터 {p.Kills}마리.";
            World.graves.Add(grave);
            Session.PendingRun = null;
            SaveSystem.DeleteRun();
            World.gold += p.Gold / 2;    // half the gold goes to the vault
            World.Log($"{Ko.I(HeroName)} {Ko.Ro(cause)} 인해 쓰러졌다 ({Diverse.World.I.Gen.RegionName(p.Pos)}). 무덤이 세워졌다.", "death");
            SaveSystem.SaveWorld(World);

            yield return new WaitForSecondsRealtime(1.6f);
            GameTime.ClearPauses();
            Ui.ShowDeath(grave);
            if (AiClient.Enabled) StartCoroutine(AiClient.Epitaph(grave, text => { if (!string.IsNullOrEmpty(text)) { grave.epitaph = text; SaveSystem.SaveWorld(World); } }));
        }

        /// <summary>Pause menu "타이틀로": keep this life so the title can continue it.</summary>
        public void SaveAndQuitToTitle()
        {
            SaveSystem.SaveWorld(World);
            SaveRun();
            GameTime.ClearPauses();
            Session.ToMenu(false);
        }

        /// <summary>Death screen "다음 삶": back to the menu's character select.</summary>
        public void NextLife()
        {
            GameTime.ClearPauses();
            Session.ToMenu(true);
        }

        void OnApplicationQuit()
        {
            if (World != null) SaveSystem.SaveWorld(World);
            SaveRun();
        }
    }
}
