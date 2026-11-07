using System;
using System.Linq;

namespace Diverse
{
    public enum Technique
    {
        Legacy, BladeReflect, WallPin, Clash, OpenGuard, Tripwire, BindPair, LureArrow, RecallArrow,
        CaptureRound, WallShatter, IgniteCasings, Overwatch, ResonanceAnchor, RewindSeal, TwinGates,
        TideSwitch, ShadowSwap, ThresholdTrap, BreakPursuit, BackLock
    }

    [Serializable]
    public class TechniqueTuning
    {
        public Technique id;
        public float reach, radius, seconds, strength;
    }

    public static class ActiveTechniques
    {
        public sealed class Rule
        {
            public Technique Id { get; }
            public WeaponType Weapon { get; }
            public string Name { get; }
            public string Role { get; }
            public string Input { get; }
            public string Signals { get; }
            public string Read { get; }
            public string Motif { get; }
            public TechniqueTuning Tuning => ActiveSkillGenerator.Rules.techniques.First(t => t.id == Id);

            public Rule(Technique id, WeaponType weapon, string name, string role, string input, string signals, string read, string motif)
            {
                Id = id; Weapon = weapon; Name = name; Role = role; Input = input;
                Signals = signals; Read = read; Motif = motif;
            }
        }

        static Rule R(Technique id, WeaponType w, string name, string role, string input, string signals, string read, string motif) =>
            new(id, w, name, role, input, signals, read, motif);

