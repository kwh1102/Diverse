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
        public bool prefetchStarted;
        public int productionVersion = 1, selectedIndex = -1;
        public bool effectRequestStarted;
        public string productionStatus, request;
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
            OfferStatus = "서로 다른 전투 규칙을 가진 후보 3개를 준비하는 중…";
            if (!resume) { draft.process.Clear(); PipelineLog.Clear(); draft.request = request; draft.selectedIndex = -1; draft.effectRequestStarted = false; draft.productionStatus = null; }
            void Trace(string line)
            {
                draft.process.Add(line); PipelineLog.Add(line); OfferStatus = line.Split('\n')[0];
            }
            try
            {
                // Separate commissions wait for the background draft, avoiding overlapping candidate/icon requests.
                while (warmingSkill != null && warmingSkill != draft)
                {
                    OfferStatus = "전투 중 시작한 후보 준비를 마친 뒤 의뢰를 진행하는 중…";
                    yield return null;
                    if (Player != owner || SkillOffer != draft) yield break;
                }
                if (!resume)
                {
                    List<ActiveSkillData> choices = null;
                    bool interruptedPrefetch = draft.prefetchStarted && draft.candidates.Count == 0 && string.IsNullOrEmpty(request);
                    if (interruptedPrefetch) Trace("중단된 사전 설계 · 자동 유료 재요청 없이 로컬 후보 준비 (요청 반영/다시 뽑기로 새 설계 가능)");
                    yield return ActiveSkillGenerator.GenerateChoices(owner, draft.slot, request, draft.candidates, draft.pinned, a => choices = a, Trace, !interruptedPrefetch);
                    if (Player != owner || SkillOffer != draft) yield break;
                    if (choices == null)
                    {
                        string reason = draft.process.LastOrDefault() ?? "요청을 완성하지 못했어. 이전 후보를 유지할게.";
                        Chat.Add((false, reason)); OfferSummary = reason; SaveRun(); yield break;
                    }
                    draft.candidates = choices; draft.iconsPrepared = draft.iconRequestStarted = false;
                    if (interruptedPrefetch) draft.iconRequestStarted = true;
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
                draft.prefetchStarted = false;
                OfferSummary = $"{ActiveSkillGenerator.SlotName(draft.slot)} 액티브 선택 · 원하는 후보를 고정하고 아래에서 요청하세요.";
                foreach (var a in draft.candidates)
                    if (!a.ArtReady && string.IsNullOrEmpty(a.artStatus)) a.artStatus = "전투 규칙 전용 아이콘과 내장 연출";
                Trace("후보 준비 완료 · 각 기술에 전용 내장 연출 적용");

                string outcomes = string.Join(" / ", draft.candidates.Select((a, i) => $"{i + 1}. {a.name} ({ActiveSkillGenerator.InputLabel(a.input)}, {ActiveSkillGenerator.RoleName(a.role)})"));
                string art = AiClient.Enabled ? " 선택한 기술에는 전용 내장 연출과 함께 쓸 보조 그림을 준비할게." : " 선택한 기술의 전용 내장 연출을 적용할게.";
                Chat.Add((false, "각 카드에 발동 조건과 전투에서 바뀌는 동작을 적었어. " + outcomes + art));
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
            SkillOffer.selectedIndex = index;
            SkillOffer.effectRequestStarted = false;
            StartCoroutine(ProduceChosenSkill());
        }
        public void RetrySkillProduction()
        {
            if (SkillOffer == null || SkillOffer.selectedIndex < 0 || ChatBusy) return;
            var a = SkillOffer.candidates[SkillOffer.selectedIndex];
            StartCoroutine(ProduceChosenSkill(!ActiveEffectArt.ReusesSavedArt(a)));
        }
        IEnumerator ProduceChosenSkill(bool fresh = false)
        {
            var draft = SkillOffer; var owner = Player;
            if (draft == null || draft.selectedIndex < 0 || draft.selectedIndex >= draft.candidates.Count) yield break;
            var a = draft.candidates[draft.selectedIndex];
            ChatBusy = Generating = true; GameTime.Pause("evolve");
            void Trace(string line) { draft.process.Add(line); PipelineLog.Add(line); OfferStatus = draft.productionStatus = line; }
            try
            {
                draft.effectRequestStarted = true; SaveRun();
                bool ready = false;
                yield return ActiveEffectArt.Prepare(a, Trace, ok => ready = ok, SaveRun, fresh);
                if (Player != owner || SkillOffer != draft) yield break;
                if (!ready)
                {
                    string cause = draft.process.LastOrDefault() ?? "이펙트 응답 없음";
                    string next = ActiveEffectArt.ReusesSavedArt(a) ? "‘저장 시트 재처리’로 추가 비용 없이 다시 처리할 수 있어." : "‘이펙트 새로 생성’을 누르면 이미지 API 비용이 다시 발생해.";
                    string message = cause + "\n현재 스킬과 보상은 유지했어. " + next + " ‘나중에 선택’으로 보류할 수도 있어.";
                    Trace("이펙트 적용 보류 · " + next);
                    OfferSummary = message; Chat.Add((false, message)); SaveRun(); yield break;
                }
                // Save finished art before equipping; a restart can reuse it without another paid request.
                SaveRun();
                int cost = draft.commission ? InventoryState.Rules.skillCommissionCost : 0;
                if (Player.Gold < cost) { Trace("의뢰 비용 부족 · 완성된 이펙트와 후보를 저장했습니다."); SaveRun(); yield break; }
                Player.Gold -= cost; Player.GrantSkill(a);
                if (!draft.commission) Player.ConsumeLevelReward();
                string artwork = ActiveEffectArt.Ready(a) ? "전용 AI 이펙트" : "전투 규칙 전용 내장 연출";
                World.Log($"{HeroName}: [{a.name}] 획득 · {artwork}", "evolve");
                Ui.Toast($"[{a.name}] 획득 · {artwork}");
                FinishReward();
            }
            finally { if (Player == owner) { ChatBusy = Generating = false; OfferStatus = SkillOffer == draft ? draft.productionStatus : null; SaveSystem.SaveWorld(World); } }
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
                if (SkillOffer.productionVersion < 1) { SkillOffer.selectedIndex = -1; SkillOffer.productionVersion = 1; }
                if (SkillOffer.pinned?.Length != 3) SkillOffer.pinned = new bool[3];
                SkillOffer.process ??= new List<string>();
                SkillOffer.candidates ??= new List<ActiveSkillData>();
                if (SkillOffer.candidates.Count != 3 || SkillOffer.candidates.Any(a => a == null || !ActiveSkillGenerator.ValidateCandidate(a, a.weaponType, SkillOffer.slot, out _)))
                { SkillOffer.candidates.Clear(); SkillOffer.pinned = new bool[3]; SkillOffer.iconsPrepared = SkillOffer.iconRequestStarted = false; }
                foreach (var a in SkillOffer.candidates) ActiveSkillArt.Restore(a);
                if (SkillOffer.selectedIndex >= SkillOffer.candidates.Count) SkillOffer.selectedIndex = -1;
                if (SkillOffer.selectedIndex >= 0)
                    SkillOffer.productionStatus = "선택한 스킬 제작 보류 · " + (ActiveEffectArt.ReusesSavedArt(SkillOffer.candidates[SkillOffer.selectedIndex]) ? "저장된 그림을 추가 비용 없이 처리할 수 있습니다." : "이펙트 새로 생성은 API 비용이 발생합니다.");
            }
        }
    }
}
