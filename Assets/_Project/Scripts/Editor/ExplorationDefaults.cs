using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>Editable place recipes live in WorldRules.asset. This explicit menu installs the initial layouts.</summary>
    public static class ExplorationDefaults
    {
        [MenuItem("Diverse/World/Apply Exploration Expansion")]
        public static void Apply()
        {
            var data = AssetDatabase.LoadAssetAtPath<WorldRulesData>("Assets/_Project/Data/WorldRules.asset");
            var r = data.def;
            r.explorationCell = 56; r.explorationJitter = 4; r.explorationClearance = 5;
            r.explorationTrailLength = 18; r.explorationTrailWidth = 1.1f;
            r.materialCell = 4; r.materialMinNeighbours = 3; r.exploration.Clear();
            Add(r, "meadow", ExplorationKind.Hamlet, "물레방앗간 마을", "바퀴는 멈췄지만 우물가에는 새 발자국이 남아 있다. 여행자들은 이 작은 마을을 보급 거점으로 쓰고 있다.");
            Add(r, "meadow", ExplorationKind.RuinedCourt, "들꽃 폐허", "기둥 사이로 들꽃이 자란다. 무너진 마당의 서쪽 구석에 오래된 여행 가방을 숨겼다는 기록이 남아 있다.");
            Add(r, "overgrowth", ExplorationKind.Jungle, "덩굴 미로", "커다란 잎과 쓰러진 고목 사이로 좁은 길이 이어진다. 길을 벗어난 빈터에는 약초를 채집하던 이의 흔적이 남아 있다.");
            Add(r, "overgrowth", ExplorationKind.Cavern, "뿌리 아래 동굴", "고목의 뿌리가 바위를 움켜쥐고 있다. 남쪽으로 열린 입구에서 들어가 왼쪽 벽을 따라가면 예전 탐험가의 은닉처에 닿는다.");
            Add(r, "volcano", ExplorationKind.Foundry, "식어버린 주조장", "광석 수레와 식어가는 화로가 버려져 있다. 작업장 양옆의 보급품을 살피면 마지막 작업자들이 떠난 이유를 알 수 있을 것이다.");
            Add(r, "volcano", ExplorationKind.Gorge, "잿빛 용암 협곡", "굳은 용암이 두 줄기의 높은 능선으로 남았다. 아래로 이어진 길 중간에는 양쪽 바위를 가로지르는 좁은 틈이 있다.");
            Add(r, "crystal", ExplorationKind.Observatory, "결정 관측소", "거대한 수정 렌즈가 별이 없는 낮에도 희미하게 빛난다. 렌즈를 지탱하던 장치와 기록판이 흩어져 있다.");
            Add(r, "crystal", ExplorationKind.Quarry, "수정 채석장", "층층이 파낸 돌벽 아래에 광맥의 흔적이 남아 있다. 작업 통로를 따라 들어가면 왼쪽 보관 구역을 살펴볼 수 있다.");
            Add(r, "glacier", ExplorationKind.Cavern, "푸른 얼음 동굴", "얼음에 갇힌 어두운 공간에서 오래된 바람이 흘러나온다. 열린 남쪽 입구 너머, 왼편 벽 아래에 작은 상자가 보인다.");
            Add(r, "glacier", ExplorationKind.FrozenCamp, "눈에 묻힌 탐사대", "썰매와 보급 상자가 눈에 묻혔다. 모닥불 자리와 천막 사이에는 탐사대가 다시 모이기로 한 표식이 남아 있다.");
            Add(r, "canyon", ExplorationKind.Gorge, "바람 깎인 협곡", "두 겹의 바위 벽 사이로 바람길이 열린다. 가운데 갈림길에서 서쪽 틈을 확인해 보자.");
            Add(r, "canyon", ExplorationKind.Oasis, "모래바람 쉼터", "작은 물웅덩이와 그늘을 중심으로 대상들이 쉬어 갔다. 물가를 한 바퀴 돌아 남서쪽의 보급품을 찾아보자.");
            Add(r, "fortress", ExplorationKind.RuinedCourt, "무너진 성문 지구", "성문은 무너졌지만 안뜰의 기둥과 보급품은 남아 있다. 탑 사이로 들어서면 옛 수비대의 기록을 읽을 수 있다.");
            Add(r, "fortress", ExplorationKind.Hamlet, "성벽 밖 보급 마을", "깃발 아래 모인 작은 집들은 먼 전쟁 뒤에도 여행자를 맞는다. 상인과 주민이 남은 물자를 나누며 살아간다.");
            Add(r, "mirror", ExplorationKind.Observatory, "깨진 거울 정원", "거울은 서로 다른 방향의 하늘을 비춘다. 깨진 기둥 주변을 돌아 기록판과 남겨진 물건을 조사해 보자.");
            Add(r, "mirror", ExplorationKind.Sanctuary, "잊힌 반사의 성소", "비어 있는 받침대들이 작은 광장을 둘러싼다. 성소 서쪽에 순례자들이 남겨 놓은 보물이 있다는 글귀가 새겨져 있다.");
            Add(r, "swamp", ExplorationKind.Wetland, "늪지 나무길", "습지를 가로지르는 나무길 끝에 속이 빈 고목이 서 있다. 판자길을 따라가며 물 밖의 작은 빈터를 살펴보자.");
            Add(r, "swamp", ExplorationKind.Jungle, "버섯 고목 군락", "거대한 뿌리와 버섯 사이로 채집꾼의 길이 구불구불 이어진다. 희귀 약초가 자라는 작은 공터가 있다.");
            EditorUtility.SetDirty(data);
            foreach (string id in WorldArtOverhaul.ExplorationThemes)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ThemeData>("Assets/_Project/Data/Themes/" + id + ".asset");
                asset.def.decor.RemoveAll(d => d.sprite.StartsWith("explore_"));
                for (int i = 3; i >= 0; i--)
                    asset.def.decor.Insert(0, new DecorEntry
                    {
                        sprite = $"explore_{id}_{i}", chance = .012f, block = true, width = i == 2 ? 1.2f : .8f,
                        depth = .5f, onCliffs = i < 2, clustered = i == 0,
                    });
                EditorUtility.SetDirty(asset);
            }
            var art = AssetDatabase.LoadAssetAtPath<ArtStyleData>("Assets/_Project/Data/ArtStyle.asset");
            art.def.heroBlinkInterval = 4.8f; art.def.heroBlinkDuration = .08f; EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets(); WorldArtOverhaul.ApplyExploration();
            Debug.Log("[Exploration] Installed 18 themed place recipes, 36 regional props, rare brief blinking and matched chest states.");
        }

        static void Add(WorldRulesDef rules, string theme, ExplorationKind kind, string name, string story)
        {
            var d = new ExplorationSiteDef { id = theme + "_" + kind, theme = theme, kind = kind, displayName = name, story = story };
            GroundShapes(d);
            rules.exploration.Add(d);
            string anchor = "explore_anchor_" + theme;
            string P(int i) => $"explore_{theme}_{i}";
            void Prop(string sprite, float x, float y, float width = 1, float depth = .6f, bool block = true, ExplorationRole role = ExplorationRole.Decor)
                => d.props.Add(new ExplorationPropDef { sprite = sprite, position = new Vector2(x, y), footprint = new Vector2(width, depth), blocks = block, role = role });
            void Record(float x = 0, float y = -7) => Prop("prop_board", x, y, .8f, .4f, false, ExplorationRole.Record);
            void Cache(float x = -6, float y = 0) => Prop("prop_chest", x, y, 1, .5f, false, ExplorationRole.Cache);
            void EdgeProps()
            {
                Prop(P(0), -11, -5); Prop(P(1), 11, -5); Prop(P(2), -11, 2); Prop(P(3), 11, 3);
            }
            switch (kind)
            {
                case ExplorationKind.Hamlet:
                    Prop("house_0_c2603e", -7, 3, 3.6f, 1.8f); Prop("house_1_4f7bd9", 7, 3, 3.6f, 1.8f);
                    Prop(anchor, -7, -6, 4.5f, 1.8f);
                    Prop("prop_well", 0, 4, 1.3f, .8f); Prop("prop_stall", 7, -6, 2.5f, 1);
                    Prop("npc_cat_0", 6, -7.5f, .7f, .4f, false, ExplorationRole.Merchant);
                    Prop("npc_villager0_0", -5, -.5f, .7f, .4f, false, ExplorationRole.Traveler);
                    Prop(P(2), -10.5f, -.5f); Prop(P(3), 10.5f, -.5f); Prop(P(0), -2.5f, 6.5f);
                    Prop("prop_lamp", -2.5f, -4); Prop("prop_lamp", 2.5f, -4);
                    Prop("prop_barrel", -10.5f, -5); Prop("prop_crate", 10.5f, 4); Prop(P(1), -11, 5);
                    Record(0, -8); Cache(10, -8); break;
                case ExplorationKind.RuinedCourt:
                    Prop(anchor, 0, 5, 5, 1.6f);
                    foreach (float x in new[] { -7f, 7f }) foreach (float y in new[] { -4f, 1f, 5f })
                        Prop(y == 1 ? "prop_pillar_broken" : "prop_pillar", x, y, .8f, .6f);
                    Prop("prop_wall", -3, -5, 2, .5f); Prop("prop_wall", 4, -5, 2, .5f);
                    EdgeProps(); Record(); Cache(-5, 1); break;
                case ExplorationKind.Cavern:
                    Prop(anchor, 0, 4, 4.5f, 1.3f);
                    Prop(P(0), -7, 3); Prop(P(1), 7, 3); Prop(P(2), 6, -3); Prop(P(3), -7, -4);
                    Prop(P(0), -5, 3); Prop(P(2), 5, 1); Prop(P(1), -8, -7); Prop(P(3), 8, -7);
                    Prop("prop_lamp", -3, -6); Prop("prop_lamp", 3, -6); Record(0, -5); Cache(-6, 0); break;
                case ExplorationKind.Jungle:
                    Prop(anchor, 0, 5, 5, 1.6f);
                    foreach (var at in new[] { new Vector2(-10, 5), new Vector2(10, 5), new Vector2(-10, -1), new Vector2(9, -1),
                        new Vector2(-11, -6), new Vector2(10, -6), new Vector2(-4, -7), new Vector2(4, -7), new Vector2(5, 3) })
                        Prop("tree_" + (theme == "swamp" ? "Marsh" : "Forest") + "_0", at.x, at.y, 1.2f, .8f);
                    Prop(P(0), -5, -1); Prop(P(1), 5, -1); Prop(P(2), -6, 4); Prop(P(3), 6, 4);
                    Prop(P(0), -5, -7); Prop(P(3), 5, -7); Record(0, -7); Cache(-6, -4);
                    Prop(P(0), 6, -4, .6f, .4f, false, ExplorationRole.Herb); break;
                case ExplorationKind.Gorge:
                    Prop(anchor, 0, 6, 4.5f, 1.3f);
                    Prop(P(3), -7, -5); Prop(P(1), 7, -5); Prop(P(2), -7, 4); Prop(P(0), 7, 4);
                    Record(0, -7); Cache(-7, -1); break;
                case ExplorationKind.Quarry:
                    Prop(anchor, 0, 4, 4.5f, 1.3f); EdgeProps();
                    Prop(P(0), -6, 4); Prop(P(1), 6, 4); Prop(P(2), 5, -4); Prop("prop_crate", -5, -4);
                    Record(); Cache(-6, 0); break;
                case ExplorationKind.Oasis:
                    Prop("prop_tent", 6, 3, 2.2f, 1.2f); Prop("prop_tent", 6, -5, 2.2f, 1.2f);
                    Prop(P(1), -10, 5); Prop(P(0), -10, -2); Prop(P(3), 2, 5); Prop("prop_barrel", 9, -2);
                    Prop("npc_villager3_0", 3, -3, .6f, .4f, false, ExplorationRole.Traveler);
                    Record(0, -7); Cache(-8, -5); break;
                case ExplorationKind.FrozenCamp:
                    Prop(anchor, 0, 5, 4.5f, 1.3f); Prop("prop_tent", -7, 2, 2.2f, 1.2f);
                    Prop("prop_tent", 7, 2, 2.2f, 1.2f); Prop("prop_campfire", 0, -1, 0, 0, false);
                    Prop(P(3), -7, -5, 1.4f, .6f); Prop(P(1), 7, -5); EdgeProps();
                    Record(0, -7); Cache(5, -3); break;
                case ExplorationKind.Foundry:
                    Prop(anchor, 0, 4, 4.5f, 1.6f);
                    Prop(P(3), -7, 4, 1.4f, .8f); Prop(P(3), 7, 4, 1.4f, .8f);
                    Prop(P(2), -7, -4); Prop("prop_crate", 7, -4); Prop(P(1), -10, 0); Prop(P(0), 10, 0);
                    Record(); Cache(5, 0); break;
                case ExplorationKind.Observatory:
                case ExplorationKind.Sanctuary:
                    Prop(anchor, 0, 5, 4.5f, 1.4f);
                    Prop(P(1), -6, 4); Prop(P(1), 6, 4); Prop(P(2), -6, -4); Prop(P(2), 6, -4);
                    Prop(P(0), -10, -2); Prop(P(3), 10, -2); Record(); Cache(-6, 0); break;
                case ExplorationKind.Wetland:
                    Prop(anchor, 0, 5, 4.5f, 1.4f); EdgeProps();
                    Prop(P(2), 6, 1); Prop(P(2), 8, -4); Prop(P(0), -6, 3); Prop(P(1), -8, -3);
                    Record(0, -6); Cache(-6, -6); break;
            }
        }

        static void GroundShapes(ExplorationSiteDef d)
        {
            void Layer(float x, float y, float w, float h, Ground ground = Ground.Grass,
                ExplorationMaterial material = ExplorationMaterial.Fixed, ExplorationShape shape = ExplorationShape.Rectangle)
                => d.grounds.Add(new ExplorationGroundDef { center = new Vector2(x, y), halfSize = new Vector2(w, h), ground = ground,
                    material = material, shape = shape, cornerCut = ground is Ground.Cliff or Ground.Peak ? 1.5f : 0 });
            void Road(float x, float y, float w, float h) => Layer(x, y, w, h, material: ExplorationMaterial.Road);
            switch (d.kind)
            {
                case ExplorationKind.Hamlet: Road(0, 0, 2, 11); Road(0, -1, 14, 1.5f); break;
                case ExplorationKind.RuinedCourt: Layer(0, 0, 10, 7, Ground.Flagstone); break;
                case ExplorationKind.Cavern:
                    Layer(0, 0, 14, 11, d.theme == "glacier" ? Ground.Ice : Ground.Ash);
                    Layer(0, 8, 14, 3, Ground.Cliff);
                    Layer(-11.5f, 3, 2.5f, 8, Ground.Cliff); Layer(11.5f, 3, 2.5f, 8, Ground.Cliff);
                    Layer(0, 9.5f, 14, 1.5f, Ground.Peak);
                    Layer(-13, 3, 1, 8, Ground.Peak); Layer(13, 3, 1, 8, Ground.Peak);
                    Layer(-6, -4, 2.5f, 2.5f, Ground.Cliff); Layer(6, -4, 2.5f, 2.5f, Ground.Cliff); break;
                case ExplorationKind.Gorge:
                    Road(0, 0, 14, 11); Layer(-9, 0, 5, 8, Ground.Cliff); Layer(9, 0, 5, 8, Ground.Cliff);
                    Layer(-12, 0, 2, 8, Ground.Peak); Layer(12, 0, 2, 8, Ground.Peak);
                    Road(0, -1, 14, 1.8f); break;
                case ExplorationKind.Jungle:
                    Layer(0, 0, 14, 11, Ground.DarkGrass);
                    Road(0, -7, 1.3f, 4); Road(-3, -3, 4.3f, 1.3f); Road(-6, 0, 1.3f, 3.1f);
                    Road(-3, 3, 4.3f, 1.3f); Road(0, 5, 1.3f, 2.1f); Road(3, -3, 3.1f, 1.1f); break;
                case ExplorationKind.Sanctuary: Layer(0, 0, 7, 7, Ground.Plaza, shape: ExplorationShape.Ellipse); break;
                case ExplorationKind.Quarry:
                    Layer(0, 0, 14, 11, Ground.Flagstone); Road(0, 0, 2, 11);
                    Layer(0, 9, 14, 2, Ground.Cliff); Layer(-12, 3.5f, 2, 7.5f, Ground.Cliff); Layer(12, 3.5f, 2, 7.5f, Ground.Cliff); break;
                case ExplorationKind.Oasis: Layer(-6, 3, 3, 3, material: ExplorationMaterial.Liquid, shape: ExplorationShape.Ellipse); break;
                case ExplorationKind.FrozenCamp: Layer(0, 0, 2, 11, Ground.Path); Layer(0, 0, 14, 2, Ground.Path); break;
                case ExplorationKind.Foundry: Layer(0, .5f, 10, 6.5f, Ground.Flagstone); break;
                case ExplorationKind.Observatory: Layer(0, 0, 13, 13, material: ExplorationMaterial.Arena, shape: ExplorationShape.Diamond); break;
                case ExplorationKind.Wetland:
                    Layer(7, -1, 3, 5, material: ExplorationMaterial.Liquid);
                    Layer(0, -2, 14, 1.5f, Ground.Wood); Layer(0, 0, 1.5f, 11, Ground.Wood); break;
            }
        }

        public static void RunBatch()
        {
            try { Apply(); ExplorationChecks.Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
