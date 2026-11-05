# World Chronicle (Diverse)

> **세계가 당신의 삶을 기억하는** 도트 액션 로그라이트. 플레이 방식이 **AI를 통해 새로운 능력으로 진화**한다.
> Unity 6 (6000.0.62f1) · URP · Input System

## 실행

1. Unity에서 `Assets/_Project/Scenes/MainMenu.unity`를 연다 (메뉴 **Diverse → 메인 메뉴 씬 열기**)
2. ▶ Play. 타이틀 → 세계 선택 → 캐릭터·무기 디자인 → `Game` 씬으로 넘어간다.

모드 구분 없이 기본 세계·적을 사용하며, 캐릭터와 무기·공격만 디자인한다. 세계는 최대 3개까지 저장한다. 기존 일반 모드 저장은 그대로 이어지고, 이전 커스텀 모드 저장(`slots/custom_*`)은 파일만 보존하며 세계 목록에는 표시하지 않는다.

`Game.unity`를 바로 열고 ▶ 해도 된다(저장된 삶을 이어 하거나, 마지막으로 고른 캐릭터로 새 삶을 시작).

## 씬 구성

| 씬 | 에디터에서 보이는 것 | 플레이하면 추가되는 것 |
|---|---|---|
| `Scenes/MainMenu.unity` | 타이틀 배경(마을 그림), 로고, 버튼, 캐릭터 선택·설정 화면(비활성) | — |
| `Scenes/Game.unity` | 카메라, HUD·일시정지·대화·진화·지도·사망 화면(`GameCanvas`), 시작 마을 미리보기 그림 | 절차 생성 월드(지형·구조물·적), 플레이어 |

- 화면을 고치려면: `Prefabs/UI/MenuCanvas.prefab`, `Prefabs/UI/GameCanvas.prefab`을 열어 위치·크기·색·문구를 직접 바꾼다. 스크립트는 값만 채운다.
- 꺼져 있는 화면(일시정지, 진화 등)은 Hierarchy에서 체크박스를 켜면 에디터에서 볼 수 있다. 플레이하면 상태에 맞게 다시 켜고 꺼진다.
- `Game.unity`의 `World Preview (editor only)`는 시작 마을을 찍어 둔 그림이다. 플레이를 누르면 사라지고 빌드에도 포함되지 않는다.
- UI 프레임 이미지: `UI/Frames/*.png`(9-slice), 타이틀 배경: `UI/TitleBackground.png`. 다시 그려도 된다.

## 에디터에서 다루는 에셋 (`Assets/_Project`)

| 경로 | 내용 |
|---|---|
| `Data/Weapons`, `Data/Costumes`, `Data/Enemies` | 무기(콤보 타격감 수치), 코스튬(색·특화 능력치), 적 수치. **Inspector에서 바로 수정** |
| `Data/PostFx.asset` | 블룸·비네트·색 보정 (카메라의 `Global Volume`이 사용) |
| `Resources/GameDatabase.asset` | 위 데이터 목록 + 코드가 생성하는 프리팹 연결 |
| `Prefabs/Actors`, `Prefabs/Combat`, `Prefabs/World` | Player, Enemy, Clone, Summon, Projectile, Pickup, Interactable, Campfire, Chunk (열면 미리보기 그림이 보임) |
| `Prefabs/UI` | `MenuCanvas`, `GameCanvas` — 모든 화면 |
| `Resources/Sprites/<key>.png` | 코드로 그린 도트를 구워 둔 PNG(16 PPU). **덮어 그리면 게임에 그대로 반영**, 지우면 코드 그림으로 돌아감 |
| `Materials/` | 프리팹 렌더러용 도트 셰이더 머티리얼 |

메뉴 **Diverse**:
- **빠진 에셋 채우기**: 없는 데이터·프리팹·씬·스프라이트만 새로 만든다. **이미 있는 파일은 절대 덮어쓰지 않는다** (처음부터 다시 만들려면 그 파일을 지우고 실행).
- **스프라이트 PNG로 굽기**: 없는 PNG만 새로 굽는다.
- **타이틀 배경·마을 미리보기 다시 찍기**: 플레이 모드에서 시작 마을을 찍어 두 그림을 새로 만든다.
- **스모크 테스트 (플레이 모드)**: 메뉴 → 게임(무기 6종, 모든 프리팹, 모든 화면) → 사망 → 메뉴 → 이어하기를 실제 버튼으로 진행해 본다. 실제 세이브는 건드리지 않는다.
  - 배치 모드: `Unity.exe -batchmode -projectPath . -executeMethod Diverse.EditorTools.SmokeTest.RunBatch` (종료 코드 0 = 통과)
- **UI 스크린샷 찍기**: 모든 화면을 `Logs/UIShots`에 저장한다 (레이아웃 확인용).

### 조작 (모든 키는 **설정 → 키 설정**에서 변경 가능)

