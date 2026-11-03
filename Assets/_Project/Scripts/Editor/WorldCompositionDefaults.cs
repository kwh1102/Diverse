using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>Explicit, repeatable install of the eight regional composition profiles. No source art or save changes.</summary>
    public static class WorldCompositionDefaults
    {
        public static readonly string[] Themes = { "overgrowth", "volcano", "crystal", "glacier", "canyon", "fortress", "mirror", "swamp" };

        [MenuItem("Diverse/World/Apply Regional Composition")]
        public static void Apply()
        {
            var asset = AssetDatabase.LoadAssetAtPath<WorldRulesData>("Assets/_Project/Data/WorldRules.asset");
            var r = asset.def;
            r.sceneryCell = 2; r.sceneryGap = .15f; r.sceneryRoadMargin = 1.3f; r.sceneryGroupCell = 44;
            r.roadExits = 3; r.roadBranchDistance = new Vector2(48, 72); r.roadSegmentLength = 12;
            r.roadTerrainAvoidance = 10; r.roadBranchWidth = 1.35f; r.roadLoopWidth = 1.05f; r.roadLoops = 2;
            r.roadWander = 9;
            r.explorationTrailConnectDistance = 48; r.explorationTrailNeighbourDistance = 96; r.explorationTrailBend = 4;
            foreach (string id in Themes)
            {
                var data = AssetDatabase.LoadAssetAtPath<ThemeData>("Assets/_Project/Data/Themes/" + id + ".asset");
                Configure(data.def); EditorUtility.SetDirty(data);
            }
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
            TileBaker.Reset(); Debug.Log("[WorldComposition] Installed all eight regions; meadow/town art and saved zone choices unchanged.");
        }

        public static void RunBatch()
        {
            try { Apply(); CompositionChecks.Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunBatchWorld()
        {
            try { Apply(); CompositionChecks.Run(); WorldOverview.RunBatch(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Diverse/World/Apply Cactus and Crystal Sizes")]
        public static void ApplyNaturalSizes()
        {
            foreach (string id in new[] { "canyon", "crystal" })
            {
                var data = AssetDatabase.LoadAssetAtPath<ThemeData>("Assets/_Project/Data/Themes/" + id + ".asset");
                ConfigureNaturalSizes(data.def); EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[NaturalSizes] Cacti/crystals: smaller common individuals, medium cluster accents and rare giants. Tree sizes and placement density preserved.");
        }

        public static void RunBatchNaturalSizes()
        {
            try { ApplyNaturalSizes(); CompositionChecks.Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void ConfigureNaturalSizes(ThemeDef theme)
        {
            foreach (var entry in theme.decor)
            {
                bool cactus = theme.id == "canyon" && (entry.sprite.StartsWith("tree_Dunes_") ||
                    entry.sprite is "deco_canyon_0" or "deco_canyon_2" or "explore_canyon_1");
                bool crystal = theme.id == "crystal" && (entry.sprite.StartsWith("tree_Crystal_") ||
                    entry.sprite.StartsWith("deco_crystal_") || entry.sprite == "explore_crystal_0");
                if (!cactus && !crystal) continue;
                // Explicit former size makes repeated installs idempotent and preserves the previous placement spacing.
                float former = entry.sprite.StartsWith("tree_") ? 2.05f : entry.sprite.StartsWith("deco_") ? 1.9f : cactus ? 1.6f : 2.1f;
                entry.spacingScale = former;
                entry.visualScale = former * (cactus ? .65f : .55f);
                entry.mediumScale = former * .8f; entry.rareScale = former;
                entry.mediumChance = .22f; entry.rareChance = .04f;
                entry.mediumPatchThreshold = .5f; entry.rarePatchThreshold = .75f;
            }
        }

        static void Configure(ThemeDef t)
        {
            var s = t.scenery = new SceneryProfile { enabled = true, groupName = t.id + " roadside place" };
            s.buildingScale = 1.5f; s.anchorScale = 1.5f; s.landmarkScale = 1.4f;
            s.patchSize = t.id is "overgrowth" or "swamp" ? 36 : 46;
            s.patchThreshold = t.id is "overgrowth" or "swamp" ? .43f : .5f;
            s.canopySpacing = t.id is "overgrowth" or "swamp" or "glacier" ? .5f : .6f;
            s.groundStrength = t.id is "fortress" or "mirror" ? .55f : .75f;
            t.density = t.id == "overgrowth" ? 1.35f : t.id == "swamp" ? 1.1f : .95f;
            foreach (var d in t.decor)
            {
                d.visualScale = 1.25f; d.scaleVariation = .12f;
                if (d.sprite.StartsWith("tree_"))
                {
                    d.role = SceneryRole.Canopy; d.visualScale = 2.05f; d.scaleVariation = .22f;
                    d.chance = t.id is "overgrowth" or "swamp" or "glacier" ? .22f : .12f;
                    d.width = 1.2f; d.depth = .8f;
                }
                else if (d.sprite.StartsWith("bush_")) { d.role = SceneryRole.Understory; d.chance = .055f; d.visualScale = 1.45f; }
                else if (d.sprite.StartsWith("rock_")) { d.role = SceneryRole.Boulder; d.chance = .025f; d.visualScale = 1.4f; }
                else if (d.sprite.StartsWith("tuft_")) d.role = SceneryRole.Understory;
                else { d.role = SceneryRole.Understory; d.chance = .025f; }
            }
            void D(string key, SceneryRole role, float scale, float chance = .04f)
            {
                var d = t.decor.Find(e => e.sprite == key);
                if (d == null) throw new InvalidOperationException("Missing regional object " + key);
                d.role = role; d.visualScale = scale; d.chance = role == SceneryRole.Supply ? 0 : chance;
                d.clustered = true;
            }
            void P(int i, SceneryRole role, float scale, float chance = .04f) => D("explore_" + t.id + "_" + i, role, scale, chance);
            void G(string key, float x, float y, float width = 1, float depth = .6f)
                => s.group.Add(new ExplorationPropDef { sprite = key, position = new Vector2(x, y), footprint = new Vector2(width, depth) });
            string E(int i) => "explore_" + t.id + "_" + i;
            switch (t.id)
            {
                case "overgrowth":
                    P(0, SceneryRole.Understory, 1.6f); P(1, SceneryRole.Canopy, 1.9f, .06f);
                    P(2, SceneryRole.Understory, 1.5f); P(3, SceneryRole.Understory, 1.4f);
                    s.groupName = "mossy fallen-log clearing";
                    G(E(2), -2.3f, 1, 1.6f); G(E(3), 2.1f, -.8f); G(E(0), -2, -2); G(E(1), 2.4f, 2);
                    break;
                case "volcano":
                    P(0, SceneryRole.Boulder, 1.65f); P(1, SceneryRole.Boulder, 1.5f);
                    P(2, SceneryRole.Supply, 1.25f); P(3, SceneryRole.Supply, 1.6f);
                    s.groupName = "abandoned ore workshop";
                    G(E(3), 0, 2, 1.5f); G(E(2), -2.8f, -.5f, 1.4f); G(E(1), 2.5f, -.5f); G("prop_crate", 2.4f, -2.4f);
                    break;
                case "crystal":
                    P(0, SceneryRole.Canopy, 2.1f, .08f); P(1, SceneryRole.Boulder, 1.6f);
                    P(2, SceneryRole.Supply, 1.25f); P(3, SceneryRole.Supply, 1.5f);
                    s.groupName = "crystal observation station";
                    G(E(0), 0, 2, 1.5f); G(E(2), -2.8f, -.6f); G(E(3), 2.5f, -.6f); G(E(1), 2.6f, 2.5f);
                    break;
                case "glacier":
                    P(0, SceneryRole.Boulder, 1.65f, .055f); P(1, SceneryRole.Supply, 1.2f);
                    P(2, SceneryRole.Understory, 1.45f); P(3, SceneryRole.Supply, 1.4f);
                    s.groupName = "snowbound supply camp";
                    G("prop_tent", 0, 2, 2.3f, 1.3f); G(E(3), -2.9f, -1, 1.4f);
                    G(E(1), 2.6f, -1); G(E(1), 2.6f, 1.2f); G(E(0), -3.2f, 2);
                    break;
                case "canyon":
                    P(0, SceneryRole.Supply, 1.3f); P(1, SceneryRole.Understory, 1.6f);
                    P(2, SceneryRole.Relic, 1.8f); P(3, SceneryRole.Supply, 1.35f);
                    s.groupName = "desert waymarker ruins";
                    G(E(3), 0, 2); G(E(2), -2.8f, 1, 1.2f); G(E(0), 2.4f, -.5f); G(E(1), -2.5f, -2);
                    break;
                case "fortress":
                    P(0, SceneryRole.Supply, 1.7f); P(1, SceneryRole.Supply, 1.6f);
                    P(2, SceneryRole.Supply, 1.4f); P(3, SceneryRole.Supply, 1.3f);
                    // Trees only grow on natural upper ground; the lowland is architectural.
                    foreach (var d in t.decor.Where(d => d.sprite.StartsWith("tree_") || d.sprite.StartsWith("bush_"))) d.onCliffs = true;
                    s.groupName = "old garrison supply yard";
                    G(E(0), -2.6f, 2, 1.8f); G(E(1), 2.7f, 2); G(E(2), -2.6f, -1); G(E(3), 2.7f, -1);
                    break;
                case "mirror":
                    P(0, SceneryRole.Supply, 1.65f); P(1, SceneryRole.Relic, 1.8f);
                    P(2, SceneryRole.Supply, 1.5f); P(3, SceneryRole.Relic, 1.4f);
                    foreach (var d in t.decor.Where(d => d.sprite.StartsWith("tree_") || d.sprite.StartsWith("bush_"))) d.onCliffs = true;
                    s.groupName = "broken mirror court";
                    G(E(0), 0, 2, 1.3f); G(E(1), -2.8f, 2); G(E(2), 2.7f, -1); G(E(3), -2.5f, -1.5f);
                    break;
                case "swamp":
                    P(0, SceneryRole.Understory, 1.5f); P(1, SceneryRole.Boulder, 1.7f);
                    P(2, SceneryRole.Understory, 1.55f); P(3, SceneryRole.Supply, 1.35f);
                    s.groupName = "reed-gatherers rest";
                    G(E(1), 0, 2, 1.4f); G(E(3), -2.7f, -.5f); G(E(0), 2.3f, -.8f); G(E(2), 2.8f, 2);
                    break;
            }
            // The older theme-specific artwork is classified too, not left on the old scatter rule.
            foreach (var d in t.decor.Where(d => d.sprite.StartsWith("deco_")))
            {
                bool large = Art.Decor(d.Key(0)).rect.height >= 30;
                d.role = t.id is "fortress" or "mirror" ? SceneryRole.Relic : large ? SceneryRole.Canopy : SceneryRole.Boulder;
                d.visualScale = large ? 1.9f : 1.3f; d.chance = large ? .055f : .025f; d.clustered = true;
            }
            ConfigureNaturalSizes(t);
        }
    }
}
