using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Evolution flow orchestration. The whole pipeline:
    ///   Telemetry → Context → (LLM #1 concept→Rule IR, or LocalComposer) → RuleValidator (type/safety/build-aware power, numbers rebalanced)
    ///   → LLM repair with machine-readable errors (≤2) → Score → top N (System rules can change N) → (LLM #2 naming) → player picks
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
        List<AbilityDef> prefetched;                   // ready, validated, named offer
        int prefetchLevel = -1;                        // level the prefetched offer was balanced for
        bool prefetching;
        int prefetchRun;                               // invalidates coroutines from an older life
        string prefetchStatus;

        // Evolution chat
        public readonly List<(bool player, string text)> Chat = new List<(bool, string)>();
        [System.NonSerialized] public bool ChatBusy;
        public int ChatCost => 30 + Player.Level * 5;  // always more than a reroll
        public int RerollCost => Mathf.RoundToInt((15 + Player.Level * 3) * Player.Abilities.RerollMul);
        const int RepairRounds = 2;

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

        /// <summary>Seconds the evolution screen waits for the background AI offer before showing local cards instead.</summary>
        const float MaxOfferWait = 35f;

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
                List<AbilityDef> offer = null;
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
        IEnumerator BuildOffer(int level, bool allowLocal, List<string> log, System.Action<List<AbilityDef>> done, bool skipAi = false)
        {
            var ctx = GenerationContext.Build(Player, Player.Telemetry, rnd, level);
            log.Add($"① 플레이 기록: {Player.Telemetry.Summary()}");
            log.Add($"③ 패턴: {(ctx.patterns.Count == 0 ? "(데이터 부족)" : string.Join(", ", ctx.patterns.Take(3).Select(p => $"{p.desc} {p.confidence:0.00}")))}");
            if (ctx.seededSources.Count > 0) log.Add($"④ 씨앗 출처: {string.Join(", ", ctx.seededSources)}");
            log.Add($"⑦ 기본 요소 검색: 관련 {ctx.retrieved.Count}개 + 탐험 {ctx.exploration.Count}개 (복잡도 티어 ≤ {ctx.maxTier})");

            List<AbilityDef> raw = null;
            bool ai = AiClient.Enabled && !skipAi;
            if (ai)
            {
                log.Add("⑧ LLM #1 호출 (" + Settings.aiModel + ")");
                yield return AiClient.Generate(ctx, 3, r => raw = r);
                log.Add("   → " + AiClient.LastStatus);
            }
            else log.Add(skipAi ? "⑧ AI가 이미 시도했으나 시간 안에 실패 → 로컬 생성기" : AiClient.HasKey ? "⑧ AI 꺼짐 → 로컬 생성기" : "⑧ API 키 없음 → 로컬 생성기");
            if (Player == null) { done(null); yield break; }

            var rejected = new List<(AbilityDef a, RuleValidator.Result r)>();
            var valid = AbilityPipeline.ValidateAll(raw, ctx.build, log, out int aiOk, rejected);
            // Repair: the validator's machine-readable errors go back to the LLM (structure only; numbers are the engine's)
            // Repair only when NO AI card survived — each repair round is a full extra LLM call (~10 s). One usable AI card
            // plus local fill-ins is a fine offer; the player is never kept waiting for a second or third AI card.
            // Someone is looking at the evolution screen right now (foreground, or a prefetch they are waiting on) → 1 round
            bool watched = allowLocal || State == GameState.Evolving && Offer == null;
            int rounds = watched ? 1 : RepairRounds;
            for (int round = 0; ai && round < rounds && rejected.Count > 0 && valid.Count == 0; round++)
            {
                var broken = rejected.Where(x => x.r.failedStage != "Duplicate").Take(3).ToList();
                rejected.Clear();
                if (broken.Count == 0) break;
                log.Add($"⑩' 수리 요청 {round + 1}회차: {broken.Count}개");
                List<AbilityDef> fixedList = null;
                yield return AiClient.Repair(ctx, broken, r => fixedList = r);
                if (Player == null) { done(null); yield break; }
                if (fixedList == null) break;
                for (int i = 0; i < fixedList.Count && i < broken.Count; i++) if (string.IsNullOrEmpty(fixedList[i].concept)) fixedList[i].concept = broken[i].a.concept;
                var more = AbilityPipeline.ValidateAll(fixedList, ctx.build, log, out int fixedOk, rejected);
                valid.AddRange(more.Where(m => valid.All(v => v.Signature() != m.Signature())));
                aiOk += fixedOk;
            }
            // Need at least 2 usable AI cards; otherwise the attempt counts as a failure while AI is on
            if (ai && aiOk < 1 && !allowLocal) { done(null); yield break; }
            var local = LocalComposer.Compose(ctx, 4, rnd);
            valid.AddRange(AbilityPipeline.ValidateAll(local, ctx.build, null, out _).Where(l => valid.All(v => v.Signature() != l.Signature())));

            // Final offers also pass the headless simulation (proc storms, entity floods, power divergence)
            var simulated = valid.Where(v =>
            {
                var res = RuleValidator.Validate(v.Clone(), ctx.build, false, true);
                if (!res.ok) log.Add($"   ✗ 시뮬레이션 {v.mechanic}: {res.Summary()}");
                return res.ok;
            }).ToList();
            if (simulated.Count >= 2) valid = simulated;
            var pick = AbilityPipeline.PickTop(valid, ctx, Player, Player.Abilities.OfferCount);
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
            // 1) Still being prepared in the background → wait for it, but not forever: after MaxOfferWait the cards come
            //    from the local generator and the AI answer (if it arrives) is kept for the next evolution
            OfferStatus = "AI가 당신의 플레이를 읽는 중…";
            float waitUntil = Time.unscaledTime + MaxOfferWait;
            while (prefetching && prefetched == null && Time.unscaledTime < waitUntil)
            {
                OfferStatus = $"AI가 당신의 플레이를 읽는 중… ({Mathf.CeilToInt(waitUntil - Time.unscaledTime)}초)";
                yield return null;
            }
            if (Player == null) yield break;
            bool waitedOut = prefetching && prefetched == null;
            // 2) A prefetched offer is ready → show it immediately (re-validated: rebalanced for the current level, still not owned)
            if (prefetched != null && prefetched.Count <= Player.Abilities.OfferCount && prefetched.All(g => RuleValidator.Validate(g, BuildContext.Of(Player)).ok))
            {
                var ready = prefetched;
                string note = prefetchStatus;
                prefetched = null;
                ShowOffer(ready, note);
                yield break;
            }
            prefetched = null;
            // 3) Nothing usable prepared. If the background AI already had its chance (it failed or is too slow), don't run a
            //    second AI round while the player watches — show local cards now. Otherwise (reroll, shrine, AI off) generate.
            PipelineLog.Clear();
            List<AbilityDef> offer = null;
            bool aiAlreadyTried = waitedOut || !string.IsNullOrEmpty(prefetchStatus);
            yield return BuildOffer(level, true, PipelineLog, r => offer = r, skipAi: aiAlreadyTried);
            if (Player == null) yield break;
            string why = waitedOut ? $"AI 응답이 {MaxOfferWait:0}초 안에 오지 않아 로컬 후보를 먼저 보여 줍니다"
                : AiClient.Enabled && (offer == null || offer.All(g => g.source != "ai")) ? AiClient.LastStatus : null;
            ShowOffer(offer ?? new List<AbilityDef>(), why);
            if (!waitedOut) RequestPrefetch();
            else StartCoroutine(SwapInLateAi(offer));      // the AI is still working: if it lands before the player picks, show it
        }

        /// <summary>
        /// The screen fell back to local cards while the AI was still answering. If the AI answer arrives while these same
        /// cards are still up (no pick, no reroll, no chat in progress), swap it in — re-validated for the current build.
        /// </summary>
        IEnumerator SwapInLateAi(List<AbilityDef> shown)
        {
            while (prefetching && prefetched == null && Offer == shown) yield return null;
            if (Offer != shown || prefetched == null || ChatBusy || Player == null) yield break;
            var b = BuildContext.Of(Player);
            var late = prefetched.Where(g => RuleValidator.Validate(g, b).ok).ToList();
            if (late.Count == 0 || late.Count(g => g.source == "ai") == 0) yield break;
            prefetched = null;
            Offer = late;
            OfferShownAt = Time.unscaledTime;           // cards changed under the cursor → lock clicks briefly
            OfferSummary = "AI 후보가 도착해 카드를 바꿨습니다.";
            Chat.Add((false, "늦었지만, 네 플레이를 읽고 새 후보를 가져왔어."));
            Sfx.Play("evolve", 0.4f, 1.4f);
        }

        void ShowOffer(List<AbilityDef> offer, string note)
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
            World.Log($"{Ko.I(HeroName)} [{g.name}] 능력을 얻었다. ({g.Explain().Replace("\n", " / ")})", "evolve");
            Fx.I?.Play(Art.Shockwave(Pal.ShadowEl, 30), Player.Pos, 0, 18, 1, null, true);
            Fx.I?.Burst(Player.Pos + Vector2.up * 0.5f, Pal.ShadowEl, 30, 6, 0.9f, -3);
            Sfx.Play("levelup");
            Offer = null;
            GameTime.Resume("evolve");
            State = GameState.Playing;
            // The owned set changed → regenerate the next prefetch so it doesn't duplicate this ability.
            // A request already in flight is kept if more evolutions are queued (its answer is re-validated against the new build).
            if (Player.PendingEvolutions > 0 && prefetching) { }
            else { ResetPrefetch(); RequestPrefetch(); }
            // If there are attribute points, choose them right away in the next screen
            if (Player.UnspentAttr > 0) OpenOverlay(GameState.Paused, "attrs");
        }

        public void RerollEvolution()
        {
            if (Player.Gold < RerollCost || OfferLocked || ChatBusy) return;
            Player.Gold -= RerollCost;
            Offer = null;
            Generating = false;
            prefetchStatus = null;      // a paid reroll gives the AI a fresh chance even if the last attempt failed
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
            string reply = null; List<AbilityDef> revised = null;
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
            var fresh = new List<AbilityDef>();
            var build = BuildContext.Of(Player);
            for (int i = 0; i < Offer.Count && i < revised.Count; i++)
            {
                var g = revised[i];
                if (g == null) continue;            // the storyteller kept this card
                var sig = g.Signature();
                if (Offer.Any(o => o.Signature() == sig)) continue;   // unchanged (or moved)
                var res = RuleValidator.Validate(g, build);
                if (!res.ok)
                {
                    // Salvage: keep the old card's (valid) structure and apply only what can be read off the new one —
                    // an element change, a new name/concept — so "make it fire" still works when the AI's structure is broken
                    var salvaged = Retint(Offer[i], g, (Chat.LastOrDefault(c => c.player).text ?? "") + " " + reply + " " + g.concept);
                    if (salvaged != null && RuleValidator.Validate(salvaged, build).ok) { g = salvaged; res = null; }
                }
                if (res != null && !res.ok || Offer.Any(o => o != Offer[i] && o.Signature() == g.Signature())) { refused++; continue; }
                g.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                LocalComposer.Name(g);
                Offer[i] = g;
                fresh.Add(g);
                changed++;
            }
            // The reply goes up the moment it arrives (the "thinking…" line is replaced in the same frame);
            // naming the changed cards happens after, in place, without holding the conversation
            string note = changed > 0 ? $" <color=#8bff9a>(후보 {changed}개 변경)</color>" : "";
            if (refused > 0) note += $" <color=#8c7aa8>(규칙에 맞지 않는 제안 {refused}개는 반영하지 않음)</color>";
            Chat.Add((false, reply + note));
            if (changed > 0)
            {
                OfferShownAt = Time.unscaledTime;   // cards changed under the cursor → lock clicks again briefly
                Sfx.Play("evolve", 0.4f, 1.4f);
                yield return AiClient.NameAll(fresh, Diverse.World.I.Gen.RegionName(Player.Pos), () => { });
            }
        }

