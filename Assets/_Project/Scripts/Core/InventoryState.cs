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

    /// <summary>Coordinates are top-left based. A weapon reserves an exclusive, entirely in-bounds 3x3 square.</summary>
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
        public InventoryItem Owner(int x, int y) => items.FirstOrDefault(i => i.kind == ItemKind.Weapon && i.Placed && Math.Abs(i.x - x) <= 1 && Math.Abs(i.y - y) <= 1);
        public IEnumerable<InventoryItem> Attached(InventoryItem w) => w == null || !w.Placed ? Enumerable.Empty<InventoryItem>() : items.Where(i => i != w && i.Placed && Math.Abs(i.x - w.x) <= 1 && Math.Abs(i.y - w.y) <= 1);
        public IEnumerable<InventoryItem> Pending => items.Where(i => !i.Placed);

        public static InventoryState Create(TravelType type)
        {
            var shape = Rules.Shape(type);
            return new InventoryState { width = shape.width, height = shape.height };
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
                if (i.kind == ItemKind.Weapon && (i.x < 1 || i.y < 1 || i.x >= width - 1 || i.y >= height - 1))
                { why = "무기 주변 3×3 영역이 가방 안에 모두 들어와야 해."; return false; }
            }
            var weapons = placed.Where(i => i.kind == ItemKind.Weapon).ToList();
            foreach (var w in weapons)
            {
                if (weapons.Any(o => o != w && Math.Abs(w.x - o.x) <= 2 && Math.Abs(w.y - o.y) <= 2))
                { why = "무기의 3×3 영역은 겹칠 수 없어."; return false; }
                foreach (var i in Attached(w))
                {
                    int dx = i.x - w.x, dy = i.y - w.y;
                    if (dx != 0 && dy != 0 && i.kind == ItemKind.Part) continue;
                    if (i.kind == ItemKind.Skill && i.skillSlot >= 0 && i.skillSlot < Slots.Length && Slots[i.skillSlot] == new Vector2Int(dx, dy)) continue;
                    why = "무기 주변: 위 Q · 왼쪽 W · 오른쪽 E · 아래 R · 대각선 파츠만 가능해.";
                    return false;
                }
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

        public bool PlaceFirst(InventoryItem item, bool equip = false)
        {
            if (!items.Contains(item)) items.Add(item);
            if (equip && Active != null)
            {
                var w = Active;
                var spots = item.kind == ItemKind.Skill ? new[] { Slots[item.skillSlot] } : new[] { new Vector2Int(-1,-1), new Vector2Int(1,-1), new Vector2Int(-1,1), new Vector2Int(1,1) };
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
            items.Remove(item);
            if (activeWeapon == id) activeWeapon = items.FirstOrDefault(i => i.kind == ItemKind.Weapon && i.Placed)?.id;
            Notify(); return true;
        }

        public bool Fuse(string a, string b, InventoryItem result, out string why)
        {
            var first = Find(a); var second = Find(b); why = null;
            if (first == null || second == null || first == second || first.kind != ItemKind.Part || second.kind != ItemKind.Part || first.complexity != second.complexity)
            { why = "같은 복잡도의 서로 다른 파츠 두 개를 골라 줘."; return false; }
            result.x = first.x; result.y = first.y;
            items.Remove(first); items.Remove(second); items.Add(result);
            if (!Validate(out why)) { items.Remove(result); items.Add(first); items.Add(second); return false; }
            Notify(); return true;
        }
    }
}
