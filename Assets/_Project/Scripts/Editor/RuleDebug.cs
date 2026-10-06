using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Batch: -executeMethod Diverse.EditorTools.RuleDebug.DumpBatch -ruleCase "바람 걸음"
    /// Prints, for one expressiveness case on each archetype build, the compiled IR, every validator issue, the power
    /// breakdown per ability and the simulation report → Logs/RuleDebug.txt. For tuning the power model, not for players.
    /// </summary>
    public static class RuleDebug
    {
        public static void DumpBatch()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-ruleCase");
            string want = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            var sb = new StringBuilder();
            foreach (var c in RuleExpressiveness.All().Where(c => want == null || c.name.Contains(want)))
            {
                sb.AppendLine($"══ {c.name} ({c.pattern})");
                foreach (var wk in new[] { WeaponKind.SwordShield, WeaponKind.Staff, WeaponKind.Dagger })
                {
                    var go = new GameObject("Dbg");
                    var p = go.AddComponent<Player>();
                    p.Weapon = DB.W(wk); p.Costume = DB.Costumes[0]; p.Abilities = new AbilityRuntime(p); p.Level = 14;
                    try
                    {
                        foreach (var s in new[] { RuleSamples.FireSource(), RuleSamples.FireSource2(), RuleSamples.DashSource() })
                            if (RuleValidator.Validate(s, BuildContext.Of(p)).ok) { s.id = "s" + p.Abilities.Owned.Count; p.Abilities.Restore(s); }
                        var a = AbilityCompiler.Compile(c.intent).ability;
                        var b = BuildContext.Of(p);
                        var r = RuleValidator.Validate(a, b, true, true);
                        sb.AppendLine($"  [{wk}] {(r.ok ? "OK" : "FAIL " + r.failedStage)} Δ{r.delta:0} budget {r.budget:0} tier {a.tier}");
                        foreach (var iss in r.issues) sb.AppendLine("    " + iss);
                        sb.AppendLine("    IR: " + JsonUtility.ToJson(a));
                        sb.AppendLine("    " + a.Explain().Replace("\n", "\n    "));
                        var rep = PowerModel.Evaluate(b.owned.Concat(new[] { a }).ToList(), b);
                        sb.AppendLine("    power: " + rep.Vector() + $" total {rep.Total:0}");
                        foreach (var kv in rep.byAbility) sb.AppendLine($"      {kv.Key.name ?? "?"}: {kv.Value:0.0}");
                        foreach (var kv in rep.freq) sb.AppendLine($"      freq {kv.Key.trigger}: {kv.Value:0.000}/s");
                        if (r.sim != null) sb.AppendLine("    " + r.sim);
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            }
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/RuleDebug.txt", sb.ToString());
            EditorApplication.Exit(0);
        }
    }
}
