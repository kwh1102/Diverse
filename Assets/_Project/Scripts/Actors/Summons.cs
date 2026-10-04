using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 플레이어의 잔영 분신. Copy면 플레이어의 다음 공격을 따라 하고, 아니면 스스로 가까운 적을 벤다.
    /// 세계 법칙: Clone의 Grip=0 → 실제 무기 대신 반투명한 '환영 무기'만 든다.
    /// </summary>
    public class Clone : Actor
    {
        Player owner;
        float life, power;
        bool copy, phantom;
        Element element;
        string tags;
        int depth;
        float atkT;
        SpriteRenderer weapon;
        Color tint;
        public static readonly List<Clone> Active = new List<Clone>();

        public static Clone Spawn(Player p, Vector2 pos, float duration, float power, bool copy, bool phantom, Element el, string tags, int depth)
        {
            var go = new GameObject(phantom ? "PhantomWeapon" : "Clone");
            var c = go.AddComponent<Clone>();
            c.team = Team.Neutral;          // 적의 타겟이 되지 않음
            c.owner = p; c.life = duration; c.power = power; c.copy = copy; c.phantom = phantom; c.element = el; c.tags = tags; c.depth = depth;
            c.Pos = WorldStreamer.I != null ? WorldStreamer.I.NearestWalkable(pos, 0.3f) : pos;
            c.maxHp = c.hp = 1;
            c.Facing = p.Facing;
            c.tint = el != Element.None ? (Color)Combat.ElementColor(el) : new Color(0.65f, 0.8f, 1f);
            c.tint.a = 0.75f;
            c.body.sprite = phantom ? Art.Weapon(p.Weapon.kind) : Art.Rabbit(p.Costume, Pose.Idle0);
            c.body.sharedMaterial = Art.AddMat;
            c.body.color = c.tint;
            c.body.flipX = p.Facing.x < 0;
            if (!phantom)
            {
                var wgo = new GameObject("weapon");
                wgo.transform.SetParent(c.visual, false);
                c.weapon = Art.MakeRenderer(wgo, Art.Weapon(p.Weapon.kind), 0, true);
                c.weapon.color = c.tint;
                wgo.transform.localPosition = Art.HandAnchor;
            }
            else c.visual.localRotation = Quaternion.Euler(0, 0, 45);
            c.shadow.enabled = false;
            Active.Add(c);
            Fx.I?.Burst(c.Pos + Vector2.up * 0.5f, (Color32)c.tint, 10, 4, 0.4f);
            GameEvents.Raise(new CombatEvent { type = Trig.CloneSpawn, source = p, position = c.Pos, tags = "CLONE", depth = depth });
            if (!copy) c.atkT = 0.2f;
            return c;
        }

        protected override void OnDestroy() { base.OnDestroy(); Active.Remove(this); }

        /// <summary>Copy 분신: 플레이어가 공격하면 같은 방향으로 따라 벤다.</summary>
        public void Mimic(Vector2 dir, ComboStep step)
        {
            if (!copy || owner == null) return;
            Strike(dir, step.range * 0.9f, step.arc, step.damage);
        }

        void Strike(Vector2 dir, float range, float arc, float mult)
        {
            Facing = dir;
            var w = owner.Weapon;
            Color32 col = element != Element.None ? Combat.ElementColor(element) : w.slashColor;
            Vector2 origin = Pos + Vector2.up * 0.45f;
            Fx.I?.Play(Art.Slash(col, Pal.White, Mathf.RoundToInt(range * Art.PPU * 0.8f), Mathf.Min(arc, 200), 1), origin, dir.Angle(), 30, 1, new Color(1, 1, 1, 0.85f), true);
            foreach (var a in new List<Actor>(Combat.Arc(origin, dir, range, arc, Team.Enemy)))
            {
                var d = new DamageInfo
                {
                    amount = owner.AttackPower * mult * power, source = owner, direction = a.Pos - Pos, knockback = 1.5f, hitstop = 0.02f,
                    canCrit = true, element = element, tags = tags, depth = depth + 1, fromAbility = true,
                };
                Combat.Hit(a, d, col, "hit_light", 0.05f, 1);
                Combat.ApplyElement(a, element, d.amount, owner, depth + 1);
            }
        }

        protected override void Update()
        {
            base.Update();
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            life -= dt;
            float fade = Mathf.Clamp01(life / 0.3f);
            var c = tint; c.a *= fade * (0.85f + Mathf.Sin(Time.time * 20) * 0.15f);
            body.color = c;
            if (weapon) weapon.color = c;
            if (phantom) visual.localRotation = Quaternion.Euler(0, 0, Time.time * 720f);
            if (life <= 0)
            {
                GameEvents.Raise(new CombatEvent { type = Trig.CloneExpire, source = owner, position = Pos, tags = "CLONE", depth = depth });
                Fx.I?.Burst(Pos + Vector2.up * 0.5f, (Color32)tint, 8, 3, 0.35f);
                Destroy(gameObject);
                return;
            }
            // 자동 공격 (Copy가 아니거나 환영 무기)
            if (!copy || phantom)
            {
                atkT -= dt;
                var t = Actor.Nearest(Pos, phantom ? 5f : 4f, Team.Enemy);
                if (t != null)
                {
                    var to = t.Pos - Pos;
                    if (phantom || to.magnitude > 1.1f) Pos = WorldStreamer.I.Move(Pos, to.normalized * (phantom ? 9f : 6f) * dt, 0.3f);
                    if (atkT <= 0 && to.magnitude < 1.4f)
                    {
                        atkT = phantom ? 0.3f : 0.45f;
                        Strike(to.normalized, 1.5f, phantom ? 360 : 150, 0.8f);
                    }
                }
                body.flipX = Facing.x < 0;
            }
        }

        protected override void Die(DamageInfo killer) => Destroy(gameObject);
        public override float TakeDamage(DamageInfo d) => 0;
    }

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
        SpriteRenderer sr;
        readonly Dictionary<Actor, float> zoneHits = new Dictionary<Actor, float>();

        static Summon Make(string name, Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var s = go.AddComponent<Summon>();
            s.owner = p; s.life = life; s.power = power; s.element = el; s.tags = tags;
            s.col = el != Element.None ? Combat.ElementColor(el) : Pal.Hex("bfe3ff");
            return s;
        }

        public static void Turret(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Turret", p, pos, life, power, el, tags);
            s.kind = Kind.Turret;
            s.sr = Art.MakeRenderer(s.gameObject, Art.Orb(Pal.White, s.col, 12), 0, true);
        }

        public static void Orb(Player p, float life, float power, Element el, int index, int total, string tags)
        {
            var s = Make("Orb", p, p.Pos, life, power, el, tags);
            s.kind = Kind.Orb; s.index = index; s.total = Mathf.Max(1, total);
            s.sr = Art.MakeRenderer(s.gameObject, Art.Orb(Pal.White, s.col, 10), 0, true);
        }

        public static void Zone(Player p, Vector2 pos, float life, float power, Element el, string tags)
        {
            var s = Make("Zone", p, pos, life, power, el, tags);
            s.kind = Kind.Zone;
            s.sr = Art.MakeRenderer(s.gameObject, Art.WarningCircle(28), -29000, true);
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
