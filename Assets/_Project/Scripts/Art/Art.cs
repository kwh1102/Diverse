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
            using (SpriteMarker.Auto()) s = custom != null ? custom : make();
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

        public static Color32 PaletteColor(Color32 color)
        {
            var p = TileBaker.PaletteColor(TileBaker.Snap(color)); p.a = color.a; return p;
        }

        /// <summary>Editor bake: draw from code even where a PNG exists in Resources/Sprites (to redraw it).</summary>
        public static bool DrawOnly;

        public static void Override(string key, Sprite s) { if (s == null) overrides.Remove(key); else overrides[key] = s; }

        /// <summary>A new design pack was activated: drop every cached sprite and override so they are drawn again with its look.</summary>
        public static void ResetLooks()
        {
            groundTableReady = false;
            cache.Clear();
            anims.Clear();
            overrides.Clear();
            enemyFrames.Clear();
            rabbitPoses.Clear();
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
            if (p[i] != null) return p[i];
            var sprite = Get($"rabbit_{c.id}_{pose}", () => DrawCostumePose(c, pose));
            return p[i] = (pose is Pose.Idle0 or Pose.Idle1) ? CleanIdleEyes(sprite, pose) : sprite;
        }

        static Sprite CleanIdleEyes(Sprite sprite, Pose pose)
        {
            // The imported 36x34 idle frame has an L-shaped right eye after downsampling.
            // Finish the native pixel face once per cached pose; other sizes/custom pictures are untouched.
            if (sprite == null || sprite.rect.width != 36 || sprite.rect.height != 34 || !sprite.texture.isReadable) return sprite;
            int width = 36, height = 34; var cv = new PixelCanvas(width, height);
            var source = sprite.texture.GetPixels32(); int sx = (int)sprite.rect.x, sy = (int)sprite.rect.y;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                cv.Px[y * width + x] = source[(sy + y) * sprite.texture.width + sx + x];
            var leftEye = cv.Px[13 * width + 19]; var rightEye = cv.Px[13 * width + 24]; var face = cv.Px[13 * width + 22];
            if (leftEye.a == 0 || rightEye.a == 0 || leftEye.r > 90 || leftEye.g > 90 || leftEye.b > 100 ||
                rightEye.r > 90 || rightEye.g > 90 || rightEye.b > 100 ||
                face.r + face.g + face.b < leftEye.r + leftEye.g + leftEye.b + 24) return sprite;
            for (int y = 12; y <= 13; y++)
            {
                var fur = cv.Px[y * width + 22];
                cv.Rect(19, y, 2, 1, fur); cv.Rect(24, y, 2, 1, fur);
            }
            if (pose == Pose.Idle0)
            { cv.Rect(20, 12, 1, 2, OutlineColor); cv.Rect(25, 12, 1, 2, OutlineColor); }
            else
            { cv.Rect(19, 12, 2, 1, OutlineColor); cv.Rect(24, 12, 2, 1, OutlineColor); }
            var result = cv.ToSprite(sprite.pivot); result.name = sprite.name; return result;
        }

        static Sprite DrawCostumePose(CostumeDef c, Pose pose)
        {
            if (c.frames != null) return DesignArt.HeroPose(c.frames, pose, false);
            // A saved custom life has a unique d:<pack> ID even when its hero has no visual customization.
            // Resolve that default appearance through the same imported base sprites as a new classic life.
            var h = c.look;
            bool baseLook = h != null && string.IsNullOrEmpty(h.image)
                && string.IsNullOrEmpty(h.fur) && string.IsNullOrEmpty(h.furShade)
                && string.IsNullOrEmpty(h.outfit) && string.IsNullOrEmpty(h.outfitShade) && string.IsNullOrEmpty(h.accent)
                && h.style < 0 && (string.IsNullOrEmpty(h.hat) || h.hat == "none")
                && (string.IsNullOrEmpty(h.species) || h.species == "토끼" || h.species == "rabbit");
            if (!DrawOnly && baseLook)
            {
                string baseId = DB.Costumes.Find(x => x.id == h.basePerk)?.id ?? DB.Costumes[0].id;
                if (Hand().TryGetValue($"rabbit_{baseId}_{pose}", out var basis))
                {
                    // Art.Get names its result after the custom key. Clone the sprite wrapper so the imported
                    // base asset keeps its name; texture, exact pixels, pivot and PPU remain shared and unchanged.
                    var rect = basis.rect;
                    return Sprite.Create(basis.texture, rect, new Vector2(basis.pivot.x / rect.width, basis.pivot.y / rect.height), basis.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                }
            }
            return DrawRabbit(c.fur, c.furShade, c.outfit, c.outfitShade, c.accent, c.style, pose, false, c.look);
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

        // ───────────────────────────── 적 ─────────────────────────────

        // Called every frame by every enemy: a per-key frame array so no lookup string is built per call
        static readonly Dictionary<string, Sprite[]> enemyFrames = new Dictionary<string, Sprite[]>();

        /// <summary>
        /// Enemy sprite from Resources/Sprites/enemy_&lt;key&gt;_&lt;frame&gt;.png: 0 idle, 1·4 the two steps, 2 wind-up, 3 hurt.
        /// Enemies have no code-drawn fallback: a missing frame shows the idle frame.
        /// </summary>
        public static Sprite Enemy(string key, int frame)
        {
            if (!enemyFrames.TryGetValue(key, out var f)) enemyFrames[key] = f = new Sprite[8];
            if (frame < 0 || frame >= f.Length) return EnemyPng(key, frame);
            return f[frame] != null ? f[frame] : f[frame] = EnemyPng(key, frame);
        }

        static Sprite EnemyPng(string key, int frame)
        {
            if (Hand().TryGetValue($"enemy_{key}_{frame}", out var s) || Hand().TryGetValue($"enemy_{key}_0", out s)) return s;
            Debug.LogWarning($"[Art] Resources/Sprites/enemy_{key}_{frame}.png is missing");
            return null;
        }

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
