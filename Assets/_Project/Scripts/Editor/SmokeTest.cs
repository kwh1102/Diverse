using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Play-mode smoke test: MainMenu → character select → Game (every weapon, every prefab, every overlay) → death → MainMenu → continue.
    /// Drives the real uGUI buttons. Uses a scratch save folder, never the real saves.
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.SmokeTest.RunBatch  (exit code 0 = pass)
    /// </summary>
    public static class SmokeTest
    {
        const string ResultKey = "Diverse.SmokeTest.Result";
        const string BatchKey = "Diverse.SmokeTest.Batch";
        const string RunningKey = "Diverse.SmokeTest.Running";
        const string DirKey = "Diverse.SmokeTest.Dir";

        // Entering play mode may reload the domain, which drops static event handlers and env vars set in-process:
        // re-attach after every reload while a test is in flight (SessionState survives reloads).
        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", SessionState.GetString(DirKey, null));
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Runner>() == null)
                new GameObject("SmokeTestRunner").AddComponent<Runner>();
        }

        [MenuItem("Diverse/스모크 테스트 (플레이 모드)", priority = 25)]
        public static void RunMenu() => Begin(false);

        public static void RunBatch() => Begin(true);

        static void Begin(bool batch)
        {
            SessionState.SetBool(BatchKey, batch);
            SessionState.EraseString(ResultKey);
            // Scratch save folder (SaveSystem reads DIVERSE_SAVE_DIR); AI off so no key or network is needed
            var dir = Path.Combine(Path.GetTempPath(), "DiverseSmokeTest");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonUtility.ToJson(new Settings { aiEnabled = false }));
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.OpenScene(UIBuilder.MenuScenePath);
            EditorApplication.playModeStateChanged += OnPlayMode;
            // In batch mode -executeMethod runs before the editor loop is up; entering play mode from there stalls.
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                if (Object.FindFirstObjectByType<Runner>() == null) new GameObject("SmokeTestRunner").AddComponent<Runner>();
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
                var result = SessionState.GetString(ResultKey, "FAIL: no result (play mode exited early)");
                bool ok = result.StartsWith("PASS");
                Debug.Log("[SmokeTest] " + result);
                if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        class Runner : MonoBehaviour
        {
            readonly StringBuilder log = new StringBuilder();
            int errors;
            string fail;
            string step = "start";

            void Awake() => DontDestroyOnLoad(gameObject);
            void OnEnable() => Application.logMessageReceived += OnLog;
            void OnDisable() => Application.logMessageReceived -= OnLog;

            void OnLog(string msg, string stack, LogType type)
            {
                if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                {
                    errors++;
                    if (errors <= 5) log.AppendLine($"  [{step}] {type}: {msg}\n{stack}");
                }
            }

            void Fail(string why) { if (fail == null) fail = $"[{step}] {why}"; }

            static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

            static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);

            static bool Visible<T>() where T : Component { var c = Find<T>(); return c != null && c.gameObject.activeInHierarchy; }

            /// <summary>Press a visible uGUI button by name under a root (what a mouse click would do).</summary>
            bool Click(Component root, string name)
            {
                if (root == null) { Fail($"no root for button '{name}'"); return false; }
                var b = root.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.name == name && x.gameObject.activeInHierarchy);
                if (b == null) { Fail($"button '{name}' not found/visible"); return false; }
                if (!b.interactable) { Fail($"button '{name}' not interactable"); return false; }
                b.onClick.Invoke();
                return true;
            }

            IEnumerator Start()
            {
                yield return Frames(2);
                var flow = Flow();
                while (fail == null)
                {
                    bool more;
                    try { more = flow.MoveNext(); }
                    catch (System.Exception e) { Fail("threw " + e); break; }
                    if (!more) break;
                    yield return flow.Current;
                }
                if (fail == null && errors > 0) fail = $"{errors} error log(s) during play";
                SessionState.SetString(ResultKey, fail == null
                    ? "PASS: menu → game (all weapons, prefabs, overlays) → death → menu → continue"
                    : "FAIL: " + fail + "\n" + log);
                EditorApplication.ExitPlaymode();
            }

            IEnumerator Flow()
            {
                // 1) MainMenu: title, settings round trip, character select
                step = "menu";
                if (Find<MenuUI>() == null) { Fail("MenuUI missing (MainMenu scene)"); yield break; }
                if (!Visible<TitleView>()) Fail("title not visible");
                if (Session.World == null) Fail("session not loaded");
                Click(Find<TitleView>(), "Settings");
                yield return Frames(2);
                if (!Visible<SettingsView>()) Fail("settings not visible");
                Click(Find<SettingsView>(), "KeysTab");
                yield return Frames(2);
                Click(Find<SettingsView>(), "Back");
                yield return Frames(2);
                if (!Visible<TitleView>()) Fail("back from settings did not show the title");
                Click(Find<TitleView>(), "Start");
                yield return Frames(2);
                if (!Visible<CharacterSelectView>()) { Fail("character select not visible"); yield break; }
                Click(Find<CharacterSelectView>(), "Weapon 2");     // crossbow
                yield return Frames(1);

                // 2) Start a life → Game scene
                step = "start life";
                Click(Find<CharacterSelectView>(), "StartLife");
                yield return Frames(10);
                if (Game.I == null) { Fail("Game scene did not load"); yield break; }
                if (Game.I.Player == null || Game.I.Player.Weapon.kind != WeaponKind.Crossbow) Fail("player not spawned with the picked weapon");
                var boot = CheckBoot();
                if (boot != null) Fail(boot);
                if (!Visible<HudView>()) Fail("HUD not visible");

                // 3) Every weapon + every runtime prefab
                foreach (WeaponKind wk in System.Enum.GetValues(typeof(WeaponKind)))
                {
                    step = "weapon " + wk;
                    Game.I.StartRun(DB.Costumes[(int)wk % DB.Costumes.Count], wk);
                    yield return Frames(20);
                    var err = Exercise(wk);
                    if (err != null) { Fail(err); yield break; }
                    yield return Frames(30);
                }

                // 4) Overlays
                var g = Game.I;
                step = "map";
                g.OpenOverlay(GameState.Map);
                yield return Frames(5);
                if (!Visible<MapView>()) Fail("map not visible");
                g.CloseOverlay();
                yield return Frames(2);

                step = "pause";
                g.OpenOverlay(GameState.Paused);
                yield return Frames(2);
                if (!Visible<PauseView>()) { Fail("pause not visible"); yield break; }
                foreach (var tab in new[] { "능력치", "연대기", "설정", "키 설정", "능력" })
                {
                    Click(Find<PauseView>(), "Tab " + tab);
                    yield return Frames(2);
                }
                g.Player.UnspentAttr = 1;
                Click(Find<PauseView>(), "Tab 능력치");
                yield return Frames(1);
                int before = g.Player.Attrs[0];
                Click(Find<PauseView>(), "Plus 0");
                if (g.Player.Attrs[0] != before + 1) Fail("attribute + did not apply");
                Click(Find<PauseView>(), "Resume");
                yield return Frames(2);
                if (g.State != GameState.Playing) Fail("resume did not close the pause menu");

                step = "dialogue";
                var npc = Interactable.All.Where(i => i != null && i.kind.StartsWith("npc_")).OrderBy(i => Vector2.Distance(i.Pos, g.Player.Pos)).FirstOrDefault();
                if (npc == null) Fail("no NPC in the starting town");
                else
                {
                    Dialogue.Open(g, npc);
                    yield return Frames(3);
                    if (!Visible<DialogueView>()) Fail("dialogue not visible");
                    var opts = Find<DialogueView>().GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToList();
                    if (opts.Count == 0) Fail("dialogue has no options");
                    else opts.Last().onClick.Invoke();     // the last option leaves on every page
                    yield return Frames(3);
                    if (g.State == GameState.Dialogue) { g.CloseOverlay(); yield return Frames(2); }
                }

                // Rule IR: every reference ability on one live player, then real combat events through the runtime
                step = "rule abilities";
                {
                    var p = g.Player;
                    int fired0 = p.Abilities.fireCount;
                    var all = new System.Collections.Generic.List<AbilityDef> { RuleSamples.FireSource(), RuleSamples.FireSource2() };
                    all.AddRange(RuleSamples.Accepted());
                    int added = 0;
                    foreach (var a in all)
                    {
                        a.EnsureLists(); RuleValidator.Tag(a);
                        a.id = "smoke" + added++;
                        p.Abilities.Add(a);      // numbers as written: this checks execution, not balance
                    }
                    p.RecalcStats();
                    var foe = Enemy.Spawn(DB.ScaledEnemy("fox", 0), p.Pos + Vector2.right * 1.5f, null);
                    foe.AddStatus("poison", 3, 1, 0, p, Pal.White);
                    GameEvents.Raise(new CombatEvent { type = Trig.Damaged, source = p, position = p.Pos, amount = p.maxHp * 0.1f });
                    for (int i = 0; i < 3; i++) GameEvents.Raise(new CombatEvent { type = Trig.PerfectDodge, source = p, position = p.Pos, direction = Vector2.right });
                    GameEvents.Raise(new CombatEvent { type = Trig.ComboFinish, source = p, position = p.Pos, direction = Vector2.right });
                    p.Heal(10);
                    foe.TakeDamage(new DamageInfo { amount = 99999, source = p, direction = Vector2.right });
                    GameEvents.Raise(new CombatEvent { type = Trig.Kill, source = p, target = foe, position = foe.Pos });
                    yield return Frames(30);
                    if (p.Abilities.fireCount - fired0 < 4) Fail($"rule abilities barely fired ({p.Abilities.fireCount - fired0})");
                    if (p.Abilities.OfferCount != 2) Fail("system rule OfferCount not applied");
                    p.Abilities.Clear(); p.RecalcStats();
                }

                // Every expressiveness case (relations, temporals, replace/constrain, meta, system…) on the live runtime:
                // real events through GameEvents + real player actions, several seconds of frames. Any exception fails the test.
                step = "rule language runtime";
                {
                    var p = g.Player;
                    int fired0 = p.Abilities.fireCount;
                    int added = 0;
                    foreach (var c in RuleExpressiveness.All())
                    {
                        var a = AbilityCompiler.Compile(c.intent).ability;
                        var r = RuleValidator.Validate(a, BuildContext.Bare(20));
                        if (r.failedStage is "Schema" or "Type" or "Reference" or "Capability" or "Bounds") { Fail($"{c.name} not structurally valid: {r.Summary()}"); continue; }
                        RuleValidator.Tag(a);
                        a.name = c.name; a.id = "expr" + added++;
                        p.Abilities.Add(a);
                    }
                    p.RecalcStats();
                    for (int wave = 0; wave < 3; wave++)
                    {
                        var foes = new System.Collections.Generic.List<Enemy>();
                        for (int i = 0; i < 4; i++) foes.Add(Enemy.Spawn(DB.ScaledEnemy("fox", 0), p.Pos + MathX.Dir(i * 90 + wave * 20) * 2f, null));
                        foes[0].AddStatus("poison", 3, 1, 0, p, Pal.White);
                        foes[1].AddStatus("burn", 3, 1, 0, p, Pal.Fire);
                        GameEvents.Raise(new CombatEvent { type = Trig.Hit, source = p, target = foes[2], position = foes[2].Pos, amount = p.AttackPower, tags = p.Weapon.tags });
                        GameEvents.Raise(new CombatEvent { type = Trig.Crit, source = p, target = foes[1], position = foes[1].Pos, amount = p.AttackPower * 2, tags = p.Weapon.tags });
                        GameEvents.Raise(new CombatEvent { type = Trig.Damaged, source = p, target = foes[3], position = p.Pos, amount = p.maxHp * 0.1f });
                        GameEvents.Raise(new CombatEvent { type = Trig.SkillCast, source = p, position = p.Pos, direction = Vector2.right, tags = "SKILL" });
                        GameEvents.Raise(new CombatEvent { type = Trig.ComboFinish, source = p, position = p.Pos, direction = Vector2.right });
                        GameEvents.Raise(new CombatEvent { type = Trig.PerfectDodge, source = p, position = p.Pos, direction = Vector2.right });
                        GameEvents.Raise(new CombatEvent { type = Trig.Guard, source = p, target = foes[3], position = p.Pos });
                        GameEvents.Raise(new CombatEvent { type = Trig.LowHealth, source = p, position = p.Pos });
                        p.TryDash(Vector2.left);
                        p.Heal(5);
                        p.AddShield(5, 3); p.invulnUntil = 0;
                        p.TakeDamage(new DamageInfo { amount = 8, source = foes[3], direction = Vector2.right });
                        p.Abilities.Raise("Pickup", p.Pos, "gold");
                        p.Abilities.Moved(7);
                        foreach (var f in foes) if (f != null && f.Alive) f.TakeDamage(new DamageInfo { amount = 99999, source = p, direction = Vector2.right });
                        foreach (var f in foes) GameEvents.Raise(new CombatEvent { type = Trig.Kill, source = p, target = f, position = f != null ? f.Pos : p.Pos, amount = 99999 });
                        yield return Frames(45);
                        foreach (var f in foes) if (f != null) f.Despawn();
                    }
                    yield return Frames(30);
                    int fired = p.Abilities.fireCount - fired0;
                    if (fired < 20) Fail($"rule language barely fired ({fired})");
                    p.Abilities.Clear(); p.RecalcStats();
                    foreach (var c in Clone.Active.ToArray()) if (c != null) Object.Destroy(c.gameObject);
                    foreach (var s in Summon.Active.ToArray()) if (s != null) Object.Destroy(s.gameObject);
                    p.hp = p.maxHp;
                    yield return Frames(2);
                }

                step = "evolve";
                g.Player.PendingEvolutions = 1;
                g.BeginEvolution();
                for (int i = 0; i < 300 && g.Offer == null; i++) yield return null;
                if (!Visible<EvolveView>()) Fail("evolution screen not visible");
                if (g.Offer == null) Fail("no evolution offer (local composer)");
                else
                {
                    for (int i = 0; i < 120 && g.OfferLocked; i++) yield return null;
                    yield return Frames(2);
                    var card = Find<EvolveView>().GetComponentsInChildren<EvolveCard>().FirstOrDefault();
                    if (card == null) Fail("no card");
                    else Click(card, card.name);
                    yield return Frames(3);
                    if (g.State == GameState.Evolving) Fail("picking a card did not close the evolution screen");
                }

                // 5) Death → MainMenu character select
                step = "death";
                g.Player.invulnUntil = 0;
                g.Player.TakeDamage(new DamageInfo { amount = 999999, direction = Vector2.right, noEvents = true });
                for (int i = 0; i < 400 && !Visible<DeathView>(); i++) yield return null;
                if (!Visible<DeathView>()) { Fail("death screen not visible"); yield break; }
                Click(Find<DeathView>(), "NextLife");
                yield return Frames(10);
                if (Find<MenuUI>() == null || !Visible<CharacterSelectView>()) { Fail("did not return to character select"); yield break; }

                // 6) New life → pause → to title (saved) → continue
                step = "continue";
                Click(Find<CharacterSelectView>(), "StartLife");
                yield return Frames(10);
                if (Game.I == null) { Fail("Game scene did not load the 2nd time"); yield break; }
                Game.I.Player.Gold = 77;
                Game.I.OpenOverlay(GameState.Paused);
                yield return Frames(2);
                Click(Find<PauseView>(), "ToTitle");
                yield return Frames(10);
                if (!Visible<TitleView>()) { Fail("to-title did not show the title"); yield break; }
                Click(Find<TitleView>(), "Continue");
                yield return Frames(10);
                if (Game.I == null || Game.I.Player == null) { Fail("continue did not load the life"); yield break; }
                if (Game.I.Player.Gold != 77) Fail($"continued life lost its state (gold {Game.I.Player.Gold})");
            }

            string CheckBoot()
            {
                if (Game.I == null) return "Game.I missing";
                if (CameraRig.I == null || CameraRig.I.Cam == null) return "CameraRig not set up";
                if (CameraRig.I.Vignette == null) return "post-fx vignette missing";
                if (Fx.I == null) return "Fx missing";
                if (Sfx.I == null) return "Sfx missing";
                if (World.I == null || WorldStreamer.I == null || WorldStreamer.I.Gen == null) return "world not initialized";
                if (Game.I.Ui == null) return "UI missing";
                if (Object.FindFirstObjectByType<EditorOnlyPreview>() != null) return "editor-only preview survived into play";
                if (DB.Weapons.Count != 6 || DB.Costumes.Count != 6 || DB.Enemies.Count < 11) return $"DB counts {DB.Weapons.Count}/{DB.Costumes.Count}/{DB.Enemies.Count}";
                // Art.Get should pick up the baked PNG (an imported asset), and it must stay readable for Art.Silhouette
                var rabbit = Art.Rabbit(DB.Costumes[0], Pose.Idle0);
                if (!AssetDatabase.Contains(rabbit)) return "baked sprite PNG not used (still procedural)";
                if (!rabbit.texture.isReadable) return "baked sprite not readable";
                return null;
            }

            string Exercise(WeaponKind wk)
            {
                var p = Game.I.Player;
                if (p == null || !p.Alive) return "no live player";
                if (p.Weapon.kind != wk) return "wrong weapon";
                if (ReferenceEquals(p.Weapon, DB.W(wk))) return "player shares the asset's WeaponDef (smith upgrades would edit the asset)";

                var e = Enemy.Spawn(DB.Elite(DB.ScaledEnemy("fox", 1)), p.Pos + Vector2.right * 2f, null);
                e.TakeDamage(new DamageInfo { amount = 99999, source = p, direction = Vector2.right });
                if (e.Alive) return "enemy survived lethal damage";

                Pickup.Drop("gold", 3, p.Pos + Vector2.up, 2);
                Clone.Spawn(p, p.Pos + Vector2.left, 1f, 1f, false, wk == WeaponKind.Dagger, Element.Fire, "TEST", 0);
                Summon.Turret(p, p.Pos + Vector2.down, 1f, 1f, Element.Frost, "TEST");
                Summon.Orb(p, 1f, 1f, Element.None, 0, 1, "TEST");
                Summon.Zone(p, p.Pos, 1f, 1f, Element.Poison, "TEST");
                Projectile.Spawn(Art.Bolt(), p.Pos, Vector2.right * 10, Team.Enemy, p, DamageInfo.Basic(5, p, Vector2.right));
                Fx.I.Number(p.Pos, "123", Color.white);
                Game.I.Ui.Toast("smoke test toast");
                return null;
            }
        }
    }
}
