using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public class SkillDef
    {
        public string id, name, desc;
        public float cooldown, lockTime;
        public Color32 color;
        public string icon;        // icon glyph
    }

    public class SkillState
    {
        public string id;
        public SkillDef def;
        public float cd;
    }

    /// <summary>
    /// Weapon-specific QWER skills. Each skill sets its own hit-feel (hitstop/shake/kick) values.
    /// </summary>
    public static class SkillDB
    {
        static readonly Dictionary<string, SkillDef> defs = new Dictionary<string, SkillDef>();

        static SkillDB()
        {
            void D(string id, string name, string desc, float cd, float lockT, string col, string icon) =>
                defs[id] = new SkillDef { id = id, name = name, desc = desc, cooldown = cd, lockTime = lockT, color = Pal.Hex(col), icon = icon };

            // Greatsword
            D("gs_leap", "분쇄 도약", "목표 지점으로 뛰어올라 땅을 내려찍는다. 착지 지점의 적을 기절시킨다.", 7, 0.55f, "ffd27a", "▼");
            D("gs_whirl", "회오리 베기", "빙글빙글 돌며 주변의 모든 것을 벤다.", 9, 1.1f, "ffd27a", "◎");
            D("gs_rush", "어깨 돌진", "앞으로 돌진하며 어깨로 적을 밀쳐 낸다.", 6, 0.35f, "ffd27a", "»");
            D("gs_titan", "거인의 일격", "힘을 모아 거대한 칼날을 내리친다.", 22, 1.0f, "ffb35a", "★");
            // Sword and shield
            D("ss_bash", "방패 강타", "방패로 후려쳐 적을 기절시킨다.", 5, 0.3f, "bfe3ff", "■");
            D("ss_parry", "방어 태세", "방패를 든다. 정면 공격을 막고 반격한다.", 4, 0.0f, "bfe3ff", "▣");
            D("ss_leap", "도약 강타", "적에게 뛰어들어 내려친다.", 8, 0.45f, "bfe3ff", "▲");
            D("ss_sanctum", "성역", "성스러운 빛의 영역을 만든다. 자신은 회복하고 적은 불태운다.", 24, 0.5f, "fff2b3", "✚");
            // Crossbow
            D("cb_pierce", "관통 볼트", "힘을 모아 강력한 관통 볼트를 쏜다.", 6, 0.45f, "fff1b0", "→");
            D("cb_scatter", "확산 사격", "전방에 부채꼴로 볼트를 쏜다.", 7, 0.3f, "fff1b0", "⋰");
            D("cb_backstep", "후퇴 사격", "뒤로 뛰어오르며 사격한다.", 6, 0.2f, "fff1b0", "«");
            D("cb_rain", "화살비", "목표 지역에 화살을 퍼붓는다.", 20, 0.4f, "ffcf6b", "☂");
            // Staff
            D("st_thrust", "번개 찌르기", "번개를 머금은 찌르기. 맞은 적에게서 연쇄된다.", 5, 0.3f, "c9b8ff", "↯");
            D("st_cyclone", "사이클론", "지팡이를 돌려 적을 끌어당긴다.", 9, 0.9f, "c9b8ff", "@");
            D("st_vault", "장대 도약", "지팡이를 짚고 뛰어넘는다. 착지 지점에 번개를 남긴다.", 7, 0.3f, "c9b8ff", "⤴");
            D("st_thunder", "천둥소리", "주변에 번개 폭풍을 불러온다.", 22, 0.6f, "fff36b", "ϟ");
            // Dagger
            D("dg_fan", "칼날 부채", "사방으로 단검을 던진다.", 5, 0.2f, "ffb3d1", "✱");
            D("dg_shadowstep", "그림자 걸음", "적의 등 뒤로 순간이동해 찌른다.", 7, 0.2f, "b18cff", "◐");
            D("dg_venom", "맹독 바르기", "칼날에 독을 바른다. 기본 공격이 중독을 건다.", 12, 0.1f, "b6f06b", "☠");
            D("dg_frenzy", "칼날 광란", "잔상을 남기며 수없이 벤다.", 22, 1.0f, "ffb3d1", "✦");
            // Katana
            D("kt_iai", "발도", "일직선으로 내달리며 길 위의 적을 벤다.", 6, 0.35f, "d4f3ff", "─");
            D("kt_crescent", "초승달 베기", "초승달 모양의 검기를 날린다.", 6, 0.3f, "d4f3ff", "☾");
            D("kt_counter", "반격 자세", "자세를 잡는다. 공격받으면 즉시 반격한다.", 8, 0.0f, "d4f3ff", "⚔");
            D("kt_flash", "천 번 베기", "순식간에 공간이 갈라진다. 범위 안의 모든 적을 찢는다.", 24, 0.9f, "e6f8ff", "✧");
        }

        public static SkillDef Get(string id) => defs.TryGetValue(id, out var d) ? d : null;

        static DamageInfo Dmg(Player p, float mult, Vector2 dir, float kb, float hs, Element el = Element.None) => new DamageInfo
        {
            amount = p.AttackPower * mult, source = p, direction = dir, knockback = kb, hitstop = hs, canCrit = true,
            tags = p.Weapon.tags + " SKILL", element = el,
        };

        static void HitAll(Player p, IEnumerable<Actor> targets, float mult, Vector2 from, float kb, float hs, Color32 col, string sfx, float shake, float kick, Element el = Element.None)
        {
            foreach (var a in new List<Actor>(targets))
            {
                var d = Dmg(p, mult, (a.Pos - from).SafeNormal(p.Facing), kb, hs, el);
                Combat.Hit(a, d, col, sfx, shake, kick);
                if (el != Element.None) Combat.ApplyElement(a, el, d.amount, p, 0);
            }
        }

        static void Stun(IEnumerable<Actor> targets, float t)
        {
            foreach (var a in new List<Actor>(targets)) if (a is Enemy e) e.Stun(t);
        }

        public static void Cast(Player p, SkillDef s, Vector2 dir, Vector2 aim)
        {
            p.StartCoroutine(Run(p, s, dir, aim));
        }

        static IEnumerator Run(Player p, SkillDef s, Vector2 dir, Vector2 aim)
        {
            var w = p.Weapon;
            Vector2 o = p.Pos + Vector2.up * 0.45f;
            Color32 col = s.color;
            switch (s.id)
            {
                // ───────── Greatsword ─────────
                case "gs_leap":
                {
                    Vector2 target = p.Pos + Vector2.ClampMagnitude(aim - p.Pos, 5f);
                    target = WorldStreamer.I.NearestWalkable(target, p.radius);
                    Sfx.Play("swing_heavy", 0.6f, 0.8f);
                    Vector2 start = p.Pos;
                    float t = 0, dur = 0.38f;
                    p.invulnUntil = Time.time + dur + 0.1f;
                    while (t < dur)
                    {
                        t += Time.deltaTime;
                        float k = t / dur;
                        p.Pos = Vector2.Lerp(start, target, MathX.EaseOutCubic(k));
                        p.SetSwing(dir.Angle() + 120, dir.Angle() + 100, 0.01f);
                        yield return null;
                    }
                    p.SetSwing(dir.Angle() + 120, dir.Angle() - 60, 0.1f);
                    Slam(p, p.Pos, 2.6f, 2.0f, col, 0.6f, 9, 0.16f);
                    Stun(Actor.InRadius(p.Pos, 2.6f, Team.Enemy), 1.0f);
                    break;
                }
                case "gs_whirl":
                {
                    float dur = 1.1f, tick = 0;
                    Sfx.Play("swing_heavy", 0.7f);
                    for (float t = 0; t < dur; t += Time.deltaTime)
                    {
                        tick -= Time.deltaTime;
                        p.SetSwing(t * 1300, t * 1300 + 10, 0.01f);
                        p.Pos = WorldStreamer.I.Move(p.Pos, (p.AimPoint - p.Pos).SafeNormal(Vector2.zero) * 2.2f * Time.deltaTime, p.radius);
                        if (tick <= 0)
                        {
                            tick = 0.18f;
                            Fx.I?.Play(Art.Slash(w.slashColor, Pal.White, 34, 359, 1), p.Pos + Vector2.up * 0.45f, t * 1300, 36, 1.2f, null, true);
                            HitAll(p, Actor.InRadius(p.Pos, 2.3f, Team.Enemy), 0.55f, p.Pos, 2.5f, 0.03f, w.slashColor, "hit_heavy", 0.12f, 1.5f);
                            Sfx.Play("swing", 0.4f, 0.8f);
                        }
                        yield return null;
                    }
                    break;
                }
                case "gs_rush":
                {
                    p.SetDash(dir * 14f, 0.28f, true);
                    Sfx.Play("dash", 0.7f, 0.7f);
                    var hit = new HashSet<Actor>();
                    for (float t = 0; t < 0.28f; t += Time.deltaTime)
                    {
                        foreach (var a in new List<Actor>(Actor.InRadius(p.Pos + dir * 0.4f, 0.9f, Team.Enemy)))
                        {
                            if (hit.Contains(a)) continue;
                            hit.Add(a);
                            var d = Dmg(p, 1.3f, dir, 9, 0.09f);
                            Combat.Hit(a, d, w.slashColor, "hit_heavy", 0.35f, 6);
                            if (a is Enemy e) e.Stun(0.6f);
                        }
                        Fx.I?.Burst(p.Pos, Pal.Hex("cdbba5"), 2, 3, 0.3f, 0, 2, 50, (-dir).Angle(), true);
                        yield return null;
                    }
                    break;
                }
                case "gs_titan":
                {
                    Sfx.Play("charge", 0.8f, 0.7f);
                    for (float t = 0; t < 0.55f; t += Time.deltaTime)
                    {
                        p.SetSwing(dir.Angle() + 150, dir.Angle() + 150, 0.01f);
                        if (Random.value < 0.6f) Fx.I?.Burst(p.Pos + Random.insideUnitCircle * 1.5f, Pal.Gold, 1, -3, 0.3f, 0, 1);
                        CameraRig.I?.Shake(0.05f);
                        yield return null;
                    }
                    p.SetSwing(dir.Angle() + 150, dir.Angle() - 40, 0.08f);
                    Vector2 at = p.Pos + dir * 2.2f;
                    Fx.I?.Play(Art.Slash(Pal.Hex("ffb35a"), Pal.White, 58, 120, 1), o, dir.Angle(), 28, 1.4f, null, true);
                    Slam(p, at, 3.4f, 4.2f, Pal.Hex("ffb35a"), 0.85f, 14, 0.22f);
                    for (int i = 1; i <= 3; i++)
                    {
                        yield return new WaitForSeconds(0.1f);
                        Vector2 c = at + dir * i * 1.4f;
                        Fx.I?.Play(Art.Shockwave(Pal.Hex("ffd27a"), 18), c, 0, 26, 1, null, true);
                        HitAll(p, Actor.InRadius(c, 1.4f, Team.Enemy), 1.0f, c, 4, 0.05f, Pal.Hex("ffd27a"), "hit", 0.2f, 3);
                    }
                    break;
                }

                // ───────── Sword & shield ─────────
                case "ss_bash":
                {
                    p.SetLunge(dir * 10f, 0.12f);
                    yield return new WaitForSeconds(0.06f);
                    Fx.I?.Play(Art.Shockwave(Pal.Hex("bfe3ff"), 14), o + dir * 0.8f, 0, 28, 1, null, true);
                    var targets = Combat.Arc(o, dir, 1.6f, 90, Team.Enemy);
                    var list = new List<Actor>(targets);
                    HitAll(p, list, 1.1f, p.Pos, 7, 0.11f, Pal.Hex("bfe3ff"), "hit_metal", 0.3f, 5);
                    Stun(list, 1.4f);
                    break;
                }
                case "ss_parry":
                {
                    p.guarding = true;
                    Sfx.Play("guard", 0.4f, 1.2f);
                    float t = 0;
                    while (t < 1.2f && (Controls.Held(Act.SkillW) || t < 0.4f))
                    {
                        t += Time.deltaTime;
                        p.Facing = (p.AimPoint - p.Pos).SafeNormal(p.Facing);
                        p.StopMoving();
                        yield return null;
                    }
                    p.guarding = false;
                    break;
                }
                case "ss_leap":
                {
                    var target = Actor.Nearest(aim, 2.5f, Team.Enemy);
                    Vector2 dst = target != null ? target.Pos - (target.Pos - p.Pos).normalized * 0.7f : p.Pos + Vector2.ClampMagnitude(aim - p.Pos, 4.5f);
                    dst = WorldStreamer.I.NearestWalkable(dst, p.radius);
                    Vector2 start = p.Pos;
                    p.invulnUntil = Time.time + 0.4f;
                    for (float t = 0; t < 0.3f; t += Time.deltaTime) { p.Pos = Vector2.Lerp(start, dst, MathX.EaseOutCubic(t / 0.3f)); yield return null; }
                    Vector2 d2 = target != null ? (target.Pos - p.Pos).SafeNormal(dir) : dir;
                    p.SetSwing(d2.Angle() + 90, d2.Angle() - 70, 0.08f);
                    Fx.I?.Play(Art.Slash(w.slashColor, Pal.White, 26, 160, 1), p.Pos + Vector2.up * 0.45f, d2.Angle(), 32, 1.2f, null, true);
                    Slam(p, p.Pos + d2 * 0.8f, 1.8f, 1.6f, w.slashColor, 0.4f, 6, 0.1f);
                    break;
                }
                case "ss_sanctum":
                {
                    Vector2 c = p.Pos;
                    Sfx.Play("evolve", 0.4f, 1.5f);
                    var ring = new GameObject("sanctum");
                    ring.transform.position = c;
                    var sr = Art.MakeRenderer(ring, Art.WarningCircle(52), -29000, true);
                    for (float t = 0, tick = 0; t < 5f; t += Time.deltaTime)
                    {
                        tick -= Time.deltaTime;
                        sr.color = new Color(1f, 0.95f, 0.6f, 0.35f + Mathf.Sin(t * 6) * 0.1f);
                        if (Random.value < 0.5f) Fx.I?.Burst(c + Random.insideUnitCircle * 3f, Pal.Holy, 1, 1, 0.8f, -3, 2, 360, 0, false, true);
                        if (tick <= 0)
                        {
                            tick = 0.5f;
                            if (Vector2.Distance(p.Pos, c) < 3.2f) p.Heal(p.maxHp * 0.03f);
                            foreach (var a in new List<Actor>(Actor.InRadius(c, 3.2f, Team.Enemy)))
                                Combat.Hit(a, new DamageInfo { amount = p.AttackPower * 0.35f, source = p, noEvents = true, fxColor = Pal.Holy, tags = "HOLY SKILL" }, Pal.Holy);
                        }
                        yield return null;
                    }
                    Object.Destroy(ring);
                    break;
                }

                // ───────── Crossbow ─────────
                case "cb_pierce":
                {
                    Sfx.Play("charge", 0.6f, 1.3f);
                    for (float t = 0; t < 0.3f; t += Time.deltaTime) { Fx.I?.Burst(o + dir * 0.6f, Pal.Gold, 1, -2, 0.2f, 0, 1); yield return null; }
                    var d = Dmg(p, 2.6f, dir, 5, 0.09f);
                    var pr = Projectile.Spawn(Art.Bolt(), o + dir * 0.5f, dir * 28f, Team.Enemy, p, d);
                    pr.pierce = 99; pr.life = 0.45f; pr.radius = 0.4f; pr.color = Pal.Gold; pr.shake = 0.25f; pr.kick = 4;
                    pr.transform.localScale = Vector3.one * 1.5f;
                    p.Knockback(-dir, 14);
                    CameraRig.I?.Kick(-dir, 6); CameraRig.I?.Shake(0.3f);
                    Sfx.Play("shoot", 1f, 0.7f);
                    break;
                }
                case "cb_scatter":
                {
                    for (int i = -3; i <= 3; i++)
                    {
                        var dd = dir.Rotate(i * 9);
                        var pr = Projectile.Spawn(Art.Bolt(), o + dd * 0.4f, dd * 18f, Team.Enemy, p, Dmg(p, 0.75f, dd, 3, 0.03f));
                        pr.life = 0.4f; pr.color = Pal.Hex("fff1b0");
                    }
                    p.Knockback(-dir, 10);
                    CameraRig.I?.Kick(-dir, 5); CameraRig.I?.Shake(0.22f);
                    Sfx.Play("shoot", 1f, 0.85f);
                    break;
                }
                case "cb_backstep":
                {
                    p.SetDash(-dir * 13f, 0.2f, true);
                    Sfx.Play("dash", 0.5f);
                    yield return new WaitForSeconds(0.05f);
                    for (int i = 0; i < 3; i++)
                    {
                        var dd = (p.AimPoint - (p.Pos + Vector2.up * 0.45f)).SafeNormal(dir).Rotate((i - 1) * 6);
                        var pr = Projectile.Spawn(Art.Bolt(), p.Pos + Vector2.up * 0.45f, dd * 20f, Team.Enemy, p, Dmg(p, 0.9f, dd, 3, 0.03f));
                        pr.life = 0.45f; pr.color = Pal.Hex("fff1b0");
                        Sfx.Play("shoot", 0.6f);
                        yield return new WaitForSeconds(0.05f);
                    }
                    break;
                }
                case "cb_rain":
                {
                    Vector2 c = p.Pos + Vector2.ClampMagnitude(aim - p.Pos, 7f);
                    var mark = new GameObject("rain");
                    mark.transform.position = c;
                    var sr = Art.MakeRenderer(mark, Art.WarningCircle(44), -29000);
                    sr.color = new Color(1f, 0.85f, 0.4f, 0.5f);
                    for (float t = 0, tick = 0; t < 2.2f; t += Time.deltaTime)
                    {
                        tick -= Time.deltaTime;
                        if (tick <= 0)
                        {
                            tick = 0.07f;
                            Vector2 hitAt = c + Random.insideUnitCircle * 2.6f;
                            var pr = Projectile.Spawn(Art.Bolt(), hitAt + new Vector2(-1.2f, 5f), new Vector2(1.2f, -5f).normalized * 30f, Team.Enemy, p, Dmg(p, 0.35f, Vector2.down, 1, 0.01f));
                            pr.life = 0.17f; pr.trail = false;
                            pr.onEnd = x => { Fx.I?.Burst(x.transform.position, Pal.Hex("cdbba5"), 3, 2, 0.25f, 0, 1, 360, 0, true); };
                        }
                        yield return null;
                    }
                    Object.Destroy(mark);
                    break;
                }

                // ───────── Staff ─────────
                case "st_thrust":
                {
                    p.SetLunge(dir * 9f, 0.12f);
                    Fx.I?.Play(Art.Thrust(Pal.Lightning, Pal.White, 48), o, dir.Angle(), 32, 1.2f, null, true);
                    var list = new List<Actor>(Combat.Line(o, o + dir * 3.2f, 0.6f, Team.Enemy));
                    HitAll(p, list, 1.4f, p.Pos, 4, 0.07f, Pal.Lightning, "thunder", 0.22f, 4, Element.Lightning);
                    break;
                }
                case "st_cyclone":
                {
                    Sfx.Play("swing", 0.6f, 0.7f);
                    for (float t = 0, tick = 0; t < 0.9f; t += Time.deltaTime)
                    {
                        tick -= Time.deltaTime;
                        p.SetSwing(t * 1500, t * 1500 + 5, 0.01f);
                        foreach (var a in new List<Actor>(Actor.InRadius(p.Pos, 4f, Team.Enemy))) a.Knockback(p.Pos - a.Pos, 0.6f);
                        if (tick <= 0)
                        {
                            tick = 0.15f;
                            Fx.I?.Play(Art.Slash(Pal.Hex("c9b8ff"), Pal.White, 36, 359, 1), p.Pos + Vector2.up * 0.45f, t * 1500, 36, 1, null, true);
                            HitAll(p, Actor.InRadius(p.Pos, 2.4f, Team.Enemy), 0.4f, p.Pos, 0.5f, 0.02f, Pal.Hex("c9b8ff"), "hit_light", 0.08f, 1);
                        }
                        yield return null;
                    }
                    break;
                }
                case "st_vault":
                {
                    Vector2 dst = WorldStreamer.I.NearestWalkable(p.Pos + Vector2.ClampMagnitude(aim - p.Pos, 5f), p.radius);
                    Vector2 start = p.Pos;
                    p.invulnUntil = Time.time + 0.4f;
                    p.LastDashOrigin = start;
                    for (float t = 0; t < 0.3f; t += Time.deltaTime) { p.Pos = Vector2.Lerp(start, dst, MathX.EaseOutCubic(t / 0.3f)); Fx.I?.Afterimage(p.GetComponentInChildren<SpriteRenderer>(), new Color(0.8f, 0.7f, 1f, 0.5f), 0.2f); yield return null; }
                    Fx.I?.Play(Art.LightningBolt(64), p.Pos, 0, 22, 1, null, true);
                    Slam(p, p.Pos, 1.8f, 1.1f, Pal.Lightning, 0.3f, 4, 0.06f, Element.Lightning);
                    break;
                }
                case "st_thunder":
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var t = Actor.Nearest(p.Pos + Random.insideUnitCircle * 3, 6f, Team.Enemy);
                        Vector2 at = t != null ? t.Pos : p.Pos + Random.insideUnitCircle * 4f;
                        Fx.I?.Play(Art.LightningBolt(90), at, 0, 22, 1.2f, null, true);
                        Fx.I?.Play(Art.Shockwave(Pal.Lightning, 16), at, 0, 26, 1, null, true);
                        HitAll(p, Actor.InRadius(at, 1.3f, Team.Enemy), 1.1f, at, 2, 0.06f, Pal.Lightning, "thunder", 0.25f, 2, Element.Lightning);
                        yield return new WaitForSeconds(0.12f);
                    }
                    break;
                }

                // ───────── Dagger ─────────
                case "dg_fan":
                {
                    for (int i = 0; i < 12; i++)
                    {
                        var dd = MathX.Dir(i * 30 + dir.Angle());
                        var pr = Projectile.Spawn(Art.Knife(), o, dd * 15f, Team.Enemy, p, Dmg(p, 0.7f, dd, 1.5f, 0.02f));
                        pr.life = 0.4f; pr.spin = true; pr.color = Pal.Hex("ffb3d1"); pr.pierce = 1;
                    }
                    Sfx.Play("swing_light", 0.8f, 1.2f);
                    break;
                }
                case "dg_shadowstep":
                {
                    var target = Actor.Nearest(aim, 3f, Team.Enemy) ?? Actor.Nearest(p.Pos, 6f, Team.Enemy);
                    if (target == null) { p.BlinkTo(p.Pos + Vector2.ClampMagnitude(aim - p.Pos, 4)); break; }
                    Vector2 behind = target.Pos + (target.Pos - p.Pos).normalized * 0.8f;
                    p.BlinkTo(behind);
                    Vector2 d2 = (target.Pos - p.Pos).SafeNormal(dir);
                    p.Facing = d2;
                    yield return new WaitForSeconds(0.04f);
                    Fx.I?.Play(Art.Slash(Pal.ShadowEl, Pal.White, 20, 200, 1), p.Pos + Vector2.up * 0.45f, d2.Angle(), 32, 1, null, true);
                    var d = Dmg(p, 2.2f, d2, 3, 0.1f);
                    d.canCrit = true;
                    target.AddStatus("mark", 4f, 0, 0, p, Pal.ShadowEl);
                    Combat.Hit(target, d, Pal.ShadowEl, "hit_slice", 0.3f, 4);
                    break;
                }
                case "dg_venom":
                {
                    p.Imbue(Element.Poison, 6f);
                    Fx.I?.Burst(p.Pos + Vector2.up * 0.5f, Pal.Hex("b6f06b"), 14, 3, 0.6f, -3);
                    Sfx.Play("charge", 0.4f, 1.4f);
                    break;
                }
                case "dg_frenzy":
                {
                    for (int i = 0; i < 9; i++)
                    {
                        var t = Actor.Nearest(p.Pos, 4.5f, Team.Enemy);
                        if (t == null) break;
                        Vector2 side = (Vector2)t.Pos + MathX.Dir(Random.Range(0, 360f)) * 0.9f;
                        side = WorldStreamer.I.NearestWalkable(side, p.radius);
                        Fx.I?.Afterimage(p.GetComponentInChildren<SpriteRenderer>(), new Color(1f, 0.6f, 0.85f, 0.7f), 0.3f);
                        p.Pos = side;
                        Vector2 d2 = (t.Pos - p.Pos).SafeNormal(dir);
                        p.Facing = d2;
                        Fx.I?.Play(Art.Slash(Pal.Hex("ffb3d1"), Pal.White, 18, 180, i % 2 == 0 ? 1 : -1), p.Pos + Vector2.up * 0.45f, d2.Angle(), 36, 1, null, true);
                        Combat.Hit(t, Dmg(p, 0.65f, d2, 0.8f, 0.03f), Pal.Hex("ffb3d1"), "hit_slice", 0.1f, 2);
                        yield return new WaitForSeconds(0.08f);
                    }
                    break;
                }

                // ───────── Katana ─────────
                case "kt_iai":
                {
                    Vector2 start = p.Pos;
                    Vector2 end = WorldStreamer.I.Raycast(p.Pos, dir, 5.5f, p.radius);
                    Sfx.Play("charge", 0.4f, 2f);
                    yield return new WaitForSeconds(0.08f);
                    p.Pos = end;
                    p.invulnUntil = Time.time + 0.3f;
                    for (int i = 0; i < 5; i++) Fx.I?.Afterimage(p.GetComponentInChildren<SpriteRenderer>(), new Color(0.8f, 0.95f, 1f, 0.6f), 0.25f + i * 0.04f);
                    Fx.I?.Play(Art.Thrust(Pal.Hex("d4f3ff"), Pal.White, Mathf.RoundToInt(Vector2.Distance(start, end) * Art.PPU) + 8), start + Vector2.up * 0.45f, dir.Angle(), 30, 1, null, true);
                    yield return new WaitForSeconds(0.12f);   // short pause, then the cut lands (iaido feel)
                    var list = new List<Actor>(Combat.Line(start, end, 0.8f, Team.Enemy));
                    if (list.Count > 0) { GameTime.Hitstop(0.12f); CameraRig.I?.Shake(0.4f); }
                    HitAll(p, list, 2.1f, start, 3, 0.05f, Pal.Hex("d4f3ff"), "hit_slice", 0.3f, 6);
                    break;
                }
                case "kt_crescent":
                {
                    var d = Dmg(p, 1.3f, dir, 3, 0.05f);
                    var pr = Projectile.Spawn(Art.Slash(Pal.Hex("d4f3ff"), Pal.White, 18, 150, 1)[1], o, dir * 13f, Team.Enemy, p, d, true);
                    pr.pierce = 99; pr.life = 0.55f; pr.radius = 0.7f; pr.color = Pal.Hex("d4f3ff"); pr.trail = true; pr.sfx = "hit_slice";
                    p.SetSwing(dir.Angle() + 80, dir.Angle() - 80, 0.1f);
                    Sfx.Play("swing", 0.8f, 1.2f);
                    break;
                }
                case "kt_counter":
                {
                    float until = Time.time + 1.0f;
                    float hpBefore = p.hp;
                    p.invulnUntil = until;
                    Fx.I?.Play(Art.Shockwave(Pal.Hex("d4f3ff"), 14), p.Pos, 0, 18, 1, null, true);
                    bool countered = false;
                    while (Time.time < until && !countered)
                    {
                        p.StopMoving();
                        foreach (var a in Actor.All)
                            if (a is Enemy e && e.Alive && e.IsAboutToHit(p.Pos)) { countered = true; break; }
                        yield return null;
                    }
                    if (countered)
                    {
                        GameTime.SlowMo(0.4f, 0.2f);
                        Sfx.Play("guard", 1f);
                        foreach (var a in new List<Actor>(Actor.InRadius(p.Pos, 3f, Team.Enemy)))
                        {
                            Fx.I?.Play(Art.Slash(Pal.White, Pal.White, 22, 220, 1), a.Pos + Vector2.up * 0.4f, Random.Range(0, 360f), 36, 1, null, true);
                            Combat.Hit(a, Dmg(p, 3f, a.Pos - p.Pos, 5, 0.12f), Pal.White, "hit_slice", 0.4f, 6);
                        }
                        GameEvents.Raise(new CombatEvent { type = Trig.Guard, source = p, position = p.Pos, tags = "GUARD" });
                    }
                    p.invulnUntil = 0;
                    break;
                }
                case "kt_flash":
                {
                    GameTime.SlowMo(0.6f, 0.15f);
                    Sfx.Play("charge", 0.8f, 1.8f);
                    yield return new WaitForSecondsRealtime(0.25f);
                    var targets = new List<Actor>(Actor.InRadius(p.Pos, 5f, Team.Enemy));
                    for (int i = 0; i < 14; i++)
                    {
                        Vector2 c = p.Pos + Random.insideUnitCircle * 4f;
                        Fx.I?.Play(Art.Thrust(Pal.Hex("e6f8ff"), Pal.White, 70), c - MathX.Dir(i * 47) * 2f, i * 47, 40, 1, null, true);
                    }
                    yield return new WaitForSecondsRealtime(0.2f);
                    CameraRig.I?.Shake(0.7f);
                    GameTime.Hitstop(0.18f);
                    foreach (var a in targets) for (int k = 0; k < 3; k++) Combat.Hit(a, Dmg(p, 1.1f, a.Pos - p.Pos, 2, 0.02f), Pal.White, "hit_slice", 0.15f, 2);
                    break;
                }
            }
        }

        /// <summary>Ground slam: shockwave + debris + shake. The core of the Greatsword's hit feel.</summary>
        static void Slam(Player p, Vector2 at, float radius, float mult, Color32 col, float shake, float kick, float hitstop, Element el = Element.None)
        {
            Fx.I?.Play(Art.Shockwave(col, Mathf.RoundToInt(radius * Art.PPU)), at, 0, 22, 1, null, true);
            Fx.I?.Burst(at, Pal.Hex("cdbba5"), 20, 7, 0.6f, 0, 2, 360, 0, true);
            Fx.I?.Burst(at, col, 10, 9, 0.35f);
            Sfx.Play("explode", 0.8f);
            CameraRig.I?.Shake(shake * Game.Settings.shakeScale);
            CameraRig.I?.Punch(1f);
            GameTime.Hitstop(hitstop);
            foreach (var a in new List<Actor>(Actor.InRadius(at, radius, Team.Enemy)))
            {
                var d = Dmg(p, mult, (a.Pos - at).SafeNormal(p.Facing), 6, hitstop * 0.5f, el);
                Combat.Hit(a, d, col, "hit_heavy", shake * 0.5f, kick);
                if (el != Element.None) Combat.ApplyElement(a, el, d.amount, p, 0);
            }
        }
    }
}
