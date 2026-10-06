using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum Team { Player, Enemy, Neutral }

    public struct DamageInfo
    {
        public float amount;
        public Actor source;
        public Vector2 direction;
        public float knockback;
        public float hitstop;
        public bool crit;
        public bool canCrit;
        public Element element;
        public string tags;
        public int depth;              // ability-chain depth
        public bool fromAbility;
        public bool noEvents;          // DoTs, etc.: do not fire Hit events
        public Color32? fxColor;

        public static DamageInfo Basic(float amount, Actor src, Vector2 dir) =>
            new DamageInfo { amount = amount, source = src, direction = dir, knockback = 2, hitstop = 0.04f, canCrit = true, tags = "" };
    }

    public class StatusEffect
    {
        public string id;
        public float time, tick, dps, slow;
        public Actor source;
        public Color32 color;
    }

    /// <summary>
    /// Common base for every combatant (player, enemy, clone).
    /// Handles position, health, knockback, hit flash, status effects, and sprite rendering.
    /// Because it uses a tile grid + direct movement instead of Rigidbody, the game is deterministic even with lots of enemies.
    /// </summary>
    public abstract class Actor : MonoBehaviour
    {
        public static readonly List<Actor> All = new List<Actor>();

        public Team team;
        public float radius = 0.4f;
        public float mass = 1f;
        public float hp, maxHp;
        public bool Alive => hp > 0 && gameObject.activeSelf;
        public Vector2 Pos { get => transform.position; set => transform.position = new Vector3(value.x, value.y, 0); }
        public Vector2 Facing = Vector2.right;
        public float invulnUntil;
        public readonly List<StatusEffect> statuses = new List<StatusEffect>();

        // Prefab hierarchy (Prefabs/Actors): root → visual → body, root → shadow
        [SerializeField] protected SpriteRenderer body;
        [SerializeField] protected SpriteRenderer shadow;
        [SerializeField] protected Transform visual;         // parent of body/weapon (for scale/rotation effects)
        protected Vector2 knockVel;
        protected float flash;
        protected float squash;             // >0 = flatten vertically, <0 = stretch vertically
        protected float hitShakeT;
        MaterialPropertyBlock mpb;

        protected virtual void Awake()
        {
            All.Add(this);
            mpb = new MaterialPropertyBlock();
        }

        protected virtual void OnDestroy() => All.Remove(this);

        public void SetShadowSize(int px)
        {
            shadow.sprite = Art.Shadow(px);
        }

        public virtual float Slow
        {
            get
            {
                float s = 0;
                foreach (var st in statuses) s = Mathf.Max(s, st.slow);
                return s;
            }
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) { Render(); return; }

            // Knockback (decays like friction)
            if (knockVel.sqrMagnitude > 0.0004f)
            {
                var w = WorldStreamer.I;
                Pos = w != null ? w.Move(Pos, knockVel * dt, radius, CanSwim, MoveBound) : Pos + knockVel * dt;
                knockVel *= Mathf.Exp(-10f * dt);
            }

            TickStatuses(dt);
            flash = Mathf.Max(0, flash - dt * 8f);
            squash = Mathf.MoveTowards(squash, 0, dt * 4f);
            hitShakeT = Mathf.Max(0, hitShakeT - Time.unscaledDeltaTime);
            Render();
        }

        void TickStatuses(float dt)
        {
            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                var s = statuses[i];
                s.time -= dt;
                if (s.dps > 0)
                {
                    s.tick -= dt;
                    if (s.tick <= 0)
                    {
                        s.tick = 0.5f;
                        var d = new DamageInfo { amount = s.dps * 0.5f, source = s.source, direction = Vector2.zero, noEvents = true, fxColor = s.color, tags = "STATUS" };
                        TakeDamage(d);
                        if (!Alive) return;
                    }
                }
                if (s.time <= 0) statuses.RemoveAt(i);
            }
        }

        public void AddStatus(string id, float time, float dps, float slow, Actor src, Color32 col)
        {
            foreach (var s in statuses)
                if (s.id == id) { s.time = Mathf.Max(s.time, time); s.dps = Mathf.Max(s.dps, dps); s.slow = Mathf.Max(s.slow, slow); return; }
            statuses.Add(new StatusEffect { id = id, time = time, dps = dps, slow = slow, source = src, color = col, tick = 0.5f });
            GameEvents.Raise(new CombatEvent { type = Trig.StatusApplied, source = src, target = this, position = Pos, tags = id.ToUpper() });
        }

        public bool HasStatus(string id) { foreach (var s in statuses) if (s.id == id) return true; return false; }

        protected virtual void Render()
        {
            // Sort by y (lower on screen draws in front)
            int order = Art.SortY(Pos.y);
            body.sortingOrder = order;
            shadow.sortingOrder = order - 2;

            // Hit flash
            body.GetPropertyBlock(mpb);
            mpb.SetFloat("_Flash", Mathf.Clamp01(flash));
            mpb.SetColor("_FlashColor", Color.white);
            body.SetPropertyBlock(mpb);

            // Squash/stretch
            float sx = 1 + squash * 0.25f, sy = 1 - squash * 0.25f;
            visual.localScale = new Vector3(sx, sy, 1);

            // Shudder in place on hit (works even during hitstop)
            if (hitShakeT > 0)
            {
                float k = hitShakeT * 10f;
                visual.localPosition = new Vector3(Mathf.Sin(Time.unscaledTime * 90f) * 0.06f * k, 0, 0);
            }
            else visual.localPosition = Vector3.zero;

            // Status color
            Color tint = Color.white;
            foreach (var s in statuses)
            {
                if (s.id == "burn") tint = Color.Lerp(tint, new Color(1f, 0.7f, 0.55f), 0.5f);
                else if (s.id == "chill" || s.id == "freeze") tint = Color.Lerp(tint, new Color(0.6f, 0.85f, 1f), 0.6f);
                else if (s.id == "poison") tint = Color.Lerp(tint, new Color(0.7f, 1f, 0.6f), 0.5f);
                else if (s.id == "mark") tint = Color.Lerp(tint, new Color(1f, 0.7f, 0.9f), 0.3f);
            }
            body.color = tint * BaseTint;
        }

        protected virtual bool CanSwim => false;
        protected virtual float MoveBound => 0;

        /// <summary>Permanent color (e.g. elites).</summary>
        protected virtual Color BaseTint => Color.white;

        public virtual void Knockback(Vector2 dir, float force)
        {
            knockVel += dir.normalized * force / Mathf.Max(0.3f, mass);
        }

        /// <summary>Apply damage. Returns the final damage dealt.</summary>
        public virtual float TakeDamage(DamageInfo d)
        {
            if (!Alive) return 0;
            if (Time.time < invulnUntil && !d.noEvents) return 0;
            float amount = d.amount;
            hp -= amount;
            flash = 1f;
            if (!d.noEvents)
            {
                squash = 0.6f;
                hitShakeT = Mathf.Max(hitShakeT, d.hitstop + 0.06f);
                if (d.knockback > 0) Knockback(d.direction, d.knockback);
            }
            OnDamaged(d, amount);
            if (hp <= 0) { hp = 0; Die(d); }
            return amount;
        }

        protected virtual void OnDamaged(DamageInfo d, float amount) { }
        protected abstract void Die(DamageInfo killer);

        public void Heal(float amount)
        {
            if (!Alive || amount <= 0) return;
            float before = hp;
            hp = Mathf.Min(maxHp, hp + amount);
            if (hp - before >= 1 && Fx.I != null && Game.Settings.damageNumbers) Fx.I.Number(Pos + Vector2.up * 0.4f, "+" + Mathf.RoundToInt(hp - before), new Color(0.5f, 1f, 0.55f), 0.9f);
        }

        public static IEnumerable<Actor> InRadius(Vector2 c, float r, Team? team = null)
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                if (i >= All.Count) continue;
                var a = All[i];
                if (a == null || !a.Alive) continue;
                if (team.HasValue && a.team != team.Value) continue;
                if ((a.Pos - c).sqrMagnitude <= (r + a.radius) * (r + a.radius)) yield return a;
            }
        }

        public static Actor Nearest(Vector2 c, float r, Team team, Actor exclude = null)
        {
            Actor best = null; float bd = r * r;
            foreach (var a in All)
            {
                if (a == null || !a.Alive || a.team != team || a == exclude) continue;
                float d = (a.Pos - c).sqrMagnitude;
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }
    }
}
