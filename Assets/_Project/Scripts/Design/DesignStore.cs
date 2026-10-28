using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Local storage for design packs: &lt;save dir&gt;/designs/&lt;id&gt;.json. A committed pack is immutable (an edit makes a new id),
    /// so graves and saved lives can always redraw the hero they had. The files are self-contained for a future upload.
    /// A hero drawn from a pack uses the costume id "d:&lt;pack id&gt;".
    /// </summary>
    public static class DesignStore
    {
        public const string CostumePrefix = "d:";
        static readonly Dictionary<string, DesignPack> packs = new Dictionary<string, DesignPack>();
        static readonly Dictionary<string, CostumeDef> costumes = new Dictionary<string, CostumeDef>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { packs.Clear(); costumes.Clear(); images.Clear(); }

        static string PathOf(string id) => Path.Combine(SaveSystem.DesignDir, id + ".json");

        public static DesignPack Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (packs.TryGetValue(id, out var p)) return p;
            try
            {
                var path = PathOf(id);
                if (!File.Exists(path)) return null;
                p = JsonUtility.FromJson<DesignPack>(File.ReadAllText(path));
                if (p == null) return null;
                p.id = id;
                DesignGuard.Sanitize(p);
                packs[id] = p;
                return p;
            }
            catch (Exception e) { Debug.LogWarning("Design pack load failed: " + e.Message); return null; }
        }

        /// <summary>Save an edited pack under a new id and return the stored copy.</summary>
        public static DesignPack Commit(DesignPack edited)
        {
            // Unchanged since it was loaded: reuse the stored pack (no new file every life)
            var stored = Load(edited.id);
            if (stored != null && JsonUtility.ToJson(stored) == JsonUtility.ToJson(edited)) return stored;
            var p = edited.Clone();
            p.parent = edited.id;
            p.id = DateTime.UtcNow.ToString("yyMMddHHmmss") + UnityEngine.Random.Range(0x100, 0xfff).ToString("x");
            p.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try
            {
                Directory.CreateDirectory(SaveSystem.DesignDir);
                File.WriteAllText(PathOf(p.id), JsonUtility.ToJson(p, true));
            }
            catch (Exception e) { Debug.LogWarning("Design pack save failed: " + e.Message); }
            packs[p.id] = p;
            return p;
        }

        /// <summary>The world's current look (default when it never had one).</summary>
        public static DesignPack OfWorld(WorldSave w) => (w != null ? Load(w.designId) : null) ?? DesignPack.Default();

        /// <summary>Only the world + enemy sections (a classic hero picked from the old character select).</summary>
        public static DesignPack WorldOnly(DesignPack p)
        {
            var c = DesignPack.Default();
            if (p == null) return c;
            c.CopySection(p, 2);
            c.CopySection(p, 3);
            c.summary = p.summary;
            return c;
        }

        public static DesignPack PackOfCostume(string costumeId) =>
            costumeId != null && costumeId.StartsWith(CostumePrefix) ? Load(costumeId.Substring(CostumePrefix.Length)) : null;

        /// <summary>Costume for a "d:&lt;id&gt;" costume id (null for normal costumes or a missing file).</summary>
        public static CostumeDef Costume(string costumeId)
        {
            if (costumeId == null || !costumeId.StartsWith(CostumePrefix)) return null;
            if (costumes.TryGetValue(costumeId, out var c)) return c;
            var p = PackOfCostume(costumeId);
            if (p == null) return null;
            return costumes[costumeId] = MakeCostume(p, costumeId);
        }

        public static CostumeDef CostumeOf(DesignPack committed) => Costume(CostumePrefix + committed.id);

        /// <summary>The generated starting passive was made on the first life: keep it with the (otherwise immutable) pack.</summary>
        public static void SavePerk(DesignPack p, AbilityRecord perk)
        {
            p.hero.perk = perk;
            costumes.Remove(CostumePrefix + p.id);
            try { File.WriteAllText(PathOf(p.id), JsonUtility.ToJson(p, true)); }
            catch (Exception e) { Debug.LogWarning("Design pack save failed: " + e.Message); }
        }

        /// <summary>Weapon name for graves / epitaphs (a designed hero's weapon, or the classic one).</summary>
        public static string WeaponName(string costumeId, int weapon) =>
            DesignCombat.For(PackOfCostume(costumeId))?.name ?? DB.W((WeaponKind)weapon).name;

        // ───────────────────────── Pictures (pixelized PNGs, designs/img/<id>.png) ─────────────────────────

        static readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();
        static string ImageDir => Path.Combine(SaveSystem.DesignDir, "img");

        public static string SaveImage(PixelCanvas cv)
        {
            var tex = cv.ToTexture();
            var id = Guid.NewGuid().ToString("N").Substring(0, 12);
            try
            {
                Directory.CreateDirectory(ImageDir);
                File.WriteAllBytes(Path.Combine(ImageDir, id + ".png"), tex.EncodeToPNG());
            }
            catch (Exception e) { Debug.LogWarning("Design image save failed: " + e.Message); }
            images[id] = tex;
            return id;
        }

        public static Texture2D Image(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (images.TryGetValue(id, out var t) && t != null) return t;
            var path = Path.Combine(ImageDir, id + ".png");
            if (!File.Exists(path)) return null;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (!t.LoadImage(File.ReadAllBytes(path))) return null;
            t.filterMode = FilterMode.Point;
            return images[id] = t;
        }

        /// <summary>A left-right mirrored copy of a stored picture (new id; pictures are immutable). null stays null.</summary>
        public static string Mirrored(string id)
        {
            var t = Image(id);
            if (t == null) return id;
            var src = t.GetPixels32();
            var cv = new PixelCanvas(t.width, t.height);
            for (int y = 0; y < t.height; y++)
                for (int x = 0; x < t.width; x++) cv.Px[y * t.width + x] = src[y * t.width + t.width - 1 - x];
            return SaveImage(cv);
        }

        /// <summary>Sprite from a stored picture. pivot is normalized (0..1).</summary>
        public static Sprite Sprite(string id, Vector2 pivot)
        {
            var t = Image(id);
            return t == null ? null : UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, Art.PPU, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Uncommitted preview: the id changes with the hero look so Art's sprite cache never shows a stale rabbit.</summary>
        public static CostumeDef PreviewCostume(DesignPack p) =>
            MakeCostume(p, "dp" + (JsonUtility.ToJson(p.hero).GetHashCode() & 0x7fffffff).ToString("x"));

        /// <summary>The hero's pose pictures (a sheet of 4, or the single picture of an older pack). null = draw the rabbit.</summary>
        static Texture2D[] HeroFrames(HeroLook h)
        {
            var idle = Image(h.image);
            if (idle == null) return null;
            if (h.frames == null || h.frames.Length < 4) return new[] { idle };
            return h.frames.Select(f => Image(f) ?? idle).ToArray();
        }

        static CostumeDef MakeCostume(DesignPack p, string id)
        {
            var h = p.hero;
            var b = DB.Costumes.Find(x => x.id == h.basePerk) ?? DB.Costumes[0];
            Color32 Col(string hex, Color32 fallback) => string.IsNullOrEmpty(hex) ? fallback : Pal.Hex(hex);
            int style = h.style >= 0 ? h.style : b.style;
            // Outfit styles 2/4/5 already put something on the head: a chosen hat replaces it
            if (h.hat != "none") style = style switch { 2 => 0, 4 => 0, 5 => 1, _ => style };
            var fur = Col(h.fur, b.fur);
            var outfit = Col(h.outfit, b.outfit);
            return new CostumeDef
            {
                id = id,
                name = h.name ?? b.name,
                // No stat perk: the hero starts with a generated passive instead (Game.StartingPerk)
                perk = h.perk != null && !string.IsNullOrEmpty(h.perk.name) ? "시작 능력: " + h.perk.name : "시작 능력: 첫 삶에서 생겨남",
                mods = null,
                tags = b.tags,
                fur = fur,
                furShade = Col(h.furShade, string.IsNullOrEmpty(h.fur) ? b.furShade : Pal.Mul(fur, 0.82f)),
                outfit = outfit,
                outfitShade = Col(h.outfitShade, string.IsNullOrEmpty(h.outfit) ? b.outfitShade : Pal.Mul(outfit, 0.75f)),
                accent = Col(h.accent, b.accent),
                style = style,
                look = h,
                frames = HeroFrames(h),
            };
        }
    }
}
