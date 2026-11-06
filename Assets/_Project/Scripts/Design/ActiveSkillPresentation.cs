using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diverse
{
    /// <summary>Registered animation frames retain their authored shape, alpha and material colours.</summary>
    public static class ActiveSkillPresentation
    {
        static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
        static ActiveRules C => ActiveSkillGenerator.Rules;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Clear(); SceneManager.sceneUnloaded -= Unloaded; SceneManager.sceneUnloaded += Unloaded; }
        static void Unloaded(Scene scene) => Clear();
        static void Release(Sprite[] frames, float delay = 0)
        { foreach (var s in frames) if (s != null) { Object.Destroy(s.texture, delay); Object.Destroy(s, delay); } }
        static void Clear() { foreach (var frames in cache.Values) Release(frames); cache.Clear(); }

        public static Sprite[] Effect(ActiveSkillData a, ActiveStep step, int kind, int pixels)
        {
            ActiveSkillStyle.Ensure(a);
            if (kind == 2 && step.delivery == "projectile") return ActiveProjectileArt.Frames(a, step);
            pixels = Mathf.Clamp(kind == 1 ? C.impactPixels : pixels, 8, C.maxVisualPixels);
            string key = a.id + ":" + string.Join(",", a.visualPalette) + ":" + JsonUtility.ToJson(step) + ":" + kind + ":" + pixels + ":" + string.Join(",", a.effectImages ?? System.Array.Empty<string>());
            if (cache.TryGetValue(key, out var found)) return found;
            // Older paid sheets often contained icon-like poses. Use the registered project motion until regenerated.
            var generated = a.effectPromptRevision >= 2 ? ActiveEffectArt.Frames(a, step, kind) : null;
            var authored=CombatVisuals.Frames(step,kind);
            if(generated==null && authored!=null)return ActiveSkillPalette.Frames(a,authored);
            if(generated!=null && a.effectPromptRevision<3)generated=ActiveSkillPalette.Frames(a,generated);
            var raw = generated ?? ActiveSkillVisuals.Effect(a, step, kind, pixels);
            if (raw == null || raw.Length == 0) return null;
            // Fit the complete registered cell; never crop each frame to a different opaque bounding box.
            int count = Mathf.Max(4, C.visualFrames), n = pixels + 6;
            var result = new Sprite[count];
            for (int f = 0; f < count; f++)
            {
                var cv = new PixelCanvas(n, n);
                var sprite = raw[Mathf.Min(raw.Length - 1, f * raw.Length / count)];
                var texture = sprite.texture; var src = texture.GetPixels32(); var rect = sprite.rect;
                float scale = pixels / Mathf.Max(rect.width, rect.height);
                int width = Mathf.Max(1, Mathf.RoundToInt(rect.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(rect.height * scale));
                int ox = (n - width) / 2, oy = (n - height) / 2;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int sx = (int)rect.x + Mathf.Min((int)rect.width - 1, x * (int)rect.width / width);
                    int sy = (int)rect.y + Mathf.Min((int)rect.height - 1, y * (int)rect.height / height);
                    var color = src[sy * texture.width + sx];
                    if (color.a >= 32) cv.Px[(oy + y) * n + ox + x] = color;
                }
                result[f] = cv.ToSpriteCentered();
            }
            if (cache.Count >= Mathf.Max(1, C.visualCacheLimit))
            { var first = cache.First(); cache.Remove(first.Key); Release(first.Value, C.maxCastDuration + C.effectDuration); }
            cache[key] = result; return result;
        }
    }
}