| 입력 | 동작 |
|---|---|
| 우클릭 | 최단 경로 이동 / 적을 우클릭하면 추적 후 자동 공격 / NPC·사물 우클릭 시 상호작용 |
| 좌클릭(누르고 있기) | 커서 방향 기본 공격 (무기별 콤보) |
| Q W E R | 무기 스킬 (Q·W·E 일반, R 궁극기) |
| Space | 대시 (충전식, 무적, 완벽한 회피 판정) |
| F | 상호작용 |
| Tab | 세계 지도 (드래그로 이동, 휠로 확대/축소, C로 내 위치) |
| C | 능력 목록 / 즉시 진화 |
| I | 인벤토리 · 무기 전환 · 파츠 합성 |
| J | 연대기 |
| 1 | 회복약 |
| S | 정지 |
| Esc | 메뉴 |

## 게임 구조

- **삶(Run)**: 코스튬 + 시작 무기 선택 → 마을(기억의 광장)에서 시작 → 무한한 세계 탐험
- **장비**: 유형별 격자 가방에서 무기 주변의 QWER·유형별 파츠를 배치한다. 선택한 무기만 효과를 사용한다. [인벤토리·합성·계승 규칙](Docs/Inventory.md)
- **죽음**: 쓰러진 자리에 무덤이 남는다. 사망 화면에서 파츠 하나를 다음 삶의 **유산**으로 선택한다. 옛 저장의 미회수 무덤에서도 능력을 파츠로 계승할 수 있다.
- **세계의 기억**: 소탕한 야영지, 연 상자, 구한 여행자, 동상, 탑의 층, 연대기는 삶이 바뀌어도 이어진다.
- **이야기꾼**: 과거 영웅을 **기록**할지(광장에 동상, 영구 공격력 보너스) **망각**할지(기억의 조각) 고른다.
- **기억의 탑**: 처음부터 북쪽에 보인다. 기억의 조각으로 층을 열면 세계가 강해지고, 꼭대기에는 망각의 기사가 있다.

### 시작 무기 6종 (각자 다른 타격감)

| 무기 | 느낌 | 히트스톱/흔들림 |
|---|---|---|
| 대검 | 느리고 묵직한 3연격, 지면 강타 | 가장 큼 (0.085~0.14초) |
| 검과 방패 | 안정적인 4연격, 가드와 반격 | 중간 |
| 석궁 | 반동으로 몸이 밀림, 관통·확산 사격 | 작음 + 반동 |
| 봉 | 긴 사거리 5연격, 번개 연쇄 | 가벼움 |
| 단검 | 매우 빠름, 치명타 중심, 그림자 이동 | 아주 짧음 |
| 도 | 발도술 — 짧은 정적 뒤 한 번에 베기 | 마무리 타격만 큼 |

### 코스튬 6종 (특화 능력치)

흰 토끼(경험치 +30%) · 갈색 토끼(치명타) · 검은 토끼(재사용 대기시간) · 회색 토끼(체력·방어) · 분홍 토끼(회복·원소) · 금빛 토끼(골드·이동 속도)

## AI 액티브와 고정 파츠

파츠는 고정 16종과 도트 PNG를 사용합니다. 레벨업 후보는 목록에서 선택하고, 같은 종류·같은 등급을 합쳐 강화합니다. 파츠는 액티브를 보조하며 같은 종류는 가장 높은 등급 하나만 적용합니다.

QWER은 습득할 때 AI가 전달 방식·패턴·효과·조건·타이밍·대가를 조합해 새로 설계합니다. 완성된 24종 스킬 표와 패시브 기술 언어는 제거했습니다. 수치는 데이터의 위력 예산으로 계산합니다. 각 스킬은 image-2.5 API로 전용 아이콘·투사체·이펙트를 만들고 저장합니다. 무기 디자인은 평타 아트만 생성합니다. AI를 사용할 수 없으면 로컬 조합으로 동작합니다.

검·활·총·봉·단검 유형별 주변 공간을 사용하고, 같은 유형·같은 슬롯끼리만 스킬을 교환합니다. 종속 스킬은 양쪽 무기의 관계를 보존하도록 함께 옮깁니다. [생성 규칙과 에디터 설정](Docs/ActiveSkills.md), [인벤토리·합성·계승](Docs/Inventory.md)

### OpenAI 연결 (선택)

1. https://platform.openai.com/api-keys 에서 API 키 발급
2. 아래 중 한 곳에 **키만** 넣는다:
   - 환경 변수 `OPENAI_API_KEY`
   - `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Diverse\openai_key.txt` (메뉴 **Diverse → AI 키 파일 위치 열기**)
   - 프로젝트 루트의 `openai_key.txt` (git에서 제외됨)
3. 게임 내 **설정 → AI 액티브**에서 모델 선택 (GPT-4o mini / GPT-4.1 mini / GPT-4.1 nano / GPT-4.1, 기본값 GPT-4o mini — 목록은 `AiClient.Models`)

