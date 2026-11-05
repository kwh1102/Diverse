using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public static class ActiveSkillRuntime
    {
        static readonly Dictionary<Player, int> running = new Dictionary<Player, int>();
        static ActiveRules C => ActiveSkillGenerator.Rules;
        class CastContext
        {
            public Player player;
            public ActiveSkillData skill;
            public string weaponId;
            public float attack, power;
            public bool healed;
            public readonly HashSet<Actor> affected = new HashSet<Actor>();
            public bool Valid => player != null && player.Alive && player.Inventory?.Active?.id == weaponId;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => running.Clear();
        public static bool CanCast(Player p, ActiveSkillData skill)
        {
            if (p.Inventory?.Active == null || skill == null || (running.TryGetValue(p, out int n) && n >= C.maxConcurrentCasts)) return false;
            if (ActiveSkillGenerator.TypeOf(p.Inventory.Active) != skill.weaponType) return false;
            if (skill.linkedMask == 0) return true;
            var attached = p.Inventory.Attached(p.Inventory.Active).Where(i => i.kind == ItemKind.Skill).ToList();
            return Enumerable.Range(0, 4).Where(slot => (skill.linkedMask & (1 << slot)) != 0)
                .All(slot => attached.Any(i => i.skillSlot == slot && i.active?.linkId == skill.linkId && i.active.linkedMask == skill.linkedMask));
        }
        public static void Cast(Player p, ActiveSkillData skill, Vector2 dir, Vector2 aim)
        {
            var ctx = new CastContext { player = p, skill = skill, weaponId = p.Inventory.Active.id, attack = p.AttackPower, power = ActiveSkillGenerator.Magnitude(skill) };
            p.StartCoroutine(Run(ctx, dir, aim));
        }
        static IEnumerator Run(CastContext ctx, Vector2 dir, Vector2 aim)
        {
            var p = ctx.player; var a = ctx.skill;
            running[p] = running.TryGetValue(p, out int n) ? n + 1 : 1;
            try
            {
                p.Abilities.OnActiveCast();
                foreach (var step in a.steps)
                {
                    if (!ctx.Valid) yield break;
                    if (step.timing == "delayed") yield return new WaitForSeconds(C.delay);
                    int pulses = step.delivery == "field" || step.timing == "repeat" ? C.maxPulses : 1;
                    Vector2 field = p.Pos + Vector2.ClampMagnitude(aim - p.Pos, ActiveSkillGenerator.Range(a, step));
                    for (int pulse = 0; pulse < pulses; pulse++)
                    {
                        if (!ctx.Valid) yield break;
                        if (ActiveSkillGenerator.Moving(step))
                        {
                            Vector2 from = p.Pos;
                            Vector2 travel = step.delivery == "retreat" ? -dir : dir;
                            float distance = Mathf.Min(C.movementDistance, ActiveSkillGenerator.Range(a, step));
                            if (step.delivery == "blink")
                            {
                                // Teleport only along a traversable path; generated skills cannot bypass terrain rules.
                                p.Pos = WorldStreamer.I.Move(from, travel * distance, p.radius, bound: p.Bound);
                            }
                            else
                            {
                                float t = 0;
                                while (t < C.movementDuration && ctx.Valid)
                                {
                                    float dt = Mathf.Min(Time.deltaTime, C.movementDuration - t); t += dt;
                                    p.Pos = WorldStreamer.I.Move(p.Pos, travel * (distance / C.movementDuration) * dt, p.radius, bound: p.Bound);
                                    yield return null;
                                }
                            }
                            if (!ctx.Valid) yield break;
                            ApplyTargets(ctx, step, new List<Actor>(Combat.Line(from, p.Pos, C.projectileRadius, Team.Enemy)), dir, 1);
                        }
                        else if (step.effect is "shield" or "heal")
                        {
                            if (step.effect == "shield") p.AddShield(ctx.attack * ctx.power / C.shieldScale / pulses, C.effectDuration);
                            else p.Heal(ctx.attack * ctx.power / C.healScale / pulses);
                            if (step.delivery == "guard") p.invulnUntil = Mathf.Max(p.invulnUntil, Time.time + C.guardInvulnerability);
                            Play(ctx, 0, p.Pos, dir, C.effectPixels);
                        }
                        else if (step.delivery == "projectile") Fire(ctx, step, dir, pulses);
                        else
                        {
                            float range = ActiveSkillGenerator.Range(a, step);
                            Vector2 origin = step.delivery == "field" ? field : p.Pos;
                            IEnumerable<Actor> targets = step.delivery switch {
                                "arc" => new List<Actor>(Combat.Arc(p.Pos, dir, range, 140, Team.Enemy)),
                                "line" => new List<Actor>(Combat.Line(p.Pos, p.Pos + dir * range, C.projectileRadius, Team.Enemy)),
                                _ => new List<Actor>(Actor.InRadius(origin, Mathf.Min(C.radius, range), Team.Enemy)) };
                            var selected = targets.Take(C.maxTargets).ToList();
                            if (step.pattern == "chain" && selected.Count > 0)
                            {
                                Actor last = selected[0];
                                selected.AddRange(Actor.InRadius(last.Pos, C.radius, Team.Enemy).Where(x => !selected.Contains(x)).Take(C.maxTargets - selected.Count));
                            }
                            float width = step.delivery == "line" ? range : step.delivery == "arc" ? range * 2 : Mathf.Min(C.radius, range) * 2;
                            Play(ctx, 0, step.delivery == "line" ? p.Pos + dir * range * .5f : origin, dir, Mathf.RoundToInt(width * Art.PPU));
                            ApplyTargets(ctx, step, selected, dir, pulses);
                        }
                        if (pulse + 1 < pulses) yield return new WaitForSeconds(C.pulseInterval);
                    }
                }
            }
            finally
            {
                if (running.TryGetValue(p, out int remaining)) { if (remaining <= 1) running.Remove(p); else running[p] = remaining - 1; }
            }
        }

        static void Play(CastContext ctx, int cell, Vector2 at, Vector2 dir, int size) => Fx.I?.Play(ActiveSkillArt.Effect(ctx.skill, cell, size), at, dir.Angle(), 20, 1, null, true);
        static string Status(string condition) => condition switch { "marked" => "mark", "burning" => "burn", "chilled" => "chill", "poisoned" => "poison", _ => null };
        static string Mark(ActiveSkillData a) => a.linkedMask == 0 ? "mark" : "active:" + a.linkId;
        static bool Eligible(CastContext ctx, ActiveStep step, Actor target)
        {
            string status = step.condition == "marked" ? Mark(ctx.skill) : Status(step.condition);
            return target != null && target.Alive && (ctx.affected.Contains(target) || ctx.affected.Count < C.maxTargets) && (status == null || target.HasStatus(status));
        }
        static DamageInfo Damage(CastContext ctx, ActiveStep step, Vector2 dir, int divisions) => new DamageInfo {
            amount = ctx.attack * ctx.power * (step.condition == "always" ? 1 : 1 + C.conditionalBonus) / Mathf.Max(1, divisions),
            source = ctx.player, direction = dir, knockback = 0, canCrit = true, element = System.Enum.TryParse(step.element, out Element el) ? el : Element.None,
            tags = "SKILL " + (step.delivery == "projectile" ? "PROJECTILE RANGED" : "MELEE"),
            impactFx = ActiveSkillArt.Effect(ctx.skill, 1, C.effectPixels), hitstop = 0.02f };
        static void ApplyTargets(CastContext ctx, ActiveStep step, IEnumerable<Actor> targets, Vector2 dir, int pulses)
        {
            foreach (var target in targets.Where(t => Eligible(ctx, step, t)).Take(C.maxTargets).ToList())
            {
                if (step.effect == "damage")
                {
                    float dealt = Combat.Hit(target, Damage(ctx, step, dir, pulses), Pal.Hex(ctx.skill.color));
                    if (dealt <= 0) continue;
                    OnHit(ctx, step, target, dir);
                }
                else OnHit(ctx, step, target, dir);
            }
        }
        static void OnHit(CastContext ctx, ActiveStep step, Actor target, Vector2 dir)
        {
            if (!ctx.Valid) return;
            if (!ctx.affected.Contains(target) && ctx.affected.Count >= C.maxTargets) return;
            ctx.affected.Add(target);
            if (!ctx.healed && step.effect == "damage") { ctx.healed = true; ctx.player.Abilities.OnActiveHit(); }
            if (step.effect == "mark") target.AddStatus(Mark(ctx.skill), C.statusDuration, 0, 0, ctx.player, Pal.Hex(ctx.skill.color));
            if (step.effect == "stun" && target is Enemy enemy) enemy.Stun(C.stunDuration);
            if (step.effect == "pull") target.Knockback(ctx.player.Pos - target.Pos, C.knockback);
            if (step.effect == "push") target.Knockback(target.Pos - ctx.player.Pos, C.knockback);
            if (step.effect == "damage" && step.element != "None")
            {
                // Bounded status application; generated lightning cannot recursively spawn more hits.
                string status = step.element switch { "Fire" => "burn", "Frost" => "chill", "Poison" => "poison", _ => null };
                if (status != null) target.AddStatus(status, C.statusDuration, 0, step.element == "Frost" ? .2f : 0, ctx.player, Pal.Hex(ctx.skill.color));
            }
            if (step.consume)
            {
                string status = step.condition == "marked" ? Mark(ctx.skill) : Status(step.condition);
                if (status != null) target.statuses.RemoveAll(s => s.id == status);
            }
            Play(ctx, 1, target.Pos, dir, C.effectPixels);
        }
        static void Fire(CastContext ctx, ActiveStep step, Vector2 dir, int pulses)
        {
            int count = step.pattern is "fan" or "ring" ? C.maxProjectiles : 1;
            float range = ActiveSkillGenerator.Range(ctx.skill, step);
            Play(ctx, 3, ctx.player.Pos, dir, C.projectilePixels);
            for (int i = 0; i < count; i++)
            {
                float angle = dir.Angle() + (step.pattern == "ring" ? i * 360f / count : step.pattern == "fan" ? (i - (count - 1) * .5f) * 15 : 0);
                Vector2 shotDir = MathX.Dir(angle);
                var projectile = Projectile.Spawn(ActiveSkillArt.Shot(ctx.skill), ctx.player.Pos + Vector2.up * .4f, shotDir * C.projectileSpeed, Team.Enemy, ctx.player,
                    Damage(ctx, step, shotDir, pulses * count * (step.pattern == "return" ? 2 : 1)), true);
                projectile.life = range / C.projectileSpeed * (step.pattern == "return" ? 2 : 1);
                projectile.radius = C.projectileRadius; projectile.pierce = step.pattern is "pierce" or "return" ? C.maxTargets : 0;
                projectile.color = Pal.Hex(ctx.skill.color); projectile.trailFx = ActiveSkillArt.Effect(ctx.skill, 2, C.projectilePixels);
                projectile.canHit = (_, target) => ctx.Valid && Eligible(ctx, step, target) && (ctx.affected.Contains(target) || ctx.affected.Count < C.maxTargets);
                projectile.resolveHit = (_, target) => { ApplyTargets(ctx, step, new[] { target }, shotDir, pulses * count * (step.pattern == "return" ? 2 : 1)); };
                bool returning = false;
                projectile.onUpdate = (pr, age) =>
                {
                    if (!ctx.Valid) { pr.Block(); return; }
                    if (step.pattern == "seek")
                    {
                        var target = Actor.Nearest(pr.transform.position, range, Team.Enemy);
                        if (target != null) pr.vel = Vector2.Lerp(pr.vel, (target.Pos - (Vector2)pr.transform.position).normalized * C.projectileSpeed, Time.deltaTime * 5);
                    }
                    if (step.pattern == "return" && age > range / C.projectileSpeed)
                    {
                        if (!returning) { returning = true; pr.ResetHits(); }
                        pr.vel = (ctx.player.Pos - (Vector2)pr.transform.position).normalized * C.projectileSpeed;
                        if (Vector2.Distance(ctx.player.Pos, pr.transform.position) < C.projectileRadius) pr.Block();
                    }
                };
            }
        }
    }
}
