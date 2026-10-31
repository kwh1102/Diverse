using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Extra hit and swing effects drawn in code (no AI pictures, no cost), mixed in by Fx.HitSpark and the player's
    /// attacks so repeated hits don't look identical. All face right (+x) and are rotated when played.
    /// </summary>
    public static partial class Art
    {
        /// <summary>X-shaped cut mark: two strokes shoot out from the centre, then thin and fade (crits, sharp weapons).</summary>
        public static Sprite[] ImpactCross(Color32 col, int sizePx) => GetAnim($"impact_x_{col}_{sizePx}", () =>
        {
            col = Pa(col);     // effects take their colours from the palette too
            var frames = new Sprite[4];
            int S = sizePx * 2 + 4;
            float c = S / 2f;
            for (int f = 0; f < 4; f++)
            {
                var cv = new PixelCanvas(S, S);
                float len = sizePx * Mathf.Min(1f, 0.55f + f * 0.25f);
                int thick = f < 2 ? 2 : 1;
                foreach (float a in new[] { 45f, 135f })
                {
                    var d = MathX.Dir(a);
                    for (int t = 0; t < thick; t++)
                    {
                        var n = new Vector2(-d.y, d.x) * (t - (thick - 1) / 2f);
                        var p0 = new Vector2(c, c) - d * len + n;
                        var p1 = new Vector2(c, c) + d * len + n;
                        cv.Line((int)p0.x, (int)p0.y, (int)p1.x, (int)p1.y, f == 0 ? Pal.White : t == 0 ? col : Pal.White);
                    }
                }
                if (f >= 2) for (int i = 0; i < S * 2; i++) { int x = (int)(Hash.F(i, f, 3) * S), y = (int)(Hash.F(i, f, 4) * S); if (Hash.F(x, y, f) > 0.5f) cv.Set(x, y, new Color32(0, 0, 0, 0)); }
                frames[f] = cv.ToSprite(new Vector2(c, c));
            }
            return frames;
        });

        /// <summary>Ring that pops outward with a few beads riding on it (heavy blows).</summary>
        public static Sprite[] ImpactRing(Color32 col, int sizePx) => GetAnim($"impact_ring_{col}_{sizePx}", () =>
        {
            col = Pa(col);     // effects take their colours from the palette too
            var frames = new Sprite[4];
            int S = sizePx * 2 + 6;
            float c = S / 2f;
            for (int f = 0; f < 4; f++)
            {
                var cv = new PixelCanvas(S, S);
                float r = sizePx * (0.35f + f * 0.22f);
                cv.Ring(c, c, r, f < 2 ? 2f : 1.2f, f == 0 ? Pal.White : col);
                for (int i = 0; i < 6; i++)
                {
                    var d = MathX.Dir(i * 60f + f * 12f);
                    float rr = r + 2 + f;
                    cv.Rect((int)(c + d.x * rr), (int)(c + d.y * rr), f < 2 ? 2 : 1, f < 2 ? 2 : 1, f < 2 ? Pal.White : col);
                }
                frames[f] = cv.ToSprite(new Vector2(c, c));
            }
            return frames;
        });

        /// <summary>Shards flung forward in a cone (the effect's +x is the blow's direction).</summary>
        public static Sprite[] ImpactShards(Color32 col, int sizePx) => GetAnim($"impact_shard_{col}_{sizePx}", () =>
        {
            col = Pa(col);     // effects take their colours from the palette too
            var frames = new Sprite[4];
            int S = sizePx * 2 + 6;
            float c = S / 2f;
            var rng = new Rng((uint)(sizePx * 31 + 7));
            var angles = new float[7];
            var speeds = new float[7];
            for (int i = 0; i < angles.Length; i++) { angles[i] = rng.Range(-55f, 55f); speeds[i] = rng.Range(0.6f, 1f); }
            for (int f = 0; f < 4; f++)
            {
                var cv = new PixelCanvas(S, S);
                for (int i = 0; i < angles.Length; i++)
                {
                    var d = MathX.Dir(angles[i]);
                    float r0 = sizePx * speeds[i] * (0.15f + f * 0.25f);
                    float len = Mathf.Max(1, sizePx * 0.35f * (1 - f * 0.22f));
                    var p0 = new Vector2(c, c) + d * r0;
                    var p1 = p0 + d * len;
                    cv.Line((int)p0.x, (int)p0.y, (int)p1.x, (int)p1.y, f < 1 ? Pal.White : col);
                    cv.Set((int)p1.x, (int)p1.y, Pal.White);
                }
                frames[f] = cv.ToSprite(new Vector2(c, c));
            }
            return frames;
        });

        /// <summary>Speed lines streaking forward (thrusts, finisher lunges). Pivot at the back end.</summary>
        public static Sprite[] SpeedLines(Color32 col, int lengthPx) => GetAnim($"speed_{col}_{lengthPx}", () =>
        {
            col = Pa(col);     // effects take their colours from the palette too
            var frames = new Sprite[4];
            int W = lengthPx + 4, H = 15;
            int[] rows = { 2, 5, 7, 9, 12 };
            for (int f = 0; f < 4; f++)
            {
                var cv = new PixelCanvas(W, H);
                for (int i = 0; i < rows.Length; i++)
                {
                    float off = Hash.F(i, 1, 5);
                    int x0 = Mathf.RoundToInt(lengthPx * (off * 0.4f + f * 0.2f));
                    int x1 = Mathf.Min(W - 1, x0 + Mathf.RoundToInt(lengthPx * (0.35f + 0.3f * Hash.F(i, 2, 5)) * (1 - f * 0.2f)));
                    if (x1 > x0) cv.Rect(x0, rows[i], x1 - x0, 1, i == 2 ? Pal.White : col);
                }
                frames[f] = cv.ToSprite(new Vector2(0, H / 2f));
            }
            return frames;
        });
    }
}
