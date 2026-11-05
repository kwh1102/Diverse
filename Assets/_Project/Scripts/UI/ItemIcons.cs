using UnityEngine.UI;

namespace Diverse
{
    public static class ItemIcons
    {
        public static void Bind(Image image,Text glyph,InventoryItem item)
        {
            bool skill=item?.kind==ItemKind.Skill;
            var sprite = PartArt.For(item);
            UIKit.Sprite(image,sprite);
            UIKit.Show(glyph,skill && sprite == null);
            if(!skill || glyph==null) return;
            var definition=SkillDB.Get(item);
            UIKit.Set(glyph,definition?.icon??"?");
            glyph.color=definition?.color??UIKit.Ink;
        }
    }
}
