using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// 에디터 메뉴: Diverse/…
    /// - 파이프라인 검증: 로컬 생성기 → RuleValidator를 수백 번 돌리고, 기준 능력(RuleSamples)·거절 샘플·구 세이브 변환을 확인
    /// - AI 키 파일 열기: openai_key.txt 위치를 탐색기로 연다
    /// - 세이브 초기화
    /// </summary>
    public static class DiverseTools
    {
        [MenuItem("Diverse/메인 메뉴 씬 열기", priority = 0)]
        public static void OpenMain() => EditorSceneManager.OpenScene(UIBuilder.MenuScenePath);

        [MenuItem("Diverse/능력 파이프라인 검증", priority = 20)]
        public static string ValidatePipeline() => RunPipelineCheck(out _);

        /// <summary>Batch: Unity -batchmode -nographics -executeMethod Diverse.EditorTools.DiverseTools.ValidatePipelineBatch (exit 0 = pass)</summary>
        public static void ValidatePipelineBatch()
        {
            string report;
            bool ok;
            try { report = RunPipelineCheck(out ok); }
            catch (System.Exception e) { report = e.ToString(); ok = false; }
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/RulePipeline.txt", report);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static Player TestPlayer(WeaponKind wk, int level, string name = "TestPlayer")
        {
            var go = new GameObject(name);
            var p = go.AddComponent<Player>();
            p.Weapon = DB.W(wk); p.Costume = DB.Costumes[(int)wk % DB.Costumes.Count];
            p.Abilities = new AbilityRuntime(p);
            p.Level = level;
            return p;
        }

        /// <summary>
        /// 1) Local composer → RuleValidator hundreds of times per weapon: no exceptions, a healthy pass rate, tier spread.
        /// 2) The hand-written reference abilities (RuleSamples) must all pass on one build and run their text/power paths.
        /// 3) Hostile samples must be rejected (or repaired with the expected code).
        /// 4) Old-format (AbilityGraph) save JSON must convert.
        /// </summary>
        static string RunPipelineCheck(out bool pass)
        {
            var sb = new StringBuilder();
            var fails = new System.Collections.Generic.List<string>();
            int total = 0, ok = 0, rejected = 0, exceptions = 0;
            var rnd = new System.Random(1234);
            var tiers = new int[5];
            var reasons = new System.Collections.Generic.Dictionary<string, int>();
            foreach (WeaponKind wk in System.Enum.GetValues(typeof(WeaponKind)))
            {
                var p = TestPlayer(wk, 3);
                try
                {
                    for (int i = 0; i < 40; i++) { p.Telemetry.Record("Dash"); p.Telemetry.Record("Attack"); p.Telemetry.Record("Hit"); }
                    for (int i = 0; i < 8; i++) p.Telemetry.Record("Kill");
                    for (int round = 0; round < 30; round++)
                    {
                        p.Level = 3 + round / 3;
                        var ctx = GenerationContext.Build(p, p.Telemetry, rnd);
                        foreach (var g in LocalComposer.Compose(ctx, 4, rnd))
                        {
                            total++;
                            try
                            {
                                var r = RuleValidator.Validate(g, BuildContext.Of(p));
                                if (!r.ok)
                                {
                                    rejected++;
                                    foreach (var e in r.Errors) reasons[e.code] = reasons.TryGetValue(e.code, out var n) ? n + 1 : 1;
                                    continue;
                                }
                                ok++;
                                tiers[Mathf.Clamp(g.tier, 0, 4)]++;
                                LocalComposer.Name(g);
                                if (string.IsNullOrEmpty(g.name)) throw new System.Exception("이름 없음 " + g.mechanic);
                                if (string.IsNullOrEmpty(g.Explain())) throw new System.Exception("설명 없음 " + g.mechanic);
                                if (AbilityRecord.From(g).ToAbility()?.Signature() != g.Signature()) throw new System.Exception("세이브 왕복 실패 " + g.mechanic);
                                if (round % 5 == 0 && p.Abilities.Owned.Count < 8) { g.id = "t" + total; p.Abilities.Restore(g); }
                            }
                            catch (System.Exception e) { exceptions++; if (exceptions < 5) sb.AppendLine("예외: " + e); }
                        }
                    }
                }
                finally { Object.DestroyImmediate(p.gameObject); }
            }
            float rate = total == 0 ? 0 : ok / (float)total;
            sb.Insert(0, $"로컬 후보 {total}개: 통과 {ok} ({rate:P0}), 거부 {rejected}, 예외 {exceptions}\n" +
                         $"티어 분포 (0 수치/1 반응/2 상태/3 메타/4 체계): {string.Join(" / ", tiers)}\n" +
                         $"거부 사유 상위: {string.Join(", ", reasons.OrderByDescending(k => k.Value).Take(8).Select(k => k.Key + "=" + k.Value))}\n");
            if (exceptions > 0) fails.Add($"exceptions {exceptions}");
            if (rate < 0.35f) fails.Add($"local pass rate {rate:P0} < 35%");
            if (tiers[2] == 0) fails.Add("no stateful (tier 2) local abilities");

            // 2) Reference abilities — all on one late-game build (tiers unlocked), each validated against the build so far
            sb.AppendLine("\n[기준 능력]");
            var hp = TestPlayer(WeaponKind.Katana, 12, "RefPlayer");
            // a poison source so 역병 has something to spread (abilities are judged on a build they fit)
            { var ps = RuleSamples.PoisonSource(); if (RuleValidator.Validate(ps, BuildContext.Of(hp)).ok) { ps.id = "poison"; hp.Abilities.Restore(ps); } }
            try
            {
                // Two fire sources: build-scaling and meta abilities are only offered once there is something to scale
                foreach (var (fire, i) in new[] { (RuleSamples.FireSource(), 0), (RuleSamples.FireSource2(), 1) })
                {
                    var fr = RuleValidator.Validate(fire, BuildContext.Of(hp));
                    if (!fr.ok) fails.Add("fire source rejected: " + fr.Summary());
                    else { fire.id = "fire" + i; hp.Abilities.Restore(fire); }
                }
                foreach (var a in RuleSamples.Accepted())
                {
                    var r = RuleValidator.Validate(a, BuildContext.Of(hp), true, true);
                    sb.AppendLine($"{(r.ok ? "✓" : "✗")} {a.name} — 티어 {a.tier}, {r.Summary()} ({r.vector}) 위험[{r.risk}]");
                    if (r.sim != null) sb.AppendLine("   " + r.sim);
                    if (r.Repairs.Any()) sb.AppendLine("   수리: " + string.Join(", ", r.Repairs.Select(x => x.code)));
                    if (!r.ok) { fails.Add($"reference '{a.name}' rejected: {r.Summary()}"); continue; }
                    sb.AppendLine("   " + a.Explain().Replace("\n", "\n   "));
                    a.id = "ref" + hp.Abilities.Owned.Count;
                    hp.Abilities.Restore(a);
                }
                var report = PowerModel.Evaluate(hp.Abilities.Owned, BuildContext.Of(hp));
                sb.AppendLine($"빌드 전체 Power {report.Total:0} ({report.Vector()})  선택지 수 {hp.Abilities.OfferCount}, 티어 보정 +{hp.Abilities.TierBias}");
                if (hp.Abilities.OfferCount != 2) fails.Add("OfferCount system rule not applied");
            }
            finally { Object.DestroyImmediate(hp.gameObject); }

            // 3) Hostile samples
            sb.AppendLine("\n[거절/수리돼야 하는 샘플]");
            var hostile = TestPlayer(WeaponKind.Greatsword, 12, "HostilePlayer");
            try
            {
                var partner = RuleSamples.LoopPartner();
                partner.EnsureLists(); RuleValidator.Tag(partner); partner.id = "loop";
                hostile.Abilities.Restore(partner);
                foreach (var (a, expect, code) in RuleSamples.Hostile())
                {
                    var r = RuleValidator.Validate(a, BuildContext.Of(hostile));
                    bool hit = expect == "reject" ? !r.ok && r.Errors.Any(e => e.code == code) : r.Repairs.Any(e => e.code == code);
                    sb.AppendLine($"{(hit ? "✓" : "✗")} {a.name}: 기대 {expect}/{code} → {(r.ok ? "통과" : "거부")} {string.Join(", ", r.issues.Select(i => i.ToString()))}");
                    if (!hit) fails.Add($"hostile '{a.name}' expected {expect} {code}");
                }
            }
            finally { Object.DestroyImmediate(hostile.gameObject); }

            // 5) Expressiveness: TFT/Sephiria-style abilities written as intents → compiler → validator (incl. simulation)
            //    expressible = compiles and passes every STRUCTURAL stage (schema/type/reference/capability/bounds/cycle/risk)
            //    balanced    = also passes power + simulation on at least one of three archetype builds (like an augment that fits a comp)
            sb.AppendLine("\n[표현력 테스트: TFT 증강·세피리아 아티팩트 패턴]");
            var builds = new List<(string name, Player p)>();
            int exprOk = 0, exprBalanced = 0, exprTotal = 0;
            try
            {
                (string, WeaponKind, AbilityDef[])[] archetypes =
                {
                    ("방패 근접", WeaponKind.SwordShield, new[] { RuleSamples.DashSource(), RuleSamples.FrostSource(), RuleSamples.FireSource(), RuleSamples.PoisonSource() }),
                    ("화염 술사", WeaponKind.Staff, new[] { RuleSamples.FireSource(), RuleSamples.FireSource2(), RuleSamples.SkillSource(), RuleSamples.FrostSource() }),
                    ("질주 소환", WeaponKind.Dagger, new[] { RuleSamples.DashSource(), RuleSamples.CloneSource(), RuleSamples.FireSource2() }),
                };
                foreach (var (name, wk, seeds) in archetypes)
                {
                    var bp = TestPlayer(wk, 14, "Expr_" + name);
                    builds.Add((name, bp));
                    foreach (var seed in seeds)
                    {
                        var sr = RuleValidator.Validate(seed, BuildContext.Of(bp));
                        if (sr.ok) { seed.id = "seed" + bp.Abilities.Owned.Count; bp.Abilities.Restore(seed); }
                        else fails.Add($"expressiveness seed rejected ({name}): {seed.name} {sr.Summary()}");
                    }
                }
                string[] structural = { "Schema", "Type", "Reference", "Capability", "Bounds", "Cycle", "Risk" };
                foreach (var c in RuleExpressiveness.All())
                {
                    exprTotal++;
                    var comp = AbilityCompiler.Compile(c.intent);
                    RuleValidator.Result best = null; string bestBuild = null; AbilityDef bestA = null;
                    foreach (var (name, bp) in builds)
                    {
                        var a = comp.ability.Clone(); a.name = c.name;
                        var r = RuleValidator.Validate(a, BuildContext.Of(bp), true, true);
                        if (best == null || r.ok && !best.ok) { best = r; bestBuild = name; bestA = a; }
                        if (r.ok) break;
                    }
                    bool expressible = best.ok || !structural.Contains(best.failedStage);
                    if (expressible) exprOk++;
                    if (best.ok) exprBalanced++;
                    string mark = best.ok ? "✓" : expressible ? "△" : "✗";
                    sb.AppendLine($"{mark} {c.name} ({c.pattern}) — 티어 {bestA.tier}, {(best.ok ? bestBuild + " 빌드: " : "")}{best.Summary()}");
                    if (comp.notes.Count > 0) sb.AppendLine("   컴파일러: " + string.Join("; ", comp.notes));
                    if (best.ok) { sb.AppendLine("   " + bestA.Explain().Replace("\n", "\n   ")); if (best.sim != null) sb.AppendLine("   " + best.sim); }
                    else sb.AppendLine("   " + string.Join("; ", best.Errors.Select(e => e.ToString())));
                }
            }
            finally { foreach (var (_, bp) in builds) Object.DestroyImmediate(bp.gameObject); }
            sb.Insert(0, $"표현력 테스트: 표현 가능 {exprOk}/{exprTotal}, 밸런스 통과 {exprBalanced}/{exprTotal}\n");
            if (exprOk < exprTotal) fails.Add($"expressiveness {exprOk}/{exprTotal}");
            if (exprBalanced < exprTotal * 0.85f) fails.Add($"balanced {exprBalanced}/{exprTotal} (< 85%)");

            // 6) Language census
            sb.AppendLine($"\n[언어 규모] " + string.Join(", ", RuleLanguage.All.GroupBy(p => p.cat).Select(g => g.Key + " " + g.Count())) +
                $", Relation {RuleLanguage.Relations.Count}, Temporal {RuleLanguage.Temporals.Count}, Entity {RuleLanguage.Entities.Length}, Status {RuleLanguage.Statuses.Length}, Element {RuleLanguage.Elements.Length}, Convert {RuleLanguage.ConvertPairs.Count}, State {RuleLanguage.StateTypes.Length}");

            // 4) Legacy save conversion
            string legacy = "{\"id\":\"x\",\"name\":\"남겨진 잔영\",\"kind\":\"trigger\",\"trigger\":\"Dash\",\"conditions\":[{\"type\":\"Cooldown\",\"value\":2}],\"effects\":[{\"action\":\"Spawn\",\"entity\":\"Clone\",\"form\":\"DashOrigin\",\"relation\":\"Copy\",\"power\":0.6,\"duration\":3,\"count\":1}],\"tags\":[\"CLONE\"]}";
            var conv = new AbilityRecord { json = legacy, name = "남겨진 잔영" }.ToAbility();
            bool convOk = conv != null && conv.rules.Count == 1 && conv.rules[0].trigger == "Dash" && conv.rules[0].ops[0].op == "Spawn";
            sb.AppendLine($"\n[구 세이브 변환] {(convOk ? "✓ " + conv.Explain() : "✗")}");
            if (!convOk) fails.Add("legacy conversion");

            pass = fails.Count == 0;
            sb.Insert(0, pass ? "PASS\n" : "FAIL: " + string.Join("; ", fails) + "\n");
            Debug.Log("[Diverse] 능력 파이프라인 검증\n" + sb);
            return sb.ToString();
        }

        [MenuItem("Diverse/AI 키 파일 위치 열기", priority = 40)]
        public static void OpenKeyFolder()
        {
            var path = SaveSystem.KeyFileHint;
            if (!System.IO.File.Exists(path)) System.IO.File.WriteAllText(path, "");
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Diverse/세계 세이브 초기화", priority = 60)]
        public static void ResetSave()
        {
            if (EditorUtility.DisplayDialog("세계 초기화", "모든 삶의 기록(무덤·연대기·탑)을 지울까요?", "지우기", "취소"))
                SaveSystem.DeleteWorld();
        }
    }
}
