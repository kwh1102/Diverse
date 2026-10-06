using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>Pause menu (Game scene): 능력 / 능력치 / 연대기 / 설정 / 키 설정 tabs.</summary>
    public class PauseView : MonoBehaviour
    {
        [SerializeField] Button[] tabs = new Button[5];     // abilities, attrs, chronicle, settings, keys
        [SerializeField] GameObject abilitiesPage, attrsPage, chroniclePage;
        [SerializeField] SettingsView settings;
        [Header("Abilities")]
        [SerializeField] Text abilitiesHeader, abilitiesList;
        [Header("Attributes")]
        [SerializeField] Text unspent;
        [SerializeField] Text[] attrLines = new Text[4];
        [SerializeField] Button[] attrPlus = new Button[4];
        [SerializeField] Text statNames, statValues;
        [Header("Chronicle")]
        [SerializeField] Text chronicleHeader, chronicleList;
        [Header("Buttons")]
        [SerializeField] Button resume, toTitle;
        [SerializeField] Color selectedTint = new Color(1f, 0.95f, 0.7f);

        static readonly string[] TabIds = { "abilities", "attrs", "chronicle", "settings", "keys" };
        string tab = "abilities";
        Game g;

        public bool Rebinding => settings != null && settings.Rebinding;

        public void Init(Game game)
        {
            g = game;
            for (int i = 0; i < tabs.Length; i++) { string id = TabIds[i]; UIKit.Bind(tabs[i], () => Open(id)); }
            for (int i = 0; i < attrPlus.Length; i++)
            {
                int k = i;
                UIKit.Bind(attrPlus[i], () =>
                {
                    var p = g.Player;
                    if (p == null || p.UnspentAttr <= 0) return;
                    p.Attrs[k]++; p.UnspentAttr--; p.Telemetry.RecordInvest((Attr)k); p.RecalcStats();
                });
            }
            UIKit.Bind(resume, () => g.CloseOverlay());
            UIKit.Bind(toTitle, () => g.SaveAndQuitToTitle());
        }

        public void Open(string id)
        {
            tab = System.Array.IndexOf(TabIds, id) >= 0 ? id : "abilities";
            for (int i = 0; i < tabs.Length; i++) tabs[i].image.color = TabIds[i] == tab ? selectedTint : Color.white;
            UIKit.Show(abilitiesPage, tab == "abilities");
            UIKit.Show(attrsPage, tab == "attrs");
            UIKit.Show(chroniclePage, tab == "chronicle");
            bool set = tab == "settings" || tab == "keys";
            UIKit.Show(settings, set);
            if (set) settings.Page(tab == "keys");
        }

        void Update()
        {
            var p = g.Player;
            if (p == null) return;
            if (tab == "abilities")
            {
                UIKit.Set(abilitiesHeader, $"<b>{p.Weapon.name}</b> · {p.Costume.name} <color=#fff2b3>({p.Costume.perk})</color>");
                UIKit.Set(abilitiesList, p.Abilities.Owned.Count == 0
                    ? "<color=#8c7aa8>아직 능력이 없다. 레벨이 오르면 플레이 방식이 능력으로 진화한다.</color>"
                    : string.Join("\n\n", p.Abilities.Owned.Select(a =>
                        $"{(a.source == "ai" ? "<color=#c9b8ff>✦AI</color>" : "<color=#8c7aa8>◇</color>")} <b><color=#fff2b3>{a.name}</color></b>  <color=#6b5c8a>{string.Join(" ", a.tags)}</color>\n<color=#c9bcd8>{a.Explain()}</color>")));
            }
            else if (tab == "attrs")
            {
                UIKit.Set(unspent, $"남은 포인트: <color=#8bff9a>{p.UnspentAttr}</color>");
                for (int i = 0; i < attrLines.Length; i++)
                {
                    var a = (Attr)i;
                    UIKit.Set(attrLines[i], $"<b>{AttrInfo.Name(a)}</b> {p.Attrs[i]}   <color=#8c7aa8>{AttrInfo.Desc(a)}</color>");
                    attrPlus[i].interactable = p.UnspentAttr > 0;
                }
                var names = new List<string>(); var vals = new List<string>();
                foreach (StatId s in System.Enum.GetValues(typeof(StatId)))
                {
                    if (s == StatId.Count) continue;
                    float v = p.Stats[s];
                    names.Add(Stats.Label(s));
                    vals.Add(Stats.IsPercent(s) ? (s == StatId.AttackSpeed || s == StatId.XpGain || s == StatId.GoldGain ? $"×{v:0.00}" : $"{v * 100:0}%") : $"{v:0.#}");
                }
                UIKit.Set(statNames, string.Join("\n", names));
                UIKit.Set(statValues, $"<color=#fff2b3>{string.Join("\n", vals)}</color>");
            }
            else if (tab == "chronicle")
            {
                var w = g.World;
                UIKit.Set(chronicleHeader, $"<b>연대기</b> — 삶 {w.lifeCount} · 무덤 {w.graves.Count} · 소탕한 야영지 {w.clearedCamps.Count} · 탑 {w.towerFloor}층");
                UIKit.Set(chronicleList, string.Join("\n", w.chronicle.AsEnumerable().Reverse().Take(200).Select(e =>
                {
                    string col = e.kind switch { "death" => "#ff8a8a", "life" => "#8bff9a", "evolve" => "#c9b8ff", "legacy" => "#fff2b3", "tower" => "#e6dcff", _ => "#c9bcd8" };
                    return $"<color=#6b5c8a>[{e.life}번째 삶]</color> <color={col}>{e.text}</color>";
                })));
            }
        }
    }
}
