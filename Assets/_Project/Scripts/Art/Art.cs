using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Diverse
{
    public enum Pose { Idle0, Idle1, Run0, Run1, Run2, Run3, Hurt, Cast, AttackWind, AttackHit }

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

        /// <summary>Sprites from the active design pack (DesignRuntime) that replace a key outright.</summary>
        static readonly Dictionary<string, Sprite> overrides = new Dictionary<string, Sprite>();
        static readonly ProfilerMarker SpriteMarker = new ProfilerMarker("Diverse.SpriteMake");   // first-time sprite drawing (read by PerfProbe)

        public static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (overrides.TryGetValue(key, out var o)) return o;
            if (cache.TryGetValue(key, out var s) && s != null) return s;
            Sprite custom = null;
            if (!DrawOnly) Hand().TryGetValue(key, out custom);
            HitchLog.SpritesMade++;
            using (SpriteMarker.Auto())
            {
                var drawn = custom != null ? custom : make();
                s = DesignRuntime.Restyle(key, drawn);
                if (s != null && s != drawn) s = KeepPalette(s);     // a custom-mode recolour or picture still uses only the palette
            }
            if (s != null) s.name = key;
            cache[key] = s;
            return s;
        }

        /// <summary>
        /// Hand-drawn sprites in Resources/Sprites (they win over the code-drawn ones), loaded together once. Looking each
        /// one up with Resources.Load on first use read it from disk mid-fight (a 20+ ms hitch per new enemy or chunk).
        /// </summary>
        static Dictionary<string, Sprite> hand;
        static Dictionary<string, Sprite> Hand()
        {
            if (hand != null) return hand;
            hand = new Dictionary<string, Sprite>();
            foreach (var sp in Resources.LoadAll<Sprite>("Sprites")) hand[sp.name] = sp;
            return hand;
        }

        /// <summary>Load the hand-drawn sprites now (scene start) instead of on the first sprite that needs one.</summary>
        public static void Preload() => Hand();

        /// <summary>Editor bake: draw from code even where a PNG exists in Resources/Sprites (to redraw it).</summary>
        public static bool DrawOnly;

        public static void Override(string key, Sprite s) { if (s == null) overrides.Remove(key); else overrides[key] = s; }

        /// <summary>A new design pack was activated: drop every cached sprite and override so they are drawn again with its look.</summary>
        public static void ResetLooks()
        {
            cache.Clear();
            anims.Clear();
            overrides.Clear();
            enemyFrames.Clear();
            rabbitPoses.Clear();
        }

        /// <summary>Snap a sprite's colours to the art rules' palette (DB.ArtStyle), keeping its rect and pivot.</summary>
        public static Sprite KeepPalette(Sprite s)
        {
            var tex = s.texture;
            if (tex == null || !tex.isReadable || DB.ArtStyle.palette.Length == 0) return s;
            var r = s.rect;
            var px = tex.GetPixels32();
            int x0 = (int)r.x, y0 = (int)r.y, w = (int)r.width, h = (int)r.height;
            var outPx = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[(y0 + y) * tex.width + x0 + x];
                    if (c.a < 128) { outPx[y * w + x] = default; continue; }
                    var p = TileBaker.PaletteColor(TileBaker.Snap(c));
                    outPx[y * w + x] = new Color32(p.r, p.g, p.b, 255);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(outPx);
            t.Apply(false, false);
            var o = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(s.pivot.x / w, s.pivot.y / h), s.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            o.name = s.name;
            return o;
        }

        public static Sprite[] GetAnim(string key, System.Func<Sprite[]> make)
        {
            if (anims.TryGetValue(key, out var s) && s != null) return s;
            HitchLog.SpritesMade++;
            using (SpriteMarker.Auto()) s = make();
            anims[key] = s;
            return s;
        }

        public static SpriteRenderer MakeRenderer(GameObject go, Sprite s, int order = 0, bool additive = false) =>
            Setup(go.AddComponent<SpriteRenderer>(), s, order, additive);

        /// <summary>Configure a renderer that already exists (prefab child).</summary>
        public static SpriteRenderer Setup(SpriteRenderer sr, Sprite s, int order = 0, bool additive = false)
        {
            sr.sprite = s;
            sr.sharedMaterial = additive ? AddMat : SpriteMat;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>y가 낮을수록(화면 아래) 앞에 그려지도록 정렬값 계산.</summary>
        public static int SortY(float y) => Mathf.Clamp(Mathf.RoundToInt(-y * 16f), -30000, 30000);

        // ───────────────────────────── 캐릭터 (토끼) ─────────────────────────────

        // Called every frame for the hero: a per-costume pose array so no lookup string is built per call
        static readonly Dictionary<CostumeDef, Sprite[]> rabbitPoses = new Dictionary<CostumeDef, Sprite[]>();

        public static Sprite Rabbit(CostumeDef c, Pose pose)
        {
            if (!rabbitPoses.TryGetValue(c, out var p)) rabbitPoses[c] = p = new Sprite[System.Enum.GetValues(typeof(Pose)).Length];
            int i = (int)pose;
            return p[i] != null ? p[i] : p[i] = Get($"rabbit_{c.id}_{pose}", () => c.frames != null ? DesignArt.HeroPose(c.frames, pose, false)
                : DrawRabbit(c.fur, c.furShade, c.outfit, c.outfitShade, c.accent, c.style, pose, false, c.look));
        }

        /// <summary>노쇠한 과거 영웅: 털이 바래고 지팡이를 짚는다.</summary>
        public static Sprite ElderRabbit(CostumeDef c) =>
            Get($"elder_{c.id}", () =>
            {
                if (c.frames != null) return DesignArt.HeroPose(c.frames, Pose.Idle0, true);
                var fur = Pal.Lerp(c.fur, Pal.Hex("cfc8c0"), 0.6f);
                var shade = Pal.Lerp(c.furShade, Pal.Hex("9d948c"), 0.6f);
                return DrawRabbit(fur, shade, Pal.Mul(c.outfit, 0.75f), Pal.Mul(c.outfitShade, 0.75f), c.accent, c.style, Pose.Idle0, true, c.look);
            });

        /// <summary>손 위치 (발 피벗 기준, world unit). 무기를 붙이는 기준점.</summary>
        public static readonly Vector2 HandAnchor = new Vector2(5f / PPU, 5.5f / PPU);

        public static Sprite Shadow(int w) => Get($"shadow_{w}", () =>
        {
            int h = Mathf.Max(3, w / 3);
            var cv = new PixelCanvas(w, h);
            var sc = Pi(DB.ArtStyle.shadow); sc.a = (byte)(DB.ArtStyle.shadowAlpha * 255);
            cv.Ellipse(w / 2f, h / 2f, w / 2f, h / 2f, sc);
            return cv.ToSpriteCentered();
        });

        public static Sprite Orb(Color32 core, Color32 glow, int size = 10) => Get($"orb_{core}_{glow}_{size}", () =>
        {
            var cv = new PixelCanvas(size, size);
            float c = size / 2f;
            glow = Pc(glow); core = Pc(core);
            cv.Circle(c, c, size / 2f, Pal.A(glow, 120));
            cv.Circle(c, c, size / 2f - 1.5f, glow);
            cv.Circle(c, c, size / 4f, core);
            cv.Set((int)c - 1, (int)c + 1, Pi(10));
            return cv.ToSpriteCentered();
        });

        public static Sprite EnemyShot(Color32 col) => Get($"eshot_{col}", () =>
        {
            var cv = new PixelCanvas(8, 8);
            cv.Circle(4, 4, 3.6f, OutlineColor);
            cv.Circle(4, 4, 2.8f, Pc(col));
            cv.Circle(3.5f, 4.5f, 1.2f, Pi(10));
            return cv.ToSpriteCentered();
        });
    }
}
