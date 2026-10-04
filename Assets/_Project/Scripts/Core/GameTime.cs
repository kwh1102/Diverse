using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 게임 시간 제어. 메뉴 일시정지·히트스톱·슬로모션을 한 곳에서 관리해 서로 충돌하지 않게 한다.
    /// 게임 로직은 Time.deltaTime, UI 연출은 Time.unscaledDeltaTime을 쓴다.
    /// </summary>
    public static class GameTime
    {
        static readonly HashSet<string> pauseReasons = new HashSet<string>();
        static float hitstopUntil;     // unscaled time
        static float hitstopScale = 0.02f;
        static float slowUntil;
        static float slowScale = 1f;

        public static bool Paused => pauseReasons.Count > 0;
        public static bool InHitstop => Time.unscaledTime < hitstopUntil;

        public static void Pause(string reason) => pauseReasons.Add(reason);
        public static void Resume(string reason) => pauseReasons.Remove(reason);
        public static void ClearPauses() => pauseReasons.Clear();

        /// <summary>타격 순간 화면을 아주 짧게 멈춘다. 더 긴 히트스톱이 이미 걸려 있으면 유지.</summary>
        public static void Hitstop(float seconds, float scale = 0.02f)
        {
            if (seconds <= 0) return;
            float until = Time.unscaledTime + seconds;
            if (until > hitstopUntil) { hitstopUntil = until; hitstopScale = scale; }
        }

        public static void SlowMo(float seconds, float scale)
        {
            float until = Time.unscaledTime + seconds;
            if (until > slowUntil || scale < slowScale) { slowUntil = until; slowScale = scale; }
        }

        public static void Tick()
        {
            float s = 1f;
            if (Time.unscaledTime < slowUntil) s = Mathf.Min(s, slowScale);
            if (Time.unscaledTime < hitstopUntil) s = Mathf.Min(s, hitstopScale);
            if (Paused) s = 0f;
            Time.timeScale = s;
        }

        public static void Reset()
        {
            pauseReasons.Clear();
            hitstopUntil = slowUntil = 0;
            Time.timeScale = 1;
        }
    }
}
