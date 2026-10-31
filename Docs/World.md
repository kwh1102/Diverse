# 월드: 5방향 영역 · 테마 · 대규모 지형 · 몬스터 스폰 (2026-10-31)

## 생성 순서
```
1 월드 골격    ZoneMap (World/ZoneMap.cs)      중앙 스폰 + 5방향 영역. 영역마다 전투 유형(CombatType)과 테마(ThemeDef)를 따로 가짐
2 랜드마크     ZoneMap.sites                    영역마다 랜드마크(순수 보스) 1곳, 경계마다 관문(혼합 보스) 1곳 — 지형보다 먼저 정해짐
3 대규모 지형  Terrain (World/Terrain.cs)       산맥(능선 노이즈, 자연스러운 안부), 메사, 강·호수, 스폰→랜드마크 길
4 테마 입히기  WorldGen.GroundAt                타일마다 테마의 바닥·액체(물/용암/독/얼음)·절벽 모양. 경계는 패치 모양으로 섞임
5 작은 장식    ChunkView                        ThemeDef.decor 목록대로 나무·바위·테마 소품 (일부는 절벽 위에도)
```
모든 단계가 월드 좌표만 읽으므로 청크 순서와 무관하게 같은 월드가 나온다. 일반 모드만 5방향을 쓰고, 커스텀 모드는 기존 노이즈 바이옴을 테마로 바꿔 쓴다.

## 전투 유형 ↔ 테마 (분리됨)
`Data/CombatTypes/*.asset`(8종)이 기본 테마를 가리키고, `WorldSave.zoneThemes`에 테마 id를 넣으면 영역별로 다른 테마를 쓸 수 있다.
월드를 만들 때 8종 중 5종을 중복 없이 뽑아 `WorldSave.zones`에 저장한다(커스텀 선택 UI를 만들면 이 목록만 채우면 됨, 중복 허용).

| 전투 유형 | 기본 테마 | 랜드마크 | 액체 |
|---|---|---|---|
| 근거리 공속 | 증식의 숲 | 태고의 고목 | 물 |
| 근거리 극딜 | 붕괴의 화산지대 | 분화의 심장 | 용암 |
| 원거리 공속 | 공명의 수정평원 | 공명탑 | 물 |
| 원거리 극딜 | 침묵의 빙하지대 | 얼음성 | 얼음(걸어서 건넘) |
| 이동속도 | 질풍의 협곡 | 바람탑 (+메사) | 물 |
| 탱킹 | 불굴의 요새 | 불굴의 성채 | 물 |
| 회피 | 뒤틀린 거울유적 | 거울 궁전 | 물 |
| 광역 | 군체의 늪 | 군체의 신전 | 독늪 |

## 높이(3/4 시점)
`Cliff`(1단)·`Peak`(2단)은 화면에서 `WorldGen.FaceHeight`(1.1칸)씩 솟아 보인다. `Diverse/Ground` 셰이더가 타일마다 기둥을 세워 가장 앞(남쪽) 기둥의 윗면 또는 남쪽 벽을 그린다.
- 절벽 윗면·벽 그림: 테마별 `Art/CliffTops.asset`, `Art/CliffFaces.asset`
- 절벽 타일과 그 뒤로 가려지는 땅(벽 높이만큼)은 이동·투사체 차단 (`WorldStreamer.BlocksOf`)
- 계단·오르막(고지대 진입)은 아직 없음 — 나중에 포켓몬 DS식으로 확장 예정

## 몬스터
야영지·유적·묘지의 무리 스폰을 없앴다. `EnemySpawner`(Game.unity → World)가 플레이어 주변 화면 밖 고리에 작은 무리를 계속 만든다.
종류는 그 지점 테마의 `enemies` 목록, 강도는 스폰에서의 거리. 멀어지면 사라진다. 보스는 랜드마크·관문·소굴에 고정.

## 에디터에서 조정할 곳
- `Data/WorldRules.asset`: 영역(중앙 반경·경계 폭·흔들림), 랜드마크 거리, 산맥(주기·두께·안부 간격), 강·호수, 길, 스폰(최대 수·반경·간격·무리 크기·정예 확률)
- `Data/Themes/*.asset`: 바닥·액체·산 많고 적음·메사·장식 목록·랜드마크·몬스터·보스
- `Resources/Materials/GroundPainted.mat`: 경계 부드러움·벽 높이·벽 텍스처 폭·절벽 그림자
- 그림 추가: `ArtSource/manifest.json` → `gen.ps1` → `Diverse/Art/Import Painted Art`

## 검증·디버그
- `Diverse/World/Overview Maps and Checks` (배치: `Diverse.EditorTools.WorldOverview.RunBatch`) → `Logs/World/`
  - `terrain_<seed>.png`: 지형(절벽은 높이별 음영, 막힌 땅은 자홍색), 랜드마크(흰 점)·관문(노란 점)
  - `zones_<seed>.png`: 테마 분포, 경계선
  - `report.txt`: 산·액체 비율, 스폰에서 걸어서 닿는 땅 비율, 랜드마크 도달 여부, 다른 순서로 다시 생성한 결정성
- `Diverse/Art/Painted World Screenshots` → `Logs/PaintedShots/` (마을, 각 랜드마크, 관문, 절벽, 물가)
- 2026-10-31 결과(시드 3개): 산 5~8%, 액체 4~7%, 도달 98~99.9%, 랜드마크 모두 도달, 결정성 0/4000 차이, 청크당 2 ms
