using UnityEngine;

namespace Diverse
{
    // Order = the painted ground layers (Art/GroundLayers.asset) and Diverse/Ground's constants. Cliff / Peak stand one / two levels high.
    public enum Ground : byte { Grass, DarkGrass, Path, Sand, Snow, Swamp, Ash, Plaza, Water, DeepWater, Wood, Cliff, Peak, Lava, Toxic, Ice, Crystal, Flagstone, Mirror, Redrock }

    public enum Biome : byte { Meadow, Forest, Dunes, Tundra, Marsh, Ashland, Crystal, Fortress, Mirror }

    public static partial class Art
    {
        // ───────────────────────────── 지면 ─────────────────────────────

        // GroundColors runs for every tile and every border pixel of every chunk bake (and the minimap): parsing hex strings
        // there made thousands of short-lived strings per chunk, and the garbage collections showed up as hitches.
        // The colours are resolved once per look (DesignRuntime.Version) into a table.
        static readonly Color32[] groundTable = new Color32[3 * 32];   // 3 colours per Ground (room to grow)
        static int groundTableFor = -1;

        public static void GroundColors(Ground g, out Color32 a, out Color32 b, out Color32 c)
        {
            if (groundTableFor != DesignRuntime.Version)
            {
                groundTableFor = DesignRuntime.Version;
                foreach (Ground k in System.Enum.GetValues(typeof(Ground)))
                {
                    ResolveGroundColors(k, out var ka, out var kb, out var kc);
                    groundTable[(int)k * 3] = ka; groundTable[(int)k * 3 + 1] = kb; groundTable[(int)k * 3 + 2] = kc;
                }
            }
            int i = (int)g * 3;
            a = groundTable[i]; b = groundTable[i + 1]; c = groundTable[i + 2];
        }

        static void ResolveGroundColors(Ground g, out Color32 a, out Color32 b, out Color32 c)
        {
            switch (g)
            {
                case Ground.Grass: a = Pal.Hex("73a730"); b = Pal.Hex("65932a"); c = Pal.Hex("84bb3f"); break;
                case Ground.DarkGrass: a = Pal.Hex("39642e"); b = Pal.Hex("325828"); c = Pal.Hex("467639"); break;
                case Ground.Path: a = Pal.Hex("c29463"); b = Pal.Hex("ab8257"); c = Pal.Hex("d2a675"); break;
                case Ground.Sand: a = Pal.Hex("e8b86f"); b = Pal.Hex("d4a65f"); c = Pal.Hex("f2c98a"); break;
                case Ground.Snow: a = Pal.Hex("dce0e7"); b = Pal.Hex("c6cad3"); c = Pal.Hex("eef1f5"); break;
                case Ground.Swamp: a = Pal.Hex("59552f"); b = Pal.Hex("4c4928"); c = Pal.Hex("6a663b"); break;
                case Ground.Ash: a = Pal.Hex("494646"); b = Pal.Hex("3d3b3b"); c = Pal.Hex("595555"); break;
                case Ground.Plaza: a = Pal.Hex("86837a"); b = Pal.Hex("75726a"); c = Pal.Hex("98958c"); break;
                case Ground.Wood: a = Pal.Hex("7c5e44"); b = Pal.Hex("6a503a"); c = Pal.Hex("8f6d50"); break;
                case Ground.DeepWater: a = Pal.Hex("115f91"); b = Pal.Hex("0e517c"); c = Pal.Hex("2a75a6"); break;
                case Ground.Cliff: a = Pal.Hex("5f5d48"); b = Pal.Hex("524f3d"); c = Pal.Hex("6e6c56"); break;
                case Ground.Peak: a = Pal.Hex("77755f"); b = Pal.Hex("67654f"); c = Pal.Hex("86846d"); break;
                case Ground.Lava: a = Pal.Hex("c2501f"); b = Pal.Hex("863721"); c = Pal.Hex("e8782a"); break;
                case Ground.Toxic: a = Pal.Hex("5c7829"); b = Pal.Hex("4f6823"); c = Pal.Hex("6d8c33"); break;
                case Ground.Ice: a = Pal.Hex("88b9e8"); b = Pal.Hex("77a6d4"); c = Pal.Hex("9ccbf2"); break;
                case Ground.Crystal: a = Pal.Hex("9fa2c7"); b = Pal.Hex("8e91b4"); c = Pal.Hex("b1b4d8"); break;
                case Ground.Flagstone: a = Pal.Hex("716f5d"); b = Pal.Hex("636151"); c = Pal.Hex("807e6b"); break;
                case Ground.Mirror: a = Pal.Hex("413e5f"); b = Pal.Hex("373452"); c = Pal.Hex("4f4b70"); break;
                case Ground.Redrock: a = Pal.Hex("a85f3d"); b = Pal.Hex("944f32"); c = Pal.Hex("ba6f4b"); break;
                default: a = Pal.Hex("51ada4"); b = Pal.Hex("46978f"); c = Pal.Hex("6cc2b9"); break;
            }
            // The map and minimap use the tiles' own palette colours (base, dark, light of the ground's ramp)
            var look = DB.ArtStyle.grounds.Find(l => l.ground == g);
            if (look != null && look.ramp.Length > 0)
            {
                int last = look.ramp.Length - 1;
                a = TileBaker.PaletteColor(look.ramp[Mathf.Min(1, last)]); b = TileBaker.PaletteColor(look.ramp[0]); c = TileBaker.PaletteColor(look.ramp[Mathf.Min(2, last)]);
            }
            DesignRuntime.Ground(g, ref a, ref b, ref c);
        }

        /// <summary>Theme scenery by sprite key (painted art in Resources/Sprites; code-drawn fallbacks for the nature sets).</summary>
        public static Sprite Decor(string key)
        {
            var parts = key.Split('_');
            if (parts.Length == 3 && System.Enum.TryParse<Biome>(parts[1], out var b) && int.TryParse(parts[2], out int v))
                switch (parts[0])
                {
                    case "tree": return Tree(b, v);
                    case "rock": return Rock(b, v);
                    case "bush": return Bush(b, v);
                    case "tuft": return Tuft(b, v);
                }
            if (parts.Length == 2 && parts[0] == "flower" && int.TryParse(parts[1], out int f)) return Flower(f);
            return Get(key, () => Scenery(key));
        }

        /// <summary>동물 NPC: owl/cat/bear/otter, villager0~9(주민), wanderer0~5(방랑자).</summary>
        public static Sprite Npc(string kind, int frame) => Get($"npc_{kind}_{frame}", () => DrawNpc(kind, frame));

    }
}
