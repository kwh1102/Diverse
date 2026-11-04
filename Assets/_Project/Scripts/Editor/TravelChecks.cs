using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    /// <summary>Offline movement regressions and visual review of the original rabbit's articulated poses.</summary>
    public static class TravelChecks
    {
        const string Output = "Logs/TravelReview";
        static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }

        [MenuItem("Diverse/Check Travel Types and Capture Motions")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            foreach (Ground ground in Enum.GetValues(typeof(Ground)))
                foreach (var type in TravelTypes.All)
                {
                    bool expected = WorldGen.IsLiquid(ground) && (type == TravelType.Flight || type == TravelType.Swim && ground != Ground.Lava);
                    Require(TravelRules.CanCrossLiquid(type, ground) == expected, $"Unexpected crossing: {type}/{ground}");
                }
            Require(TravelRules.CanCrossLiquid(TravelType.Swim, Ground.Toxic), "Swamp water must be swimmable");
            Require(!TravelRules.CanCrossLiquid(TravelType.Swim, Ground.Lava), "Swimming must reject lava");
            Require(TravelRules.CanCrossLiquid(TravelType.Flight, Ground.Lava), "Flight must cross lava");

            Require(TravelRules.TryFindExit(Vector2.zero, Vector2.right, p => true, p => p.x >= 1.19f, out var exit)
                && exit.x >= 1.19f && exit.x <= TravelRules.ExitAllowance, "Nearby bank should be reachable");
            Require(TravelRules.TryFindExit(Vector2.zero, Vector2.right, p => true, p => p.x >= 1.5f, out _), "Allowance boundary should succeed");
            Require(!TravelRules.TryFindExit(Vector2.zero, Vector2.right, p => true, p => p.x > 1.51f, out _), "Distant bank must not grant unlimited travel");
            Require(!TravelRules.TryFindExit(Vector2.zero, Vector2.right, p => p.x < 0.5f, p => p.x > 1, out _), "Must not jump over a wall or lava");
            Require(!TravelRules.TryFindExit(Vector2.zero, Vector2.zero, p => true, p => false, out _), "No aim must not produce an exit");
            Require(!TravelRules.TryFindExit(Vector2.zero, Vector2.right, p => true, p => true, out _), "Dry ground must not grant an extra dash");
            CheckPlayerTerrain();

            foreach (var type in TravelTypes.All)
            {
                var pack = DesignPack.Default(); pack.travelType = type; pack.travelSelected = true;
                pack.hero.imagePrompt = "a small violet octopus with gold eyes";
                string prompt = DesignAi.HeroSheetPrompt(pack);
                Require(prompt.Contains("4 columns and 3 rows") && prompt.Contains(TravelTypes.MotionRow(type)) &&
                    prompt.Contains(pack.hero.imagePrompt) && prompt.Contains("Keep identical face"), "Missing identity or travel instructions: " + type);
                pack.hero.frames = new string[8];
                Require(DesignAi.NeedsHeroSheet(pack, false, false), "A newly selected movement must regenerate its missing motion row");
                pack.hero.frames = new string[12];
                Require(!DesignAi.NeedsHeroSheet(pack, false, false), "Unchanged complete sheet should not be redrawn");
            }

            CaptureRabbit();
            CheckButtons();
            File.WriteAllText(Output + "/checks.txt", "PASS: themed liquids, bounded exit assistance, six motion prompts, regeneration, original rabbit face pixels, six button roles.\n");
            Debug.Log("[TravelChecks] PASS — " + Path.GetFullPath(Output));
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void CaptureRabbit()
        {
            var costume = DB.Costumes.First(c => c.id == "white");
            var method = typeof(Art).Assembly.GetType("Diverse.RabbitTravelArt").GetMethod("Frame", BindingFlags.Public | BindingFlags.Static);
            var original = Art.Rabbit(costume, Pose.Idle0);
            SaveSprite(original, "original-runtime");
            var sheet = new PixelCanvas(9 * 64, 6 * 52);
            sheet.Rect(0, 0, sheet.W, sheet.H, Pal.Hex("242633"));
            var manifest = new StringBuilder("Each row: original, entry A/B, motion A/B/C/D, recovery A/B. Rows: Dash, Flight, Burrow, Swim, Blink, Bounce.\n");
            var faceChanges = new List<string>();
            for (int row = 0; row < TravelTypes.All.Length; row++)
            {
                var type = TravelTypes.All[row];
                Stamp(sheet, original, 32 - (int)original.pivot.x, (5 - row) * 52 + 5 - (int)original.pivot.y);
                for (int frame = 0; frame < 8; frame++)
                {
                    var sprite = (Sprite)method.Invoke(null, new object[] { costume, type, frame });
                    Require(sprite != null && sprite.pixelsPerUnit == original.pixelsPerUnit, "Rabbit scale changed");
                    SaveSprite(sprite, $"{type.ToString().ToLowerInvariant()}_{frame}");
                    Stamp(sheet, sprite, (frame + 1) * 64 + 32 - (int)sprite.pivot.x, (5 - row) * 52 + 5 - (int)sprite.pivot.y);
                    // Locate the original eye/forehead patch verbatim in every visible pose.
                    if (type != TravelType.Burrow || frame < 2 || frame > 5)
                        if (!ContainsFace(sprite, original)) faceChanges.Add($"{type}/{frame}");
                    if (frame == 7) Require(OriginalRecovery(sprite, original), "Recovery must restore every original pixel: " + type);
                }
            }
            var tex = sheet.ToTexture();
            File.WriteAllBytes(Output + "/rabbit-motion-comparison.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            File.WriteAllText(Output + "/motion-layout.txt", manifest.ToString());
            Require(faceChanges.Count == 0, "Original face pixels changed: " + string.Join(", ", faceChanges));
        }

        static bool ContainsFace(Sprite sprite, Sprite reference)
        {
            // Original source rectangle x18..26, y8..18: eyes, face, and the neck/scarf fastening together.
            var r = reference.rect;
            var face = reference.texture.GetPixels((int)r.x + 18, (int)r.y + 8, 9, 11);
            var px = sprite.texture.GetPixels();
            int width = sprite.texture.width, height = sprite.texture.height;
            for (int y = 0; y <= height - 11; y++) for (int x = 0; x <= width - 9; x++)
            {
                bool match = true;
                for (int fy = 0; fy < 11 && match; fy++) for (int fx = 0; fx < 9; fx++)
                    if ((Color32)face[fy * 9 + fx] is var a && (Color32)px[(y + fy) * width + x + fx] is var b &&
                        (a.a > 0 ? !a.Equals(b) : fy >= 4 && b.a > 0)) { match = false; break; }
                if (match) return true;
            }
            return false;
        }

        static bool OriginalRecovery(Sprite sprite, Sprite reference)
        {
            var r = reference.rect;
            var original = reference.texture.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
            var result = sprite.texture.GetPixels();
            for (int y = 0; y < r.height; y++) for (int x = 0; x < r.width; x++)
            {
                Color32 a = original[y * (int)r.width + x], b = result[(y + 2) * sprite.texture.width + x + 10];
                if (a.a != b.a || a.a > 0 && !a.Equals(b)) return false;
            }
            return true;
        }

        static void Stamp(PixelCanvas canvas, Sprite sprite, int ox, int oy)
        {
            var r = sprite.rect;
            var px = sprite.texture.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
            for (int y = 0; y < r.height; y++) for (int x = 0; x < r.width; x++)
                if (px[y * (int)r.width + x].a > 0) canvas.Set(ox + x, oy + y, px[y * (int)r.width + x]);
        }

        static void SaveSprite(Sprite sprite, string name)
        {
            var r = sprite.rect;
            var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
            tex.SetPixels(sprite.texture.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height)); tex.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void CheckButtons()
        {
            var sheet = new PixelCanvas(6 * 40, 4 * 24);
            sheet.Rect(0, 0, sheet.W, sheet.H, Pal.Hex("242633"));
            int col = 0;
            foreach (ButtonRole role in Enum.GetValues(typeof(ButtonRole)))
            {
                var go = new GameObject("Button review", typeof(RectTransform), typeof(Image), typeof(Button));
                try
                {
                    var button = go.GetComponent<Button>();
                    GameButtonStyle.Apply(button, role);
                    Require(button.image.sprite != null && button.transition == Selectable.Transition.SpriteSwap, "Missing button skin");
                    Stamp(sheet, button.image.sprite, col * 40 + 8, 72);
                    Stamp(sheet, button.spriteState.highlightedSprite, col * 40 + 8, 48);
                    Stamp(sheet, button.spriteState.pressedSprite, col * 40 + 8, 24);
                    Stamp(sheet, button.spriteState.disabledSprite, col * 40 + 8, 0);
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                col++;
            }
            var tex = sheet.ToTexture(); File.WriteAllBytes(Output + "/button-frames.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void CheckPlayerTerrain()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var previous = WorldStreamer.I;
            var root = new GameObject("Travel collision regression");
            var world = root.AddComponent<WorldStreamer>(); world.EditorRegister();
            var player = root.AddComponent<Player>(); player.radius = 0.4f;
            var chunk = root.AddComponent<ChunkView>();
            typeof(ChunkView).GetProperty("Ready").SetValue(chunk, true);
            var chunks = (System.Collections.IDictionary)typeof(WorldStreamer).GetField("chunks", flags).GetValue(world);
            chunks[Vector2Int.zero] = chunk;
            var groundType = typeof(WorldStreamer).GetNestedType("GroundChunk", BindingFlags.NonPublic);
            var entry = Activator.CreateInstance(groundType, true);
            var tiles = (Ground[,])groundType.GetField("tiles").GetValue(entry);
            var valid = (bool[,])groundType.GetField("valid").GetValue(entry);
            var groundCache = (System.Collections.IDictionary)typeof(WorldStreamer).GetField("groundCache", flags).GetValue(world);
            groundCache[Vector2Int.zero] = entry;
            var blocks = new byte[WorldGen.Chunk * WorldGen.Chunk];
            ((System.Collections.IDictionary)typeof(WorldStreamer).GetField("blockCache", flags).GetValue(world))[Vector2Int.zero] = blocks;
            var free = typeof(Player).GetMethod("TravelFree", flags);
            try
            {
                foreach (var liquid in new[] { Ground.Water, Ground.DeepWater, Ground.Toxic, Ground.Lava })
                {
                    Map(x => x >= 5 && x < 9 ? liquid : DB.SpawnTheme.ground);
                    player.Pos = new Vector2(6.5f, 6.5f);
                    Set("activeTravel", TravelType.Flight);
                    Require((bool)free.Invoke(player, new object[] { world, player.Pos }), "Flight collision blocked " + liquid);
                    Set("activeTravel", TravelType.Swim);
                    Require((bool)free.Invoke(player, new object[] { world, player.Pos }) == (liquid != Ground.Lava), "Swim collision mismatch " + liquid);
                    if (liquid == Ground.Lava)
                        Require(!(bool)free.Invoke(player, new object[] { world, new Vector2(9.1f, 6.5f) }), "Swimmer footprint clipped through lava at the bank");
                    player.Pos = new Vector2(4.5f, 6.5f);
                    var args = new object[] { TravelType.Swim, Vector2.right };
                    Require((bool)typeof(Player).GetMethod("PrepareTravel", flags).Invoke(player, args) == (liquid != Ground.Lava), "Swim entry did not respect " + liquid);
                }
                foreach (var type in new[] { TravelType.Burrow, TravelType.Swim })
                {
                    Map(x => x >= 5 && x < 9 ? type == TravelType.Burrow ? Ground.Cliff : Ground.Toxic : DB.SpawnTheme.ground);
                    Set("activeTravel", type); Set("enteredWater", type == TravelType.Swim); Set("completingTravel", false);
                    Set("dashVel", Vector2.right * 16); Set("dashRecharge", 0f); Set("dashT", 0f); Set("travelDuration", 0.4f);
                    player.DashCharges = 0; player.Pos = new Vector2(8.3f, 6.5f); player.Facing = Vector2.right;
                    typeof(Player).GetMethod("CompleteNearTravelExit", flags).Invoke(player, null);
                    Require((bool)typeof(Player).GetField("completingTravel", flags).GetValue(player), "Nearby exit not completed for " + type);
                    typeof(Player).GetMethod("MoveTravel", flags).Invoke(player, new object[] { 0.1f });
                    Require(world.Free(player.Pos, player.radius) && player.Pos.x > 9, "Exit finish did not reach safe ground: " + type);
                    Require(player.DashCharges == 0, "Exit assistance must not grant gauge");
                    Set("completingTravel", false); player.Pos = new Vector2(6.2f, 6.5f);
                    typeof(Player).GetMethod("CompleteNearTravelExit", flags).Invoke(player, null);
                    Require((bool)typeof(Player).GetField("completingTravel", flags).GetValue(player), "Distant exit should finish at the nearest reachable bank: " + type);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (previous != null) previous.EditorRegister();
            }

            void Set(string name, object value) => typeof(Player).GetField(name, flags).SetValue(player, value);
            void Map(Func<int, Ground> at)
            {
                for (int y = 0; y < tiles.GetLength(1); y++) for (int x = 0; x < tiles.GetLength(0); x++)
                { tiles[x, y] = at(x - 1); valid[x, y] = true; }
                for (int y = 0; y < WorldGen.Chunk; y++) for (int x = 0; x < WorldGen.Chunk; x++)
                { var g = at(x); blocks[y * WorldGen.Chunk + x] = WorldGen.IsLiquid(g) ? (byte)1 : WorldGen.Level(g) > 0 ? (byte)2 : (byte)0; }
            }
        }
    }
}
