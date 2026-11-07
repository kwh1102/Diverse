using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diverse
{
    /// <summary>Transparent, stage-specific pixel animations. Never reads weapon art or an inventory icon.</summary>
    public static class ActiveSkillVisuals
    {
        static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
        class Entry { public ActiveStep step; public int kind, size; public Color32 color; public Sprite[] frames; }
        static readonly List<Entry> entries = new List<Entry>();
        static ActiveRules C => ActiveSkillGenerator.Rules;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Clear(); SceneManager.sceneUnloaded -= Unloaded; SceneManager.sceneUnloaded += Unloaded;
        }
        static void Unloaded(Scene scene) => Clear();
        static void Clear()
        {
            foreach (var e in entries) foreach (var sprite in e.frames)
                if (sprite != null) { Object.Destroy(sprite.texture); Object.Destroy(sprite); }
            entries.Clear(); cache.Clear();
        }
        public static Sprite[] Effect(ActiveSkillData skill, ActiveStep step, int kind, int pixels)
        {
            step ??= skill.steps[0];
            int size = Mathf.Clamp(pixels, 8, C.maxVisualPixels);
            string key = $"{step.delivery}:{step.pattern}:{step.effect}:{step.element}:{step.timing}:{skill.color}:{kind}:{size}";
            if (cache.TryGetValue(key, out var found)) return found;
            if (entries.Count >= Mathf.Max(1, C.visualCacheLimit))
            {
                // Reuse the closest existing animation at the session cap. Never evict a live projectile's texture.
                var wanted = Pal.Hex(skill.color);
                return entries.OrderBy(e => (e.step.delivery != step.delivery ? 10000 : 0) + (e.kind != kind ? 5000 : 0) +
                    (e.step.effect != step.effect ? 2000 : 0) + (e.step.pattern != step.pattern ? 500 : 0) +
                    Mathf.Abs(e.size - size) * 20 + Mathf.Abs(e.color.r - wanted.r) + Mathf.Abs(e.color.g - wanted.g) + Mathf.Abs(e.color.b - wanted.b)).First().frames;
            }
            var frames = new Sprite[Mathf.Max(2, C.visualFrames)];
            Color32 col = Pal.Lerp(Pal.Hex(skill.color), Pal.OutlineSoft, .15f);
            Color32 light = Pal.Lerp(col, Pal.White, .62f);
            int side = size + 6; float center = side * .5f, radius = size * .5f;
            for (int f = 0; f < frames.Length; f++)
            {
                float t = f / (float)(frames.Length - 1);
                var cv = new PixelCanvas(side, side); var at = new Vector2(center, center);
                void Line(Vector2 a, Vector2 b, Color32 c, float width = 1) => cv.ThickLine(a, b, width, c);
                void Arc(float r, float start, float end, Color32 c)
                {
                    Vector2 previous = at + MathX.Dir(start) * r;
                    for (int n = 1; n <= 24; n++)
                    {
                        Vector2 next = at + MathX.Dir(Mathf.Lerp(start, end, n / 24f)) * r;
                        Line(previous, next, c); previous = next;
                    }
                }
                if (kind == 1)
                {
                    // Distinct hit motifs: inward control hooks, recovery cross, or outward shards.
                    if (step.effect is "heal" or "shield")
                    {
                        float r = radius * (1 - t * .6f);
                        Line(at - Vector2.right * r, at + Vector2.right * r, col);
                        Line(at - Vector2.up * r, at + Vector2.up * r, light);
                    }
                    else if (step.effect == "mark")
                    {
                        Arc(radius * .5f, 0, 360, col);
                        Line(at + Vector2.up * radius * .8f, at + Vector2.up * radius * .25f, light);
                    }
                    else for (int n = 0; n < (step.element == "Frost" ? 6 : 5); n++)
                    {
                        var axis = MathX.Dir(n * 72 + (step.effect is "pull" or "stun" ? 30 : 0));
                        float r = radius * (step.effect == "pull" ? 1 - t * .8f : .25f + t * .7f);
                        Line(at + axis * r * .55f, at + axis * r, n % 2 == 0 ? light : col);
                        if (step.element == "Lightning") Line(at + axis * r, at + axis * r + new Vector2(2, -2), light);
                        if (step.element == "Frost") Line(at + axis * r * .7f, at + axis * r * .7f + new Vector2(2, 2), col);
                    }
                }
                else if (kind == 0 && step.delivery == "projectile" && (step.pattern is "fan" or "ring" or "converge"))
                {
                    int spokes = step.pattern == "fan" ? 3 : 6;
                    for (int n = 0; n < spokes; n++)
                    {
                        float angle = step.pattern == "fan" ? (n - 1) * 30 : n * 360f / spokes;
                        var axis = MathX.Dir(angle);
                        float r = radius * (step.pattern == "converge" ? 1 - t * .75f : .25f + t * .7f);
                        Line(at + axis * r * .5f, at + axis * r, n % 2 == 0 ? light : col);
                    }
                }
                else if (kind == 2 || ActiveSkillGenerator.Moving(step))
                {
                    for (int n = -1; n <= 1; n++)
                    {
                        float y = center + n * radius * .25f;
                        if (step.pattern is "curve" or "return") Arc(radius * .6f, -100 + t * 30, 30 + t * 30, n == 0 ? light : col);
                        else Line(new Vector2(center - radius + t * radius, y), new Vector2(center + radius * .6f, y), n == 0 ? light : col);
                    }
                    if (step.delivery == "blink") Arc(radius * (1 - t * .5f), -90, 90, col);
                }
                else if (kind == 3 || step.delivery is "projectile" or "line" or "wall")
                {
                    float length = radius * (kind == 3 ? 1 - t * .7f : Mathf.Min(1, .4f + t * 2));
                    if (step.delivery == "wall")
                    {
                        Line(at - Vector2.right * length, at + Vector2.right * length, col, 1.4f);
                        for (int n = -2; n <= 2; n++) Line(at + new Vector2(n * length / 2, -3), at + new Vector2(n * length / 2, 3), light);
                    }
                    else
                    {
                        Line(at - Vector2.right * length, at + Vector2.right * length, col, 1.2f);
                        Line(at + Vector2.right * length, at + new Vector2(length * .5f, 3), light);
                        Line(at + Vector2.right * length, at + new Vector2(length * .5f, -3), light);
                        if (step.pattern == "chain") for (int n = -2; n < 2; n++)
                            Line(at + new Vector2(n * length / 2, n % 2 == 0 ? -3 : 3), at + new Vector2((n + 1) * length / 2, n % 2 == 0 ? 3 : -3), light);
                        if (step.pattern is "return" or "curve") Arc(length * .6f, -100, 100, light);
                        if (step.pattern is "pierce" or "seek") Line(at - Vector2.right * length, at + Vector2.right * length * .5f, light);
                    }
                }
                else if (step.delivery == "arc")
                {
                    float head = Mathf.Lerp(-70, 70, t);
                    Arc(radius, Mathf.Max(-70, head - 75), head, col);
                    Arc(radius - 2, Mathf.Max(-70, head - 55), head, light);
                }
                else
                {
                    bool anchored = step.delivery is "trap" or "field";
                    float r = radius * (step.effect == "pull" ? 1 - t * .65f : anchored ? .85f : .2f + .8f * t);
                    Arc(r, 0, 360, col);
                    if (step.effect == "shield") Arc(r - 2, -70, 70, light);
                    int nodes = step.delivery == "trap" ? 4 : 6;
                    for (int n = 0; n < nodes; n++)
                    {
                        Vector2 p = at + MathX.Dir(n * 360f / nodes + (step.delivery == "field" ? t * 30 : 0)) * r;
                        cv.Rect(Mathf.RoundToInt(p.x) - 1, Mathf.RoundToInt(p.y) - 1, 2, 2, light);
                    }
                }
                // Palette is opaque where painted; transparent ground stays truly transparent.
                frames[f] = cv.ToSpriteCentered();
            }
            cache[key] = frames;
            entries.Add(new Entry { step = step, kind = kind, size = size, color = Pal.Hex(skill.color), frames = frames });
            return frames;
        }
        public static Sprite Shot(ActiveSkillData skill, ActiveStep step) => Effect(skill, step, 3, C.projectilePixels)[0];
        public static Sprite Icon(ActiveSkillData a)
        {
            var step = a.steps[0];
            var frames = Effect(a, step, step.delivery == "arc" ? 0 : step.delivery == "projectile" ? 3 : 0, C.iconPixels);
            return frames[frames.Length / 2];
        }
    }
}
