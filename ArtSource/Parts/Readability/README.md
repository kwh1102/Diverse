# 파츠 가독성·UI 수정

## 이미지

- 최종 T1 64×64 / 진화 96×96. 이전 최종 PNG를 확대하지 않고 고해상도 원본을 다시 가져왔다.
- 태엽 시계와 달그림자 고리 T2~T5는 기본 제공 `image_gen` 편집으로 새로 제작했다. [실제 프롬프트](imagegen_prompt.txt), 원본 `representatives.png`, [전후 비교](representatives_comparison.png).
- 알파를 RGB와 분리해 바깥 반투명 가장자리를 제거하고, 면적 축소와 디더링 없는 재질별 4톤 팔레트로 미세한 색 잡음을 정리했다. 고리 안쪽 등 실제 빈 공간은 유지한다.
- 재질의 큰 면과 연속된 보라 외곽선을 보존한다. 등급별 크기와 구조는 T2~T5 순서로 발전하며, 같은 계열의 방향·주재료·색상은 유지했다.
- 전체 176 PNG의 크기, 이진 알파, 팔레트, 여백과 기존 GUID를 검사했다. [검사 기록](validation.json). 수정 전 최종 PNG는 `Before/`에 보관한다.
- `PartArt.For`는 카탈로그 파츠를 더 이상 불투명 경계로 다시 잘라 확대하지 않는다. 위키와 게임에서 같은 캔버스를 사용한다.

## UI

- 선택 카드: 36×36 박스 안의 이미지 영역을 30→28로 조정하고 PNG의 여백을 그대로 보존한다.
- 합성 재료 A/B: 각 재료의 현재 `Ability.Explain()`을 별도 스크롤 영역에 표시한다. 결과의 계승 능력과 진화 설명은 기존 계산 결과를 사용한다. 내용이 바뀌었을 때 설명 스크롤을 맨 위로 돌린다.
- 합성 결과·재료·파츠 목록, 인벤토리 상세·장착 요약·수령 대기, 보상 카드·대화·기록 등 유효한 19개 스크롤 영역에 방향에 맞는 스크롤바를 연결했다. 넘치면 막대를 보이고 막대의 공간은 미리 확보한다.
- 인벤토리 칸 24→30, 간격 2 유지. 가방·좌우 패널·제목·수령 대기를 함께 조정했다. 10×5 / 7×7 / 6×8 / 8×6 형태 모두 중앙 영역 안에 들어간다. 등급/수량은 칸 아래 영역에 놓고 아이콘과 겹치지 않게 했다. 드래그 판정과 무기 윤곽은 기존 GridLayoutGroup의 새 칸 크기를 따른다.
- 기존 `GameCanvas.prefab`을 부분 수정했다. 기존 오브젝트 ID와 프리팹 GUID를 유지한다. 초기 데이터 저작 스크립트와 능력·수치·레시피는 변경하지 않았다.

## 확인 위치

Unity 에디터: `Assets/_Project/Prefabs/UI/GameCanvas.prefab` → `Evolve/Panel/Card 1~3`, `PartFusion/Panel/MaterialA·MaterialB·Result`, `Inventory/Panel`.

아트: `Assets/_Project/Resources/Sprites/Parts` 및 `Evolutions`.

Unity를 실행하지 않은 정적 배치 미리보기다. 실제 런타임 스크린샷이나 동작 테스트 결과가 아니다.

- [10×5 인벤토리](inventory_10x5.png), [7×7](inventory_7x7.png), [6×8](inventory_6x8.png), [8×6](inventory_8x6.png)
- [선택 카드](reward_cards.png), [합성 창](fusion.png)

`Tools/patch_part_readability.py`는 직렬화된 기존 프리팹을 부분 수정하며, `UIBuilderPartReadability.cs`는 같은 구성을 에디터에서 유지하는 메뉴다. `Tools/preview_part_readability.py`는 프리팹 좌표와 PNG를 사용한 정적 배치 확인용이다. 게임 실행·컴파일·테스트는 수행하지 않았다.
