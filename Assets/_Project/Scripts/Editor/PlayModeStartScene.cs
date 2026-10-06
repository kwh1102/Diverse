using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Diverse.EditorTools
{
    /// <summary>
    /// 에디터에서 Play를 누를 때 시작 씬을 정한다.
    /// - MainMenu/Game 씬을 열어 둔 상태: 그 씬에서 바로 시작 (Game 씬 단독 테스트 가능)
    /// - 그 외 씬(빈 씬 등): MainMenu 씬부터 시작
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeStartScene
    {
        static PlayModeStartScene()
        {
            EditorSceneManager.activeSceneChangedInEditMode += (_, _) => Apply();
            EditorSceneManager.sceneOpened += (_, _) => Apply();
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            var active = SceneManager.GetActiveScene().path;
            if (active == UIBuilder.MenuScenePath || active == UIBuilder.GameScenePath)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(UIBuilder.MenuScenePath);
        }
    }
}
