using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diverse
{
    /// <summary>Flight bodies keep native pixels. Trails and fading action frames never stand in for a body.</summary>
    public static class ActiveProjectileArt
    {
        sealed class Flight
        {
            public Sprite[] frames;
            public float scale;
        }
        static readonly Dictionary<string, Flight> cache = new Dictionary<string, Flight>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Clear(); SceneManager.sceneUnloaded -= Unloaded; SceneManager.sceneUnloaded += Unloaded; }
        static void Unloaded(Scene scene) => Clear();
        static void Clear() { foreach (var entry in cache.Values) Release(entry, 0); cache.Clear(); }
        static void Release(Flight entry, float delay)
        { foreach (var frame in entry.frames) if (frame != null) Object.Destroy(frame, delay); }

        static Flight Get(ActiveSkillData skill, ActiveStep step)
        {
            var v = CombatVisuals.Library;
            string key = skill.id + ":" + skill.steps.IndexOf(step) + ":" + skill.effectProcessedRevision + ":" +
                skill.weaponType + ":" + step.element + ":" + step.pattern + ":" + string.Join(",", skill.effectImages ?? System.Array.Empty<string>());
            if (cache.TryGetValue(key, out var found)) return found;
            var raw = ActiveEffectArt.Frames(skill, step, 2);
            if (raw != null) raw = ActiveSkillPalette.Frames(skill, raw, generated: true);
            var flight = Build(raw, v);
            if (flight == null) flight = Build(ActiveSkillPalette.Frames(skill,CombatVisuals.ProjectileFrames(skill, step)), v);
            // The last fallback is a physical projectile, never the procedural movement-line row.
            flight ??= Build(new[] { ActiveSkillVisuals.Shot(skill, step) }, v);
            if (flight == null) return null;
            if (cache.Count >= Mathf.Max(1, ActiveSkillGenerator.Rules.visualCacheLimit))
            {
                var first = cache.First(); cache.Remove(first.Key);
                Release(first.Value, ActiveSkillGenerator.Rules.maxCastDuration + ActiveSkillGenerator.Rules.effectDuration);
            }
            cache[key] = flight; return flight;
        }
        static Flight Build(Sprite[] raw, CombatVisualLibrary v)
        {
            if (raw == null || raw.Length == 0 || raw.Any(s => s == null || !s.texture.isReadable)) return null;
            int width = (int)raw[0].rect.width, height = (int)raw[0].rect.height;
            int left = width, bottom = height, right = -1, top = -1;
            foreach (var sprite in raw)
            {
                if ((int)sprite.rect.width != width || (int)sprite.rect.height != height) return null;
                var pixels = sprite.texture.GetPixels32(); int stride = sprite.texture.width;
                int ox = (int)sprite.rect.x, oy = (int)sprite.rect.y, opaque = 0; bool core = false;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    var p = pixels[(oy + y) * stride + ox + x];
                    if (p.a < 96) continue;
                    left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                    bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
                    if (p.a < 160) continue;
                    opaque++;
                    if (x + 1 < width && y + 1 < height && pixels[(oy+y)*stride+ox+x+1].a >= 160 &&
                        pixels[(oy+y+1)*stride+ox+x].a >= 160 && pixels[(oy+y+1)*stride+ox+x+1].a >= 160) core = true;
                }
                if (opaque < 8 || !core) return null; // Every loop frame needs a substantial opaque head.
            }
            if (right < left || top < bottom) return null;
            int bodyWidth = right - left + 1, bodyHeight = top - bottom + 1;
            float longest = Mathf.Max(bodyWidth, bodyHeight);
            float maxPixels = v != null ? v.skillProjectileMaxPixels : ActiveSkillGenerator.Rules.projectilePixels * 2;
            float minThickness = v != null ? v.skillProjectileMinThickness : 3;
            if (bodyHeight * maxPixels / longest < minThickness) return null; // A trail-only row cannot pass as a body.
            float desired = v != null ? v.skillProjectilePixels : ActiveSkillGenerator.Rules.projectilePixels;
            float scale = Mathf.Min(maxPixels / longest, Mathf.Max(desired / longest, minThickness / bodyHeight));
            // All frames share ONE crop and pivot: no per-frame recentering or jitter, no 48px -> 16px resample.
            left = Mathf.Max(0, left - 1); bottom = Mathf.Max(0, bottom - 1);
            right = Mathf.Min(width - 1, right + 1); top = Mathf.Min(height - 1, top + 1);
            // Retain the registered collision pivot even when a short wake makes the crop asymmetric.
            int cropWidth = right - left + 1, cropHeight = top - bottom + 1;
            var pivot = new Vector2((raw[0].pivot.x - left) / cropWidth, (raw[0].pivot.y - bottom) / cropHeight);
            if (pivot.x < 0 || pivot.x > 1 || pivot.y < 0 || pivot.y > 1) return null;
            var frames = raw.Select(s => Sprite.Create(s.texture,
                new Rect(s.rect.x + left, s.rect.y + bottom, cropWidth, cropHeight),
                pivot, s.pixelsPerUnit, 0, SpriteMeshType.FullRect)).ToArray();
            return new Flight { frames = frames, scale = scale };
        }
        public static Sprite[] Frames(ActiveSkillData skill, ActiveStep step) => Get(skill, step)?.frames;
        public static void Apply(Projectile projectile, ActiveSkillData skill, ActiveStep step)
        {
            var flight = Get(skill, step); if (flight == null) return;
            projectile.SetFlightVisual(flight.frames, flight.scale, ActiveSkillGenerator.Rules.effectFramesPerSecond,
                Color.white);
        }
    }
}
