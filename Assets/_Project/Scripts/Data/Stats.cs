using System;
using UnityEngine;

namespace Diverse
{
    public enum StatId
    {
        MaxHp, Attack, AttackSpeed, MoveSpeed, CritChance, CritDamage,
        CooldownReduction, Armor, XpGain, GoldGain, Regen, DashCooldown, ElementPower, Count
    }

    /// <summary>
    /// 최종값 = (기본값 + 합연산) × (1 + 곱연산 합).
    /// 장비·코스튬·능력·버프가 같은 스탯에 겹쳐도 계산이 예측 가능하도록 한 곳에서 처리한다.
    /// </summary>
    [Serializable]
    public class Stats
    {
        readonly float[] baseV = new float[(int)StatId.Count];
        readonly float[] add = new float[(int)StatId.Count];
        readonly float[] mul = new float[(int)StatId.Count];

        public float this[StatId id] => Get(id);

        public float Get(StatId id)
        {
            int i = (int)id;
            float v = (baseV[i] + add[i]) * Mathf.Max(0.1f, 1f + mul[i]);
            switch (id)
            {
                case StatId.CritChance: return Mathf.Clamp(v, 0, 1);
                case StatId.CooldownReduction: return Mathf.Clamp(v, 0, 0.6f);
                case StatId.Armor: return Mathf.Clamp(v, 0, 0.75f);
                default: return v;
            }
        }

        public void SetBase(StatId id, float v) => baseV[(int)id] = v;
        public float GetBase(StatId id) => baseV[(int)id];
        public void Add(StatId id, float v) => add[(int)id] += v;
        public void Mul(StatId id, float v) => mul[(int)id] += v;

        public void ClearModifiers()
        {
            Array.Clear(add, 0, add.Length);
            Array.Clear(mul, 0, mul.Length);
        }

        public static string Label(StatId id) => id switch
        {
            StatId.MaxHp => "최대 체력",
            StatId.Attack => "공격력",
            StatId.AttackSpeed => "공격 속도",
            StatId.MoveSpeed => "이동 속도",
            StatId.CritChance => "치명타 확률",
            StatId.CritDamage => "치명타 피해",
            StatId.CooldownReduction => "재사용 감소",
            StatId.Armor => "피해 감소",
            StatId.XpGain => "경험치 획득",
            StatId.GoldGain => "골드 획득",
            StatId.Regen => "초당 회복",
            StatId.DashCooldown => "대시 재사용",
            StatId.ElementPower => "원소 위력",
            _ => id.ToString(),
        };

        public static bool IsPercent(StatId id) => id is StatId.CritChance or StatId.CritDamage or StatId.CooldownReduction
            or StatId.Armor or StatId.XpGain or StatId.GoldGain or StatId.AttackSpeed or StatId.ElementPower;
    }

    /// <summary>레벨업 때 투자하는 4대 능력치. AI 컨텍스트의 "투자 성향"으로도 쓰인다.</summary>
    public enum Attr { STR, DEX, INT, VIT }

    public static class AttrInfo
    {
        public static string Name(Attr a) => a switch
        {
            Attr.STR => "힘", Attr.DEX => "민첩", Attr.INT => "지능", _ => "활력",
        };

        public static string Desc(Attr a) => a switch
        {
            Attr.STR => "공격력 +6%, 넉백 강화",
            Attr.DEX => "공격 속도 +5%, 치명타 +2%",
            Attr.INT => "원소 위력 +10%, 재사용 -3%",
            _ => "최대 체력 +12, 회복 +0.15/s",
        };

        public static void Apply(Stats s, Attr a, int points)
        {
            if (points <= 0) return;
            switch (a)
            {
                case Attr.STR: s.Mul(StatId.Attack, 0.06f * points); break;
                case Attr.DEX: s.Mul(StatId.AttackSpeed, 0.05f * points); s.Add(StatId.CritChance, 0.02f * points); break;
                case Attr.INT: s.Add(StatId.ElementPower, 0.10f * points); s.Add(StatId.CooldownReduction, 0.03f * points); break;
                case Attr.VIT: s.Add(StatId.MaxHp, 12f * points); s.Add(StatId.Regen, 0.15f * points); break;
            }
        }
    }
}
