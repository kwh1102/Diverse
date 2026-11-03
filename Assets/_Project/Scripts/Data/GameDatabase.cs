using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Root data asset (Resources/GameDatabase.asset): lists every weapon, costume and enemy asset,
    /// plus the prefabs the code spawns at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Diverse/Game Database", fileName = "GameDatabase")]
    public class GameDatabase : ScriptableObject
    {
        public List<WeaponData> weapons = new List<WeaponData>();
        public List<CostumeData> costumes = new List<CostumeData>();
        public List<EnemyData> enemies = new List<EnemyData>();
        [Tooltip("XP curve, evolution frequency, skill unlock levels, warp stones.")]
        public ProgressionDef progression = new ProgressionDef();

        [Tooltip("Hit-stop limits: how hits feel (a short slow-down, never a hard freeze).")]
        public FeelDef feel = new FeelDef();

        [Header("Ability rules (Rule IR limits, power budget, tier unlocks)")]
        public AbilityRulesData abilityRules;

        [Header("Art rules: the palette and tile looks every picture follows")]
        public ArtStyleData artStyle;

        [Header("World: zones, themes, terrain, monster spawning")]
        public WorldRulesData worldRules;
        [Tooltip("The 8 build types; each names its default theme")]
        public List<CombatTypeData> combatTypes = new List<CombatTypeData>();
        [Tooltip("Every theme. The first one is the spawn's neutral land")]
        public List<ThemeData> themes = new List<ThemeData>();

        [Header("Prefabs")]
        public Player playerPrefab;
        public Enemy enemyPrefab;
        public Clone clonePrefab;
        public Summon summonPrefab;
        public Projectile projectilePrefab;
        public Pickup pickupPrefab;
        public Interactable interactablePrefab;

        public static GameDatabase Load()
        {
            var db = Resources.Load<GameDatabase>("GameDatabase");
            if (db == null) throw new System.Exception("Resources/GameDatabase.asset is missing. Run menu Diverse/Rebuild Project Assets.");
            return db;
        }
    }
}
