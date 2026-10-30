using UnityEngine;

namespace Diverse
{
    /// <summary>Inspector-editable zone type (one of the 8 builds). Registered in GameDatabase.zones.</summary>
    [CreateAssetMenu(menuName = "Diverse/Zone", fileName = "Zone")]
    public class ZoneData : ScriptableObject
    {
        public ZoneDef def = new ZoneDef();
    }
}
