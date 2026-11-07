using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Generated pictures are shared by cards, equipment, fusion and inheritance.</summary>
    public static class PartArt
    {
        static readonly Dictionary<string, Sprite> cache=new Dictionary<string, Sprite>();
        static readonly Dictionary<Sprite,Sprite> centered=new Dictionary<Sprite,Sprite>();
        public static void InvalidateWeapon(string id)=>cache.Remove("weapon:"+id);
        public static uint Hash(string value) { uint h=2166136261; foreach(char c in value??"") h=(h^c)*16777619; return h; }
        public static void Describe(InventoryItem item)
        {
            item.name=PartPresentation.DisplayName(item.Ability);
            item.iconKey=item.Ability?.partImage;
        }
        // Catalog parts are authored with a centered, padded canvas. Preserve it in every UI.
        public static Sprite For(InventoryItem item)=>item?.kind==ItemKind.Part?Raw(item):CenterIcon(Raw(item));

        static Sprite CenterIcon(Sprite source)
        {
            if(source==null) return null;
            if(centered.TryGetValue(source,out var icon) && icon!=null) return icon;
            var texture=source.texture;var region=source.rect;
            if(!texture.isReadable)
                return centered[source]=Sprite.Create(texture,region,new Vector2(.5f,.5f),source.pixelsPerUnit,0,SpriteMeshType.FullRect);
            var pixels=texture.GetPixels32();
            int x0=Mathf.RoundToInt(region.x),y0=Mathf.RoundToInt(region.y),width=Mathf.RoundToInt(region.width),height=Mathf.RoundToInt(region.height);
            int left=width,bottom=height,right=-1,top=-1;
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                if(pixels[(y0+y)*texture.width+x0+x].a<16) continue;
                left=Mathf.Min(left,x);right=Mathf.Max(right,x);bottom=Mathf.Min(bottom,y);top=Mathf.Max(top,y);
            }
            if(right<left || top<bottom) return source;
            int w=right-left+1,h=top-bottom+1,side=Mathf.Max(w,h)+4;
            var canvas=new PixelCanvas(side,side);int dx=(side-w)/2,dy=(side-h)/2;
            for(int y=0;y<h;y++) for(int x=0;x<w;x++) canvas.Set(dx+x,dy+y,pixels[(y0+bottom+y)*texture.width+x0+left+x]);
            return centered[source]=canvas.ToSpriteCentered();
        }

        static Sprite Raw(InventoryItem item)
        {
            if(item==null) return null;
            if(item.kind==ItemKind.Skill) return ActiveSkillArt.Icon(item.active);
            string key=item.kind==ItemKind.Weapon?"weapon:"+item.id:item.kind==ItemKind.Part?"part:"+item.Ability?.tier+":"+item.Ability?.partImage+":"+item.Ability?.Signature():"consumable:"+item.consumable;
            if(cache.TryGetValue(key,out var sprite) && sprite!=null) return sprite;
            if(item.kind==ItemKind.Weapon)
                return cache[key]=item.design!=null?DesignStore.Sprite(item.design.weapon.image,new Vector2(.5f,.5f))??DesignArt.Weapon(item.design.weapon,centered:true)??BuiltinWeapon(item.weapon.type):BuiltinWeapon(item.weapon.type);
            if(item.kind==ItemKind.Part)
            {
                var generated=Resources.Load<Sprite>(item.Ability?.partImage ?? "") ?? DesignStore.Sprite(item.Ability?.partImage,new Vector2(.5f,.5f));
                if(generated!=null) return cache[key]=generated;
                return Pending();
            }
            var baked=Resources.Load<Sprite>("Sprites/inventory_"+item.consumable);
            if(baked!=null) return cache[key]=baked;
            var c=new PixelCanvas(24,24); var metal=Pal.Hex("d7b77a"); var shine=Pal.Hex("fff3c9");
            var liquid=Pal.Hex(item.consumable=="xp"?"76b9ff":item.consumable=="cleanse"?"96e1a6":"f47794");
            c.Rect(9,18,6,3,metal); c.Rect(10,14,4,4,shine); c.Ellipse(12,9,6,7,Pal.Hex("6c526b")); c.Ellipse(12,8,5,5,liquid); c.Rect(9,8,2,4,shine);
            c.Outline(Pal.Hex("23212d")); return cache[key]=c.ToSpriteCentered();
        }
        static Sprite Pending()
        {
            if(cache.TryGetValue("pending",out var sprite)) return sprite;
            var c=new PixelCanvas(24,24); var light=Pal.Hex("aaa3b8");
            c.Rect(5,4,14,16,Pal.Hex("4c465a")); c.Rect(9,15,6,2,light); c.Rect(14,12,2,4,light); c.Rect(11,10,4,2,light); c.Rect(11,7,2,2,light);
            c.Outline(Pal.Hex("23212d")); return cache["pending"]=c.ToSpriteCentered();
        }
        static Sprite BuiltinWeapon(WeaponType type) => Art.StockWeapon(type);
        public static InventoryItem Potion(string kind)=>new InventoryItem {kind=ItemKind.Consumable,consumable=kind,name=kind=="xp"?"경험의 물약":kind=="cleanse"?"정화의 물약":"회복약"};
        public static InventoryItem Preview(AbilityDef a)=>InventoryItem.Part(a);
        public static Sprite Relic()
        {
            if(cache.TryGetValue("relic",out var sprite)) return sprite;
            var c=new PixelCanvas(24,32); c.Ellipse(12,4,10,4,Pal.Hex("6c6778")); c.Ellipse(12,5,8,3,Pal.Hex("9a90a5"));
            c.Rect(10,6,4,13,Pal.Hex("b9cbd4")); c.Rect(10,6,1,13,Pal.Hex("f3eee6")); c.Rect(6,19,12,3,Pal.Hex("d7b77a"));
            c.Rect(10,22,4,6,Pal.Hex("72516a")); c.Rect(9,28,6,3,Pal.Hex("d7b77a")); c.Outline(Pal.Hex("24212e"));
            return cache["relic"]=c.ToSprite(new Vector2(12,3));
        }
    }
}
