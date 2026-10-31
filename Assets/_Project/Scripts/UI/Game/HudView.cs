using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
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
        [SerializeField] Image goldIcon;
        [SerializeField] Text goldText, regionText, coordText;
        [SerializeField] RectTransform goldHover;
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
        [SerializeField] Text fpsText;                 // 설정 → 그래픽 → FPS 표시

        [SerializeField, Tooltip("Seconds between rebuilding the HUD texts (gold, quests, abilities…)")] float textInterval = 0.1f;

        readonly List<Image> dashes = new List<Image>();
        readonly List<Image> dashFills = new List<Image>();
        static readonly Act[] SkillActs = { Act.SkillQ, Act.SkillW, Act.SkillE, Act.SkillR };
        float textT;
        int shownHp = -1, shownHpMax = -1;
        int fpsFrames;
        float fpsTime, fpsWorst;
        int hintLevel = -1;
        string hintText;
        Interactable shownInteract;
        float bossShown;
        Vector2 bossBase;
        Game g;

        public void Init(Game game)
        {
            g = game;
            if (bossBar != null) bossBase = bossBar.anchoredPosition;
            UIKit.Sprite(goldIcon, Art.Item("gold"));
        }

        /// <summary>Average FPS and the slowest frame of the last half second (a stutter shows as a long worst frame).</summary>
        void TickFps()
        {
            UIKit.Show(fpsText, GraphicsQuality.ShowFps);
            if (fpsText == null || !GraphicsQuality.ShowFps) return;
            float dt = Time.unscaledDeltaTime;
            fpsFrames++; fpsTime += dt; fpsWorst = Mathf.Max(fpsWorst, dt);
            if (fpsTime < 0.5f) return;
            int fps = Mathf.RoundToInt(fpsFrames / fpsTime), worstMs = Mathf.RoundToInt(fpsWorst * 1000);
            UIKit.Set(fpsText, $"{fps} FPS  <color={(worstMs > 34 ? "#ff8a8a" : "#8c7aa8")}>최대 {worstMs}ms</color>");
            fpsFrames = 0; fpsTime = 0; fpsWorst = 0;
        }

        string NextSkillHint(int level)
        {
            if (level != hintLevel) { hintLevel = level; hintText = $"레벨 {level}에 익힌다. 대장장이에게 무기를 강화해도 배울 수 있다."; }
            return hintText;
        }

        static readonly ProfilerMarker HudMarker = new ProfilerMarker("Diverse.Hud");   // read by DesignPerf / PerfProbe

        public void Tick()
        {
            using var _perf = HudMarker.Auto();
            var p = g.Player;
            TickFps();
            if (p == null) return;
            var w = g.World;

            UIKit.Sprite(portrait, Art.Rabbit(p.Costume, Pose.Idle0));
            // Texts are rebuilt a few times a second, not every frame: each rebuild allocates new strings (GC hitches)
            textT -= Time.unscaledDeltaTime;
            bool slow = textT <= 0;
            if (slow) textT = textInterval;
            if (slow) UIKit.Set(heroLine, $"<b>{p.HeroName}</b>  <color=#c9b8ff>Lv.{p.Level}</color>  <color=#8c7aa8>{w.lifeCount}번째 삶</color>");
            UIKit.Fill(hpFill, p.hp / p.maxHp);
            int hp = Mathf.CeilToInt(p.hp), hpMax = Mathf.CeilToInt(p.maxHp);
            if (hp != shownHp || hpMax != shownHpMax) { shownHp = hp; shownHpMax = hpMax; UIKit.Set(hpText, $"{hp}/{hpMax}"); }
            UIKit.Show(shieldFill, p.Shield > 0);
            UIKit.Fill(shieldFill, p.Shield / p.maxHp);
            UIKit.Fill(xpFill, p.Xp / p.XpToNext);
            UIKit.Pool(dashTemplate, dashes, p.DashChargesMax);
            while (dashFills.Count < dashes.Count) dashFills.Add(dashes[dashFills.Count].transform.Find("Fill")?.GetComponent<Image>());
            for (int i = 0; i < dashes.Count; i++)
            {
                float k = i < p.DashCharges ? 1 : i == p.DashCharges ? p.DashRechargeProgress : 0;
                UIKit.Fill(dashFills[i], k);
            }

            if (slow)
            {
                UIKit.Set(goldText, p.Gold.ToString());
                UIKit.Set(regionText, World.I.Gen.RegionName(p.Pos));
                UIKit.Set(coordText, $"({p.Pos.x:0}, {p.Pos.y:0})  {Controls.KeyName(Act.Map)} 지도");
                UIKit.Set(moveHint, $"<color=#8c7aa8>{Controls.KeyName(Act.Move)}</color> 이동/공격");
                UIKit.Set(dashHint, $"<color=#8c7aa8>{Controls.KeyName(Act.Dash)}</color> 대시");
                var qs = w.quests.Where(x => x.status == 1 || x.status == 2).Take(3).Select(q =>
                {
                    string st = q.status == 2 ? "<color=#8bff9a>✔ 보고</color> " : q.kind == "hunt" ? $"<color=#c9b8ff>{q.have}/{q.need}</color> " : "";
                    string dist = q.status == 1 && q.kind != "hunt" ? $" <color=#8c7aa8>{Vector2.Distance(p.Pos, new Vector2(q.tx, q.ty)):0}m</color>" : "";
                    return st + q.title + dist;
                });
                UIKit.Set(questsText, string.Join("\n", qs));
            }
            if (minimap != null) minimap.Tick(g);

            var levels = DB.Progression.skillLevels;
            int learned = p.LearnedSkills;
            for (int i = 0; i < skills.Length; i++)
            {
                // Only the next skill in line says when it comes; later ones just say they're locked
                string hint = i == learned && i < levels.Length ? NextSkillHint(levels[i]) : i > learned ? "앞의 기술을 먼저 익혀야 한다." : null;
                skills[i]?.Tick(p, p.Skills[i], Controls.KeyName(SkillActs[i]), tooltip, hint);
            }

            if (slow) UIKit.Set(abilitiesText, string.Join("\n", p.Abilities.Owned.AsEnumerable().Reverse().Take(6).Reverse()
                .Select(a => $"<color={(a.source == "ai" ? "#c9b8ff" : "#fff2b3")}>◆</color> {a.name}")));
            UIKit.Show(evolveHint, p.PendingEvolutions > 0);
            if (p.PendingEvolutions > 0)
            {
                if (slow) UIKit.Set(evolveHint, $"<color=#fff2b3>★ 진화 가능 ×{p.PendingEvolutions}</color> — [{Controls.KeyName(Act.Abilities)}] 또는 전투 종료 시 자동");
                var c = evolveHint.color; c.a = 0.6f + Mathf.Sin(Time.unscaledTime * 5) * 0.4f; evolveHint.color = c;
            }
            UIKit.Show(attrHint, p.UnspentAttr > 0);
            if (slow && p.UnspentAttr > 0) UIKit.Set(attrHint, $"<color=#8bff9a>+ 능력치 {p.UnspentAttr}점</color> — [{Controls.KeyName(Act.Pause)}] → 능력치");

            // Interaction hint above the nearest interactable
            var it = g.State == GameState.Playing ? Interactable.Nearest(p.Pos) : null;
            UIKit.Show(interactHint, it != null);
            if (it == null) shownInteract = null;
            if (it != null)
            {
                Vector2 sp = CameraRig.I.Cam.WorldToScreenPoint(it.Pos + Vector2.up * 1.6f);
                var parent = (RectTransform)interactHint.parent;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, sp, null, out var local)) interactHint.localPosition = local;
                if (slow || it != shownInteract) UIKit.Set(interactText, $"[{Controls.KeyName(Act.Interact)}] {it.label}");
                shownInteract = it;
            }

            bool boss = g.Boss != null && g.Boss.Alive && Vector2.Distance(p.Pos, g.Boss.Pos) <= 20f;
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
            }
        }
    }
}
