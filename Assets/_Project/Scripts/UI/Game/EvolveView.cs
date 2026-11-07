using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// Evolution screen: one plain status line, 3 cards (click-locked for a moment after they appear),
    /// a chat with the Storyteller that can revise the cards, and an optional pipeline log for the curious.
    /// </summary>
    public class EvolveView : MonoBehaviour
    {
        [SerializeField, HideInInspector] int uiRevision;
        [SerializeField] Text status;
        [SerializeField] Button pipelineToggle;
        [SerializeField] GameObject pipelinePanel;
        [SerializeField] Text pipelineText;
        [SerializeField] EvolveCard[] cards = new EvolveCard[3];
        [Header("Chat")]
        [SerializeField] GameObject chatPanel;
        [SerializeField] Text chatLog, chatWarning;
        [SerializeField] ScrollRect chatScroll;
        [SerializeField] InputField chatInput;
        [SerializeField] Button send;
        [Header("Bottom")]
        [SerializeField] Button reroll, skip;
        [Header("Panel height with / without the chat row")]
        [SerializeField] RectTransform panel;
        [SerializeField] float heightWithChat = 350, heightWithoutChat = 266;

        bool showPipeline;
        int lastChat = -1;
        Game g;

        public bool Typing => chatInput != null && chatInput.isFocused;

        public void Init(Game game)
        {
            g = game;
            UIKit.Bind(pipelineToggle, () => showPipeline = !showPipeline);
            for (int i = 0; i < cards.Length; i++) { int k = i; cards[i].Init(() => { if (!Locked) { g.ChooseEvolution(k); Sfx.Play("ui_select"); } }); }
            for (int i = 0; i < cards.Length; i++) { int k = i; cards[i].BindPin(() => g.PinSkill(k)); }
            UIKit.Bind(send, Send);
            UIKit.Bind(reroll, () => g.RerollEvolution());
            UIKit.Bind(skip, () => g.SkipEvolution());
            if (chatInput != null)
            {
                chatInput.onSubmit.AddListener(_ => Send());
                Controls.TypingField = chatInput;   // keyboard game actions pause while this field has focus
            }
        }

        bool Locked => g.OfferLocked || g.ChatBusy || g.Generating || showPipeline;

        void Send()
        {
            if (!g.CanChat || string.IsNullOrWhiteSpace(chatInput.text)) return;
            g.SendChat(chatInput.text);
            chatInput.text = "";
        }

        void OnEnable() { showPipeline = false; lastChat = -1; }

        void Update()
        {
            if (g == null || g.Player == null) return;
            UIKit.Label(pipelineToggle, showPipeline ? "닫기" : "과정 보기");
            UIKit.Show(pipelinePanel, showPipeline);
            if (showPipeline) UIKit.Set(pipelineText, string.Join("\n\n", g.PipelineLog));

            bool offer = g.HasOffer;
            if (!offer) UIKit.Set(status, $"<color=#c9b8ff>{g.OfferStatus ?? "플레이 방식을 분석하는 중"}{new string('.', 1 + (int)(Time.unscaledTime * 3) % 3)}</color>");
            else UIKit.Set(status, $"<color=#c9b8ff>{g.OfferStatus ?? g.OfferSummary}</color>");

            float lockK = Mathf.Clamp01((Time.unscaledTime - g.OfferShownAt) / Game.OfferLockSeconds);
            for (int i = 0; i < cards.Length; i++)
            {
                bool on = offer && (g.IsSkillOffer ? i < g.SkillOffer.candidates.Count : i < g.Offer.Count);
                UIKit.Show(cards[i], on);
                if (on && g.IsSkillOffer) cards[i].TickActive(g.SkillOffer.candidates[i], i, Locked, g.OfferLocked ? lockK : -1, g.SkillOffer.pinned[i]);
                else if (on) cards[i].Tick(g.Offer[i], i, Locked, g.OfferLocked ? lockK : -1);
                if (on && !Locked && Controls.KeyDown(Key.Digit1 + i)) { g.ChooseEvolution(i); return; }
            }

            bool chat = g.IsSkillOffer;
            UIKit.Show(chatPanel, chat);
            // Bottom buttons are anchored to the panel's bottom edge, so they follow the height
            if (panel != null) panel.sizeDelta = new Vector2(panel.sizeDelta.x, chat || showPipeline ? heightWithChat : heightWithoutChat);
            if (chat)
            {
                // Rebuild the log only when it changes (the "thinking" dots tick 3×/s). Rebuilding + ForceUpdateCanvases every
                // frame re-laid-out the whole canvas, including the input field, which made typing stutter.
                int dots = g.ChatBusy ? 1 + (int)(Time.unscaledTime * 3) % 3 : 0;
                int n = g.Chat.Count * 8 + (g.ChatBusy ? 4 : 0) + dots;
                if (n != lastChat)
                {
                    bool grew = lastChat < 0 || n / 8 != lastChat / 8;
                    lastChat = n;
                    var lines = g.Chat.Select(c => c.player ? $"<color=#fff2b3>나:</color> {c.text}" : $"<color=#c9b8ff>이야기꾼:</color> <color=#e6dcff>{c.text}</color>").ToList();
                    if (lines.Count == 0 && !g.ChatBusy) lines.Add("<color=#c9b8ff>이야기꾼:</color> 원하는 동작을 아래에 적어 줘. 고정하지 않은 후보에 요청을 반영할게.");
                    if (g.ChatBusy) lines.Add("<color=#8c7aa8>이야기꾼이 생각하는 중" + new string('.', dots) + "</color>");
                    UIKit.Set(chatLog, string.Join("\n", lines));
                    if (grew && chatScroll != null)
                    { LayoutRebuilder.ForceRebuildLayoutImmediate(chatScroll.content); Canvas.ForceUpdateCanvases(); chatScroll.StopMovement(); chatScroll.verticalNormalizedPosition = 0; }
                }
                UIKit.Label(send, "요청 반영");
                send.interactable = g.CanChat && !string.IsNullOrWhiteSpace(chatInput.text);
                bool poor = !g.CanChat && !g.ChatBusy && g.Player.Gold < g.ChatCost;
                UIKit.Show(chatWarning, poor);
                if (poor) UIKit.Set(chatWarning, $"<color=#ff8a8a>대화에는 {g.ChatCost} 골드가 필요하다</color>");
            }

            UIKit.Show(reroll, offer);
            UIKit.Show(skip, offer);
            if (offer)
            {
                bool production = g.IsSkillOffer && g.SkillOffer.selectedIndex >= 0;
                UIKit.Label(reroll, production ? ActiveEffectArt.ReusesSavedArt(g.SkillOffer.candidates[g.SkillOffer.selectedIndex]) ? "저장 시트 재처리 (무료)" : "이펙트 새로 생성 (API 비용)" : $"다시 뽑기 ({g.RerollCost} 골드)");
                reroll.interactable = (production || g.Player.Gold >= g.RerollCost) && !Locked;
                skip.interactable = !g.ChatBusy;
                UIKit.Label(skip, g.IsSkillOffer ? g.SkillOffer.commission ? "의뢰 취소" : "나중에 선택" : "넘기기 (+20G)");
            }
        }
    }
}
