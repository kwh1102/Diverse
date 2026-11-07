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
    public class SkillState
    {
        public string id;
        public SkillDef def;
        public float cd;
        public int spentCharges;
        public int Capacity => ActiveSkillGenerator.UsesTrapCharges(def?.active) ? ActiveSkillGenerator.Rules.trapCharges : 0;
        public bool Ready => Capacity > 0 ? spentCharges < Capacity : cd <= 0;

        public void Spend(float seconds)
        {
            if (Capacity == 0) { cd = seconds; return; }
            if (spentCharges++ == 0) cd = seconds;
        }

        public void Tick(float dt, float recharge)
        {
            if (Capacity == 0) { cd = Mathf.Max(0, cd - dt); return; }
            cd -= dt;
            while (spentCharges > 0 && cd <= 0)
            {
                spentCharges--;
                cd += Mathf.Max(.01f, recharge);
            }
            if (spentCharges == 0) cd = 0;
        }
    }
    public static class SkillDB
    {
        public static SkillDef Get(string id) => null; // Legacy IDs never choose one of the old 24 behaviours.
        public static SkillDef Get(InventoryItem item) => item?.active == null ? null : ActiveSkillGenerator.Definition(item.active);
        public static void Cast(Player player, SkillDef skill, Vector2 direction, Vector2 aim)
        { if (skill?.active != null) ActiveSkillRuntime.Cast(player, skill.active, direction, aim); }
    }
}
