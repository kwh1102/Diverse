using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Diverse
{
    /// <summary>One world controller: shared GPU sprite motion, tiny per-chunk masks and a hard-capped native particle pool.</summary>
    public class EnvironmentAnimator : MonoBehaviour
    {
        sealed class Emitter
        {
            public Transform anchor;
            public SpriteRenderer visible;
            public EnvironmentEffect effect;
            public Vector2 offset;
            public float next, interval, scale;
            public Color color;
        }
        static readonly ProfilerMarker Marker = new ProfilerMarker("Diverse.Environment");
        static readonly int ClockId = Shader.PropertyToID("_EnvironmentTime"), EnabledId = Shader.PropertyToID("_EnvironmentEnabled");
        readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        readonly Dictionary<string, EnvironmentObjectDef> rules = new Dictionary<string, EnvironmentObjectDef>();
        readonly List<Emitter> emitters = new List<Emitter>();
        readonly Sprite[] sheet = new Sprite[4];
        readonly ParticleSystem[] pools = new ParticleSystem[4];
        readonly Color32[] maskPixels = new Color32[WorldGen.Chunk * WorldGen.Chunk];
        Material particleAtlas, particleDot;
        WorldStreamer world;
        float clock, tick, weather;
        int cursor;
        EnvironmentDef S => DB.WorldRules.environment;
        public float Clock => clock;
        public float ReviewTime = -1;
        public bool Active => S.enabled;
        public int MaterialCount => materials.Count;
        public int EmitterCount => emitters.Count;
        public int ParticleCount { get { int n = 0; foreach (var pool in pools) if (pool != null) n += pool.particleCount; return n; } }

        public void Initialize(WorldStreamer streamer)
        {
            world = streamer; clock = tick = weather = 0; cursor = 0; ReviewTime = -1;
            emitters.Clear(); rules.Clear();
            foreach (var pool in pools) if (pool != null) pool.Clear();
            Shader.SetGlobalFloat(ClockId, 0); Shader.SetGlobalFloat(EnabledId, Active ? 1 : 0);
            if (!Active || sheet[0] != null) return;
            var texture = Resources.Load<Texture2D>("Environment/ambient");
            if (texture == null) return;
            for (int row = 0; row < 4; row++)
            {
                sheet[row] = Sprite.Create(texture, new Rect(0, (3 - row) * 32, 32, 32),
                    row == 3 ? new Vector2(.5f, .5f) : new Vector2(.5f, 1f / 32), Art.PPU, 0, SpriteMeshType.FullRect);
                sheet[row].name = "environment overlay " + row;
            }
            // Pay native system/material setup during world loading, not when a new effect first enters the screen.
            if (Application.isPlaying) for (int kind = 0; kind < pools.Length; kind++) Pool(kind);
        }

        EnvironmentObjectDef Rule(string key)
        {
            if (rules.TryGetValue(key, out var rule)) return rule;
            foreach (var candidate in S.objects) if (candidate.Matches(key)) { rule = candidate; break; }
            rules[key] = rule; return rule;
        }

        Material MaterialFor(ThemeDef theme, int kind)
        {
            int key = theme.index * 8 + kind;
            if (materials.TryGetValue(key, out var material)) return material;
            var p = theme.environment;
            material = new Material(Art.SpriteMat) { name = "Environment " + theme.id + " " + kind };
            material.SetFloat("_EnvMotion", kind);
            material.SetFloat("_EnvFPS", S.framesPerSecond); material.SetFloat("_EnvSpeed", kind == 5 ? S.liquidSpeed : S.windSpeed);
            material.SetFloat("_EnvSpatial", S.windSpatialScale);
            material.SetFloat("_EnvStrength", kind == 1 ? p.wind : kind == 2 ? p.cloth : kind == 3 ? p.glint : kind == 5 ? S.waterStrength : 1);
            material.SetColor("_EnvTint", p.accent);
            materials[key] = material; return material;
        }

        public void Register(SpriteRenderer sr, string key, ThemeDef theme)
        {
            if (!Active || sr == null) return;
            var rule = Rule(key);
            if (rule == null) return;
            if (rule.motion != EnvironmentMotion.Still) sr.sharedMaterial = MaterialFor(theme, (int)rule.motion);
            if (rule.effect != EnvironmentEffect.None && sheet[0] != null)
            {
                if (rule.effect is EnvironmentEffect.Flame or EnvironmentEffect.Portal)
                {
                    var overlay = new GameObject("environment " + rule.effect);
                    overlay.transform.SetParent(sr.transform, false);
                    overlay.transform.localPosition = rule.offset;
                    overlay.transform.localScale = Vector3.one * rule.scale;
                    var fx = Art.MakeRenderer(overlay, sheet[rule.effect == EnvironmentEffect.Flame ? 0 : 3], sr.sortingOrder + 1);
                    fx.sharedMaterial = MaterialFor(theme, 4);
                }
                emitters.Add(new Emitter { anchor = sr.transform, visible = sr, effect = rule.effect, offset = rule.offset,
                    scale = rule.scale, interval = rule.interval,
                    color = theme.environment.accent,
                    next = clock + Hash.F(Mathf.FloorToInt(sr.transform.position.x), Mathf.FloorToInt(sr.transform.position.y), world.Gen.seed + 8141) * rule.interval });
            }
        }

        public Texture2D BindGround(SpriteRenderer renderer, Vector2Int chunk)
        {
            if (!Active) return null;
            int n = WorldGen.Chunk, liquids = 0;
            uint best = uint.MaxValue; Vector2 bubble = Vector2.zero; byte bubbleKind = 0;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int tx = chunk.x * n + x, ty = chunk.y * n + y;
                var ground = world.GroundAtTile(tx, ty);
                byte mask = WorldGen.IsWater(ground) ? (byte)64 : ground == Ground.Lava ? (byte)255 : ground == Ground.Toxic ? (byte)160 : (byte)0;
                if (mask != 0)
                {
                    // Projected cliff faces and caps must remain still even when the hidden floor is liquid.
                    for (int down = 1; down <= Mathf.CeilToInt(2 * WorldGen.FaceHeight); down++)
                        if (Mathf.CeilToInt(WorldGen.Level(world.GroundAtTile(tx, ty - down)) * WorldGen.FaceHeight) >= down) { mask = 0; break; }
                }
                maskPixels[y * n + x] = new Color32(mask, 0, 0, 255);
                if (mask != 0)
                {
                    liquids++;
                    uint priority = Hash.U(tx, ty, world.Gen.seed + 8157);
                    if (priority < best) { best = priority; bubble = new Vector2(x + .5f, y + .5f); bubbleKind = mask; }
                }
            }
            if (liquids == 0) return null;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "environment liquid mask", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(maskPixels); texture.Apply(false, true);
            renderer.sharedMaterial = MaterialFor(DB.SpawnTheme, 5);
            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            properties.SetTexture("_EnvironmentMask", texture); renderer.SetPropertyBlock(properties);
            var region = world.Gen.ThemeAt(chunk.x * n + bubble.x, chunk.y * n + bubble.y);
            emitters.Add(new Emitter { anchor = renderer.transform, visible = renderer,
                effect = bubbleKind > 200 ? EnvironmentEffect.Smoke : EnvironmentEffect.Bubble, offset = bubble,
                interval = S.liquidInterval, next = clock + (best % 60) * .1f, scale = 1, color = region.environment.accent });
            return texture;
        }

        void LateUpdate()
        {
            using var _ = Marker.Auto();
            Shader.SetGlobalFloat(EnabledId, Active ? 1 : 0);
            if (!Active || world == null) return;
            if (!GameTime.Paused) clock += Time.deltaTime;
            Shader.SetGlobalFloat(ClockId, ReviewTime >= 0 ? ReviewTime : clock);
            if (GameTime.Paused || ReviewTime >= 0) return;
            tick -= Time.deltaTime;
            if (tick > 0) return;
            tick = Mathf.Max(.02f, S.tickInterval);
            var camera = CameraRig.I != null ? CameraRig.I.Cam : Camera.main;
            if (camera == null || !camera.orthographic) return;
            float height = camera.orthographicSize + S.cullMargin, width = height * camera.aspect;
            var center = (Vector2)camera.transform.position;
            int emitted = 0, available = Mathf.Max(0, S.maxParticles - ParticleCount);
            for (int checkedCount = 0; checkedCount < S.emitterScanBudget && emitters.Count > 0 && emitted < S.emissionBudget && available > 0; checkedCount++)
            {
                if (cursor >= emitters.Count) cursor = 0;
                var e = emitters[cursor];
                if (e.anchor == null || e.visible == null)
                { emitters[cursor] = emitters[emitters.Count - 1]; emitters.RemoveAt(emitters.Count - 1); continue; }
                cursor++;
                var p = (Vector2)e.anchor.TransformPoint(e.offset);
                // Camera-space bounds are authoritative; Renderer.isVisible may lag manual/batch camera renders.
                if (Mathf.Abs(p.x - center.x) > width || Mathf.Abs(p.y - center.y) > height || !e.visible.enabled || clock < e.next) continue;
                e.next = clock + Mathf.Max(.2f, e.interval);
                if (e.effect == EnvironmentEffect.Portal)
                {
                    var offset = MathX.Dir(Hash.F(cursor, Mathf.FloorToInt(clock * 10), world.Gen.seed) * 360) * S.portalRadius;
                    Emit(3, p + offset, -offset / Mathf.Max(.1f, S.portalLife), S.portalLife, S.portalParticleSize, new Color(.55f, .85f, 1, .65f));
                }
                else if (e.effect == EnvironmentEffect.Bubble)
                {
                    var color = e.color; color.a = .5f;
                    Emit(2, p, Vector2.up * S.bubbleRise, S.bubbleLife, S.bubbleSize * e.scale, color);
                }
                else Emit(1, p + Vector2.up * .3f, new Vector2(.08f, S.smokeRise), S.smokeLife, S.smokeSize * e.scale, new Color(.8f, .82f, .8f, .3f));
                emitted++; available--;
            }
            var theme = world.Gen.ThemeAt(center.x, center.y); var profile = theme.environment;
            weather -= tick;
            if (available > 0 && weather <= 0 && emitted < S.emissionBudget && profile.weatherRate > 0)
            {
                weather = 1 / profile.weatherRate;
                int salt = Mathf.FloorToInt(clock * 10);
                var p = center + new Vector2((Hash.F(salt, 1, world.Gen.seed + 8151) * 2 - 1) * width,
                    (Hash.F(salt, 2, world.Gen.seed + 8151) * 2 - 1) * height);
                Emit(0, p, profile.weatherVelocity, profile.weatherLife, profile.weatherSize, profile.dust);
            }
        }

        void Emit(int kind, Vector2 p, Vector2 velocity, float life, float size, Color color)
        {
            var pool = Pool(kind);
            if (pool == null) return;
            var parameters = new ParticleSystem.EmitParams { position = new Vector3(p.x, p.y, 0), velocity = velocity,
                startLifetime = life, startSize = size, startColor = color, rotation = kind == 0 ? p.x + p.y : 0 };
            pool.Emit(parameters, 1);
        }

        ParticleSystem Pool(int kind)
        {
            if (pools[kind] != null) return pools[kind];
            if (kind is 1 or 2 && sheet[0] == null) return null;
            var go = new GameObject("environment particle pool " + kind); go.transform.SetParent(transform, false);
            var pool = go.AddComponent<ParticleSystem>();
            pool.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = pool.main; main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = S.maxParticles; main.startSpeed = 0; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = pool.emission; emission.enabled = false;
            var shape = pool.shape; shape.enabled = false;
            var colors = pool.colorOverLifetime; colors.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.8f, .2f), new GradientAlphaKey(0, 1) });
            colors.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sortingOrder = 30000;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (kind is 1 or 2)
            {
                if (particleAtlas == null) particleAtlas = new Material(Shader.Find("Diverse/EnvironmentParticles")) { mainTexture = sheet[0].texture, name = "Environment atlas particles" };
                renderer.sharedMaterial = particleAtlas;
                var animation = pool.textureSheetAnimation; animation.enabled = true; animation.numTilesX = 4; animation.numTilesY = 4;
                animation.animation = ParticleSystemAnimationType.SingleRow; animation.rowMode = ParticleSystemAnimationRowMode.Custom;
                animation.rowIndex = kind; animation.frameOverTime = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, 1));
                animation.cycleCount = 1;
            }
            else
            {
                if (particleDot == null) particleDot = new Material(Shader.Find("Diverse/EnvironmentParticles")) { mainTexture = Texture2D.whiteTexture, name = "Environment dust particles" };
                renderer.sharedMaterial = particleDot;
            }
            pool.Play(); pools[kind] = pool; return pool;
        }

        void OnDestroy()
        {
            foreach (var material in materials.Values) Release(material);
            Release(particleAtlas); Release(particleDot);
            foreach (var sprite in sheet) Release(sprite);
        }

        static void Release(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset);
        }
    }
}
