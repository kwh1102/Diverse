# Cute world production art

Current production source: **scenery.png, actors.png, hero.png, props.png, landmarks.png**. Generated with the built-in ImageGen tool using the user-provided Sephiria village screenshot as a **style reference**, then integrated by `Assets/_Project/Scripts/Editor/WorldArtOverhaul.cs`. The earlier realistic tree sheet was rejected and is not imported or stored in this production folder.

The large sheets are source artwork, not in-game screenshots and not the runtime resolution. Import finds actual transparent row/column gutters, crops complete silhouettes, samples by area-majority into the shared palette, keeps binary alpha, and writes native PNG sprites at 16 PPU/Point filtering. Existing PNG GUIDs are retained. `managed.txt` is the authoritative generated output manifest and protects these assets from generic procedural redraw commands.

All source art is deliberately cute, simple and chunky: broad pixel clusters and a few flat light/shade regions, rather than realistic leaves, bark, fine texture or dithering. The terrain, physics and placement are separate code/data systems. See `Docs/WorldArt.md` for the complete runtime and verification workflow.

## Final prompt set

The following is the production prompt specification. Tool mode: **built-in ImageGen**, actual transparent background for every sheet. The supplied game screenshot is always a **style reference only**, never a sheet to crop/extract.

The supplied village/terrain screenshots are preserved under `References/` for reproducible art direction.

### Shared direction

Create new actual sprite assets in the adorable, simple LOW RESOLUTION game look of the attached Sephiria screenshot. NOT realistic, NOT a detailed illustration. Big clean flat pixel clusters, puffy round shapes and toy-like proportions, maximum 3–4 flat shades per material plus one charcoal outline. Think each small sprite is drawn on a 20–40 pixel native canvas, enlarged nearest-neighbor: all pixels are obvious squares. Upper-left light with one broad highlight and one broad lower-right shade. No gradients, antialiasing, realistic bark/fur, individual leaf detail, grain or dithering. Straight front-facing overhead RPG camera with horizontal/vertical ground axes, not isometric. Isolated complete silhouettes, common baseline, wide transparent gutters, no text/grid/labels/shadows/ground.

### scenery.png — 4 columns × 4 rows

Row 1: cute round light-green tree with a large puffy crown and short thick trunk; puffy dark-green oak; three-lobed round cypress; chunky black forked volcanic tree with three tiny orange embers.

Row 2: snow-topped pine with three tiers; round drooping swamp tree; rounded branching cactus; simple three-prong blue crystal cluster.

Row 3: round green bush; three-leaf fern; reed tuft; small pale-blue ice chunk.

Row 4: small warm-gray rounded stone with an angular highlight; medium cool-gray round boulder with one broad facet; dark volcanic stone with two orange cracks; small lavender crystal cluster.

Native tree canvas roughly 32×40 pixels; large flat highlight patches, no branching-canopy realism.

### actors.png — 4 columns × 4 rows

Little chibi figures around 20×24 native pixels; bosses around 36×42. Dot eyes, stubby bodies/limbs, no textured fur or feathers.

Row 1: red-capped mushroom creature with beige stem face and feet; lime-green jelly with two dark eyes; orange fox with large ears/fluffy tail; round gray raccoon with eye mask.

Row 2: orange flame with charcoal face/yellow eyes; cyan frost spirit; round brown boar with pink snout/short tusks; purple bat with open rounded wings.

Row 3: round green frog; big red mushroom boss with two stubby arms; auburn fox spirit with three broad tails; chunky dark armored knight with blue visor and short broad sword.

Row 4: owl storyteller with blue scarf; friendly bear blacksmith with apron; cream cat merchant with green vest; otter ferryman with a straw hat.

### hero.png — 5 columns × 2 rows

The SAME tiny beige rabbit adventurer in every cell: long upright ears, two black dot eyes, round large head, tiny torso, stubby feet, red scarf trailing left. Empty hands, no weapon/hat/armor. Native silhouette around 18×28 pixels; three flat cream/beige fur tones and two scarf tones. Slightly right-facing front/overhead RPG view. Same identity, scale and foot baseline, ears fully inside every cell.

Row 1: idle; breathing/blinking idle; run left foot forward; run passing; run right foot forward.

Row 2: opposite run passing; hurt recoil; raised-hand casting; leaning-back attack windup; leaning-forward empty-handed attack follow-through. Clearly readable pose changes, no extra characters or symbols.

### props.png — 4 columns × 4 rows

Objects on roughly 32×40 to 64×64 native canvases. Cute chunky buildings with roof tops and short front walls; simple architectural details.

Row 1: wood cottage with blue-gray gabled roof/central door; cream plaster cottage with terracotta roof; wood stall with sage cloth awning; triangular ochre tent with dark doorway.

Row 2: mossy shrine with one blue gem; short obelisk with cyan rune; broken squat watchtower with arch doorway; short broken low stone wall.

Row 3: closed wooden chest with brass latch; round stone well with blue water/wood roof; blank wooden sign on short post; short wooden lantern post.

Row 4: broken stone pillar with angular top/moss base; intact short stone pillar; round wooden barrel/two dark bands; square wood crate with diagonal brace.

### landmarks.png — 4 columns × 2 rows

Large landmarks on roughly 96×96 native canvases, broad simple masses and large flat color regions; no detailed illustrative texture. Eight isolated objects with full bases and wide margins.

Row 1: enormous puffy oak with broad short trunk and arched root doorway; broad charcoal volcano with orange crater and two lava channels; monumental cluster of five blue/lavender crystals on a low stone plinth; pale-blue ice castle with three chunky towers and front arch.

Row 2: broad ochre mesa with a little ruined wind tower, simple strata and cave entrance; squat mossy fortress with two round towers/central gate/blue banners; dark-violet ruined palace with cyan mirror portal/broken arch/two pillars; giant purple mushroom shrine with three broad caps, wood stairs and central dark door.

## Import and review

Run **Diverse → Art → Apply Cute World Overhaul** to reproduce the native assets. **Check World Overhaul** writes contracts/spacing/seam checks and a native asset preview to `Logs/Redesign`. **Capture All Terrain Regions** writes actual gameplay screenshots to `Logs/AllTerrain` using scratch saves.

The follow-up background material pass is drawn by the native `TileBaker` renderer; these five approved source sheets remain unchanged. It adds clustered surfaces, soft intermediate colours, finished land/water edges and wooden deck detail. **Apply Background Material Pass** updates only background settings. The full importer keeps the original 66-colour sprite palette while background intermediate colours are appended to the game palette. See `Docs/WorldArt.md` for rules and validation.
