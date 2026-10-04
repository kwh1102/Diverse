using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Evolution flow orchestration. The whole pipeline:
    ///   Telemetry → Context → (LLM #1 or LocalComposer) → Validate/Repair → Synergy/Balance → Score → top 3 → (LLM #2 naming) → player picks
    /// </summary>
    public partial class Game
    {
        public readonly List<string> PipelineLog = new List<string>();

        public void BeginEvolution()
        {
            if (Generating || Player == null) return;
            StartCoroutine(EvolutionRoutine());
        }

        IEnumerator EvolutionRoutine()
        {
            Generating = true;
            Offer = null;
            PipelineLog.Clear();
            State = GameState.Evolving;
            GameTime.Pause("evolve");
            Sfx.Play("evolve", 0.6f);
            Ui.OnOverlayOpened(GameState.Evolving, null);

            var ctx = GenerationContext.Build(Player, Player.Telemetry, rnd);
            PipelineLog.Add($"① 플레이 기록: {Player.Telemetry.Summary()}");
            PipelineLog.Add($"③ 패턴: {(ctx.patterns.Count == 0 ? "(데이터 부족)" : string.Join(", ", ctx.patterns.Take(3).Select(p => $"{p.desc} {p.confidence:0.00}")))}");
            if (ctx.seededSources.Count > 0) PipelineLog.Add($"④ 씨앗 출처: {string.Join(", ", ctx.seededSources)}");
            PipelineLog.Add($"⑦ 기본 요소 검색: 관련 {ctx.retrieved.Count}개 + 탐험 {ctx.exploration.Count}개");

            List<AbilityGraph> raw = null;
            if (AiClient.Enabled)
            {
                OfferStatus = "AI가 당신의 플레이를 읽는 중…";
                PipelineLog.Add("⑧ LLM #1 호출 (" + Settings.aiModel + ")");
                yield return AiClient.Generate(ctx, 4, r => raw = r);
                PipelineLog.Add("   → " + AiClient.LastStatus);
            }
            else PipelineLog.Add(AiClient.HasKey ? "⑧ AI 꺼짐 → 로컬 생성기" : "⑧ API 키 없음 → 로컬 생성기");

            var local = LocalComposer.Compose(ctx, 4, rnd);
            var candidates = new List<AbilityGraph>();
            if (raw != null) candidates.AddRange(raw);
            candidates.AddRange(local);

            // ⑩~⑭ Validate → repair → balance
            var valid = new List<AbilityGraph>();
            int rejected = 0, repaired = 0;
            foreach (var g in candidates)
            {
                var res = AbilityValidator.Validate(g, Player);
                if (!res.ok) { rejected++; if (g.source == "ai") PipelineLog.Add($"   ✗ {g.mechanic}: {string.Join(", ", res.errors)}"); continue; }
                if (res.repairs.Count > 0) { repaired++; if (g.source == "ai") PipelineLog.Add($"   ⚙ {g.mechanic}: {string.Join(", ", res.repairs)}"); }
                if (valid.Any(v => v.Signature() == g.Signature())) continue;
                g.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                valid.Add(g);
            }
            PipelineLog.Add($"⑩⑪ 검증: 통과 {valid.Count} / 거부 {rejected} / 수리 {repaired}");

            // ⑬ Score — AI candidates first (local fills the gaps), avoid giving 3 of the same kind
            var scored = valid.Select(g => (g, s: AbilityValidator.Score(g, ctx) + (g.source == "ai" ? 0.8f : 0))).OrderByDescending(x => x.s).ToList();
            var pick = new List<AbilityGraph>();
            foreach (var (g, _) in scored)
            {
                if (pick.Count >= 3) break;
                if (pick.Count(x => x.kind != "trigger") >= 1 && g.kind != "trigger") continue;
                if (pick.Any(x => x.trigger == g.trigger && x.kind == "trigger" && g.kind == "trigger")) continue;
                pick.Add(g);
            }
            foreach (var (g, _) in scored) { if (pick.Count >= 3) break; if (!pick.Contains(g)) pick.Add(g); }
            PipelineLog.Add($"⑫⑬ 시너지·점수 → 상위 {pick.Count}개");

            // ⑮ Name/description
            foreach (var g in pick) LocalComposer.Name(g);
            if (AiClient.Enabled && pick.Any(g => g.source == "ai"))
            {
                OfferStatus = "이야기꾼이 능력에 이름을 붙이는 중…";
                yield return AiClient.NameAll(pick, Diverse.World.I.Gen.RegionName(Player.Pos), () => { });
                PipelineLog.Add("⑮ LLM #2 이름 짓기 완료");
            }
            else PipelineLog.Add("⑮ 로컬 이름 짓기");

            Offer = pick;
            OfferStatus = null;
            Generating = false;
        }

        public void ChooseEvolution(int i)
        {
            if (Offer == null || i < 0 || i >= Offer.Count) return;
            var g = Offer[i];
            Player.Abilities.Add(g);
            Player.Telemetry.RecordChoice(g);
            Player.PendingEvolutions--;
            World.Log($"{Ko.I(HeroName)} [{g.name}] 능력을 얻었다. ({g.Explain()})", "evolve");
            Fx.I?.Play(Art.Shockwave(Pal.ShadowEl, 30), Player.Pos, 0, 18, 1, null, true);
            Fx.I?.Burst(Player.Pos + Vector2.up * 0.5f, Pal.ShadowEl, 30, 6, 0.9f, -3);
            Sfx.Play("levelup");
            Offer = null;
            GameTime.Resume("evolve");
            State = GameState.Playing;
            // If there are attribute points, choose them right away in the next screen
            if (Player.UnspentAttr > 0) OpenOverlay(GameState.Paused, "attrs");
        }

        public void RerollEvolution()
        {
            if (Player.Gold < RerollCost) return;
            Player.Gold -= RerollCost;
            Offer = null;
            Generating = false;
            BeginEvolution();
        }

        public int RerollCost => 15 + Player.Level * 3;

        public void SkipEvolution()
        {
            Offer = null;
            Player.PendingEvolutions--;
            Player.GainGold(20);
            GameTime.Resume("evolve");
            State = GameState.Playing;
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
            float min = 25 + World.towerFloor * 20, max = 110 + World.towerFloor * 40;
            int kind = r.Range(0, 4);
            switch (kind)
            {
                case 0:
                {
                    var s = gen.FindNearest(Vector2.zero, StructKind.Camp, min, max, x => !World.clearedCamps.Contains(x.id) && Hash.F((int)x.center.x, (int)x.center.y, salt) > 0.3f);
                    if (s == null) return null;
                    return new QuestState { id = "q" + salt, kind = "clear", targetId = s.id, tx = s.center.x, ty = s.center.y, title = $"{s.name} 소탕", desc = $"여행자: \"{DB.Enemy(s.enemy).name} 무리 때문에 교역로가 막혔어요.\"", rewardGold = 40 + s.tier * 15, rewardShards = 1, status = 0 };
                }
                case 1:
                {
                    string[] pool = { "shroom", "jelly", "fox", "raccoon", "wisp", "bat" };
                    string e = r.Pick(pool);
                    int need = r.Range(6, 12);
                    return new QuestState { id = "q" + salt, kind = "hunt", enemy = e, need = need, title = $"{DB.Enemy(e).name} {need}마리 사냥", desc = "마을의 밭과 길이 위험해지고 있어요.", rewardGold = 25 + need * 3, status = 0 };
                }
                case 2:
                {
                    var s = gen.FindNearest(Vector2.zero, StructKind.Ruins, min, max * 1.3f, x => !World.Flag("ruin:" + x.id));
                    if (s == null) return null;
                    return new QuestState { id = "q" + salt, kind = "investigate", targetId = s.id, tx = s.center.x, ty = s.center.y, title = $"{s.name} 조사", desc = "이야기꾼이 오래된 제단의 비문을 궁금해해요.", rewardGold = 30, rewardShards = 2, status = 0 };
                }
                default:
                {
                    var s = gen.FindNearest(Vector2.zero, StructKind.Lair, min * 1.5f, max * 2f, x => !World.clearedCamps.Contains(x.id));
                    if (s == null) return null;
                    return new QuestState { id = "q" + salt, kind = "clear", targetId = s.id, tx = s.center.x, ty = s.center.y, title = $"{DB.Enemy(s.boss).name} 토벌", desc = $"{s.name} 깊은 곳에 무언가가 산다.", rewardGold = 120, rewardShards = 3, status = 0 };
                }
            }
        }

        void UpdateQuests()
        {
            foreach (var q in World.quests)
                if (q.status == 1 && q.kind == "investigate" && World.Flag("ruin:" + q.targetId)) { q.status = 2; Ui.Toast($"의뢰 완료: {q.title}"); }
        }

        public void ClaimQuest(QuestState q)
        {
            if (q.status != 2) return;
            q.status = 3;
            Player.GainGold(q.rewardGold);
            World.memoryShards += q.rewardShards;
            Player.GainXp(20 + q.rewardGold * 0.5f);
            World.Log($"{Ko.I(HeroName)} 의뢰를 완수했다: [{q.title}]", "quest");
            Sfx.Play("levelup", 0.7f);
            Ui.Toast($"보상: 골드 {q.rewardGold}" + (q.rewardShards > 0 ? $", 기억의 조각 {q.rewardShards}개" : ""));
            EnsureQuests();
        }
    }
}
