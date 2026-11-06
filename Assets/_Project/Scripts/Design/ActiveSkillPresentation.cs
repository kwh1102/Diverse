using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diverse
{
    /// <summary>AI detail/colour on an executable silhouette. Registration and sweep timing belong to combat, not an image model.</summary>
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
            pixels = Mathf.Clamp(kind == 1 ? C.impactPixels : pixels, 8, C.maxVisualPixels);
            string key = a.id + ":" + string.Join(",", a.visualPalette) + ":" + JsonUtility.ToJson(step) + ":" + kind + ":" + pixels + ":" + a.effectImages?.FirstOrDefault();
            if (cache.TryGetValue(key, out var found)) return found;
            var raw = ActiveEffectArt.Frames(a, step, kind) ?? ActiveSkillVisuals.Effect(a, step, kind, pixels);
            bool arc = kind == 0 && step.delivery == "arc";
            bool line = kind == 0 && step.delivery is "line" or "wall";
            bool ring = kind == 0 && step.delivery is "nova" or "field" or "trap" or "guard";
            bool trail = kind == 2 && ActiveSkillGenerator.Moving(step);
            bool shape = arc || line || ring || trail;
            int count = Mathf.Max(4, C.visualFrames), n = pixels + 6; float centre = n * .5f, radius = pixels * .5f;
            var result = new Sprite[count];
            var tones = a.visualPalette.Select(c => Pal.Hex(c)).ToArray();
            for (int f = 0; f < count; f++)
            {
                float t = f / (float)(count - 1);
                var cv = new PixelCanvas(n, n);
                var sprite = raw[Mathf.Min(raw.Length - 1, f * raw.Length / count)];
                var texture = sprite.texture; var src = texture.GetPixels32(); var rect = sprite.rect;
                for (int y = 3; y < n - 3; y++) for (int x = 3; x < n - 3; x++)
                {
                    float dx = x + .5f - centre, dy = y + .5f - centre, alpha = 1, value = .65f;
                    bool inside = !shape;
                    if (arc)
                    {
                        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, d = Mathf.Sqrt(dx * dx + dy * dy);
                        float head = Mathf.Lerp(70, -70, MathX.EaseOutCubic(t));
                        float tail = Mathf.Min(70, head + C.arcTrailDegrees * Mathf.Pow(Mathf.Sin(t * Mathf.PI), .45f));
                        float along = Mathf.InverseLerp(tail, head, angle);
                        float thickness = radius * Mathf.Lerp(.09f, C.arcThickness, along) * (1 - t * .35f);
                        inside = angle >= head && angle <= tail && d <= radius && d >= radius - thickness;
                        value = d > radius - thickness * .4f ? .95f : .65f;
                        alpha = Mathf.Clamp01((1 - t) * 3);
                    }
                    else if (line || trail)
                    {
                        float reach = Mathf.Min(1, t * 2 + .2f), fade = Mathf.Clamp01((t - .4f) / .6f);
                        float along = (dx + radius) / (2 * radius);
                        float half = trail ? Mathf.Max(1, pixels * .05f) : Mathf.Max(1, pixels * .09f * Mathf.Sin(along * Mathf.PI));
                        inside = along >= fade * .8f && along <= reach && Mathf.Abs(dy) <= half;
                        value = Mathf.Abs(dy) < half * .4f ? .95f : .65f;
                    }
                    else if (ring)
                    {
                        float py = dy / .55f, d = Mathf.Sqrt(dx * dx + py * py);
                        float r = radius * Mathf.Lerp(.4f, 1, Mathf.Clamp01(t * 1.5f));
                        inside = Mathf.Abs(d - r) < Mathf.Max(1, radius * .12f * (1 - t * .7f));
                        value = dy > 0 ? .6f : .95f; alpha = 1 - t * .7f;
                        if (step.delivery == "guard") inside = inside || Mathf.Abs(dx) < 1 && Mathf.Abs(dy) < r * .4f;
                    }
                    if (!inside) continue;
                    float growth = kind == 1 ? Mathf.Lerp(.65f, 1.15f, t) : 1;
                    float u = Mathf.Clamp01(.5f + dx / pixels / growth), v = Mathf.Clamp01(.5f + dy / pixels / growth);
                    int sx = Mathf.Clamp((int)(rect.x + u * (rect.width - 1)), 0, texture.width - 1);
                    int sy = Mathf.Clamp((int)(rect.y + v * (rect.height - 1)), 0, texture.height - 1);
                    var detail = src[sy * texture.width + sx];
                    if (!shape)
                    {
                        if (detail.a < 32) continue;
                        value = Mathf.Max(detail.r, Mathf.Max(detail.g, detail.b)) / 255f;
                        alpha = detail.a / 255f * (kind == 1 ? Mathf.Clamp01((1 - t) * 2) : 1);
                    }
                    else if (detail.a >= 32 && value < .9f)
                        value = Mathf.Lerp(value, Mathf.Max(detail.r, Mathf.Max(detail.g, detail.b)) / 255f, .35f);
                    var col = tones[value < .22f ? 0 : value < .5f ? 1 : value < .85f ? 2 : 3]; col.a = (byte)Mathf.RoundToInt(alpha * 255);
                    cv.Px[y * n + x] = col;
                }
                result[f] = cv.ToSpriteCentered();
            }
            if (cache.Count >= Mathf.Max(1, C.visualCacheLimit))
            { var first = cache.First(); cache.Remove(first.Key); Release(first.Value, C.maxCastDuration + C.effectDuration); }
            cache[key] = result; return result;
        }
    }
}
