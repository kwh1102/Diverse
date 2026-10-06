using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Signature attacks (EnemyDef.attack) and boss signature moves. Every pattern has a readable telegraph whose shape
    /// matches where the hit lands, so the dash can always answer it — the game gets harder by reading, not by numbers.
    /// The numbers per enemy (speed, length, count) come from the EnemyData assets (attackParam / attackCount).
    /// </summary>
    public partial class Enemy
    {
        float signatureRecover;              // recovery after this pattern (punish window); 0 = default
        /// <summary>Signature attacks started (any enemy). Lets the smoke test confirm every pattern actually fires.</summary>
        public static readonly HashSet<string> SignaturesFired = new HashSet<string>();
        float stunnedByWall;                 // Gore: crashed into a wall
        Vector2 sigFrom, sigTo;              // line patterns: start/end
        readonly List<Vector2> sigPoints = new List<Vector2>();
        readonly List<SpriteRenderer> extraWarns = new List<SpriteRenderer>();

        float SignatureWindupMul => def.boss ? 1f : def.attack switch
        {
            EnemyAttack.Tongue => 1.1f, EnemyAttack.Burrow => 1.4f, EnemyAttack.Dive => 1.2f, EnemyAttack.IceLine => 1.1f, _ => 1f,
        };

        // ───────────── Telegraph helpers ─────────────

        SpriteRenderer ExtraWarn(int i)
        {
            while (extraWarns.Count <= i)
            {
                var go = new GameObject("warn+");
                go.transform.SetParent(transform.parent, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = warn.sharedMaterial;
                sr.sortingOrder = warn.sortingOrder;
                extraWarns.Add(sr);
            }
            return extraWarns[i];
        }

        void HideExtraWarns() { foreach (var w in extraWarns) if (w != null) w.enabled = false; }

        void CancelSignature()
        {
            HideExtraWarns();
            sigPoints.Clear();
            lift = 0; sink = 0;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            foreach (var w in extraWarns) if (w != null) Destroy(w.gameObject);
        }

        /// <summary>Warning dots along a line (tongue, ice spikes, dive path).</summary>
        void LineWarns(Vector2 a, Vector2 b, int n, int radiusPx)
        {
            sigPoints.Clear();
            for (int i = 0; i < n; i++) sigPoints.Add(Vector2.Lerp(a, b, n == 1 ? 1 : (i + 1f) / n));
            for (int i = 0; i < sigPoints.Count; i++)
            {
                var w = ExtraWarn(i);
                w.sprite = Art.WarningCircle(radiusPx);
                w.enabled = true;
                w.transform.position = sigPoints[i];
            }
            for (int i = sigPoints.Count; i < extraWarns.Count; i++) extraWarns[i].enabled = false;
        }

        void TintWarns(float k)
        {
            var c = new Color(1f, 0.25f, 0.3f, 0.25f + k * 0.55f);
            warn.color = c;
            foreach (var w in extraWarns) if (w != null && w.enabled) { w.color = c; w.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, MathX.EaseOutCubic(k)); }
        }

        /// <summary>Perfect-dodge check for signature attacks: is the hero inside the telegraphed area right now?</summary>
        bool SignatureThreatens(Vector2 playerPos, out bool hit)
        {
            hit = false;
            bool sig = (def.attack != EnemyAttack.Default && !def.boss) || (def.boss && bossAttack >= 4);
            if (!sig) return false;
            if (sigPoints.Count > 0)
            {
                foreach (var q in sigPoints) if (Vector2.Distance(q, playerPos) < 1.4f) { hit = true; break; }
            }
            else hit = Vector2.Distance(strikeTarget, playerPos) < 2f || Vector2.Distance(Pos, playerPos) < def.attackRange + 1f;
            return true;
        }

        // ───────────── Movement flavors ─────────────

        /// <summary>Hop-hop movement with irregular timing and sideways drift (frogs, jellies).</summary>
        void ChaseHopping(Player p, Vector2 toP, float dist, float want, float slowMul, float dt)
        {
            hopT -= dt;
            if (hopT > 0) return;
            hopT = def.hopInterval * Random.Range(0.6f, 1.5f);
            Vector2 dir = toP.normalized;
            if (dist < want * 0.6f) dir = -dir;                              // too close: hop back out
            else if (dist < want * 1.1f) dir = dir.Rotate(Random.value < 0.5f ? 80 : -80);   // in range: hop sideways
            else dir = dir.Rotate(Random.Range(-40f, 40f));                   // approaching: zig-zag
            Knockback(dir, def.speed * 4.5f * slowMul * Mathf.Max(0.3f, mass));   // ≈ 0.45 × speed units per hop
            squash = 0.6f;
            Fx.I?.Burst(Pos, Pal.Hex("cdbba5"), 3, 2, 0.25f, 0, 1, 360, 0, true);
        }

        void Strafe(Vector2 toP, float slowMul, float dt)
        {
            if (Random.value < dt * 0.6f) strafeSign = -strafeSign;   // changes direction every ~1.7 s on average
            MoveToward(Pos + toP.normalized.Rotate(90 * strafeSign), def.speed * 0.5f * def.strafe * slowMul, dt);
        }

        // ───────────── Regular enemies ─────────────

        void BeginSignature(Player p)
        {
            signatureRecover = 0;
            sigPoints.Clear();
            HideExtraWarns();
            float range = def.attackRange;
            switch (def.attack)
            {
                case EnemyAttack.Lunge:      // fox: aimed lunge ending in a slash, then one more quick slash
                    strikeTarget = Pos + strikeDir * Mathf.Min(Vector2.Distance(p.Pos, Pos) + 0.6f, range + 1.8f);
                    LineWarns(Pos, strikeTarget, 4, 9);
                    warn.sprite = Art.WarningCircle(Mathf.RoundToInt(1.1f * Art.PPU));
                    break;
                case EnemyAttack.Tongue:     // frog: thin long line
                    sigFrom = Pos + Vector2.up * 0.3f;
                    sigTo = sigFrom + strikeDir * def.attackParam;
                    LineWarns(sigFrom, sigTo, Mathf.Max(3, Mathf.RoundToInt(def.attackParam / 0.7f)), 6);
                    warn.sprite = Art.WarningCircle(10);
                    break;
                case EnemyAttack.Burrow:     // shroom: sinks, then pops up under where the hero was
                    strikeTarget = p.Pos;
                    warn.sprite = Art.WarningCircle(Mathf.RoundToInt(1.3f * Art.PPU));
                    break;
                case EnemyAttack.Volley:     // raccoon: small cone of the 3 lanes
                    warn.sprite = Art.WarningCircle(10);
                    strikeTarget = p.Pos;
                    break;
                case EnemyAttack.Orbit:      // wisp: ring around the hero where the shots converge
                    strikeTarget = p.Pos;
                    warn.sprite = Art.WarningCircle(Mathf.RoundToInt(1.4f * Art.PPU));
                    break;
                case EnemyAttack.IceLine:    // frostling: a line of spikes rushing toward the hero
                    sigFrom = Pos;
                    sigTo = Pos + strikeDir * range;
                    LineWarns(sigFrom, sigTo, Mathf.Max(3, def.attackCount), 10);
                    warn.sprite = Art.WarningCircle(8);
                    break;
                case EnemyAttack.Gore:       // boar: long straight charge
                    sigFrom = Pos;
                    sigTo = Pos + strikeDir * def.attackParam * 0.55f;
                    LineWarns(sigFrom, sigTo, 5, 12);
                    warn.sprite = Art.WarningCircle(12);
                    break;
                case EnemyAttack.Dive:       // bat: rises, then swoops through the hero and out the other side
                    sigFrom = Pos;
                    sigTo = p.Pos + strikeDir * 2.2f;
                    LineWarns(sigFrom, sigTo, 4, 7);
                    warn.sprite = Art.WarningCircle(7);
                    break;
            }
        }

        void UpdateSignatureWarn(float k)
        {
            squash = Mathf.Lerp(0, 0.4f, k);
            switch (def.boss ? EnemyAttack.Default : def.attack)
            {
                case EnemyAttack.Burrow:
                    // Sinking into the ground; the circle follows the hero for the first half, then locks in
                    var p = Player.I;
                    if (p != null && k < 0.55f) strikeTarget = Vector2.Lerp(strikeTarget, p.Pos, 0.15f);
                    warn.transform.position = strikeTarget;
                    sink = k;
                    break;
                case EnemyAttack.Orbit:
                case EnemyAttack.Volley:
                    warn.transform.position = strikeTarget;
                    break;
                case EnemyAttack.Dive:
                    warn.transform.position = Pos;
                    lift = k * 0.6f;   // rises before swooping
                    break;
                default:
                    warn.transform.position = def.boss ? strikeTarget : Pos;
                    break;
            }
            warn.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, MathX.EaseOutCubic(k));
            TintWarns(k);
        }

        void StartSignature(Player p)
        {
            SignaturesFired.Add(def.attack.ToString());
            switch (def.attack)
            {
                case EnemyAttack.Lunge:
                    Sfx.Play("dash", 0.4f, 1.3f);
                    break;
                case EnemyAttack.Tongue:
                {
                    // Instant lash along the line; pulls the hero in if it connects
                    Fx.I?.Play(Art.Thrust(Pal.Hex("ff7a9a"), Pal.Hex("ffd0dc"), Mathf.RoundToInt(def.attackParam * Art.PPU)), sigFrom, strikeDir.Angle(), 40, 0.8f, null, false);
                    Sfx.Play("swing_light", 0.5f, 1.5f);
                    if (p != null && Combat.DistToSegment(p.Pos + Vector2.up * 0.3f, sigFrom, sigTo) < 0.45f + p.radius)
                    {
                        HitPlayer(p, 1f, strikeDir, 0);
                        p.Knockback(-strikeDir, 9f);    // yanked toward the frog
                    }
                    HideExtraWarns();
                    sigPoints.Clear();
                    signatureRecover = 0.7f;           // tongue out = open to punish
                    break;
                }
                case EnemyAttack.Burrow:
                    Pos = WorldStreamer.I.NearestWalkable(strikeTarget, radius);
                    sink = 0;
                    Fx.I?.Burst(Pos, Pal.Hex("8a6a4a"), 18, 6, 0.5f, 0, 2, 360, 0, true);
                    Fx.I?.Play(Art.Shockwave(Pal.Hex("cdbba5"), 22), Pos, 0, 22, 1, null, true);
                    CameraRig.I?.Shake(0.2f);
                    Sfx.Play("explode", 0.4f, 1.4f);
                    if (p != null && Vector2.Distance(p.Pos, Pos) < 1.3f + p.radius) HitPlayer(p, 1.3f, (p.Pos - Pos).SafeNormal(Vector2.up), 6);
                    signatureRecover = 0.8f;
                    break;
                case EnemyAttack.Volley:
                case EnemyAttack.Orbit:
                    strikeStep = 0;
                    stateT = 99;    // fire the first shot right away
                    break;
                case EnemyAttack.IceLine:
                    strikeStep = 0;
                    stateT = 0;
                    Sfx.Play("charge", 0.3f, 2f);
                    break;
                case EnemyAttack.Gore:
                    Sfx.Play("dash", 0.7f, 0.5f);
                    sigFrom = Pos;
                    break;
                case EnemyAttack.Dive:
                    sigFrom = Pos;
                    Sfx.Play("dash", 0.35f, 1.6f);
                    break;
            }
        }

        /// <summary>Per-frame part of a signature attack. Returns true when finished.</summary>
        bool UpdateSignature(Player p, float dt)
        {
            switch (def.attack)
            {
                case EnemyAttack.Lunge:
                {
                    // Dash to the marked point, slash on arrival; then a short second slash (attackCount - 1 follow-ups)
                    if (strikeStep == 0)
                    {
                        Vector2 to = strikeTarget - Pos;
                        float step = def.attackParam * 9f * dt;
                        if (to.magnitude > step && stateT < 0.4f) { Pos = WorldStreamer.I.Move(Pos, to.normalized * step, radius); return false; }
                        HideExtraWarns(); sigPoints.Clear();
                        Slash(p, 1f);
                        strikeStep = 1; stateT = 0;
                        return def.attackCount <= 1;
                    }
                    if (stateT < 0.32f) return false;
                    if (p != null) strikeDir = (p.Pos - Pos).SafeNormal(strikeDir);
                    Facing = strikeDir;
                    Slash(p, 0.7f);
                    strikeStep++; stateT = 0;
                    signatureRecover = 0.6f;
                    return strikeStep >= def.attackCount;
                }
                case EnemyAttack.Volley:
                {
                    // attackCount quick shots; the last one leads the hero's movement
                    if (stateT < 0.16f) return false;
                    stateT = 0;
                    if (p == null) return true;
                    bool last = strikeStep == def.attackCount - 1;
                    Vector2 aim = p.Pos;
                    if (last) aim += (p.Pos - lastPlayerPos) / Mathf.Max(0.016f, Time.deltaTime) * 0.35f;
                    Shoot((aim + Vector2.up * 0.3f - (Pos + Vector2.up * 0.5f)).SafeNormal(strikeDir), 10f, 1f);
                    strikeStep++;
                    signatureRecover = 0.5f;
                    return strikeStep >= def.attackCount;
                }
                case EnemyAttack.Orbit:
                {
                    // Shots launched sideways that curve back in onto the marked spot
                    if (strikeStep >= def.attackCount) { warn.enabled = false; return stateT > 0.2f; }
                    if (stateT < 0.1f) return false;
                    stateT = 0;
                    float side = strikeStep % 2 == 0 ? 1 : -1;
                    var o = Pos + Vector2.up * 0.5f;
                    var to = strikeTarget + Vector2.up * 0.3f - o;
                    var dir = to.normalized.Rotate(side * (55 + strikeStep * 8));
                    var pr = Shoot(dir, 5.5f, 0.8f);
                    var target = strikeTarget + Vector2.up * 0.3f;
                    pr.onUpdate = (pj, t) =>
                    {
                        var want = (target - (Vector2)pj.transform.position).normalized * 6.5f;
                        pj.vel = Vector2.Lerp(pj.vel, want, 1 - Mathf.Exp(-3.2f * Time.deltaTime));
                    };
                    pr.life = 2.2f;
                    strikeStep++;
                    warn.enabled = true; warn.transform.position = strikeTarget;
                    signatureRecover = 0.4f;
                    return false;
                }
                case EnemyAttack.IceLine:
                {
                    // Spikes erupt one after another along the telegraphed line
                    if (stateT < 0.09f) return false;
                    stateT = 0;
                    if (strikeStep >= sigPoints.Count) { HideExtraWarns(); sigPoints.Clear(); signatureRecover = 0.6f; return true; }
                    var at = sigPoints[strikeStep];
                    ExtraWarn(strikeStep).enabled = false;
                    Fx.I?.Play(Art.Shockwave(Pal.Frost, 10), at, 0, 26, 1, null, true);
                    Fx.I?.Burst(at + Vector2.up * 0.3f, Pal.Frost, 6, 4, 0.4f, -2);
                    Sfx.Play("hit_light", 0.25f, 1.6f);
                    if (p != null && Vector2.Distance(p.Pos, at) < 0.7f + p.radius)
                    {
                        HitPlayer(p, 0.9f, strikeDir, 2);
                        p.AddStatus("chill", 1.5f, 0, 0.4f, this, Pal.Frost);
                    }
                    strikeStep++;
                    return false;
                }
                case EnemyAttack.Gore:
                {
                    // Charge in a straight line. Hitting a wall stuns the boar (a reward for baiting it into rocks/trees)
                    var before = Pos;
                    float speed = def.attackParam * 1.6f;
                    Pos = WorldStreamer.I.Move(Pos, strikeDir * speed * dt, radius);
                    if (Random.value < 0.6f) Fx.I?.Burst(Pos, Pal.Hex("cdbba5"), 1, 2, 0.3f, 0, 2, 40, (-strikeDir).Angle(), true);
                    if (p != null && Vector2.Distance(p.Pos, Pos) < radius + p.radius + 0.25f) { HitPlayer(p, 1.3f, strikeDir, 9); HideExtraWarns(); sigPoints.Clear(); signatureRecover = 0.6f; return true; }
                    bool blocked = (Pos - before).sqrMagnitude < speed * dt * speed * dt * 0.1f;
                    if (blocked)
                    {
                        CameraRig.I?.Shake(0.3f);
                        Sfx.Play("hit_heavy", 0.6f, 0.7f);
                        Fx.I?.Burst(Pos + strikeDir * 0.5f, Pal.Hex("cdbba5"), 14, 5, 0.5f, 0, 2, 120, (-strikeDir).Angle(), true);
                        HideExtraWarns(); sigPoints.Clear();
                        Stun(1.6f);
                        return false;   // Stun switched the state
                    }
                    if (Vector2.Distance(sigFrom, Pos) > def.attackParam * 0.55f + 0.5f || stateT > 1.2f) { HideExtraWarns(); sigPoints.Clear(); signatureRecover = 0.7f; return true; }
                    return false;
                }
                case EnemyAttack.Dive:
                {
                    // Swoop along the line through the hero
                    float dur = Mathf.Max(0.2f, Vector2.Distance(sigFrom, sigTo) / (def.attackParam * 10f));
                    float k = Mathf.Clamp01(stateT / dur);
                    Pos = WorldStreamer.I.Move(Pos, (Vector2.Lerp(sigFrom, sigTo, k) - Pos), radius);
                    lift = (1 - k) * 0.6f;
                    if (strikeStep == 0 && p != null && Vector2.Distance(p.Pos + Vector2.up * 0.3f, Pos + Vector2.up * 0.3f) < radius + p.radius + 0.2f) { HitPlayer(p, 1f, strikeDir, 4); strikeStep = 1; }
                    if (k >= 1) { HideExtraWarns(); sigPoints.Clear(); lift = 0; signatureRecover = 0.5f; return true; }
                    return false;
                }
            }
            return true;
        }

        Vector2 lastPlayerPos;
        void LateUpdate() { if (Player.I != null) lastPlayerPos = Player.I.Pos; }

        void Slash(Player p, float mult)
        {
            Fx.I?.Play(Art.Slash(Pal.Hex("ff8a8a"), Pal.White, Mathf.RoundToInt(def.attackRange * Art.PPU * 0.9f), 140, strikeStep % 2 == 0 ? 1 : -1), Pos + Vector2.up * 0.4f, strikeDir.Angle(), 30, 0.9f, null, true);
            Sfx.Play("swing_light", 0.45f, 0.9f);
            if (p != null && Vector2.Distance(p.Pos, Pos + strikeDir * def.attackRange * 0.5f) < def.attackRange * 0.8f + p.radius) HitPlayer(p, mult, strikeDir, 4);
        }

        Projectile Shoot(Vector2 dir, float speed, float mult)
        {
            var o = Pos + Vector2.up * 0.5f;
            var pr = Projectile.Spawn(Art.EnemyShot(def.projectileColor), o, dir * speed, Team.Player, this,
                new DamageInfo { amount = def.damage * mult, source = this, direction = dir, knockback = 3, element = def.element });
            pr.life = 1.6f; pr.color = def.projectileColor; pr.radius = 0.2f;
            Sfx.Play("shoot", 0.25f, 1.3f);
            return pr;
        }

        // ───────────── Bosses: a signature move each ─────────────

        /// <summary>4 = fox: triple dash, 5 = shroom: spore field, 6 = knight: sword lines.</summary>
        int BossSignature(float dist) => def.id switch
        {
            "boss_fox" => 4,
            "boss_shroom" => 5,
            "boss_knight" => 6,
            _ => dist > 3.5f ? 1 : 2,
        };

        void BeginBossSignature(Player p)
        {
            sigPoints.Clear();
            HideExtraWarns();
            switch (bossAttack)
            {
                case 4:   // Red-tail: dashes through the hero several times, re-aiming each time
                    sigFrom = Pos;
                    sigTo = p.Pos + strikeDir * 2.5f;
                    LineWarns(sigFrom, sigTo, 6, 12);
                    warn.sprite = Art.WarningCircle(14);
                    strikeTarget = p.Pos;
                    break;
                case 5:   // Shroom king: spore circles drop around the hero, then burst (stand in the gaps)
                {
                    int n = bossPhase > 0 ? 7 : 5;
                    for (int i = 0; i < n; i++)
                    {
                        var q = i == 0 ? p.Pos : p.Pos + MathX.Dir(i * 360f / (n - 1) + Random.Range(-15f, 15f)) * Random.Range(1.8f, 3.4f);
                        sigPoints.Add(q);
                        var w = ExtraWarn(i);
                        w.sprite = Art.WarningCircle(Mathf.RoundToInt(1.2f * Art.PPU));
                        w.enabled = true;
                        w.transform.position = q;
                    }
                    warn.sprite = Art.WarningCircle(Mathf.RoundToInt(1.6f * Art.PPU));
                    strikeTarget = Pos;
                    break;
                }
                case 6:   // Knight: three sword lines in a fan (phase 2: five), then each erupts
                {
                    int n = bossPhase > 0 ? 5 : 3;
                    for (int i = 0; i < n; i++)
                    {
                        var d = strikeDir.Rotate((i - (n - 1) / 2f) * 22f);
                        for (int j = 1; j <= 6; j++)
                        {
                            var q = Pos + d * j * 1.1f;
                            sigPoints.Add(q);
                            var w = ExtraWarn(sigPoints.Count - 1);
                            w.sprite = Art.WarningCircle(9);
                            w.enabled = true;
                            w.transform.position = q;
                        }
                    }
                    warn.sprite = Art.WarningCircle(Mathf.RoundToInt(1.4f * Art.PPU));
                    strikeTarget = Pos;
                    break;
                }
            }
        }

        void StartBossSignature(Player p)
        {
            SignaturesFired.Add("Boss" + bossAttack);
            strikeStep = 0;
            stateT = 0;
            switch (bossAttack)
            {
                case 4: sigFrom = Pos; Sfx.Play("dash", 0.8f, 0.6f); break;
                case 5:
                    Sfx.Play("explode", 0.7f, 0.8f);
                    CameraRig.I?.Shake(0.35f);
                    foreach (var q in sigPoints)
                    {
                        Fx.I?.Play(Art.Shockwave(Pal.Hex("b6f06b"), Mathf.RoundToInt(1.2f * Art.PPU)), q, 0, 22, 1, null, true);
                        Fx.I?.Burst(q + Vector2.up * 0.2f, Pal.Hex("b6f06b"), 10, 4, 0.6f, -1);
                    }
                    if (p != null)
                        foreach (var q in sigPoints)
                            if (Vector2.Distance(p.Pos, q) < 1.2f + p.radius)
                            {
                                HitPlayer(p, 1.1f, (p.Pos - q).SafeNormal(Vector2.up), 5);
                                p.AddStatus("poison", 3f, def.damage * 0.25f, 0.15f, this, Pal.Hex("b6f06b"));
                                break;
                            }
                    HideExtraWarns(); sigPoints.Clear();
                    break;
                case 6: Sfx.Play("swing_heavy", 0.8f, 0.7f); break;
            }
        }

        bool UpdateBossSignature(Player p, float dt)
        {
            switch (bossAttack)
            {
                case 4:
                {
                    // Up to 3 dashes (2 in phase 1), each re-aimed at the hero with a short telegraph between
                    int dashes = bossPhase > 0 ? 3 : 2;
                    float dur = 0.32f;
                    if (stateT <= dur)
                    {
                        float k = stateT / dur;
                        Pos = WorldStreamer.I.Move(Pos, Vector2.Lerp(sigFrom, sigTo, MathX.EaseOutCubic(k)) - Pos, radius);
                        if (Random.value < 0.6f) Fx.I?.Afterimage(body, new Color(1f, 0.5f, 0.4f, 0.6f), 0.2f);
                        if (p != null && Vector2.Distance(p.Pos, Pos) < radius + p.radius + 0.25f && (strikeStep & 0x100) == 0) { HitPlayer(p, 1.1f, strikeDir, 7); strikeStep |= 0x100; }
                        return false;
                    }
                    int done = (strikeStep & 0xff) + 1;
                    if (done >= dashes || p == null) { HideExtraWarns(); sigPoints.Clear(); return true; }
                    if (stateT < dur + 0.35f)
                    {
                        // brief re-aim telegraph
                        if (sigPoints.Count == 0)
                        {
                            strikeDir = (p.Pos - Pos).SafeNormal(strikeDir);
                            Facing = strikeDir;
                            sigFrom = Pos; sigTo = p.Pos + strikeDir * 2.5f;
                            LineWarns(sigFrom, sigTo, 6, 12);
                        }
                        TintWarns((stateT - dur) / 0.35f);
                        return false;
                    }
                    HideExtraWarns(); sigPoints.Clear();
                    strikeStep = done;   // clears the "already hit" bit
                    stateT = 0;
                    Sfx.Play("dash", 0.7f, 0.7f);
                    return false;
                }
                case 5:
                    return stateT > 0.3f;
                case 6:
                {
                    // Erupt outward one ring of points at a time
                    if (stateT < 0.08f) return false;
                    stateT = 0;
                    int ring = (strikeStep & 0xff) + 1;    // j index along each line (bit 0x100 = already hit)
                    if (ring > 6) { HideExtraWarns(); sigPoints.Clear(); return true; }
                    for (int i = 0; i < sigPoints.Count; i++)
                    {
                        if (i % 6 != ring - 1) continue;
                        var q = sigPoints[i];
                        ExtraWarn(i).enabled = false;
                        Fx.I?.Play(Art.Shockwave(def.projectileColor, 10), q, 0, 26, 1, null, true);
                        if (p != null && Vector2.Distance(p.Pos, q) < 0.65f + p.radius && (strikeStep & 0x100) == 0) { HitPlayer(p, 1.2f, (p.Pos - Pos).SafeNormal(strikeDir), 5); strikeStep |= 0x100; }
                    }
                    Sfx.Play("hit_heavy", 0.3f, 1.3f);
                    strikeStep = (strikeStep & 0x100) | ring;
                    return false;
                }
            }
            return true;
        }
    }
}
