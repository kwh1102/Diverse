# World Chronicle (Diverse)

> **세계가 당신의 삶을 기억하는** 도트 액션 로그라이트. 플레이 방식이 **AI를 통해 새로운 능력으로 진화**한다.
> Unity 6 (6000.0.62f1) · URP · Input System

## 실행

1. Unity에서 `Assets/_Project/Scenes/Main.unity`를 연다 (메뉴 **Diverse → 메인 씬 열기**)
2. ▶ Play. 모든 오브젝트는 코드로 생성되므로 씬에는 `Game` 오브젝트 하나만 있다.

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
| J | 연대기 |
| 1 | 회복약 |
| S | 정지 |
| Esc | 메뉴 |

## 게임 구조

- **삶(Run)**: 코스튬 + 시작 무기 선택 → 마을(기억의 광장)에서 시작 → 무한한 세계 탐험
- **죽음**: 쓰러진 자리에 무덤이 남고, 가장 강했던 능력 2개가 새겨진다. 다음 삶에서 무덤을 찾아가면 **계승**할 수 있다.
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

## AI 능력 진화 (기획의 핵심)

```
[플레이] → ① 텔레메트리 → ② 특징 추출 → ③ 패턴 분석 → ④ 능력 그래프 분석 (Active/Seeded)
        → ⑥ 생성 컨텍스트 → ⑦ 원자 검색 (+ 탐색용 풀)
        → ⑧ LLM #1: 능력 그래프 생성 (구조만)
        → ⑩ 스키마 검증 → ⑪ 세계 규칙 검증/수리 → ⑫ 시너지(태그) → ⑬ 점수화 → ⑭ 파워 버짓 (수치는 코드가 결정)
        → ⑮ LLM #2: 이름/설명 → 플레이어가 3개 중 1개 선택
```

- AI는 **스킬 목록에서 고르지 않는다.** **능력 언어**의 원자(Event/Action/Relation/Entity/Form/Element/Temporal/Condition + 기본 법칙)로 그래프를 조립한다.
- 밸런스 수치는 AI 출력과 상관없이 항상 `AbilityValidator.Balance`가 정한다.
- 기존 강화 능력(예: `[CLONE] ×1.25`)은 같은 태그를 가진 새 능력에 자동으로 적용된다.
- API 키가 없거나 호출이 실패하면 **로컬 생성기(`LocalComposer`)**가 같은 형식으로 그래프를 만들기 때문에 게임은 항상 동작한다.
- 레벨 N이 되는 순간 N+1 레벨용 후보를 백그라운드에서 미리 생성한다. AI 응답이 쓸 수 없으면 레벨업 전까지 재시도하므로 진화 창은 대기 없이 열린다.
- 진화 창에서 이야기꾼(AI)과 대화해 후보를 고칠 수 있다 (골드 소모, 다시 뽑기보다 비쌈). 파이프라인 상세는 **과정 보기**로 확인.

### OpenAI 연결 (선택)

1. https://platform.openai.com/api-keys 에서 API 키 발급
2. 아래 중 한 곳에 **키만** 넣는다:
   - 환경 변수 `OPENAI_API_KEY`
   - `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Diverse\openai_key.txt` (메뉴 **Diverse → AI 키 파일 위치 열기**)
   - 프로젝트 루트의 `openai_key.txt` (git에서 제외됨)
3. 게임 내 **설정 → AI 진화**에서 모델 선택 (GPT-4o mini / GPT-4.1 mini / GPT-4.1 nano / GPT-4.1, 기본값 GPT-4o mini — 목록은 `AiClient.Models`)

> **비용 안내**: OpenAI API는 무료 제공량이 없다. 계정에 크레딧이 있어야 호출된다 (`429 insufficient_quota`는 크레딧이 없다는 뜻). `gpt-4o-mini` 기준 진화 1회(호출 2번)는 보통 1원도 안 되지만 "완전 무료"는 아니다. 키가 없어도 로컬 모드로 게임 전체를 플레이할 수 있다.

## 코드 구조 (`Assets/_Project/Scripts`)

| 폴더 | 내용 |
|---|---|
| `Core/` | `Game`(전체 흐름, partial), `GameEvolution`(진화·의뢰), `GameTime`(히트스톱·슬로모션), `CameraRig`(흔들림·킥·펀치), `Controls`(Input System + 키 재설정), `SaveData`(세계의 기억), `GameEvents`, `Util` |
| `Data/` | `Defs.cs` — 무기 콤보(타격감 수치), 코스튬, 적 테이블 · `Stats.cs` — 능력치 공식 |
| `Art/` | 모든 도트를 코드로 생성(`PixelCanvas`): 캐릭터, 적, 지형, 건물, 이펙트. **`Resources/Sprites/<key>.png`를 넣으면 그 이미지가 우선 사용됨** |
| `World/` | `WorldGen`(시드 기반 무한 지형·구조물), `WorldStreamer`(청크 로딩, 충돌, A* 경로 탐색), `ChunkView`, `World`(구조물 생성 + 기억 반영), `Dialogue`, `Interactable`, `Pickup` |
| `Actors/` | `Actor`(공통: 체력·넉백·피격 플래시·상태이상), `Player`, `Enemy`(공격 예고, AI 7종, 보스 패턴), `Summons`(분신·포탑·궤도 구슬·장판) |
| `Combat/` | `Combat`(판정·피해·타격 연출), `Projectile`, `Skills`(스킬 24종), `Fx`(이펙트 풀) |
| `Abilities/` | `AbilityLanguage`(원자 DSL), `AbilityGraph`, `Telemetry`, `AbilityPipeline`(컨텍스트·검증·밸런스), `LocalComposer`, `AiClient`(OpenAI), `AbilityRuntime`(실행) |
| `UI/` | 도트 스타일 IMGUI: HUD, 진화, 지도, 일시정지·설정·키 설정, 대화 |
| `Editor/` | `Diverse` 메뉴: 파이프라인 검증, AI 키 폴더, 세이브 초기화 |

### 수정을 시작하기 좋은 곳

- **타격감 조정**: `Data/Defs.cs` → 무기별 `ComboStep` (`hitstop`, `shake`, `kick`, `lunge`, `knockback`)
- **스킬 추가**: `Combat/Skills.cs` → `SkillDB`에 정의 + `Run()`에 `case` 추가 + `WeaponDef.skills` 변경
- **새 적**: `DB.BuildEnemies()`에 추가 + `Art/ArtEnemies.cs`에 그리기 함수 + `WorldGen.FillSpec` 등장 목록에 추가
- **새 구조물**: `StructKind` + `WorldGen.PickKind/FillSpec` + `World.BuildStructure`
- **진짜 도트 그림으로 교체**: `Assets/_Project/Resources/Sprites/rabbit_white_Idle0.png`(16 PPU) 같은 파일을 넣으면 코드로 그린 그림 대신 사용됨

## 라이선스

- 폰트: [갈무리(Galmuri)](https://github.com/quiple/galmuri) (SIL Open Font License 1.1) — `Resources/Fonts/LICENSE.txt`
- 그 외 그래픽과 사운드는 모두 코드로 생성.
