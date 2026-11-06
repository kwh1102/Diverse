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
        [Header("Manual skill motion families")]
        public Sprite[] skillSlash, skillThrust, skillMuzzle, skillImpact, skillShock;
        public Sprite[] skillFire, skillIce, skillLightning, skillEarth, skillArcane, skillWind, skillPoison, skillHoly;
        public Sprite[] skillPull, skillGuard, skillHeal, skillField, skillTrap, skillDash;
        public Sprite[] skillArrow, skillBullet, skillOrb, skillBlade, skillSpear, skillTrail;
        public float skillMainSeconds = .38f, skillAfterSeconds = .24f, skillImpactPixels = 30;
        public float skillProjectilePixels = 26, skillProjectileMinThickness = 5, skillProjectileMaxPixels = 40;
        public float skillMuzzlePixels = 28, skillTrailPixels = 20, skillTrailStartDistance = .3f, skillTrailInterval = .055f;
        public float skillUltimateScale = 1.15f;
    }
}
