using System;
using UnityEngine;

namespace Diverse
{
    [Serializable]
    public class InventoryShape
    {
        public TravelType type;
        public int width = 7, height = 7;
        public InventoryShape(TravelType t, int w, int h) { type = t; width = w; height = h; }
    }

    [Serializable]
    public class InventoryRules
    {
        public InventoryShape[] shapes = {
            new InventoryShape(TravelType.Dash, 7, 7), new InventoryShape(TravelType.Flight, 10, 5),
            new InventoryShape(TravelType.Burrow, 6, 8), new InventoryShape(TravelType.Swim, 8, 6),
            new InventoryShape(TravelType.Blink, 7, 7), new InventoryShape(TravelType.Bounce, 6, 8)
        };
        public int maxWeapons = 4;
        public int fusionBaseCost = 30, fusionCostPerTier = 25, maxPartTier = 5;
        public int skillCommissionCost = 75;
        public int legacyBaseTier = 1, legacyBossesPerTier = 2;
        public int weaponUnlockLevel = 5, weaponUnlockBosses = 1;
        public float relicMinDistance = 80;
        public int smithWeaponCost = 180;
        public int potionPrice = 18, xpPotionPrice = 35, antidotePrice = 12;
        public float potionHealFraction = 0.4f, xpPotionAmount = 50;
        public int consumableStack = 9;
        public int partIconPixels = 32, partIconColors = 16;
        public int partImageParallel = 3;

        public InventoryShape Shape(TravelType type) => Array.Find(shapes, s => s.type == type) ?? shapes[0];
        public int LegacyTier(WorldSave world) => Mathf.Min(maxPartTier, legacyBaseTier + world.bossKills / Mathf.Max(1, legacyBossesPerTier));
        public int FusionCost(int tier) => fusionBaseCost + fusionCostPerTier * tier;
    }
}
