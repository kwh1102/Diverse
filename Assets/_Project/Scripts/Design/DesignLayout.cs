using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// How a designed world is arranged, so the scenery makes sense for its concept instead of being sprinkled everywhere:
    /// a city gets a street grid with buildings lining the streets, a village winding lanes with houses along them, a forest
    /// dense groves and clearings, a farm rows of crops, ruins broken paving, a wasteland almost nothing.
    /// Each designed object has a size (large/medium/small/tiny) and a place (road/cluster/scatter/rows/rare).
    /// Water, the town plaza, roads to landmarks and structures are never touched (WorldGen decides those).
    /// </summary>
    public static class DesignLayout
    {
        public static readonly string[] Styles = { "wild", "forest", "city", "village", "farm", "ruins", "wasteland" };
        public static readonly string[] Sizes = { "large", "medium", "small", "tiny" };
        public static readonly string[] Places = { "road", "cluster", "scatter", "rows", "rare" };

        class Item { public Sprite sprite; public string size, place; public bool building; }

        static string builtFor;
        static readonly List<Item> items = new List<Item>();

        static WorldLook World => DesignRuntime.Active.world;

        /// <summary>A designed world with its own scenery: ChunkView hands decoration over to Decorate.</summary>
        public static bool Active => World.HasDecor;

        static List<Item> Items
        {
            get
            {
                var w = World;
                string key = string.Join(",", w.decorImages ?? new string[0]) + string.Join(",", w.townImages ?? new string[0]);
                if (key == builtFor) return items;
                builtFor = key;
                items.Clear();
                for (int i = 0; i < 12 && w.decorImages != null; i++)
                {
                    var tex = DesignStore.Image(w.decorImages[i]);
                    if (tex == null) continue;
                    items.Add(new Item
                    {
                        sprite = DesignStore.Sprite(w.decorImages[i], new Vector2(0.5f, 1f / tex.height)),
                        size = w.decorSizes != null && i < w.decorSizes.Length && w.decorSizes[i] != null ? w.decorSizes[i] : Sizes[Mathf.Min(3, i / 3)],
                        place = w.decorPlaces != null && i < w.decorPlaces.Length && w.decorPlaces[i] != null ? w.decorPlaces[i] : "scatter",
                    });
                }
                // The world's houses also line the streets of a city / village
                if (w.HasTown && (w.layout == "city" || w.layout == "village"))
                    for (int i = 0; i < 2; i++)
                    {
                        var tex = DesignStore.Image(w.townImages[i]);
                        if (tex != null) items.Add(new Item { sprite = DesignStore.Sprite(w.townImages[i], new Vector2(0.5f, 1f / tex.height)), size = "large", place = "road", building = true });
                    }
                return items;
            }
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;

        // ───────────────────────── Ground (called by WorldGen for open land only) ─────────────────────────

        /// <summary>Roads / lanes / paving of the layout on open land. g = what the biome made; returns the tile to use.</summary>
        public static Ground Ground(int tx, int ty, Ground g, int seed)
        {
            if (!Active) return g;
            float x = tx + 0.5f, y = ty + 0.5f;
            switch (World.layout)
            {
                case "city":
                    if (Mod(tx, 12) < 2 || Mod(ty, 12) < 2) return Diverse.Ground.Path;
                    break;
                case "village":
                    if (Mathf.Abs(Noise.Perlin(x, y, seed + 701, 0.035f) - 0.5f) < 0.03f || Mathf.Abs(Noise.Perlin(y, x, seed + 702, 0.03f) - 0.5f) < 0.025f)
                        return Diverse.Ground.Path;
                    break;
                case "farm":
                    if (Mod(tx, 18) == 0 || Mod(ty, 18) == 0) return Diverse.Ground.Path;
                    break;
                case "ruins":
                    if (Noise.Perlin(x, y, seed + 703, 0.08f) > 0.62f && Hash.F(tx, ty, seed + 704) > 0.3f) return Diverse.Ground.Plaza;
                    break;
            }
            return g;
        }

        // ───────────────────────── Scenery (called by ChunkView per open tile) ─────────────────────────

        static Item Pick(string size, string place, float r)
        {
            var list = Items;
            var exact = list.Where(i => i.size == size && i.place == place).ToList();
            if (exact.Count == 0) exact = list.Where(i => i.size == size && i.place != "rare").ToList();
            if (exact.Count == 0) return null;
            return exact[Mathf.Min(exact.Count - 1, (int)(r * exact.Count))];
        }

        /// <summary>
        /// Place this world's scenery on open tile (tx, ty) of a chunk (tiles has a 1-tile border). The rules follow the layout;
        /// overall density is far lower than the hand-made grass tufts so a world reads clearly.
        /// </summary>
        public static void Decorate(ChunkView view, Ground[,] tiles, int tx, int ty, int wx, int wy, Vector2 p, int seed)
        {
            if (Items.Count == 0) return;
            bool Road(int dx, int dy) { var t = tiles[tx + 1 + dx, ty + 1 + dy]; return t == Diverse.Ground.Path || t == Diverse.Ground.Plaza; }
            bool Wet(int dx, int dy) => WorldGen.IsWater(tiles[tx + 1 + dx, ty + 1 + dy]);
            bool nearRoad = Road(0, 1) || Road(0, -1) || Road(1, 0) || Road(-1, 0);
            // Buildings stand just ABOVE a street with their front on it (a tall sprite then rises into its own block instead of
            // covering the road), side by side along the street. Side streets get lamps and the like.
            bool facesStreet = Road(0, -1) && !Road(0, 1);
            bool nearWater = Wet(1, 0) || Wet(-1, 0) || Wet(0, 1) || Wet(0, -1);
            float h = Hash.F(wx, wy, seed + 17), r = Hash.F(wx, wy, seed + 29);
            float grove = Noise.Perlin(p.x, p.y, seed + 333, 0.05f);

            Item item = null;
            switch (World.layout)
            {
                case "city":
                    if (facesStreet && !nearWater && Mod(wx, 3) == 0 && h < 0.9f) item = Pick("large", "road", r);
                    else if (nearRoad && !facesStreet && Mod(wx + wy, 5) == 0 && h < 0.5f) item = Pick("medium", "road", r);
                    else if (!nearRoad && h < 0.025f) item = Pick("small", "scatter", r);
                    else if (h < 0.035f) item = Pick("tiny", "scatter", r);
                    break;
                case "village":
                    if (facesStreet && !nearWater && Mod(wx, 4) == 0 && h < 0.45f) item = Pick("large", "road", r);
                    else if (nearRoad && h < 0.06f) item = Pick("medium", "road", r);
                    else if (grove > 0.6f && h < 0.12f && Mod(wx, 2) == 0) item = Pick("large", "cluster", r);
                    else if (h < 0.03f) item = Pick("small", "scatter", r);
                    else if (h < 0.05f) item = Pick("tiny", "scatter", r);
                    break;
                case "forest":
                    if (grove > 0.52f && !nearWater && h < 0.3f && Mod(wx, 2) == 0 && Mod(wy, 2) == 1) item = Pick("large", "cluster", r);
                    else if (grove > 0.45f && h < 0.05f) item = Pick("medium", "cluster", r);
                    else if (h < 0.03f) item = Pick("small", "scatter", r);
                    else if (h < 0.05f) item = Pick("tiny", "scatter", r);
                    break;
                case "farm":
                    if (Mod(wy, 3) == 0 && grove > 0.38f && h < 0.85f) item = Pick("small", "rows", r) ?? Pick("medium", "rows", r);
                    else if (grove < 0.3f && h < 0.02f && !nearWater) item = Pick("large", "scatter", r);
                    else if (h < 0.025f) item = Pick("tiny", "scatter", r);
                    break;
                case "ruins":
                    if (h < 0.012f && !nearWater) item = Pick("large", "scatter", r);
                    else if (h < 0.045f) item = Pick("medium", "scatter", r);
                    else if (h < 0.065f) item = Pick("small", "scatter", r);
                    else if (h < 0.08f) item = Pick("tiny", "scatter", r);
                    break;
                case "wasteland":
                    if (h < 0.004f && !nearWater) item = Pick("large", "scatter", r);
                    else if (h < 0.012f) item = Pick("medium", "scatter", r);
                    else if (h < 0.025f) item = Pick("tiny", "scatter", r);
                    break;
                default: // wild
                    if (grove > 0.6f && !nearWater && h < 0.16f && Mod(wx, 2) == 0 && Mod(wy, 2) == 1) item = Pick("large", "cluster", r) ?? Pick("large", "scatter", r);
                    else if (h < 0.008f) item = Pick("medium", "scatter", r);
                    else if (h < 0.03f) item = Pick("small", "scatter", r);
                    else if (h < 0.05f) item = Pick("tiny", "scatter", r);
                    break;
            }
            // Something special now and then
            if (item == null && h > 0.997f) item = Pick("medium", "rare", r) ?? Pick("large", "rare", r);
            if (item == null) return;

            float wUnits = item.sprite.rect.width / Art.PPU;
            switch (item.size)
            {
                case "large":
                    var sr = view.AddDecor(item.sprite, p, true, Mathf.Max(0.6f, wUnits * (item.building ? 0.8f : 0.4f)), item.building ? 0.9f : 0.55f, false, item.building ? "building" : "decor");
                    if (!item.building) sr.flipX = r > 0.5f;
                    break;
                case "medium":
                    view.AddDecor(item.sprite, p, true, Mathf.Max(0.4f, wUnits * 0.6f), 0.45f, false, "prop").flipX = r > 0.5f;
                    break;
                case "small":
                    view.AddDecor(item.sprite, p + new Vector2(r * 0.6f - 0.3f, 0), false, 0, 0, false, "prop").flipX = r > 0.5f;
                    break;
                default:
                    view.AddDecor(item.sprite, p + new Vector2(r * 0.8f - 0.4f, h * 0.8f - 0.4f), false, 0, 0, flat: true);
                    break;
            }
        }
    }
}
