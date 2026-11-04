using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public partial class Player
    {
        public InventoryState Inventory { get; private set; }
        readonly Dictionary<string, SkillState> inventorySkills = new Dictionary<string, SkillState>();
        readonly HashSet<AbilityDef> equippedParts = new HashSet<AbilityDef>();
        string shownWeapon;

        public void InitializeInventory()
        {
            Inventory = InventoryState.Create(MovementType);
            var pack = DesignStore.PackOfCostume(Costume.id);
            Inventory.Receive(new InventoryItem { kind = ItemKind.Weapon, weapon = Weapon, name = Weapon.name, design = pack?.Clone(), startingWeapon = true });
            Inventory.Changed = SyncInventory;
            SyncInventory();
        }

        public InventoryState InventorySnapshot()
        {
            StoreSkillCooldowns();
            var copy = JsonUtility.FromJson<InventoryState>(JsonUtility.ToJson(Inventory));
            foreach (var item in copy.items)
            {
                var live = Inventory.Find(item.id);
                item.cooldownRemaining = Mathf.Max(0, live.cooldownUntil - Time.time);
            }
            return copy;
        }

        public void RestoreInventory(InventoryState saved)
        {
            if (saved == null) return;
            Inventory.Changed = null;
            Inventory = saved;
            Inventory.items ??= new List<InventoryItem>();
            foreach (var i in Inventory.items)
            {
                i.cooldownUntil = Time.time + i.cooldownRemaining;
                if (i.kind == ItemKind.Weapon && i.design != null)
                {
                    // Inventory JSON bypasses DesignStore.Load/DesignPack.Clone: normalize optional arrays here too.
                    DesignGuard.Sanitize(i.design);
                    i.weapon.origin = DesignCombat.For(i.design);
                }
            }
            Inventory.Changed = SyncInventory;
            shownWeapon = null; inventorySkills.Clear(); equippedParts.Clear();
            SyncInventory();
        }

        void StoreSkillCooldowns()
        {
            if (Inventory == null) return;
            foreach (var kv in inventorySkills)
            {
                var item = Inventory.Find(kv.Key);
                if (item != null && System.Array.IndexOf(Skills, kv.Value) >= 0) item.cooldownUntil = Time.time + Mathf.Max(0, kv.Value.cd);
            }
        }

        public void SyncInventory()
        {
            if (Inventory?.Active == null) return;
            StoreSkillCooldowns();
            var w = Inventory.Active;
            bool changedWeapon = shownWeapon != w.id;
            Weapon = w.weapon;
            if (changedWeapon)
            {
                shownWeapon = w.id;
                StopMoving();
                if (basicAttackRoutine != null) { StopCoroutine(basicAttackRoutine); basicAttackRoutine = null; }
                attackStrikePending = false; queuedAttack = false; comboIndex = 0; comboTimer = 0;
                weaponSwingT = weaponSwingDur = 0; lungeT = 0;
                // A skill still finishing cannot be bypassed by selecting a different weapon.
                DesignRuntime.EquipWeapon(w.design, Weapon);
                RefreshLook();
            }
            var attached = Inventory.Attached(w).ToList();
            for (int slot=0;slot<Skills.Length;slot++)
            {
                var item = attached.FirstOrDefault(i => i.kind == ItemKind.Skill && i.skillSlot == slot);
                if (item == null)
                {
                    Skills[slot] = new SkillState { id = Weapon.skills[slot] };
                    continue;
                }
                if (!inventorySkills.TryGetValue(item.id, out var s))
                {
                    var d = SkillDB.Get(item.skillId);
                    inventorySkills[item.id] = s = new SkillState { id = item.skillId, def = d == null ? null : new SkillDef {
                        id = d.id, name = item.name, desc = d.desc, cooldown = d.cooldown, lockTime = d.lockTime, color = d.color, icon = d.icon } };
                }
                s.cd = Mathf.Max(0, item.cooldownUntil - Time.time);
                Skills[slot] = s;
            }
            var wanted = new HashSet<AbilityDef>(attached.Where(i => i.kind == ItemKind.Part && i.Ability != null).Select(i => i.Ability));
            foreach (var a in equippedParts.Where(a => !wanted.Contains(a)).ToList()) { Abilities.Remove(a,preserveState:true); equippedParts.Remove(a); }
            foreach (var item in attached.Where(i => i.kind == ItemKind.Part && i.Ability != null))
            {
                var a = item.Ability;
                a.name=item.name;
                if (equippedParts.Contains(a)) continue;
                Abilities.Restore(a); equippedParts.Add(a);
                if (!item.acquisitionApplied) { item.acquisitionApplied = true; Abilities.AcquireOnce(a); }
            }
            Abilities.RefreshEquipmentStats();
        }

        public int AcquiredSkills => Inventory == null ? LearnedSkills : Enumerable.Range(0,4).Count(slot =>
            (Inventory.Active.learnedSkillMask & (1 << slot)) != 0);

        SkillState AcquireNextSkill()
        {
            var w = Inventory.Active;
            for (int slot=0;slot<4;slot++)
            {
                if ((w.learnedSkillMask & (1 << slot)) != 0) continue;
                var d = SkillDB.Get(Weapon.skills[slot]);
                if (d == null) continue;
                var item = new InventoryItem { kind = ItemKind.Skill, skillId = d.id, skillSlot = slot, name = d.name, sourceWeapon = w.id };
                w.learnedSkillMask |= 1 << slot;
                // A skill remains an item even when its original weapon is discarded.
                Inventory.Receive(item, equip: true);
                GameEvents.Notify($"[{item.name}] {"QWER"[slot]} 스킬 아이템 획득 — 해당 방향에 놓으면 장착돼.");
                return new SkillState { id = d.id, def = d };
            }
            return null;
        }

        public void MigrateAbilities(IEnumerable<AbilityRecord> records)
        {
            foreach (var rec in records)
            {
                var a = rec.ToAbility();
                if (a != null)
                {
                    var item=InventoryItem.Part(a); item.acquisitionApplied=true;
                    Inventory.Receive(item,equip:true);
                }
            }
        }

        public bool UseItem(string id)
        {
            var item = Inventory.Find(id);
            if (item?.kind != ItemKind.Consumable || !item.Placed || item.count <= 0) return false;
            switch (item.consumable)
            {
                case "heal": if (hp >= maxHp) return false; Heal(maxHp * InventoryState.Rules.potionHealFraction); break;
                case "xp": GainXp(InventoryState.Rules.xpPotionAmount); break;
                case "cleanse": if (statuses.Count == 0) return false; statuses.Clear(); break;
                default: return false;
            }
            item.count--; if (item.count <= 0) Inventory.items.Remove(item);
            Inventory.Notify(); Sfx.Play("levelup", .4f); return true;
        }
    }
}
