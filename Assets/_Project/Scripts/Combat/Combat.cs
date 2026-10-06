using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 공격 판정·피해 계산·타격감 연출을 한 곳에 모은 유틸.
    /// 플레이어 기본공격, 스킬, AI 생성 능력이 모두 이 함수를 거친다 → 연출이 일관된다.
    /// </summary>
    public static class Combat
    {
        static readonly List<Actor> buffer = new List<Actor>();

        /// <summary>부채꼴 범위 판정. arc 360이면 원형.</summary>
        public static List<Actor> Arc(Vector2 origin, Vector2 dir, float range, float arcDeg, Team targetTeam)
        {
            buffer.Clear();
            float half = arcDeg * 0.5f;
            foreach (var a in Actor.All)
            {
                if (a == null || !a.Alive || a.team != targetTeam) continue;
                Vector2 to = a.Pos + Vector2.up * 0.3f - origin;
                float d = to.magnitude;
                if (d > range + a.radius) continue;
                if (arcDeg < 359 && d > 0.3f && Vector2.Angle(dir, to) > half + Mathf.Rad2Deg * Mathf.Atan2(a.radius, Mathf.Max(0.1f, d))) continue;
                buffer.Add(a);
            }
            return buffer;
        }

        /// <summary>직선(캡슐) 판정.</summary>
        public static List<Actor> Line(Vector2 a, Vector2 b, float width, Team targetTeam)
        {
            buffer.Clear();
            foreach (var x in Actor.All)
            {
                if (x == null || !x.Alive || x.team != targetTeam) continue;
                float d = DistToSegment(x.Pos + Vector2.up * 0.3f, a, b);
                if (d <= width + x.radius) buffer.Add(x);
            }
            return buffer;
        }

        public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-5f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// 플레이어 측 공격의 최종 피해 계산 + 치명타 + 연출 + 이벤트 발행.
        /// </summary>
        public static float Hit(Actor target, DamageInfo d, Color32 fxColor, string sfx = "hit", float shake = 0.12f, float kick = 2f)
        {
            if (target == null || !target.Alive) return 0;
            var p = Player.I;
            bool fromPlayerSide = d.source != null && d.source.team == Team.Player;
            if (fromPlayerSide && p != null)
            {
                d.amount *= p.DamageMultiplierAgainst(target, d.tags);
                if (d.canCrit && Random.value < p.Stats[StatId.CritChance] + p.BonusCritAgainst(target))
                {
                    d.crit = true;
                    d.amount *= 1.5f + p.Stats[StatId.CritDamage];
                }
                if (d.element != Element.None) d.amount *= 1 + p.Stats[StatId.ElementPower] * 0.5f;
            }
            d.amount = Mathf.Max(1, d.amount * Random.Range(0.92f, 1.08f));
            float dealt = target.TakeDamage(d);
            if (dealt <= 0) return 0;

            Vector2 at = target.Pos + Vector2.up * 0.45f;
            // 연출 — 무기별 값으로 차등
            float hs = d.hitstop * (d.crit ? 1.5f : 1f);
            if (!d.noEvents)
            {
                GameTime.Hitstop(hs);
                if (CameraRig.I != null && Game.Settings.screenShake)
                {
                    float s = Game.Settings.shakeScale;
                    CameraRig.I.Shake(shake * (d.crit ? 1.4f : 1f) * s);
                    CameraRig.I.Kick(d.direction, kick * (d.crit ? 1.5f : 1f) * s);
                    if (d.crit) CameraRig.I.Punch(0.5f);
                }
                Fx.I?.HitSpark(at, d.direction.SafeNormal(Vector2.right), d.fxColor ?? fxColor, d.crit, d.crit ? 1.3f : 1f);
                Sfx.Play(d.crit ? "crit" : sfx, d.crit ? 0.9f : 0.7f);
            }
            if (Game.Settings.damageNumbers && Fx.I != null)
            {
                var col = d.crit ? new Color(1f, 0.85f, 0.3f) : (target.team == Team.Player ? new Color(1f, 0.4f, 0.45f) : Color.white);
                if (d.noEvents && d.fxColor.HasValue) col = d.fxColor.Value;
                Fx.I.Number(at + Vector2.up * 0.2f, Mathf.RoundToInt(dealt) + (d.crit ? "!" : ""), col, d.crit ? 1.35f : 1f);
            }

            if (fromPlayerSide && !d.noEvents)
            {
                var ev = new CombatEvent { type = Trig.Hit, source = d.source, target = target, position = target.Pos, direction = d.direction, amount = dealt, tags = d.tags ?? "", depth = d.depth };
                GameEvents.Raise(ev);
                if (d.crit) { ev.type = Trig.Crit; GameEvents.Raise(ev); }
                if (!target.Alive) { ev.type = Trig.Kill; GameEvents.Raise(ev); }
            }
            return dealt;
        }

        public static Color32 ElementColor(Element e) => e switch
        {
            Element.Fire => Pal.Fire,
            Element.Frost => Pal.Frost,
            Element.Lightning => Pal.Lightning,
            Element.Shadow => Pal.ShadowEl,
            Element.Holy => Pal.Holy,
            Element.Poison => Pal.Hex("b6f06b"),
            Element.Wind => Pal.Wind,
            Element.Blood => Pal.Blood,
            Element.Void => Pal.Hex("6b4fa8"),
            Element.Arcane => Pal.Hex("ff9cf2"),
            _ => Pal.White,
        };

        /// <summary>원소 부가 효과 (화상, 둔화, 연쇄 번개 등).</summary>
        public static void ApplyElement(Actor target, Element e, float power, Actor src, int depth)
        {
            if (target == null || !target.Alive || e == Element.None) return;
            switch (e)
            {
                case Element.Fire: target.AddStatus("burn", 3f, power * 0.35f, 0, src, Pal.Fire); break;
                case Element.Frost: target.AddStatus("chill", 2f, 0, 0.45f, src, Pal.Frost); break;
                case Element.Poison: target.AddStatus("poison", 4f, power * 0.3f, 0.1f, src, Pal.Hex("b6f06b")); break;
                case Element.Shadow: target.AddStatus("mark", 4f, 0, 0, src, Pal.ShadowEl); break;
                case Element.Blood: target.AddStatus("bleed", 3f, power * 0.3f, 0, src, Pal.Blood); break;
                case Element.Void: target.AddStatus("weaken", 3f, 0, 0, src, Pal.Hex("6b4fa8")); break;
                case Element.Wind: target.Knockback(target.Pos - (src != null ? src.Pos : target.Pos), 3f); break;
                case Element.Lightning:
                    if (depth < 2)
                    {
                        var next = Actor.Nearest(target.Pos, 3.5f, target.team, target);
                        if (next != null)
                        {
                            Fx.I?.Play(Art.LightningBolt(48), next.Pos, 0, 24, 0.6f, null, true);
                            var d = new DamageInfo { amount = power * 0.5f, source = src, direction = next.Pos - target.Pos, knockback = 0.5f, hitstop = 0.01f, element = Element.None, tags = "LIGHTNING", depth = depth + 1, fromAbility = true, canCrit = false };
                            Hit(next, d, Pal.Lightning, "hit_light", 0.05f, 1);
                        }
                    }
                    break;
            }
        }
    }
}
