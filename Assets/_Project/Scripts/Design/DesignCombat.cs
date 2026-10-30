using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// A designed weapon's combat: the AI picks attack PARTS (move types + a weight) and QWER skills from the existing pool;
    /// every number comes from the existing weapon assets. Each part copies a combo step of the weapon that does that move
    /// (light / medium / heavy = the lightest / median / heaviest hitter), then the whole combo is scaled so its power equals
    /// the median power of the built-in weapons. So a pistol, an octopus ink spray or a toothpick are all as strong as a sword.
    /// </summary>
    public static class DesignCombat
    {
        public static readonly string[] Moves = { "slash", "wide", "thrust", "spin", "smash", "shot", "spread" };
        public static readonly string[] Weights = { "light", "medium", "heavy" };
        public static readonly string[] Ultimates = { "gs_titan", "ss_sanctum", "cb_rain", "st_thunder", "dg_frenzy", "kt_flash" };
        public static string[] Basics => AllSkills.Where(s => !Ultimates.Contains(s)).ToArray();
        public static readonly string[] AllSkills =
        {
            "gs_leap", "gs_whirl", "gs_rush", "gs_titan", "ss_bash", "ss_parry", "ss_leap", "ss_sanctum",
            "cb_pierce", "cb_scatter", "cb_backstep", "cb_rain", "st_thrust", "st_cyclone", "st_vault", "st_thunder",
            "dg_fan", "dg_shadowstep", "dg_venom", "dg_frenzy", "kt_iai", "kt_crescent", "kt_counter", "kt_flash",
        };

        public static string Classify(ComboStep s) =>
            s.spin ? "spin" : s.projectile ? (s.projectileCount > 1 ? "spread" : "shot") : s.thrust ? "thrust"
            : s.arc >= 165 ? "wide" : s.damage >= 1.5f ? "smash" : "slash";

        static bool Ranged(string move) => move is "shot" or "spread";

        class Template { public ComboStep step; public WeaponDef src; public string move; public float hit; }

        static List<Template> templates;
        static List<Template> Templates => templates ??= DB.Weapons.Values
            .SelectMany(w => w.combo.Select(s => new Template { step = s, src = w, move = Classify(s), hit = w.baseDamage * s.damage }))
            .ToList();

        static float Median(IEnumerable<float> v)
        {
            var a = v.OrderBy(x => x).ToArray();
            return a.Length == 0 ? 0 : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
        }

        // ───────────────────────── Power (same measure for built-in and designed weapons) ─────────────────────────

        static float Coverage(ComboStep s) => s.projectile
            ? 2.4f * Mathf.Pow(Mathf.Max(1, s.projectileCount), 0.6f)
            : s.range * (s.spin || s.arc >= 300 ? 1.8f : Mathf.Sqrt(Mathf.Max(20, s.arc) / 150f));

        public static float Power(WeaponDef w) =>
            w.combo == null || w.combo.Length == 0 ? 0 : w.combo.Average(s => w.baseDamage * s.damage * Coverage(s) / Mathf.Max(0.05f, s.windup + s.recovery));

        public static float Budget => Median(DB.Weapons.Values.Select(Power));

        // ───────────────────────── Build ─────────────────────────

        static readonly Dictionary<string, WeaponDef> cache = new Dictionary<string, WeaponDef>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); templates = null; }

        public static bool IsDesigned(WeaponLook look) => look != null && look.moves != null && look.moves.Count > 0;

        /// <summary>Weapon built from a pack (cached per look). null when the look has no attack parts (classic weapon).</summary>
        public static WeaponDef For(DesignPack p)
        {
            if (p == null || !IsDesigned(p.weapon)) return null;
            var key = JsonUtility.ToJson(p.weapon) + JsonUtility.ToJson(p.attack);
            if (cache.TryGetValue(key, out var w)) return w;
            return cache[key] = Build(p.weapon, p.attack);
        }

        static Template Pick(string move, string weight)
        {
            var c = Templates.Where(t => t.move == move).OrderBy(t => t.hit).ToList();
            if (c.Count == 0) c = Templates.Where(t => t.move == "slash").OrderBy(t => t.hit).ToList();
            return weight == "light" ? c[0] : weight == "heavy" ? c[c.Count - 1] : c[c.Count / 2];
        }

        static WeaponDef Build(WeaponLook look, AttackLook atk)
        {
            var parts = look.moves.Select(m => Pick(m, look.weight)).ToList();
            float B = Median(DB.Weapons.Values.Select(x => x.baseDamage));
            var first = parts[0].src;
            bool shoots = parts.Any(t => Ranged(t.move));
            var w = new WeaponDef
            {
                // kind only drives a few presentation special cases (aiming pose, clones) and the save slot
                kind = shoots ? WeaponKind.Crossbow : first.kind == WeaponKind.SwordShield ? WeaponKind.Katana : first.kind,
                name = look.name ?? "이름 없는 무기",
                desc = "",
                tags = string.Join(" ", new[] { shoots ? "RANGED PROJECTILE" : "MELEE", parts.Any(t => !Ranged(t.move)) && shoots ? "MELEE" : null, "DESIGN" }.Where(s => s != null)),
                slashColor = string.IsNullOrEmpty(atk.slash) ? first.slashColor : Pal.Hex(atk.slash),
                slashCore = string.IsNullOrEmpty(atk.slashCore) ? first.slashCore : Pal.Hex(atk.slashCore),
                baseDamage = B,
                comboReset = Median(DB.Weapons.Values.Select(x => x.comboReset)),
                moveSpeedMul = Median(DB.Weapons.Values.Select(x => x.moveSpeedMul)),
                attackRange = parts.Min(t => t.src.attackRange),
                combo = parts.Select(t =>
                {
                    var s = JsonUtility.FromJson<ComboStep>(JsonUtility.ToJson(t.step));
                    s.damage = t.hit / B;
                    return s;
                }).ToArray(),
                skills = Skills(look, first),
                element = Element.None,
                sfxSwing = first.sfxSwing,
                sfxHit = first.sfxHit,
            };
            float k = Budget / Mathf.Max(0.001f, Power(w));
            foreach (var s in w.combo) s.damage *= k;
            w.origin = w;
            return w;
        }

        /// <summary>QWER: three basic skills + one ultimate from the pool; anything invalid falls back to the first part's weapon.</summary>
        static string[] Skills(WeaponLook look, WeaponDef fallback)
        {
            var basics = Basics;
            var s = (look.skills ?? new string[0]).Take(3).Where(id => basics.Contains(id)).Distinct().ToList();
            foreach (var id in fallback.skills.Where(id => basics.Contains(id))) if (s.Count < 3 && !s.Contains(id)) s.Add(id);
            foreach (var id in basics) if (s.Count < 3 && !s.Contains(id)) s.Add(id);
            var ult = look.skills != null && look.skills.Length >= 4 && Ultimates.Contains(look.skills[3]) ? look.skills[3]
                    : fallback.skills.FirstOrDefault(id => Ultimates.Contains(id)) ?? Ultimates[0];
            s.Add(ult);
            return s.ToArray();
        }

        /// <summary>"베기 → 베기 → 회전" for the studio.</summary>
        public static string Describe(WeaponLook look)
        {
            if (!IsDesigned(look)) return "기본 무기";
            string Ko(string m) => m switch
            {
                "slash" => "베기", "wide" => "넓게 베기", "thrust" => "찌르기", "spin" => "회전", "smash" => "내려치기", "shot" => "발사", "spread" => "퍼뜨려 쏘기", _ => m,
            };
            string weight = look.weight == "light" ? "가볍게" : look.weight == "heavy" ? "묵직하게" : "보통";
            return string.Join(" → ", look.moves.Select(Ko)) + $" ({weight})";
        }
    }
}
