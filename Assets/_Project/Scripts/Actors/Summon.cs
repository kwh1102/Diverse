using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>터렛 / 궤도 구체 / 장판 — 단순한 소환물.</summary>
    public class Summon : MonoBehaviour
    {
        enum Kind { Turret, Orb, Zone }
        Kind kind;
        Player owner;
        float life, power, t, angle;
        Element element;
        string tags;
        Color32 col;
        int index, total;
        [SerializeField] SpriteRenderer sr;
        readonly Dictionary<Actor, float> zoneHits = new Dictionary<Actor, float>();

        static Summon Make(string name, Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Instantiate(DB.Asset.summonPrefab, pos, Quaternion.identity);
            s.name = name;
            s.owner = p; s.life = life; s.power = power; s.element = el; s.tags = tags;
            s.col = el != Element.None ? Combat.ElementColor(el) : Pal.Hex("bfe3ff");
            return s;
        }

        public static void Turret(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Turret", p, pos, life, power, el, tags);
            s.kind = Kind.Turret;
            Art.Setup(s.sr, Art.Orb(Pal.White, s.col, 12), 0, true);
        }

        public static void Orb(Player p, float life, float power, Element el, int index, int total, string tags)
        {
            var s = Make("Orb", p, p.Pos, life, power, el, tags);
            s.kind = Kind.Orb; s.index = index; s.total = Mathf.Max(1, total);
            Art.Setup(s.sr, Art.Orb(Pal.White, s.col, 10), 0, true);
        }

        public static void Zone(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Zone", p, pos, life, power, el, tags);
            s.kind = Kind.Zone;
            Art.Setup(s.sr, Art.WarningCircle(28), -29000, true);
            var c = (Color)s.col; c.a = 0.55f; s.sr.color = c;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            life -= dt; t += dt;
            if (life <= 0 || owner == null) { Destroy(gameObject); return; }
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
