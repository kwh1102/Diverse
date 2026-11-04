using UnityEngine;

namespace Diverse
{
    /// <summary>Eight articulated poses from the original PNG pixels. The face is never resampled or redrawn.</summary>
    internal static class RabbitTravelArt
    {
        public const int FrameCount = 8;
        public static void Prewarm(CostumeDef costume)
        {
            if (costume.frames != null) return;
            foreach (var type in TravelTypes.All)
                for (int frame = 0; frame < FrameCount; frame++) Frame(costume, type, frame);
        }

        public static Sprite Frame(CostumeDef costume, TravelType type, int frame) =>
            Art.Get($"rabbit_motion_{costume.id}_{type}_{frame}", () => Draw(costume, type, Mathf.Clamp(frame, 0, FrameCount - 1)));

        static Sprite Draw(CostumeDef costume, TravelType type, int frame)
        {
            var source = Art.Rabbit(costume, Pose.Idle0);
            if (!source.texture.isReadable || source.packed) return source;
            var rect = source.rect;
            int width = (int)rect.width, height = (int)rect.height;
            if (width != 36 || height != 34) return Art.Rabbit(costume, (Pose)((int)Pose.Run0 + frame % 4));
            var src = source.texture.GetPixels((int)rect.x, (int)rect.y, width, height);
            var cv = new PixelCanvas(56, 44);
            bool moving = frame >= 2 && frame <= 5;
            int stride = frame % 4;
            int hx = 0, hy = 0;
            Vector2Int lf = Vector2Int.zero, rf = Vector2Int.zero, paw = Vector2Int.zero;
            switch (type)
            {
                case TravelType.Dash:
                    hx = moving ? 2 : frame < 2 ? 1 : 0;
                    hy = moving ? -2 + stride % 2 : frame < 2 ? -1 : 0;
                    lf = new Vector2Int(moving ? (stride < 2 ? -3 : 2) : 0, stride % 2);
                    rf = new Vector2Int(moving ? (stride < 2 ? 3 : -2) : 0, (stride + 1) % 2);
                    paw = new Vector2Int(moving ? (stride < 2 ? 3 : -1) : 0, 1);
                    break;
                case TravelType.Flight:
                    hy = moving ? 1 + stride % 2 : frame < 2 ? -1 : 0;
                    lf = new Vector2Int(moving ? -2 : 0, moving ? 3 : 0);
                    rf = new Vector2Int(moving ? -1 : 0, moving ? 2 : 0);
                    paw = new Vector2Int(moving ? -1 : 0, moving ? 3 + stride % 2 : 0);
                    break;
                case TravelType.Burrow:
                    if (moving)
                    {
                        cv.Ellipse(29, 5, 9, 3, Pal.Hex("28313b"));
                        cv.Ellipse(29, 6, 8, 3, Pal.Hex("ac875a"));
                        cv.Rect(24 + frame % 2, 7, 5, 1, Pal.Hex("dec491"));
                        cv.Set(18 + frame, 4, Pal.Hex("ac875a"));
                        Copy(13, 24, 15, 5, -1 + stride, -19);
                        return cv.ToSprite(new Vector2(28, 3));
                    }
                    hy = frame < 2 ? -2 - frame * 2 : frame == 6 ? -3 : 0;
                    paw = new Vector2Int(2, -1);
                    break;
                case TravelType.Swim:
                    hx = moving ? 4 : 1; hy = moving ? -5 + stride % 2 : frame < 2 ? -2 : -1;
                    lf = new Vector2Int(moving ? -5 : 0, moving ? 2 + stride % 2 : 0);
                    rf = new Vector2Int(moving ? -4 : 0, moving ? 3 - stride % 2 : 0);
                    paw = new Vector2Int(moving ? (stride < 2 ? 5 : -1) : 2, moving ? 0 : -1);
                    break;
                case TravelType.Blink:
                    hy = frame < 2 ? -1 : 0;
                    if (moving) hx = stride % 2 == 0 ? -1 : 1;
                    break;
                case TravelType.Bounce:
                    hy = frame < 2 || frame == 6 ? -3 : moving ? 1 : 0;
                    lf = new Vector2Int(-1, moving ? 3 : 0);
                    rf = new Vector2Int(moving ? -1 : 1, moving ? 3 : 0);
                    paw = new Vector2Int(moving ? 1 : 0, moving ? 2 : -1);
                    break;
            }

            // Head, neck/scarf fastening and torso are ONE original pixel layer. Moving these independently
            // exposed outline pixels and tore the neck seam. Only the loose fabric and limbs articulate.
            if (frame == 7) { hx = hy = 0; lf = rf = paw = Vector2Int.zero; }
            if (type == TravelType.Flight && frame is >= 1 and <= 6) Cape(cv, src[9 * width + 11], frame, hx, hy);
            else Copy(7, 7, 6, 7, hx, hy);
            Copy(16, 1, 5, 3, hx + lf.x, hy + lf.y);
            Copy(21, 1, 4, 3, hx + rf.x, hy + rf.y);
            Copy(24, 5, 2, 3, hx + paw.x, hy + paw.y);
            Copy(13, 4, 16, 30, hx, hy, omitPaw: true);
            if (type == TravelType.Swim && moving)
            {
                cv.Set(16 - stride, 5 + stride % 2, TravelTypes.Color(type));
                cv.Set(14 - stride, 4, TravelTypes.Color(type));
            }
            if (type == TravelType.Blink && moving)
            {
                cv.Set(16 + stride, 12, TravelTypes.Color(type));
                cv.Set(43 - stride % 2, 20, TravelTypes.Color(type));
                cv.Set(17 - stride, 25, TravelTypes.Color(type));
            }
            return cv.ToSprite(new Vector2(28, 3));

            void Copy(int x0, int y0, int w, int h, int dx, int dy, bool omitPaw = false)
            {
                for (int y = y0; y < Mathf.Min(height, y0 + h); y++)
                    for (int x = x0; x < Mathf.Min(width, x0 + w); x++)
                    {
                        Color32 c = src[y * width + x];
                        if (c.a == 0 || omitPaw && x >= 24 && y >= 5 && y <= 7) continue;
                        cv.Set(10 + x + dx, 2 + y + dy, c);
                    }
            }
        }

        static void Cape(PixelCanvas cv, Color32 red, int frame, int dx, int dy)
        {
            // The existing scarf unfolds into a fabric cape in the SAME colour; no added costume.
            int lift = frame switch { 1 => 2, 2 => 6, 3 => 3, 4 => -1, 5 => 3, _ => 0 };
            for (int x = 13; x <= 30; x++)
            {
                float t = (30 - x) / 17f;
                int upper = Mathf.RoundToInt(12 + (1 + lift) * t);
                int lower = Mathf.RoundToInt(11 - Mathf.Sin(t * Mathf.PI) * 5 + lift * t);
                cv.Rect(x + dx, lower + dy, 1, Mathf.Max(1, upper - lower + 1), Pal.Hex("28313b"));
                if (upper - lower > 1) cv.Rect(x + dx, lower + 1 + dy, 1, upper - lower - 1, red);
            }
        }
    }
}
