using System.Linq;

namespace Diverse
{
    /// <summary>Motion instructions are separate from icon motifs, with an explicit persistent flight-body contract.</summary>
    public static class ActiveEffectPrompt
    {
        public static string Build(ActiveSkillData skill, int[] rows, string size)
        {
            string Element(string element) => element switch {
                "Fire"=>"layered curling flame tongues, opening lobes and short ember curls",
                "Frost"=>"cut-glass facets, angular growing crystalline spikes and separating ice splinters",
                "Lightning"=>"a sharp forked lightning spine, staggered branching strokes and broken electrical arcs",
                "Wind"=>"interleaving tapered wind ribbons and an opening spiral with empty space between bands",
                "Poison"=>"viscous broken lobes, short low bubbles and a few heavy droplets",
                "Blood"=>"a narrow torn ribbon with two heavy droplets and a sharp edge",
                "Holy"=>"bevelled ray tips, a brief four-point contact star and short curved halos",
                "Arcane"=>"interlocking faceted shards with offset bevel glints",
                "Void"=>"inward-folding broken edges around an open centre",
                "Shadow"=>"two interrupted knife-like ribbons with a small negative-space notch",
                _=>"sharp bevelled strokes, impact wedges and a few large fragments" };
            string profile = skill.weaponType switch {
                WeaponType.Sword => "Broad cut with a clean bevel and an open inner edge; contact separates into a short diagonal shear.",
                WeaponType.Dagger => "Narrow nick with an off-axis fork, a tight second cut and quick, close follow-through.",
                WeaponType.Bow => "Long directional tension and a slim, visible leading head; contact bursts across the direction of flight.",
                WeaponType.Gun => "Compact angular pressure front and dense leading body; hard-edged recoil and sudden fragments.",
                _ => "Asymmetric arcane material, layered facets or two broad interleaving ribbons, no generic crescent."
            };
            return $"Production transparent PNG combat-animation atlas, exactly FOUR columns and {rows.Length} equal rows. Canvas {size}. " +
                "Every row is one FOUR-frame animation with a fixed cell centre/pivot and common framing. Entire effect remains inside its cell with 15 percent clear gutters. No text, grid, scenery, backgrounds, painted checkerboard or UI. " +
                "Art direction: compact, hand-pixelled overhead action RPG, viewed at actual game zoom on muted grass, soil and stone. Render for a 48-pixel logical cell. Use crisp stepped contours, connected pixel clusters, muted plum shadows, distinct flat tones and a small bright leading edge. Preserve open space. No fine-grain texture, dithering, gradients, painterly shading, bloom, clouds of motes or opaque disks. " +
                ActiveSkillStyle.Prompt(skill) +
                $"Weapon motion vocabulary: {profile} Ground, defensive and control rows keep their own silhouette and pivot. " +
                (skill.slot == 3 ? "Reserve the widest silhouette and one strong asymmetric secondary burst for the signature action. Keep screen space around the character clear. " : "") +
                "COLOUR CONTRACT: use ONLY the supplied icon palette/material colours. Element describes MOTION and SHAPE, never a compulsory colour. For example a purple icon's flame stays purple; do not force orange fire or blue ice. " +
                $"Specific projectile/body motif: {skill.projectilePrompt}. Specific action/impact motif: {skill.effectPrompt}. " +
                "The engine separately draws weapon swings, white hit strokes, player poses and persistent placed objects. Supply only the skill's own moving material: one distinct silhouette, one compact secondary action and a few larger fragments. The main silhouette must differ from an icon, badge or magic circle. " +
                "Non-flight timing: frame 1 a small ignition at the anchor, frame 2 a clearly expanded active silhouette, frame 3 a broken follow-through, frame 4 a much smaller release. Vary silhouette and occupied area, not hue alone. The engine controls direction, aim, spawn position, travel, and hit timing. " +
                string.Join(" ",rows.Select((key,row)=> {
                    var step=skill.steps[key/3];int kind=key%3;
                    string motion=kind==2 && step.delivery=="projectile"?
                        "FLYING BODY LOOP, not a trail: a solid opaque projectile matching the body motif, centred at the collision pivot, pointing RIGHT. Keep the same clearly visible head and body in ALL FOUR frames. Vary fins/ridges and short backward wake only. Core must be several logical pixels thick; no empty, dissolving, launch-only or line-only frame. Keep head at same coordinates; no forward movement across cells. No repeating speed-line marks; the engine supplies the continuous wake.":
                        kind==2?"MOVEMENT WAKE, right-facing. Keep its dense edge at the pivot and taper backwards into two broken ribbons; the engine moves the character.":
                        kind==1?"CONTACT at the fixed target-body pivot. Small compressed contact point, then a clear material-specific break or splash, then fragments close to the target. Leave room for the engine's diagonal hit stroke. Do not redraw the launch or moving projectile.":
                        step.delivery switch {
                            "arc"=>"SWEEP from upper RIGHT to lower RIGHT around the empty central pivot. Let the specified material decide the contour: staggered crystal teeth, curled flame tongues, a split ribbon or offset metal facets. Use the right half, leaving the caster side open.",
                            "line"=>"Piercing stroke grows LEFT to RIGHT across the cell, from a left endpoint to a sharp right tip. The cell centre is the MIDPOINT of the line hit area, not the caster. Keep a pointed tip, inner spine and separated parallel wake within the endpoint gutters.",
                            "projectile"=>"LAUNCH at the fixed muzzle pivot. A compact rightward discharge with one material signature, then tight recoil flecks. Only one flash, even for spread or ring patterns.",
                            "nova"=>"A ground-centred eruption grows outward with an expanding low elliptical shock, asymmetric core and separated shards.",
                            "field"=>"A low ground PULSE at the field centre: short interior bands or angular rising material. Leave open gaps and the outer edge clear for the persistent range marker.",
                            "trap"=>"TRIGGER burst at the fixed ground pivot. A compact snap with short sharp material fragments from the middle outward. The real trap and its range marker are drawn separately.",
                            "guard"=>"A curved protective shell builds around an empty central body space, with bevelled side segments and moving contact glints; no shield badge.",
                            "wall"=>"Deployment ripple only at the feet of the separately placed wall: short low shock ellipse and dust/chip wedges; no giant stripe or copied wall icon.",
                            _=>"ARRIVAL at the ground-level destination, facing RIGHT. Tight split wake opens and releases near the feet. The departure already has a separate wake." };
                    string control=step.effect=="pull"?"All hooks and fragments converge INWARD toward the centre.":step.effect=="push"?"Wedges and rings expand OUTWARD from the centre.":
                        step.effect=="heal"?"Uplifting gentle curved wisps and distinct glint clusters around an empty central body; do not depict injury or an explosion.":"";
                    return $"Row {row+1}, step {key/3+1}, {step.delivery}/{step.pattern}/{step.effect}/{step.element}: {motion} Use this material vocabulary: {Element(step.element)}. {control}";
                })) + $" Executable action description (do not invent mechanics): {ActiveSkillGenerator.VisualSummary(skill)}.";
        }
    }
}
