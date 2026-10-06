using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using static Diverse.EditorTools.ProjectAssetBuilder;

namespace Diverse.EditorTools
{
    /// <summary>
    /// First-time construction of the uGUI canvases (Prefabs/UI) and the two scenes (MainMenu, Game).
    /// Layout units match the old IMGUI screens: a 640x360 reference canvas (CanvasScaler) where 1 unit = U(1).
    /// Never overwrites an existing prefab or scene: delete one to have it rebuilt.
    /// </summary>
    public static class UIBuilder
    {
        internal const string UIDir = Root + "/UI";
        const string UIPrefabDir = PrefabDir + "/UI";
        const string SceneDir = Root + "/Scenes";
        public const string MenuScenePath = SceneDir + "/MainMenu.unity";
        public const string GameScenePath = SceneDir + "/Game.unity";
        static readonly Vector2 RefRes = new Vector2(640, 360);

        static Font font, fontBold;
        static Sprite panelSpr, slotSpr, buttonSpr, buttonHoverSpr, whiteSpr;

        public static void BuildAll()
        {
            EnsureDir(UIDir); EnsureDir(UIPrefabDir); EnsureDir(SceneDir);
            font = Resources.Load<Font>("Fonts/Galmuri11");
            fontBold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
            BuildFrames();

            var menu = Prefab(UIPrefabDir + "/MenuCanvas.prefab", BuildMenuCanvas);
            var game = Prefab(UIPrefabDir + "/GameCanvas.prefab", BuildGameCanvas);
            if (!File.Exists(MenuScenePath)) BuildMenuScene(menu);
            if (!File.Exists(GameScenePath)) BuildGameScene(game);
            BuildSettings();
        }

        static GameObject Prefab(string path, System.Func<GameObject> build)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = build();
            var p = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return p;
        }

