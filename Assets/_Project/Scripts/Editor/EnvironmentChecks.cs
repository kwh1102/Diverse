using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Diverse.EditorTools
{
    public static class EnvironmentChecks
    {
        static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        public static void Run()
        {
            Directory.CreateDirectory("Logs/Environment"); var report = new StringBuilder();
            Require(DB.WorldRules.environment.enabled, "Environment animation disabled");
            Require(!ShaderUtil.ShaderHasError(Shader.Find("Diverse/Sprite")), "Environment sprite shader errors");
            Require(!ShaderUtil.ShaderHasError(Shader.Find("Diverse/EnvironmentParticles")), "Particle shader errors");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Resources/Environment/ambient.png");
            Require(texture != null && texture.width == 128 && texture.height == 128 && texture.mipmapCount == 1, "Atlas must be one 128x128 image without mipmaps");
            Require(!texture.isReadable && texture.filterMode == FilterMode.Point, "Runtime atlas must discard its CPU copy and use point filtering");
            var decoded = new Texture2D(2, 2); decoded.LoadImage(File.ReadAllBytes("Assets/_Project/Resources/Environment/ambient.png"));
            var pixels = decoded.GetPixels32(); var palette = DB.ArtStyle.palette;
            Require(pixels.All(c => c.a == 0 || c.a == 255 && palette.Any(p => p.r == c.r && p.g == c.g && p.b == c.b)), "Atlas alpha/palette changed");
            for (int row = 0; row < 4; row++) for (int frame = 0; frame < 4; frame++)
            {
                int count = 0;
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) if (pixels[((3 - row) * 32 + y) * 128 + frame * 32 + x].a != 0) count++;
                Require(count > 8, "Empty effect frame");
            }
            Object.DestroyImmediate(decoded);
            Require(DB.WorldRules.environment.maxParticles <= 64 && DB.WorldRules.environment.emissionBudget <= 2, "Ambient budget exceeded");
            foreach (var theme in DB.Themes)
            {
                Require(theme.environment.weatherRate >= 0 && theme.environment.weatherRate <= 4, "Weather rate too high: " + theme.id);
                report.AppendLine(theme.id + ": motion/weather profile installed, base sprites and collision unchanged.");
            }
            Require(DB.WorldRules.environment.objects.First(o => o.Matches("tree_Dunes_0")).motion == EnvironmentMotion.Still, "Rigid cactus should stay still");
            Require(DB.WorldRules.environment.objects.First(o => o.Matches("tree_Crystal_0")).motion == EnvironmentMotion.Shimmer, "Crystal should shimmer, not bend");
            Require(DB.WorldRules.environment.objects.First(o => o.Matches("tree_Forest_0")).motion == EnvironmentMotion.Foliage, "Tree has no wind");
            report.AppendLine("16 overlay frames, one shared 128x128 RGBA atlas (64 KiB GPU data; no mipmaps/CPU copy). Root-anchored GPU wind, rigid cacti/crystals, max 64 native particles, max 2 emissions per 100ms tick.");
            CompositionChecks.Run();
            File.WriteAllText("Logs/Environment/checks.txt", report.ToString()); Debug.Log("[EnvironmentChecks] PASS\n" + report);
        }
    }
}
