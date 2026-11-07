#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public class CombatUiReviewRunner : MonoBehaviour
    {
        const string Key="Diverse.CombatUiV2.Review";
        string Output=>SessionState.GetString(Key+".Output","Logs/CombatUiV2");
        bool failed;
        void Update(){if(!failed && Time.realtimeSinceStartup>600)Error("Review timeout","",LogType.Error);}
        static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        void OnEnable()=>Application.logMessageReceived+=Error;
        void OnDisable()=>Application.logMessageReceived-=Error;
        void Error(string text,string trace,LogType type)
        {if(failed || type is not (LogType.Exception or LogType.Error or LogType.Assert))return;failed=true;File.WriteAllText(Output+"/error.txt",text+"\n"+trace);EditorApplication.ExitPlaymode();}
        static IEnumerator Frames(int n){for(int i=0;i<n;i++)yield return null;}
        static T View<T>()where T:UnityEngine.Object=>FindFirstObjectByType<T>(FindObjectsInactive.Include);
        void Note(string message)=>File.AppendAllText(Output+"/review.txt",message+"\n");
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);Note("OK "+message);}
        void Shot(string name)=>ReferenceReviewRunner.Shot(name,true,Output,315);
        IEnumerator Start()
        {
            yield return Frames(90);var g=Game.I;Check(g!=null && !Game.Settings.aiEnabled,"Actual Game scene; AI disabled");
            Time.captureDeltaTime=1f/60;var p=g.Player;p.Level=20;p.Gold=999;p.invulnUntil=float.MaxValue;p.claimedSkillMask=15;
            p.pendingRewardLevels.Clear();p.PendingEvolutions=0;
            foreach(var e in Actor.All.OfType<Enemy>().ToArray())e.enabled=false;
            string fixture=Output+"/Scratch/inventory.json";
            if(File.Exists(fixture))p.RestoreInventory(JsonUtility.FromJson<InventoryState>(File.ReadAllText(fixture)));
            foreach(string key in new[]{"lucky_die","wind_boot","feather_ring"})
                if(!p.Inventory.items.Any(i=>i.kind==ItemKind.Part && i.iconKey==key))p.Inventory.Receive(InventoryItem.Part(PartCatalog.Create(key)));
            p.Inventory.Notify();
            if (SessionState.GetBool(Key+".Effects", false)) { yield return EffectsReview(g, p); yield break; }
            if(SessionState.GetBool(Key+".Focus",false)){yield return ProjectileFocus(g,p);yield break;}
            string original=JsonUtility.ToJson(p.InventorySnapshot());
            g.OpenOverlay(GameState.Inventory);yield return Frames(8);
            var inventory=View<InventoryView>();
            foreach(var dims in new[]{new Vector2Int(10,5),new Vector2Int(7,7),new Vector2Int(6,8),new Vector2Int(8,6)})
            {
                var bagState=JsonUtility.FromJson<InventoryState>(original);bagState.width=dims.x;bagState.height=dims.y;
                foreach(var item in bagState.items)item.x=item.y=-1;
                foreach(var item in bagState.items.OrderBy(i=>i.kind==ItemKind.Weapon?0:1).ToArray())bagState.PlaceFirst(item);
                p.RestoreInventory(bagState);p.Inventory.Notify();yield return Frames(6);
                Shot("inventory_"+dims.x+"x"+dims.y);
                var grid=inventory.transform.Find("Panel/Bag/GridBounds/Grid") as RectTransform;
                var bag=inventory.transform.Find("Panel/Bag") as RectTransform;var corners=new Vector3[4];grid.GetWorldCorners(corners);
                float left=bag.InverseTransformPoint(corners[0]).x-bag.rect.xMin;
                float right=bag.rect.xMax-bag.InverseTransformPoint(corners[2]).x;
                Check(left>=17 && right>=17,"Bag horizontal margins "+dims+" = "+left+" / "+right);
            }
            p.RestoreInventory(JsonUtility.FromJson<InventoryState>(original));p.Inventory.width=10;p.Inventory.height=5;p.Inventory.Notify();
            var part=p.Inventory.items.First(i=>i.kind==ItemKind.Part);inventory.SelectItem(part.id);yield return Frames(6);Shot("inventory_part_selected");
            part.ancestor=string.Join("\n",Enumerable.Repeat("긴 설명을 스크롤해도 제목과 버튼은 테두리 안에 유지됩니다.",16));
            yield return Frames(5);Shot("inventory_long_top");
            var so=new SerializedObject(inventory);var scroll=so.FindProperty("detailsScroll").objectReferenceValue as ScrollRect;
            Check(scroll.content.rect.height>scroll.viewport.rect.height,"Actual long description scroll overflow");scroll.verticalNormalizedPosition=0;yield return Frames(5);Shot("inventory_long_bottom");part.ancestor=null;
            var potion=p.Inventory.items.FirstOrDefault(i=>i.kind==ItemKind.Consumable);if(potion!=null){inventory.SelectItem(potion.id);yield return Frames(5);Shot("inventory_action_buttons");}
            g.OpenFusion(part.id);yield return Frames(6);Shot("fusion_layout");g.CloseOverlay();g.CloseOverlay();
            g.State=GameState.Playing;GameTime.ClearPauses();g.Ui.OnOverlayOpened(GameState.Playing,null);
            yield return Frames(8);Check(g.State==GameState.Playing && Time.timeScale>0,"Combat capture is unpaused with overlays closed");
            p.DebugTeleport(WorldStreamer.I.NearestWalkable(new Vector2(24,-20),p.radius,8));var center=p.Pos;WorldStreamer.I.Warm(center);CameraRig.I.Snap(center);
            yield return Frames(15);CameraRig.I.target=null;CameraRig.I.Snap(center);
            // Live attacks/casts, not static sprite placement. Each direction captures windup, strike and follow-through.
            var directions=new[]{Vector2.right,new Vector2(1,1).normalized,Vector2.up,new Vector2(-1,1).normalized,Vector2.left,new Vector2(-1,-1).normalized,Vector2.down,new Vector2(1,-1).normalized};
            for(int d=0;d<directions.Length;d++)
            {
                p.DebugTeleport(center);typeof(Player).GetMethod("BasicAttack",Private).Invoke(p,new object[]{directions[d]});
                for(int f=0;f<6;f++){yield return Frames(3);Shot("basic_dir"+d+"_"+f);}
                yield return new WaitForSeconds(.6f);
            }
            foreach(string delivery in new[]{"arc","line","projectile","nova","wall"})
            {
                var active=p.Inventory.Active;
                foreach(var old in p.Inventory.Attached(active).Where(i=>i.kind==ItemKind.Skill && i.skillSlot==0).ToArray())p.Inventory.items.Remove(old);
                var skill=new ActiveSkillData{name="위치·방향 확인",weaponType=ActiveSkillGenerator.TypeOf(active),slot=0,color="9dcbd8",steps={new ActiveStep{delivery=delivery,effect=delivery=="wall"?"block":"damage",element=delivery=="nova"?"Fire":"None"}}};
                p.GrantSkill(skill);p.Inventory.Notify();
                for(int d=0;d<directions.Length;d++)
                {
                    p.DebugTeleport(center);Check(ActiveSkillRuntime.CanCast(p,skill),delivery+" attached for live cast");
                    ActiveSkillRuntime.Cast(p,skill,directions[d],center+directions[d]*2);
                    for(int f=0;f<3;f++){yield return Frames(delivery=="projectile"?3:4);Shot(delivery+"_dir"+d+"_"+f);}
                    yield return new WaitForSeconds(ActiveSkillGenerator.Rules.effectDuration+.1f);
                }
            }
            // Position-aware layers in the actual scene.
            for(int i=0;i<4;i++){CombatVisuals.Explosion(center+new Vector2(2,0),1.5f,i==0?Element.Fire:i==1?Element.Frost:i==2?Element.Arcane:Element.None,Color.white);yield return Frames(3);Shot("explosion_variant_"+i);yield return new WaitForSeconds(.4f);}
            var target=Enemy.Spawn(DB.ScaledEnemy("fox",0),center+Vector2.right*2,null);target.enabled=false;
            Combat.Hit(target,new DamageInfo{source=p,amount=1,direction=Vector2.right,tags="BASIC",hitstop=.01f},Pal.Hex("bddde1"));
            yield return Frames(3);Shot("actual_contact_point");Destroy(target.gameObject);
            Check(g.World.designCalls==0 && !Game.Settings.aiEnabled,"External generation API calls = 0");
            File.WriteAllText(Output+"/result.txt","PASS: actual Game scene, 4 inventory shapes, long text, action footer, fusion and 8-direction basic/active/projectile/barrier motion captured. Offline scratch save; live saves unchanged.");
            SessionState.SetBool(Key+".OK",true);EditorApplication.ExitPlaymode();
        }
        IEnumerator ProjectileFocus(Game g,Player p)
        {
            p.DebugTeleport(WorldStreamer.I.NearestWalkable(new Vector2(24,-20),p.radius,8));var center=p.Pos;
            WorldStreamer.I.Warm(center);CameraRig.I.Snap(center);yield return Frames(15);CameraRig.I.target=null;CameraRig.I.Snap(center);
            var active=p.Inventory.Active;
            foreach(var old in p.Inventory.Attached(active).Where(i=>i.kind==ItemKind.Skill && i.skillSlot==0).ToArray())p.Inventory.items.Remove(old);
            var skill=new ActiveSkillData{name="발사 기준점 확인",weaponType=ActiveSkillGenerator.TypeOf(active),slot=0,steps={new ActiveStep{delivery="projectile"}}};
            p.GrantSkill(skill);p.Inventory.Notify();
            for(int d=0;d<8;d++)
            {
                Vector2 dir=MathX.Dir(d*45);p.DebugTeleport(center);
                Check(ActiveSkillRuntime.CanCast(p,skill),"Final projectile fixture attached "+d);
                ActiveSkillRuntime.Cast(p,skill,dir,center+dir*2);
                for(int f=0;f<4;f++){yield return Frames(3);Shot("projectile_final_dir"+d+"_"+f);}
                yield return new WaitForSeconds(.6f);
            }
            // The actual manual-cast method must still interrupt an unfinished basic attack.
            p.DebugTeleport(center);p.Skills[0].cd=0;
            typeof(Player).GetMethod("BasicAttack",Private).Invoke(p,new object[]{Vector2.right});
            var pending=typeof(Player).GetField("attackStrikePending",Private);Check((bool)pending.GetValue(p),"Basic windup is pending before manual cast");
            bool accepted=(bool)typeof(Player).GetMethod("CastSkill",Private).Invoke(p,new object[]{0});
            Check(accepted && !(bool)pending.GetValue(p),"Manual Q cast cancels pending basic windup");
            Check(g.World.designCalls==0,"Focused review API calls = 0");
            File.WriteAllText(Output+"/result.txt","PASS: final projectile spawn height, forward origin and delayed trail captured in 8 directions; live manual Q interrupts pending basic windup. Offline scratch save.");
            SessionState.SetBool(Key+".OK",true);EditorApplication.ExitPlaymode();
        }

        IEnumerator EffectsReview(Game game, Player player)
        {
            var center = WorldStreamer.I.NearestWalkable(new Vector2(24, -20), player.radius, 8);
            player.DebugTeleport(center);
            WorldStreamer.I.Warm(center);
            CameraRig.I.target = null;
            CameraRig.I.Snap(center);
            foreach (var old in Actor.All.OfType<Enemy>().ToArray()) old.Despawn();
            var cast = typeof(Player).GetMethod("CastSkill", Private);
            var basic = typeof(Player).GetMethod("BasicAttack", Private);
            var cues = (Dictionary<string, float>)typeof(Sfx).GetField("lastPlayed", Private).GetValue(Sfx.I);
            var rules = ActiveSkillGenerator.Rules;
            var savedSkills = player.Inventory.items.Where(i => i.kind == ItemKind.Skill).Select(i => i.active)
                .Where(ActiveEffectArt.ReusesSavedArt).ToArray();
            var savedShot = savedSkills.FirstOrDefault(a => a.steps.Count == 1 && a.steps[0].delivery == "projectile");
            yield return Frames(10);

            ActiveSkillData Equip(ActiveStep step, ActiveSkillData saved = null, int slot = 0)
            {
                var weapon = player.Inventory.Active;
                foreach (var item in player.Inventory.Attached(weapon).Where(i => i.kind == ItemKind.Skill && i.skillSlot == slot).ToArray())
                    player.Inventory.items.Remove(item);
                var skill = saved ?? new ActiveSkillData { name = "전투 연출 검증", color = "a9d3de", steps = { step } };
                skill.slot = slot;
                skill.weaponType = ActiveSkillGenerator.TypeOf(weapon);
                player.GrantSkill(skill);
                player.Inventory.Notify();
                Check(ActiveSkillRuntime.CanCast(player, skill), skill.steps[0].delivery + " attached");
                return skill;
            }

            Enemy Target(Vector2 at)
            {
                var enemy = Enemy.Spawn(DB.ScaledEnemy("fox", 0), at, null);
                enemy.enabled = false;
                enemy.transform.Find("visual").localScale = Vector3.one;
                enemy.hp = enemy.maxHp = 999;
                return enemy;
            }

            foreach (var saved in savedSkills)
                yield return ActiveEffectArt.Prepare(saved, Note, ok => Note($"Saved AI {saved.steps[0].delivery} reprocessed={ok}"));
            var mixedSheet = new Texture2D(256, 128, TextureFormat.RGBA32, false);
            mixedSheet.SetPixels32(new Color32[256 * 128]);
            for (int frame = 0; frame < 4; frame++)
                mixedSheet.SetPixels32(frame * 64, 64, 64, 64, CombatVisuals.Library.slash[frame].texture.GetPixels32());
            mixedSheet.Apply();
            var mixedSkill = new ActiveSkillData { steps = { new ActiveStep { delivery = "arc" } } };
            var processed = ActiveEffectProcessor.Read(mixedSheet, new[] { 0, 1 }, 1, rules, Note, out _, mixedSkill);
            Destroy(mixedSheet);
            Check(processed?[0] != null && processed[4] == null, "A rejected contact row preserves the accepted action row");

            var original = player.InventorySnapshot();
            var meleeBag = InventoryState.Create(player.MovementType);
            var sword = DB.W(WeaponType.Sword).Clone();
            var melee = new InventoryItem { kind = ItemKind.Weapon, weapon = sword, name = sword.name };
            meleeBag.Receive(melee);
            Check(meleeBag.Select(melee.id), "Melee weapon fixture selected");
            player.RestoreInventory(meleeBag);
            yield return Frames(2);

            var trapSkill = Equip(new ActiveStep { delivery = "trap", element = "Wind" });
            ActiveSkillRuntime.Cast(player, trapSkill, Vector2.right, center);
            yield return Frames(3);
            Check(View<ActiveGroundAnchor>() == null, "Trap windup precedes placement");
            Shot("trap_place_pose");
            yield return Frames(12);
            var trap = View<ActiveGroundAnchor>();
            var placed = center + Vector2.right * (player.radius + rules.anchorWidth * .8f);
            Check(trap != null && Vector2.Distance(trap.Position, placed) < .01f && cues.ContainsKey("trap_place"),
                "Trap prefab, ground coordinate and placement cue");
            Vector2 secondTrap = center + Vector2.left * (player.radius + rules.anchorWidth * .8f);
            ActiveSkillRuntime.Cast(player, trapSkill, Vector2.left, center);
            yield return new WaitForSeconds(rules.trapArmTime + CombatVisuals.Library.placementWindup + .08f);
            Check(trap != null && trap.gameObject.activeInHierarchy &&
                FindObjectsByType<ActiveGroundAnchor>(FindObjectsSortMode.None).Any(t => t != trap && Vector2.Distance(t.Position, secondTrap) < .01f),
                "Two trap bodies remain armed simultaneously");
            var charge = player.Skills[0];
            charge.Spend(rules.trapRecharge);
            Check(charge.Ready && charge.spentCharges == 1, "First trap charge can be followed immediately");
            charge.Spend(rules.trapRecharge);
            Check(!charge.Ready, "Both charges consumed");
            charge.Tick(rules.trapRecharge, rules.trapRecharge);
            Check(charge.Ready && charge.spentCharges == 1, "Trap charges refill sequentially");
            Shot("trap_armed");
            var enemy = Target(placed + Vector2.right * (trap.Radius + 1));
            yield return Frames(4);
            Check(enemy.hp == 999, "Target outside trigger radius remains unharmed");
            enemy.Pos = placed + Vector2.right * trap.Radius * .6f;
            yield return Frames(3);
            Check((trap == null || !trap.gameObject.activeSelf) && enemy.hp < 999 && cues.ContainsKey("trap_trigger"),
                "Trap triggers, applies damage and disappears");
            float afterTrap = enemy.hp;
            yield return Frames(8);
            Shot("trap_trigger");
            yield return new WaitForSeconds(rules.pulseInterval + .1f);
            Check(Mathf.Approximately(afterTrap, enemy.hp), "Trap damage occurs once");
            enemy.Despawn();
            yield return Frames(2);
            yield return new WaitForSeconds(rules.trapDuration + .15f);
            Check(View<ActiveGroundAnchor>() == null, "Empty trap expires");

            var field = Equip(new ActiveStep { delivery = "field", origin = "aim", element = "Frost" });
            Vector2 aim = center + Vector2.right * 2.4f;
            enemy = Target(aim);
            ActiveSkillRuntime.Cast(player, field, Vector2.right, aim);
            yield return Frames(12);
            float afterPulse = enemy.hp;
            Check(afterPulse < 999 && View<ActiveGroundAnchor>() != null, "Field pulses at its placed coordinate");
            yield return new WaitForSeconds(.43f);
            Check(View<ActiveGroundAnchor>() != null, "Field boundary persists between pulses");
            Shot("field_between_pulses");
            yield return new WaitForSeconds(rules.effectDuration);
            Check(enemy.hp < afterPulse && View<ActiveGroundAnchor>() == null, "Field repeats and cleans up");
            enemy.Despawn();

            var wall = Equip(new ActiveStep { delivery = "wall", effect = "block", origin = "aim" });
            var direction = MathX.Dir(24);
            aim = center + direction * 2.4f;
            ActiveSkillRuntime.Cast(player, wall, direction, aim);
            yield return Frames(5);
            Check(View<ActiveBarrier>() is { Active: true } && cues.ContainsKey("wall_place"), "Angled wall body and placement cue");
            Shot("wall_angled");
            var hostile = Projectile.Spawn(Art.Bolt(), aim + direction * 2, -direction * 11, Team.Player, null, new DamageInfo { amount = 1 });
            yield return Frames(20);
            Check(hostile == null && cues.ContainsKey("guard"), "Enemy shot blocked at barrier, with sound");
            yield return new WaitForSeconds(rules.effectDuration);

            var slash = Equip(new ActiveStep { delivery = "arc", element = "Frost" });
            direction = Vector2.right;
            enemy = Target(center + Vector2.right * 1.35f);
            var other = Target(center + new Vector2(1.6f, .85f));
            ActiveSkillRuntime.Cast(player, slash, direction, center + direction * 2);
            yield return Frames(4);
            Shot("skill_melee_contact");
            Check(enemy.hp < 999 && other.hp < 999 && cues.ContainsKey("swing"), "Multi-target melee hits and swing cue");
            var cuts = CombatVisuals.Library.cutHit;
            Check(cuts.Length == 5 && Fx.I.GetComponentsInChildren<SpriteRenderer>()
                .Count(sr => sr.enabled && cuts.Contains(sr.sprite)) == 2, "One diagonal hit stroke per damaged enemy");
            yield return Frames(8);
            Shot("skill_melee_followthrough");
            enemy.Despawn(); other.Despawn();
            yield return new WaitForSeconds(.45f);
            player.DebugTeleport(center);
            enemy = Target(center + Vector2.right * 1.35f);
            ActiveSkillRuntime.Cast(player, slash, Vector2.right, center + Vector2.right * 2);
            Check(enemy.hp < 999, "Tap melee resolves in the input frame");
            var followUp = Equip(new ActiveStep { delivery = "arc" }, new ActiveSkillData {
                name = "W 연계 검증", color = "eec296", linkSlot = 0, linkEvent = "impact", linkAction = "burst",
                steps = { new ActiveStep { delivery = "arc" } }
            }, slot: 1);
            Check(ActiveSkillSynergy.Available(player, followUp) && ActiveSkillSynergy.Ready(player, followUp), "W finds the live Q impact producer on this weapon");
            float afterQ = enemy.hp;
            ActiveSkillRuntime.Cast(player, followUp, Vector2.left, center + Vector2.left * 2);
            Check(enemy.hp < afterQ, "W facing away still detonates at Q contact");
            yield return Frames(4);
            Shot("qw_impact_link");
            float afterW = enemy.hp;
            ActiveSkillRuntime.Cast(player, followUp, Vector2.left, center + Vector2.left * 2);
            Check(Mathf.Approximately(enemy.hp, afterW) && !ActiveSkillSynergy.Ready(player, followUp), "The same Q event is consumed once");
            enemy.Despawn();
            yield return new WaitForSeconds(.45f);
            for (int slot = 0; slot < 4; slot++)
            {
                List<ActiveSkillData> offers = null;
                yield return ActiveSkillGenerator.GenerateChoices(player, slot, null, null, new bool[3], a => offers = a, allowAi: false);
                Check(offers?.Count == 3 && offers.Count(a => a.HasSynergy) == (slot == 0 ? 0 : 1) &&
                    offers.Where(a => a.HasSynergy).All(a => ActiveSkillSynergy.Available(player, a)),
                    "Independent and optional earlier-slot offers for " + ActiveSkillGenerator.SlotName(slot));
            }
            var guard = Equip(new ActiveStep { delivery = "guard", effect = "shield" });
            ActiveSkillRuntime.Cast(player, guard, Vector2.right, center);
            Check(player.Shield > 0, "Guard applies its shield in the input frame");
            yield return Frames(5);
            Shot("skill_guard");
            foreach (string movement in new[] { "dash", "retreat", "blink" })
            {
                player.DebugTeleport(center);
                var move = Equip(new ActiveStep { delivery = movement, element = "Wind" });
                Vector2 before = player.Pos;
                ActiveSkillRuntime.Cast(player, move, Vector2.right, center + Vector2.right * 3);
                yield return new WaitForSeconds(rules.movementDuration + .06f);
                Check((player.Pos.x - before.x) * (movement == "retreat" ? -1 : 1) > 1, "Movement cast displaces " + movement);
                Shot("skill_" + movement);
                yield return Frames(10);
            }
            player.DebugTeleport(center);
            foreach (string delivery in new[] { "line", "nova" })
            {
                Vector2 targetAt = center + Vector2.right * 1.5f;
                enemy = Target(targetAt);
                var stage = Equip(new ActiveStep { delivery = delivery, element = "Arcane" }, slot: delivery == "nova" ? 3 : 0);
                ActiveSkillRuntime.Cast(player, stage, Vector2.right, targetAt);
                Check(enemy.hp < 999, "Active damage and visual " + delivery);
                yield return Frames(4);
                Shot("skill_" + delivery);
                enemy.Despawn();
                yield return Frames(15);
            }
            enemy = Target(center + Vector2.right * 1.3f);
            for (int i = 0; i < player.Weapon.combo.Length && i < 4; i++)
            {
                player.DebugTeleport(center);
                enemy.Pos = center + Vector2.right * 1.3f;
                float hp = enemy.hp;
                basic.Invoke(player, new object[] { Vector2.right });
                yield return new WaitForSeconds(player.Weapon.combo[i].windup / player.Stats[StatId.AttackSpeed] + .04f);
                yield return Frames(5);
                Shot($"basic_combo_{i}");
                Check(enemy.hp < hp, "Basic combo contact " + i);
                yield return new WaitForSeconds(player.Weapon.combo[i].recovery / player.Stats[StatId.AttackSpeed] + .04f);
            }
            enemy.Despawn();
            yield return Frames(5);
            foreach (var type in new[] { WeaponType.Staff, WeaponType.Dagger, WeaponType.Bow, WeaponType.Gun })
            {
                var bag = InventoryState.Create(player.MovementType);
                var weapon = DB.W(type).Clone();
                var item = new InventoryItem { kind = ItemKind.Weapon, weapon = weapon, name = weapon.name };
                bag.Receive(item);
                Check(bag.Select(item.id), "Selected five-type weapon profile " + type);
                player.RestoreInventory(bag);
                player.DebugTeleport(center);
                enemy = Target(center + Vector2.right * (weapon.combo[0].projectile ? 2.8f : 1.12f));
                basic.Invoke(player, new object[] { Vector2.right });
                yield return new WaitForSeconds(weapon.combo[0].windup / player.Stats[StatId.AttackSpeed] + .045f);
                Shot($"basic_{type}_release");
                if (weapon.combo[0].projectile) yield return Frames(8);
                Check(enemy.hp < 999, "Basic damage and contact " + type);
                Shot($"basic_{type}_contact");
                enemy.Despawn();
                yield return new WaitForSeconds(.6f);
            }
            player.RestoreInventory(original);
            yield return Frames(3);
            var shot = Equip(new ActiveStep { delivery = "projectile" });
            for (int i = 0; i < 8; i++)
            {
                direction = MathX.Dir(i * 45);
                player.DebugTeleport(center);
                ActiveSkillRuntime.Cast(player, shot, direction, center + direction * 3);
                var live = Projectile.Live.LastOrDefault(pr => pr.owner == player);
                Check(live != null && live.Travelled < .01f, "Tap projectile launches in the input frame " + i);
                Check(live != null && live.animationFrames?.Length > 0 && Vector2.Angle(live.vel, direction) < .1f,
                    "Visible projectile body and direction " + i);
                var launch = (Vector2)live.transform.position - direction * live.Travelled;
                var muzzleFrames = ActiveSkillArt.Effect(shot, shot.steps[0], 3, rules.projectilePixels);
                var flash = Fx.I.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault(sr => sr.enabled && muzzleFrames.Contains(sr.sprite));
                Check(flash != null && Vector2.Distance(launch, flash.transform.position) < .02f,
                    "Projectile and muzzle flash share release coordinate " + i);
                float error = Vector2.Distance(launch, player.ProjectileOrigin(direction));
                Check(error < .2f, $"Projectile begins at the visible muzzle {i} ({error:0.000} world units)");
                Shot($"shot_{i}_release");
                yield return Frames(11);
                Shot($"shot_{i}_flight");
                Check(Fx.I.GetComponentsInChildren<TrailRenderer>().Any(r => r.positionCount > 1), "Continuous flight ribbon " + i);
                yield return new WaitForSeconds(.72f);
            }
            if (savedShot != null && ActiveEffectArt.Ready(savedShot) && savedShot.steps[0].origin == "aim")
            {
                shot = Equip(savedShot.steps[0], savedShot, savedShot.slot);
                direction = Vector2.left;
                aim = center + direction * 2.2f;
                player.DebugTeleport(center);
                player.AimPoint = aim;
                ActiveSkillRuntime.Cast(player, shot, direction, aim);
                var live = Projectile.Live.LastOrDefault(pr => pr.owner == player);
                Check(live != null, "Saved tap projectile skips its old startup timer and renders on the input frame");
                var launch = (Vector2)live.transform.position;
                Check(Vector2.Distance(launch, player.ProjectileOrigin(live.vel.normalized)) < .05f,
                    "Saved aim projectile still releases from the held weapon");
                Shot("saved_ai_projectile_release");
                yield return Frames(11);
                Shot("saved_ai_projectile_flight");
                yield return new WaitForSeconds(.65f);
                if (savedShot.SeekingRupture)
                {
                    player.DebugTeleport(center);
                    enemy = Target(center + direction * 2.4f);
                    var bystander = Target(enemy.Pos + Vector2.up * .82f);
                    ActiveSkillRuntime.Cast(player, shot, direction, aim);
                    yield return Frames(13);
                    Check(enemy.hp < 999 && bystander.hp < 999, "Saved single seeker ruptures onto a second enemy");
                    Shot("saved_ai_projectile_rupture");
                    enemy.Despawn(); bystander.Despawn();
                    yield return new WaitForSeconds(.45f);
                }
            }
            var clips = (Dictionary<string, AudioClip>)typeof(Sfx).GetField("clips", Private).GetValue(Sfx.I);
            Check(new[] { "trap_place", "trap_trigger", "wall_place", "guard", "swing", "bow_release", "gun_shot", "skill_cast" }
                .All(cue => clips.TryGetValue(cue, out var clip) && clip != null && clip.samples > 0),
                "Combat cues resolve to audio clips");
            player.Skills[0].cd = 0;
            Check((bool)cast.Invoke(player, new object[] { 0 }), "Q input reaches the same active path");
            yield return new WaitForSeconds(.65f);

            var darkGround = Enumerable.Range(-12, 25).SelectMany(x => Enumerable.Range(-12, 25)
                .Select(y => new Vector2(x * 24 + .5f, y * 24 + .5f)))
                .OrderBy(point => point.sqrMagnitude)
                .FirstOrDefault(point => WorldStreamer.I.Gen.GroundAt(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y))
                    is Ground.Ash or Ground.Swamp or Ground.Mirror);
            if (darkGround != Vector2.zero)
            {
                player.RestoreInventory(meleeBag);
                WorldStreamer.I.Warm(darkGround);
                player.DebugTeleport(darkGround);
                var darkCenter = player.Pos;
                CameraRig.I.Snap(darkCenter);
                yield return Frames(15);
                foreach (var old in Actor.All.OfType<Enemy>().ToArray()) old.Despawn();
                enemy = Target(darkCenter + Vector2.right * 1.6f);
                slash = Equip(new ActiveStep { delivery = "arc", element = "Lightning" });
                ActiveSkillRuntime.Cast(player, slash, Vector2.right, darkCenter + Vector2.right * 2);
                yield return Frames(5);
                Check(enemy.hp < 999, "Combat contact against a dark biome backdrop");
                Shot("dark_ground_contact");
            }
            Check(game.World.designCalls == 0, "Image API calls = 0");
            File.WriteAllText(Output + "/result.txt", "PASS: live five-weapon combat, input-frame launch, QWER choice policy and link consumption, eleven delivery families, charged traps, persistent field, angled barrier blocking, 8 directional projectile wakes and saved-art review.");
            SessionState.SetBool(Key+".OK", true);
            EditorApplication.ExitPlaymode();
        }
    }
}
#endif
