using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Stable physical objects, shared by offer cards, inventory, fusion and legacy. No network wait for icons.</summary>
    public static class PartArt
    {
        static readonly string[] Objects = { "안경", "나침반", "깃털", "반지", "부적", "기어", "방울", "보석" };
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        public static void InvalidateWeapon(string id) => cache.Remove("weapon:"+id);
        public static uint Hash(string value) { uint h = 2166136261; foreach (char c in value ?? "") h = (h ^ c) * 16777619; return h; }
        public static void Describe(InventoryItem item)
        {
            var a = item.Ability;
            string element = a?.rules.SelectMany(r => r.ops).Select(o => o.element).FirstOrDefault(RuleLanguage.IsElement) ?? "Arcane";
            int form = (int)(Hash(a?.Signature()) % Objects.Length);
            item.iconKey = element + ":" + form + ":" + Hash(a?.Signature());
            string prefix = element switch { "Fire" => "불꽃", "Frost" => "서리", "Lightning" => "번개", "Shadow" => "그림자", "Holy" => "빛", "Poison" => "맹독", "Wind" => "바람", "Blood" => "핏빛", "Void" => "공허", _ => "별빛" };
            item.name = prefix + "의 " + Objects[form];
        }

        public static Sprite For(InventoryItem i)
        {
            if (i == null) return null;
            if (i.kind == ItemKind.Weapon)
            {
                string key = "weapon:" + i.id;
                if (cache.TryGetValue(key, out var ws)) return ws;
                return cache[key] = i.design != null ? DesignStore.Sprite(i.design.weapon.image, new Vector2(.5f,.5f)) ?? DesignArt.Weapon(i.design.weapon, centered: true) ?? BuiltinWeapon(i.weapon.kind) : BuiltinWeapon(i.weapon.kind);
            }
            if (i.kind == ItemKind.Part && string.IsNullOrEmpty(i.iconKey)) Describe(i);
            string k = i.kind == ItemKind.Part ? i.iconKey : i.kind == ItemKind.Skill ? "skill:" + i.skillId : "consumable:" + i.consumable;
            if (cache.TryGetValue(k, out var sprite)) return sprite;
            if (i.kind == ItemKind.Consumable)
            {
                var baked=Resources.Load<Sprite>("Sprites/inventory_"+i.consumable);
                if (baked!=null) return cache[k]=baked;
            }
            var c = new PixelCanvas(24,24);
            var metal = Pal.Hex("d7b77a"); var shade = Pal.Hex("6c526b"); var shine = Pal.Hex("fff3c9");
            if (i.kind == ItemKind.Skill)
            {
                var color = SkillDB.Get(i.skillId)?.color ?? metal;
                c.Rect(5,3,14,18,shade); c.Rect(6,4,12,16,metal); c.Rect(7,5,10,14,color);
                uint h = Hash(i.skillId); for (int n=0;n<5;n++) c.Line(8,7+n*2,10+(int)((h>>(n*3))%7),7+n*2,shine);
            }
            else if (i.kind == ItemKind.Consumable)
            {
                var liquid = Pal.Hex(i.consumable == "xp" ? "76b9ff" : i.consumable == "cleanse" ? "96e1a6" : "f47794");
                c.Rect(9,18,6,3,metal); c.Rect(10,14,4,4,shine); c.Ellipse(12,9,6,7,shade); c.Ellipse(12,8,5,5,liquid); c.Rect(9,8,2,4,shine);
            }
            else
            {
                var p = k.Split(':'); int.TryParse(p.ElementAtOrDefault(1), out int form);
                System.Enum.TryParse(p[0], out Element el); var color = Combat.ElementColor(el);
                switch (form)
                {
                    case 0: c.Ring(7,12,5,2,metal); c.Ring(17,12,5,2,metal); c.Rect(10,12,4,2,metal); c.Rect(4,11,4,3,color); c.Rect(15,11,4,3,color); break;
                    case 1: c.Circle(12,12,8,shade); c.Ring(12,12,8,2,metal); c.Line(8,8,16,16,color); c.Line(11,11,15,16,shine); break;
                    case 2: c.ThickLine(new Vector2(6,4),new Vector2(17,19),3,color); c.Line(5,3,17,19,shine); c.Line(9,9,16,10,shade); c.Line(12,13,19,14,shade); break;
                    case 3: c.Ring(12,9,6,3,metal); c.Rect(8,14,8,5,shade); c.Rect(10,15,4,5,color); c.Set(11,18,shine); break;
                    case 4: c.Line(5,20,18,20,metal); c.Line(5,20,9,15,metal); c.Line(18,20,14,15,metal); c.Rect(7,4,10,13,shade); c.Rect(8,5,8,11,color); c.Line(9,13,14,8,shine); break;
                    case 5: c.Rect(3,9,18,6,metal); c.Rect(9,3,6,18,metal); c.Circle(12,12,8,metal); c.Circle(12,12,4,shade); c.Circle(12,12,2,color); break;
                    case 6: c.Ring(12,20,2,1,metal); c.Ellipse(12,11,6,7,color); c.Rect(4,5,16,2,metal); c.Circle(12,3,2,shine); break;
                    default: c.ThickLine(new Vector2(6,12),new Vector2(12,19),3,shade); c.ThickLine(new Vector2(12,19),new Vector2(18,12),3,color); c.ThickLine(new Vector2(6,12),new Vector2(12,4),3,color); c.ThickLine(new Vector2(12,4),new Vector2(18,12),3,metal); c.Line(12,17,9,12,shine); break;
                }
                // Engraving is derived from the mechanic, so equal-looking forms still have distinct pixels.
                uint h = Hash(k);
                for (int n=0;n<4;n++) c.Set(9+n*2,6+(int)((h>>(n*3))%5),shine);
            }
            c.Outline(Pal.Hex("23212d"));
            return cache[k] = c.ToSpriteCentered();
        }

        static Sprite BuiltinWeapon(WeaponKind kind)
        {
            // Art.Weapon may contain the current designed weapon's override; draw other kinds from their baked assets.
            return Resources.Load<Sprite>("Sprites/weapon_" + kind) ?? Art.Weapon(kind);
        }
        public static InventoryItem Potion(string kind) => new InventoryItem { kind = ItemKind.Consumable, consumable = kind,
            name = kind == "xp" ? "경험의 물약" : kind == "cleanse" ? "정화의 물약" : "회복약" };
        public static InventoryItem Preview(AbilityDef a) => InventoryItem.Part(a);

        public static Sprite Relic()
        {
            if (cache.TryGetValue("relic",out var sprite)) return sprite;
            var c=new PixelCanvas(24,32);
            c.Ellipse(12,4,10,4,Pal.Hex("6c6778")); c.Ellipse(12,5,8,3,Pal.Hex("9a90a5"));
            c.Rect(10,6,4,13,Pal.Hex("b9cbd4")); c.Rect(10,6,1,13,Pal.Hex("f3eee6"));
            c.Rect(6,19,12,3,Pal.Hex("d7b77a")); c.Rect(10,22,4,6,Pal.Hex("72516a")); c.Rect(9,28,6,3,Pal.Hex("d7b77a"));
            c.Outline(Pal.Hex("24212e")); return cache["relic"]=c.ToSprite(new Vector2(12,3));
        }
    }
}
