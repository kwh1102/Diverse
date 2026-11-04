using System;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public enum TravelType { Dash, Flight, Burrow, Swim, Blink, Bounce }

    public static class TravelTypes
    {
        public const int Price = 0;
        public const float FlightDuration = 0.4f;
        // Match the dash before movement types were introduced. Shared with the UI demonstration.
        public const float AfterimageInterval = 0.03f;
        public const float AfterimageLifetime = 0.22f;
        public const float AfterimageOpacity = 0.7f;

        public static Color AfterimageColor(TravelType type)
        {
            Color color = type == TravelType.Dash ? new Color(0.7f, 0.85f, 1f) : Color(type);
            color.a = AfterimageOpacity;
            return color;
        }
        public static readonly TravelType[] All = (TravelType[])Enum.GetValues(typeof(TravelType));

        public static bool Unlocked(WorldSave world, TravelType type) =>
            type == TravelType.Dash || world?.unlockedTravelTypes?.Contains(type) == true;

        public static string Name(TravelType type) => type switch
        {
            TravelType.Flight => "날개형", TravelType.Burrow => "굴파기형", TravelType.Swim => "물살형",
            TravelType.Blink => "쏙쏙형", TravelType.Bounce => "통통형", _ => "대시형"
        };

        public static string Description(TravelType type) => type switch
        {
            TravelType.Flight => "강·늪·용암 위를 날아요. 다시 누르면 충전 1회를 써 더 날아요.",
            TravelType.Burrow => "스페이스를 누르는 동안 잠복해 벽을 지나가요.",
            TravelType.Swim => "강과 늪에서 스페이스를 누르는 동안 헤엄쳐요. 용암은 건널 수 없어요.",
            TravelType.Blink => "조준 방향의 가까운 빈자리로 순간이동해요.",
            TravelType.Bounce => "크게 뛰고 착지 직후 한 번 더 튈 수 있어요.",
            _ => "조준 방향으로 짧게 돌진해요."
        };

        public static bool UsesGauge(TravelType type) => type is TravelType.Swim or TravelType.Burrow;

        // One shop capacity upgrade adds one internal charge, expressed as seconds for continuous abilities.
        public static float GaugeSecondsPerUnit(TravelType type) => type switch
        {
            TravelType.Swim => 0.88f, TravelType.Burrow => 0.4f, _ => 0
        };

        public static string CapacityUpgradeName(TravelType type) => type switch
        {
            TravelType.Flight => "날개 신발", TravelType.Burrow => "굴파기 신발", TravelType.Swim => "물살 신발",
            TravelType.Blink => "순간이동 신발", TravelType.Bounce => "도약 신발", _ => "대시 신발"
        };

        public static string CapacityUpgradeDescription(TravelType type)
        {
            if (UsesGauge(type))
                return $"최대 {(type == TravelType.Swim ? "수영" : "잠복")} 게이지 +{GaugeSecondsPerUnit(type):0.##}초";
            return type switch
            {
                TravelType.Flight => "비행 횟수 +1", TravelType.Blink => "순간이동 횟수 +1",
                TravelType.Bounce => "도약 횟수 +1", _ => "대시 횟수 +1"
            };
        }

        public static string MotionRow(TravelType type) => type switch
        {
            TravelType.Flight => "taking off with its own wings, fins, or existing cloak opening; gliding with those surfaces raised; gliding with them lowered; landing and folding them back, preserving the exact same character and outfit",
            TravelType.Burrow => "crouching and digging headfirst; mostly underground with only a low mound and small hints of the character visible; a second low mound with shifted dirt; climbing back out of the soil",
            TravelType.Swim => "diving headfirst; horizontal swimming with limbs reaching forward; horizontal swimming with limbs pulling backward and kicking; surfacing",
            TravelType.Blink => "bracing to blink; dissolving into a narrow fading silhouette; reappearing from a narrow silhouette; fully reformed with a settling pose",
            TravelType.Bounce => "deeply crouched before jumping; extending upward off the ground; airborne with feet tucked; squashing on landing",
            _ => "leaning into a sprint; stretched forward in a dash; a second running stride; braking upright"
        };

        public static string EffectSubject(TravelType type) => type switch
        {
            TravelType.Flight => "a sweeping arc of wind and drifting feather-shaped motes",
            TravelType.Burrow => "an earthy ground-level burst of loose dirt and small stones",
            TravelType.Swim => "a low blue water splash with a small foamy wake",
            TravelType.Blink => "a compact violet crescent of displaced air and flickering sparks",
            TravelType.Bounce => "a low oval dust ring and springy upward flecks",
            _ => "a small ground-level dash puff"
        };

        public static Color32 Color(TravelType type) => Pal.Hex(type switch
        {
            TravelType.Flight => "cde5de", TravelType.Burrow => "ba8c60", TravelType.Swim => "78c8df",
            TravelType.Blink => "b398df", TravelType.Bounce => "f1cb94", _ => "e8dcc8"
        });
    }
}
