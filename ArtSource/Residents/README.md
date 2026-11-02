# 주민과 방랑자 원본

Built-in ImageGen으로 생성한 투명 원본 시트입니다. villagers.png: 5×2, 주민 10종. wanderers.png: 3×2, 방랑자 6종.

최종 프롬프트: prompts.txt. 런타임 스프라이트: Assets/_Project/Resources/Sprites/npc_villager{0..9}_{0..1}.png 및 npc_wanderer{0..5}_{0..1}.png. 각 34×30, 그림 높이 25px, 16 PPU, Point 필터, 공유 팔레트, 바닥 피벗. 두 번째 프레임은 같은 원본에서 1픽셀 움직이는 대기 동작으로 가져옵니다.

Unity 메뉴 Diverse → Art → Import Resident and Wanderer Art로 다시 가져옵니다. 기존 주민 PNG의 GUID는 유지합니다. WorldArtOverhaul과 Exploration Art 재가져오기도 이 시트를 사용하므로 플레이어 토끼가 주민 외형을 덮어쓰지 않습니다.

눈은 `Editor/NpcEyeImport.cs`에서 최종 도트의 기준점을 보존합니다. 면적 평균 축소로 눈 폭/높이가 달라지는 문제를 막는 가져오기 보정이며 원본 생성 프롬프트와 시트는 그대로 보관합니다. 같은 메뉴로 기존 곰·고양이·수달의 눈도 다시 가져옵니다.
