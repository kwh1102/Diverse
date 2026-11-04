using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// Design studio: the player writes what their hero and weapon & attack should look like, the AI
    /// (or the free local reader) turns it into a DesignPack, and the preview is drawn locally right away.
    /// MainMenu: replaces the character select before a new life (the chassis = which existing weapon/perk it plays as).
    /// Game (from the pause menu): edits the look mid-life; the chassis stays locked so nothing about balance changes.
    /// </summary>
    public class DesignStudioView : MonoBehaviour
    {
        [SerializeField] Button[] tabs = new Button[2];      // hero, weapon & attack
        [SerializeField] Text tabHint;
        [SerializeField] InputField prompt;
        [SerializeField] Button generate, undo, resetTab;
        [SerializeField] Text info, status;
        [Header("Preview")]
        [SerializeField] Image hero, heroWeapon, projectile, slash;
        [SerializeField] Button flipWeapon;            // mirror the weapon picture when the image model drew it backwards
        [SerializeField] Button flipEffects;           // turn the swing / muzzle effect around when it still plays backwards
        [Header("Chassis (new life only)")]
        [SerializeField] GameObject chassisRow;
        [SerializeField] Text weaponBase, perkBase;
        [SerializeField] Button weaponBaseButton, perkBaseButton;
        [Header("Buttons")]
        [SerializeField] Button startButton, backButton, classicButton;
        [SerializeField] Color selectedTint = new Color(1f, 0.95f, 0.7f);

        MenuUI menu;
        Game game;
        bool InGame => game != null;

        DesignPack working;
        readonly Stack<DesignPack> history = new Stack<DesignPack>();
        GameObject typeWindow;
        Text typeBalance, typeDescription;
        Image modalMotion;
        TravelAbilityPreview abilityPreview;
        float typePreviewStartedAt;
        readonly List<Button> typeRows = new List<Button>();
        bool busy;
        string statusText = "";
        InputField prevTyping;

        public bool IsOpen => gameObject.activeSelf;
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
            UIKit.Bind(tabs[0], OpenTypes);
            GameButtonStyle.Apply(tabs[0], ButtonRole.Category);
            UIKit.Show(tabs[1], false);
            UIKit.Show(resetTab, false);
            UIKit.Bind(generate, Generate);
            GameButtonStyle.Apply(generate, ButtonRole.Craft);
            GameButtonStyle.Apply(startButton, ButtonRole.Primary);
            UIKit.Bind(flipEffects, () =>
            {
                if (busy) return;
                Push();
                working.attack.flipEffects = !working.attack.flipEffects;
                Preview();
            });
            UIKit.Bind(flipWeapon, () =>
            {
                if (InGame || busy || working.weapon.image == null) return;
                Push();
                working.weapon.image = DesignStore.Mirrored(working.weapon.image);
                Preview();
            });
            UIKit.Bind(undo, () => { if (history.Count > 0 && !busy) { working = history.Pop(); Preview(); } });
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
                string slot = Session.Slot.ToString();
                if (working == null || openedFor != slot) { openedFor = slot; Begin(DesignStore.OfWorld(Session.World).Clone()); }
                else Preview();
            }
            prevTyping = Controls.TypingField;
            Controls.TypingField = prompt;
        }

        void OnDisable()
        {
            UIKit.Show(typeWindow, false);
            if (Controls.TypingField == prompt) Controls.TypingField = prevTyping;
        }

        /// <summary>Pause menu → studio: start from the look this life is drawn with; attack parts, skills and passive stay locked.</summary>
        public void OpenInGame()
        {
            var p = game.Player;
            if (p == null) return;
            var pack = (DesignStore.PackOfCostume(p.Costume.id) ?? DesignPack.Default()).Clone();
            pack.weapon.baseWeapon = p.Weapon.kind.ToString();
            if (!p.Costume.id.StartsWith(DesignStore.CostumePrefix)) pack.hero.basePerk = p.Costume.id;
            UIKit.Show(gameObject, true);
            Begin(pack);
        }

        void Begin(DesignPack pack)
        {

            working = pack;
            if (InGame || !string.IsNullOrEmpty(pack.id)) working.travelSelected = true;
            history.Clear();
            prompt.SetTextWithoutNotify("");
            UIKit.Show(typeWindow, false);
            statusText = DesignAi.Available ? $"AI 디자인: {AiClient.ModelLabel(Game.Settings.aiModel)}" : "AI 꺼짐 — 무료 로컬 해석으로 만들어요";
            busy = false;
            UIKit.Label(startButton, InGame ? "적용하고 닫기" : "삶을 시작하기");
            UIKit.Label(backButton, InGame ? "취소" : "뒤로");
            UIKit.Set(tabHint, InGame ? "이번 삶에서는 캐릭터 외형만 수정할 수 있어요." : "유형을 고르고 캐릭터 외형과 공격을 한 번에 설명해 주세요.");
            if (prompt.placeholder is Text ph)
                ph.text = InGame ? "예) 망토를 보라색으로 바꿔줘" : "예) 마법사 모자를 쓴 보라색 문어. 다리로 적을 공격한다.";
            Preview();
        }

        void Close()
        {
            StopAllCoroutines();
            busy = false;
            // Cancel: draw the life's own look again
            if (InGame && game.Player != null)
            {
                DesignRuntime.Activate(DesignStore.PackOfCostume(game.Player.Costume.id) ?? DesignPack.Default());
                DesignRuntime.Skin(game.Player.Weapon);
                game.Player.RefreshLook();
            }
            UIKit.Show(gameObject, false);
        }

        void OpenTypes()
        {
            if (busy || working == null) return;
            prompt.DeactivateInputField();
            if (typeWindow == null) BuildTypes();
            typeWindow.SetActive(true);
            typePreviewStartedAt = Time.unscaledTime;
            typeWindow.transform.SetAsLastSibling();
            RefreshTypes();
        }

        void BuildTypes()
        {
            var overlay = new GameObject("Travel types", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(transform, false);
            var rect = (RectTransform)overlay.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0.09f, 0.07f, 0.13f, 0.96f);
            typeWindow = overlay;

            var heading = Instantiate(tabHint, rect);
            Place(heading.rectTransform, new Vector2(0, 109), new Vector2(420, 24));
            heading.alignment = TextAnchor.MiddleCenter;
            heading.text = "이동 유형 · 스페이스";
            typeBalance = Instantiate(heading, rect);
            Place(typeBalance.rectTransform, new Vector2(0, 86), new Vector2(420, 20));

            foreach (var type in TravelTypes.All)
            {
                int index = typeRows.Count;
                var row = TypeButton(rect, new Vector2((index % 3 - 1) * 114, 61 - index / 3 * 33), 106, () => ChooseType(type));
                ((RectTransform)row.transform).sizeDelta = new Vector2(106, 28);
                row.name = "Travel " + type;
                typeRows.Add(row);
            }
            var stage = new GameObject("Space ability demonstration", typeof(RectTransform), typeof(RectMask2D), typeof(TravelAbilityPreview));
            stage.transform.SetParent(rect, false);
            Place((RectTransform)stage.transform, new Vector2(0, -41), new Vector2(276, 92));
            modalMotion = MotionImage(stage.transform);
            Place(modalMotion.rectTransform, Vector2.zero, new Vector2(56, 44));
            abilityPreview = stage.GetComponent<TravelAbilityPreview>();
            var spaceKey = Instantiate(heading, stage.transform);
            Place(spaceKey.rectTransform, new Vector2(0, 32), new Vector2(110, 12));
            spaceKey.fontSize = 8; spaceKey.text = "SPACE";
            abilityPreview.Init(modalMotion, spaceKey);
            typeDescription = Instantiate(heading, rect);
            Place(typeDescription.rectTransform, new Vector2(0, -99), new Vector2(420, 18));
            var close = TypeButton(rect, new Vector2(0, -128), 100, () => UIKit.Show(typeWindow, false));
            close.name = "Close Travel Types";
            UIKit.Label(close, "닫기");
            GameButtonStyle.UseOriginal(close, backButton);
        }

        static Image MotionImage(Transform parent)
        {
            var node = new GameObject("Travel motion preview", typeof(RectTransform), typeof(Image));
            node.transform.SetParent(parent, false);
            var image = node.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        Button TypeButton(RectTransform parent, Vector2 position, float width, System.Action click)
        {
            var button = Instantiate(generate, parent);
            button.onClick = new Button.ButtonClickedEvent();
            Place((RectTransform)button.transform, position, new Vector2(width, 22));
            UIKit.Bind(button, click);
            return button;
        }

        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        void RefreshTypes()
        {
            var world = Session.World;
            UIKit.Set(typeBalance, InGame ? "현재 삶의 유형은 고정돼요." : $"기억의 금고 · {world.gold} 골드");
            for (int i = 0; i < typeRows.Count; i++)
            {
                var type = TravelTypes.All[i];
                bool unlocked = TravelTypes.Unlocked(world, type);
                string state = working.travelSelected && working.travelType == type ? "선택됨" : unlocked ? "선택" : $"해금 {TravelTypes.Price} G";
                UIKit.Label(typeRows[i], $"{TravelTypes.Name(type)} · {state}");
                typeRows[i].interactable = !InGame && (unlocked || world.gold >= TravelTypes.Price);
                GameButtonStyle.Apply(typeRows[i], ButtonRole.Choice, working.travelSelected && working.travelType == type, TravelTypes.Color(type));
            }
        }

        void ChooseType(TravelType type)
        {
            if (InGame || busy) return;
            var world = Session.World;
            if (!TravelTypes.Unlocked(world, type))
            {
                if (world.gold < TravelTypes.Price) return;
                world.gold -= TravelTypes.Price;
                (world.unlockedTravelTypes ??= new List<TravelType>()).Add(type);
                SaveSystem.SaveWorld(world);
                RefreshTypes();
                return;
            }
            if (!working.travelSelected || working.travelType != type)
            {
                Push();
                working.travelSelected = true;
                if (working.travelType != type && working.hero.frames?.Length == 12)
                    working.hero.frames = working.hero.frames.Take(8).ToArray();
                working.travelType = type;
                working.travelEffect = null;
                typePreviewStartedAt = Time.unscaledTime;
                Preview();
            }
            RefreshTypes();
        }

        void Push() => history.Push(working.Clone());

        void Generate()
        {
            if (busy) return;
            if (!RequireType()) return;
            if (typeWindow != null && typeWindow.activeSelf) return;
            string request = DesignGuard.CleanPrompt(prompt.text);
            if (string.IsNullOrWhiteSpace(request)) { statusText = "원하는 캐릭터나 변경 사항을 적어 주세요."; return; }
            bool initial = !InGame && working.source == "default";
            var power = DesignGuard.PowerRequests(request).ToList();
            Push();
            if (!DesignAi.Available)
            {
                Done(DesignLocal.Generate(working, request, initial, InGame), power, "로컬 해석 완료 (무료)");
                return;
            }
            busy = true;
            statusText = "AI가 디자인하는 중…";
            StartCoroutine(DesignAi.Generate(this, working, request, initial, InGame, s => statusText = s, (result, why) =>
            {
                busy = false;
                if (result == null)
                    result = DesignLocal.Generate(working, request, initial, InGame);
                else
                {
                    Session.World.designCalls++;
                    Session.World.designCostUsd += DesignAi.LastCostUsd;
                }
                Done(result, power, why);
            }));
        }

        void Done(DesignPack result, List<string> power, string why)
        {
            result.ignored = (power.Count > 0 ? power : result.ignored ?? new List<string>()).Distinct().Take(6).ToList();
            working = result;
            prompt.SetTextWithoutNotify("");
            statusText = why;
            Preview();
            Sfx.Play("ui_select");
        }

        // ───────────────────────── Commit ─────────────────────────

        bool RequireType()
        {
            if (working.travelSelected) return true;
            statusText = "먼저 이동 유형을 골라 주세요.";
            OpenTypes();
            return false;
        }

        void StartLife()
        {
            if (busy || !RequireType() || (typeWindow != null && typeWindow.activeSelf)) return;
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

        void Preview()
        {
            DesignRuntime.Activate(working);
            previewCostume = DesignStore.PreviewCostume(working);
        }

        void Update()
        {
            if (working == null) return;
            // Esc / any state change closes the pause menu → the studio cancels with it
            if (InGame && (game.State != GameState.Paused || game.Player == null)) { Close(); return; }

            float t = Time.unscaledTime;
            if (typeWindow != null && typeWindow.activeSelf)
            {
                if (Controls.Down(Act.Dash)) typePreviewStartedAt = t - 0.45f;
                float elapsed = t - typePreviewStartedAt;
                float demoPhase = Mathf.Repeat(elapsed, 2.8f);
                float motionTime = Mathf.Clamp01((demoPhase - 0.45f) / TravelAbilityPreview.Duration(working.travelType)) * 1.79f;
                var motion = working.travelSelected ? TravelMotionPreview(working.travelType, motionTime) : null;
                abilityPreview?.Draw(working.travelType, motion, elapsed);
                UIKit.Set(typeDescription, working.travelSelected ? TravelTypes.Description(working.travelType) : "미리 볼 유형을 골라 주세요.");
            }
            // The hero loops through what it does in play: walk, breathe, wind up and strike
            float phase = t % 4f;
            var pose = phase < 2f ? (Pose)((int)Pose.Run0 + (int)(t * 8) % 4)
                     : phase < 3f ? ((int)(t * 2.2f) % 2 == 0 ? Pose.Idle0 : Pose.Idle1)
                     : phase < 3.3f ? Pose.AttackWind : phase < 3.7f ? Pose.AttackHit : Pose.Idle0;
            UIKit.Sprite(hero, Art.Rabbit(previewCostume, pose));
            var kind = DesignRuntime.Chassis(working);
            UIKit.Sprite(heroWeapon, DesignRuntime.WeaponHidden ? null : Art.Weapon(kind));
            UIKit.Show(flipWeapon, !InGame && working.weapon.image != null && !DesignRuntime.WeaponHidden);
            UIKit.Show(flipEffects, !InGame && working.attack.effectImages != null);
            var shot = working.attack.projectile;
            var b = DesignCombat.For(working) ?? DB.W(kind);
            bool shoots = shot.image != null || shot.shape != "builtin" || shot.pixels != null || b.combo.Any(s => s.projectile) || kind == WeaponKind.Dagger;
            UIKit.Sprite(projectile, shoots ? (kind == WeaponKind.Dagger && shot.image == null && shot.shape == "builtin" && shot.pixels == null ? Art.Knife() : Art.Bolt()) : null);
            // Only the fully drawn frames of the swing (the first ones are still sweeping in and look cut off)
            var swing = Art.Slash(b.slashColor, b.slashCore, 14, 160, 1);
            UIKit.Sprite(slash, swing[Mathf.Min(swing.Length - 1, swing.Length / 2 + (int)(t * 5) % Mathf.Max(1, swing.Length - swing.Length / 2))]);

            tabs[0].interactable = !busy;
            UIKit.Label(tabs[0], working.travelSelected ? $"유형 · {TravelTypes.Name(working.travelType)}" : "유형 선택 · 미선택");
            generate.interactable = !busy;
            undo.interactable = !busy && history.Count > 0;
            UIKit.Label(generate, busy ? "만드는 중…" : "요청 적용");

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

        Sprite TravelMotionPreview(TravelType type, float elapsed)
        {
            float phase = Mathf.Repeat(elapsed, 1.8f);
            int frame = phase < 0.2f ? 0 : phase < 1.3f ? 1 + Mathf.FloorToInt((phase - 0.2f) * 8f) % 2 : 3;
            int rabbitFrame = phase < 0.2f ? Mathf.FloorToInt(phase * 10) : phase < 1.3f ? 2 + Mathf.FloorToInt((phase - 0.2f) * 12f) % 4 : 6 + Mathf.Min(1, Mathf.FloorToInt((phase - 1.3f) * 4f));
            if (previewCostume.frames == null) return RabbitTravelArt.Frame(previewCostume, type, rabbitFrame);
            if (previewCostume.frames.Length == 12)
                return Art.Get($"travel_{previewCostume.id}_{frame}", () => DesignArt.HeroPose(previewCostume.frames, Pose.Idle0, false, frame));
            // Until the selected motion is generated, demonstrate the ability with the original rabbit.
            return RabbitTravelArt.Frame(DB.Costumes[0], type, rabbitFrame);
        }


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
            string body = $"<b>{p.hero.name ?? "이름 없는 영웅"}</b> · {(p.travelSelected ? TravelTypes.Name(p.travelType) : "유형 미선택")}";
            string combat = $"{(DesignCombat.For(p) ?? DB.W(DesignRuntime.Chassis(p))).name} · {Ko(p.attack.origin)}에서 발사";
            var lines = new List<string> { body, combat };
            if (!string.IsNullOrEmpty(p.summary)) lines.Add($"<color=#c9bcd8>{p.summary}</color>");
            foreach (var s in p.ignored) lines.Add($"<color=#ffb36b>✕ {s}</color>");
            return string.Join("\n", lines);
        }
    }
}
