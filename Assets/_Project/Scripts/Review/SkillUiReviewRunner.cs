#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public class SkillUiReviewRunner : MonoBehaviour
    {
        bool failed;
        const string Key = "Diverse.SkillUiReview";
        string Output => SessionState.GetString(Key + ".Output", "Logs/SkillUiReview");
        void OnEnable() => Application.logMessageReceived += Error;
        void OnDisable() => Application.logMessageReceived -= Error;
        void Error(string text, string trace, LogType type)
        {
            if (failed || type is not (LogType.Exception or LogType.Error or LogType.Assert)) return;
            failed = true; File.WriteAllText(Output + "/error.txt", text + "\n" + trace); EditorApplication.ExitPlaymode();
        }
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        void Shot(string name) => ReferenceReviewRunner.Shot(name, true, Output, 360);
        void Note(string text) => File.AppendAllText(Output + "/review.txt", text + "\n");
        void Check(bool condition, string text) { if (!condition) throw new System.InvalidOperationException(text); Note(text + " OK"); }
        static T View<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        static T Ref<T>(Object view, string field) where T : Object => new SerializedObject(view).FindProperty(field).objectReferenceValue as T;
        void Footer(InventoryView view, params string[] fields)
        {
            var box = view.transform.Find("Panel/ItemBox") as RectTransform;
            foreach (string field in fields)
            {
                var button = Ref<Button>(view, field);
                var corners = new Vector3[4]; button.GetComponent<RectTransform>().GetWorldCorners(corners);
                Check(button.gameObject.activeInHierarchy && box.InverseTransformPoint(corners[0]).y < box.rect.yMin + 78,
                    field + " 버튼 실제 표시·하단 배치");
            }
        }

        IEnumerator Start()
        {
            yield return Frames(90);
            var g = Game.I; Check(g != null && !Game.Settings.aiEnabled, "실제 Game 씬 · AI 호출 차단");
            Time.captureDeltaTime = 1f / 60;
            var p = g.Player; p.Level = 20; p.Gold = 10000; p.invulnUntil = float.MaxValue;
            foreach (var enemy in Actor.All.OfType<Enemy>().ToArray()) enemy.enabled = false;
            string fixturePath = Output + "/Scratch/fixture.json";
            var skill = File.Exists(fixturePath) ? JsonUtility.FromJson<ActiveSkillData>(File.ReadAllText(fixturePath)) : ActiveSkillGenerator.Local(WeaponType.Sword, 0);
            skill.slot = 0; ActiveSkillArt.Restore(skill);
            var item = p.GrantSkill(skill); g.InventoryChanged();
            Note("저장 그림 사용: " + skill.name + " / icon=" + skill.ArtReady + " / AI effect=" + ActiveEffectArt.Ready(skill) + " / palette=" + string.Join(",", skill.visualPalette));
            g.OpenOverlay(GameState.Inventory); yield return Frames(5);
            var inventory = View<InventoryView>(); inventory.SelectItem(item.id); yield return Frames(6);
            var heading = Ref<Text>(inventory, "itemHeading"); var scroll = Ref<ScrollRect>(inventory, "detailsScroll");
            Check(heading != null && scroll != null && scroll.verticalScrollbar != null, "고정 헤더·본문·스크롤바 실제 참조");
            Check(scroll.viewport.rect.height > 60, "설명 본문 가변 높이");
            Shot("01_inventory_skill_top");
            item.ancestor = string.Join("\n", Enumerable.Repeat("긴 설명 스크롤 확인 · 위쪽 이름과 아래쪽 동작 버튼은 고정되고, 이 본문만 스크롤합니다.", 16));
            yield return Frames(5); Shot("02_inventory_long_top");
            Check(scroll.content.rect.height > scroll.viewport.rect.height, "긴 본문 실제 스크롤 범위");
            scroll.verticalNormalizedPosition = 0; yield return Frames(5); Shot("02_inventory_skill_bottom");
            item.ancestor = null;
            var discard = Ref<Button>(inventory, "discard"); var box = inventory.transform.Find("Panel/ItemBox") as RectTransform;
            var corners = new Vector3[4]; discard.GetComponent<RectTransform>().GetWorldCorners(corners);
            Check(box.InverseTransformPoint(corners[0]).y < box.rect.yMin + 35, "버리기 버튼 하단 배치");
            var potion = p.Inventory.items.First(i => i.kind == ItemKind.Consumable);
            inventory.SelectItem(potion.id); yield return Frames(5); Footer(inventory, "use", "discard"); Shot("03_inventory_use_discard");
            inventory.SelectItem(p.Inventory.items.First(i => i.kind == ItemKind.Weapon && i.id != p.Inventory.activeWeapon && i.Placed).id);
            yield return Frames(5); Footer(inventory, "equip", "discard"); Shot("04_inventory_equip_discard");
            var pending = new InventoryItem { kind = ItemKind.Skill, skillSlot = 1, active = ActiveSkillGenerator.Local(WeaponType.Sword, 1), name = "그림 대기 스킬" };
            p.Inventory.items.Add(pending); p.Inventory.Notify(); inventory.SelectItem(pending.id);
            yield return Frames(5); Footer(inventory, "receive", "redraw", "discard"); Shot("04_inventory_receive_redraw_discard");

            g.CloseOverlay(); yield return Frames(3);
            // Use the actual prefetch path, then promote the same object into a milestone offer.
            g.RequestPrefetch();
            for (int i = 0; i < 180 && g.SkillPrefetch?.iconsPrepared != true; i++) yield return null;
            var prepared = g.SkillPrefetch;
            Check(prepared?.candidates.Count == 3 && prepared.iconsPrepared && g.State == GameState.Playing && !g.Generating && !g.ChatBusy, "전투 중 사전 준비 · 전투/입력 유지");
            p.pendingRewardLevels.Add(DB.Progression.skillLevels[prepared.slot]); p.PendingEvolutions = 1; g.BeginEvolution(); yield return Frames(8);
            Check(ReferenceEquals(prepared, g.SkillOffer), "보상 창에서 동일한 사전 준비 결과 재사용");
            g.Chat.Add((true, "검 Q · 돌진 후 돌아오는 기술을 원해."));
            g.Chat.Add((false, "요청한 돌진과 재입력 동작을 확인했어. 고정하지 않은 후보에서 원하는 조작과 후속 동작을 선택해 줘."));
            yield return Frames(6);
            var evolve = View<EvolveView>(); var chat = Ref<ScrollRect>(evolve, "chatScroll");
            Check(chat.viewport.rect.height > 0 && Ref<Text>(evolve, "chatLog").preferredHeight > 0, "채팅 영역 양수 높이 · 대화 내용 표시");
            Shot("05_chat_history");
            for (int i = 0; i < 12; i++) g.Chat.Add((i % 2 == 0, "긴 대화도 입력창을 가리지 않고 끝까지 읽을 수 있어야 해. 대화 스크롤 확인 " + i));
            yield return Frames(5); Shot("06_chat_history_scrolled");

            g.SkillOffer = null; g.Offer = null; p.pendingRewardLevels.Clear(); p.PendingEvolutions = 0;
            p.claimedSkillMask = 15; g.State = GameState.Playing; GameTime.ClearPauses(); g.Ui.OnOverlayOpened(GameState.Playing, null);
            p.Pos = WorldStreamer.I.NearestWalkable(new Vector2(24, -20), p.radius, 8); WorldStreamer.I.Warm(p.Pos); CameraRig.I.Snap(p.Pos);
            yield return Frames(10);
            ActiveSkillRuntime.Cast(p, skill, Vector2.right, p.Pos + Vector2.right * 2);
            for (int i = 0; i < 6; i++) { yield return new WaitForSeconds(.04f); Shot("07_skill_motion_" + i); }
            Check(g.World.designCalls == 0 && !Game.Settings.aiEnabled, "전체 화면 확인 중 외부 API 호출 0회");
            File.WriteAllText(Output + "/result.txt", "PASS: inventory header/body/footer/scrollbar, chat visibility/scroll, prefetch promotion and actual skill motion captured. Offline scratch save only.");
            SessionState.SetBool(Key + ".OK", true); EditorApplication.ExitPlaymode();
        }
    }
}
#endif
