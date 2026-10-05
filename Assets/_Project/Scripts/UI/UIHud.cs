using System.Linq;
using UnityEngine;

namespace Diverse
{
    public partial class UI
    {
        // ───────────────────────── HUD ─────────────────────────

        void DrawHud()
        {
            var p = g.Player;
            if (p == null) return;

            // Top left: portrait + HP/XP
            var pr = new Rect(U(8), U(8), U(40), U(40));
            GUI.Box(pr, GUIContent.none, box);
            Sprite(new Rect(pr.x + U(2), pr.y + U(2), pr.width - U(4), pr.height - U(4)), Art.Rabbit(p.Costume, Pose.Idle0));
            float bx = pr.xMax + U(6);
            Shadowed(new Rect(bx, U(7), U(200), U(14)), $"<b>{p.HeroName}</b>  <color=#c9b8ff>Lv.{p.Level}</color>  <color=#8c7aa8>{g.World.lifeCount}번째 삶</color>", small);
            Bar(new Rect(bx, U(21), U(130), U(9)), p.hp / p.maxHp, new Color(0.95f, 0.33f, 0.4f), new Color(0.25f, 0.1f, 0.15f), $"{Mathf.CeilToInt(p.hp)}/{Mathf.CeilToInt(p.maxHp)}");
            if (p.Shield > 0) Bar(new Rect(bx, U(31), U(130 * Mathf.Clamp01(p.Shield / p.maxHp)), U(2)), 1, new Color(0.55f, 0.9f, 1f), Color.clear);
            Bar(new Rect(bx, U(35), U(130), U(5)), p.Xp / p.XpToNext, new Color(0.45f, 0.75f, 1f), new Color(0.1f, 0.12f, 0.25f));
            // Dash charges
            for (int i = 0; i < p.DashChargesMax; i++)
            {
                var r = new Rect(bx + i * U(10), U(43), U(8), U(4));
                float k = i < p.DashCharges ? 1 : i == p.DashCharges ? p.DashRechargeProgress : 0;
                Bar(r, k, new Color(0.75f, 0.9f, 1f), new Color(0.15f, 0.15f, 0.25f));
            }

            // Top right: gold, shards, potions, region
            var tr = new Rect(Screen.width - U(170), U(8), U(162), U(46));
            GUI.Box(tr, GUIContent.none, box);
            Sprite(new Rect(tr.x + U(4), tr.y + U(4), U(10), U(10)), Art.Item("gold"));
            Shadowed(new Rect(tr.x + U(16), tr.y + U(3), U(60), U(12)), p.Gold.ToString(), small);
            Sprite(new Rect(tr.x + U(60), tr.y + U(4), U(10), U(10)), Art.Item("shard"));
            Shadowed(new Rect(tr.x + U(72), tr.y + U(3), U(40), U(12)), g.World.memoryShards.ToString(), small);
            Sprite(new Rect(tr.x + U(104), tr.y + U(4), U(10), U(10)), Art.Item("potion"));
            Shadowed(new Rect(tr.x + U(116), tr.y + U(3), U(50), U(12)), $"{p.Potions} [{Controls.KeyName(Act.Potion)}]", small);
            Shadowed(new Rect(tr.x + U(4), tr.y + U(17), tr.width, U(12)), $"<color=#fff2b3>{World.I.Gen.RegionName(p.Pos)}</color>", small);
            Shadowed(new Rect(tr.x + U(4), tr.y + U(30), tr.width, U(12)), $"<color=#8c7aa8>({p.Pos.x:0}, {p.Pos.y:0})  {Controls.KeyName(Act.Map)} 지도</color>", small);
            if (p.HasRaft) Sprite(new Rect(tr.xMax - U(14), tr.y + U(30), U(10), U(10)), Art.Item("raft"));

            // Currency tooltips
            var mouse = Event.current.mousePosition;
            (string head, string body)? pendingTip = null;
            if (new Rect(tr.x, tr.y, U(56), U(15)).Contains(mouse))
                pendingTip = ("<b><color=#fff2b3>골드</color></b>",
                    $"몬스터·상자·의뢰에서 얻는다. 상점, 대장간, 여관, 진화 다시 뽑기({g.RerollCost}G)와 이야기꾼과의 대화({g.ChatCost}G)에 쓴다.\n죽으면 절반이 기억의 금고로 가고, 금고의 골드는 다음 삶에 일부 전해진다. (금고: {g.World.gold}G)");
            else if (new Rect(tr.x + U(58), tr.y, U(44), U(15)).Contains(mouse))
                pendingTip = ("<b><color=#c9b8ff>기억의 조각</color></b>",
                    $"세계가 기억하는 재화 — 죽어도 사라지지 않는다. 보스, 제단, 유적, 의뢰에서 얻는다.\n기억의 탑 다음 층을 여는 데 {Dialogue.TowerCost(g.World.towerFloor)}개가 필요하다. 층이 열릴 때마다 모든 삶의 최대 체력이 오른다.");
            else if (new Rect(tr.x + U(102), tr.y, U(60), U(15)).Contains(mouse))
                pendingTip = ("<b><color=#ff8a8a>회복약</color></b>",
                    $"[{Controls.KeyName(Act.Potion)}] 최대 체력의 40%를 회복한다. 최대 5개.\n마을 상인에게 30골드에 살 수 있고, 상자에서도 나온다.");
            else if (p.HasRaft && new Rect(tr.xMax - U(16), tr.y + U(28), U(14), U(14)).Contains(mouse))
                pendingTip = ("<b><color=#c4936a>뗏목</color></b>", "강과 호수 위를 건널 수 있다. 물 위에서도 평소처럼 이동하면 된다.");

            // Minimap
            DrawMinimap(new Rect(Screen.width - U(78), U(58), U(70), U(70)));

            // Active quests
            float qy = U(132);
            foreach (var q in g.World.quests.Where(x => x.status == 1 || x.status == 2).Take(3))
            {
                string st = q.status == 2 ? "<color=#8bff9a>✔ 보고</color> " : q.kind == "hunt" ? $"<color=#c9b8ff>{q.have}/{q.need}</color> " : "";
                string dist = q.status == 1 && q.kind != "hunt" ? $" <color=#8c7aa8>{Vector2.Distance(p.Pos, new Vector2(q.tx, q.ty)):0}m</color>" : "";
                Shadowed(new Rect(Screen.width - U(170), qy, U(162), U(12)), st + q.title + dist, new GUIStyle(small) { alignment = TextAnchor.UpperRight });
                qy += U(12);
            }

            // Bottom center: skill bar
            DrawSkillBar(p);

            // Bottom left: owned abilities
            float ay = Screen.height - U(16);
            int n = 0;
            foreach (var a in p.Abilities.Owned.AsEnumerable().Reverse().Take(6))
            {
                Shadowed(new Rect(U(8), ay, U(240), U(12)), $"<color={(a.source == "ai" ? "#c9b8ff" : "#fff2b3")}>◆</color> {a.name}", small);
                ay -= U(12); n++;
            }
            if (p.PendingEvolutions > 0)
            {
                float pulse = 0.6f + Mathf.Sin(Time.unscaledTime * 5) * 0.4f;
                Shadowed(new Rect(U(8), ay - U(4), U(260), U(14)), $"<color=#fff2b3>★ 진화 가능 ×{p.PendingEvolutions}</color> — [{Controls.KeyName(Act.Abilities)}] 또는 전투 종료 시 자동", small, new Color(1, 1, 1, pulse));
            }
            if (p.UnspentAttr > 0) Shadowed(new Rect(U(8), ay - U(18), U(260), U(14)), $"<color=#8bff9a>+ 능력치 {p.UnspentAttr}점</color> — [{Controls.KeyName(Act.Pause)}] → 능력치", small);

            // Interaction hint
            var it = Interactable.Nearest(p.Pos);
            if (it != null && g.State == GameState.Playing)
            {
                var sp = CameraRig.I.Cam.WorldToScreenPoint(it.Pos + Vector2.up * 1.6f);
                var r = new Rect(sp.x - U(60), Screen.height - sp.y - U(8), U(120), U(14));
                GUI.Box(new Rect(r.x + U(10), r.y, r.width - U(20), r.height), GUIContent.none, box);
                Shadowed(r, $"[{Controls.KeyName(Act.Interact)}] {it.label}", smallC);
            }

            // Boss bar
            if (g.Boss != null && g.Boss.Alive)
            {
                bossBarShown = Mathf.MoveTowards(bossBarShown, 1, Time.unscaledDeltaTime * 3);
                var r = new Rect(Screen.width * 0.25f, U(20) - (1 - bossBarShown) * U(30), Screen.width * 0.5f, U(10));
                Shadowed(new Rect(r.x, r.y - U(13), r.width, U(12)), $"<b>{g.Boss.def.name}</b>", smallC, Pal.Hex("ff8a8a"));
                Bar(r, g.Boss.hp / g.Boss.maxHp, new Color(0.85f, 0.2f, 0.3f), new Color(0.2f, 0.05f, 0.1f));
            }
            else bossBarShown = 0;

            // Generating indicator
            if (g.Generating && g.State != GameState.Evolving)
                Shadowed(new Rect(0, Screen.height * 0.3f, Screen.width, U(14)), g.OfferStatus ?? "…", smallC);
            if (pendingTip.HasValue && g.State == GameState.Playing) Tooltip(mouse, pendingTip.Value.head, pendingTip.Value.body);
        }

        /// <summary>A small panel next to the cursor that stays on screen.</summary>
        void Tooltip(Vector2 at, string head, string body)
        {
            float w = U(190);
            float h = small.CalcHeight(new GUIContent(body), w - U(12)) + U(22);
            var r = new Rect(at.x - w - U(6), at.y + U(10), w, h);
            if (r.x < U(4)) r.x = at.x + U(12);
            if (r.yMax > Screen.height - U(4)) r.y = Screen.height - U(4) - h;
            GUI.Box(r, GUIContent.none, panel);
            Shadowed(new Rect(r.x + U(6), r.y + U(4), r.width - U(12), U(14)), head, small);
            GUI.Label(new Rect(r.x + U(6), r.y + U(18), r.width - U(12), h - U(20)), body, small);
        }

        void DrawSkillBar(Player p)
        {
            float slot = U(30), gap = U(4);
            float total = slot * 4 + gap * 3;
            float x = (Screen.width - total) / 2, y = Screen.height - slot - U(10);
            Act[] acts = { Act.SkillQ, Act.SkillW, Act.SkillE, Act.SkillR };
            for (int i = 0; i < 4; i++)
            {
                var s = p.Skills[i];
                var r = new Rect(x + i * (slot + gap), y, slot, slot);
                GUI.Box(r, GUIContent.none, box);
                if (s.def != null)
                {
                    var ic = new GUIStyle(title) { fontSize = Mathf.RoundToInt(title.fontSize * 0.9f) };
                    Shadowed(r, s.def.icon, ic, s.def.color);
                    if (s.cd > 0)
                    {
                        float k = s.cd / (s.def.cooldown * (1 - p.Stats[StatId.CooldownReduction]));
                        GUI.DrawTexture(new Rect(r.x + U(2), r.y + U(2), r.width - U(4), (r.height - U(4)) * Mathf.Clamp01(k)), whiteTex, ScaleMode.StretchToFill, true, 0, new Color(0.05f, 0.03f, 0.1f, 0.7f), 0, 0);
                        Shadowed(r, s.cd.ToString(s.cd < 1 ? "0.0" : "0"), labelC);
                    }
                    var hover = r.Contains(Event.current.mousePosition);
                    if (hover)
                    {
                        var tip = new Rect(r.x - U(60), r.y - U(48), U(150), U(44));
                        GUI.Box(tip, GUIContent.none, panel);
                        Shadowed(new Rect(tip.x + U(6), tip.y + U(4), tip.width - U(12), U(40)), $"<b>{s.def.name}</b> <color=#8c7aa8>({s.def.cooldown}초)</color>\n{s.def.desc}", small);
                    }
                }
                Shadowed(new Rect(r.x + U(2), r.y + U(1), r.width, U(10)), Controls.KeyName(acts[i]), small, Pal.Hex("fff2b3"));
            }
            // Basic attack / dash keys
            Shadowed(new Rect(x + total + U(6), y + U(4), U(120), U(12)), $"<color=#8c7aa8>{Controls.KeyName(Act.Move)}</color> 이동/공격", small);
            Shadowed(new Rect(x + total + U(6), y + U(16), U(120), U(12)), $"<color=#8c7aa8>{Controls.KeyName(Act.Dash)}</color> 대시", small);
        }

        // ───────────────────────── Dialogue ─────────────────────────

        void DrawDialogue()
        {
            var d = Dialogue.Current;
            if (d == null) { g.CloseOverlay(); return; }
            var area = new Rect(Screen.width * 0.12f, Screen.height - U(150), Screen.width * 0.76f, U(140));
            GUI.Box(area, GUIContent.none, panel);
            var pr = new Rect(area.x + U(10), area.y + U(10), U(56), U(56));
            GUI.Box(pr, GUIContent.none, box);
            Sprite(new Rect(pr.x + U(3), pr.y + U(3), pr.width - U(6), pr.height - U(6)), d.portrait);
            Shadowed(new Rect(pr.xMax + U(10), area.y + U(8), area.width, U(14)), $"<b><color=#fff2b3>{d.speaker}</color></b>", label);
            GUI.Label(new Rect(pr.xMax + U(10), area.y + U(24), area.width - U(90), U(60)), d.text, label);
            float oy = area.y + U(86);
            float ow = (area.width - U(30)) / 2;
            for (int i = 0; i < d.options.Count; i++)
            {
                var o = d.options[i];
                var r = new Rect(area.x + U(10) + (i % 2) * (ow + U(10)), oy + (i / 2) * U(22) - (d.options.Count > 4 ? U(22) * ((d.options.Count - 1) / 2 - 1) : 0), ow, U(19));
                if (Btn(r, $"{i + 1}. {o.text}", o.enabled) || (o.enabled && Controls.KeyDown(UnityEngine.InputSystem.Key.Digit1 + i)))
                {
                    o.act?.Invoke();
                    return;
                }
            }
        }

        // ───────────────────────── Death ─────────────────────────

        GraveRecord deathGrave;
        public void ShowDeath(GraveRecord gr) { deathGrave = gr; }

        void DrawDeath()
        {
            if (deathGrave == null) return;
            Dim(0.65f);
            var area = Center(320, 220);
            GUI.Box(area, GUIContent.none, panel);
            Shadowed(new Rect(area.x, area.y + U(10), area.width, U(20)), $"{deathGrave.heroName}의 삶이 끝났다", title, Pal.Hex("ff8a8a"));
            Sprite(new Rect(area.x + area.width / 2 - U(16), area.y + U(36), U(32), U(44)), Art.Prop("grave"));
            GUI.Label(new Rect(area.x + U(16), area.y + U(86), area.width - U(32), U(30)), $"<i>\"{deathGrave.epitaph}\"</i>", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            string carried = deathGrave.abilities.Count > 0 ? "무덤에 새겨진 능력: " + string.Join(", ", deathGrave.abilities.Select(a => a.name)) : "무덤에 남은 능력이 없다.";
            GUI.Label(new Rect(area.x + U(16), area.y + U(120), area.width - U(32), U(40)),
                $"<color=#c9b8ff>{carried}</color>\n지닌 골드의 절반은 금고로 갔다. 다음 삶에서 무덤을 찾아 능력을 되찾자.", new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
            if (Btn(new Rect(area.x + area.width / 2 - U(70), area.yMax - U(32), U(140), U(22)), "다음 삶 시작하기")) { deathGrave = null; g.ToCharacterSelect(); }
        }
    }
}
