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

        GraveRecord record;
        Game g;

        public void Init(Game game)
        {
            g = game;
            UIKit.Bind(nextLife, () => { record = null; g.NextLife(); });
        }

        public void Show(GraveRecord r) => record = r;
        public bool HasRecord => record != null;

        void Update()
        {
            if (record == null) return;
            UIKit.Set(title, $"{record.heroName}의 삶이 끝났다");
            UIKit.Sprite(grave, Art.Prop("grave"));
            UIKit.Set(epitaph, $"<i>\"{record.epitaph}\"</i>");
            string c = record.abilities.Count > 0 ? "무덤에 새겨진 능력: " + string.Join(", ", record.abilities.Select(a => a.name)) : "무덤에 남은 능력이 없다.";
            UIKit.Set(carried, $"<color=#c9b8ff>{c}</color>\n지닌 골드의 절반은 금고로 갔다. 다음 삶에서 무덤을 찾아 능력을 되찾자.");
        }
    }
}
