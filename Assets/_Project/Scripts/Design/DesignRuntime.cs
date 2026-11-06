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
        public static DesignPack CombatPack { get; private set; } = DesignPack.Default();
        static string activeJson;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = DesignPack.Default(); CombatPack = DesignPack.Default(); activeJson = null; effects.Clear(); activeWeapon = null; }

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
            CombatPack = p;
            activeJson = json;
            Art.ResetLooks();
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
        public static void EquipWeapon(DesignPack pack, WeaponDef weapon)
        {
            CombatPack = pack ?? DesignPack.Default();
            Art.ResetLooks(); effects.Clear();
            activeWeapon = pack != null ? DesignCombat.For(pack) : null;
            if (pack == null) return;
            var tex = DesignStore.Image(pack.weapon.image);
            var sprite = tex != null ? DesignStore.Sprite(pack.weapon.image, new Vector2(2f / tex.width, .5f)) : DesignArt.Weapon(pack.weapon);
            if (sprite != null) Art.Override("weapon_" + weapon.kind, sprite);
            var shot = DesignStore.Sprite(pack.attack.projectile.image, new Vector2(.5f,.5f)) ?? DesignArt.Projectile(pack.attack.projectile, pack.weapon);
            if (shot != null) { Art.Override("bolt", shot); Art.Override("knife", shot); }
        }

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
            var src = w.origin != null ? DesignCombat.For(CombatPack) ?? w.origin : DB.W(w.kind);
            w.name = src.name;
            w.slashColor = src.slashColor;
            w.slashCore = src.slashCore;
        }

        // ───────────────────────── Attack / hold points (visual) ─────────────────────────

        /// <summary>Where attacks visually come out, relative to the feet. Default = the old Pos + up * 0.45.</summary>
        public static Vector2 AttackOrigin(Vector2 dir) => CombatPack.attack.origin switch
        {
            "Mouth" => new Vector2(dir.x < -0.05f ? -0.28f : 0.28f, 0.72f),
            "Eyes" => new Vector2(dir.x < -0.05f ? -0.3f : 0.3f, 0.9f),
            "Above" => new Vector2(0, 1.35f),
            _ => new Vector2(0, 0.45f),
        };

        public static bool HoldInMouth => CombatPack.weapon.hold == "Mouth" && (DesignArt.HasWeapon(CombatPack.weapon) || !string.IsNullOrEmpty(CombatPack.weapon.image));
        public static bool WeaponHidden => CombatPack.weapon.hold == "Hidden";

        /// <summary>Mouth position on the hero sprite (a generated picture is any shape: roughly the front of the head).</summary>
        public static Vector2 MouthAnchor(CostumeDef c) => c?.frames != null
            ? new Vector2(c.frames[0].width * 0.3f / Art.PPU, c.frames[0].height * 0.62f / Art.PPU)
            : new Vector2(6f / Art.PPU, 9.5f / Art.PPU);

        public static Color32? Trail => string.IsNullOrEmpty(CombatPack.attack.trail) ? null : Pal.Hex(CombatPack.attack.trail);
        public static bool ShotSpins => CombatPack.attack.projectile.spin;

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
            var ids = CombatPack.attack.effectImages;
            if (ids == null || (ids.Length != 4 && ids.Length != 16)) return null;
            if (ids.Length == 16) return AnimatedEffect(slot,widthPx,stretch);
            var tex = DesignStore.Image(ids[slot]);
            if (tex == null) return null;
            widthPx = Mathf.Clamp(widthPx, 6, 96);
            bool flip = CombatPack.attack.flipEffects;
            var key = $"{ids[slot]}_{widthPx}_{stretch}_{flip}";
            if (!effects.TryGetValue(key, out var f)) effects[key] = f = DesignArt.Effect(tex, kind, widthPx, stretch, flip);
            return f;
        }

        static Sprite[] AnimatedEffect(int slot,int widthPx,float stretch)
        {
            var ids=CombatPack.attack.effectImages;bool flip=CombatPack.attack.flipEffects;
            string key=string.Join(",",ids)+":"+slot+":"+widthPx+":"+stretch+":"+flip;
            if(effects.TryGetValue(key,out var found))return found;
            var result=new Sprite[4];
            for(int f=0;f<4;f++)
            {
                var tex=DesignStore.Image(ids[slot*4+f]);if(tex==null)return null;
                int w=Mathf.Clamp(Mathf.RoundToInt(widthPx*stretch),6,96),h=Mathf.Max(4,Mathf.RoundToInt(widthPx*tex.height/(float)tex.width));
                var cv=new PixelCanvas(w+6,h+6);var px=tex.GetPixels32();
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {int sx=(flip?w-1-x:x)*tex.width/w;cv.Px[(y+3)*cv.W+x+3]=px[y*tex.height/h*tex.width+sx];}
                result[f]=cv.ToSpriteCentered();
            }
            return effects[key]=result;
        }

        public static Sprite[] TravelFx(DesignPack pack)
        {
            if (string.IsNullOrEmpty(pack?.travelEffect)) return null;
            var key = "travel_" + pack.travelEffect;
            if (effects.TryGetValue(key, out var frames)) return frames;
            var tex = DesignStore.Image(pack.travelEffect);
            return tex == null ? null : effects[key] = DesignArt.Effect(tex, DesignArt.EffectKind.Impact, 24);
        }

        public static Sprite[] SwingFx(Color32 col, int radiusPx) => Mine(col) && CombatPack.attack.effectFrames == 4 ? EffectOf(0, DesignArt.EffectKind.Swing, radiusPx * 2) : null;
        public static Sprite[] ThrustFx(Color32 col, int lengthPx) => null; // A thrust uses its native directional stroke, never a stretched crescent.
        public static Sprite[] ImpactFx(Color32 col, int sizePx) => Mine(col) ? EffectOf(1, DesignArt.EffectKind.Impact, sizePx * 2) : null;
        public static Sprite[] TrailFx => EffectOf(2, DesignArt.EffectKind.Trail, 10);
        public static Sprite[] MuzzleFx => EffectOf(3, DesignArt.EffectKind.Muzzle, 16);
    }
}
