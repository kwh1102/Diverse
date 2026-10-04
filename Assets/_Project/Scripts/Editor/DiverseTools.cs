using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// 에디터 메뉴: Diverse/…
    /// - 파이프라인 검증: 로컬 생성기 → 검증기 → 밸런스를 수백 번 돌려 실패/예외가 없는지 확인
    /// - AI 키 파일 열기: openai_key.txt 위치를 탐색기로 연다
    /// - 세이브 초기화
    /// </summary>
    public static class DiverseTools
    {
        [MenuItem("Diverse/메인 씬 열기", priority = 0)]
        public static void OpenMain() => EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity");

        [MenuItem("Diverse/능력 파이프라인 검증", priority = 20)]
        public static string ValidatePipeline()
        {
            var sb = new StringBuilder();
            int total = 0, ok = 0, rejected = 0, exceptions = 0;
            var rnd = new System.Random(1234);
            var kinds = new System.Collections.Generic.Dictionary<string, int>();
            foreach (WeaponKind wk in System.Enum.GetValues(typeof(WeaponKind)))
            {
                var go = new GameObject("TestPlayer");
                try
                {
                    var p = go.AddComponent<Player>();
                    p.Weapon = DB.W(wk); p.Costume = DB.Costumes[(int)wk % DB.Costumes.Count];
                    p.Abilities = new AbilityRuntime(p);
                    p.Level = 6;
                    // 가짜 행동 기록
                    for (int i = 0; i < 40; i++) { p.Telemetry.Record("Dash"); p.Telemetry.Record("Attack"); p.Telemetry.Record("Hit"); }
                    for (int i = 0; i < 8; i++) p.Telemetry.Record("Kill");
                    for (int round = 0; round < 30; round++)
                    {
                        var ctx = GenerationContext.Build(p, p.Telemetry, rnd);
                        foreach (var g in LocalComposer.Compose(ctx, 4, rnd))
                        {
                            total++;
                            try
                            {
                                var r = AbilityValidator.Validate(g, p);
                                if (!r.ok) { rejected++; continue; }
                                ok++;
                                LocalComposer.Name(g);
                                if (string.IsNullOrEmpty(g.name)) throw new System.Exception("이름 없음 " + g.mechanic);
                                g.Explain();
                                kinds[g.kind + ":" + g.trigger] = kinds.TryGetValue(g.kind + ":" + g.trigger, out var n) ? n + 1 : 1;
                                if (round % 6 == 0 && p.Abilities.Owned.Count < 8) { g.id = "t" + total; p.Abilities.Owned.Add(g); }
                            }
                            catch (System.Exception e) { exceptions++; if (exceptions < 5) sb.AppendLine("예외: " + e.Message); }
                        }
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            // AI 응답 형태(JSON) 파싱·수리 테스트
            string aiJson = "{\"abilities\":[{\"mechanic\":\"spawn_clone_after_dodge\",\"kind\":\"trigger\",\"trigger\":\"Dash\",\"conditions\":[],\"effects\":[{\"action\":\"Spawn\",\"entity\":\"Clone\",\"form\":\"DashOrigin\",\"relation\":\"Copy\"}],\"semanticReason\":\"test\"}," +
                            "{\"mechanic\":\"bad\",\"kind\":\"trigger\",\"trigger\":\"TimeReverse\",\"effects\":[{\"action\":\"Rewind\"}]}," +
                            "{\"mechanic\":\"hit_nova\",\"kind\":\"trigger\",\"trigger\":\"Hit\",\"effects\":[{\"action\":\"Nova\",\"element\":\"Fire\",\"relation\":\"Teleport\"}]}]}";
            var list = JsonUtility.FromJson<AiListProbe>(aiJson);
            var go2 = new GameObject("TestPlayer2");
            var p2 = go2.AddComponent<Player>(); p2.Weapon = DB.W(WeaponKind.Katana); p2.Costume = DB.Costumes[0]; p2.Abilities = new AbilityRuntime(p2); p2.Level = 3;
            int aiOk = 0;
            foreach (var g in list.abilities)
            {
                g.conditions ??= new System.Collections.Generic.List<CondNode>(); g.effects ??= new System.Collections.Generic.List<EffectNode>(); g.tags ??= new System.Collections.Generic.List<string>();
                var r = AbilityValidator.Validate(g, p2);
                sb.AppendLine($"AI샘플 {g.mechanic}: {(r.ok ? "통과" : "거부")} {string.Join(",", r.errors)} {string.Join(",", r.repairs)} → {(r.ok ? g.Explain() : "")}");
                if (r.ok) aiOk++;
            }
            Object.DestroyImmediate(go2);

            sb.Insert(0, $"로컬 후보 {total}개: 통과 {ok}, 거부 {rejected}, 예외 {exceptions}\nAI 샘플 3개 중 통과 {aiOk} (기대값 2)\n트리거 분포: {string.Join(", ", kinds.OrderByDescending(k => k.Value).Take(10).Select(k => k.Key + "=" + k.Value))}\n");
            Debug.Log("[Diverse] 파이프라인 검증\n" + sb);
            return sb.ToString();
        }

        [System.Serializable] class AiListProbe { public System.Collections.Generic.List<AbilityGraph> abilities; }

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
