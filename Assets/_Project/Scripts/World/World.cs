using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// World manager: turns structure specs (StructureSpec) into real objects and applies the world memory (WorldSave).
    /// - Cleared camp → no enemies spawn; instead a camp of the wanderer who cleared it remains
    /// - Opened chest → shown as an open chest
    /// - Previous character's grave / hero statue → placed at its saved position
    /// </summary>
    public class World : MonoBehaviour
    {
        public static World I { get; private set; }
        public WorldStreamer Streamer;
        public WorldGen Gen => Streamer.Gen;
        public WorldSave Save => Game.I.World;
        readonly Dictionary<string, int> campAlive = new Dictionary<string, int>();
        readonly HashSet<string> builtGraves = new HashSet<string>();

        public static World Create(int seed)
        {
            var go = new GameObject("WorldManager");
            I = go.AddComponent<World>();
            I.Streamer = WorldStreamer.Create(seed);
            return I;
        }

        public void ResetForRun()
        {
            campAlive.Clear();
            builtGraves.Clear();
            Streamer.Clear();
            foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) Destroy(e.gameObject);
            foreach (var s in FindObjectsByType<Summon>(FindObjectsSortMode.None)) Destroy(s.gameObject);
            foreach (var c in FindObjectsByType<Clone>(FindObjectsSortMode.None)) Destroy(c.gameObject);
            foreach (var pr in FindObjectsByType<Projectile>(FindObjectsSortMode.None)) Destroy(pr.gameObject);
            foreach (var pk in FindObjectsByType<Pickup>(FindObjectsSortMode.None)) Destroy(pk.gameObject);
            Interactable.All.Clear();
        }

        public void OnChunkBuilt(ChunkView c)
        {
            // Graves/statues in this chunk
            foreach (var g in Save.graves)
            {
                var k = WorldStreamer.ChunkOf(new Vector2(g.x, g.y));
                if (k != c.key) continue;
                var pos = new Vector2(g.x, g.y);
                if (g.statue)
                {
                    var it = Interactable.Create(c.transform, "statue", "statue_" + g.life, $"{g.heroName}의 석상", pos, Art.Prop("statue"), c);
                    it.data = g;
                    c.BlockArea(pos + Vector2.up * 0.3f, 1.4f, 0.8f);
                }
                else
                {
                    var it = Interactable.Create(c.transform, "grave", "grave_" + g.life, $"{g.heroName}의 무덤", pos, Art.Prop("grave"), c);
                    it.data = g;
                    if (!g.recovered) it.sr.color = new Color(1f, 0.95f, 1f);
                }
            }
            string ck = c.key.x + "," + c.key.y;
            if (!Save.exploredChunks.Contains(ck)) Save.exploredChunks.Add(ck);
        }

        // ───────────────────── Structure building ─────────────────────

        public void BuildStructure(ChunkView c, StructureSpec s)
        {
            switch (s.kind)
            {
                case StructKind.Town: BuildTown(c); break;
                case StructKind.Tower: BuildTower(c, s); break;
                case StructKind.Camp: BuildCamp(c, s); break;
                case StructKind.Ruins: BuildRuins(c, s); break;
                case StructKind.Shrine: BuildShrine(c, s); break;
                case StructKind.Chest: BuildChest(c, s); break;
                case StructKind.Grove: BuildGrove(c, s); break;
                case StructKind.Pond: break; // the ground already has water
                case StructKind.Wanderer: BuildWanderer(c, s); break;
                case StructKind.Graveyard: BuildGraveyard(c, s); break;
                case StructKind.Lair: BuildLair(c, s); break;
                case StructKind.Ferry: BuildFerry(c, s); break;
                case StructKind.Obelisk: BuildObelisk(c, s); break;
                case StructKind.Fortress: BuildFortress(c, s); break;
            }
        }

        void BuildFerry(ChunkView c, StructureSpec s)
        {
            var it = Interactable.Create(c.transform, "ferry", "ferry:" + s.id, "뱃사공 수달", s.center, Art.Npc("otter", 0), c);
            it.idleFrames = new[] { Art.Npc("otter", 0), Art.Npc("otter", 1) };
            it.data = s;
            c.AddDecor(Art.Prop("raft"), s.center + new Vector2(1.6f, -0.6f), false, 0, 0, true);
            c.AddDecor(Art.Prop("barrel"), s.center + new Vector2(-1.4f, 0.4f), true, 0.6f, 0.4f);
        }

        void BuildObelisk(ChunkView c, StructureSpec s)
        {
            c.AddDecor(Art.Prop("obelisk"), s.center + new Vector2(0, 1.5f), true, 1.2f, 0.6f, shape: Footprint.Rect);
            var glow = new GameObject("obeliskGlow");
            glow.transform.SetParent(c.transform, false);
            glow.transform.position = s.center + new Vector2(0, 4.4f);
            var gsr = Art.MakeRenderer(glow, Art.Glow(16), 31000, true);
            gsr.color = Save.clearedCamps.Contains(s.id) ? new Color(0.6f, 1f, 0.7f, 0.4f) : new Color(1f, 0.45f, 0.55f, 0.45f);
            var rng = new Rng(Hash.Str(s.id));
            for (int i = 0; i < 5; i++)
                c.AddDecor(Art.Prop("pillar_broken"), s.center + MathX.Dir(i * 72 + rng.Range(-10, 10)) * 5.5f, true, 0.75f, 0.45f);
            if (Save.clearedCamps.Contains(s.id)) return;
            SpawnGroup(c, s, s.enemy, s.enemyCount, s.radius * 0.5f);
            SpawnBoss(c, s, s.boss, s.center + Vector2.down * 1.5f, 0.5f);
        }

        void BuildFortress(ChunkView c, StructureSpec s)
        {
            // A ring of walls with a gap facing the origin (the way the hero comes from)
            float gate = (-s.center).Angle();
            for (int a = 0; a < 360; a += 12)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(a, gate)) < 20) continue;
                var p = s.center + MathX.Dir(a) * (s.radius - 1.5f);
                c.AddDecor(Art.Prop("wall"), p, true, 2.1f, 0.7f, shape: Footprint.Rect);
            }
            c.AddDecor(Art.Prop("banner"), s.center + new Vector2(-2.5f, 3), true, 0.4f, 0.3f);
            c.AddDecor(Art.Prop("banner"), s.center + new Vector2(2.5f, 3), true, 0.4f, 0.3f);
            if (Save.clearedCamps.Contains(s.id))
            {
                var chest = Interactable.Create(c.transform, "chest", "chest:" + s.id, Save.Flag("chest:" + s.id) ? "빈 상자" : "요새의 보물", s.center, Art.Prop(Save.Flag("chest:" + s.id) ? "chest_open" : "chest"), c);
                chest.data = s;
                return;
            }
            SpawnGroup(c, s, s.enemy, s.enemyCount, s.radius * 0.55f, 0.35f);
            SpawnBoss(c, s, s.boss, s.center + Vector2.up * 1.5f, 1f);
        }

        void SpawnBoss(ChunkView c, StructureSpec s, string id, Vector2 at, float extraTier)
        {
            var boss = Enemy.Spawn(DB.ScaledEnemy(id, s.tier + extraTier + Save.towerFloor * 0.5f), Streamer.NearestWalkable(at, 0.8f), s.id);
            c.Register(boss);
            campAlive[s.id] = campAlive.TryGetValue(s.id, out var n) ? n + 1 : 1;
        }

        void House(ChunkView c, Vector2 pos, int v, Color32 roof, string kind, string label)
        {
            c.AddDecor(Art.House(v, roof), pos, false, 0, 0, false, "house");
            c.BlockArea(pos + new Vector2(0, 1.9f), 3.6f, 3.2f);
            if (kind != null)
            {
                var door = Interactable.Create(c.transform, kind, kind, label, pos + new Vector2(0, -0.3f), Art.Pixel, c);
                door.sr.enabled = false;
                door.range = 1.6f;
            }
        }

        void BuildTown(ChunkView c)
        {
            // Plaza center: well + storyteller
            c.AddDecor(Art.Prop("well"), new Vector2(0, 1.2f), false, 0, 0);
            c.BlockArea(new Vector2(0, 1.6f), 1.6f, 1.0f, Footprint.Ellipse);
            var owl = Interactable.Create(c.transform, "npc_storyteller", "storyteller", "이야기꾼 올리", new Vector2(-2.5f, 0.3f), Art.Npc("owl", 0), c);
            owl.idleFrames = new[] { Art.Npc("owl", 0), Art.Npc("owl", 1) };
            var board = Interactable.Create(c.transform, "board", "board", "의뢰 게시판", new Vector2(3f, 1.8f), Art.Prop("board"), c);
            c.BlockArea(new Vector2(3f, 2.1f), 1.5f, 0.6f);

            // Buildings
            House(c, new Vector2(-8.5f, 5.5f), 0, Pal.Hex("c2603e"), "inn", "바람결 여관");
            House(c, new Vector2(8.5f, 5.5f), 1, Pal.Hex("4f7bd9"), null, null);
            House(c, new Vector2(-8.5f, -8f), 1, Pal.Hex("6aa36a"), null, null);
            House(c, new Vector2(8.5f, -8f), 0, Pal.Hex("8c5bd6"), "bank", "기억의 금고");

            // Smithy & shop
            c.AddDecor(Art.Prop("anvil"), new Vector2(5.5f, -2.5f), true, 1, 0.6f);
            var bear = Interactable.Create(c.transform, "npc_smith", "smith", "대장장이 브루노", new Vector2(6.8f, -2.2f), Art.Npc("bear", 0), c);
            bear.idleFrames = new[] { Art.Npc("bear", 0), Art.Npc("bear", 1) };
            c.AddDecor(Art.Prop("stall"), new Vector2(-5.5f, -2.5f), false, 0, 0);
            c.BlockArea(new Vector2(-5.5f, -2.2f), 2.0f, 0.8f);
            var cat = Interactable.Create(c.transform, "npc_merchant", "merchant", "상인 냥냥", new Vector2(-5.5f, -3.6f), Art.Npc("cat", 0), c);
            cat.idleFrames = new[] { Art.Npc("cat", 0), Art.Npc("cat", 1) };

            // Residents
            for (int i = 0; i < 3; i++)
            {
                var pos = new Vector2(-3 + i * 3.5f, -6f + (i % 2) * 1.2f);
                var v = Interactable.Create(c.transform, "npc_villager", "villager" + i, "주민", pos, Art.Npc("villager" + i, 0), c);
                v.idleFrames = new[] { Art.Npc("villager" + i, 0), Art.Npc("villager" + i, 1) };
                v.data = i;
            }

            // Lamps, fences, flower beds
            foreach (var lp in new[] { new Vector2(-4, 4), new Vector2(4, 4), new Vector2(-4, -4.5f), new Vector2(4, -4.5f) })
            {
                c.AddDecor(Art.Prop("lamp"), lp, true, 0.6f, 0.4f);
                var glow = new GameObject("glow");
                glow.transform.SetParent(c.transform, false);
                glow.transform.position = lp + new Vector2(0, 1.6f);
                var gsr = Art.MakeRenderer(glow, Art.Glow(14), 31000, true);
                gsr.color = new Color(1f, 0.85f, 0.5f, 0.35f);
            }
            for (int i = -13; i <= 13; i += 1)
            {
                if (Mathf.Abs(i) < 3) continue;
                c.AddDecor(Art.Prop("fence"), new Vector2(i, 13.2f), true, 1, 0.3f, shape: Footprint.Rect);
                c.AddDecor(Art.Prop("fence"), new Vector2(i, -13.2f), true, 1, 0.3f, shape: Footprint.Rect);
            }
            for (int i = 0; i < 18; i++)
            {
                float a = i * 20 * Mathf.Deg2Rad;
                var fp = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6.2f;
                if (Mathf.Abs(fp.x) < 2.6f || Mathf.Abs(fp.y) < 2.6f) continue;
                c.AddDecor(Art.Flower(i), fp, false, 0, 0, true);
            }
            c.AddDecor(Art.Prop("barrel"), new Vector2(-6.2f, 3.2f), true, 0.6f, 0.4f);
            c.AddDecor(Art.Prop("crate"), new Vector2(-5.3f, 3.0f), true, 0.75f, 0.45f, shape: Footprint.Rect);
            c.AddDecor(Art.Prop("crate"), new Vector2(10.5f, 2.8f), true, 0.75f, 0.45f, shape: Footprint.Rect);
            Discover("town");
        }

        void BuildTower(ChunkView c, StructureSpec s)
        {
            c.AddDecor(Art.Tower(), s.center, false, 0, 0, false, "tower");
            c.BlockArea(s.center + new Vector2(0, 2.4f), 3.6f, 3.2f);
            var gate = Interactable.Create(c.transform, "tower_gate", "tower", "기억의 탑", s.center + new Vector2(0, -0.6f), Art.Prop("gate"), c);
            gate.range = 2f;
            for (int i = 0; i < 6; i++)
            {
                var p = s.center + MathX.Dir(i * 60 + 30) * 6;
                c.AddDecor(Art.Prop(i % 2 == 0 ? "pillar" : "pillar_broken"), p, true, 0.75f, 0.45f);
            }
            var glow = new GameObject("towerGlow");
            glow.transform.SetParent(c.transform, false);
            glow.transform.position = s.center + new Vector2(0, 11.6f);
            var gsr = Art.MakeRenderer(glow, Art.Glow(30), 31000, true);
            gsr.color = new Color(0.8f, 0.7f, 1f, 0.5f);
        }

        void BuildCamp(ChunkView c, StructureSpec s)
        {
            bool cleared = Save.clearedCamps.Contains(s.id);
            c.AddDecor(Art.Prop("tent"), s.center + new Vector2(-2.2f, 1.8f), true, 2.3f, 1.3f, shape: Footprint.Tent);
            c.AddDecor(Art.Prop("tent"), s.center + new Vector2(2.4f, 1.4f), true, 2.3f, 1.3f, shape: Footprint.Tent);
            c.AddDecor(Art.Prop("campfire"), s.center, false, 0, 0);
            c.AddDecor(Art.Prop("crate"), s.center + new Vector2(3.6f, -1.8f), true, 0.75f, 0.45f, shape: Footprint.Rect);
            c.AddDecor(Art.Prop("barrel"), s.center + new Vector2(-3.5f, -1.5f), true, 0.6f, 0.4f);
            if (!cleared)
            {
                AddFire(c, s.center);
                SpawnGroup(c, s, s.enemy, s.enemyCount, s.radius * 0.6f);
                // Captured traveler (rescue)
                if (Hash.F((int)s.center.x, (int)s.center.y, Gen.seed) > 0.55f && !Save.Flag("rescued:" + s.id))
                {
                    var cage = Interactable.Create(c.transform, "cage", "cage:" + s.id, "붙잡힌 여행자", s.center + new Vector2(0, -3f), Art.Prop("cage"), c);
                    cage.data = s;
                    var who = new GameObject("prisoner");
                    who.transform.SetParent(cage.transform, false);
                    who.transform.localPosition = new Vector3(0, 0.05f, 0);
                    Art.MakeRenderer(who, Art.Npc("villager9", 0), Art.SortY(s.center.y - 3f) - 1);
                }
            }
            else
            {
                // A camp the wanderer cleared becomes a place where travelers rest
                var v = Interactable.Create(c.transform, "npc_villager", "camp_traveler:" + s.id, "여행자", s.center + new Vector2(1, -1), Art.Npc("villager7", 0), c);
                v.idleFrames = new[] { Art.Npc("villager7", 0), Art.Npc("villager7", 1) };
                v.data = 99;
                AddFire(c, s.center);
            }
        }

        void AddFire(ChunkView c, Vector2 p)
        {
            var go = new GameObject("fire");
            go.transform.SetParent(c.transform, false);
            go.transform.position = p + new Vector2(0, 0.15f);
            go.AddComponent<FireFx>();
        }

        void BuildRuins(ChunkView c, StructureSpec s)
        {
            var rng = new Rng(Hash.Str(s.id));
            for (int i = 0; i < 7; i++)
            {
                var p = s.center + MathX.Dir(i * 51 + rng.Range(0, 20)) * rng.Range(3f, 7f);
                c.AddDecor(Art.Prop(rng.Chance(0.5f) ? "pillar_broken" : "pillar"), p, true, 0.75f, 0.45f);
            }
            if (!Save.Flag("ruin:" + s.id))
            {
                var alt = Interactable.Create(c.transform, "ruin_altar", "ruin:" + s.id, "고대 제단", s.center, Art.Prop("altar"), c);
                alt.data = s;
                c.BlockArea(s.center + Vector2.up * 0.3f, 1.6f, 0.6f);
            }
            if (!Save.clearedCamps.Contains(s.id)) SpawnGroup(c, s, s.enemy, s.enemyCount, s.radius * 0.7f);
        }

        void BuildShrine(ChunkView c, StructureSpec s)
        {
            bool used = Save.Flag("shrine:" + s.id);
            var it = Interactable.Create(c.transform, "shrine", "shrine:" + s.id, used ? "고요한 제단" : "기억의 제단", s.center, Art.Prop("shrine"), c);
            it.data = s;
            if (used) it.sr.color = new Color(0.7f, 0.7f, 0.75f);
            c.BlockArea(s.center + Vector2.up * 0.3f, 1.2f, 0.6f);
            for (int i = 0; i < 4; i++) c.AddDecor(Art.Prop("pillar"), s.center + MathX.Dir(45 + i * 90) * 2.6f, true, 0.75f, 0.45f);
        }

        void BuildChest(ChunkView c, StructureSpec s)
        {
            bool opened = Save.Flag("chest:" + s.id);
            var it = Interactable.Create(c.transform, "chest", "chest:" + s.id, opened ? "빈 상자" : "상자", s.center, Art.Prop(opened ? "chest_open" : "chest"), c);
            it.data = s;
            if (!opened && s.enemyCount > 0 && !Save.clearedCamps.Contains(s.id)) SpawnGroup(c, s, s.enemy, s.enemyCount, 2.5f);
        }

        void BuildGrove(ChunkView c, StructureSpec s)
        {
            var rng = new Rng(Hash.Str(s.id));
            for (int i = 0; i < 9; i++)
            {
                var p = s.center + MathX.Dir(i * 40) * rng.Range(4f, 6.5f);
                c.AddDecor(Art.Tree(Biome.Meadow, 2), p, true, 1.0f, 0.6f);
            }
            for (int i = 0; i < 14; i++) c.AddDecor(Art.Flower(i), s.center + Random.insideUnitCircle * 3.5f, false, 0, 0, true);
            if (!Save.Flag("grove:" + s.id))
            {
                var it = Interactable.Create(c.transform, "herb", "grove:" + s.id, "빛나는 약초", s.center, Art.Bush(Biome.Meadow, 0), c);
                it.data = s;
                var glow = new GameObject("glow");
                glow.transform.SetParent(it.transform, false);
                glow.transform.localPosition = new Vector3(0, 0.3f, 0);
                var g = Art.MakeRenderer(glow, Art.Glow(12), 31000, true);
                g.color = new Color(0.6f, 1f, 0.6f, 0.4f);
            }
        }

        void BuildWanderer(ChunkView c, StructureSpec s)
        {
            if (Save.Flag("wanderer:" + s.id)) return;
            var it = Interactable.Create(c.transform, "wanderer", "wanderer:" + s.id, "길 잃은 방랑자", s.center, Art.Npc("villager" + (Hash.U(1, 2, (int)s.center.x) % 6), 0), c);
            it.data = s;
            c.AddDecor(Art.Prop("campfire"), s.center + new Vector2(1.2f, -0.3f), false, 0, 0);
            AddFire(c, s.center + new Vector2(1.2f, -0.3f));
        }

        void BuildGraveyard(ChunkView c, StructureSpec s)
        {
            var rng = new Rng(Hash.Str(s.id));
            for (int i = 0; i < 8; i++)
            {
                var p = s.center + new Vector2((i % 4 - 1.5f) * 2.2f, (i / 4 - 0.5f) * 2.6f) + new Vector2(rng.Range(-0.3f, 0.3f), 0);
                c.AddDecor(Art.Prop("grave"), p, true, 0.7f, 0.35f, shape: Footprint.Rect);
            }
            c.AddDecor(Art.Tree(Biome.Ashland, 0), s.center + new Vector2(-5, 3), true, 0.6f, 0.4f);
            if (!Save.clearedCamps.Contains(s.id)) SpawnGroup(c, s, "wisp", s.enemyCount, s.radius * 0.7f);
        }

        void BuildLair(ChunkView c, StructureSpec s)
        {
            var rng = new Rng(Hash.Str(s.id));
            for (int i = 0; i < 8; i++)
            {
                var p = s.center + MathX.Dir(i * 45 + rng.Range(-10, 10)) * rng.Range(7f, 9f);
                c.AddDecor(Art.Rock(Biome.Ashland, i % 3), p, true, 1.0f, 0.55f);
            }
            if (Save.clearedCamps.Contains(s.id))
            {
                var chest = Interactable.Create(c.transform, "chest", "chest:" + s.id, Save.Flag("chest:" + s.id) ? "빈 상자" : "보스의 보물", s.center, Art.Prop(Save.Flag("chest:" + s.id) ? "chest_open" : "chest"), c);
                chest.data = s;
                return;
            }
            SpawnGroup(c, s, s.enemy, s.enemyCount, s.radius * 0.6f);
            SpawnBoss(c, s, s.boss, s.center, 0);
        }

        /// <param name="eliteChance">extra chance of an elite; elites also appear more often the farther out the group is</param>
        public void SpawnGroup(ChunkView c, StructureSpec s, string enemyId, int count, float spread, float eliteChance = 0)
        {
            if (count <= 0) return;
            var rng = new Rng(Hash.Str(s.id + Save.lifeCount));
            int alive = 0;
            int ring = WorldGen.RingAt(s.center.magnitude);
            eliteChance += ring * 0.08f;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = s.center + MathX.Dir(rng.Range(0f, 360f)) * rng.Range(1.5f, spread + 1.5f);
                p = Streamer.NearestWalkable(p, 0.4f);
                // Mixed groups: occasionally a different species
                string id = rng.Chance(0.2f) ? (s.biome == Biome.Forest ? "bat" : "jelly") : enemyId;
                var def = DB.ScaledEnemy(id, s.tier + Game.I.World.towerFloor * 0.5f);
                if (ring > 0 && rng.Chance(eliteChance)) def = DB.Elite(def);
                var e = Enemy.Spawn(def, p, s.id);
                c.Register(e);
                alive++;
            }
            campAlive[s.id] = (campAlive.TryGetValue(s.id, out var n) ? n : 0) + alive;
        }

        public void OnCampEnemyKilled(string campId)
        {
            if (string.IsNullOrEmpty(campId) || !campAlive.ContainsKey(campId)) return;
            campAlive[campId]--;
            if (campAlive[campId] > 0) return;
            campAlive.Remove(campId);
            if (Save.clearedCamps.Contains(campId)) return;
            Save.clearedCamps.Add(campId);
            StructureSpec spec = null;
            foreach (var s in Gen.StructuresNear(Player.I.Pos, 30)) if (s.id == campId) spec = s;
            string name = spec?.name ?? "몬스터 무리";
            GameEvents.Notify($"{name} 소탕! 세계가 이 일을 기억할 것이다.");
            Save.Log($"{Ko.I(Game.I.HeroName)} {name}에서 몬스터를 몰아냈다.", "deed");
            Game.I.OnCampCleared(campId, spec);
            Sfx.Play("levelup", 0.6f);
        }

        public void Discover(string id)
        {
            if (Save.discovered.Contains(id)) return;
            Save.discovered.Add(id);
        }

        /// <summary>Discover structures near the player (shown on the map, recorded as exploration).</summary>
        public void UpdateDiscovery(Vector2 p)
        {
            foreach (var s in Gen.StructuresNear(p, 9))
            {
                if (!s.landmark || Save.discovered.Contains(s.id)) continue;
                if (Vector2.Distance(s.center, p) > s.radius + 6) continue;
                Discover(s.id);
                GameEvents.Notify($"발견: {s.name}");
                GameEvents.World("explore", s.kind.ToString());
            }
        }
    }

    /// <summary>Campfire flicker (pixel sparks + light).</summary>
    public class FireFx : MonoBehaviour
    {
        SpriteRenderer glow, flame;
        float t;
        void Start()
        {
            var g = new GameObject("glow"); g.transform.SetParent(transform, false);
            glow = Art.MakeRenderer(g, Art.Glow(20), 31000, true);
            var f = new GameObject("flame"); f.transform.SetParent(transform, false);
            flame = Art.MakeRenderer(f, Art.Orb(Pal.Hex("fff36b"), Pal.Hex("ff8a3d"), 8), Art.SortY(transform.position.y) + 1, true);
        }
        void Update()
        {
            t += Time.deltaTime;
            float k = 0.85f + Mathf.PerlinNoise(t * 6, 0.3f) * 0.3f;
            glow.color = new Color(1f, 0.6f, 0.3f, 0.35f * k);
            flame.transform.localScale = new Vector3(1, k * 1.2f, 1);
            if (Random.value < 0.15f) Fx.I?.Burst((Vector2)transform.position + Vector2.up * 0.3f, Random.value < 0.5f ? Pal.Fire : Pal.Lightning, 1, 1.5f, 0.6f, -2f, 1, 40, 90, false, true);
        }
    }
}
