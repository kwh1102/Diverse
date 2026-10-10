# Rule IR 마이그레이션 — 인수인계

> ✅ **2026-10-06 2차 작업으로 마이그레이션 완료.** 컴파일 OK, `DiverseTools.ValidatePipelineBatch` PASS, `SmokeTest.RunBatch` PASS. 커밋하지 않았다.
> 아래 4·5절은 1차 중단 시점 기록이다. 맨 아래 "7. 2차 작업 결과"를 먼저 볼 것.
> 원본 대화: `Docs/gpt_rule_language_conversation.txt` (ChatGPT 공유 링크에서 추출, 4턴).

## 1. 목표 (GPT 대화 결론)

기존 능력 언어는 `Trigger → Effect` 한 줄 문법(AbilityGraph: trigger / modifier / stat 3종)이라 TFT 증강·세피리아 아티팩트 같은 능력(트레이드오프, 상태 누적, 규칙 재정의, 빌드 스케일링, 다른 능력 수정, 시스템 수정)을 표현할 수 없다. 해결책은 언어의 기본 단위를 **"작은 프로그램"**으로 바꾸는 것이다.

```
Ability = State 변수(bounded) + Rule[] (+ 엔진이 계산하는 tags/tier/power)
Rule.kind = Trigger    (이벤트 → 조건 → 연산)
          | Continuous (조건 → 패시브 수정/변환, 트리거 없음)
          | Meta       (태그로 고른 "다른 보유 능력"을 수정)
          | System     (진화 선택지 수·희귀도·리롤·생성 가중치)
```

