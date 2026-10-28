using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Diverse.EditorTools.ProjectAssetBuilder;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Design studio panel (DesignStudioView): built into fresh canvases by UIBuilder, and added to EXISTING
    /// MenuCanvas/GameCanvas prefabs in place by InstallDesignStudio (never rebuilds them, so editor tweaks survive).
    /// </summary>
    public static partial class UIBuilder
    {
        [MenuItem("Diverse/디자인 스튜디오 UI 추가 (기존 프리팹에)")]
        public static void InstallDesignStudio()
        {
            font = Resources.Load<Font>("Fonts/Galmuri11");
            fontBold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
            BuildFrames();

            Patch(UIPrefabDir + "/MenuCanvas.prefab", root =>
            {
                var menu = root.GetComponent<MenuUI>();
                if (root.GetComponentInChildren<DesignStudioView>(true) != null || menu == null) return false;
                var v = BuildDesignStudio(root.transform, false);
                UIKit.Show(v, false);
                Wire(menu, "design", v);
                return true;
            });
            Patch(UIPrefabDir + "/GameCanvas.prefab", root =>
            {
                var pause = root.GetComponentInChildren<PauseView>(true);
                if (root.GetComponentInChildren<DesignStudioView>(true) != null || pause == null) return false;
                var v = BuildDesignStudio(root.transform, true);
                // Above the pause menu, under the tooltip
                var tooltip = root.GetComponentInChildren<TooltipView>(true);
                if (tooltip != null && tooltip.transform.parent == root.transform) v.transform.SetSiblingIndex(tooltip.transform.GetSiblingIndex());
                UIKit.Show(v, false);
                WireDesignButton(pause, v);
                return true;
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[Diverse] design studio installed");
        }

        static void Patch(string path, System.Func<GameObject, bool> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { if (edit(root)) PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void WireDesignButton(PauseView pause, DesignStudioView studio)
        {
            var panel = pause.transform.Find("Panel");
            var b = Btn(panel != null ? panel : pause.transform, "Design", "디자인 수정", TL, new Vector2(180, 270), new Vector2(110, 20));
            Wire(pause, "designButton", b);
            Wire(pause, "design", studio);
        }

        static DesignStudioView BuildDesignStudio(Transform parent, bool inGame)
        {
            const float W = 600, H = 330;
            var shade = Stretch(parent, "DesignStudio");
            Box(shade, whiteSpr, new Color(0.05f, 0.03f, 0.08f, inGame ? 0.7f : 0.45f)).raycastTarget = true;
            var v = shade.gameObject.AddComponent<DesignStudioView>();
            var a = Panel(shade, "Panel", W, H);
            Label(a, "Heading", "어떤 모습으로 살아갈까요?", TL, new Vector2(0, 8), new Vector2(W, 20), 17, TextAnchor.MiddleCenter, Pal.Hex("fff2d6"), bold: true);

            // Preview (drawn locally from the pack — free)
            var pv = Node(a, "Preview", TL, new Vector2(16, 34), new Vector2(220, 258));
            Box(pv, slotSpr);
            Label(pv, "PreviewHeading", "<color=#8c7aa8>미리보기</color>", TL, new Vector2(6, 4), new Vector2(100, 12), 8);
            var hero = Pic(pv, "Rabbit", TL, new Vector2(6, 16), new Vector2(96, 96), Baked("rabbit_white_Idle0"));
            var heroWeapon = Pic(pv, "Weapon", TL, new Vector2(54, 62), new Vector2(48, 28), Baked("weapon_Greatsword"));
            Label(pv, "AttackHeading", "<color=#8c7aa8>공격</color>", TL, new Vector2(112, 16), new Vector2(100, 12), 8);
            var projectile = Pic(pv, "Projectile", TL, new Vector2(112, 32), new Vector2(40, 24), Baked("bolt"));
            var slash = Pic(pv, "Slash", TL, new Vector2(156, 28), new Vector2(56, 56), null);
            slash.enabled = false;               // filled at runtime (UIKit.Sprite enables it)
            Label(pv, "WorldHeading", "<color=#8c7aa8>세계</color>", TL, new Vector2(6, 118), new Vector2(100, 12), 8);
            var ground = Pic(pv, "Ground", TL, new Vector2(6, 132), new Vector2(136, 68), null);
            ground.enabled = false;
            var tree = Pic(pv, "Tree", TL, new Vector2(140, 116), new Vector2(72, 84), Baked("tree_Meadow_1"));
            var enemies = new Image[3];
            for (int i = 0; i < 3; i++) enemies[i] = Pic(pv, "Enemy " + i, TL, new Vector2(10 + i * 70, 204), new Vector2(36, 36), Baked($"enemy_{(i == 0 ? "shroom" : i == 1 ? "fox" : "wisp")}_0"));
            var enemyNames = Label(pv, "EnemyNames", "버섯 꼬마 · 여우 산적 · 떠도는 넋", TL, new Vector2(4, 242), new Vector2(212, 12), 8, TextAnchor.MiddleCenter, Pal.Hex("c9bcd8"));

            // Tabs + prompt
            const float x0 = 246, cw = 338;
            var tabsRt = Node(a, "Tabs", TL, new Vector2(x0, 34), new Vector2(cw, 20));
            var tl = tabsRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            tl.spacing = 4; tl.childControlWidth = true; tl.childForceExpandWidth = true; tl.childControlHeight = true;
            string[] names = { "캐릭터", "무기·공격", "세계", "적" };
            var tabs = new Object[4];
            for (int i = 0; i < 4; i++) tabs[i] = Btn(tabsRt, "Tab " + i, names[i], TL, Vector2.zero, new Vector2(80, 20), 10);
            var hint = Label(a, "TabHint", "토끼의 털·옷·모자를 적어 주세요.", TL, new Vector2(x0, 58), new Vector2(cw, 14), 9, color: Pal.Hex("c9bcd8"));

            var inputRt = Node(a, "Prompt", TL, new Vector2(x0, 74), new Vector2(cw, 52));
            Box(inputRt, slotSpr);
            var inText = TextOn(Stretch(inputRt, "Text", 6, 4, 6, 4), "", 10, TextAnchor.UpperLeft, Color.white, shadow: false);
            inText.supportRichText = false;
            var ph = TextOn(Stretch(inputRt, "Placeholder", 6, 4, 6, 4), "예) 민트색 털에 남색 망토, 마법사 모자를 쓴 토끼", 10, TextAnchor.UpperLeft, Pal.Hex("6b5c8a"), shadow: false);
            var input = inputRt.gameObject.AddComponent<InputField>();
            input.textComponent = inText; input.placeholder = ph; input.characterLimit = DesignGuard.MaxPrompt;
            input.lineType = InputField.LineType.MultiLineNewline;

            var gen = Btn(a, "Generate", "디자인 만들기", TL, new Vector2(x0, 130), new Vector2(130, 20), 10);
            var undo = Btn(a, "Undo", "되돌리기", TL, new Vector2(x0 + 136, 130), new Vector2(76, 20), 10);
            var reset = Btn(a, "ResetTab", "이 탭 기본값", TL, new Vector2(x0 + 218, 130), new Vector2(120, 20), 10);
            var info = Label(a, "Info", "", TL, new Vector2(x0, 156), new Vector2(cw, 62), 9);
            var status = Label(a, "Status", "", TL, new Vector2(x0, 220), new Vector2(cw, 26), 8, color: Pal.Hex("8c7aa8"));

            // Chassis: which existing weapon / perk the design plays as (hidden in-game)
            var row = Node(a, "Chassis", TL, new Vector2(x0, 250), new Vector2(cw, 34));
            var wb = Label(row, "WeaponBase", "전투 방식 대검", TL, new Vector2(0, 0), new Vector2(262, 15), 9);
            var wbb = Btn(row, "WeaponBaseButton", "바꾸기", TL, new Vector2(270, 0), new Vector2(68, 15), 9);
            var pb = Label(row, "PerkBase", "계승 능력 흰 토끼", TL, new Vector2(0, 18), new Vector2(262, 15), 9);
            var pbb = Btn(row, "PerkBaseButton", "바꾸기", TL, new Vector2(270, 18), new Vector2(68, 15), 9);

            var back = Btn(a, "Back", "뒤로", TL, new Vector2(14, H - 32), new Vector2(70, 22));
            var classic = Btn(a, "Classic", "기본 캐릭터", TL, new Vector2(92, H - 32), new Vector2(110, 22));
            var start = Btn(a, "StartLife", "삶을 시작하기", TL, new Vector2(W - 150, H - 32), new Vector2(136, 22));
            if (inGame) UIKit.Show(classic, false);

            WireArray(v, "tabs", tabs);
            Wire(v, "tabHint", hint); Wire(v, "prompt", input);
            Wire(v, "generate", gen); Wire(v, "undo", undo); Wire(v, "resetTab", reset);
            Wire(v, "info", info); Wire(v, "status", status);
            Wire(v, "hero", hero); Wire(v, "heroWeapon", heroWeapon); Wire(v, "projectile", projectile); Wire(v, "slash", slash);
            Wire(v, "ground", ground); Wire(v, "tree", tree);
            WireArray(v, "enemies", enemies);
            Wire(v, "enemyNames", enemyNames);
            Wire(v, "chassisRow", row.gameObject);
            Wire(v, "weaponBase", wb); Wire(v, "perkBase", pb);
            Wire(v, "weaponBaseButton", wbb); Wire(v, "perkBaseButton", pbb);
            Wire(v, "startButton", start); Wire(v, "backButton", back); Wire(v, "classicButton", classic);
            return v;
        }
    }
}