        public static readonly Rule[] All =
        {
            R(Technique.BladeReflect, WeaponType.Sword, "칼등 반사", "guard", "tap", "impact",
                "칼 앞에 짧게 반사 자세를 잡아, 닿은 적 탄환 한 발을 조준 방향으로 되돌립니다.", "opposed facets and a clean rebound chevron"),
            R(Technique.WallPin, WeaponType.Sword, "벽쐐기", "control", "tap", "impact",
                "앞의 적 하나를 밉니다. 단단한 벽까지 밀면 적을 고정하고 충격 피해를 줍니다.", "a broad wedge biting into a jagged wall seam"),
            R(Technique.Clash, WeaponType.Sword, "맞부딪치기", "guard", "tap", "impact",
                "정면에서 접근하는 돌진을 짧게 받아냅니다. 맞물리면 적의 돌진을 끊고 서로 튕겨 나갑니다.", "two opposing hooked blades meeting in a split star"),
            R(Technique.OpenGuard, WeaponType.Sword, "방패 틈 벌리기", "precision", "tap", "impact",
                "공격을 준비 중인 적의 앞에서 방향을 비틉니다. 열린 옆면을 처음 때리는 평타가 강화됩니다.", "a hinged plate with a highlighted open side"),
            R(Technique.Tripwire, WeaponType.Bow, "길목 봉합", "trap", "recast", "placement impact",
                "두 번 조준해 땅에 실을 걸고, 선을 가로지르는 첫 적을 붙잡습니다.", "two anchored arrowheads and a taut braided strand"),
            R(Technique.BindPair, WeaponType.Bow, "끌매듭", "control", "recast", "impact",
                "서로 다른 적 둘을 연결합니다. 둘이 멀어져 실이 팽팽해지면 가운데로 끌어당깁니다.", "interlocking coils that tighten and snap inward"),
            R(Technique.LureArrow, WeaponType.Bow, "울림촉", "control", "tap", "placement",
                "조준한 바닥에 울림촉을 박아 주변 일반 적의 추적을 그 지점으로 돌립니다.", "a grounded arrow with asymmetric chime ripples"),
            R(Technique.RecallArrow, WeaponType.Bow, "회수 사선", "precision", "recast", "placement impact arrival",
                "화살을 땅에 박은 뒤 자리를 옮기고 재입력합니다. 화살이 내 현재 위치로 돌아오는 선을 벱니다.", "a reversing barbed arrow and a split return ribbon"),
            R(Technique.CaptureRound, WeaponType.Gun, "탄환 압수", "guard", "tap", "impact",
                "조준선에서 마주 오는 적 탄환을 가로막습니다. 성공하면 다음 평타에 압수한 탄의 위력이 실립니다.", "a brass cartridge closing around a bright pellet"),
            R(Technique.WallShatter, WeaponType.Gun, "벽면 파쇄", "precision", "tap", "impact",
                "사거리 안의 단단한 벽에 탄을 쏘면 충돌 면을 따라 파편이 퍼져 벽 옆의 적을 맞힙니다.", "a shallow spray of chipped masonry and sparks"),
            R(Technique.IgniteCasings, WeaponType.Gun, "탄피 점화", "sustain", "tap", "impact",
                "평타로 떨어뜨린 최근 탄피 중 조준한 구역 안에 있는 탄피를 태웁니다.", "upright brass shells blooming into short flames"),
            R(Technique.Overwatch, WeaponType.Gun, "숨 고르기", "precision", "tap", "impact",
                "제자리에 서서 사선을 지킵니다. 사선을 처음 가로지르는 적에게 자동으로 탄을 발사합니다.", "a notched sight line with a bright trigger notch"),
            R(Technique.ResonanceAnchor, WeaponType.Staff, "공명 닻", "sustain", "tap", "placement impact",
                "땅에 공명 닻을 둡니다. 다른 액티브를 쓰면 닻이 한 번 공명해 그 자리에서 충격을 냅니다.", "a suspended stone lens with an expanding three-lobed wave"),
            R(Technique.RewindSeal, WeaponType.Staff, "되감기 인장", "control", "recast", "placement impact",
                "적 하나의 위치를 기록합니다. 재입력하면 이동 가능한 경로를 따라 기록한 자리로 되돌립니다.", "an hourglass stamp and staggered reverse ticks"),
            R(Technique.TwinGates, WeaponType.Staff, "쌍문 전도", "control", "recast", "placement impact",
                "두 번 조준해 입구와 출구를 놓습니다. 입구에 들어온 탄환 한 발이 출구 방향으로 나옵니다.", "two beveled keyhole gates and asymmetric entrance sparks"),
            R(Technique.TideSwitch, WeaponType.Staff, "밀물과 썰물", "control", "recast", "placement impact",
                "조준한 구역에 안쪽으로 흐르는 물결을 만듭니다. 재입력하면 물결이 뒤집혀 적을 바깥으로 밀어냅니다.", "curved surf crests that reverse around an open eye"),
            R(Technique.ShadowSwap, WeaponType.Dagger, "그림자 교대", "mobility", "recast", "placement arrival",
                "현재 위치에 그림자를 남깁니다. 사거리 안에서 재입력하면 그림자와 자리를 바꿉니다.", "two offset torn silhouettes trading places"),
            R(Technique.ThresholdTrap, WeaponType.Dagger, "문턱 덫", "trap", "tap", "placement impact",
                "방향이 있는 덫을 놓습니다. 앞쪽에서 안으로 경계를 넘는 적에게만 덫 자리에서 폭발합니다.", "a one-way jaw with a short ground-level fracture"),
            R(Technique.BreakPursuit, WeaponType.Dagger, "추적선 끊기", "mobility", "tap", "placement arrival",
                "짧게 물러나며 출발점에 미끼를 남깁니다. 나를 추적 중이던 일반 적의 시선을 미끼로 돌립니다.", "a staggered footprint that dissolves into a false target"),
            R(Technique.BackLock, WeaponType.Dagger, "빈틈 잠금", "precision", "tap", "impact",
                "공격을 마친 적의 등 뒤에서 쓰면 잠시 방향 전환을 봉쇄합니다. 옆으로 돌아가 공격할 틈이 생깁니다.", "an off-axis clasp snapping shut behind a target"),
        };

        public static Rule Get(Technique id)
        {
            int index = (int)id - 1;
            return index >= 0 && index < All.Length ? All[index] : null;
        }

        public static bool HasSignal(Technique id, string signal) =>
            Get(id)?.Signals.Split(' ').Contains(signal) == true;

        public static bool UsesShot(Technique id) => id is Technique.BladeReflect or Technique.LureArrow or Technique.RecallArrow
            or Technique.CaptureRound or Technique.WallShatter or Technique.Overwatch or Technique.TwinGates;

        public static bool AcceptsLinkedPoint(Technique id) => id is Technique.Tripwire or Technique.LureArrow or Technique.RecallArrow
            or Technique.ResonanceAnchor or Technique.TideSwitch or Technique.ThresholdTrap;
    }
}
