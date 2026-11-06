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
        public static Sprite[] Frames(ActiveStep step,int kind)
        {
            var v=Library;if(v==null)return null;
            if(kind==1)return step.effect is "heal"?Pick(v.skillHeal,v.ring):step.effect is "shield"?Pick(v.skillGuard,v.ring):
                step.effect=="damage"?step.element=="None"?Pick(v.skillImpact,v.contact):SkillBurst(step.element):Pick(v.skillPull,v.ring);
            if(kind==3)return Pick(v.skillMuzzle,v.contact);
            if(kind==2)return step.delivery=="projectile"?v.bolt:Pick(v.skillDash,v.trail);
            return step.delivery switch {
                "arc"=>Pick(v.skillSlash,v.slash),"line"=>Pick(v.skillThrust,v.thrust),
                "nova"=>step.effect=="heal"?Pick(v.skillHeal,v.ring):step.effect=="shield"?Pick(v.skillGuard,v.ring):step.effect=="pull"?Pick(v.skillPull,v.ring):SkillBurst(step.element),
                "field"=>step.effect=="heal"?Pick(v.skillHeal,v.ring):step.effect=="shield"?Pick(v.skillGuard,v.ring):step.effect=="pull"?Pick(v.skillPull,v.ring):Pick(v.skillField,v.ring),
                "trap"=>Pick(v.skillTrap,v.ring),
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
        public static bool Basic(Vector2 at,Vector2 dir,int radius,float reach,float scale,bool thrust,bool spin,bool flip,Color color,Transform follow)
        {
            var v=Library;if(v==null)return false;
            float size=radius/Mathf.Max(1,v.slashReferenceRadius)*scale;
            if(thrust){at+=dir*reach*.5f;size=reach*Art.PPU/Mathf.Max(1,v.thrust[0].rect.width-6)*scale;}
            var main=thrust?v.thrust:v.slash;
            var generated = !thrust ? DesignRuntime.SwingFx((Color32)color,radius) : null;
            if(generated!=null)Fx.I?.Play(generated,at,dir.Angle(),v.framesPerSecond,scale,null,false,0,follow,flip);
            else Fx.I?.Play(main,at,dir.Angle(),v.framesPerSecond,size,Tint(color),false,0,follow,flip);
            if(spin)Fx.I?.Play(v.slash,at,dir.Angle()+180,v.framesPerSecond,size,Tint(color),false,0,follow);
            // A narrow leading-edge streak; this is not another enlarged full sweep.
            if(!thrust && GraphicsQuality.KeepOptional())
                Fx.I?.Play(v.cut,at,dir.Angle(),v.framesPerSecond,size*v.accentScale,Tint(color),false,0,follow,flip);
            return true;
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
        public static void Accent(ActiveSkillData skill,ActiveStep step,Vector2 at,Vector2 dir,int pixels,Color color,int order,Transform follow)
        {
            var v=Library;if(v==null || !GraphicsQuality.KeepOptional())return;
            bool area=step.delivery is "nova" or "field" or "trap" or "wall";
            var frames=step.delivery is "arc" or "line"?v.cut:area?Pick(v.skillShock,v.ring):
                step.delivery=="guard"?Pick(v.skillHoly,v.ring):step.delivery=="projectile"?v.cut:Pick(v.skillDash,v.cut);
            if(frames==null || frames.Length==0)return;
            frames=ActiveSkillPalette.Frames(skill,frames);
            float scale=pixels/Mathf.Max(1f,frames[0].rect.width-6);
            float strength=skill.slot==3?v.skillUltimateScale:1;
            // Ground shock stays on the ground plane; direction changes only directional strokes.
            Fx.I?.Play(frames,at,area || step.delivery=="guard"?0:dir.Angle(),frames.Length/v.skillAfterSeconds,
                scale*strength,null,false,area?order-1:order+1,follow,delay:v.skillMainSeconds*.18f);
            if(step.delivery is "arc" or "line")
            {
                var stroke=ActiveSkillPalette.Frames(skill,Pick(v.skillDash,v.cut));float width=pixels/Mathf.Max(1f,stroke[0].rect.width-6);
                Fx.I?.Play(stroke,at,dir.Angle(),stroke.Length/v.skillAfterSeconds,width*.75f,null,false,order+2,follow,delay:v.skillMainSeconds*.32f);
            }
            // A field/trap keeps its readable floor boundary; its element erupts above it rather than replacing it.
            if((step.delivery is "field" or "trap") && step.effect=="damage")
            {
                var burst=ActiveSkillPalette.Frames(skill,SkillBurst(step.element));
                Fx.I?.Play(burst,at,0,burst.Length/v.skillMainSeconds,pixels/Mathf.Max(1f,burst[0].rect.width-6),null,false,order+2,delay:v.skillMainSeconds*.12f);
            }
        }
        public static void SkillImpact(ActiveSkillData skill,ActiveStep step,Vector2 at,Vector2 dir)
        {
            var v=Library;if(v==null || !GraphicsQuality.KeepOptional())return;
            var frames=ActiveSkillPalette.Frames(skill,Pick(v.skillShock,v.ring));
            Fx.I?.Play(frames,at-Vector2.up*ActiveSkillGenerator.Rules.actionHeight,0,frames.Length/v.skillAfterSeconds,
                v.skillImpactPixels/Mathf.Max(1f,frames[0].rect.width-6),null,false,Art.SortY(at.y)-1,delay:v.skillAfterSeconds*.2f);
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
