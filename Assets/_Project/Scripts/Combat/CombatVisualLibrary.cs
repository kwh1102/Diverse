using UnityEngine;

namespace Diverse
{
    [CreateAssetMenu(menuName="Diverse/Combat Visual Library")]
    public class CombatVisualLibrary : ScriptableObject
    {
        public Sprite[] slash, contact, fire, earth, cut, thrust, ring, ice, arcane, shards, trail, bolt, barriers;
        public float framesPerSecond = 30, contactScale = .55f, accentScale = .8f, slashReferenceRadius = 24;
        public float trailStartDistance = 1.25f;
        public float[] barrierReferenceLengths = {64,52,68,68};
    }
}
