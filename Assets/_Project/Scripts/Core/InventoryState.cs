using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public enum ItemKind { Weapon, Skill, Part, Consumable }

    [Serializable]
    public class InventoryItem
    {
        public string id = Guid.NewGuid().ToString("N");
        public ItemKind kind;
        public string name;
        public int x = -1, y = -1;
        public WeaponDef weapon;
        public DesignPack design;
        public string skillId;
        public ActiveSkillData active;
        [NonSerialized] public bool generatingActive;
        public bool pendingActive;
        public bool pendingArt;
        public string sourceWeapon;
        public int learnedSkillMask;
        public bool startingWeapon;
        public int skillSlot;
        public AbilityRecord part;
        public int complexity;
        public string iconKey, consumable, ancestor;
        public int count = 1;
        public bool acquisitionApplied;
        [NonSerialized] public float cooldownUntil;
        public float cooldownRemaining;
        [NonSerialized] AbilityDef ability;
        public AbilityDef Ability => ability ??= part?.ToAbility();
        public bool Placed => x >= 0 && y >= 0;
        public InventoryItem Copy() => JsonUtility.FromJson<InventoryItem>(JsonUtility.ToJson(this));

        public static InventoryItem Part(AbilityDef a, string ancestor = null)
        {
            if (string.IsNullOrEmpty(a.id)) a.id = Guid.NewGuid().ToString("N");
            var item = new InventoryItem { kind = ItemKind.Part, part = AbilityRecord.From(a), complexity = a.tier, ancestor = ancestor };
            PartArt.Describe(item);
            return item;
        }
    }

    /// <summary>Coordinates are top-left based. A weapon reserves the exclusive cells of its finite type footprint.</summary>
    [Serializable]
    public class InventoryState
    {
        public int version = 1, width, height;
        public string activeWeapon;
        public List<InventoryItem> items = new List<InventoryItem>();
        [NonSerialized] public Action Changed;
        [NonSerialized] public int revision;
        public static InventoryRules Rules => DB.Asset.inventory;
        public static readonly Vector2Int[] Slots = { new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, 1) };
        public InventoryItem Active => items.FirstOrDefault(i => i.id == activeWeapon && i.kind == ItemKind.Weapon && i.Placed);
        public InventoryItem At(int x, int y) => items.FirstOrDefault(i => i.Placed && i.x == x && i.y == y);
        public InventoryItem Find(string id) => items.FirstOrDefault(i => i.id == id);
        public InventoryItem Owner(int x, int y) => items.FirstOrDefault(i => i.kind == ItemKind.Weapon && i.Placed && ActiveSkillGenerator.Shape(i).Contains(x - i.x, y - i.y));
        public IEnumerable<InventoryItem> Attached(InventoryItem w) => w == null || !w.Placed ? Enumerable.Empty<InventoryItem>() : items.Where(i => i != w && i.Placed && ActiveSkillGenerator.Shape(w).Contains(i.x - w.x, i.y - w.y));
        public static Vector2Int Slot(InventoryItem w, int slot) => ActiveSkillGenerator.Shape(w).skills[slot];
        public IEnumerable<InventoryItem> Pending => items.Where(i => !i.Placed);

        public static InventoryState Create(TravelType type)
        {
            var shape = Rules.Shape(type);
            return new InventoryState { version = 2, width = shape.width, height = shape.height };
        }

        public void Notify() { revision++; Changed?.Invoke(); }

        public bool Validate(out string why)
        {
            why = null;
            var placed = items.Where(i => i.Placed).ToList();
            if (placed.Count(i => i.kind == ItemKind.Weapon) > Rules.maxWeapons) { why = "보관할 수 있는 무기 수를 넘었어."; return false; }
            var cells = new HashSet<int>();
            foreach (var i in placed)
            {
                if (i.x >= width || i.y >= height || !cells.Add(i.y * width + i.x)) { why = "빈 칸에 놓아 줘."; return false; }
                if (i.kind == ItemKind.Weapon && ActiveSkillGenerator.Shape(i).Cells.Any(d => i.x + d.x < 0 || i.y + d.y < 0 || i.x + d.x >= width || i.y + d.y >= height))
                { why = "무기 유형별 주변 공간이 가방 안에 모두 들어와야 해."; return false; }
            }
            var weapons = placed.Where(i => i.kind == ItemKind.Weapon).ToList();
            foreach (var w in weapons)
            {
                var shape = ActiveSkillGenerator.Shape(w);
                var footprint = new HashSet<Vector2Int>(shape.Cells.Select(d => new Vector2Int(w.x, w.y) + d));
                if (weapons.Any(o => o != w && ActiveSkillGenerator.Shape(o).Cells.Any(d => footprint.Contains(new Vector2Int(o.x, o.y) + d))))
                { why = "무기 주변 공간은 겹칠 수 없어."; return false; }
                foreach (var i in Attached(w))
                {
                    int dx = i.x - w.x, dy = i.y - w.y;
                    if (i.kind == ItemKind.Part && shape.parts.Contains(new Vector2Int(dx, dy))) continue;
                    if (i.kind == ItemKind.Skill && i.active != null && i.active.weaponType == ActiveSkillGenerator.TypeOf(w) && i.skillSlot >= 0 && i.skillSlot < 4 && shape.skills[i.skillSlot] == new Vector2Int(dx, dy)) continue;
                    why = "같은 무기 유형·같은 QWER 슬롯에 장착하고, 파츠는 표시된 파츠 칸에 놓아 주세요.";
                    return false;
                }
            }
            foreach (var skill in placed.Where(i => i.kind == ItemKind.Skill && i.active?.linkedMask != 0 && i.active != null))
            {
                var owner = Owner(skill.x, skill.y);
                if (owner == null) continue;
                var group = Attached(owner).Where(i => i.kind == ItemKind.Skill).ToList();
                if (Enumerable.Range(0, 4).Where(s => (skill.active.linkedMask & (1 << s)) != 0).Any(s => !group.Any(i => i.skillSlot == s && i.active?.linkId == skill.active.linkId)))
                { why = $"{skill.active.Links} 종속 스킬은 함께 장착해야 해."; return false; }
            }
            return true;
        }

        public bool Move(string id, int x, int y, out string why)
            => TryMove(id,x,y,true,out why);

        public bool CanMove(string id,int x,int y,out string why)
            => TryMove(id,x,y,false,out why);

        bool TryMove(string id,int x,int y,bool commit,out string why)
        {
            var item = Find(id); why = null;
            if (item == null || x < 0 || y < 0 || x >= width || y >= height) { why = "가방 안의 칸을 골라 줘."; return false; }
            var old = items.Select(i => (i, i.x, i.y)).ToList();
            var target = At(x, y);
            if (item == target) return true;
            if (item.kind == ItemKind.Skill)
                return MoveSkill(item, x, y, commit, out why);
            if (item.kind == ItemKind.Weapon)
            {
                var group = Attached(item).ToList();
                int dx = x - item.x, dy = y - item.y;
                if (group.Any(a => a.x + dx < 0 || a.y + dy < 0 || a.x + dx >= width || a.y + dy >= height))
                { why = "장착된 아이템까지 가방 안에 들어와야 해."; return false; }
                foreach (var a in group) { a.x += dx; a.y += dy; }
            }
            else if (target != null)
            {
                if (!item.Placed || target.kind == ItemKind.Weapon) { why = "빈 칸을 골라 줘."; return false; }
                target.x = item.x; target.y = item.y;
            }
            item.x = x; item.y = y;
            if (!Validate(out why))
            {
                foreach (var p in old) { p.i.x = p.x; p.i.y = p.y; }
                return false;
            }
            if(!commit)
            {
                foreach(var p in old) {p.i.x=p.x;p.i.y=p.y;}
                return true;
            }
            if (Active == null && item.kind == ItemKind.Weapon) activeWeapon = item.id;
            Notify(); return true;
        }

        bool MoveSkill(InventoryItem item, int x, int y, bool commit, out string why)
        {
            why = null;
            if (item.active == null) { why = "스킬 데이터를 준비하는 중입니다."; return false; }
            if (item.generatingActive) { why = "이 스킬의 전용 능력과 이미지를 생성하는 중입니다."; return false; }
            var source = item.Placed ? Owner(item.x, item.y) : null;
            var destination = Owner(x, y);
            var target = At(x, y);
            if (target?.generatingActive == true) { why = "교환할 스킬의 생성이 끝날 때까지 기다려 주세요."; return false; }
            if (destination != null && (ActiveSkillGenerator.TypeOf(destination) != item.active.weaponType || Slot(destination, item.skillSlot) != new Vector2Int(x - destination.x, y - destination.y)))
            { why = "같은 무기 유형의 같은 QWER 슬롯으로만 교환할 수 있어."; return false; }
            var old = items.Select(i => (i, i.x, i.y)).ToList();
            void Store(InventoryItem stored, int preferredX = -1, int preferredY = -1)
            {
                stored.x = stored.y = -1;
                if (preferredX >= 0 && preferredY >= 0 && At(preferredX, preferredY) == null && Owner(preferredX, preferredY) == null)
                { stored.x = preferredX; stored.y = preferredY; return; }
                for (int sy = 0; sy < height; sy++) for (int sx = 0; sx < width; sx++)
                    if (At(sx, sy) == null && Owner(sx, sy) == null) { stored.x = sx; stored.y = sy; return; }
            }
            var incoming = source == null ? items.Where(i => i == item || item.active.linkedMask != 0 && i.active?.linkId == item.active.linkId).ToList()
                : Attached(source).Where(i => i.kind == ItemKind.Skill).ToList();
            int mask = 1 << item.skillSlot;
            var outgoing = destination == null ? new List<InventoryItem>() : Attached(destination).Where(i => i.kind == ItemKind.Skill).ToList();
            bool expanded;
            do
            {
                int before = mask;
                foreach (var skill in incoming.Concat(outgoing).Where(s => (mask & (1 << s.skillSlot)) != 0)) mask |= skill.active?.linkedMask ?? 0;
                expanded = before != mask;
            } while (expanded);
            incoming = incoming.Where(i => (mask & (1 << i.skillSlot)) != 0).ToList();
            outgoing = outgoing.Where(i => (mask & (1 << i.skillSlot)) != 0).ToList();
            if (incoming.Concat(outgoing).Any(i => i.generatingActive)) { why = "교환할 스킬의 생성이 끝날 때까지 기다려 주세요."; return false; }
            string labels = string.Concat(Enumerable.Range(0, 4).Where(s => (mask & (1 << s)) != 0).Select(s => "QWER"[s]));
            if (destination == null)
            {
                if (target != null && (incoming.Count != 1 || target.kind != ItemKind.Skill || target.skillSlot != item.skillSlot || target.active?.weaponType != item.active.weaponType || target.active?.linkedMask != 0))
                { why = "묶음 스킬은 빈 보관 칸으로 이동해 주세요."; return false; }
                if (target != null && source != null) { var d = Slot(source, item.skillSlot); target.x = source.x + d.x; target.y = source.y + d.y; }
                else if (target != null) { target.x = item.x; target.y = item.y; }
                foreach (var skill in incoming) skill.x = skill.y = -1;
                item.x = x; item.y = y;
                foreach (var skill in incoming.Where(i => i != item)) Store(skill);
            }
            else
            {
                foreach (var skill in incoming.Concat(outgoing)) skill.x = skill.y = -1;
                foreach (var skill in incoming) { var d = Slot(destination, skill.skillSlot); skill.x = destination.x + d.x; skill.y = destination.y + d.y; }
                foreach (var skill in outgoing)
                {
                    if (source != null) { var d = Slot(source, skill.skillSlot); skill.x = source.x + d.x; skill.y = source.y + d.y; }
                    else Store(skill);
                }
            }
            if (!Validate(out why)) { foreach (var p in old) { p.i.x = p.x; p.i.y = p.y; } return false; }
            if (!commit) foreach (var p in old) { p.i.x = p.x; p.i.y = p.y; }
            else Notify();
            why = labels.Length > 1 ? $"{labels} 종속 관계를 보존해 함께 교환합니다." : "같은 유형·같은 슬롯 교환";
            return true;
        }

        public bool PlaceFirst(InventoryItem item, bool equip = false)
        {
            if (!items.Contains(item)) items.Add(item);
            if (equip && Active != null)
            {
                var w = Active;
                var spots = item.kind == ItemKind.Skill ? new[] { Slot(w, item.skillSlot) } : ActiveSkillGenerator.Shape(w).parts;
                foreach (var d in spots) if (At(w.x + d.x, w.y + d.y) == null && Move(item.id, w.x + d.x, w.y + d.y, out _)) return true;
            }
            // Rewards default to storage, so receiving a part never silently changes the build.
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                if (At(x, y) != null || item.kind != ItemKind.Weapon && Owner(x, y) != null) continue;
                if (Move(item.id, x, y, out _)) return true;
            }
            item.x = item.y = -1; Notify(); return false;
        }

        public void Receive(InventoryItem item, bool equip = false)
        {
            if (item.kind == ItemKind.Consumable)
            {
                foreach (var stack in items.Where(i => i.kind == item.kind && i.consumable == item.consumable && i.count < Rules.consumableStack))
                {
                    int n = Math.Min(item.count, Rules.consumableStack - stack.count); stack.count += n; item.count -= n;
                    if (item.count <= 0) { Notify(); return; }
                }
            }
            PlaceFirst(item, equip);
        }

        public bool Select(string id)
        {
            var w = Find(id);
            if (w?.kind != ItemKind.Weapon || !w.Placed) return false;
            activeWeapon = id; Notify(); return true;
        }

        public bool Discard(string id, out string why)
        {
            why = null; var item = Find(id);
            if (item == null) return false;
            if (item.kind == ItemKind.Weapon && item.Placed && items.Count(i => i.kind == ItemKind.Weapon && i.Placed) == 1)
            { why = "마지막 무기는 버릴 수 없어."; return false; }
            if (item.kind == ItemKind.Weapon)
                foreach (var attached in Attached(item).ToList()) attached.x = attached.y = -1;
            if (item.active?.linkedMask > 0)
                items.RemoveAll(i => i.active?.linkId == item.active.linkId);
            else items.Remove(item);
            if (activeWeapon == id) activeWeapon = items.FirstOrDefault(i => i.kind == ItemKind.Weapon && i.Placed)?.id;
            Notify(); return true;
        }

        public bool Fuse(string a, string b, InventoryItem result, out string why)
        {
            var first = Find(a); var second = Find(b); why = null;
            if (first == null || second == null || first == second || first.kind != ItemKind.Part || second.kind != ItemKind.Part || first.complexity != second.complexity || first.complexity >= Rules.maxPartTier || result?.Ability == null || result.complexity != first.complexity + 1 || result.Ability.Signature() != PartCatalog.Fuse(first.Ability, second.Ability)?.Signature())
            { why = "최고 등급 미만의 같은 등급 파츠 두 개가 필요해."; return false; }
            result.x = first.x; result.y = first.y;
            items.Remove(first); items.Remove(second); items.Add(result);
            if (!Validate(out why)) { items.Remove(result); items.Add(first); items.Add(second); return false; }
            Notify(); return true;
        }
    }
}
