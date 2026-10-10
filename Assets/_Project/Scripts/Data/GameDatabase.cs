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
