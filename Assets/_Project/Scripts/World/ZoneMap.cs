using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>Zone mechanics carried by one spawned monster (see ZoneTrait). Strengths are already scaled by distance.</summary>
    public class ZoneMods
    {
        public float armor;          // share of max HP taken off each hit
        public int shieldHits;       // hits fully blocked before the shield breaks (refills)
        public float deflect;        // chance to block a hit from range
        public float thorns;         // melee hits return this × damage
        public float deathBurst;     // explodes on death, × damage
        public float skittish;       // 0~1: leaps away when the hero gets close
        public float miasma;         // damage per second near it, × damage
        public float barrage;        // 0~1: projectile rings
        public int brood;            // small copies left on death
        public string burstFx, moteFx;   // the zone's drawn blast / drifting puff (sprite keys; null = particles)

        public ZoneMods Copy() => (ZoneMods)MemberwiseClone();
    }

    /// <summary>
    /// The five zones of a normal-mode world, laid around the spawn like the slices of a pentagon.
    /// - Each point belongs to one zone (a) and blends toward its nearest neighbor (b) close to the border (t = 0~0.5),
    ///   so terrain, monsters and their mechanics change gradually instead of along a hard line.
    /// - Borders wander (domain-warped noise) so they never look like straight spokes.
    /// - Every zone has one pure boss in its middle; every border has a mixed mid-boss.
    /// - The deep layer (reached through a cleared zone boss's gate) is the same zone everywhere, at full strength.
    /// Pure data from the seed: the same world always gets the same layout.
    /// </summary>
    public class ZoneMap
    {
        public const int Count = 5;
        const float Slice = 360f / Count;

        public readonly ZoneDef[] zones = new ZoneDef[Count];
        public readonly float rotation;          // start angle of zone 0 (degrees, counter-clockwise from +x)
        public readonly bool deep;
        public readonly int seed;                // terrain seed of this layer
        public readonly int layerKey;            // LayerKey of this layer
        public readonly List<StructureSpec> bosses = new List<StructureSpec>();
        static ZoneRulesDef Rules => DB.ZoneRules;

        public struct Sample
        {
            public int a, b;      // own zone, nearest neighbor
            public float t;       // neighbor share: 0 deep inside a zone, 0.5 on the border
        }

        // ───────────── Building ─────────────

        /// <summary>0 on the surface, 1 + BuildStyle in a deep layer (graves and warp stones remember it).</summary>
        public static int LayerKey(WorldSave w) => w == null || w.layer == 0 || !TryStyle(w.deepZone, out var s) ? 0 : 1 + (int)s;

        public static bool TryStyle(string name, out BuildStyle s) => System.Enum.TryParse(name, out s) && System.Enum.IsDefined(typeof(BuildStyle), s);

        /// <summary>The map of a normal-mode world (choosing its five zones the first time). Null when no zone data exists.</summary>
        public static ZoneMap ForWorld(WorldSave w)
        {
            if (DB.Zones.Count == 0) return null;
            EnsureZones(w);
            if (w.layer > 0 && TryStyle(w.deepZone, out var deepStyle) && DB.Zone(deepStyle) != null)
                return new ZoneMap(w, deepStyle);
            w.layer = 0;
            return new ZoneMap(w, null);
        }

        /// <summary>A fresh world draws 5 different zone types at random. A list that was already set (custom seeds later) is kept.</summary>
        public static void EnsureZones(WorldSave w)
        {
            bool ok = w.zones != null && w.zones.Count == Count;
            if (ok) foreach (var z in w.zones) if (!TryStyle(z, out var s) || DB.Zone(s) == null) ok = false;
            if (ok) return;
            var pool = new List<BuildStyle>(DB.Zones.Keys);
            pool.Sort();
            var rng = new Rng(Hash.U(w.seed, 5, 90210));
            w.zones = new List<string>();
            for (int i = 0; i < Count; i++)
            {
                // Distinct while the pool lasts (8 types → always distinct)
                var s = pool.Count > 0 ? pool[rng.Range(0, pool.Count)] : (BuildStyle)rng.Range(0, 8);
                pool.Remove(s);
                w.zones.Add(s.ToString());
            }
        }

        ZoneMap(WorldSave w, BuildStyle? deepStyle)
        {
            deep = deepStyle.HasValue;
            seed = deep ? (int)(Hash.U(w.seed, 1 + (int)deepStyle.Value, 4441) % 2000000000u) + 1000 : w.seed;
            layerKey = deep ? 1 + (int)deepStyle.Value : 0;
            var rng = new Rng(Hash.U(seed, 77, 31337));
            rotation = rng.Range(0f, 360f);
            for (int i = 0; i < Count; i++)
            {
                TryStyle(deep ? deepStyle.Value.ToString() : w.zones[i], out var s);
                zones[i] = DB.Zone(s);
                if (zones[i] == null) foreach (var any in DB.Zones.Values) { zones[i] = any; break; }
            }
            PlaceBosses(rng);
        }

        void PlaceBosses(Rng rng)
        {
            var r = Rules;
            string pre = deep ? $"L{layerKey}:" : "";
            if (deep)
            {
                var z = zones[0];
                float a = rng.Range(0f, 360f);
                bosses.Add(new StructureSpec
                {
                    id = pre + "deepboss", kind = StructKind.ZoneBoss, zone = z, zone2 = z, center = MathX.Dir(a) * rng.Range(r.deepBossDistance.x, r.deepBossDistance.y),
                    radius = 11, landmark = true, boss = z.boss, name = $"{z.deepName}의 심장",
                });
            }
            else
            {
                for (int i = 0; i < Count; i++)
                {
                    var z = zones[i];
                    float m = Mathf.Clamp(r.zoneBossBorderMargin, 0, Slice / 2 - 1);
                    float a = rotation + i * Slice + rng.Range(m, Slice - m);
                    bosses.Add(new StructureSpec
                    {
                        id = "zoneboss" + i, kind = StructKind.ZoneBoss, zone = z, zone2 = z, center = MathX.Dir(a) * rng.Range(r.zoneBossDistance.x, r.zoneBossDistance.y),
                        radius = 11, landmark = true, boss = z.boss, name = $"{z.name}의 주인",
                    });
                }
            }
            // Mid-bosses on every border (in a deep layer: guardians of the same zone)
            for (int i = 0; i < Count; i++)
            {
                var za = zones[i]; var zb = zones[(i + 1) % Count];
                float a = rotation + (i + 1) * Slice + rng.Range(-3f, 3f);
                var c = MathX.Dir(a) * rng.Range(r.borderBossDistance.x, r.borderBossDistance.y);
                if (deep) c *= 1.35f;
                bosses.Add(new StructureSpec
                {
                    id = pre + "borderboss" + i, kind = StructKind.BorderBoss, zone = za, zone2 = zb, center = c, radius = 8, landmark = true,
                    boss = rng.Chance(0.5f) ? za.boss : zb.boss,
                    name = za == zb ? $"{za.prefix}의 수호자" : $"{za.prefix}·{zb.prefix} 경계의 수호자",
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

        /// <summary>The zone in charge at p for a random roll (0~1): near borders the neighbor wins as often as it blends in.</summary>
        public ZoneDef ZoneAt(Vector2 p, float roll)
        {
            return Pick(At(p), roll);
        }

        /// <summary>
        /// The zone that wins for a roll (0~1). The roll is compared with the share of the zone that comes NEXT (counter-
        /// clockwise) of the pair; that share is 0.5 on both sides of the border, so the same roll gives the same zone across
        /// the line and patches flow over it instead of every tile flipping there (which drew the border as a seam).
        /// </summary>
        ZoneDef Pick(Sample s, float roll)
        {
            bool bNext = s.b == (s.a + 1) % Count;
            float nextShare = bNext ? s.t : 1 - s.t;
            return roll < nextShare ? zones[bNext ? s.b : s.a] : zones[bNext ? s.a : s.b];
        }

        public ZoneDef MainZone(Vector2 p) => zones[At(p).a];

        /// <summary>The zone that owns a biome (each zone type has its own), or null (the spawn's meadow).</summary>
        public ZoneDef ByBiome(Biome b)
        {
            foreach (var z in zones) if (z.biome == b) return z;
            return null;
        }

        /// <summary>Terrain biome: neutral meadow at the spawn, then the zone's biome, mixed in patches near borders.</summary>
        public Biome BiomeAt(float x, float y)
        {
            var p = new Vector2(x, y);
            float d = p.magnitude, c = Rules.centerRadius;
            // Smooth noise only: borders mix in organic tongues and islands, never a checkerboard of single tiles
            float patch = Mathf.Clamp01((Noise.Fractal(x, y, seed + 901, 0.05f, 2) - 0.3f) / 0.4f);
            if (d < c) return Biome.Meadow;
            if (d < c + 22 && patch > (d - c) / 22f) return Biome.Meadow;
            return Pick(At(p), patch).biome;
        }

        public string RegionName(Vector2 p)
        {
            var s = At(p);
            var za = zones[s.a]; var zb = zones[s.b];
            if (deep) return za.deepName;
            if (s.t > 0.32f && za != zb) return $"{za.prefix}·{zb.prefix} 경계";
            return za.name;
        }

        // ───────────── Monsters ─────────────

        public string PickEnemy(Vector2 at, Rng rng)
        {
            var z = ZoneAt(at, rng.Value);
            return z.enemies != null && z.enemies.Length > 0 ? rng.Pick(z.enemies) : "shroom";
        }

        static float Ramp(float d, Vector2 range) => Mathf.Clamp01((d - range.x) / Mathf.Max(1f, range.y - range.x));

        /// <summary>How the zone mechanics at p scale group sizes (Horde).</summary>
        public float GroupSizeMul(Vector2 at)
        {
            var s = At(at);
            float p1, p2; Strength(at.magnitude, out p1, out p2);
            float h = 0;
            void Add(ZoneDef z, float share)
            {
                if (z.primary == ZoneTrait.Horde) h = Mathf.Max(h, p1 * share);
                if (z.secondary == ZoneTrait.Horde) h = Mathf.Max(h, p2 * share);
            }
            Add(zones[s.a], 1); Add(zones[s.b], s.t * 2);
            return Mathf.Lerp(1, Rules.horde, h);
        }

        void Strength(float dist, out float p1, out float p2)
        {
            if (deep) { p1 = p2 = 1; return; }
            p1 = Ramp(dist, Rules.primaryTraitRange);
            p2 = Ramp(dist, Rules.secondaryTraitRange);
        }

        /// <summary>
        /// Put the zone mechanics on a monster spawned at p (tier = its difficulty tier). Bosses use their structure's zones
        /// at a strength of at least 70%; regular monsters get their own zone's mechanics plus the neighbor's near borders.
        /// </summary>
        public void Apply(EnemyDef def, Vector2 at, float tier, StructureSpec bossSite = null)
        {
            var r = Rules;
            def.hp *= 1 + tier * r.hpPerTier;
            def.damage *= 1 + tier * r.damagePerTier;
            var m = new ZoneMods();
            Strength(at.magnitude, out var p1, out var p2);
            if (bossSite != null && bossSite.zone != null)
            {
                p1 = Mathf.Max(p1, 0.7f); p2 = Mathf.Max(p2, 0.7f);
                AddTraits(def, m, bossSite.zone, p1, p2);
                if (bossSite.zone2 != null && bossSite.zone2 != bossSite.zone) AddTraits(def, m, bossSite.zone2, p1, p2);
                def.tint = Color.Lerp(Color.white, bossSite.zone.color, 0.55f);
                if (bossSite.kind == StructKind.BorderBoss && bossSite.zone2 != null)
                    def.tint = Color.Lerp(def.tint, Color.Lerp(Color.white, bossSite.zone2.color, 0.55f), 0.5f);
            }
            else
            {
                var s = At(at);
                AddTraits(def, m, zones[s.a], p1, p2);
                if (zones[s.b] != zones[s.a] && s.t > 0.03f) AddTraits(def, m, zones[s.b], p1 * s.t * 2, p2 * s.t * 2);
            }
            var look = bossSite?.zone ?? zones[At(at).a];
            m.burstFx = ZoneArtDef.FxKey(look.style, "burst");
            m.moteFx = ZoneArtDef.FxKey(look.style, "mote");
            def.mods = m;
        }

        static void AddTraits(EnemyDef def, ZoneMods m, ZoneDef z, float p1, float p2)
        {
            AddTrait(def, m, z.primary, p1);
            AddTrait(def, m, z.secondary, p2);
        }

        static void AddTrait(EnemyDef def, ZoneMods m, ZoneTrait t, float s)
        {
            if (s < 0.05f) return;
            var r = Rules;
            switch (t)
            {
                case ZoneTrait.Armor: m.armor = Mathf.Max(m.armor, r.armor * s); break;
                case ZoneTrait.HitShield: m.shieldHits = Mathf.Max(m.shieldHits, Mathf.Max(1, Mathf.RoundToInt(r.shieldHits * s))); break;
                case ZoneTrait.Deflect: m.deflect = Mathf.Max(m.deflect, r.deflect * s); break;
                case ZoneTrait.Thorns: m.thorns = Mathf.Max(m.thorns, r.thorns * s); break;
                case ZoneTrait.DeathBurst: if (s > 0.15f) m.deathBurst = Mathf.Max(m.deathBurst, r.deathBurst * s); break;
                case ZoneTrait.Swift: def.speed *= Mathf.Lerp(1, r.swift, s); break;
                case ZoneTrait.Skittish: if (!def.boss) m.skittish = Mathf.Max(m.skittish, s); break;
                case ZoneTrait.Miasma: m.miasma = Mathf.Max(m.miasma, r.miasma * s); break;
                case ZoneTrait.Relentless: def.attackCooldown *= Mathf.Lerp(1, r.relentless, s); break;
                case ZoneTrait.Barrage: m.barrage = Mathf.Max(m.barrage, s); break;
                case ZoneTrait.Heavy: def.damage *= Mathf.Lerp(1, r.heavy, s); def.attackWindup *= Mathf.Lerp(1, 1.15f, s); break;
                case ZoneTrait.Horde: if (!def.boss) def.hp *= Mathf.Lerp(1, r.hordeHp, s); break;
                case ZoneTrait.Brood: if (!def.boss && s > 0.1f) m.brood = Mathf.Max(m.brood, s > 0.5f ? r.brood : 1); break;
            }
        }

        /// <summary>Plain words for a mechanic (map / dialogue).</summary>
        public static string TraitName(ZoneTrait t) => t switch
        {
            ZoneTrait.Armor => "단단한 갑피 (약한 타격 경감)",
            ZoneTrait.HitShield => "타격 보호막 (횟수로 깨짐)",
            ZoneTrait.Deflect => "원거리 공격 튕겨냄",
            ZoneTrait.Thorns => "가시 (근접 타격 반사)",
            ZoneTrait.DeathBurst => "죽을 때 폭발",
            ZoneTrait.Swift => "빠른 몸놀림",
            ZoneTrait.Skittish => "가까이 가면 도약해 달아남",
            ZoneTrait.Miasma => "피할 수 없는 독기",
            ZoneTrait.Relentless => "쉴 새 없는 공격",
            ZoneTrait.Barrage => "탄막",
            ZoneTrait.Heavy => "묵직한 일격",
            ZoneTrait.Horde => "끝없는 무리",
            ZoneTrait.Brood => "죽으면 새끼가 나옴",
            _ => "",
        };
    }
}
