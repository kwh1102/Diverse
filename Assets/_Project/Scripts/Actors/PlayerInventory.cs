using System;
using System.Collections;
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
                    i.weapon.type = i.design.weapon.type;
                }
                else if (i.kind == ItemKind.Weapon && Inventory.version < 2) i.weapon.type = DB.W(i.weapon.kind).type;
                if (i.kind == ItemKind.Skill && i.active == null)
                {
                    var source = Inventory.Find(i.sourceWeapon);
                    var legacyType = i.skillId?.StartsWith("cb_") == true ? WeaponType.Bow : i.skillId?.StartsWith("st_") == true ? WeaponType.Staff : i.skillId?.StartsWith("dg_") == true ? WeaponType.Dagger : WeaponType.Sword;
                    i.active = ActiveSkillGenerator.Local(source?.kind == ItemKind.Weapon ? ActiveSkillGenerator.TypeOf(source) : legacyType, Mathf.Clamp(i.skillSlot, 0, 3));
                    i.skillId = i.active.id; i.name = i.active.name;
                }
                if (i.kind == ItemKind.Part && i.Ability != null)
                { i.part = AbilityRecord.From(i.Ability); i.complexity = i.Ability.tier; PartArt.Describe(i); }
                if (i.active != null) ActiveSkillArt.Restore(i.active);
            }
            MigrateFootprints();
            Inventory.Changed = SyncInventory;
            shownWeapon = null; inventorySkills.Clear(); equippedParts.Clear(); Abilities.Clear();
            SyncInventory();
            foreach (var item in Inventory.items.Where(i => i.active != null))
            {
                item.pendingActive = item.pendingArt = item.generatingActive = false;
            }
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
                if(Game.I?.State != GameState.Inventory) StopMoving();
                if (basicAttackRoutine != null) { StopCoroutine(basicAttackRoutine); basicAttackRoutine = null; }
                attackStrikePending = false; queuedAttack = false; comboIndex = 0; comboTimer = 0;
                basicActionLock = 0;
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
                    Skills[slot] = new SkillState();
                    continue;
                }
                if (!inventorySkills.TryGetValue(item.id, out var s) || s.def?.active != item.active)
                {
                    var d = SkillDB.Get(item);
                    inventorySkills[item.id] = s = new SkillState { id = item.skillId, def = d };
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

        public int AcquiredSkills => Enumerable.Range(0,4).Count(slot => (claimedSkillMask & (1 << slot)) != 0);
        public bool SkillUnlocked(int slot) => slot >= 0 && slot < DB.Progression.skillLevels.Length && Level >= DB.Progression.skillLevels[slot];

        public void ConsumeLevelReward()
        {
            if (pendingRewardLevels.Count > 0) pendingRewardLevels.RemoveAt(0);
            PendingEvolutions = pendingRewardLevels.Count;
        }
        public InventoryItem GrantSkill(ActiveSkillData active)
        {
            var weapon = Inventory.items.FirstOrDefault(i => i.kind == ItemKind.Weapon && ActiveSkillGenerator.TypeOf(i) == active.weaponType);
            var item = new InventoryItem { kind = ItemKind.Skill, active = active, skillId = active.id, skillSlot = active.slot,
                name = active.name, sourceWeapon = weapon?.id };
            claimedSkillMask |= 1 << active.slot;
            Inventory.Receive(item, equip: Inventory.Active != null && ActiveSkillGenerator.TypeOf(Inventory.Active) == active.weaponType);
            return item;
        }
        SkillState AcquireNextSkill()
        {
            for (int slot=0;slot<4;slot++)
            {
                if (!SkillUnlocked(slot) || (claimedSkillMask & (1 << slot)) != 0) continue;
                int level = DB.Progression.skillLevels[slot];
                if (!pendingRewardLevels.Contains(level)) pendingRewardLevels.Add(level);
                PendingEvolutions = pendingRewardLevels.Count;
                return null;
            }
            return null;
        }

        public string ActiveGenerationStatus { get; private set; }

        void MigrateFootprints()
        {
            if (Inventory.version >= 2) return;
            string selected = Inventory.activeWeapon;
            var old = Inventory.items.Where(i => i.Placed).Select(i => (item: i, x: i.x, y: i.y)).ToList();
            var groups = old.Where(p => p.item.kind == ItemKind.Weapon).Select(p => (weapon: p.item,
                attached: old.Where(a => a.item != p.item && Mathf.Abs(a.x - p.x) <= 1 && Mathf.Abs(a.y - p.y) <= 1).Select(a => a.item).ToList())).ToList();
            foreach (var i in Inventory.items) i.x = i.y = -1;
            foreach (var g in groups)
            {
                Inventory.PlaceFirst(g.weapon);
                foreach (var i in g.attached)
                {
                    var shape = ActiveSkillGenerator.Shape(g.weapon);
                    var offsets = i.kind == ItemKind.Skill ? new[] { InventoryState.Slot(g.weapon, i.skillSlot) } : shape.parts;
                    bool placed = false;
                    if (g.weapon.Placed) foreach (var d in offsets)
                        if (Inventory.At(g.weapon.x + d.x, g.weapon.y + d.y) == null && Inventory.Move(i.id, g.weapon.x + d.x, g.weapon.y + d.y, out _)) { placed = true; break; }
                    if (!placed) Inventory.PlaceFirst(i);
                }
            }
            foreach (var entry in old.Where(p => !p.item.Placed && groups.All(g => g.weapon != p.item && !g.attached.Contains(p.item)))) Inventory.PlaceFirst(entry.item);
            Inventory.version = 2;
            if (Inventory.Find(selected)?.Placed == true) Inventory.activeWeapon = selected;
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
