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
        [SerializeField] SpriteRenderer weapon;   // prefab child (visual/weapon); hidden for the phantom weapon
        Color tint;
        public static readonly List<Clone> Active = new List<Clone>();

        public static Clone Spawn(Player p, Vector2 pos, float duration, float power, bool copy, bool phantom, Element el, string tags, int depth)
        {
            var c = Instantiate(DB.Asset.clonePrefab);
            c.name = phantom ? "PhantomWeapon" : "Clone";
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
                c.weapon.sprite = Art.Weapon(p.Weapon.kind);
                c.weapon.color = c.tint;
                c.weapon.transform.localPosition = Art.HandAnchor;
            }
            else
            {
                c.weapon.gameObject.SetActive(false);
                c.weapon = null;
                c.visual.localRotation = Quaternion.Euler(0, 0, 45);
            }
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
}
