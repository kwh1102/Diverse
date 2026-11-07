# Combat and inventory V2

The current UI source is `Assets/_Project/Scripts/Editor/UIBuilderInventoryArtwork.cs`. Its authoring pass edits the existing `GameCanvas.prefab` in place. It is also the final step of the previous readability authoring pass, so older layout passes cannot leave the inventory at the intermediate geometry.

Inventory slots are 32×32 with 24×24 centred artwork and a 1px gap. The 10×5 grid has 24.5px of actual left/right clearance inside the 378px satchel; narrower bags have greater horizontal clearance. The 6×8 grid fits within the 263px-high grid bounds. The handle is a matching 20×10 native UI part placed on the same top edge. Equipment, detail, workshop and pending-item tray each have their own frame/material. Headings, body scrolling and action footers have separate internal padding. The active weapon keeps a 2px continuous rim, versus 1px for inactive weapons; the rim has a dark backing, a light edge and small active corner fittings.

`Tools/author_combat_ui_v2.py` authors the code-native pixel UI and imports the ImageGen sources into native combat sprites. It preserves asset GUIDs. Low-alpha source haze is excluded at import, and frame colours are sampled only from the covered subject. Shared row canvases/palettes keep animation registration stable. All shipped frames use actual alpha, Point filtering, Single sprites, 16 PPU, readable textures and no compression. UI sprites use the existing UI convention of 100 PPU and authored slicing borders.

Two built-in ImageGen calls produced `combat_imagegen.png` and `barriers_imagegen.png`. Exact prompts are retained in `imagegen_prompts.json`. The first sheet provides six-frame slash/contact/fire/earth motion. Additional thin cuts, thrusts, ground rings, ice/arcane bursts, shards, projectile bodies and trails are pre-authored into PNGs offline. The second sheet supplies four barrier orientations. Diagonal ground lines and all four body pivots are registered during import.

The running game loads `Resources/CombatVisualLibrary.asset`, registered in `GameDatabase`, and plays these frames through the existing Fx pool. It does not rasterize the project-authored combat layers during an attack. New valid generated attack animations can provide the main silhouette, while the project layers supply sharp leading strokes, contact flashes and ground shock. Legacy icon-like attack sheets use project motion until regenerated. Candidate icons remain separate from combat art.

The wall remains an owned, finite-lifetime projectile blocker, with no autonomous damage or terrain collision. Its body uses the registered directional masonry sprites rather than copies of the skill icon or a runtime stack of strips. Caster-origin walls are placed immediately ahead of the player using the existing actor radius and barrier width. Projectile spawning uses the same action height as the muzzle visual and starts ahead of the hand; its thin trail waits for sufficient flight distance. A full-width registered thrust aligns with the actual line origin. Repeated opaque weapon-copy smears were disabled when the new visual library is present.

## Actual runtime review

`Diverse.EditorTools.CombatUiReview.RunBatch` launches the real Game scene in Unity using a separate scratch save with AI disabled. It exercises the actual inventory view, layout groups, scrollbars, action buttons, fusion view, private basic-attack routine, active runtime, projectile update and placed barriers. The original live save is read only for its inventory/art fixture; live settings, credentials and save files are not written.

The full final review is in `Logs/CombatUiV2/20261107-024134`: four bag sizes, long descriptions, action buttons, fusion, six samples of basic motion in eight directions, three samples of arc/line/projectile/nova/wall casts in eight directions, four explosion variants and an actual enemy contact effect. It passed with no game-generation API calls. Representative copied captures and an animation under `Review/` are real runtime renders, not static layout drawings.

`RunFocus` separately rechecks the last projectile-origin/trail changes and the live manual Q cast interrupting pending basic windup. Its output directory and result are recorded in `runtime_review.json`.

Gameplay data assets, part ability values, recipes and the initial part authoring scripts were not edited. No commit was made. In Unity, open the existing GameCanvas prefab for `Inventory/Panel`, the ActiveBarrier prefab for the placed body, and CombatVisualLibrary.asset for the effect arrays and visual timing/scale settings.

## Slot-guide and satchel restoration

The original generated satchel body and handle are again imported from `ArtSource/InventoryPolish/satchel_imagegen.png`, preserving their sprite GUIDs. Only surrounding panels/slots use the procedural UI frames. Body cropping excludes the original large handle bases; the separate matching 20×10 handle keeps its authored placement and the current 24.5px minimum horizontal clearance. The bag title begins 30px from the edge to clear the buckles.

QWER/◇ guides now use the dedicated upper-left SkillKey label, including occupied attachment slots. Tier/count text uses a separate lower-right 10px-high caption so the font line is not truncated. The guide has an outline and is drawn after the item icon. This follow-up was authored in the prefab and builder without Unity, compilation or tests, as requested; earlier runtime captures above predate this restoration.

## Compact live inventory follow-up

The inventory panel uses an authored 0.85 scale (increased from 0.8 after the compact layout pass). Its logical size is 680×376: the satchel is 378×312, the equipment/workshop column stays 112px wide, and the detail column is 162px wide. The header and pending tray align with the satchel; the handle remains centred. The smaller outer UI retains larger bag contents and separate text/button padding. Footprint pixel snapping includes inherited panel scale, and only the active weapon receives corner fittings.

Inventory no longer pauses simulation or dims the world. Player movement, existing travel targets, combat timers and enemy spawning continue. World clicks outside the panel can set a new travel target; the panel rectangle and active drag/settle capture pointer input. Changing weapons from the inventory preserves travel. Map, fusion and design overlay behavior is unchanged. This follow-up was inspected as source/prefab data only; Unity, compilation and tests were not run.
