using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Default world data: 9 themes (the spawn's meadow + one per build type), the 8 build types, the world rules, and the
    /// EnemySpawner in Game.unity. Only creates what is missing — edited assets are never overwritten.
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.WorldThemeDefaults.RunBatch
    /// </summary>
    public static class WorldThemeDefaults
    {
        const string DataDir = "Assets/_Project/Data";

        [MenuItem("Diverse/World/Create Missing World Data")]
        public static void CreateAll()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Resources/GameDatabase.asset");
            Directory.CreateDirectory($"{DataDir}/Themes");
            Directory.CreateDirectory($"{DataDir}/CombatTypes");

            var themes = new Dictionary<string, ThemeData>();
            foreach (var t in Themes())
            {
                string path = $"{DataDir}/Themes/{t.id}.asset";
                var a = AssetDatabase.LoadAssetAtPath<ThemeData>(path);
                if (a == null) { a = ScriptableObject.CreateInstance<ThemeData>(); a.def = t; AssetDatabase.CreateAsset(a, path); }
                themes[t.id] = a;
                if (!db.themes.Contains(a)) db.themes.Add(a);
            }
            // The spawn's land must stay first (DB.SpawnTheme)
            db.themes.Remove(themes["meadow"]);
            db.themes.Insert(0, themes["meadow"]);

            foreach (var (type, name, theme, summary) in CombatTypes())
            {
                string path = $"{DataDir}/CombatTypes/{type}.asset";
                var a = AssetDatabase.LoadAssetAtPath<CombatTypeData>(path);
                if (a == null)
                {
                    a = ScriptableObject.CreateInstance<CombatTypeData>();
                    a.def = new CombatTypeDef { type = type, displayName = name, theme = themes[theme], summary = summary };
                    AssetDatabase.CreateAsset(a, path);
                }
                if (!db.combatTypes.Contains(a)) db.combatTypes.Add(a);
            }

            string rulesPath = $"{DataDir}/WorldRules.asset";
            var rules = AssetDatabase.LoadAssetAtPath<WorldRulesData>(rulesPath);
            if (rules == null) { rules = ScriptableObject.CreateInstance<WorldRulesData>(); AssetDatabase.CreateAsset(rules, rulesPath); }
            db.worldRules = rules;
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            // Monsters roam the world: the spawner lives next to World in Game.unity
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Game.unity", OpenSceneMode.Single);
            var world = Object.FindFirstObjectByType<World>();
            if (world != null && world.GetComponent<EnemySpawner>() == null)
            {
                world.gameObject.AddComponent<EnemySpawner>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log($"[WorldThemeDefaults] themes {db.themes.Count}, combat types {db.combatTypes.Count}");
        }

        public static void RunBatch()
        {
            try { CreateAll(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // ───────────── Defaults ─────────────

        static IEnumerable<(CombatType, string, string, string)> CombatTypes()
        {
            yield return (CombatType.MeleeRapid, "근거리 공속", "overgrowth", "끊임없이 붙어서 몰아치며 적의 재생을 압도한다");
            yield return (CombatType.MeleeBurst, "근거리 극딜", "volcano", "위험한 순간에 파고들어 결정적인 한 방");
            yield return (CombatType.RangedRapid, "원거리 공속", "crystal", "위치를 지키며 탄막을 계속 명중시킨다");
            yield return (CombatType.RangedBurst, "원거리 극딜", "glacier", "움직임을 읽고 정확한 한 발로 끝낸다");
            yield return (CombatType.Speed, "이동속도", "canyon", "계속 달리며 공격을 유도하고 빈틈을 친다");
            yield return (CombatType.Tank, "탱킹", "fortress", "물러서지 않고 버티며 전세를 뒤집는다");
            yield return (CombatType.Evasion, "회피", "mirror", "관찰하고 완벽하게 피해 역공 기회를 만든다");
            yield return (CombatType.Area, "광역", "swamp", "넓은 공격으로 무리 전체를 한꺼번에 무너뜨린다");
        }

        static DecorEntry D(string sprite, int variants, float chance, bool block = false, float w = 0.8f, float d = 0.5f, bool flat = false, bool clustered = false, bool cliffs = false) =>
            new DecorEntry { sprite = sprite, variants = variants, chance = chance, block = block, width = w, depth = d, flat = flat, clustered = clustered, onCliffs = cliffs };

        /// <summary>Trees, rocks, bushes and tufts of a biome's art set (plus the theme's own pieces).</summary>
        static List<DecorEntry> Nature(Biome b, float trees, params DecorEntry[] extra)
        {
            var list = new List<DecorEntry>
            {
                D($"tree_{b}_{{v}}", 4, trees, true, 1.0f, 0.6f, clustered: true),
                D($"rock_{b}_{{v}}", 3, 0.004f, true, 0.85f, 0.55f, cliffs: true),
                D($"bush_{b}_{{v}}", 2, 0.03f, cliffs: true),
                D($"tuft_{b}_{{v}}", 2, 0.07f, flat: true, cliffs: true),
            };
            list.InsertRange(0, extra);
            return list;
        }

        static ThemeDef T(string id, string name, string prefix, Biome b, string color, Ground g, Ground alt, float altShare, Ground liquid, float mountains, float water,
                          float density, List<DecorEntry> decor, string landmark, Vector2 footprint, float arena, Ground arenaGround, string boss, params string[] enemies) =>
            new ThemeDef
            {
                id = id, displayName = name, prefix = prefix, biome = b, mapColor = Pal.Hex(color),
                ground = g, groundAlt = alt, altShare = altShare, liquid = liquid, road = id == "fortress" ? Ground.Flagstone : Ground.Path,
                mountainBias = mountains, waterBias = water, mesas = id == "canyon" ? 0.7f : 0, density = density, decor = decor,
                landmarkName = landmark, landmarkSprite = string.IsNullOrEmpty(landmark) ? "" : "landmark_" + id, landmarkFootprint = footprint,
                arenaRadius = arena, arenaGround = arenaGround, boss = boss, enemies = new List<string>(enemies),
            };

        static IEnumerable<ThemeDef> Themes()
        {
            var meadow = Nature(Biome.Meadow, 0.035f, D("flower_{v}", 5, 0.03f, flat: true));
            yield return T("meadow", "바람결 초원", "초원", Biome.Meadow, "6fb04a", Ground.Grass, Ground.DarkGrass, 0.3f, Ground.Water, 0, 0,
                1, meadow, "", Vector2.zero, 0, Ground.Plaza, "boss_fox", "shroom", "jelly", "fox", "frog");
            yield return T("overgrowth", "증식의 숲", "숲", Biome.Forest, "2f7a3c", Ground.DarkGrass, Ground.Grass, 0.35f, Ground.Water, 0.05f, 0.05f,
                1.6f, Nature(Biome.Forest, 0.09f, D("deco_overgrowth_0", 1, 0.004f, true, 0.8f, 0.5f), D("deco_overgrowth_1", 1, 0.002f, true, 1.8f, 0.5f),
                    D("deco_overgrowth_2", 1, 0.003f, true, 0.8f, 0.5f), D("deco_overgrowth_3", 1, 0.004f, cliffs: true)),
                "태고의 고목", new Vector2(5, 2.5f), 18, Ground.Grass, "boss_fox", "fox", "shroom", "raccoon", "bat");
            yield return T("volcano", "붕괴의 화산지대", "화산", Biome.Ashland, "a8382a", Ground.Ash, Ground.Redrock, 0.25f, Ground.Lava, 0.15f, -0.05f,
                0.8f, Nature(Biome.Ashland, 0.02f, D("deco_volcano_0", 1, 0.003f, true, 1.0f, 0.6f), D("deco_volcano_1", 1, 0.006f, cliffs: true),
                    D("deco_volcano_2", 1, 0.004f, true, 0.6f, 0.4f, cliffs: true), D("deco_volcano_3", 1, 0.002f, flat: true)),
                "분화의 심장", new Vector2(9, 4), 20, Ground.Ash, "boss_knight", "boar", "wisp", "bat");
            yield return T("crystal", "공명의 수정평원", "수정", Biome.Crystal, "a98fd6", Ground.Crystal, Ground.Grass, 0.2f, Ground.Water, -0.2f, 0,
                0.7f, Nature(Biome.Crystal, 0.02f, D("deco_crystal_0", 1, 0.004f, true, 1.2f, 0.6f, cliffs: true), D("deco_crystal_1", 1, 0.002f, true, 0.7f, 0.4f),
                    D("deco_crystal_2", 1, 0.003f, flat: true), D("deco_crystal_3", 1, 0.003f, true, 0.6f, 0.4f)),
                "공명탑", new Vector2(5, 3), 18, Ground.Crystal, "boss_shroom", "jelly", "wisp", "bat");
            yield return T("glacier", "침묵의 빙하지대", "빙하", Biome.Tundra, "a9d6ef", Ground.Snow, Ground.Ice, 0.15f, Ground.Ice, 0.1f, 0.05f,
                0.8f, Nature(Biome.Tundra, 0.04f, D("deco_glacier_0", 1, 0.004f, true, 0.8f, 0.5f, cliffs: true), D("deco_glacier_1", 1, 0.004f),
                    D("deco_glacier_2", 1, 0.003f, true, 1.0f, 0.6f), D("deco_glacier_3", 1, 0.002f)),
                "얼음성", new Vector2(8, 3.5f), 20, Ground.Snow, "boss_knight", "frostling", "boar", "bat");
            yield return T("canyon", "질풍의 협곡", "협곡", Biome.Dunes, "d0773e", Ground.Redrock, Ground.Sand, 0.35f, Ground.Water, 0.1f, -0.15f,
                0.6f, Nature(Biome.Dunes, 0.012f, D("deco_canyon_0", 1, 0.004f, true, 0.8f, 0.5f), D("deco_canyon_1", 1, 0.004f),
                    D("deco_canyon_2", 1, 0.003f, flat: true), D("deco_canyon_3", 1, 0.002f, true, 0.3f, 0.3f)),
                "바람탑", new Vector2(4, 2.5f), 18, Ground.Sand, "boss_fox", "raccoon", "boar", "jelly");
            yield return T("fortress", "불굴의 요새", "요새", Biome.Fortress, "8d8a80", Ground.Grass, Ground.Flagstone, 0.35f, Ground.Water, 0.05f, 0,
                0.8f, Nature(Biome.Fortress, 0.03f, D("deco_fortress_0", 1, 0.003f, true, 2.0f, 0.6f), D("deco_fortress_1", 1, 0.003f, true, 1.2f, 0.5f),
                    D("deco_fortress_2", 1, 0.002f, true, 1.6f, 0.6f), D("deco_fortress_3", 1, 0.002f, true, 1.0f, 0.4f)),
                "불굴의 성채", new Vector2(9, 4), 20, Ground.Flagstone, "boss_knight", "boar", "fox", "raccoon");
            yield return T("mirror", "뒤틀린 거울유적", "거울", Biome.Mirror, "5b3f86", Ground.Mirror, Ground.Ash, 0.25f, Ground.Water, 0, 0,
                0.8f, Nature(Biome.Mirror, 0.03f, D("deco_mirror_0", 1, 0.004f, cliffs: true), D("deco_mirror_1", 1, 0.002f, true, 0.8f, 0.4f),
                    D("deco_mirror_2", 1, 0.003f, true, 0.7f, 0.5f), D("deco_mirror_3", 1, 0.002f, flat: true)),
                "거울 궁전", new Vector2(8, 3), 18, Ground.Mirror, "boss_shroom", "wisp", "bat", "jelly");
            yield return T("swamp", "군체의 늪", "늪", Biome.Marsh, "5f7a2c", Ground.Swamp, Ground.DarkGrass, 0.35f, Ground.Toxic, -0.15f, 0.15f,
                1.1f, Nature(Biome.Marsh, 0.04f, D("deco_swamp_0", 1, 0.004f, true, 0.8f, 0.5f), D("deco_swamp_1", 1, 0.003f, true, 1.8f, 0.6f),
                    D("deco_swamp_2", 1, 0.003f, true, 0.8f, 0.5f), D("deco_swamp_3", 1, 0.004f, flat: true)),
                "군체의 신전", new Vector2(8, 3.5f), 18, Ground.Swamp, "boss_shroom", "frog", "jelly", "shroom", "wisp");
        }
    }
}
