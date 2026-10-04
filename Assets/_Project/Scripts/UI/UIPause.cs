using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Diverse
{
    /// <summary>
    /// Pause menu: abilities / stats / chronicle / settings / key bindings tabs.
    /// </summary>
    public partial class UI
    {
        Act? rebinding;
        InputActionRebindingExtensions.RebindingOperation rebindOp;

        void DrawPause()
        {
            Dim(0.6f);
            var area = Center(470, 300);
            GUI.Box(area, GUIContent.none, panel);
            bool inGame = g.Player != null;
            string[] tabs = inGame ? new[] { "abilities", "attrs", "chronicle", "settings", "keys" } : new[] { "settings", "keys" };
            string[] names = inGame ? new[] { "능력", "능력치", "연대기", "설정", "키 설정" } : new[] { "설정", "키 설정" };
            if (!tabs.Contains(tab)) tab = tabs[0];
            float tw = (area.width - U(24)) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                var r = new Rect(area.x + U(12) + i * tw, area.y + U(10), tw - U(4), U(20));
                var st = tab == tabs[i] ? new GUIStyle(button) { normal = { background = buttonHoverTex, textColor = Pal.Hex("fff2b3") } } : button;
                if (GUI.Button(r, names[i], st)) { tab = tabs[i]; scroll = Vector2.zero; Sfx.Play("ui"); }
            }
            var body = new Rect(area.x + U(12), area.y + U(36), area.width - U(24), area.height - U(72));
            switch (tab)
            {
                case "abilities": DrawAbilities(body); break;
                case "attrs": DrawAttrs(body); break;
                case "chronicle": DrawChronicle(body); break;
                case "settings": DrawSettings(body); break;
                case "keys": DrawKeys(body); break;
            }
            var br = new Rect(area.x + U(12), area.yMax - U(30), U(110), U(20));
            if (inGame)
            {
                if (Btn(br, "계속하기")) g.CloseOverlay();
                if (Btn(new Rect(area.xMax - U(122), br.y, U(110), U(20)), "타이틀로 (저장)"))
                {
                    SaveSystem.SaveWorld(g.World);
                    g.CloseOverlay();
                    g.ToCharacterSelect();
                    g.State = GameState.Title;
                }
            }
            else if (Btn(br, "뒤로")) { g.State = GameState.Title; Controls.Save(Game.Settings); }
        }

        void DrawAbilities(Rect r)
        {
            var p = g.Player;
            GUI.Box(r, GUIContent.none, box);
            var inner = new Rect(r.x + U(6), r.y + U(6), r.width - U(12), r.height - U(12));
            float h = Mathf.Max(inner.height, p.Abilities.Owned.Count * U(34) + U(40));
            scroll = GUI.BeginScrollView(inner, scroll, new Rect(0, 0, inner.width - U(14), h), GUIStyle.none, GUIStyle.none);
            float y = 0;
            Shadowed(new Rect(0, y, inner.width, U(14)), $"<b>{p.Weapon.name}</b> · {p.Costume.name} <color=#fff2b3>({p.Costume.perk})</color>", small); y += U(16);
            if (p.Abilities.Owned.Count == 0) GUI.Label(new Rect(0, y, inner.width, U(30)), "<color=#8c7aa8>아직 능력이 없다. 레벨이 오르면 플레이 방식이 능력으로 진화한다.</color>", small);
            foreach (var a in p.Abilities.Owned)
            {
                string src = a.source == "ai" ? "<color=#c9b8ff>✦AI</color>" : "<color=#8c7aa8>◇</color>";
                Shadowed(new Rect(0, y, inner.width, U(13)), $"{src} <b><color=#fff2b3>{a.name}</color></b>  <color=#6b5c8a>{string.Join(" ", a.tags)}</color>", small);
                GUI.Label(new Rect(U(10), y + U(12), inner.width - U(14), U(22)), $"<color=#c9bcd8>{a.Explain()}</color>", small);
                y += U(34);
            }
            GUI.EndScrollView();
        }

        void DrawAttrs(Rect r)
        {
            var p = g.Player;
            GUI.Box(r, GUIContent.none, box);
            float x = r.x + U(8), y = r.y + U(8);
            Shadowed(new Rect(x, y, r.width, U(14)), $"남은 포인트: <color=#8bff9a>{p.UnspentAttr}</color>", label); y += U(20);
            for (int i = 0; i < 4; i++)
            {
                var a = (Attr)i;
                Shadowed(new Rect(x, y + U(3), U(60), U(14)), $"<b>{AttrInfo.Name(a)}</b> {p.Attrs[i]}", label);
                Shadowed(new Rect(x + U(70), y + U(4), U(130), U(14)), $"<color=#8c7aa8>{AttrInfo.Desc(a)}</color>", small);
                if (Btn(new Rect(x + U(200), y, U(22), U(18)), "+", p.UnspentAttr > 0))
                {
                    p.Attrs[i]++; p.UnspentAttr--; p.Telemetry.RecordInvest(a); p.RecalcStats();
                }
                y += U(22);
            }
            // Detailed stats
            float sx = r.x + U(240), sy = r.y + U(8);
            foreach (StatId s in System.Enum.GetValues(typeof(StatId)))
            {
                if (s == StatId.Count) continue;
                float v = p.Stats[s];
                string val = Stats.IsPercent(s) ? (s == StatId.AttackSpeed || s == StatId.XpGain || s == StatId.GoldGain ? $"×{v:0.00}" : $"{v * 100:0}%") : $"{v:0.#}";
                Shadowed(new Rect(sx, sy, U(110), U(12)), Stats.Label(s), small);
                Shadowed(new Rect(sx + U(90), sy, U(100), U(12)), $"<color=#fff2b3>{val}</color>", small);
                sy += U(13);
            }
        }

        void DrawChronicle(Rect r)
        {
            GUI.Box(r, GUIContent.none, box);
            var inner = new Rect(r.x + U(6), r.y + U(6), r.width - U(12), r.height - U(12));
            var list = g.World.chronicle.AsEnumerable().Reverse().ToList();
            float h = Mathf.Max(inner.height, list.Count * U(26) + U(40));
            scroll = GUI.BeginScrollView(inner, scroll, new Rect(0, 0, inner.width - U(14), h), GUIStyle.none, GUIStyle.none);
            float y = 0;
            Shadowed(new Rect(0, y, inner.width, U(14)), $"<b>연대기</b> — 삶 {g.World.lifeCount} · 무덤 {g.World.graves.Count} · 소탕한 야영지 {g.World.clearedCamps.Count} · 탑 {g.World.towerFloor}층", small);
            y += U(18);
            foreach (var e in list)
            {
                string col = e.kind switch { "death" => "#ff8a8a", "life" => "#8bff9a", "evolve" => "#c9b8ff", "legacy" => "#fff2b3", "tower" => "#e6dcff", _ => "#c9bcd8" };
                GUI.Label(new Rect(0, y, inner.width - U(16), U(26)), $"<color=#6b5c8a>[{e.life}번째 삶]</color> <color={col}>{e.text}</color>", small);
                y += U(26);
            }
            GUI.EndScrollView();
        }

        void DrawSettings(Rect r)
        {
            var s = Game.Settings;
            GUI.Box(r, GUIContent.none, box);
            float x = r.x + U(8), y = r.y + U(8), w = r.width - U(16);
            Shadowed(new Rect(x, y, U(100), U(14)), "효과음", label);
            s.sfx = GUI.HorizontalSlider(new Rect(x + U(100), y + U(4), U(150), U(12)), s.sfx, 0, 1); Sfx.Volume = s.sfx; y += U(18);
            Shadowed(new Rect(x, y, U(100), U(14)), "음악", label);
            float m = GUI.HorizontalSlider(new Rect(x + U(100), y + U(4), U(150), U(12)), s.music, 0, 1);
            if (!Mathf.Approximately(m, s.music)) { s.music = m; Sfx.SetMusicVolume(m); }
            y += U(18);
            Shadowed(new Rect(x, y, U(100), U(14)), "화면 흔들림", label);
            s.shakeScale = GUI.HorizontalSlider(new Rect(x + U(100), y + U(4), U(150), U(12)), s.shakeScale, 0, 1.5f);
            s.screenShake = s.shakeScale > 0.01f;
            y += U(18);
            if (Btn(new Rect(x, y, U(170), U(18)), "피해 숫자: " + (s.damageNumbers ? "켜짐" : "꺼짐"))) s.damageNumbers = !s.damageNumbers;
            y += U(26);

            // AI settings
            Shadowed(new Rect(x, y, w, U(14)), "<b>AI 진화 (OpenAI)</b>", label); y += U(16);
            string keyState = AiClient.HasKey ? "<color=#8bff9a>API 키 확인됨</color>" : "<color=#ff8a8a>API 키 없음</color> — 로컬 모드로 실행 중";
            Shadowed(new Rect(x, y, w, U(12)), keyState, small); y += U(13);
            GUI.Label(new Rect(x, y, w, U(26)), $"<color=#8c7aa8>키 위치: 환경 변수 OPENAI_API_KEY 또는 키만 적힌 파일:\n{SaveSystem.KeyFileHint}</color>", small);
            y += U(28);
            if (Btn(new Rect(x, y, U(120), U(18)), "AI: " + (s.aiEnabled ? "켜짐" : "꺼짐"))) s.aiEnabled = !s.aiEnabled;
            Shadowed(new Rect(x + U(130), y + U(2), U(40), U(14)), "모델", small);
            s.aiModel = GUI.TextField(new Rect(x + U(165), y, U(120), U(18)), s.aiModel, new GUIStyle(box) { font = font, fontSize = small.fontSize, normal = { textColor = Color.white, background = slotTex } });
            if (Btn(new Rect(x + U(290), y, U(90), U(18)), "폴더 열기")) Application.OpenURL("file:///" + Application.persistentDataPath);
            y += U(22);
            if (!string.IsNullOrEmpty(AiClient.LastStatus)) Shadowed(new Rect(x, y, w, U(12)), $"<color=#8c7aa8>마지막 호출: {AiClient.LastStatus}</color>", small);
            if (GUI.changed) SaveSystem.SaveSettings(s);
        }

        void DrawKeys(Rect r)
        {
            GUI.Box(r, GUIContent.none, box);
            float x = r.x + U(8), y = r.y + U(6);
            float colW = (r.width - U(24)) / 2;
            int i = 0;
            foreach (var d in Controls.Defaults)
            {
                float cx = x + (i % 2) * (colW + U(8));
                float cy = y + (i / 2) * U(21);
                Shadowed(new Rect(cx, cy + U(3), colW - U(80), U(14)), d.label, small);
                string key = rebinding == d.act ? "<color=#fff2b3>키를 누르세요…</color>" : Controls.KeyName(d.act);
                var conflict = Controls.Conflict(d.act);
                if (conflict.HasValue && rebinding != d.act) key = $"<color=#ff8a8a>{key}</color>";
                if (Btn(new Rect(cx + colW - U(78), cy, U(76), U(18)), key, rebinding == null))
                {
                    rebinding = d.act;
                    rebindOp = Controls.Rebind(d.act, () => { rebinding = null; Controls.Save(Game.Settings); });
                }
                i++;
            }
            float by = y + ((Controls.Defaults.Length + 1) / 2) * U(21) + U(4);
            if (Btn(new Rect(x, by, U(120), U(18)), "기본값으로")) { Controls.ResetAll(); Controls.Save(Game.Settings); }
            Shadowed(new Rect(x + U(130), by + U(2), U(300), U(14)), "<color=#8c7aa8>빨간색 = 다른 동작과 키가 겹침. Esc로 취소.</color>", small);
        }
    }
}
