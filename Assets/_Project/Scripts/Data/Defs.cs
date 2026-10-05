using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum WeaponKind { Greatsword, SwordShield, Crossbow, Staff, Dagger, Katana }
    public enum Element { None, Fire, Frost, Lightning, Shadow, Holy, Poison, Wind }

    /// <summary>
    /// One step of a basic-attack combo. Hit feel is decided almost entirely by these numbers.
    /// </summary>
    public class ComboStep
    {
        public float windup = 0.06f;     // delay before the hit lands
        public float recovery = 0.18f;   // time until the next input is accepted
        public float range = 1.6f;       // reach (world units)
        public float arc = 140f;         // slash angle (degrees)
        public float damage = 1f;        // attack multiplier
        public float lunge = 0.25f;      // distance the player steps forward on attack
        public float knockback = 3f;     // distance enemies get pushed
        public float hitstop = 0.05f;    // freeze-frame duration on hit
        public float shake = 0.15f;      // screen shake (0~1)
        public float kick = 2f;          // screen kick (pixels)
        public bool thrust;              // thrust (straight line) attack
        public bool spin;                // 360-degree attack
        public bool projectile;          // fires a projectile
        public int projectileCount = 1;
        public float projectileSpread;
        public float recoil;             // knockback the player takes from firing
        public float swingDir = 1;       // slash direction (1 = top to bottom, -1 = reverse)
        public float fxScale = 1f;
    }

    public class WeaponDef
    {
        public WeaponKind kind;
        public string name;
        public string desc;
        public string tags;              // ability language tags ("SWORD HEAVY MELEE")
        public Color32 slashColor;
        public Color32 slashCore;
        public float baseDamage = 10;
        public float comboReset = 0.7f;  // combo resets after this long without attacking
        public float moveSpeedMul = 1f;
        public float attackRange = 1.6f; // range at which right-click auto-attack starts
        public ComboStep[] combo;
        public string[] skills = new string[4];  // QWER skill ids
        public Element element;
        public string sfxSwing = "swing";
        public string sfxHit = "hit";
    }

    public class CostumeDef
    {
        public string id, name, perk;
        public Color32 fur, furShade, outfit, outfitShade, accent;
        public int style;
        public System.Action<Stats> apply;
        public string tags;              // costume-specific tags (fed into the AI context)
    }

    public enum EnemyBrain { Melee, Hopper, Ranged, Charger, Caster, Swarm, Boss }

    public class EnemyDef
    {
        public string id, name;
        public EnemyBrain brain;
        public float hp = 30, damage = 8, speed = 2.2f, radius = 0.45f;
        public float attackRange = 1.1f, attackWindup = 0.45f, attackCooldown = 1.4f;
        public float aggroRange = 7f, xp = 6, gold = 2;
        public float mass = 1f;
        public string sprite;
        public Color32 projectileColor = Pal.Blood;
        public Element element;
        public bool boss;
        public bool elite;               // outer-ring variant (tinted, bigger)
        public string[] lines;           // lines the boss speaks
    }

    /// <summary>
    /// Game data tables. To tweak numbers, change only this file.
    /// </summary>
    public static class DB
    {
        public static readonly Dictionary<WeaponKind, WeaponDef> Weapons = new Dictionary<WeaponKind, WeaponDef>();
        public static readonly List<CostumeDef> Costumes = new List<CostumeDef>();
        public static readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();

        static DB()
        {
            BuildWeapons();
            BuildCostumes();
            BuildEnemies();
        }

        public static WeaponDef W(WeaponKind k) => Weapons[k];
        public static CostumeDef Costume(string id) => Costumes.Find(c => c.id == id) ?? Costumes[0];
        public static EnemyDef Enemy(string id) => Enemies.TryGetValue(id, out var e) ? e : Enemies["shroom"];

        static void BuildWeapons()
        {
            // Greatsword: slow, heavy, the biggest freeze-frame and screen shake.
            Weapons[WeaponKind.Greatsword] = new WeaponDef
            {
                kind = WeaponKind.Greatsword, name = "대검",
                desc = "느리지만 한 방이 묵직하다. 휘두를 때마다 몸이 앞으로 쏠린다.",
                tags = "SWORD HEAVY MELEE", baseDamage = 22, comboReset = 0.9f, moveSpeedMul = 0.92f, attackRange = 2.1f,
                slashColor = Pal.Hex("ffd27a"), slashCore = Pal.White, sfxSwing = "swing_heavy", sfxHit = "hit_heavy",
                combo = new[]
                {
                    new ComboStep { windup = 0.13f, recovery = 0.32f, range = 2.3f, arc = 170, damage = 1.0f, lunge = 0.45f, knockback = 5, hitstop = 0.085f, shake = 0.28f, kick = 5, swingDir = 1, fxScale = 1.35f },
                    new ComboStep { windup = 0.12f, recovery = 0.34f, range = 2.3f, arc = 170, damage = 1.1f, lunge = 0.45f, knockback = 5, hitstop = 0.085f, shake = 0.28f, kick = 5, swingDir = -1, fxScale = 1.35f },
                    new ComboStep { windup = 0.22f, recovery = 0.55f, range = 2.6f, arc = 360, damage = 1.9f, lunge = 0.2f, knockback = 8, hitstop = 0.14f, shake = 0.5f, kick = 9, spin = true, fxScale = 1.5f },
                },
                skills = new[] { "gs_leap", "gs_whirl", "gs_rush", "gs_titan" },
            };

            // Sword and shield: steady, mid-weight 4-hit combo with a guard.
            Weapons[WeaponKind.SwordShield] = new WeaponDef
            {
                kind = WeaponKind.SwordShield, name = "검과 방패",
                desc = "균형 잡힌 연격과 단단한 방패. 막고 되받아친다.",
                tags = "SWORD SHIELD MELEE", baseDamage = 13, comboReset = 0.6f, attackRange = 1.6f, moveSpeedMul = 1f,
                slashColor = Pal.Hex("bfe3ff"), slashCore = Pal.White, sfxSwing = "swing", sfxHit = "hit_metal",
                combo = new[]
                {
                    new ComboStep { windup = 0.05f, recovery = 0.17f, range = 1.7f, arc = 140, damage = 0.9f, lunge = 0.25f, knockback = 2.5f, hitstop = 0.045f, shake = 0.12f, kick = 2.5f, swingDir = 1 },
                    new ComboStep { windup = 0.05f, recovery = 0.17f, range = 1.7f, arc = 140, damage = 0.9f, lunge = 0.25f, knockback = 2.5f, hitstop = 0.045f, shake = 0.12f, kick = 2.5f, swingDir = -1 },
                    new ComboStep { windup = 0.06f, recovery = 0.2f, range = 1.9f, arc = 40, damage = 1.1f, lunge = 0.45f, knockback = 3, hitstop = 0.05f, shake = 0.14f, kick = 3, thrust = true },
                    new ComboStep { windup = 0.1f, recovery = 0.36f, range = 1.6f, arc = 120, damage = 1.6f, lunge = 0.5f, knockback = 6, hitstop = 0.09f, shake = 0.3f, kick = 6, swingDir = 1, fxScale = 1.2f },
                },
                skills = new[] { "ss_bash", "ss_parry", "ss_leap", "ss_sanctum" },
            };

            // Crossbow: ranged; each shot pushes the body back with recoil.
            Weapons[WeaponKind.Crossbow] = new WeaponDef
            {
                kind = WeaponKind.Crossbow, name = "석궁",
                desc = "멀리서 정확하게. 쏠 때마다 반동으로 뒤로 밀린다.",
                tags = "BOW RANGED PROJECTILE", baseDamage = 12, comboReset = 0.8f, attackRange = 7.5f, moveSpeedMul = 1.05f,
                slashColor = Pal.Hex("fff1b0"), slashCore = Pal.White, sfxSwing = "shoot", sfxHit = "hit",
                combo = new[]
                {
                    new ComboStep { windup = 0.04f, recovery = 0.3f, range = 9, damage = 1.0f, projectile = true, recoil = 0.22f, knockback = 2, hitstop = 0.035f, shake = 0.1f, kick = 2.5f },
                    new ComboStep { windup = 0.04f, recovery = 0.3f, range = 9, damage = 1.0f, projectile = true, recoil = 0.22f, knockback = 2, hitstop = 0.035f, shake = 0.1f, kick = 2.5f },
                    new ComboStep { windup = 0.1f, recovery = 0.45f, range = 9, damage = 0.8f, projectile = true, projectileCount = 3, projectileSpread = 14, recoil = 0.45f, knockback = 3, hitstop = 0.05f, shake = 0.2f, kick = 4 },
                },
                skills = new[] { "cb_pierce", "cb_scatter", "cb_backstep", "cb_rain" },
            };

            // Staff: long reach, sweeping strikes, lightning.
            Weapons[WeaponKind.Staff] = new WeaponDef
            {
                kind = WeaponKind.Staff, name = "봉",
                desc = "긴 사거리로 휘감아 치는 연격. 끝에 맺힌 수정이 번개를 부른다.",
                tags = "STAFF MELEE MAGIC", baseDamage = 11, comboReset = 0.65f, attackRange = 2.2f, element = Element.Lightning,
                slashColor = Pal.Hex("c9b8ff"), slashCore = Pal.Hex("f4efff"), sfxSwing = "swing", sfxHit = "hit",
                combo = new[]
                {
                    new ComboStep { windup = 0.05f, recovery = 0.16f, range = 2.3f, arc = 150, damage = 0.8f, lunge = 0.15f, knockback = 2, hitstop = 0.04f, shake = 0.1f, kick = 2, swingDir = 1 },
                    new ComboStep { windup = 0.05f, recovery = 0.16f, range = 2.4f, arc = 30, damage = 0.9f, lunge = 0.3f, knockback = 2.5f, hitstop = 0.04f, shake = 0.1f, kick = 2, thrust = true },
                    new ComboStep { windup = 0.05f, recovery = 0.16f, range = 2.3f, arc = 150, damage = 0.8f, lunge = 0.15f, knockback = 2, hitstop = 0.04f, shake = 0.1f, kick = 2, swingDir = -1 },
                    new ComboStep { windup = 0.08f, recovery = 0.18f, range = 2.4f, arc = 360, damage = 0.7f, lunge = 0f, knockback = 2.5f, hitstop = 0.045f, shake = 0.14f, kick = 2, spin = true },
                    new ComboStep { windup = 0.12f, recovery = 0.38f, range = 2.6f, arc = 50, damage = 1.6f, lunge = 0.45f, knockback = 6, hitstop = 0.09f, shake = 0.3f, kick = 6, thrust = true, fxScale = 1.3f },
                },
                skills = new[] { "st_thrust", "st_cyclone", "st_vault", "st_thunder" },
            };

            // Dagger: very fast, light, strong crits.
            Weapons[WeaponKind.Dagger] = new WeaponDef
            {
                kind = WeaponKind.Dagger, name = "단검",
                desc = "눈보다 빠른 난도질. 가볍지만 급소를 노린다.",
                tags = "DAGGER MELEE SWIFT", baseDamage = 7, comboReset = 0.45f, attackRange = 1.3f, moveSpeedMul = 1.12f,
                slashColor = Pal.Hex("ffb3d1"), slashCore = Pal.White, sfxSwing = "swing_light", sfxHit = "hit_light",
                combo = new[]
                {
                    new ComboStep { windup = 0.025f, recovery = 0.09f, range = 1.35f, arc = 110, damage = 0.85f, lunge = 0.2f, knockback = 1, hitstop = 0.025f, shake = 0.06f, kick = 1.5f, swingDir = 1, fxScale = 0.8f },
                    new ComboStep { windup = 0.025f, recovery = 0.09f, range = 1.35f, arc = 110, damage = 0.85f, lunge = 0.2f, knockback = 1, hitstop = 0.025f, shake = 0.06f, kick = 1.5f, swingDir = -1, fxScale = 0.8f },
                    new ComboStep { windup = 0.03f, recovery = 0.1f, range = 1.5f, arc = 30, damage = 1.0f, lunge = 0.35f, knockback = 1.5f, hitstop = 0.03f, shake = 0.08f, kick = 2, thrust = true, fxScale = 0.8f },
                    new ComboStep { windup = 0.06f, recovery = 0.22f, range = 1.5f, arc = 360, damage = 1.4f, lunge = 0.1f, knockback = 3.5f, hitstop = 0.06f, shake = 0.18f, kick = 3, spin = true, fxScale = 0.9f },
                },
                skills = new[] { "dg_fan", "dg_shadowstep", "dg_venom", "dg_frenzy" },
            };

            // Katana: quickdraw. A short pause, then a single sharp cut.
            Weapons[WeaponKind.Katana] = new WeaponDef
            {
                kind = WeaponKind.Katana, name = "도",
                desc = "칼집에서 뽑는 순간이 가장 날카롭다. 일섬의 미학.",
                tags = "BLADE MELEE IAI", baseDamage = 14, comboReset = 0.7f, attackRange = 1.9f,
                slashColor = Pal.Hex("d4f3ff"), slashCore = Pal.White, sfxSwing = "swing", sfxHit = "hit_slice",
                combo = new[]
                {
                    new ComboStep { windup = 0.04f, recovery = 0.18f, range = 2.0f, arc = 150, damage = 1.0f, lunge = 0.35f, knockback = 2, hitstop = 0.05f, shake = 0.12f, kick = 3, swingDir = 1, fxScale = 1.1f },
                    new ComboStep { windup = 0.04f, recovery = 0.18f, range = 2.0f, arc = 150, damage = 1.0f, lunge = 0.35f, knockback = 2, hitstop = 0.05f, shake = 0.12f, kick = 3, swingDir = -1, fxScale = 1.1f },
                    new ComboStep { windup = 0.16f, recovery = 0.42f, range = 2.4f, arc = 60, damage = 2.0f, lunge = 1.1f, knockback = 4, hitstop = 0.11f, shake = 0.35f, kick = 7, thrust = true, fxScale = 1.3f },
                },
                skills = new[] { "kt_iai", "kt_crescent", "kt_counter", "kt_flash" },
            };
        }

        static void BuildCostumes()
        {
            Costumes.Add(new CostumeDef
            {
                id = "white", name = "흰 토끼", perk = "경험치 +30%",
                fur = Pal.Hex("fbf6f0"), furShade = Pal.Hex("d9cfc8"), outfit = Pal.Hex("6aa3e8"), outfitShade = Pal.Hex("4a76b8"), accent = Pal.Hex("f26d6d"), style = 0,
                apply = s => s.Mul(StatId.XpGain, 0.30f), tags = "GROWTH",
            });
            Costumes.Add(new CostumeDef
            {
                id = "brown", name = "갈색 토끼", perk = "치명타 확률 +12%, 치명타 피해 +25%",
                fur = Pal.Hex("b98258"), furShade = Pal.Hex("8a5c3d"), outfit = Pal.Hex("4f8a5b"), outfitShade = Pal.Hex("356141"), accent = Pal.Hex("e9c46a"), style = 1,
                apply = s => { s.Add(StatId.CritChance, 0.12f); s.Add(StatId.CritDamage, 0.25f); }, tags = "CRIT",
            });
            Costumes.Add(new CostumeDef
            {
                id = "black", name = "검은 토끼", perk = "재사용 대기시간 -15%, 대시 재사용 -20%",
                fur = Pal.Hex("4a4458"), furShade = Pal.Hex("2f2a3a"), outfit = Pal.Hex("8c5bd6"), outfitShade = Pal.Hex("61409a"), accent = Pal.Hex("c9b8ff"), style = 2,
                apply = s => { s.Add(StatId.CooldownReduction, 0.15f); s.Mul(StatId.DashCooldown, -0.2f); }, tags = "SHADOW",
            });
            Costumes.Add(new CostumeDef
            {
                id = "gray", name = "회색 토끼", perk = "최대 체력 +30, 받는 피해 -10%",
                fur = Pal.Hex("a9a6b3"), furShade = Pal.Hex("7c7987"), outfit = Pal.Hex("b0603e"), outfitShade = Pal.Hex("7e4129"), accent = Pal.Hex("d9e1ea"), style = 3,
                apply = s => { s.Add(StatId.MaxHp, 30); s.Add(StatId.Armor, 0.10f); }, tags = "GUARD",
            });
            Costumes.Add(new CostumeDef
            {
                id = "pink", name = "분홍 토끼", perk = "초당 체력 회복 +0.8, 원소 위력 +20%",
                fur = Pal.Hex("ffd6e0"), furShade = Pal.Hex("e6a8b9"), outfit = Pal.Hex("fff1c9"), outfitShade = Pal.Hex("d9c48f"), accent = Pal.Hex("ff7fa8"), style = 4,
                apply = s => { s.Add(StatId.Regen, 0.8f); s.Add(StatId.ElementPower, 0.2f); }, tags = "NATURE",
            });
            Costumes.Add(new CostumeDef
            {
                id = "gold", name = "금빛 토끼", perk = "골드 획득 +50%, 이동 속도 +8%",
                fur = Pal.Hex("f7d98b"), furShade = Pal.Hex("d1a95a"), outfit = Pal.Hex("c23b4e"), outfitShade = Pal.Hex("8a2536"), accent = Pal.Hex("fff2b3"), style = 5,
                apply = s => { s.Mul(StatId.GoldGain, 0.5f); s.Mul(StatId.MoveSpeed, 0.08f); }, tags = "FORTUNE",
            });
        }

        static void BuildEnemies()
        {
            void Add(EnemyDef e) => Enemies[e.id] = e;
            Add(new EnemyDef { id = "shroom", name = "버섯 꼬마", brain = EnemyBrain.Melee, hp = 26, damage = 7, speed = 1.9f, xp = 5, gold = 1, sprite = "shroom" });
            Add(new EnemyDef { id = "jelly", name = "젤리", brain = EnemyBrain.Hopper, hp = 20, damage = 6, speed = 2.6f, xp = 4, gold = 1, sprite = "jelly", radius = 0.4f, attackRange = 1.4f });
            Add(new EnemyDef { id = "fox", name = "여우 산적", brain = EnemyBrain.Melee, hp = 40, damage = 10, speed = 2.7f, xp = 9, gold = 4, sprite = "fox", attackWindup = 0.38f, attackCooldown = 1.1f });
            Add(new EnemyDef { id = "raccoon", name = "너구리 사수", brain = EnemyBrain.Ranged, hp = 28, damage = 9, speed = 2.1f, xp = 9, gold = 4, sprite = "raccoon", attackRange = 6f, attackWindup = 0.55f, attackCooldown = 1.8f, projectileColor = Pal.Hex("ffcf6b") });
            Add(new EnemyDef { id = "wisp", name = "떠도는 넋", brain = EnemyBrain.Caster, hp = 30, damage = 9, speed = 1.7f, xp = 10, gold = 3, sprite = "wisp", attackRange = 5.5f, attackWindup = 0.7f, attackCooldown = 2.1f, projectileColor = Pal.ShadowEl, element = Element.Shadow });
            Add(new EnemyDef { id = "boar", name = "돌갑옷 멧돼지", brain = EnemyBrain.Charger, hp = 70, damage = 14, speed = 1.8f, xp = 16, gold = 6, sprite = "boar", radius = 0.6f, mass = 2.5f, attackRange = 4.5f, attackWindup = 0.7f, attackCooldown = 2.4f });
            Add(new EnemyDef { id = "bat", name = "밤박쥐", brain = EnemyBrain.Swarm, hp = 12, damage = 5, speed = 3.4f, xp = 3, gold = 1, sprite = "bat", radius = 0.35f, attackRange = 0.9f, attackWindup = 0.25f, attackCooldown = 0.9f, mass = 0.5f });
            Add(new EnemyDef { id = "frostling", name = "서리 정령", brain = EnemyBrain.Caster, hp = 34, damage = 10, speed = 1.8f, xp = 11, gold = 4, sprite = "frostling", attackRange = 5.5f, attackWindup = 0.65f, attackCooldown = 2f, projectileColor = Pal.Frost, element = Element.Frost });

            Add(new EnemyDef
            {
                id = "boss_fox", name = "붉은 꼬리 두목", brain = EnemyBrain.Boss, boss = true, hp = 520, damage = 16, speed = 2.6f, radius = 0.9f, mass = 6,
                xp = 120, gold = 60, sprite = "boss_fox", attackRange = 2.4f, attackWindup = 0.55f, attackCooldown = 1.3f, aggroRange = 9,
                lines = new[] { "이 숲길은 내 거다, 토끼.", "꼬리가 불타는 맛을 보여주마!", "...다음 삶에서 보자." },
            });
            Add(new EnemyDef
            {
                id = "boss_shroom", name = "고목 버섯왕", brain = EnemyBrain.Boss, boss = true, hp = 640, damage = 14, speed = 1.6f, radius = 1.1f, mass = 10,
                xp = 140, gold = 70, sprite = "boss_shroom", attackRange = 3f, attackWindup = 0.8f, attackCooldown = 1.6f, aggroRange = 9, projectileColor = Pal.Hex("b6f06b"), element = Element.Poison,
                lines = new[] { "포자가... 너를 기억할 것이다.", "숲을 어지럽히는 자여.", "흙으로... 돌아가자..." },
            });
            Add(new EnemyDef
            {
                id = "boss_knight", name = "망각의 기사", brain = EnemyBrain.Boss, boss = true, hp = 820, damage = 20, speed = 2.2f, radius = 0.9f, mass = 8,
                xp = 200, gold = 90, sprite = "boss_knight", attackRange = 2.6f, attackWindup = 0.6f, attackCooldown = 1.2f, aggroRange = 10, projectileColor = Pal.ShadowEl, element = Element.Shadow,
                lines = new[] { "이름 없는 자여, 탑은 너를 허락하지 않는다.", "기억은 무겁다. 내려놓아라.", "...그래, 너는 기억되겠지." },
            });
        }

        public static EnemyDef ScaledEnemy(string id, float tier)
        {
            var b = Enemy(id);
            float hpMul = 1f + tier * 0.35f, dmgMul = 1f + tier * 0.18f;
            return new EnemyDef
            {
                id = b.id, name = b.name, brain = b.brain, hp = b.hp * hpMul, damage = b.damage * dmgMul, speed = b.speed * (1 + Mathf.Min(tier, 10) * 0.02f), radius = b.radius,
                attackRange = b.attackRange, attackWindup = b.attackWindup, attackCooldown = b.attackCooldown, aggroRange = b.aggroRange,
                xp = b.xp * (1 + tier * 0.25f), gold = b.gold * (1 + tier * 0.25f), mass = b.mass, sprite = b.sprite, projectileColor = b.projectileColor,
                element = b.element, boss = b.boss, lines = b.lines,
            };
        }

        /// <summary>A stronger variant that shows up in the outer rings: tougher, faster to attack, better loot, shown with a tint.</summary>
        public static EnemyDef Elite(EnemyDef d)
        {
            if (d.boss) return d;
            d.elite = true;
            d.name = "정예 " + d.name;
            d.hp *= 2.2f; d.damage *= 1.35f;
            d.attackCooldown *= 0.8f; d.speed *= 1.1f;
            d.radius *= 1.15f; d.mass *= 1.6f;
            d.xp *= 2.5f; d.gold *= 2.5f;
            return d;
        }
    }
}
