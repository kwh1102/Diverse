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
        public float skillMainSeconds = .42f, skillAfterSeconds = .28f, skillImpactPixels = 43;
        public float skillProjectilePixels = 32, skillProjectileMinThickness = 6, skillProjectileMaxPixels = 46;
        public float skillMuzzlePixels = 36, skillTrailPixels = 20, skillTrailStartDistance = .3f, skillTrailInterval = .055f;
        public float skillActionScale = 1.17f, skillUltimateScale = 1.23f, skillGroundPulseScale = .94f;
        public float skillAccentAlpha = .64f, skillGroundAccentAlpha = .5f;
        [Header("Combat cues")]
        public Sprite[] cutHit;
        public Sprite[] bluntHit, daggerHit, gunFlash, crystalHit;
        public float basicSweepScale = 1.18f, basicStrokeWeight = 1.5f, basicSweepSeconds = .24f;
        public float weaponContactScale = 1.48f, basicProjectileScale = 1.24f, basicMuzzlePixels = 29;
        [Header("Projectile ribbons (native pixel width)")]
        public float arrowWakePixels = 1.7f, arrowWakeSeconds = .16f;
        public float bulletWakePixels = 2.35f, bulletWakeSeconds = .11f;
        public float magicWakePixels = 4.6f, magicWakeSeconds = .24f;
        public float bladeWakePixels = 3.2f, bladeWakeSeconds = .17f;
        public float muzzleSeconds = .16f, cutSeconds = .17f, cutScale = .92f, basicSweepRadiusPixels = 29;
        public float placementWindup = .13f, placementSquash = .95f, placementHandDrop = .14f;
    }
}
