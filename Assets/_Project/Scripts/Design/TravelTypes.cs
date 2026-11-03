using System;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    public enum TravelType { Dash, Flight, Burrow, Swim, Blink, Bounce }

    public static class TravelTypes
    {
        public const int Price = 0;
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
            TravelType.Flight => "빠르게 날아서 물 위를 건너요.",
            TravelType.Burrow => "땅속으로 들어가 얇은 벽을 지나가요.",
            TravelType.Swim => "가까운 물에 뛰어들어 빠르게 헤엄쳐요.",
            TravelType.Blink => "조준 방향의 가까운 빈자리로 순간이동해요.",
            TravelType.Bounce => "크게 뛰고 착지 직후 한 번 더 튈 수 있어요.",
            _ => "조준 방향으로 짧게 돌진해요."
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
