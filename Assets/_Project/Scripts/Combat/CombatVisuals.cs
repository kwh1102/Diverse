using UnityEngine;

namespace Diverse
{
    /// <summary>Project-authored motion, with cached icon-palette variants. Never generates effect geometry or calls AI.</summary>
    public static class CombatVisuals
    {
        public static CombatVisualLibrary Library => DB.Asset.combatVisuals;
        public static Color Tint(Color color) => Color.Lerp(Color.white,color,.3f);
        public static Sprite[] BurstFrames(string element) => element switch {
            "Fire" => Library?.fire, "Frost" => Library?.ice,
            "Arcane" or "Void" or "Shadow" or "Lightning" or "Holy" or "Poison" or "Blood" => Library?.arcane,
            _ => Library?.earth };
        public static Sprite[] Frames(ActiveSkillData skill, ActiveStep step, int kind)
        {
            var v=Library;if(v==null)return null;
            if(kind==1)return step.effect is "heal"?Pick(v.skillHeal,v.ring):step.effect is "shield"?Pick(v.skillGuard,v.ring):
                step.effect=="damage"?step.element=="None"?Pick(v.skillImpact,v.contact):SkillBurst(step.element):Pick(v.skillPull,v.ring);
            if (kind == 3) return skill.weaponType switch
            {
                WeaponType.Gun => Pick(v.gunFlash, v.skillMuzzle),
                WeaponType.Staff => Pick(v.crystalHit, v.skillMuzzle),
                WeaponType.Dagger => Pick(v.daggerHit, v.skillMuzzle),
                _ => Pick(v.skillMuzzle, v.contact),
            };
            if(kind==2)return step.delivery=="projectile"?v.bolt:Pick(v.skillDash,v.trail);
            return step.delivery switch {
                "arc" => skill.weaponType == WeaponType.Staff ? SkillBurst(step.element) :
                    skill.weaponType == WeaponType.Dagger ? Pick(v.daggerHit, v.skillSlash) : Pick(v.skillSlash, v.slash),
                "line" => Pick(v.skillThrust,v.thrust),
                "nova"=>step.effect=="heal"?Pick(v.skillHeal,v.ring):step.effect=="shield"?Pick(v.skillGuard,v.ring):step.effect=="pull"?Pick(v.skillPull,v.ring):SkillBurst(step.element),
                "field"=>step.effect=="heal"?Pick(v.skillHeal,v.ring):step.effect=="shield"?Pick(v.skillGuard,v.ring):step.effect=="pull"?Pick(v.skillPull,v.ring):
                    step.element=="None"?Pick(v.skillField,v.ring):SkillBurst(step.element),
                "trap"=>step.element=="None"?Pick(v.skillTrap,v.shards):SkillBurst(step.element),
                "guard"=>Pick(v.skillGuard,v.ring),"wall"=>Pick(v.skillShock,v.ring),
                "projectile"=>Pick(v.skillMuzzle,v.contact),
                "dash" or "retreat" or "blink"=>Pick(v.skillDash,v.cut),_=>null };
        }
        static Sprite[] Pick(Sprite[] preferred,Sprite[] fallback) => preferred?.Length>0?preferred:fallback;
        public static Sprite[] SkillBurst(string element)
        {
            var v=Library;if(v==null)return null;
            return element switch {
                "Fire"=>Pick(v.skillFire,v.fire),"Frost"=>Pick(v.skillIce,v.ice),
                "Lightning"=>Pick(v.skillLightning,v.arcane),"Wind"=>Pick(v.skillWind,v.ring),
                "Poison" or "Blood"=>Pick(v.skillPoison,v.earth),"Holy"=>Pick(v.skillHoly,v.contact),
                "Arcane" or "Void" or "Shadow"=>Pick(v.skillArcane,v.arcane),_=>Pick(v.skillEarth,v.earth) };
        }
        public static Sprite[] ProjectileFrames(ActiveSkillData skill,ActiveStep step)
        {
            var v=Library;if(v==null)return null;
            return skill.weaponType switch {
                WeaponType.Bow=>Pick(v.skillArrow,v.bolt),WeaponType.Gun=>Pick(v.skillBullet,v.bolt),
                WeaponType.Sword or WeaponType.Dagger=>Pick(v.skillBlade,v.bolt),
                _=>step.pattern is "pierce" or "seek"?Pick(v.skillSpear,v.bolt):Pick(v.skillOrb,v.bolt) };
        }
        public static bool Basic(WeaponType type, Vector2 at, Vector2 dir, float reach, ComboStep step, float scale, bool finisher, Color color, Transform follow)
        {
            var v = Library;
            if (v == null) return false;
            float angle = dir.Angle();
            int pixels = Mathf.Max(4, Mathf.RoundToInt(reach * Art.PPU));
            float size = Mathf.Min(1, scale) * v.basicSweepScale;
            var generated = step.thrust ? null : DesignRuntime.SwingFx((Color32)color, pixels);
            Color32 shade = type == WeaponType.Staff ? Pal.Lerp(Pal.Hex("a47c50"), (Color32)color, .36f) :
                Pal.Lerp(Pal.Hex("8d8c9d"), (Color32)color, .45f);
            Color32 edge = type == WeaponType.Staff ? Pal.Hex("ffe2ae") : Pal.White;
            var main = step.thrust ? Art.WeaponThrust(shade, edge, pixels, type, v.basicStrokeWeight) :
                Art.WeaponSweep(shade, edge, pixels, Mathf.Min(step.arc, 359), step.swingDir, type, v.basicStrokeWeight, true);
            if (generated != null)
            {
                var accent = Color.white; accent.a = type == WeaponType.Staff ? .42f : .62f;
                Fx.I?.Play(generated, at, angle, generated.Length / v.cutSeconds, size * .86f, accent, false, 0, follow, step.swingDir < 0);
            }
            float fps = main.Length / v.basicSweepSeconds;
            Fx.I?.Play(main, at, angle, fps, size, null, false, Art.SortY(at.y) - 1, follow);
            if (step.spin && type == WeaponType.Staff)
                Fx.I?.Play(main, at, angle + 160, v.framesPerSecond, size * .76f, new Color(1, 1, 1, .55f),
                    false, Art.SortY(at.y) - 2, follow, delay: v.cutSeconds * .2f);
            if (finisher && type == WeaponType.Staff && step.thrust)
                Fx.I?.Play(v.skillShock, at + dir * reach, 0, v.framesPerSecond, .44f, Tint(color), false, Art.SortY(at.y) - 2);
            return true;
        }

