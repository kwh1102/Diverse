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

        /// <summary>
        /// Re-lay the studio's preview box into a tidy grid (character | weapon · shot · effect / ground · two scenery objects /
        /// enemies). Patches the existing prefabs in place: positions only, plus the cells added since (labels, second scenery).
        /// </summary>
        [MenuItem("Diverse/디자인 스튜디오 미리보기 정리 (기존 프리팹에)")]
        public static void UpdateDesignPreviewLayout()
        {
            font = Resources.Load<Font>("Fonts/Galmuri11");
            fontBold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
            BuildFrames();
            foreach (var path in new[] { UIPrefabDir + "/MenuCanvas.prefab", UIPrefabDir + "/GameCanvas.prefab" })
                Patch(path, root =>
                {
                    var v = root.GetComponentInChildren<DesignStudioView>(true);
                    var pv = v != null ? v.transform.Find("Panel/Preview") : null;
                    if (pv == null) return false;
                    LayoutPreview(pv, v);
                    return true;
                });
            AssetDatabase.SaveAssets();
            Debug.Log("[Diverse] design preview layout updated");
        }

        static void LayoutPreview(Transform pv, DesignStudioView v)
        {
            void Place(string name, float x, float y, float w, float h)
            {
                var rt = pv.Find(name) as RectTransform;
                if (rt == null) return;
                rt.anchorMin = rt.anchorMax = rt.pivot = TL;
                rt.anchoredPosition = new Vector2(x, -y);
                rt.sizeDelta = new Vector2(w, h);
            }
            Text Heading(string name, string text)
            {
                var t = pv.Find(name)?.GetComponent<Text>() ?? Label(pv, name, "", TL, Vector2.zero, new Vector2(10, 10), 8);
                t.text = $"<color=#8c7aa8>{text}</color>";
                return t;
            }
            Heading("PreviewHeading", "캐릭터");
            Heading("AttackHeading", "무기 · 발사 · 효과");
            Heading("WorldHeading", "세계");
            Heading("EnemyHeading", "적");
            if (pv.Find("Decor2") == null)
            {
                var d2 = Pic(pv, "Decor2", TL, Vector2.zero, new Vector2(10, 10), null);
                d2.enabled = false;
                Wire(v, "decor2", d2);
            }
            Place("PreviewHeading", 6, 4, 96, 12);
            Place("Rabbit", 6, 16, 96, 96);
            Place("AttackHeading", 110, 4, 106, 12);
            Place("Weapon", 110, 18, 104, 30);
            if (pv.Find("FlipWeapon") == null) Wire(v, "flipWeapon", Btn(pv, "FlipWeapon", "무기 좌우 반전", TL, Vector2.zero, new Vector2(10, 10), 8));
            Place("FlipWeapon", 110, 49, 104, 13);
            Place("Projectile", 110, 68, 48, 28);
            Place("Slash", 162, 60, 52, 40);
            if (pv.Find("FlipEffects") == null) Wire(v, "flipEffects", Btn(pv, "FlipEffects", "효과 반전", TL, Vector2.zero, new Vector2(10, 10), 8));
            Place("FlipEffects", 162, 101, 52, 13);
            Place("WorldHeading", 6, 116, 96, 12);
            Place("Ground", 6, 130, 104, 58);
            Place("Tree", 114, 118, 48, 72);
            Place("Decor2", 166, 132, 46, 58);
            if (pv.Find("FixEnemies") == null) Wire(v, "fixEnemies", Btn(pv, "FixEnemies", "적 방향·잘림 고치기", TL, Vector2.zero, new Vector2(10, 10), 8));
            Place("EnemyHeading", 6, 194, 96, 12);
            Place("FixEnemies", 110, 193, 104, 13);
            for (int i = 0; i < 3; i++) Place("Enemy " + i, 14 + i * 70, 206, 40, 34);
            Place("EnemyNames", 4, 242, 212, 12);
        }

        /// <summary>Mode + save slot screen (SaveSelectView) and the title's "모드·세계 선택" button, added to the existing MenuCanvas.</summary>
        [MenuItem("Diverse/모드·세이브 슬롯 화면 추가 (기존 프리팹에)")]
        public static void InstallSaveSlots()
        {
            font = Resources.Load<Font>("Fonts/Galmuri11");
            fontBold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
            BuildFrames();
            Patch(UIPrefabDir + "/MenuCanvas.prefab", root =>
            {
                var menu = root.GetComponent<MenuUI>();
                if (menu == null || root.GetComponentInChildren<SaveSelectView>(true) != null) return false;
                var v = BuildSaveSelect(root.transform);
                UIKit.Show(v, false);
                Wire(menu, "slots", v);
                AddSlotButton(root.GetComponentInChildren<TitleView>(true));
                return true;
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[Diverse] save slot screen installed");
        }

        static void AddSlotButton(TitleView title)
        {
            var col = title != null ? title.transform.Find("Buttons") as RectTransform : null;
            if (col == null || col.Find("ChangeSlot") != null) return;
            var b = Btn(col, "ChangeSlot", "모드·세계 선택", TL, Vector2.zero, new Vector2(140, 22));
            var quit = col.Find("Quit");
            if (quit != null) b.transform.SetSiblingIndex(quit.GetSiblingIndex());
            col.sizeDelta = new Vector2(col.sizeDelta.x, 190);
            Wire(title, "slotButton", b);
        }

        static SaveSelectView BuildSaveSelect(Transform parent)
        {
            const float W = 420, H = 270;
            var shade = Stretch(parent, "SaveSelect");
            Box(shade, whiteSpr, new Color(0.05f, 0.03f, 0.08f, 0.45f)).raycastTarget = true;
            var v = shade.gameObject.AddComponent<SaveSelectView>();
            var a = Panel(shade, "Panel", W, H);
            Label(a, "Heading", "어떤 세계에서 살아갈까요?", TL, new Vector2(0, 8), new Vector2(W, 20), 17, TextAnchor.MiddleCenter, Pal.Hex("fff2d6"), bold: true);
            var normal = Btn(a, "NormalTab", "일반 모드", TL, new Vector2(20, 36), new Vector2(186, 22));
            var custom = Btn(a, "CustomTab", "커스텀 모드", TL, new Vector2(214, 36), new Vector2(186, 22));
            var desc = Label(a, "ModeDesc", "", TL, new Vector2(20, 64), new Vector2(380, 28), 9, color: Pal.Hex("c9bcd8"));

            var list = Node(a, "Slots", TL, new Vector2(20, 96), new Vector2(380, 140));
            var vl = list.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 6; vl.childControlHeight = false; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            var slot = Btn(list, "Slot", "세계 1", TL, Vector2.zero, new Vector2(380, 40), 10);
            var txt = slot.transform.Find("Text") as RectTransform;
            txt.offsetMin = new Vector2(10, 2); txt.offsetMax = new Vector2(-72, -2);
            var t = txt.GetComponent<Text>();
            t.alignment = TextAnchor.MiddleLeft; t.horizontalOverflow = HorizontalWrapMode.Wrap;
            Btn(slot.transform, "Delete", "삭제", new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(58, 22), 9).GetComponent<RectTransform>().pivot = new Vector2(1, 0.5f);

            Btn(a, "Back", "뒤로", TL, new Vector2(20, H - 32), new Vector2(80, 22));
            Wire(v, "normalTab", normal); Wire(v, "customTab", custom); Wire(v, "modeDesc", desc);
            Wire(v, "slotTemplate", slot);
            LayoutSaveSelect(v);
            return v;
        }

        /// <summary>
        /// Title keeps only 모드·세계 선택 / 설정 / 종료; each world row on the slot screen gets 이어하기·새 삶 / 삶 끝내기 / 삭제
        /// and the slot screen a 뒤로 button. Patches the existing MenuCanvas in place.
        /// </summary>
        [MenuItem("Diverse/메인 메뉴 정리 (기존 프리팹에)")]
        public static void UpdateMenuLayout()
        {
            font = Resources.Load<Font>("Fonts/Galmuri11");
            fontBold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
            BuildFrames();
            Patch(UIPrefabDir + "/MenuCanvas.prefab", root =>
            {
                var title = root.GetComponentInChildren<TitleView>(true);
                var col = title != null ? title.transform.Find("Buttons") as RectTransform : null;
                if (col != null)
                {
                    foreach (var n in new[] { "Continue", "Abandon", "Start", "ResetWorld" })
                    {
                        var b = col.Find(n);
                        if (b != null) Object.DestroyImmediate(b.gameObject);
                    }
                    var slotB = col.Find("ChangeSlot");
                    if (slotB != null) { slotB.SetAsFirstSibling(); Wire(title, "slotButton", slotB.GetComponent<Button>()); }
                    col.sizeDelta = new Vector2(col.sizeDelta.x, 90);
                }
                var v = root.GetComponentInChildren<SaveSelectView>(true);
                if (v != null) LayoutSaveSelect(v);
                return true;
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[Diverse] main menu layout updated");
        }

        /// <summary>Slot screen: a wider panel, per-row Play / Abandon / Delete buttons, and 뒤로 (adds what is missing).</summary>
        static void LayoutSaveSelect(SaveSelectView v)
        {
            const float W = 480, H = 290;
            var a = v.transform.Find("Panel") as RectTransform;
            a.sizeDelta = new Vector2(W, H);
            void Place(Transform parent, string name, float x, float y, float w, float h)
            {
                var rt = parent.Find(name) as RectTransform;
                if (rt == null) return;
                rt.anchorMin = rt.anchorMax = rt.pivot = TL;
                rt.anchoredPosition = new Vector2(x, -y);
                rt.sizeDelta = new Vector2(w, h);
            }
            Place(a, "Heading", 0, 8, W, 20);
            Place(a, "NormalTab", 20, 36, 216, 22);
            Place(a, "CustomTab", 244, 36, 216, 22);
            Place(a, "ModeDesc", 20, 64, 440, 28);
            Place(a, "Slots", 20, 96, 440, 156);

            var quit = a.Find("Quit");
            if (quit != null) { quit.name = "Back"; UIKit.Label(quit.GetComponent<Button>(), "뒤로"); }
            if (a.Find("Back") == null) Btn(a, "Back", "뒤로", TL, Vector2.zero, new Vector2(80, 22));
            Place(a, "Back", 20, H - 32, 80, 22);
            Wire(v, "backButton", a.Find("Back").GetComponent<Button>());

            var slot = a.Find("Slots/Slot") as RectTransform;
            slot.sizeDelta = new Vector2(440, 46);
            var txt = slot.Find("Text") as RectTransform;
            txt.offsetMin = new Vector2(10, 2); txt.offsetMax = new Vector2(-248, -2);
            // Right-aligned row buttons: [Play][Abandon][Delete]
            (string name, string caption, float w)[] row = { ("Delete", "삭제", 50), ("Abandon", "삶 끝내기", 76), ("Play", "이어하기", 104) };
            float right = 8;
            foreach (var (name, caption, w) in row)
            {
                var b = slot.Find(name) as RectTransform ?? Btn(slot, name, caption, new Vector2(1, 0.5f), Vector2.zero, new Vector2(w, 24), 9).GetComponent<RectTransform>();
                b.anchorMin = b.anchorMax = b.pivot = new Vector2(1, 0.5f);
                b.anchoredPosition = new Vector2(-right, 0);
                b.sizeDelta = new Vector2(w, 24);
                right += w + 4;
            }
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
            LayoutPreview(pv, v);
            Wire(v, "chassisRow", row.gameObject);
            Wire(v, "weaponBase", wb); Wire(v, "perkBase", pb);
            Wire(v, "weaponBaseButton", wbb); Wire(v, "perkBaseButton", pbb);
            Wire(v, "startButton", start); Wire(v, "backButton", back); Wire(v, "classicButton", classic);
            return v;
        }
    }
}
