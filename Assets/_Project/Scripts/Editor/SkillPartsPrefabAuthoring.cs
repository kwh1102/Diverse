using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    /// <summary>Idempotent prefab authoring after the user's normal editor import. Never runs game/tests.</summary>
    [InitializeOnLoad]
    public static class SkillPartsPrefabAuthoring
    {
        const string Path = "Assets/_Project/Prefabs/UI/GameCanvas.prefab";
        static SkillPartsPrefabAuthoring()
        {
            EditorApplication.delayCall += InstallWhenReady;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += InstallWhenReady; };
        }
        static void InstallWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += InstallWhenReady; return; }
            Install();
        }
        [MenuItem("Diverse/T1 합성·스킬 선택 UI 반영")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Path); if (asset == null) return;
            var view = asset.GetComponentInChildren<InventoryView>(true);
            var cards = asset.GetComponentsInChildren<EvolveCard>(true);
            if (view == null || cards.Length != 3 || asset.GetComponentInChildren<EvolveView>(true) == null) return;
            var installed = new SerializedObject(asset.GetComponentInChildren<EvolveView>(true));
            if (installed.FindProperty("uiRevision").intValue >= 2 && view != null && new SerializedObject(view).FindProperty("footprintView").objectReferenceValue != null &&
                cards.Length == 3 && cards.All(c => new SerializedObject(c).FindProperty("pin").objectReferenceValue != null)) return;
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                view = root.GetComponentInChildren<InventoryView>(true);
                var serialized = new SerializedObject(view);
                var grid = serialized.FindProperty("grid").objectReferenceValue as GridLayoutGroup;
                if (grid == null) return;
                var graphic = serialized.FindProperty("footprintView").objectReferenceValue as InventoryFootprintGraphic;
                if (graphic == null) graphic = grid.transform.Find("WeaponFootprints")?.GetComponent<InventoryFootprintGraphic>();
                if (graphic == null)
                {
                    var obj = new GameObject("WeaponFootprints", typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement), typeof(InventoryFootprintGraphic));
                    obj.transform.SetParent(grid.transform, false); graphic = obj.GetComponent<InventoryFootprintGraphic>();
                    obj.layer = grid.gameObject.layer;
                    var newRect = graphic.rectTransform;
                    newRect.anchorMin = Vector2.zero; newRect.anchorMax = Vector2.one; newRect.pivot = new Vector2(0,1); newRect.offsetMin = newRect.offsetMax = Vector2.zero;
                }
                var rect = graphic.rectTransform;
                graphic.raycastTarget = false;
                var layout = graphic.GetComponent<LayoutElement>() ?? graphic.gameObject.AddComponent<LayoutElement>(); layout.ignoreLayout = true;
                Set(graphic, "grid", grid); Set(view, "footprintView", graphic); rect.SetAsLastSibling();
                var evolve = root.GetComponentInChildren<EvolveView>(true);
                var evolveData = new SerializedObject(evolve);
                var template = evolveData.FindProperty("send").objectReferenceValue as Button;
                if (template == null) return;
                foreach (var card in evolve.GetComponentsInChildren<EvolveCard>(true))
                {
                    var cardData = new SerializedObject(card);
                    var pin = cardData.FindProperty("pin").objectReferenceValue as Button;
                    if (pin == null) pin = card.transform.Find("Pin")?.GetComponent<Button>();
                    if (pin == null)
                    {
                        pin = Object.Instantiate(template, card.transform); pin.name = "Pin";
                        var pr = (RectTransform)pin.transform;
                        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(1,1); pr.anchoredPosition = new Vector2(-5,-3); pr.sizeDelta = new Vector2(47,14);
                        var head = cardData.FindProperty("header").objectReferenceValue as Text;
                        if (head != null) { var size = head.rectTransform.sizeDelta; size.x = ((RectTransform)card.transform).sizeDelta.x - 63; head.rectTransform.sizeDelta = size; head.resizeTextForBestFit = true; head.resizeTextMinSize = 6; }
                    }
                    pin.onClick.RemoveAllListeners();
                    UIKit.Label(pin, "후보 고정"); pin.gameObject.SetActive(false); Set(card, "pin", pin);
                }
                var heading = evolve.transform.Find("Panel/Heading")?.GetComponent<Text>(); if (heading != null) heading.text = "성장 보상";
                var pipeline = evolveData.FindProperty("pipelinePanel").objectReferenceValue as GameObject;
                var pipelineHeading = pipeline != null ? pipeline.transform.Find("Heading")?.GetComponent<Text>() : null;
                if (pipelineHeading != null) pipelineHeading.text = "후보 설계·검증 기록";
                var processText = evolveData.FindProperty("pipelineText").objectReferenceValue as Text;
                if (pipeline != null && processText != null) PrepareLog(pipeline.transform, processText);
                foreach (var card in evolve.GetComponentsInChildren<EvolveCard>(true))
                {
                    var data = new SerializedObject(card);
                    var tags = data.FindProperty("tags").objectReferenceValue as Text;
                    if (tags != null) { tags.resizeTextForBestFit = false; tags.fontSize = 7; tags.horizontalOverflow = HorizontalWrapMode.Wrap; tags.verticalOverflow = VerticalWrapMode.Overflow; }
                }
                var fusion = root.GetComponentInChildren<PartFusionView>(true);
                if (fusion != null)
                {
                    var data = new SerializedObject(fusion);
                    var generate = data.FindProperty("generate").objectReferenceValue as Button;
                    if (generate != null) generate.gameObject.SetActive(false);
                    var status = data.FindProperty("status").objectReferenceValue as Text;
                    if (status != null) { status.resizeTextForBestFit = false; status.fontSize = 8; status.horizontalOverflow = HorizontalWrapMode.Wrap; status.verticalOverflow = VerticalWrapMode.Overflow; }
                }
                var input = evolveData.FindProperty("chatInput").objectReferenceValue as InputField;
                if (input?.placeholder is Text placeholder) placeholder.text = "예) 검 Q · 충전 후 돌진하고 재입력하면 돌아오기";
                var log = evolveData.FindProperty("chatLog").objectReferenceValue as Text;
                if (log != null) log.text = "고정하지 않은 후보에 원하는 조작과 전술을 요청하세요.";
                evolveData.Update(); evolveData.FindProperty("uiRevision").intValue = 2; evolveData.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Path);
                Debug.Log("T1 합성·액티브 후보 선택 UI를 기존 프리팹에 추가했습니다. 게임/테스트는 실행하지 않았습니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target); serialized.FindProperty(field).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static void PrepareLog(Transform panel, Text body)
        {
            body.supportRichText = false; body.resizeTextForBestFit = false; body.fontSize = 9;
            body.horizontalOverflow = HorizontalWrapMode.Wrap; body.verticalOverflow = VerticalWrapMode.Overflow;
            var scroll = body.GetComponentInParent<ScrollRect>();
            if (scroll == null)
            {
                var obj = new GameObject("GenerationLog", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
                obj.layer = panel.gameObject.layer; obj.transform.SetParent(panel, false);
                var rect = (RectTransform)obj.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(8, 8); rect.offsetMax = new Vector2(-8, -24);
                obj.GetComponent<Image>().color = Color.clear;
                body.transform.SetParent(rect, false);
                var content = body.rectTransform; content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0,1); content.anchoredPosition = content.sizeDelta = Vector2.zero;
                var fit = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll = obj.GetComponent<ScrollRect>(); scroll.viewport = rect; scroll.content = content;
            }
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        }
    }
}
