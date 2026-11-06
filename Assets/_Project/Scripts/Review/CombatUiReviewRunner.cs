#if UNITY_EDITOR
using System;
using System.Collections;
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
    }
}
#endif
