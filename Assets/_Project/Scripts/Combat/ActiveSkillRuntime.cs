using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Bounded per-cast graphs. Events and anchors never escape the lifetime of a manual cast.</summary>
    public static class ActiveSkillRuntime
    {
        static readonly Dictionary<Player, int> running = new Dictionary<Player, int>();
        static readonly Dictionary<string, CastContext> recasts = new Dictionary<string, CastContext>();
        static ActiveRules C => ActiveSkillGenerator.Rules;
        class Stage
        {
            public Vector2 start, position, impact;
            public bool hit, arrived, finished;
            public int live;
        }
        class CastContext
        {
            public Player player;
            public ActiveSkillData skill;
            public string weaponId;
            public float attack, power, deadline, control, range, evolutionHeal;
            public bool healed, closed, awaitingRecast, recast;
            public int live, shots;
            public PartAttackState parts;
            public readonly HashSet<Actor> affected = new HashSet<Actor>();
            public bool Valid => !closed && Time.time < deadline && player != null && player.Alive && player.Inventory?.Active?.id == weaponId && player.Skills.Any(s => s?.def?.active?.id == skill.id);
        }
        static string Key(Player p, ActiveSkillData a) => p.GetInstanceID() + ":" + a.id;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { running.Clear(); recasts.Clear(); }
        public static bool TryRecast(Player p, ActiveSkillData skill)
        {
            if (skill == null || !recasts.TryGetValue(Key(p, skill), out var ctx) || !ctx.Valid || !ctx.awaitingRecast) return false;
            ctx.recast = true; return true;
        }
        public static bool CanCast(Player p, ActiveSkillData skill)
        {
            if (p.Inventory?.Active == null || skill == null || !p.SkillUnlocked(skill.slot) || (running.TryGetValue(p, out int count) && count >= C.maxConcurrentCasts)) return false;
            if (ActiveSkillGenerator.TypeOf(p.Inventory.Active) != skill.weaponType) return false;
            if (skill.linkedMask == 0) return true;
            var attached = p.Inventory.Attached(p.Inventory.Active).Where(i => i.kind == ItemKind.Skill).ToList();
            return Enumerable.Range(0, 4).Where(slot => (skill.linkedMask & (1 << slot)) != 0)
                .All(slot => p.SkillUnlocked(slot) && attached.Any(i => i.skillSlot == slot && i.active?.linkId == skill.linkId && i.active.linkedMask == skill.linkedMask));
        }
        public static void Cast(Player p, ActiveSkillData skill, Vector2 dir, Vector2 aim)
        {
            if (!CanCast(p, skill)) return;
            ActiveSkillStyle.Ensure(skill);
            var ctx = new CastContext { player = p, skill = skill, weaponId = p.Inventory.Active.id, attack = p.AttackPower,
                power = ActiveSkillGenerator.Magnitude(skill), deadline = Time.time + C.maxCastDuration,
                control = p.Abilities.ActiveControl, range = p.Abilities.ActiveRange };
            p.StartCoroutine(Run(ctx, dir, aim));
        }
        static IEnumerator Run(CastContext ctx, Vector2 dir, Vector2 aim)
        {
            var p = ctx.player; var a = ctx.skill; var act = p.SkillAct(p.Skills[a.slot]);
            string key = Key(p, a);
            running[p] = running.TryGetValue(p, out int count) ? count + 1 : 1;
            if (a.input == "recast") recasts[key] = ctx;
            try
            {
                if (a.input == "charge")
                {
                    float start = Time.time; Play(ctx, a.steps[0], 0, p.Pos, dir, C.projectilePixels);
                    while (ctx.Valid && Controls.Held(act) && Time.time - start < C.chargeDuration)
                    { p.HoldSkillAction(); yield return null; }
                    if (!ctx.Valid) yield break;
                    ctx.power *= 1 + C.chargeBonus * Mathf.Clamp01((Time.time - start) / Mathf.Max(.01f, C.chargeDuration));
                    aim = p.AimPoint; dir = (aim - p.Pos).SafeNormal(dir);
                }
                ctx.parts = p.Abilities.BeginActive(a, aim);
                ctx.range += Mathf.Min(DB.Asset.abilityRules.def.partTraits.rangeCap, ctx.parts.range);
                var evolution = p.Abilities.OnActiveCast(a, aim);
                ctx.power *= 1 + evolution.damage;
                ctx.range = Mathf.Min(1 + DB.Asset.abilityRules.def.maxRangeBonus, ctx.range + evolution.range);
                ctx.control = Mathf.Min(1 + DB.Asset.abilityRules.def.maxControlBonus, ctx.control + evolution.control);
                ctx.evolutionHeal = evolution.heal;
                p.Skills[a.slot].cd *= 1 - evolution.cooldown;
                int cycles = a.input == "channel" ? C.maxPulses : 1;
                for (int cycle = 0; cycle < cycles && ctx.Valid; cycle++)
                {
                    if (cycle > 0 && !Controls.Held(act)) break;
                    Stage previous = null;
                    foreach (var step in a.steps)
                    {
                        if (!ctx.Valid) yield break;
                        if (step.trigger == "recast")
                        {
                            ctx.awaitingRecast = true; float until = Time.time + C.recastWindow;
                            while (ctx.Valid && !ctx.recast && Time.time < until) yield return null;
                            ctx.awaitingRecast = false;
                            if (!ctx.recast || !ctx.Valid) yield break;
                            aim = p.AimPoint; dir = (aim - p.Pos).SafeNormal(dir);
                        }
                        else if (previous != null && step.trigger != "after")
                        {
                            while (ctx.Valid && !previous.finished && !(step.trigger == "hit" && previous.hit)) yield return null;
                            if (!ctx.Valid || step.trigger == "hit" && !previous.hit || step.trigger == "arrive" && !previous.arrived) yield break;
                        }
                        if (step.timing == "delayed") yield return new WaitForSeconds(C.delay);
                        if (!ctx.Valid) yield break;
                        float range = ActiveSkillGenerator.Range(a, step) * ctx.range;
                        Vector2 origin = step.origin switch {
                            "aim" => p.Pos + Vector2.ClampMagnitude(aim - p.Pos, range),
                            "previous" => previous?.position ?? p.Pos,
                            "impact" => previous?.impact ?? p.Pos,
                            _ => p.Pos };
                        // A movement recast can return to the exact start of the earlier movement.
                        Vector2? returnTo = ActiveSkillGenerator.Moving(step) && step.origin == "previous" && step.trigger == "recast" ? previous?.start : null;
                        var stage = new Stage { start = p.Pos, position = origin };
                        yield return Execute(ctx, step, stage, origin, dir, range, cycles, returnTo);
                        previous = stage;
                    }
                    if (cycle + 1 < cycles)
                    {
                        float until = Time.time + C.channelDuration / Mathf.Max(1, cycles - 1);
                        while (ctx.Valid && Controls.Held(act) && Time.time < until) { p.HoldSkillAction(); yield return null; }
                        aim = p.AimPoint; dir = (aim - p.Pos).SafeNormal(dir);
                    }
                }
                while (ctx.Valid && ctx.live > 0) yield return null;
            }
            finally
            {
                p.Abilities.EndActive(ctx.parts, ctx.Valid);
                ctx.closed = true; recasts.Remove(key);
                if (running.TryGetValue(p, out int remaining)) { if (remaining <= 1) running.Remove(p); else running[p] = remaining - 1; }
            }
        }
        static IEnumerator Execute(CastContext ctx, ActiveStep step, Stage stage, Vector2 origin, Vector2 dir, float range, int cycles, Vector2? returnTo)
        {
            var p = ctx.player;
            int pulses = ctx.skill.input == "channel" ? 1 : step.delivery == "field" || step.timing == "repeat" ? C.maxPulses : 1;
            if (step.delivery == "trap")
            {
                float until = Time.time + C.effectDuration, fx = 0;
                while (ctx.Valid && Time.time < until && !Actor.InRadius(origin, Mathf.Min(C.radius, range), Team.Enemy).Any(t => Eligible(ctx, step, t)))
                {
                    if (Time.time >= fx) { Play(ctx, step, 0, origin, dir, Mathf.RoundToInt(Mathf.Min(C.radius, range) * 2 * Art.PPU), C.pulseInterval); fx = Time.time + C.pulseInterval; }
                    yield return null;
                }
                if (!ctx.Valid) yield break;
                if (Time.time >= until) { stage.finished = true; yield break; }
            }
            if (step.delivery == "wall")
            {
                Vector2 side = new Vector2(-dir.y, dir.x) * Mathf.Min(C.radius, range);
                p.PresentSkill(ctx.skill, step, dir);
                Vector2 placement=step.origin=="caster"?origin+dir*(p.radius+C.anchorWidth):origin;
                stage.position=placement;
                Play(ctx, step, 0, placement, dir, C.effectPixels);
                var barrier = ActiveBarrier.Spawn(p, ctx.skill, placement - side, placement + side, () => ctx.Valid);
                try { while (ctx.Valid && barrier != null && barrier.Active) yield return null; }
                finally { if (barrier != null) barrier.Dismiss(); }
                stage.finished = true; yield break;
            }
            for (int pulse = 0; pulse < pulses && ctx.Valid; pulse++)
            {
                p.PresentSkill(ctx.skill, step, dir);
                // A projected slash still starts with a real swing at the hand. Its target-area visual and hit origin stay intact.
                if (step.delivery == "arc" && Vector2.Distance(origin, p.Pos) > C.weaponSwingPixels / (2f * Art.PPU))
                    Play(ctx, step, 0, p.Pos, dir, C.weaponSwingPixels, followCaster: true);
                if (ActiveSkillGenerator.Moving(step))
                {
                    Vector2 from = p.Pos;
                    Vector2 delta = returnTo.HasValue ? Vector2.ClampMagnitude(returnTo.Value - from, C.movementDistance * ctx.range) :
                        (step.delivery == "retreat" ? -dir : dir) * Mathf.Min(C.movementDistance * ctx.range, range);
                    int trailPixels = Mathf.RoundToInt(C.movementTrailSpacing * Art.PPU);
                    Play(ctx, step, 2, from, delta, trailPixels);
                    var ghost = (Color)Pal.Hex(ctx.skill.color); ghost.a = .45f; p.SkillAfterimage(ghost);
                    if (step.delivery == "blink") p.Pos = WorldStreamer.I.Move(from, delta, p.radius, bound: p.Bound);
                    else
                    {
                        float elapsed = 0; Vector2 trailAt = from;
                        while (elapsed < C.movementDuration && ctx.Valid)
                        {
                            float dt = Mathf.Min(Time.deltaTime, C.movementDuration - elapsed); elapsed += dt;
                            p.HoldSkillAction(); p.Pos = WorldStreamer.I.Move(p.Pos, delta / Mathf.Max(.01f, C.movementDuration) * dt, p.radius, bound: p.Bound);
                            if (Vector2.Distance(trailAt, p.Pos) >= C.movementTrailSpacing)
                            { Play(ctx, step, 2, p.Pos, delta, trailPixels); p.SkillAfterimage(ghost); trailAt = p.Pos; }
                            yield return null;
                        }
                    }
                    if (!ctx.Valid) yield break;
                    stage.position = p.Pos; stage.arrived = true;
                    ApplyTargets(ctx, step, stage, new List<Actor>(Combat.Line(from, p.Pos, C.projectileRadius, Team.Enemy)), dir, cycles);
                    Play(ctx, step, 0, p.Pos, delta, C.effectPixels);
                }
                else if (step.effect is "shield" or "heal")
                {
                    if (step.effect == "shield") p.AddShield(ctx.attack * ctx.power / C.shieldScale / (pulses * cycles), C.effectDuration);
                    else p.Heal(ctx.attack * ctx.power / C.healScale / (pulses * cycles));
                    if (step.delivery == "guard") p.invulnUntil = Mathf.Max(p.invulnUntil, Time.time + C.guardInvulnerability);
                    Play(ctx, step, 0, p.Pos, dir, C.effectPixels);
                }
                else if (step.delivery == "projectile") Fire(ctx, step, stage, origin, dir, range, pulses * cycles);
                else
                {
                    Vector2 at = origin;
                    IEnumerable<Actor> targets = step.delivery switch {
                        "arc" => new List<Actor>(Combat.Arc(at, dir, range, 140, Team.Enemy)),
                        "line" => new List<Actor>(Combat.Line(at, at + dir * range, C.projectileRadius, Team.Enemy)),
                        _ => new List<Actor>(Actor.InRadius(at, Mathf.Min(C.radius * ctx.range, range), Team.Enemy)) };
                    var selected = targets.Take(C.maxTargets).ToList();
                    if (step.pattern == "chain" && selected.Count > 0)
                        selected.AddRange(Actor.InRadius(selected[0].Pos, C.radius, Team.Enemy).Where(x => !selected.Contains(x)).Take(C.maxTargets - selected.Count));
                    float width = step.delivery == "line" ? range : step.delivery == "arc" ? range * 2 : Mathf.Min(C.radius * ctx.range, range) * 2;
                    Play(ctx, step, 0, step.delivery == "line" ? at + dir * range * .5f : at, dir, Mathf.RoundToInt(width * Art.PPU));
                    ApplyTargets(ctx, step, stage, selected, dir, pulses * cycles); stage.position = at;
                }
                if (pulse + 1 < pulses) yield return new WaitForSeconds(step.delivery == "field" ? C.effectDuration / Mathf.Max(1, pulses - 1) : C.pulseInterval);
            }
            if (stage.live == 0) stage.finished = true;
        }
        static void Play(CastContext ctx, ActiveStep step, int cell, Vector2 at, Vector2 dir, int size, float duration = 0, bool followCaster = false)
        {
            if (cell == 1) size = C.impactPixels;
            var frames = ActiveSkillArt.Effect(ctx.skill, step, cell, size);
            float scale = Mathf.Max(8, size) / Mathf.Max(1f, frames[0].rect.width - 6);
            bool ground = cell == 0 && step.delivery is "field" or "trap" or "nova" or "wall";
            bool attached = followCaster || cell == 0 && step.origin == "caster" && step.delivery is "arc" or "guard";
            Vector2 position = at + (ground ? Vector2.zero : Vector2.up * C.actionHeight);
            if (cell == 0 && step.delivery == "arc") position = Combat.MeleeFxOrigin(position, dir);
            var visuals=CombatVisuals.Library;
            float seconds = duration > 0 ? duration : cell == 1 ? C.impactVisualDuration : visuals!=null?visuals.skillMainSeconds:C.castVisualDuration;
            Color? tint=null; // Generated sheets and authored motion already carry this skill's palette.
            bool upright=ground || step.delivery=="guard" || cell==1 && (step.effect is "heal" or "shield");
            Fx.I?.Play(frames, position, upright ? 0 : dir.Angle(), frames.Length / Mathf.Max(.01f, seconds), scale, tint, false,
                Art.SortY(at.y) + (ground ? -1 : 5), attached ? ctx.player.transform : null);
            if(cell==0)CombatVisuals.Accent(ctx.skill,step,position,dir,size,Pal.Hex(ctx.skill.color),Art.SortY(at.y)+(ground?-1:5),attached?ctx.player.transform:null);
        }
        static string Status(string condition) => condition switch { "marked" => "mark", "burning" => "burn", "chilled" => "chill", "poisoned" => "poison", _ => null };
        static string Mark(ActiveSkillData a) => a.linkedMask == 0 ? "mark" : "active:" + a.linkId;
        static bool Eligible(CastContext ctx, ActiveStep step, Actor target)
        {
            string status = step.condition == "marked" ? Mark(ctx.skill) : Status(step.condition);
            return target != null && target.Alive && (ctx.affected.Contains(target) || ctx.affected.Count < C.maxTargets) && (status == null || target.HasStatus(status));
        }
        static DamageInfo Damage(CastContext ctx, ActiveStep step, Vector2 dir, int divisions)
        {
            var frames=ActiveSkillArt.Effect(ctx.skill,step,1,C.effectPixels);
            return new DamageInfo {
                amount = ctx.attack * ctx.power * (step.condition == "always" ? 1 : 1 + C.conditionalBonus) / Mathf.Max(1, divisions),
                source = ctx.player, direction = dir, knockback = 0, canCrit = true,
                element = System.Enum.TryParse(step.element, out Element el) ? el : Element.None,
                tags = "SKILL " + (step.delivery == "projectile" ? "PROJECTILE RANGED" : "MELEE"),
                partAttack = ctx.parts, impactFx=frames,normalImpactBlend=true,hitstop=.02f,
                impactScale=frames?.Length>0?(CombatVisuals.Library!=null?CombatVisuals.Library.skillImpactPixels:C.impactPixels)/Mathf.Max(1f,frames[0].rect.width-6):1,
                impactSeconds=C.impactVisualDuration,impactUnrotated=step.element!="None" };
        }
        static void ApplyTargets(CastContext ctx, ActiveStep step, Stage stage, IEnumerable<Actor> targets, Vector2 dir, int divisions, float travelled = 0)
        {
            foreach (var target in targets.Take(C.maxTargets).ToList())
            {
                if (!ctx.Valid || !Eligible(ctx, step, target)) continue;
                if (step.effect == "damage")
                {
                    var hit = Damage(ctx, step, dir, divisions); hit.travelled = travelled;
                    if (Combat.Hit(target, hit, Pal.Hex(ctx.skill.color)) <= 0) continue;
                    CombatVisuals.SkillImpact(ctx.skill,step,target.Pos+Vector2.up*C.actionHeight,dir);
                }
                if (!stage.hit) { stage.hit = true; stage.impact = target.Pos; }
                ctx.affected.Add(target);
                if (!ctx.healed && step.effect == "damage") { ctx.healed = true; ctx.player.Abilities.OnActiveHit(ctx.evolutionHeal); }
                if (step.effect == "mark") target.AddStatus(Mark(ctx.skill), C.statusDuration * ctx.control, 0, 0, ctx.player, Pal.Hex(ctx.skill.color));
                if (step.effect == "stun" && target is Enemy enemy) enemy.Stun(C.stunDuration * ctx.control);
                if (step.effect == "pull") target.Knockback(stage.position - target.Pos, C.knockback * ctx.player.Abilities.PartPullMultiplier(ctx.parts));
                if (step.effect == "push") target.Knockback(target.Pos - stage.position, C.knockback);
                ctx.player.Abilities.PartControl(ctx.parts, step, target);
                if (step.effect == "damage" && step.element != "None")
                {
                    string status = step.element switch { "Fire" => "burn", "Frost" => "chill", "Poison" => "poison", _ => null };
                    if (status != null) target.AddStatus(status, C.statusDuration * ctx.control, 0, step.element == "Frost" ? .2f : 0, ctx.player, Pal.Hex(ctx.skill.color));
                }
                if (step.consume)
                {
                    string status = step.condition == "marked" ? Mark(ctx.skill) : Status(step.condition);
                    if (status != null) target.statuses.RemoveAll(s => s.id == status);
                }
                // Damage already plays this skill's dedicated impact through Combat.Hit.
                if (step.effect != "damage") Play(ctx, step, 1, target.Pos, dir, C.effectPixels);
            }
        }
        static void Fire(CastContext ctx, ActiveStep step, Stage stage, Vector2 origin, Vector2 dir, float range, int divisions)
        {
            int count = step.pattern is "fan" or "ring" or "converge" ? C.maxProjectiles : 1;
            count = Mathf.Min(count, C.maxProjectiles * C.maxPulses - ctx.shots); if (count <= 0) return;
            int muzzlePixels=CombatVisuals.Library!=null?Mathf.RoundToInt(CombatVisuals.Library.skillMuzzlePixels):C.projectilePixels;
            Vector2 muzzle=step.origin=="caster"?origin+dir*(ctx.player.radius+C.projectileRadius):origin;
            if(step.pattern!="converge")Play(ctx,step,3,muzzle,dir,muzzlePixels);
            for (int i = 0; i < count; i++)
            {
                float angle = dir.Angle() + (step.pattern is "ring" or "converge" ? i * 360f / count : step.pattern == "fan" ? (i - (count - 1) * .5f) * 15 : 0);
                Vector2 shotDir = MathX.Dir(angle);
                Vector2 castOrigin=origin+Vector2.up*C.actionHeight;
                float muzzleOffset=step.origin=="caster"?ctx.player.radius+C.projectileRadius:0;
                Vector2 start = step.pattern == "converge" ? castOrigin + shotDir * range : castOrigin+shotDir*muzzleOffset;
                if (step.pattern == "converge") shotDir = -shotDir;
                if(step.pattern=="converge")Play(ctx,step,3,start-Vector2.up*C.actionHeight,shotDir,muzzlePixels);
                var projectile = Projectile.Spawn(ActiveSkillArt.Shot(ctx.skill, step), start, shotDir * C.projectileSpeed, Team.Enemy, ctx.player, Damage(ctx, step, shotDir, divisions * count), false);
                ActiveProjectileArt.Apply(projectile,ctx.skill,step);
                ctx.shots++; ctx.live++; stage.live++;
                projectile.life = range / C.projectileSpeed * (step.pattern == "return" ? 2 : 1);
                projectile.radius = C.projectileRadius; projectile.pierce = step.pattern is "pierce" or "return" ? C.maxTargets : 0;
                projectile.bounces = step.pattern == "bounce" ? 1 : 0;
                ctx.player.Abilities.PreparePartProjectile(ctx.parts, projectile, ctx.shots == 1);
                projectile.color = Pal.Hex(ctx.skill.color);
                // Dissipating impact flecks form the trail; don't leave copies of the flying body behind.
                var library=CombatVisuals.Library;
                projectile.trailFx = ActiveSkillPalette.Frames(ctx.skill,library?.skillTrail?.Length>0?library.skillTrail:library?.trail);
                projectile.normalTrailBlend = true;
                if(library!=null && projectile.trailFx?.Length>0)
                {
                    projectile.trailScale=library.skillTrailPixels/Mathf.Max(1f,projectile.trailFx[0].rect.width-6);
                    projectile.trailInterval=library.skillTrailInterval;projectile.trailStartDistance=library.skillTrailStartDistance;
                    projectile.trailTint=Color.white;
                }
                projectile.canHit = (_, target) => ctx.Valid && Eligible(ctx, step, target);
                projectile.resolveHit = (pr, target) => ApplyTargets(ctx, step, stage, new[] { target }, pr.vel.normalized, divisions * count * (step.pattern == "return" ? 2 : 1), pr.Travelled);
                projectile.onEnd = pr =>
                {
                    ctx.live--; stage.live--;
                    if (pr.Expired) { stage.arrived = true; stage.position = pr.transform.position; }
                    if (stage.live == 0) stage.finished = true;
                };
                bool returning = false;
                projectile.onUpdate = (pr, age) =>
                {
                    if (!ctx.Valid) { pr.Block(); return; }
                    if (step.pattern == "curve") pr.vel = MathX.Dir(pr.vel.Angle() + C.curveStrength * Time.deltaTime) * C.projectileSpeed;
                    if (step.pattern == "seek")
                    {
                        var target = Actor.Nearest(pr.transform.position, range, Team.Enemy);
                        if (target != null) pr.vel = Vector2.Lerp(pr.vel, (target.Pos - (Vector2)pr.transform.position).normalized * C.projectileSpeed, Time.deltaTime * 5);
                    }
                    if (step.pattern == "return" && age > range / C.projectileSpeed)
                    {
                        pr.homing = 0;
                        if (!returning) { returning = true; pr.ResetHits(); }
                        pr.vel = (ctx.player.Pos - (Vector2)pr.transform.position).normalized * C.projectileSpeed;
                        if (Vector2.Distance(ctx.player.Pos, pr.transform.position) < C.projectileRadius) pr.Block();
                    }
                    ctx.player.Abilities.SteerPartProjectile(ctx.parts, pr, age);
                };
            }
        }
    }
}
