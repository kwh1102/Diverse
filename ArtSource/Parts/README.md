# 고정 파츠 도트 원본

`catalog.png`는 고정 파츠 16종의 도트 미리보기입니다. 왼쪽부터 행 순서로 AbilityRules.asset의 목록과 대응합니다.
각 파츠는 32×32 투명 PNG, 16 PPU, Point 필터로 저장합니다. 게임에서는 Resources/Sprites/Parts의 개별 PNG를 사용합니다.
프로젝트의 기존 코드 기반 도트 아트 방식에 맞춰 직접 제작했습니다. 외부 이미지 생성 API는 사용하지 않았습니다.
재제작: 번들 Python으로 Tools/author_fixed_parts.py 실행. 데이터 에셋도 함께 갱신하므로 Inspector 수정 후에는 주의하세요.
