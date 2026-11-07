using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Diverse
{
    public static class ActiveSkillSynergy
    {
        public static readonly string[] Events = { "impact", "placement", "arrival" };
        static readonly string[] Actions = { "burst", "draw", "echo", "tempo", "relay" };

        struct Signal
        {
            public Vector2 point;
            public float expires;
            public string weaponId, skillId;
        }
        static ConditionalWeakTable<Player, Dictionary<(int, string), Signal>> signals = new();
        static ActiveRules Rules => ActiveSkillGenerator.Rules;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => signals = new();

        public static bool Provides(ActiveSkillData source, string kind) => source?.HasTechnique == true ? ActiveTechniques.HasSignal(source.technique, kind) : source?.steps != null && source.steps.Any(s => kind switch
        {
            "placement" => s.delivery is "trap" or "wall" || s.delivery == "field" && s.effect is not ("heal" or "shield"),
            "arrival" => ActiveSkillGenerator.Moving(s) || s.delivery == "projectile",
            "impact" => s.effect is not ("heal" or "shield" or "block"),
            _ => false
        });

        public static bool ValidShape(ActiveSkillData skill) =>
            string.IsNullOrEmpty(skill.linkEvent) && string.IsNullOrEmpty(skill.linkAction) ||
            skill.linkSlot >= 0 && skill.linkSlot < skill.slot &&
            Events.Contains(skill.linkEvent) && Actions.Contains(skill.linkAction) &&
            (skill.HasTechnique ? skill.linkAction == "tempo" || skill.linkAction == "relay" && ActiveTechniques.AcceptsLinkedPoint(skill.technique)
                : skill.linkAction is "burst" or "draw" or "echo");

        internal static void RestoreLegacyBundles(IEnumerable<ActiveSkillData> skills)
        {
            var legacy = skills.Where(s => s?.HasLegacyBundle == true).ToArray();
            foreach (var skill in legacy)
            {
                if (skill.steps?.Any(s => s?.condition == "marked") == true)
                {
                    var producer = legacy.FirstOrDefault(s => !string.IsNullOrEmpty(skill.LegacyGroup) &&
                        s.LegacyGroup == skill.LegacyGroup && s.weaponType == skill.weaponType &&
                        s.slot >= 0 && s.slot < skill.slot &&
                        s.steps?.Any(step => step?.effect == "mark" && step.condition == "always") == true);
                    if (producer != null && !skill.HasSynergy)
                    {
                        skill.linkSlot = producer.slot;
                        skill.linkEvent = "impact";
                        skill.linkAction = "burst";
                    }
                    if (!skill.steps.Any(ActiveSkillGenerator.StandaloneAction))
                    {
                        var first = skill.steps.First(s => s?.condition == "marked");
                        first.condition = "always";
                        first.consume = false;
                    }
                }
            }
            foreach (var skill in legacy) skill.ClearLegacyBundle();
        }

        public static InventoryItem WeaponFor(Player player, WeaponType type) =>
            player.Inventory.Active is { } active && ActiveSkillGenerator.TypeOf(active) == type
                ? active
                : player.Inventory.items.FirstOrDefault(i => i.kind == ItemKind.Weapon && i.Placed && ActiveSkillGenerator.TypeOf(i) == type)
                  ?? player.Inventory.items.FirstOrDefault(i => i.kind == ItemKind.Weapon && ActiveSkillGenerator.TypeOf(i) == type);

        public static IEnumerable<ActiveSkillData> Earlier(Player player, WeaponType type, int slot) =>
            player.Inventory.Attached(WeaponFor(player, type)).Where(i => i.kind == ItemKind.Skill && i.active != null &&
                i.active.weaponType == type && i.active.slot >= 0 && i.active.slot < slot && player.SkillUnlocked(i.active.slot)).Select(i => i.active);

        public static bool Available(Player player, ActiveSkillData skill) => skill.HasSynergy &&
            Earlier(player, skill.weaponType, skill.slot).Any(s => s.slot == skill.linkSlot && Provides(s, skill.linkEvent));

        public static void Record(Player player, ActiveSkillData source, string weaponId, string kind, Vector2 point)
        {
            signals.GetOrCreateValue(player)[(source.slot, kind)] = new Signal {
                weaponId = weaponId, skillId = source.id, point = point, expires = Time.time + Rules.linkWindow };
        }

        public static bool Ready(Player player, ActiveSkillData skill) => Find(player, skill, out _, out _);

        static bool Find(Player player, ActiveSkillData skill, out Dictionary<(int, string), Signal> owned, out Vector2 point)
        {
            point = default;
            owned = null;
            if (skill?.HasSynergy != true || !signals.TryGetValue(player, out owned)) return false;
            var key = (skill.linkSlot, skill.linkEvent);
            if (skill.linkSlot < 0 || skill.linkSlot >= skill.slot ||
                !owned.TryGetValue(key, out var signal) || Time.time >= signal.expires ||
                signal.weaponId != player.Inventory.Active?.id ||
                player.Skills[skill.linkSlot]?.def?.active?.id != signal.skillId ||
                (signal.point - player.Pos).sqrMagnitude > Rules.linkReach * Rules.linkReach) return false;
            point = signal.point;
            return true;
        }

        public static bool TryConsume(Player player, ActiveSkillData skill, out Vector2 point)
        {
            if (!Find(player, skill, out var owned, out point)) return false;
            owned.Remove((skill.linkSlot, skill.linkEvent));
            return true;
        }

        public static bool TryRelay(Player player, ActiveSkillData skill, float range, Vector2 aim, out Vector2 point)
        {
            if (!Find(player, skill, out var owned, out point) || (point - player.Pos).sqrMagnitude > range * range ||
                (point - aim).sqrMagnitude > Rules.radius * Rules.radius || !WorldStreamer.I.Walkable(point)) return false;
            owned.Remove((skill.linkSlot, skill.linkEvent));
            return true;
        }

        public static string Describe(ActiveSkillData skill)
        {
            if (!skill.HasSynergy) return "";
            string cause = skill.linkEvent switch { "placement" => "설치 지점", "arrival" => "도착 지점", _ => "명중 지점" };
            if (skill.linkAction == "relay") return $"{ActiveSkillGenerator.SlotName(skill.linkSlot)} 연계 · {cause} 근처를 조준하면 그 자리에 설치";
            if (skill.linkAction == "tempo") return $"{ActiveSkillGenerator.SlotName(skill.linkSlot)} 연계 · {cause} 신호로 사거리·작동 시간 +{(Rules.techniqueLinkScale - 1) * 100:0}%";
            string effect = skill.linkAction switch { "draw" => "끌어모음", "echo" => "연결선 타격", _ => "추가 파열" };
            return $"{ActiveSkillGenerator.SlotName(skill.linkSlot)} 연계 · {cause}에서 {effect}";
        }
    }
}
