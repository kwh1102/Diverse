#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class TechniqueChecks
    {
        public static IEnumerator Run(Player player, Vector2 center, Action<bool, string> check, Action<string> capture, string output)
        {
            var original = player.InventorySnapshot();
            var dir = Vector2.left;
            var rules = ActiveSkillGenerator.Rules;
            var all = ActiveTechniques.All;
            check(all.Length == 20 && rules.techniques.Length == all.Length &&
                all.All(r => r.Tuning.id == r.Id && r.Tuning.seconds > 0), "20 distinct technique rules are backed by tuning");
            var pictures = all.Select(r => TechniqueFx.Frames(r.Id, TechniqueFx.Phase.Resolve, Pal.White)[1].texture.EncodeToPNG());
            check(pictures.Select(Convert.ToBase64String).Distinct().Count() == 20,
                "20 technique pools have different rendered resolution frames with the same palette");
            string previews = Path.Combine(output, "TechniquePreviews");
            Directory.CreateDirectory(previews);
            foreach (var rule in all)
            {
                var color = rule.Weapon switch { WeaponType.Sword => Pal.Gold, WeaponType.Bow => Pal.Wind,
                    WeaponType.Gun => Pal.Fire, WeaponType.Staff => Pal.Frost, _ => Pal.ShadowEl };
                var frames = TechniqueFx.Frames(rule.Id, TechniqueFx.Phase.Resolve, color);
                File.WriteAllBytes(Path.Combine(previews, $"{(int)rule.Id:00}_{rule.Id}.png"), frames[1].texture.EncodeToPNG());
            }

            void EquipWeapon(WeaponType type)
            {
                var bag = InventoryState.Create(player.MovementType);
                var weapon = DB.W(type).Clone();
                var item = new InventoryItem { kind = ItemKind.Weapon, weapon = weapon, name = weapon.name };
                bag.Receive(item);
                bag.Select(item.id);
                player.RestoreInventory(bag);
                player.DebugTeleport(center);
            }
            ActiveSkillData Equip(Technique id, int slot = 0)
            {
                var item = player.Inventory.Attached(player.Inventory.Active).FirstOrDefault(i => i.kind == ItemKind.Skill && i.skillSlot == slot);
                if (item != null) player.Inventory.Discard(item.id, out _);
                var rule = ActiveTechniques.Get(id);
                var a = new ActiveSkillData { technique = id, weaponType = player.Weapon.type, name = rule.Name,
                    slot = slot, input = rule.Input, color = "8fd9d5" };
                player.GrantSkill(a);
                check(ActiveSkillGenerator.ValidateCandidate(a, player.Weapon.type, slot, out _) && ActiveSkillRuntime.CanCast(player, a), id + " new data attaches");
                return a;
            }
            Enemy Spawn(Vector2 point)
            {
                var e = Enemy.Spawn(DB.ScaledEnemy("fox", 0), point, null);
                e.enabled = false;
                e.transform.Find("visual").localScale = Vector3.one;
                e.hp = e.maxHp = 999;
                return e;
            }
            void Cast(ActiveSkillData a, Vector2 point) => ActiveSkillRuntime.Cast(player, a, (point - player.Pos).SafeNormal(dir), point);

            EquipWeapon(WeaponType.Sword);
            var legacyLine = new ActiveSkillData { weaponType = WeaponType.Sword, slot = 0, name = "유리칼 직선베기",
                color = "4fa8aa", steps = { new ActiveStep { delivery = "line", origin = "aim" } } };
            player.GrantSkill(legacyLine);
            player.Facing = dir;
            var muzzle = player.ProjectileOrigin(dir);
            var target = Spawn(muzzle + dir * 1.4f - Vector2.up * .3f);
            Cast(legacyLine, center + dir * 4);
            var stroke = Art.DirectStroke(Pal.Hex(legacyLine.color));
            var cue = Fx.I.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault(sr => sr.enabled && stroke.Contains(sr.sprite));
            check(cue != null && Vector2.Distance(cue.transform.position, muzzle) < .04f && target.hp < 999,
                $"Saved line/aim begins on the visible weapon and hits before the cursor (muzzle {muzzle}, cue {cue?.transform.position}, target HP {target.hp})");
            capture("legacy_aim_line_weapon_start");
            target.Despawn();
            yield return new WaitForSeconds(.35f);

            var reflect = Equip(Technique.BladeReflect);
            muzzle = player.ProjectileOrigin(dir);
            Cast(reflect, center + dir * 4);
            var incoming = Projectile.Spawn(Art.Bolt(), muzzle + dir * 2, -dir * 16, Team.Player, null, default);
            for (int i = 0; i < 14 && incoming != null; i++) yield return null;
            check(incoming == null && Projectile.Live.Any(shot => shot.owner == player && Vector2.Dot(shot.vel, dir) > 0),
                "Blade reflect intercepts a real hostile projectile and sends it down the aim line");
            capture("technique_blade_reflect");
            yield return new WaitForSeconds(.7f);

            EquipWeapon(WeaponType.Dagger);
            var shadow = Equip(Technique.ShadowSwap);
            Cast(shadow, center);
            var moved = WorldStreamer.I.Move(center, dir * 2, player.radius);
            player.Pos = moved;
            check(Vector2.Distance(moved, center) > 1 && ActiveSkillRuntime.TryRecast(player, shadow), "Shadow swap accepts its own recast");
            yield return null; yield return null;
            check(Vector2.Distance(player.Pos, center) < .1f, "Shadow swap returns to the recorded shadow coordinate");
            capture("technique_shadow_swap");
            yield return new WaitForSeconds(.4f);

            var trap = Equip(Technique.ThresholdTrap);
            var at = center + dir * 2;
            Cast(trap, at);
            yield return new WaitForSeconds(.5f);
            var anchor = UnityEngine.Object.FindFirstObjectByType<ActiveGroundAnchor>();
            check(anchor != null, "Directional trap is physically placed");
            at = anchor.Position;
            target = Spawn(at - dir * 1.8f);
            yield return null; yield return null;
            target.Pos = at;
            yield return null; yield return null;
            check(target.hp == 999 && anchor != null, "Wrong side does not activate the directional trap");
            target.Pos = at + dir * 1.8f;
            yield return null; yield return null;
            target.Pos = at;
            yield return null; yield return null;
            var trapFrames = TechniqueFx.Frames(Technique.ThresholdTrap, TechniqueFx.Phase.Resolve, Pal.Hex(trap.color));
            var bursts = Fx.I.GetComponentsInChildren<SpriteRenderer>().Where(sr => sr.enabled && trapFrames.Contains(sr.sprite)).ToArray();
            check(target.hp < 999 && bursts.Length == 1 && Vector2.Distance(bursts[0].transform.position, at) < .03f,
                "Correct crossing produces exactly one technique burst at the trap anchor");
            capture("technique_threshold_trap");
            target.Despawn();
            yield return new WaitForSeconds(.4f);

            var oldTrap = new ActiveSkillData { name = "턱걸이 덫", weaponType = WeaponType.Dagger, slot = 1,
                color = "d9a379", steps = { new ActiveStep { delivery = "trap", origin = "aim" },
                    new ActiveStep { delivery = "arc", origin = "caster", trigger = "hit" } } };
            player.GrantSkill(oldTrap);
            Cast(oldTrap, center + dir * 3);
            yield return new WaitForSeconds(.5f);
            anchor = UnityEngine.Object.FindFirstObjectByType<ActiveGroundAnchor>();
            check(anchor != null, "Saved trap-plus-caster-arc is placed");
            var nearPlayer = Spawn(center + dir * .95f);
            target = Spawn(anchor.Position);
            yield return null; yield return null;
            check(target.hp < 999 && nearPlayer.hp == 999, "Saved trap triggers locally without the former caster hit");
            capture("legacy_trap_local_burst");
            nearPlayer.Despawn(); target.Despawn();

            EquipWeapon(WeaponType.Staff);
            var gate = Equip(Technique.TwinGates);
            var entry = center + dir * 1.4f;
            Cast(gate, entry);
            player.AimPoint = center - dir * 1.45f;
            check(ActiveSkillRuntime.TryRecast(player, gate), "Twin gates accept a distinct exit position");
            player.AimPoint = entry;
            yield return null; yield return null;
            incoming = Projectile.Spawn(Art.Bolt(), entry + dir * 1.05f + Vector2.up * rules.actionHeight, -dir * 13, Team.Player, null, default);
            for (int i = 0; i < 15 && incoming != null && ((Vector2)incoming.transform.position - center).x < .8f; i++) yield return null;
            check(incoming != null && ((Vector2)incoming.transform.position - center).x > .8f,
                $"A real projectile reaches the paired gate exit chosen in the recast frame (shot {incoming?.transform.position}, center {center})");
            capture("technique_twin_gates");
            incoming?.Block(false);

            var resonance = Equip(Technique.ResonanceAnchor, 1);
            resonance.linkSlot = 0;
            resonance.linkEvent = "placement";
            resonance.linkAction = "relay";
            check(ActiveSkillSynergy.Ready(player, resonance), "Placed gate publishes a usable Q to W signal");
            Cast(resonance, entry + Vector2.up * .6f);
            var marker = TechniqueFx.Frames(resonance.technique, TechniqueFx.Phase.Marker, Pal.Hex(resonance.color))[0];
            var placed = Fx.I.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault(sr => sr.enabled && sr.sprite == marker);
            check(placed != null && Vector2.Distance(placed.transform.position, entry) < .05f && !ActiveSkillSynergy.Ready(player, resonance),
                "W snaps to the earlier placement and spends the signal");
            target = Spawn(entry);
            GameEvents.Raise(new CombatEvent { type = Trig.SkillCast, skillSlot = 2, source = player, position = center });
            yield return null; yield return null;
            check(target.hp < 999, "Another manual skill event triggers the placed resonance once");
            capture("technique_resonance_link");
            target.Despawn();
            player.RestoreInventory(original);
        }
    }
}
#endif
