using System.Collections;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Zone mechanics (EnemyDef.mods, put on by ZoneMap.Apply). Each one is a problem a certain build answers well,
    /// and each one is readable: blocked hits say so, explosions are telegraphed, miasma drifts visibly.
    /// </summary>
    public partial class Enemy
    {
        int shieldHits;             // hit shield left (refills after a pause in hits)
        float shieldIdle, thornsCd, miasmaT, barrageT, skittishCd;
        bool barrageWarned, burstPending;
        static ZoneRulesDef Rules => DB.ZoneRules;
        static readonly Color32 ShieldCol = new Color32(0x8f, 0xe3, 0xff, 0xff);
        static readonly Color32 MiasmaCol = new Color32(0xa8, 0x8c, 0xc8, 0xff);
        static readonly Color32 BarrageCol = new Color32(0xd6, 0xc8, 0xff, 0xff);

        void InitZone()
        {
            var m = def.mods;
            if (m == null) return;
            shieldHits = m.shieldHits;
            barrageT = Random.Range(1.2f, Rules.barrageInterval);
            miasmaT = Random.Range(0f, 0.5f);
        }

        /// <summary>Its projectile: the zone's drawn shot when it has one, else a dot in its color.</summary>
        Sprite ShotSprite => Art.HandSprite(def.shot) ?? Art.EnemyShot(def.projectileColor);

        /// <summary>Shielded monsters look icy blue until the shield breaks.</summary>
        Color ZoneTint => def.mods != null && shieldHits > 0 ? new Color(0.78f, 0.93f, 1f) : Color.white;

        void Provoke()
        {
            aggro = true;
            if (state == S.Idle) { state = S.Chase; stateT = 0; if (def.boss) BossIntro(); }
        }

        public override float TakeDamage(DamageInfo d)
        {
            var m = def?.mods;
            if (m == null || !Alive || d.source == null || d.source.team != Team.Player) return base.TakeDamage(d);
            bool ranged = Vector2.Distance(d.source.Pos, Pos) > 3f;
            if (!d.noEvents)
            {
                // Deflect: hits from range glance off while it isn't busy attacking (melee answers it)
                if (m.deflect > 0 && ranged && (state is S.Idle or S.Chase or S.Recover) && Random.value < m.deflect)
                {
                    Provoke();
                    flash = 0.6f;
                    Fx.I?.Number(Pos + Vector2.up * 1.0f, "튕김", new Color(0.85f, 0.85f, 0.9f), 0.8f, 0.5f);
                    Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.White, 5, 4, 0.25f);
                    Sfx.Play("guard", 0.35f, 1.3f);
                    return 0;
                }
                // Hit shield: each hit (however small) takes one layer off (attack speed answers it)
                if (shieldHits > 0)
                {
                    shieldHits--;
                    shieldIdle = 0;
                    Provoke();
                    flash = 0.6f;
                    Fx.I?.Burst(Pos + Vector2.up * 0.5f, ShieldCol, shieldHits > 0 ? 4 : 14, shieldHits > 0 ? 2.5f : 5f, 0.3f);
                    Fx.I?.Number(Pos + Vector2.up * 1.0f, shieldHits > 0 ? "보호막 " + shieldHits : "보호막 파괴", ShieldCol, shieldHits > 0 ? 0.7f : 0.95f, 0.5f);
                    Sfx.Play(shieldHits > 0 ? "guard" : "hit_metal", shieldHits > 0 ? 0.25f : 0.6f, 1.5f);
                    return 0;
                }
            }
            // Armor: every hit loses a flat amount (big hits answer it). Bosses carry a much smaller share of their huge HP.
            if (m.armor > 0)
            {
                float armor = maxHp * m.armor * (def.boss ? 0.12f : 1f);
                d.amount = Mathf.Max(d.amount - armor, d.amount * 0.25f);
            }
            float dealt = base.TakeDamage(d);
            if (dealt > 0 && !d.noEvents)
            {
                shieldIdle = 0;
                // Thorns: melee hits prick back (range answers it). Chip damage: no hurt invulnerability for the hero.
                if (m.thorns > 0 && !ranged && thornsCd <= 0 && d.source is Player pl)
                {
                    thornsCd = 0.3f;
                    pl.TakeChipDamage(def.damage * m.thorns, this, Pal.Hex("ffb35a"));
                    Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.Hex("ffb35a"), 5, 4, 0.25f, 0, 1, 50, (pl.Pos - Pos).Angle());
                }
            }
            return dealt;
        }

        void TickZone(float dt)
        {
            var m = def.mods;
            if (m == null || spawnT > 0) return;
            if (m.shieldHits > 0 && shieldHits < m.shieldHits)
            {
                shieldIdle += dt;
                if (shieldIdle >= Rules.shieldRefill)
                {
                    shieldHits = m.shieldHits; shieldIdle = 0;
                    Fx.I?.Burst(Pos + Vector2.up * 0.5f, ShieldCol, 10, 2, 0.4f);
                }
            }
            thornsCd -= dt;
            var p = Player.I;
            if (p == null || !p.Alive || !aggro || HasStatus("freeze")) return;
            float dist = Vector2.Distance(p.Pos, Pos);

            // Miasma: a cloud that hurts anyone close, no matter how well they dodge (toughness answers it)
            if (m.miasma > 0)
            {
                miasmaT -= dt;
                if (miasmaT <= 0)
                {
                    miasmaT = 0.5f;
                    float r = Rules.miasmaRadius;
                    var puff = Art.HandSprite(m.moteFx);
                    if (puff != null) Fx.I?.Pop(puff, Pos + Random.insideUnitCircle * r * 0.8f, 1.1f, 0.6f, 1.4f, 0, false, new Vector2(0, 0.5f));
                    else Fx.I?.Burst(Pos + Random.insideUnitCircle * r * 0.8f, MiasmaCol, 3, 0.5f, 0.9f, -0.6f, 2, 360, 0, false, true);
                    if (dist < r + p.radius) p.TakeChipDamage(def.damage * m.miasma * 0.5f, this, MiasmaCol);
                }
            }

            // Barrage: a slow ring of shots every few seconds, flashed beforehand (dodging answers it)
            if (m.barrage > 0 && (state is S.Chase or S.Recover) && dist < def.aggroRange * 1.4f)
            {
                barrageT -= dt;
                if (barrageT <= 0.5f && !barrageWarned)
                {
                    barrageWarned = true;
                    flash = Mathf.Max(flash, 0.8f);
                    squash = 0.4f;
                    Sfx.Play("charge", 0.2f, 1.8f);
                }
                if (barrageT <= 0)
                {
                    barrageWarned = false;
                    barrageT = Rules.barrageInterval / Mathf.Lerp(0.6f, 1f, m.barrage) * Random.Range(0.85f, 1.15f);
                    FireBarrage(m.barrage);
                }
            }

            // Skittish: leaps away when the hero closes in (speed answers it)
            if (m.skittish > 0 && state == S.Chase)
            {
                skittishCd -= dt;
                if (skittishCd <= 0 && dist < 2.2f)
                {
                    skittishCd = Rules.skittishCooldown / Mathf.Lerp(0.6f, 1f, m.skittish);
                    var away = (Pos - p.Pos).SafeNormal(Vector2.right).Rotate(Random.Range(-40f, 40f));
                    Knockback(away, 30f * Mathf.Max(0.3f, mass));
                    squash = -0.5f;
                    Fx.I?.Burst(Pos, Pal.Hex("cdbba5"), 6, 3, 0.3f, 0, 2, 70, (-away).Angle(), true);
                    Sfx.Play("dash", 0.25f, 1.4f);
                }
            }
        }

        void FireBarrage(float strength)
        {
            int n = Mathf.Max(5, Mathf.RoundToInt(Rules.barrageShots * Mathf.Lerp(0.6f, 1f, strength))) + (def.boss ? 6 : 0);
            float off = Random.Range(0f, 360f);
            var o = Pos + Vector2.up * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var d = MathX.Dir(off + i * 360f / n);
                var pr = Projectile.Spawn(string.IsNullOrEmpty(def.shot) ? Art.EnemyShot(BarrageCol) : ShotSprite, o, d * 4.2f, Team.Player, this,
                    new DamageInfo { amount = def.damage * 0.7f, source = this, direction = d, knockback = 2.5f, element = def.element });
                pr.life = 3f; pr.color = BarrageCol; pr.radius = 0.2f;
            }
            SignaturesFired.Add("ZoneBarrage");
            Sfx.Play("shoot", 0.4f, 1.1f);
        }

        /// <summary>On death: a telegraphed blast (range answers it) and/or small copies (area damage answers it).</summary>
        void ZoneOnDeath()
        {
            var m = def.mods;
            if (m == null) return;
            if (m.deathBurst > 0) { burstPending = true; StartCoroutine(DeathBurst(def.damage * m.deathBurst)); }
            if (m.brood > 0 && !def.boss) { BroodPending = true; StartCoroutine(BroodNextFrame(m.brood)); }
        }

        /// <summary>
        /// Copies about to hatch: the camp must not count as cleared yet. They hatch a frame later because the kill can happen
        /// inside a loop over Actor.All (projectiles, area hits), which a new actor would break.
        /// </summary>
        public bool BroodPending { get; private set; }

        IEnumerator BroodNextFrame(int n)
        {
            yield return null;
            SpawnBrood(n);
            BroodPending = false;
        }

        IEnumerator DeathBurst(float damage)
        {
            float r = Rules.deathBurstRadius, delay = 0.75f, t = 0;
            var at = Pos;
            warn.sprite = Art.WarningCircle(Mathf.RoundToInt(r * Art.PPU));
            warn.transform.position = at;
            warn.enabled = true;
            while (t < delay)
            {
                t += Time.deltaTime;
                float k = t / delay;
                warn.color = new Color(1f, 0.45f, 0.2f, 0.25f + k * 0.6f);
                warn.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, MathX.EaseOutCubic(k));
                yield return null;
            }
            warn.enabled = false;
            SignaturesFired.Add("ZoneDeathBurst");
            var blast = Art.HandSprite(def.mods?.burstFx);
            if (blast != null) Fx.I?.Pop(blast, at + Vector2.up * 0.3f, 0.45f, 0.5f, r * 2f * Art.PPU / Mathf.Max(8f, blast.rect.width), 0, true);
            else Fx.I?.Play(Art.Shockwave(Pal.Hex("ff8a3d"), Mathf.RoundToInt(r * Art.PPU)), at, 0, 22, 1, null, true);
            Fx.I?.Burst(at + Vector2.up * 0.3f, Pal.Hex("ffb35a"), 16, 6, 0.5f, 0, 2, 360, 0, false, true);
            Sfx.Play("explode", 0.6f, 1.2f);
            CameraRig.I?.Shake(0.2f);
            var p = Player.I;
            if (p != null && p.Alive && Vector2.Distance(p.Pos, at) < r + p.radius)
                p.TakeDamage(new DamageInfo { amount = damage, source = this, direction = (p.Pos - at).SafeNormal(Vector2.up), knockback = 6, hitstop = 0.05f });
            burstPending = false;
        }

        void SpawnBrood(int n)
        {
            for (int i = 0; i < n; i++)
            {
                var d = def.Copy();
                d.mods = def.mods.Copy();
                d.mods.brood = 0; d.mods.deathBurst = 0;
                d.name = "새끼 " + def.name;
                d.elite = false;
                d.hp = def.hp * 0.3f; d.damage = def.damage * 0.6f; d.speed = def.speed * 1.15f;
                d.radius = def.radius * 0.75f; d.mass = def.mass * 0.6f;
                d.xp = def.xp * 0.2f; d.gold = def.gold * 0.2f;
                var at = WorldStreamer.I.NearestWalkable(Pos + Random.insideUnitCircle * 0.8f, d.radius);
                var e = Spawn(d, at, campId);
                e.aggro = true;
                e.spawnT = 0.15f;
                WorldStreamer.I.ChunkAt(at)?.Register(e);
            }
        }
    }
}
