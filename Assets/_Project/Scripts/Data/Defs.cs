using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum WeaponKind { Greatsword, SwordShield, Crossbow, Staff, Dagger, Katana }
    public enum Element { None, Fire, Frost, Lightning, Shadow, Holy, Poison, Wind, Blood, Void, Arcane }

    /// <summary>
    /// One step of a basic-attack combo. Hit feel is decided almost entirely by these numbers.
    /// </summary>
    [Serializable]
    public class ComboStep
    {
        public float windup = 0.06f;     // delay before the hit lands
        public float recovery = 0.18f;   // time until the next input is accepted
        public float range = 1.6f;       // reach (world units)
        public float arc = 140f;         // slash angle (degrees)
        public float damage = 1f;        // attack multiplier
        public float lunge = 0.25f;      // distance the player steps forward on attack
        public float knockback = 3f;     // distance enemies get pushed
        public float hitstop = 0.05f;    // freeze-frame duration on hit
        public float shake = 0.15f;      // screen shake (0~1)
        public float kick = 2f;          // screen kick (pixels)
        public bool thrust;              // thrust (straight line) attack
        public bool spin;                // 360-degree attack
        public bool projectile;          // fires a projectile
        public int projectileCount = 1;
        public float projectileSpread;
        public float recoil;             // knockback the player takes from firing
        public float swingDir = 1;       // slash direction (1 = top to bottom, -1 = reverse)
        public float fxScale = 1f;
    }

    [Serializable]
    public class WeaponDef
    {
        public WeaponKind kind;
        public string name;
        [TextArea] public string desc;
        public string tags;              // ability language tags ("SWORD HEAVY MELEE")
        public Color32 slashColor;
        public Color32 slashCore;
        public float baseDamage = 10;
        public float comboReset = 0.7f;  // combo resets after this long without attacking
        public float moveSpeedMul = 1f;
        public float attackRange = 1.6f; // range at which right-click auto-attack starts
        public ComboStep[] combo;
        public string[] skills = new string[4];  // QWER skill ids
        public Element element;
        public string sfxSwing = "swing";
        public string sfxHit = "hit";

        /// <summary>Per-run copy (the smith upgrades baseDamage on it). Combo/skill arrays stay shared with the asset.</summary>
        public WeaponDef Clone() => (WeaponDef)MemberwiseClone();
    }

    /// <summary>One costume perk line: stat += value (Add) or stat *= 1 + value (Mul).</summary>
    [Serializable]
    public struct StatMod
    {
        public StatId stat;
        public bool mul;
        public float value;

        public StatMod(StatId stat, float value, bool mul) { this.stat = stat; this.value = value; this.mul = mul; }
    }

    [Serializable]
    public class CostumeDef
    {
        public string id, name, perk;
        public Color32 fur, furShade, outfit, outfitShade, accent;
        public int style;
        public StatMod[] mods;
        public string tags;              // costume-specific tags (fed into the AI context)

        public void Apply(Stats s)
        {
            if (mods == null) return;
            foreach (var m in mods)
                if (m.mul) s.Mul(m.stat, m.value); else s.Add(m.stat, m.value);
        }
    }

    public enum EnemyBrain { Melee, Hopper, Ranged, Charger, Caster, Swarm, Boss }

    /// <summary>
    /// The signature attack an enemy uses (on top of its brain's movement). Each one has its own telegraph shape and dodge window.
    /// </summary>
    public enum EnemyAttack
    {
        Default,     // whatever the brain does (slash / hop / shot / charge)
        Lunge,       // fox: short dash that ends in a slash; a second quick slash follows
        Tongue,      // frog-like: a long thin line telegraph, then a fast tongue lash that pulls the player in
        Burrow,      // shroom: sinks, pops up under the player after a ring telegraph
        Volley,      // raccoon: 3 aimed shots in a quick burst, the last one leading the player
        Orbit,       // wisp: shots that curve around and converge on the player's position
        IceLine,     // frostling: a line of frost spikes travelling toward the player
        Gore,        // boar: charges, and if it hits a wall it is stunned briefly (punishable)
        Dive,        // bat: hovers, then swoops in a straight line through the player
    }

    [Serializable]
    public class EnemyDef
    {
        public string id, name;
        public EnemyBrain brain;
        [Tooltip("Signature attack (see EnemyAttack). Default = the brain's basic attack.")]
        public EnemyAttack attack;
        public float hp = 30, damage = 8, speed = 2.2f, radius = 0.45f;
        public float attackRange = 1.1f, attackWindup = 0.45f, attackCooldown = 1.4f;
        public float aggroRange = 7f, xp = 6, gold = 2;
        public float mass = 1f;
        [Header("Movement")]
        [Tooltip("Hopper movement: seconds between hops while chasing (0 = walk normally).")]
        public float hopInterval;
        [Tooltip("Strafe sideways while in range instead of standing still (0 = never, 1 = always).")]
        public float strafe;
        [Header("Signature attack")]
        [Tooltip("Lunge/Gore/Dive travel speed, Tongue length, IceLine spike count… (meaning depends on the attack).")]
        public float attackParam = 1f;
        [Tooltip("Number of hits/projectiles in the pattern (Volley shots, Lunge follow-ups, Orbit shots).")]
        public int attackCount = 1;
        public string sprite;
        public Color32 projectileColor = new Color32(0xff, 0x5a, 0x6e, 0xff);   // Pal.Blood
        public Element element;
        public bool boss;
        [NonSerialized] public bool elite;   // outer-ring variant (tinted, bigger); set at spawn time only
        public string[] lines;           // lines the boss speaks
    }

    /// <summary>
    /// How a hero grows: XP curve, how often evolutions come, when skills unlock.
    /// Lives in Resources/GameDatabase.asset so it can be tuned in the Inspector.
    /// </summary>
    [Serializable]
    public class ProgressionDef
    {
        [Tooltip("XP needed for level 2.")]
        public float xpBase = 60;
        [Tooltip("Each level needs this much more than the last (×).")]
        public float xpGrowth = 1.32f;
        [Tooltip("An ability evolution is offered every N levels (1 = every level).")]
        public int evolveEvery = 2;
        [Tooltip("Levels at which the next QWER skill is learned (in order). Skills can also be learned from the smith.")]
        public int[] skillLevels = { 3, 6, 10, 15 };
        [Tooltip("Smith: weapon level at which each QWER skill can be learned (in order).")]
        public int[] smithSkillLevels = { 1, 2, 4, 6 };
        [Tooltip("Warp stone price at the merchant.")]
        public int warpStoneCost = 120;
        [Tooltip("How close (world units) the hero must be to a warp stone to travel from the map.")]
        public float warpRange = 6f;

        public float XpFor(int level) => Mathf.Round(xpBase * Mathf.Pow(xpGrowth, level - 1));
        public bool EvolvesAt(int level) => evolveEvery <= 1 || level % evolveEvery == 0;
    }

    /// <summary>
    /// Game data tables, read from Resources/GameDatabase.asset.
    /// To tweak numbers, edit the assets under Assets/_Project/Data in the Inspector.
    /// </summary>
    public static class DB
    {
        public static readonly Dictionary<WeaponKind, WeaponDef> Weapons = new Dictionary<WeaponKind, WeaponDef>();
        public static readonly List<CostumeDef> Costumes = new List<CostumeDef>();
        public static readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();
        public static GameDatabase Asset { get; private set; }

        static DB()
        {
            Asset = GameDatabase.Load();
            foreach (var w in Asset.weapons) if (w != null) Weapons[w.def.kind] = w.def;
            foreach (var c in Asset.costumes) if (c != null) Costumes.Add(c.def);
            foreach (var e in Asset.enemies) if (e != null) Enemies[e.def.id] = e.def;
        }

        public static ProgressionDef Progression => Asset.progression ?? (Asset.progression = new ProgressionDef());
        public static WeaponDef W(WeaponKind k) => Weapons[k];
        public static CostumeDef Costume(string id) => Costumes.Find(c => c.id == id) ?? Costumes[0];
        public static EnemyDef Enemy(string id) => Enemies.TryGetValue(id, out var e) ? e : Enemies["shroom"];

        public static EnemyDef ScaledEnemy(string id, float tier)
        {
            var b = Enemy(id);
            float hpMul = 1f + tier * 0.35f, dmgMul = 1f + tier * 0.18f;
            return new EnemyDef
            {
                id = b.id, name = b.name, brain = b.brain, attack = b.attack, hp = b.hp * hpMul, damage = b.damage * dmgMul, speed = b.speed * (1 + Mathf.Min(tier, 10) * 0.02f), radius = b.radius,
                attackRange = b.attackRange, attackWindup = b.attackWindup, attackCooldown = b.attackCooldown, aggroRange = b.aggroRange,
                xp = b.xp * (1 + tier * 0.25f), gold = b.gold * (1 + tier * 0.25f), mass = b.mass, sprite = b.sprite, projectileColor = b.projectileColor,
                hopInterval = b.hopInterval, strafe = b.strafe, attackParam = b.attackParam, attackCount = b.attackCount,
                element = b.element, boss = b.boss, lines = b.lines,
            };
        }

        /// <summary>A stronger variant that shows up in the outer rings: tougher, faster to attack, better loot, shown with a tint.</summary>
        public static EnemyDef Elite(EnemyDef d)
        {
            if (d.boss) return d;
            d.elite = true;
            d.name = "정예 " + d.name;
            d.hp *= 2.2f; d.damage *= 1.35f;
            d.attackCooldown *= 0.8f; d.speed *= 1.1f;
            d.radius *= 1.15f; d.mass *= 1.6f;
            d.xp *= 2.5f; d.gold *= 2.5f;
            return d;
        }
    }
}
