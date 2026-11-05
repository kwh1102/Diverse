using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    [Serializable]
    public class SkillOfferSave
    {
        public int slot = -1;
        public bool commission;
        public List<ActiveSkillData> candidates = new List<ActiveSkillData>();
        public bool[] pinned = new bool[3];
        public List<string> process = new List<string>();
        public bool iconsPrepared, iconRequestStarted;
    }

    public partial class Game
    {
        public SkillOfferSave SkillOffer;
        public bool IsSkillOffer => SkillOffer != null;
        public bool HasOffer => IsSkillOffer ? SkillOffer.candidates?.Count == 3 : Offer != null;
        public void PinSkill(int index)
        {
            if (!IsSkillOffer || ChatBusy || index < 0 || index >= 3) return;
            SkillOffer.pinned[index] = !SkillOffer.pinned[index]; OfferStatus = null; SaveRun();
        }
        public void OpenSkillCommission(int slot)
        {
            if (State != GameState.Dialogue || !Player.SkillUnlocked(slot) || Player.Gold < InventoryState.Rules.skillCommissionCost) return;
            Dialogue.Current = null; Offer = null;
            SkillOffer = new SkillOfferSave { slot = slot, commission = true };
            OpenOverlay(GameState.Evolving); BeginSkillCandidates(null);
        }
        void BeginSkillCandidates(string request, bool resume = false)
        {
            if (ChatBusy || SkillOffer == null) return;
            State = GameState.Evolving; GameTime.Pause("evolve");
            Ui.OnOverlayOpened(GameState.Evolving, null);
            StartCoroutine(SkillCandidatesRoutine(request, resume));
        }
        IEnumerator SkillCandidatesRoutine(string request, bool resume)
        {
            var owner = Player; var draft = SkillOffer;
            ChatBusy = Generating = true;
            OfferStatus = "서로 다른 조작과 역할의 후보 3개를 설계하는 중…";
            if (!resume) { draft.process.Clear(); PipelineLog.Clear(); }
            void Trace(string line)
            {
                draft.process.Add(line); PipelineLog.Add(line); OfferStatus = line.Split('\n')[0];
            }
            try
            {
                if (!resume)
                {
                    List<ActiveSkillData> choices = null;
                    yield return ActiveSkillGenerator.GenerateChoices(owner, draft.slot, request, draft.candidates, draft.pinned, a => choices = a, Trace);
                    if (Player != owner || SkillOffer != draft || choices == null) yield break;
                    draft.candidates = choices; draft.iconsPrepared = draft.iconRequestStarted = false;
                    SaveRun(); // Text candidates survive a slow or interrupted image request.
                }
                if (!draft.iconRequestStarted)
                {
                    draft.iconRequestStarted = true; SaveRun();
                    yield return ActiveSkillArt.PrepareIcons(draft.candidates.Where((a, i) => !draft.pinned[i]).ToList(), Trace);
                }
                else if (!draft.iconsPrepared) Trace("중단된 아이콘 요청 · 자동 유료 재요청 없이 로컬 도트 사용 (인벤토리에서 재요청 가능)");
                if (Player != owner || SkillOffer != draft) yield break;
                draft.iconsPrepared = true; OfferShownAt = Time.unscaledTime;
                OfferSummary = $"{ActiveSkillGenerator.SlotName(draft.slot)} 액티브 선택 · 원하는 후보를 고정하고 아래에서 요청하세요.";
                foreach (var a in draft.candidates)
                    if (!a.ArtReady && string.IsNullOrEmpty(a.artStatus)) a.artStatus = "로컬 도트 아이콘 · 전투 이펙트 준비 완료";
                Trace("후보 준비 완료 · 선택 즉시 단계별 전투 이펙트 적용");
                Chat.Add((false, "후보 3개의 아이콘과 동작을 준비했어. 고정하지 않은 후보는 요청으로 바꿀 수 있어."));
                SaveRun();
                SaveSystem.SaveWorld(World);
            }
            finally { if (Player == owner) { ChatBusy = Generating = false; OfferStatus = null; } }
        }
        void ChooseSkill(int index)
        {
            if (!HasOffer || OfferLocked || ChatBusy || index < 0 || index >= SkillOffer.candidates.Count) return;
            int cost = SkillOffer.commission ? InventoryState.Rules.skillCommissionCost : 0;
            if (Player.Gold < cost) { OfferStatus = "의뢰 비용이 부족합니다."; return; }
            var a = SkillOffer.candidates[index]; Player.Gold -= cost;
            Player.GrantSkill(a);
            bool commission = SkillOffer.commission; SkillOffer = null;
            if (!commission) Player.ConsumeLevelReward();
            World.Log($"{HeroName}: [{a.name}] {ActiveSkillGenerator.TypeName(a.weaponType)} {ActiveSkillGenerator.SlotName(a.slot)} 선택", "evolve");
            Ui.Toast($"[{a.name}] 획득 · 전용 동작과 이펙트 즉시 사용 가능");
            FinishReward();
        }
        void FinishReward()
        {
            Offer = null; SkillOffer = null; Chat.Clear();
            GameTime.Resume("evolve"); GameTime.Resume("overlay"); State = GameState.Playing;
            SaveRun(); SaveSystem.SaveWorld(World); OpenOverlay(GameState.Inventory);
        }
        public void RestoreLevelRewards(RunSave saved)
        {
            Player.pendingRewardLevels = saved.pendingRewardLevels ?? new List<int>();
            Player.claimedSkillMask = saved.claimedSkillMask;
            if (saved.rewardVersion == 0)
            {
                Player.pendingRewardLevels.Clear();
                for (int i = 0; i < saved.pendingEvolutions; i++) Player.pendingRewardLevels.Add(0);
                foreach (var item in Player.Inventory.items.Where(i => i.kind == ItemKind.Skill)) Player.claimedSkillMask |= 1 << item.skillSlot;
                for (int slot = 0; slot < 4; slot++)
                    if (Player.SkillUnlocked(slot) && (Player.claimedSkillMask & (1 << slot)) == 0) Player.pendingRewardLevels.Add(DB.Progression.skillLevels[slot]);
            }
            Player.PendingEvolutions = Player.pendingRewardLevels.Count;
            SkillOffer = saved.skillOffer;
            if (SkillOffer != null)
            {
                if (SkillOffer.slot < 0 || SkillOffer.slot >= 4 || !Player.SkillUnlocked(SkillOffer.slot)) { SkillOffer = null; return; }
                if (SkillOffer.pinned?.Length != 3) SkillOffer.pinned = new bool[3];
                SkillOffer.process ??= new List<string>();
                SkillOffer.candidates ??= new List<ActiveSkillData>();
                if (SkillOffer.candidates.Count != 3 || SkillOffer.candidates.Any(a => a == null || a.linkedMask != 0 ||
                    !ActiveSkillGenerator.ValidateSet(new List<ActiveSkillData> { a }, a.weaponType, new[] { SkillOffer.slot }, out _)))
                { SkillOffer.candidates.Clear(); SkillOffer.pinned = new bool[3]; SkillOffer.iconsPrepared = SkillOffer.iconRequestStarted = false; }
                foreach (var a in SkillOffer.candidates) ActiveSkillArt.Restore(a);
            }
        }
    }
}
