using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>In-run HUD (Game scene): portrait + bars, currencies, minimap, quests, skill bar, abilities, interaction hint, boss bar.</summary>
    public class HudView : MonoBehaviour
    {
        [Header("Top left")]
        [SerializeField] Image portrait;
        [SerializeField] Text heroLine;
        [SerializeField] Image hpFill, shieldFill, xpFill;
        [SerializeField] Text hpText;
        [SerializeField] Image dashTemplate;           // child "Fill" is the recharge bar
        [Header("Top right")]
        [SerializeField] Image goldIcon, shardIcon, potionIcon, raftIcon;
        [SerializeField] Text goldText, shardText, potionText, regionText, coordText;
        [SerializeField] RectTransform goldHover, shardHover, potionHover;
        [SerializeField] MinimapView minimap;
        [SerializeField] Text questsText;
        [Header("Bottom")]
        [SerializeField] SkillSlot[] skills = new SkillSlot[4];
        [SerializeField] Text moveHint, dashHint;
        [SerializeField] Text abilitiesText, evolveHint, attrHint;
        [Header("World-anchored / overlays")]
        [SerializeField] RectTransform interactHint;
        [SerializeField] Text interactText;
        [SerializeField] RectTransform bossBar;
        [SerializeField] Text bossName;
        [SerializeField] Image bossFill;
        [SerializeField] Text generatingText;
        [SerializeField] TooltipView tooltip;

        readonly List<Image> dashes = new List<Image>();
        float bossShown;
        Vector2 bossBase;
        Game g;

        public void Init(Game game)
        {
            g = game;
            if (bossBar != null) bossBase = bossBar.anchoredPosition;
            UIKit.Sprite(goldIcon, Art.Item("gold"));
            UIKit.Sprite(shardIcon, Art.Item("shard"));
            UIKit.Sprite(potionIcon, Art.Item("potion"));
            UIKit.Sprite(raftIcon, Art.Item("raft"));
        }

        public void Tick()
        {
            var p = g.Player;
            if (p == null) return;
            var w = g.World;

            UIKit.Sprite(portrait, Art.Rabbit(p.Costume, Pose.Idle0));
            UIKit.Set(heroLine, $"<b>{p.HeroName}</b>  <color=#c9b8ff>Lv.{p.Level}</color>  <color=#8c7aa8>{w.lifeCount}번째 삶</color>");
            UIKit.Fill(hpFill, p.hp / p.maxHp);
            UIKit.Set(hpText, $"{Mathf.CeilToInt(p.hp)}/{Mathf.CeilToInt(p.maxHp)}");
            UIKit.Show(shieldFill, p.Shield > 0);
            UIKit.Fill(shieldFill, p.Shield / p.maxHp);
            UIKit.Fill(xpFill, p.Xp / p.XpToNext);
            UIKit.Pool(dashTemplate, dashes, p.DashChargesMax);
            for (int i = 0; i < dashes.Count; i++)
            {
                float k = i < p.DashCharges ? 1 : i == p.DashCharges ? p.DashRechargeProgress : 0;
                UIKit.Fill(dashes[i].transform.Find("Fill")?.GetComponent<Image>(), k);
            }

            UIKit.Set(goldText, p.Gold.ToString());
            UIKit.Set(shardText, w.memoryShards.ToString());
            UIKit.Set(potionText, $"{p.Potions} [{Controls.KeyName(Act.Potion)}]");
            UIKit.Set(regionText, World.I.Gen.RegionName(p.Pos));
            UIKit.Set(coordText, $"({p.Pos.x:0}, {p.Pos.y:0})  {Controls.KeyName(Act.Map)} 지도");
            UIKit.Show(raftIcon, p.HasRaft);
            if (minimap != null) minimap.Tick(g);

            var qs = w.quests.Where(x => x.status == 1 || x.status == 2).Take(3).Select(q =>
            {
                string st = q.status == 2 ? "<color=#8bff9a>✔ 보고</color> " : q.kind == "hunt" ? $"<color=#c9b8ff>{q.have}/{q.need}</color> " : "";
                string dist = q.status == 1 && q.kind != "hunt" ? $" <color=#8c7aa8>{Vector2.Distance(p.Pos, new Vector2(q.tx, q.ty)):0}m</color>" : "";
                return st + q.title + dist;
            });
            UIKit.Set(questsText, string.Join("\n", qs));

            Act[] acts = { Act.SkillQ, Act.SkillW, Act.SkillE, Act.SkillR };
            for (int i = 0; i < skills.Length; i++) skills[i]?.Tick(p, p.Skills[i], Controls.KeyName(acts[i]), tooltip);
            UIKit.Set(moveHint, $"<color=#8c7aa8>{Controls.KeyName(Act.Move)}</color> 이동/공격");
            UIKit.Set(dashHint, $"<color=#8c7aa8>{Controls.KeyName(Act.Dash)}</color> 대시");

            UIKit.Set(abilitiesText, string.Join("\n", p.Abilities.Owned.AsEnumerable().Reverse().Take(6).Reverse()
                .Select(a => $"<color={(a.source == "ai" ? "#c9b8ff" : "#fff2b3")}>◆</color> {a.name}")));
            UIKit.Show(evolveHint, p.PendingEvolutions > 0);
            if (p.PendingEvolutions > 0)
            {
                UIKit.Set(evolveHint, $"<color=#fff2b3>★ 진화 가능 ×{p.PendingEvolutions}</color> — [{Controls.KeyName(Act.Abilities)}] 또는 전투 종료 시 자동");
                var c = evolveHint.color; c.a = 0.6f + Mathf.Sin(Time.unscaledTime * 5) * 0.4f; evolveHint.color = c;
            }
            UIKit.Show(attrHint, p.UnspentAttr > 0);
            if (p.UnspentAttr > 0) UIKit.Set(attrHint, $"<color=#8bff9a>+ 능력치 {p.UnspentAttr}점</color> — [{Controls.KeyName(Act.Pause)}] → 능력치");

            // Interaction hint above the nearest interactable
            var it = g.State == GameState.Playing ? Interactable.Nearest(p.Pos) : null;
            UIKit.Show(interactHint, it != null);
            if (it != null)
            {
                Vector2 sp = CameraRig.I.Cam.WorldToScreenPoint(it.Pos + Vector2.up * 1.6f);
                var parent = (RectTransform)interactHint.parent;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, sp, null, out var local)) interactHint.localPosition = local;
                UIKit.Set(interactText, $"[{Controls.KeyName(Act.Interact)}] {it.label}");
            }

            bool boss = g.Boss != null && g.Boss.Alive;
            bossShown = boss ? Mathf.MoveTowards(bossShown, 1, Time.unscaledDeltaTime * 3) : 0;
            UIKit.Show(bossBar, boss);
            if (boss)
            {
                bossBar.anchoredPosition = bossBase + Vector2.up * (1 - bossShown) * 60f;
                UIKit.Set(bossName, $"<b>{g.Boss.def.name}</b>");
                UIKit.Fill(bossFill, g.Boss.hp / g.Boss.maxHp);
            }

            bool gen = g.Generating && g.State != GameState.Evolving;
            UIKit.Show(generatingText, gen);
            if (gen) UIKit.Set(generatingText, g.OfferStatus ?? "…");

            // Currency tooltips
            if (tooltip != null && g.State == GameState.Playing)
            {
                if (UIKit.Hovered(goldHover))
                    tooltip.Show("<b><color=#fff2b3>골드</color></b>",
                        $"몬스터·상자·의뢰에서 얻는다. 상점, 대장간, 여관, 진화 다시 뽑기({g.RerollCost}G)와 이야기꾼과의 대화({g.ChatCost}G)에 쓴다.\n죽으면 절반이 기억의 금고로 가고, 금고의 골드는 다음 삶에 일부 전해진다. (금고: {w.gold}G)");
                else if (UIKit.Hovered(shardHover))
                    tooltip.Show("<b><color=#c9b8ff>기억의 조각</color></b>",
                        $"세계가 기억하는 재화 — 죽어도 사라지지 않는다. 보스, 제단, 유적, 의뢰에서 얻는다.\n기억의 탑 다음 층을 여는 데 {Dialogue.TowerCost(w.towerFloor)}개가 필요하다. 층이 열릴 때마다 모든 삶의 최대 체력이 오른다.");
                else if (UIKit.Hovered(potionHover))
                    tooltip.Show("<b><color=#ff8a8a>회복약</color></b>",
                        $"[{Controls.KeyName(Act.Potion)}] 최대 체력의 40%를 회복한다. 최대 5개.\n마을 상인에게 30골드에 살 수 있고, 상자에서도 나온다.");
                else if (p.HasRaft && UIKit.Hovered(raftIcon.rectTransform))
                    tooltip.Show("<b><color=#c4936a>뗏목</color></b>", "강과 호수 위를 건널 수 있다. 물 위에서도 평소처럼 이동하면 된다.");
            }
        }
    }
}
