using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 투사체 (볼트, 단검, 마법구, 적 탄환). 관통·유도·폭발·회수 등 옵션 조합으로 표현.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public Team targetTeam;
        public Actor owner;
        public Vector2 vel;
        public float life = 1.5f;
        public float radius = 0.25f;
        public int pierce;
        public float homing;            // 유도 강도
        public float explodeRadius;
        public DamageInfo dmg;
        public Color32 color = new Color32(0xff, 0xf8, 0xef, 0xff);   // Pal.White
        public string sfx = "hit";
        public float shake = 0.08f, kick = 1.5f;
        public bool trail = true;
        public bool spin;
        public System.Action<Projectile, Actor> onHit;
        public System.Action<Projectile, float> onUpdate;    // steering (curving shots); gets the age in seconds
        public System.Action<Projectile> onEnd;
        readonly HashSet<Actor> hitSet = new HashSet<Actor>();
        public static readonly List<Projectile> Live = new List<Projectile>();
        void Awake() => Live.Add(this);
        void OnDestroy() => Live.Remove(this);

        /// <summary>Stopped by a barrier.</summary>
        public void Block()
        {
            Fx.I?.Burst(transform.position, color, 6, 4, 0.25f);
            End();
        }
        [SerializeField] SpriteRenderer sr;
        float trailT;
        float t;

        public static Projectile Spawn(Sprite s, Vector2 pos, Vector2 vel, Team target, Actor owner, DamageInfo dmg, bool additive = false)
        {
            var p = Instantiate(DB.Asset.projectilePrefab, pos, Quaternion.Euler(0, 0, vel.Angle()));
            Art.Setup(p.sr, s, 0, additive);
            p.vel = vel;
            p.targetTeam = target;
            p.owner = owner;
            p.dmg = dmg;
            return p;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            t += dt;
            if (t >= life) { End(); return; }

            onUpdate?.Invoke(this, t);
            if (homing > 0)
            {
                var tgt = Actor.Nearest(transform.position, 6f, targetTeam);
                if (tgt != null)
                {
                    var want = ((Vector2)tgt.Pos + Vector2.up * 0.3f - (Vector2)transform.position).normalized * vel.magnitude;
                    vel = Vector2.Lerp(vel, want, 1 - Mathf.Exp(-homing * dt));
                }
            }

            Vector2 p = (Vector2)transform.position + vel * dt;
            if (WorldStreamer.I != null && WorldStreamer.I.IsSolidObstacle(p))
            {
                // 나무·벽에 박힘 (물 위로는 날아간다)
                Fx.I?.Burst(p, color, 4, 4, 0.25f);
                End();
                return;
            }
            transform.position = p;
            transform.rotation = spin ? Quaternion.Euler(0, 0, t * 900f) : Quaternion.Euler(0, 0, vel.Angle());
            sr.sortingOrder = Art.SortY(p.y - 0.5f) + 10;

            if (trail)
            {
                trailT -= dt;
                if (trailT <= 0) { trailT = 0.025f; Fx.I?.Burst(p, color, 1, 0.5f, 0.2f, 0, 1); }
            }

            foreach (var a in Actor.All)
            {
                if (a == null || !a.Alive || a.team != targetTeam || hitSet.Contains(a)) continue;
                if (((Vector2)a.Pos + Vector2.up * 0.35f - p).sqrMagnitude > (radius + a.radius) * (radius + a.radius)) continue;
                hitSet.Add(a);
                var d = dmg;
                d.direction = vel.normalized;
                if (owner != null && owner.team == Team.Player) Combat.Hit(a, d, color, sfx, shake, kick);
                else
                {
                    a.TakeDamage(d);
                }
                if (d.element != Element.None) Combat.ApplyElement(a, d.element, d.amount, owner, d.depth);
                onHit?.Invoke(this, a);
                if (explodeRadius > 0) { Explode(); return; }
                if (pierce-- <= 0) { End(); return; }
            }
        }

        void Explode()
        {
            Vector2 p = transform.position;
            Fx.I?.Play(Art.Shockwave(color, Mathf.RoundToInt(explodeRadius * Art.PPU)), p, 0, 24, 1, null, true);
            Fx.I?.Burst(p, color, 14, 8, 0.4f);
            Sfx.Play("explode", 0.6f);
            foreach (var a in new List<Actor>(Actor.InRadius(p, explodeRadius, targetTeam)))
            {
                if (hitSet.Contains(a)) continue;
                var d = dmg; d.direction = a.Pos - p; d.amount *= 0.7f;
                if (owner != null && owner.team == Team.Player) Combat.Hit(a, d, color, "hit", shake, kick);
                else a.TakeDamage(d);
            }
            End();
        }

        void End()
        {
            onEnd?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
