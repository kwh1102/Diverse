# 파츠 아트 인계

이미지 생성은 사용자 요청으로 보류했다. T1은 기존 임시 PNG를 사용하며 T2 이상은 전용 그림이 없으면 준비 중 아이콘을 표시한다. 이전 T1 콜라주와 등급 테두리 합성은 사용하지 않는다.

- 다른 세션용 짧은 프롬프트: [IMAGEGEN_PROMPT.md](IMAGEGEN_PROMPT.md)
- 전체 176개 파일명·설명·경로: [image_manifest.csv](image_manifest.csv)
- 진화 초기 정의: [evolutions.json](evolutions.json)
- 실행 데이터: `Assets/_Project/Data/AbilityRules.asset`
- 규칙 설명: [파츠 진화](../../Docs/PartEvolution.md), [로컬 위키](../../Docs/Wiki/index.html)

T1 32종은 32×32, 진화 36계열 쌍 × 4등급은 48×48 투명 PNG로 제작한다. 진화 외형은 계열 조합·등급별로 공유하며 실제 T1 재료 구성과 능력은 유지한다. 재료 그림을 붙인 배지·장식으로 대신하지 않는다.

기존 `.meta` GUID를 보존한다. Sprite Single, 16 PPU, Point, readable, 무압축, 알파 투명도로 가져온다. T1은 `parts[].sprite`를 해당 ID 경로로 연결하고 진화는 지정된 경로에 저장한다. 파일 추가만으로 Sprite 설정이 보장되지는 않으므로 제작 세션에서 TextureImporter도 설정한다.

`Tools/export_wiki.py`는 읽기 전용 도감 추출기다. `author_part_catalog.py`, `author_part_evolutions.py`는 **초기 데이터 재저작 도구**여서 Inspector에서 수정한 값을 덮어쓴다. 이미지 연결이나 일반 위키 갱신을 위해 실행하지 않는다.
