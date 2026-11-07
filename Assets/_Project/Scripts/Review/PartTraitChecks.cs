#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>Offline integration checks against real actors, combat and cast snapshots. Scratch run only.</summary>
    public static class PartTraitChecks
    {
        public static void Run(Player p, Action<bool, string> check)
        {
            var saved = p.Abilities.Owned.ToArray(); var skills = p.Skills.ToArray();
            var position = p.Pos; float hp = p.hp, shield = p.Shield, invuln = p.invulnUntil;
            int charges = p.DashCharges;
            var target = Enemy.Spawn(DB.Enemy("shroom"), p.Pos + Vector2.right * 2, null);
            target.enabled = false; target.maxHp = target.hp = 10000; target.Facing = Vector2.right;
            var skill = new ActiveSkillData { slot = 0, steps = { new ActiveStep { delivery = "projectile" } } };
            for (int i = 0; i < 4; i++) p.Skills[i] = new SkillState { cd = 10 };
            void Equip(params string[] ids) { p.Abilities.Clear(); foreach (var id in ids) p.Abilities.Add(PartCatalog.Create(id)); }
            DamageInfo Hit(PartAttackState state, float distance = 0) => new DamageInfo { amount = 10, source = p, partAttack = state,
                tags = state.active ? "SKILL PROJECTILE" : "BASIC", travelled = distance };
            float Modified(PartAttackState state, float distance = 0)
            { var hit = Hit(state, distance); p.Abilities.ModifyPartHit(target, ref hit, out _, out _); return hit.amount; }
            try
            {
                check(PartCatalog.All.Length == 32 && PartCatalog.All.Select(d => d.mechanic).Distinct().Count() == 32, "32종 서로 다른 고유 능력 정의");
                var random = new System.Random(875);
                for (int level = 1; level < DB.Progression.skillLevels[0]; level++)
                    for (int sample = 0; sample < 100; sample++)
                        check(PartCatalog.Offer(random, 3, level).All(a => !PartCatalog.Find(a.ingredients[0].id).requiresActive), "초반 후보 액티브 전용 제외");
                check(PartCatalog.All.Count(d => !d.requiresActive) == 12, "초반 12종 / 해금 이후 전체 32종");
                check(PartCatalog.All.All(d => !PartTraits.Text(d, 1).Contains("{")), "32종 설명 수치 치환");
                var fusion = PartCatalog.Fuse(PartCatalog.Create("rune_scope"), PartCatalog.Create("long_reed"));
                var restored = PartCatalog.Normalize(JsonUtility.FromJson<AbilityDef>(JsonUtility.ToJson(fusion)));
                check(restored.ingredients.Count == 2 && restored.Explain().Contains("후속 탄") && restored.Explain().Contains("날아간"), "합성·저장 복원 후 서로 다른 투사체 능력 계승");
                p.Abilities.Clear(); p.Abilities.Add(PartCatalog.Create("focus_lens", 5)); p.Abilities.Add(PartCatalog.Create("focus_lens", 5));
                p.Abilities.Tick(1); check(p.Abilities.BeginActive(skill, target.Pos).firstDamage <= DB.Asset.abilityRules.def.partTraits.damageCap, "T5 중복 구성·고유 능력 강도 상한");

                Equip("sword_knot"); check(Modified(p.Abilities.BeginBasic(true)) > Modified(p.Abilities.BeginBasic(false)), "매듭 · 실제 평타 마무리 보너스");
                Equip("moon_charm"); var basic = p.Abilities.BeginBasic(false); var info = Hit(basic);
                p.Abilities.ModifyPartHit(target, ref info, out float rearCrit, out _);
                target.Facing = Vector2.left; info = Hit(basic); p.Abilities.ModifyPartHit(target, ref info, out float frontCrit, out _);
                check(rearCrit > frontCrit, "초승달 · 앞/뒤 치명타 조건 구분");
                target.Facing = Vector2.right;
                Equip("lucky_die"); int crits = 0;
                Action<CombatEvent> listen = e => { if (e.type == Trig.Crit && e.source == p) crits++; };
                GameEvents.Combat += listen;
                p.Stats.SetBase(StatId.CritChance, 0);
                try { for (int i = 0; i < 5; i++) { info = Hit(p.Abilities.BeginBasic(false)); info.canCrit = true; Combat.Hit(target, info, Pal.Gold, shake: 0, kick: 0); } }
                finally { GameEvents.Combat -= listen; }
                check(crits == 1, "주사위 · 실제 Combat.Hit에서 다섯 번째만 확정 치명타");
                Equip("razor_gem"); target.hp = target.maxHp; basic = p.Abilities.BeginBasic(false); float healthy = Modified(basic);
                target.hp = target.maxHp * .1f; check(Modified(basic) > healthy, "칼날 수정 · 낮은 적 체력 조건"); target.hp = target.maxHp;

                Equip("ward_buckle"); p.Shield = 0;
                GameEvents.Raise(CombatEvent.Of(Trig.PerfectDodge, p, p.Pos)); check(p.Shield > 0, "수호 버클 · 완벽 회피 보호막");
                p.Shield = 0; GameEvents.Raise(CombatEvent.Of(Trig.PerfectDodge, p, p.Pos)); check(p.Shield == 0, "보호막 재발동 대기");
                Equip("feather_ring"); p.DashCharges = 0; float beforeTravel = p.DashRechargeProgress;
                GameEvents.Raise(CombatEvent.Of(Trig.PerfectDodge, p, p.Pos)); check(p.DashCharges > 0 || p.DashRechargeProgress > beforeTravel, "깃털 고리 · Space 충전 실제 회복");
                Equip("vital_vial"); p.hp = p.maxHp * .5f; float beforeHp = p.hp;
                Combat.Hit(target, Hit(p.Abilities.BeginBasic(false)), Pal.Gold, shake: 0, kick: 0); check(p.hp > beforeHp, "생명의 약병 · 평타 실제 체력 회복");
                beforeHp = p.hp; Combat.Hit(target, Hit(p.Abilities.BeginBasic(false)), Pal.Gold, shake: 0, kick: 0); check(p.hp == beforeHp, "약병 재발동 대기");
                Equip("iron_plate"); p.Abilities.Tick(1); check(p.Abilities.IncomingMultiplier(target, 10) < 1, "철갑 · 제자리 버티기 방어");
                check(p.Abilities.IncomingMultiplier(target, 10) == 1, "철갑 다음 피격 1회 소비");
                Equip("heart_lock"); p.Shield = 0; p.hp = p.maxHp * .4f; p.Abilities.IncomingMultiplier(target, p.maxHp * .1f);
                check(p.Shield > 0, "심장 자물쇠 · 위험 체력 보호막");
                Equip("wind_boot"); p.Pos += Vector2.right * 4; p.Abilities.Tick(.1f); check(Modified(p.Abilities.BeginBasic(false)) > 10, "바람 장화 · 이동 충전 후 평타 강화");
                Equip("silk_grip"); p.Abilities.Tick(1); float speed = p.Stats[StatId.AttackSpeed];
                for (int i = 0; i < 3; i++) p.Abilities.PartHit(target, Hit(p.Abilities.BeginBasic(false)));
                check(p.Stats[StatId.AttackSpeed] > speed, "비단 · 정지 평타 중첩"); p.Pos += Vector2.right; p.Abilities.Tick(.1f);
                check(Mathf.Approximately(p.Stats[StatId.AttackSpeed], speed), "비단 · 이동 시 중첩 해제");
                Equip("breeze_ribbon"); speed = p.MoveSpeed; GameEvents.Raise(CombatEvent.Of(Trig.DashEnd, p, p.Pos)); check(p.MoveSpeed > speed, "산들바람 · 이동 종료 가속");

                Equip("focus_lens"); p.Abilities.Tick(1); check(Modified(p.Abilities.BeginActive(skill, target.Pos)) > 10, "집중 렌즈 · 정지 후 첫 적중");
                Equip("prism_core"); skill.steps[0].element = "Fire"; p.Abilities.BeginActive(skill, target.Pos); skill.steps[0].element = "Frost";
                check(Modified(p.Abilities.BeginActive(skill, target.Pos)) > 10, "분광 핵 · 다른 원소 연결"); skill.steps[0].element = "None";
                Equip("ruby_pin"); info = Hit(p.Abilities.BeginBasic(false)); info.crit = true; p.Abilities.PartHit(target, info);
                var active = p.Abilities.BeginActive(skill, target.Pos); info = Hit(active); p.Abilities.ModifyPartHit(target, ref info, out var rubyCrit, out _);
                check(rubyCrit > 0, "홍옥 핀 · 치명타 충전의 액티브 인계");
                Equip("clock_spring"); p.Skills[0].cd = 10; p.Abilities.BeginActive(skill, target.Pos); check(p.Skills[0].cd < 10, "태엽 · 쉬었다 쓴 스킬 대기시간 감소");
                Equip("silver_gear"); for (int i = 0; i < 3; i++) p.Abilities.PartHit(target, Hit(p.Abilities.BeginBasic(false)));
                p.Skills[0].cd = 10; p.Abilities.BeginActive(skill, target.Pos); check(p.Skills[0].cd < 10, "은빛 톱니 · 평타에서 액티브로 연결");
                Equip("echo_bell"); skill.slot = 0; p.Abilities.BeginActive(skill, target.Pos); skill.slot = 1; p.Skills[0].cd = 10;
                p.Abilities.BeginActive(skill, target.Pos); check(p.Skills[0].cd < 10, "메아리 · 직전의 다른 슬롯 회수"); skill.slot = 0;
                Equip("hourglass_bead"); active = p.Abilities.BeginActive(skill, target.Pos); p.Skills[0].cd = 10;
                p.Abilities.EndActive(active, true); float refunded = p.Skills[0].cd; p.Abilities.EndActive(active, true);
                check(refunded < 10 && p.Skills[0].cd == refunded, "모래시계 · 빗나간 스킬 한 번만 회수");
                active = p.Abilities.BeginActive(skill, target.Pos); p.Skills[0].cd = 10; p.Abilities.EndActive(active, false);
                check(p.Skills[0].cd == 10, "중단한 스킬은 빗나감 보상 제외");
                Equip("arrow_fletching", "falcon_sight", "spiral_nozzle"); active = p.Abilities.BeginActive(skill, target.Pos);
                var projectile = Projectile.Spawn(Art.Bolt(), p.Pos, Vector2.right, Team.Enemy, p, Hit(active)); projectile.enabled = false;
                p.Abilities.PreparePartProjectile(active, projectile, true); p.Abilities.SteerPartProjectile(active, projectile, .1f);
                check(projectile.pierce > 0 && projectile.homing > 0 && Mathf.Abs(projectile.vel.y) > 0, "화살깃·매눈·나선 · 실제 투사체 관통/유도/궤적");
                UnityEngine.Object.Destroy(projectile.gameObject);
                Equip("rune_scope"); active = p.Abilities.BeginActive(skill, target.Pos); float first = Modified(active); p.Abilities.PartHit(target, Hit(active));
                check(Modified(active) > first && Modified(p.Abilities.BeginActive(skill, target.Pos)) == first, "룬 조준경 · 동일 시전 후속 탄만 강화");
                Equip("long_reed"); active = p.Abilities.BeginActive(skill, target.Pos); check(Modified(active, 3) > Modified(active, 0), "갈대 관 · 실제 비행 거리 조건");
                Equip("trail_compass"); check(p.Abilities.BeginActive(skill, p.Pos + Vector2.right * 4).range > 0 && p.Abilities.BeginActive(skill, p.Pos).range == 0, "나침반 · 먼 조준점만 사거리 보너스");
                Equip("star_map"); active = p.Abilities.BeginActive(skill, target.Pos); p.Abilities.PartHit(target, Hit(active));
                check(p.Abilities.BeginActive(skill, target.Pos).range > 0, "별자리 · 적중 위치 재조준");
                Equip("amber_mirror"); p.Shield = 0; p.invulnUntil = 0; p.hp = p.maxHp;
                p.TakeDamage(new DamageInfo { amount = 10, source = target }); active = p.Abilities.BeginActive(skill, target.Pos);
                check(Modified(active) > 10, "호박 거울 · 받은 체력 피해의 수동 반격 인계");
                Equip("ember_crucible"); active = p.Abilities.BeginActive(skill, target.Pos); info = Hit(active); info.element = Element.Fire; p.Abilities.PartHit(target, info);
                check(target.HasStatus("burn"), "불씨 도가니 · 원소 액티브 적중의 화상");
                Equip("thunder_pin"); target.AddStatus("mark", 2, 0, 0, p, Pal.Gold); active = p.Abilities.BeginActive(skill, target.Pos); p.Abilities.PartHit(target, Hit(active));
                check(target.Stunned, "뇌전 침 · 표식 적 기절");
                Equip("frost_shard", "anchor_hook", "gravity_stone"); active = p.Abilities.BeginActive(skill, target.Pos);
                p.Abilities.PartControl(active, new ActiveStep { effect = "pull" }, target);
                check(target.HasStatus("freeze") && target.HasStatus("part:anchor") && p.Abilities.PartPullMultiplier(active) > 1, "서리·닻·중력 · 빙결/둔화/끌기 서로 다른 기능");
            }
            finally
            {
                UnityEngine.Object.Destroy(target.gameObject); p.Abilities.Clear(); foreach (var a in saved) p.Abilities.Restore(a); p.Abilities.Recompute();
                for (int i = 0; i < 4; i++) p.Skills[i] = skills[i];
                p.Pos = position; p.hp = hp; p.Shield = shield; p.invulnUntil = invuln; p.DashCharges = charges;
            }
        }
    }
}
#endif
