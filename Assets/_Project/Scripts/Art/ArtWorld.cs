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
        static readonly Color32 ShoreEdge = Pal.Hex("3b2f3a"), ShoreDeep = Pal.Hex("2f5fa8"), ShoreFoam = Pal.Hex("bfe6ff");

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

        /// <summary>
        /// 청크 하나의 지면을 텍스처 한 장으로 굽는다. 타일 경계에 디더링/테두리를 넣어 도트 느낌을 살린다.
        /// </summary>
        public static Texture2D BakeGround(Ground[,] tiles, int size, int wx0, int wy0, int seed)
        {
            int T = PPU;
            var px = new Color32[size * T * size * T];
            int W = size * T;
            for (int ty = 0; ty < size; ty++)
                for (int tx = 0; tx < size; tx++)
                {
                    var g = tiles[tx + 1, ty + 1];
                    GroundColors(g, out var a, out var b, out var c);
                    bool water = g == Ground.Water || g == Ground.DeepWater;
                    for (int y = 0; y < T; y++)
                        for (int x = 0; x < T; x++)
                        {
                            int gx = (wx0 + tx) * T + x, gy = (wy0 + ty) * T + y;
                            float n = Hash.F(gx, gy, seed);
                            Color32 col = a;
                            if (n < 0.18f) col = b;
                            else if (n > 0.94f) col = c;
                            if (DesignRuntime.GroundTexel(g, gx, gy, out var texel)) col = texel;   // generated ground texture

                            // 이웃 타일과의 경계 처리
                            Ground l = tiles[tx, ty + 1], r = tiles[tx + 2, ty + 1], d = tiles[tx + 1, ty], u = tiles[tx + 1, ty + 2];
                            if (!water)
                            {
                                if (x == 0 && l != g && Pri(l) > Pri(g)) col = Mix(col, l, gx, gy, seed);
                                if (x == T - 1 && r != g && Pri(r) > Pri(g)) col = Mix(col, r, gx, gy, seed);
                                if (y == 0 && d != g && Pri(d) > Pri(g)) col = Mix(col, d, gx, gy, seed);
                                if (y == T - 1 && u != g && Pri(u) > Pri(g)) col = Mix(col, u, gx, gy, seed);
                            }
                            else
                            {
                                // 물가: 위쪽 땅과 맞닿으면 거품/모래 테두리, 잔물결
                                bool landUp = u != Ground.Water && u != Ground.DeepWater;
                                bool landL = l != Ground.Water && l != Ground.DeepWater;
                                bool landR = r != Ground.Water && r != Ground.DeepWater;
                                bool landD = d != Ground.Water && d != Ground.DeepWater;
                                if (landUp && y >= T - 3) col = y == T - 1 ? ShoreEdge : ShoreDeep;
                                else if ((landL && x == 0) || (landR && x == T - 1) || (landD && y == 0)) col = ShoreFoam;
                                else if (((gx / 3 + gy * 2) % 13 == 0) && n > 0.5f) col = c;
                            }

                            // Bridge: planks across the travel direction, dark rails where it borders water
                            if (g == Ground.Wood)
                            {
                                bool wl = l == Ground.Water || l == Ground.DeepWater, wr = r == Ground.Water || r == Ground.DeepWater;
                                bool wd = d == Ground.Water || d == Ground.DeepWater, wu = u == Ground.Water || u == Ground.DeepWater;
                                bool horizontal = (wu || wd) && !(wl || wr);
                                int along = horizontal ? gx : gy;
                                col = along % 4 == 0 ? b : (Hash.F(gx / 4, gy / 4, seed) > 0.5f ? a : c);
                                if ((wl && x <= 1) || (wr && x >= T - 2) || (wd && y <= 1) || (wu && y >= T - 2)) col = Pal.WoodDark;
                            }

                            // 풀 지면에 작은 풀잎 무늬
                            if ((g == Ground.Grass || g == Ground.DarkGrass) && Hash.F(gx / 2, gy / 2, seed + 77) > 0.985f && y > 1)
                            {
                                col = b;
                                px[(ty * T + y - 1) * W + tx * T + x] = b;
                            }
                            px[(ty * T + y) * W + tx * T + x] = col;
                        }
                }
            var tex = new Texture2D(W, W, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
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

        static int Pri(Ground g) => g switch
        {
            Ground.Water or Ground.DeepWater => -1,
            Ground.Path => 6, Ground.Plaza => 7, Ground.Wood => 8,
            Ground.Snow => 5, Ground.Sand => 4, Ground.Ash => 3, Ground.Swamp => 2, Ground.DarkGrass => 1,
            _ => 0,
        };

        static Color32 Mix(Color32 baseCol, Ground other, int gx, int gy, int seed)
        {
            GroundColors(other, out var a, out _, out _);
            return Hash.F(gx, gy, seed + 5) > 0.5f ? a : baseCol;
        }

        /// <summary>마을 사람 (동물 NPC). kind: owl(이야기꾼), cat(상인), bear(대장장이), rabbit(주민)</summary>
        public static Sprite Npc(string kind, int frame) => Get($"npc_{kind}_{frame}", () => DrawNpc(kind, frame));

    }
}