/// <summary>The old card with the element the storyteller's (invalid) revision asked for. Null when there is nothing to carry over.</summary>
        static AbilityDef Retint(AbilityDef old, AbilityDef revised, string text)
        {
            string el = revised.rules.SelectMany(r => r.ops).Select(o => o.element).FirstOrDefault(RuleLanguage.IsElement) ?? ElementIn(text);
            if (el == null) return null;
            var c = old.Clone();
            bool any = false;
            foreach (var o in c.rules.SelectMany(r => r.ops))
            {
                var p = RuleLanguage.Get(o.op);
                if (p == null || p.kinds != "T" || o.op is "AddState" or "SetState" or "StorePosition" or "StoreTarget") continue;
                o.element = el;
                if (o.op == "ApplyStatus" || o.op == "Detonate") o.status = RuleLanguage.StatusOf(el) ?? o.status;
                any = true;
            }
            if (!any)
            {
                // No op that carries an element (a stat / trade-off card): give the card that element's character —
                // its rules now count as that element (meta/build scaling sees it) and gain a small elemental aura
                var aura = new RuleDef { kind = RuleKind.Continuous };
                aura.ops.Add(new OpDef { op = "Aura", element = el, status = RuleLanguage.StatusOf(el), amount = 0.15f, radius = 2.2f });
                c.rules.Add(aura);
                any = true;
            }
            if (c.Signature() == old.Signature()) return null;
            c.id = null; c.name = null; c.desc = null; c.source = "ai";
            c.concept = revised.concept ?? old.concept;
            return c;
        }

