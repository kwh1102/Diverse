using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>Compatibility entry point: measure the current world renderer, not the retired ground baker.</summary>
    public static class DesignPerf
    {
        public static string Run()
        {
            string report = WorldVisualChecks.RunPerformance(); Debug.Log(report); return report;
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
