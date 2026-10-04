using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 도트 이펙트 프레임 생성. 세피리아식 "두꺼운 초승달 궤적 + 흰 코어 + 끝단 픽셀 부스러기" 스타일.
    /// 모든 이펙트는 오른쪽(+x)을 향하고, 회전시켜 사용한다.
    /// </summary>
    public static partial class Art
    {
        public const int SlashFrames = 5;

        /// <summary>베기 궤적. arc(도)·반경(px)·방향(1/-1)에 따라 프레임마다 쓸고 지나가며 사라진다.</summary>
        public static Sprite[] Slash(Color32 col, Color32 core, int radiusPx, float arc, float swingDir) =>
            GetAnim($"slash_{col}_{core}_{radiusPx}_{(int)arc}_{swingDir}", () =>
            {
                var frames = new Sprite[SlashFrames];
                int S = radiusPx * 2 + 6;
                float c = S / 2f;
                float half = arc / 2f;
                for (int f = 0; f < SlashFrames; f++)
                {
                    var cv = new PixelCanvas(S, S);
                    // 앞 끝(head)이 빠르게 진행하고, 꼬리(tail)가 따라오며 사라진다
                    float headT = Mathf.Clamp01((f + 1) / 2.5f);
                    float tailT = Mathf.Clamp01((f - 0.5f) / 3.5f);
                    float a0 = swingDir > 0 ? half - arc * tailT : -half + arc * tailT;
                    float a1 = swingDir > 0 ? half - arc * headT : -half + arc * headT;
                    float lo = Mathf.Min(a0, a1), hi = Mathf.Max(a0, a1);
                    float thickBase = Mathf.Max(3, radiusPx * 0.34f) * (1f - f * 0.17f);
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x + 0.5f - c, dy = y + 0.5f - c;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                            if (ang < lo || ang > hi) continue;
                            // 진행 방향 끝에 가까울수록 두껍게 (초승달)
                            float along = swingDir > 0 ? (hi - ang) / Mathf.Max(1, hi - lo) : (ang - lo) / Mathf.Max(1, hi - lo);
                            float th = thickBase * Mathf.Lerp(0.25f, 1f, Mathf.Sqrt(1 - along));
                            float inner = radiusPx - th;
                            if (d > radiusPx || d < inner) continue;
                            float rel = (d - inner) / Mathf.Max(1, th);
                            Color32 px = rel > 0.55f ? core : col;
                            if (rel < 0.18f) px = Pal.A(col, 170);
                            if (f >= 3 && Hash.F(x, y, f) > 0.55f) continue;   // 사라질 때 디더링
                            cv.Set(x, y, px);
                        }
                    // 끝단 부스러기
                    if (f >= 1)
                    {
                        var rng = new Rng((uint)(f * 97 + radiusPx));
                        for (int i = 0; i < 4 + f; i++)
                        {
                            float ang = Mathf.Lerp(lo, hi, rng.Value) * Mathf.Deg2Rad;
                            float r = radiusPx + rng.Range(0f, 3f) + f;
                            cv.Set((int)(c + Mathf.Cos(ang) * r), (int)(c + Mathf.Sin(ang) * r), f < 3 ? core : col);
                        }
                    }
                    frames[f] = cv.ToSprite(new Vector2(c, c));
                }
                return frames;
            });

        /// <summary>찌르기 궤적 (가로로 길게 뻗는 창 모양).</summary>
        public static Sprite[] Thrust(Color32 col, Color32 core, int lengthPx) =>
            GetAnim($"thrust_{col}_{core}_{lengthPx}", () =>
            {
                var frames = new Sprite[SlashFrames];
                int W = lengthPx + 8, H = 14;
                for (int f = 0; f < SlashFrames; f++)
                {
                    var cv = new PixelCanvas(W, H);
                    float reach = Mathf.Clamp01((f + 1) / 2f);
                    float fade = Mathf.Clamp01((f - 1) / 3f);
                    int x0 = Mathf.RoundToInt(lengthPx * fade * 0.8f);
                    int x1 = Mathf.RoundToInt(lengthPx * reach);
                    for (int x = x0; x < x1; x++)
                    {
                        float t = x / (float)lengthPx;
                        int half = Mathf.RoundToInt(Mathf.Lerp(1, 4, Mathf.Sin(t * Mathf.PI)) * (1 - fade * 0.6f));
                        cv.Rect(x, 7 - half, 1, half * 2, col);
                        if (half > 1) cv.Rect(x, 7 - half / 2, 1, Mathf.Max(1, half), core);
                    }
                    // 끝 반짝임
                    if (f <= 2) { cv.Rect(x1, 6, 3, 2, core); cv.Set(x1 + 3, 7, col); cv.Set(x1 - 1, 9, col); cv.Set(x1 - 1, 4, col); }
                    frames[f] = cv.ToSprite(new Vector2(2, 7));
                }
                return frames;
            });

        /// <summary>피격 스파크: 방사형 별 + 사라지는 십자.</summary>
        public static Sprite[] HitSpark(Color32 col, int sizePx) =>
            GetAnim($"hit_{col}_{sizePx}", () =>
            {
                var frames = new Sprite[4];
                int S = sizePx * 2 + 2;
                float c = S / 2f;
                for (int f = 0; f < 4; f++)
                {
                    var cv = new PixelCanvas(S, S);
                    float r = sizePx * (0.45f + f * 0.2f);
                    int rays = 8;
                    for (int i = 0; i < rays; i++)
                    {
                        float a = i * Mathf.PI * 2 / rays + (i % 2) * 0.2f;
                        float len = r * (i % 2 == 0 ? 1f : 0.6f);
                        float start = f * sizePx * 0.18f;
                        var p0 = new Vector2(c + Mathf.Cos(a) * start, c + Mathf.Sin(a) * start);
                        var p1 = new Vector2(c + Mathf.Cos(a) * len, c + Mathf.Sin(a) * len);
                        cv.Line((int)p0.x, (int)p0.y, (int)p1.x, (int)p1.y, f < 2 ? Pal.White : col);
                    }
                    if (f == 0) { cv.Circle(c, c, sizePx * 0.45f, Pal.White); cv.Circle(c, c, sizePx * 0.25f, col); }
                    if (f == 1) cv.Ring(c, c, sizePx * 0.6f, 1.2f, col);
                    frames[f] = cv.ToSprite(new Vector2(c, c));
                }
                return frames;
            });

        /// <summary>원형 충격파 (지면 내려찍기, 폭발).</summary>
        public static Sprite[] Shockwave(Color32 col, int radiusPx) =>
            GetAnim($"shock_{col}_{radiusPx}", () =>
            {
                var frames = new Sprite[5];
                int S = radiusPx * 2 + 4;
                float c = S / 2f;
                for (int f = 0; f < 5; f++)
                {
                    var cv = new PixelCanvas(S, S);
                    float r = radiusPx * (0.35f + f * 0.165f);
                    float th = Mathf.Max(1.2f, radiusPx * 0.22f * (1 - f * 0.18f));
                    // 아이소메트릭 느낌으로 납작한 링
                    for (int y = 0; y < S; y++)
                        for (int x = 0; x < S; x++)
                        {
                            float dx = x + 0.5f - c, dy = (y + 0.5f - c) / 0.62f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d <= r && d >= r - th)
                            {
                                if (f >= 3 && Hash.F(x, y, f + 11) > 0.6f) continue;
                                cv.Set(x, y, d > r - th * 0.45f ? Pal.White : col);
                            }
                            else if (f == 0 && d < r * 0.6f) cv.Set(x, y, Pal.A(col, 130));
                        }
                    frames[f] = cv.ToSprite(new Vector2(c, c));
                }
                return frames;
            });

        /// <summary>대시 먼지 구름.</summary>
        public static Sprite[] Dust() => GetAnim("dust", () =>
        {
            var frames = new Sprite[5];
            var col = Pal.Hex("f2e6d6"); var sh = Pal.Hex("cdbba5");
            for (int f = 0; f < 5; f++)
            {
                var cv = new PixelCanvas(14, 12);
                float r = 2.5f + f * 0.9f;
                cv.Circle(5, 4 + f * 0.6f, r * (1 - f * 0.12f), sh);
                cv.Circle(9, 5 + f * 0.7f, r * 0.8f * (1 - f * 0.12f), col);
                cv.Circle(6, 6 + f * 0.8f, r * 0.7f * (1 - f * 0.15f), col);
                if (f >= 3) for (int i = 0; i < cv.Px.Length; i++) if (Hash.F(i, f, 3) > 0.55f) cv.Px[i] = default;
                frames[f] = cv.ToSprite(new Vector2(7, 2));
            }
            return frames;
        });

        /// <summary>번개 줄기 (세로, 위에서 아래로).</summary>
        public static Sprite[] LightningBolt(int heightPx) => GetAnim($"bolt_{heightPx}", () =>
        {
            var frames = new Sprite[4];
            for (int f = 0; f < 4; f++)
            {
                var cv = new PixelCanvas(22, heightPx);
                var rng = new Rng((uint)(f * 131 + 5));
                int x = 11;
                for (int y = heightPx - 1; y >= 0; y -= 3)
                {
                    int nx = Mathf.Clamp(x + rng.Range(-3, 4), 3, 18);
                    var core = f < 2 ? Pal.White : Pal.Lightning;
                    cv.ThickLine(new Vector2(x, y), new Vector2(nx, y - 3), f < 2 ? 1.6f : 0.8f, Pal.Lightning);
                    cv.Line(x, y, nx, y - 3, core);
                    if (rng.Chance(0.15f)) cv.Line(nx, y - 3, nx + rng.Range(-6, 7), y - 8, Pal.Lightning);
                    x = nx;
                }
                if (f < 2) cv.Ellipse(11, 2, 9, 3, Pal.A(Pal.Lightning, 180));
                frames[f] = cv.ToSprite(new Vector2(11, 2));
            }
            return frames;
        });

        /// <summary>작은 사각 파티클 (2x2, 3x3).</summary>
        public static Sprite Particle(int size) => Get($"particle_{size}", () =>
        {
            var cv = new PixelCanvas(size, size);
            cv.Rect(0, 0, size, size, Color.white);
            return cv.ToSpriteCentered();
        });

        public static Sprite Diamond(int size) => Get($"diamond_{size}", () =>
        {
            var cv = new PixelCanvas(size, size);
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                if (Mathf.Abs(x - c) + Mathf.Abs(y - c) <= c + 0.01f) cv.Set(x, y, Color.white);
            return cv.ToSpriteCentered();
        });

        /// <summary>바닥 경고 원 (적 공격 예고).</summary>
        public static Sprite WarningCircle(int radiusPx) => Get($"warn_{radiusPx}", () =>
        {
            int S = radiusPx * 2 + 2;
            var cv = new PixelCanvas(S, S);
            float c = S / 2f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x + 0.5f - c, dy = (y + 0.5f - c) / 0.62f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= radiusPx) cv.Set(x, y, d > radiusPx - 1.5f ? new Color32(255, 255, 255, 230) : new Color32(255, 255, 255, 70));
                }
            return cv.ToSpriteCentered();
        });

        /// <summary>우클릭 이동 지점 표시.</summary>
        public static Sprite[] ClickMarker() => GetAnim("click", () =>
        {
            var frames = new Sprite[4];
            for (int f = 0; f < 4; f++)
            {
                var cv = new PixelCanvas(16, 12);
                float r = 7 - f * 1.4f;
                for (int y = 0; y < 12; y++) for (int x = 0; x < 16; x++)
                {
                    float dx = x + 0.5f - 8, dy = (y + 0.5f - 6) / 0.6f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= r && d >= r - 1.3f) cv.Set(x, y, new Color32(140, 255, 160, 255));
                }
                frames[f] = cv.ToSpriteCentered();
            }
            return frames;
        });

        /// <summary>잔상용 실루엣 (원본 스프라이트를 단색으로).</summary>
        public static Sprite Silhouette(Sprite src)
        {
            if (src == null) return null;
            return Get("sil_" + src.name, () =>
            {
                var t = src.texture;
                var r = src.rect;
                var px = t.GetPixels32();
                var cv = new PixelCanvas((int)r.width, (int)r.height);
                for (int y = 0; y < cv.H; y++)
                    for (int x = 0; x < cv.W; x++)
                    {
                        var c = px[((int)r.y + y) * t.width + (int)r.x + x];
                        if (c.a > 0) cv.Set(x, y, Color.white);
                    }
                return cv.ToSprite(src.pivot);
            });
        }

        public static Sprite Ring(int radiusPx, float thickness) => Get($"ring_{radiusPx}_{thickness}", () =>
        {
            int S = radiusPx * 2 + 2;
            var cv = new PixelCanvas(S, S);
            cv.Ring(S / 2f, S / 2f, radiusPx, thickness, Color.white);
            return cv.ToSpriteCentered();
        });

        public static Sprite Disc(int radiusPx) => Get($"disc_{radiusPx}", () =>
        {
            int S = radiusPx * 2 + 2;
            var cv = new PixelCanvas(S, S);
            cv.Circle(S / 2f, S / 2f, radiusPx, Color.white);
            return cv.ToSpriteCentered();
        });

        /// <summary>둥근 빛 번짐 (가산 셰이더용, 알파 그라데이션을 도트 단계로).</summary>
        public static Sprite Glow(int radiusPx) => Get($"glow_{radiusPx}", () =>
        {
            int S = radiusPx * 2 + 2;
            var cv = new PixelCanvas(S, S);
            float c = S / 2f;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c)) / radiusPx;
                if (d > 1) continue;
                byte a = d < 0.33f ? (byte)160 : d < 0.66f ? (byte)90 : (byte)40;
                cv.Set(x, y, new Color32(255, 255, 255, a));
            }
            return cv.ToSpriteCentered();
        });
    }
}
