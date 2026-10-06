using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Headless, deterministic combat simulation (design doc #10 / #39) — not the Unity scene but an abstract fight:
    /// a seeded stream of player events at the build's frequencies, a pack of enemies with HP and statuses, the ability's
    /// states / entities, and the event → rule → event proc chain with the real depth limit.
    ///
    /// What it checks is the DYNAMIC behaviour of trigger rules, which the static model can only estimate:
    ///   proc storms · entity floods · chains that always hit the depth cap · and the realised output of the trigger part,
    ///   compared against PowerModel's estimate of the same part (continuous stat rules are exact in the static model and
    ///   are not re-simulated). Both runs (with / without the ability) consume the SAME event stream, so the difference is
    ///   the ability alone. Units match PowerModel: damage in attack-power multiples × (100 / 1.5).
    /// Scenarios: normal pack · dense swarm · elite duel · low-HP pressure.
    /// </summary>
    public static class RuleSimulator
    {
        public class Report
        {
            public float power;                 // realised trigger-part output (PowerModel points)
            public float staticTrigger;         // PowerModel's estimate of the same trigger part
            public float peakProcsPerSecond;
            public int peakEntities;
            public bool hitDepthCap;
            public bool simulated;              // false when the ability has no trigger part (nothing dynamic to check)
            public readonly List<string> scenarios = new List<string>();
            public string Summary() => !simulated ? "sim: no trigger part (static model is exact)"
                : $"sim {power:0} vs static {staticTrigger:0} · peak procs {peakProcsPerSecond:0.#}/s · peak entities {peakEntities}{(hitDepthCap ? " · DEPTH CAP" : "")} · {string.Join(", ", scenarios)}";
        }

        struct Scenario { public string name; public int enemies; public float enemyHp, pressure, hpStart, density; }

        static readonly Scenario[] Scenarios =
        {
            new Scenario { name = "pack", enemies = 4, enemyHp = 5, pressure = 1, hpStart = 1, density = 1 },
            new Scenario { name = "swarm", enemies = 10, enemyHp = 2.5f, pressure = 1.5f, hpStart = 1, density = 1.6f },
            new Scenario { name = "elite", enemies = 1, enemyHp = 40, pressure = 0.8f, hpStart = 1, density = 0.4f },
            new Scenario { name = "lowhp", enemies = 4, enemyHp = 5, pressure = 1.3f, hpStart = 0.3f, density = 1 },
        };

        const float U = 100f / 1.5f;

        public static Report Run(AbilityDef a, BuildContext b)
        {
            var rep = new Report();
            rep.simulated = a.rules.Any(r => r.kind == RuleKind.Trigger);
            if (!rep.simulated) return rep;
            rep.staticTrigger = StaticTriggerPart(a, b);
            float sum = 0;
            for (int s = 0; s < Scenarios.Length; s++)
            {
                var sc = Scenarios[s];
                var with = Fight(sc, b, a, 1000 + s * 17);
                var without = Fight(sc, b, null, 1000 + s * 17);
                float gain = (with.damage - without.damage) * U + (with.sustain - without.sustain) * 250 + (with.shield - without.shield) * 160
                             + (with.control - without.control) * 25 + (with.utility - without.utility) * 60;
                sum += gain;
                rep.peakProcsPerSecond = Mathf.Max(rep.peakProcsPerSecond, with.peakProcs);
                rep.peakEntities = Mathf.Max(rep.peakEntities, with.peakEntities);
                if (with.chains > 10 && with.depthCapHits >= with.chains * 0.8f) rep.hitDepthCap = true;
                rep.scenarios.Add($"{sc.name} {gain:+0;-0}");
            }
            rep.power = sum / Scenarios.Length;
            return rep;
        }

        /// <summary>PowerModel's estimate of only the trigger part of the ability (what the simulation reproduces).</summary>
        static float StaticTriggerPart(AbilityDef a, BuildContext b)
        {
            // The ability's OWN contribution only: the uplift it gives to other owned abilities (more crits feeding a
            // crit ability…) is resolved exactly by the static fixed point and is not re-simulated here
            var onlyTriggers = a.Clone();
            onlyTriggers.rules.RemoveAll(r => r.kind != RuleKind.Trigger);
            // timed stat buffs are stats, not simulated damage — compare like with like
            foreach (var r in onlyTriggers.rules) r.ops.RemoveAll(o => o.op is "Buff" or "ResetCooldowns" or "GainDash" or "Blink" or "Leap" or "Swap" or "Cleanse");
            onlyTriggers.rules.RemoveAll(r => r.ops.Count == 0);
            onlyTriggers.states = a.states;
            var rep = PowerModel.Evaluate(b.owned.Concat(new[] { onlyTriggers }).ToList(), b);
            return rep.byAbility.TryGetValue(onlyTriggers, out var v) ? v : 0;
        }

        class Foe { public float hp, maxHp; public readonly Dictionary<string, float> st = new Dictionary<string, float>(); public bool elite; public bool Alive => hp > 0; }

        class Outcome { public float damage, sustain, shield, control, utility, peakProcs, kills; public int peakEntities, depthCapHits, chains; }

        static Outcome Fight(Scenario sc, BuildContext b, AbilityDef a, int seed)
        {
            var cfg = b.cfg;
            var events = new System.Random(seed);            // the shared event stream (identical with / without)
            var rng = new System.Random(seed * 7 + 3);        // the ability's own randomness
            var o = new Outcome();
            var foes = Enumerable.Range(0, sc.enemies).Select(_ => new Foe { hp = sc.enemyHp, maxHp = sc.enemyHp, elite = sc.name == "elite" }).ToList();
            float hp = sc.hpStart;
            var states = new Dictionary<string, float>();
            var lastFire = new Dictionary<RuleDef, float>();
            var nth = new Dictionary<RuleDef, int>();
            var entities = new List<(float life, float power, string kind)>();
            float empower = 0;
            int procsWindow = 0; float windowT = 0;
            const float dt = 0.1f;
            float T = Mathf.Max(10, cfg.simSeconds);
            var buildRep = PowerModel.Evaluate(b.owned, b);
            var rates = BaseRates(b, buildRep);
            var hitOnce = new HashSet<Foe>();
            bool lowLatch = false;
            // Generic status presence the static model assumes (weapon element + common sources): seeded on the shared stream
            string ws = string.IsNullOrEmpty(b.weaponElement) ? null : RuleLanguage.StatusOf(b.weaponElement);
            float t = 0;

            Foe Pick(System.Random r) { var alive = foes.Where(f => f.Alive).ToList(); return alive.Count == 0 ? null : alive[r.Next(alive.Count)]; }
            void Respawn() { foreach (var f in foes) if (!f.Alive && events.NextDouble() < 0.06) { f.hp = f.maxHp; f.st.Clear(); hitOnce.Remove(f); } }

            void Damage(Foe f, float amount, int depth, bool fromAbility)
            {
                if (f == null || !f.Alive) return;
                if (f.st.ContainsKey("mark")) amount *= 1.25f;
                if (f.st.ContainsKey("weaken")) amount *= 1.15f;
                float before = f.hp;
                f.hp -= amount;
                o.damage += Mathf.Min(before, amount);
                if (before > 0 && f.hp <= 0) o.kills++;
                if (a == null) return;
                if (hitOnce.Add(f)) Fire("FirstHit", depth, f, amount);
                Fire("Hit", depth, f, amount / 1f);
                if (rng.NextDouble() < 0.12) Fire("Crit", depth, f, amount);
                if (!f.Alive)
                {
                    Fire("Kill", depth, f, amount);
                    if (amount - before > f.maxHp * 0.5f) Fire("Overkill", depth, f, amount - before);
                    if (f.elite) Fire("EliteKill", depth, f, amount);
                }
            }

            float StateOf(string name) => states.TryGetValue(name ?? "", out var v) ? v : 0;

            float Val(string reference, Foe f, float amount) => reference switch
            {
                null or "" => 0,
                "event.amount" => amount,
                "self.hpPct" => hp, "self.missingHpPct" => 1 - hp,
                "target.hpPct" => f != null ? f.hp / f.maxHp : 1,
                "enemies.near" => Mathf.Min(foes.Count(x => x.Alive), 8 * sc.density),
                "entities.count" => entities.Count,
                _ => reference.StartsWith("state:") ? StateOf(reference.Substring(6)) : RuleLanguage.Value(reference)?.expected ?? 0,
            };

            bool Cond(RuleDef r, CondDef c, Foe f, float amount)
            {
                switch (c.type)
                {
                    case "Chance": return rng.NextDouble() < c.value;
                    case "EveryNth": nth[r] = (nth.TryGetValue(r, out var n) ? n : 0) + 1; return nth[r] % Mathf.Max(2, (int)c.value) == 0;
                    case "TargetHas": return f != null && f.st.ContainsKey(c.text);
                    case "TargetLacks": return f != null && !f.st.ContainsKey(c.text);
                    case "TargetIs": return f != null && (c.text == "Elite" || c.text == "Boss" ? f.elite : c.text == "Normal" ? !f.elite : rng.NextDouble() < 0.4);
                    case "HasEntity": return entities.Count > 0;
                    case "NoEntity": return entities.Count == 0;
                    case "Compare":
                    {
                        float v = Val(c.a, f, amount);
                        return c.cmp switch { ">=" => v >= c.value, ">" => v > c.value, "<=" => v <= c.value, "<" => v < c.value, _ => Mathf.Abs(v - c.value) < 0.01f };
                    }
                }
                return rng.NextDouble() < 0.5;
            }

            void SetState(string name, float v, int depth)
            {
                var def = a.State(name);
                if (def == null) return;
                float before = StateOf(name);
                v = Mathf.Clamp(v, 0, def.max);
                states[name] = v;
                if (before < def.max && v >= def.max) RunState("StateFull", name, depth);
                if (before > 0 && v <= 0) RunState("StateEmpty", name, depth);
            }

            void RunState(string trig, string name, int depth)
            {
                foreach (var r in a.rules.Where(r => r.trigger == trig && r.param == name).ToList())
                    if (r.when.All(c => Cond(r, c, null, 0))) { procsWindow++; foreach (var op in r.ops) Exec(op, depth + 1, null, 0); }
            }

            void Fire(string trig, int depth, Foe f, float amount)
            {
                if (a == null) return;
                if (depth > cfg.maxProcDepth) { o.depthCapHits++; return; }
                foreach (var r in a.rules.Where(r => r.kind == RuleKind.Trigger && r.trigger == trig))
                {
                    float cd = Mathf.Max(cfg.minInternalCooldown, r.cooldown);
                    if (lastFire.TryGetValue(r, out var lf) && t - lf < cd) continue;
                    if (!r.when.All(c => Cond(r, c, f, amount))) continue;
                    lastFire[r] = t;
                    procsWindow++;
                    if (depth > 0) o.chains++;
                    foreach (var op in r.ops) Exec(op, depth + 1, f, amount);
                }
            }

            float Mag(OpDef op, Foe f, float amount)
            {
                float m = op.amount + op.per * Val(op.scale, f, amount);
                var prim = RuleLanguage.Get(op.op);
                if (!string.IsNullOrEmpty(op.scale) && prim != null && prim.max > 0) m = Mathf.Min(m, prim.max * 1.5f);
                return m;
            }

            int AreaHits(float radius) => Mathf.Max(1, Mathf.Min(cfg.maxTargetsPerProc, Mathf.RoundToInt(Mathf.Clamp(1 + 0.35f * radius * radius, 1, 5) * sc.density)));

            void Exec(OpDef op, int depth, Foe f, float amount)
            {
                float m = Mag(op, f, amount);
                int reps = op.time == "Periodic" ? Mathf.Max(2, op.times) : op.time == "ForNextN" ? Mathf.Max(1, op.times) : 1;
                float relMul = RuleLanguage.Relations.TryGetValue(op.rel ?? "", out var rel) ? 1 + rel.powerMul * (op.rel is "Chain" or "Bounce" or "Pierce" or "Spread" ? Mathf.Max(1, op.times) / 2f : 1) : 1;
                for (int k = 0; k < reps; k++)
                {
                    Foe tgt = f != null && f.Alive ? f : Pick(rng);
                    switch (op.op)
                    {
                        case "Damage": Damage(tgt, m * relMul, depth, true); break;
                        case "Drain": Damage(tgt, m * relMul, depth, true); o.sustain += m * 0.4f * 0.02f; break;
                        case "Execute": if (tgt != null && !tgt.elite && tgt.hp / tgt.maxHp <= op.amount) Damage(tgt, tgt.hp + 1, depth, true); break;
                        case "Detonate": if (tgt != null && tgt.st.Remove(op.status ?? "")) Damage(tgt, m * 1.3f * relMul, depth, true); break;
                        case "Nova": case "Slash": case "Beam": case "DetonateEntities":
                        {
                            int n = op.op == "Nova" ? AreaHits(op.radius) : op.op == "Slash" ? 2 : op.op == "Beam" ? 3 : Mathf.Max(1, entities.Count);
                            foreach (var x in foes.Where(x => x.Alive).Take(n).ToList()) Damage(x, m * relMul, depth, true);
                            if (op.op == "DetonateEntities") entities.Clear();
                            break;
                        }
                        case "Projectile":
                            for (int i = 0; i < Mathf.Max(1, op.count); i++) if (rng.NextDouble() < 0.6) Damage(Pick(rng), m * relMul, depth, true);
                            Fire("ProjectileEnd", depth, null, 0);
                            break;
                        case "Chain":
                            for (int i = 0; i < Mathf.Max(1, op.count); i++) Damage(i == 0 ? tgt : Pick(rng), m * (i == 0 ? 1 : 0.75f), depth, true);
                            break;
                        case "ApplyStatus": case "Slow":
                        {
                            if (tgt == null) break;
                            string st = op.op == "Slow" ? "chill" : op.status ?? "burn";
                            tgt.st[st] = Mathf.Max(1, op.duration);
                            if (st is "burn" or "poison" or "bleed") { float dot = m * Mathf.Max(1, op.duration) * 0.7f; tgt.hp -= dot; o.damage += dot; }
                            else if (st == "chill") o.control += op.duration * 0.25f;
                            else o.damage += 0.12f * op.duration;
                            Fire("StatusApplied", depth, tgt, 0);
                            break;
                        }
                        case "Spread": case "Transfer":
                            for (int i = 0; i < Mathf.Max(1, op.count); i++) { var x = Pick(rng); if (x != null) { x.st[op.status ?? "burn"] = 3; o.damage += 0.3f; Fire("StatusApplied", depth, x, 0); } }
                            break;
                        case "Stun": o.control += m; break;
                        case "Push": case "Pull": o.control += m * 0.05f * AreaHits(op.radius); break;
                        case "Taunt": o.shield += 0.02f * m; break;
                        case "Heal": { float h = Mathf.Min(1 - hp, m); hp += h; o.sustain += m; break; }
                        case "Shield": o.shield += m * Mathf.Min(1, op.duration / 3f); break;
                        case "Cleanse": o.shield += 0.5f * m; break;
                        case "Buff": break;     // a timed stat — valued exactly by the static model (excluded from the comparison)
                        case "ResetCooldowns": o.utility += m; break;
                        case "GainDash": case "Blink": case "Leap": case "Swap": o.utility += 0.15f; break;
                        case "EmpowerNext": empower += m * Mathf.Max(1, op.count); break;
                        case "Spawn":
                            for (int i = 0; i < Mathf.Max(1, op.count); i++)
                                if (entities.Count < cfg.maxSpawnPerOwner)
                                {
                                    entities.Add((Mathf.Min(op.duration, cfg.maxSpawnDuration), m, op.entity));
                                    if (op.entity is "Clone" or "PhantomWeapon") Fire("CloneSpawn", depth, null, 0);
                                }
                            break;
                        case "AddState": SetState(op.state, StateOf(op.state) + m, depth); break;
                        case "SetState": SetState(op.state, op.amount, depth); break;
                        // economy/pickups in the same units as PowerModel (economy points ÷ 60 utility, hearts as sustain)
                        case "DropPickup": if (op.mode == "heart") o.sustain += m / 100f * 0.08f * 8; else o.utility += m * 0.8f / 60f / 100f; break;
                        case "GainGold": o.utility += m * 0.8f / 100f; break;
                        case "GainXp": o.utility += m * 1.2f / 100f; break;
                        case "GainPotion": o.sustain += 900f / 250f * Mathf.Max(1, op.count); break;
                    }
                }
            }

            for (t = 0; t < T; t += dt)
            {
                // Basic attacks (the baseline, identical with/without)
                if (events.NextDouble() < rates["Hit"] * dt)
                {
                    var f = Pick(events);
                    float dmg = 1 + empower;
                    if (a != null) { o.damage += empower > 0 ? 0 : 0; empower = 0; }
                    if (f != null) Damage(f, dmg, 0, false);
                }
                // Other player events at the build's rates (scenario-scaled)
                foreach (var kv in rates)
                {
                    if (kv.Key == "Hit") continue;
                    float rate = kv.Value * (kv.Key is "Damaged" or "PerfectDodge" or "Guard" ? sc.pressure : 1);
                    if (events.NextDouble() >= rate * dt) continue;
                    if (kv.Key == "ExtraHit") { var f = Pick(events); if (f != null) { Fire("Hit", 1, f, 0.6f); if (events.NextDouble() < 0.12) Fire("Crit", 1, f, 0.6f); } continue; }
                    if (kv.Key == "ExtraKill") { var f = Pick(events); if (f != null) Fire("Kill", 1, f, 1); continue; }
                    if (kv.Key == "Damaged") hp = Mathf.Max(0.05f, hp - 0.08f);
                    Fire(kv.Key, 0, kv.Key is "Damaged" or "Guard" ? Pick(events) : null, kv.Key == "Damaged" ? 0.08f : 0);
                }
                if (a != null)
                {
                    foreach (var r in a.rules.Where(r => r.trigger == "Every"))
                        if (Mathf.Repeat(t, Mathf.Max(1, r.period)) < dt) Fire("Every", 0, null, 0);
                    if (hp <= 0.3f && !lowLatch) { lowLatch = true; Fire("LowHealth", 0, null, 0); }
                    else if (hp > 0.45f) lowLatch = false;
                }
                // Statuses on enemies at the share the static model assumes for this build (weapon element, owned abilities…)
                foreach (var s in RuleLanguage.Statuses)
                {
                    float want = buildRep.statusPresence.TryGetValue(s, out var pr) ? pr : 0.05f;
                    var alive = foes.Where(f => f.Alive).ToList();
                    if (alive.Count == 0) continue;
                    float have = alive.Count(f => f.st.ContainsKey(s)) / (float)alive.Count;
                    if (have < want && events.NextDouble() < (want - have) * dt * 4) { var f = alive[events.Next(alive.Count)]; f.st[s] = 3; }
                }
                // Statuses tick; entities act and expire
                foreach (var f in foes.Where(f => f.Alive).ToList())
                    foreach (var k in f.st.Keys.ToList())
                    {
                        f.st[k] -= dt;
                        if (f.st[k] <= 0) { f.st.Remove(k); if (f.Alive && a != null) Fire("StatusExpired", 0, f, 0); }
                    }
                for (int i = entities.Count - 1; i >= 0; i--)
                {
                    var e = entities[i];
                    e.life -= dt;
                    float dps = e.kind switch { "Clone" => 1f, "PhantomWeapon" => 1f, "Turret" => 0.8f, "Mine" => 0.5f, "Zone" => 0.7f, "Orb" => 0.35f, "Totem" => 0.45f, _ => 0.1f } * 1.5f;
                    if (rng.NextDouble() < dt * 1.2f) Damage(Pick(rng), e.power * dps / 1.2f, 1, true);
                    entities[i] = e;
                    if (e.life <= 0) { entities.RemoveAt(i); if (a != null) { Fire(e.kind is "Clone" or "PhantomWeapon" ? "CloneExpire" : "EntityExpired", 0, null, 0); } }
                }
                o.peakEntities = Mathf.Max(o.peakEntities, entities.Count);
                if (a != null)
                    foreach (var s in a.states)
                    {
                        float v = StateOf(s.name);
                        if (s.type == "Timer" && v > 0) SetState(s.name, v - dt, 0);
                        else if (s.type == "Charge" && s.regen > 0) SetState(s.name, v + s.regen * dt, 0);
                        else if (s.decay > 0 && v > 0) SetState(s.name, v - s.decay * dt, 0);
                    }
                hp = Mathf.Min(1, hp + 0.004f * dt);
                Respawn();
                windowT += dt;
                if (windowT >= 1) { o.peakProcs = Mathf.Max(o.peakProcs, procsWindow / windowT); procsWindow = 0; windowT = 0; }
            }
            o.damage /= T; o.kills /= T; o.sustain /= T; o.shield /= T; o.control /= T; o.utility /= T;
            return o;
        }

        /// <summary>
        /// The event rates the power model computed for this build (base + what owned abilities produce). Basic hits are
        /// simulated against enemy HP; the extra Hit/Crit/Kill/StatusApplied produced by owned abilities are replayed as
        /// ambient events at their rates, so the candidate sees the same event stream the static model assumed.
        /// </summary>
        static Dictionary<string, float> BaseRates(BuildContext b, PowerReport buildRep)
        {
            var d = new Dictionary<string, float>();
            foreach (var kv in buildRep.eventRates)
            {
                if (kv.Key.Contains('|')) continue;      // per-status rates are reproduced through statusPresence
                if (kv.Key is "Every" or "StateFull" or "StateEmpty" or "LowHealth" or "Kill" or "Crit" or "FirstHit" or "Overkill" or "EliteKill" or "StatusApplied" or "StatusExpired") continue;
                d[kv.Key] = kv.Value;
            }
            // Hits: basic-attack hits are simulated; owned abilities' extra hits/kills/crits are replayed as "Extra*" ambient events
            float baseHit = RuleLanguage.Get("Hit")?.freq ?? 2;
            if (b.telemetry != null) { float obs = b.telemetry.ObservedRate("Hit", b.cfg, out float trust); if (trust > 0) baseHit = Mathf.Lerp(baseHit, obs, trust); }
            float allHit = buildRep.eventRates.TryGetValue("Hit", out var h) ? h : baseHit;
            d["Hit"] = baseHit;
            d["ExtraHit"] = Mathf.Max(0, allHit - baseHit);
            float baseKill = RuleLanguage.Get("Kill")?.freq ?? 0.3f;
            d["ExtraKill"] = Mathf.Max(0, (buildRep.eventRates.TryGetValue("Kill", out var k) ? k : baseKill) - baseKill);
            if (!b.shieldWeapon) d["Guard"] = Mathf.Min(d.TryGetValue("Guard", out var g) ? g : 0, 0.005f);
            return d;
        }
    }
}
