using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Owned, placed enemy-projectile blocker; no autonomous attack or terrain collision.</summary>
    public class ActiveBarrier : MonoBehaviour
    {
        [SerializeField] SpriteRenderer body;
        static readonly List<ActiveBarrier> live = new List<ActiveBarrier>();
        Vector2 from, to;
        float expires, width;
        Func<bool> valid;
        ActiveSkillData skill;
        public Player Owner { get; private set; }
        public bool Active => enabled && Time.time < expires && Owner != null && Owner.Alive && valid != null && valid();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry() => live.Clear();
        void OnEnable() => live.Add(this);
        void OnDisable() => live.Remove(this);
        public static ActiveBarrier Spawn(Player owner, ActiveSkillData skill, Vector2 from, Vector2 to, Func<bool> valid)
        {
            var barrier = Instantiate(DB.Asset.activeBarrierPrefab, (from + to) * .5f, Quaternion.identity);
            barrier.Owner = owner; barrier.skill = skill;
            barrier.from = from; barrier.to = to; barrier.valid = valid;
            barrier.expires = Time.time + ActiveSkillGenerator.Rules.effectDuration;
            barrier.width = ActiveSkillGenerator.Rules.anchorWidth;
            var library=CombatVisuals.Library;
            Vector2 axis=to-from;
            int orientation=Mathf.Abs(axis.y)<Mathf.Abs(axis.x)*.5f?0:Mathf.Abs(axis.x)<Mathf.Abs(axis.y)*.5f?1:axis.x*axis.y>=0?2:3;
            var sprite=library!=null && library.barriers.Length>orientation?library.barriers[orientation]:barrier.body.sprite;
            float reference=library!=null?library.barrierReferenceLengths[orientation]:64;
            barrier.body.transform.localScale=Vector3.one*(axis.magnitude*Art.PPU/Mathf.Max(1,reference));
            Art.Setup(barrier.body,sprite,Art.SortY(Mathf.Min(from.y,to.y)),false);
            float authoredAngle = orientation switch { 1 => 90, 2 => 45, 3 => -45, _ => 0 };
            float correction = Mathf.Repeat(axis.Angle() - authoredAngle + 90, 180) - 90;
            barrier.body.transform.localRotation = Quaternion.Euler(0, 0, correction);
            return barrier;
        }
        void Update() { if (!Active) Dismiss(); }
        public void Dismiss() { enabled = false; Destroy(gameObject); }
        public static bool TryBlock(Projectile shot, Vector2 start, Vector2 end)
        {
            if (shot.targetTeam != Team.Player) return false;
            foreach (var barrier in live)
            {
                if (barrier == null || !barrier.Active) continue;
                float radius = barrier.width + shot.radius;
                if (SegmentDistanceSquared(start, end, barrier.from, barrier.to) > radius * radius) continue;
                Vector2 ab = barrier.to - barrier.from;
                Vector2 contact = barrier.from + ab * Mathf.Clamp01(Vector2.Dot(start - barrier.from, ab) / Mathf.Max(.0001f, ab.sqrMagnitude));
                Fx.I?.Play(Art.HitSpark(ActiveSkillStyle.Tone(barrier.skill,.7f), 6), contact, shot.vel.Angle(), 24, 1, null, false);
                Sfx.Play("guard", .43f);
                shot.Block(false); return true;
            }
            return false;
        }
        static float PointDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return (p - a - d * Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(.0001f, d.sqrMagnitude))).sqrMagnitude;
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 ab = b - a, cd = d - c; float determinant = Cross(ab, cd);
            if (Mathf.Abs(determinant) > .00001f)
            {
                float t = Cross(c - a, cd) / determinant, u = Cross(c - a, ab) / determinant;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return 0;
            }
            return Mathf.Min(Mathf.Min(PointDistanceSquared(a,c,d),PointDistanceSquared(b,c,d)),
                Mathf.Min(PointDistanceSquared(c,a,b),PointDistanceSquared(d,a,b)));
        }
    }
}
