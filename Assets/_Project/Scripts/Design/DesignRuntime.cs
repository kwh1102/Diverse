using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// The design pack currently drawn. Activating one resets Art's sprite cache and registers sprite overrides
    /// (weapon_&lt;kind&gt;, bolt, knife). The built-in world and enemies are never redrawn.
    /// Pictures come from DesignStore (generated + pixelized); without them the local shape fallback is used.
    /// Numbers never come from here: a designed weapon is built by DesignCombat from the existing weapon assets.
    /// </summary>
    public static class DesignRuntime
    {
        public static DesignPack Active { get; private set; } = DesignPack.Default();
        static string activeJson;
        static readonly Dictionary<string, SkillDef> skillNames = new Dictionary<string, SkillDef>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = DesignPack.Default(); activeJson = null; skillNames.Clear(); effects.Clear(); activeWeapon = null; }

        /// <summary>The WeaponKind this pack plays as: its designed weapon's internal kind, or the classic fallback.</summary>
        public static WeaponKind Chassis(DesignPack p)
        {
            var w = DesignCombat.For(p);
            if (w != null) return w.kind;
            return Enum.TryParse(p?.weapon?.baseWeapon, out WeaponKind k) ? k : WeaponKind.Greatsword;
        }

        /// <summary>Draw with this pack from now on. Returns false when it was already active (nothing to redraw).</summary>
        public static bool Activate(DesignPack p)
        {
            p ??= DesignPack.Default();
            var json = JsonUtility.ToJson(p);
            if (json == activeJson) return false;
            Active = p;
            activeJson = json;
            Art.ResetLooks();
            skillNames.Clear();
            effects.Clear();
            activeWeapon = DesignCombat.For(p);

            // Generated weapon pictures point right with the grip at the left edge
            var wtex = DesignStore.Image(p.weapon.image);
            var weapon = wtex != null ? DesignStore.Sprite(p.weapon.image, new Vector2(2f / wtex.width, 0.5f)) : DesignArt.Weapon(p.weapon);
            if (weapon != null) Art.Override("weapon_" + Chassis(p), weapon);
            var shot = DesignStore.Sprite(p.attack.projectile.image, new Vector2(0.5f, 0.5f)) ?? DesignArt.Projectile(p.attack.projectile, p.weapon);
            if (shot != null) { Art.Override("bolt", shot); Art.Override("knife", shot); }
            return true;
        }

        /// <summary>
        /// Game scene: the look for this life (the hero's own pack, or the default look for a classic hero) + the weapon skin.
        /// Returns true when the look changed, so already-built chunks must be rebuilt.
        /// </summary>
        public static bool BeginRun(CostumeDef costume, WeaponDef weapon)
        {
            var pack = DesignStore.PackOfCostume(costume.id) ?? DesignPack.Default();
            bool changed = Activate(pack);
            Skin(weapon);
            return changed;
        }

        /// <summary>
        /// Name and slash colors of the per-run weapon copy. A designed weapon takes them from the active pack's build
        /// (a mid-life edit can rename/recolor it); a classic weapon gets its asset's. Numbers are never touched.
        /// </summary>
        public static void Skin(WeaponDef w)
        {
            var src = w.origin != null ? DesignCombat.For(Active) ?? w.origin : DB.W(w.kind);
            w.name = src.name;
            w.slashColor = src.slashColor;
            w.slashCore = src.slashCore;
        }

        /// <summary>QWER display names from the designed weapon (the skill itself is unchanged). Called by SkillDB.Get.</summary>
        public static SkillDef RenameSkill(SkillDef d)
        {
            if (d == null) return null;
            var w = Active.weapon;
            if (w.skills == null || w.skillNames == null) return d;
            int i = Array.IndexOf(w.skills, d.id);
            if (i < 0 || i >= w.skillNames.Length || string.IsNullOrEmpty(w.skillNames[i])) return d;
            if (!skillNames.TryGetValue(d.id, out var r))
                skillNames[d.id] = r = new SkillDef { id = d.id, name = w.skillNames[i], desc = d.desc, cooldown = d.cooldown, lockTime = d.lockTime, color = d.color, icon = d.icon };
            return r;
        }

        // ───────────────────────── Attack / hold points (visual) ─────────────────────────

        /// <summary>Where attacks visually come out, relative to the feet. Default = the old Pos + up * 0.45.</summary>
        public static Vector2 AttackOrigin(Vector2 dir) => Active.attack.origin switch
        {
            "Mouth" => new Vector2(dir.x < -0.05f ? -0.28f : 0.28f, 0.72f),
            "Eyes" => new Vector2(dir.x < -0.05f ? -0.3f : 0.3f, 0.9f),
            "Above" => new Vector2(0, 1.35f),
            _ => new Vector2(0, 0.45f),
        };

        public static bool HoldInMouth => Active.weapon.hold == "Mouth" && (DesignArt.HasWeapon(Active.weapon) || !string.IsNullOrEmpty(Active.weapon.image));
        public static bool WeaponHidden => Active.weapon.hold == "Hidden";

        /// <summary>Mouth position on the hero sprite (a generated picture is any shape: roughly the front of the head).</summary>
        public static Vector2 MouthAnchor(CostumeDef c) => c?.frames != null
            ? new Vector2(c.frames[0].width * 0.3f / Art.PPU, c.frames[0].height * 0.62f / Art.PPU)
            : new Vector2(6f / Art.PPU, 9.5f / Art.PPU);

        public static Color32? Trail => string.IsNullOrEmpty(Active.attack.trail) ? null : Pal.Hex(Active.attack.trail);
        public static bool ShotSpins => Active.attack.projectile.spin;

        // ───────────────────────── Generated effects (called by Art / Player / Projectile) ─────────────────────────

        static readonly Dictionary<string, Sprite[]> effects = new Dictionary<string, Sprite[]>();

        /// <summary>The active look's designed weapon (built once per Activate, not per hit).</summary>
        static WeaponDef activeWeapon;

        /// <summary>Is this colour the designed weapon's? (its swings, hits and skills use the generated effects)</summary>
        static bool Mine(Color32 col)
        {
            var w = activeWeapon;
            return w != null && w.slashColor.r == col.r && w.slashColor.g == col.g && w.slashColor.b == col.b;
        }

        static Sprite[] EffectOf(int slot, DesignArt.EffectKind kind, int widthPx, float stretch = 1)
        {
            var ids = Active.attack.effectImages;
            if (ids == null || ids.Length != 4) return null;
            var tex = DesignStore.Image(ids[slot]);
            if (tex == null) return null;
            widthPx = Mathf.Clamp(widthPx, 6, 96);
            bool flip = Active.attack.flipEffects;
            var key = $"{ids[slot]}_{widthPx}_{stretch}_{flip}";
            if (!effects.TryGetValue(key, out var f)) effects[key] = f = DesignArt.Effect(tex, kind, widthPx, stretch, flip);
            return f;
        }

        public static Sprite[] TravelFx(DesignPack pack)
        {
            if (string.IsNullOrEmpty(pack?.travelEffect)) return null;
            var key = "travel_" + pack.travelEffect;
            if (effects.TryGetValue(key, out var frames)) return frames;
            var tex = DesignStore.Image(pack.travelEffect);
            return tex == null ? null : effects[key] = DesignArt.Effect(tex, DesignArt.EffectKind.Impact, 24);
        }

        public static Sprite[] SwingFx(Color32 col, int radiusPx) => Mine(col) ? EffectOf(0, DesignArt.EffectKind.Swing, radiusPx * 2) : null;
        public static Sprite[] ThrustFx(Color32 col, int lengthPx) => Mine(col) ? EffectOf(0, DesignArt.EffectKind.Swing, lengthPx / 2, 2f) : null;
        public static Sprite[] ImpactFx(Color32 col, int sizePx) => Mine(col) ? EffectOf(1, DesignArt.EffectKind.Impact, sizePx * 2) : null;
        public static Sprite[] TrailFx => EffectOf(2, DesignArt.EffectKind.Trail, 10);
        public static Sprite[] MuzzleFx => EffectOf(3, DesignArt.EffectKind.Muzzle, 16);
    }
}
