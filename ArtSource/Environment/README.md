# 환경 효과 원본

2026-11-02. 내장 `image_gen` 도구로 만든 4열 × 4행 효과 시트다. CLI/API fallback은 사용하지 않았다. 원본은 `effects-source.png`이며 게임에서 직접 로드하지 않는다.

참고한 현재 에셋은 `prop_campfire.png`, `prop_warpstone.png`, `explore_fortress_1.png`, `tree_Forest_0.png`다. 기존 나무·깃발·건물·워프스톤을 다시 그리거나 교체하지 않는다. 생성된 불꽃/연기/물방울/포탈 내부는 별도의 작은 효과 레이어로 사용한다.

`EnvironmentDefaults.ImportSheet`가 프레임별 투명 경계를 확인하고, 행마다 같은 배율과 밑면/중심을 사용해 기존 팔레트·이진 알파·16 PPU의 128×128 아틀라스 `Assets/_Project/Resources/Environment/ambient.png`로 가져온다. 각 셀은 32×32이며 런타임에는 네 행의 Sprite 래퍼만 공유한다. PNG는 약 5KB, RGBA GPU 픽셀 데이터는 64KiB다. 밉맵과 CPU 읽기 복사본을 사용하지 않는다.

재적용 메뉴: **Diverse → World → Apply Environment Animation**. 환경 설정만 설치하며 기존 지형·군집·크기 설정은 유지한다. 세부 설정과 검증은 `Docs/EnvironmentAnimation.md`를 따른다.

## 최종 생성 프롬프트

Use case: precise-object-edit. Asset type: production animation VFX sprite sheet for the existing cute pixel RPG shown in references. The four input images are identity/palette references ONLY, NOT objects to redraw or replace. Preserve their chunky native pixel style and warm orange/cream flame, charcoal outline, soft cyan magic palette. Generate ONE transparent sprite sheet, STRICT 4 columns by 4 rows = 16 isolated effect frames. Each equal cell same canvas, same centered anchor, generous transparent gutters. Read left-to-right temporal frames 0,1,2,3 with seamless loop from 3 back to 0. Row1: ONLY the tiny warm orange and pale yellow flame ABOVE the existing campfire, no logs/base/ground. Flame gently curls left, rises, curls right, settles, fixed bottom anchor, 3 flat colors. Row2: a small soft gray/cream smoke wisp subtly curls upward, 3 flat translucent-looking tones but use discrete pixel shapes. Row3: small cyan water bubble rising and popping into a short ripple, 3 blue tones, bottom-center anchor. Row4: small cyan/teal portal energy swirl with a DARK EMPTY transparent center, flat angular pixel spiral slightly advancing clockwise each frame, SAME outer size and center in all four frames, no stone frame and no new architecture. Low resolution native-pixel appearance, effects comparable to 12-20 actual pixels tall magnified nearest-neighbor. Strict flat pixel clusters, no antialiasing, gradients, realism, labels, UI, ground shadows or background. Do NOT draw the tree, banner or warpstone, only these four families of overlay effects. Exact 4x4 uniform grid, transparent background.
