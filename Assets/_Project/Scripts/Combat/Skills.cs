using System;
using UnityEngine;

namespace Diverse
{
    [Serializable]
    public class SkillDef
    {
        public string id, name, desc, icon;
        public float cooldown, lockTime;
        public Color32 color;
        public ActiveSkillData active;
    }
    public class SkillState { public string id; public SkillDef def; public float cd; }
    public static class SkillDB
    {
        public static SkillDef Get(string id) => null; // Legacy IDs never choose one of the old 24 behaviours.
        public static SkillDef Get(InventoryItem item) => item?.active == null ? null : ActiveSkillGenerator.Definition(item.active);
        public static void Cast(Player player, SkillDef skill, Vector2 direction, Vector2 aim)
        { if (skill?.active != null) ActiveSkillRuntime.Cast(player, skill.active, direction, aim); }
    }
}
