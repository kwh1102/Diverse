using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public class DialogueOption
    {
        public string text;
        public System.Action act;
        public bool enabled = true;
        public DialogueOption(string t, System.Action a, bool en = true) { text = t; act = a; enabled = en; }
    }

    public class DialoguePage
    {
        public string speaker;
        public Sprite portrait;
        public string text;
        public List<DialogueOption> options = new List<DialogueOption>();
    }

    /// <summary>
    /// Interaction dialogue. Builds a page per object type.
    /// Dialogue lines reflect the world memory (WorldSave): previous lives, quest results, etc.
    /// </summary>
    public static class Dialogue
    {
        public static DialoguePage Current;
        static Interactable target;

        static void Show(DialoguePage p)
        {
            Current = p;
            if (Game.I.State != GameState.Dialogue) Game.I.OpenOverlay(GameState.Dialogue);
        }

        static void Close() { Current = null; Game.I.CloseOverlay(); }
        static DialogueOption Bye(string t = "떠나기") => new DialogueOption(t, Close);

        public static void Open(Game g, Interactable it)
        {
            target = it;
            var w = g.World;
            var p = g.Player;
            switch (it.kind)
            {
                case "npc_storyteller": Storyteller(g); break;
                case "board": Board(g); break;
                case "npc_merchant": Merchant(g); break;
                case "npc_smith": Smith(g); break;
                case "inn": Inn(g); break;
                case "bank": Bank(g); break;
                case "npc_villager": Villager(g, it); break;
                case "chest": Chest(g, it); break;
                case "grave": Grave(g, it); break;
                case "statue": Statue(g, it); break;
                case "shrine": Shrine(g, it); break;
                case "ruin_altar": RuinAltar(g, it); break;
                case "cage": Cage(g, it); break;
                case "herb": Herb(g, it); break;
                case "wanderer": Wanderer(g, it); break;
                case "tower_gate": Tower(g, it); break;
                default: Show(new DialoguePage { speaker = it.label, text = "...", options = { Bye() } }); break;
            }
        }

        // ───────────── Town ─────────────

        static void Storyteller(Game g)
        {
            var w = g.World;
            var lastGrave = w.graves.LastOrDefault();
            string text;
            if (w.lifeCount <= 1)
                text = "오, 처음 보는 얼굴이구나. 난 이 세계의 기록을 지키는 올리란다.\n저 멀리 북쪽에 빛나는 탑이 보이니? 기억의 탑이야. 세계가 기억하는 삶에게만 문을 열지.\n나가서 살아 보렴. 네가 한 일은 내가 적어 두마.";
            else if (lastGrave != null)
                text = $"...돌아왔구나. 예전의 그 토끼는 아니지만.\n지난번엔 {Ko.I(lastGrave.heroName)} {Ko.Ro(lastGrave.cause)} 인해 쓰러졌지. 그 자리에 무덤이 있단다.\n찾아가면 남겨진 기술을 되찾을 수 있을지도 몰라.";
            else text = "어서 오렴. 세계가 너의 다음 이야기를 기다리고 있어.";

            var page = new DialoguePage { speaker = "이야기꾼 올리", portrait = Art.Npc("owl", 0), text = text };
            page.options.Add(new DialogueOption("지난 기록 듣기", () => { Game.I.Ui.OpenTab("chronicle"); Close(); Game.I.OpenOverlay(GameState.Paused, "chronicle"); }));
            if (w.graves.Count > 0)
                page.options.Add(new DialogueOption("지난 영웅을 기록하거나 잊기", () => Remember(g)));
            page.options.Add(new DialogueOption($"기억의 조각 바치기 (보유 {w.memoryShards})", () =>
            {
                Show(new DialoguePage
                {
                    speaker = "이야기꾼 올리", portrait = Art.Npc("owl", 0),
                    text = $"기억의 조각은 탑의 열쇠란다. 탑 {w.towerFloor + 1}층에는 {TowerCost(w.towerFloor)}개가 필요해.\n지금 {w.memoryShards}개 가지고 있구나. 제단, 유적, 보스의 둥지에서 모아 오렴.",
                    options = { Bye("알겠어요") },
                });
            }));
            page.options.Add(Bye());
            Show(page);
        }

        static void Remember(Game g)
        {
            var w = g.World;
            var page = new DialoguePage { speaker = "이야기꾼 올리", portrait = Art.Npc("owl", 0), text = "기록된 영웅은 광장에 석상이 세워지지만, 잊힌 영웅의 무덤은 기억의 조각이 되어 세계로 돌아간단다.\n누구 이야기를 할까?" };
            foreach (var gr in w.graves.Where(x => string.IsNullOrEmpty(x.forgottenBy)).Take(4))
            {
                var grave = gr;
                page.options.Add(new DialogueOption($"{grave.life}번째 삶 {grave.heroName} (Lv.{grave.level}, {DB.W((WeaponKind)grave.weapon).name})", () =>
                {
                    Show(new DialoguePage
                    {
                        speaker = "이야기꾼 올리", portrait = Art.Npc("owl", 0),
                        text = $"{grave.heroName}... \"{grave.epitaph}\"\n기록으로 남길까, 아니면 보내 줄까?",
                        options =
                        {
                            new DialogueOption("기록하기 (+석상, 공격력 ×1.03)", () =>
                            {
                                grave.forgottenBy = "recorded"; grave.statue = true;
                                grave.x = Random.Range(-10f, 10f); grave.y = Random.Range(9f, 11f);
                                w.Log($"이야기꾼이 {grave.heroName}의 이름을 기록했다. 광장에 석상이 세워졌다.", "legacy");
                                Game.I.Player.RecalcStats();
                                Game.I.Ui.Toast($"광장에 {grave.heroName}의 석상이 세워집니다. (마을을 나갔다 오면 보여요)");
                                Close();
                            }),
                            new DialogueOption("보내 주기 (기억의 조각 +2)", () =>
                            {
                                grave.forgottenBy = "forgotten"; w.memoryShards += 2;
                                w.Log($"{grave.heroName}의 이름이 잊혔다. 그 기억은 조각이 되어 세계로 돌아갔다.", "legacy");
                                Game.I.Ui.Toast("기억의 조각 +2");
                                Close();
                            }),
                            Bye("나중에"),
                        },
                    });
                }));
            }
            page.options.Add(Bye("나중에"));
            Show(page);
        }

        public static int TowerCost(int floor) => 3 + floor * 3;

        static void Board(Game g)
        {
            var w = g.World;
            g.EnsureQuests();
            var page = new DialoguePage { speaker = "의뢰 게시판", portrait = Art.Prop("board"), text = "마을 사람들과 여행자들의 의뢰가 붙어 있다." };
            foreach (var q in w.quests.Where(x => x.status < 3))
            {
                var quest = q;
                string st = q.status switch { 0 => "[새 의뢰]", 1 => q.kind == "hunt" ? $"[진행 중 {q.have}/{q.need}]" : "[진행 중]", _ => "[완료! 보상 받기]" };
                page.options.Add(new DialogueOption($"{st} {q.title}", () =>
                {
                    if (quest.status == 0)
                    {
                        Show(new DialoguePage
                        {
                            speaker = quest.title, portrait = Art.Prop("board"),
                            text = quest.desc + $"\n\n보상: 골드 {quest.rewardGold}" + (quest.rewardShards > 0 ? $", 기억의 조각 {quest.rewardShards}개" : "") + (quest.kind != "hunt" ? "\n(수락하면 지도에 위치가 표시됩니다.)" : ""),
                            options = { new DialogueOption("수락", () => { quest.status = 1; Game.I.Ui.Toast($"의뢰 수락: {quest.title}"); Close(); }), Bye("돌아가기") },
                        });
                    }
                    else if (quest.status == 2) { Game.I.ClaimQuest(quest); Close(); }
                    else Close();
                }));
            }
            page.options.Add(Bye());
            Show(page);
        }

        static void Merchant(Game g)
        {
            var p = g.Player;
            var page = new DialoguePage { speaker = "상인 냥냥", portrait = Art.Npc("cat", 0), text = $"어서 오라냥! 골드는 있냥? 지금 {p.Gold} 골드 있다냥." };
            page.options.Add(new DialogueOption($"회복약 (30 골드) — 보유 {p.Potions}/5", () =>
            {
                if (p.Gold >= 30 && p.Potions < 5) { p.Gold -= 30; p.Potions++; Sfx.Play("coin"); }
                Merchant(g);
            }, p.Gold >= 30 && p.Potions < 5));
            page.options.Add(new DialogueOption("대시 신발 (80 골드) — 대시 횟수 +1", () =>
            {
                if (p.Gold >= 80) { p.Gold -= 80; p.DashChargesMax++; p.DashCharges++; Sfx.Play("coin"); }
                Merchant(g);
            }, p.Gold >= 80 && p.DashChargesMax < 4));
            page.options.Add(Bye());
            Show(page);
        }

        static void Smith(Game g)
        {
            var p = g.Player;
            int lvl = p.Weapon.baseDamage > DB.W(p.Weapon.kind).baseDamage ? Mathf.RoundToInt((p.Weapon.baseDamage / DB.W(p.Weapon.kind).baseDamage - 1) / 0.12f) : 0;
            int cost = 50 + lvl * 40;
            var page = new DialoguePage { speaker = "대장장이 브루노", portrait = Art.Npc("bear", 0), text = $"흠. 쓸 만한 {p.Weapon.name}. 좀 더 벼려 줄까? (현재 강화 +{lvl})" };
            page.options.Add(new DialogueOption($"무기 강화 ({cost} 골드) — 공격력 +12%", () =>
            {
                if (p.Gold >= cost) { p.Gold -= cost; p.Weapon.baseDamage *= 1.12f; p.RecalcStats(); Sfx.Play("hit_metal"); CameraRig.I.Shake(0.2f); Game.I.Ui.Toast($"{p.Weapon.name} +{lvl + 1}"); }
                Close();
            }, p.Gold >= cost));
            page.options.Add(Bye());
            Show(page);
        }

        static void Inn(Game g)
        {
            var p = g.Player;
            var page = new DialoguePage { speaker = "바람결 여관", portrait = Art.House(0, Pal.Hex("c2603e")), text = "따뜻한 수프와 폭신한 침대. (휴식하면 체력이 모두 회복됩니다. 10 골드)" };
            page.options.Add(new DialogueOption("휴식 (10 골드)", () => { if (p.Gold >= 10) { p.Gold -= 10; p.Heal(p.maxHp); Sfx.Play("levelup", 0.5f); } Close(); }, p.Gold >= 10));
            if (p.UnspentAttr > 0) page.options.Add(new DialogueOption($"생각 정리하기 (능력치 {p.UnspentAttr}점 분배)", () => { Close(); Game.I.OpenOverlay(GameState.Paused, "attrs"); }));
            page.options.Add(Bye());
            Show(page);
        }

        static void Bank(Game g)
        {
            var w = g.World; var p = g.Player;
            var page = new DialoguePage { speaker = "기억의 금고", portrait = Art.House(0, Pal.Hex("8c5bd6")), text = $"여기 맡긴 골드는 다음 삶에게 전해집니다.\n금고: {w.gold} 골드 / 보유: {p.Gold} 골드" };
            page.options.Add(new DialogueOption("전부 맡기기", () => { w.gold += p.Gold; p.Gold = 0; Sfx.Play("coin"); Bank(g); }, p.Gold > 0));
            page.options.Add(new DialogueOption("50 꺼내기", () => { int a = Mathf.Min(50, w.gold); w.gold -= a; p.Gold += a; Sfx.Play("coin"); Bank(g); }, w.gold > 0));
            page.options.Add(Bye());
            Show(page);
        }

        static readonly string[][] villagerLines =
        {
            new[] { "오늘은 날씨가 좋네요. 저 너머 숲은 좀 으스스하지만...", "어제 탑의 빛이 더 밝아졌대요." },
            new[] { "할아버지 말로는 탑에 오르려던 영웅이 셀 수 없이 많았대요.", "게시판에서 의뢰를 받으면 먹고살 만큼은 벌 거예요." },
            new[] { "지나가던 방랑자가 동쪽에서 여우 산적 야영지를 봤대요.", "무덤은 너무 멀리 남기지 마세요. 다시 찾아가기 힘들거든요." },
        };

        static void Villager(Game g, Interactable it)
        {
            var w = g.World;
            int i = it.data is int n ? n : 0;
            string line;
            if (i == 99) line = "야영지가 소탕된 덕분에 이제 여기서 편히 쉴 수 있어요. 세계는 그 방랑자를 기억해요.";
            else
            {
                // Mentions past heroes
                var statue = w.graves.LastOrDefault(x => x.statue);
                if (statue != null && Random.value < 0.4f) line = $"{statue.heroName}의 석상 봤어요? 몬스터를 {statue.kills}마리나 쓰러뜨렸대요!";
                else if (w.clearedCamps.Count > 0 && Random.value < 0.4f) line = $"방랑자들 덕분에 이 근처 몬스터 야영지 {w.clearedCamps.Count}곳이 사라졌어요. 이젠 상인들도 와요.";
                else line = villagerLines[i % villagerLines.Length][Random.Range(0, 2)];
            }
            Show(new DialoguePage { speaker = it.label, portrait = it.sr.sprite, text = line, options = { Bye("잘 있어요") } });
        }

        // ───────────── Field ─────────────

        static void Chest(Game g, Interactable it)
        {
            var s = it.data as StructureSpec;
            if (g.World.Flag("chest:" + s.id)) { Show(new DialoguePage { speaker = "빈 상자", portrait = Art.Prop("chest_open"), text = "안에 아무것도 남아 있지 않다.", options = { Bye() } }); return; }
            g.World.SetFlag("chest:" + s.id);
            it.sr.sprite = Art.Prop("chest_open");
            it.label = "빈 상자";
            Sfx.Play("chest");
            int gold = 20 + s.tier * 12 + Random.Range(0, 20);
            Pickup.Drop("gold", gold / 5f, it.Pos + Vector2.down * 0.3f, 5);
            if (s.kind == StructKind.Lair || Random.value < 0.35f) Pickup.Drop("shard", 1, it.Pos + Vector2.down * 0.3f);
            if (Random.value < 0.4f) g.Player.Potions = Mathf.Min(5, g.Player.Potions + 1);
            Fx.I?.Burst(it.Pos + Vector2.up * 0.4f, Pal.Gold, 16, 4, 0.6f, -3);
            GameEvents.World("explore", "chest");
            Game.I.Ui.Toast("상자를 열었다!");
        }

        static void Grave(Game g, Interactable it)
        {
            var gr = it.data as GraveRecord;
            var page = new DialoguePage
            {
                speaker = $"{gr.life}번째 삶 — {gr.heroName}의 무덤", portrait = Art.Prop("grave"),
                text = $"\"{gr.epitaph}\"\n레벨 {gr.level} · {DB.W((WeaponKind)gr.weapon).name} · 몬스터 {gr.kills}마리 처치",
            };
            if (!gr.recovered && gr.abilities.Count > 0)
            {
                page.text += "\n\n무덤에 아직 무언가가 남아 있다...";
                foreach (var rec in gr.abilities)
                {
                    var r = rec;
                    page.options.Add(new DialogueOption($"[{r.name}] 계승하기", () =>
                    {
                        var graph = r.ToGraph();
                        graph.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                        AbilityValidator.Balance(graph, g.Player);
                        g.Player.Abilities.Add(graph);
                        gr.recovered = true;
                        g.World.Log($"{Ko.I(g.HeroName)} {gr.heroName}의 무덤에서 [{graph.name}] 능력을 계승했다.", "legacy");
                        Fx.I?.Play(Art.Shockwave(Pal.ShadowEl, 30), it.Pos, 0, 16, 1, null, true);
                        Fx.I?.Burst(it.Pos + Vector2.up * 0.5f, Pal.ShadowEl, 30, 5, 1f, -3);
                        Sfx.Play("evolve");
                        Game.I.Ui.Toast($"{gr.heroName}의 능력 [{graph.name}] 계승");
                        GameEvents.World("investigate", "grave");
                        Close();
                    }));
                }
                page.options.Add(new DialogueOption("편히 잠들게 두기 (기억의 조각 +1)", () => { gr.recovered = true; g.World.memoryShards++; Close(); }));
            }
            page.options.Add(Bye("고개 숙여 인사하고 떠나기"));
            Show(page);
        }

        static void Statue(Game g, Interactable it)
        {
            var gr = it.data as GraveRecord;
            Show(new DialoguePage
            {
                speaker = $"영웅 {gr.heroName}의 석상", portrait = Art.Prop("statue"),
                text = $"\"{gr.epitaph}\"\n받침대에 능력 {gr.abilities.Count}개의 이름이 새겨져 있다.\n(석상 하나마다 다음 삶들의 공격력이 영구히 +3%)",
                options = { Bye() },
            });
        }

        static void Shrine(Game g, Interactable it)
        {
            var s = it.data as StructureSpec;
            string key = "shrine:" + s.id;
            if (g.World.Flag(key)) { Show(new DialoguePage { speaker = "고요한 제단", portrait = Art.Prop("shrine"), text = "빛이 사그라들었다. 다른 삶에서 다시 모일 것 같다.", options = { Bye() } }); return; }
            var page = new DialoguePage { speaker = "기억의 제단", portrait = Art.Prop("shrine"), text = "제단 위에 희미한 빛이 일렁인다. 손을 대자 지난 움직임들이 꿈틀거린다..." };
            page.options.Add(new DialogueOption("빛을 받아들이기 (즉시 진화)", () =>
            {
                g.World.SetFlag(key); it.sr.color = new Color(0.7f, 0.7f, 0.75f);
                g.Player.PendingEvolutions++;
                GameEvents.World("investigate", "shrine");
                Close();
                g.BeginEvolution();
            }));
            page.options.Add(new DialogueOption("조각으로 바꾸기 (기억의 조각 +2)", () =>
            {
                g.World.SetFlag(key); it.sr.color = new Color(0.7f, 0.7f, 0.75f);
                g.World.memoryShards += 2;
                Game.I.Ui.Toast("기억의 조각 +2");
                GameEvents.World("investigate", "shrine");
                Close();
            }));
            page.options.Add(Bye());
            Show(page);
        }

        static void RuinAltar(Game g, Interactable it)
        {
            var s = it.data as StructureSpec;
            if (!g.World.clearedCamps.Contains(s.id) && Actor.Nearest(it.Pos, 9, Team.Enemy) != null)
            {
                Show(new DialoguePage { speaker = "고대 제단", portrait = Art.Prop("altar"), text = "몬스터들이 제단을 지키고 있다. 먼저 처리하자.", options = { Bye() } });
                return;
            }
            g.World.SetFlag("ruin:" + s.id);
            g.World.memoryShards += 1;
            GameEvents.World("investigate", "ruins");
            var lore = new[]
            {
                "\"탑은 잊지 않기 위해 세워졌다. 그러나 아무것도 잊지 못하는 자는 탑을 오를 수도 없다.\"",
                "\"토끼들은 몇 번이고 다시 태어난다. 그들이 한 일은 세계만이 기억한다.\"",
                "\"꼭대기의 기사도 한때는 영웅이었다. 모든 것을 기억하기를 택한 자였다.\"",
            };
            g.World.Log($"{Ko.I(g.HeroName)} {s.name}에서 비문을 해독했다.", "lore");
            Show(new DialoguePage { speaker = s.name, portrait = Art.Prop("altar"), text = lore[Mathf.Abs(s.id.GetHashCode()) % lore.Length] + "\n\n(기억의 조각 +1)", options = { Bye() } });
        }

        static void Cage(Game g, Interactable it)
        {
            var s = it.data as StructureSpec;
            if (Actor.Nearest(it.Pos, 7, Team.Enemy) != null)
            {
                Show(new DialoguePage { speaker = "붙잡힌 여행자", portrait = Art.Npc("villager9", 0), text = "쉿! 아직 경비가 근처에 있어요... 제발 도와주세요!", options = { Bye() } });
                return;
            }
            g.World.SetFlag("rescued:" + s.id);
            GameEvents.World("rescue", s.id);
            g.World.Log($"{Ko.I(g.HeroName)} {s.name}에 붙잡힌 여행자를 구출했다.", "deed");
            g.Player.GainGold(30);
            g.Player.Heal(30);
            Object.Destroy(it.gameObject);
            Show(new DialoguePage { speaker = "구출된 여행자", portrait = Art.Npc("villager9", 0), text = "고마워요, 정말 고마워요! 마을에 당신 이야기를 전할게요. 자, 별건 아니지만...\n(+30 골드)", options = { Bye() } });
        }

        static void Herb(Game g, Interactable it)
        {
            var s = it.data as StructureSpec;
            g.World.SetFlag("grove:" + s.id);
            g.Player.Heal(g.Player.maxHp * 0.5f);
            g.Player.AddBuff(StatId.MoveSpeed, 0.2f, 30);
            GameEvents.World("explore", "herb");
            Object.Destroy(it.gameObject);
            Game.I.Ui.Toast("빛나는 약초를 먹었다. 몸이 가벼워진다. (회복 + 30초간 이동 속도 증가)");
        }

        static void Wanderer(Game g, Interactable it)
        {
            var s = it.data as StructureSpec;
            var target = Diverse.World.I.Gen.FindNearest(it.Pos, StructKind.Shrine, 10, 120, x => !g.World.Flag("shrine:" + x.id))
                         ?? Diverse.World.I.Gen.FindNearest(it.Pos, StructKind.Chest, 10, 120, x => !g.World.Flag("chest:" + x.id));
            string tip = target != null ? $"{Dir(target.center - it.Pos)}쪽으로 가면 {Ko.I(target.name)} 있어요. 지도에 표시해 둘게요." : "너무 오래 헤매서 이젠 아무것도 모르겠어요.";
            Show(new DialoguePage
            {
                speaker = "길 잃은 방랑자", portrait = it.sr.sprite,
                text = "헤매는 것도 지쳤어요... 그래도 많은 걸 봤죠.\n" + tip,
                options =
                {
                    new DialogueOption("고마워요 (길 안내 받기)", () =>
                    {
                        if (target != null) Diverse.World.I.Discover(target.id);
                        g.World.SetFlag("wanderer:" + s.id);
                        GameEvents.World("rescue", "wanderer");
                        Close();
                    }),
                },
            });
        }

        static string Dir(Vector2 d)
        {
            float a = d.Angle();
            if (a > -22.5f && a <= 22.5f) return "동";
            if (a > 22.5f && a <= 67.5f) return "북동";
            if (a > 67.5f && a <= 112.5f) return "북";
            if (a > 112.5f && a <= 157.5f) return "북서";
            if (a > -67.5f && a <= -22.5f) return "남동";
            if (a > -112.5f && a <= -67.5f) return "남";
            if (a > -157.5f && a <= -112.5f) return "남서";
            return "서";
        }

        static void Tower(Game g, Interactable it)
        {
            var w = g.World;
            int cost = TowerCost(w.towerFloor);
            var page = new DialoguePage
            {
                speaker = "기억의 탑", portrait = Art.Tower(),
                text = w.towerFloor == 0
                    ? $"거대한 문이 닫혀 있다. 머릿속에 목소리가 울린다.\n\"쌓아 온 것을 증명하라. 기억의 조각 {cost}개.\""
                    : $"{w.towerFloor}층이 열렸다. 다음 층의 문에는 기억의 조각 {cost}개가 필요하다.\n(층을 열 때마다 세계의 몬스터가 강해지고, 모든 삶의 최대 체력이 +10)",
            };
            page.options.Add(new DialogueOption($"조각 바치기 ({w.memoryShards}/{cost})", () =>
            {
                w.memoryShards -= cost;
                w.towerFloor++;
                w.Log($"{Ko.I(g.HeroName)} 기억의 탑 {w.towerFloor}층을 열었다.", "tower");
                Sfx.Play("boss_roar");
                CameraRig.I.Shake(0.6f);
                g.Player.RecalcStats();
                Close();
                if (w.towerFloor >= 3)
                {
                    // Top floor: the Knight of Oblivion
                    var e = Enemy.Spawn(DB.ScaledEnemy("boss_knight", w.towerFloor), it.Pos + Vector2.down * 4f, "tower");
                    Game.I.Ui.Toast("탑 꼭대기에서 무언가가 내려온다...");
                }
                else Game.I.Ui.Toast($"탑 {w.towerFloor}층이 열렸다. 세계가 변하고 있다.");
            }, w.memoryShards >= cost));
            page.options.Add(Bye());
            Show(page);
        }
    }
}
