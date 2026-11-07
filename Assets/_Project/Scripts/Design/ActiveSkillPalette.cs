using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diverse
{
    /// <summary>Recolours authored motion once per palette; preserves source geometry and native pixel resolution.</summary>
    public static class ActiveSkillPalette
    {
        static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Clear(); SceneManager.sceneUnloaded -= Unloaded; SceneManager.sceneUnloaded += Unloaded; }
        static void Unloaded(Scene scene) => Clear();
        static void Release(Sprite[] frames, float delay)
        { foreach (var s in frames) { Object.Destroy(s.texture, delay); Object.Destroy(s, delay); } }
        static void Clear() { foreach (var frames in cache.Values) Release(frames, 0); cache.Clear(); }
        public static Color32 Secondary(ActiveSkillData skill)
        {
            var primary = Pal.Hex(skill.color); var icon = DesignStore.Image(skill.iconImage);
            if (icon == null || !icon.isReadable) return primary;
            Color.RGBToHSV(primary, out float hue, out _, out _);
            var group = icon.GetPixels32().Where(p => {
                Color.RGBToHSV(p, out float h, out float s, out float v);
                return p.a > 160 && s > .25f && v > .3f && Mathf.Abs(Mathf.DeltaAngle(hue*360,h*360)) > 45;
            }).GroupBy(p => (p.r/32,p.g/32,p.b/32)).OrderByDescending(g => g.Count()).FirstOrDefault();
            return group != null ? group.First() : primary;
        }
        public static Sprite[] Frames(ActiveSkillData skill, Sprite[] source, bool generated = false)
        {
            if (source == null || source.Length == 0) return source;
            ActiveSkillStyle.Ensure(skill);
            string key = skill.iconImage + ":" + string.Join(",",skill.visualPalette) + ":" + generated + ":" + string.Join(",",source.Select(s => s.GetInstanceID()));
            if (cache.TryGetValue(key, out var found)) return found;
            Color32 shadow=Pal.Hex(skill.visualPalette[0]), dark=Pal.Hex(skill.visualPalette[1]), primary=Pal.Hex(skill.visualPalette[2]);
            Color32 secondary=Secondary(skill), light=Pal.Hex(skill.visualPalette[3]);
            Color.RGBToHSV(primary, out float primaryHue, out _, out _);
            Color.RGBToHSV(secondary, out float secondaryHue, out _, out _);
            var frames = new Sprite[source.Length];
            for (int f=0;f<source.Length;f++)
            {
                var s=source[f];int w=(int)s.rect.width,h=(int)s.rect.height;var raw=s.texture.GetPixels32();var pixels=new Color32[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    var p=raw[((int)s.rect.y+y)*s.texture.width+(int)s.rect.x+x];if(p.a==0)continue;
                    Color.RGBToHSV(p,out float hue,out float sat,out float value);
                    Color32 mapped=value<.25f?shadow:value<.48f?Pal.Lerp(dark,primary,(value-.25f)/.23f):
                        value<.8f?Pal.Lerp(primary,light,(value-.48f)/.32f):Pal.Lerp(light,Pal.White,(value-.8f)/.2f);
                    // Deliberate secondary bands keep material contrast; their colour comes from the icon as well.
                    bool accent = generated
                        ? Mathf.Abs(Mathf.DeltaAngle(hue * 360, secondaryHue * 360)) + 18 < Mathf.Abs(Mathf.DeltaAngle(hue * 360, primaryHue * 360))
                        : hue > .6f && hue < .88f;
                    if(sat>.3f && value>.3f && value<.75f && accent)
                        mapped=Pal.Lerp(dark,secondary,Mathf.InverseLerp(.25f,.8f,value));
                    mapped.a=p.a;pixels[y*w+x]=mapped;
                }
                var texture=new Texture2D(w,h,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                texture.SetPixels32(pixels);texture.Apply();
                frames[f]=Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(s.pivot.x/w,s.pivot.y/h),s.pixelsPerUnit,0,SpriteMeshType.FullRect);
            }
            if(cache.Count>=Mathf.Max(1,ActiveSkillGenerator.Rules.visualCacheLimit))
            {var first=cache.First();cache.Remove(first.Key);Release(first.Value,ActiveSkillGenerator.Rules.maxCastDuration+ActiveSkillGenerator.Rules.effectDuration);}
            return cache[key]=frames;
        }
    }
}