/// <summary>An element named in Korean or English text ("화염으로 바꿔 줘", "make it frost").</summary>
        static string ElementIn(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string t = text.ToLowerInvariant();
            (string word, string el)[] map =
            {
                ("화염", "Fire"), ("불", "Fire"), ("fire", "Fire"), ("냉기", "Frost"), ("얼음", "Frost"), ("서리", "Frost"), ("frost", "Frost"), ("ice", "Frost"),
                ("번개", "Lightning"), ("전기", "Lightning"), ("lightning", "Lightning"), ("그림자", "Shadow"), ("어둠", "Shadow"), ("shadow", "Shadow"),
                ("신성", "Holy"), ("빛", "Holy"), ("holy", "Holy"), ("독", "Poison"), ("poison", "Poison"), ("바람", "Wind"), ("wind", "Wind"),
                ("피", "Blood"), ("blood", "Blood"), ("공허", "Void"), ("void", "Void"), ("비전", "Arcane"), ("arcane", "Arcane"),
            };
            // the earliest mention wins (the player's request comes first in the text)
            return map.Select(m => (m.el, i: t.IndexOf(m.word, System.StringComparison.Ordinal))).Where(x => x.i >= 0).OrderBy(x => x.i).Select(x => x.el).FirstOrDefault();
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
