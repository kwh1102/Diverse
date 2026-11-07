using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// 에디터 메뉴: Diverse/…
    /// - AI 키 파일 열기: openai_key.txt 위치를 탐색기로 연다
    /// - 세이브 초기화
    /// </summary>
    public static class DiverseTools
    {
        [MenuItem("Diverse/메인 메뉴 씬 열기", priority = 0)]
        public static void OpenMain() => EditorSceneManager.OpenScene(UIBuilder.MenuScenePath);

        [MenuItem("Diverse/AI 키 파일 위치 열기", priority = 40)]
        public static void OpenKeyFolder()
        {
            var path = SaveSystem.KeyFileHint;
            if (!System.IO.File.Exists(path)) System.IO.File.WriteAllText(path, "");
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Diverse/세계 세이브 초기화", priority = 60)]
        public static void ResetSave()
        {
            if (EditorUtility.DisplayDialog("세계 초기화", "모든 삶의 기록(무덤·연대기·탑)을 지울까요?", "지우기", "취소"))
                SaveSystem.DeleteWorld();
        }
    }
}