설계 원칙 4개 (대화 #43):
- **Primitive는 closed**: 런타임에서 가능한 동작은 전부 개발자가 구현한 핸들러뿐이다 (`RuleLanguage`에 등록 + `AbilityRuntime.Apply`에 case).
- **Composition은 open**: LLM은 primitive를 자유롭게 조합한다. 완성 스킬(Fireball 등)은 DB에 넣지 않는다.
- **Meta modification은 restricted**: Rule→Rule 조작은 whitelist된 연산(Amplify/Repeat/Haste/Infuse/ExtendTagged)과 Tier 제한(레벨 해금)으로만 허용한다. DeleteCondition/ReplaceTrigger 같은 연산은 넣지 않았다.
- **Validation은 deterministic**: LLM이 밸런스를 판단하지 않는다. 검증기가 Schema → Type → Reference → Capability → Bounds → Cycle → Duplicate → Tier → Power(빌드 인지 ΔPower + 자동 수치 조정) → RuntimeCost를 순서대로 돌리고, 실패하면 machine-readable 코드(`stage/code/at/required_fix`)를 낸다. 숫자는 AI가 아니라 balancer가 정한다.

추가 결정 사항:
- Complexity Tier: 0 수치 · 1 반응 · 2 상태 · 3 메타 · 4 시스템. 레벨 해금은 `AbilityRulesDef.tierUnlockLevel`에서 정한다.
- Power 모델: `RulePower = OpPower × TriggerFrequency(텔레메트리 반영) × ConditionAvailability × meta`. 상태형 규칙은 유입량으로 빈도를 제한하고, 메타/시스템 능력은 `Power(build+A) − Power(build)`로 평가한다.
- LLM 파이프라인은 2단계로 한다: Step A 창작 의도(concept) → Step B Rule IR JSON → 검증 실패 시 에러 코드를 주고 최대 2~3회 repair → 그래도 실패하면 폐기.

## 2. 파일 목록

### 새로 만든 파일 (모두 untracked)
| 파일 | 역할 |
|---|---|
| `Scripts/Data/AbilityRulesData.cs` | `AbilityRulesData`(ScriptableObject) + `AbilityRulesDef`: 세계 불변량(규칙/상태/연산 상한, proc depth, spawn cap, proc budget), 파워 예산, Tier 해금 레벨, 텔레메트리 신뢰도 |
| `Scripts/Abilities/RuleIR.cs` | IR 데이터: `AbilityDef`, `StateDef`, `RuleDef`, `CondDef`, `OpDef`, `RuleKind`. **`AbilityRecord` 이 파일로 이동** (구 세이브 → `LegacyGraph.Convert()` 마이그레이션 포함) |
| `Scripts/Abilities/RuleLanguage.cs` | Primitive 레지스트리(`Prim`)로, 기존 AbilityLanguage를 대체한다. Trigger 25 / Selector 8 / Position 7 / Condition 6 / Value 14 / Op 37을 등록했다. 각 항목에 Ctx 타입, tier, 기본값·상한, 런타임 비용, causes(사이클 탐지), risk를 붙였다. 엔티티 Capability(Grip 규칙), Convert 허용 쌍, 스탯 bounds, 계층 태그(`TagMatches`)도 이 파일에 있다 |
| `Scripts/Abilities/RuleText.cs` | IR → 한국어 설명(카드·일시정지·연대기·LLM 이름짓기 입력) |
| `Scripts/Abilities/PowerModel.cs` | `BuildContext`(플레이어 빌드 스냅샷), `PowerReport`, `PowerModel`(빈도 고정점 반복, 상태 유입, 메타 반영, 8차원 PowerVector), `RuleConfig`(데이터 에셋 또는 기본값) |
| `Scripts/Abilities/RuleValidator.cs` | 위 10단계 검증·자동수리·수치 재조정, `Tag()`/`ComputeTier()`/`Mechanic()` |

### 수정한 파일
| 파일 | 변경 |
|---|---|
| `Scripts/Abilities/AbilityRuntime.cs` | **전면 재작성.** Rule IR 실행기: 상태 저장/감쇠/StateFull, Every 주기, Continuous 스탯 재평가(0.25s), per-hit `DamageMultiplier`, Convert 훅(`FilterHeal`, DamageDealt>Heal), Meta(`RebuildMeta`), System 조회(`OfferCount`/`TierBias`/`BudgetMul`/`RerollMul`/`TagWeight`), proc budget·depth·cooldown, EmpowerNext, 모든 Op 핸들러. `Add()`(획득 트리거 발생)와 `Restore()`(로드용) 분리. 기존 `AbilityRecord`는 RuleIR.cs로 이동 |
| `Scripts/Abilities/AbilityPipeline.cs` | **전면 재작성.** `GenerationContext`(Prim 검색, 관측 빈도, maxTier, BuildContext) + `AbilityPipeline.ValidateAll/Score/PickTop`. 구 `AbilityValidator`는 삭제됨 |
| `Scripts/Abilities/LocalComposer.cs` | **전면 재작성.** 구조 템플릿(Reactive/Numeric/Accumulate/Threshold/TradeOff/BuildScaling/Conditional/Conversion/Anchor/Meta/System)으로 Rule IR 조립 + `Name(AbilityDef)` |
| `Scripts/Abilities/Telemetry.cs` | `combatT` 누적, `ObservedRate(action, cfg, out trust)` 추가, `RecordChoice(AbilityDef)`로 시그니처 변경 |
| `Scripts/Abilities/AiClient.cs` | **변경 없음.** 재작성 도중 중단해서 HEAD로 되돌렸다. 여전히 AbilityGraph 기반이다 |

## 3. 이미 패치한 연결 지점
- **Player.cs**: `DamageMultiplierAgainst` → `Abilities.DamageMultiplier`. `AddShield(amount, duration, fx=true)`(지속시간은 max로 연장). `FilterHeal`/`OnHealed` override(Heal→Shield 변환, `Trig.Healed` 발생). 레벨업 시 `Abilities.Raise("LevelUp")`. 보호막이 0이 되면 `Trig.ShieldBroken` 발생
- **Actor.cs**: `Heal`이 `FilterHeal` → `OnHealed`를 거친다(virtual). 내가 건 상태이상이 만료되면 `Trig.StatusExpired`(tags=status id) 발생. DoT 처치도 `Trig.Kill`(depth 1). `StatusApplied`의 tags를 대문자가 아닌 소문자 id로 바꿨다(런타임 param 비교용)
- **GameEvents.cs**: `Trig`에 `StatusExpired`, `ShieldBroken`, `Healed` 추가
- **Summon.cs**: `Summon.Active` 리스트(Awake/OnDestroy, SpawnCap용)
- **Game.cs**: 보스 처치 시 `Raise("BossKilled")`, 군집 소탕 시 `Raise("CampCleared")`. 이어하기는 `rec.ToAbility()` + `Abilities.Restore`. 무덤 각인은 `tier>=1` 중 `power` 상위 2개
- **Session.cs**: AbandonRun 무덤 각인을 `ToAbility()` / `tier` / `power` 기준으로 변경

## 4. 남은 작업 체크리스트
- [ ] `GameDatabase`에 `public AbilityRulesData abilityRules;` 추가 + `Data/AbilityRules.asset` 생성·연결 (ProjectAssetBuilder.BuildData에 "없으면 생성" 추가, 기존 에셋은 덮어쓰지 말 것). `PowerModel.cs`의 `RuleConfig`가 이 필드를 참조한다
- [x] ~~`Telemetry.ObservedRate`~~ — 구현 완료 (임계값 튜닝은 플레이테스트 필요)
- [x] ~~LocalComposer / AbilityPipeline 재작성~~ — 완료. 컴파일·실행 검증은 아직 안 함
- [ ] **AiClient 재작성**: SystemPrompt를 Rule IR 스키마로 교체(`RuleLanguage.Describe(ctx.retrieved/exploration)` 사용), `GraphList`/`ChatResp`를 `List<AbilityDef>`로 변경, Step A(concept)→B(IR) 2단계 출력, `Repair(candidate, result.Machine())` 루프 최대 2회, `StructureJson`(amount/per/duration/count/radius 0 처리), `NameAll(List<AbilityDef>)`, Epitaph는 그대로
- [ ] **GameEvolution.cs 교체**: `List<AbilityGraph>` → `List<AbilityDef>`. 검증은 `AbilityPipeline.ValidateAll(…, BuildContext.Of(Player, level), …)`, 선택은 `PickTop(valid, ctx, Player, Player.Abilities.OfferCount)`. `RerollCost`에 `Abilities.RerollMul`을 곱한다. 프리페치 재검증은 `RuleValidator.Validate(g, BuildContext.Of(Player), rebalance:true)`, Chat 경로도 같은 방식으로 바꾼다
- [ ] **Game.cs** `Offer` 타입을 `List<AbilityDef>`로
- [ ] **UI**: `EvolveCard.Tick(AbilityDef …)` — kind 대신 `RuleText.TierLabel(a.tier)`, desc는 여러 줄. EvolveView는 카드 개수 가변(OfferCount 2~3), HudView/PauseView 확인
- [ ] **무덤(Dialogue.cs ~L298)**: `r.ToAbility()`. 계승 시 `RuleValidator.Validate(graph, BuildContext.Of(g.Player))`로 재조정. 실패하면 원래 수치 그대로 `Abilities.Add`
- [ ] **세이브**: `AbilityRecord`는 RuleIR.cs에 있다. 구 JSON은 `LegacyGraph`로 변환. 실제 구 세이브로 로드 테스트 필요
- [ ] `AbilityGraph.cs`, `AbilityLanguage.cs` 삭제 (+ .meta). 참조가 전부 사라진 뒤에 지울 것
- [ ] `Editor/DiverseTools.ValidatePipeline` 갱신: LocalComposer→RuleValidator 반복, 예외·거부 통계, AI 샘플 JSON을 Rule IR로, 아래 테스트 능력 검증 추가
- [ ] **수동 작성 테스트 능력 6종** (같은 런타임에서 특별 취급 없이 돌아가야 함, 대화 #29~34 + 피의 계약):
  잔영 반격(PerfectDodge→AddState, StateFull→Spawn Clone Copy+Reset) · 역병(Kill+TargetHas poison→Spread) · 고통의 기억(Damaged→AddState scale event.amount, PerfectDodge→Nova scale state+SetState 0) · 원소 집착(Continuous ModifyStat ElementPower scale build.tag:FIRE) · 메아리치는 불꽃(Meta RepeatTagged FIRE) · 도박사의 선택(System OfferCount -1 + TierBias) · (보너스) 피의 계약(MaxHp −, Attack scale self.missingHpPct, hp≤30% Heal>Shield)
  + 반드시 거절돼야 하는 샘플: 무한 Kill→Nova 루프(수리 확인), PerfectDodge→Damage(EventTarget 없음), Clone mode Equip(Grip), 미정의 state 참조
- [ ] 배치 컴파일: `Unity.exe -batchmode -nographics -quit -projectPath . -logFile Logs/compile.log`
- [ ] SmokeTest: `-executeMethod Diverse.EditorTools.SmokeTest.RunBatch` (진화 카드 선택 경로가 새 타입으로 동작하는지 확인). 필요하면 SmokeTest에 테스트 능력 6종을 `Abilities.Add`한 뒤 전투 이벤트를 발생시키는 단계 추가

## 5. 컴파일 상태와 깨진 참조 (예상)
배치 컴파일을 돌리지 않았으므로 아래 목록은 코드를 읽고 추정한 것이다. 새 파일 자체의 오타나 컴파일 오류도 있을 수 있다.
- `Abilities/AbilityGraph.cs`: `AbilityRuntime.IsMultiplicative`(삭제됨) 참조
- `Abilities/AiClient.cs`: `AbilityLanguage.Describe(ctx.retrieved)` — retrieved 타입이 `List<Prim>`으로 바뀜. `GenerationContext.owned`가 `List<AbilityDef>`
- `Abilities/PowerModel.cs`(`RuleConfig`): `DB.Asset.abilityRules` 필드가 아직 없음
- `Core/GameEvolution.cs`: `AbilityValidator`(삭제됨), `List<AbilityGraph>`, `Player.Abilities.Add(AbilityGraph)`, `LocalComposer.Name(AbilityGraph)`, `LocalComposer.Compose` 반환 타입, `Telemetry.RecordChoice(AbilityGraph)`, `g.kind`/`g.trigger`/`g.Signature()` 사용처
- `Core/Game.cs`: `Offer` 필드 타입(`List<AbilityGraph>`)
- `UI/Game/EvolveCard.cs`: `AbilityGraph` 파라미터, `a.kind`
- `World/Dialogue.cs`: `rec.ToGraph()`, `AbilityValidator.Balance`
- `Editor/DiverseTools.cs`: `AbilityValidator`, `AbilityGraph`, `CondNode`, `EffectNode`, `LocalComposer.Compose` 결과 사용
- `UI/Game/PauseView.cs`, `HudView.cs`: 사용하는 멤버(name/source/tags/Explain)는 AbilityDef에도 있어서 그대로 컴파일될 가능성이 높다
- 이름 충돌 주의: `AbilityRecord`가 RuleIR.cs에만 있는지 확인할 것(구 AbilityRuntime.cs에서는 제거함)

## 6. 에디터에서 볼 곳 (완료 후)
- `Assets/_Project/Data/AbilityRules.asset` — 불변량·예산·Tier 해금 (생성 후)
- 메뉴 `Diverse/능력 파이프라인 검증` — 갱신 후 통과/거부/수리 통계
- 진화 화면 "과정 보기" — 후보별 ✓/✗, 티어, ΔPower/예산, PowerVector

## 7. 2차 작업 결과 (2026-10-06)
4절 체크리스트는 모두 끝났다.
- `GameDatabase.abilityRules` 필드를 추가했고 `Data/AbilityRules.asset`을 생성했다(ProjectAssetBuilder는 없을 때만 만든다). 기존 프리팹·씬은 변경되지 않았다
- `AiClient`: Rule IR 시스템 프롬프트(concept → states/rules, 예시 포함), `Generate`, `Repair(ctx, broken)`(검증기 `Result.Machine()` 에러 전달), `Chat`, `StructureJson`, `NameAll(List<AbilityDef>)`
- `GameEvolution`: `AbilityPipeline.ValidateAll` → 실패 후보를 LLM 수리 최대 2라운드 → 로컬 보충 → `PickTop(…, Abilities.OfferCount)`. `RerollCost × RerollMul`. 프리페치 재검증은 `RuleValidator`로 한다
- UI(`EvolveCard`는 Tier 라벨 표시), 무덤 계승(`ToAbility` + 현 빌드로 재조정, 실패하면 각인된 수치 유지), `AbilityGraph`/`AbilityLanguage` 삭제
- `Editor/RuleSamples.cs`(새 파일): 기준 능력 7종 + FIRE 소스 2종 + 거절 샘플 7종 + 루프 파트너
- `DiverseTools`: 메뉴 "능력 파이프라인 검증" + 배치 `ValidatePipelineBatch` → `Logs/RulePipeline.txt`
- `SmokeTest`: "rule abilities" 단계를 추가했다(기준 능력을 실제 플레이어에 붙이고 Damaged/PerfectDodge/ComboFinish/Heal/Kill 이벤트를 발생시켜 발동 수와 OfferCount 확인)

검증 중 고친 버그:
- `Prim.needs` 부분 문자열 매칭("status"가 "at"에 매칭되던 문제) → `Prim.Needs(word)`
- 구 세이브 변환에서 `Dash` → `DashStart` 오매핑
- 사이클 탐지: 한 능력 안의 루프는 런타임(`ABILITY:id`)상 안전하므로 제외하고, 2개 이상 능력 사이의 루프만 검사한다. 루프 이득은 기대 타겟 수로 계산한다
- 밸런서: 스케일 계수 상한을 이론적 최대가 아닌 전형값 기준으로 바꿨다(피격량 상태는 최대 체력의 일부). 런타임에서 스케일된 크기를 primitive max × 1.5로 clamp한다
- 순수 System 능력은 전투력 하한(TOO_WEAK/NO_EFFECT)을 면제한다

현재 수치(로컬 2160회): 통과 71%, 예외 0, 티어 분포 0/1/2/3/4 = 178/490/786/53/31. 거부 사유는 대부분 TOO_WEAK(448)였다.

### 다음에 볼 만한 것 (미완 아님, 튜닝 과제)
- 잔영 반격처럼 StateFull이 드물게 터지는 능력은 예산이 몰려 분신이 "12초·200%"로 커진다. Spawn duration/power 상한을 AbilityRulesDef로 빼서 조정할 것
- 로컬 TOO_WEAK 비율이 높다. LocalComposer가 너무 드문 조건 조합을 고르는 것으로 보인다
- 실제 API 키로 AI 생성·수리 경로는 아직 테스트하지 않았다(배치 테스트는 AI를 끄고 돌린다)
- 원소 집착 같은 빌드 스케일 능력은 해당 태그 능력이 2개 이하이면 TOO_WEAK로 거절된다(의도된 동작: 쌓인 뒤에 등장)

## 8. 3차 작업: 기술언어 최종 완성 (2026-10-06)
GPT 대화가 요구한 범위를 모두 구현했다. 검증 결과: 컴파일 OK, `ValidatePipelineBatch` PASS, `SmokeTest.RunBatch` PASS. 커밋하지 않았다.

### 대화 요구사항과 구현 위치
| 대화 항목 | 구현 |
|---|---|
| Rule 종류 12종 (Effect/Modifier/Conversion/Replacement/Constraint/Scaling/Accumulation/Threshold/Spatial/Meta/System/Generation) | RuleKind 6종(Trigger·Continuous·Replace·Constrain·Meta·System) + 컴파일러 매크로 14종(`AbilityCompiler.Expand`) |
| Ontology: Actor/CombatObject + Capability | `RuleLanguage.Entities`(9종) + `Capabilities`. 새 소환물 Mine/Totem/Barrier/Decoy(`Summon.cs`) |
| Value ontology (Player/Combat/Run) | Value 29종 (`self.*`, `target.*`, `enemies.*`, `time.*`, `entities.count`, `build.*`, `run.*`) |
| Tag DB 계층 | `RuleLanguage.TagParent` |
| State DB (Counter/Stack/Charge/Timer/StoredValue/Flag/Reference/Position…) | StateTypes 7종. Charge(자동 충전), Timer(카운트다운→StateEmpty), Target(적 기억) |
| Trigger DB | 34종 (FirstHit, Overkill, EliteKill, Moved, Pickup, CombatStart/End, EntityExpired, StateEmpty 등 추가) |
| Selector DB (Actor/Entity/Ability/Rule) | 적 선택자 15 + 소환물 선택자 3 + 메타 선택자 6(`ByTag/ByElement/ByTrigger/Strongest/Newest/All`) |
| Position/Form DB | 14종 (DashPath, Forward, Ring, BehindTarget, RandomNear, LastPosition, EntityPos 등) |
| Condition DB (수치/상태/공간/시간/빌드) | 13종 (TargetIs, HasEntity/NoEntity, WithinTime, Same/DifferentTarget, DominantElement 추가) |
| Operation DB (전투/이동/소환/상태/수정/변환) | Op 67종 |
| Modifier 방식 ADD/MULTIPLY/OVERRIDE/MIN/MAX | `ModifyStat.mode` Add/Mul/Min/Max/Override(티어 4). 적용 지점은 `Stats.Clamp` 훅 |
| Conversion pair DB | 10쌍 (모두 런타임 구현: Heal/Overheal/DamageDealt/DamageTaken/Gold/Crit/Overkill/Shield/MoveDistance/Xp) |
| Relation DB + 타입 제약 | `OpDef.rel` 17종, op마다 허용 relation(`Prim.relations`), 소환물은 Capability로 검사 |
| Temporal DB (+ 최대 tick 제한) | `OpDef.time` 9종 (Periodic, UntilHit/UntilDamaged/UntilNextAttack, ForNextN, Stack, Refresh…) |
| Element DB + affinity(편향만) | 10원소 (Blood/Void/Arcane 추가, 상태 bleed/weaken). `ElementAffinity`는 검색 편향에만 사용 |
| Meta Tier 3 + "초기에는 금지"였던 Tier 4 | Amplify/Repeat/Haste/Infuse/Extend/Enlarge/Multiply/Retag/Gate + Tier 4 Unchain(조건 무시)/Retrigger(트리거 교체, 화이트리스트 쌍) |
| System DB | OfferCount(±1)/TierBias/BudgetBias/RerollDiscount/TagBias/ExtraEvolution/UpgradeRandom/DelayedReward(황금알) |
| Primitive metadata (Input/Output/Tier/BaseCost/RuntimeCost/Risk/Constraints) | `Prim` 필드. 수치는 `AbilityRules.asset`의 prims 표(83행)로 Inspector에서 조정 |
| Power Vector 8차원 + Trigger frequency + build-aware | `PowerModel` (+ 오버킬 모델, 자기 피드백 차단, 트리거 교체 가치) |
| Risk Vector | `RiskModel.cs` (8축, cap/warn + 축별 최소 티어) |
| Simulation Validator | `RuleSimulator.cs` (4 시나리오, 같은 이벤트 스트림으로 with/without 비교, proc storm / entity flood / depth cap / 정적 추정과의 괴리) |
| Validator 순서 #38 | Schema→Type→Reference→Capability→Bounds→Cycle→Duplicate→Tier→Power→Cycle2→Risk→RuntimeCost→Simulation |
| Intent → Compiler (#14, #36) | `AbilityCompiler.cs` (별칭 해석 + 매크로 전개). LLM은 `AbilityIntent`(concept + mechanics 매크로 / rules 초안)를 쓴다 |
| Repair loop (#37) | `GameEvolution`이 `Result.Machine()` 에러를 LLM에 돌려줌 (최대 2회) |
| 수치 데이터화 (CLAUDE.md) | 전역 불변량·예산·위험·시뮬레이션 기준·primitive 표 → `AbilityRules.asset` |
| TFT/세피리아 능력 20~30개 표현 테스트 (2턴) | `Editor/RuleExpressiveness.cs` 32종: **표현 가능 32/32, 밸런스 통과 30/32** |

### 새 파일 (3차)
`Abilities/AbilityCompiler.cs`, `Abilities/RiskModel.cs`, `Abilities/RuleSimulator.cs`, `Editor/RuleExpressiveness.cs`, `Editor/RuleDebug.cs`(튜닝용 덤프: `-executeMethod Diverse.EditorTools.RuleDebug.DumpBatch -ruleCase <이름>` → `Logs/RuleDebug.txt`)

### 게임 쪽 연결 (3차)
- `Player`: 대시 대체/봉인, 물약 대체/봉인, 자연 회복 봉인, 이동 거리, 콤보 마무리 대체, 받는 피해(Resist / DamageTaken→Shield), XP 변환, `Stats.Clamp` 훅
- `Summon`: 지뢰/토템/방벽/미끼 + Follow/Attach/Extend/Burst. `Clone`: Extend/Burst. `Projectile`: Live 목록, Block
- `Enemy.Taunt`, `Pickup` → Pickup 이벤트, `Combat`: 새 원소 3종

### 검증 중 고친 모델 버그 (3차)
- 능력이 자기 자신의 상태이상 생산을 자기 조건으로 세던 자기 피드백 (NoFreeRecursion 위반)
- `> 0` 상태 게이트가 발동 빈도를 inflow/0.25로 잘못 제한
- Convert 비율이 primitive 기본값(1.0)으로 덮어써지던 문제 → pair 표 기준으로 수정, 밸런서가 범위 안에서 조정
- 오버킬 미반영 → 단일 대형 타격 할인 (E[min(hp, M)])
- 처형 가치 과대평가 → θ²H/2
- 물약/자연회복 봉인 가격이 실제 가치의 ~35배였음
- 밸런서가 크기만 조정 → 빈도 레버(쿨다운·확률·N번째·주기·임계값)도 조정. 조정 후 Cycle 재검사
- 시뮬레이터를 정적 모델과 동일한 이벤트율·상태 존재율·단위로 맞춤 (보유 능력이 만드는 이벤트도 재생)
- 런타임 미구현 Convert 쌍 2개(MoveDistance>Damage, Xp>Gold) 구현

### 남은 것 (튜닝 과제, 미완 아님)
- 표현력 테스트 △ 2건(부서진 방패, 뒤바뀐 박자)은 일반 빈도(가드 0.08/s, 완벽 회피 0.06/s)에서 보상이 작다는 모델의 정당한 판정이다. 해당 행동을 자주 하는 플레이어는 텔레메트리로 빈도가 올라가 통과한다
- 실제 API 키로 AI(Intent→Compiler) 경로 테스트
- 로컬 생성기 거부의 대부분이 TOO_WEAK — LocalComposer의 조합 가중치 조정 여지
