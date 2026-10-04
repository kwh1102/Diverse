using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// The whole UI. Uses IMGUI, but with a pixel font (Galmuri) + pixel 9-slice frames for a pixel-art look.
    /// Screens: title / character select / HUD / evolution / map / pause (abilities, stats, chronicle, settings, key bindings) / dialogue / death
    /// </summary>
    public partial class UI : MonoBehaviour
    {
        Game g;
        Font font, fontBold;
        float scale = 2;
        GUIStyle label, labelC, labelR, title, small, smallC, button, panel, box;
        Texture2D panelTex, panelDarkTex, buttonTex, buttonHoverTex, whiteTex, slotTex;
        readonly List<(string text, float t)> toasts = new List<(string, float)>();
        string tab = "abilities";
        int selCostume, selWeapon;
        float bossBarShown;
        Vector2 scroll;

        public static UI Create(Game game)
        {
            var go = new GameObject("UI");
            var ui = go.AddComponent<UI>();
            ui.g = game;
            ui.font = Resources.Load<Font>("Fonts/Galmuri11");
            ui.fontBold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
            ui.BuildTextures();
            var w = game.World;
            ui.selCostume = Mathf.Max(0, DB.Costumes.FindIndex(c => c.id == w.lastCostume));
            ui.selWeapon = w.lastWeapon;
            return ui;
        }

        void BuildTextures()
        {
            panelTex = Frame(Pal.Hex("2b2238"), Pal.Hex("4a3d5c"), Pal.Hex("e8d8c0"), 240);
            panelDarkTex = Frame(Pal.Hex("1b1524"), Pal.Hex("33294a"), Pal.Hex("8c7aa8"), 230);
            buttonTex = Frame(Pal.Hex("3d3150"), Pal.Hex("564670"), Pal.Hex("c9b8e8"), 255);
            buttonHoverTex = Frame(Pal.Hex("5a4878"), Pal.Hex("7a649e"), Pal.Hex("fff2b3"), 255);
            slotTex = Frame(Pal.Hex("1b1524"), Pal.Hex("2b2238"), Pal.Hex("6b5c8a"), 255);
            whiteTex = Texture2D.whiteTexture;
        }

        /// <summary>Pixel-style 12x12 9-slice frame texture (1px dark border + 1px highlight + fill).</summary>
        static Texture2D Frame(Color32 fill, Color32 inner, Color32 edge, byte alpha)
        {
            var cv = new PixelCanvas(12, 12);
            var dark = Pal.Hex("120d18");
            cv.Rect(1, 0, 10, 12, dark); cv.Rect(0, 1, 12, 10, dark);
            cv.Rect(1, 1, 10, 10, edge);
            cv.Rect(2, 2, 8, 8, inner);
            cv.Rect(3, 3, 6, 6, Pal.A(fill, alpha));
            cv.Set(2, 9, Pal.White);
            return cv.ToTexture();
        }

        void Styles()
        {
            scale = Mathf.Max(1, Mathf.Floor(Screen.height / 360f));
            int fs = Mathf.RoundToInt(11 * scale);
            GUIStyle Make(Font f, int size, TextAnchor a, Color c, bool wrap = true) => new GUIStyle
            {
                font = f, fontSize = size, alignment = a, wordWrap = wrap, richText = true,
                normal = { textColor = c },
            };
            label = Make(font, fs, TextAnchor.UpperLeft, Pal.Hex("f2e8d8"));
            labelC = Make(font, fs, TextAnchor.MiddleCenter, Pal.Hex("f2e8d8"));
            labelR = Make(font, fs, TextAnchor.MiddleRight, Pal.Hex("f2e8d8"));
            small = Make(font, Mathf.RoundToInt(fs * 0.85f), TextAnchor.UpperLeft, Pal.Hex("c9bcd8"));
            smallC = Make(font, Mathf.RoundToInt(fs * 0.85f), TextAnchor.MiddleCenter, Pal.Hex("c9bcd8"));
            title = Make(fontBold, Mathf.RoundToInt(fs * 1.6f), TextAnchor.MiddleCenter, Pal.Hex("fff2d6"));
            int b = Mathf.RoundToInt(4 * scale);
            button = new GUIStyle
            {
                font = font, fontSize = fs, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true,
                normal = { background = buttonTex, textColor = Pal.Hex("f2e8d8") },
                hover = { background = buttonHoverTex, textColor = Pal.Hex("fff8e0") },
                active = { background = buttonHoverTex, textColor = Color.white },
                border = new RectOffset(4, 4, 4, 4), padding = new RectOffset(b * 2, b * 2, b, b),
            };
            panel = new GUIStyle { normal = { background = panelTex }, border = new RectOffset(4, 4, 4, 4), padding = new RectOffset(b * 3, b * 3, b * 3, b * 3) };
            box = new GUIStyle { normal = { background = slotTex }, border = new RectOffset(4, 4, 4, 4), padding = new RectOffset(b, b, b, b) };
            panelTex.filterMode = buttonTex.filterMode = buttonHoverTex.filterMode = slotTex.filterMode = panelDarkTex.filterMode = FilterMode.Point;
            if (font != null) font.material.mainTexture.filterMode = FilterMode.Point;
            if (fontBold != null) fontBold.material.mainTexture.filterMode = FilterMode.Point;
        }

        public void Toast(string text)
        {
            toasts.Add((text, Time.unscaledTime));
            if (toasts.Count > 4) toasts.RemoveAt(0);
        }

        public void OnOverlayOpened(GameState s, string t)
        {
            if (s == GameState.Map) MapOpened();
            if (t != null) tab = t;
            scroll = Vector2.zero;
            rebinding = null;
        }

        public void OpenTab(string t) => tab = t;
        public void CloseDialogue() { Dialogue.Current = null; }

        void OnGUI()
        {
            if (font == null) return;
            Styles();
            GUI.depth = 0;
            var s = g.State;
            g.UiCapturingMouse = false;
            switch (s)
            {
                case GameState.Title: DrawTitle(); break;
                case GameState.CharacterSelect: DrawSelect(); break;
                case GameState.Playing: DrawHud(); break;
                case GameState.Evolving: DrawHud(); DrawEvolve(); break;
                case GameState.Map: DrawMap(); break;
                case GameState.Paused: DrawHud(); DrawPause(); break;
                case GameState.Settings: DrawHud(); DrawPause(); break;
                case GameState.Dialogue: DrawHud(); DrawDialogue(); break;
                case GameState.Dead: DrawHud(); DrawDeath(); break;
            }
            DrawToasts();
            if (s != GameState.Playing) g.UiCapturingMouse = true;
        }

        // ───────────────────────── Shared ─────────────────────────

        float U(float v) => v * scale;

        void Shadowed(Rect r, string text, GUIStyle st, Color? col = null)
        {
            var c = st.normal.textColor;
            st.normal.textColor = new Color(0.05f, 0.03f, 0.08f, 0.9f);
            GUI.Label(new Rect(r.x + scale, r.y + scale, r.width, r.height), text, st);
            st.normal.textColor = col ?? c;
            GUI.Label(r, text, st);
            st.normal.textColor = c;
        }

        void Bar(Rect r, float k, Color fill, Color back, string text = null)
        {
            GUI.DrawTexture(new Rect(r.x - scale, r.y - scale, r.width + 2 * scale, r.height + 2 * scale), whiteTex, ScaleMode.StretchToFill, true, 0, new Color(0.07f, 0.04f, 0.1f), 0, 0);
            GUI.DrawTexture(r, whiteTex, ScaleMode.StretchToFill, true, 0, back, 0, 0);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), whiteTex, ScaleMode.StretchToFill, true, 0, fill, 0, 0);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), scale), whiteTex, ScaleMode.StretchToFill, true, 0, new Color(1, 1, 1, 0.35f), 0, 0);
            if (text != null) Shadowed(r, text, smallC);
        }

        void Sprite(Rect r, Sprite s, bool flip = false, Color? tint = null)
        {
            if (s == null) return;
            var t = s.texture;
            var tr = s.textureRect;
            var uv = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height);
            if (flip) { uv.x += uv.width; uv.width = -uv.width; }
            // Integer scaling while preserving aspect ratio
            float k = Mathf.Max(1, Mathf.Floor(Mathf.Min(r.width / tr.width, r.height / tr.height)));
            var dst = new Rect(r.x + (r.width - tr.width * k) / 2, r.y + (r.height - tr.height * k) / 2, tr.width * k, tr.height * k);
            var old = GUI.color;
            if (tint.HasValue) GUI.color = tint.Value;
            GUI.DrawTextureWithTexCoords(dst, t, uv);
            GUI.color = old;
        }

        bool Btn(Rect r, string text, bool enabled = true)
        {
            var old = GUI.enabled;
            GUI.enabled = enabled;
            bool b = GUI.Button(r, text, button);
            GUI.enabled = old;
            if (b) Sfx.Play("ui_select", 0.5f);
            return b;
        }

        void Dim(float a = 0.55f) => GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), whiteTex, ScaleMode.StretchToFill, true, 0, new Color(0.05f, 0.03f, 0.08f, a), 0, 0);

        Rect Center(float w, float h) => new Rect((Screen.width - U(w)) / 2, (Screen.height - U(h)) / 2, U(w), U(h));

        void DrawToasts()
        {
            float y = U(70);
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - toasts[i].t;
                if (age > 4.5f) { toasts.RemoveAt(i); continue; }
            }
            foreach (var (text, t) in toasts)
            {
                float age = Time.unscaledTime - t;
                float a = age < 0.2f ? age / 0.2f : age > 3.8f ? 1 - (age - 3.8f) / 0.7f : 1;
                var size = small.CalcSize(new GUIContent(text));
                float w = Mathf.Min(size.x + U(16), Screen.width * 0.8f);
                var r = new Rect((Screen.width - w) / 2, y - (1 - Mathf.Clamp01(age / 0.2f)) * U(8), w, U(18));
                GUI.color = new Color(1, 1, 1, a);
                GUI.Box(r, GUIContent.none, box);
                Shadowed(r, text, smallC);
                GUI.color = Color.white;
                y += U(21);
            }
        }

        // ───────────────────────── Title ─────────────────────────

        void DrawTitle()
        {
            Dim(0.35f);
            float t = Time.unscaledTime;
            var tr = new Rect(0, Screen.height * 0.18f + Mathf.Sin(t * 1.5f) * U(2), Screen.width, U(40));
            var st = new GUIStyle(title) { fontSize = Mathf.RoundToInt(title.fontSize * 1.6f) };
            Shadowed(tr, "World Chronicle", st, Pal.Hex("fff2d6"));
            Shadowed(new Rect(0, tr.yMax + U(4), Screen.width, U(16)), "— 세계는 당신의 삶을 기억한다 —", labelC, Pal.Hex("c9b8ff"));

            // Rabbits
            float rx = Screen.width / 2 - U(70);
            for (int i = 0; i < 6; i++)
            {
                var c = DB.Costumes[i];
                var pose = (int)(t * 3 + i) % 2 == 0 ? Pose.Idle0 : Pose.Idle1;
                Sprite(new Rect(rx + i * U(24) - U(10), tr.yMax + U(26), U(26) * 2, U(30) * 2), Art.Rabbit(c, pose), i >= 3);
            }

            float bw = 140, bh = 22;
            var r = new Rect((Screen.width - U(bw)) / 2, Screen.height * 0.62f, U(bw), U(bh));
            string start = g.World.lifeCount == 0 ? "새로운 삶 시작" : $"이어하기 ({g.World.lifeCount + 1}번째 삶)";
            if (Btn(r, start)) g.State = GameState.CharacterSelect;
            r.y += U(bh + 6);
            if (Btn(r, "설정")) { tab = "settings"; g.State = GameState.Settings; }
            r.y += U(bh + 6);
            if (g.World.lifeCount > 0 && Btn(r, confirmReset ? "정말 세계를 초기화할까요?" : "새로운 세계 시작")) { if (confirmReset) { g.ResetWorld(); confirmReset = false; } else confirmReset = true; }
            r.y += U(bh + 6);
            if (Btn(r, "종료")) { Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
            string ai = AiClient.HasKey ? (Game.Settings.aiEnabled ? $"AI 진화: 켜짐 ({Game.Settings.aiModel})" : "AI 진화: 꺼짐 (키 있음)") : "AI 진화: 로컬 모드 (API 키 없음)";
            Shadowed(new Rect(U(8), Screen.height - U(18), Screen.width, U(14)), $"{ai}   ·   세계 시드 {g.World.seed}   ·   기억의 조각 {g.World.memoryShards}   ·   탑 {g.World.towerFloor}층", small);
        }
        bool confirmReset;

        // ───────────────────────── Character select ─────────────────────────

        void DrawSelect()
        {
            Dim(0.6f);
            var area = Center(440, 300);
            GUI.Box(area, GUIContent.none, panel);
            Shadowed(new Rect(area.x, area.y + U(8), area.width, U(20)), "누구로 살아갈까요?", title);

            // Costume preview
            var c = DB.Costumes[selCostume];
            var w = DB.W((WeaponKind)selWeapon);
            var pv = new Rect(area.x + U(16), area.y + U(40), U(120), U(120));
            GUI.Box(pv, GUIContent.none, box);
            float t = Time.unscaledTime;
            var pose = (Pose)((int)Pose.Run0 + (int)(t * 8) % 4);
            Sprite(new Rect(pv.x + U(10), pv.y + U(10), pv.width - U(20), pv.height - U(20)), Art.Rabbit(c, (int)(t * 0.5f) % 2 == 0 ? pose : Pose.Idle0));
            var wr = new Rect(pv.x + U(50), pv.y + U(56), U(50), U(30));
            Sprite(wr, Art.Weapon(w.kind));

            // Costume list
            float x0 = area.x + U(146), y0 = area.y + U(40);
            Shadowed(new Rect(x0, y0, U(280), U(14)), "<b>의상</b>", label);
            for (int i = 0; i < DB.Costumes.Count; i++)
            {
                var cc = DB.Costumes[i];
                var r = new Rect(x0 + (i % 6) * U(46), y0 + U(16), U(42), U(42));
                GUI.Box(r, GUIContent.none, i == selCostume ? button : box);
                Sprite(new Rect(r.x + U(3), r.y + U(3), r.width - U(6), r.height - U(6)), Art.Rabbit(cc, Pose.Idle0));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { selCostume = i; Sfx.Play("ui"); }
            }
            Shadowed(new Rect(x0, y0 + U(62), U(280), U(30)), $"<b>{c.name}</b>  <color=#fff2b3>{c.perk}</color>", label);

            // Weapon list
            y0 += U(84);
            Shadowed(new Rect(x0, y0, U(280), U(14)), "<b>시작 무기</b>", label);
            for (int i = 0; i < 6; i++)
            {
                var ww = DB.W((WeaponKind)i);
                var r = new Rect(x0 + (i % 3) * U(94), y0 + U(16) + (i / 3) * U(26), U(90), U(22));
                if (GUI.Button(r, ww.name, i == selWeapon ? new GUIStyle(button) { normal = { background = buttonHoverTex, textColor = Pal.Hex("fff2b3") } } : button)) { selWeapon = i; Sfx.Play("ui"); }
            }
            Shadowed(new Rect(x0, y0 + U(72), U(285), U(40)), w.desc, small);

            // Skill preview
            float sy = area.y + U(170);
            Shadowed(new Rect(area.x + U(16), sy, U(120), U(14)), "<b>스킬</b>", label);
            string[] keys = { "Q", "W", "E", "R" };
            for (int i = 0; i < 4; i++)
            {
                var sd = SkillDB.Get(w.skills[i]);
                var r = new Rect(area.x + U(16), sy + U(16) + i * U(18), U(122), U(16));
                Shadowed(r, $"<color=#c9b8ff>{keys[i]}</color> {sd.name}", small);
            }

            var bs = new Rect(area.x + area.width - U(150), area.yMax - U(34), U(136), U(24));
            if (Btn(bs, "삶을 시작하기")) g.StartRun(c, (WeaponKind)selWeapon);
            if (Btn(new Rect(area.x + U(14), area.yMax - U(34), U(70), U(24)), "뒤로")) g.State = GameState.Title;
            if (Controls.KeyDown(UnityEngine.InputSystem.Key.Enter)) g.StartRun(c, (WeaponKind)selWeapon);
        }
    }
}