        static void BuildSettings()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.path != MenuScenePath && s.path != GameScenePath && s.path != Root + "/Scenes/Main.unity" && s.path != "Assets/Scenes/SampleScene.unity").ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(GameScenePath, true));
            scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ───────────────────────── Frame sprites ─────────────────────────

        /// <summary>The pixel 9-slice frames of the old IMGUI skin, as editable PNGs (UI/Frames).</summary>
        static void BuildFrames()
        {
            panelSpr = Frame("panel", Pal.Hex("2b2238"), Pal.Hex("4a3d5c"), Pal.Hex("e8d8c0"), 240);
            slotSpr = Frame("slot", Pal.Hex("1b1524"), Pal.Hex("2b2238"), Pal.Hex("6b5c8a"), 255);
            buttonSpr = Frame("button", Pal.Hex("3d3150"), Pal.Hex("564670"), Pal.Hex("c9b8e8"), 255);
            buttonHoverSpr = Frame("button_hover", Pal.Hex("5a4878"), Pal.Hex("7a649e"), Pal.Hex("fff2b3"), 255);
            whiteSpr = Frame("white", Color.white, Color.white, Color.white, 255, plain: true);
        }

        static Sprite Frame(string name, Color32 fill, Color32 inner, Color32 edge, byte alpha, bool plain = false)
        {
            string path = $"{UIDir}/Frames/{name}.png";
            if (!File.Exists(path))
            {
                EnsureDir(UIDir + "/Frames");
                var cv = new PixelCanvas(12, 12);
                if (plain) cv.Rect(0, 0, 12, 12, Color.white);
                else
                {
                    var dark = Pal.Hex("120d18");
                    cv.Rect(1, 0, 10, 12, dark); cv.Rect(0, 1, 12, 10, dark);
                    cv.Rect(1, 1, 10, 10, edge);
                    cv.Rect(2, 2, 8, 8, inner);
                    cv.Rect(3, 3, 6, 6, Pal.A(fill, alpha));
                    cv.Set(2, 9, Pal.White);
                }
                var tex = cv.ToTexture();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.spritePixelsPerUnit = 100;
                imp.spriteBorder = new Vector4(4, 4, 4, 4);
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        internal static void ImportPicture(string path, float ppu)
        {
            if (!File.Exists(path)) return;
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = ppu;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        // ───────────────────────── Primitives ─────────────────────────

        /// <summary>Rect in old IMGUI units: x,y from the anchor corner (y grows downward like the IMGUI screens).</summary>
        static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(pos.x, -pos.y);
            return rt;
        }

        static RectTransform Stretch(Transform parent, string name, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        static Image Box(RectTransform rt, Sprite s, Color? c = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.type = s == whiteSpr ? Image.Type.Simple : Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 0.5f;          // 2x pixel frames at the reference size
            img.color = c ?? Color.white;
            return img;
        }

        static Text Label(Transform parent, string name, string text, Vector2 anchor, Vector2 pos, Vector2 size,
                          int fontSize = 11, TextAnchor align = TextAnchor.UpperLeft, Color? color = null, bool bold = false, bool shadow = true)
        {
            var rt = Node(parent, name, anchor, pos, size);
            return TextOn(rt, text, fontSize, align, color, bold, shadow);
        }

        static Text TextOn(RectTransform rt, string text, int fontSize = 11, TextAnchor align = TextAnchor.UpperLeft, Color? color = null, bool bold = false, bool shadow = true)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = bold ? fontBold : font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color ?? UIKit.Ink;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            if (shadow)
            {
                var sh = rt.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0.05f, 0.03f, 0.08f, 0.9f);
                sh.effectDistance = new Vector2(1, -1);
            }
            return t;
        }

        static Button Btn(Transform parent, string name, string caption, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize = 11)
        {
            var rt = Node(parent, name, anchor, pos, size);
            var img = Box(rt, buttonSpr);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.SpriteSwap;
            b.spriteState = new SpriteState { highlightedSprite = buttonHoverSpr, pressedSprite = buttonHoverSpr, selectedSprite = buttonSpr, disabledSprite = buttonSpr };
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            var txt = TextOn(Stretch(rt, "Text", 4, 0, 4, 0), caption, fontSize, TextAnchor.MiddleCenter);
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            return b;
        }

        static Image Bar(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color fill, Color back, out Image fillImg)
        {
            var rt = Node(parent, name, anchor, pos, size);
            var bg = Box(rt, whiteSpr, back);
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.07f, 0.04f, 0.1f);
            outline.effectDistance = new Vector2(1, -1);
            var f = Stretch(rt, "Fill");
            fillImg = Box(f, whiteSpr, fill);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 0.7f;
            return bg;
        }

        static Image Pic(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Sprite s, bool raycast = false)
        {
            var rt = Node(parent, name, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.preserveAspect = true;
            img.raycastTarget = raycast;
            return img;
        }

        static Slider MakeSlider(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, float min, float max)
        {
            var rt = Node(parent, name, anchor, pos, size);
            Box(Stretch(rt, "Background", 0, 4, 0, 4), whiteSpr, new Color(0.15f, 0.1f, 0.2f));
            var fillArea = Stretch(rt, "Fill Area", 0, 4, 0, 4);
            var fill = Box(Stretch(fillArea, "Fill"), whiteSpr, new Color(0.79f, 0.72f, 1f));
            var handleArea = Stretch(rt, "Handle Slide Area", 4, 0, 4, 0);
            var handle = Node(handleArea, "Handle", new Vector2(0, 0.5f), Vector2.zero, new Vector2(8, 0), new Vector2(0.5f, 0.5f));
            handle.anchorMin = new Vector2(0, 0); handle.anchorMax = new Vector2(0, 1);
            var himg = Box(handle, buttonHoverSpr);
            var s = rt.gameObject.AddComponent<Slider>();
            s.fillRect = (RectTransform)fill.transform;
            s.handleRect = handle;
            s.targetGraphic = himg;
            s.minValue = min; s.maxValue = max; s.value = (min + max) / 2;
            var nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
            return s;
        }

        /// <summary>Scrollable rich-text body (ability list, chronicle, pipeline log, chat).</summary>
        static Text ScrollText(Transform parent, string name, RectTransform area, out ScrollRect scroll)
        {
            var go = area.gameObject;
            go.name = name;
            var viewport = Stretch(area, "Viewport", 4, 4, 4, 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Node(viewport, "Content", new Vector2(0, 1), Vector2.zero, new Vector2(0, 0), new Vector2(0, 1));
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.sizeDelta = new Vector2(0, 0);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = TextOn(content, "", 9, TextAnchor.UpperLeft, Pal.Hex("c9bcd8"));
            scroll = go.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 18;
            // ScrollRect needs a raycast target to receive the wheel
            var hit = go.GetComponent<Image>() ?? Box(area, whiteSpr, new Color(0, 0, 0, 0));
            hit.raycastTarget = true;
            return t;
        }

        static RectTransform Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var c = go.AddComponent<UnityEngine.Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            c.pixelPerfect = true;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = RefRes;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1;           // follow height, like IMGUI's Screen.height / 360
            go.AddComponent<GraphicRaycaster>();
            return (RectTransform)go.transform;
        }

        static readonly Vector2 TL = new Vector2(0, 1), TR = new Vector2(1, 1), BL = new Vector2(0, 0), BR = new Vector2(1, 0),
                                C = new Vector2(0.5f, 0.5f), TC = new Vector2(0.5f, 1), BC = new Vector2(0.5f, 0);

        /// <summary>Centered panel (like the old Center(w, h)); children use TL-relative coordinates.</summary>
        static RectTransform Panel(Transform parent, string name, float w, float h, Sprite s = null)
        {
            var rt = Node(parent, name, C, Vector2.zero, new Vector2(w, h), C);
            Box(rt, s ?? panelSpr).raycastTarget = true;
            return rt;
        }

        static void Wire(Object target, string field, Object value) => ProjectAssetBuilder.Set(target, field, value);

        static void WireArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.Exception($"{target.GetType().Name} has no field '{field}'");
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── Menu canvas ─────────────────────────

        static GameObject BuildMenuCanvas()
        {
            var root = Canvas("MenuCanvas", 0);
            var menu = root.gameObject.AddComponent<MenuUI>();

            // Still background (baked town) + shade
            var bg = Stretch(root, "Background");
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UIDir + "/TitleBackground.png");
            bgImg.color = bgImg.sprite != null ? Color.white : new Color(0.09f, 0.07f, 0.12f);
            var bgFit = bg.gameObject.AddComponent<AspectRatioFitter>();
            bgFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            bgFit.aspectRatio = RefRes.x / RefRes.y;

            var title = BuildTitle(root);
            var select = BuildSelect(root);
            var settings = BuildSettingsView(root, standalone: true);
            UIKit.Show(select, false);
            UIKit.Show(settings, false);
            Wire(menu, "title", title);
            Wire(menu, "select", select);
            Wire(menu, "settings", settings);
            return root.gameObject;
        }

        static TitleView BuildTitle(Transform parent)
        {
            var rt = Stretch(parent, "Title");
            var v = rt.gameObject.AddComponent<TitleView>();
            var logo = Label(rt, "Logo", "Diverse", TC, new Vector2(0, 58), new Vector2(400, 44), 28, TextAnchor.MiddleCenter, Pal.Hex("fff2d6"), bold: true);
            logo.rectTransform.pivot = new Vector2(0.5f, 1);
            Label(rt, "Subtitle", "— 세계는 당신의 삶을 기억한다 —", TC, new Vector2(0, 104), new Vector2(400, 16), 11, TextAnchor.MiddleCenter, Pal.Hex("c9b8ff"))
                .rectTransform.pivot = new Vector2(0.5f, 1);
            var rabbits = new Image[6];
            for (int i = 0; i < 6; i++)
            {
                var c = DefaultData.Costumes[i];
                rabbits[i] = Pic(rt, "Rabbit " + i, TC, new Vector2(-70 + i * 24 + 16, 126), new Vector2(52, 60), Baked($"rabbit_{c.id}_Idle0"));
                rabbits[i].rectTransform.pivot = new Vector2(0.5f, 1);
                if (i >= 3) rabbits[i].rectTransform.localScale = new Vector3(-1, 1, 1);
            }
            // Button column (old: 62% of the height, 140 x 22, 6 apart)
            var col = Node(rt, "Buttons", TC, new Vector2(0, 216), new Vector2(140, 150), new Vector2(0.5f, 1));
            var layout = col.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6; layout.childControlHeight = false; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            Button B(string n, string cap) { var b = Btn(col, n, cap, TL, Vector2.zero, new Vector2(140, 22)); return b; }
            var cont = B("Continue", "이어하기 — 영웅 Lv.1");
            var abandon = B("Abandon", "이 삶을 끝내고 새로 시작");
            var start = B("Start", "새로운 삶 시작");
            var settings = B("Settings", "설정");
            var reset = B("ResetWorld", "새로운 세계 시작");
            var quit = B("Quit", "종료");
            var footer = Label(rt, "Footer", "AI 진화: 로컬 모드   ·   세계 시드 0", BL, new Vector2(8, -18), new Vector2(620, 14), 9);
            footer.rectTransform.pivot = new Vector2(0, 0);
            footer.rectTransform.anchoredPosition = new Vector2(8, 4);

            Wire(v, "logo", logo.rectTransform);
            WireArray(v, "rabbits", rabbits);
            Wire(v, "continueButton", cont); Wire(v, "abandonButton", abandon); Wire(v, "startButton", start);
            Wire(v, "settingsButton", settings); Wire(v, "resetButton", reset); Wire(v, "quitButton", quit);
            Wire(v, "footer", footer);
            return v;
        }

        static CharacterSelectView BuildSelect(Transform parent)
        {
            var shade = Stretch(parent, "CharacterSelect");
            Box(shade, whiteSpr, new Color(0.05f, 0.03f, 0.08f, 0.45f));
            var v = shade.gameObject.AddComponent<CharacterSelectView>();
            var a = Panel(shade, "Panel", 440, 300);
            Label(a, "Heading", "누구로 살아갈까요?", TL, new Vector2(0, 8), new Vector2(440, 20), 17, TextAnchor.MiddleCenter, Pal.Hex("fff2d6"), bold: true);

            var pv = Node(a, "Preview", TL, new Vector2(16, 40), new Vector2(120, 120));
            Box(pv, slotSpr);
            var preview = Pic(pv, "Rabbit", TL, new Vector2(10, 10), new Vector2(100, 100), Baked("rabbit_white_Idle0"));
            var previewWeapon = Pic(pv, "Weapon", TL, new Vector2(50, 56), new Vector2(50, 30), Baked("weapon_Greatsword"));

            float x0 = 146, y0 = 40;
            Label(a, "CostumeHeading", "<b>의상</b>", TL, new Vector2(x0, y0), new Vector2(280, 14));
            var costumes = Node(a, "Costumes", TL, new Vector2(x0, y0 + 16), new Vector2(280, 42));
            var cl = costumes.gameObject.AddComponent<HorizontalLayoutGroup>();
            cl.spacing = 4; cl.childControlWidth = false; cl.childControlHeight = false; cl.childForceExpandWidth = false;
            var ct = Btn(costumes, "Costume", "", TL, Vector2.zero, new Vector2(42, 42));
            Object.DestroyImmediate(ct.transform.Find("Text").gameObject);
            ct.GetComponent<Image>().sprite = slotSpr;
            Pic(ct.transform, "Icon", TL, new Vector2(3, 3), new Vector2(36, 36), Baked("rabbit_white_Idle0"));
            var costumeInfo = Label(a, "CostumeInfo", "<b>흰 토끼</b>  <color=#fff2b3>경험치 +30%</color>", TL, new Vector2(x0, y0 + 62), new Vector2(280, 30));

            y0 += 84;
            Label(a, "WeaponHeading", "<b>시작 무기</b>", TL, new Vector2(x0, y0), new Vector2(280, 14));
            var weapons = Node(a, "Weapons", TL, new Vector2(x0, y0 + 16), new Vector2(282, 48));
            var wl = weapons.gameObject.AddComponent<GridLayoutGroup>();
            wl.cellSize = new Vector2(90, 22); wl.spacing = new Vector2(4, 4); wl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; wl.constraintCount = 3;
            var wt = Btn(weapons, "Weapon", "대검", TL, Vector2.zero, new Vector2(90, 22));
            var weaponDesc = Label(a, "WeaponDesc", "느리지만 한 방이 묵직하다.", TL, new Vector2(x0, y0 + 72), new Vector2(285, 40), 9, color: Pal.Hex("c9bcd8"));

            float sy = 170;
            Label(a, "SkillHeading", "<b>스킬</b>", TL, new Vector2(16, sy), new Vector2(120, 14));
            var skills = new Text[4];
            string[] keys = { "Q", "W", "E", "R" };
            for (int i = 0; i < 4; i++)
                skills[i] = Label(a, "Skill " + keys[i], $"<color=#c9b8ff>{keys[i]}</color> 스킬", TL, new Vector2(16, sy + 16 + i * 18), new Vector2(122, 16), 9, color: Pal.Hex("c9bcd8"));

            var startB = Btn(a, "StartLife", "삶을 시작하기", TL, new Vector2(440 - 150, 300 - 34), new Vector2(136, 24));
            var backB = Btn(a, "Back", "뒤로", TL, new Vector2(14, 300 - 34), new Vector2(70, 24));

            Wire(v, "preview", preview); Wire(v, "previewWeapon", previewWeapon);
            Wire(v, "costumeTemplate", ct); Wire(v, "weaponTemplate", wt);
            Wire(v, "costumeInfo", costumeInfo); Wire(v, "weaponDesc", weaponDesc);
            WireArray(v, "skillLines", skills);
            Wire(v, "startButton", startB); Wire(v, "backButton", backB);
            return v;
        }

        /// <summary>Settings + key bindings pages. standalone = title-screen version with its own tabs and a back button.</summary>
        static SettingsView BuildSettingsView(Transform parent, bool standalone)
        {
            RectTransform host, body;
            if (standalone)
            {
                host = Stretch(parent, "Settings");
                Box(host, whiteSpr, new Color(0.05f, 0.03f, 0.08f, 0.45f));
                var panel = Panel(host, "Panel", 470, 300);
                body = Node(panel, "Body", TL, new Vector2(12, 36), new Vector2(446, 228));
            }
            else
            {
                host = Node(parent, "Settings", TL, new Vector2(12, 36), new Vector2(446, 228));
                body = host;
            }
            var v = host.gameObject.AddComponent<SettingsView>();

            // Settings page
            var sp = Stretch(body, "SettingsPage");
            Box(sp, slotSpr);
            float x = 8, y = 8;
            Label(sp, "SfxLabel", "효과음", TL, new Vector2(x, y), new Vector2(100, 14));
            var sfx = MakeSlider(sp, "Sfx", TL, new Vector2(x + 100, y + 1), new Vector2(150, 12), 0, 1); y += 18;
            Label(sp, "MusicLabel", "음악", TL, new Vector2(x, y), new Vector2(100, 14));
            var music = MakeSlider(sp, "Music", TL, new Vector2(x + 100, y + 1), new Vector2(150, 12), 0, 1); y += 18;
            Label(sp, "ShakeLabel", "화면 흔들림", TL, new Vector2(x, y), new Vector2(100, 14));
            var shake = MakeSlider(sp, "Shake", TL, new Vector2(x + 100, y + 1), new Vector2(150, 12), 0, 1.5f); y += 18;
            var dmg = Btn(sp, "DamageNumbers", "피해 숫자: 켜짐", TL, new Vector2(x, y), new Vector2(170, 18), 9); y += 26;
            Label(sp, "AiHeading", "<b>AI 진화 (OpenAI)</b>", TL, new Vector2(x, y), new Vector2(430, 14)); y += 16;
            var keyState = Label(sp, "KeyState", "API 키 없음", TL, new Vector2(x, y), new Vector2(430, 12), 9); y += 13;
            var keyHint = Label(sp, "KeyHint", "키 위치: …", TL, new Vector2(x, y), new Vector2(430, 26), 9, color: UIKit.Muted); y += 28;
            var ai = Btn(sp, "AiToggle", "AI: 켜짐", TL, new Vector2(x, y), new Vector2(120, 18), 9);
            var folder = Btn(sp, "OpenFolder", "폴더 열기", TL, new Vector2(x + 130, y), new Vector2(90, 18), 9); y += 22;
            Label(sp, "ModelLabel", "모델", TL, new Vector2(x, y + 2), new Vector2(40, 14), 9);
            var models = Node(sp, "Models", TL, new Vector2(x + 36, y), new Vector2(394, 18));
            var ml = models.gameObject.AddComponent<HorizontalLayoutGroup>();
            ml.spacing = 4; ml.childControlWidth = true; ml.childForceExpandWidth = true; ml.childControlHeight = true;
            var mt = Btn(models, "Model", "GPT-4o mini", TL, Vector2.zero, new Vector2(94, 18), 9);
            y += 20;
            var modelHint = Label(sp, "ModelHint", "", TL, new Vector2(x + 36, y), new Vector2(394, 12), 9, color: UIKit.Muted); y += 14;
            var lastCall = Label(sp, "LastCall", "", TL, new Vector2(x, y), new Vector2(430, 12), 9, color: UIKit.Muted);

            // Key bindings page
            var kp = Stretch(body, "KeysPage");
            Box(kp, slotSpr);
            var grid = Node(kp, "Bindings", TL, new Vector2(8, 6), new Vector2(430, 150));
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(211, 18); gl.spacing = new Vector2(8, 3); gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 2;
            // Row = button on the right with a "Label" text on its left
            var row = Btn(grid, "Binding", "Q", TL, Vector2.zero, new Vector2(211, 18), 9);
            var rowText = row.transform.Find("Text").GetComponent<RectTransform>();
            rowText.anchorMin = new Vector2(1, 0); rowText.anchorMax = new Vector2(1, 1);
            rowText.pivot = new Vector2(1, 0.5f); rowText.sizeDelta = new Vector2(76, 0); rowText.anchoredPosition = Vector2.zero;
            var lbl = Label(row.transform, "Label", "동작", BL, Vector2.zero, new Vector2(130, 18), 9, TextAnchor.MiddleLeft);
            lbl.rectTransform.anchorMin = new Vector2(0, 0); lbl.rectTransform.anchorMax = new Vector2(0, 1);
            lbl.rectTransform.pivot = new Vector2(0, 0.5f); lbl.rectTransform.anchoredPosition = new Vector2(6, 0);
            var reset = Btn(kp, "ResetKeys", "기본값으로", TL, new Vector2(8, 160), new Vector2(120, 18), 9);
            Label(kp, "KeysHint", "<color=#8c7aa8>빨간색 = 다른 동작과 키가 겹침. Esc로 취소.</color>", TL, new Vector2(138, 162), new Vector2(300, 14), 9);
            UIKit.Show(kp, false);

            Wire(v, "settingsPage", sp.gameObject);
            Wire(v, "sfx", sfx); Wire(v, "music", music); Wire(v, "shake", shake);
            Wire(v, "damageNumbers", dmg); Wire(v, "aiToggle", ai); Wire(v, "openFolder", folder);
            Wire(v, "keyState", keyState); Wire(v, "keyHint", keyHint); Wire(v, "modelHint", modelHint); Wire(v, "lastCall", lastCall);
            Wire(v, "modelTemplate", mt);
            Wire(v, "keysPage", kp.gameObject);
            Wire(v, "keyTemplate", row); Wire(v, "resetKeys", reset);

            if (standalone)
            {
                var panel = body.parent;
                var tabs = Node(panel, "Tabs", TL, new Vector2(12, 10), new Vector2(446, 20));
                var tl = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
                tl.spacing = 4; tl.childControlWidth = true; tl.childForceExpandWidth = true; tl.childControlHeight = true;
                var t1 = Btn(tabs, "SettingsTab", "설정", TL, Vector2.zero, new Vector2(100, 20));
                var t2 = Btn(tabs, "KeysTab", "키 설정", TL, Vector2.zero, new Vector2(100, 20));
                var back = Btn(panel, "Back", "뒤로", TL, new Vector2(12, 300 - 30), new Vector2(110, 20));
                Wire(v, "settingsTab", t1); Wire(v, "keysTab", t2); Wire(v, "backButton", back);
            }
            return v;
        }

        // ───────────────────────── Game canvas ─────────────────────────

        static GameObject BuildGameCanvas()
        {
            var root = Canvas("GameCanvas", 0);
            var ui = root.gameObject.AddComponent<GameUI>();

            var hud = BuildHud(root, out var tooltip);
            var toasts = BuildToasts(root);       // under the overlays: an open menu is never covered by a notice
            var dim = Stretch(root, "Dim");
            Box(dim, whiteSpr, new Color(0.05f, 0.03f, 0.08f, 0.6f)).raycastTarget = true;
            var pause = BuildPause(root);
            var dialogue = BuildDialogue(root);
            var death = BuildDeath(root);
            var evolve = BuildEvolve(root);
            var map = BuildMap(root);
            tooltip.transform.SetAsLastSibling();
            foreach (var go in new Component[] { pause, dialogue, death, evolve, map }) UIKit.Show(go, false);
            UIKit.Show(dim.gameObject, false);

            Wire(ui, "hud", hud); Wire(ui, "pause", pause); Wire(ui, "dialogue", dialogue); Wire(ui, "death", death);
            Wire(ui, "evolve", evolve); Wire(ui, "map", map); Wire(ui, "toasts", toasts); Wire(ui, "dim", dim.gameObject);
            return root.gameObject;
        }

        static HudView BuildHud(Transform root, out TooltipView tooltip)
        {
            var rt = Stretch(root, "Hud");
            var v = rt.gameObject.AddComponent<HudView>();
            Color hpCol = new Color(0.95f, 0.33f, 0.4f), xpCol = new Color(0.45f, 0.75f, 1f);

            // Top left
            var pf = Node(rt, "PortraitFrame", TL, new Vector2(8, 8), new Vector2(40, 40));
            Box(pf, slotSpr);
            var portrait = Pic(pf, "Portrait", TL, new Vector2(2, 2), new Vector2(36, 36), Baked("rabbit_white_Idle0"));
            float bx = 54;
            var hero = Label(rt, "HeroLine", "<b>영웅</b>  <color=#c9b8ff>Lv.1</color>", TL, new Vector2(bx, 7), new Vector2(220, 14), 9);
            Bar(rt, "Hp", TL, new Vector2(bx, 21), new Vector2(130, 9), hpCol, new Color(0.25f, 0.1f, 0.15f), out var hpFill);
            var hpText = Label(hpFill.transform.parent, "Value", "100/100", C, Vector2.zero, new Vector2(130, 9), 8, TextAnchor.MiddleCenter);
            hpText.rectTransform.pivot = C;
            var shieldFill = Box(Node(rt, "Shield", TL, new Vector2(bx, 31), new Vector2(130, 2)), whiteSpr, new Color(0.55f, 0.9f, 1f));
            shieldFill.type = Image.Type.Filled; shieldFill.fillMethod = Image.FillMethod.Horizontal; shieldFill.fillAmount = 0;
            Bar(rt, "Xp", TL, new Vector2(bx, 35), new Vector2(130, 5), xpCol, new Color(0.1f, 0.12f, 0.25f), out var xpFill);
            var dashes = Node(rt, "Dashes", TL, new Vector2(bx, 43), new Vector2(130, 4));
            var dl = dashes.gameObject.AddComponent<HorizontalLayoutGroup>();
            dl.spacing = 2; dl.childControlWidth = false; dl.childForceExpandWidth = false; dl.childControlHeight = false;
            var dash = Bar(dashes, "Dash", TL, Vector2.zero, new Vector2(8, 4), new Color(0.75f, 0.9f, 1f), new Color(0.15f, 0.15f, 0.25f), out _);

            // Top right box
            var tr = Node(rt, "Status", TR, new Vector2(-8, 8), new Vector2(162, 46));
            tr.anchoredPosition = new Vector2(-8, -8);
            Box(tr, slotSpr).raycastTarget = true;
            var goldIcon = Pic(tr, "GoldIcon", TL, new Vector2(4, 4), new Vector2(10, 10), Baked("item_gold"));
            var goldText = Label(tr, "Gold", "0", TL, new Vector2(16, 3), new Vector2(42, 12), 9);
            var shardIcon = Pic(tr, "ShardIcon", TL, new Vector2(60, 4), new Vector2(10, 10), Baked("item_shard"));
            var shardText = Label(tr, "Shards", "0", TL, new Vector2(72, 3), new Vector2(30, 12), 9);
            var potionIcon = Pic(tr, "PotionIcon", TL, new Vector2(104, 4), new Vector2(10, 10), Baked("item_potion"));
            var potionText = Label(tr, "Potions", "2 [1]", TL, new Vector2(116, 3), new Vector2(46, 12), 9);
            var region = Label(tr, "Region", "기억의 광장", TL, new Vector2(4, 17), new Vector2(156, 12), 9, color: UIKit.Highlight);
            var coord = Label(tr, "Coords", "(0, 0)  Tab 지도", TL, new Vector2(4, 30), new Vector2(140, 12), 9, color: UIKit.Muted);
            var raftIcon = Pic(tr, "RaftIcon", TL, new Vector2(148, 30), new Vector2(10, 10), Baked("item_raft"), raycast: true);
            Image Hover(string n, float x, float w) { var h = Box(Node(tr, n, TL, new Vector2(x, 0), new Vector2(w, 15)), whiteSpr, new Color(0, 0, 0, 0)); h.raycastTarget = true; return h; }
            var goldHover = Hover("GoldHover", 0, 56); var shardHover = Hover("ShardHover", 58, 44); var potionHover = Hover("PotionHover", 102, 60);

            // Minimap under it
            var mm = Node(rt, "Minimap", TR, Vector2.zero, new Vector2(70, 70));
            mm.anchoredPosition = new Vector2(-8, -58);
            Box(mm, slotSpr);
            var minimapCanvas = BuildMapCanvas(mm, 3, 7);
            var minimap = mm.gameObject.AddComponent<MinimapView>();
            Wire(minimap, "map", minimapCanvas);

            var quests = Label(rt, "Quests", "", TR, Vector2.zero, new Vector2(162, 40), 9, TextAnchor.UpperRight);
            quests.rectTransform.anchoredPosition = new Vector2(-8, -132);

            // Bottom center skill bar: 4 x 30, 4 apart, 10 above the bottom
            var bar = Node(rt, "SkillBar", BC, Vector2.zero, new Vector2(132, 30), BC);
            bar.anchoredPosition = new Vector2(0, 10);
            var slots = new SkillSlot[4];
            string[] keys = { "Q", "W", "E", "R" };
            for (int i = 0; i < 4; i++)
            {
                var s = Node(bar, "Skill " + keys[i], TL, new Vector2(i * 34, 0), new Vector2(30, 30));
                Box(s, slotSpr).raycastTarget = true;
                var icon = TextOn(Stretch(s, "Icon"), "★", 16, TextAnchor.MiddleCenter, Pal.Hex("ffd27a"), bold: true);
                var shadeRt = Stretch(s, "Cooldown Shade", 2, 2, 2, 2);
                var shade = Box(shadeRt, whiteSpr, new Color(0.05f, 0.03f, 0.1f, 0.7f));
                shade.type = Image.Type.Filled; shade.fillMethod = Image.FillMethod.Vertical; shade.fillOrigin = (int)Image.OriginVertical.Top; shade.fillAmount = 0.4f;
                var cd = TextOn(Stretch(s, "Cooldown"), "3", 11, TextAnchor.MiddleCenter);
                var key = Label(s, "Key", keys[i], TL, new Vector2(2, 1), new Vector2(28, 10), 8, color: UIKit.Highlight);
                slots[i] = s.gameObject.AddComponent<SkillSlot>();
                Wire(slots[i], "icon", icon); Wire(slots[i], "cooldown", cd); Wire(slots[i], "key", key); Wire(slots[i], "cooldownShade", shade);
            }
            var moveHint = Label(bar, "MoveHint", "<color=#8c7aa8>우클릭</color> 이동/공격", TL, new Vector2(138, 4), new Vector2(120, 12), 9);
            var dashHint = Label(bar, "DashHint", "<color=#8c7aa8>Space</color> 대시", TL, new Vector2(138, 16), new Vector2(120, 12), 9);

            // Bottom left: owned abilities (grows upward), evolve / attribute hints above them
            var abil = Label(rt, "Abilities", "", BL, Vector2.zero, new Vector2(240, 80), 9, TextAnchor.LowerLeft);
            abil.rectTransform.pivot = BL; abil.rectTransform.anchoredPosition = new Vector2(8, 6);
            var stack = Node(rt, "Hints", BL, Vector2.zero, new Vector2(300, 34), BL);
            stack.anchoredPosition = new Vector2(8, 88);
            var evolveHint = Label(stack, "EvolveHint", "★ 진화 가능", BL, Vector2.zero, new Vector2(300, 14), 9);
            evolveHint.rectTransform.pivot = BL; evolveHint.rectTransform.anchoredPosition = Vector2.zero;
            var attrHint = Label(stack, "AttrHint", "+ 능력치", BL, Vector2.zero, new Vector2(300, 14), 9);
            attrHint.rectTransform.pivot = BL; attrHint.rectTransform.anchoredPosition = new Vector2(0, 16);

            // Interaction hint (follows the interactable)
            var ih = Node(rt, "InteractHint", C, Vector2.zero, new Vector2(100, 14), C);
            Box(ih, slotSpr);
            var ihText = TextOn(Stretch(ih, "Text"), "[F] 상호작용", 9, TextAnchor.MiddleCenter);
            ihText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Boss bar
            var bb = Node(rt, "BossBar", TC, Vector2.zero, new Vector2(320, 10), TC);
            bb.anchoredPosition = new Vector2(0, -20);
            bb.anchorMin = new Vector2(0.25f, 1); bb.anchorMax = new Vector2(0.75f, 1); bb.sizeDelta = new Vector2(0, 10);
            Box(bb, whiteSpr, new Color(0.2f, 0.05f, 0.1f));
            var bossFill = Box(Stretch(bb, "Fill"), whiteSpr, new Color(0.85f, 0.2f, 0.3f));
            bossFill.type = Image.Type.Filled; bossFill.fillMethod = Image.FillMethod.Horizontal;
            var bossName = Label(bb, "Name", "<b>보스</b>", TC, new Vector2(0, -13), new Vector2(320, 12), 9, TextAnchor.LowerCenter, Pal.Hex("ff8a8a"));
            bossName.rectTransform.pivot = BC; bossName.rectTransform.anchoredPosition = new Vector2(0, 2);

            var gen = Label(rt, "Generating", "AI가 당신의 플레이를 읽는 중…", TC, new Vector2(0, 108), new Vector2(500, 14), 9, TextAnchor.MiddleCenter);
            gen.rectTransform.pivot = TC;

            // Tooltip (shared)
            var tt = Node(rt, "Tooltip", TL, Vector2.zero, new Vector2(190, 40), TL);
            Box(tt, panelSpr);
            var tl = tt.gameObject.AddComponent<VerticalLayoutGroup>();
            tl.padding = new RectOffset(6, 6, 4, 6); tl.spacing = 2; tl.childControlHeight = true; tl.childControlWidth = true; tl.childForceExpandHeight = false;
            var fit = tt.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var tHead = TextOn(Node(tt, "Head", TL, Vector2.zero, new Vector2(178, 12)), "<b>제목</b>", 9);
            var tBody = TextOn(Node(tt, "Body", TL, Vector2.zero, new Vector2(178, 24)), "설명", 9, color: Pal.Hex("c9bcd8"));
            tooltip = tt.gameObject.AddComponent<TooltipView>();
            Wire(tooltip, "head", tHead); Wire(tooltip, "body", tBody);
            UIKit.Show(tooltip, false);

            Wire(v, "portrait", portrait); Wire(v, "heroLine", hero);
            Wire(v, "hpFill", hpFill); Wire(v, "shieldFill", shieldFill); Wire(v, "xpFill", xpFill); Wire(v, "hpText", hpText);
            Wire(v, "dashTemplate", dash);
            Wire(v, "goldIcon", goldIcon); Wire(v, "shardIcon", shardIcon); Wire(v, "potionIcon", potionIcon); Wire(v, "raftIcon", raftIcon);
            Wire(v, "goldText", goldText); Wire(v, "shardText", shardText); Wire(v, "potionText", potionText);
            Wire(v, "regionText", region); Wire(v, "coordText", coord);
            Wire(v, "goldHover", goldHover.rectTransform); Wire(v, "shardHover", shardHover.rectTransform); Wire(v, "potionHover", potionHover.rectTransform);
            Wire(v, "minimap", minimap); Wire(v, "questsText", quests);
            WireArray(v, "skills", slots);
            Wire(v, "moveHint", moveHint); Wire(v, "dashHint", dashHint);
            Wire(v, "abilitiesText", abil); Wire(v, "evolveHint", evolveHint); Wire(v, "attrHint", attrHint);
            Wire(v, "interactHint", ih); Wire(v, "interactText", ihText);
            Wire(v, "bossBar", bb); Wire(v, "bossName", bossName); Wire(v, "bossFill", bossFill);
            Wire(v, "generatingText", gen); Wire(v, "tooltip", tooltip);
            return v;
        }

        /// <summary>RawImage map surface with a marker template (shared by minimap and world map).</summary>
        static MapCanvas BuildMapCanvas(RectTransform parent, float inset, int markerSize)
        {
            var surf = Stretch(parent, "Map", inset, inset, inset, inset);
            var raw = surf.gameObject.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            surf.gameObject.AddComponent<RectMask2D>();
            var marker = TextOn(Node(surf, "Marker", C, Vector2.zero, new Vector2(16, 16), C), "•", markerSize, TextAnchor.MiddleCenter);
            marker.horizontalOverflow = HorizontalWrapMode.Overflow;
            var name = TextOn(Node(marker.transform, "Name", C, new Vector2(0, 10), new Vector2(120, 12), TC), "", 8, TextAnchor.UpperCenter);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIKit.Show(marker, false);
            var mc = surf.gameObject.AddComponent<MapCanvas>();
            Wire(mc, "image", raw); Wire(mc, "markerTemplate", marker);
            return mc;
        }

        static PauseView BuildPause(Transform root)
        {
            var host = Stretch(root, "Pause");
            var v = host.gameObject.AddComponent<PauseView>();
            var a = Panel(host, "Panel", 470, 300);
            var tabsRt = Node(a, "Tabs", TL, new Vector2(12, 10), new Vector2(446, 20));
            var tl = tabsRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            tl.spacing = 4; tl.childControlWidth = true; tl.childForceExpandWidth = true; tl.childControlHeight = true;
            string[] names = { "능력", "능력치", "연대기", "설정", "키 설정" };
            var tabs = names.Select(n => (Object)Btn(tabsRt, "Tab " + n, n, TL, Vector2.zero, new Vector2(80, 20))).ToArray();

            var pageRect = new Vector2(446, 228);
            // Abilities
            var ap = Node(a, "AbilitiesPage", TL, new Vector2(12, 36), pageRect);
            Box(ap, slotSpr);
            var aHead = Label(ap, "Header", "<b>무기</b> · 코스튬", TL, new Vector2(6, 6), new Vector2(430, 14), 9);
            var aList = ScrollText(ap, "List", Node(ap, "List", TL, new Vector2(6, 22), new Vector2(434, 200)), out _);
            // Attributes
            var tp = Node(a, "AttrsPage", TL, new Vector2(12, 36), pageRect);
            Box(tp, slotSpr);
            var unspent = Label(tp, "Unspent", "남은 포인트: 0", TL, new Vector2(8, 8), new Vector2(220, 14));
            var attrLines = new Text[4]; var plus = new Object[4];
            for (int i = 0; i < 4; i++)
            {
                attrLines[i] = Label(tp, "Attr " + i, "<b>힘</b> 0", TL, new Vector2(8, 30 + i * 22), new Vector2(190, 18), 9);
                plus[i] = Btn(tp, "Plus " + i, "+", TL, new Vector2(200, 28 + i * 22), new Vector2(22, 18));
            }
            var statNames = Label(tp, "StatNames", "최대 체력", TL, new Vector2(240, 8), new Vector2(90, 200), 9);
            var statValues = Label(tp, "StatValues", "100", TL, new Vector2(330, 8), new Vector2(100, 200), 9);
            // Chronicle
            var cp = Node(a, "ChroniclePage", TL, new Vector2(12, 36), pageRect);
            Box(cp, slotSpr);
            var cHead = Label(cp, "Header", "<b>연대기</b>", TL, new Vector2(6, 6), new Vector2(430, 14), 9);
            var cList = ScrollText(cp, "List", Node(cp, "List", TL, new Vector2(6, 22), new Vector2(434, 200)), out _);
            // Settings (shared view, no own tabs)
            var settings = BuildSettingsView(a, standalone: false);

            var resume = Btn(a, "Resume", "계속하기", TL, new Vector2(12, 270), new Vector2(110, 20));
            var toTitle = Btn(a, "ToTitle", "타이틀로 (저장)", TL, new Vector2(470 - 122, 270), new Vector2(110, 20));
            UIKit.Show(tp, false); UIKit.Show(cp, false); UIKit.Show(settings, false);

            WireArray(v, "tabs", tabs);
            Wire(v, "abilitiesPage", ap.gameObject); Wire(v, "attrsPage", tp.gameObject); Wire(v, "chroniclePage", cp.gameObject);
            Wire(v, "settings", settings);
            Wire(v, "abilitiesHeader", aHead); Wire(v, "abilitiesList", aList);
            Wire(v, "unspent", unspent); WireArray(v, "attrLines", attrLines); WireArray(v, "attrPlus", plus);
            Wire(v, "statNames", statNames); Wire(v, "statValues", statValues);
            Wire(v, "chronicleHeader", cHead); Wire(v, "chronicleList", cList);
            Wire(v, "resume", resume); Wire(v, "toTitle", toTitle);
            return v;
        }

        static DialogueView BuildDialogue(Transform root)
        {
            // Bottom box: 76% wide, 140 tall, 10 above the bottom
            var host = Node(root, "Dialogue", BC, Vector2.zero, new Vector2(0, 140), BC);
            host.anchorMin = new Vector2(0.12f, 0); host.anchorMax = new Vector2(0.88f, 0); host.sizeDelta = new Vector2(0, 140);
            host.anchoredPosition = new Vector2(0, 10);
            Box(host, panelSpr).raycastTarget = true;
            var v = host.gameObject.AddComponent<DialogueView>();
            var pf = Node(host, "PortraitFrame", TL, new Vector2(10, 10), new Vector2(56, 56));
            Box(pf, slotSpr);
            var portrait = Pic(pf, "Portrait", TL, new Vector2(3, 3), new Vector2(50, 50), Baked("npc_owl_0"));
            var speaker = Label(host, "Speaker", "<b><color=#fff2b3>이야기꾼 올리</color></b>", TL, new Vector2(76, 8), new Vector2(380, 14));
            var body = Label(host, "Body", "대사가 여기에 표시된다.", TL, new Vector2(76, 24), new Vector2(380, 60));
            body.rectTransform.anchorMax = new Vector2(1, 1); body.rectTransform.sizeDelta = new Vector2(-90, 60);
            var opts = Node(host, "Options", TL, new Vector2(10, 86), new Vector2(0, 46));
            opts.anchorMax = new Vector2(1, 1); opts.sizeDelta = new Vector2(-20, 46);
            var grid = opts.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(220, 19); grid.spacing = new Vector2(10, 3); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
            var opt = Btn(opts, "Option", "1. 떠나기", TL, Vector2.zero, new Vector2(220, 19), 9);
            Wire(v, "portrait", portrait); Wire(v, "speaker", speaker); Wire(v, "body", body); Wire(v, "optionTemplate", opt);
            return v;
        }

        static DeathView BuildDeath(Transform root)
        {
            var host = Stretch(root, "Death");
            var v = host.gameObject.AddComponent<DeathView>();
            var a = Panel(host, "Panel", 320, 220);
            var title = Label(a, "Title", "영웅의 삶이 끝났다", TL, new Vector2(0, 10), new Vector2(320, 20), 17, TextAnchor.MiddleCenter, Pal.Hex("ff8a8a"), bold: true);
            var grave = Pic(a, "Grave", TL, new Vector2(144, 36), new Vector2(32, 44), Baked("prop_grave"));
            var epitaph = Label(a, "Epitaph", "<i>\"묘비명\"</i>", TL, new Vector2(16, 86), new Vector2(288, 30), 11, TextAnchor.MiddleCenter);
            var carried = Label(a, "Carried", "무덤에 남은 능력이 없다.", TL, new Vector2(16, 120), new Vector2(288, 40), 9, TextAnchor.UpperCenter, Pal.Hex("c9bcd8"));
            var next = Btn(a, "NextLife", "다음 삶 시작하기", TL, new Vector2(90, 220 - 32), new Vector2(140, 22));
            Wire(v, "title", title); Wire(v, "grave", grave); Wire(v, "epitaph", epitaph); Wire(v, "carried", carried); Wire(v, "nextLife", next);
            return v;
        }

        static EvolveView BuildEvolve(Transform root)
        {
            var host = Stretch(root, "Evolve");
            var v = host.gameObject.AddComponent<EvolveView>();
            const float W = 470, H = 330;
            var a = Panel(host, "Panel", W, H);
            Label(a, "Heading", "진화", TL, new Vector2(0, 8), new Vector2(W, 20), 17, TextAnchor.MiddleCenter, Pal.Hex("e6dcff"), bold: true);
            var toggle = Btn(a, "PipelineToggle", "과정 보기", TL, new Vector2(W - 70, 10), new Vector2(58, 16), 9);
            var status = Label(a, "Status", "플레이 방식을 분석하는 중...", TL, new Vector2(12, 32), new Vector2(W - 24, 16), 9, TextAnchor.MiddleCenter);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.resizeTextForBestFit = true; status.resizeTextMinSize = 7; status.resizeTextMaxSize = 9;

            float cw = (W - 24 - 16) / 3;
            var cards = new Object[3];
            for (int i = 0; i < 3; i++)
            {
                var r = Node(a, "Card " + (i + 1), TL, new Vector2(12 + i * (cw + 8), 52), new Vector2(cw, 150));
                var img = Box(r, slotSpr);
                var b = r.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                b.transition = Selectable.Transition.SpriteSwap;
                b.spriteState = new SpriteState { highlightedSprite = buttonSpr, pressedSprite = buttonHoverSpr, disabledSprite = slotSpr };
                var group = r.gameObject.AddComponent<CanvasGroup>();
                var head = Label(r, "Header", $"<color=#6b5c8a>{i + 1}</color>  ◇ 로컬", TL, new Vector2(6, 5), new Vector2(cw - 12, 12), 9);
                var title = Label(r, "Title", "<b><color=#fff2b3>능력 이름</color></b>", TL, new Vector2(6, 18), new Vector2(cw - 12, 30));
                var desc = Label(r, "Desc", "설명", TL, new Vector2(6, 48), new Vector2(cw - 12, 56), 9, color: Pal.Hex("c9bcd8"));
                var reason = Label(r, "Reason", "", TL, new Vector2(6, 104), new Vector2(cw - 12, 28), 9);
                var tags = Label(r, "Tags", "", BL, Vector2.zero, new Vector2(cw - 12, 12), 9);
                tags.rectTransform.pivot = BL; tags.rectTransform.anchoredPosition = new Vector2(6, 2);
                var lockBar = Box(Node(r, "LockBar", BL, Vector2.zero, new Vector2(cw - 16, 2), BL), whiteSpr, new Color(0.8f, 0.72f, 1f));
                lockBar.rectTransform.anchoredPosition = new Vector2(8, 2);
                lockBar.type = Image.Type.Filled; lockBar.fillMethod = Image.FillMethod.Horizontal;
                var card = r.gameObject.AddComponent<EvolveCard>();
                Wire(card, "button", b); Wire(card, "header", head); Wire(card, "title", title); Wire(card, "desc", desc);
                Wire(card, "reason", reason); Wire(card, "tags", tags); Wire(card, "lockBar", lockBar); Wire(card, "group", group);
                cards[i] = card;
            }

            // Chat
            var chat = Node(a, "Chat", TL, new Vector2(12, 210), new Vector2(W - 24, 78));
            Box(chat, slotSpr);
            var log = ScrollText(chat, "Log", Node(chat, "Log", TL, new Vector2(4, 3), new Vector2(W - 32, 50)), out var logScroll);
            var inputRt = Node(chat, "Input", TL, new Vector2(4, 56), new Vector2(W - 142, 18));
            Box(inputRt, slotSpr);
            var inText = TextOn(Stretch(inputRt, "Text", 4, 0, 4, 0), "", 9, TextAnchor.MiddleLeft, Color.white, shadow: false);
            inText.supportRichText = false;
            var ph = TextOn(Stretch(inputRt, "Placeholder", 4, 0, 4, 0), "예) 2번을 화염 대신 냉기로 바꿔 줘 / 대시와 어울리는 능력은?", 9, TextAnchor.MiddleLeft, Pal.Hex("6b5c8a"), shadow: false);
            var input = inputRt.gameObject.AddComponent<InputField>();
            input.textComponent = inText; input.placeholder = ph; input.characterLimit = 200;
            input.lineType = InputField.LineType.SingleLine;
            var send = Btn(chat, "Send", "보내기 (30G)", TL, new Vector2(W - 134, 56), new Vector2(106, 18), 9);
            var warn = Label(chat, "Warning", "", TR, Vector2.zero, new Vector2(300, 12), 9, TextAnchor.LowerRight);
            warn.rectTransform.pivot = BR; warn.rectTransform.anchoredPosition = new Vector2(-4, 2);

            var reroll = Btn(a, "Reroll", "다시 뽑기 (18 골드)", BL, new Vector2(12, -10), new Vector2(140, 20), 9);
            var skip = Btn(a, "Skip", "건너뛰기 (+20 골드)", BR, new Vector2(-12, -10), new Vector2(120, 20), 9);
            var hint = Label(a, "PickHint", "<color=#6b5c8a>카드 클릭 또는 1·2·3으로 선택</color>", BC, new Vector2(0, -13), new Vector2(W, 14), 9, TextAnchor.MiddleCenter);

            // Pipeline log over the cards
            var pp = Node(a, "Pipeline", TL, new Vector2(12, 52), new Vector2(W - 24, H - 90));
            Box(pp, panelSpr).raycastTarget = true;
            Label(pp, "Heading", "<b>AI 진화 과정</b> <color=#8c7aa8>— 플레이 기록 → 패턴 → AI 생성 → 검증 → 점수 → 이름</color>", TL, new Vector2(8, 6), new Vector2(W - 40, 12), 9);
            var pText = ScrollText(pp, "Log", Node(pp, "Log", TL, new Vector2(8, 22), new Vector2(W - 40, H - 120)), out _);
            UIKit.Show(pp, false);

            Wire(v, "status", status); Wire(v, "pipelineToggle", toggle); Wire(v, "pipelinePanel", pp.gameObject); Wire(v, "pipelineText", pText);
            WireArray(v, "cards", cards);
            Wire(v, "chatPanel", chat.gameObject); Wire(v, "chatLog", log); Wire(v, "chatWarning", warn); Wire(v, "chatScroll", logScroll);
            Wire(v, "chatInput", input); Wire(v, "send", send);
            Wire(v, "reroll", reroll); Wire(v, "skip", skip); Wire(v, "panel", a);
            return v;
        }

        static MapView BuildMap(Transform root)
        {
            var host = Stretch(root, "Map", 16, 16, 16, 16);
            Box(host, panelSpr).raycastTarget = true;
            var v = host.gameObject.AddComponent<MapView>();
            Label(host, "Title", "세계 지도", TC, new Vector2(0, 6), new Vector2(200, 16), 11, TextAnchor.MiddleCenter, UIKit.Highlight).rectTransform.pivot = TC;
            Label(host, "Help", "<color=#8c7aa8>드래그: 이동 · 휠: 확대 · C: 내 위치</color>", TL, new Vector2(10, 6), new Vector2(260, 16), 9);
            var close = Label(host, "Close", "Tab/Esc 닫기", TR, Vector2.zero, new Vector2(160, 16), 9, TextAnchor.UpperRight, UIKit.Muted);
            close.rectTransform.anchoredPosition = new Vector2(-10, -6);
            var viewRt = Stretch(host, "View", 8, 26, 8, 22);
            var mapCanvas = BuildMapCanvas(viewRt, 0, 11);
            var hit = viewRt.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var player = Pic(mapCanvas.transform, "Player", C, Vector2.zero, new Vector2(12, 12), Baked("rabbit_white_Idle0"));
            player.rectTransform.pivot = new Vector2(0.5f, 0);
            var left = Label(host, "FooterLeft", "확대 ×3.0", BL, Vector2.zero, new Vector2(300, 14), 9, color: UIKit.Muted);
            left.rectTransform.pivot = BL; left.rectTransform.anchoredPosition = new Vector2(10, 4);
            var right = Label(host, "FooterRight", "", BR, Vector2.zero, new Vector2(300, 14), 9, TextAnchor.LowerRight);
            right.rectTransform.pivot = BR; right.rectTransform.anchoredPosition = new Vector2(-10, 4);
            Wire(v, "map", mapCanvas); Wire(v, "closeHint", close); Wire(v, "footerLeft", left); Wire(v, "footerRight", right); Wire(v, "playerIcon", player);
            return v;
        }

        static ToastView BuildToasts(Transform root)
        {
            // Stack under the top of the screen (old IMGUI: y 70, 18 tall, 21 apart); ToastView sizes and places each box
            var host = Node(root, "Toasts", TC, Vector2.zero, new Vector2(600, 90), TC);
            host.anchoredPosition = new Vector2(0, -70);
            var item = Node(host, "Toast", TC, Vector2.zero, new Vector2(200, 18), TC);
            Box(item, slotSpr).raycastTarget = false;
            item.gameObject.AddComponent<CanvasGroup>();
            var label = TextOn(Stretch(item, "Text", 8, 0, 8, 0), "알림", 9, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var v = host.gameObject.AddComponent<ToastView>();
            Wire(v, "template", item);
            return v;
        }

        // ───────────────────────── Scenes ─────────────────────────

        static void EventSystemObject()
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        static void BuildMenuScene(GameObject canvas)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.07f, 0.12f);
            camGo.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<AudioListener>();
            new GameObject("Sfx").AddComponent<Sfx>();
            PrefabUtility.InstantiatePrefab(canvas);
            EventSystemObject();
            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        static void BuildGameScene(GameObject canvasPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera: orthographic, pixel-snapped by CameraRig; post-processing on, no AA
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.07f, 0.12f);
            cam.nearClipPlane = -50; cam.farClipPlane = 50;
            cam.allowMSAA = false;
            cam.allowHDR = true;
            cam.orthographicSize = 360f / (2f * Art.PPU);
            camGo.transform.position = new Vector3(0, -1.5f, -10);
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            camGo.AddComponent<CameraRig>();
            var volGo = new GameObject("Global Volume");
            volGo.transform.SetParent(camGo.transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = PostFxProfile();

            var fx = new GameObject("Fx").AddComponent<Fx>();
            Wire(fx, "font", fontBold);
            new GameObject("Sfx").AddComponent<Sfx>();

            var streamer = new GameObject("World").AddComponent<WorldStreamer>();
            Wire(streamer, "chunkPrefab", AssetDatabase.LoadAssetAtPath<ChunkView>(PrefabDir + "/World/Chunk.prefab"));
            var world = new GameObject("WorldManager").AddComponent<World>();
            world.Streamer = streamer;
            Wire(world, "firePrefab", AssetDatabase.LoadAssetAtPath<FireFx>(PrefabDir + "/World/Campfire.prefab"));

            var canvas = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab);
            EventSystemObject();

            var game = new GameObject("Game").AddComponent<Game>();
            game.Ui = canvas.GetComponent<GameUI>();
            Wire(game, "worldManager", world);

            // Editor-only picture of the starting town where the world will stream in, so the scene view is not empty.
            // It is a plain sprite (no game logic), hidden as soon as play starts, and stripped from builds (EditorOnly).
            var town = AssetDatabase.LoadAssetAtPath<Sprite>(UIDir + "/TownPreview.png");
            if (town != null)
            {
                var pv = new GameObject("World Preview (editor only)") { tag = "EditorOnly" };
                pv.transform.position = new Vector3(0, 2, 5);
                var sr = pv.AddComponent<SpriteRenderer>();
                sr.sprite = town;
                sr.sortingOrder = -32760;
                pv.AddComponent<EditorOnlyPreview>();
            }

            EditorSceneManager.SaveScene(scene, GameScenePath);
        }
    }
}
