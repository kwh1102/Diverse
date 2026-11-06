# Manual skill effects V3

25 registered motion families, 192 native transparent PNG frames. This pass changes presentation, generation prompts and flight-body selection only; damage, cooldowns, recipes, collision radius, projectile speed/lifetime and executable skill steps are unchanged.

## Art and import

The selected ImageGen source is `skill_motion_source.png`. The built-in image generation tool used the existing feather ring, clock and an earlier game screenshot as style references. Its final prompt is retained verbatim in `imagegen_prompt.txt`. Two earlier drafts were rejected (painted/grainy, then overly simplified) and are not imported.

`Tools/author_skill_fx.py` splits the selected sheet with common row registration, strips low-alpha haze, imports native 64×64 cells with a shared 14-colour palette and preserves existing GUIDs. It authors the remaining motion families offline. All resulting `Assets/_Project/Resources/Sprites/Combat/skill*.png` use actual alpha, Sprite Single, 16 PPU, Point, readable and no compression. `asset_manifest.json` maps every family to its PNG GUIDs. The existing `CombatVisualLibrary.asset` stores all arrays and presentation settings; no runtime shape rasterization is needed for these families. Palette copies are cached at native resolution.

The original `Tools/author_combat_ui_v2.py` preserves the skill arrays/settings if its basic-art/UI pass is run later. Rebuild this pass with the author script above. It does not launch Unity or make image API requests.

| Use | Families |
| --- | --- |
| Melee | skillSlash, skillThrust |
| Launch / contact / wake | skillMuzzle, skillImpact, skillShock, skillTrail |
| Flight bodies | skillArrow, skillBullet, skillOrb, skillBlade, skillSpear |
| Element motion | skillFire, skillIce, skillLightning, skillEarth, skillArcane, skillWind, skillPoison, skillHoly |
| Control / defence / recovery | skillPull, skillGuard, skillHeal |
| Anchors / movement | skillField, skillTrap, skillDash |

`native_motion_preview.png` and `motion_families.gif` show the registered source families. These are native asset previews, not in-game captures. Neutral/seed colours in the preview are replaced by the individual skill icon palette at runtime.

## Colours and generated effects

`ActiveSkillPalette` applies the icon's primary shade ramp and sampled secondary accent to project motion; it does not multiply an orange or blue sprite by an unrelated colour. Old generated sheets also receive that palette correction. New generation prompts specify the primary/secondary icon colours and describe elements as shape/motion rather than forced colours.

`ActiveEffectPrompt` now receives `projectilePrompt` and `effectPrompt`; the old image request omitted both. It separates launch, contact and flight rows, with delivery-specific geometry, element-specific motion, pull/push direction and material detail. Flight rows require a solid head in all four frames. Newly processed sheets use a shared per-row palette and opaque pixel clusters, avoiding soft haze and frame-to-frame palette flicker. Existing saved originals can be reprocessed without another image request. This pass does not automatically purchase replacement art for existing skills.

## Projectile visibility and placement

The old presentation path shrank the whole flight cell to 16px and could accept a movement-line row as a flying body. `ActiveProjectileArt` now keeps native pixels, validates an opaque connected core in every loop frame, uses one shared crop/pivot and scales the rendered body independently from the world-space collision radius. Missing/line-only flight rows use the matching weapon-type projectile. Trails are a separate, smaller, lower-order layer and cannot replace the body's renderer.

Launch flashes use the forward muzzle position at the same action height as the body. Converging projectiles flash at their actual inward-facing launch points. Flight rotation follows velocity. Line effects use the midpoint of the hit segment; ground rings stay unrotated, and elemental contact eruptions stay upright. Contact size/duration are explicit presentation values. Optional delayed secondary layers use the existing FX pool and game time, so they pause with hitstop.

## Review status

Native sprites and serialized sprite/import references were inspected. Source formatting was checked. Unity, compilation, game execution and tests were not run, following the user's earlier instruction; in-game visual quality and timing remain unverified for this pass.

In Unity, inspect `Assets/_Project/Resources/CombatVisualLibrary.asset` for all families, sizes and timing; inspect `Assets/_Project/Resources/Sprites/Combat/skill*.png` for individual frames. Generation instructions live in `Assets/_Project/Scripts/Design/ActiveEffectPrompt.cs`.
