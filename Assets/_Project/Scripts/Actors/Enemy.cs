using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 적. 상태: Idle(배회) → Chase → Windup(예고) → Strike → Recover.
    /// 모든 공격은 예고(경고 원/깜빡임)가 있어 회피(대시)가 가능하다 → '완벽한 회피' 판정의 근거.
    /// </summary>
    public partial class Enemy : Actor
    {
        [System.NonSerialized] public EnemyDef def;
        public string campId;               // 소속 군집 (전부 처치하면 군집 해결)
        public Vector2 home;
        enum S { Idle, Chase, Windup, Strike, Recover, Stunned, Dying }
        S state;
        float stateT, attackCd, wanderT, animT, stunT;
        Vector2 wanderTo, strikeDir, strikeTarget;
        [SerializeField] SpriteRenderer warn;   // attack telegraph (prefab child, hidden until windup)
        bool aggro;
        int bossPhase;
        int bossAttack, lastBossAttack;
        float spawnT = 0.35f;
        float hopT;                         // hopper movement: time until the next hop
        int strikeStep;                     // multi-hit patterns (Lunge follow-up, Volley shots)
        float strafeSign = 1;

        public static Enemy Spawn(EnemyDef def, Vector2 pos, string campId)
        {
            var e = Instantiate(DB.Asset.enemyPrefab);
            e.name = def.name;
            e.Init(def, pos, campId);
            return e;
        }

        void Init(EnemyDef d, Vector2 pos, string camp)
        {
            def = d;
            team = Team.Enemy;
            radius = d.radius;
            mass = d.mass;
            maxHp = hp = d.hp;
            campId = camp;
            Pos = pos; home = pos;
            body.sprite = Art.Enemy(d.sprite, 0);
            SetShadowSize(Mathf.RoundToInt(d.radius * 32));
            attackCd = Random.Range(0.3f, 1.2f);
            wanderTo = pos;
            warn.enabled = false;
            visual.localScale = Vector3.zero;
        }

        public void Despawn() { Destroy(gameObject); }

        public void Stun(float t)
        {
            if (def.boss) t *= 0.35f;
            if (!Alive) return;
            state = S.Stunned; stunT = t; warn.enabled = false;
            CancelSignature();   // a stun interrupts any pattern (and its telegraphs)
            Fx.I?.Number(Pos + Vector2.up * 1.1f, "기절", new Color(1f, 0.95f, 0.5f), 0.8f, 0.6f);
        }

        /// <summary>플레이어의 '완벽한 회피' 판정: 곧 공격이 닿을 상황인가?</summary>
        public bool IsAboutToHit(Vector2 playerPos)
        {
            if (state != S.Windup) return false;
            float remain = Windup - stateT;
            if (remain > 0.25f) return false;
            if (SignatureThreatens(playerPos, out bool hit)) return hit;
            return Vector2.Distance(Pos, playerPos) < def.attackRange + 1.2f || Vector2.Distance(strikeTarget, playerPos) < 1.6f;
        }

        float Windup => def.attackWindup * (def.boss && bossPhase > 0 ? 0.8f : 1f) * SignatureWindupMul;
        float RecoverTime => def.boss ? 0.45f : signatureRecover > 0 ? signatureRecover : 0.35f;

        protected override Color BaseTint => def != null && def.elite ? new Color(1f, 0.72f, 0.78f) : Color.white;

        protected override void Update()
        {
            float dt = Time.deltaTime;
            if (dt > 0 && Alive) Think(dt);
            base.Update();
            Animate(dt);
        }

        void Think(float dt)
        {
            if (spawnT > 0) { spawnT -= dt; return; }
            var p = Player.I;
            if (p == null || !p.Alive) { state = S.Idle; }
            stateT += dt;
            attackCd -= dt;
            float slowMul = 1 - Slow;
            if (HasStatus("freeze")) return;

            Vector2 toP = p != null ? p.Pos - Pos : Vector2.zero;
            float dist = toP.magnitude;

            switch (state)
            {
                case S.Stunned:
                    stunT -= dt;
                    if (stunT <= 0) { state = S.Chase; stateT = 0; }
                    return;

                case S.Idle:
                    if (p != null && p.Alive && (dist < def.aggroRange || (aggro && dist < def.aggroRange * 2)))
                    {
                        state = S.Chase; stateT = 0; aggro = true;
                        if (def.boss) { BossIntro(); }
                        // 같은 군집 동료도 깨움
                        foreach (var a in All) if (a is Enemy e && e != this && e.campId == campId && campId != null && Vector2.Distance(e.Pos, Pos) < 8) e.aggro = true;
                    }
                    wanderT -= dt;
                    if (wanderT <= 0) { wanderT = Random.Range(1.5f, 3.5f); wanderTo = home + Random.insideUnitCircle * 2.5f; }
                    MoveToward(wanderTo, def.speed * 0.35f * slowMul, dt);
                    break;

                case S.Chase:
                    if (p == null || !p.Alive || dist > def.aggroRange * 2.2f) { state = S.Idle; aggro = false; break; }
                    Facing = toP.SafeNormal(Facing);
                    float want = def.brain is EnemyBrain.Ranged or EnemyBrain.Caster ? def.attackRange * 0.8f : def.attackRange * 0.85f;
                    if (def.hopInterval > 0) ChaseHopping(p, toP, dist, want, slowMul, dt);
                    else if (def.brain is EnemyBrain.Ranged or EnemyBrain.Caster && dist < want * 0.55f)
                        MoveToward(Pos - toP.normalized, def.speed * 0.8f * slowMul, dt);    // 거리 유지
                    else if (dist > want)
                        MoveToward(p.Pos, def.speed * slowMul * (def.brain == EnemyBrain.Swarm ? 1 + Mathf.Sin(Time.time * 6 + GetInstanceID()) * 0.3f : 1), dt);
                    else if (def.strafe > 0 && attackCd > 0.2f)
                        Strafe(toP, slowMul, dt);    // circle the hero between attacks instead of standing still
                    if (dist <= def.attackRange + p.radius && attackCd <= 0 && (def.brain is not (EnemyBrain.Ranged or EnemyBrain.Caster) || WorldStreamer.I.LineClear(Pos + Vector2.up * 0.4f, p.Pos + Vector2.up * 0.4f)))
                        BeginWindup(p);
                    Separate(dt);
                    break;

                case S.Windup:
                    UpdateWarn();
                    if (stateT >= Windup) { state = S.Strike; stateT = 0; strikeStep = 0; DoStrike(); }
                    break;

                case S.Strike:
                    if (def.attack != EnemyAttack.Default && !def.boss)
                    {
                        if (UpdateSignature(p, dt)) { state = S.Recover; stateT = 0; }
                    }
                    else if (def.boss && bossAttack >= 4)
                    {
                        if (UpdateBossSignature(p, dt)) { state = S.Recover; stateT = 0; }
                    }
                    else if (def.brain == EnemyBrain.Charger || (def.boss && bossAttack == 1))
                    {
                        // 돌진
                        var before = Pos;
                        Pos = WorldStreamer.I.Move(Pos, strikeDir * (def.boss ? 13f : 11f) * dt, radius);
                        if (Random.value < 0.5f) Fx.I?.Burst(Pos, Pal.Hex("cdbba5"), 1, 2, 0.3f, 0, 2, 40, (-strikeDir).Angle(), true);
                        if (p != null && Vector2.Distance(p.Pos, Pos) < radius + p.radius + 0.2f) HitPlayer(p, 1.2f, strikeDir, 7);
                        if (stateT > 0.45f || (Pos - before).sqrMagnitude < 0.0001f) { state = S.Recover; stateT = 0; CameraRig.I?.Shake(0.15f); }
                    }
                    else if (def.brain == EnemyBrain.Hopper)
                    {
                        float k = Mathf.Clamp01(stateT / 0.3f);
                        Pos = WorldStreamer.I.Move(Pos, strikeDir * 7f * dt, radius);
                        if (k >= 1) { Land(p); state = S.Recover; stateT = 0; }
                    }
                    else { state = S.Recover; stateT = 0; }
                    break;

                case S.Recover:
                    if (stateT >= RecoverTime) { state = S.Chase; stateT = 0; }
                    break;
            }
        }

        void MoveToward(Vector2 target, float speed, float dt)
        {
            Vector2 to = target - Pos;
            if (to.sqrMagnitude < 0.01f) return;
            var before = Pos;
            Pos = WorldStreamer.I.Move(Pos, to.normalized * speed * dt, radius);
            // 막혔으면 옆으로 비켜가기
            if ((Pos - before).sqrMagnitude < speed * dt * speed * dt * 0.1f)
                Pos = WorldStreamer.I.Move(Pos, to.normalized.Rotate(GetInstanceID() % 2 == 0 ? 75 : -75) * speed * dt, radius);
            if (Mathf.Abs(to.x) > 0.05f) Facing = to.normalized;
        }

        void Separate(float dt)
        {
            foreach (var a in All)
            {
                if (a == this || a == null || !a.Alive || a.team != Team.Enemy) continue;
                Vector2 d = Pos - a.Pos;
                float min = radius + a.radius;
                if (d.sqrMagnitude < min * min && d.sqrMagnitude > 0.0001f)
                    Pos = WorldStreamer.I.Move(Pos, d.normalized * (min - d.magnitude) * 4f * dt, radius);
            }
        }

        void BeginWindup(Player p)
        {
            state = S.Windup; stateT = 0;
            attackCd = def.attackCooldown * Random.Range(0.85f, 1.2f);
            strikeDir = (p.Pos - Pos).SafeNormal(Facing);
            strikeTarget = p.Pos;
            Facing = strikeDir;
            if (def.boss) PickBossAttack(p);
            warn.enabled = true;
            if ((def.attack != EnemyAttack.Default && !def.boss) || (def.boss && bossAttack >= 4))
            {
                if (def.boss) BeginBossSignature(p); else BeginSignature(p);
                Sfx.Play("charge", 0.15f, 1.5f);
                return;
            }
            float r = def.brain switch
            {
                EnemyBrain.Ranged or EnemyBrain.Caster => 0.9f,
                EnemyBrain.Charger => 0.8f,
                EnemyBrain.Hopper => 1.2f,
                EnemyBrain.Boss => bossAttack == 2 ? 3.2f : bossAttack == 1 ? 1.2f : def.attackRange + 0.4f,
                _ => def.attackRange * 0.9f,
            };
            warn.sprite = Art.WarningCircle(Mathf.RoundToInt(r * Art.PPU));
            if (def.brain == EnemyBrain.Hopper) strikeTarget = p.Pos;
            Sfx.Play("charge", 0.15f, 1.5f);
        }

        void UpdateWarn()
        {
            float k = stateT / Windup;
            if ((def.attack != EnemyAttack.Default && !def.boss) || (def.boss && bossAttack >= 4)) { UpdateSignatureWarn(k); return; }
            Vector2 at = def.brain switch
            {
                EnemyBrain.Ranged or EnemyBrain.Caster => strikeTarget,
                EnemyBrain.Hopper => strikeTarget,
                EnemyBrain.Charger => Pos + strikeDir * 1.2f,
                EnemyBrain.Boss => bossAttack == 2 ? (Vector2)Pos : bossAttack == 1 ? Pos + strikeDir * 1.5f : Pos + strikeDir * def.attackRange * 0.5f,
                _ => Pos + strikeDir * def.attackRange * 0.5f,
            };
            warn.transform.position = at;
            warn.color = new Color(1f, 0.25f, 0.3f, 0.25f + k * 0.55f);
            warn.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, MathX.EaseOutCubic(k));
            squash = Mathf.Lerp(0, 0.4f, k);  // 몸을 웅크림
        }

        void DoStrike()
        {
            warn.enabled = false;
            squash = -0.5f;
            var p = Player.I;
            if (def.boss && bossAttack >= 4) { StartBossSignature(p); return; }
            if (def.attack != EnemyAttack.Default && !def.boss) { StartSignature(p); return; }
            switch (def.brain)
            {
                case EnemyBrain.Melee:
                case EnemyBrain.Swarm:
                {
                    Knockback(strikeDir, 6);
                    Fx.I?.Play(Art.Slash(Pal.Hex("ff8a8a"), Pal.White, Mathf.RoundToInt(def.attackRange * Art.PPU * 0.8f), 120, 1), Pos + Vector2.up * 0.4f, strikeDir.Angle(), 30, 0.9f, null, true);
                    if (p != null && Vector2.Distance(p.Pos, Pos + strikeDir * def.attackRange * 0.5f) < def.attackRange * 0.75f + p.radius) HitPlayer(p, 1, strikeDir, 4);
                    Sfx.Play("swing_light", 0.4f, 0.8f);
                    break;
                }
                case EnemyBrain.Ranged:
                case EnemyBrain.Caster:
                {
                    Vector2 o = Pos + Vector2.up * 0.5f;
                    Vector2 dir = (strikeTarget + Vector2.up * 0.3f - o).SafeNormal(strikeDir);
                    int n = def.brain == EnemyBrain.Caster ? 3 : 1;
                    for (int i = 0; i < n; i++)
                    {
                        var d = dir.Rotate((i - (n - 1) / 2f) * 15);
                        var pr = Projectile.Spawn(Art.EnemyShot(def.projectileColor), o, d * (def.brain == EnemyBrain.Caster ? 6f : 9f), Team.Player, this,
                            new DamageInfo { amount = def.damage, source = this, direction = d, knockback = 3, element = def.element });
                        pr.life = 1.6f; pr.color = def.projectileColor; pr.radius = 0.2f;
                    }
                    Sfx.Play("shoot", 0.3f, 1.3f);
                    break;
                }
                case EnemyBrain.Charger:
                    Sfx.Play("dash", 0.6f, 0.6f);
                    break;
                case EnemyBrain.Hopper:
                    strikeDir = (strikeTarget - Pos).SafeNormal(strikeDir) * Mathf.Min(1.2f, Vector2.Distance(strikeTarget, Pos) / 2.1f);
                    break;
                case EnemyBrain.Boss:
                    BossStrike(p);
                    break;
            }
        }

        void Land(Player p)
        {
            squash = 0.7f;
            Fx.I?.Burst(Pos, Pal.Hex("6fe0b8"), 8, 4, 0.4f, 0, 2, 360, 0, true);
            if (p != null && Vector2.Distance(p.Pos, Pos) < 1.2f + p.radius) HitPlayer(p, 1, (p.Pos - Pos).normalized, 4);
        }

        void HitPlayer(Player p, float mult, Vector2 dir, float kb)
        {
            if (p.TakeDamage(new DamageInfo { amount = def.damage * mult, source = this, direction = dir, knockback = kb, hitstop = 0.05f, element = def.element }) > 0)
                Fx.I?.HitSpark(p.Pos + Vector2.up * 0.5f, dir, Pal.Blood, false);
        }

        // ───────────── 보스 패턴 ─────────────

        void BossIntro()
        {
            Sfx.Play("boss_roar");
            CameraRig.I?.Shake(0.5f);
            Game.I?.ShowBoss(this);
            if (def.lines != null && def.lines.Length > 0) GameEvents.Notify($"{def.name}: \"{def.lines[0]}\"");
        }

        void PickBossAttack(Player p)
        {
            if (bossPhase == 0 && hp < maxHp * 0.5f)
            {
                bossPhase = 1;
                Sfx.Play("boss_roar");
                if (def.lines != null && def.lines.Length > 1) GameEvents.Notify($"{def.name}: \"{def.lines[1]}\"");
                Fx.I?.Play(Art.Shockwave(Pal.Blood, 40), Pos, 0, 18, 1, null, true);
            }
            float d = Vector2.Distance(p.Pos, Pos);
            // Every boss has its own signature move (4+), used more in phase 2; never the same signature twice in a row
            float sig = bossPhase > 0 ? 0.45f : 0.25f;
            if (lastBossAttack < 4 && Random.value < sig) bossAttack = BossSignature(d);
            else if (d > 3.5f) bossAttack = Random.value < 0.6f ? 1 : 3;    // 돌진 또는 탄막
            else bossAttack = Random.value < 0.5f ? 0 : 2;              // 베기 또는 내려찍기
            lastBossAttack = bossAttack;
        }

        void BossStrike(Player p)
        {
            Color32 col = def.projectileColor;
            switch (bossAttack)
            {
                case 0: // 넓은 베기
                    Fx.I?.Play(Art.Slash(Pal.Hex("ff8a8a"), Pal.White, 44, 200, 1), Pos + Vector2.up * 0.6f, strikeDir.Angle(), 28, 1.2f, null, true);
                    if (p != null && Vector2.Distance(p.Pos, Pos) < def.attackRange + 0.6f && Vector2.Angle(p.Pos - Pos, strikeDir) < 110) HitPlayer(p, 1.2f, strikeDir, 6);
                    Sfx.Play("swing_heavy", 0.7f);
                    break;
                case 1: // 돌진 (Strike 상태에서 이동)
                    Sfx.Play("dash", 0.8f, 0.5f);
                    break;
                case 2: // 내려찍기 충격파
                    Fx.I?.Play(Art.Shockwave(col, 52), Pos, 0, 20, 1, null, true);
                    Fx.I?.Burst(Pos, Pal.Hex("cdbba5"), 24, 8, 0.6f, 0, 2, 360, 0, true);
                    CameraRig.I?.Shake(0.45f);
                    Sfx.Play("explode", 0.9f);
                    if (p != null && Vector2.Distance(p.Pos, Pos) < 3.2f) HitPlayer(p, 1.4f, (p.Pos - Pos).normalized, 8);
                    break;
                case 3: // 원형 탄막
                {
                    int n = bossPhase > 0 ? 16 : 10;
                    float off = Random.Range(0, 360f);
                    for (int i = 0; i < n; i++)
                    {
                        var d = MathX.Dir(off + i * 360f / n);
                        var pr = Projectile.Spawn(Art.EnemyShot(col), Pos + Vector2.up * 0.6f, d * 5.5f, Team.Player, this,
                            new DamageInfo { amount = def.damage * 0.8f, source = this, direction = d, knockback = 3, element = def.element });
                        pr.life = 2.4f; pr.color = col; pr.radius = 0.22f;
                    }
                    Sfx.Play("shoot", 0.6f, 0.7f);
                    break;
                }
            }
        }

        // ───────────── 피해 / 사망 ─────────────

        protected override void OnDamaged(DamageInfo d, float amount)
        {
            aggro = true;
            if (state == S.Idle) { state = S.Chase; stateT = 0; if (def.boss) BossIntro(); }
            // 강한 타격은 공격 예고를 끊는다 (보스 제외)
            if (!def.boss && state == S.Windup && d.knockback >= 5) { state = S.Recover; stateT = 0; warn.enabled = false; CancelSignature(); }
        }

        protected override void Die(DamageInfo killer)
        {
            warn.enabled = false;
            CancelSignature();
            Sfx.Play("enemy_die", 0.6f);
            Fx.I?.Burst(Pos + Vector2.up * 0.4f, Pal.White, 12, 6, 0.45f);
            Fx.I?.Burst(Pos + Vector2.up * 0.4f, Pal.Hex("ffb3d1"), 8, 4, 0.5f, 0, 2, 360, 0, true);
            Fx.I?.Play(Art.Shockwave(Pal.White, Mathf.RoundToInt(radius * 24)), Pos, 0, 26, 1, null, true);
            if (def.boss)
            {
                GameTime.SlowMo(1.5f, 0.2f);
                CameraRig.I?.Shake(0.8f);
                if (def.lines != null && def.lines.Length > 2) GameEvents.Notify($"{def.name}: \"{def.lines[2]}\"");
            }
            Game.I?.OnEnemyKilled(this);
            // 사라지는 연출: 납작해지며 깜빡임
            StartCoroutine(DeathAnim());
        }

        System.Collections.IEnumerator DeathAnim()
        {
            float t = 0;
            shadow.enabled = false;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                visual.localScale = new Vector3(1 + t * 2, Mathf.Max(0.05f, 1 - t * 4), 1);
                body.color = new Color(1, 1, 1, 1 - t * 4);
                yield return null;
            }
            Destroy(gameObject);
        }

        void Animate(float dt)
        {
            if (!Alive) return;
            if (spawnT > 0)
            {
                float k = 1 - spawnT / 0.35f;
                visual.localScale = Vector3.one * MathX.EaseOutBack(k);
                return;
            }
            animT += dt * (state == S.Chase ? 2.2f : 1.2f);
            int frame = (int)(animT * 3) % 2;
            if (state == S.Windup) frame = 2;
            body.sprite = Art.Enemy(def.sprite, frame == 2 ? 2 : frame);
            if (Mathf.Abs(Facing.x) > 0.05f) body.flipX = Facing.x < 0;
            if (state == S.Windup && (int)(stateT * 16) % 2 == 0) flash = Mathf.Max(flash, 0.35f);
            if (state == S.Stunned) visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 20) * 6);
            else visual.localRotation = Quaternion.identity;
        }
    }
}
