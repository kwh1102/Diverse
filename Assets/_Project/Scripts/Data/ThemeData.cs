using UnityEngine;

namespace Diverse
{
    /// <summary>A zone look (Data/Themes/*.asset). See ThemeDef.</summary>
    [CreateAssetMenu(menuName = "Diverse/Theme", fileName = "Theme")]
    public class ThemeData : ScriptableObject { public ThemeDef def = new ThemeDef(); }
}
