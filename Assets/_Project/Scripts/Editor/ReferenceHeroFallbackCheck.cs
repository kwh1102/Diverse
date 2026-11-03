using System;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class ReferenceHeroFallbackCheck
    {
        public static void RunBatch()
        {
            try
            {
                Art.ResetLooks();
                var pack = DesignPack.Default(); pack.id = "reference-fallback-check";
                pack.hero = new HeroLook { basePerk = "white" };
                var custom = DesignStore.PreviewCostume(pack); custom.id = "d:reference-fallback-check";
                int checkedPoses = 0;
                foreach (Pose pose in Enum.GetValues(typeof(Pose)))
                {
                    var expected = Art.Rabbit(DB.Costume("white"), pose);
                    string name = expected.name;
                    var actual = Art.Rabbit(custom, pose);
                    if (actual.texture != expected.texture || actual.rect != expected.rect || actual.pivot != expected.pivot || actual.pixelsPerUnit != expected.pixelsPerUnit)
                        throw new Exception("Default custom hero differs from imported base: " + pose);
                    if (expected.name != name) throw new Exception("Imported sprite renamed: " + pose);
                    checkedPoses++;
                }
                var tintedPack = pack.Clone(); tintedPack.id = "reference-tinted-check"; tintedPack.hero.fur = "e7a9cf";
                var tintedCostume = DesignStore.PreviewCostume(tintedPack); tintedCostume.id = "d:reference-tinted-check";
                var tinted = Art.Rabbit(tintedCostume, Pose.Idle0);
                if (tinted.texture == Art.Rabbit(custom, Pose.Idle0).texture) throw new Exception("Explicit hero colour was ignored");
                var picturedPack = pack.Clone(); picturedPack.id = "reference-picture-check";
                var pictured = DesignStore.PreviewCostume(picturedPack); pictured.id = "d:reference-picture-check";
                pictured.frames = new[] { new Texture2D(17, 23, TextureFormat.RGBA32, false) };
                if (Art.Rabbit(pictured, Pose.Idle0).texture == Art.Rabbit(custom, Pose.Idle0).texture)
                    throw new Exception("Explicit hero picture was ignored");
                Debug.Log("[ReferenceHeroFallbackCheck] PASS: all " + checkedPoses + " custom-default poses share the imported base pixels/pivots; explicit colours/pictures preserved; imported sprite names unchanged.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
