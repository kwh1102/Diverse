using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public partial class Game
    {
        public readonly List<string> PipelineLog = new List<string>();
        public string OfferSummary;
        public float OfferShownAt;
        public const float OfferLockSeconds = .8f;
        public readonly List<(bool player, string text)> Chat = new List<(bool, string)>();
        public bool ChatBusy;
        public int ChatCost => 0;
        public bool CanChat => IsSkillOffer && !ChatBusy && !Generating;
        public int RerollCost => Mathf.RoundToInt(15 + Player.Level * 3);
        public bool OfferLocked => HasOffer && Time.unscaledTime - OfferShownAt < OfferLockSeconds;
        void ResetPrefetch() { }
        public void RequestPrefetch() { }
        public void BeginEvolution()
        {
            if (Generating || Player == null) return;
            State = GameState.Evolving; GameTime.Pause("evolve");
            if (SkillOffer != null || Player.pendingRewardLevels.Count > 0 && DB.Progression.SkillSlotAt(Player.pendingRewardLevels[0]) >= 0)
            {
                SkillOffer ??= new SkillOfferSave { slot = DB.Progression.SkillSlotAt(Player.pendingRewardLevels[0]) };
                Offer = null;
                if (SkillOffer.candidates.Count != 3) BeginSkillCandidates(null);
                else
                {
                    PipelineLog.Clear(); PipelineLog.AddRange(SkillOffer.process);
                    OfferShownAt = Time.unscaledTime; OfferSummary = SkillOffer.productionStatus ?? $"{ActiveSkillGenerator.SlotName(SkillOffer.slot)} 액티브 선택";
                    if (!SkillOffer.iconsPrepared) BeginSkillCandidates(null, true);
                    else Ui.OnOverlayOpened(GameState.Evolving, null);
                }
                return;
            }
            if (Player.PendingEvolutions <= 0) { State = GameState.Playing; GameTime.Resume("evolve"); return; }
            Offer = PartCatalog.Offer(rnd, Player.Abilities.OfferCount);
            OfferShownAt = Time.unscaledTime; OfferStatus = null;
            OfferSummary = "T1 파츠 32종 중 하나를 선택하세요. 같은 등급의 모든 파츠를 합성해 T5까지 성장시킬 수 있습니다.";
            PipelineLog.Clear(); PipelineLog.Add("고정 파츠 목록 → 중복 없는 후보 → 장착 시 보조 효과 적용");
            Ui.OnOverlayOpened(GameState.Evolving, null); Sfx.Play("evolve", .6f);
        }
        public void ChooseEvolution(int index)
        {
            if (IsSkillOffer) { ChooseSkill(index); return; }
            if (Offer == null || OfferLocked || index < 0 || index >= Offer.Count) return;
            var part = InventoryItem.Part(Offer[index]); Player.Inventory.Receive(part);
            Player.Telemetry.RecordChoice(Offer[index]); Player.ConsumeLevelReward();
            World.Log($"{HeroName}: [{part.name}] 파츠 획득", "evolve");
            Ui.Toast($"[{part.name}] 획득 — 무기 유형별 파츠 칸에 장착해 주세요.");
            FinishReward();
        }
        public void RerollEvolution()
        {
            if (Player == null || !HasOffer || ChatBusy || OfferLocked) return;
            if (!(IsSkillOffer && SkillOffer.selectedIndex >= 0) && Player.Gold < RerollCost) return;
            if (IsSkillOffer)
            {
                if (SkillOffer.selectedIndex >= 0) { RetrySkillProduction(); return; }
                if (SkillOffer.pinned.All(p => p)) { OfferStatus = "고정하지 않은 후보가 필요합니다."; return; }
                Player.Gold -= RerollCost; BeginSkillCandidates(null); SaveRun(); return;
            }
            Player.Gold -= RerollCost; Offer = null; BeginEvolution();
        }
        public void SkipEvolution()
        {
            if (!HasOffer || ChatBusy) return;
            if (IsSkillOffer && !SkillOffer.commission)
            {
                // Milestone rewards cannot be silently lost; defer the same persisted candidates.
                GameTime.Resume("evolve"); State = GameState.Playing; SaveRun(); OpenOverlay(GameState.Inventory); return;
            }
            if (IsSkillOffer) { FinishReward(); return; }
            Player.ConsumeLevelReward(); Player.GainGold(20); FinishReward();
        }
        public void SendChat(string text)
        {
            if (!CanChat || string.IsNullOrWhiteSpace(text)) return;
            if (SkillOffer.pinned.All(p => p)) { OfferStatus = "요청을 반영하려면 후보 하나 이상의 고정을 풀어 주세요."; return; }
            text = text.Trim(); if (text.Length > DesignGuard.MaxPrompt) text = text.Substring(0, DesignGuard.MaxPrompt);
            Chat.Add((true, text)); BeginSkillCandidates(text);
        }
        // ───────────────────────── Interaction ─────────────────────────

        public bool TryInteractAt(Vector2 world)
        {
            var it = Interactable.At(world);
            if (it == null) return false;
            if (Vector2.Distance(it.Pos, Player.Pos) > it.range + 0.2f)
            {
                StartCoroutine(WalkThenInteract(it));
                return true;
            }
            Interact(it);
            return true;
        }

        IEnumerator WalkThenInteract(Interactable it)
        {
            Player.SetDestination(it.Pos + (Player.Pos - it.Pos).normalized * 0.8f, true);
            float timeout = 8;
            while (it != null && Player != null && Vector2.Distance(it.Pos, Player.Pos) > it.range && timeout > 0)
            {
                timeout -= Time.deltaTime;
                if (Controls.Down(Act.Move) || Controls.Down(Act.Attack)) yield break;
                yield return null;
            }
            if (it != null && Player != null && State == GameState.Playing && Vector2.Distance(it.Pos, Player.Pos) <= it.range + 0.3f) Interact(it);
        }

        public void Interact(Interactable it)
        {
            Player.StopMoving();
            Dialogue.Open(this, it);
        }

        // ───────────────────────── Quests ─────────────────────────

        public void EnsureQuests()
        {
            World.quests.RemoveAll(q => q.status == 3);
            int active = World.quests.Count(q => q.status < 3);
            var gen = Diverse.World.I.Gen;
            int guard = 0;
            while (active < 3 && guard++ < 10)
            {
                var q = MakeQuest(gen, active + World.lifeCount * 3 + guard);
                if (q == null) continue;
                if (World.quests.Any(x => x.targetId == q.targetId && x.kind == q.kind)) continue;
                World.quests.Add(q);
                active++;
            }
        }

        QuestState MakeQuest(WorldGen gen, int salt)
        {
            var r = new Rng((uint)(World.seed + salt * 31337));
            float min = 25, max = 110;
            // Keep quest targets inside the area the hero can currently reach
            float bound = Player != null ? Player.Bound : 0;
            if (bound > 0) { max = Mathf.Min(max, bound - 10); min = Mathf.Min(min, max * 0.5f); }
            float Cap(float v) => bound > 0 ? Mathf.Min(v, bound - 10) : v;
            int kind = r.Range(0, 4);
            switch (kind)
            {
                case 0:
                case 1:
                {
                    // Monsters roam everywhere (EnemySpawner): hunt a species that lives in this world's themes
                    var pool = new List<string>(DB.SpawnTheme.enemies);
                    if (gen.Zones != null) foreach (var t in gen.Zones.themes) pool.AddRange(t.enemies);
                    if (pool.Count == 0) pool.Add("shroom");
                    string e = pool[r.Range(0, pool.Count)];
                    int need = r.Range(6, 12);
                    return new QuestState { id = "q" + salt, kind = "hunt", enemy = e, need = need, title = $"{DB.Enemy(e).name} {need}마리 사냥", desc = "마을의 밭과 길이 위험해지고 있어요.", rewardGold = 25 + need * 3, status = 0 };
                }
                case 2:
                {
                    var s = gen.FindNearest(Vector2.zero, StructKind.Ruins, min, Cap(max * 1.3f), x => !World.Flag("ruin:" + x.id));
                    if (s == null) return null;
                    return new QuestState { id = "q" + salt, kind = "investigate", targetId = s.id, tx = s.center.x, ty = s.center.y, title = $"{s.name} 조사", desc = "이야기꾼이 오래된 제단의 비문을 궁금해해요.", rewardGold = 30, status = 0 };
                }
                default:
                {
                    var s = gen.FindNearest(Vector2.zero, StructKind.Lair, min * 1.5f, Cap(max * 2f), x => !World.clearedCamps.Contains(x.id));
                    if (s == null) return null;
                    return new QuestState { id = "q" + salt, kind = "clear", targetId = s.id, tx = s.center.x, ty = s.center.y, title = $"{DB.Enemy(s.boss).name} 토벌", desc = $"{s.name} 깊은 곳에 무언가가 산다.", rewardGold = 120, status = 0 };
                }
            }
        }

        void UpdateQuests()
        {
            foreach (var q in World.quests)
            {
                if (q.status != 1) continue;
                // Also catches targets finished before the quest was accepted (the "!" used to stay on the map forever)
                bool done = q.kind == "investigate" ? World.Flag("ruin:" + q.targetId)
                          : q.kind == "clear" && World.clearedCamps.Contains(q.targetId);
                if (done) { q.status = 2; Ui.Toast($"의뢰 완료: {q.title} — 게시판에 보고하세요"); }
            }
        }

        public void ClaimQuest(QuestState q)
        {
            if (q.status != 2) return;
            q.status = 3;
            Player.GainGold(q.rewardGold);
            Player.GainXp(20 + q.rewardGold * 0.5f);
            World.Log($"{Ko.I(HeroName)} 의뢰를 완수했다: [{q.title}]", "quest");
            Sfx.Play("levelup", 0.7f);
            Ui.Toast($"보상: 골드 {q.rewardGold}");
            EnsureQuests();
        }
    }
}
