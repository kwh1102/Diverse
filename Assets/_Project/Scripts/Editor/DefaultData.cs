using System.Collections.Generic;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// The original hard-coded data table, kept only as the seed for Diverse/Rebuild Project Assets.
    /// The game reads Assets/_Project/Data/*.asset; edit those in the Inspector, not this file.
    /// </summary>
    public static class DefaultData
    {
        public static readonly List<CostumeDef> Costumes = new List<CostumeDef>();
        public static readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();

        static DefaultData()
        {
            BuildCostumes();
            BuildEnemies();
        }

        public static void BuildCostumes()
        {
            Costumes.Add(new CostumeDef
            {
                id = "white", name = "흰 토끼", perk = "경험치 +30%",
                fur = Pal.Hex("f2efe6"), furShade = Pal.Hex("c9c6cc"), outfit = Pal.Hex("2986b8"), outfitShade = Pal.Hex("1f4f7a"), accent = Pal.Hex("cb3416"), style = 0,
                mods = new[] { new StatMod(StatId.XpGain, 0.30f, true) }, tags = "GROWTH",
            });
            Costumes.Add(new CostumeDef
            {
                id = "brown", name = "갈색 토끼", perk = "치명타 확률 +12%, 치명타 피해 +25%",
                fur = Pal.Hex("c1a572"), furShade = Pal.Hex("a48656"), outfit = Pal.Hex("70a744"), outfitShade = Pal.Hex("387840"), accent = Pal.Hex("e1c549"), style = 1,
                mods = new[] { new StatMod(StatId.CritChance, 0.12f, false), new StatMod(StatId.CritDamage, 0.25f, false) }, tags = "CRIT",
            });
            Costumes.Add(new CostumeDef
            {
                id = "black", name = "검은 토끼", perk = "재사용 대기시간 -15%, 대시 재사용 -20%",
                fur = Pal.Hex("4a4b55"), furShade = Pal.Hex("3a3e4c"), outfit = Pal.Hex("8a5fa8"), outfitShade = Pal.Hex("4b3566"), accent = Pal.Hex("c79bd6"), style = 2,
                mods = new[] { new StatMod(StatId.CooldownReduction, 0.15f, false), new StatMod(StatId.DashCooldown, -0.2f, true) }, tags = "SHADOW",
            });
            Costumes.Add(new CostumeDef
            {
                id = "gray", name = "회색 토끼", perk = "최대 체력 +30, 받는 피해 -10%",
                fur = Pal.Hex("8a8a94"), furShade = Pal.Hex("5c5a61"), outfit = Pal.Hex("7a6457"), outfitShade = Pal.Hex("544440"), accent = Pal.Hex("c9c6cc"), style = 3,
                mods = new[] { new StatMod(StatId.MaxHp, 30, false), new StatMod(StatId.Armor, 0.10f, false) }, tags = "GUARD",
            });
            Costumes.Add(new CostumeDef
            {
                id = "pink", name = "분홍 토끼", perk = "초당 체력 회복 +0.8, 원소 위력 +20%",
                fur = Pal.Hex("e7a9cf"), furShade = Pal.Hex("b86f9c"), outfit = Pal.Hex("e1c549"), outfitShade = Pal.Hex("a48656"), accent = Pal.Hex("f2efe6"), style = 4,
                mods = new[] { new StatMod(StatId.Regen, 0.8f, false), new StatMod(StatId.ElementPower, 0.2f, false) }, tags = "NATURE",
            });
            Costumes.Add(new CostumeDef
            {
                id = "gold", name = "금빛 토끼", perk = "골드 획득 +50%, 이동 속도 +8%",
                fur = Pal.Hex("e1c549"), furShade = Pal.Hex("a48656"), outfit = Pal.Hex("cb3416"), outfitShade = Pal.Hex("572526"), accent = Pal.Hex("f2efe6"), style = 5,
                mods = new[] { new StatMod(StatId.GoldGain, 0.5f, true), new StatMod(StatId.MoveSpeed, 0.08f, true) }, tags = "FORTUNE",
            });
        }

        public static void BuildEnemies()
        {
            void Add(EnemyDef e) => Enemies[e.id] = e;
            Add(new EnemyDef { id = "shroom", name = "버섯 꼬마", brain = EnemyBrain.Melee, hp = 26, damage = 7, speed = 1.9f, xp = 5, gold = 1, sprite = "shroom" });
            Add(new EnemyDef { id = "jelly", name = "젤리", brain = EnemyBrain.Hopper, hp = 20, damage = 6, speed = 2.6f, xp = 4, gold = 1, sprite = "jelly", radius = 0.4f, attackRange = 1.4f });
            Add(new EnemyDef { id = "fox", name = "여우 산적", brain = EnemyBrain.Melee, hp = 40, damage = 10, speed = 2.7f, xp = 9, gold = 4, sprite = "fox", attackWindup = 0.38f, attackCooldown = 1.1f });
            Add(new EnemyDef { id = "raccoon", name = "너구리 사수", brain = EnemyBrain.Ranged, hp = 28, damage = 9, speed = 2.1f, xp = 9, gold = 4, sprite = "raccoon", attackRange = 6f, attackWindup = 0.55f, attackCooldown = 1.8f, projectileColor = Pal.Hex("ffcf6b") });
            Add(new EnemyDef { id = "wisp", name = "떠도는 넋", brain = EnemyBrain.Caster, hp = 30, damage = 9, speed = 1.7f, xp = 10, gold = 3, sprite = "wisp", attackRange = 5.5f, attackWindup = 0.7f, attackCooldown = 2.1f, projectileColor = Pal.ShadowEl, element = Element.Shadow });
            Add(new EnemyDef { id = "boar", name = "돌갑옷 멧돼지", brain = EnemyBrain.Charger, hp = 70, damage = 14, speed = 1.8f, xp = 16, gold = 6, sprite = "boar", radius = 0.6f, mass = 2.5f, attackRange = 4.5f, attackWindup = 0.7f, attackCooldown = 2.4f });
            Add(new EnemyDef { id = "bat", name = "밤박쥐", brain = EnemyBrain.Swarm, hp = 12, damage = 5, speed = 3.4f, xp = 3, gold = 1, sprite = "bat", radius = 0.35f, attackRange = 0.9f, attackWindup = 0.25f, attackCooldown = 0.9f, mass = 0.5f });
            Add(new EnemyDef { id = "frog", name = "개구리 사냥꾼", brain = EnemyBrain.Hopper, attack = EnemyAttack.Tongue, hp = 32, damage = 9, speed = 2.8f, xp = 8, gold = 3, sprite = "frog", radius = 0.42f, mass = 0.9f, attackRange = 3.6f, attackWindup = 0.5f, attackCooldown = 1.9f, aggroRange = 7.5f, hopInterval = 0.5f, attackParam = 3.4f });
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
    }
}
