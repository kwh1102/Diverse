using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Bounded per-cast graphs, persistent traps and optional links between manual casts.</summary>
    public static partial class ActiveSkillRuntime
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
            public float attack, power, deadline, control, range, evolutionHeal, techniqueTempo = 1;
            public bool healed, closed, awaitingRecast, recast, counted;
            public Vector2 recastAim;
            public int live, shots;
            public PartAttackState parts;
            public Dictionary<ActiveStep, ActiveGroundAnchor> channelFields;
            public readonly HashSet<Actor> affected = new HashSet<Actor>();
            public bool Valid => !closed && Time.time < deadline && player != null && player.Alive && player.Inventory?.Active?.id == weaponId && player.Skills.Any(s => s?.def?.active?.id == skill.id);
        }
        static string Key(Player p, ActiveSkillData a) => p.GetInstanceID() + ":" + a.id;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { running.Clear(); recasts.Clear(); ResetTechniques(); }
        public static bool TryRecast(Player p, ActiveSkillData skill)
        {
            if (skill == null || !recasts.TryGetValue(Key(p, skill), out var ctx) || !ctx.Valid || !ctx.awaitingRecast) return false;
            ctx.recastAim = p.AimPoint;
            ctx.recast = true; return true;
        }
        public static bool CanCast(Player p, ActiveSkillData skill)
        {
            if (p.Inventory?.Active == null || skill == null || !p.SkillUnlocked(skill.slot) || (running.TryGetValue(p, out int count) && count >= C.maxConcurrentCasts)) return false;
            if (ActiveSkillGenerator.TypeOf(p.Inventory.Active) != skill.weaponType) return false;
            if (skill.input == "recast" && recasts.ContainsKey(Key(p, skill))) return false;
            return true;
        }
        public static void Cast(Player p, ActiveSkillData skill, Vector2 dir, Vector2 aim)
        {
            if (!CanCast(p, skill)) return;
            ActiveSkillStyle.Ensure(skill);
            var ctx = new CastContext { player = p, skill = skill, weaponId = p.Inventory.Active.id, attack = p.AttackPower,
                power = ActiveSkillGenerator.Magnitude(skill), deadline = Time.time + C.maxCastDuration,
                control = p.Abilities.ActiveControl, range = p.Abilities.ActiveRange };
            p.StartCoroutine(RunImmediate(Run(ctx, dir, aim)));
        }
        // Start each nested step in the current update. Only a real timer or a frame yield suspends the cast.
        static IEnumerator RunImmediate(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            try
            {
                while (stack.Count > 0)
                {
                    var current = stack.Peek();
                    if (!current.MoveNext()) { (stack.Pop() as System.IDisposable)?.Dispose(); continue; }
                    if (current.Current is IEnumerator child && current.Current is not CustomYieldInstruction) stack.Push(child);
                    else yield return current.Current;
                }
            }
            finally { while (stack.Count > 0) (stack.Pop() as System.IDisposable)?.Dispose(); }
        }
        static void ReleaseCast(CastContext ctx)
        {
            if (!ctx.counted) return;
            ctx.counted = false;
            if (!running.TryGetValue(ctx.player, out int remaining)) return;
            if (remaining <= 1) running.Remove(ctx.player);
            else running[ctx.player] = remaining - 1;
        }
        static IEnumerator Run(CastContext ctx, Vector2 dir, Vector2 aim)
        {
            var p = ctx.player; var a = ctx.skill; var act = p.SkillAct(p.Skills[a.slot]);
            string key = Key(p, a);
            running[p] = running.TryGetValue(p, out int count) ? count + 1 : 1;
            ctx.counted = true;
            if (a.input == "recast") recasts[key] = ctx;
            try
            {
                if (a.input == "charge")
                {
                    float start = Time.time;
                    CombatVisuals.Charge(p, Pal.Hex(a.color));
                    Sfx.Play("charge", .55f);
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
                if (a.linkAction == "relay" && a.HasTechnique)
                {
                    float reach = ActiveTechniques.Get(a.technique).Tuning.reach * ctx.range;
                    if (ActiveSkillSynergy.TryRelay(p, a, reach, aim, out var anchor))
                    { aim = anchor; dir = (anchor - p.Pos).SafeNormal(dir); }
                }
                else if (ActiveSkillSynergy.TryConsume(p, a, out Vector2 linkAt))
                {
                    if (a.HasTechnique) { ctx.techniqueTempo = C.techniqueLinkScale; ctx.range *= ctx.techniqueTempo; }
                    else ResolveLink(ctx, linkAt, dir);
                }
                if (a.HasTechnique)
                {
                    yield return ExecuteTechnique(ctx, dir, aim);
                    while (ctx.Valid && ctx.live > 0) yield return null;
                    yield break;
                }
                if (a.slot == 3) CombatVisuals.UltimateRelease(p, Pal.Hex(a.color));
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
                            aim = ctx.recastAim; dir = (aim - p.Pos).SafeNormal(dir);
                        }
                        else if (previous != null && step.trigger != "after")
                        {
                            while (ctx.Valid && !previous.finished && !(step.trigger == "hit" && previous.hit)) yield return null;
                            if (!ctx.Valid || step.trigger == "hit" && !previous.hit || step.trigger == "arrive" && !previous.arrived) yield break;
                        }
                        // Older designs could put an untelegraphed timer before the first tap action.
                        if (step.timing == "delayed" && (previous != null || a.input != "tap"))
                        {
                            if (previous == null) { CombatVisuals.Charge(p, Pal.Hex(a.color)); Sfx.Play("charge", .45f); }
                            yield return new WaitForSeconds(C.delay);
                        }
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
                        // A placed trap owns its activation. Old follow-ups resolve in its ground burst.
                        if (step.delivery == "trap") break;
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
                if (ctx.channelFields != null)
                    foreach (var field in ctx.channelFields.Values) if (field != null) field.Dismiss();
                p.Abilities.EndActive(ctx.parts, ctx.Valid);
                ctx.closed = true; recasts.Remove(key);
                ReleaseCast(ctx);
            }
        }
        static void ResolveLink(CastContext ctx, Vector2 at, Vector2 dir)
        {
            Vector2 from = ctx.player.Pos;
            bool echo = ctx.skill.linkAction == "echo", draw = ctx.skill.linkAction == "draw";
            var color = Pal.Hex(ctx.skill.color);
            CombatVisuals.Link(from, at, C.linkRadius, color, ctx.skill.linkAction, ctx.skill.slot == 3);
            Sfx.Play(draw ? "skill_control" : "skill_cast", .65f, .85f);
            IEnumerable<Actor> targets = echo
                ? new List<Actor>(Combat.Line(from, at, C.projectileRadius, Team.Enemy))
                : Actor.InRadius(at, C.linkRadius, Team.Enemy);
            foreach (var enemy in targets.Take(C.maxTargets).ToArray())
            {
                if (draw)
                {
                    enemy.Knockback(at - enemy.Pos, C.knockback * ctx.control);
                    continue;
                }
                Combat.Hit(enemy, new DamageInfo {
                    source = ctx.player, direction = (enemy.Pos - at).SafeNormal(dir), amount = ctx.attack * C.linkDamage,
                    tags = "SKILL LINK", hitstop = .018f, canCrit = true
                }, color, "hit");
            }
        }
        static IEnumerator Execute(CastContext ctx, ActiveStep step, Stage stage, Vector2 origin, Vector2 dir, float range, int cycles, Vector2? returnTo)
        {
            var p = ctx.player;
            float radius = Mathf.Min(C.radius * ctx.range, range);
            if (step.delivery == "trap")
            {
                Vector2 placement = step.origin == "caster" ? origin + dir * (p.radius + C.anchorWidth * .8f) : origin;
                stage.position = placement;
                float windup = p.PresentPlacement(placement, dir);
                if (windup > 0) yield return new WaitForSeconds(windup);
                if (!ctx.Valid) yield break;
                radius = Mathf.Min(C.trapRadius * ctx.range, range);
                float placedAt = Time.time, until = placedAt + C.trapDuration;
                ctx.deadline = Mathf.Max(ctx.deadline, until + C.maxCastDuration);
                var trap = ActiveGroundAnchor.PlaceTrap(p, placement, radius, Pal.Hex(ctx.skill.color), () => ctx.Valid);
                ActiveSkillSynergy.Record(p, ctx.skill, ctx.weaponId, "placement", placement);
                // An armed object consumes the trap limit, not a concurrent cast slot.
                ReleaseCast(ctx);
                Sfx.Play("trap_place", .7f);
                try
                {
                    while (ctx.Valid && trap != null && trap.gameObject.activeSelf && Time.time < until)
                    {
                        var target = Time.time >= placedAt + C.trapArmTime ?
                            Actor.InRadius(trap.Position, trap.Radius, Team.Enemy).FirstOrDefault(t => Eligible(ctx, step, t)) : null;
                        if (target != null)
                        {
                            trap.Dismiss();
                            Sfx.Play("trap_trigger", .75f);
                            var trapCue = new ActiveSkillData { technique = Technique.ThresholdTrap, color = ctx.skill.color };
                            TechniqueFx.Show(trapCue, placement, dir, radius * 2);
                            int legacyFollowups = ctx.skill.steps.SkipWhile(s => s != step).Skip(1).Count(s => s.effect == "damage" && s.trigger is "hit" or "after");
                            ApplyTargets(ctx, step, stage, new[] { target }, dir, cycles, damageScale: 1 + legacyFollowups * C.utilityStepCost, localOnly: true);
                            if (stage.hit && target is Enemy enemy && target.Alive && step.effect == "damage")
                                enemy.Stun(C.trapRootTime * ctx.control);
                            break;
                        }
                        yield return null;
                    }
                }
                finally { if (trap != null && trap.gameObject.activeSelf) trap.Dismiss(); }
                stage.finished = true;
                yield break;
            }
            if (step.delivery == "wall")
            {
                Vector2 side = new Vector2(-dir.y, dir.x) * Mathf.Min(C.radius, range);
                p.PresentSkill(ctx.skill, step, dir);
                Vector2 placement=step.origin=="caster"?origin+dir*(p.radius+C.anchorWidth):origin;
                stage.position=placement;
                var barrier = ActiveBarrier.Spawn(p, ctx.skill, placement - side, placement + side, () => ctx.Valid);
                ActiveSkillSynergy.Record(p, ctx.skill, ctx.weaponId, "placement", placement);
                Play(ctx, step, 0, placement, dir, C.effectPixels);
                Sfx.Play("wall_place", .7f);
                try { while (ctx.Valid && barrier != null && barrier.Active) yield return null; }
                finally { if (barrier != null) barrier.Dismiss(); }
                stage.finished = true; yield break;
            }
            ActiveGroundAnchor field = null;
            if (step.delivery == "field" && step.effect is not ("heal" or "shield"))
            {
                ActiveSkillSynergy.Record(p, ctx.skill, ctx.weaponId, "placement", origin);
                if (ctx.skill.input == "channel")
                {
                    ctx.channelFields ??= new Dictionary<ActiveStep, ActiveGroundAnchor>();
                    if (ctx.channelFields.TryGetValue(step, out var anchor)) anchor.MoveTo(origin);
                    else ctx.channelFields[step] = ActiveGroundAnchor.Spawn(origin, radius, false, Pal.Hex(ctx.skill.color), () => ctx.Valid);
                }
                else field = ActiveGroundAnchor.Spawn(origin, radius, false, Pal.Hex(ctx.skill.color), () => ctx.Valid);
            }
            try { yield return Pulses(ctx, step, stage, origin, dir, range, cycles, returnTo, radius); }
            finally { if (field != null) field.Dismiss(); }
            if (stage.live == 0) stage.finished = true;
        }

        static IEnumerator Pulses(CastContext ctx, ActiveStep step, Stage stage, Vector2 origin, Vector2 dir, float range,
            int cycles, Vector2? returnTo, float radius)
        {
            var p = ctx.player;
            int pulses = ctx.skill.input == "channel" ? 1 : step.delivery == "field" || step.timing == "repeat" ? C.maxPulses : 1;
            for (int pulse = 0; pulse < pulses && ctx.Valid; pulse++)
            {
                if (pulse == 0 || step.delivery != "field") p.PresentSkill(ctx.skill, step, dir);
                if (ActiveSkillGenerator.Moving(step))
                {
                    Sfx.Play(step.delivery == "blink" ? "skill_cast" : "dash", .65f);
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
                    ActiveSkillSynergy.Record(p, ctx.skill, ctx.weaponId, "arrival", p.Pos);
                    ApplyTargets(ctx, step, stage, new List<Actor>(Combat.Line(from, p.Pos, C.projectileRadius, Team.Enemy)), delta.SafeNormal(dir), cycles);
                    Play(ctx, step, 0, p.Pos, delta, C.effectPixels);
                }
                else if (step.effect is "shield" or "heal")
                {
                    Sfx.Play(step.effect == "shield" ? "guard" : "skill_heal", pulse == 0 ? .65f : .38f);
                    if (step.effect == "shield") p.AddShield(ctx.attack * ctx.power / C.shieldScale / (pulses * cycles), C.effectDuration);
                    else p.Heal(ctx.attack * ctx.power / C.healScale / (pulses * cycles));
                    if (step.delivery == "guard") p.invulnUntil = Mathf.Max(p.invulnUntil, Time.time + C.guardInvulnerability);
                    Play(ctx, step, 0, p.Pos, dir, C.effectPixels);
                }
                else if (step.delivery == "projectile")
                {
                    if (ctx.Valid) Fire(ctx, step, stage, origin, dir, range, pulses * cycles);
                }
                else
                {
                    PlayActionSound(ctx.skill, step, pulse);
                    Vector2 at = step.delivery == "line" ? p.ProjectileOrigin(dir) :
                        step.delivery == "arc" ? p.Pos + Vector2.up * C.actionHeight : origin;
                    Vector2 end = step.delivery == "line" ? ClampAtWall(at, at + dir * range) : at;
                    IEnumerable<Actor> targets = step.delivery switch {
                        "arc" => new List<Actor>(Combat.Arc(at, dir, range, 140, Team.Enemy)),
                        "line" => new List<Actor>(Combat.Line(at, end, C.projectileRadius, Team.Enemy)),
                        _ => new List<Actor>(Actor.InRadius(at, radius, Team.Enemy)) };
                    var selected = targets.Take(C.maxTargets).ToList();
                    if (step.pattern == "chain" && selected.Count > 0)
                    {
                        var first = selected.OrderBy(t => (t.Pos - at).sqrMagnitude).First();
                        var branches = Actor.InRadius(first.Pos, C.radius, Team.Enemy)
                            .Where(t => !selected.Contains(t) && Eligible(ctx, step, t)).Take(C.maxTargets - selected.Count).ToArray();
                        foreach (var branch in branches)
                            CombatVisuals.Trace(first.Pos, branch.Pos, Pal.Hex(ctx.skill.color));
                        selected.AddRange(branches);
                    }
                    float width = step.delivery == "line" ? range : step.delivery == "arc" ? range * 2 : radius * 2;
                    if (step.delivery == "line") CombatVisuals.DirectStroke(at, end, Pal.Hex(ctx.skill.color));
                    else Play(ctx, step, 0, at, dir, Mathf.RoundToInt(width * Art.PPU));
                    ApplyTargets(ctx, step, stage, selected, dir, pulses * cycles); stage.position = at;
                }
                if (pulse + 1 < pulses) yield return new WaitForSeconds(step.delivery == "field" ? C.effectDuration / Mathf.Max(1, pulses - 1) : C.pulseInterval);
            }
        }
        static void PlayActionSound(ActiveSkillData skill, ActiveStep step, int pulse)
        {
            string cue = step.delivery is "arc" or "line" ? "swing" : "skill_cast";
            Sfx.Play(cue, pulse == 0 ? .6f : .38f, skill.slot == 3 ? .86f : 1f);
        }
        static void Play(CastContext ctx, ActiveStep step, int cell, Vector2 at, Vector2 dir, int size)
        {
            var visuals = CombatVisuals.Library;
            if (cell == 1) size = Mathf.RoundToInt(visuals?.skillImpactPixels ?? C.impactPixels);
            var frames = ActiveSkillArt.Effect(ctx.skill, step, cell, size);
            if (frames == null || frames.Length == 0) return;
            float scale = Mathf.Max(8, size) / Mathf.Max(1f, frames[0].rect.width - 6);
            bool self = step.effect is "heal" or "shield";
            bool ground = cell == 0 && !self && step.delivery is ("field" or "trap" or "nova" or "wall");
            bool attached = cell == 0 && (self || step.delivery == "arc");
            Vector2 position = at + (ground || cell == 3 ? Vector2.zero : Vector2.up * C.actionHeight);
            if (cell == 0 && step.delivery == "arc")
                position = ctx.player.SwingOrigin(dir);
            if (cell == 0 && visuals != null)
            {
                if (ground && step.delivery is ("field" or "trap")) scale *= visuals.skillGroundPulseScale;
                else if (step.delivery != "line")
                    scale *= ctx.skill.slot == 3 ? visuals.skillUltimateScale : visuals.skillActionScale;
            }
            float seconds = cell == 1 ? C.impactVisualDuration : cell == 3 && visuals != null ? visuals.muzzleSeconds :
                visuals != null ? visuals.skillMainSeconds : C.castVisualDuration;
            int order = Art.SortY(at.y) + (ground ? -1 : attached && step.delivery == "arc" ? -3 : 5);
            Transform follow = attached ? ctx.player.transform : null;
            var animation = Fx.I?.Play(frames, position, ground || self ? 0 : dir.Angle(), frames.Length / Mathf.Max(.01f, seconds),
                scale, null, false, order, follow);
            if (animation != null && cell == 0 && step.delivery == "line" && visuals != null)
                CombatVisuals.Thicken(animation, ctx.skill.slot == 3 ? visuals.skillUltimateScale : visuals.skillActionScale);
            if (cell == 0) CombatVisuals.Accent(ctx.skill, step, position, dir, size, order, follow);
        }
        static string Status(string condition) => condition switch { "marked" => "mark", "burning" => "burn", "chilled" => "chill", "poisoned" => "poison", _ => null };
        static bool Eligible(CastContext ctx, ActiveStep step, Actor target)
        {
            string status = Status(step.condition);
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
                impactSeconds=C.impactVisualDuration,impactUnrotated=step.element!="None",
                impactCutAngle=step.delivery=="arc" && ctx.skill.weaponType==WeaponType.Sword?52:0, impactDirection=dir,
                hitVisual=step.delivery is "arc" or "line" or "projectile" ?
                    CombatVisuals.HitFor(ctx.skill.weaponType, step.delivery == "projectile", step.delivery == "line") : HitVisual.Generic };
        }
        static void ApplyTargets(CastContext ctx, ActiveStep step, Stage stage, IEnumerable<Actor> targets, Vector2 dir, int divisions, float travelled = 0, float damageScale = 1, bool localOnly = false)
        {
            foreach (var target in targets.Take(C.maxTargets).ToList())
            {
                if (!ctx.Valid || !Eligible(ctx, step, target)) continue;
                if (step.effect == "damage")
                {
                    var hit = Damage(ctx, step, dir, divisions); hit.travelled = travelled;
                    hit.amount *= damageScale; hit.suppressImpactFx = localOnly;
                    if (Combat.Hit(target, hit, Pal.Hex(ctx.skill.color)) <= 0) continue;
                }
                if (!stage.hit)
                {
                    stage.hit = true; stage.impact = target.Pos;
                    ActiveSkillSynergy.Record(ctx.player, ctx.skill, ctx.weaponId, "impact", target.Pos);
                    if (ctx.skill.SeekingRupture) SeekingRupture(ctx, target, dir);
                }
                ctx.affected.Add(target);
                if (!ctx.healed && step.effect == "damage") { ctx.healed = true; ctx.player.Abilities.OnActiveHit(ctx.evolutionHeal); }
                if (step.effect == "mark") target.AddStatus("mark", C.statusDuration * ctx.control, 0, 0, ctx.player, Pal.Hex(ctx.skill.color));
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
                    string status = Status(step.condition);
                    if (status != null) target.statuses.RemoveAll(s => s.id == status);
                }
                // Damage already plays this skill's dedicated impact through Combat.Hit.
                if (step.effect != "damage" && !localOnly)
                {
                    Play(ctx, step, 1, target.Pos, dir, C.effectPixels);
                    Sfx.Play("skill_control", .48f);
                }
            }
        }
        static void SeekingRupture(CastContext ctx, Actor contact, Vector2 dir)
        {
            Vector2 point = contact.Pos;
            Color32 color = Pal.Hex(ctx.skill.color);
            CombatVisuals.ImpactPulse(point, C.seekingBurstRadius, ctx.skill, ctx.skill.steps[0].element);
            Sfx.Play("skill_cast", .5f, 1.12f);
            foreach (var target in Actor.InRadius(point, C.seekingBurstRadius, Team.Enemy)
                         .Where(t => t != contact && t.Alive).Take(C.maxTargets - 1).ToArray())
                Combat.Hit(target, new DamageInfo { source = ctx.player, amount = ctx.attack * C.seekingBurstDamage,
                    direction = (target.Pos - point).SafeNormal(dir), tags = "SKILL PROJECTILE", noEvents = true }, color);
        }

        static void RecordArrival(CastContext ctx, Stage stage, Vector2 point)
        {
            if (stage.arrived) return;
            stage.arrived = true;
            stage.position = point;
            ActiveSkillSynergy.Record(ctx.player, ctx.skill, ctx.weaponId, "arrival", point);
        }
        static void Fire(CastContext ctx, ActiveStep step, Stage stage, Vector2 origin, Vector2 dir, float range, int divisions)
        {
            int count = step.pattern is "fan" or "ring" or "converge" ? C.maxProjectiles : 1;
            count = Mathf.Min(count, C.maxProjectiles * C.maxPulses - ctx.shots); if (count <= 0) return;
            string sound = ctx.skill.weaponType switch { WeaponType.Bow => "bow_release", WeaponType.Gun => "gun_shot", _ => "skill_cast" };
            Sfx.Play(sound, .68f);
            int muzzlePixels=CombatVisuals.Library!=null?Mathf.RoundToInt(CombatVisuals.Library.skillMuzzlePixels):C.projectilePixels;
            bool fromWeapon = step.origin is ("caster" or "aim") && step.pattern != "converge";
            Vector2 castOrigin = fromWeapon ? ctx.player.ProjectileOrigin(dir) : origin + Vector2.up * C.actionHeight;
            if (fromWeapon && step.origin == "aim" && Vector2.Dot(origin - castOrigin, dir) > C.projectileRadius)
            {
                dir = (origin - castOrigin).SafeNormal(dir);
                castOrigin = ctx.player.ProjectileOrigin(dir);
            }
            if(step.pattern!="converge")Play(ctx,step,3,castOrigin,dir,muzzlePixels);
            for (int i = 0; i < count; i++)
            {
                float angle = dir.Angle() + (step.pattern is "ring" or "converge" ? i * 360f / count : step.pattern == "fan" ? (i - (count - 1) * .5f) * 15 : 0);
                Vector2 shotDir = MathX.Dir(angle);
                Vector2 start = step.pattern == "converge" ? castOrigin + shotDir * range : castOrigin;
                if (step.pattern == "converge") shotDir = -shotDir;
                if(step.pattern=="converge")Play(ctx,step,3,start,shotDir,muzzlePixels);
                var projectile = Projectile.Spawn(ActiveSkillArt.Shot(ctx.skill, step), start, shotDir * C.projectileSpeed, Team.Enemy, ctx.player, Damage(ctx, step, shotDir, divisions * count), false);
                ActiveProjectileArt.Apply(projectile,ctx.skill,step);
                ctx.shots++; ctx.live++; stage.live++;
                projectile.life = range / C.projectileSpeed * (step.pattern == "return" ? 2 : 1);
                projectile.radius = C.projectileRadius; projectile.pierce = step.pattern is "pierce" or "return" ? C.maxTargets : 0;
                projectile.bounces = step.pattern == "bounce" ? 1 : 0;
                ctx.player.Abilities.PreparePartProjectile(ctx.parts, projectile, ctx.shots == 1);
                projectile.color = Pal.Hex(ctx.skill.color);
                CombatVisuals.Wake(projectile, ctx.skill.weaponType, step.pattern, Pal.Hex(ctx.skill.color));
                projectile.canHit = (_, target) => ctx.Valid && Eligible(ctx, step, target);
                projectile.resolveHit = (pr, target) => ApplyTargets(ctx, step, stage, new[] { target }, pr.vel.normalized, divisions * count * (step.pattern == "return" ? 2 : 1), pr.Travelled);
                projectile.onEnd = pr =>
                {
                    ctx.live--; stage.live--;
                    if (pr.Expired && ctx.Valid)
                        RecordArrival(ctx, stage, (Vector2)pr.transform.position - Vector2.up * C.actionHeight);
                    if (stage.live == 0) stage.finished = true;
                };
                if (step.pattern == "bounce") projectile.onBounce = pr =>
                {
                    CombatVisuals.ProjectileTurn(pr.transform.position, pr.vel.normalized, pr.color);
                    Sfx.Play("hit_metal", .46f, 1.35f);
                };
                bool returning = false;
                projectile.onUpdate = (pr, age) =>
                {
                    if (!ctx.Valid) { pr.Block(); return; }
                    if (step.pattern == "curve") pr.vel = MathX.Dir(pr.vel.Angle() + C.curveStrength * Time.deltaTime) * C.projectileSpeed;
                    if (step.pattern == "seek")
                    {
                        var target = Actor.Nearest(pr.transform.position, range, Team.Enemy);
                        if (target != null) pr.vel = Vector2.Lerp(pr.vel, (Projectile.TargetPoint(target) - (Vector2)pr.transform.position).normalized * C.projectileSpeed, Time.deltaTime * 5);
                    }
                    if (step.pattern == "return" && age > range / C.projectileSpeed)
                    {
                        pr.homing = 0;
                        if (!returning)
                        {
                            returning = true; pr.ResetHits();
                            CombatVisuals.ProjectileTurn(pr.transform.position, -pr.vel.normalized, pr.color);
                        }
                        var catchPoint = Projectile.TargetPoint(ctx.player);
                        pr.vel = (catchPoint - (Vector2)pr.transform.position).normalized * C.projectileSpeed;
                        if (Vector2.Distance(catchPoint, pr.transform.position) < C.projectileRadius)
                        {
                            RecordArrival(ctx, stage, ctx.player.Pos);
                            pr.Block(false);
                        }
                    }
                    ctx.player.Abilities.SteerPartProjectile(ctx.parts, pr, age);
                };
            }
        }
    }
}
