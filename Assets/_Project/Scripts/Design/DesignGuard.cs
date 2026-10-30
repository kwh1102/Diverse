using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Deterministic gate between a prompt/AI answer and the game.
    /// 1) Balance: a pack has no stat fields at all (unknown JSON fields are dropped by JsonUtility); the chassis must be an
    ///    existing weapon/costume; power requests in the prompt are listed back to the player as "not applied".
    /// 2) Readability: sizes, palette shifts and tints are clamped so enemies, telegraphs and the ground stay legible.
    /// 3) Safety: names lose rich-text tags and are length-limited.
    /// </summary>
    public static class DesignGuard
    {
        public const int MaxWeaponLength = 34, MaxWeaponThickness = 5;
        public const int MaxGridW = 32, MaxGridH = 14, MaxProjectile = 12;
        public const int MaxName = 12, MaxPrompt = 200;

        static readonly Regex PowerWords = new Regex(
            @"공격력|데미지|대미지|피해량|피해를|체력|hp|HP|방어력|무적|즉사|한\s?방|원샷|공속|이속|이동\s?속도|공격\s?속도|사거리|사정거리|범위|치명|크리|쿨\s?타임|쿨다운|재사용|레벨|경험치|골드|무한|최강|사기|강하게|더 아프|관통|자동\s?조준|유도|[0-9]+\s?(배|%|퍼센트|초)",
            RegexOptions.Compiled);

        /// <summary>Clauses of the prompt that ask for power. Shown as "not applied" (the pack can't express them anyway).</summary>
        public static List<string> PowerRequests(string prompt)
        {
            var found = new List<string>();
            if (string.IsNullOrWhiteSpace(prompt)) return found;
            foreach (var raw in Regex.Split(prompt, @"[,.!?\n]|그리고|하고\s|이고\s|면서"))
            {
                var clause = raw.Trim();
                if (clause.Length == 0 || !PowerWords.IsMatch(clause)) continue;
                if (clause.Length > 24) clause = clause.Substring(0, 24) + "…";
                found.Add($"‘{clause}’ — 능력치는 바꿀 수 없어요 (모습만 반영)");
            }
            return found;
        }

        /// <summary>Exactly n picture prompts (or null). A missing entry stays null and that picture is skipped.</summary>
        static string[] Prompts(string[] a, int n) => a == null || a.Length == 0 ? null : Enumerable.Range(0, n).Select(i => i < a.Length ? CleanImagePrompt(a[i]) : null).ToArray();

        static string[] Ids(string[] a, int n) => a != null && a.Length == n ? a : null;

        static readonly Regex BareHandWords = new Regex(@"맨손|주먹|무기\s?없|발차기|박치기|몸통\s?박치기|맨몸|촉수|꼬리로|입김|브레스|불을 뿜|날개로", RegexOptions.Compiled);
        static readonly Regex TweakWords = new Regex(@"^.{0,30}(더\s|조금|약간|살짝|색만|색깔만|크게|작게|길게|짧게|진하게|연하게|밝게|어둡게|그대로)", RegexOptions.Compiled);

        /// <summary>The player wants to fight without holding anything (fists, tentacles, breath…).</summary>
        public static bool BareHanded(string prompt) => !string.IsNullOrEmpty(prompt) && BareHandWords.IsMatch(prompt);

        /// <summary>A short "a bit more / only the colour" request: refine the previous design instead of drawing a new one.</summary>
        public static bool IsTweak(string prompt) => !string.IsNullOrEmpty(prompt) && TweakWords.IsMatch(prompt);

        public static string CleanPrompt(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Trim();
            return s.Length > MaxPrompt ? s.Substring(0, MaxPrompt) : s;
        }

        /// <summary>English picture description for the image model (plain text, bounded).</summary>
        public static string CleanImagePrompt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = Regex.Replace(s, "<[^>]*>", "").Replace('\n', ' ').Trim();
            return s.Length > 300 ? s.Substring(0, 300) : s;
        }

        public static string CleanName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = Regex.Replace(s, "<[^>]*>", "").Replace("<", "").Replace(">", "").Replace("\n", " ").Trim();
            if (s.Length > MaxName) s = s.Substring(0, MaxName);
            return s.Length == 0 ? null : s;
        }

        static string Hex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            if (s.Length != 7 || !ColorUtility.TryParseHtmlString(s, out _)) return null;
            return s.ToLowerInvariant();
        }

        static string OneOf(string v, string[] allowed, string fallback)
        {
            if (string.IsNullOrWhiteSpace(v)) return fallback;
            var hit = allowed.FirstOrDefault(a => string.Equals(a, v.Trim(), StringComparison.OrdinalIgnoreCase));
            return hit ?? fallback;
        }

        static string[] Grid(string[] rows, int maxW, int maxH)
        {
            if (rows == null || rows.Length == 0) return null;
            var clean = rows.Where(r => r != null).Select(r => new string(r.Where(ch => ch == '.' || ch == ' ' || (ch >= '1' && ch <= '9')).ToArray()))
                            .Take(maxH).Select(r => r.Length > maxW ? r.Substring(0, maxW) : r).ToArray();
            int filled = clean.Sum(r => r.Count(ch => ch != '.' && ch != ' '));
            return filled >= 6 ? clean : null;
        }

        static string[] Palette(string[] p) => p == null ? null : p.Select(Hex).Where(h => h != null).Take(9).ToArray();

        static void Shift(PaletteShift s, float maxTint, float minSat, float minBright, float maxBright)
        {
            s.hueShift = Mathf.Repeat(s.hueShift + 180f, 360f) - 180f;
            if (float.IsNaN(s.hueShift)) s.hueShift = 0;
            s.saturation = s.saturation <= 0 || float.IsNaN(s.saturation) ? 1 : Mathf.Clamp(s.saturation, minSat, 1.6f);
            s.brightness = s.brightness <= 0 || float.IsNaN(s.brightness) ? 1 : Mathf.Clamp(s.brightness, minBright, maxBright);
            s.tint = Hex(s.tint);
            s.tintAmount = s.tint == null || float.IsNaN(s.tintAmount) ? 0 : Mathf.Clamp(s.tintAmount, 0, maxTint);
        }

        /// <summary>Bring any pack (AI answer, local parse, old file) into the allowed space. Never throws.</summary>
        public static void Sanitize(DesignPack p)
        {
            p.hero ??= new HeroLook();
            p.weapon ??= new WeaponLook();
            p.attack ??= new AttackLook();
            p.attack.projectile ??= new ProjectileLook();
            p.world ??= new WorldLook();
            p.enemies ??= new EnemyLook();
            p.enemies.names ??= new List<NameEntry>();
            p.prompts ??= new List<string>();
            while (p.prompts.Count < 4) p.prompts.Add("");
            for (int i = 0; i < p.prompts.Count; i++) p.prompts[i] = CleanPrompt(p.prompts[i]);
            p.ignored ??= new List<string>();
            p.ignored = p.ignored.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Regex.Replace(s, "<[^>]*>", "")).Distinct().Take(6).ToList();
            p.summary = Regex.Replace(p.summary ?? "", "<[^>]*>", "");
            if (p.summary.Length > 60) p.summary = p.summary.Substring(0, 60);

            var h = p.hero;
            h.name = CleanName(h.name);
            if (!DB.Costumes.Any(c => c.id == h.basePerk)) h.basePerk = DB.Costumes[0].id;
            h.fur = Hex(h.fur); h.furShade = Hex(h.furShade); h.outfit = Hex(h.outfit); h.outfitShade = Hex(h.outfitShade);
            h.accent = Hex(h.accent); h.hatColor = Hex(h.hatColor);
            h.style = Mathf.Clamp(h.style, -1, 5);
            h.hat = OneOf(h.hat, DesignArt.Hats, "none");
            h.species = CleanName(h.species);
            h.imagePrompt = CleanImagePrompt(h.imagePrompt);
            h.perkConcept = CleanImagePrompt(h.perkConcept);
            if (h.perk != null && string.IsNullOrEmpty(h.perk.json)) h.perk = null;

            var w = p.weapon;
            w.name = CleanName(w.name);
            w.baseWeapon = OneOf(w.baseWeapon, Enum.GetNames(typeof(WeaponKind)), nameof(WeaponKind.Greatsword));
            w.shape = OneOf(w.shape, DesignArt.WeaponShapes, "builtin");
            w.length = w.length <= 0 ? 0 : Mathf.Clamp(w.length, 6, MaxWeaponLength);
            w.thickness = w.thickness <= 0 ? 0 : Mathf.Clamp(w.thickness, 1, MaxWeaponThickness);
            w.tip = OneOf(w.tip, DesignArt.Tips, "point");
            w.imagePrompt = CleanImagePrompt(w.imagePrompt);
            // Attack parts: closed vocabulary, 1..4 moves; QWER ids are checked again by DesignCombat
            w.moves = (w.moves ?? new List<string>()).Select(m => OneOf(m, DesignCombat.Moves, null)).Where(m => m != null).Take(4).ToList();
            w.weight = OneOf(w.weight, DesignCombat.Weights, "medium");
            w.skills = w.skills?.Where(s => DesignCombat.AllSkills.Contains(s)).Take(4).ToArray();
            w.skillNames = w.skillNames?.Take(4).Select(CleanName).ToArray();
            w.main = Hex(w.main); w.shade = Hex(w.shade); w.accent = Hex(w.accent); w.grip = Hex(w.grip); w.glow = Hex(w.glow);
            w.hold = OneOf(w.hold, DesignArt.Holds, "Hand");
            w.pixels = Grid(w.pixels, MaxGridW, MaxGridH);
            w.palette = Palette(w.palette);
            if (w.pixels != null && (w.palette == null || w.palette.Length == 0)) w.pixels = null;

            var a = p.attack;
            a.origin = OneOf(a.origin, DesignArt.Origins, "Hand");
            a.slash = Hex(a.slash); a.slashCore = Hex(a.slashCore); a.trail = Hex(a.trail);
            var pr = a.projectile;
            pr.shape = OneOf(pr.shape, DesignArt.ProjectileShapes, "builtin");
            pr.size = pr.size <= 0 ? 0 : Mathf.Clamp(pr.size, 4, MaxProjectile);
            pr.main = Hex(pr.main); pr.accent = Hex(pr.accent);
            pr.pixels = Grid(pr.pixels, MaxProjectile, MaxProjectile);
            pr.palette = Palette(pr.palette);
            pr.imagePrompt = CleanImagePrompt(pr.imagePrompt);
            if (pr.pixels != null && (pr.palette == null || pr.palette.Length == 0)) pr.pixels = null;

            // Ground must stay walkable-looking and enemies must stay readable against it
            Shift(p.world, 0.55f, 0.35f, 0.6f, 1.25f);
            p.world.grass = Hex(p.world.grass); p.world.path = Hex(p.world.path); p.world.water = Hex(p.world.water); p.world.foliage = Hex(p.world.foliage);
            p.world.mood = CleanName(p.world.mood);
            p.world.themePrompt = CleanImagePrompt(p.world.themePrompt);
            p.world.rough = Hex(p.world.rough);
            var wl = p.world;
            // Older packs (4 tiles / 8 objects) are widened to today's layout so their pictures keep working
            if (wl.tileImages != null && wl.tileImages.Length == 4) wl.tileImages = wl.tileImages.SelectMany(t => new[] { t, t }).ToArray();
            if (wl.decorImages != null && wl.decorImages.Length == 8)
            {
                wl.decorSizes = new[] { "large", "large", "medium", "medium", "small", "small", "tiny", "tiny", null, null, null, null };
                wl.decorImages = wl.decorImages.Concat(new string[4]).ToArray();
            }
            wl.tilePrompts = Prompts(wl.tilePrompts, 8); wl.tileImages = Ids(wl.tileImages, 8);
            wl.layout = OneOf(wl.layout, DesignLayout.Styles, "wild");
            wl.decorPrompts = Prompts(wl.decorPrompts, 12); wl.decorImages = Ids(wl.decorImages, 12);
            wl.decorSizes = wl.decorSizes == null ? null : Enumerable.Range(0, 12).Select(i => i < wl.decorSizes.Length ? OneOf(wl.decorSizes[i], DesignLayout.Sizes, null) : null).ToArray();
            wl.decorPlaces = wl.decorPlaces == null ? null : Enumerable.Range(0, 12).Select(i => i < wl.decorPlaces.Length ? OneOf(wl.decorPlaces[i], DesignLayout.Places, null) : null).ToArray();
            wl.townPrompts = Prompts(wl.townPrompts, 4); wl.townImages = Ids(wl.townImages, 4);
            wl.ambient = OneOf(wl.ambient, DesignArt.Ambients, "none");
            wl.ambientColor = Hex(wl.ambientColor);
            p.attack.effectPrompts = Prompts(p.attack.effectPrompts, 4);
            p.attack.effectImages = Ids(p.attack.effectImages, 4);
            if (p.hero.frames != null && p.hero.frames.Length != 4 && p.hero.frames.Length != 6 && p.hero.frames.Length != 8) p.hero.frames = null;
            p.weapon.attackPose = CleanImagePrompt(p.weapon.attackPose);
            Shift(p.enemies, 0.5f, 0.45f, 0.7f, 1.2f);
            p.enemies.theme = CleanName(p.enemies.theme);
            p.enemies.families = (p.enemies.families ?? new List<EnemyFamily>())
                .Where(f => f != null && CleanImagePrompt(f.imagePrompt) != null)
                .Select(f => new EnemyFamily { name = CleanName(f.name), imagePrompt = CleanImagePrompt(f.imagePrompt) })
                .Take(4).ToList();
            p.enemies.art = (p.enemies.art ?? new List<EnemyArt>())
                .Where(a => a != null && DB.Enemies.ContainsKey(a.id ?? ""))
                .Select(a => new EnemyArt { id = a.id, imagePrompt = CleanImagePrompt(a.imagePrompt), image = a.image, image2 = a.image2, image3 = a.image3, image4 = a.image4 })
                .GroupBy(a => a.id).Select(g => g.First()).ToList();
            p.enemies.names = p.enemies.names
                .Where(n => n != null && DB.Enemies.ContainsKey(n.id ?? ""))
                .Select(n => new NameEntry { id = n.id, name = CleanName(n.name) })
                .Where(n => n.name != null)
                .GroupBy(n => n.id).Select(g => g.First()).ToList();
        }
    }
}
