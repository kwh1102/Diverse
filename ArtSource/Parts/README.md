# 파츠 아트 제작 기록

T1 32종(64×64)과 진화 144종(96×96), 총 176종의 전용 알파 투명 PNG를 매니페스트 경로에 저장했다. 현재 고유 능력과 매니페스트 모티프를 기준으로 제작했으며 능력·수치·레시피는 변경하지 않았다.

기본 제공 `image_gen`을 사용했다. CLI/API 대체 경로는 사용하지 않았다. T1 첫 20종은 개별 생성하고 이후 사용자 선택에 따라 8칸 시트로 전환했다. 마지막 T1 시트는 5종이며 메아리 방울 수정본을 포함한다. 진화는 한 계열 쌍의 T2~T5를 같은 행에 배치하고 두 계열 쌍씩 한 시트로 생성했다.

- [개별 프롬프트](imagegen_jobs.json), [시트 프롬프트](imagegen_sheets.json), [저장 결과와 실제 프롬프트](imagegen_results.json), [최초 집중 렌즈 프롬프트](focus_lens_prompt.txt).
- `Sheets/`: 선택한 시트 원본. `Crops/`: 실제 알파 연결 성분으로 분리한 원본. 고정 칸 경계를 넘는 실루엣도 온전히 분리하며 이웃 도구와 안내선은 제외한다.
- 개별 생성 원본 위치는 `imagegen_results.json`에 기록했다. 게임에서 참조하는 모든 최종 PNG는 프로젝트 안에 있다.
- `catalog_generated_1.png`: T1 전체. `catalog_generated_2.png`, `catalog_generated_3.png`: 계열 쌍별 T2~T5 비교표.

- 다른 세션용 짧은 프롬프트: [IMAGEGEN_PROMPT.md](IMAGEGEN_PROMPT.md)
- 전체 176개 파일명·설명·경로: [image_manifest.csv](image_manifest.csv)
- 진화 초기 정의: [evolutions.json](evolutions.json)
- 실행 데이터: `Assets/_Project/Data/AbilityRules.asset`
- 규칙 설명: [파츠 진화](../../Docs/PartEvolution.md), [로컬 위키](../../Docs/Wiki/index.html)

진화 외형은 계열 조합·등급별로 공유하며 실제 T1 재료 구성과 능력은 유지한다. T2의 작은 기본 몸체에서 T3의 은빛 보강 구조, T4의 이중 구조와 금 장식, T5의 완성된 합금·결정 구조로 발전한다. 재료 그림을 붙인 배지·장식으로 대신하지 않는다.

`imagegen_pipeline.py`는 생성 원본을 가져오는 아트 전용 도구다. 초기 32/48px 결과에서 발생한 디테일 손실을 개선해, 현재는 `refine_readability.py`의 면적 축소·재질별 4톤 팔레트·강한 알파 마스크·연속 보라 외곽선을 사용한다. 최종 64/96px 파일의 여백을 `PartArt`에서도 보존한다. 태엽 시계와 달그림자 고리 T2~T5는 기본 제공 imagegen으로 다시 그렸고 나머지는 보관한 고해상도 원본에서 다시 가져왔다. [수정 기록 및 미리보기](Readability/README.md)를 참고한다.

도구 하나의 형태, 금속·유리·천 구분, 등급별 성장, 원본 가장자리, 실제 알파와 픽셀 크기를 확인했다. 게임 실행·컴파일·테스트는 사용자 요청에 따라 하지 않았다.

기존 `.meta` GUID를 보존했다. Sprite Single, 16 PPU, Point, readable, 무압축, mipmap 없음, 알파 투명도 설정을 `.meta`에 반영했다. T1은 `parts[].sprite`를 해당 ID 경로로 연결하고 진화는 지정된 경로에 저장했다. 에디터에서 `Assets/_Project/Resources/Sprites/Parts`와 `Evolutions` 폴더, `Assets/_Project/Data/AbilityRules.asset`의 `parts`·`evolutions`에서 확인할 수 있다.

`Tools/export_wiki.py`는 읽기 전용 도감 추출기다. `author_part_catalog.py`, `author_part_evolutions.py`는 **초기 데이터 재저작 도구**여서 Inspector에서 수정한 값을 덮어쓴다. 이미지 연결이나 일반 위키 갱신을 위해 실행하지 않는다.
