using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Inspector-editable tuning for the Rule Language (Data/AbilityRules.asset, referenced from GameDatabase).
    /// World invariants (hard limits), the power budget and the complexity-tier unlocks live here, not in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Diverse/Ability Rules", fileName = "AbilityRules")]
    public class AbilityRulesData : ScriptableObject
    {
        public AbilityRulesDef def = new AbilityRulesDef();
    }

    [Serializable]
    public class AbilityRulesDef
    {
        [Header("World invariants — hard limits every ability must respect")]
        public int maxRulesPerAbility = 4;
        public int maxStatesPerAbility = 3;
        public int maxOpsPerRule = 3;
        public int maxConditionsPerRule = 3;
        [Tooltip("Ability → event → ability chains stop at this depth")]
        public int maxProcDepth = 3;
        [Tooltip("One selector never returns more targets than this")]
        public int maxTargetsPerProc = 6;
        [Tooltip("Live spawned entities per owner")]
        public int maxSpawnPerOwner = 8;
        [Tooltip("Rule executions per frame across all abilities (proc budget)")]
        public int maxProcsPerFrame = 40;
        [Tooltip("Upper bound of any ability state value")]
        public float maxStateValue = 999;
        [Tooltip("Every trigger rule waits at least this long between fires")]
        public float minInternalCooldown = 0.12f;
        [Tooltip("Cooldown the repairer adds to break an unbounded trigger cycle")]
        public float cycleRepairCooldown = 1f;
        [Tooltip("A feedback cycle whose expected gain per loop is below this converges and is allowed")]
        public float maxCycleGain = 0.8f;
        [Tooltip("Spawn ops: expected live entities (rate × lifetime) above this are rejected")]
        public float maxExpectedLiveSpawns = 6f;
        [Tooltip("Longest spawned entity lifetime (s)")]
        public float maxSpawnDuration = 8f;
        [Tooltip("Max ticks for Periodic temporals / uses for ForNextN / jumps for Chain-like relations")]
        public int maxTemporalTicks = 6;
        [Tooltip("Max seconds a deferred temporal (UntilHit/UntilDamaged/UntilNextAttack) waits before it expires")]
        public float maxArmedSeconds = 8f;
        [Tooltip("Cascade relation: max re-fires from corpses per execution")]
        public int maxCascade = 3;
        [Tooltip("Meta selectors affect at most this many abilities")]
        public int maxMetaTargets = 5;

        [Header("Power budget (points; 100 ≈ one extra base-attack DPS)")]
        public float budgetBase = 32;
        public float budgetPerTier = 7;
        [Tooltip("Accepted window around the budget after the balancer has scaled the numbers")]
        public float acceptMin = 0.45f, acceptMax = 1.4f;
        [Tooltip("Extra budget per complexity tier above 1 (weird abilities are allowed to be a bit stronger)")]
        public float complexityBonus = 0.08f;

        [Header("Complexity tiers: 0 numeric · 1 reactive · 2 stateful · 3 meta · 4 system")]
        [Tooltip("Player level needed to be offered each tier")]
        public int[] tierUnlockLevel = { 1, 1, 3, 6, 9 };

        [Header("Risk (0..1 per axis; above the cap the ability is rejected, above warn it needs a higher tier)")]
        public float riskCap = 0.85f;
        public float riskWarn = 0.55f;

        [Header("Simulation check (headless, deterministic)")]
        [Tooltip("Seconds of simulated combat per scenario")]
        public float simSeconds = 60;
        [Tooltip("Reject when simulated procs/s exceed this")]
        public float simMaxProcsPerSecond = 25;
        [Tooltip("Reject when the simulated power differs from the static estimate by more than this factor")]
        public float simMaxDivergence = 3f;

        [Header("Primitive tuning (0 = code default). Rebuilt from code with Diverse/빠진 에셋 채우기 when empty")]
        public List<PrimTuning> prims = new List<PrimTuning>();

        [Header("Build-aware estimation")]
        [Tooltip("Events observed before telemetry fully replaces the generic trigger frequency")]
        public float telemetryFullTrust = 25;
        [Tooltip("Seconds of play before observed frequencies are used at all")]
        public float telemetryMinSeconds = 30;

        public int MaxTierAt(int level)
        {
            int t = 0;
            for (int i = 0; i < tierUnlockLevel.Length; i++) if (level >= tierUnlockLevel[i]) t = i;
            return t;
        }

        public float Budget(int level, int tier) =>
            (budgetBase + budgetPerTier * (level / 3)) * (1 + complexityBonus * Mathf.Max(0, tier - 1));
    }
}
