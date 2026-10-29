using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Graphics options from Settings (설정 → 그래픽 page): frame cap / VSync, how many effects the Fx layer spawns,
    /// post-processing level, FPS readout. Apply() is called at scene start and whenever an option changes.
    /// </summary>
    public static class GraphicsQuality
    {
        /// <summary>Frame cap choices on the page; 0 = follow the monitor with VSync.</summary>
        public static readonly int[] FpsCaps = { 30, 60, 120, 0 };

        /// <summary>0 low · 1 medium · 2 high.</summary>
        public static int Effects { get; private set; } = 2;
        public static int PostFx { get; private set; } = 2;
        public static bool ShowFps { get; private set; }

        /// <summary>Share of particles kept at the current effect level.</summary>
        public static float EffectScale => Effects == 0 ? 0.35f : Effects == 1 ? 0.65f : 1f;

        /// <summary>Presets: (fpsCap, effects, postFx) for 낮음 / 보통 / 높음.</summary>
        public static readonly (int fps, int effects, int post)[] Presets = { (60, 0, 0), (60, 1, 1), (0, 2, 2) };

        public static void Apply(Settings s)
        {
            // VSync waits for the monitor, so a cap below the refresh rate needs it off (targetFrameRate is ignored with VSync)
            QualitySettings.vSyncCount = s.fpsCap > 0 ? 0 : 1;
            // With VSync the cap is ignored; it only matters where VSync can't work (driver forces it off, batch mode),
            // so frames don't run uncapped there
            Application.targetFrameRate = s.fpsCap > 0 ? s.fpsCap : Mathf.Max(60, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            Effects = Mathf.Clamp(s.effects, 0, 2);
            PostFx = Mathf.Clamp(s.postFx, 0, 2);
            ShowFps = s.showFps;
            CameraRig.I?.SetPostFx(PostFx);
        }

        public static void ApplyPreset(Settings s, int preset)
        {
            var p = Presets[Mathf.Clamp(preset, 0, Presets.Length - 1)];
            s.fpsCap = p.fps; s.effects = p.effects; s.postFx = p.post;
            Apply(s);
        }

        /// <summary>The preset these settings match (-1 = custom mix).</summary>
        public static int PresetOf(Settings s)
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Presets[i].fps == s.fpsCap && Presets[i].effects == s.effects && Presets[i].post == s.postFx) return i;
            return -1;
        }

        /// <summary>Medium effects keep every other optional effect (afterimages); low keeps none.</summary>
        static int skip;
        public static bool KeepOptional() => Effects == 2 || (Effects == 1 && (++skip & 1) == 0);
    }
}
