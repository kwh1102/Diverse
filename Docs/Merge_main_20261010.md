# main 통합 검토 보고서 (2026-10-10)

## 상태와 이력
- 작업 브랜치: `5-기술언어-재정립`, 시작 커밋 `d066bd5`.
- 작업 시작 시 tracked/untracked 변경 없음.
- 백업: `codex/backup-5-before-main-20261010` → `d066bd5`.
- `git fetch origin` 후 최신 원격 main: `6eef826` (PR #8, `6-2차-수정` 병합).
- 공통 조상: `2213517`. 현재 브랜치는 RuleIR 도입 1커밋, main은 적 패턴·성장·지도·워프 변경을 포함.
- 로컬 main은 `fec90b5`로 원격보다 6커밋 뒤였으므로 `origin/main`을 현재 브랜치에 `--no-commit --no-ff`로 병합.
- main으로의 최종 병합은 수행하지 않음. 통합본은 사용자 승인에 따라 현재 기능 브랜치에 커밋하고 해당 원격 브랜치로 push한다.
- 최초 검증 후 커밋을 보류했으며, 이후 사용자가 커밋·push·병합 전 버전 전환을 명시적으로 승인했다. stash 31ecca9에 보관된 검증 완료 통합본을 복원했고 코드/에셋 스냅샷 일치를 확인했다. stash는 보존한다.

## 실제 충돌 7개와 해결
| 파일 | 해결 |
| --- | --- |
| Resources/GameDatabase.asset | 개구리 적 등록·progression 데이터와 abilityRules 참조 모두 보존. 적 목록 아래 순서를 지켜 YAML 구조 유지. |
| Abilities/AbilityGraph.cs | 현재 브랜치의 기존 RuleIR 전환에 따른 삭제 유지. main의 설명 개선 의도를 RuleText와 새 AbilityDef 기반 UI/AI 경로에 반영. 호출자에 구 모델 참조가 남지 않았는지 확인. |
| Abilities/AiClient.cs | AbilityDef·concept·다중 규칙 입력 유지. main의 짧은 AI 분위기 문구 생성 정책 반영. 정확한 수치는 코드의 Explain()이 표시. |
| Abilities/LocalComposer.cs | 새 RuleIR 이름 생성과 Explain() 설명 유지. main에서 변경한 설명 제공 의도도 유지. 구 모델 전용 kind/effects 코드는 복원하지 않음. |
| Actors/Player.cs | FilterXp와 LevelUp 능력 이벤트 유지. main의 성장 곡선·진화 주기·기술 습득과 알림 통합. |
| Core/Game.cs | ToAbility()/Restore()를 통한 능력 복원과 WarpStones/RestoreSkills 복원을 함께 수행. Restore는 획득 이벤트·일회성 보상을 재실행하지 않음. |
| UI/Game/EvolveView.cs | main의 채팅 갱신 빈도 제한과 입력 포커스 처리 유지. 메시지 수와 busy 상태를 별도로 인코딩해 응답 도착 시 갱신 누락도 해결. |

## 논리적 충돌 검토와 수정
- 진화 주기가 2레벨 간격으로 바뀌었는데 RuleIR 사전 생성은 Level+1을 사용하던 문제: NextEvolveLevel을 사용하도록 수정. 대기 중 진화는 현재 레벨 기준 유지.
- RuleIR 대시 교체가 main의 지도 장거리 이동을 취소하지 않던 문제: 교체 성공 시 StopMoving() 호출. 일반 대시와 같은 이동 취소 의도 유지.
- main에서 자동 병합된 EvolveCard가 정확한 RuleIR 규칙을 표시하도록 확인. AI 문구는 규칙 아래 별도 표시.
- RuleText에 공격력/최대 체력 기준 및 거리 단위를 명시해 삭제된 구 AbilityGraph의 설명 개선 반영.
- Actor의 시각적 점프/잠복과 RuleIR 상태 처리, Enemy의 새 패턴/취소와 상태 이벤트, Projectile의 조향 콜백과 능력 콜백 공존 확인.
- ScaledEnemy의 새 패턴 필드 전달, DB의 abilityRules/progression 공존, 세이브의 구 능력 변환/기술/워프 필드와 UI·지도·의뢰 호출 경로 검토.
- 실제 모호한 기능 선택을 요구하는 충돌은 발견하지 않음.

## 검증
- 최종 Unity 컴파일 성공.
- 파이프라인: PASS, 종료 코드 0. 표현 가능 32/32, 밸런스 통과 30/32(거부 사례 포함); 로컬 후보 2160개 중 통과 1530, 거부 630, 예외 0.
- 최종 스모크: PASS, 종료 코드 0. 아래 회귀 검증까지 포함.
- 최종 로그: `Logs/merge-pipeline-final.log`, `Logs/RulePipeline.txt`, `Logs/merge-smoke-verified.log` (git ignore 대상).
- 충돌 마커 0, 미해결 Git 인덱스 항목 0, 작업 트리와 스테이징 코드 차이 0.
- `git diff --cached --check`에서는 원격 main에서 그대로 들어온 frog.asset/frog.asset.meta의 빈 YAML 값 뒤 공백 4개만 경고. 코드 충돌 또는 컴파일 오류는 아님. 추가 수정의 `git diff --check`는 통과.
- 시작 HEAD 대비 코드/에셋 50개 변경, 모두 스테이징. 이 보고서도 통합 커밋에 포함.
- 테스트 실행 도중 한 차례 기존 테스트가 아직 프로젝트를 사용하고 있어 중복 Unity 실행이 종료됨. 기존 실행 종료를 확인한 뒤 순차 실행한 최종 검증은 모두 성공.
- Unity 6000.0.62f1 컴파일 및 기존 파이프라인 검증.
- 전체 플레이 모드 스모크: 메뉴, 무기 6종, 적 패턴, 성장/기술, 유적 보상, 지도/워프, 모든 오버레이, RuleIR 실행, 사망, 이어하기.
- 추가 회귀 검증: RuleIR 능력·기술 1개·워프석 3개 동시 저장/복원; 대시 교체 시 지도 이동 취소.
- 임시 세이브 디렉터리 사용, 테스트 AI 비활성화. 실제 API 호출 검증은 제외.
- git diff --check 및 충돌 마커/미해결 인덱스 검사.

## 에디터에서 확인
`Assets/_Project/Scenes/MainMenu.unity` 또는 `Game.unity`를 열어 플레이.
`Assets/_Project/Resources/GameDatabase.asset`에서 Progression과 Ability Rules 확인.
Diverse → 능력 파이프라인 검증 / 스모크 테스트 (플레이 모드)로 재검증 가능.

## 변경 파일 (시작 HEAD 대비, 아래 경로는 저장소 루트 기준)
- `Assets/_Project/Data/Enemies/bat.asset`
- `Assets/_Project/Data/Enemies/boar.asset`
- `Assets/_Project/Data/Enemies/boss_fox.asset`
- `Assets/_Project/Data/Enemies/boss_knight.asset`
- `Assets/_Project/Data/Enemies/boss_shroom.asset`
- `Assets/_Project/Data/Enemies/fox.asset`
- `Assets/_Project/Data/Enemies/frog.asset`
- `Assets/_Project/Data/Enemies/frog.asset.meta`
- `Assets/_Project/Data/Enemies/frostling.asset`
- `Assets/_Project/Data/Enemies/jelly.asset`
- `Assets/_Project/Data/Enemies/raccoon.asset`
- `Assets/_Project/Data/Enemies/shroom.asset`
- `Assets/_Project/Data/Enemies/wisp.asset`
- `Assets/_Project/Prefabs/UI/GameCanvas.prefab`
- `Assets/_Project/Prefabs/UI/MenuCanvas.prefab`
- `Assets/_Project/Resources/GameDatabase.asset`
- `Assets/_Project/Scripts/Abilities/AiClient.cs`
- `Assets/_Project/Scripts/Abilities/RuleText.cs`
- `Assets/_Project/Scripts/Actors/Actor.cs`
- `Assets/_Project/Scripts/Actors/Enemy.cs`
- `Assets/_Project/Scripts/Actors/EnemyPatterns.cs`
- `Assets/_Project/Scripts/Actors/EnemyPatterns.cs.meta`
- `Assets/_Project/Scripts/Actors/Player.cs`
- `Assets/_Project/Scripts/Art/ArtEnemies.cs`
- `Assets/_Project/Scripts/Art/ArtWorld.cs`
- `Assets/_Project/Scripts/Combat/Projectile.cs`
- `Assets/_Project/Scripts/Core/Controls.cs`
- `Assets/_Project/Scripts/Core/Game.cs`
- `Assets/_Project/Scripts/Core/GameEvolution.cs`
- `Assets/_Project/Scripts/Core/GameWarp.cs`
- `Assets/_Project/Scripts/Core/GameWarp.cs.meta`
- `Assets/_Project/Scripts/Core/SaveData.cs`
- `Assets/_Project/Scripts/Data/Defs.cs`
- `Assets/_Project/Scripts/Data/GameDatabase.cs`
- `Assets/_Project/Scripts/Editor/DefaultData.cs`
- `Assets/_Project/Scripts/Editor/SmokeTest.cs`
- `Assets/_Project/Scripts/Editor/UIScreenshots.cs`
- `Assets/_Project/Scripts/UI/Game/EvolveCard.cs`
- `Assets/_Project/Scripts/UI/Game/EvolveView.cs`
- `Assets/_Project/Scripts/UI/Game/HudView.cs`
- `Assets/_Project/Scripts/UI/Game/MapCanvas.cs`
- `Assets/_Project/Scripts/UI/Game/MapView.cs`
- `Assets/_Project/Scripts/UI/Game/MinimapView.cs`
- `Assets/_Project/Scripts/UI/Game/SkillSlot.cs`
- `Assets/_Project/Scripts/UI/Menu/CharacterSelectView.cs`
- `Assets/_Project/Scripts/World/Dialogue.cs`
- `Assets/_Project/Scripts/World/World.cs`
- `Assets/_Project/Scripts/World/WorldGen.cs`
- `Assets/_Project/Scripts/World/WorldStreamer.cs`
- `ProjectSettings/ProjectSettings.asset`
