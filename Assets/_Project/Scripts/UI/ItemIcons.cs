using UnityEngine.UI;

namespace Diverse
{
    public static class ItemIcons
    {
        public static void Bind(Image image,Text glyph,InventoryItem item)
        {
            bool skill=item?.kind==ItemKind.Skill;
            UIKit.Sprite(image,skill?null:PartArt.For(item));
            UIKit.Show(glyph,skill);
            if(!skill || glyph==null) return;
            var definition=SkillDB.Get(item.skillId);
            UIKit.Set(glyph,definition?.icon??"?");
            glyph.color=definition?.color??UIKit.Ink;
        }
    }
}
