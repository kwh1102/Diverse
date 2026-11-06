using System;
using UnityEngine;

namespace Diverse
{
    [CreateAssetMenu(menuName = "Diverse/Skills and Fixed Parts", fileName = "AbilityRules")]
    public class AbilityRulesData : ScriptableObject
    {
        public AbilityRulesDef def = new AbilityRulesDef();
    }

    [Serializable]
    public class AbilityRulesDef
    {
        public ActiveRules active = new ActiveRules();
        public float partRankBonus = .12f;
        public float[] partTierPower = { 1, 1.8f, 3, 4.5f, 6.4f };
        public float maxRangeBonus = .5f, maxControlBonus = .5f;
        public float partShieldDuration = 2, partMaxShield = .75f, partMaxHeal = .15f;
        public float telemetryFullTrust = 25, telemetryMinSeconds = 30;
        public PartDef[] parts = Array.Empty<PartDef>();
        public PartEvolutionDef[] evolutions = Array.Empty<PartEvolutionDef>();
        public float evolutionComboWindow = 6, evolutionAimDistance = 2;
        public float evolutionDamageCap = .35f, evolutionRangeCap = .2f, evolutionControlCap = .3f, evolutionCooldownCap = .2f;
        public WeaponFootprint[] weaponShapes = Array.Empty<WeaponFootprint>();
    }
}
