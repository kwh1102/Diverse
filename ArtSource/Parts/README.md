# 파츠 아트 제작 인계

현재 카탈로그는 T1 32종이다. 이미지 생성은 사용자 요청으로 보류했으며 신규 원본이 없는 파츠는 기존 PNG를 임시 공유한다. 기존 catalog.png는 이전 16종 시안이므로 최신 도감은 Docs/Parts.md를 본다.

- 다른 세션에 붙여 넣기: [IMAGEGEN_PROMPT.md](IMAGEGEN_PROMPT.md)
- 32개 파일명·이름·최종 경로·임시 원본: [image_manifest.csv](image_manifest.csv)
- 데이터와 합성 규칙: Docs/Parts.md, Docs/PartsCatalog.csv

T1 원본은 투명 32×32 PNG로 제작한다. T2~T5는 주재료와 배지를 합성하므로 결과별 이미지를 생성하지 않는다. 선택적으로 48×48 투명 장식 trim_t2.png~trim_t5.png를 추가한다.

원본을 교체할 때 기존 .meta GUID를 유지한다. Sprite Single, 16 PPU, Point 필터, readable, 무압축, 알파 투명도를 사용한다. 이미지 제작 후 Tools/author_part_catalog.py를 실행하면 존재하는 신규 PNG를 연결하고 도감을 갱신한다. 이 도구는 데이터 에셋도 다시 작성하므로 Inspector에서 조정한 값이 있다면 차이를 확인한다. Tools/author_fixed_parts.py는 같은 도구로 연결하는 호환 진입점이다.
