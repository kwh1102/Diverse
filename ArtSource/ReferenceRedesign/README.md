# Reference redesign — applied art and regional terrain

The user's reference.png is the visual authority. Applied work includes the default rabbit, meadow and volcanic objects, jelly/flame enemies, the shared UI skin, and terrain for all nine base regions. Other costumes, NPCs, bosses, buildings and the remaining regional objects, effects and complete UI layout redesign remain separate work.

## Source and import

- `reference.png`: user-supplied art direction.
- `hero.png`, `objects.png`, `enemies.png`: generated with the built-in ImageGen editor using reference.png. These are production source atlases, not screenshots of gameplay.
- `managed.txt`: sprite keys owned by this importer; general procedural redraw commands preserve these PNGs.
- Unity menu: **Diverse → Art → Apply Reference Redesign (first review)**. Imports the source sheets, snaps to the project palette, exports binary-alpha point-filtered PNGs at 16 PPU, retains existing `.meta` GUIDs and styles the existing UI prefabs in place.
- Atlas cell rectangles are explicitly checked rather than assuming exact generated grid boundaries. Character frames use common canvas sizes and foot pivots; no weapons are baked into the default idle/running rabbit.
- The generic painted-art importer is not used. Reapplying this review intentionally resets this slice's art settings and UI colours; it does not recreate prefabs or change layout.

## Review and checks

First-slice validation (2026-10-31): 52 managed sprite keys imported; all 50 previously existing sprite GUIDs preserved (two new standalone bridge/pond assets). Unity compilation and import passed. The existing play-mode smoke test passed the full menu → game (all weapons, prefabs, overlays) → death → menu → continue flow. Terrain/HUD/pause review renders are in Logs/ReferenceReview. The bridge/pond are prepared assets; they are not yet placed into procedural gameplay.

Open `Assets/_Project/Scenes/Game.unity` and enter Play for the actual game. The title/town preview images are older captures, not a preview of this slice. Open `Assets/_Project/Prefabs/UI/GameCanvas.prefab` or `MenuCanvas.prefab` to edit the shared UI skin. The first slice updates UI materials/colours; a full UI layout pass awaits visual feedback.

**Diverse → Art → Capture Reference Review** renders actual Game-scene terrain, player and UI into `Logs/ReferenceReview/`. It uses a dedicated `Logs/ReferenceReviewSave` scratch save with meadow and volcano zones and AI disabled. The helper runner is editor-only and excluded from builds. It searches generated terrain; it does not manufacture a reference lookalike scene.

Validation commands:

```
Unity.exe -batchmode -nographics -projectPath . -executeMethod Diverse.EditorTools.ReferenceArtImport.RunBatch -logFile Logs/ReferenceArtImport.log
Unity.exe -batchmode -projectPath . -executeMethod Diverse.EditorTools.ReferenceReviewCapture.RunBatch -logFile Logs/ReferenceReviewCapture.log
Unity.exe -batchmode -projectPath . -executeMethod Diverse.EditorTools.SmokeTest.RunBatch -logFile Logs/ReferenceArtSmoke.log
```

## Generation prompts

Shared instructions: use the supplied reference as edit target/art direction; top-down 3/4 orthographic camera; chunky low-resolution square pixel clusters, flat 2–4 tone material ramps, dark charcoal outlines, olive/lime foliage, warm tan earth, cool charcoal stone and intense orange lava. Actual transparent background, isolated complete silhouettes, ample gutters, no text or baked checkerboard, no gradients or painted microtexture.

- **Hero atlas:** 5 columns × 2 rows. Same beige rabbit with long upright ears, small black vertical eyes, short torso, stubby feet and trailing red scarf. Empty hands, no armor. Row 1: relaxed idle, breathing idle, left-foot run contact, run passing, right-foot run contact. Row 2: opposite run passing, hurt recoil, cast with raised hand, leaning-back attack windup, leaning-forward attack hit. Same scale, same baseline, roughly 18×28 native pixel silhouette.
- **Object atlas:** 4 columns × 3 rows. Row 1: reference round leafy tree with short brown trunk, second canopy variation, small green bush, small warm tan field rock. Row 2: blank wooden signpost with horizontal wood marks, yellow three-flower cluster, reference charred branching tree with orange cracks, volcanic rock pile with orange fissures. Row 3: reference horizontal stone bridge, round stone-rimmed blue pond with reeds, closed wooden chest using the same style, campfire using the same style. No ground patches or ground shadows. Keep complete isolated silhouettes; bridge and pond can occupy wider source regions.
- **Enemy atlas:** 5 columns × 2 rows. Row 1: reference squat olive/lime slime with two dark vertical eyes: idle, stretched bounce, squash windup, rightward lunge, hurt. Row 2: reference orange/yellow flame with small charcoal face, two yellow eyes, no limbs/logs: idle, taller flicker, compressed windup, rightward attack with sparks, contracted hurt. Native scale approximately 16×12 for slime and 16×25 for flame, enlarged as pixels. Same identity and palette per row.

