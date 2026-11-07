using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Diverse
{
    public static partial class ActiveSkillRuntime
    {
        sealed class ShotGate
        {
            public CastContext cast;
            public Vector2 start, end, exit, direction;
            public float radius;
            public bool used;
        }

        sealed class GunMemory
        {
            public readonly List<(Vector2 point, float expires, Fx.HeldAnimation marker)> casings = new();
            public float loadedUntil, capturedPower;
        }

        static readonly List<ShotGate> shotGates = new();
        static readonly Dictionary<Player, CastContext> clashes = new();
        static ConditionalWeakTable<InventoryItem, GunMemory> gunMemory = new();

        static void ResetTechniques() { shotGates.Clear(); clashes.Clear(); gunMemory = new(); }

        public static void RecordCasing(Player player, Vector2 point)
        {
            var weapon = player.Inventory?.Active;
            if (weapon?.weapon.type != WeaponType.Gun || !player.Skills.Any(s => s.def?.active?.technique == Technique.IgniteCasings)) return;
            var skill = player.Skills.First(s => s.def?.active?.technique == Technique.IgniteCasings).def.active;
            var memory = gunMemory.GetOrCreateValue(weapon);
            memory.casings.RemoveAll(c => c.expires <= Time.time);
            if (memory.casings.Count >= C.casingLimit)
            {
                memory.casings[0].marker?.Dispose();
                memory.casings.RemoveAt(0);
            }
            memory.casings.Add((point, Time.time + C.casingLifetime,
                TechniqueFx.Hold(skill, point, Vector2.right, .5f, C.casingLifetime)));
        }

        public static float UseCapturedRound(Player player)
        {
            if (player.Inventory?.Active is not { } weapon || !gunMemory.TryGetValue(weapon, out var memory) ||
                memory.loadedUntil <= Time.time) return 0;
            memory.loadedUntil = 0;
            return memory.capturedPower;
        }

        public static bool InterruptCharge(Player player, Enemy enemy)
        {
            if (!clashes.TryGetValue(player, out var ctx) || !ctx.Valid || !enemy.Charging) return false;
            var direction = (enemy.Pos - player.Pos).SafeNormal(player.Facing);
            if (Vector2.Dot(direction, player.Facing) <= 0) return false;
            clashes.Remove(player);
            enemy.Stun(C.techniqueControlSeconds * ctx.control);
            var contact = (enemy.Pos + player.Pos) * .5f;
            TechniqueFx.Show(ctx.skill, contact + Vector2.up * C.actionHeight, direction, ActiveTechniques.Get(ctx.skill.technique).Tuning.radius * 2);
            Deal(ctx, enemy, direction, 1);
            enemy.Knockback(direction, C.knockback * ctx.control);
            player.Knockback(-direction, C.knockback);
            return true;
        }

        public static bool InterceptShot(Projectile shot, Vector2 from, Vector2 to)
        {
            foreach (var gate in shotGates)
            {
                if (gate.used || !gate.cast.Valid) continue;
                var ctx = gate.cast;
                bool portal = ctx.skill.technique == Technique.TwinGates;
                if (!portal && shot.targetTeam != Team.Player) continue;
                if (!NearSegments(from, to, gate.start, gate.end, gate.radius + shot.radius)) continue;
                gate.used = true;
                var contact = gate.start == gate.end ? gate.start : ClosestOnSegment(to, gate.start, gate.end);
                TechniqueFx.Show(ctx.skill, contact, gate.direction, gate.radius * 2);
                ActiveSkillSynergy.Record(ctx.player, ctx.skill, ctx.weaponId, "impact", contact);
                if (portal)
                {
                    shot.RedirectFrom(gate.exit, gate.direction);
                    TechniqueFx.Show(ctx.skill, gate.exit, gate.direction, gate.radius * 2);
                }
                else
                {
                    shot.Block(false);
                    if (ctx.skill.technique == Technique.BladeReflect)
                        LaunchTechniqueRound(ctx, contact, gate.direction, ActiveTechniques.Get(ctx.skill.technique).Tuning.reach + C.range);
                    else if (ctx.player.Inventory.Active is { } weapon)
                    {
                        var memory = gunMemory.GetOrCreateValue(weapon);
                        memory.loadedUntil = Time.time + C.casingLifetime;
                        float cap = C.captureBonusCap * ctx.attack;
                        memory.capturedPower = Mathf.Clamp(shot.dmg.amount, cap * .5f, cap);
                    }
                }
                Sfx.Play(portal ? "skill_cast" : "hit_metal", .75f);
                return true;
            }
            return false;
        }

        static IEnumerator ExecuteTechnique(CastContext ctx, Vector2 dir, Vector2 aim)
        {
            var p = ctx.player;
            var a = ctx.skill;
            var rule = ActiveTechniques.Get(a.technique);
            var tuning = rule.Tuning;
            float reach = tuning.reach * ctx.range;
            float radius = tuning.radius * ctx.range;
            float seconds = tuning.seconds * ctx.techniqueTempo;
            float until = Time.time + seconds;
            var at = AimGround(p, aim, reach);
            var height = Vector2.up * C.actionHeight;
            ctx.deadline = Mathf.Max(ctx.deadline, until + C.techniqueRecastSeconds + C.maxCastDuration);
            if (a.technique != Technique.ThresholdTrap)
            {
                p.PresentSkill(a, dir, a.technique is Technique.WallPin or Technique.OpenGuard or Technique.BackLock);
                string cue = a.technique switch {
                    Technique.BladeReflect or Technique.Clash or Technique.CaptureRound => "guard",
                    Technique.Tripwire or Technique.LureArrow or Technique.RecallArrow => "bow_release",
                    Technique.WallShatter => "gun_shot",
                    Technique.WallPin or Technique.OpenGuard or Technique.BackLock => "swing",
                    Technique.BreakPursuit => "dash",
                    _ => "skill_cast"
                };
                Sfx.Play(cue, .6f);
            }

            switch (a.technique)
            {
                case Technique.BladeReflect:
                case Technique.CaptureRound:
                {
                    var muzzle = p.ProjectileOrigin(dir);
                    var end = a.technique == Technique.BladeReflect ? muzzle : muzzle + dir * reach;
                    using var marker = TechniqueFx.Hold(a, muzzle, dir, radius * 2);
                    if (end != muzzle) TechniqueFx.Trace(a, muzzle, end);
                    yield return WatchShot(ctx, new ShotGate { cast = ctx, start = muzzle, end = end, direction = dir, radius = radius }, until);
                    break;
                }
                case Technique.WallPin:
                {
                    var target = EnemyAhead(p.Pos, dir, reach);
                    if (target == null) break;
                    Vector2 from = target.Pos;
                    var pushed = WorldStreamer.I.Move(from, dir * reach, target.radius);
                    target.Pos = pushed;
                    TechniqueFx.Trace(a, from + height, pushed + height);
                    if (FindWall(from, dir, reach + target.radius, out var wall, out _))
                    {
                        target.Stun(C.techniqueControlSeconds * ctx.control);
                        TechniqueFx.Show(a, wall + height, dir, radius * 2);
                        Deal(ctx, target, dir, 1);
                    }
                    break;
                }
                case Technique.Clash:
                {
                    clashes[p] = ctx;
                    using var marker = TechniqueFx.Hold(a, p.SwingOrigin(dir), dir, radius * 2);
                    try
                    {
                        while (ctx.Valid && Time.time < until && clashes.TryGetValue(p, out var active) && active == ctx)
                        {
                            var enemy = Actor.InRadius(p.Pos, reach, Team.Enemy).OfType<Enemy>()
                                .FirstOrDefault(e => e.Charging && Vector2.Dot(e.Facing, (p.Pos - e.Pos).normalized) > 0 &&
                                    Vector2.Dot(dir, (e.Pos - p.Pos).normalized) > 0);
                            if (enemy != null && InterruptCharge(p, enemy)) break;
                            yield return null;
                        }
                    }
                    finally { if (clashes.TryGetValue(p, out var active) && active == ctx) clashes.Remove(p); }
                    break;
                }
                case Technique.OpenGuard:
                {
                    var enemy = EnemyAhead(p.Pos, dir, reach, e => e.Preparing && Vector2.Dot(e.Facing, (p.Pos - e.Pos).normalized) > 0);
                    if (enemy == null) break;
                    var flank = new Vector2(-enemy.Facing.y, enemy.Facing.x);
                    if (Vector2.Dot(flank, dir) < 0) flank = -flank;
                    enemy.OpenSide(flank, seconds, ctx.power);
                    TechniqueFx.Show(a, enemy.Pos + height, flank, radius * 2);
                    ActiveSkillSynergy.Record(p, a, ctx.weaponId, "impact", enemy.Pos);
                    break;
                }
                case Technique.Tripwire:
                {
                    var first = PlacePoint(ctx, at, dir);
                    using var stake = TechniqueFx.Hold(a, first, dir, radius * 3);
                    yield return Reaim(ctx);
                    if (!ctx.recast || !ctx.Valid) break;
                    var second = PlacePoint(ctx, AimGround(p, ctx.recastAim, reach), dir);
                    if ((second - first).sqrMagnitude < radius * radius || !WorldStreamer.I.LineClear(first, second)) break;
                    using var endStake = TechniqueFx.Hold(a, second, dir, radius * 3);
                    using var line = TechniqueFx.HoldPath(a, first, second);
                    yield return WatchCrossing(ctx, first, second, radius, seconds, enemy =>
                    {
                        var contact = ClosestOnSegment(enemy.Pos, first, second);
                        enemy.Stun(C.techniqueControlSeconds * ctx.control);
                        TechniqueFx.Show(a, contact, dir, radius * 5);
                        Deal(ctx, enemy, dir, 1);
                    });
                    break;
                }
                case Technique.BindPair:
                {
                    var first = EnemyAt(at, radius);
                    if (first == null) break;
                    using var marked = TechniqueFx.Hold(a, first.Pos + height, dir, radius);
                    yield return Reaim(ctx);
                    if (!ctx.recast || !ctx.Valid || first == null || !first.Alive) break;
                    var second = EnemyAt(AimGround(p, ctx.recastAim, reach), radius, e => e != first);
                    if (second == null) break;
                    float slack = Vector2.Distance(first.Pos, second.Pos) + radius;
                    until = Time.time + seconds;
                    using var line = TechniqueFx.HoldPath(a, first.Pos + height, second.Pos + height);
                    while (ctx.Valid && Time.time < until && first != null && first.Alive && second != null && second.Alive)
                    {
                        TechniqueFx.SetPath(line, first.Pos + height, second.Pos + height);
                        if (Vector2.Distance(first.Pos, second.Pos) >= slack)
                        {
                            var middle = (first.Pos + second.Pos) * .5f;
                            first.Knockback(middle - first.Pos, C.knockback * ctx.control);
                            second.Knockback(middle - second.Pos, C.knockback * ctx.control);
                            TechniqueFx.Show(a, middle + height, second.Pos - first.Pos, radius);
                            Deal(ctx, first, middle - first.Pos, .5f);
                            Deal(ctx, second, middle - second.Pos, .5f);
                            break;
                        }
                        yield return null;
                    }
                    break;
                }
                case Technique.LureArrow:
                {
                    at = PlacePoint(ctx, at, dir);
                    TechniqueFx.Show(a, at, dir, radius);
                    using var marker = TechniqueFx.Hold(a, at, dir, radius);
                    foreach (var enemy in Actor.InRadius(at, radius, Team.Enemy).OfType<Enemy>().Take(C.maxTargets))
                        enemy.Taunt(at, seconds);
                    while (ctx.Valid && Time.time < until) yield return null;
                    break;
                }
                case Technique.RecallArrow:
                {
                    at = PlacePoint(ctx, at, dir);
                    using var marker = TechniqueFx.Hold(a, at, dir, radius * 4);
                    yield return Reaim(ctx);
                    if (!ctx.recast || !ctx.Valid) break;
                    var from = at + height; var to = p.ProjectileOrigin((at - p.Pos).SafeNormal(dir));
                    var reached = ClampAtWall(from, to);
                    TechniqueFx.Trace(a, from, reached);
                    foreach (var enemy in Combat.Line(from, reached, radius, Team.Enemy).Take(C.maxTargets).ToArray())
                        Deal(ctx, enemy, (reached - from).SafeNormal(dir), 1);
                    ActiveSkillSynergy.Record(p, a, ctx.weaponId, "arrival", reached - height);
                    break;
                }
                case Technique.WallShatter:
                {
                    var muzzle = p.ProjectileOrigin(dir);
                    bool hit = FindWall(muzzle, dir, reach, out var wall, out var normal);
                    TechniqueFx.Trace(a, muzzle, hit ? wall : muzzle + dir * reach);
                    if (!hit) break;
                    var side = new Vector2(-normal.y, normal.x);
                    TechniqueFx.Show(a, wall, side, radius * 2);
                    TechniqueFx.Trace(a, wall - side * radius, wall + side * radius);
                    foreach (var enemy in Combat.Line(wall - side * radius, wall + side * radius, tuning.radius * .4f, Team.Enemy).Take(C.maxTargets).ToArray())
                        if (Vector2.Dot(enemy.Pos + height - wall, normal) >= -enemy.radius) Deal(ctx, enemy, normal, 1);
                    break;
                }
                case Technique.IgniteCasings:
                {
                    if (!gunMemory.TryGetValue(p.Inventory.Active, out var memory)) break;
                    memory.casings.RemoveAll(c => c.expires <= Time.time);
                    var ignited = memory.casings.Where(c => (c.point - at).sqrMagnitude <= radius * radius).ToArray();
                    var targets = new HashSet<Actor>();
                    foreach (var casing in ignited)
                    {
                        memory.casings.Remove(casing);
                        casing.marker?.Dispose();
                        TechniqueFx.Show(a, casing.point, dir, tuning.seconds * 4);
                        foreach (var enemy in Actor.InRadius(casing.point, tuning.seconds * 2, Team.Enemy).Take(C.maxTargets).ToArray())
                            if (targets.Add(enemy)) Deal(ctx, enemy, (enemy.Pos - casing.point).SafeNormal(dir), 1);
                    }
                    break;
                }
                case Technique.Overwatch:
                {
                    Vector2 stand = p.Pos;
                    Vector2 from = p.ProjectileOrigin(dir), end = ClampAtWall(from, from + dir * reach);
                    using var line = TechniqueFx.HoldPath(a, from, end);
                    yield return WatchCrossing(ctx, from - height, end - height, radius, seconds, enemy =>
                    {
                        LaunchTechniqueRound(ctx, from, (Projectile.TargetPoint(enemy) - from).SafeNormal(dir), reach);
                    }, () => (p.Pos - stand).sqrMagnitude < radius * radius);
                    break;
                }
                case Technique.ResonanceAnchor:
                {
                    at = PlacePoint(ctx, at, dir, showTrace: false);
                    using var marker = TechniqueFx.Hold(a, at, dir, radius);
                    // The player's opening cast is emitted before the subscription sees the next key.
                    float armedAt = Time.time;
                    bool triggered = false;
                    void OnCast(CombatEvent e) { if (e.type == Trig.SkillCast && e.source == p && e.skillSlot != a.slot && Time.time >= armedAt) triggered = true; }
                    GameEvents.Combat += OnCast;
                    try
                    {
                        while (ctx.Valid && Time.time < until && !triggered) yield return null;
                        if (ctx.Valid && triggered)
                        {
                            TechniqueFx.Show(a, at, dir, radius * 2);
                            foreach (var enemy in Actor.InRadius(at, radius, Team.Enemy).Take(C.maxTargets).ToArray())
                                Deal(ctx, enemy, (enemy.Pos - at).SafeNormal(dir), 1);
                        }
                    }
                    finally { GameEvents.Combat -= OnCast; }
                    break;
                }
                case Technique.RewindSeal:
                {
                    var enemy = EnemyAt(at, radius);
                    if (enemy == null || enemy.def.boss) break;
                    at = enemy.Pos;
                    ActiveSkillSynergy.Record(p, a, ctx.weaponId, "placement", at);
                    using var marker = TechniqueFx.Hold(a, at, dir, radius * 2);
                    yield return Reaim(ctx);
                    if (!ctx.recast || !ctx.Valid || enemy == null || !enemy.Alive) break;
                    var from = enemy.Pos;
                    enemy.Pos = WorldStreamer.I.Move(from, Vector2.ClampMagnitude(at - from, reach), enemy.radius);
                    TechniqueFx.Trace(a, from + height, enemy.Pos + height);
                    TechniqueFx.Show(a, enemy.Pos, dir, radius * 2);
                    ActiveSkillSynergy.Record(p, a, ctx.weaponId, "impact", enemy.Pos);
                    break;
                }
                case Technique.TwinGates:
                {
                    var entry = PlacePoint(ctx, at, dir, showTrace: false) + height;
                    using var first = TechniqueFx.Hold(a, entry, dir, radius * 2);
                    yield return Reaim(ctx);
                    if (!ctx.recast || !ctx.Valid) break;
                    var exit = AimGround(p, ctx.recastAim, reach) + height;
                    if ((exit - entry).sqrMagnitude < radius * radius * 4) break;
                    var direction = (exit - entry).SafeNormal(dir);
                    using var second = TechniqueFx.Hold(a, exit, direction, radius * 2);
                    yield return WatchShot(ctx, new ShotGate { cast = ctx, start = entry, end = entry, exit = exit, direction = direction, radius = radius }, Time.time + seconds);
                    break;
                }
                case Technique.TideSwitch:
                {
                    at = PlacePoint(ctx, at, dir, showTrace: false);
                    using var marker = TechniqueFx.Hold(a, at, dir, radius * 2);
                    Tide(ctx, at, dir, radius, false, true);
                    float nextPulse = Time.time + C.pulseInterval;
                    ctx.awaitingRecast = true;
                    try
                    {
                        while (ctx.Valid && !ctx.recast && Time.time < until)
                        {
                            if (Time.time >= nextPulse)
                            {
                                Tide(ctx, at, dir, radius, false, false);
                                nextPulse += C.pulseInterval;
                            }
                            yield return null;
                        }
                    }
                    finally { ctx.awaitingRecast = false; }
                    if (ctx.recast && ctx.Valid) Tide(ctx, at, dir, radius, true, true);
                    break;
                }
                case Technique.ShadowSwap:
                {
                    at = PlacePoint(ctx, p.Pos, dir, showTrace: false);
                    using var marker = TechniqueFx.Hold(a, at, dir, radius);
                    yield return Reaim(ctx);
                    if (!ctx.recast || !ctx.Valid || Vector2.Distance(p.Pos, at) > reach ||
                        !WorldStreamer.I.Walkable(at) || !WorldStreamer.I.LineClear(p.Pos, at)) break;
                    var before = p.Pos;
                    p.SkillAfterimage(Pal.A(Pal.Hex(a.color), 130));
                    p.StopMoving(); p.Pos = at;
                    TechniqueFx.Show(a, before + height, dir, radius);
                    TechniqueFx.Show(a, at + height, -dir, radius);
                    ActiveSkillSynergy.Record(p, a, ctx.weaponId, "arrival", at);
                    break;
                }
                case Technique.ThresholdTrap:
                    yield return DirectionalTrap(ctx, at, dir, radius, seconds);
                    break;
                case Technique.BreakPursuit:
                {
                    var before = p.Pos;
                    p.SkillAfterimage(Pal.Hex(a.color));
                    p.StopMoving();
                    p.Pos = WorldStreamer.I.Move(before, -dir * reach, p.radius, bound: p.Bound);
                    PlacePoint(ctx, before, dir, showTrace: false);
                    TechniqueFx.Trace(a, before + height, p.Pos + height);
                    using var decoy = TechniqueFx.Hold(a, before, dir, radius * .5f);
                    foreach (var enemy in Actor.InRadius(before, radius, Team.Enemy).OfType<Enemy>().Where(e => e.Aggro).Take(C.maxTargets))
                        enemy.Taunt(before, seconds);
                    ActiveSkillSynergy.Record(p, a, ctx.weaponId, "arrival", p.Pos);
                    while (ctx.Valid && Time.time < until) yield return null;
                    break;
                }
                case Technique.BackLock:
                {
                    var enemy = EnemyAhead(p.Pos, dir, reach, e => e.Recovering && Vector2.Dot(e.Facing, (p.Pos - e.Pos).normalized) < -C.openSideDot);
                    if (enemy == null) break;
                    enemy.LockFacing(seconds);
                    TechniqueFx.Show(a, enemy.Pos + height, -enemy.Facing, radius * 2);
                    Deal(ctx, enemy, dir, 1);
                    break;
                }
            }
        }

        static IEnumerator Reaim(CastContext ctx)
        {
            ctx.awaitingRecast = true;
            float until = Time.time + C.techniqueRecastSeconds * ctx.techniqueTempo;
            try { while (ctx.Valid && !ctx.recast && Time.time < until) yield return null; }
            finally { ctx.awaitingRecast = false; }
        }

        static IEnumerator WatchShot(CastContext ctx, ShotGate gate, float until)
        {
            shotGates.Add(gate);
            try { while (ctx.Valid && !gate.used && Time.time < until) yield return null; }
            finally { shotGates.Remove(gate); }
        }

        static IEnumerator WatchCrossing(CastContext ctx, Vector2 from, Vector2 to, float width, float seconds,
            Action<Enemy> trigger, Func<bool> stay = null)
        {
            var previous = new Dictionary<Enemy, Vector2>();
            float until = Time.time + seconds;
            while (ctx.Valid && Time.time < until && (stay == null || stay()))
            {
                foreach (var actor in Actor.All)
                {
                    if (actor is not Enemy enemy || !enemy.Alive) continue;
                    Vector2 point = enemy.Pos;
                    if (previous.TryGetValue(enemy, out var old) && (point - old).sqrMagnitude > Mathf.Epsilon &&
                        Combat.DistToSegment(old, from, to) > width + enemy.radius &&
                        NearSegments(old, point, from, to, width + enemy.radius))
                    { trigger(enemy); yield break; }
                    previous[enemy] = point;
                }
                yield return null;
            }
        }

        static IEnumerator DirectionalTrap(CastContext ctx, Vector2 at, Vector2 dir, float radius, float seconds)
        {
            var p = ctx.player; var a = ctx.skill;
            float windup = p.PresentPlacement(at, dir);
            if (windup > 0) yield return new WaitForSeconds(windup);
            if (!ctx.Valid) yield break;
            var trap = ActiveGroundAnchor.PlaceTrap(p, at, radius, Pal.Hex(a.color), () => ctx.Valid);
            ActiveSkillSynergy.Record(p, a, ctx.weaponId, "placement", at);
            Sfx.Play("trap_place", .7f);
            ReleaseCast(ctx);
            using var marker = TechniqueFx.Hold(a, at, dir, radius * 2);
            var previous = new Dictionary<Enemy, Vector2>();
            float arm = Time.time + C.trapArmTime, until = Time.time + seconds;
            try
            {
                while (ctx.Valid && trap != null && trap.gameObject.activeSelf && Time.time < until)
                {
                    foreach (var actor in Actor.All)
                    {
                        if (actor is not Enemy enemy || !enemy.Alive) continue;
                        var point = enemy.Pos;
                        if (previous.TryGetValue(enemy, out var old) && Time.time >= arm &&
                            (old - at).sqrMagnitude > radius * radius && Combat.DistToSegment(at, old, point) <= radius &&
                            Vector2.Dot(old - at, dir) > 0 && Vector2.Dot(point - old, dir) < 0)
                        {
                            marker?.Dispose(); trap.Dismiss();
                            TechniqueFx.Show(a, at, dir, radius * 2);
                            Sfx.Play("trap_trigger", .75f);
                            Deal(ctx, enemy, dir, 1);
                            enemy.Stun(C.trapRootTime * ctx.control);
                            yield break;
                        }
                        previous[enemy] = point;
                    }
                    yield return null;
                }
            }
            finally { if (trap != null && trap.gameObject.activeSelf) trap.Dismiss(); }
        }

        static void Tide(CastContext ctx, Vector2 at, Vector2 dir, float radius, bool reverse, bool hit)
        {
            TechniqueFx.Show(ctx.skill, at, reverse ? -dir : dir, radius * 2);
            foreach (var enemy in Actor.InRadius(at, radius, Team.Enemy).Take(C.maxTargets).ToArray())
            {
                Vector2 force = reverse ? enemy.Pos - at : at - enemy.Pos;
                enemy.Knockback(force, C.knockback * ctx.control);
                if (hit) Deal(ctx, enemy, force.SafeNormal(dir), .5f);
            }
        }

        static void LaunchTechniqueRound(CastContext ctx, Vector2 at, Vector2 dir, float reach)
        {
            var a = ctx.skill;
            var flight = TechniqueFx.Flight(a);
            var projectile = Projectile.Spawn(flight[0], at, dir * C.projectileSpeed, Team.Enemy, ctx.player, default);
            projectile.SetFlightVisual(flight, 1, C.effectFramesPerSecond, Color.white);
            projectile.radius = C.projectileRadius;
            projectile.life = reach / C.projectileSpeed;
            ctx.live++;
            projectile.canHit = (_, _) => ctx.Valid;
            projectile.onEnd = _ => ctx.live--;
            projectile.resolveHit = (shot, target) =>
            {
                TechniqueFx.Show(a, Projectile.TargetPoint(target), shot.vel.normalized, ActiveTechniques.Get(a.technique).Tuning.radius * 2);
                Deal(ctx, target, shot.vel.normalized, 1);
                if (a.technique == Technique.Overwatch && target is Enemy enemy) enemy.Stun(C.techniqueControlSeconds * ctx.control);
            };
            CombatVisuals.Wake(projectile, a.weaponType, "single", Pal.Hex(a.color));
        }

        static void Deal(CastContext ctx, Actor target, Vector2 direction, float share)
        {
            var a = ctx.skill;
            if (Combat.Hit(target, new DamageInfo {
                source = ctx.player, amount = ctx.attack * ctx.power * share, direction = direction,
                tags = ActiveTechniques.UsesShot(a.technique) ? "SKILL PROJECTILE RANGED" : "SKILL",
                canCrit = true, hitstop = .025f, partAttack = ctx.parts, suppressImpactFx = true
            }, Pal.Hex(a.color)) <= 0) return;
            ActiveSkillSynergy.Record(ctx.player, a, ctx.weaponId, "impact", target.Pos);
            if (!ctx.healed) { ctx.healed = true; ctx.player.Abilities.OnActiveHit(ctx.evolutionHeal); }
        }

        static Vector2 AimGround(Player player, Vector2 aim, float reach)
        {
            var point = player.Pos + Vector2.ClampMagnitude(aim - player.Pos, reach);
            var walkable = WorldStreamer.I.NearestWalkable(point, C.projectileRadius, C.anchorWidth);
            return Vector2.Distance(walkable, player.Pos) <= reach && WorldStreamer.I.Walkable(walkable) ? walkable : player.Pos;
        }

        static Vector2 PlacePoint(CastContext ctx, Vector2 point, Vector2 direction, bool showTrace = true)
        {
            if (showTrace) TechniqueFx.Trace(ctx.skill, ctx.player.ProjectileOrigin(direction), point);
            ActiveSkillSynergy.Record(ctx.player, ctx.skill, ctx.weaponId, "placement", point);
            return point;
        }

        static Enemy EnemyAt(Vector2 at, float radius, Func<Enemy, bool> filter = null) =>
            Actor.InRadius(at, radius, Team.Enemy).OfType<Enemy>().Where(e => filter == null || filter(e))
                .OrderBy(e => (e.Pos - at).sqrMagnitude).FirstOrDefault();

        static Enemy EnemyAhead(Vector2 at, Vector2 dir, float reach, Func<Enemy, bool> filter = null) =>
            EnemyAt(at, reach, e => Vector2.Dot((e.Pos - at).normalized, dir) > .35f &&
                WorldStreamer.I.LineClear(at, e.Pos) && (filter == null || filter(e)));

        static Vector2 ClampAtWall(Vector2 from, Vector2 to) =>
            FindWall(from, (to - from).normalized, Vector2.Distance(from, to), out var wall, out _) ? wall : to;

        static bool FindWall(Vector2 from, Vector2 dir, float range, out Vector2 hit, out Vector2 normal)
        {
            hit = from; normal = -dir;
            float sample = 1f / Art.PPU;
            var world = WorldStreamer.I;
            for (float d = sample; d <= range; d += sample)
            {
                var next = from + dir * d;
                if (world.IsSolidObstacle(next))
                {
                    if (world.IsSolidObstacle(new Vector2(next.x, hit.y))) normal = new Vector2(-Mathf.Sign(dir.x), 0);
                    else if (world.IsSolidObstacle(new Vector2(hit.x, next.y))) normal = new Vector2(0, -Mathf.Sign(dir.y));
                    return true;
                }
                hit = next;
            }
            return false;
        }

        static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return d.sqrMagnitude < Mathf.Epsilon ? a : a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
        }

        static bool NearSegments(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float width)
        {
            static float Cross(Vector2 x, Vector2 y) => x.x * y.y - x.y * y.x;
            var ab = b - a; var cd = d - c;
            float determinant = Cross(ab, cd);
            if (Mathf.Abs(determinant) > Mathf.Epsilon)
            {
                float t = Cross(c - a, cd) / determinant, u = Cross(c - a, ab) / determinant;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return true;
            }
            return Combat.DistToSegment(a, c, d) <= width || Combat.DistToSegment(b, c, d) <= width ||
                Combat.DistToSegment(c, a, b) <= width || Combat.DistToSegment(d, a, b) <= width;
        }
    }
}
