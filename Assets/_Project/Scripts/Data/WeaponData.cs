using UnityEngine;

namespace Diverse
{
    /// <summary>Inspector-editable weapon (combo hit-feel numbers live in def.combo).</summary>
    [CreateAssetMenu(menuName = "Diverse/Weapon", fileName = "Weapon")]
    public class WeaponData : ScriptableObject
    {
        public WeaponDef def = new WeaponDef();
    }
}
