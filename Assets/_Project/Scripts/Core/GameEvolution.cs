using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Evolution flow orchestration. The whole pipeline:
    ///   Telemetry → Context → (LLM #1 or LocalComposer) → Validate/Repair → Synergy/Balance → Score → top 3 → (LLM #2 naming) → player picks
    ///
    /// Prefetch: as soon as level N is reached, the offer for level N+1 starts generating in the background from the play data so far.
    /// If the AI answer is unusable it keeps retrying until the level-up actually happens, so the evolution screen opens instantly
    /// and the local generator is only a last resort.
    /// </summary>
    public partial class Game
    {
        public readonly List<string> PipelineLog = new List<string>();
        /// <summary>One plain sentence for the player: where this offer came from.</summary>
        [System.NonSerialized] public string OfferSummary;
        [System.NonSerialized] public float OfferShownAt;                     // unscaled time the cards appeared (click lockout)
        public const float OfferLockSeconds = 0.8f;

        // Background prefetch state
        List<AbilityGraph> prefetched;                 // ready, validated, named offer
        int prefetchLevel = -1;                        // level the prefetched offer was balanced for
        bool prefetching;
        int prefetchRun;                               // invalidates coroutines from an older life
        string prefetchStatus;

        // Evolution chat
        public readonly List<(bool player, string text)> Chat = new List<(bool, string)>();
        [System.NonSerialized] public bool ChatBusy;
        public int ChatCost => 30 + Player.Level * 5;  // always more than a reroll
        public int RerollCost => 15 + Player.Level * 3;

        public bool OfferLocked => Offer != null && Time.unscaledTime - OfferShownAt < OfferLockSeconds;

        // ───────────────────────── Prefetch ─────────────────────────

        /// <summary>Called on every level-up (and at the start of a life): prepare the offer for the next level.</summary>
        public void RequestPrefetch()
        {
            if (Player == null || prefetching || prefetched != null) return;
            // Normally for the next level; if an evolution is already waiting, for that one
            int target = Player.Level + (Player.PendingEvolutions > 0 ? 0 : 1);
            StartCoroutine(PrefetchRoutine(target, prefetchRun));
        }

        void ResetPrefetch()
        {
            prefetchRun++;
            prefetched = null; prefetchLevel = -1; prefetching = false; prefetchStatus = null;
        }

        IEnumerator PrefetchRoutine(int level, int run)
        {
            prefetching = true;
            prefetchLevel = level;
            int attempt = 0;
            float wait = 2f;
            while (run == prefetchRun && Player != null)
            {
                attempt++;
                List<AbilityGraph> offer = null;
                var log = new List<string>();
                yield return BuildOffer(level, false, log, r => offer = r);
                if (run != prefetchRun || Player == null) yield break;
                if (offer != null)
                {
                    prefetched = offer;
                    if (State != GameState.Evolving || Offer == null) { PipelineLog.Clear(); PipelineLog.AddRange(log); }
                    prefetchStatus = attempt > 1 ? $"AI 응답을 {attempt}번 시도 끝에 준비했다." : null;
                    break;
                }
                prefetchStatus = AiClient.LastStatus;
                // Retrying can't fix a bad key / unknown model / no credit → stop and let the level-up fall back to local
                if (AiClient.LastFatal || !AiClient.Enabled) break;
                // The level-up already happened and the player is waiting → stop retrying, the screen falls back to local
                if (State == GameState.Evolving && Offer == null && Player.Level >= level) break;
                float until = Time.unscaledTime + wait;
                while (Time.unscaledTime < until && run == prefetchRun) yield return null;
                wait = Mathf.Min(wait * 1.6f, 20f);
            }
            if (run == prefetchRun) prefetching = false;
        }

        /// <summary>
        /// Generate → validate → score → name. Calls back with 3 cards, or null if the AI produced nothing usable and
        /// allowLocal is false. With AI off / no key, local candidates are always accepted.
        /// </summary>
        IEnumerator BuildOffer(int level, bool allowLocal, List<string> log, System.Action<List<AbilityGraph>> done)
        {
            var ctx = GenerationContext.Build(Player, Player.Telemetry, rnd);
            ctx.tier = level / 3;
            log.Add($"① 플레이 기록: {Player.Telemetry.Summary()}");
            log.Add($"③ 패턴: {(ctx.patterns.Count == 0 ? "(데이터 부족)" : string.Join(", ", ctx.patterns.Take(3).Select(p => $"{p.desc} {p.confidence:0.00}")))}");
            if (ctx.seededSources.Count > 0) log.Add($"④ 씨앗 출처: {string.Join(", ", ctx.seededSources)}");
            log.Add($"⑦ 기본 요소 검색: 관련 {ctx.retrieved.Count}개 + 탐험 {ctx.exploration.Count}개");

            List<AbilityGraph> raw = null;
            bool ai = AiClient.Enabled;
            if (ai)
            {
                log.Add("⑧ LLM #1 호출 (" + Settings.aiModel + ")");
                yield return AiClient.Generate(ctx, 4, r => raw = r);
                log.Add("   → " + AiClient.LastStatus);
            }
            else log.Add(AiClient.HasKey ? "⑧ AI 꺼짐 → 로컬 생성기" : "⑧ API 키 없음 → 로컬 생성기");
            if (Player == null) { done(null); yield break; }

            var valid = ValidateAll(raw, level, log, out int aiOk);
            // Need at least 2 usable AI cards; otherwise the attempt counts as a failure while AI is on
            if (ai && aiOk < 2 && !allowLocal) { done(null); yield break; }
            var local = LocalComposer.Compose(ctx, 4, rnd);
            valid.AddRange(ValidateAll(local, level, null, out _).Where(l => valid.All(v => v.Signature() != l.Signature())));

            var pick = PickTop3(valid, ctx);
            log.Add($"⑫⑬ 시너지·점수 → 상위 {pick.Count}개");

            foreach (var g in pick) LocalComposer.Name(g);
            if (ai && pick.Any(g => g.source == "ai"))
            {
                yield return AiClient.NameAll(pick, Diverse.World.I.Gen.RegionName(Player.Pos), () => { });
                log.Add("⑮ LLM #2 이름 짓기 완료");
            }
            else log.Add("⑮ 로컬 이름 짓기");

            done(pick);
        }

        List<AbilityGraph> ValidateAll(List<AbilityGraph> candidates, int level, List<string> log, out int ok)
        {
            var valid = new List<AbilityGraph>();
            int rejected = 0, repaired = 0;
            ok = 0;
            if (candidates == null) return valid;
            foreach (var g in candidates)
            {
                var res = AbilityValidator.Validate(g, Player, level);
                if (!res.ok) { rejected++; log?.Add($"   ✗ {g.mechanic}: {string.Join(", ", res.errors)}"); continue; }
                if (res.repairs.Count > 0) { repaired++; log?.Add($"   ⚙ {g.mechanic}: {string.Join(", ", res.repairs)}"); }
                if (valid.Any(v => v.Signature() == g.Signature())) continue;
                g.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                valid.Add(g);
            }
            ok = valid.Count;
            log?.Add($"⑩⑪ 검증: 통과 {valid.Count} / 거부 {rejected} / 수리 {repaired}");
            return valid;
        }

        List<AbilityGraph> PickTop3(List<AbilityGraph> valid, GenerationContext ctx)
        {
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
            return pick;
        }

        // ───────────────────────── Showing the offer ─────────────────────────

        public void BeginEvolution()
        {
            if (Generating || Player == null) return;
            StartCoroutine(EvolutionRoutine());
        }

        IEnumerator EvolutionRoutine()
        {
            Generating = true;
            Offer = null;
            Chat.Clear();
            State = GameState.Evolving;
            GameTime.Pause("evolve");
            Sfx.Play("evolve", 0.6f);
            Ui.OnOverlayOpened(GameState.Evolving, null);

            int level = Player.Level;
            RequestPrefetch();   // nothing in flight (e.g. a shrine evolution) → start one now and wait for it
            // 1) Still being prepared in the background → wait for it (it gives up once the AI can't succeed)
            OfferStatus = "AI가 당신의 플레이를 읽는 중…";
            while (prefetching && prefetched == null) yield return null;
            if (Player == null) yield break;
            // 2) A prefetched offer is ready → show it immediately (re-validated: rebalanced for the current level, still not owned)
            if (prefetched != null && prefetched.All(g => AbilityValidator.Validate(g, Player).ok))
            {
                var ready = prefetched;
                string note = prefetchStatus;
                prefetched = null;
                ShowOffer(ready, note);
                yield break;
            }
            prefetched = null;
            // 3) Nothing prepared (reroll, AI off, repeated failures) → generate now; local fills in if the AI fails
            PipelineLog.Clear();
            List<AbilityGraph> offer = null;
            yield return BuildOffer(level, true, PipelineLog, r => offer = r);
            if (Player == null) yield break;
            string why = AiClient.Enabled && (offer == null || offer.All(g => g.source != "ai")) ? AiClient.LastStatus : null;
            ShowOffer(offer ?? new List<AbilityGraph>(), why);
            RequestPrefetch();
        }

        void ShowOffer(List<AbilityGraph> offer, string note)
        {
            Offer = offer;
            OfferStatus = null;
            Generating = false;
            OfferShownAt = Time.unscaledTime;
            int ai = offer.Count(g => g.source == "ai");
            OfferSummary = !AiClient.HasKey ? "API 키가 없어 로컬 생성기가 만든 후보입니다."
                : !Settings.aiEnabled ? "AI가 꺼져 있어 로컬 생성기가 만든 후보입니다."
                : ai == offer.Count ? "AI가 최근 플레이를 읽고 만든 후보입니다."
                : ai > 0 ? $"AI 후보 {ai}개 + 로컬 후보 {offer.Count - ai}개입니다."
                : "AI 응답을 쓸 수 없어 로컬 생성기가 만든 후보입니다.";
            if (!string.IsNullOrEmpty(note)) OfferSummary += $" ({note})";
            Chat.Add((false, ai > 0 ? "어떤 능력이 마음에 드니? 바라는 게 있으면 말해 주렴. 후보를 고쳐 볼게." : "원하는 방향이 있으면 말해 주렴."));
        }

        public void ChooseEvolution(int i)
        {
            if (Offer == null || i < 0 || i >= Offer.Count || OfferLocked || ChatBusy) return;
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
            // The owned set changed → regenerate the next prefetch so it doesn't duplicate this ability
            ResetPrefetch();
            RequestPrefetch();
            // If there are attribute points, choose them right away in the next screen
            if (Player.UnspentAttr > 0) OpenOverlay(GameState.Paused, "attrs");
        }

        public void RerollEvolution()
        {
            if (Player.Gold < RerollCost || OfferLocked || ChatBusy) return;
            Player.Gold -= RerollCost;
            Offer = null;
            Generating = false;
            BeginEvolution();
        }

        public void SkipEvolution()
        {
            if (ChatBusy) return;
            Offer = null;
            Player.PendingEvolutions--;
            Player.GainGold(20);
            GameTime.Resume("evolve");
            State = GameState.Playing;
            RequestPrefetch();
        }

        // ───────────────────────── Chat ─────────────────────────

        public bool CanChat => Offer != null && !ChatBusy && AiClient.Enabled && Player.Gold >= ChatCost;

        /// <summary>The player talks to the AI about the current cards. Costs gold; the AI may revise the cards.</summary>
        public void SendChat(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || !CanChat) return;
            if (text.Length > 200) text = text.Substring(0, 200);
            Player.Gold -= ChatCost;
            Chat.Add((true, text));
            StartCoroutine(ChatRoutine());
        }

        IEnumerator ChatRoutine()
        {
            ChatBusy = true;
            int cost = ChatCost;
            string reply = null; List<AbilityGraph> revised = null;
            var ctx = GenerationContext.Build(Player, Player.Telemetry, rnd);
            yield return AiClient.Chat(ctx, Offer, Chat, (r, a) => { reply = r; revised = a; });
            ChatBusy = false;
            if (Player == null || Offer == null) yield break;
            if (reply == null)
            {
                Player.Gold += cost;   // refund: the message never reached the storyteller
                Chat.Add((false, $"<color=#ff8a8a>(전달 실패: {AiClient.LastStatus} — 골드를 돌려받았다)</color>"));
                yield break;
            }

            // Replace only the cards that actually changed; anything invalid keeps the old card
            int changed = 0, refused = 0;
            var fresh = new List<AbilityGraph>();
            int level = Player.Level;
            for (int i = 0; i < Offer.Count && i < revised.Count; i++)
            {
                var g = revised[i];
                if (g == null) continue;
                var sig = g.Signature();
                if (Offer.Any(o => o.Signature() == sig)) continue;   // unchanged (or moved)
                var res = AbilityValidator.Validate(g, Player, level);
                if (!res.ok || Offer.Any(o => o != Offer[i] && o.Signature() == g.Signature())) { refused++; continue; }
                g.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                LocalComposer.Name(g);
                Offer[i] = g;
                fresh.Add(g);
                changed++;
            }
            if (changed > 0)
            {
                yield return AiClient.NameAll(fresh, Diverse.World.I.Gen.RegionName(Player.Pos), () => { });
                if (Offer == null) yield break;
                OfferShownAt = Time.unscaledTime;   // cards changed under the cursor → lock clicks again briefly
                Sfx.Play("evolve", 0.4f, 1.4f);
            }
            string note = changed > 0 ? $" <color=#8bff9a>(후보 {changed}개 변경)</color>" : "";
            if (refused > 0) note += $" <color=#8c7aa8>(규칙에 맞지 않는 제안 {refused}개는 반영하지 않음)</color>";
            Chat.Add((false, reply + note));
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
            // Keep quest targets inside the area the hero can currently reach
            float bound = Player != null ? Player.Bound : 0;
            if (bound > 0) { max = Mathf.Min(max, bound - 10); min = Mathf.Min(min, max * 0.5f); }
            float Cap(float v) => bound > 0 ? Mathf.Min(v, bound - 10) : v;
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
                    var s = gen.FindNearest(Vector2.zero, StructKind.Ruins, min, Cap(max * 1.3f), x => !World.Flag("ruin:" + x.id));
                    if (s == null) return null;
                    return new QuestState { id = "q" + salt, kind = "investigate", targetId = s.id, tx = s.center.x, ty = s.center.y, title = $"{s.name} 조사", desc = "이야기꾼이 오래된 제단의 비문을 궁금해해요.", rewardGold = 30, rewardShards = 2, status = 0 };
                }
                default:
                {
                    var s = gen.FindNearest(Vector2.zero, StructKind.Lair, min * 1.5f, Cap(max * 2f), x => !World.clearedCamps.Contains(x.id));
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
