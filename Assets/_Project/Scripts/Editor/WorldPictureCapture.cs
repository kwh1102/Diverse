using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Captures the starting town from a real play session (the world only exists at runtime) into:
    /// - UI/TitleBackground.png: dimmed, the still background of the MainMenu title
    /// - UI/TownPreview.png: 16 px per unit, an editor-only stand-in in Game.unity so the scene view is not empty
    /// Then wires both into MenuCanvas.prefab / Game.unity. Skips pictures that already exist (repaint them freely).
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.WorldPictureCapture.RunBatch
    /// </summary>
    public static class WorldPictureCapture
    {
        const string RunningKey = "Diverse.Capture.Running";
        const string BatchKey = "Diverse.Capture.Batch";
        const string DirKey = "Diverse.Capture.Dir";
        static string TitlePath => UIBuilder.UIDir + "/TitleBackground.png";
        static string TownPath => UIBuilder.UIDir + "/TownPreview.png";
        const int W = 640, H = 360;

        [MenuItem("Diverse/타이틀 배경·마을 미리보기 다시 찍기", priority = 82)]
        public static void RunMenu()
        {
            if (!EditorUtility.DisplayDialog("다시 찍기", "TitleBackground.png와 TownPreview.png를 지우고 플레이 모드에서 새로 찍습니다.", "진행", "취소")) return;
            AssetDatabase.DeleteAsset(TitlePath);
            AssetDatabase.DeleteAsset(TownPath);
            Begin(false);
        }

        public static void RunBatch() => Begin(true);

        static void Begin(bool batch)
        {
            if (File.Exists(TitlePath) && File.Exists(TownPath)) { Wire(); if (batch) EditorApplication.Exit(0); return; }
            // Scratch save so the capture never touches the player's world (fixed seed: same picture every time)
            var dir = Path.Combine(Path.GetTempPath(), "DiverseCapture");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0 }));
            var w = new WorldSave { seed = 12345 };
            File.WriteAllText(Path.Combine(dir, "world.json"), JsonUtility.ToJson(w));
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", SessionState.GetString(DirKey, null));
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Shooter>() == null)
                new GameObject("Capture").AddComponent<Shooter>();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                if (Object.FindFirstObjectByType<Shooter>() == null) new GameObject("Capture").AddComponent<Shooter>();
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
                UIBuilder.ImportPicture(TitlePath, 100);
                UIBuilder.ImportPicture(TownPath, Art.PPU);
                bool ok = File.Exists(TitlePath) && File.Exists(TownPath);
                if (ok) Wire();
                Debug.Log("[Diverse] 마을 사진 " + (ok ? "저장 완료" : "실패"));
                if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        /// <summary>Hook the pictures into the menu background and the Game scene's editor-only preview.</summary>
        static void Wire()
        {
            var title = AssetDatabase.LoadAssetAtPath<Sprite>(TitlePath);
            var town = AssetDatabase.LoadAssetAtPath<Sprite>(TownPath);

            string menuPath = ProjectAssetBuilder.PrefabDir + "/UI/MenuCanvas.prefab";
            var menu = PrefabUtility.LoadPrefabContents(menuPath);
            var bg = menu.transform.Find("Background")?.GetComponent<Image>();
            if (bg != null && bg.sprite == null && title != null) { bg.sprite = title; bg.color = Color.white; PrefabUtility.SaveAsPrefabAsset(menu, menuPath); }
            PrefabUtility.UnloadPrefabContents(menu);

            var scene = EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            if (town != null && Object.FindFirstObjectByType<EditorOnlyPreview>(FindObjectsInactive.Include) == null)
            {
                var pv = new GameObject("World Preview (editor only)") { tag = "EditorOnly" };
                pv.transform.position = new Vector3(0, 2, 5);
                var sr = pv.AddComponent<SpriteRenderer>();
                sr.sprite = town;
                sr.sortingOrder = -32760;
                pv.AddComponent<EditorOnlyPreview>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        class Shooter : MonoBehaviour
        {
            IEnumerator Start()
            {
                // Let the starting chunks stream in and the spawn pop-ins finish, with the player out of the shot
                for (int i = 0; i < 90; i++) yield return null;
                var g = Game.I;
                if (g != null && g.Player != null) g.Player.gameObject.SetActive(false);
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
                var rig = CameraRig.I;
                rig.target = null;
                rig.Snap(new Vector2(0, 2));
                // (No WaitForEndOfFrame: it never fires in batch mode. Rendering into our own target works mid-frame.)
                yield return null;
                yield return null;
                Debug.Log("[Diverse] 마을 사진 찍는 중");

                var cam = rig.Cam;
                var rt = new RenderTexture(W, H, 24) { filterMode = FilterMode.Point };
                float oldSize = cam.orthographicSize;
                cam.orthographicSize = H / (2f * Art.PPU);    // 1 texel = 1 art pixel
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
                cam.orthographicSize = oldSize;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                rt.Release();

                if (!File.Exists(TownPath)) File.WriteAllBytes(TownPath, tex.EncodeToPNG());
                // The old title dimmed the world by 35% so the text reads well
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i] = Color32.Lerp(px[i], new Color32(13, 8, 20, 255), 0.35f);
                tex.SetPixels32(px);
                if (!File.Exists(TitlePath)) File.WriteAllBytes(TitlePath, tex.EncodeToPNG());
                Destroy(tex);
                EditorApplication.ExitPlaymode();
            }
        }
    }
}
