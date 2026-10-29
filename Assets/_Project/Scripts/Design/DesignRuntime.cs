using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// The design pack currently drawn. Activating one resets Art's sprite cache and registers sprite overrides
    /// (weapon_&lt;kind&gt;, bolt, knife); world props and enemies are replaced or recolored as Art builds them (Restyle).
    /// Pictures come from DesignStore (generated + pixelized); without them the local shape/recolor fallback is used.
    /// Numbers never come from here: a designed weapon is built by DesignCombat from the existing weapon assets.
    /// </summary>
    public static class DesignRuntime
    {
        public static DesignPack Active { get; private set; } = DesignPack.Default();
        static string activeJson;
        static readonly Dictionary<string, SkillDef> skillNames = new Dictionary<string, SkillDef>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = DesignPack.Default(); activeJson = null; skillNames.Clear(); effects.Clear(); activeWeapon = null; tilePixels = null; }

        /// <summary>Bumped by every Activate: caches built from the active look (scenery lists…) compare against it.</summary>
        public static int Version { get; private set; }

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
            // Normal mode keeps the hand-made world and enemies whatever a pack says: only the hero and weapon are designed
            if (Session.Mode == GameMode.Normal && (!p.world.IsDefault || !p.enemies.Identity || p.enemies.art.Count > 0 || p.enemies.names.Count > 0))
            {
                p = p.Clone();
                p.world = new WorldLook();
                p.enemies = new EnemyLook();
            }
            var json = JsonUtility.ToJson(p);
            if (json == activeJson) return false;
            Active = p;
            activeJson = json;
            Art.ResetLooks();
            MapCanvas.ClearCache();      // the map paints with the ground colours of the look
            skillNames.Clear();
            effects.Clear();
            activeWeapon = DesignCombat.For(p);
            CacheTiles(p.world);
            Version++;

            // Generated weapon pictures point right with the grip at the left edge
            var wtex = DesignStore.Image(p.weapon.image);
            var weapon = wtex != null ? DesignStore.Sprite(p.weapon.image, new Vector2(2f / wtex.width, 0.5f)) : DesignArt.Weapon(p.weapon);
            if (weapon != null) Art.Override("weapon_" + Chassis(p), weapon);
            var shot = DesignStore.Sprite(p.attack.projectile.image, new Vector2(0.5f, 0.5f)) ?? DesignArt.Projectile(p.attack.projectile, p.weapon);
            if (shot != null) { Art.Override("bolt", shot); Art.Override("knife", shot); }
            return true;
        }

        /// <summary>
        /// Game scene: the look for this life (the hero's own pack, or the world's look with a classic hero) + the weapon skin.
        /// Returns true when the look changed, so already-built chunks must be rebuilt.
        /// </summary>
        public static bool BeginRun(CostumeDef costume, WeaponDef weapon)
        {
            var pack = DesignStore.PackOfCostume(costume.id) ?? DesignStore.WorldOnly(DesignStore.OfWorld(Session.World));
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
            : new Vector2(4.5f / Art.PPU, 11.5f / Art.PPU);

        public static Color32? Trail => string.IsNullOrEmpty(Active.attack.trail) ? null : Pal.Hex(Active.attack.trail);
        public static bool ShotSpins => Active.attack.projectile.spin;

        public static string EnemyName(string id, string fallback) => Active.enemies.NameOf(id) ?? fallback;

        // ───────────────────────── World / enemy pictures (called by Art) ─────────────────────────

        static bool WorldKey(string key) =>
            key.StartsWith("tree_") || key.StartsWith("rock_") || key.StartsWith("bush_") || key.StartsWith("flower_") || key.StartsWith("tuft_");

        static int Variant(string key)
        {
            int cut = key.LastIndexOf('_');
            int.TryParse(cut >= 0 ? key.Substring(cut + 1) : "0", out int v);
            return v + key.Length;   // different keys pick different variants
        }

        static Sprite Picture(string id, Sprite original)
        {
            var tex = DesignStore.Image(id);
            if (tex == null) return null;
            // Keep the hand-made sprite's foot line so placement and footprints still line up
            float py = original != null && original.rect.height > 0 ? original.pivot.y / original.rect.height : 0;
            return DesignStore.Sprite(id, new Vector2(0.5f, Mathf.Max(1f / tex.height, py * 0.5f)));
        }

        public static Sprite Restyle(string key, Sprite s)
        {
            if (s == null) return null;
            if (key.StartsWith("enemy_"))
            {
                var e = Active.enemies;
                // enemy_<sprite>_<frame>: a redrawn enemy replaces the hand-made one, otherwise it can be recolored
                int cut = key.LastIndexOf('_');
                var sprite = key.Substring(6, cut - 6);
                int.TryParse(key.Substring(cut + 1), out int frame);
                var id = DB.Enemies.Values.FirstOrDefault(d => d.sprite == sprite)?.id ?? sprite;
                var art = e.art.FirstOrDefault(a => a.id == id);
                var idle = DesignStore.Image(art?.image);
                if (idle != null) return DesignArt.EnemyFrame(idle, DesignStore.Image(art.image2), DesignStore.Image(art.image3), frame, DesignStore.Image(art.image4));
                return e.Identity ? s : DesignArt.Recolor(s, c => DesignArt.Shift(c, e));
            }
            var w = Active.world;
            if (w.IsDefault) return s;
            // Hand-made nature only shows up where the designed layout doesn't place scenery itself: recolour it to fit
            if (WorldKey(key))
            {
                bool leafy = !key.StartsWith("rock_") && !key.StartsWith("flower_") && !string.IsNullOrEmpty(w.foliage);
                var leaf = leafy ? Pal.Hex(w.foliage) : default;
                return DesignArt.Recolor(s, c => DesignArt.Shift(leafy ? DesignArt.Foliage(c, leaf) : c, w));
            }
            if (w.HasTown)
            {
                if (key.StartsWith("house_")) return Picture(w.townImages[Variant(key) % 2], s) ?? s;
                if (key == "prop_lamp") return Picture(w.townImages[2], s) ?? s;
                if (key == "prop_well") return Picture(w.townImages[3], s) ?? s;
            }
            // Everything else built by hand (fences, stalls, crates…) takes the world's palette so it doesn't stick out
            if ((key.StartsWith("prop_") || key.StartsWith("house_")) && !w.Identity) return DesignArt.Recolor(s, c => DesignArt.Shift(c, w));
            return s;
        }

        static int TileSlot(Ground g) => g switch
        {
            Diverse.Ground.Grass or Diverse.Ground.DarkGrass => 0,
            Diverse.Ground.Path or Diverse.Ground.Plaza => 1,
            Diverse.Ground.Water or Diverse.Ground.DeepWater => 2,
            Diverse.Ground.Sand or Diverse.Ground.Snow or Diverse.Ground.Swamp or Diverse.Ground.Ash => 3,
            _ => -1,
        };

        static readonly Dictionary<string, Color32> tileAverage = new Dictionary<string, Color32>();

        static Color32 Average(string id)
        {
            if (tileAverage.TryGetValue(id, out var c)) return c;
            var tex = DesignStore.Image(id);
            if (tex == null) return Pal.White;
            float r = 0, g = 0, b = 0;
            var px = tex.GetPixels32();
            foreach (var p in px) { r += p.r; g += p.g; b += p.b; }
            return tileAverage[id] = new Color32((byte)(r / px.Length), (byte)(g / px.Length), (byte)(b / px.Length), 255);
        }

        /// <summary>
        /// Ground colours (map, minimap, edges between grounds). With generated textures they are the textures' real average
        /// colour, so the map matches the world; otherwise the AI's hex colours, then the palette shift.
        /// </summary>
        public static void Ground(Ground g, ref Color32 a, ref Color32 b, ref Color32 c)
        {
            var w = Active.world;
            if (w.IsDefault) return;
            int slot = TileSlot(g);
            if (w.HasTiles && slot >= 0)
            {
                var avg = Average(w.tileImages[slot * 2]);
                if (g == Diverse.Ground.DarkGrass || g == Diverse.Ground.DeepWater || g == Diverse.Ground.Swamp) avg = Pal.Mul(avg, 0.82f);
                a = avg; b = Pal.Mul(avg, 0.85f); c = Pal.Lerp(avg, Pal.White, 0.15f);
                return;
            }
            string over = slot switch { 0 => w.grass, 1 => g == Diverse.Ground.Path ? w.path : null, 2 => w.water, 3 => w.rough, _ => null };
            if (!string.IsNullOrEmpty(over))
            {
                var baseCol = Pal.Hex(over);
                if (g == Diverse.Ground.DarkGrass) baseCol = Pal.Mul(baseCol, 0.78f);
                if (g == Diverse.Ground.DeepWater) baseCol = Pal.Mul(baseCol, 0.82f);
                if (g == Diverse.Ground.Swamp || g == Diverse.Ground.Ash) baseCol = Pal.Mul(baseCol, 0.85f);
                a = baseCol; b = Pal.Mul(baseCol, 0.9f); c = Pal.Lerp(baseCol, Pal.White, 0.16f);
            }
            a = DesignArt.Shift(a, w); b = DesignArt.Shift(b, w); c = DesignArt.Shift(c, w);
        }

        /// <summary>
        /// Generated ground texture at world pixel (gx, gy): two variants per ground, mixed in large soft patches so the
        /// repetition of one swatch never shows; each swatch is tiled mirrored (seamless by construction).
        /// false = no texture for this ground (the colour dither is used).
        /// </summary>
        public static bool GroundTexel(Ground g, int gx, int gy, out Color32 col)
        {
            col = default;
            int slot = TileSlot(g);
            if (tilePixels == null || slot < 0) return false;
            // This runs for every ground pixel of every chunk (65k per chunk): cached pixel arrays, and the variant patch
            // noise once per 4x4 block (consecutive pixels of a row share it)
            int bx = gx >> 2, by = gy >> 2;
            if (bx != patchX || by != patchY)
            {
                patchX = bx; patchY = by;
                patchB = Noise.Perlin(bx / 4f, by / 4f, 4711, 0.09f) + (Hash.F(bx, by, 4712) - 0.5f) * 0.12f > 0.5f;
            }
            var px = tilePixels[slot * 2 + (patchB ? 1 : 0)];
            if (px == null) return false;
            int n = tileSize;
            int x = (int)((uint)gx % (uint)(n * 2)), y = (int)((uint)gy % (uint)(n * 2));
            if (x >= n) x = n * 2 - 1 - x;
            if (y >= n) y = n * 2 - 1 - y;
            col = px[y * n + x];
            if (g == Diverse.Ground.DarkGrass || g == Diverse.Ground.DeepWater || g == Diverse.Ground.Swamp) col = Pal.Mul(col, 0.82f);
            return true;
        }

        // Ground texture pixels of the active look (filled by Activate; null = no generated ground)
        static Color32[][] tilePixels;
        static int tileSize;
        static int patchX = int.MinValue, patchY;
        static bool patchB;

        static void CacheTiles(WorldLook w)
        {
            tilePixels = null;
            patchX = int.MinValue;
            if (!w.HasTiles) return;
            var tiles = w.tileImages.Select(DesignStore.Image).ToArray();
            if (tiles.Any(t => t == null) || tiles.Any(t => t.width != tiles[0].width || t.height != tiles[0].width)) return;
            tileSize = tiles[0].width;
            tilePixels = tiles.Select(t => t.GetPixels32()).ToArray();
        }

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

        public static Sprite[] SwingFx(Color32 col, int radiusPx) => Mine(col) ? EffectOf(0, DesignArt.EffectKind.Swing, radiusPx * 2) : null;
        public static Sprite[] ThrustFx(Color32 col, int lengthPx) => Mine(col) ? EffectOf(0, DesignArt.EffectKind.Swing, lengthPx / 2, 2f) : null;
        public static Sprite[] ImpactFx(Color32 col, int sizePx) => Mine(col) ? EffectOf(1, DesignArt.EffectKind.Impact, sizePx * 2) : null;
        public static Sprite[] TrailFx => EffectOf(2, DesignArt.EffectKind.Trail, 10);
        public static Sprite[] MuzzleFx => EffectOf(3, DesignArt.EffectKind.Muzzle, 16);

        // ───────────────────────── Ambient particles ─────────────────────────

        static float ambientT;

        /// <summary>Called every frame by Game: drifting snow / petals / bubbles… around the camera, as the world look asks.</summary>
        public static void TickAmbient(Vector2 center, float dt)
        {
            var w = Active.world;
            if (w.ambient == "none" || Fx.I == null) return;
            ambientT -= dt;
            if (ambientT > 0) return;
            // Fewer specks at lower effect settings; spread over the whole view (the wheel changes how much is visible)
            ambientT = (w.ambient == "rain" ? 0.02f : 0.07f) / GraphicsQuality.EffectScale;
            var col = string.IsNullOrEmpty(w.ambientColor) ? Pal.White : Pal.Hex(w.ambientColor);
            var view = CameraRig.I != null ? CameraRig.I.ViewRect() : new Rect(center.x - 12, center.y - 7, 24, 15);
            var p = new Vector2(UnityEngine.Random.Range(view.xMin, view.xMax), UnityEngine.Random.Range(view.yMin, view.yMax + 1));
            switch (w.ambient)
            {
                case "snow": Fx.I.Burst(p, col, 1, 0.8f, 3f, 0.4f, 1, 60, 270); break;
                case "rain": Fx.I.Burst(p, Pal.A(col, 180), 1, 9f, 0.6f, 6f, 1, 10, 255); break;
                case "petals":
                case "leaves": Fx.I.Burst(p, col, 1, 1.2f, 3f, 0.25f, 2, 90, 300); break;
                case "bubbles":
                case "notes":
                case "embers": Fx.I.Burst(p, col, 1, 1f, 2.6f, -0.35f, w.ambient == "embers" ? 1 : 2, 50, 90, false, true); break;
                case "sparkles": Fx.I.Burst(p, col, 1, 0.3f, 1.4f, 0, 1, 360, 0, false, true); break;
                case "dust": Fx.I.Burst(p, Pal.A(col, 150), 1, 1.5f, 2.5f, 0, 1, 40, 0); break;
            }
        }
    }
}
