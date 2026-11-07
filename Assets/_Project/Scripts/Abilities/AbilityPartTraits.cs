using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public partial class AbilityRuntime
    {
        PartTraitRules T => DB.Asset.abilityRules.def.partTraits;
        float[] traitPower = new float[Enum.GetValues(typeof(PartMechanic)).Length];
        PartDef[] traitDefs = new PartDef[Enum.GetValues(typeof(PartMechanic)).Length];
        readonly Dictionary<PartMechanic, float> traitCooldowns = new Dictionary<PartMechanic, float>();
        Vector2 traitPosition, starPosition;
        float stillTime, walked, windUntil, rubyUntil, mirrorUntil, mirrorDamage, hasteUntil, starUntil;
        float lastTraitCastTime;
        int basicHits, gearHits, silkStacks, lastTraitSlot = -1;
        Element lastTraitElement;

        void RefreshTraits()
        {
            Array.Clear(traitPower, 0, traitPower.Length);
            foreach (var d in PartCatalog.All) traitDefs[(int)d.mechanic] = d;
            foreach (var a in Equipped) foreach (var i in a.ingredients)
            {
                var d = PartCatalog.Find(i.id); if (d == null || d.mechanic == PartMechanic.None) continue;
                int index = (int)d.mechanic;
                traitPower[index] = Mathf.Min(T.maxStrength, traitPower[index] + i.count / (float)PartCatalog.Units(a.tier) * PartCatalog.Rank(a));
            }
        }
        void ClearTraits()
        {
            Array.Clear(traitPower, 0, traitPower.Length); traitCooldowns.Clear();
            traitPosition = player.Pos; stillTime = walked = windUntil = rubyUntil = mirrorUntil = mirrorDamage = hasteUntil = starUntil = 0;
            basicHits = gearHits = silkStacks = 0; lastTraitSlot = -1; lastTraitElement = Element.None; lastTraitCastTime = 0;
        }
        PartDef Def(PartMechanic kind) => traitDefs[(int)kind];
        float Power(PartMechanic kind, PartAttackState attack = null) => (attack?.strengths ?? traitPower)[(int)kind];
        float Amount(PartMechanic kind, PartAttackState attack = null) => Power(kind, attack) * (Def(kind)?.amount ?? 0);
        bool Ready(PartMechanic kind)
        {
            if (Power(kind) <= 0 || traitCooldowns.TryGetValue(kind, out var until) && Time.time < until) return false;
            traitCooldowns[kind] = Time.time + Def(kind).cooldown; return true;
        }
        void TickTraits(float dt)
        {
            float moved = Vector2.Distance(traitPosition, player.Pos); traitPosition = player.Pos;
            if (moved > T.stationaryTolerance * dt && !player.BasicLunging) { stillTime = 0; silkStacks = 0; }
            else stillTime += dt;
            var boot = Def(PartMechanic.WindBoot);
            if (Power(PartMechanic.WindBoot) > 0 && boot != null)
            {
                walked = Mathf.Min(boot.distance, walked + moved);
                if (walked >= boot.distance) { windUntil = Time.time + boot.duration; walked = 0; }
            }
        }
        float TraitStat(StatId stat, float value)
        {
            if (stat == StatId.AttackSpeed) value *= 1 + Mathf.Min(T.speedCap, Amount(PartMechanic.SilkGrip) * silkStacks);
            if (stat == StatId.MoveSpeed && Time.time < hasteUntil) value *= 1 + Mathf.Min(T.speedCap, Amount(PartMechanic.BreezeRibbon));
            return value;
        }
        float TraitIncoming(float amount)
        {
            float reduction = 0;
            if (Power(PartMechanic.IronPlate) > 0 && stillTime >= Def(PartMechanic.IronPlate).duration && Ready(PartMechanic.IronPlate)) reduction = Amount(PartMechanic.IronPlate);
            var heart = Def(PartMechanic.HeartLock);
            if (Power(PartMechanic.HeartLock) > 0 && player.hp - amount <= player.maxHp * heart.threshold && Ready(PartMechanic.HeartLock))
                player.AddShield(player.AttackPower * Mathf.Min(T.shieldCap, Amount(PartMechanic.HeartLock)), heart.duration);
            return 1 - Mathf.Min(T.reductionCap, reduction);
        }
        void TraitEvent(CombatEvent e)
        {
            if (e.source != player) return;
            if (e.type == Trig.PerfectDodge)
            {
                if (Ready(PartMechanic.WardBuckle)) player.AddShield(player.AttackPower * Mathf.Min(T.shieldCap, Amount(PartMechanic.WardBuckle)), Def(PartMechanic.WardBuckle).duration);
                if (Ready(PartMechanic.FeatherRing)) player.RecoverTravel(Amount(PartMechanic.FeatherRing));
            }
            if (e.type == Trig.DashEnd && Ready(PartMechanic.BreezeRibbon)) hasteUntil = Time.time + Def(PartMechanic.BreezeRibbon).duration;
            if (e.type == Trig.Damaged && Power(PartMechanic.AmberMirror) > 0)
            {
                mirrorDamage = Mathf.Min(player.AttackPower * T.damageCap, e.amount * Amount(PartMechanic.AmberMirror));
                mirrorUntil = Time.time + Def(PartMechanic.AmberMirror).duration;
            }
        }
        public PartAttackState BeginBasic(bool finisher)
        {
            var attack = new PartAttackState { finisher = finisher, strengths = (float[])traitPower.Clone() };
            if (finisher) attack.damage += Amount(PartMechanic.SwordKnot, attack);
            if (Time.time < windUntil && Power(PartMechanic.WindBoot) > 0) { attack.firstDamage += Amount(PartMechanic.WindBoot, attack); windUntil = 0; }
            return attack;
        }
        public PartAttackState BeginActive(ActiveSkillData skill, Vector2 aim)
        {
            var attack = new PartAttackState { active = true, slot = skill.slot, strengths = (float[])traitPower.Clone(), canMiss = skill.HasTechnique ? ActiveTechniques.UsesShot(skill.technique) : skill.steps.Any(s => s.effect == "damage") };
            if (Power(PartMechanic.FocusLens) > 0 && stillTime >= Def(PartMechanic.FocusLens).duration) attack.firstDamage += Amount(PartMechanic.FocusLens, attack);
            Element element = skill.steps.Select(s => Enum.TryParse(s.element, out Element e) ? e : Element.None).FirstOrDefault(e => e != Element.None);
            if (element != Element.None && lastTraitElement != Element.None && element != lastTraitElement && Time.time - lastTraitCastTime <= Def(PartMechanic.PrismCore).duration)
                attack.damage += Amount(PartMechanic.PrismCore, attack);
            if (Time.time < rubyUntil && Power(PartMechanic.RubyPin) > 0) { attack.critChance += Amount(PartMechanic.RubyPin, attack); rubyUntil = 0; }
            if (Time.time < mirrorUntil && Power(PartMechanic.AmberMirror) > 0) { attack.flatFirst = mirrorDamage; mirrorUntil = mirrorDamage = 0; }
            float refund = 0;
            if (Power(PartMechanic.ClockSpring) > 0 && (lastTraitSlot < 0 || Time.time - lastTraitCastTime >= Def(PartMechanic.ClockSpring).duration)) refund += Amount(PartMechanic.ClockSpring, attack);
            if (Power(PartMechanic.SilverGear) > 0 && gearHits >= Def(PartMechanic.SilverGear).count) { refund += Amount(PartMechanic.SilverGear, attack); gearHits = 0; }
            player.Skills[skill.slot].cd *= 1 - Mathf.Min(T.refundCap, refund);
            if (lastTraitSlot >= 0 && lastTraitSlot != skill.slot && Time.time - lastTraitCastTime <= Def(PartMechanic.EchoBell).duration && Ready(PartMechanic.EchoBell))
                player.Skills[lastTraitSlot].cd *= 1 - Mathf.Min(T.refundCap, Amount(PartMechanic.EchoBell, attack));
            if (Power(PartMechanic.TrailCompass) > 0 && Vector2.Distance(player.Pos, aim) >= Def(PartMechanic.TrailCompass).distance) attack.range += Amount(PartMechanic.TrailCompass, attack);
            if (Power(PartMechanic.StarMap) > 0 && Time.time < starUntil && Vector2.Distance(aim, starPosition) <= Def(PartMechanic.StarMap).distance)
            { attack.range += Amount(PartMechanic.StarMap, attack); attack.firstDamage += Power(PartMechanic.StarMap, attack) * Def(PartMechanic.StarMap).secondary; starUntil = 0; }
            lastTraitSlot = skill.slot; lastTraitCastTime = Time.time; lastTraitElement = element;
            return attack;
        }
        public void EndActive(PartAttackState attack, bool completed)
        {
            if (attack == null || attack.closed) return; attack.closed = true;
            if (completed && attack.canMiss && attack.hits == 0 && Power(PartMechanic.HourglassBead, attack) > 0)
                player.Skills[attack.slot].cd *= 1 - Mathf.Min(T.refundCap, Amount(PartMechanic.HourglassBead, attack));
        }
        public void ModifyPartHit(Actor target, ref DamageInfo damage, out float critChance, out float critDamage)
        {
            critChance = critDamage = 0; var attack = damage.partAttack;
            if (attack == null || damage.noEvents) return;
            float bonus = attack.damage + (attack.hits == 0 ? attack.firstDamage : 0);
            critChance = attack.critChance; critDamage = attack.critDamage;
            if (!attack.active)
            {
                var moon = Def(PartMechanic.MoonCharm);
                if (Power(PartMechanic.MoonCharm, attack) > 0 && Vector2.Dot((player.Pos - target.Pos).normalized, target.Facing) <= moon.threshold) critChance += Amount(PartMechanic.MoonCharm, attack);
                if (Power(PartMechanic.LuckyDie, attack) > 0 && (basicHits + 1) % Def(PartMechanic.LuckyDie).count == 0)
                { critChance = 1; critDamage += Amount(PartMechanic.LuckyDie, attack); }
                if (Power(PartMechanic.RazorGem, attack) > 0 && target.hp <= target.maxHp * Def(PartMechanic.RazorGem).threshold) bonus += Amount(PartMechanic.RazorGem, attack);
            }
            else
            {
                if (damage.tags.Contains("PROJECTILE"))
                {
                    if (attack.targetHits.ContainsKey(target.GetInstanceID())) bonus += Amount(PartMechanic.RuneScope, attack);
                    if (Power(PartMechanic.LongReed, attack) > 0 && damage.travelled >= Def(PartMechanic.LongReed).distance) bonus += Amount(PartMechanic.LongReed, attack);
                }
            }
            critChance = critChance >= 1 ? 1 : Mathf.Min(T.critChanceCap, critChance);
            critDamage = Mathf.Min(T.critDamageCap, critDamage);
            damage.amount *= 1 + Mathf.Min(T.damageCap, bonus);
            if (attack.hits == 0) damage.amount += attack.flatFirst;
        }
        public void PartHit(Actor target, DamageInfo damage)
        {
            var attack = damage.partAttack; if (attack == null || damage.noEvents) return;
            bool first = attack.hits++ == 0;
            int key = target.GetInstanceID(); attack.targetHits[key] = attack.targetHits.TryGetValue(key, out int n) ? n + 1 : 1;
            if (damage.crit && Power(PartMechanic.RubyPin) > 0) rubyUntil = Time.time + Def(PartMechanic.RubyPin).duration;
            if (!attack.active)
            {
                basicHits++; if (Power(PartMechanic.SilverGear) > 0) gearHits = Mathf.Min(Def(PartMechanic.SilverGear).count, gearHits + 1);
                if (Power(PartMechanic.SilkGrip) > 0 && stillTime >= Def(PartMechanic.SilkGrip).duration) silkStacks = Mathf.Min(Def(PartMechanic.SilkGrip).count, silkStacks + 1);
                if (Ready(PartMechanic.VitalVial)) player.Heal((player.maxHp - player.hp) * Mathf.Min(T.healCap, Amount(PartMechanic.VitalVial)));
                return;
            }
            if (first)
            {
                if (Power(PartMechanic.StarMap, attack) > 0) { starPosition = target.Pos; starUntil = Time.time + Def(PartMechanic.StarMap).duration; }
                if (target.Alive && damage.element != Element.None && Power(PartMechanic.EmberCrucible, attack) > 0)
                    target.AddStatus("burn", Def(PartMechanic.EmberCrucible).duration, player.AttackPower * Mathf.Min(T.procDamageCap, Amount(PartMechanic.EmberCrucible, attack)), 0, player, Pal.Fire);
                if (target.Alive && target.statuses.Any(s => s.id == "mark" || s.id.StartsWith("active:")) && Power(PartMechanic.ThunderPin, attack) > 0 && Ready(PartMechanic.ThunderPin) && target is Enemy enemy)
                    enemy.Stun(Amount(PartMechanic.ThunderPin, attack));
            }
        }
        public float PartPullMultiplier(PartAttackState attack) => 1 + Mathf.Min(T.rangeCap, Amount(PartMechanic.GravityStone, attack));
        public void PartControl(PartAttackState attack, ActiveStep step, Actor target)
        {
            if (target == null || !target.Alive || attack == null) return;
            if (step.effect is "pull" or "push" && Power(PartMechanic.AnchorHook, attack) > 0)
                target.AddStatus("part:anchor", Def(PartMechanic.AnchorHook).duration, 0, Mathf.Min(T.slowCap, Amount(PartMechanic.AnchorHook, attack)), player, Pal.Frost);
            if (step.effect is "pull" or "push" or "stun" or "mark")
            {
                bool first = attack.controlHits++ == 0;
                if (first && Power(PartMechanic.FrostShard, attack) > 0 && Ready(PartMechanic.FrostShard) && target is Enemy enemy)
                {
                    float seconds = Amount(PartMechanic.FrostShard, attack); enemy.Stun(seconds);
                    target.AddStatus("freeze", seconds, 0, T.slowCap, player, Pal.Frost);
                }
            }
        }
        public void PreparePartProjectile(PartAttackState attack, Projectile projectile, bool first)
        {
            if (first && Power(PartMechanic.ArrowFletching, attack) > 0) projectile.pierce = Mathf.Min(ActiveSkillGenerator.Rules.maxTargets,
                projectile.pierce + Mathf.Max(1, Mathf.RoundToInt(Def(PartMechanic.ArrowFletching).count * Power(PartMechanic.ArrowFletching, attack))));
            projectile.homing = Mathf.Min(T.homingCap, Amount(PartMechanic.FalconSight, attack));
            projectile.radius += Mathf.Min(ActiveSkillGenerator.Rules.projectileRadius, Power(PartMechanic.SpiralNozzle, attack) * (Def(PartMechanic.SpiralNozzle)?.secondary ?? 0));
        }
        public void SteerPartProjectile(PartAttackState attack, Projectile projectile, float age)
        {
            float amplitude = Amount(PartMechanic.SpiralNozzle, attack);
            if (amplitude > 0) projectile.vel = projectile.vel.Rotate((Mathf.Sin(age * T.spiralFrequency) - Mathf.Sin(Mathf.Max(0, age - Time.deltaTime) * T.spiralFrequency)) * amplitude);
        }
    }
}
