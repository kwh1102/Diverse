using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>터렛 / 궤도 구체 / 장판 / 지뢰 / 토템 / 방벽 / 미끼 — 능력이 소환하는 단순한 존재. 관계(Follow·Attach)와 수명 조작을 받는다.</summary>
    public class Summon : MonoBehaviour
    {
        enum Kind { Turret, Orb, Zone, Mine, Totem, Barrier, Decoy }
        Kind kind;
        Player owner;
        float life, power, t, angle;
        Element element;
        string tags;
        Color32 col;
        int index, total;
        [SerializeField] SpriteRenderer sr;
        readonly Dictionary<Actor, float> zoneHits = new Dictionary<Actor, float>();
        public static readonly List<Summon> Active = new List<Summon>();

        Actor attachTo;            // Attach relation: rides on an enemy
        bool follow;               // Follow relation: stays near the owner
        public string KindName => kind.ToString();
        public Vector2 Pos => transform.position;
        public void Follow() => follow = true;
        public void Attach(Actor a) => attachTo = a;
        public void Extend(float seconds) => life += seconds;

        /// <summary>DetonateEntities: burst for mult × attack power and disappear.</summary>
        public void Burst(float mult, float radius, string ability)
        {
            if (owner == null) return;
            Vector2 pos = transform.position;
            Fx.I?.Play(Art.Shockwave(col, Mathf.RoundToInt(radius * Art.PPU)), pos, 0, 24, 1, null, true);
            foreach (var a in new List<Actor>(Actor.InRadius(pos, radius, Team.Enemy)))
            {
                var d = new DamageInfo { amount = owner.AttackPower * mult, source = owner, direction = a.Pos - pos, knockback = 3, hitstop = 0.04f, element = element, tags = tags + " " + ability, canCrit = true, fromAbility = true, depth = 2 };
                Combat.Hit(a, d, col, "hit", 0.1f, 1.5f);
            }
            owner.Abilities?.Raise("EntityExpired", pos, kind.ToString());
            Destroy(gameObject);
        }

        void Awake() => Active.Add(this);
        void OnDestroy() => Active.Remove(this);

        static Summon Make(string name, Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Instantiate(DB.Asset.summonPrefab, pos, Quaternion.identity);
            s.name = name;
            s.owner = p; s.life = life; s.power = power; s.element = el; s.tags = tags;
            s.col = el != Element.None ? Combat.ElementColor(el) : Pal.Hex("bfe3ff");
            return s;
        }

        public static Summon Turret(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Turret", p, pos, life, power, el, tags);
            s.kind = Kind.Turret;
            Art.Setup(s.sr, Art.Orb(Pal.White, s.col, 12), 0, true);
            return s;
        }

        public static Summon Orb(Player p, float life, float power, Element el, int index, int total, string tags)
        {
            var s = Make("Orb", p, p.Pos, life, power, el, tags);
            s.kind = Kind.Orb; s.index = index; s.total = Mathf.Max(1, total);
            Art.Setup(s.sr, Art.Orb(Pal.White, s.col, 10), 0, true);
            return s;
        }

        /// <summary>Mine: arms after 0.4s, explodes on the first enemy that steps on it.</summary>
        public static Summon Mine(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Mine", p, pos, life, power, el, tags);
            s.kind = Kind.Mine;
            Art.Setup(s.sr, Art.Diamond(7), 0, true);
            s.sr.color = s.col;
            return s;
        }

        /// <summary>Totem: pulses an aura — heals the owner when near, damages enemies around it.</summary>
        public static Summon Totem(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Totem", p, pos, life, power, el, tags);
            s.kind = Kind.Totem;
            Art.Setup(s.sr, Art.Orb(s.col, Pal.White, 14), 0, true);
            return s;
        }

        /// <summary>Barrier: blocks enemy projectiles that touch it.</summary>
        public static Summon Barrier(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Barrier", p, pos, life, power, el, tags);
            s.kind = Kind.Barrier;
            Art.Setup(s.sr, Art.Ring(16, 2), 0, true);
            var c = (Color)s.col; c.a = 0.7f; s.sr.color = c;
            return s;
        }

        /// <summary>Decoy: pulls aggro — nearby enemies are drawn toward it (taunt).</summary>
        public static Summon Decoy(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Decoy", p, pos, life, power, el, tags);
            s.kind = Kind.Decoy;
            Art.Setup(s.sr, Art.Rabbit(p.Costume, Pose.Idle0), 0, false);
            var c = (Color)s.col; c.a = 0.6f; s.sr.color = c;
            return s;
        }

        public static Summon Zone(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Zone", p, pos, life, power, el, tags);
            s.kind = Kind.Zone;
            Art.Setup(s.sr, Art.WarningCircle(28), -29000, true);
            var c = (Color)s.col; c.a = 0.55f; s.sr.color = c;
            return s;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            life -= dt; t += dt;
            if (life <= 0 || owner == null)
            {
                if (owner != null) owner.Abilities?.Raise("EntityExpired", transform.position, kind.ToString());
                Destroy(gameObject);
                return;
            }
            if (attachTo != null && attachTo.Alive) transform.position = attachTo.Pos;
            else if (follow && kind != Kind.Orb) transform.position = Vector2.Lerp(transform.position, owner.Pos, 1 - Mathf.Exp(-4 * dt));
            Vector2 pos = transform.position;
            switch (kind)
            {
                case Kind.Turret:
                    sr.sortingOrder = Art.SortY(pos.y);
                    transform.position = pos + Vector2.up * Mathf.Sin(t * 4) * 0.004f;
                    if (t >= 0.6f)
                    {
                        var target = Actor.Nearest(pos, 7f, Team.Enemy);
                        if (target != null)
                        {
                            t = 0;
                            var dir = ((Vector2)target.Pos + Vector2.up * 0.3f - pos).normalized;
                            var d = new DamageInfo { amount = owner.AttackPower * power * 0.6f, source = owner, direction = dir, knockback = 1, hitstop = 0.015f, element = element, tags = tags, canCrit = true, fromAbility = true, depth = 2 };
                            var pr = Projectile.Spawn(Art.Orb(Pal.White, col, 6), pos, dir * 14f, Team.Enemy, owner, d, true);
                            pr.color = col; pr.life = 0.7f;
                            Sfx.Play("shoot", 0.25f, 1.4f);
                        }
                    }
                    break;
                case Kind.Orb:
                {
                    angle += dt * 200f;
                    var c = owner.Pos + Vector2.up * 0.5f + MathX.Dir(angle + index * 360f / total) * 1.6f;
                    transform.position = c;
                    sr.sortingOrder = Art.SortY(c.y - 0.5f);
                    foreach (var a in new List<Actor>(Actor.InRadius(c, 0.35f, Team.Enemy)))
                    {
                        if (zoneHits.TryGetValue(a, out var last) && Time.time - last < 0.5f) continue;
                        zoneHits[a] = Time.time;
                        var d = new DamageInfo { amount = owner.AttackPower * power * 0.5f, source = owner, direction = a.Pos - owner.Pos, knockback = 2, hitstop = 0.02f, element = element, tags = tags, canCrit = true, fromAbility = true, depth = 2 };
                        Combat.Hit(a, d, col, "hit_light", 0.05f, 1);
                        Combat.ApplyElement(a, element, d.amount, owner, 2);
                    }
                    break;
                }
                case Kind.Mine:
                {
                    sr.sortingOrder = Art.SortY(pos.y) - 5;
                    if (t < 0.4f) break;
                    if (Actor.Nearest(pos, 0.7f, Team.Enemy) == null) break;
                    Fx.I?.Play(Art.Shockwave(col, 36), pos, 0, 24, 1, null, true);
                    Sfx.Play("explode", 0.5f);
                    foreach (var a in new List<Actor>(Actor.InRadius(pos, 1.6f, Team.Enemy)))
                    {
                        var d = new DamageInfo { amount = owner.AttackPower * power, source = owner, direction = a.Pos - pos, knockback = 4, hitstop = 0.05f, element = element, tags = tags, canCrit = true, fromAbility = true, depth = 2 };
                        Combat.Hit(a, d, col, "hit_heavy", 0.15f, 2);
                        Combat.ApplyElement(a, element, d.amount, owner, 2);
                    }
                    owner.Abilities?.Raise("EntityExpired", pos, "Mine");
                    Destroy(gameObject);
                    return;
                }
                case Kind.Totem:
                {
                    sr.sortingOrder = Art.SortY(pos.y);
                    if (t < 1f) break;
                    t = 0;
                    Fx.I?.Play(Art.Shockwave(col, 40), pos, 0, 16, 1, null, true);
                    if (Vector2.Distance(owner.Pos, pos) < 2.5f) owner.Heal(owner.maxHp * 0.01f * power);
                    foreach (var a in new List<Actor>(Actor.InRadius(pos, 2.5f, Team.Enemy)))
                    {
                        var d = new DamageInfo { amount = owner.AttackPower * power * 0.3f, source = owner, direction = a.Pos - pos, knockback = 0, element = element, tags = tags, fromAbility = true, depth = 2, noEvents = true, fxColor = col };
                        Combat.Hit(a, d, col);
                    }
                    break;
                }
                case Kind.Barrier:
                {
                    sr.sortingOrder = Art.SortY(pos.y) + 5;
                    foreach (var pr in Projectile.Live)
                        if (pr != null && pr.targetTeam == Team.Player && ((Vector2)pr.transform.position - pos).sqrMagnitude < 1.4f) pr.Block();
                    break;
                }
                case Kind.Decoy:
                {
                    sr.sortingOrder = Art.SortY(pos.y);
                    foreach (var a in Actor.InRadius(pos, 5f, Team.Enemy)) if (a is Enemy e) e.Taunt(pos, 0.3f);
                    break;
                }
                case Kind.Zone:
                {
                    var c = (Color)col; c.a = 0.35f + Mathf.Sin(t * 8) * 0.1f; c.a *= Mathf.Clamp01(life / 0.4f);
                    sr.color = c;
                    if (Random.value < 0.4f) Fx.I?.Burst(pos + Random.insideUnitCircle * 1.5f, col, 1, 1, 0.5f, -2);
                    foreach (var a in new List<Actor>(Actor.InRadius(pos, 1.8f, Team.Enemy)))
                    {
                        if (zoneHits.TryGetValue(a, out var last) && Time.time - last < 0.5f) continue;
                        zoneHits[a] = Time.time;
                        var d = new DamageInfo { amount = owner.AttackPower * power * 0.35f, source = owner, direction = Vector2.zero, knockback = 0, hitstop = 0, element = element, tags = tags, canCrit = false, fromAbility = true, depth = 2, noEvents = true, fxColor = col };
                        Combat.Hit(a, d, col);
                        Combat.ApplyElement(a, element, owner.AttackPower * power * 0.3f, owner, 2);
                    }
                    break;
                }
            }
        }
    }
}
