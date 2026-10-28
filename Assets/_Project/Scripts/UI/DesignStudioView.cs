using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// Design studio: the player writes what their hero / weapon & attack / world / enemies should look like, the AI
    /// (or the free local reader) turns it into a DesignPack, and the preview is drawn locally right away.
    /// MainMenu: replaces the character select before a new life (the chassis = which existing weapon/perk it plays as).
    /// Game (from the pause menu): edits the look mid-life; the chassis stays locked so nothing about balance changes.
    /// </summary>
    public class DesignStudioView : MonoBehaviour
    {
        [SerializeField] Button[] tabs = new Button[4];      // hero, weapon & attack, world, enemies
        [SerializeField] Text tabHint;
        [SerializeField] InputField prompt;
        [SerializeField] Button generate, undo, resetTab;
        [SerializeField] Text info, status;
        [Header("Preview")]
        [SerializeField] Image hero, heroWeapon, projectile, slash, ground, tree, decor2;
        [SerializeField] Button flipWeapon;            // mirror the weapon picture when the image model drew it backwards
        [SerializeField] Button fixEnemies;            // repair older enemy pictures (cut-off neighbours, poses facing the wrong way)
        [SerializeField] Button flipEffects;           // turn the swing / muzzle effect around when it still plays backwards
        [SerializeField] Image[] enemies = new Image[3];
        [SerializeField] Text enemyNames;
        [Header("Chassis (new life only)")]
        [SerializeField] GameObject chassisRow;
        [SerializeField] Text weaponBase, perkBase;
        [SerializeField] Button weaponBaseButton, perkBaseButton;
        [Header("Buttons")]
        [SerializeField] Button startButton, backButton, classicButton;
        [SerializeField] Color selectedTint = new Color(1f, 0.95f, 0.7f);

        static readonly string[] Hints =
        {
            "어떤 캐릭터로 살고 싶은지 자유롭게 적어 주세요. 토끼가 아니어도 돼요.",
            "무기와 싸우는 방식을 적어 주세요. 공격 동작과 스킬이 새로 정해져요.",
            "세계의 분위기와 땅·물·나무·바위의 모습을 적어 주세요.",
            "적들의 모습과 테마를 적어 주세요. (공격 패턴과 크기는 그대로)",
        };
        static readonly string[] Examples =
        {
            "예) 마법사 모자를 쓴 보라색 문어",
            "예) 은색 권총을 들고 총알을 빠르게 발사",
            "예) 사탕으로 만든 숲과 우유가 흐르는 강",
            "예) 모든 적이 태엽 장난감 병정과 로봇",
        };
        static readonly string[] EnemySamples = { "shroom", "fox", "wisp" };

        MenuUI menu;
        Game game;
        bool InGame => game != null;

        DesignPack working, original;
        readonly Stack<DesignPack> history = new Stack<DesignPack>();
        readonly string[] drafts = { "", "", "", "" };
        readonly string[] sent = { "", "", "", "" };
        int tab;
        bool busy;
        string statusText = "";
        InputField prevTyping;

        public bool IsOpen => gameObject.activeSelf;
        static bool Custom => Session.Mode == GameMode.Custom;
        string openedFor;   // the save slot the work in progress belongs to

        public void Init(MenuUI m)
        {
            menu = m;
            BindCommon();
            UIKit.Bind(startButton, StartLife);
            UIKit.Bind(backButton, () => menu.Show(MenuUI.Screen.Slots));   // the studio is opened from a world on the slot screen
        }

        public void InitInGame(Game g)
        {
            game = g;
            BindCommon();
            UIKit.Bind(startButton, ApplyInGame);
            UIKit.Bind(backButton, Close);
            UIKit.Show(gameObject, false);
        }

        void BindCommon()
        {
            for (int i = 0; i < tabs.Length; i++) { int k = i; UIKit.Bind(tabs[i], () => SelectTab(k)); }
            UIKit.Bind(generate, Generate);
            UIKit.Bind(fixEnemies, () =>
            {
                if (busy) return;
                Push();
                busy = true;
                statusText = "적 그림 정리 중…";
                StartCoroutine(DesignAi.RepairEnemies(working, s => { busy = false; statusText = s; Preview(); }));
            });
            UIKit.Bind(flipEffects, () =>
            {
                if (busy) return;
                Push();
                working.attack.flipEffects = !working.attack.flipEffects;
                Preview();
            });
            UIKit.Bind(flipWeapon, () =>
            {
                if (busy || working.weapon.image == null) return;
                Push();
                working.weapon.image = DesignStore.Mirrored(working.weapon.image);
                Preview();
            });
            UIKit.Bind(undo, () => { if (history.Count > 0 && !busy) { working = history.Pop(); Preview(); } });
            UIKit.Bind(resetTab, () =>
            {
                if (busy) return;
                Push();
                working.CopySection(DesignPack.Default(), tab);
                if (tab == 1 && InGame) working.weapon.baseWeapon = original.weapon.baseWeapon;
                drafts[tab] = sent[tab] = "";
                prompt.SetTextWithoutNotify("");
                Preview();
            });
            prompt.onValueChanged.RemoveAllListeners();
            prompt.onValueChanged.AddListener(v => drafts[tab] = DesignGuard.CleanPrompt(v));
            prompt.characterLimit = DesignGuard.MaxPrompt;
            // Designs are made from scratch: no preset character / weapon / perk pickers
            UIKit.Show(classicButton, false);
            UIKit.Show(weaponBaseButton, false);
            UIKit.Show(perkBaseButton, false);
        }

        // ───────────────────────── Open / close ─────────────────────────

        void OnEnable()
        {
            if (Session.World == null) return;
            // Menu: start from the world's look once per visit; coming back from the classic select keeps the work in progress
            if (!InGame)
            {
                string slot = Session.Mode + "_" + Session.Slot;
                if (working == null || openedFor != slot) { openedFor = slot; Begin(DesignStore.OfWorld(Session.World).Clone()); }
                else Preview();
            }
            prevTyping = Controls.TypingField;
            Controls.TypingField = prompt;
        }

        void OnDisable()
        {
            if (Controls.TypingField == prompt) Controls.TypingField = prevTyping;
        }

        /// <summary>Pause menu → studio: start from the look this life is drawn with; attack parts, skills and passive stay locked.</summary>
        public void OpenInGame()
        {
            var p = game.Player;
            if (p == null) return;
            var pack = (DesignStore.PackOfCostume(p.Costume.id) ?? DesignStore.WorldOnly(DesignStore.OfWorld(Session.World))).Clone();
            pack.weapon.baseWeapon = p.Weapon.kind.ToString();
            if (!p.Costume.id.StartsWith(DesignStore.CostumePrefix)) pack.hero.basePerk = p.Costume.id;
            UIKit.Show(gameObject, true);
            Begin(pack);
        }

        void Begin(DesignPack pack)
        {
            original = pack.Clone();
            working = pack;
            history.Clear();
            for (int i = 0; i < 4; i++) drafts[i] = sent[i] = working.prompts[i] ?? "";
            statusText = DesignAi.Available ? $"AI 디자인: {AiClient.ModelLabel(Game.Settings.aiModel)}" : "AI 꺼짐 — 무료 로컬 해석으로 만들어요";
            busy = false;
            UIKit.Label(startButton, InGame ? "적용하고 닫기" : "삶을 시작하기");
            UIKit.Label(backButton, InGame ? "취소" : "뒤로");
            // Normal mode: only the character and weapon tabs
            for (int i = 2; i < tabs.Length; i++) UIKit.Show(tabs[i], Custom);
            if (!Custom && tab >= 2) tab = 0;
            SelectTab(tab);
            Preview();
        }

        void Close()
        {
            StopAllCoroutines();
            busy = false;
            // Cancel: draw the life's own look again
            if (InGame && game.Player != null)
            {
                DesignRuntime.Activate(DesignStore.PackOfCostume(game.Player.Costume.id) ?? DesignStore.WorldOnly(DesignStore.OfWorld(Session.World)));
                DesignRuntime.Skin(game.Player.Weapon);
                game.Player.RefreshLook();
            }
            UIKit.Show(gameObject, false);
        }

        void SelectTab(int k)
        {
            tab = Mathf.Clamp(k, 0, 3);
            prompt.SetTextWithoutNotify(drafts[tab]);
            if (prompt.placeholder is Text ph) ph.text = Examples[tab];
            UIKit.Set(tabHint, Hints[tab]);
            Sfx.Play("ui");
        }

        // ───────────────────────── Generate ─────────────────────────

        void Push() => history.Push(working.Clone());

        void Generate()
        {
            if (busy) return;
            // Every tab whose prompt changed goes into ONE request; with nothing changed, the current tab is redone
            // Normal mode designs only the hero and the weapon (the world and enemies stay hand-made)
            var sections = Enumerable.Range(0, 4).Select(i => drafts[i].Length > 0 && drafts[i] != sent[i] && (Custom || i < 2)).ToArray();
            if (!sections.Any(s => s))
            {
                if (drafts[tab].Length == 0) { statusText = "<color=#ffb36b>먼저 원하는 모습을 적어 주세요.</color>"; return; }
                sections[tab] = true;
            }
            Push();
            for (int i = 0; i < 4; i++) if (sections[i]) working.prompts[i] = drafts[i];
            var power = Enumerable.Range(0, 4).Where(i => sections[i]).SelectMany(i => DesignGuard.PowerRequests(drafts[i])).ToList();

            if (DesignAi.Available)
            {
                busy = true;
                statusText = "AI가 디자인하는 중…";
                StartCoroutine(DesignAi.Generate(this, working, sections, InGame, s => statusText = s, (result, why) =>
                {
                    busy = false;
                    if (result == null)
                    {
                        result = DesignLocal.Generate(working, sections, InGame);
                        why = $"<color=#ffb36b>{why}</color> → 로컬 해석으로 대신 만들었어요";
                    }
                    else
                    {
                        Session.World.designCalls++;
                        Session.World.designCostUsd += DesignAi.LastCostUsd;
                    }
                    Done(result, sections, power, why);
                }));
            }
            else Done(DesignLocal.Generate(working, sections, InGame), sections, power, "로컬 해석 완료 (무료)");
        }

        void Done(DesignPack result, bool[] sections, List<string> power, string why)
        {
            // The local detector and the AI flag the same request in different words: keep one list
            result.ignored = (power.Count > 0 ? power : result.ignored ?? new List<string>()).Distinct().Take(6).ToList();
            working = result;
            for (int i = 0; i < 4; i++) if (sections[i]) sent[i] = drafts[i];
            statusText = why;
            Preview();
            Sfx.Play("ui_select");
        }

        // ───────────────────────── Commit ─────────────────────────

        void StartLife()
        {
            if (busy) return;
            var committed = DesignStore.Commit(working);
            Session.World.designId = committed.id;
            SaveSystem.SaveWorld(Session.World);
            Session.NewLife(DesignStore.CostumeOf(committed), DesignRuntime.Chassis(committed));
        }

        void ApplyInGame()
        {
            if (busy) return;
            var committed = DesignStore.Commit(working);
            Session.World.designId = committed.id;
            game.ApplyDesign(committed);
            UIKit.Show(gameObject, false);
        }

        // ───────────────────────── Preview (drawn locally, free) ─────────────────────────

        CostumeDef previewCostume;

        readonly Sprite[] previewDecor = new Sprite[2];

        void Preview()
        {
            DesignRuntime.Activate(working);
            previewCostume = DesignStore.PreviewCostume(working);
            // The world's own scenery: its first large and first medium object (hand-made tree / rock otherwise)
            var w = working.world;
            for (int k = 0; k < 2; k++)
            {
                previewDecor[k] = null;
                if (!w.HasDecor) continue;
                string want = k == 0 ? "large" : "medium";
                for (int i = 0; i < 12 && previewDecor[k] == null; i++)
                {
                    string size = w.decorSizes != null && w.decorSizes[i] != null ? w.decorSizes[i] : DesignLayout.Sizes[Mathf.Min(3, i / 3)];
                    if (size == want && w.decorImages[i] != null) previewDecor[k] = DesignStore.Sprite(w.decorImages[i], new Vector2(0.5f, 0.5f));
                }
            }
        }

        void Update()
        {
            if (working == null) return;
            // Esc / any state change closes the pause menu → the studio cancels with it
            if (InGame && (game.State != GameState.Paused || game.Player == null)) { Close(); return; }

            float t = Time.unscaledTime;
            // The hero loops through what it does in play: walk, breathe, wind up and strike
            float phase = t % 4f;
            var pose = phase < 2f ? (Pose)((int)Pose.Run0 + (int)(t * 8) % 4)
                     : phase < 3f ? ((int)(t * 2.2f) % 2 == 0 ? Pose.Idle0 : Pose.Idle1)
                     : phase < 3.3f ? Pose.AttackWind : phase < 3.7f ? Pose.AttackHit : Pose.Idle0;
            UIKit.Sprite(hero, Art.Rabbit(previewCostume, pose));
            var kind = DesignRuntime.Chassis(working);
            UIKit.Sprite(heroWeapon, DesignRuntime.WeaponHidden ? null : Art.Weapon(kind));
            UIKit.Show(flipWeapon, working.weapon.image != null && !DesignRuntime.WeaponHidden);
            UIKit.Show(fixEnemies, Custom && working.enemies.art.Any(a => a.image != null));
            UIKit.Show(flipEffects, working.attack.effectImages != null);
            var shot = working.attack.projectile;
            var b = DesignCombat.For(working) ?? DB.W(kind);
            bool shoots = shot.image != null || shot.shape != "builtin" || shot.pixels != null || b.combo.Any(s => s.projectile) || kind == WeaponKind.Dagger;
            UIKit.Sprite(projectile, shoots ? (kind == WeaponKind.Dagger && shot.image == null && shot.shape == "builtin" && shot.pixels == null ? Art.Knife() : Art.Bolt()) : null);
            // Only the fully drawn frames of the swing (the first ones are still sweeping in and look cut off)
            var swing = Art.Slash(b.slashColor, b.slashCore, 14, 160, 1);
            UIKit.Sprite(slash, swing[Mathf.Min(swing.Length - 1, swing.Length / 2 + (int)(t * 5) % Mathf.Max(1, swing.Length - swing.Length / 2))]);
            UIKit.Sprite(ground, DesignArt.GroundSample());
            UIKit.Sprite(tree, previewDecor[0] ?? Art.Tree(Biome.Meadow, 1));
            UIKit.Sprite(decor2, previewDecor[1] ?? Art.Rock(Biome.Meadow, 0));
            for (int i = 0; i < enemies.Length && i < EnemySamples.Length; i++)
                UIKit.Sprite(enemies[i], Art.Enemy(EnemySamples[i], (int)(t * 3 + i) % 2));
            UIKit.Set(enemyNames, string.Join(" · ", EnemySamples.Select(id => DesignRuntime.EnemyName(id, DB.Enemy(id).name))));

            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].image.color = i == tab ? selectedTint : Color.white;
                bool dirty = drafts[i].Length > 0 && drafts[i] != sent[i];
                UIKit.Label(tabs[i], TabNames[i] + (dirty ? " *" : ""));
            }
            generate.interactable = !busy;
            undo.interactable = !busy && history.Count > 0;
            UIKit.Label(generate, busy ? "만드는 중…" : Enumerable.Range(0, 4).Any(i => drafts[i].Length > 0 && drafts[i] != sent[i]) ? "디자인 만들기" : "다시 만들기");

            UIKit.Set(info, Describe());
            var w = Session.World;
            UIKit.Set(status, statusText + (w != null && w.designCalls > 0 ? $"\n<color=#6b5c8a>이 세계의 AI 디자인 {w.designCalls}회 · 누적 약 ${w.designCostUsd:0.0000}</color>" : ""));

            // Combat and starting passive are generated, never picked from presets; numbers are the engine's
            var qwer = string.Join(" ", b.skills.Take(4).Select((id, i) => "QWER"[i] + " " + SkillDB.Get(id)?.name));
            UIKit.Set(weaponBase, $"공격 <color=#fff2b3>{DesignCombat.Describe(working.weapon)}</color>  <color=#8c7aa8>{qwer}</color>");
            var perk = working.hero.perk;
            UIKit.Set(perkBase, perk != null && !string.IsNullOrEmpty(perk.name)
                ? $"시작 능력 <color=#fff2b3>{perk.name}</color>"
                : "시작 능력 <color=#8c7aa8>삶을 시작하면 캐릭터 설명으로 만들어져요 (밸런스 검증)</color>");
        }

        static readonly string[] TabNames = { "캐릭터", "무기·공격", "세계", "적" };

        static string Ko(string v) => v switch
        {
            "none" => "없음", "crown" => "왕관", "wizard" => "마법사 모자", "cap" => "모자", "band" => "머리띠", "horns" => "뿔", "flower" => "꽃",
            "helmet" => "투구", "halo" => "후광", "ribbon" => "리본", "leaf" => "새싹", "glasses" => "안경",
            "builtin" => "기본", "blade" => "검", "curved" => "곡도", "needle" => "가는 막대", "stick" => "막대", "spear" => "창", "axe" => "도끼",
            "hammer" => "망치", "club" => "몽둥이", "wand" => "지팡이", "bow" => "활",
            "Hand" => "손", "Mouth" => "입", "Eyes" => "눈", "Above" => "머리 위", "Hidden" => "숨김",
            "weapon" => "무기 모양", "bolt" => "화살", "orb" => "구슬", "star" => "별", "shard" => "결정", "ring" => "고리", "wave" => "검기",
            "heart" => "하트", "note" => "음표", "bubble" => "방울",
            _ => v,
        };

        string Describe()
        {
            var p = working;
            string body = tab switch
            {
                0 => p.hero.image != null
                    ? $"<b>{p.hero.name ?? "이름 없는 영웅"}</b> · {p.hero.species ?? "새 캐릭터"} <color=#8c7aa8>(새로 그림)</color>"
                    : $"<b>{p.hero.name ?? "이름 없는 토끼"}</b> · 머리: {Ko(p.hero.hat)} <color=#8c7aa8>(기본 도트)</color>",
                1 => $"<b>{(DesignCombat.For(p) ?? DB.W(DesignRuntime.Chassis(p))).name}</b> · {Ko(p.weapon.hold)}에 들기"
                     + (p.weapon.image != null ? " <color=#8c7aa8>(새로 그림)</color>" : p.weapon.pixels != null ? " (도트 직접 그림)" : $" · {Ko(p.weapon.shape)}")
                     + $"\n발사: {Ko(p.attack.origin)}에서" + (p.attack.projectile.image != null ? " · 투사체 새로 그림" : $" · 투사체 {Ko(p.attack.projectile.shape)}"),
                2 => p.world.IsDefault ? "기본 세계" : $"<b>{p.world.mood ?? "새로운 세계"}</b>"
                     + Swatches(("풀", p.world.grass), ("길", p.world.path), ("물", p.world.water), ("거친 땅", p.world.rough), ("숲", p.world.foliage))
                     + (p.world.HasTiles || p.world.HasDecor || p.world.HasTown
                        ? $"\n<color=#8c7aa8>새로 그림: {string.Join(", ", new[] { p.world.HasTiles ? "땅" : null, p.world.HasDecor ? $"풍경 {p.world.decorImages.Count(s => s != null)}종 ({LayoutKo(p.world.layout)} 배치)" : null, p.world.HasTown ? "마을 건물" : null }.Where(s => s != null))}" + (p.world.ambient != "none" ? $" · 공기 중 {p.world.ambient}" : "") + "</color>" : ""),
                _ => p.enemies.Identity && p.enemies.names.Count == 0 && p.enemies.art.Count == 0 ? "기본 적"
                     : $"<b>{p.enemies.theme ?? "새 테마"}</b> · 새로 그린 적 {p.enemies.art.Count(a => a.image != null)}종 · 이름 바뀐 적 {p.enemies.names.Count}종",
            };
            var lines = new List<string> { body };
            if (!string.IsNullOrEmpty(p.summary)) lines.Add($"<color=#c9bcd8>{p.summary}</color>");
            foreach (var s in p.ignored) lines.Add($"<color=#ffb36b>✕ {s}</color>");
            return string.Join("\n", lines);
        }

        static string LayoutKo(string l) => l switch
        {
            "city" => "도시", "village" => "마을", "forest" => "숲", "farm" => "농장", "ruins" => "폐허", "wasteland" => "황무지", _ => "들판",
        };

        static string Swatches(params (string label, string hex)[] cols) =>
            string.Concat(cols.Where(c => !string.IsNullOrEmpty(c.hex)).Select(c => $"  <color={c.hex}>■</color>{c.label}"));
    }
}
