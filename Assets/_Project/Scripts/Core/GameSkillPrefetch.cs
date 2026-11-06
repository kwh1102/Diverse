using System.Collections;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public partial class Game
    {
        public SkillOfferSave SkillPrefetch { get; private set; }
        SkillOfferSave warmingSkill;
        Player prefetchOwner;
        Coroutine prefetchRoutine;

        void ResetPrefetch()
        {
            // Equipping parts or swapping weapons must not restart a paid request for this life.
            if (prefetchOwner == Player) return;
            if (prefetchRoutine != null) StopCoroutine(prefetchRoutine);
            prefetchRoutine = null; warmingSkill = null; SkillPrefetch = null; prefetchOwner = Player;
        }
        public void RequestPrefetch()
        {
            if (State != GameState.Playing || Player == null || !Player.Alive || !ActiveSkillGenerator.Rules.prefetchSkills) return;
            if (prefetchOwner != Player) ResetPrefetch();
            if (SkillPrefetch != null && (Player.claimedSkillMask & (1 << SkillPrefetch.slot)) != 0) SkillPrefetch = null;
            if (SkillOffer != null || SkillPrefetch != null || warmingSkill != null || Generating || InventoryBusy || PartVisualBusy) return;
            int slot = Enumerable.Range(0, 4).Where(i => (Player.claimedSkillMask & (1 << i)) == 0).DefaultIfEmpty(-1).First();
            if (slot < 0 || Player.Level + ActiveSkillGenerator.Rules.skillPrefetchLeadLevels < DB.Progression.skillLevels[slot]) return;
            var draft = SkillPrefetch = new SkillOfferSave { slot = slot, prefetchStarted = true };
            warmingSkill = draft; SaveRun();
            prefetchRoutine = StartCoroutine(WarmSkills(Player, draft));
        }
        IEnumerator WarmSkills(Player owner, SkillOfferSave draft)
        {
            bool Current() => Player == owner && (SkillPrefetch == draft || SkillOffer == draft);
            void Trace(string line)
            {
                draft.process.Add(line);
                if (SkillOffer == draft) { PipelineLog.Add(line); OfferStatus = line.Split('\n')[0]; }
            }
            try
            {
                Trace($"전투 중 {ActiveSkillGenerator.SlotName(draft.slot)} 다음 보상 사전 설계 · 후보 3개와 아이콘만 준비, 전용 이펙트는 선택 후 제작");
                System.Collections.Generic.List<ActiveSkillData> choices = null;
                yield return ActiveSkillGenerator.GenerateChoices(owner, draft.slot, null, draft.candidates, draft.pinned, a => choices = a, Trace);
                if (!Current() || choices == null) yield break;
                draft.candidates = choices; SaveRun();
                draft.iconRequestStarted = true; SaveRun();
                yield return ActiveSkillArt.PrepareIcons(draft.candidates, Trace);
                if (!Current()) yield break;
                draft.iconsPrepared = true;
                Trace("사전 준비 완료 · 저장한 후보·아이콘을 보상 창에서 재사용");
            }
            finally
            {
                if (warmingSkill == draft) { warmingSkill = null; prefetchRoutine = null; }
                if (Player == owner) { SaveRun(); SaveSystem.SaveWorld(World); }
            }
        }
        SkillOfferSave TakeSkillPrefetch(int slot)
        {
            if (SkillPrefetch == null || SkillPrefetch.slot != slot) return null;
            var draft = SkillPrefetch; SkillPrefetch = null; return draft;
        }
        bool WaitForSkillPrefetch()
        {
            if (SkillOffer == null || warmingSkill != SkillOffer) return false;
            ChatBusy = Generating = true;
            PipelineLog.Clear(); PipelineLog.AddRange(SkillOffer.process);
            OfferStatus = "전투 중 시작한 후보 준비를 마무리하는 중…";
            Ui.OnOverlayOpened(GameState.Evolving, null);
            StartCoroutine(WaitForWarmSkills(SkillOffer, Player)); return true;
        }
        IEnumerator WaitForWarmSkills(SkillOfferSave draft, Player owner)
        {
            while (warmingSkill == draft && Player == owner) yield return null;
            if (Player != owner || SkillOffer != draft) yield break;
            ChatBusy = Generating = false;
            BeginSkillCandidates(null, draft.candidates.Count == 3);
        }
        void RestoreSkillPrefetch(SkillOfferSave draft)
        {
            if (draft == null || draft.slot < 0 || draft.slot >= 4 || (Player.claimedSkillMask & (1 << draft.slot)) != 0) return;
            draft.candidates ??= new System.Collections.Generic.List<ActiveSkillData>();
            draft.process ??= new System.Collections.Generic.List<string>(); draft.pinned = new bool[3];
            if (draft.candidates.Count != 0 && (draft.candidates.Count != 3 || draft.candidates.Any(a => a == null || a.linkedMask != 0 ||
                !ActiveSkillGenerator.ValidateSet(new System.Collections.Generic.List<ActiveSkillData> { a }, a.weaponType, new[] { draft.slot }, out _)))) return;
            foreach (var a in draft.candidates) ActiveSkillArt.Restore(a);
            draft.prefetchStarted = true; SkillPrefetch = draft;
            // An interrupted request is never automatically charged again after loading.
        }
    }
}
