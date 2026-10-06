# Diverse (World Chronicle) — 작업 규칙

Unity 6 (6000.0.62f1) · URP · Input System · 2D 도트 로그라이트. 사람이 Unity 에디터에서 보고 고칠 수 있는 구조를 유지하는 것이 최우선이다.

## 구조 원칙 (반드시 지킬 것)

1. **보이는 것은 씬·프리팹에 둔다.** UI, 캐릭터, 고정 오브젝트는 코드에서 `new GameObject`로 만들지 않는다.
   - UI는 uGUI(Canvas) 프리팹: `Assets/_Project/Prefabs/UI/MenuCanvas.prefab`, `GameCanvas.prefab`
   - 런타임에 생성되는 것은 프리팹을 `Instantiate`: `Resources/GameDatabase.asset`에 프리팹 참조를 둔다
   - 예외: 절차 생성 월드(청크·장식), `Fx` 이펙트 풀, `MapCanvas`의 지도 텍스처
2. **수치는 데이터 에셋에 둔다.** 무기/코스튬/적은 `Assets/_Project/Data/*.asset`(ScriptableObject). 새 수치를 코드 상수로 추가하지 말고 해당 `*Def`에 필드를 추가한다.
3. **스크립트는 값을 채우기만 한다.** View(MonoBehaviour)는 `[SerializeField]` 참조로 프리팹 자식을 받고, 텍스트/표시 여부/버튼 이벤트만 다룬다. 레이아웃(위치·크기·색)을 코드에서 정하지 않는다.
4. **목록은 템플릿 복제.** 개수가 변하는 UI(스킬 슬롯, 버튼 목록, 카드, 토스트)는 비활성 템플릿 자식을 두고 `UIKit.Pool`로 복제한다. 템플릿은 프리팹에 남겨 에디터에서 모양을 고칠 수 있게 한다.
5. **MonoBehaviour 1개 = 파일 1개, 파일명 = 클래스명.** 안 지키면 프리팹/씬에 붙일 수 없다(`m_Script: {fileID: 0}`).
6. **씬은 두 개.** `Scenes/MainMenu.unity`(타이틀·캐릭터 선택·설정) → `Scenes/Game.unity`(플레이). 씬 사이 데이터는 `Core/Session.cs`로만 넘긴다.

## 새 기능을 추가할 때

- 새 화면/패널: `GameCanvas.prefab`(또는 `MenuCanvas.prefab`)에 자식으로 추가 → View 스크립트 작성 → `GameUI`/`MenuUI`에 표시 조건 추가. 에디터에서 직접 편집할 수 없는 상황(배치 모드)이라면 `Editor/UIBuilder.cs`에 같은 구성을 추가하되, **기존 프리팹을 지우고 재생성하지 말 것** — 사용자가 에디터에서 고친 내용이 사라진다. 기존 프리팹에 자식을 더하려면 `PrefabUtility.LoadPrefabContents`로 열어 추가하는 에디터 스크립트를 쓴다.
- 새 적: `Data/Enemies`에 EnemyData 에셋 + `GameDatabase.enemies`에 등록 + 그림(`Art/ArtEnemies.cs` 또는 `Resources/Sprites/enemy_<sprite>_0~2.png`)
- 새 무기·코스튬: 데이터 에셋 추가 후 `GameDatabase`에 등록
- 작업 후 사용자에게 "에디터에서 어디를 열면 보이는지"를 함께 알려 준다.

## 검증 (Unity 에디터가 닫혀 있을 때 배치 모드로)

```
"/c/Program Files/Unity/Hub/Editor/6000.0.62f1/Editor/Unity.exe" -batchmode -projectPath . -executeMethod <메서드> -logFile Logs/x.log
```
- 컴파일만: `-batchmode -nographics -quit` (메서드 없이)
- 스모크 테스트: `Diverse.EditorTools.SmokeTest.RunBatch` — 메뉴→게임(무기 6종, 모든 프리팹, 모든 오버레이)→사망→메뉴→이어하기를 실제 버튼으로 진행. 종료 코드 0 = 통과
- UI 스크린샷: `Diverse.EditorTools.UIScreenshots.RunBatch` → `Logs/UIShots/*.png` (레이아웃 확인용)
- 테스트는 `DIVERSE_SAVE_DIR`로 임시 세이브 폴더를 쓴다. 실제 세이브(`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Diverse`)를 건드리지 말 것
- 배치 모드에서는 `WaitForEndOfFrame`이 오지 않는다. `-executeMethod`에서 바로 `EnterPlaymode`하면 멈추므로 `EditorApplication.delayCall`로 미룬다

## 기타

- 커밋은 사용자가 직접 한다. 요청 없이 커밋하지 말 것.
- 소스 파일은 CRLF(.gitattributes/autocrlf), 한국어 UI 문구.
- 폰트: uGUI `Text` + Galmuri(TTF, 동적 폰트). TextMeshPro는 쓰지 않는다(한국어 폰트 에셋 생성이 필요해서).
