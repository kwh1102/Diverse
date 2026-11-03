using UnityEditor;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Redraw the code-drawn figures into Resources/Sprites/*.png after their dot templates (Art/ArtHero.cs) or the
    /// costume colours changed. The PNGs keep their .meta (GUID), so prefabs and UI that show them stay linked.
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.ArtRedraw.RunBatchCharacters
    /// </summary>
    public static class ArtRedraw
    {
        static readonly string[] Characters = { "rabbit_", "elder_", "npc_" };

        [MenuItem("Diverse/Art/Redraw Characters (hero, elders, town folk)")]
        public static void RedrawCharacters()
        {
            Art.ResetLooks();
            ProjectAssetBuilder.BakeSprites(Characters);
        }

        [MenuItem("Diverse/Art/Redraw Enemies")]
        public static void RedrawEnemies()
        {
            Art.ResetLooks();
            ProjectAssetBuilder.BakeSprites("enemy_");
        }

        public static void RunBatchEnemies()
        {
            try { RedrawEnemies(); EditorApplication.Exit(0); }
            catch (System.Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static readonly string[] Objects = { "tree_", "rock_", "bush_", "tuft_", "flower_", "prop_", "house_", "deco_", "landmark_" };

        [MenuItem("Diverse/Art/Redraw Objects (nature, props, houses, landmarks)")]
        public static void RedrawObjects()
        {
            Art.ResetLooks();
            ProjectAssetBuilder.BakeSprites(Objects);
        }

        public static void RunBatchObjects()
        {
            try { RedrawObjects(); EditorApplication.Exit(0); }
            catch (System.Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Diverse/Art/Redraw Gear and UI Frames")]
        public static void RedrawGear()
        {
            Art.ResetLooks();
            ProjectAssetBuilder.BakeSprites("weapon_", "shield", "bolt", "knife", "item_", "shadow_");
            UIBuilder.RedrawFrames();
        }

        public static void RunBatchGear()
        {
            try { RedrawGear(); EditorApplication.Exit(0); }
            catch (System.Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunBatchCharacters()
        {
            try { RedrawCharacters(); EditorApplication.Exit(0); }
            catch (System.Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
