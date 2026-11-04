using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    public class InventoryView : MonoBehaviour
    {
        [SerializeField] GridLayoutGroup grid;
        [SerializeField] InventoryCell cellTemplate;
        [SerializeField] Button pendingTemplate;
        [SerializeField] Text title, details, status, pendingLabel;
        [SerializeField] Image preview;
        [SerializeField] Button equip, use, discard, materialA, materialB, fuse, confirm, cancel, close, generate;
        [SerializeField] GameObject promptPanel;
        [SerializeField] InputField weaponPrompt;
        [SerializeField] RectTransform activeFrame;
        readonly List<InventoryCell> cells = new List<InventoryCell>();
        readonly List<Button> pending = new List<Button>();
        Game g;
        string selected, dragged, first, second, message;
        int revision = -1;
        InventoryState shown;
        InventoryItem shownFusion;
        public bool Typing => weaponPrompt != null && weaponPrompt.isFocused;
        public bool Busy => g != null && g.InventoryBusy;

        public void Init(Game game)
        {
            g = game;
            UIKit.Bind(close, () => g.CloseOverlay());
            UIKit.Bind(equip, () => { if (Bag.Select(selected)) Changed(); });
            UIKit.Bind(use, () => { if (g.Player.UseItem(selected)) Changed(); else message = "지금은 사용할 필요가 없어."; });
            UIKit.Bind(discard, () =>
            {
                if (!Mutable) return;
                if (Bag.Discard(selected, out string why)) { selected = null; Changed(); }
                else message = why;
            });
            UIKit.Bind(materialA, () => { first = selected; message = "첫 번째 재료를 골랐어. 같은 복잡도의 다른 파츠를 선택해 줘."; });
            UIKit.Bind(materialB, () => { second = selected; message = "두 번째 재료를 골랐어. 합성 버튼으로 결과를 만들어 봐."; });
            UIKit.Bind(fuse, () => { message = null; g.StartFusion(first,second); });
            UIKit.Bind(confirm, () => { g.ConfirmFusion(); revision = -1; });
            UIKit.Bind(cancel, () => { g.CancelFusion(); message = null; });
            UIKit.Bind(generate, () => { message = null; g.CreateRewardWeapon(weaponPrompt.text); });
        }

        InventoryState Bag => g.Player.Inventory;
        bool Mutable => !g.InventoryBusy && g.FusionPreview == null;
        void Changed() { message = null; g.InventoryChanged(); revision = -1; }
        public void Opened() { selected = null; message = null; revision = -1; }
        void OnDisable() { if (Controls.TypingField == weaponPrompt) Controls.TypingField = null; }

        public void Click(int x, int y)
        {
            if (!Mutable) return;
            var item = Bag.At(x,y);
            if (item != null) { selected = item.id; message = null; revision = -1; return; }
            if (selected == null) return;
            if (Bag.Move(selected,x,y,out string why)) Changed(); else message = why;
        }
        public void QuickUse(int x, int y)
        {
            if (!Mutable) return;
            var item = Bag.At(x,y); if (item == null) return;
            selected = item.id;
            if (item.kind == ItemKind.Weapon) Bag.Select(item.id);
            else if (item.kind == ItemKind.Consumable) g.Player.UseItem(item.id);
            Changed();
        }
        public void BeginDrag(int x,int y) { if (!Mutable) return; dragged = Bag.At(x,y)?.id; selected = dragged; revision = -1; }
        public void EndDrag() => dragged = null;
        public void Drop(int x,int y)
        {
            if (!Mutable || dragged == null) return;
            if (Bag.Move(dragged,x,y,out string why)) Changed(); else message = why;
            dragged = null;
        }

        void Update()
        {
            if (g?.Player?.Inventory == null) return;
            if (Typing) Controls.TypingField = weaponPrompt;
            else if (Controls.TypingField == weaponPrompt) Controls.TypingField = null;
            if (shown != Bag || revision != Bag.revision)
            {
                shown = Bag; revision = Bag.revision;
                grid.constraintCount = Bag.width;
                UIKit.Pool(cellTemplate,cells,Bag.width*Bag.height);
                for (int i=0;i<Bag.width*Bag.height;i++) cells[i].Bind(this,i%Bag.width,i/Bag.width,Bag,selected);
                if (activeFrame != null && Bag.Active != null)
                {
                    var w=Bag.Active; var pitch=grid.cellSize+grid.spacing;
                    activeFrame.anchoredPosition=((RectTransform)grid.transform).anchoredPosition+new Vector2((w.x-1)*pitch.x,-(w.y-1)*pitch.y);
                    activeFrame.sizeDelta=grid.cellSize*3+grid.spacing*2;
                }
                var waiting = Bag.Pending.ToList();
                UIKit.Pool(pendingTemplate,pending,waiting.Count);
                for (int i=0;i<waiting.Count;i++)
                {
                    var item = waiting[i]; UIKit.Label(pending[i],item.name);
                    UIKit.Bind(pending[i],() => { if (!Mutable) return; selected = item.id; message = "가방의 빈 칸을 눌러 수령해 줘."; revision = -1; });
                }
                UIKit.Set(pendingLabel,$"수령 대기 {waiting.Count}");
            }
            var selectedItem = Bag.Find(selected);
            var itemShown = g.FusionPreview ?? selectedItem;
            if (shownFusion != g.FusionPreview) { shownFusion = g.FusionPreview; message = null; }
            UIKit.Set(title,$"{TravelTypes.Name(g.Player.MovementType)} 가방 {Bag.width}×{Bag.height} · {g.Player.Gold}G · 무기 {Bag.items.Count(i=>i.kind==ItemKind.Weapon&&i.Placed)}/{InventoryState.Rules.maxWeapons}");
            UIKit.Sprite(preview,PartArt.For(itemShown));
            UIKit.Set(details,Description(itemShown));
            string statusText=message ?? g.InventoryStatus ?? "클릭 후 빈 칸에 배치 · 드래그로 교환\n무기 이동 시 장착물도 함께 이동\n초록 테두리: 사용 중인 무기 영역";
            UIKit.Set(status,statusText);
            equip.interactable = Mutable && selectedItem?.kind == ItemKind.Weapon && selectedItem.Placed;
            use.interactable = Mutable && selectedItem?.kind == ItemKind.Consumable && selectedItem.Placed;
            discard.interactable = Mutable && selectedItem != null;
            materialA.interactable = materialB.interactable = Mutable && selectedItem?.kind == ItemKind.Part;
            UIKit.Label(materialA,first == null ? "재료 A 선택" : "A: " + (Bag.Find(first)?.name ?? "다시 선택"));
            UIKit.Label(materialB,second == null ? "재료 B 선택" : "B: " + (Bag.Find(second)?.name ?? "다시 선택"));
            var a = Bag.Find(first); var b = Bag.Find(second);
            fuse.interactable = Mutable && a != null && b != null && a != b && a.complexity == b.complexity;
            UIKit.Label(fuse,a != null ? $"합성 ({InventoryState.Rules.FusionCost(a.complexity)}G)" : "파츠 합성");
            UIKit.Show(confirm,g.FusionPreview != null); UIKit.Show(cancel,g.FusionPreview != null);
            UIKit.Show(promptPanel,g.HasWeaponReward && g.FusionPreview == null);
            UIKit.Show(status,!g.HasWeaponReward || g.FusionPreview != null);
            var promptLabel=promptPanel.transform.Find("Label")?.GetComponent<Text>();
            UIKit.Set(promptLabel,g.InventoryBusy ? "무기 생성 중…" : message ?? g.InventoryStatus ?? "발견한 무기를 디자인해 줘");
            generate.interactable = !Busy && !string.IsNullOrWhiteSpace(weaponPrompt.text);
            close.interactable = !Busy;
        }

        static string Description(InventoryItem item)
        {
            if (item == null) return "아이템을 선택해 줘.\n\n  파츠 Q 파츠\n     W 무기 E\n  파츠 R 파츠";
            string desc = item.kind switch {
                ItemKind.Part => $"복잡도 T{item.complexity}\n{item.Ability?.Explain()}",
                ItemKind.Skill => $"{ "QWER"[item.skillSlot]} 스킬 · {SkillDB.Get(item.skillId)?.desc}",
                ItemKind.Weapon => $"무기 · 주변 3×3 확보\n{item.weapon.desc}",
                _ => item.consumable == "xp" ? $"경험치 +{InventoryState.Rules.xpPotionAmount}" : item.consumable == "cleanse" ? "상태 이상 제거" : $"체력 {InventoryState.Rules.potionHealFraction*100:0}% 회복"
            };
            return $"<b>{item.name}</b>\n{desc}" + (string.IsNullOrEmpty(item.ancestor) ? "" : "\n유산: " + item.ancestor);
        }
    }
}
