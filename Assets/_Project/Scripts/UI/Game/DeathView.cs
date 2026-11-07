using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>Death screen (Game scene): grave, epitaph (the AI may rewrite it a moment later), carried abilities.</summary>
    public class DeathView : MonoBehaviour
    {
        [SerializeField] Text title, epitaph, carried;
        [SerializeField] Image grave;
        [SerializeField] Button nextLife;
        [SerializeField] Button legacyNext;
        int legacyIndex;

        GraveRecord record;
        Game g;

        public void Init(Game game)
        {
            g = game;
            UIKit.Bind(nextLife, () => { record = null; g.NextLife(); });
            UIKit.Bind(legacyNext, () =>
            {
                var eligible = record.parts.Where(i => i.complexity <= InventoryState.Rules.LegacyTier(g.World)).ToList();
                if (eligible.Count == 0) return;
                legacyIndex = (legacyIndex+1)%eligible.Count;
                g.SelectLegacy(record,eligible[legacyIndex].id);
            });
        }

        public void Show(GraveRecord r) { record = r; legacyIndex = 0; }
        public bool HasRecord => record != null;

        void Update()
        {
            if (record == null) return;
            UIKit.Set(title, $"{record.heroName}의 삶이 끝났다");
            UIKit.Sprite(grave, Art.Prop("grave"));
            UIKit.Set(epitaph, $"<i>\"{record.epitaph}\"</i>");
            var selected = g.World.legacyPart;
            string c = selected != null ? $"다음 삶에 남길 파츠: <b>{selected.name}</b> (T{selected.complexity})\n{selected.Ability?.Explain()}" : "계승할 수 있는 파츠가 없다.";
            UIKit.Set(carried,$"<color=#c9b8ff>{c}</color>\n계승 한도: 파츠 1개 · 복잡도 T{InventoryState.Rules.LegacyTier(g.World)} 이하\n지닌 골드의 절반은 금고로 갔다.");
            if (legacyNext != null) legacyNext.interactable = record.parts.Count(i=>i.complexity <= InventoryState.Rules.LegacyTier(g.World)) > 1;
        }
    }
}