        public static void BasicMuzzle(WeaponType type, Vector2 at, Vector2 dir, Color color)
        {
            var v = Library;
            var frames = type switch {
                WeaponType.Gun when v != null => Pick(v.gunFlash, v.skillMuzzle),
                WeaponType.Bow => Art.BowRelease((Color32)color),
                _ => v?.skillMuzzle
            };
            if (frames == null || frames.Length == 0)
            {
                Fx.I?.Burst(at, color, 4, 4, .16f, dirDeg: dir.Angle());
                return;
            }
            float seconds = v != null ? v.muzzleSeconds : .14f;
            float pixels = v != null ? v.basicMuzzlePixels : 18;
            Fx.I?.Play(frames, at, dir.Angle(), frames.Length / seconds, pixels / Mathf.Max(1, frames[0].rect.width - 6), Tint(color));
            if (DesignRuntime.MuzzleFx is { Length: > 0 } material)
                Fx.I?.Play(material, at, dir.Angle(), material.Length / seconds,
                    pixels * .58f / Mathf.Max(1, material[0].rect.width - 6), new Color(1, 1, 1, .45f));
        }

        public static HitVisual HitFor(WeaponType type, bool ranged, bool thrust = false)
        {
            if (ranged) return type switch {
                WeaponType.Bow => HitVisual.Arrow, WeaponType.Gun => HitVisual.Bullet,
                WeaponType.Staff => HitVisual.Arcane, _ => HitVisual.Pierce
            };
            if (type == WeaponType.Staff) return HitVisual.Blunt;
            if (thrust) return HitVisual.Pierce;
            return type == WeaponType.Dagger ? HitVisual.Dagger : HitVisual.Blade;
        }

        public static void BasicFlight(Projectile shot, WeaponType type)
        {
            var v = Library;
            if (v == null) return;
            var frames = type switch
            {
                WeaponType.Gun => v.skillBullet, WeaponType.Bow => v.skillArrow,
                WeaponType.Staff => v.skillOrb, _ => v.skillBlade,
            };
            var look = DesignRuntime.CombatPack.attack.projectile;
            if (string.IsNullOrEmpty(look.image) && look.shape == "builtin" && look.pixels?.Length is not > 0)
                shot.SetFlightVisual(frames, type == WeaponType.Gun ? .62f : .72f, v.framesPerSecond, Color.white);
            shot.ScaleFlightVisual(v.basicProjectileScale);
        }

