using System;
using UnityEngine;

namespace Diverse
{
    /// <summary>The combat style a zone favors. Normal-mode worlds put 5 different ones around the spawn.</summary>
    public enum BuildStyle { MeleeBurst, MeleeSpeed, RangedBurst, RangedSpeed, Mobility, Tank, Evasion, Area }

    /// <summary>
    /// A zone mechanic put on its monsters. Each one is a problem a certain build answers well:
    /// Armor → big hits, HitShield → many hits, Deflect → melee, Thorns/DeathBurst → range, Swift/Skittish → move speed,
    /// Miasma/Relentless → toughness, Barrage/Heavy → dodging, Horde/Brood → area damage.
    /// </summary>
    public enum ZoneTrait { None, Armor, HitShield, Deflect, Thorns, DeathBurst, Swift, Skittish, Miasma, Relentless, Barrage, Heavy, Horde, Brood }

    /// <summary>One zone type (Data/Zones/*.asset). Theme, look, monsters and the mechanics that favor its build.</summary>
    [Serializable]
    public class ZoneDef
    {
        public BuildStyle style;
        [Tooltip("Zone (theme) name shown in the HUD and on the map.")]
        public string name;
        [Tooltip("Short adjective for mixed names at borders (\"용암\", \"서리\"…).")]
        public string prefix;
        [Tooltip("The build this zone favors (shown to the player).")]
        public string buildName;
        [TextArea] public string desc;
        public Biome biome;
        [Tooltip("Map / boss tint.")]
        public Color32 color = new Color32(255, 255, 255, 255);
        [Tooltip("Regular monsters (EnemyData ids).")]
        public string[] enemies;
        [Tooltip("Pure zone boss (EnemyData id). Its deep-layer version is the same boss, much stronger.")]
        public string boss;
        [Tooltip("Unlocks first (closer to the spawn).")]
        public ZoneTrait primary;
        [Tooltip("Unlocks farther out.")]
        public ZoneTrait secondary;
        [Tooltip("Name of the deep layer reached through this zone's gate.")]
        public string deepName;
        [Tooltip("Look of the zone: ground, scenery, monsters, effects (drawn by Diverse/구역 아트 생성 into Resources/Sprites).")]
        public ZoneArtDef art = new ZoneArtDef();
    }

    /// <summary>
    /// The zone's own look. The prompts go to the image model once (editor tool ZoneArtBuilder); the pixelized results are
    /// saved as Resources/Sprites PNGs under fixed keys, so the game just loads them (and they can be repainted by hand).
    /// </summary>
    [Serializable]
    public class ZoneArtDef
    {
        [Tooltip("English, one line: what this land is made of. Added to every picture so they share one theme.")]
        public string theme;
        [Tooltip("Main ground of the zone and the patches mixed into it.")]
        public Ground ground = Ground.Grass, groundAccent = Ground.DarkGrass;
        [Tooltip("English ground textures: main A, main B, accent A, accent B → ground_<Ground>_<0|1>.png")]
        public string[] tilePrompts = new string[4];
        [Tooltip("English scenery: 4 trees (large), 3 rocks, 2 bushes, 2 tiny ground details → tree_/rock_/bush_/tuft_<Biome>_<n>.png")]
        public string[] decorPrompts = new string[11];
        [Tooltip("English, one per regular monster (same order as enemies) → enemy_<sprite>_<frame>.png")]
        public string[] monsterPrompts;
        [Tooltip("English: the zone's master.")]
        public string bossPrompt;
        [Tooltip("English effects: enemy shot, hit on the hero, death blast / hazard, drifting ambient mote → zfx_<style>_shot|hit|burst|mote.png")]
        public string[] effectPrompts = new string[4];
        [Tooltip("Ambient motes: how they drift (snow, embers, spores, dust, sparkles, leaves, pollen, mist).")]
        public string ambient = "dust";

        public static string FxKey(BuildStyle s, string kind) => $"zfx_{s}_{kind}";
    }

    /// <summary>Numbers for the five-zone layout and zone mechanics (GameDatabase → zoneRules).</summary>
    [Serializable]
    public class ZoneRulesDef
    {
        [Header("Layout")]
        [Tooltip("Neutral meadow around the spawn (world units).")]
        public float centerRadius = 42;
        [Tooltip("Zones blend over this many world units on each side of a border.")]
        public float blendWidth = 38;
        [Tooltip("How far borders wander from straight lines (world units).")]
        public float borderWiggle = 26;

        [Header("Bosses")]
        [Tooltip("Pure zone boss: distance from the spawn (min, max).")]
        public Vector2 zoneBossDistance = new Vector2(220, 300);
        [Tooltip("Pure zone bosses stay at least this many degrees away from their zone's borders.")]
        public float zoneBossBorderMargin = 18;
        [Tooltip("Border mid-boss: distance from the spawn (min, max).")]
        public Vector2 borderBossDistance = new Vector2(110, 160);
        [Tooltip("Border mid-boss: XP / gold multiplier (it should feel like a big step of growth).")]
        public float borderBossReward = 3f;

        [Header("Difficulty (grows with distance)")]
        [Tooltip("The first zone mechanic starts at this distance and is at full strength at the second number.")]
        public Vector2 primaryTraitRange = new Vector2(55, 230);
        [Tooltip("The second zone mechanic starts / is full.")]
        public Vector2 secondaryTraitRange = new Vector2(150, 360);
        [Tooltip("Extra HP per tier in zone worlds (on top of the normal tier scaling).")]
        public float hpPerTier = 0.12f;
        [Tooltip("Extra damage per tier in zone worlds.")]
        public float damagePerTier = 0.06f;
        [Tooltip("Deep layer: tier added everywhere.")]
        public float deepTierBonus = 5f;
        [Tooltip("Deep layer boss: distance from the spawn (min, max).")]
        public Vector2 deepBossDistance = new Vector2(150, 200);

        [Header("Mechanic strength (at full strength)")]
        [Tooltip("Armor: each hit loses this share of the monster's max HP (never below 25% of the hit).")]
        public float armor = 0.05f;
        [Tooltip("Hit shield: hits fully blocked; refills after a pause.")]
        public int shieldHits = 4;
        public float shieldRefill = 3f;
        [Tooltip("Deflect: chance to block a hit from farther than 3 units while not attacking.")]
        public float deflect = 0.55f;
        [Tooltip("Thorns: a melee hit returns this × the monster's damage.")]
        public float thorns = 0.45f;
        [Tooltip("Death burst: explosion after a short telegraph, × the monster's damage.")]
        public float deathBurst = 1.2f;
        public float deathBurstRadius = 1.9f;
        [Tooltip("Swift: move speed multiplier.")]
        public float swift = 1.45f;
        [Tooltip("Skittish: leaps away when the hero is close (seconds between leaps).")]
        public float skittishCooldown = 2.2f;
        [Tooltip("Miasma: damage per second near the monster, × its damage. Cannot be dodged.")]
        public float miasma = 0.3f;
        public float miasmaRadius = 2.3f;
        [Tooltip("Relentless: attack cooldown multiplier.")]
        public float relentless = 0.6f;
        [Tooltip("Barrage: seconds between projectile rings.")]
        public float barrageInterval = 3.2f;
        public int barrageShots = 8;
        [Tooltip("Heavy: damage multiplier (attacks also wind up a bit longer).")]
        public float heavy = 1.5f;
        [Tooltip("Horde: group size multiplier; each member has less HP.")]
        public float horde = 2f;
        public float hordeHp = 0.6f;
        [Tooltip("Brood: small copies left behind on death.")]
        public int brood = 2;
    }
}
