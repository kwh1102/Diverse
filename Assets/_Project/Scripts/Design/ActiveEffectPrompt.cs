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
                "Poison" or "Blood"=>"a dense lobed splash with separated substantial droplets and a broken ground ripple",
                "Holy"=>"bevelled ray tips, a brief four-point contact star and short curved halos",
                "Arcane" or "Void" or "Shadow"=>"twisting jagged ribbons, faceted shards and a broken low shock ellipse",
                _=>"sharp bevelled strokes, impact wedges and a few large fragments" };
            return $"Production transparent PNG combat-animation atlas, exactly FOUR columns and {rows.Length} equal rows. Canvas {size}. " +
                "Every row is one FOUR-frame animation with a fixed cell centre/pivot and common framing. Entire effect remains inside its cell with 15 percent clear gutters. No text, grid, scenery, backgrounds, painted checkerboard or UI. " +
                "Match moderately detailed existing top-down pixel RPG sprites: crisp stepped contours, readable connected pixel clusters, dark plum shadow edges, 3-4 distinct flat tones per material, bevel highlights and shaped interior shadows. Richer than flat glyphs, simpler than painted HD art. No fine-grain texture, dither, smooth gradients, blur or large soft glow. " +
                ActiveSkillStyle.Prompt(skill) +
                "COLOUR CONTRACT: use ONLY the supplied icon palette/material colours. Element describes MOTION and SHAPE, never a compulsory colour. For example a purple icon's flame stays purple; do not force orange fire or blue ice. " +
                $"Specific projectile/body motif: {skill.projectilePrompt}. Specific action/impact motif: {skill.effectPrompt}. " +
                "These are actual in-world animations, never pictures of the skill icon, equipment badges or emblems. Build strong anticipation -> active leading edge -> separated follow-through -> short resolving tail. Multiple purposeful layers are welcome: shaped core, sharp secondary stroke and a few substantial fragments. Keep empty spaces and the character readable. Flight rows instead LOOP with a stable substantial body. " +
                string.Join(" ",rows.Select((key,row)=> {
                    var step=skill.steps[key/3];int kind=key%3;
                    string motion=kind==2 && step.delivery=="projectile"?
                        "FLYING BODY LOOP, not a trail: a solid opaque projectile matching the body motif, centred at the collision pivot, pointing RIGHT. Keep the same clearly visible head and body in ALL FOUR frames. Vary fins/ridges and short backward wake only. Core must be several logical pixels thick; no empty, dissolving, launch-only or line-only frame. Keep head at same coordinates; no forward movement across cells.":
                        kind==2?"Movement wake directed RIGHT, parallel sharp swept lines and curled short tails; no projectile body or inventory object.":
                        kind==1?"Contact animation centred on the target: compact ignition, forceful asymmetric star or splash, separated wedges/fragments, short disappearing tail. No whole travelling projectile or copied launch effect.":
                        step.delivery switch {
                            "arc"=>"Sharp crescent swings from upper RIGHT to lower RIGHT about the transparent hand anchor at cell centre, with a bevelled leading edge, inner curved wake and separated tapered ends. Keep the LEFT half empty.",
                            "line"=>"Piercing stroke grows LEFT to RIGHT across the cell, from a left endpoint to a sharp right tip. The cell centre is the MIDPOINT of the line hit area, not the caster. Keep a pointed tip, inner spine and separated parallel wake within the endpoint gutters.",
                            "projectile"=>"A forceful directional LAUNCH flash at the centre, flaring toward RIGHT with short curved discharge fins. No flying body and no inventory arrow icon.",
                            "nova"=>"A ground-centred eruption grows outward with an expanding low elliptical shock, asymmetric core and separated shards.",
                            "field"=>"A low elliptical floor boundary with four distinct corner/node accents, then moving bands and rising short internal strokes. Keep the floor centre transparent.",
                            "trap"=>"A low compact floor seal builds in angular segments, charges its corner nodes then breaks outward in short fragments; no upright sphere.",
                            "guard"=>"A curved protective shell builds around an empty central body space, with bevelled side segments and moving contact glints; no shield badge.",
                            "wall"=>"Deployment ripple only at the feet of the separately placed wall: short low shock ellipse and dust/chip wedges; no giant stripe or copied wall icon.",
                            _=>"A directional RIGHT-facing departure streak, sharp forked ribbons and a clean opening arrival ripple; no whole actor/weapon copies." };
                    string control=step.effect=="pull"?"All hooks and fragments converge INWARD toward the centre.":step.effect=="push"?"Wedges and rings expand OUTWARD from the centre.":
                        step.effect=="heal"?"Uplifting gentle curved wisps and distinct glint clusters around an empty central body; do not depict injury or an explosion.":"";
                    return $"Row {row+1}, step {key/3+1}, {step.delivery}/{step.pattern}/{step.effect}/{step.element}: {motion} Surface/motion detail: {Element(step.element)}. {control}";
                })) + $" Executable action description (do not invent mechanics): {ActiveSkillGenerator.VisualSummary(skill)}.";
        }
    }
}