> **비용 안내**: OpenAI API는 무료 제공량이 없다. 계정에 크레딧이 있어야 호출된다 (`429 insufficient_quota`는 크레딧이 없다는 뜻). `gpt-4o-mini` 기준 진화 1회(호출 2번)는 보통 1원도 안 되지만 "완전 무료"는 아니다. 키가 없어도 로컬 모드로 게임 전체를 플레이할 수 있다.

## 코드 구조 (`Assets/_Project/Scripts`)

| 폴더 | 내용 |
|---|---|
| `Core/` | `Session`(두 씬이 공유하는 설정·세이브·씬 이동), `Game`(게임 씬 흐름, partial), `GameEvolution`(진화·의뢰), `GameTime`(히트스톱·슬로모션), `CameraRig`(흔들림·킥·펀치), `Controls`(Input System + 키 재설정), `SaveData`(세계의 기억), `GameEvents`, `Util` |
| `Data/` | `Defs.cs` — 데이터 형식(`WeaponDef`/`ComboStep`/`CostumeDef`/`EnemyDef`)과 `DB`(에셋에서 읽음) · `WeaponData`/`CostumeData`/`EnemyData`/`GameDatabase` — ScriptableObject · `Stats.cs` — 능력치 공식 |
| `Art/` | 도트 생성기(`PixelCanvas`): 캐릭터, 적, 지형, 건물, 이펙트. `Resources/Sprites/<key>.png`가 있으면 그 이미지를 우선 사용 |
| `World/` | `WorldGen`(시드 기반 무한 지형·구조물), `WorldStreamer`(청크 로딩, 충돌, A* 경로 탐색), `ChunkView`, `World`(구조물 생성 + 기억 반영), `FireFx`, `Dialogue`, `Interactable`, `Pickup` |
| `Actors/` | `Actor`(공통: 체력·넉백·피격 플래시·상태이상), `Player`, `Enemy`(공격 예고, AI 7종, 보스 패턴), `Clone`(분신), `Summon`(포탑·궤도 구슬·장판) |
| `Combat/` | `Combat`(판정·피해·타격 연출), `Projectile`, `Skills`·`ActiveSkillGenerator`·`ActiveSkillRuntime`(AI 조합 액티브), `Fx`(이펙트 풀) |
| `Abilities/` | `FixedParts`(고정 파츠·저장 호환), `AbilityRuntime`(파츠 보조 효과), `Telemetry`, `AiClient` |
| `UI/` | uGUI 화면 스크립트(값만 채움): `Menu/`(MenuUI, TitleView, CharacterSelectView), `Game/`(GameUI, HudView, PauseView, DialogueView, EvolveView, MapView, DeathView, ToastView…), `SettingsView`(두 씬 공용), `UIKit` |
| `Editor/` | `Diverse` 메뉴: 파이프라인 검증, 스모크 테스트, UI 스크린샷, 빠진 에셋 채우기(`ProjectAssetBuilder`·`UIBuilder`, 기본 수치는 `DefaultData`), 마을 그림 찍기, AI 키 폴더, 세이브 초기화 |

### 수정을 시작하기 좋은 곳

- **타격감 조정**: `Data/Weapons/<무기>.asset` → `Combo` 항목 (`hitstop`, `shake`, `kick`, `lunge`, `knockback`)
- **적·코스튬 수치**: `Data/Enemies/*.asset`, `Data/Costumes/*.asset` (코스튬 특화 능력치는 `Mods`: 스탯 / 곱연산 여부 / 값)
- **액티브 규칙·파츠·무기 공간**: `Data/AbilityRules.asset`과 `Combat/ActiveSkillGenerator.cs`, `Combat/ActiveSkillRuntime.cs`
- **새 적**: `Data/Enemies`에서 **Create → Diverse → Enemy**로 만들고 `GameDatabase`의 `Enemies`에 추가 + `Resources/Sprites/enemy_<sprite>_0~4.png` 그림(코드 대체 그림 없음) + `WorldGen.FillSpec` 등장 목록에 추가
- **새 구조물**: `StructKind` + `WorldGen.PickKind/FillSpec` + `World.BuildStructure`
- **도트 그림 교체**: `Resources/Sprites/`의 PNG를 고치면 된다. 키 이름 규칙은 `Art.Get` 호출부 참고 (예: `rabbit_white_Idle0`, `enemy_fox_0`, `prop_well`)

## 라이선스

- 폰트: [갈무리(Galmuri)](https://github.com/quiple/galmuri) (SIL Open Font License 1.1) — `Resources/Fonts/LICENSE.txt`
- 기본 그래픽은 코드로 그리는 픽셀 아트와 ImageGen 원본 시트를 함께 사용한다. 현재 월드 원본·프롬프트는 `ArtSource/WorldOverhaul/`, 아트·배치·검증 안내는 [Docs/WorldArt.md](Docs/WorldArt.md), 8개 지역 군집·크기·도로 설정은 [Docs/WorldComposition.md](Docs/WorldComposition.md), 환경 움직임·효과·최적화는 [Docs/EnvironmentAnimation.md](Docs/EnvironmentAnimation.md). 사운드는 코드로 생성한다.
