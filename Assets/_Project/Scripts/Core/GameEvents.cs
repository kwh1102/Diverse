using System;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 전투·탐험에서 일어나는 "사실"을 알리는 이벤트 허브.
    /// 텔레메트리(행동 기록)와 능력 런타임이 이 이벤트를 구독한다 → 전투 코드는 능력 시스템을 몰라도 된다.
    /// </summary>
    public static class GameEvents
    {
        public static event Action<CombatEvent> Combat;
        public static event Action<string, string> WorldAction;   // (kind, detail) 예: ("explore", "ruins")
        public static event Action<string> Toast;                  // 화면 상단 알림

        public static void Raise(CombatEvent e) => Combat?.Invoke(e);
        public static void World(string kind, string detail = "") => WorldAction?.Invoke(kind, detail);
        public static void Notify(string msg) => Toast?.Invoke(msg);

        public static void ClearAll()
        {
            Combat = null; WorldAction = null; Toast = null;
        }
    }

    /// <summary>전투 이벤트 종류. 능력 Rule IR의 Trigger 원자 중 전투에서 오는 것들 (RuleLanguage 참고).</summary>
    public enum Trig
    {
        None,
        Attack,         // 기본 공격 시작
        Hit,            // 피해를 입힘
        Crit,           // 치명타
        Kill,           // 처치
        Dash,           // 대시 시작
        DashEnd,        // 대시 종료
        PerfectDodge,   // 적 공격 직전 회피
        Damaged,        // 피해를 받음
        SkillCast,      // 스킬 사용
        ComboFinish,    // 콤보 마지막 타
        LowHealth,      // 체력 30% 이하 진입
        Interval,       // 주기
        CloneSpawn,
        CloneExpire,
        StatusApplied,
        Guard,          // 가드/패링 성공
        StatusExpired,  // 내가 건 상태이상이 살아 있는 적에게서 끝남 (tags = status id)
        ShieldBroken,   // 보호막이 피해로 깨짐
        Healed,         // 회복을 받음 (amount = 회복량)
    }

    public struct CombatEvent
    {
        public Trig type;
        public Actor source;
        public Actor target;
        public Vector2 position;
        public Vector2 direction;
        public float amount;
        public string tags;       // 공백 구분 태그 ("SWORD MELEE")
        public int depth;         // 능력이 능력을 연쇄 발동할 때 무한 루프 방지

        public static CombatEvent Of(Trig t, Actor src, Vector2 pos) =>
            new CombatEvent { type = t, source = src, position = pos, tags = "" };
    }
}
