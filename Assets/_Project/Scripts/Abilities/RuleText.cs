using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Diverse
{
    /// <summary>Human-readable Korean text for Rule IR (cards, pause screen, chronicle, LLM naming input).</summary>
    public static class RuleText
    {
        public static string Explain(AbilityDef a)
        {
            if (a?.rules == null) return "";
            return string.Join("\n", a.rules.Select(r => Rule(a, r)));
        }

        public static string Rule(AbilityDef a, RuleDef r)
        {
            var sb = new StringBuilder();
            var conds = r.when.Select(c => Cond(a, c)).Where(s => s.Length > 0).ToList();
            switch (r.kind)
            {
                case RuleKind.Trigger:
                    sb.Append(Trigger(a, r));
                    foreach (var c in conds) sb.Append(" · ").Append(c);
                    if (r.cooldown >= 0.5f) sb.Append($" ({r.cooldown:0.#}초마다 최대 1회)");
                    sb.Append(" → ");
                    break;
                case RuleKind.Continuous:
                    if (conds.Count > 0) sb.Append(string.Join(" · ", conds)).Append("일 때: ");
                    break;
                case RuleKind.Replace:
                    sb.Append(Action(r.replaces)).Append(" 대신 ");
                    break;
                case RuleKind.Meta:
                    sb.Append(Selection(r, r.ops.FirstOrDefault())).Append(": ");
                    break;
            }
            sb.Append(string.Join(" + ", r.ops.Select(o => r.kind == RuleKind.Meta ? MetaOp(o) : r.kind == RuleKind.Replace ? ReplaceOp(o) : Op(a, o))));
            return sb.ToString();
        }

        static string Action(string a) => a switch
        {
            "Dash" => "대시", "Potion" => "물약", "ComboFinisher" => "콤보 마무리에 더해", "Regen" => "자연 회복", "Skill" => "스킬", _ => a,
        };

        static string Selection(RuleDef r, OpDef o)
        {
            string arg = !string.IsNullOrEmpty(r.selectArg) ? r.selectArg : o?.tag;
            return (string.IsNullOrEmpty(r.select) ? "ByTag" : r.select) switch
            {
                "ByElement" => $"{Element(arg)} 능력들",
                "ByTrigger" => $"'{RuleLanguage.Get(arg)?.ko ?? arg}' 능력들",
                "Strongest" => "가장 강한 능력",
                "Newest" => "가장 최근 능력",
                "All" => "다른 모든 능력",
                _ => $"[{arg}] 능력들",
            };
        }

        public static string Trigger(AbilityDef a, RuleDef r)
        {
            var p = RuleLanguage.Get(r.trigger);
            switch (r.trigger)
            {
                case "Every": return $"{r.period:0.#}초마다";
                case "StateFull": return $"[{StateName(a, r.param)}]{(Hangul.HasBatchim(StateName(a, r.param)) ? "이" : "가")} 가득 차면";
                case "StateEmpty": return $"[{StateName(a, r.param)}]{(Hangul.HasBatchim(StateName(a, r.param)) ? "이" : "가")} 바닥나면";
                case "StatusApplied": return string.IsNullOrEmpty(r.param) ? p.ko : $"{Status(r.param)}에 걸게 할 때";
                case "StatusExpired": return string.IsNullOrEmpty(r.param) ? p.ko : $"{Status(r.param)}{(Hangul.HasBatchim(Status(r.param)) ? "이" : "가")} 끝날 때";
                case "EntityExpired": return string.IsNullOrEmpty(r.param) ? p.ko : $"{Entity(r.param)}{(Hangul.HasBatchim(Entity(r.param)) ? "이" : "가")} 사라질 때";
                case "Pickup": return r.param switch { "gold" => "골드를 주울 때", "heart" => "하트를 주울 때", "xp" => "경험치를 주울 때", _ => p.ko };
            }
            return p?.ko ?? r.trigger;
        }

        public static string Cond(AbilityDef a, CondDef c)
        {
            switch (c.type)
            {
                case "Chance": return $"{c.value * 100:0}% 확률";
                case "EveryNth": return $"{c.value:0}번째마다";
                case "TargetHas": return $"대상이 {Status(c.text)} 상태";
                case "TargetLacks": return $"대상이 {Status(c.text)} 상태가 아님";
                case "TargetIs": return c.text switch { "Elite" => "정예 상대", "Boss" => "보스 상대", "Normal" => "일반 적 상대", "Ranged" => "원거리 적 상대", _ => "근접 적 상대" };
                case "EventTag": return $"[{c.text}]";
                case "HasEntity": return string.IsNullOrEmpty(c.text) ? "소환물이 있을 때" : $"{Entity(c.text)}{(Hangul.HasBatchim(Entity(c.text)) ? "이" : "가")} 있을 때";
                case "NoEntity": return string.IsNullOrEmpty(c.text) ? "소환물이 없을 때" : $"{Entity(c.text)}{(Hangul.HasBatchim(Entity(c.text)) ? "이" : "가")} 없을 때";
                case "WithinTime": return $"{RuleLanguage.Get(c.text)?.ko?.Replace(" 때", "") ?? c.text} {c.value:0.#}초 안";
                case "SameTarget": return "같은 적";
                case "DifferentTarget": return "다른 적";
                case "DominantElement": return $"주력 원소가 {Element(c.text)}";
                case "Compare": return $"{Value(a, c.a)} {Cmp(c.cmp)} {Num(c.a, c.value)}";
            }
            return c.type;
        }

        static string Cmp(string c) => c switch { ">=" => "≥", "<=" => "≤", "==" => "=", _ => c };

        static string Num(string reference, float v) =>
            reference != null && reference.EndsWith("Pct") ? $"{v * 100:0}%" : $"{v:0.##}";

        public static string Value(AbilityDef a, string reference)
        {
            if (string.IsNullOrEmpty(reference)) return "";
            if (reference.StartsWith("state:"))
            {
                string st = reference.Substring(6);
                // a state filled with event.amount on Damaged/Healed holds a fraction of max HP
                bool hp = a != null && a.rules.Any(r => r.trigger is "Damaged" or "Healed" && r.ops.Any(o => o.op == "AddState" && o.state == st && o.scale == "event.amount"));
                return hp ? $"[{StateName(a, st)}](최대 체력 대비)" : $"[{StateName(a, st)}]";
            }
            if (reference.StartsWith("build.tag:")) return $"보유한 [{reference.Substring(10)}] 능력 수";
            if (reference.StartsWith("build.element:")) return $"보유한 {Element(reference.Substring(14))} 능력 수";
            if (reference.StartsWith("build.tier:")) return $"{TierLabel(int.TryParse(reference.Substring(11), out var t) ? t : 0)} 능력 수";
            return RuleLanguage.Value(reference)?.ko ?? reference;
        }

        public static string StateName(AbilityDef a, string name)
        {
            var s = a?.State(name);
            return !string.IsNullOrEmpty(s?.label) ? s.label : name;
        }

        public static string Status(string s) => s switch
        {
            "burn" => "화상", "chill" => "냉기", "poison" => "중독", "mark" => "표식", "bleed" => "출혈", "weaken" => "약화", null or "" => "상태이상", _ => s,
        };

        public static string Element(string e) => e switch
        {
            "Fire" => "화염", "Frost" => "냉기", "Lightning" => "번개", "Shadow" => "그림자", "Holy" => "신성", "Poison" => "독", "Wind" => "바람",
            "Blood" => "피", "Void" => "공허", "Arcane" => "비전", _ => e,
        };

        public static string Entity(string e) => e switch
        {
            "Clone" => "잔상 분신", "Turret" => "포탑", "Orb" => "빛의 구슬", "Zone" => "장판", "PhantomWeapon" => "환영 무기",
            "Mine" => "지뢰", "Totem" => "토템", "Barrier" => "방벽", "Decoy" => "미끼", _ => e,
        };

        static string Target(string t) => t switch
        {
            null or "" or "EventTarget" => "", "Self" => "자신에게 ", _ => (RuleLanguage.Selector(t)?.ko ?? t) + "에게 ",
        };

        static string At(string at) => at switch
        {
            null or "" or "EventPos" => "", "Self" => "자신 주변에 ", "Ring" => "자신 주위로 ", _ => (RuleLanguage.Position(at)?.ko ?? at) + "에 ",
        };

        static string Pct(float v) => $"{v * 100:0}%";

        /// <summary>"60%" or "20% + 5% × [잃은 체력 비율]".</summary>
        static string Mag(AbilityDef a, OpDef o, System.Func<float, string> f)
        {
            string s = f(o.amount);
            if (!string.IsNullOrEmpty(o.scale) && o.per != 0)
                s = (o.amount != 0 ? s + " + " : "") + $"{f(o.per)} × {Value(a, o.scale)}";
            return s;
        }

        static string Eul(string w) => w + (Hangul.HasBatchim(w) ? "을" : "를");

        public static string Op(AbilityDef a, OpDef o)
        {
            string el = RuleLanguage.IsElement(o.element) ? Element(o.element) + " " : "";
            string body = o.op switch
            {
                "Damage" => $"{Target(o.target)}{el}피해 (공격력의 {Mag(a, o, Pct)})",
                "Drain" => $"{Target(o.target)}피해 (공격력의 {Mag(a, o, Pct)})를 주고 그 40%만큼 회복",
                "Nova" => o.at == "DashPath" ? $"대시 경로를 따라 {el}피해 (공격력의 {Mag(a, o, Pct)})" : $"{At(o.at)}{el}충격파 (공격력의 {Mag(a, o, Pct)}) (반경 {o.radius:0.#}m)",
                "Slash" => $"전방으로 {el}참격 (공격력의 {Mag(a, o, Pct)})",
                "Beam" => $"조준 방향으로 {el}광선 (공격력의 {Mag(a, o, Pct)}) (길이 {o.radius:0.#}m)",
                "Projectile" => $"{At(o.at)}{el}투사체 {o.count}발 (각 공격력의 {Mag(a, o, Pct)}){(o.mode == "Ring" ? " 사방으로" : o.mode == "Seek" ? " 유도" : "")}",
                "Chain" => $"{Target(o.target)}연쇄 번개 {o.count}회 (공격력의 {Mag(a, o, Pct)})",
                "ApplyStatus" => $"{Target(o.target)}{Status(o.status)} 부여 ({o.duration:0.#}초)",
                "Slow" => $"{Target(o.target)}강한 둔화 {o.amount:0.#}초",
                "Detonate" => $"{Target(o.target)}{Eul(Status(o.status))} 터뜨려 피해 (공격력의 {Mag(a, o, Pct)}) (남은 1초당 +20%)",
                "Spread" => $"{Eul(Status(o.status))} 주변 적 {o.count}명에게 전염",
                "Transfer" => $"내가 건 상태이상을 주변 적 {o.count}명에게 옮김",
                "Execute" => $"{Target(o.target)}체력 {Pct(o.amount)} 이하면 처형",
                "Stun" => $"{Target(o.target)}기절 {o.amount:0.#}초",
                "Taunt" => $"{At(o.at)}적을 {o.amount:0.#}초 도발",
                "Push" => $"{At(o.at)}적 밀쳐내기 (반경 {o.radius:0.#}m)",
                "Pull" => $"{At(o.at)}적 끌어당기기 (반경 {o.radius:0.#}m)",
                "Cleanse" => $"상태이상 해제 + {o.amount:0.#}초 무적",
                "Heal" => $"내 최대 체력의 {Mag(a, o, v => $"{v * 100:0.#}%")} 회복",
                "Shield" => $"내 최대 체력에 비례한 보호막 {Mag(a, o, Pct)} ({o.duration:0.#}초)",
                "Buff" => $"{Stats.Label(ParseStat(o.stat))} +{Mag(a, o, Pct)} ({o.duration:0.#}초)",
                "Blink" => string.IsNullOrEmpty(o.target) ? $"{At(o.at)}순간이동" : $"{Target(o.target).Replace("에게 ", "")} 곁으로 순간이동",
                "Leap" => $"{At(o.at).Replace("에 ", "")}{(string.IsNullOrEmpty(o.at) ? "그곳" : "")}으로 도약",
                "Swap" => $"{Target(o.target).Replace("에게 ", "")}{(Hangul.HasBatchim(RuleLanguage.Selector(o.target)?.ko ?? "") ? "과" : "와")} 위치 교환",
                "MoveEntity" => $"{RuleLanguage.Selector(o.target)?.ko ?? o.target}{(Hangul.HasBatchim(RuleLanguage.Selector(o.target)?.ko ?? "") ? "을" : "를")} {At(o.at).Replace("에 ", "")}(으)로 이동",
                "ResetCooldowns" => $"재사용 대기 -{Pct(o.amount)}",
                "GainDash" => $"대시 충전 +{o.count}",
                "Spawn" => $"{At(o.at)}{el}{Entity(o.entity)} {(o.count > 1 ? o.count + "개 " : "")}소환 ({o.duration:0.#}초, 공격 위력 {Mag(a, o, Pct)}{(o.mode == "Copy" || o.rel == "Copy" ? ", 내 공격 복제" : "")})",
                "DetonateEntities" => $"{RuleLanguage.Selector(o.target)?.ko ?? o.target} 폭파 ({Mag(a, o, Pct)})",
                "ExtendEntities" => $"{RuleLanguage.Selector(o.target)?.ko ?? o.target} 수명 +{o.amount:0.#}초",
                "EmpowerNext" => $"다음 공격{(o.count > 1 ? " " + o.count + "회" : "")} {el}피해 +{Mag(a, o, Pct)}",
                "AddState" => o.scale == "event.amount" && o.amount == 0 ? $"받은 양을 [{StateName(a, o.state)}]에 쌓음" : $"[{StateName(a, o.state)}] {(o.amount < 0 ? "-" : "+")}{Mag(a, o, v => $"{System.Math.Abs(v):0.##}")}",
                "SetState" => a?.State(o.state)?.type == "Timer" ? $"[{StateName(a, o.state)}] {o.amount:0.#}초 시작" : o.amount == 0 ? $"[{StateName(a, o.state)}] 소모" : $"[{StateName(a, o.state)}] = {o.amount:0.##}",
                "StorePosition" => $"{At(o.at).Replace("에 ", "")}{(string.IsNullOrEmpty(o.at) ? "그 위치" : "")}를 [{StateName(a, o.state)}]에 기억",
                "StoreTarget" => $"{Target(o.target).Replace("에게 ", "")}{(string.IsNullOrEmpty(o.target) || o.target == "EventTarget" ? "그 적" : "")}을 [{StateName(a, o.state)}]으로 기억",
                "GainGold" => $"골드 +{o.amount:0}",
                "GainXp" => $"경험치 +{o.amount:0}",
                "GainPotion" => $"물약 +{o.count}",
                "DropPickup" => $"{At(o.at)}{(o.mode == "gold" ? "골드" : o.mode == "xp" ? "경험치" : "하트")} 떨굼",
                "ModifyStat" => StatText(a, o),
                "AmplifyDamage" => $"주는 피해 +{Mag(a, o, Pct)}",
                "Resist" => $"받는 피해 -{Mag(a, o, Pct)}",
                "Aura" => $"주변 {o.radius:0.#}m 적에게 초당 {el}피해 {Pct(o.amount)}{(string.IsNullOrEmpty(o.status) ? "" : $" + {Status(o.status)}")}",
                "StatusPotency" => $"{(string.IsNullOrEmpty(o.status) ? "모든 상태이상" : Status(o.status))}의 위력·지속 +{Pct(o.amount)}",
                "Convert" => RuleLanguage.ConvertPairs.TryGetValue(o.from + ">" + o.to, out var cp) ? $"{cp.ko} ({Pct(o.amount)})" : $"{o.from}→{o.to}",
                "Forbid" => $"{Action(o.mode)}{(Hangul.HasBatchim(Action(o.mode)) ? "을" : "를")} 쓸 수 없다",
                "CapStat" => $"{Stats.Label(ParseStat(o.stat))}{(Hangul.HasBatchim(Stats.Label(ParseStat(o.stat))) ? "은" : "는")} {StatAmount(o.stat, o.amount)}를 넘을 수 없다",
                "OfferCount" => $"진화 선택지 {o.amount:+0;-0}",
                "TierBias" => "더 기묘한 진화가 등장",
                "BudgetBias" => $"앞으로 얻는 능력의 위력 +{Pct(o.amount)}",
                "RerollDiscount" => $"다시 뽑기 비용 -{Pct(o.amount)}",
                "TagBias" => $"[{o.tag}] 진화가 더 자주 등장",
                "ExtraEvolution" => $"지금 즉시 진화 +{System.Math.Max(1, o.count)}",
                "UpgradeRandom" => $"보유 능력 하나가 영구히 ×{1 + o.amount:0.##}",
                "DelayedReward" => $"적 {o.times}마리를 처치하면 골드 {o.amount:0}과 추가 진화",
                _ => o.op,
            };
            return Temporal(o) + body + Relation(o);
        }

        static string Temporal(OpDef o) => o.time switch
        {
            "Delay" or null or "" when o.delay > 0.05f => $"{o.delay:0.#}초 뒤 ",
            "Periodic" => $"{o.duration:0.#}초 동안 {System.Math.Max(2, o.times)}번 ",
            "UntilHit" => "다음 적중 때 ",
            "UntilDamaged" => "다음에 피격되면 ",
            "UntilNextAttack" => "다음 공격 때 ",
            "ForNextN" => $"다음 {System.Math.Max(1, o.times)}번의 적중마다 ",
            _ => "",
        };

        static string Relation(OpDef o)
        {
            int n = o.times > 0 ? o.times : 2;
            return o.rel switch
            {
                "Echo" => " (0.5초 뒤 50% 메아리)",
                "Mirror" => " (반대편에도)",
                "Chain" => $" (주변 적 {n}명에게 연쇄)",
                "Bounce" => $" ({n}번 튕김)",
                "Return" => " (되돌아옴)",
                "Split" => $" (맞히면 {System.Math.Max(2, o.times)}갈래로 분열)",
                "Pierce" => $" ({n}명 관통)",
                "Seek" => o.mode == "Seek" ? "" : " (유도)",
                "Follow" => " (나를 따라다님)",
                "Attach" => " (적에게 달라붙음)",
                "Spread" => $" (주변 {n}명에게 전파)",
                "Cascade" => " (처치하면 그 자리에서 다시)",
                "Alternate" => " (번갈아 반대 속성)",
                "Inherit" => " (내 속성 계승)",
                "Link" => $" (주변 {n}명과 피해 공유)",
                "Orbit" => " (주위를 공전)",
                _ => "",
            } + (o.time == "Stack" ? " (중첩)" : "");
        }

        static string MetaOp(OpDef o) => o.op switch
        {
            "AmplifyTagged" => $"효과 ×{1 + o.amount:0.##}",
            "RepeatTagged" => $"발동하면 {Pct(o.amount)} 위력으로 한 번 더",
            "HasteTagged" => $"재사용 -{Pct(o.amount)}, 확률 +{Pct(o.amount)}",
            "InfuseTagged" => $"{Element(o.element)} 속성이 깃듦",
            "ExtendTagged" => $"지속시간 +{Pct(o.amount)}",
            "EnlargeTagged" => $"범위 +{Pct(o.amount)}",
            "MultiplyTagged" => $"투사체·소환·연쇄 수 +{System.Math.Max(1, o.count)}",
            "RetagAbilities" => $"[{o.tag}] 능력으로도 취급",
            "GateTagged" => $"체력 {Pct(o.amount)} 이상일 때만 발동하지만 위력 ×1.6",
            "UnchainTagged" => "확률·N번째 조건을 무시",
            "RetriggerTagged" => $"'{RuleLanguage.Get(o.from)?.ko ?? o.from}' 대신 '{RuleLanguage.Get(o.to)?.ko ?? o.to}' 발동",
            _ => o.op,
        };

        static string ReplaceOp(OpDef o) => o.mode switch
        {
            "Blink" => "앞으로 순간이동",
            "Nova" => $"{(RuleLanguage.IsElement(o.element) ? Element(o.element) + " " : "")}충격파 {Pct(o.amount)}",
            "Shield" => $"내 최대 체력에 비례한 보호막 {Pct(o.amount)}",
            "Spawn" => $"{Entity(o.entity)} 소환",
            "Projectile" => $"투사체 {System.Math.Max(1, o.count)}발 ({Pct(o.amount)})",
            _ => o.mode,
        };

        static string StatAmount(string stat, float v)
        {
            bool mul = RuleLanguage.StatInfo.TryGetValue(stat ?? "", out var si) && si.mul;
            return mul ? $"기본의 {(1 + v) * 100:0}%" : Stats.IsPercent(ParseStat(stat)) ? $"{v * 100:0}%" : $"{v:0.#}";
        }

        static string StatText(AbilityDef a, OpDef o)
        {
            var id = ParseStat(o.stat);
            bool pct = RuleLanguage.StatInfo.TryGetValue(o.stat ?? "", out var si) && (si.mul || Stats.IsPercent(id));
            System.Func<float, string> f = v => pct ? $"{v * 100:+0;-0}%" : $"{v:+0.#;-0.#}";
            string label = Stats.Label(id);
            switch (o.mode)
            {
                case "Min": return $"{label} 최소 {StatAmount(o.stat, o.amount)}";
                case "Max": return $"{label} 최대 {StatAmount(o.stat, o.amount)}";
                case "Override": return $"{label}{(Hangul.HasBatchim(label) ? "이" : "가")} 항상 {StatAmount(o.stat, o.amount)}";
            }
            if (string.IsNullOrEmpty(o.scale) || o.per == 0) return $"{label} {f(o.amount)}";
            string scaled = $"{f(o.per)} × {Value(a, o.scale)}";
            return o.amount == 0 ? $"{label} {scaled}" : $"{label} {f(o.amount)}, {scaled}";
        }

        public static StatId ParseStat(string s) => System.Enum.TryParse<StatId>(s, out var id) ? id : StatId.Attack;

        public static string TierLabel(int tier) => tier switch
        {
            0 => "기본", 1 => "반응", 2 => "상태", 3 => "메타", _ => "체계",
        };
    }

    static class Hangul
    {
        public static bool HasBatchim(string w) => !string.IsNullOrEmpty(w) && Ko.HasBatchim(w);
    }
}
