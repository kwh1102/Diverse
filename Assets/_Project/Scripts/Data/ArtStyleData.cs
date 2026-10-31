using UnityEngine;

namespace Diverse
{
    /// <summary>The game's art rules (Data/ArtStyle.asset). See ArtStyleDef.</summary>
    [CreateAssetMenu(menuName = "Diverse/Art Style", fileName = "ArtStyle")]
    public class ArtStyleData : ScriptableObject { public ArtStyleDef def = new ArtStyleDef(); }
}
