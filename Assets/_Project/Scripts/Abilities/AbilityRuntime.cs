using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    [Serializable]
    public class AbilityRecord
    {
        public string json;     // AbilityGraph JSON (the ability carved on a grave / recovered from one)
        public string name;
        public static AbilityRecord From(AbilityGraph g) => new AbilityRecord { json = JsonUtility.ToJson(g), name = g.name };
        public AbilityGraph ToGraph() => JsonUtility.FromJson<AbilityGraph>(json);
    }

    /// <summary>
    /// ⑪ Compile → runs the abilities on the player at runtime.
    /// Subscribes to GameEvents, checks conditions, and executes effect nodes.
    /// Synergy: an effect's power is multiplied by every modifier matching its tags (the AI doesn't need to reason about this).
    /// </summary>
    public class AbilityRuntime
    {
        public readonly List<AbilityGraph> Owned = new List<AbilityGraph>();
        readonly Dictionary<AbilityGraph, float> lastFire = new Dictionary<AbilityGraph, float>();
        readonly Dictionary<AbilityGraph, int> counters = new Dictionary<AbilityGraph, int>();
        readonly Dictionary<AbilityGraph, int> charges = new Dictionary<AbilityGraph, int>();
        readonly Player p;
        float intervalT;
        public int fireCount;

        public AbilityRuntime(Player owner) { p = owner; }

        public void Add(AbilityGraph g)
        {
            Owned.Add(g);
            Recompute();
        }

        public void Clear() { Owned.Clear(); lastFire.Clear(); counters.Clear(); charges.Clear(); }

        /// <summary>Re-apply stat-type abilities.</summary>
        public void Recompute() => p.RecalcStats();

        public void ApplyStats(Stats s)
        {
            foreach (var g in Owned)
                if (g.kind == "stat")
                {
                    var id = AbilityGraph.ParseStat(g.stat);
                    if (IsMultiplicative(id)) s.Mul(id, g.statValue);
                    else s.Add(id, g.statValue);
                }
        }

        public static bool IsMultiplicative(StatId id) => id is StatId.Attack or StatId.AttackSpeed or StatId.MoveSpeed or StatId.XpGain or StatId.GoldGain;

        /// <summary>Synergy Resolver: multiplier from every modifier that matches the effect's tags.</summary>
        public float SynergyMul(IEnumerable<string> tags)
        {
            float m = 1;
            foreach (var g in Owned)
                if (g.kind == "modifier" && tags.Contains(g.modTag)) m *= g.modMul;
            return m;
        }

        public float SynergyMul(string tagsSpaceSeparated) =>
            string.IsNullOrEmpty(tagsSpaceSeparated) ? 1 : SynergyMul(tagsSpaceSeparated.Split(' '));

        public void Tick(float dt)
        {
            intervalT += dt;
            if (intervalT >= 0.5f)
            {
                intervalT = 0;
                Fire(new CombatEvent { type = Trig.Interval, source = p, position = p.Pos, tags = "" });
            }
        }

        public void Fire(CombatEvent ev)
        {
            if (ev.depth > 2) return;           // prevent infinite ability chains
            if (ev.source != p && ev.type != Trig.CloneExpire && ev.type != Trig.CloneSpawn && !(ev.source is Clone)) return;
            for (int i = 0; i < Owned.Count; i++)
            {
                var g = Owned[i];
                if (g.kind != "trigger") continue;
                if (Trigger.Parse(g.trigger) != ev.type) continue;
                // Don't let an ability that chains through its own Hit fire again
                if (ev.tags != null && ev.tags.Contains("ABILITY:" + g.id)) continue;
                if (!CheckConditions(g, ev)) continue;
                lastFire[g] = Time.time;
                fireCount++;
                Execute(g, ev);
            }
        }

        bool CheckConditions(AbilityGraph g, CombatEvent ev)
        {
            foreach (var c in g.conditions)
            {
                switch (c.type)
                {
                    case "Chance": if (UnityEngine.Random.value > c.value) return false; break;
                    case "Cooldown": if (lastFire.TryGetValue(g, out var lf) && Time.time - lf < c.value) return false; break;
                    case "HealthBelow": if (p.hp / p.maxHp > c.value) return false; break;
                    case "WithinRange": if (ev.target == null || Vector2.Distance(ev.target.Pos, p.Pos) > c.value) return false; break;
                    case "TargetHasStatus": if (ev.target == null || !ev.target.HasStatus(c.text)) return false; break;
                    case "EveryNth":
                        counters[g] = (counters.TryGetValue(g, out var n) ? n : 0) + 1;
                        if (counters[g] % Mathf.Max(2, (int)c.value) != 0) return false;
                        break;
                }
            }
            if (!g.conditions.Any(c => c.type == "Cooldown") && lastFire.TryGetValue(g, out var last) && Time.time - last < 0.12f) return false;
            return true;
        }

        void Execute(AbilityGraph g, CombatEvent ev)
        {
            p.StartCoroutine(Run(g, ev));
        }

        IEnumerator Run(AbilityGraph g, CombatEvent ev)
        {
            foreach (var e in g.effects)
            {
                if (e.delay > 0) yield return new WaitForSeconds(e.delay);
                if (p == null || !p.Alive) yield break;
                Apply(g, e, ev);
                if (e.relation == "Repeat")
                {
                    yield return new WaitForSeconds(Mathf.Max(0.25f, e.delay));
                    if (p == null || !p.Alive) yield break;
                    var e2 = e; Apply(g, e2, ev, 0.6f);
                }
            }
        }

        Vector2 Where(EffectNode e, CombatEvent ev)
        {
            switch (e.form)
            {
                case "AtSelf": case "Ring": return p.Pos;
                case "DashOrigin": return p.LastDashOrigin;
                case "DashPath": return p.Pos;
                case "Forward": return p.Pos;
                default: return ev.target != null ? ev.target.Pos : (ev.position != Vector2.zero ? ev.position : p.Pos);
            }
        }

        void Apply(AbilityGraph g, EffectNode e, CombatEvent ev, float scale = 1f)
        {
            var el = EffectNode.ParseElement(e.element);
            Color32 col = el != Element.None ? Combat.ElementColor(el) : p.Weapon.slashColor;
            string tags = string.Join(" ", g.tags) + " ABILITY:" + g.id;
            float syn = SynergyMul(g.tags);
            float baseDmg = p.AttackPower;
            Vector2 at = Where(e, ev);
            int depth = ev.depth + 1;
            DamageInfo D(float mult, Vector2 dir) => new DamageInfo
            {
                amount = baseDmg * mult * syn * scale, source = p, direction = dir, knockback = 2.5f, hitstop = 0.03f, canCrit = true,
                element = el, tags = tags, depth = depth, fromAbility = true,
            };

            switch (e.action)
            {
                case "Damage":
                {
                    if (e.form == "DashPath")
                    {
                        var hits = new List<Actor>(Combat.Line(p.LastDashOrigin, p.Pos, 0.9f, Team.Enemy));
                        Fx.I?.Play(Art.Thrust(col, Pal.White, Mathf.Max(8, Mathf.RoundToInt(Vector2.Distance(p.LastDashOrigin, p.Pos) * Art.PPU))), p.LastDashOrigin, (p.Pos - p.LastDashOrigin).Angle(), 30, 1, null, true);
                        foreach (var a in hits) { Combat.Hit(a, D(e.power, p.Pos - p.LastDashOrigin), col, "hit_slice", 0.12f, 2); Combat.ApplyElement(a, el, baseDmg * e.power, p, depth); }
                    }
                    else if (e.radius > 0)
                    {
                        Fx.I?.Play(Art.Shockwave(col, Mathf.RoundToInt(e.radius * Art.PPU)), at, 0, 26, 1, null, true);
                        foreach (var a in new List<Actor>(Actor.InRadius(at, e.radius, Team.Enemy))) { Combat.Hit(a, D(e.power, a.Pos - at), col, "hit", 0.1f, 1.5f); Combat.ApplyElement(a, el, baseDmg * e.power, p, depth); }
                    }
                    else
                    {
                        var t = ev.target != null && ev.target.Alive ? ev.target : Actor.Nearest(at, 3f, Team.Enemy);
                        if (t != null)
                        {
                            Fx.I?.Play(Art.Slash(col, Pal.White, 14, 200, 1), t.Pos + Vector2.up * 0.4f, UnityEngine.Random.Range(0, 360f), 30, 1, null, true);
                            Combat.Hit(t, D(e.power, t.Pos - p.Pos), col, "hit_slice", 0.1f, 2);
                            Combat.ApplyElement(t, el, baseDmg * e.power, p, depth);
                        }
                    }
                    break;
                }
                case "Nova":
                {
                    float power = e.power;
                    if (e.relation == "Consume" && ev.target != null)
                    {
                        string st = ev.target.statuses.FirstOrDefault()?.id;
                        if (st != null) { ev.target.statuses.RemoveAll(s => s.id == st); power *= 1.8f; }
                    }
                    Fx.I?.Play(Art.Shockwave(col, Mathf.RoundToInt(e.radius * Art.PPU)), at, 0, 22, 1, null, true);
                    Fx.I?.Burst(at, col, 16, 9, 0.45f);
                    CameraRig.I?.Shake(0.18f);
                    Sfx.Play("explode", 0.5f);
                    foreach (var a in new List<Actor>(Actor.InRadius(at, e.radius, Team.Enemy))) { Combat.Hit(a, D(power, a.Pos - at), col, "hit", 0.08f, 1); Combat.ApplyElement(a, el, baseDmg * power, p, depth); }
                    break;
                }
                case "Projectile":
                {
                    int n = Mathf.Max(1, e.count);
                    Vector2 origin = e.form == "AtTarget" && ev.target != null ? ev.target.Pos + Vector2.up * 0.4f : p.Pos + Vector2.up * 0.5f;
                    Vector2 aim = (p.AimPoint - origin).SafeNormal(p.Facing);
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 dir = e.form == "Ring" || e.form == "AtTarget" ? MathX.Dir(360f / n * i + UnityEngine.Random.Range(0, 20f)) : aim.Rotate((i - (n - 1) / 2f) * 12f);
                        var pr = Projectile.Spawn(el != Element.None ? Art.Orb(Pal.White, col, 8) : Art.Knife(), origin, dir * 12f, Team.Enemy, p, D(e.power, dir), el != Element.None);
                        pr.color = col; pr.life = 0.8f; pr.homing = e.relation == "Chain" ? 6 : 2.5f; pr.pierce = e.relation == "Transfer" ? 2 : 0; pr.spin = el == Element.None;
                    }
                    Sfx.Play("shoot", 0.4f);
                    break;
                }
                case "Lightning":
                {
                    var t = ev.target != null && ev.target.Alive ? ev.target : Actor.Nearest(at, 6f, Team.Enemy);
                    int chains = Mathf.Max(1, e.count);
                    var hit = new HashSet<Actor>();
                    for (int i = 0; i < chains && t != null; i++)
                    {
                        hit.Add(t);
                        Fx.I?.Play(Art.LightningBolt(64), t.Pos, 0, 22, 1, null, true);
                        Combat.Hit(t, D(e.power * (i == 0 ? 1 : 0.75f), Vector2.down), Pal.Lightning, "thunder", 0.12f, 1.5f);
                        Actor next = null; float bd = 16;
                        foreach (var a in Actor.All) if (a != null && a.Alive && a.team == Team.Enemy && !hit.Contains(a)) { float d = (a.Pos - t.Pos).sqrMagnitude; if (d < bd) { bd = d; next = a; } }
                        t = next;
                    }
                    break;
                }
                case "Spawn":
                {
                    for (int i = 0; i < Mathf.Max(1, e.count); i++)
                    {
                        Vector2 sp = at + (e.count > 1 ? MathX.Dir(i * 360f / e.count) * 0.6f : Vector2.zero);
                        switch (e.entity)
                        {
                            case "Clone":
                            case "PhantomWeapon":
                                Clone.Spawn(p, sp, e.duration, e.power * syn * scale, e.relation == "Copy", e.entity == "PhantomWeapon", el, tags, depth);
                                break;
                            case "Turret": Summon.Turret(p, sp, e.duration, e.power * syn * scale, el, tags); break;
                            case "Orb": Summon.Orb(p, e.duration, e.power * syn * scale, el, i, e.count, tags); break;
                            case "Zone": Summon.Zone(p, sp, e.duration, e.power * syn * scale, el, tags); break;
                        }
                    }
                    break;
                }
                case "Heal": p.Heal(p.maxHp * e.power * syn); Fx.I?.Burst(p.Pos + Vector2.up * 0.5f, Pal.Hex("8bff9a"), 8, 3, 0.6f, -2); break;
                case "Shield": p.AddShield(p.maxHp * e.power * syn, e.duration); break;
                case "Buff": p.AddBuff(AbilityGraph.ParseStat(e.stat), e.power * syn, e.duration); break;
                case "ApplyStatus":
                {
                    var t = ev.target != null && ev.target.Alive ? ev.target : Actor.Nearest(at, 3f, Team.Enemy);
                    if (t != null)
                    {
                        Combat.ApplyElement(t, el == Element.None ? Element.Fire : el, baseDmg * e.power * syn, p, depth);
                        if (e.relation == "Mark") t.AddStatus("mark", e.duration, 0, 0, p, Pal.ShadowEl);
                    }
                    break;
                }
                case "Pull":
                    Fx.I?.Play(Art.Shockwave(col, Mathf.RoundToInt(e.radius * Art.PPU)), at, 0, 18, 1, null, true);
                    foreach (var a in new List<Actor>(Actor.InRadius(at, e.radius, Team.Enemy))) a.Knockback(at - a.Pos, 7f);
                    break;
                case "Push":
                    Fx.I?.Play(Art.Shockwave(Pal.White, Mathf.RoundToInt(e.radius * Art.PPU)), at, 0, 26, 1, null, true);
                    foreach (var a in new List<Actor>(Actor.InRadius(at, e.radius, Team.Enemy))) { a.Knockback(a.Pos - at, e.power); Combat.Hit(a, D(0.2f, a.Pos - at), Pal.White, "hit_light", 0.06f, 1); }
                    break;
                case "Blink":
                {
                    var t = ev.target != null ? ev.target : Actor.Nearest(p.Pos, 5f, Team.Enemy);
                    Vector2 dst = t != null ? (Vector2)t.Pos - (t.Pos - p.Pos).normalized * 0.8f : p.Pos + p.Facing * e.radius;
                    p.BlinkTo(dst);
                    break;
                }
                case "ResetCooldown": p.ReduceCooldowns(e.power * syn); break;
                case "Store":
                    charges[g] = Mathf.Min(e.count, (charges.TryGetValue(g, out var c) ? c : 0) + 1);
                    break;
                case "Release":
                {
                    int stored = 0;
                    foreach (var kv in charges.ToList()) { stored += kv.Value; charges[kv.Key] = 0; }
                    if (stored <= 0) break;
                    float mul = e.power * (1 + stored * 0.35f);
                    Fx.I?.Play(Art.Shockwave(col, Mathf.RoundToInt(e.radius * Art.PPU)), p.Pos, 0, 22, 1.2f, null, true);
                    foreach (var a in new List<Actor>(Actor.InRadius(p.Pos, e.radius, Team.Enemy))) Combat.Hit(a, D(mul, a.Pos - p.Pos), col, "hit_heavy", 0.2f, 3);
                    break;
                }
            }
        }

        public int ChargesFor(AbilityGraph g) => charges.TryGetValue(g, out var c) ? c : 0;
    }
}
