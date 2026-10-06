using UnityEngine;

namespace Diverse
{
    /// <summary>Inspector-editable costume (colors + stat perks).</summary>
    [CreateAssetMenu(menuName = "Diverse/Costume", fileName = "Costume")]
    public class CostumeData : ScriptableObject
    {
        public CostumeDef def = new CostumeDef();
    }
}
