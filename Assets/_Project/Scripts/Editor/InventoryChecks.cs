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
    /// <summary>Optional editor-only inventory artwork preview. Never opens user save files.</summary>
    public static class InventoryChecks
    {
        static InventoryItem Weapon() => new InventoryItem { kind=ItemKind.Weapon,name="검",weapon=DB.W(WeaponType.Sword).Clone() };
        static InventoryItem Part(int tier=1,string element="Wind") => InventoryItem.Part(PartCatalog.Create("focus_lens", tier));

        [MenuItem("Diverse/인벤토리 미리보기 저장 (편집 모드)")]
        public static void CapturePreview()
        {
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
                var w2=Weapon(); w2.weapon=DB.W(WeaponType.Bow).Clone(); w2.name=w2.weapon.name; bag.Receive(w2);
                for(int slot=0;slot<4;slot++)
                {
                    var d=InventoryState.Slot(w,slot); var active=ActiveSkillGenerator.Local(ActiveSkillGenerator.TypeOf(w),slot);
                    var s=new InventoryItem{kind=ItemKind.Skill,active=active,name=active.name,skillId=active.id,skillSlot=slot}; bag.items.Add(s); bag.Move(s.id,w.x+d.x,w.y+d.y,out _);
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