The imported assets are a reconstruction, not a pixel-exact extraction. Visual feedback on the real game screenshots should determine the next pass.

## Terrain depth revision

The first terrain feedback called for clearer boundaries, the reference's oblique depth, and better volcanic ground. The terrain renderer now samples connected tier footprints instead of attaching separate front walls to square tiles. `ArtStyle.asset` exposes `cliffRise` (24px), `cliffSkew` (4px) and corner radius (8px). Visible tops, side returns, cap fringes, directional wall shading and their occlusion are rendered together. Raised decorations and the navigation exclusion height use the same projection values.

Volcanic floors use interrupted, staggered fractures and quiet basalt face patches rather than a regular paving grid. Lava patterns use two-pixel clusters. Terrain blending chooses the dominant actual material when materials have equal priority; equal-priority water cannot arbitrarily win over lava because it was the first neighbour sampled. River and pond liquids follow the warped primary zone boundary (neutral water remains near spawn), while soil and scenery still mix in patches. This removes tiny water/ice islands inside a volcanic river.

Unity menu **Diverse → Art → Apply Terrain Depth Review** reapplies the terrain settings. The reference importer also retains this revision. Edit `Assets/_Project/Data/ArtStyle.asset` to tune the projection and ramps. Gameplay can be inspected in `Assets/_Project/Scenes/Game.unity`.

Validation: actual Game-scene captures in `Logs/ReferenceReview`; before images in `Logs/TerrainReview/before`. For both meadow and volcano, a single 2×2-chunk region and the four separate chunks baked in reverse order matched at every pixel (including negative coordinates). The fixed volcanic-river regression at (62,-140) is lava. World checks for seed 12345 report 99.9% reachable open ground, no unreachable sites, and 0/4000 determinism mismatches. Logs: `TerrainReviewCapture.log`, `TerrainWorldChecks.log`, `TerrainArtSmoke.log`.

## All regional terrain

`BiomeTerrainStyle.Configure` extends the reference terrain settings, appending palette entries without renumbering the existing colours. `TileBakerBiomes` draws small, flat pixel clusters at world coordinates; texture boundaries remain continuous across chunks. Existing serialized pattern enum values are preserved; the regional patterns are appended. `TerrainArtReview.Apply` and the reference atlas importer both retain these settings.

| Region | Ground and cliff material |
| --- | --- |
| Overgrowth | Deep green moss, small ferns/leaf litter, root-threaded stone |
| Swamp | Wet silt depressions, lichen edges, muddy root faces, muted acidic pools |
| Glacier | Quiet snowdrifts, blue ice plates/cracks, flat faceted ice faces |
| Canyon | Wind-marked sand, warm sandstone patches and horizontal wall strata |
| Crystal | Muted mineral bed, sparse cyan/lilac fragments, crystalline wall planes |
| Fortress | Worn flagstones with chipped corners/moss joints, masonry faces |
| Mirror | Dark slate-blue broken paving, restrained reflection marks and faceted walls |
| Meadow / Volcano | Retain the reviewed meadow/basalt family and oblique connected contours |

Fortress now uses flagstone as its main surface with grass patches; crystal uses flagstone rather than bright grass for its secondary surface. These replacements are all walkable materials. Terrain height, map noise, combat rules, enemy data and user saves are unchanged by this regional material pass. Explicit image-based custom world designs still keep their own pictures. The current custom slot was inspected read-only and has no terrain image overrides, so it uses these same base terrain materials.

Unity menu **Diverse → Art → Capture All Terrain Regions** renders all nine regions through the actual `Game.unity` scene to `Logs/AllTerrain`. The capture uses its own `Logs/AllTerrainScratch` save and sequentially sets its five test zones to each target theme; it does not read or alter the player's active save. Each region is checked against a contiguous 2×2-chunk render, including reverse-order chunk baking. All nine checks passed with zero mismatched pixels. World connectivity/determinism checks passed seeds 12345, 777 and 4242 (98.3–99.9% reachable, no unreachable sites, 0/4000 mismatches per seed), and the menu → game → all weapons/overlays → death → menu → continue smoke test passed. Logs: `AllTerrainCapture.log`, `BiomeWorldChecks.log`, `BiomeTerrainSmoke.log`.
