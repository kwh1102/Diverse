using UnityEngine;

namespace Diverse
{
    public static class ExplorationBuilder
    {
        public static void Build(World world, ChunkView chunk, StructureSpec site)
        {
            var def = world.Gen.Exploration.Definition(site);
            if (def == null) return;
            for (int i = 0; i < def.props.Count; i++)
            {
                var prop = def.props[i]; var pos = site.center + prop.position;
                var sprite = Art.Decor(prop.sprite);
                string id = site.id + ":" + i;
                if (prop.role == ExplorationRole.Decor)
                {
                    int level = WorldGen.Level(world.Gen.GroundAt(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y)));
                    chunk.AddDecor(sprite, pos + new Vector2(level * WorldGen.FaceSkew, level * WorldGen.FaceHeight),
                        prop.blocks && level == 0, prop.footprint.x, prop.footprint.y, name: "exploration " + prop.sprite,
                        scale: site.theme.SceneryScale(prop.sprite));
                    continue;
                }
                string kind = prop.role switch
                {
                    ExplorationRole.Record => "exploration_record", ExplorationRole.Cache => "chest",
                    ExplorationRole.Merchant => "npc_merchant", ExplorationRole.Traveler => "npc_villager", _ => "herb",
                };
                string label = prop.role switch
                {
                    ExplorationRole.Record => "남겨진 기록", ExplorationRole.Cache => "숨겨진 보물",
                    ExplorationRole.Merchant => "떠돌이 상인", ExplorationRole.Traveler => "탐험 중인 여행자", _ => "희귀 약초",
                };
                if (prop.role == ExplorationRole.Cache)
                {
                    bool open = world.Save.Flag("chest:" + id);
                    sprite = Art.Prop(open ? "chest_open" : "chest"); label = open ? "빈 상자" : label;
                }
                if (prop.role == ExplorationRole.Herb && world.Save.Flag("grove:" + id)) continue;
                var it = Interactable.Create(chunk.transform, kind, id, label, pos, sprite, chunk);
                if (prop.role == ExplorationRole.Record) it.data = site;
                else if (prop.role == ExplorationRole.Traveler) it.data = 99;
                else it.data = new StructureSpec { id = id, name = site.name, tier = site.tier, theme = site.theme };
                if (prop.blocks) chunk.BlockArea(pos + Vector2.up * prop.footprint.y * .5f, prop.footprint.x, prop.footprint.y);
            }
        }
    }
}
