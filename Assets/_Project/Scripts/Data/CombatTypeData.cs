using UnityEngine;

namespace Diverse
{
    /// <summary>A build type a zone favours (Data/CombatTypes/*.asset). See CombatTypeDef.</summary>
    [CreateAssetMenu(menuName = "Diverse/Combat Type", fileName = "CombatType")]
    public class CombatTypeData : ScriptableObject { public CombatTypeDef def = new CombatTypeDef(); }
}
