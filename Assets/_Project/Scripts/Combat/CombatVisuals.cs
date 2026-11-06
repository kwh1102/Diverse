using UnityEngine;

namespace Diverse
{
    /// <summary>Project-authored animation layers. Never calls AI or creates pixel textures in combat.</summary>
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
            if(kind==1)return step.effect=="damage"?v.contact:v.ring;
            if(kind==3)return v.contact;
            if(kind==2)return step.delivery=="projectile"?v.bolt:v.trail;
            return step.delivery switch {
                "arc"=>v.slash,"line"=>v.thrust,"nova"=>BurstFrames(step.element),
                "field" or "trap" or "guard" or "wall"=>v.ring,
                "dash" or "retreat" or "blink"=>v.cut,_=>null };
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
        public static void Accent(ActiveStep step,Vector2 at,Vector2 dir,float scale,Color color,int order,Transform follow)
        {
            var v=Library;if(v==null || !GraphicsQuality.KeepOptional())return;
            var frames=step.delivery=="arc"?v.cut:step.delivery=="line"?v.cut:step.delivery=="nova"?v.ring:null;
            if(frames!=null)Fx.I?.Play(frames,at,step.delivery=="nova"?0:dir.Angle(),v.framesPerSecond,scale*v.accentScale,Tint(color),false,order+1,follow);
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