        public static void WeaponContact(HitVisual hit, Vector2 at, Vector2 dir, Color32 color, bool crit, bool hasMain)
        {
            var v = Library;
            if (v == null) return;
            Sprite[] frames = hit switch {
                HitVisual.Blunt => Pick(v.bluntHit, v.shards), HitVisual.Dagger => Pick(v.daggerHit, v.cutHit),
                HitVisual.Arcane => Pick(v.crystalHit, v.skillArcane), HitVisual.Blade => v.contact,
                HitVisual.Bullet => Pick(v.gunFlash, v.skillImpact), HitVisual.Arrow => v.skillImpact,
                _ => v.cutHit
            };
            if (frames?.Length > 0)
            {
                float scale = hit == HitVisual.Blunt ? .8f : hit == HitVisual.Dagger ? .62f : .54f;
                if (hasMain) scale *= .78f;
                var tint = hit is HitVisual.Blunt or HitVisual.Bullet ? (Color)Pal.Hex("ffe8bc") : Tint(color);
                Fx.I?.Play(frames, at, dir.Angle(), frames.Length / v.cutSeconds, scale * v.weaponContactScale * (crit ? 1.2f : 1),
                    tint, false, Art.SortY(at.y) + 34);
            }
            if (hit is HitVisual.Blunt or HitVisual.Bullet or HitVisual.Arcane)
                Fx.I?.Burst(at, color, crit ? 8 : 5, 3.8f, .20f, size: 2, spreadDeg: 104, dirDeg: dir.Angle(), ground: hit == HitVisual.Blunt);
        }

        public static void Wake(Projectile shot, WeaponType type, string pattern, Color color)
        {
            var v = Library;
            if (v == null) return;
            var (width, duration) = type switch {
                WeaponType.Bow => (v.arrowWakePixels, v.arrowWakeSeconds),
                WeaponType.Gun => (v.bulletWakePixels, v.bulletWakeSeconds),
                WeaponType.Staff => (v.magicWakePixels, v.magicWakeSeconds),
                _ => (v.bladeWakePixels, v.bladeWakeSeconds)
            };
            if (pattern is "seek" or "curve" or "return") duration *= 1.35f;
            shot.trail = false;
            Fx.I?.TrackProjectile(shot, color, width, duration);
        }

        public static void Trace(Vector2 from, Vector2 to, Color color)
        {
            var v = Library;
            if (v == null || (to - from).sqrMagnitude <= .01f) return;
            Vector2 delta = to - from;
            var trace = v.thrust;
            Fx.I?.Play(trace, (from + to) * .5f + Vector2.up * .32f, delta.Angle(), trace.Length / v.cutSeconds,
                delta.magnitude * Art.PPU / (trace[0].rect.width - 6), Tint(color));
        }

        public static void ProjectileTurn(Vector2 at, Vector2 dir, Color32 color)
        {
            var v = Library;
            if (v == null) return;
            Fx.I?.Play(v.cut, at, dir.Angle(), v.cut.Length / v.cutSeconds, .42f, Tint(color));
            Fx.I?.Burst(at, color, 4, 3, .15f, spreadDeg: 90, dirDeg: dir.Angle());
        }

        public static void ImpactPulse(Vector2 at, float radius, ActiveSkillData skill, string element)
        {
            var v = Library;
            if (v == null) return;
            var authored = element == "None" ? Pick(v.crystalHit, v.skillShock) : SkillBurst(element);
            var frames = ActiveSkillPalette.Frames(skill, authored);
            float scale = radius * 2 * Art.PPU / (frames[0].rect.width - 6);
            Fx.I?.Play(frames, at, 0, frames.Length / v.skillAfterSeconds, scale, null,
                false, Art.SortY(at.y) + 3);
        }

        public static void UltimateRelease(Player player, Color32 color)
        {
            var v = Library;
            if (v == null) return;
            var at = player.Pos;
            Fx.I?.Play(v.skillShock, at, 0, v.skillShock.Length / v.skillAfterSeconds,
                .62f, Tint(color), false, Art.SortY(at.y) - 2);
            Fx.I?.Burst(at, color, 8, 3.4f, .2f, size: 2, ground: true);
        }

