using UnityEngine;

namespace Diverse
{
    internal static class RabbitTravelArt
    {
        // Four fixed poses per type: entry, two movement poses, and exit.
        static readonly (Pose pose, float angle, float width, float height)[][] Poses =
        {
            new[] { (Pose.Run0, -12f, 1.04f, 0.9f), (Pose.Run1, -22f, 1.08f, 0.93f), (Pose.Run3, -18f, 1.10f, 0.93f), (Pose.Idle0, 7f, 1f, 0.94f) },
            new[] { (Pose.Run0, -20f, 0.93f, 1.08f), (Pose.Run1, -31f, 1.04f, 1.02f), (Pose.Run3, -16f, 1.1f, 0.92f), (Pose.Idle0, 8f, 1.06f, 0.9f) },
            new[] { (Pose.AttackHit, -44f, 1.05f, 0.75f), (Pose.Run1, -12f, 1.14f, 0.24f), (Pose.Run3, 8f, 1.15f, 0.30f), (Pose.Run0, 24f, 0.92f, 0.84f) },
            new[] { (Pose.Run0, -55f, 0.95f, 1.02f), (Pose.Run1, -65f, 0.92f, 1.04f), (Pose.Run3, -56f, 0.92f, 0.96f), (Pose.Run0, 14f, 0.96f, 0.89f) },
            new[] { (Pose.Idle0, -8f, 1.08f, 0.9f), (Pose.Run0, -10f, 0.38f, 1.14f), (Pose.Run1, 8f, 0.63f, 1.05f), (Pose.Idle0, 8f, 1.08f, 0.92f) },
            new[] { (Pose.Idle0, 0f, 1.16f, 0.71f), (Pose.Run0, -12f, 0.85f, 1.15f), (Pose.Run2, 8f, 1.02f, 0.85f), (Pose.Idle0, 0f, 1.18f, 0.70f) },
        };

        public static void Prewarm(CostumeDef costume)
        {
            if (costume.frames != null) return;
            foreach (var type in TravelTypes.All)
                for (int frame = 0; frame < 4; frame++)
                    Frame(costume, type, frame);
        }

        public static Sprite Frame(CostumeDef costume, TravelType type, int frame) =>
            Art.Get($"rabbit_travel_{costume.id}_{type}_{frame}", () => Draw(costume, type, frame));

        static Sprite Draw(CostumeDef costume, TravelType type, int frame)
        {
            var pose = Poses[(int)type][frame];
            var sprite = Art.Rabbit(costume, pose.pose);
            if (!sprite.texture.isReadable || sprite.packed) return sprite;
            var rect = sprite.rect;
            int w = (int)rect.width, h = (int)rect.height;
            var pixels = sprite.texture.GetPixels((int)rect.x, (int)rect.y, w, h);
            var canvas = new PixelCanvas(w * 2, h * 2);
            var pivot = new Vector2(canvas.W / 2f, 3f);
            if (type == TravelType.Flight && frame != 3)
            {
                var root = pivot + new Vector2(-4, h * 0.37f);
                var tip = root + new Vector2(-12, frame == 1 ? 11 : frame == 2 ? -4 : 5);
                canvas.ThickLine(root, tip, 3.1f, Pal.Outline);
                canvas.ThickLine(root, tip, 1.9f, Pal.Hex("ead9b8"));
            }
            var center = new Vector2(w / 2f, h * 0.40f) - sprite.pivot;
            float radians = -pose.angle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            for (int y = 0; y < canvas.H; y++)
                for (int x = 0; x < canvas.W; x++)
                {
                    var delta = new Vector2(x + 0.5f, y + 0.5f) - pivot - center;
                    int sx = Mathf.FloorToInt((delta.x * cos - delta.y * sin) / pose.width + center.x + sprite.pivot.x);
                    int sy = Mathf.FloorToInt((delta.x * sin + delta.y * cos) / pose.height + center.y + sprite.pivot.y);
                    if ((uint)sx < (uint)w && (uint)sy < (uint)h && pixels[sy * w + sx].a > 0)
                        canvas.Px[y * canvas.W + x] = pixels[sy * w + sx];
                }
            return canvas.ToSprite(pivot);
        }
    }
}
