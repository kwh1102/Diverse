using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum Pose { Idle0, Idle1, Run0, Run1, Run2, Run3, Hurt, Cast }

    /// <summary>
    /// 모든 도트 스프라이트를 코드로 생성·캐시한다. 외부 PNG를 넣고 싶으면
    /// Resources/Sprites/&lt;key&gt;.png 를 두면 그쪽을 우선 사용한다(Load 참고).
    /// 기본 단위: 16px = 1 world unit.
    /// </summary>
    public static partial class Art
    {
        public const int PPU = 16;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite[]> anims = new Dictionary<string, Sprite[]>();

        static Material spriteMat, addMat;
        public static Material SpriteMat => spriteMat ??= new Material(Resources.Load<Shader>("Shaders/DiverseSprite")) { name = "DiverseSprite" };
        public static Material AddMat => addMat ??= new Material(Resources.Load<Shader>("Shaders/DiverseSpriteAdd")) { name = "DiverseAdd" };

        static Sprite whitePx;
        public static Sprite Pixel
        {
            get
            {
                if (whitePx != null) return whitePx;
                var c = new PixelCanvas(2, 2);
                c.Rect(0, 0, 2, 2, Color.white);
                return whitePx = c.ToSpriteCentered();
            }
        }

        public static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (cache.TryGetValue(key, out var s) && s != null) return s;
            var custom = Resources.Load<Sprite>("Sprites/" + key);
            s = custom != null ? custom : make();
            if (s != null) s.name = key;
            cache[key] = s;
            return s;
        }

        public static Sprite[] GetAnim(string key, System.Func<Sprite[]> make)
        {
            if (anims.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            anims[key] = s;
            return s;
        }

        public static SpriteRenderer MakeRenderer(GameObject go, Sprite s, int order = 0, bool additive = false)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sharedMaterial = additive ? AddMat : SpriteMat;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>y가 낮을수록(화면 아래) 앞에 그려지도록 정렬값 계산.</summary>
        public static int SortY(float y) => Mathf.Clamp(Mathf.RoundToInt(-y * 16f), -30000, 30000);

        // ───────────────────────────── 캐릭터 (토끼) ─────────────────────────────

        public static Sprite Rabbit(CostumeDef c, Pose pose) =>
            Get($"rabbit_{c.id}_{pose}", () => DrawRabbit(c.fur, c.furShade, c.outfit, c.outfitShade, c.accent, c.style, pose, false));

        /// <summary>노쇠한 과거 영웅: 털이 바래고 지팡이를 짚는다.</summary>
        public static Sprite ElderRabbit(CostumeDef c) =>
            Get($"elder_{c.id}", () =>
            {
                var fur = Pal.Lerp(c.fur, Pal.Hex("cfc8c0"), 0.6f);
                var shade = Pal.Lerp(c.furShade, Pal.Hex("9d948c"), 0.6f);
                return DrawRabbit(fur, shade, Pal.Mul(c.outfit, 0.75f), Pal.Mul(c.outfitShade, 0.75f), c.accent, c.style, Pose.Idle0, true);
            });

        static Sprite DrawRabbit(Color32 fur, Color32 furShade, Color32 outfit, Color32 outfitShade, Color32 accent, int style, Pose pose, bool elder)
        {
            var cv = new PixelCanvas(26, 30);
            int bob = 0, lf = 0, rf = 0, earSway = 0;
            switch (pose)
            {
                case Pose.Idle1: bob = -1; earSway = 1; break;
                case Pose.Run0: lf = 2; rf = -2; bob = 0; earSway = -1; break;
                case Pose.Run1: lf = 0; rf = 0; bob = 1; earSway = -2; break;
                case Pose.Run2: lf = -2; rf = 2; bob = 0; earSway = -1; break;
                case Pose.Run3: lf = 0; rf = 0; bob = 1; earSway = -2; break;
                case Pose.Hurt: bob = -1; earSway = 2; break;
                case Pose.Cast: bob = 0; earSway = -1; break;
            }
            if (elder) bob -= 1;
            int cx = 13;
            int y0 = 2;

            // 망토(뒤) — style 1,3,5
            if (style == 1 || style == 3 || style == 5)
            {
                for (int y = 0; y < 9; y++)
                {
                    int w = 3 + y / 3;
                    cv.Rect(cx - 4 - w + 2, y0 + 1 + y + bob, w, 1, y < 2 ? outfitShade : outfit);
                }
            }

            // 발
            cv.Ellipse(cx - 2 + lf, y0 + 1, 2.4f, 1.6f, furShade);
            cv.Ellipse(cx + 2 + rf, y0 + 1, 2.4f, 1.6f, fur);

            // 몸통 (옷)
            int by = y0 + 2 + bob;
            cv.Ellipse(cx, by + 3.5f, 4.6f, 4.2f, outfit);
            cv.Rect(cx - 4, by + 1, 9, 2, outfitShade);
            // 배 털
            cv.Ellipse(cx + 1, by + 3.2f, 2.0f, 2.2f, fur);
            // 벨트/장식
            if (style == 2 || style == 4) cv.Rect(cx - 4, by + 2, 9, 1, accent);
            if (style == 0 || style == 5) { cv.Rect(cx - 3, by + 6, 7, 2, accent); cv.Set(cx + 3, by + 5, accent); }
            if (style == 3) cv.Rect(cx - 1, by + 6, 3, 2, accent);

            // 앞 팔
            cv.Ellipse(cx + 3.5f, by + 3.5f, 1.6f, 1.6f, fur);

            // 머리
            int hy = by + 11;
            cv.Ellipse(cx + 0.5f, hy, 7.0f, 5.8f, fur);

            // 귀
            int earTop = elder ? 6 : 8;
            for (int e = 0; e < 2; e++)
            {
                float ex = e == 0 ? cx - 2.5f : cx + 2.5f;
                float lean = (e == 0 ? -0.5f : 0.6f) + earSway * 0.35f;
                for (int k = 0; k < earTop; k++)
                {
                    float t = k / (float)earTop;
                    float x = ex + lean * k * 0.6f;
                    float w = Mathf.Lerp(2.3f, 1.4f, t);
                    if (elder && e == 1 && k > 3) { x += (k - 3) * 0.9f; }
                    cv.Ellipse(x, hy + 4 + k, w, 1.2f, e == 0 ? furShade : fur);
                    if (k > 1 && k < earTop - 1) cv.Ellipse(x, hy + 4 + k, w * 0.38f, 0.8f, Pal.EarPink);
                }
            }

            // 모자 / 머리 장식
            if (style == 2) { cv.Ellipse(cx + 0.5f, hy + 4, 6.5f, 2.2f, outfitShade); cv.Rect(cx - 5, hy + 3, 12, 1, accent); }
            if (style == 4) { cv.Ellipse(cx - 3f, hy + 5, 2.2f, 1.6f, accent); cv.Ellipse(cx - 0.5f, hy + 5, 1.2f, 1.2f, Pal.Mul(accent, 0.8f)); }
            if (style == 5) { cv.Rect(cx - 3, hy + 5, 7, 2, Pal.Gold); cv.Set(cx - 3, hy + 7, Pal.Gold); cv.Set(cx, hy + 7, Pal.Gold); cv.Set(cx + 3, hy + 7, Pal.Gold); cv.Set(cx, hy + 6, Pal.Blood); }

            // 얼굴
            int ey = hy - 1;
            if (pose == Pose.Hurt)
            {
                cv.Line(cx + 1, ey + 2, cx + 3, ey, Pal.EyeDark); cv.Line(cx + 1, ey, cx + 3, ey + 2, Pal.EyeDark);
                cv.Line(cx + 5, ey + 2, cx + 6, ey, Pal.EyeDark);
            }
            else if (elder)
            {
                cv.Rect(cx + 1, ey + 1, 2, 1, Pal.EyeDark); cv.Rect(cx + 5, ey + 1, 2, 1, Pal.EyeDark);
                cv.Rect(cx + 1, ey - 3, 6, 2, Pal.Hex("f2ece4")); // 수염
            }
            else
            {
                cv.Rect(cx + 1, ey, 2, 3, Pal.EyeDark); cv.Set(cx + 1, ey + 2, Pal.White);
                cv.Rect(cx + 5, ey, 2, 3, Pal.EyeDark); cv.Set(cx + 5, ey + 2, Pal.White);
            }
            cv.Set(cx + 4, ey - 1, Pal.Hex("e07a8a"));
            cv.Rect(cx - 1, ey - 2, 2, 1, Pal.A(Pal.Blush, 200));
            cv.Rect(cx + 6, ey - 2, 2, 1, Pal.A(Pal.Blush, 200));

            // 스카프(목) — style 0,1
            if (style == 0 || style == 1) { cv.Rect(cx - 4, by + 7, 9, 2, accent); cv.Rect(cx - 5, by + 5, 2, 3, accent); }

            // 그림자 쪽 음영
            cv.InnerShade(fur, furShade, 1, -1);
            cv.InnerShade(outfit, outfitShade, 1, -1);
            cv.Outline(Pal.Outline);
            if (elder)
            {
                cv.Line(cx + 7, 0, cx + 7, 14, Pal.Wood);
                cv.Ellipse(cx + 7, 14, 1.4f, 1.4f, Pal.WoodDark);
            }
            return cv.ToSprite(new Vector2(13, 1));
        }

        /// <summary>손 위치 (발 피벗 기준, world unit). 무기를 붙이는 기준점.</summary>
        public static readonly Vector2 HandAnchor = new Vector2(3.5f / PPU, 7.5f / PPU);

        public static Sprite Shadow(int w) => Get($"shadow_{w}", () =>
        {
            int h = Mathf.Max(3, w / 3);
            var cv = new PixelCanvas(w, h);
            cv.Ellipse(w / 2f, h / 2f, w / 2f, h / 2f, Pal.Shadow);
            return cv.ToSpriteCentered();
        });

        // ───────────────────────────── 무기 ─────────────────────────────
        // 모두 오른쪽(+x)을 향하고 피벗은 손잡이.

        public static Sprite Weapon(WeaponKind k) => Get($"weapon_{k}", () =>
        {
            switch (k)
            {
                case WeaponKind.Greatsword:
                {
                    var cv = new PixelCanvas(34, 10);
                    cv.Rect(0, 4, 6, 2, Pal.WoodDark);           // 손잡이
                    cv.Rect(1, 4, 1, 2, Pal.Gold);
                    cv.Rect(6, 1, 2, 8, Pal.GoldDark);           // 가드
                    cv.Rect(6, 2, 2, 6, Pal.Gold);
                    for (int x = 8; x < 33; x++)
                    {
                        int half = x > 28 ? Mathf.Max(0, 32 - x) : 3;
                        cv.Rect(x, 5 - half, 1, half * 2, Pal.Steel);
                        cv.Set(x, 5 - half, Pal.SteelShade);
                    }
                    cv.Rect(9, 5, 18, 1, Pal.White);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(3, 5));
                }
                case WeaponKind.SwordShield:
                {
                    var cv = new PixelCanvas(22, 8);
                    cv.Rect(0, 3, 4, 2, Pal.WoodDark);
                    cv.Rect(4, 1, 2, 6, Pal.Gold);
                    for (int x = 6; x < 21; x++)
                    {
                        int half = x > 18 ? Mathf.Max(0, 20 - x) : 1;
                        cv.Rect(x, 4 - half, 1, half * 2 + 1, Pal.Steel);
                    }
                    cv.Rect(7, 4, 11, 1, Pal.White);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(2, 4));
                }
                case WeaponKind.Crossbow:
                {
                    var cv = new PixelCanvas(18, 16);
                    cv.Rect(0, 6, 14, 3, Pal.Wood);            // 개머리
                    cv.Rect(0, 6, 14, 1, Pal.WoodDark);
                    cv.Rect(3, 4, 2, 3, Pal.WoodDark);         // 손잡이
                    for (int i = 0; i < 7; i++)                 // 활대
                    {
                        cv.Set(13 - i / 3, 8 + i, Pal.SteelShade);
                        cv.Set(13 - i / 3, 7 - i, Pal.SteelShade);
                    }
                    cv.Line(11, 14, 6, 7, Pal.White);
                    cv.Line(11, 1, 6, 7, Pal.White);
                    cv.Rect(8, 7, 9, 1, Pal.Steel);            // 볼트
                    cv.Set(17, 7, Pal.SteelDark);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(4, 7));
                }
                case WeaponKind.Staff:
                {
                    var cv = new PixelCanvas(32, 10);
                    cv.Rect(0, 4, 24, 2, Pal.Wood);
                    cv.Rect(0, 4, 24, 1, Pal.WoodDark);
                    cv.Rect(8, 4, 1, 2, Pal.Gold);
                    cv.Rect(16, 4, 1, 2, Pal.Gold);
                    cv.Circle(27, 5, 4, Pal.Hex("7b5cff"));
                    cv.Circle(27, 5, 2.4f, Pal.Hex("c9b8ff"));
                    cv.Set(26, 6, Pal.White);
                    cv.Line(23, 1, 25, 3, Pal.Gold); cv.Line(23, 9, 25, 7, Pal.Gold);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(10, 5));
                }
                case WeaponKind.Dagger:
                {
                    var cv = new PixelCanvas(14, 7);
                    cv.Rect(0, 2, 3, 2, Pal.WoodDark);
                    cv.Rect(3, 1, 1, 5, Pal.Gold);
                    for (int x = 4; x < 13; x++)
                    {
                        int half = x > 10 ? 0 : 1;
                        cv.Rect(x, 3 - half, 1, half * 2 + 1, Pal.Steel);
                    }
                    cv.Set(5, 3, Pal.White); cv.Rect(5, 3, 5, 1, Pal.White);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(1, 3));
                }
                default: // Katana
                {
                    var cv = new PixelCanvas(30, 9);
                    cv.Rect(0, 3, 6, 2, Pal.Hex("3a2a4a"));
                    for (int i = 0; i < 6; i += 2) cv.Set(i, 4, Pal.Hex("d9c4f0"));
                    cv.Ellipse(7, 4, 1.4f, 2.6f, Pal.Gold);
                    for (int x = 8; x < 29; x++)
                    {
                        float t = (x - 8) / 20f;
                        int y = 3 + Mathf.RoundToInt(t * t * 3f);
                        cv.Rect(x, y, 1, 2, Pal.Steel);
                        cv.Set(x, y + 1, Pal.White);
                    }
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(3, 4));
                }
            }
        });

        public static Sprite Shield() => Get("shield", () =>
        {
            var cv = new PixelCanvas(12, 14);
            cv.Ellipse(6, 8, 5, 6, Pal.SteelShade);
            cv.Rect(1, 6, 10, 7, Pal.SteelShade);
            cv.Ellipse(6, 8, 3.8f, 4.8f, Pal.Hex("4f7bd9"));
            cv.Rect(5, 3, 2, 9, Pal.Gold); cv.Rect(3, 8, 6, 2, Pal.Gold);
            cv.Outline(Pal.Outline);
            return cv.ToSpriteCentered();
        });

        // ───────────────────────────── 투사체 ─────────────────────────────

        public static Sprite Bolt() => Get("bolt", () =>
        {
            var cv = new PixelCanvas(12, 5);
            cv.Rect(0, 2, 9, 1, Pal.Wood);
            cv.Rect(0, 1, 2, 3, Pal.Hex("e8e2d6"));
            cv.Rect(9, 1, 2, 3, Pal.Steel); cv.Set(11, 2, Pal.Steel);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(6, 2.5f));
        });

        public static Sprite Knife() => Get("knife", () =>
        {
            var cv = new PixelCanvas(9, 5);
            cv.Rect(0, 2, 3, 1, Pal.WoodDark);
            cv.Rect(3, 1, 5, 3, Pal.Steel); cv.Set(8, 2, Pal.Steel); cv.Rect(3, 2, 5, 1, Pal.White);
            cv.Outline(Pal.Outline);
            return cv.ToSpriteCentered();
        });

        public static Sprite Orb(Color32 core, Color32 glow, int size = 10) => Get($"orb_{core}_{glow}_{size}", () =>
        {
            var cv = new PixelCanvas(size, size);
            float c = size / 2f;
            cv.Circle(c, c, size / 2f, Pal.A(glow, 120));
            cv.Circle(c, c, size / 2f - 1.5f, glow);
            cv.Circle(c, c, size / 4f, core);
            cv.Set((int)c - 1, (int)c + 1, Pal.White);
            return cv.ToSpriteCentered();
        });

        public static Sprite EnemyShot(Color32 col) => Get($"eshot_{col}", () =>
        {
            var cv = new PixelCanvas(8, 8);
            cv.Circle(4, 4, 3.6f, Pal.Outline);
            cv.Circle(4, 4, 2.8f, col);
            cv.Circle(3.5f, 4.5f, 1.2f, Pal.White);
            return cv.ToSpriteCentered();
        });
    }
}
