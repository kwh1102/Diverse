using UnityEngine;

namespace Diverse
{
    /// <summary>World layout, terrain and spawning values (Data/WorldRules.asset). See WorldRulesDef.</summary>
    [CreateAssetMenu(menuName = "Diverse/World Rules", fileName = "WorldRules")]
    public class WorldRulesData : ScriptableObject { public WorldRulesDef def = new WorldRulesDef(); }
}
