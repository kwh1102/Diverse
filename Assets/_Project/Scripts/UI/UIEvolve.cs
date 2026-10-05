using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Evolution screen: one plain status line, 3 cards (click-locked for a moment after they appear),
    /// a chat with the Storyteller that can revise the cards, and an optional pipeline log for the curious.
    /// </summary>
    public partial class UI
    {
        string chatInput = "";
        bool showPipeline;
        Vector2 chatScroll;
        int lastChatCount;

        void DrawEvolve()
        {
            Dim(0.6f);
            bool chat = g.Offer != null && AiClient.Enabled;
            var area = Center(470, chat ? 330 : 270);
            GUI.Box(area, GUIContent.none, panel);
            Shadowed(new Rect(area.x, area.y + U(8), area.width, U(20)), "진화", title, Pal.Hex("e6dcff"));

            // Pipeline details (toggle) — top right
            var tog = new Rect(area.xMax - U(70), area.y + U(10), U(58), U(16));
            if (GUI.Button(tog, showPipeline ? "닫기" : "과정 보기", new GUIStyle(button) { fontSize = small.fontSize })) showPipeline = !showPipeline;

            // One clear sentence instead of the raw pipeline log
            var statusR = new Rect(area.x + U(12), area.y + U(32), area.width - U(24), U(16));
            if (g.Offer == null)
            {
                string dots = new string('.', 1 + (int)(Time.unscaledTime * 3) % 3);
                Shadowed(statusR, $"<color=#c9b8ff>{g.OfferStatus ?? "플레이 방식을 분석하는 중"}{dots}</color>", smallC);
                if (showPipeline) DrawPipelineLog(new Rect(area.x + U(12), area.y + U(52), area.width - U(24), area.height - U(64)));
                return;
            }
            // Shrink the font rather than spilling out of the box (long failure notes)
            var sumStyle = new GUIStyle(smallC) { wordWrap = false, clipping = TextClipping.Clip };
            string sum = $"<color=#c9b8ff>{g.OfferSummary}</color>";
            while (sumStyle.fontSize > 8 && sumStyle.CalcSize(new GUIContent(sum)).x > statusR.width) sumStyle.fontSize--;
            Shadowed(statusR, sum, sumStyle);

            // Cards (not clickable while the process overlay covers them)
            bool locked = g.OfferLocked || g.ChatBusy || showPipeline;
            float lockK = Mathf.Clamp01((Time.unscaledTime - g.OfferShownAt) / Game.OfferLockSeconds);
            float cw = (area.width - U(24) - U(16)) / 3;
            float cardY = area.y + U(52), cardH = U(150);
            for (int i = 0; i < g.Offer.Count; i++)
            {
                var a = g.Offer[i];
                var r = new Rect(area.x + U(12) + i * (cw + U(8)), cardY, cw, cardH);
                bool hover = !locked && r.Contains(Event.current.mousePosition);
                var old = GUI.color;
                if (locked) GUI.color = new Color(1, 1, 1, 0.55f + 0.45f * lockK);
                GUI.Box(r, GUIContent.none, hover ? button : box);
                string src = a.source == "ai" ? "<color=#c9b8ff>✦ AI 생성</color>" : "<color=#8c7aa8>◇ 로컬</color>";
                string kind = a.kind == "modifier" ? "증폭" : a.kind == "stat" ? "기본" : "메커니즘";
                Shadowed(new Rect(r.x + U(6), r.y + U(5), r.width - U(12), U(12)), $"<color=#6b5c8a>{i + 1}</color>  {src}  <color=#8c7aa8>{kind}</color>", small);
                Shadowed(new Rect(r.x + U(6), r.y + U(18), r.width - U(12), U(30)), $"<b><color=#fff2b3>{a.name}</color></b>", label);
                GUI.Label(new Rect(r.x + U(6), r.y + U(48), r.width - U(12), U(56)), a.desc ?? a.Explain(), small);
                if (!string.IsNullOrEmpty(a.semanticReason))
                    GUI.Label(new Rect(r.x + U(6), r.y + U(104), r.width - U(12), U(28)), $"<color=#8c7aa8><i>\"{a.semanticReason}\"</i></color>", small);
                Shadowed(new Rect(r.x + U(6), r.yMax - U(14), r.width - U(12), U(12)), $"<color=#6b5c8a>{string.Join(" ", a.tags.Take(4))}</color>", small);
                GUI.color = old;
                // Click-mistake guard: the cards can't be picked for a moment after they appear (or change)
                if (locked)
                {
                    if (g.OfferLocked) Bar(new Rect(r.x + U(8), r.yMax - U(4), (r.width - U(16)), U(2)), lockK, new Color(0.8f, 0.72f, 1f), new Color(0.15f, 0.1f, 0.2f));
                    continue;
                }
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { g.ChooseEvolution(i); Sfx.Play("ui_select"); return; }
                if (!chatFocused && Controls.KeyDown(UnityEngine.InputSystem.Key.Digit1 + i)) { g.ChooseEvolution(i); return; }
            }

            float y = cardY + cardH + U(8);
            if (chat && !showPipeline)
            {
                DrawChat(new Rect(area.x + U(12), y, area.width - U(24), U(78)));
                y += U(84);
            }

            var br = new Rect(area.x + U(12), area.yMax - U(30), U(140), U(20));
            if (Btn(br, $"다시 뽑기 ({g.RerollCost} 골드)", g.Player.Gold >= g.RerollCost && !locked)) g.RerollEvolution();
            if (Btn(new Rect(area.xMax - U(132), br.y, U(120), U(20)), "건너뛰기 (+20 골드)", !g.ChatBusy)) g.SkipEvolution();
            Shadowed(new Rect(area.x, br.y + U(3), area.width, U(14)), "<color=#6b5c8a>카드 클릭 또는 1·2·3으로 선택</color>", smallC);

            if (showPipeline) DrawPipelineLog(new Rect(area.x + U(12), area.y + U(52), area.width - U(24), area.height - U(90)));
        }

        bool chatFocused;

        void DrawChat(Rect r)
        {
            GUI.Box(r, GUIContent.none, box);
            var log = new Rect(r.x + U(4), r.y + U(3), r.width - U(8), r.height - U(28));
            // Message history (auto-scrolls to the newest)
            float lineW = log.width - U(12);
            float h = 0;
            foreach (var (pl, text) in g.Chat) h += small.CalcHeight(new GUIContent(ChatLine(pl, text)), lineW) + U(2);
            if (g.ChatBusy) h += U(14);
            if (g.Chat.Count != lastChatCount || g.ChatBusy) { chatScroll.y = 99999; lastChatCount = g.Chat.Count; }
            chatScroll = GUI.BeginScrollView(log, chatScroll, new Rect(0, 0, lineW, Mathf.Max(h, log.height)), GUIStyle.none, GUIStyle.none);
            float y = 0;
            foreach (var (pl, text) in g.Chat)
            {
                var line = ChatLine(pl, text);
                float lh = small.CalcHeight(new GUIContent(line), lineW);
                GUI.Label(new Rect(0, y, lineW, lh), line, small);
                y += lh + U(2);
            }
            if (g.ChatBusy) GUI.Label(new Rect(0, y, lineW, U(14)), "<color=#8c7aa8>이야기꾼이 생각하는 중" + new string('.', 1 + (int)(Time.unscaledTime * 3) % 3) + "</color>", small);
            GUI.EndScrollView();

            // Input row
            var inR = new Rect(r.x + U(4), r.yMax - U(22), r.width - U(118), U(18));
            bool can = g.CanChat;
            GUI.SetNextControlName("evoChat");
            var fieldStyle = new GUIStyle(box) { font = font, fontSize = small.fontSize, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white, background = slotTex }, padding = new RectOffset((int)U(4), (int)U(4), 0, 0) };
            var e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "evoChat";
            if (enter) e.Use();
            chatInput = GUI.TextField(inR, chatInput ?? "", 200, fieldStyle);
            chatFocused = GUI.GetNameOfFocusedControl() == "evoChat";
            if (string.IsNullOrEmpty(chatInput) && !chatFocused)
                GUI.Label(new Rect(inR.x + U(4), inR.y, inR.width, inR.height), "<color=#6b5c8a>예) 2번을 화염 대신 냉기로 바꿔 줘 / 대시와 어울리는 능력은?</color>", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft });
            string costTxt = $"보내기 ({g.ChatCost}G)";
            bool send = Btn(new Rect(inR.xMax + U(4), inR.y, U(106), U(18)), costTxt, can && !string.IsNullOrWhiteSpace(chatInput)) || (enter && can);
            if (send && !string.IsNullOrWhiteSpace(chatInput))
            {
                g.SendChat(chatInput);
                chatInput = "";
            }
            if (!can && !g.ChatBusy && g.Player.Gold < g.ChatCost)
                Shadowed(new Rect(r.x, r.y - U(12), r.width - U(4), U(12)), $"<color=#ff8a8a>대화에는 {g.ChatCost} 골드가 필요하다</color>", new GUIStyle(small) { alignment = TextAnchor.UpperRight });
        }

        static string ChatLine(bool player, string text) =>
            player ? $"<color=#fff2b3>나:</color> {text}" : $"<color=#c9b8ff>이야기꾼:</color> <color=#e6dcff>{text}</color>";

        void DrawPipelineLog(Rect r)
        {
            GUI.Box(r, GUIContent.none, panel);
            var inner = new Rect(r.x + U(8), r.y + U(6), r.width - U(16), r.height - U(12));
            Shadowed(new Rect(inner.x, inner.y, inner.width, U(12)), "<b>AI 진화 과정</b> <color=#8c7aa8>— 플레이 기록 → 패턴 → AI 생성 → 검증 → 점수 → 이름</color>", small);
            var body = new Rect(inner.x, inner.y + U(16), inner.width, inner.height - U(16));
            float w = body.width - U(8);
            string text = string.Join("\n", g.PipelineLog);
            float h = small.CalcHeight(new GUIContent(text), w);
            pipelineScroll = GUI.BeginScrollView(body, pipelineScroll, new Rect(0, 0, w, Mathf.Max(h, body.height)), GUIStyle.none, GUIStyle.none);
            GUI.Label(new Rect(0, 0, w, h), $"<color=#c9bcd8>{text}</color>", small);
            GUI.EndScrollView();
        }
        Vector2 pipelineScroll;
    }
}
