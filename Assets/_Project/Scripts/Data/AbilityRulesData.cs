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
        public float partShieldDuration = 2, partMaxShield = .75f, partMaxHeal = .15f;
        public float telemetryFullTrust = 25, telemetryMinSeconds = 30;
        public PartDef[] parts = Array.Empty<PartDef>();
        public WeaponFootprint[] weaponShapes = Array.Empty<WeaponFootprint>();
    }
}
