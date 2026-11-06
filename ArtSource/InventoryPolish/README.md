> Superseded by [CombatUiV2](../CombatUiV2/README.md). The current inventory uses the V2 authoring pass and actual runtime review. Images below document the earlier static pass.

# Inventory and combat presentation polish

## Artwork

`satchel_imagegen.png` is the selected transparent ImageGen source. One generation was used for the reusable satchel panel. `Tools/polish_inventory_art.py` imports the body and handle at native UI resolution, retains hard alpha, removes the empty center's gradient and authors matching code-native pixel frames. These are single, Point-filtered, readable, uncompressed sprites in `Assets/_Project/UI/Frames/satchel_*.png`. Existing asset GUIDs are preserved when the importer is repeated.

Prompt: ONE rectangular open-front satchel back panel for a cozy top-down pixel RPG UI; dark aubergine fabric/leather, slate-purple binding, pale silver hardware, restrained brass rivets, a centered carry handle; empty plain central 80%; crisp chunky pixel clusters, dark purple outline, 3–4 flat tones per material; transparent exterior; no text, grid, items, characters, bloom, gradients, ground or cast shadow. Logical 128×128 art, centered with clear margins. The source was split at the body and handle, the body imported at 128×96 and handle at 40×18.

## Authored UI

- `GameCanvas.prefab / Inventory / Panel`: 30×30 cells, centered 24×24 artwork, quieter frames, generated satchel, thin weapon footprint outline. Existing 10×5, 7×7, 6×8 and 8×6 footprints fit within the existing panel and adjacent columns.
- `PartFusion / Panel / Parts`: top-stretched content, 4px internal padding, 42px rows, icons centered vertically, name/tier block with middle-left alignment. Viewport and scrollbar retain their own reserved widths.
- `Hud / SkillBar / Skill Q,W,E,R / Artwork`: 5px inset (20×20 art inside 30×30 slots), keys and cooldown layers retain their original bounds.
- `UIBuilderInventoryArtwork.cs` mirrors the serialized changes and is called after the existing readability/layout authoring pass. No runtime view sets this layout.

## Combat and generation

- QWER is considered before held/buffered basic attacks. Successful casts cancel pending basic windup/recovery and its queued repeat; a cancelled windup cannot later hit or launch a clone attack. Already resolved hits and launched projectiles remain.
- `ActiveBarrier.prefab` is registered in `GameDatabase`. The entity owns its renderer, lifetime and validity; weapon/skill changes, owner death and cast expiry end it. It uses the actual skill icon's materials and crest. The original duration, length and blocking width still come from `ActiveRules`. It blocks enemy projectiles only, including crossings between frames; it adds no terrain collision, health or autonomous attacks.
- Generated active frames keep their actual silhouette, RGB and alpha. They are no longer replaced with generic arc/triangle/ring masks or a single four-colour tint. Saved effect source sheets can be reprocessed without a new image request.
- Basic swings use one clean, tapered normal-alpha stroke. Large overlapping additive echoes and empty-swing particle layers were removed. New basic effect requests use four real animation rows, each with four registered frames; legacy four-picture packs still load.
- New weapon/shot prompts demand the full tip/grip and 20% gutters inside separate cells. The importer checks clipping before cropping, retains thin/disconnected weapon details and adds transparent margins. Failed source cells use the normal fallback and the paid source sheet is retained. This does not reconstruct missing pixels in older already-clipped saved PNGs or perform paid regeneration on them.

## Review limits

`inventory_*.png` and `fusion_rows.png` are **static authoring previews with representative contents**, generated from prefab dimensions and the real sprites. They are not Unity screenshots. The preview deliberately simplifies button states, scrolling and runtime text wrapping. `static_review.json` records object ID preservation, centered artwork geometry and sprite import/alpha inspection.

Unity, the game, compilers and tests were not run. Gameplay numbers, part abilities, recipes and `Tools/author_part*.py` were not edited. No real save files were modified. The generated panel is visible by opening the existing GameCanvas prefab; the placed object can be edited in the ActiveBarrier prefab.
