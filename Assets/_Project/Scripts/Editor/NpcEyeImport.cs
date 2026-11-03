using System.Collections.Generic;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static partial class WorldArtOverhaul
    {
        // Authored landmarks on the final native 34/36 x 30 canvas. A tiny categorical feature must not be
        // area-averaged like fur: that made the two eyes different widths, heights, or one row apart.
        // The owl and hero already have correct native eyes and are deliberately absent.
        internal static readonly Dictionary<string, Vector3Int> NpcEyes = new Dictionary<string, Vector3Int>
        {
            { "bear", new Vector3Int(16,21,17) }, { "cat", new Vector3Int(11,17,15) }, { "otter", new Vector3Int(11,17,16) },
            { "villager0", new Vector3Int(13,20,14) }, { "villager1", new Vector3Int(10,16,13) },
            { "villager2", new Vector3Int(13,19,14) }, { "villager3", new Vector3Int(13,20,14) },
            { "villager4", new Vector3Int(14,21,14) }, { "villager5", new Vector3Int(11,18,15) },
            { "villager6", new Vector3Int(11,17,16) }, { "villager7", new Vector3Int(12,19,15) },
            { "villager8", new Vector3Int(13,19,14) }, { "villager9", new Vector3Int(11,18,15) },
            { "wanderer0", new Vector3Int(12,18,15) }, { "wanderer1", new Vector3Int(14,19,16) },
            { "wanderer2", new Vector3Int(16,21,16) }, { "wanderer3", new Vector3Int(13,17,14) },
            { "wanderer4", new Vector3Int(14,20,19) }, { "wanderer5", new Vector3Int(15,20,16) },
        };

        static void PreserveNpcEyes(string key, Color32[] pixels, int width, int height, int frame)
        {
            if (!key.StartsWith("npc_")) return;
            string name = key.Substring(4, key.LastIndexOf('_')-4);
            if (!NpcEyes.TryGetValue(name, out var eyes)) return;
            int y = eyes.z + (frame == 1 ? 1 : 0);
            Repair(eyes.x,y); Repair(eyes.y,y);

            void Repair(int x, int bottom)
            {
                bool Dark(Color32 c) => c.a > 0 && c.r+c.g+c.b < 200;
                var votes = new Dictionary<int,int>();
                // Recover the local face colour independently on each side (fur, eye mask, glasses lens).
                for (int by = bottom-1; by <= bottom+1; by++) for (int bx = x-1; bx <= x+1; bx++)
                {
                    var c = pixels[by*width+bx]; if (c.a == 0 || Dark(c)) continue;
                    int packed = c.r<<16 | c.g<<8 | c.b;
                    votes[packed] = votes.TryGetValue(packed,out int count) ? count+1 : 1;
                }
                int best = -1, total = 0;
                foreach (var vote in votes) if (vote.Value > total) { best = vote.Key; total = vote.Value; }
                if (best < 0) return;
                var fill = new Color32((byte)(best>>16),(byte)(best>>8),(byte)best,255);
                for (int by = bottom-1; by <= bottom+1; by++) for (int bx = x-1; bx <= x+1; bx++)
                    if (Dark(pixels[by*width+bx])) pixels[by*width+bx] = fill;
                var ink = palette[Mathf.Clamp(DB.ArtStyle.outline,0,palette.Length-1)];
                pixels[bottom*width+x] = ink; pixels[(bottom+1)*width+x] = ink;
            }
        }
    }
}
