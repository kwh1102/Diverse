using System.Collections;
using System.Collections.Generic;
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

                // 0) Title (모드·세계 선택 / 설정 / 종료) → slot screen. A normal-mode world designs only the hero and weapon;
                //    worlds are created, continued and deleted per row
                step = "title";
                var titleView = Find<TitleView>();
                if (!Visible<TitleView>()) { Fail("title not shown first"); yield break; }
                if (titleView.GetComponentsInChildren<Button>().Any(b => b.name is "Continue" or "Abandon" or "Start" or "ResetWorld"))
                    Fail("title still has world buttons (they belong on the slot screen)");
                if (Session.World == null) Fail("session not loaded");
                Click(titleView, "Settings");
                yield return Frames(2);
                if (!Visible<SettingsView>()) Fail("settings not visible");
                Click(Find<SettingsView>(), "KeysTab");
                yield return Frames(2);
                Click(Find<SettingsView>(), "Back");
                yield return Frames(2);
                if (!Visible<TitleView>()) Fail("back from settings did not show the title");

                step = "save slots";
                Click(titleView, "ChangeSlot");
                yield return Frames(2);
                if (!Visible<SaveSelectView>()) { Fail("slot screen did not open from the title"); yield break; }
                var slotsView = Find<SaveSelectView>();
                Transform Row(int i) => slotsView.transform.Find("Panel/Slots/Slot " + i);
                Click(slotsView, "NormalTab");
                yield return Frames(1);
                Click(Row(1), "Play");               // empty second slot → new world → design the first life
                yield return Frames(2);
                if (Session.Mode != GameMode.Normal || Session.Slot != 2) { Fail("creating a normal world failed"); yield break; }
                var normalStudio = Find<DesignStudioView>();
                if (!Visible<DesignStudioView>()) { Fail("a new world did not open the design studio"); yield break; }
                if (normalStudio.GetComponentsInChildren<Button>().Any(b => b.name == "Tab 2" || b.name == "Tab 3")) Fail("normal mode still offers world/enemy design");
                Click(normalStudio, "Back");
                yield return Frames(2);
                if (!Visible<SaveSelectView>()) { Fail("studio back did not return to the slot screen"); yield break; }
                Click(slotsView, "NormalTab");
                yield return Frames(2);
                Click(Row(1), "Delete");
                yield return Frames(1);
                if (SaveSystem.Peek(GameMode.Normal, 2) == null) Fail("one press already deleted the world (no confirmation)");
                Click(Row(1), "Delete");
                yield return Frames(1);
                if (SaveSystem.Peek(GameMode.Normal, 2) != null) Fail("deleting a world left it behind");
                Click(slotsView, "CustomTab");
                yield return Frames(1);
                Click(Row(0), "Play");
                yield return Frames(2);
                if (Session.Mode != GameMode.Custom || Session.Slot != 1) { Fail("opening a custom world failed"); yield break; }

                // Design pieces that the live AI path uses (no network here)
                step = "design units";
                var errUnits = DesignUnits();
                if (errUnits != null) { Fail(errUnits); yield break; }

                // Design studio (AI off → local reader); no preset pickers are offered
                step = "design studio";
                if (!Visible<DesignStudioView>()) { Fail("design studio not visible"); yield break; }
                var studio = Find<DesignStudioView>();
                if (studio.GetComponentsInChildren<Button>().Any(b => b.name is "Classic" or "WeaponBaseButton" or "PerkBaseButton"))
                    Fail("preset character/weapon/perk pickers are still shown");
                var promptField = studio.GetComponentInChildren<InputField>();
                string[] prompts =
                {
                    "민트색 털에 남색 망토, 마법사 모자를 쓴 토끼",
                    "이쑤시개를 입에 물고 있다가 퉤 하고 발사하고 싶어, 공격력 10배",
                    "눈 내린 겨울 숲, 하늘색 호수",
                    "모든 적이 과자로 만들어진 세계",
                };
                for (int i = 0; i < 4; i++)
                {
                    Click(studio, "Tab " + i);
                    promptField.text = prompts[i];
                }
                Click(studio, "Generate");
                yield return Frames(3);
                var pack = DesignRuntime.Active;
                if (pack.weapon.shape != "needle" || pack.weapon.hold != "Mouth" || pack.attack.origin != "Mouth" || pack.weapon.baseWeapon != "Crossbow")
                    Fail($"toothpick prompt not understood ({pack.weapon.shape}/{pack.weapon.hold}/{pack.attack.origin}/{pack.weapon.baseWeapon})");
                if (pack.ignored.Count == 0) Fail("power request was not reported as ignored");
                if (pack.hero.hat != "wizard" || pack.world.IsDefault || pack.enemies.Identity) Fail("hero/world/enemy prompts not applied");
                if (Art.Bolt() == null || Art.Weapon(WeaponKind.Crossbow) == null) Fail("designed sprites missing");
                Click(studio, "Undo");
                yield return Frames(1);
                if (DesignRuntime.Active.weapon.shape == "needle") Fail("undo did not restore the previous look");
                Click(studio, "Generate");          // nothing changed → redo the current tab
                yield return Frames(1);
                for (int i = 0; i < 4; i++) { Click(studio, "Tab " + i); promptField.text = prompts[i] + " 좋아"; }
                Click(studio, "Generate");
                yield return Frames(2);

                // 2) Start a life → Game scene
                step = "start life";
                Click(studio, "StartLife");
                yield return Frames(10);
                if (Game.I == null) { Fail("Game scene did not load"); yield break; }
                if (Game.I.Player == null || Game.I.Player.Weapon.kind != WeaponKind.Crossbow) Fail("player not spawned with the designed chassis");
                if (Game.I.Player != null && !Game.I.Player.Costume.id.StartsWith(DesignStore.CostumePrefix)) Fail("player not drawn from the design pack");
                if (Game.I.Player != null && Game.I.Player.Weapon.name != "이쑤시개") Fail("weapon skin (name) not applied");
                if (Game.I.Player != null && Mathf.Abs(DesignCombat.Power(Game.I.Player.Weapon) - DesignCombat.Budget) > DesignCombat.Budget * 0.01f)
                    Fail($"designed weapon is off the power budget ({DesignCombat.Power(Game.I.Player.Weapon):0.0} vs {DesignCombat.Budget:0.0})");
                if (Game.I.Player != null && Game.I.Player.Costume.mods != null) Fail("designed hero kept a preset stat perk");
                yield return Frames(5);
                if (Game.I.Player != null && Game.I.Player.Abilities.Owned.Count == 0) Fail("designed hero got no starting passive");
                if (DesignStore.PackOfCostume(Game.I.Player.Costume.id)?.hero.perk == null) Fail("starting passive was not kept with the design");
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

                // 3b) Every enemy's signature attack runs its full telegraph → strike → recover cycle without errors
                var g = Game.I;
                step = "enemy patterns";
                // One enemy at a time, right next to the hero, so each gets a clear shot at its pattern
                g.Player.invulnUntil = Time.time + 999;
                // Leftover clones/summons from the weapon checks would kill the test enemies before they attack
                foreach (var s in Object.FindObjectsByType<Summon>(FindObjectsSortMode.None)) Object.Destroy(s.gameObject);
                foreach (var c in Object.FindObjectsByType<Clone>(FindObjectsSortMode.None)) Object.Destroy(c.gameObject);
                foreach (var a in Actor.All.ToArray()) if (a is Enemy) Object.Destroy(a.gameObject);
                yield return Frames(2);
                Enemy.SignaturesFired.Clear();
                var expected = new HashSet<string>();
                foreach (var id in DB.Enemies.Keys)
                {
                    var def = DB.ScaledEnemy(id, 0);
                    if (def.attack != EnemyAttack.Default && !def.boss) expected.Add(def.attack.ToString());
                    var e = Enemy.Spawn(def, g.Player.Pos + Vector2.right * Mathf.Min(2f, def.attackRange * 0.7f), null);
                    float until = Time.realtimeSinceStartup + (def.boss ? 8f : 4f);
                    while (Time.realtimeSinceStartup < until && e != null && e.Alive) { g.Player.invulnUntil = Time.time + 999; yield return null; }
                    if (def.attack != EnemyAttack.Default && !def.boss && !Enemy.SignaturesFired.Contains(def.attack.ToString()))
                        Debug.Log($"[SmokeTest] {id}: {(e == null || !e.Alive ? "died before attacking" : "never attacked")} at {(e != null ? Vector2.Distance(e.Pos, g.Player.Pos) : -1):0.0}m");
                    if (e != null) Object.Destroy(e.gameObject);
                    yield return Frames(2);
                }
                foreach (var x in expected) if (!Enemy.SignaturesFired.Contains(x)) Fail($"signature attack {x} never fired");
                g.Player.invulnUntil = 0;

                // 3c) New hero: no QWER skills yet; leveling learns them; evolutions only every N levels
                step = "progression";
                if (g.Player.LearnedSkills != 0) Fail($"a new hero should start with no skills (has {g.Player.LearnedSkills})");
                var prog = DB.Progression;
                g.Player.PendingEvolutions = 0;
                while (g.Player.Level < prog.skillLevels[0]) g.Player.GainXp(g.Player.XpToNext - g.Player.Xp + 0.01f);
                if (g.Player.LearnedSkills != 1) Fail($"level {prog.skillLevels[0]} should teach the first skill (learned {g.Player.LearnedSkills})");
                int expectEvolves = 0;
                for (int l = 2; l <= g.Player.Level; l++) if (prog.EvolvesAt(l)) expectEvolves++;
                if (g.Player.PendingEvolutions != expectEvolves) Fail($"evolutions {g.Player.PendingEvolutions} != {expectEvolves} expected for level {g.Player.Level}");
                g.Player.PendingEvolutions = 0;
                yield return Frames(2);

                // 3d) Ruin altar pays its shard once
                step = "ruin altar";
                {
                    var spec = new StructureSpec { id = "smoke_ruin", name = "시험 유적", kind = StructKind.Ruins, center = g.Player.Pos + Vector2.right * 6 };
                    var chunk = WorldStreamer.I.ChunkAt(g.Player.Pos);
                    var alt = Interactable.Create(chunk.transform, "ruin_altar", "ruin:" + spec.id, "고대 제단", spec.center, Art.Prop("altar"), chunk);
                    alt.data = spec;
                    int shards = g.World.memoryShards;
                    Dialogue.Open(g, alt); yield return Frames(2); g.CloseOverlay(); yield return Frames(1);
                    Dialogue.Open(g, alt); yield return Frames(2); g.CloseOverlay(); yield return Frames(1);
                    if (g.World.memoryShards != shards + 1) Fail($"altar paid {g.World.memoryShards - shards} shards (expected 1)");
                    Object.Destroy(alt.gameObject);
                }

                // 3e) Camp counts as cleared even if its chunk was reloaded mid-fight (spawn counter used to double)
                step = "camp clear";
                {
                    var c1 = Enemy.Spawn(DB.ScaledEnemy("fox", 0), g.Player.Pos + Vector2.up * 4, "smoke_camp");
                    var c2 = Enemy.Spawn(DB.ScaledEnemy("fox", 0), g.Player.Pos + Vector2.up * 5, "smoke_camp");
                    var q = new QuestState { id = "smoke_q", kind = "clear", targetId = "smoke_camp", title = "시험", status = 1 };
                    g.World.quests.Add(q);
                    c1.TakeDamage(new DamageInfo { amount = 99999, source = g.Player, direction = Vector2.up });
                    if (g.World.clearedCamps.Contains("smoke_camp")) Fail("camp cleared while a member is still alive");
                    c2.TakeDamage(new DamageInfo { amount = 99999, source = g.Player, direction = Vector2.up });
                    yield return Frames(2);
                    if (!g.World.clearedCamps.Contains("smoke_camp")) Fail("camp not cleared after its last member died");
                    if (q.status != 2) Fail($"clear quest not completed (status {q.status})");
                    g.World.quests.Remove(q);
                }

                // 3f) Warp stones: buy → place in the field → map click travels back to town
                step = "warp";
                {
                    g.Player.WarpStones = 1;
                    var field = WorldStreamer.I.NearestWalkable(new Vector2(30, 0), g.Player.radius);
                    WorldStreamer.I.Warm(field);
                    g.Player.Pos = field;
                    foreach (var a in Actor.All.ToArray()) if (a is Enemy e && Vector2.Distance(e.Pos, field) < 12) Object.Destroy(e.gameObject);
                    yield return Frames(2);
                    if (!g.PlaceWarpStone()) Fail("could not place a warp stone in the field");
                    else
                    {
                        if (g.WarpStoneInReach() == null) Fail("placed warp stone not in reach");
                        var town = g.WarpStones().First(w => w.id == Game.TownWarpId);
                        if (!g.TryWarp(town, out var why)) Fail("warp failed: " + why);
                        yield return Frames(5);
                        if (Vector2.Distance(g.Player.Pos, town.Pos) > 3) Fail($"warp did not move the hero (at {g.Player.Pos})");
                    }
                }

                // 4) Overlays
                step = "map";
                g.OpenOverlay(GameState.Map);
                yield return Frames(5);
                if (!Visible<MapView>()) Fail("map not visible");
                g.CloseOverlay();
                yield return Frames(2);

                step = "map travel";
                g.Player.SetTravelTarget(g.Player.Pos + new Vector2(6, 0));
                for (int i = 0; i < 60; i++) yield return null;
                if (g.Player.TravelTarget.HasValue && !g.Player.Moving) Fail("travel target set but the hero is not moving");
                g.Player.StopMoving();

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

                step = "merchant purchase limits";
                var merchant = Interactable.All.FirstOrDefault(i => i != null && i.kind == "npc_merchant");
                if (merchant == null) Fail("merchant missing");
                else
                {
                    int gold = g.Player.Gold, dashMax = g.Player.DashChargesMax, dash = g.Player.DashCharges;
                    g.Player.Gold = 1000; g.Player.DashChargesMax = 2; g.Player.DashCharges = 2;
                    Dialogue.Open(g, merchant);
                    Dialogue.Current.options[1].act(); Dialogue.Current.options[1].act();
                    yield return Frames(3);
                    if (g.Player.DashChargesMax != 4 || Dialogue.Current.options[1].enabled) Fail("dash purchase limit not enforced");
                    var buttons = Find<DialogueView>().GetComponentsInChildren<Button>(true);
                    var shoe = buttons.FirstOrDefault(b => b.gameObject.activeInHierarchy && b.GetComponentInChildren<Text>().text.Contains("대시 신발"));
                    if (shoe == null || shoe.interactable || shoe.colors.disabledColor == shoe.colors.normalColor) Fail("disabled purchase has no distinct appearance");
                    int paid = g.Player.Gold;
                    Dialogue.Current.options[1].act();
                    if (g.Player.Gold != paid || g.Player.DashChargesMax != 4) Fail("disabled purchase callback charged gold");
                    g.CloseOverlay(); g.Player.Gold = gold; g.Player.DashChargesMax = dashMax; g.Player.DashCharges = dash;
                }

                step = "warp stone removal";
                {
                    var pos = g.Player.Pos; int gold = g.Player.Gold, carried = g.Player.WarpStones;
                    var stone = new WarpStone { id = "smoke_remove", name = "철거 검증", x = 50, y = 50 };
                    g.World.warpStones.Add(stone);
                    if (g.RemoveWarpStone(stone)) Fail("remote removal allowed");
                    g.Player.Pos = stone.Pos;
                    if (!g.RemoveWarpStone(stone)) Fail("nearby removal failed");
                    if (g.Player.Gold != gold + DB.Progression.warpStoneCost / 2 || g.Player.WarpStones != carried) Fail("removal refund or stone consumption incorrect");
                    if (g.World.warpStones.Any(w => w.id == stone.id) || g.RemoveWarpStone(stone)) Fail("removed stone can be refunded twice");
                    if (g.RemoveWarpStone(new WarpStone { id = Game.TownWarpId })) Fail("town stone removal allowed");
                    g.Player.Pos = pos; g.Player.Gold = gold;
                }

                step = "grave inheritance preview";
                {
                    var ability = new AbilityDef { id = "smoke_legacy", name = "기억 전승 검증", rules = { new RuleDef
                    {
                        kind = RuleKind.Replace, replaces = "Dash", ops = { new OpDef { op = "ReplaceWith", mode = "Blink" } },
                    } } };
                    var grave = new GraveRecord { heroName = "검증 영웅", abilities = { AbilityRecord.From(ability) } };
                    var it = Object.Instantiate(DB.Asset.interactablePrefab);
                    it.kind = "grave"; it.data = grave;
                    Dialogue.Open(g, it); yield return Frames(2);
                    int owned = g.Player.Abilities.Owned.Count;
                    Dialogue.Current.options[0].act(); yield return Frames(2);
                    if (grave.recovered || g.Player.Abilities.Owned.Count != owned || string.IsNullOrWhiteSpace(Dialogue.Current.text)) Fail("preview consumed ability or omitted description");
                    Dialogue.Current.options[1].act(); yield return Frames(2);
                    if (grave.recovered) Fail("back from preview consumed grave");
                    Dialogue.Current.options[0].act(); yield return Frames(2);
                    Dialogue.Current.options[0].act(); yield return Frames(2);
                    if (!grave.recovered || g.Player.Abilities.Owned.Count != owned + 1) Fail("inheritance confirmation failed");
                    g.Player.Abilities.Owned.RemoveAll(a => a.name == ability.name);
                    Object.Destroy(it.gameObject);
                }

                // Attacks and both dash variants pause movement without losing the map destination.
                step = "replacement dash travel";
                {
                    var p = g.Player;
                    var replacement = new AbilityDef { id = "smoke_replace_dash", rules = { new RuleDef
                    {
                        kind = RuleKind.Replace, replaces = "Dash", ops = { new OpDef { op = "ReplaceWith", mode = "Blink" } },
                    } } };
                    p.Abilities.Add(replacement);
                    p.SetTravelTarget(p.Pos + Vector2.right * 8);
                    p.DashCharges = p.DashChargesMax;
                    p.TryDash(Vector2.left);
                    if (!p.TravelTarget.HasValue) Fail("replacement dash lost map travel");
                    p.Abilities.Clear(); p.RecalcStats();
                    var destination = p.TravelTarget;
                    p.TryDash(Vector2.left);
                    if (p.TravelTarget != destination) Fail("normal dash lost map travel");
                    yield return new WaitForSeconds(0.4f);
                    typeof(Player).GetMethod("BasicAttack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(p, new object[] { Vector2.up });
                    if (p.TravelTarget != destination) Fail("basic attack lost map travel");
                    yield return new WaitForSeconds(1f);
                    if (p.TravelTarget.HasValue && !p.Moving) Fail("movement did not resume after attack");
                    p.LearnNextSkill();
                    var skill = p.Skills.FirstOrDefault(s => s.def != null);
                    if (skill != null)
                    {
                        skill.cd = 0;
                        typeof(Player).GetMethod("CastSkill", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .Invoke(p, new object[] { System.Array.IndexOf(p.Skills, skill) });
                        if (p.TravelTarget != destination) Fail("skill cast lost map travel");
                        p.LearnNextSkill();
                        var followup = p.Skills.FirstOrDefault(s => s.def != null && s != skill);
                        if (followup != null)
                        {
                            followup.cd = 0;
                            p.DashCharges = 2;
                            p.TryDash(Vector2.up);
                            if (!p.IsDashing || p.DashCharges != 1) Fail("dash blocked during skill motion");
                            typeof(Player).GetMethod("CastSkill", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                .Invoke(p, new object[] { System.Array.IndexOf(p.Skills, followup) });
                            if (followup.cd <= 0) Fail("skill blocked during dash/skill motion");
                            p.TryDash(Vector2.left);
                            if (p.DashCharges != 0) Fail("second dash blocked during dash motion");
                            // Readiness still applies while animation locks no longer block other actions.
                            float cooldown = followup.cd;
                            typeof(Player).GetMethod("CastSkill", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                .Invoke(p, new object[] { System.Array.IndexOf(p.Skills, followup) });
                            if (followup.cd != cooldown) Fail("skill cooldown bypassed");
                        }
                        yield return new WaitForSeconds(1f);
                    }
                    p.StopMoving();
                }

                step = "stack limits and removal";
                {
                    var p = g.Player;
                    var a = new AbilityDef { id = "stack_regression", name = "스택 검증",
                        states = { new StateDef { name = "count", label = "중첩", max = 3 } },
                        rules = { new RuleDef { kind = RuleKind.Trigger, trigger = "Damaged",
                            ops = { new OpDef { op = "AddState", state = "count", amount = 10 } } } } };
                    p.Abilities.Add(a);
                    p.Abilities.Fire(new CombatEvent { type = Trig.Damaged, source = p, position = p.Pos });
                    if (p.Abilities.GetState(a, "count") != 3) Fail("stack exceeded its maximum");
                    if (!a.Explain().Contains("최대 3")) Fail("stack description omitted its maximum");
                    p.Abilities.Remove(a);
                    if (p.Abilities.Owned.Contains(a) || p.Abilities.GetState(a, "count") != 0) Fail("ability removal retained state");
                }

                step = "developer level progression";
                {
                    var p = g.Player;
                    int oldLevel = p.Level, oldPending = p.PendingEvolutions, oldPoints = p.UnspentAttr;
                    int learned = p.LearnedSkills;
                    int target = p.NextEvolveLevel + 5, expectedEvolutions = 0;
                    for (int lv = oldLevel + 1; lv <= target; lv++) if (DB.Progression.EvolvesAt(lv)) expectedEvolutions++;
                    p.DebugSetLevel(target);
                    if (p.Level != target || p.PendingEvolutions != oldPending + expectedEvolutions)
                        Fail("developer levels omitted evolution rewards");
                    int awarded = p.PendingEvolutions;
                    p.DebugSetLevel(target);
                    if (p.PendingEvolutions != awarded) Fail("same developer level awarded duplicate evolutions");
                    p.DebugSetLevel(oldLevel);
                    if (p.Level != oldLevel || p.PendingEvolutions != awarded) Fail("level decrease corrupted earned evolutions");
                    p.PendingEvolutions = oldPending; p.UnspentAttr = oldPoints; p.RestoreSkills(learned);
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

                // 4b) Mid-life design edit from the pause menu: look changes, chassis and numbers don't
                step = "design in game";
                var kindBefore = g.Player.Weapon.kind;
                float damageBefore = g.Player.Weapon.baseDamage;
                g.OpenOverlay(GameState.Paused);
                yield return Frames(2);
                Click(Find<PauseView>(), "Design");
                yield return Frames(2);
                if (!Visible<DesignStudioView>()) { Fail("design studio did not open from the pause menu"); yield break; }
                var gameStudio = Find<DesignStudioView>();
                Click(gameStudio, "Tab 2");
                gameStudio.GetComponentInChildren<InputField>().text = "가을 단풍 숲";
                Click(gameStudio, "Tab 1");
                gameStudio.GetComponentInChildren<InputField>().text = "보라색 몽둥이, 묵직하게";
                Click(gameStudio, "Generate");
                yield return Frames(2);
                Click(gameStudio, "StartLife");     // "적용하고 닫기"
                yield return Frames(5);
                if (Visible<DesignStudioView>()) Fail("design studio did not close after applying");
                if (!g.Player.Costume.id.StartsWith(DesignStore.CostumePrefix)) Fail("in-game design did not redraw the hero");
                if (g.Player.Weapon.kind != kindBefore || g.Player.Weapon.baseDamage != damageBefore) Fail("in-game design changed the weapon chassis/numbers");
                if (DesignRuntime.Active.world.IsDefault) Fail("in-game world design not active");
                g.CloseOverlay();
                yield return Frames(30);

                // 4c) A fully pictured design (synthetic pictures, no network): ground textures, scenery, town, effects, enemies, poses
                step = "pictured design";
                var pictured = DesignTestData.Pictured(g.Player);
                g.ApplyDesign(DesignStore.Commit(pictured));
                yield return Frames(40);
                // City layout: a street grid on open land, buildings lining the streets
                bool street = false;
                for (int t = 24; t < 200 && !street; t += 12) street |= WorldStreamer.I.GroundAtTile(t, -40) == Ground.Path;
                if (!street) Fail("city layout laid no streets");
                if (!Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Any(s => s.name == "building" || s.name == "decor" || s.name == "prop")) Fail("no designed scenery was placed");
                if (Art.Enemy("shroom", 1).rect.height < 10) Fail("enemy moving pose missing");
                var designed = DesignCombat.For(DesignRuntime.Active);
                if (designed == null || Art.Slash(designed.slashColor, designed.slashCore, 20, 140, 1).Length != 5) Fail("designed swing effect not used");
                if (!DesignRuntime.GroundTexel(Ground.Grass, 3, 5, out _)) Fail("ground texture not used");
                if (g.Player.Weapon.kind != kindBefore) Fail("pictured design changed the weapon chassis");
                yield return Frames(20);

                // 5) Death → MainMenu design studio
                step = "death";
                g.Player.invulnUntil = 0;
                g.Player.TakeDamage(new DamageInfo { amount = 999999, direction = Vector2.right, noEvents = true });
                for (int i = 0; i < 400 && !Visible<DeathView>(); i++) yield return null;
                if (!Visible<DeathView>()) { Fail("death screen not visible"); yield break; }
                Click(Find<DeathView>(), "NextLife");
                yield return Frames(10);
                if (Find<MenuUI>() == null || !Visible<DesignStudioView>()) { Fail("did not return to the design studio"); yield break; }

                // 6) New life → pause → to title (saved) → continue
                step = "continue";
                Click(Find<DesignStudioView>(), "StartLife");
                yield return Frames(10);
                if (Game.I == null) { Fail("Game scene did not load the 2nd time"); yield break; }
                string designedCostume = Game.I.Player.Costume.id;
                Game.I.Player.Gold = 77;
                Game.I.Player.WarpStones = 3;
                Game.I.Player.LearnNextSkill();
                var savedAbility = RuleSamples.FireSource();
                savedAbility.EnsureLists(); RuleValidator.Tag(savedAbility);
                Game.I.Player.Abilities.Add(savedAbility);
                string savedSignature = savedAbility.Signature();
                Game.I.OpenOverlay(GameState.Paused);
                yield return Frames(2);
                Click(Find<PauseView>(), "ToTitle");
                yield return Frames(10);
                if (!Visible<TitleView>()) { Fail("to-title did not show the title"); yield break; }
                Click(Find<TitleView>(), "ChangeSlot");
                yield return Frames(2);
                var slotRow = Find<SaveSelectView>().transform.Find("Panel/Slots/Slot 0");
                if (slotRow.Find("Abandon") == null || !slotRow.Find("Abandon").gameObject.activeInHierarchy) Fail("a world with a life in progress offers no 삶 끝내기");
                Click(slotRow, "Play");              // 이어하기
                yield return Frames(10);
                if (Game.I == null || Game.I.Player == null) { Fail("continue did not load the life"); yield break; }
                if (Game.I.Player.Gold != 77) Fail($"continued life lost its state (gold {Game.I.Player.Gold})");
                if (Game.I.Player.Costume.id != designedCostume) Fail("continued life lost its designed hero");
                if (Game.I.Player.WarpStones != 3) Fail("continued life lost carried warp stones");
                if (Game.I.Player.LearnedSkills != 1) Fail("continued life lost learned skills");
                if (!Game.I.Player.Abilities.Owned.Any(a => a.Signature() == savedSignature)) Fail("continued life lost RuleIR ability");
            }

            /// <summary>Offline checks of the picture → sprite pieces (frames, effects, prompt detection).</summary>
            static string DesignUnits()
            {
                if (!DesignGuard.BareHanded("주먹으로 때리고 발차기") || DesignGuard.BareHanded("종으로 음파 공격")) return "bare-hand detection wrong";
                if (!DesignGuard.IsTweak("조금 더 길게") || DesignGuard.IsTweak("종으로 무기를 바꿔줘")) return "tweak detection wrong";
                // A fake 2x2 sheet: four blobs of different heights → shared scale, every pose drawable
                var sheet = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                var px = new Color32[64 * 64];
                for (int i = 0; i < 4; i++)
                    for (int y = 4; y < 20 + i * 2; y++)
                        for (int x = 8; x < 20; x++) px[((i / 2 == 0 ? 32 : 0) + y) * 64 + (i % 2) * 32 + x] = new Color32((byte)(60 * i), 120, 200, 255);
                sheet.SetPixels32(px); sheet.Apply();
                var cells = Pixelizer.ConvertSet(sheet, new[] { new RectInt(0, 32, 32, 32), new RectInt(32, 32, 32, 32), new RectInt(0, 0, 32, 32), new RectInt(32, 0, 32, 32) }, 28, true);
                if (cells.Any(c => c == null)) return "pixelizer lost a pose";
                if (cells[0].H != 30 || cells[3].H <= cells[0].H) return $"poses not on one scale ({cells[0].H}, {cells[3].H})";
                var frames = cells.Select(c => c.ToTexture()).ToArray();
                foreach (Pose pose in System.Enum.GetValues(typeof(Pose)))
                    if (DesignArt.HeroPose(frames, pose, false) == null) return "hero pose missing: " + pose;
                if (DesignArt.Effect(frames[0], DesignArt.EffectKind.Swing, 40).Length != 5) return "swing effect frames wrong";
                if (DesignArt.EnemyFrame(frames[0], frames[1], frames[2], 2) == null || DesignArt.EnemyFrame(frames[0], null, null, 3) == null) return "enemy frame missing";
                var mirrored = DesignStore.Image(DesignStore.Mirrored(DesignStore.SaveImage(cells[0])));
                if (mirrored == null || mirrored.width != cells[0].W) return "mirroring a picture failed";
                Object.DestroyImmediate(sheet);

                // A neighbour's figure spilling over the grid line is cut away; a pose drawn facing the other way is turned back
                var spill = new Texture2D(64, 32, TextureFormat.RGBA32, false);
                var sp = new Color32[64 * 32];
                for (int y = 4; y < 28; y++) for (int x = 10; x < 20; x++) sp[y * 64 + x] = new Color32(200, 80, 80, 255);    // subject, cell 0
                for (int y = 4; y < 28; y++) sp[y * 64 + 8] = new Color32(200, 80, 80, 255);                                     // its arm (left)
                for (int y = 2; y < 26; y++) for (int x = 26; x < 32; x++) sp[y * 64 + x] = new Color32(80, 80, 200, 255);    // neighbour spill at the edge
                // cell 1: an L-shaped pose, mirrored relative to cell 0's arm side
                for (int y = 4; y < 28; y++) for (int x = 44; x < 54; x++) sp[y * 64 + x] = new Color32(200, 80, 80, 255);
                for (int y = 4; y < 28; y++) sp[y * 64 + 55] = new Color32(200, 80, 80, 255);                                    // arm on the right
                spill.SetPixels32(sp); spill.Apply();
                var two = Pixelizer.ConvertSet(spill, new[] { new RectInt(0, 0, 32, 32), new RectInt(32, 0, 32, 32) }, 24, true, alignFacing: true);
                Object.DestroyImmediate(spill);
                if (two[0] == null || two[1] == null) return "pixelizer lost a cell";
                bool blue = false;
                foreach (var c in two[0].Px) if (c.a > 0 && c.b > c.r + 40) blue = true;
                if (blue) return "a neighbouring figure that spilled into the cell was kept";
                if (Pixelizer.FacesOpposite(two[1], two[0])) return "a pose facing the other way was not turned around";
                return null;
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

            string CheckAttackInput(Player p)
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var input = typeof(Player).GetMethod("HandleAttackInput", flags);
                var queued = typeof(Player).GetMethod("TryQueuedAttack", flags);
                var timers = typeof(Player).GetMethod("UpdateTimers", flags);
                float recovery = p.Weapon.combo.Max(s => s.windup + s.recovery) / p.Stats[StatId.AttackSpeed] + 0.2f;
                int attacks = 0;
                void Count(CombatEvent ev) { if (ev.source == p && ev.type == Trig.Attack) attacks++; }
                void Input(bool pressed, bool held) => input.Invoke(p, new object[] { pressed, held, Vector2.right });
                void Recover() { timers.Invoke(p, new object[] { recovery }); queued.Invoke(p, null); }
                GameEvents.Combat += Count;
                try
                {
                    // One physical click spans several frames before its release.
                    Input(true, true);
                    for (int frame = 0; frame < 5; frame++) Input(false, true);
                    Input(false, false);
                    Recover();
                    if (attacks != 1) return "single click repeated an attack after release";

                    Input(true, true);
                    Recover();
                    Input(false, true);
                    if (attacks != 3) return "holding attack stopped repeating";
                    Input(false, false);
                    Recover();
                    if (attacks != 3) return "held attack left an extra attack after release";

                    Input(true, true);
                    Input(true, true); // A second distinct click during recovery should still buffer.
                    Input(false, false);
                    Recover();
                    if (attacks != 5) return "second click during recovery was lost";
                    Recover();
                    if (attacks != 5) return "buffered click executed more than once";

                    p.DashCharges = 2;
                    p.TryDash(Vector2.right);
                    Input(true, true);
                    queued.Invoke(p, null);
                    if (attacks != 5) return "attack fired during dash";
                    typeof(Player).GetMethod("UpdateMovement", flags).Invoke(p, new object[] { 0.2f });
                    Recover();
                    if (attacks != 6) return "click buffered during dash was lost";
                    return null;
                }
                finally
                {
                    GameEvents.Combat -= Count;
                    p.StopAllCoroutines();
                }
            }

            string Exercise(WeaponKind wk)
            {
                var p = Game.I.Player;
                if (p == null || !p.Alive) return "no live player";
                if (p.Weapon.kind != wk) return "wrong weapon";
                if (ReferenceEquals(p.Weapon, DB.W(wk))) return "player shares the asset's WeaponDef (smith upgrades would edit the asset)";
                if (Object.FindFirstObjectByType<DeveloperMode>() == null) return "developer tools missing";
                var attackInputError = CheckAttackInput(p);
                if (attackInputError != null) return attackInputError;

                if (wk == WeaponKind.Crossbow)
                {
                    var clone = Clone.Spawn(p, p.Pos + Vector2.left * 2, 1, 1, true, false, Element.None, "REGRESSION", 0);
                    p.AimPoint = p.Pos + Vector2.up * 5;
                    int before = Projectile.Live.Count;
                    clone.Mimic(Vector2.right, p.Weapon.combo[0]);
                    var bolts = Projectile.Live.Skip(before).ToArray();
                    if (bolts.Length != p.Weapon.combo[0].projectileCount) return "crossbow clone did not shoot bolts";
                    var expected = (p.AimPoint - (clone.Pos + Vector2.up * 0.45f)).normalized;
                    if (Vector2.Dot(bolts[bolts.Length / 2].vel.normalized, expected) < 0.9f) return "clone did not aim from its position to cursor";
                    Object.Destroy(clone.gameObject);
                }

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