        public static void Link(Vector2 from, Vector2 at, float radius, Color color, string action, bool ultimate)
        {
            var v = Library;
            if (v == null) return;
            Trace(from, at, color);
            var frames = action == "draw" ? v.skillPull : action == "echo" ? v.skillLightning : Pick(v.crystalHit, v.skillArcane);
            float size = radius * 2 * Art.PPU / (frames[0].rect.width - 6);
            Fx.I?.Play(frames, at, 0, frames.Length / v.skillAfterSeconds, size, Tint(color), false, Art.SortY(at.y) + 1);
            if (ultimate)
                Fx.I?.Burst(at, color, 13, 4.5f, .27f, size: 2, ground: true);
        }

        public static void Charge(Player player, Color color)
        {
            var v = Library;
            if (v == null) return;
            Fx.I?.Play(v.skillPull, player.Pos, 0, v.skillPull.Length / v.skillMainSeconds, .48f, Tint(color),
                false, Art.SortY(player.Pos.y) - 2, player.transform);
        }

        public static void DirectStroke(Vector2 from, Vector2 to, Color32 color)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude <= Mathf.Epsilon) return;
            var frames = Art.DirectStroke(color);
            var animation = Fx.I?.Play(frames, from, delta.Angle(), frames.Length / Library.skillAfterSeconds,
                order: Art.SortY(from.y) + 10);
            if (animation != null)
                animation.transform.localScale = new Vector3(delta.magnitude * Art.PPU / (frames[0].rect.width - 1), Library.skillActionScale, 1);
        }
        public static bool Contact(Vector2 at,Vector2 dir,Color color,bool crit,float scale,float heavy)
        {
            var v=Library;if(v==null)return false;
            Fx.I?.Play(v.contact,at,dir.Angle(),v.framesPerSecond,scale*v.contactScale,Tint(color));
            if(GraphicsQuality.KeepOptional())
            {
                Fx.I?.Play(heavy>.5f?v.shards:v.cut,at,dir.Angle(),v.framesPerSecond,scale*v.contactScale,Tint(color));
                if(crit || heavy>.5f)Fx.I?.Play(v.ring,at-Vector2.up*.25f,0,v.framesPerSecond,scale*v.contactScale,Tint(color),false,Art.SortY(at.y)-1);
            }
            return true;
        }
        public static void Accent(ActiveSkillData skill, ActiveStep step, Vector2 at, Vector2 dir, int pixels, int order, Transform follow)
        {
            var v = Library;
            if (v == null) return;
            bool self = step.effect is "heal" or "shield";
            bool directional = step.delivery is "arc" or "line";
            bool area = !self && step.delivery == "nova" && GraphicsQuality.KeepOptional();
            if (directional || area)
            {
                var frames = directional ? step.delivery == "arc" ? v.slash : v.cut : Pick(v.skillShock, v.ring);
                var edge = (Color)Pal.White;
                edge.a = directional ? v.skillAccentAlpha : v.skillGroundAccentAlpha;
                float scale = pixels / Mathf.Max(1, frames[0].rect.width - 6) * (skill.slot == 3 ? v.skillUltimateScale : 1);
                var accent = Fx.I?.Play(frames, at, area ? 0 : dir.Angle(), frames.Length / v.skillAfterSeconds,
                    scale, edge, false, area ? order - 1 : order + 1, follow);
                if (accent != null && step.delivery == "line") Thicken(accent, v.skillActionScale);
            }
        }

        public static void Thicken(SpriteRenderer sprite, float weight)
        {
            var scale = sprite.transform.localScale;
            scale.y *= weight;
            sprite.transform.localScale = scale;
        }

        public static void TargetCut(Vector2 at, Vector2 direction, float diagonal, bool crit)
        {
            var v = Library;
            if (v == null) return;
            var frames = Pick(v.cutHit, v.cut);
            Color color = crit ? Pal.Hex("ffe7a4") : Pal.White;
            Fx.I?.Play(frames, at, direction.Angle() + diagonal, frames.Length / v.cutSeconds,
                v.cutScale * (crit ? 1.16f : 1), color, false, Art.SortY(at.y) + 42);
        }
        public static bool Explosion(Vector2 at,float radius,Element element,Color color)
        {
            var v=Library;if(v==null)return false;
            float scale=radius*Art.PPU/v.slashReferenceRadius;
            Fx.I?.Play(BurstFrames(element.ToString()),at,0,v.framesPerSecond,scale,Tint(color));
            if(GraphicsQuality.KeepOptional())Fx.I?.Play(v.ring,at,0,v.framesPerSecond,scale,Tint(color),false,Art.SortY(at.y)-1);
            return true;
        }
    }
}
