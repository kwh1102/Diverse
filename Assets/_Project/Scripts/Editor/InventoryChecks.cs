using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Diverse.EditorTools
{
    /// <summary>Edit-mode assertions only. Never enters play mode, starts a run, calls AI, or opens user save files.</summary>
    public static class InventoryChecks
    {
        static int assertions;
        static void Check(bool condition,string message) { assertions++; if (!condition) throw new Exception("Inventory check: "+message); }
        static InventoryItem Weapon() => new InventoryItem { kind=ItemKind.Weapon,name="검",weapon=DB.W(WeaponKind.Greatsword).Clone() };
        static InventoryItem Part(int tier=1,string element="Wind") => InventoryItem.Part(new AbilityDef { id=Guid.NewGuid().ToString("N"),name="샘플",tier=tier,
            rules={new RuleDef {kind=RuleKind.Trigger,trigger="Attack",ops={new OpDef {op="Nova",element=element,at="Self",amount=.4f,radius=2}}}} });

        [MenuItem("Diverse/인벤토리 규칙 검사 (편집 모드)")]
        public static void RunBatch()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("인벤토리 검사는 편집 모드에서만 실행해 주세요.");
            UIBuilder.InstallInventory();
            assertions=0;
            foreach (TravelType type in TravelTypes.All)
            {
                var bag=InventoryState.Create(type); int capacity=0;
                for (int i=0;i<5;i++) { var w=Weapon(); if (bag.PlaceFirst(w)) capacity++; }
                Check(capacity>=3 && capacity<=4,type+" weapon capacity "+capacity);
                Check(bag.Validate(out _),type+" valid packing");
            }
            var inv=InventoryState.Create(TravelType.Dash); var weapon=Weapon(); inv.Receive(weapon);
            Check(weapon.x==1 && weapon.y==1,"initial center");
            Check(!inv.Move(weapon.id,0,0,out _),"edge placement denied");
            Check(weapon.x==1 && weapon.y==1,"edge rollback");
            var other=Weapon(); inv.items.Add(other);
            Check(!inv.Move(other.id,3,1,out _),"overlapping 3x3 denied");
            Check(inv.Move(other.id,4,1,out _),"adjacent 3x3 accepted");
            var potion=PartArt.Potion("heal"); inv.items.Add(potion);
            Check(!inv.Move(potion.id,0,0,out _),"consumable denied in part slot");
            Check(inv.Move(potion.id,6,6,out _),"consumable accepted in storage");
            var q=new InventoryItem {kind=ItemKind.Skill,name="Q",skillId="gs_leap",skillSlot=0}; inv.items.Add(q);
            Check(!inv.Move(q.id,0,1,out _),"Q denied at W");
            Check(!inv.Move(q.id,0,0,out _),"Q denied at diagonal");
            Check(inv.Move(q.id,1,0,out _),"Q attaches north");
            var part=Part(); inv.items.Add(part);
            Check(!inv.Move(part.id,0,1,out _),"part denied at W");
            Check(inv.Move(part.id,0,0,out _),"part attaches diagonally");
            Check(inv.Move(part.id,3,0,out _),"part transfers between weapons");
            Check(inv.Owner(part.x,part.y)==other,"transferred owner");
            Check(inv.Select(other.id) && inv.Active==other,"active weapon follows selection");
            Check(!inv.Move(weapon.id,3,3,out _),"invalid bundle move rejected");
            Check(q.x==1 && q.y==0 && weapon.x==1,"bundle rollback includes skills");
            Check(inv.Move(weapon.id,1,4,out _),"bundle moves into free square");
            Check(q.x==1 && q.y==3,"skill follows weapon");
            var part2=Part(); inv.items.Add(part2); inv.Move(part2.id,4,6,out _);
            var result=Part(2); result.complexity=2;
            Check(!inv.Fuse(part.id,part.id,result,out _),"same item cannot fuse itself");
            Check(inv.Find(part.id)!=null,"failed fusion preserves material");
            int oldCount=inv.items.Count;
            Check(inv.Fuse(part.id,part2.id,result,out _),"equal complexity fuses");
            Check(inv.items.Count==oldCount-1 && inv.Find(part.id)==null && inv.Find(part2.id)==null,"fusion consumes exactly two");
            Check(result.x==3 && result.y==0 && inv.Owner(result.x,result.y)==other,"fusion keeps first material location");
            result.ancestor="하람 → 별빛"; result.acquisitionApplied=true; result.cooldownRemaining=7;
            var copy=JsonUtility.FromJson<InventoryState>(JsonUtility.ToJson(inv));
            Check(copy.Validate(out _),"JSON retains valid placement");
            Check(copy.Active.id==other.id,"JSON retains active weapon");
            Check(copy.Find(result.id).complexity==2 && copy.Find(result.id).ancestor==result.ancestor && copy.Find(result.id).acquisitionApplied,"JSON retains fusion and legacy metadata");
            Check(copy.Find(result.id).Ability.rules.Count==result.Ability.rules.Count,"JSON retains executable rules");
            inv.Discard(weapon.id,out _);
            Check(!inv.Discard(other.id,out _),"last placed weapon cannot be discarded");
            var full=InventoryState.Create(TravelType.Dash);
            for (int y=0;y<full.height;y++) for(int x=0;x<full.width;x++) full.items.Add(new InventoryItem{kind=ItemKind.Consumable,name="물약",x=x,y=y,consumable="heal",count=InventoryState.Rules.consumableStack});
            var reward=Part(); full.Receive(reward);
            Check(full.Pending.Contains(reward) && !reward.Placed,"full bag preserves reward for later claim");
            Check(!full.Move(reward.id,0,0,out _),"pending reward cannot evict occupied cell into pending storage");
            var stacked=InventoryState.Create(TravelType.Dash); stacked.Receive(PartArt.Potion("heal")); stacked.Receive(PartArt.Potion("heal"));
            Check(stacked.items.Count==1 && stacked.items[0].count==2,"consumables stack");
            Check(PartArt.For(reward)==PartArt.For(reward.Copy()),"icon stable across reload");
            Check(InventoryState.Rules.Shape(TravelType.Flight).width>InventoryState.Rules.Shape(TravelType.Dash).width,"flight wider");
            Check(InventoryState.Rules.Shape(TravelType.Flight).height<InventoryState.Rules.Shape(TravelType.Dash).height,"flight shorter");
            Check(InventoryState.Rules.LegacyTier(new WorldSave{bossKills=4})>InventoryState.Rules.LegacyTier(new WorldSave()),"legacy complexity unlocks with progress");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/GameCanvas.prefab");
            Check(prefab.GetComponentInChildren<InventoryView>(true)!=null,"inventory in prefab");
            Check(prefab.GetComponentsInChildren<EvolveCard>(true).All(c=>new SerializedObject(c).FindProperty("partIcon").objectReferenceValue!=null),"all offer cards have part icons");
            Check(prefab.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"no missing prefab scripts");
            CheckEquipment();
            Debug.Log($"INVENTORY CHECKS PASSED: {assertions} edit-mode assertions. No smoke test or play mode.");
        }

        static void CheckEquipment()
        {
            var equipmentScene=EditorSceneManager.NewPreviewScene();
            var hero=((GameObject)PrefabUtility.InstantiatePrefab(DB.Asset.playerPrefab.gameObject,equipmentScene)).GetComponent<Player>();
            try
            {
                hero.Costume=DB.Costume("white"); hero.Weapon=DB.W(WeaponKind.Greatsword).Clone();
                hero.Abilities=new AbilityRuntime(hero); hero.InitializeInventory(); hero.hp=hero.maxHp;
                var starting=hero.Inventory.Active;
                var second=Weapon(); hero.Inventory.Receive(second);
                float baseline=hero.Stats[StatId.Attack];
                var passive=InventoryItem.Part(new AbilityDef { id="equipment_check",tier=0,
                    rules={new RuleDef {kind=RuleKind.Continuous,ops={new OpDef{op="ModifyStat",stat="Attack",amount=.2f}}}} });
                passive.acquisitionApplied=true;
                hero.Inventory.Receive(passive);
                Check(Mathf.Approximately(hero.Stats[StatId.Attack],baseline),"stored part has no passive effect");
                hero.Inventory.Move(passive.id,0,0,out _);
                Check(hero.Stats[StatId.Attack]>baseline,"equipping applies passive immediately");
                hero.Inventory.Select(second.id);
                Check(Mathf.Approximately(hero.Stats[StatId.Attack],baseline),"weapon switch removes old part effect");
                hero.Inventory.Select(starting.id);
                Check(hero.Stats[StatId.Attack]>baseline,"weapon switch restores equipped effect");
                hero.LearnNextSkill(); hero.Skills[0].cd=9;
                hero.Inventory.Select(second.id); hero.Inventory.Select(starting.id);
                Check(hero.Skills[0].cd>=8.9f,"weapon switch preserves skill cooldown");
                var snapshot=hero.InventorySnapshot();
                Check(snapshot.items.Any(i=>i.kind==ItemKind.Skill && i.cooldownRemaining>=8.9f),"save preserves remaining skill cooldown");
                hero.Inventory.Move(passive.id,6,6,out _);
                Check(Mathf.Approximately(hero.Stats[StatId.Attack],baseline),"unequipping removes passive immediately");
                var fresh=InventoryState.Create(TravelType.Dash); fresh.Receive(Weapon()); hero.RestoreInventory(fresh);
                hero.MigrateAbilities(new[]{AbilityRecord.From(new AbilityDef {id="old_save",tier=0,rules={new RuleDef{kind=RuleKind.Continuous,ops={new OpDef{op="ModifyStat",stat="Attack",amount=.1f}}}}})});
                Check(hero.Inventory.items.Count(i=>i.kind==ItemKind.Part)==1,"old ability migrates to physical part");
                Check(hero.Inventory.items.Single(i=>i.kind==ItemKind.Part).acquisitionApplied,"migration does not re-grant old one-shot rewards");
            }
            finally { Object.DestroyImmediate(hero.gameObject); EditorSceneManager.ClosePreviewScene(equipmentScene); }
        }

        [MenuItem("Diverse/인벤토리 미리보기 저장 (편집 모드)")]
        public static void CapturePreview()
        {
            RunBatch();
            var scene=EditorSceneManager.NewPreviewScene();
            GameObject root=null,camObject=null;
            RenderTexture target=null; Texture2D texture=null; bool scheduled=false;
            void Cleanup()
            {
                if(root!=null) Object.DestroyImmediate(root); if(camObject!=null) Object.DestroyImmediate(camObject);
                if(target!=null) Object.DestroyImmediate(target); if(texture!=null) Object.DestroyImmediate(texture);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            try
            {
                root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/GameCanvas.prefab"),scene);
                root.SetActive(true);
                foreach(Transform child in root.transform) child.gameObject.SetActive(child.name=="Inventory");
                foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=31;
                var canvas=root.GetComponent<Canvas>();
                camObject=new GameObject("Inventory preview camera",typeof(Camera)); SceneManager.MoveGameObjectToScene(camObject,scene);
                var cam=camObject.GetComponent<Camera>(); cam.cullingMask=1<<31; cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=Pal.Hex("1e202d");
                cam.scene=scene; cam.cameraType=CameraType.Game;
                cam.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
                cam.orthographic=true; cam.orthographicSize=180; cam.transform.position=new Vector3(0,0,-10);
                target=new RenderTexture(1280,720,24); cam.targetTexture=target;
                canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=cam;
                var canvasRect=(RectTransform)root.transform; canvasRect.sizeDelta=new Vector2(640,360); canvasRect.position=Vector3.zero; canvasRect.localScale=Vector3.one;
                var scaler=root.GetComponent<CanvasScaler>(); scaler.enabled=false; canvas.scaleFactor=1;
                var view=root.GetComponentInChildren<InventoryView>(true); view.gameObject.SetActive(true);
                var so=new SerializedObject(view);
                T Ref<T>(string name) where T:Object => so.FindProperty(name).objectReferenceValue as T;
                var bag=InventoryState.Create(TravelType.Flight); var w=Weapon(); bag.Receive(w);
                var w2=Weapon(); w2.weapon=DB.W(WeaponKind.Crossbow).Clone(); w2.name=w2.weapon.name; bag.Receive(w2);
                for(int slot=0;slot<4;slot++)
                {
                    var d=InventoryState.Slots[slot]; var s=new InventoryItem{kind=ItemKind.Skill,skillId=w.weapon.skills[slot],skillSlot=slot}; bag.items.Add(s); bag.Move(s.id,w.x+d.x,w.y+d.y,out _);
                }
                var part=Part(); bag.items.Add(part); bag.Move(part.id,0,0,out _);
                var fire=Part(2,"Fire"); bag.items.Add(fire); bag.Move(fire.id,2,2,out _);
                bag.Receive(PartArt.Potion("heal")); bag.Receive(PartArt.Potion("xp")); bag.Receive(PartArt.Potion("cleanse"));
                var grid=Ref<GridLayoutGroup>("grid"); grid.constraintCount=bag.width;
                var cell=Ref<InventoryCell>("cellTemplate"); var pool=new List<InventoryCell>(); UIKit.Pool(cell,pool,bag.width*bag.height);
                for(int i=0;i<pool.Count;i++) pool[i].Bind(view,i%bag.width,i/bag.width,bag,part.id);
                Ref<Text>("title").text="날개형 가방 10×5 · 240G · 무기 2/4";
                Ref<Text>("details").text=$"<b>{part.name}</b>\n복잡도 T1\n{part.Ability.Explain()}\n유산: 하람";
                UIKit.Sprite(Ref<Image>("preview"),PartArt.For(part));
                Ref<Text>("status").text="클릭 후 빈 칸에 배치 · 드래그로 교환\n무기 이동 시 장착물도 함께 이동\n초록 테두리: 사용 중인 무기 영역";
                Ref<Text>("pendingLabel").text="수령 대기 0";
                Ref<GameObject>("promptPanel").SetActive(false); Ref<Button>("confirm").gameObject.SetActive(false); Ref<Button>("cancel").gameObject.SetActive(false);
                var frame=Ref<RectTransform>("activeFrame"); frame.anchoredPosition=new Vector2(12,-54); frame.sizeDelta=new Vector2(88,88);
                Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform); Canvas.ForceUpdateCanvases();
                scheduled=true;
                EditorApplication.QueuePlayerLoopUpdate();
                EditorApplication.delayCall+=()=>EditorApplication.delayCall+=()=>
                {
                    int code=0;
                    try
                    {
                        Canvas.ForceUpdateCanvases(); cam.Render();
                        RenderTexture.active=target; texture=new Texture2D(1280,720,TextureFormat.RGBA32,false); texture.ReadPixels(new Rect(0,0,1280,720),0,0); texture.Apply();
                        File.WriteAllBytes("Logs/InventoryPreview.png",texture.EncodeToPNG()); RenderTexture.active=null;
                        Debug.Log("INVENTORY PREVIEW: Logs/InventoryPreview.png (edit mode only)");
                    }
                    catch(Exception e) { code=1; Debug.LogException(e); }
                    finally { Cleanup(); if(Application.isBatchMode) EditorApplication.Exit(code); }
                };
            }
            finally
            {
                if(!scheduled) Cleanup();
            }
        }
    }
}
