using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Draws the normal-mode zones with the image model (the same pipeline as the custom-mode design studio) and bakes the
    /// results into Resources/Sprites, where the game picks them up by key — so the art ships with the game and can be
    /// repainted by hand like any other sprite.
    /// 1) Setup (edit mode): every zone gets its own monsters (Data/Enemies) and art prompts (Data/Zones → art).
    /// 2) Draw (play mode, needs an API key): per zone 1 ground sheet, 3 scenery sheets, 1 picture per monster, 1 effect sheet;
    ///    plus the spawn meadow and the shared props. Pictures land in Logs/ZoneArt first; a picture already there is not
    ///    drawn again (delete it to redraw); every picture is checked (Check) and redrawn up to 3 times.
    /// 3) Import (edit mode): copy into Resources/Sprites with pixel-art import settings and the right pivots.
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.ZoneArtBuilder.RunBatch   (exit 0 = everything drawn)
    /// Only the setup: …ZoneArtBuilder.SetupBatch
    /// </summary>
    public static class ZoneArtBuilder
    {
        const string RunningKey = "Diverse.ZoneArt.Running";
        const string BatchKey = "Diverse.ZoneArt.Batch";
        const string Stage = "Logs/ZoneArt";
        const string SpriteDir = ProjectAssetBuilder.SpriteDir;
        const int Parallel = 6;

        const string Style = "Detailed pixel art video game sprite, 16-bit style, rich colours with soft shading and highlights, readable silhouette, " +
                             "dark outline, no text, no ground, no cast shadow, isolated on a fully transparent background. ";
        const string EffectStyle = "Pixel art video game visual effects, 16-bit style, bright glowing colours, no characters, no text, " +
                                   "isolated on a fully transparent background. ";
        const string TileStyle = "Top-down pixel art video game terrain textures, 16-bit style, flat lighting, no objects, no shadows, no perspective, no text. ";

        [MenuItem("Diverse/구역 아트 생성 (AI, 없는 그림만)", priority = 91)]
        public static void RunMenu()
        {
            if (!EditorUtility.DisplayDialog("구역 아트 생성", "이미지 모델로 일반 모드 구역의 지면·배경·몬스터·이펙트와 공용 소품을 그려 Resources/Sprites에 넣습니다.\n" +
                    "Logs/ZoneArt에 이미 있는 그림은 다시 그리지 않습니다. API 비용이 듭니다 (전체 약 $0.5, low 품질).", "진행", "취소")) return;
            Begin(false);
        }

        public static void RunBatch() => Begin(true);

        public static void SetupBatch()
        {
            Setup();
            EditorApplication.Exit(0);
        }

        static void Begin(bool batch)
        {
            Setup();
            Directory.CreateDirectory(Stage);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Runner>() == null) new GameObject("ZoneArtRunner").AddComponent<Runner>();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && Object.FindFirstObjectByType<Runner>() == null) new GameObject("ZoneArtRunner").AddComponent<Runner>();
            if (s != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            SessionState.SetBool(RunningKey, false);
            int imported = Import();
            var result = File.Exists(Path.Combine(Stage, "result.txt")) ? File.ReadAllText(Path.Combine(Stage, "result.txt")) : "FAIL: no result";
            Debug.Log($"[ZoneArt] {result.Split('\n')[0]} — {imported}개 스프라이트를 {SpriteDir}에 넣었다");
            if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1);
        }

        // ───────────────────────── 1) Setup: zone monsters + prompts in the data assets ─────────────────────────

        static void Setup()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(ProjectAssetBuilder.ResDir + "/GameDatabase.asset");
            // Monsters: a copy of the template species' asset (its tuned behaviour), with a new body
            foreach (var m in ZoneArtDefaults.Monsters)
            {
                string path = $"{ProjectAssetBuilder.DataDir}/Enemies/{m.id}.asset";
                var data = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
                if (data == null)
                {
                    var template = AssetDatabase.LoadAssetAtPath<EnemyData>($"{ProjectAssetBuilder.DataDir}/Enemies/{m.template}.asset");
                    data = ScriptableObject.CreateInstance<EnemyData>();
                    data.def = JsonUtility.FromJson<EnemyDef>(JsonUtility.ToJson(template.def));
                    data.def.id = m.id; data.def.name = m.name; data.def.sprite = m.id;
                    data.def.shot = ZoneArtDef.FxKey(m.zone, "shot"); data.def.hitFx = ZoneArtDef.FxKey(m.zone, "hit");
                    AssetDatabase.CreateAsset(data, path);
                }
                if (!db.enemies.Contains(data)) db.enemies.Add(data);
            }
            foreach (var zd in db.zones)
            {
                var z = zd.def;
                bool dirty = false;
                if (z.art == null || string.IsNullOrEmpty(z.art.theme)) { z.art = ZoneArtDefaults.Art(z.style); dirty = true; }
                var mine = ZoneArtDefaults.EnemiesOf(z.style);
                if (z.enemies == null || !z.enemies.All(id => mine.Contains(id))) { z.enemies = mine; dirty = true; }
                // The zone master: its own picture, the move of the body it borrowed
                var boss = db.enemies.FirstOrDefault(e => e != null && e.def.id == z.boss);
                if (boss != null && string.IsNullOrEmpty(boss.def.signature) && ZoneArtDefaults.BossSignature.TryGetValue(z.boss, out var sig))
                {
                    boss.def.signature = sig; boss.def.sprite = z.boss;
                    boss.def.shot = ZoneArtDef.FxKey(z.style, "shot"); boss.def.hitFx = ZoneArtDef.FxKey(z.style, "hit");
                    EditorUtility.SetDirty(boss);
                }
                if (dirty) EditorUtility.SetDirty(zd);
            }
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── 2) Draw (play mode) ─────────────────────────

        const int Attempts = 3;   // a sheet whose pictures fail the check is drawn again, up to this many times

        /// <summary>
        /// Why a converted picture is unusable, or null when it is fine. Catches what the image model / cutout gets wrong:
        /// an empty or crumb-sized cell, a flat single-colour shape (a cell cut through a figure), a box that kept its
        /// background, a white silhouette, and a pose much smaller or bigger than the standing pose (target height).
        /// </summary>
        static string Check(PixelCanvas cv, int target = 0, bool allowWhite = false)
        {
            if (cv == null) return "empty";
            int opaque = 0, white = 0;
            var colors = new Dictionary<int, int>();
            foreach (var c in cv.Px)
            {
                if (c.a == 0) continue;
                opaque++;
                int key = c.r << 16 | c.g << 8 | c.b;
                colors[key] = colors.TryGetValue(key, out var n) ? n + 1 : 1;
                // near-white AND colourless: a silhouette, not snow or ice (those carry a blue tint)
                if (c.r > 200 && c.g > 200 && c.b > 200 && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b)) < 20) white++;
            }
            if (opaque < (allowWhite ? 6 : 12)) return "empty";
            if (colors.Count < 5) return "flat";
            // One colour covering most of it: a flat shape (a cell cut through a figure, a blob) instead of a shaded drawing
            if (opaque > 120 && colors.Values.Max() > opaque * 0.45f) return "flat";
            if (!allowWhite && opaque > 300 && colors.Count < 12) return "flat";   // a big drawing always has shading; a few flat colours = a cut-out shape
            // A full box is fine for a crate; a box that is mostly ONE colour is a background the cutout left behind
            if (cv.W * cv.H > 64 && opaque > cv.W * cv.H * 0.93f && colors.Values.Max() > opaque * 0.4f) return "boxy";
            if (!allowWhite && white > opaque * 0.5f) return "white";
            if (target > 0 && (cv.H < target * 0.7f || cv.H > target * 1.35f)) return $"size {cv.W}x{cv.H} (want h{target})";
            if (target > 0 && cv.W > target * 2.6f) return $"wide {cv.W}x{cv.H}";
            return null;
        }

        static PixelCanvas LoadStaged(string key)
        {
            var path = Path.Combine(Stage, key + ".png");
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2);
            try
            {
                if (!t.LoadImage(File.ReadAllBytes(path))) return null;
                var cv = new PixelCanvas(t.width, t.height);
                System.Array.Copy(t.GetPixels32(), cv.Px, cv.Px.Length);
                return cv;
            }
            finally { Object.DestroyImmediate(t); }
        }

        /// <summary>Is this key staged and good? Grounds: present. Effects: no white check. Monster frames: against frame 0.</summary>
        static bool Good(string key)
        {
            var cv = LoadStaged(key);
            if (cv == null) return false;
            if (key.StartsWith("ground_")) return true;
            if (key.StartsWith("enemy_") && !key.EndsWith("_0"))
            {
                var idle = LoadStaged(key.Substring(0, key.Length - 1) + "0");
                return idle != null && Check(cv, idle.H) == null;
            }
            return Check(cv, 0, key.StartsWith("zfx_")) == null;
        }

        class Runner : MonoBehaviour
        {
            readonly StringBuilder log = new StringBuilder();
            int calls, failed;
            readonly Queue<(string label, System.Func<bool, IEnumerator> job, int attempt)> queue = new Queue<(string, System.Func<bool, IEnumerator>, int)>();
            int running;

            void Awake() => DontDestroyOnLoad(gameObject);

            IEnumerator Start()
            {
                yield return null;
                Session.EnsureLoaded();
                if (string.IsNullOrEmpty(SaveSystem.LoadApiKey())) { Finish("FAIL: API 키 없음 (OPENAI_API_KEY)"); yield break; }
                foreach (var z in DB.Zones.Values.OrderBy(z => z.style)) Plan(z);
                PlanMeadow();
                PlanProps();
                log.AppendLine($"jobs: {queue.Count}");
                while (queue.Count > 0 || running > 0)
                {
                    while (running < Parallel && queue.Count > 0)
                    {
                        var job = queue.Dequeue();
                        running++;
                        StartCoroutine(Run(job.label, job.job, job.attempt));
                    }
                    yield return null;
                }
                Finish((failed == 0 ? "PASS" : $"FAIL: {failed} sheet(s) still bad after {Attempts} tries") + $" — {calls} pictures (~${calls * 0.006f:0.00})");
            }

            IEnumerator Run(string label, System.Func<bool, IEnumerator> job, int attempt)
            {
                var it = job(attempt == Attempts - 1);
                bool ok = true;
                while (true)
                {
                    object cur;
                    try { if (!it.MoveNext()) break; cur = it.Current; }
                    catch (System.Exception e) { Debug.LogWarning($"[ZoneArt] {label} failed: {e}"); ok = false; break; }
                    if (cur is bool b) { ok = b; break; }
                    yield return cur;
                }
                if (ok) log.AppendLine($"drawn: {label}" + (attempt > 0 ? $" (try {attempt + 1})" : ""));
                else if (attempt + 1 < Attempts) { log.AppendLine($"retry: {label} — {lastWhy}"); queue.Enqueue((label, job, attempt + 1)); }
                else { failed++; log.AppendLine($"FAILED: {label} — {lastWhy} {ImageClient.LastStatus}"); }
                running--;
            }

            string lastWhy = "";

            void Finish(string result)
            {
                File.WriteAllText(Path.Combine(Stage, "result.txt"), result + "\n" + log);
                Debug.Log("[ZoneArt] " + result);
                EditorApplication.ExitPlaymode();
            }

            void Add(string label, string[] keys, System.Func<bool, IEnumerator> job)
            {
                if (keys.All(Good)) return;
                queue.Enqueue((label, job, 0));
            }

            // ── per zone ──

            void Plan(ZoneDef z)
            {
                var a = z.art;
                string theme = a.theme;
                string g0 = $"ground_{a.ground}", g1 = $"ground_{a.groundAccent}";
                var tiles = new[] { g0 + "_0", g0 + "_1", g1 + "_0", g1 + "_1" };
                Add(z.name + " ground", tiles, _ => Tiles(a.tilePrompts, tiles, theme));
                PlanDecor(z.name, z.biome, a.decorPrompts, theme);

                // Monsters: one per picture (four poses in a 2x2 grid), so no figure spills into another's cell
                var subjects = z.enemies.Select((id, i) => (id, prompt: a.monsterPrompts != null && i < a.monsterPrompts.Length ? a.monsterPrompts[i] : null))
                    .Append((id: z.boss, prompt: a.bossPrompt)).Where(s => s.prompt != null).ToList();
                foreach (var s in subjects)
                {
                    var d = DB.Enemy(s.id);
                    var keys = Enumerable.Range(0, 5).Select(f => $"enemy_{d.sprite}_{f}").ToArray();
                    Add($"{z.name} {d.name}", keys, last => Monster(s.id, s.prompt, theme, last));
                }

                var fx = new[] { "shot", "hit", "burst", "mote" }.Select(k => ZoneArtDef.FxKey(z.style, k)).ToArray();
                Add(z.name + " effects", fx, last => Effects(a.effectPrompts, fx, last));
            }

            void PlanDecor(string name, Biome b, string[] p, string theme)
            {
                if (p == null || p.Length < 11) return;
                string[] keys =
                {
                    $"tree_{b}_0", $"tree_{b}_1", $"tree_{b}_2", $"tree_{b}_3",
                    $"rock_{b}_0", $"rock_{b}_1", $"rock_{b}_2", $"bush_{b}_0",
                    $"bush_{b}_1", $"tuft_{b}_0", $"tuft_{b}_1", null,
                };
                int[] heights = { 46, 44, 46, 40, 18, 15, 16, 14, 14, 8, 8, 0 };
                var prompts = p.Take(11).Append(null).ToArray();
                for (int s = 0; s < 3; s++)
                {
                    int first = s * 4;
                    var k = keys.Skip(first).Take(4).ToArray();
                    Add($"{name} scenery {s + 1}", k.Where(x => x != null).ToArray(),
                        last => Objects(prompts.Skip(first).Take(4).ToArray(), k, heights.Skip(first).Take(4).ToArray(), theme, last));
                }
            }

            void PlanMeadow()
            {
                var keys = new[] { "ground_Grass_0", "ground_Grass_1", "ground_DarkGrass_0", "ground_DarkGrass_1" };
                Add("meadow ground", keys, _ => Tiles(ZoneArtDefaults.MeadowTiles, keys, ZoneArtDefaults.MeadowTheme));
                PlanDecor("meadow", Biome.Meadow, ZoneArtDefaults.MeadowDecor, ZoneArtDefaults.MeadowTheme);
            }

            void PlanProps()
            {
                int n = 0;
                foreach (var sheet in ZoneArtDefaults.PropSheets)
                {
                    var keys = sheet.Select(x => "prop_" + x.id).ToArray();
                    // The same height and feet point as the old prop, so placement and blocking stay right
                    var heights = sheet.Select(x => Mathf.RoundToInt(Art.Prop(x.id).rect.height)).ToArray();
                    var prompts = sheet.Select(x => x.prompt).ToArray();
                    var pivots = sheet.Select(x => Art.Prop(x.id)).ToArray();
                    Add($"props {++n}", keys, last => Objects(prompts, keys, heights, "a cozy fantasy adventure world", last, pivots));
                }
            }

            // ── picture jobs (yield false = draw again) ──

            static RectInt Cell(Texture2D t, int col, int row, int cols, int rows)
            {
                int w = t.width / cols, h = t.height / rows;
                return new RectInt(col * w, t.height - (row + 1) * h, w, h);   // row 0 = top
            }

            static readonly string[] Corners = { "Top-left", "Top-right", "Bottom-left", "Bottom-right" };
            const string Margins = "Each one is drawn small, fully inside its own quarter with wide empty margins; nothing touches or crosses the middle lines. ";

            IEnumerator Tiles(string[] prompts, string[] keys, string theme)
            {
                string[] what = { "the main ground", "the main ground (variant)", "the patches mixed into it", "the patches (variant)" };
                var parts = Enumerable.Range(0, 4).Select(i => $"{Corners[i]}, {what[i]}: {prompts[i]}");
                Texture2D tex = null;
                calls++;
                yield return ImageClient.Generate(TileStyle + "A 2x2 grid of four square texture swatches filling the whole image edge to edge, no borders or gaps. " +
                    "Each swatch is subtle and low contrast: one main colour with fine detail, no big shapes, no grid lines, no checkerboard. " +
                    "The two variants of each pair must have the same colour and brightness so they blend. " +
                    string.Join(". ", parts) + ". World: " + theme + ".", t => tex = t, transparent: false);
                if (tex == null) { lastWhy = "no picture"; yield return false; yield break; }
                var sw = Enumerable.Range(0, 4).Select(i => Pixelizer.Swatch(tex, Cell(tex, i % 2, i / 2, 2, 2), 32)).ToArray();
                Object.Destroy(tex);
                // The two variants of a ground are mixed in patches: if their colour differs a lot the ground looks blotchy
                for (int pair = 0; pair < 4; pair += 2)
                {
                    float diff = Vector4.Distance(Average(sw[pair]), Average(sw[pair + 1]));
                    if (diff > 0.12f) { lastWhy = $"{keys[pair]} variants differ ({diff:0.00})"; yield return false; yield break; }
                }
                for (int i = 0; i < 4; i++) Save(keys[i], sw[i], new Vector2(0.5f, 0.5f));
                yield return true;
            }

            static Vector4 Average(PixelCanvas cv)
            {
                Vector4 s = Vector4.zero;
                foreach (var c in cv.Px) s += new Vector4(c.r, c.g, c.b, 0) / 255f;
                return s / Mathf.Max(1, cv.Px.Length);
            }

            /// <summary>Four separate objects (null prompt = empty cell), each shrunk to its height; feet at the bottom. Only bad / missing keys are replaced.</summary>
            IEnumerator Objects(string[] prompts, string[] keys, int[] heights, string theme, bool last, Sprite[] pivotsFrom = null)
            {
                var parts = Enumerable.Range(0, 4).Select(i => $"{Corners[i]}: {(i < prompts.Length && prompts[i] != null ? prompts[i] : "nothing (leave empty)")}");
                Texture2D tex = null;
                calls++;
                yield return ImageClient.Generate(Style + "A 2x2 grid of four separate scenery objects for a top-down game world, standing upright, front view. " + Margins +
                    string.Join(". ", parts) + ". World: " + theme + ".", t => tex = t);
                if (tex == null) { lastWhy = "no picture"; yield return false; yield break; }
                bool allGood = true;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i] == null || i >= prompts.Length || prompts[i] == null || Good(keys[i])) continue;
                    var cv = Pixelizer.Convert(tex, Cell(tex, i % 2, i / 2, 2, 2), heights[i], byHeight: true);
                    var why = Check(cv);
                    if (why != null) { allGood = false; lastWhy = $"{keys[i]}: {why}"; continue; }
                    var pivot = new Vector2(0.5f, Mathf.Min(0.5f, 1f / cv.H));
                    if (pivotsFrom != null && pivotsFrom[i] != null)
                    {
                        var old = pivotsFrom[i];
                        pivot = new Vector2(0.5f, Mathf.Clamp01(old.pivot.y / old.rect.height));
                    }
                    Save(keys[i], cv, pivot);
                }
                Object.Destroy(tex);
                yield return allGood;
            }

            /// <summary>One monster, four poses in a 2x2 grid: standing, step A, step B, attacking.</summary>
            IEnumerator Monster(string id, string prompt, string theme, bool last)
            {
                var d = DB.Enemy(id);
                Texture2D tex = null;
                calls++;
                yield return ImageClient.Generate(Style + "A sprite sheet in a 2x2 grid: the SAME single monster drawn four times with identical design, colours and size, " +
                    "full body, side view, facing right. " + Margins +
                    "Top-left: standing still. Top-right: walking, front leg forward. Bottom-left: walking, back leg forward. Bottom-right: attacking, lunging forward. " +
                    "Monster: " + prompt + (d.boss ? " (a big boss monster)" : "") + ". World: " + theme + ".", t => tex = t);
                if (tex == null) { lastWhy = "no picture"; yield return false; yield break; }
                // Same height as the body it replaces, so hitboxes and spacing still match
                string body = !string.IsNullOrEmpty(d.signature) ? d.signature : ZoneArtDefaults.Monsters.FirstOrDefault(m => m.id == d.id)?.template ?? d.sprite;
                int height = Art.EnemyHeight(body);
                var poses = Pixelizer.ConvertSet(tex, Enumerable.Range(0, 4).Select(i => Cell(tex, i % 2, i / 2, 2, 2)).ToArray(), height, byHeight: true, alignFacing: true);
                Object.Destroy(tex);
                var idleWhy = Check(poses[0], height);
                if (idleWhy != null) { lastWhy = $"{d.id} standing: {idleWhy}"; yield return false; yield break; }
                int bad = 0;
                for (int i = 1; i < 4; i++)
                {
                    var why = Check(poses[i], poses[0].H);
                    if (why == null) continue;
                    bad++; lastWhy = $"{d.id} pose {i}: {why}";
                    poses[i] = poses[0];   // a broken pose shows the standing one
                }
                if (bad >= 2 && !last) { yield return false; yield break; }
                var feet = new Vector2(0.5f, 1f / poses[0].H);
                Save($"enemy_{d.sprite}_0", poses[0], feet);   // standing
                Save($"enemy_{d.sprite}_1", poses[1], feet);   // step A
                Save($"enemy_{d.sprite}_4", poses[2], feet);   // step B
                Save($"enemy_{d.sprite}_2", poses[3], feet);   // attacking
                Save($"enemy_{d.sprite}_3", poses[0], feet);   // hurt: standing (the hit flash shows it)
                yield return true;
            }

            IEnumerator Effects(string[] prompts, string[] keys, bool last)
            {
                Texture2D tex = null;
                calls++;
                yield return ImageClient.Generate(EffectStyle + "A 2x2 grid of four separate effects. " + Margins +
                    $"Top-left, a small projectile: {prompts[0]}. Top-right, a hit impact burst: {prompts[1]}. " +
                    $"Bottom-left, a big explosion: {prompts[2]}. Bottom-right, a tiny drifting particle: {prompts[3]}.", t => tex = t);
                if (tex == null) { lastWhy = "no picture"; yield return false; yield break; }
                int[] size = { 12, 22, 44, 9 };
                bool allGood = true;
                for (int i = 0; i < 4; i++)
                {
                    if (Good(keys[i])) continue;
                    var cv = Pixelizer.ConvertSet(tex, new[] { Cell(tex, i % 2, i / 2, 2, 2) }, size[i], byHeight: false, colors: 10, outline: i == 0)[0];
                    var why = Check(cv, 0, true);
                    if (why != null) { allGood = false; lastWhy = $"{keys[i]}: {why}"; continue; }
                    Save(keys[i], cv, new Vector2(0.5f, 0.5f));
                }
                Object.Destroy(tex);
                yield return allGood;
            }

            static void Save(string key, PixelCanvas cv, Vector2 pivot)
            {
                if (cv == null || key == null) return;
                var t = cv.ToTexture();
                File.WriteAllBytes(Path.Combine(Stage, key + ".png"), t.EncodeToPNG());
                Object.Destroy(t);
                File.AppendAllText(Path.Combine(Stage, "pivots.txt"), $"{key} {pivot.x:0.0000} {pivot.y:0.0000}\n");
            }
        }

        // ───────────────────────── 3) Import ─────────────────────────

        /// <summary>Copy every staged picture into Resources/Sprites (replacing the old drawing) with pixel-art import settings.</summary>
        public static int Import()
        {
            var pivotFile = Path.Combine(Stage, "pivots.txt");
            if (!File.Exists(pivotFile)) return 0;
            var pivots = new Dictionary<string, Vector2>();
            foreach (var line in File.ReadAllLines(pivotFile))
            {
                var p = line.Split(' ');
                if (p.Length == 3) pivots[p[0]] = new Vector2(float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
            }
            var copied = new List<string>();
            foreach (var kv in pivots)
            {
                var src = Path.Combine(Stage, kv.Key + ".png");
                if (!File.Exists(src)) continue;
                var dst = $"{SpriteDir}/{kv.Key}.png";
                if (File.Exists(dst) && File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src)) && AssetImporter.GetAtPath(dst) is TextureImporter done && done.spritePixelsPerUnit == Art.PPU) continue;
                File.Copy(src, dst, true);
                copied.Add(kv.Key);
            }
            AssetDatabase.Refresh();
            foreach (var key in copied)
            {
                var path = $"{SpriteDir}/{key}.png";
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                if (imp == null) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = Art.PPU;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.isReadable = true;                    // ground textures and afterimages read pixels
                imp.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings();
                imp.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                imp.SetTextureSettings(settings);
                imp.spritePivot = pivots[key];
                imp.SaveAndReimport();
            }
            return copied.Count;
        }

        [MenuItem("Diverse/구역 아트 다시 넣기 (Logs/ZoneArt → Sprites)", priority = 92)]
        public static void ImportMenu() => Debug.Log($"[ZoneArt] {Import()}개 넣음");
    }
}
