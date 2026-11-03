using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// Save slots (up to 3 worlds), opened from the title.
    /// The world and enemies are built in; the hero and weapon can be designed.
    /// Each world row: 이어하기 / 새 삶 시작 / 새 세계 만들기 (Play), 삶 끝내기 (Abandon, when a life is in progress) and
    /// 삭제 (Delete). Ending a life and deleting a world ask once more before they happen.
    /// </summary>
    public class SaveSelectView : MonoBehaviour
    {
        [SerializeField] Button slotTemplate;         // children: "Text" (title + info), "Play", "Abandon", "Delete" buttons
        [SerializeField] Button backButton;

        readonly List<Button> slots = new List<Button>();
        MenuUI menu;
        int confirmDelete, confirmAbandon;   // slot number waiting for its second press (0 = none)
        readonly SlotInfo[] infos = new SlotInfo[SaveSystem.SlotCount];
        bool dirty = true;   // re-read the slot files when a slot changes or the screen opens

        public void Init(MenuUI m)
        {
            menu = m;
            UIKit.Bind(backButton, () => menu.Show(MenuUI.Screen.Title));
            UIKit.Pool(slotTemplate, slots, SaveSystem.SlotCount);
            for (int i = 0; i < slots.Count; i++)
            {
                int n = i + 1;
                UIKit.Bind(slots[i], () => Play(n));
                UIKit.Bind(Child(slots[i], "Play"), () => Play(n));
                UIKit.Bind(Child(slots[i], "Abandon"), () => Abandon(n));
                UIKit.Bind(Child(slots[i], "Delete"), () => Delete(n));
            }
        }

        static Button Child(Button row, string name) => row.transform.Find(name)?.GetComponent<Button>();

        void OnEnable()
        {
            confirmDelete = confirmAbandon = 0;
            dirty = true;
        }

        /// <summary>Continue the life in progress, or design a new life (an empty slot gets its new world first).</summary>
        void Play(int slot)
        {
            confirmDelete = confirmAbandon = 0;
            Session.OpenSlot(slot);
            if (Session.HasRunToContinue) Session.ContinueLife();
            else menu.Show(menu.NewLifeScreen);
        }

        /// <summary>Ending a life counts as a death: the hero gets a grave where they stood, the world stays.</summary>
        void Abandon(int slot)
        {
            confirmDelete = 0;
            if (confirmAbandon != slot) { confirmAbandon = slot; return; }
            Session.OpenSlot(slot);
            Session.AbandonRun();
            confirmAbandon = 0;
            dirty = true;
        }

        void Delete(int slot)
        {
            confirmAbandon = 0;
            if (confirmDelete != slot) { confirmDelete = slot; return; }
            SaveSystem.DeleteSlot(slot);
            confirmDelete = 0;
            dirty = true;
        }

        void Update()
        {
            if (dirty) { dirty = false; for (int i = 0; i < infos.Length; i++) infos[i] = SaveSystem.Peek(i + 1); }
            for (int i = 0; i < slots.Count; i++)
            {
                int n = i + 1;
                var info = infos[i];
                bool run = info != null && info.hasRun;
                string text = info == null
                    ? $"<b>세계 {n}</b>   <color=#8c7aa8>비어 있음</color>"
                    : $"<b>세계 {n}</b>   {(info.world.lifeCount == 0 ? "아직 시작 전" : info.world.lifeCount + "번째 삶")} · 금고 {info.world.gold}G"
                      + $"\n<color=#8c7aa8>{(run ? "이어할 삶 있음 · " : "")}마지막 플레이 {info.lastPlayed:yyyy-MM-dd HH:mm}</color>";
                UIKit.Label(slots[i], text);
                UIKit.Label(Child(slots[i], "Play"), info == null ? "새 세계 만들기" : run ? "이어하기" : "새 삶 시작");
                var abandon = Child(slots[i], "Abandon");
                UIKit.Show(abandon, run);
                UIKit.Label(abandon, confirmAbandon == n ? "정말 끝낼까?" : "삶 끝내기");
                var del = Child(slots[i], "Delete");
                UIKit.Show(del, info != null);
                UIKit.Label(del, confirmDelete == n ? "정말 삭제?" : "삭제");
            }
        }
    }
}
