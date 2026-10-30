# 대규모 지형 시스템 (Landforms) — 2026-10-30

무한맵 위에 산맥·협곡, 강·다리·여울, 호수·습지, 성채 폐허·버려진 마을을 **월드 좌표 기준으로** 깔고, 각 청크는 자기 타일만 계산해 그린다.

## 구조

```
Landforms (World/Landforms.cs)          순수 함수. (시드, 타일 좌표) → Feature
   ├ 기복 필드 n = Fractal(0.0045)       능선 n≈0.5 → 산맥 / 등고선 n≈0.36·0.64 → 강
   ├ 범위 마스크 (산맥용·강용 따로)       어디에 산맥만 / 강만 / 둘 다 / 평원이 되는지
   ├ 호수 노이즈 (강 근처 저지대에 가중)
   ├ 숨은 길 격자 (96타일 간격, 물결선)  산맥→협곡, 강→다리, 호수→둑길, 유적 근처→도로
   └ 사이트 (길 교차점마다 추첨)          성채(Keep) / 마을(Village)의 타일 배치(성벽·성문·붕괴·안뜰·건물터)
WorldGen.GroundAt  (World/WorldGen.cs)  Feature → Ground 타일. 마을 광장·탑·보스 전장·구조물 마당은 지형 억제
WorldGen.GroundOf                        청크(+테두리) 타일 생성 후 바위 침식 2회 (조각 제거). 이웃 2칸 이내 원값만 참조 → 청크 순서 무관
WorldStreamer                            Rock/Peak/Wall 타일은 이동·경로탐색·투사체 모두 차단 (물은 뗏목이면 통과, 여울은 걸어서 통과)
ChunkView                                지면 텍스처 + 장식(산기슭 바위, 강변 갈대, 밀림) + 사이트 소품(깃발·화로·잔해)
World.BuildKeep / BuildVillage           중심 청크가 수비대·대장·보물 생성 (기존 camp/clearedCamps/chest 저장 로직 재사용)
Art.BakeGround (Art/ArtWorld.cs)         Rock/Peak/Wall: 윗면 + 남쪽 절벽면(균열/석재) + 북쪽 밝은 능선 + 그림자 → 높이감
```

새 Ground: `Rock, Peak, Wall`(통행 불가) `Scree, Ford, Rubble`(통행 가능). 새 StructKind: `Keep, Village`. 새 Feature enum은 Landforms.cs 참고.

### 왜 한 필드에서 산과 강을 뽑는가
같은 노이즈의 등고선은 서로 교차하지 않는다. 능선(n≈0.5)과 계곡선(n≈0.36)은 평행하게 흐르므로 강은 자연히 산맥 기슭을 따라가며, 절대 산을 가로지르지 않는다. 마스크 노이즈를 따로 두어 획일적으로 "산마다 강"이 되지 않게 했다.

### 연결성 보장
숨은 길 격자는 어떤 지형도 끊지 못한다(협곡·다리·둑길). 96×96 격자 안에 완전히 갇힌 작은 고립지(강이 고리로 닫힌 섬 등)는 남을 수 있는데, 구조물 후보는 `Landforms.CanLeave`(16방향 40타일 직선 탐색)로 고립지 안에는 놓지 않는다.

### 억제 규칙 (의도된 "열린 땅")
- 마을 반경 62: 산·성채 없음 / 반경 21: 물 없음
- 기억의 탑 반경 24, 구역 보스 전장 반경+16, 일반 구조물 반경+3: 산·물 없음
- 사이트는 원점에서 110 이상, 탑 50·구역 보스 44 이내에는 생성 안 됨

## 검증 (2026-10-30, Logs/Terrain/report.txt, 시드 6개 × 구역/클래식)

| 항목 | 결과 |
| --- | --- |
| 같은 시드·다른 청크 순서 → 같은 타일/구조물 | 12/12 통과 |
| 청크당 지면 계산 | 1.0~1.6 ms (16×16, 침식 포함) |
| 산맥 / 물 / 성벽 비율 (±384타일 창) | 6.5~12.7 % / 4.5~10 % / 0.8~1.0 % |
| 원점에서 걸어서 닿는 열린 땅 | 99.1~99.9 % (고립 구역 13~37개, 최대 1,762타일) |
| 고립된 구조물 | 0 / 1,700여 개 |
| 외톨이 바위 타일 | 최대 1개 (침식 후) |
| 창 안의 성채·마을 | 성채 6~11, 마을 3~11 |
| 플레이 모드 스모크 테스트 (SmokeTest.RunBatch) | PASS, 종료 코드 0 (Logs/terrain_smoke.log) |

스크린샷(Logs/TerrainShots)으로 확인한 것: 산기슭 절벽면과 설산 능선, 협곡 통로, 직선 판자 다리(길 교차점이면 ㄴ자), 징검돌 여울, 강변 습지, 성채 남문·안뜰·내성, 마을 광장·집터, 밀림과 빈터.

배치 실행:
```
Unity.exe -batchmode -nographics -projectPath . -executeMethod Diverse.EditorTools.TerrainTest.RunBatch -logFile Logs/terrain_test.log
Unity.exe -batchmode -projectPath . -executeMethod Diverse.EditorTools.TerrainScreenshots.RunBatch -logFile Logs/terrain_shots.log
```
에디터 메뉴: Diverse → 지형 생성 검증 / 지형 스크린샷 찍기. 개요 지도(1px=1타일)는 `Logs/Terrain/map_<seed>_<mode>.png`.

## 에디터에서 조정할 곳
수치는 아직 Landforms.cs 상수(주파수·임계값·길 간격)에 있다. 다음 단계로 `TerrainRulesDef`를 GameDatabase에 두어 인스펙터에서 조정하게 만드는 것을 권한다.

## 남은 문제 / 향후
- 강 고리 안의 섬(최대 ~1,700타일)은 들어갈 수 없다. 다리 하나를 더 놓는 규칙(고리 감지는 지역적으로 불가 → 여울 확률로 완화 중).
- 고원·절벽 단차(2층 지형)는 미구현. 신전/대형 유적도 기존 소형 Ruins뿐.
- 성채 수비대는 중심 청크에 묶여 있어 중심 청크가 언로드되면 사라진다(기존 camp와 같은 방식).
- 산 색은 바이옴과 무관하게 한 팔레트. 화산 구역은 검붉은 암석 변형이 어울린다.
- 적 AI는 경로탐색 없이 직진+옆걸음이라 성벽 모서리에 걸릴 수 있다(나무와 같은 기존 한계).
- 기존 세이브의 무덤·워프 스톤이 새 산맥 안에 들어갈 수 있다(플레이어 위치는 NearestWalkable로 보정됨).
