using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// The five zones of a normal-mode world, laid around the spawn like the slices of a pentagon.
    /// - Each zone has a build type (CombatTypeDef) and, separately, a look (ThemeDef). Both blend toward the neighbour near
    ///   a border (t = 0~0.5), so nothing changes along a hard line; the borders wander (domain-warped) instead of being
    ///   straight spokes. The spawn keeps a neutral theme (DB.SpawnTheme) that fades out into the zones.
    /// - Every zone has a landmark with its pure boss (Landmark site) in its middle, every border a mixed site (Border site).
    /// Pure data from the seed: the same world always gets the same layout. Terrain does not follow these borders —
    /// mountains and rivers run across them and only change their look.
    /// </summary>
    public class ZoneMap
    {
        public const int Count = 5;
        const float Slice = 360f / Count;

        public readonly CombatType[] combat = new CombatType[Count];
        public readonly ThemeDef[] themes = new ThemeDef[Count];
        public readonly float rotation;          // start angle of zone 0 (degrees, counter-clockwise from +x)
        public readonly int seed;
        /// <summary>Zone landmarks (pure bosses) and border sites (mixed bosses), in world coordinates.</summary>
        public readonly List<StructureSpec> sites = new List<StructureSpec>();
        static WorldRulesDef Rules => DB.WorldRules;

        public struct Sample
        {
            public int a, b;      // own zone, nearest neighbour
            public float t;       // neighbour share: 0 deep inside a zone, 0.5 on the border
        }

        // ───────────── Building ─────────────

        /// <summary>The zones of a normal-mode world (choosing them the first time). w may be null (editor tools): seed only.</summary>
        public static ZoneMap ForWorld(WorldSave w, int seed)
        {
            if (DB.CombatTypes.Count == 0) return null;
            var zones = w?.zones ?? new List<string>();
            var themeIds = w?.zoneThemes ?? new List<string>();
            EnsureZones(zones, seed);
            return new ZoneMap(seed, zones, themeIds);
        }

        /// <summary>
        /// A fresh world draws 5 different build types (of the 8) at random. A valid list that was already set — e.g. picked
        /// by the player for a custom seed, repeats allowed — is kept as it is.
        /// </summary>
        public static void EnsureZones(List<string> zones, int seed)
        {
            bool ok = zones.Count == Count;
            if (ok) foreach (var z in zones) if (!System.Enum.TryParse<CombatType>(z, out _)) ok = false;
            if (ok) return;
            var pool = new List<CombatType>((CombatType[])System.Enum.GetValues(typeof(CombatType)));
            var rng = new Rng(Hash.U(seed, 5, 90210));
            zones.Clear();
            for (int i = 0; i < Count; i++)
            {
                var c = pool[rng.Range(0, pool.Count)];
                pool.Remove(c);
                zones.Add(c.ToString());
            }
        }

        ZoneMap(int seed, List<string> zones, List<string> themeIds)
        {
            this.seed = seed;
            var rng = new Rng(Hash.U(seed, 77, 31337));
            rotation = rng.Range(0f, 360f);
            for (int i = 0; i < Count; i++)
            {
                System.Enum.TryParse(zones[i], out combat[i]);
                var custom = i < themeIds.Count && !string.IsNullOrEmpty(themeIds[i]) ? DB.Theme(themeIds[i]) : null;
                themes[i] = custom ?? DB.ThemeOf(combat[i]);
            }
            PlaceSites(rng);
        }

        void PlaceSites(Rng rng)
        {
            var r = Rules;
            for (int i = 0; i < Count; i++)
            {
                // The landmark sits well inside its slice (never near a border) at a random distance in range
                float a = rotation + i * Slice + rng.Range(Slice * 0.3f, Slice * 0.7f);
                var t = themes[i];
                sites.Add(new StructureSpec
                {
                    id = "landmark" + i, kind = StructKind.Landmark, zone = i, zone2 = i, theme = t,
                    center = MathX.Dir(a) * rng.Range(r.landmarkDistance.x, r.landmarkDistance.y),
                    radius = t.arenaRadius, landmark = true, boss = t.boss,
                    name = string.IsNullOrEmpty(t.landmarkName) ? t.displayName : t.landmarkName,
                });
            }
            for (int i = 0; i < Count; i++)
            {
                int j = (i + 1) % Count;
                var ta = themes[i]; var tb = themes[j];
                float a = rotation + j * Slice + rng.Range(-4f, 4f);
                sites.Add(new StructureSpec
                {
                    id = "border" + i, kind = StructKind.Border, zone = i, zone2 = j, theme = ta,
                    center = MathX.Dir(a) * rng.Range(r.borderSiteDistance.x, r.borderSiteDistance.y),
                    radius = 11, landmark = true, boss = rng.Chance(0.5f) ? ta.boss : tb.boss,
                    name = ta == tb ? $"{ta.prefix}의 관문" : $"{ta.prefix}·{tb.prefix} 경계의 관문",
                });
            }
        }

        // ───────────── Sampling ─────────────

        public Sample At(Vector2 p)
        {
            var r = Rules;
            // Domain warp: the borders wander instead of being straight spokes
            var q = p + new Vector2(Noise.Fractal(p.x, p.y, seed + 811, 0.006f, 2) - 0.5f, Noise.Fractal(p.x, p.y, seed + 823, 0.006f, 2) - 0.5f) * (2f * r.borderWiggle);
            float rel = Mathf.Repeat(Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg - rotation, 360f);
            int a = Mathf.Min(Count - 1, (int)(rel / Slice));
            float within = rel - a * Slice;
            int b; float edgeDeg;
            if (within < Slice / 2) { b = (a + Count - 1) % Count; edgeDeg = within; }
            else { b = (a + 1) % Count; edgeDeg = Slice - within; }
            float arc = edgeDeg * Mathf.Deg2Rad * Mathf.Max(1f, q.magnitude);
            float k = Mathf.Clamp01(arc / Mathf.Max(1f, r.blendWidth));
            return new Sample { a = a, b = b, t = 0.5f * (1 - k * k * (3 - 2 * k)) };
        }

        /// <summary>
        /// The zone that wins at p for a roll (0~1). The roll is compared with the share of the zone that comes NEXT
        /// (counter-clockwise) of the pair; that share is 0.5 on both sides of the border, so the same roll gives the same
        /// zone across the line and patches flow over it instead of every tile flipping there.
        /// </summary>
        public int ZoneAt(Vector2 p, float roll)
        {
            var s = At(p);
            bool bNext = s.b == (s.a + 1) % Count;
            float nextShare = bNext ? s.t : 1 - s.t;
            return roll < nextShare ? (bNext ? s.b : s.a) : (bNext ? s.a : s.b);
        }

        /// <summary>Spawn share at p: 1 in the spawn's own land, fading to 0 over 30 tiles.</summary>
        public static float SpawnShare(Vector2 p) => 1 - Mathf.Clamp01((p.magnitude - Rules.centerRadius) / 30f);

        /// <summary>
        /// Theme at p in organic patches: the spawn's theme near the centre, the zone's theme, and near a border the two
        /// mixed in tongues and islands (smooth noise only, never a checkerboard of single tiles).
        /// </summary>
        public ThemeDef ThemeAt(float x, float y)
        {
            var p = new Vector2(x, y);
            float patch = Mathf.Clamp01((Noise.Fractal(x, y, seed + 901, 0.05f, 2) - 0.3f) / 0.4f);
            if (patch < SpawnShare(p)) return DB.SpawnTheme;
            return themes[ZoneAt(p, patch)];
        }

        /// <summary>A theme value blended smoothly by zone shares (terrain uses this so ranges don't jump at borders).</summary>
        public float Blend(Vector2 p, System.Func<ThemeDef, float> f)
        {
            var s = At(p);
            float v = f(themes[s.a]) * (1 - s.t) + f(themes[s.b]) * s.t;
            float c = SpawnShare(p);
            return c > 0 ? Mathf.Lerp(v, f(DB.SpawnTheme), c) : v;
        }

        public string RegionName(Vector2 p)
        {
            if (SpawnShare(p) > 0.5f) return DB.SpawnTheme.displayName;
            var s = At(p);
            var ta = themes[s.a]; var tb = themes[s.b];
            if (s.t > 0.32f && ta != tb) return $"{ta.prefix}·{tb.prefix} 경계";
            return ta.displayName;
        }

        public string CombatName(int zone) => DB.CombatTypes.TryGetValue(combat[zone], out var d) ? d.displayName : combat[zone].ToString();
    }
}
