using UnityEngine;

namespace Diverse
{
    internal static class TravelSilhouette
    {
        static readonly Sprite[,] Frames = new Sprite[TravelTypes.All.Length, 8];

        public static Sprite Frame(TravelType type, float time)
        {
            int frame = Mathf.FloorToInt(Mathf.Repeat(time, 1.2f) * (8f / 1.2f));
            return Frames[(int)type, frame] ??= Draw(type, frame);
        }

        static Sprite Draw(TravelType type, int frame)
        {
            var canvas = new PixelCanvas(48, 32);
            var ink = Pal.Hex("d7cae6");
            var accent = TravelTypes.Color(type);
            float phase = frame / 7f;
            float arc = Mathf.Sin(phase * Mathf.PI);
            float stride = Mathf.Sin(frame * Mathf.PI / 2f);
            var center = new Vector2(12 + phase * 24, 11);
            canvas.Line(3, 5, 44, 5, Pal.Hex("625676"));
            switch (type)
            {
                case TravelType.Flight:
                    center.y += arc * 10;
                    canvas.ThickLine(center, center + new Vector2(-9, 3 + stride * 6), 2, ink);
                    break;
                case TravelType.Bounce:
                    center.y += arc * 12;
                    break;
                case TravelType.Burrow:
                    if (frame is > 0 and < 7)
                    {
                        canvas.Ellipse(center.x, 5, 6, 2, accent);
                        canvas.Set(Mathf.RoundToInt(center.x - 4), 8 + frame % 3, accent);
                        return canvas.ToSpriteCentered();
                    }
                    center.y -= 2;
                    break;
                case TravelType.Swim:
                    canvas.Line(3, 9, 44, 9, accent);
                    center.y -= arc * 2;
                    canvas.ThickLine(center, center + new Vector2(-8, stride * 2), 1.4f, ink);
                    break;
                case TravelType.Blink:
                    center.x = frame < 4 ? 12 : 36;
                    if (frame is 3 or 4)
                    {
                        canvas.Line((int)center.x, 8, (int)center.x, 22, accent);
                        return canvas.ToSpriteCentered();
                    }
                    break;
                default:
                    canvas.Line((int)center.x - 11, 11, (int)center.x - 6, 11, accent);
                    break;
            }
            bool swim = type == TravelType.Swim && frame is > 0 and < 7;
            canvas.Ellipse(center.x, center.y, swim ? 6 : 4, swim ? 2 : 4, ink);
            canvas.Circle(center.x + (swim ? 5 : 3), center.y + (swim ? 1 : 5), 3, ink);
            if (!swim)
            {
                float tuck = type == TravelType.Bounce ? arc * 3 : 0;
                canvas.ThickLine(center, center + new Vector2(-2 + stride * 2, -5 + tuck), 1, ink);
                canvas.ThickLine(center, center + new Vector2(2 - stride * 2, -5 + tuck), 1, ink);
            }
            return canvas.ToSpriteCentered();
        }
    }
}
