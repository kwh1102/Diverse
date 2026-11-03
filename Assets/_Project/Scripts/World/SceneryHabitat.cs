using UnityEngine;

namespace Diverse
{
    /// <summary>Shared world-space patches for vegetation, mineral beds and their ground. Never reads chunks or props.</summary>
    public static class SceneryHabitat
    {
        public static float Sample(Vector2 p, ThemeDef theme, int seed)
        {
            var s = theme.scenery;
            if (!s.enabled) return Noise.Perlin(p.x, p.y, seed + 333, .045f);
            float size = Mathf.Max(12, s.patchSize);
            var warp = new Vector2(Noise.Perlin(p.x, p.y, seed + 531, 1 / (size * 2)),
                Noise.Perlin(p.x, p.y, seed + 533, 1 / (size * 2))) - Vector2.one * .5f;
            var q = p + warp * size * .65f;
            float field = Noise.Perlin(q.x, q.y, seed + 333, 1 / size);
            float patch = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(s.patchThreshold - s.patchFeather,
                s.patchThreshold + s.patchFeather, field));
            float clearing = Noise.Perlin(p.x, p.y, seed + 537, 1 / Mathf.Max(10, s.clearingSize));
            return patch * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(s.clearingThreshold, s.clearingThreshold + .12f, clearing)));
        }
    }
}
