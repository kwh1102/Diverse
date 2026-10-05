using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// The player (rabbit wanderer).
    /// - Right-click: pathfind to a point, or chase & auto-attack when an enemy is clicked (LoL style)
    /// - Left-click: basic attack toward the cursor (combo)
    /// - QWER: weapon-specific skills / Space: dash (toward the cursor)
    /// Hit-feel values come from WeaponDef.ComboStep.
    /// </summary>
    public partial class Player : Actor
    {
        public static Player I { get; private set; }

        public WeaponDef Weapon;
        public CostumeDef Costume;
        public readonly Stats Stats = new Stats();
        public AbilityRuntime Abilities;
        public Telemetry Telemetry = new Telemetry();
        public int Level = 1;
        public float Xp, XpToNext = 30;
        public int Gold, Kills, Potions = 2;
        public int[] Attrs = new int[4];
        public int UnspentAttr;
        public int PendingEvolutions;
        public string HeroName;
        public bool HasRaft;                       // can paddle across water (bought from a ferryman)
        public bool Ferrying;                      // riding the ferryman's raft right now
        public bool OnWater { get; private set; }
        /// <summary>How far from the origin this hero may go right now (0 = unlimited).</summary>
        public float Bound => Game.I != null && Game.I.World != null ? WorldGen.Bound(Level, Game.I.World.bossKills) : 0;
        public float Shield;
        float shieldUntil;

        // Movement
        readonly List<Vector2> path = new List<Vector2>();
        Actor attackTarget;
        float repathT;
        public Vector2 AimPoint;
        public Vector2 LastDashOrigin;
        public bool Moving { get; private set; }

        // Attack
        int comboIndex;
        float comboTimer;           // combo resets when this runs out
        float actionLock;           // can't act until this expires (attack recovery)
        float lungeT; Vector2 lungeVel;
        float weaponSwingT, weaponSwingDur; float weaponFrom, weaponTo;
        bool queuedAttack; Vector2 queuedDir;

        // Dash
        float dashT, dashCd;
        public float DashRechargeTime => 1.1f * Stats[StatId.DashCooldown];
        public float DashRechargeProgress => DashCharges >= DashChargesMax ? 1 : dashRecharge / DashRechargeTime;
        Vector2 dashVel;
        float ghostT;
        public int DashCharges = 2, DashChargesMax = 2;
        float dashRecharge;

        // Skills
        public readonly SkillState[] Skills = new SkillState[4];
        float lowHealthLatch;

        // Buffs
        readonly List<(StatId stat, float value, float until)> buffs = new List<(StatId, float, float)>();

        SpriteRenderer weaponSr, shieldSr, raftSr;
        Transform weaponPivot;
        float animT;
        float stepDustT;

        public static Player Spawn(CostumeDef costume, WeaponDef weapon, Vector2 pos)
        {
            var go = new GameObject("Player");
            var p = go.AddComponent<Player>();
            I = p;
            p.team = Team.Player;
            p.radius = 0.35f;
            p.Costume = costume;
            p.Weapon = weapon;
            p.Abilities = new AbilityRuntime(p);
            p.Pos = pos;
            p.AimPoint = pos + Vector2.right;
            p.SetupVisual();
            for (int i = 0; i < 4; i++) p.Skills[i] = new SkillState { id = weapon.skills[i], def = SkillDB.Get(weapon.skills[i]) };
            p.RecalcStats();
            p.hp = p.maxHp;
            GameEvents.Combat += p.OnCombatEvent;
            return p;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            GameEvents.Combat -= OnCombatEvent;
            if (I == this) I = null;
        }

        void SetupVisual()
        {
            body.sprite = Art.Rabbit(Costume, Pose.Idle0);
            SetShadowSize(14);
            weaponPivot = new GameObject("weaponPivot").transform;
            weaponPivot.SetParent(visual, false);
            weaponPivot.localPosition = Art.HandAnchor;
            var w = new GameObject("weapon");
            w.transform.SetParent(weaponPivot, false);
            weaponSr = Art.MakeRenderer(w, Art.Weapon(Weapon.kind), 0);
            if (Weapon.kind == WeaponKind.SwordShield)
            {
                var s = new GameObject("shield");
                s.transform.SetParent(visual, false);
                s.transform.localPosition = new Vector3(-0.15f, 0.42f, 0);
                shieldSr = Art.MakeRenderer(s, Art.Shield(), 0);
            }
        }

        // ───────────────────────── Stats ─────────────────────────

        public void RecalcStats()
        {
            float hpRatio = maxHp > 0 ? hp / maxHp : 1;
            Stats.ClearModifiers();
            Stats.SetBase(StatId.MaxHp, 100 + (Level - 1) * 8);
            Stats.SetBase(StatId.Attack, Weapon.baseDamage * (1 + (Level - 1) * 0.06f));
            Stats.SetBase(StatId.AttackSpeed, 1);
            Stats.SetBase(StatId.MoveSpeed, 4.6f * Weapon.moveSpeedMul);
            Stats.SetBase(StatId.CritChance, 0.05f);
            Stats.SetBase(StatId.CritDamage, 0.0f);
            Stats.SetBase(StatId.XpGain, 1);
            Stats.SetBase(StatId.GoldGain, 1);
            Stats.SetBase(StatId.DashCooldown, 1);
            Stats.SetBase(StatId.Regen, 0.3f);
            Costume.apply?.Invoke(Stats);
            for (int i = 0; i < 4; i++) AttrInfo.Apply(Stats, (Attr)i, Attrs[i]);
            Abilities?.ApplyStats(Stats);
            foreach (var b in buffs) if (Time.time < b.until) Stats.Mul(b.stat, b.value);
            if (Game.I != null && Game.I.World != null) Game.I.ApplyLegacy(Stats);
            maxHp = Stats[StatId.MaxHp];
            hp = Mathf.Clamp(hpRatio * maxHp, 0, maxHp);
        }

        public float AttackPower => Stats[StatId.Attack];
        public float MoveSpeed => Stats[StatId.MoveSpeed] * (1 - Slow);

        public float DamageMultiplierAgainst(Actor target, string tags)
        {
            float m = 1;
            if (target.HasStatus("mark")) m *= 1.25f;
            if (!string.IsNullOrEmpty(tags)) m *= Abilities.SynergyMul(tags);
            return m;
        }

        public float BonusCritAgainst(Actor target) => target.HasStatus("chill") ? 0.1f : 0;

        public void AddBuff(StatId stat, float value, float duration)
        {
            buffs.Add((stat, value, Time.time + duration));
            RecalcStats();
            Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.Gold, 8, 3, 0.5f, -3);
        }

        public void AddShield(float amount, float duration)
        {
            Shield = Mathf.Max(Shield, amount);
            shieldUntil = Time.time + duration;
            Fx.I?.Play(Art.Shockwave(Pal.Hex("8fe3ff"), 14), Pos + Vector2.up * 0.4f, 0, 20, 1, null, true);
            Sfx.Play("guard", 0.4f);
        }

        public void ReduceCooldowns(float fraction)
        {
            foreach (var s in Skills) s.cd *= 1 - fraction;
            dashCd *= 1 - fraction;
            DashCharges = Mathf.Min(DashChargesMax, DashCharges + 1);
        }

        public void GainXp(float amount)
        {
            Xp += amount * Stats[StatId.XpGain];
            while (Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                XpToNext = Mathf.Round(30 * Mathf.Pow(1.22f, Level - 1));
                UnspentAttr += 1;
                PendingEvolutions++;
                RecalcStats();
                hp = Mathf.Min(maxHp, hp + maxHp * 0.25f);
                Fx.I?.Play(Art.Shockwave(Pal.Gold, 24), Pos, 0, 18, 1, null, true);
                Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.Gold, 24, 6, 0.8f, -4);
                Fx.I?.Number(Pos + Vector2.up * 1.2f, "레벨 업!", new Color(1f, 0.9f, 0.4f), 1.3f, 1.2f);
                Sfx.Play("levelup");
                GameEvents.Notify($"레벨 {Level}! 진화가 준비되었다 ({Controls.KeyName(Act.Abilities)} 또는 자동)");
                Game.I?.RequestPrefetch();
            }
        }

        public void GainGold(int g)
        {
            int amount = Mathf.Max(1, Mathf.RoundToInt(g * Stats[StatId.GoldGain]));
            Gold += amount;
        }

        // ───────────────────────── Update ─────────────────────────

        protected override void Update()
        {
            float dt = Time.deltaTime;
            if (Alive && dt > 0 && !GameTime.Paused && Game.I != null && Game.I.State == GameState.Playing && !Ferrying)
            {
                HandleInput();
                UpdateMovement(dt);
                UpdateTimers(dt);
                Abilities.Tick(dt);
                var near = Actor.Nearest(Pos, 12, Team.Enemy);
                Telemetry.Tick(dt, near != null ? Vector2.Distance(near.Pos, Pos) : 99);
            }
            base.Update();
            Animate(dt);
        }

        void UpdateTimers(float dt)
        {
            comboTimer -= dt;
            if (comboTimer <= 0) comboIndex = 0;
            actionLock -= dt;
            dashCd -= dt;
            if (DashCharges < DashChargesMax)
            {
                dashRecharge += dt;
                if (dashRecharge >= DashRechargeTime) { dashRecharge = 0; DashCharges++; }
            }
            foreach (var s in Skills) if (s.cd > 0) s.cd -= dt;
            if (Shield > 0 && Time.time > shieldUntil) Shield = 0;
            hp = Mathf.Min(maxHp, hp + Stats[StatId.Regen] * dt);
            // Buff expiry
            bool changed = false;
            for (int i = buffs.Count - 1; i >= 0; i--) if (Time.time >= buffs[i].until) { buffs.RemoveAt(i); changed = true; }
            if (changed) RecalcStats();
            if (hp / maxHp <= 0.3f)
            {
                if (lowHealthLatch <= 0) { GameEvents.Raise(new CombatEvent { type = Trig.LowHealth, source = this, position = Pos }); lowHealthLatch = 1; }
            }
            else lowHealthLatch = 0;
            if (Time.time > invulnUntil && dashT <= 0 && CameraRig.I?.Vignette != null)
                CameraRig.I.Vignette.color.Override(Color.Lerp(Color.black, new Color(0.6f, 0, 0.1f), Mathf.Clamp01(1 - hp / maxHp / 0.35f)));
        }

        void HandleInput()
        {
            if (Game.I.UiCapturingMouse) return;
            AimPoint = CameraRig.I.MouseWorld();
            if (CameraRig.I != null)
                CameraRig.I.lookAhead = Vector2.ClampMagnitude((AimPoint - Pos) * 0.12f, 1.2f);

            // Right-click: move / target
            if (Controls.Held(Act.Move) && !Controls.Down(Act.Move))
            {
                repathT -= Time.deltaTime;
                if (repathT <= 0 && attackTarget == null) { repathT = 0.12f; SetDestination(AimPoint, false); }
            }
            if (Controls.Down(Act.Move))
            {
                var enemy = EnemyUnderCursor(AimPoint);
                if (enemy != null)
                {
                    attackTarget = enemy;
                    Fx.I?.Play(Art.ClickMarker(), enemy.Pos, 0, 14, 1, new Color(1, 0.4f, 0.4f));
                }
                else
                {
                    attackTarget = null;
                    if (!Game.I.TryInteractAt(AimPoint)) SetDestination(AimPoint, true);
                }
            }
            if (Controls.Down(Act.Stop)) { path.Clear(); attackTarget = null; }

            // Left-click: attack toward the cursor (queued while locked)
            if (Controls.Held(Act.Attack))
            {
                var dir = (AimPoint - (Pos + Vector2.up * 0.4f)).SafeNormal(Facing);
                if (actionLock > 0.08f) { queuedAttack = true; queuedDir = dir; }
                else if (actionLock <= 0) { path.Clear(); attackTarget = null; BasicAttack(dir); }
                else { queuedAttack = true; queuedDir = dir; }
            }

            if (Controls.Down(Act.Dash)) TryDash((AimPoint - Pos).SafeNormal(Facing));
            if (Controls.Down(Act.SkillQ)) CastSkill(0);
            if (Controls.Down(Act.SkillW)) CastSkill(1);
            if (Controls.Down(Act.SkillE)) CastSkill(2);
            if (Controls.Down(Act.SkillR)) CastSkill(3);
            if (Controls.Down(Act.Potion)) UsePotion();

            if (queuedAttack && actionLock <= 0)
            {
                queuedAttack = false;
                BasicAttack(queuedDir);
            }
        }

        static Actor EnemyUnderCursor(Vector2 p)
        {
            Actor best = null; float bd = 0.9f;
            foreach (var a in All)
            {
                if (a == null || !a.Alive || a.team != Team.Enemy) continue;
                float d = Vector2.Distance(a.Pos + Vector2.up * 0.4f, p) - a.radius * 0.5f;
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }

        public void SetDestination(Vector2 target, bool showMarker)
        {
            path.Clear();
            path.AddRange(PathFinder.Find(WorldStreamer.I, Pos, target, 4000, HasRaft, Bound));
            if (showMarker) Fx.I?.Play(Art.ClickMarker(), path.Count > 0 ? path[path.Count - 1] : target, 0, 14);
        }

        public void StopMoving() { path.Clear(); attackTarget = null; }

        void UpdateMovement(float dt)
        {
            Moving = false;
            if (dashT > 0)
            {
                dashT -= dt;
                Pos = WorldStreamer.I.Move(Pos, dashVel * dt, radius, HasRaft, Bound);
                ghostT -= dt;
                if (ghostT <= 0) { ghostT = 0.03f; Fx.I?.Afterimage(body, new Color(0.7f, 0.85f, 1f, 0.7f), 0.22f); }
                if (dashT <= 0) { GameEvents.Raise(new CombatEvent { type = Trig.DashEnd, source = this, position = Pos }); Telemetry.Record("DashEnd"); }
                return;
            }
            if (lungeT > 0)
            {
                lungeT -= dt;
                Pos = WorldStreamer.I.Move(Pos, lungeVel * dt, radius, HasRaft, Bound);
                lungeVel *= Mathf.Exp(-14 * dt);
            }
            if (actionLock > 0) return;

            // Chase target
            if (attackTarget != null)
            {
                if (!attackTarget.Alive) { attackTarget = null; path.Clear(); return; }
                float range = Weapon.attackRange;
                float d = Vector2.Distance(attackTarget.Pos, Pos);
                if (d <= range + attackTarget.radius)
                {
                    path.Clear();
                    BasicAttack((attackTarget.Pos + Vector2.up * 0.3f - (Pos + Vector2.up * 0.4f)).SafeNormal(Facing));
                    return;
                }
                repathT -= dt;
                if (repathT <= 0 || path.Count == 0) { repathT = 0.25f; path.Clear(); path.AddRange(PathFinder.Find(WorldStreamer.I, Pos, attackTarget.Pos, 1500, HasRaft, Bound)); }
            }

            if (path.Count > 0)
            {
                Vector2 next = path[0];
                Vector2 to = next - Pos;
                float step = MoveSpeed * dt;
                if (to.magnitude <= step + 0.02f)
                {
                    Pos = WorldStreamer.I.Move(Pos, to, radius, HasRaft, Bound);
                    path.RemoveAt(0);
                }
                else
                {
                    var before = Pos;
                    Pos = WorldStreamer.I.Move(Pos, to.normalized * step, radius, HasRaft, Bound);
                    if ((Pos - before).sqrMagnitude < step * step * 0.04f) { path.Clear(); }   // stuck → stop
                }
                Facing = to.SafeNormal(Facing);
                Moving = true;
                stepDustT -= dt;
                if (stepDustT <= 0) { stepDustT = 0.28f; Fx.I?.Burst(Pos, Pal.Hex("e8dcc8"), 2, 1.2f, 0.3f, 0, 1, 60, (-Facing).Angle()); }
            }
        }

        // ───────────────────────── Basic attack ─────────────────────────

        void BasicAttack(Vector2 dir)
        {
            if (actionLock > 0 || dashT > 0) return;
            var steps = Weapon.combo;
            var step = steps[comboIndex % steps.Length];
            bool finisher = comboIndex % steps.Length == steps.Length - 1;
            comboIndex++;
            float aspd = Stats[StatId.AttackSpeed];
            actionLock = (step.windup + step.recovery) / aspd;
            comboTimer = actionLock + Weapon.comboReset;
            Facing = dir;
            Telemetry.Record("Attack");
            if (step.projectile) Telemetry.Record("RangedAttack");
            GameEvents.Raise(new CombatEvent { type = Trig.Attack, source = this, position = Pos, direction = dir, tags = Weapon.tags });

            // Weapon swing animation
            float baseAngle = dir.Angle();
            if (step.thrust || step.projectile) { weaponFrom = baseAngle; weaponTo = baseAngle; }
            else if (step.spin) { weaponFrom = baseAngle; weaponTo = baseAngle + 360; }
            else { weaponFrom = baseAngle + step.arc * 0.5f * step.swingDir; weaponTo = baseAngle - step.arc * 0.5f * step.swingDir; }
            weaponSwingT = 0; weaponSwingDur = Mathf.Max(0.08f, step.windup / aspd + 0.08f);

            StartCoroutine(AttackRoutine(step, dir, finisher, aspd));
            foreach (var c in Clone.Active.ToArray()) if (c != null) c.Mimic(dir, step);
        }

        System.Collections.IEnumerator AttackRoutine(ComboStep step, Vector2 dir, bool finisher, float aspd)
        {
            // Windup: pull back slightly (anticipation)
            squash = -0.25f;
            if (step.windup > 0.1f) Sfx.Play("charge", 0.25f, 1.6f);
            yield return new WaitForSeconds(step.windup / aspd);
            if (!Alive) yield break;

            // Lunge forward
            lungeT = 0.12f;
            lungeVel = dir * step.lunge / 0.06f;
            squash = 0.35f;

            Vector2 origin = Pos + Vector2.up * 0.45f;
            Color32 col = Weapon.slashColor;
            var w = Weapon;
            float dmg = AttackPower * step.damage;
            Sfx.Play(w.sfxSwing, 0.7f);

            if (step.projectile)
            {
                // Recoil: the body gets pushed back
                Knockback(-dir, step.recoil * 30f);
                CameraRig.I?.Kick(-dir, step.kick);
                CameraRig.I?.Shake(step.shake * 0.6f * Game.Settings.shakeScale);
                Fx.I?.Burst(origin + dir * 0.6f, Pal.Hex("fff1b0"), 6, 6, 0.2f, 0, 1, 50, dir.Angle());
                for (int i = 0; i < step.projectileCount; i++)
                {
                    var d = dir.Rotate((i - (step.projectileCount - 1) / 2f) * step.projectileSpread);
                    var info = new DamageInfo { amount = dmg, source = this, direction = d, knockback = step.knockback, hitstop = step.hitstop, canCrit = true, tags = w.tags, element = w.element };
                    var pr = Projectile.Spawn(Art.Bolt(), origin + d * 0.5f, d * 19f, Team.Enemy, this, info);
                    pr.color = Pal.Hex("fff1b0"); pr.life = step.range / 19f; pr.shake = step.shake; pr.kick = step.kick; pr.sfx = w.sfxHit;
                }
            }
            else
            {
                // Slash effect
                int radiusPx = Mathf.RoundToInt(step.range * Art.PPU * 0.85f);
                if (step.thrust)
                    Fx.I?.Play(Art.Thrust(col, w.slashCore, Mathf.RoundToInt(step.range * Art.PPU)), origin, dir.Angle(), 32, step.fxScale, null, true, 0, transform);
                else
                    Fx.I?.Play(Art.Slash(col, w.slashCore, radiusPx, Mathf.Min(step.arc, 359), step.spin ? 1 : step.swingDir), origin, dir.Angle(), step.spin ? 28 : 32, step.fxScale, null, true, 0, transform, step.swingDir < 0 && !step.spin);
                if (step.spin) Fx.I?.Play(Art.Slash(col, w.slashCore, radiusPx, 359, -1), origin, dir.Angle() + 180, 28, step.fxScale, null, true, 0, transform);

                List<Actor> targets = step.thrust
                    ? new List<Actor>(Combat.Line(origin, origin + dir * step.range, 0.55f, Team.Enemy))
                    : new List<Actor>(Combat.Arc(origin, dir, step.range, step.arc, Team.Enemy));
                bool any = false;
                foreach (var a in targets)
                {
                    var el = AttackElement;
                    var info = new DamageInfo { amount = dmg, source = this, direction = (a.Pos - Pos).SafeNormal(dir), knockback = step.knockback, hitstop = step.hitstop, canCrit = true, tags = w.tags, element = el };
                    Combat.Hit(a, info, col, w.sfxHit, step.shake, step.kick);
                    if (el != Element.None && (el != w.element || Random.value < 0.2f)) Combat.ApplyElement(a, el, dmg * 0.5f, this, 0);
                    any = true;
                }
                if (any && w.kind == WeaponKind.Greatsword) Fx.I?.Burst(Pos + dir * 0.8f, Pal.Hex("cdbba5"), 8, 4, 0.4f, 0, 2, 120, dir.Angle(), true);
            }
            if (finisher)
            {
                Telemetry.Record("ComboFinish");
                GameEvents.Raise(new CombatEvent { type = Trig.ComboFinish, source = this, position = Pos, direction = dir, tags = w.tags });
            }
        }

        // ───────────────────────── Dash ─────────────────────────

        public void TryDash(Vector2 dir)
        {
            if (DashCharges <= 0 || dashT > 0) return;
            DashCharges--;
            dashRecharge = 0;
            path.Clear(); attackTarget = null;
            actionLock = 0; queuedAttack = false;
            LastDashOrigin = Pos;
            dashT = 0.16f;
            dashVel = dir * 22f;
            Facing = dir;
            invulnUntil = Time.time + 0.22f;
            Fx.I?.DustPuff(Pos, dir.x > 0);
            Fx.I?.Burst(Pos, Pal.Hex("e8dcc8"), 6, 4, 0.35f, 0, 2, 70, (-dir).Angle(), true);
            Sfx.Play("dash", 0.6f);
            squash = -0.5f;
            Telemetry.Record("Dash");

            // Perfect dodge: an enemy attack was about to land
            bool perfect = false;
            foreach (var a in All)
                if (a is Enemy e && e.Alive && e.IsAboutToHit(Pos)) { perfect = true; break; }
            GameEvents.Raise(new CombatEvent { type = Trig.Dash, source = this, position = Pos, direction = dir, tags = "DODGE" });
            if (perfect)
            {
                Telemetry.Record("PerfectDodge");
                GameTime.SlowMo(0.35f, 0.3f);
                Fx.I?.Number(Pos + Vector2.up * 1.2f, "완벽한 회피!", new Color(0.6f, 0.9f, 1f), 1.1f);
                Fx.I?.Play(Art.Shockwave(Pal.Hex("bfe3ff"), 22), Pos, 0, 20, 1, null, true);
                GameEvents.Raise(new CombatEvent { type = Trig.PerfectDodge, source = this, position = Pos, direction = dir, tags = "DODGE" });
            }
        }

        public void BlinkTo(Vector2 dst)
        {
            dst = WorldStreamer.I.NearestWalkable(dst, radius, 6, HasRaft);
            if (Bound > 0 && dst.magnitude > Bound && dst.magnitude > Pos.magnitude) return;
            Fx.I?.Afterimage(body, new Color(0.7f, 0.6f, 1f, 0.8f), 0.3f);
            Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.ShadowEl, 10, 4, 0.3f);
            Pos = dst;
            Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.ShadowEl, 10, 4, 0.3f);
            path.Clear();
        }

        // ───────────────────────── Skills ─────────────────────────

        void CastSkill(int i)
        {
            var s = Skills[i];
            if (s.def == null || s.cd > 0 || actionLock > 0.05f || dashT > 0) return;
            path.Clear(); attackTarget = null;
            s.cd = s.def.cooldown * (1 - Stats[StatId.CooldownReduction]);
            Vector2 dir = (AimPoint - (Pos + Vector2.up * 0.4f)).SafeNormal(Facing);
            Facing = dir;
            actionLock = s.def.lockTime;
            Telemetry.Record("SkillCast");
            SkillDB.Cast(this, s.def, dir, AimPoint);
            GameEvents.Raise(new CombatEvent { type = Trig.SkillCast, source = this, position = Pos, direction = dir, tags = Weapon.tags + " SKILL" });
        }

        Element imbue; float imbueUntil;
        public void Imbue(Element e, float duration) { imbue = e; imbueUntil = Time.time + duration; }
        public Element AttackElement => Time.time < imbueUntil ? imbue : Weapon.element;

        public void SetLunge(Vector2 vel, float time) { lungeVel = vel; lungeT = time; }
        public void SetDash(Vector2 vel, float time, bool invuln = true)
        {
            dashVel = vel; dashT = time; LastDashOrigin = Pos;
            if (invuln) invulnUntil = Time.time + time + 0.05f;
        }
        public void SetSwing(float from, float to, float dur) { weaponFrom = from; weaponTo = to; weaponSwingT = 0; weaponSwingDur = dur; }

        void UsePotion()
        {
            if (Potions <= 0 || hp >= maxHp) return;
            Potions--;
            Heal(maxHp * 0.4f);
            Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.Blood, 14, 3, 0.7f, -4);
            Sfx.Play("pickup");
        }

        // ───────────────────────── Damage / events ─────────────────────────

        public override float TakeDamage(DamageInfo d)
        {
            if (!Alive || Time.time < invulnUntil || dashT > 0) return 0;
            float amount = d.amount * (1 - Stats[StatId.Armor]);
            if (guarding && Vector2.Dot(-d.direction.normalized, Facing) > 0.2f)
            {
                amount *= 0.2f;
                GameEvents.Raise(new CombatEvent { type = Trig.Guard, source = this, target = d.source, position = Pos, tags = "SHIELD GUARD" });
                Telemetry.Record("Guard");
                Sfx.Play("guard", 0.8f);
                Fx.I?.HitSpark(Pos + Facing * 0.5f + Vector2.up * 0.4f, Facing, Pal.Hex("bfe3ff"), true);
                GameTime.Hitstop(0.08f);
                if (d.source != null) d.source.Knockback(d.source.Pos - Pos, 6);
                d.knockback *= 0.2f;
            }
            if (Shield > 0)
            {
                float absorbed = Mathf.Min(Shield, amount);
                Shield -= absorbed; amount -= absorbed;
                Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.Hex("8fe3ff"), 6, 3, 0.3f);
            }
            d.amount = amount;
            float before = hp;
            float dealt = base.TakeDamage(d);
            if (dealt > 0)
            {
                invulnUntil = Time.time + 0.45f;
                Sfx.Play("hurt", 0.8f);
                GameTime.Hitstop(0.06f);
                CameraRig.I?.Shake(0.35f * Game.Settings.shakeScale);
                if (Game.Settings.damageNumbers) Fx.I?.Number(Pos + Vector2.up * 0.9f, Mathf.RoundToInt(dealt).ToString(), new Color(1f, 0.35f, 0.4f), 1.1f);
                Telemetry.Record("Damaged");
                GameEvents.Raise(new CombatEvent { type = Trig.Damaged, source = this, target = d.source, position = Pos, amount = dealt });
                Game.I?.LastDamageSource(d.source);
            }
            return dealt;
        }

        public bool guarding;

        protected override void Die(DamageInfo killer)
        {
            Sfx.Play("death");
            GameTime.SlowMo(1.2f, 0.25f);
            Fx.I?.Burst(Pos + Vector2.up * 0.5f, Pal.White, 30, 6, 1.2f, -2);
            body.sprite = Art.Rabbit(Costume, Pose.Hurt);
            Game.I?.OnPlayerDied(killer);
        }

        void OnCombatEvent(CombatEvent e)
        {
            if (e.source != this && !(e.source is Clone)) return;
            switch (e.type)
            {
                case Trig.Hit: Telemetry.Record("Hit"); break;
                case Trig.Crit: Telemetry.Record("Crit"); break;
                case Trig.Kill: Telemetry.Record("Kill"); Kills++; break;
            }
            Abilities.Fire(e);
        }

        // ───────────────────────── Animation ─────────────────────────

        void Animate(float dt)
        {
            animT += dt;
            Pose pose;
            if (!Alive) pose = Pose.Hurt;
            else if (flash > 0.5f) pose = Pose.Hurt;
            else if (dashT > 0) pose = Pose.Run1;
            else if (Moving) pose = (Pose)((int)Pose.Run0 + (int)(animT * 10) % 4);
            else pose = (int)(animT * 2.2f) % 2 == 0 ? Pose.Idle0 : Pose.Idle1;
            body.sprite = Art.Rabbit(Costume, pose);
            UpdateRaft();
            bool left = Facing.x < -0.05f;
            if (Mathf.Abs(Facing.x) > 0.05f) body.flipX = left;

            // Weapon: swings during attacks, otherwise held at the side
            if (weaponSr != null)
            {
                float ang;
                if (weaponSwingT < weaponSwingDur)
                {
                    weaponSwingT += dt;
                    float k = MathX.EaseOutCubic(weaponSwingT / weaponSwingDur);
                    ang = Mathf.Lerp(weaponFrom, weaponTo, k);
                }
                else
                {
                    float idle = Weapon.kind == WeaponKind.Crossbow ? Facing.Angle() : (body.flipX ? 120 : 60) + Mathf.Sin(animT * 3) * 3;
                    if (Weapon.kind == WeaponKind.Crossbow && actionLock <= 0) idle = (AimPoint - Pos).Angle();
                    ang = idle;
                }
                weaponPivot.localRotation = Quaternion.Euler(0, 0, ang);
                bool behind = ang > 20 && ang < 160 && weaponSwingT >= weaponSwingDur;
                weaponSr.sortingOrder = body.sortingOrder + (behind ? -1 : 1);
                weaponSr.flipY = Mathf.Abs(Mathf.DeltaAngle(ang, 0)) > 90;
                weaponPivot.localPosition = new Vector3(body.flipX ? -Art.HandAnchor.x : Art.HandAnchor.x, Art.HandAnchor.y, 0);
            }
            if (shieldSr != null)
            {
                shieldSr.sortingOrder = body.sortingOrder + (guarding ? 2 : -1);
                shieldSr.transform.localPosition = guarding ? (Vector3)(Facing * 0.35f + Vector2.up * 0.4f) : new Vector3(body.flipX ? 0.15f : -0.15f, 0.42f, 0);
            }
            if (Shield > 0) body.color = Color.Lerp(body.color, new Color(0.7f, 0.95f, 1f), 0.3f + Mathf.Sin(Time.time * 10) * 0.1f);
            if (Time.time < invulnUntil && dashT <= 0 && Alive) body.enabled = (int)(Time.time * 20) % 2 == 0;
            else body.enabled = true;
        }

        /// <summary>Show the raft under the hero while on water.</summary>
        void UpdateRaft()
        {
            var w = WorldStreamer.I;
            OnWater = (HasRaft || Ferrying) && w != null && w.IsLoaded(Pos) && WorldGen.IsWater(w.GroundAtTile(Mathf.FloorToInt(Pos.x), Mathf.FloorToInt(Pos.y)));
            if (OnWater && raftSr == null)
            {
                var go = new GameObject("raft");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0, 0.1f, 0);
                raftSr = Art.MakeRenderer(go, Art.Prop("raft"), 0);
            }
            if (raftSr == null) return;
            raftSr.enabled = OnWater;
            raftSr.sortingOrder = body.sortingOrder - 1;
            raftSr.transform.localPosition = new Vector3(0, 0.1f + Mathf.Sin(animT * 2.5f) * 0.03f, 0);
            if (OnWater) visual.localPosition += new Vector3(0, 0.12f + Mathf.Sin(animT * 2.5f) * 0.03f, 0);
        }

        protected override bool CanSwim => HasRaft || Ferrying;
        protected override float MoveBound => Bound;

        public bool IsDashing => dashT > 0;
        public bool InAction => actionLock > 0;
    }
}
