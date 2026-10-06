using UnityEngine;

namespace Diverse
{
    /// <summary>Inspector-editable enemy. Tier scaling and elite variants are applied on top at spawn time (DB.ScaledEnemy / DB.Elite).</summary>
    [CreateAssetMenu(menuName = "Diverse/Enemy", fileName = "Enemy")]
    public class EnemyData : ScriptableObject
    {
        public EnemyDef def = new EnemyDef();
    }
}
