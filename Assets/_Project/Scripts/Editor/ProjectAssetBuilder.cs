using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Diverse.EditorTools
{
    /// <summary>
    /// One-time setup of the editor-facing assets. After this the scenes, prefabs and data are the source of truth,
    /// edited in the editor; this tool only fills in what is MISSING and never overwrites existing files.
    /// - Data/: one ScriptableObject per weapon / costume / enemy + Resources/GameDatabase.asset
    /// - Prefabs/: Player, Enemy, Clone, Summon, Projectile, Pickup, Interactable, Chunk, Campfire (+ UI, see UIBuilder)
    /// - Scenes/MainMenu.unity, Scenes/Game.unity (see UIBuilder)
    /// - Resources/Sprites/: the code-drawn pixel art baked to PNG (Art.Get prefers these, so they can be repainted)
    /// Batch mode: Unity -batchmode -quit -executeMethod Diverse.EditorTools.ProjectAssetBuilder.BuildAllBatch
    /// </summary>
    public static class ProjectAssetBuilder
    {
        internal const string Root = "Assets/_Project";
        internal const string DataDir = Root + "/Data";
        internal const string PrefabDir = Root + "/Prefabs";
        internal const string ResDir = Root + "/Resources";
        internal const string SpriteDir = ResDir + "/Sprites";

        [MenuItem("Diverse/빠진 에셋 채우기 (데이터·프리팹·씬)", priority = 80)]
        public static void BuildAllMenu()
        {
            if (!EditorUtility.DisplayDialog("빠진 에셋 채우기", "없는 데이터 에셋, 프리팹, 씬, 스프라이트만 새로 만듭니다. 이미 있는 파일은 건드리지 않습니다.", "진행", "취소")) return;
            BuildAll();
        }

        [MenuItem("Diverse/스프라이트 PNG로 굽기", priority = 81)]
        public static void BakeSpritesMenu()
        {
            if (!EditorUtility.DisplayDialog("스프라이트 굽기", "코드로 그린 도트를 Resources/Sprites에 PNG로 저장합니다. 이미 있는 PNG는 건드리지 않습니다.", "진행", "취소")) return;
            BakeSprites();
        }

        public static void BuildAllBatch() => BuildAll();

        static void BuildAll()
        {
            EnsureDir(DataDir); EnsureDir(PrefabDir); EnsureDir(ResDir);
            BakeSprites();
            var db = BuildData();
            BuildPrefabs(db);
            UIBuilder.BuildAll();
            AssetDatabase.SaveAssets();
            Debug.Log("[Diverse] 빠진 에셋 채우기 완료");
        }

        // ───────────────────────── Data ─────────────────────────

        static GameDatabase BuildData()
        {
            var db = LoadOrCreate<GameDatabase>(ResDir + "/GameDatabase.asset");
            db.weapons = DefaultData.Weapons.Values
                .Select(w => DataAsset<WeaponData>($"{DataDir}/Weapons/{w.kind}.asset", a => a.def = w)).ToList();
            db.costumes = DefaultData.Costumes
                .Select(c => DataAsset<CostumeData>($"{DataDir}/Costumes/{c.id}.asset", a => a.def = c)).ToList();
            db.enemies = DefaultData.Enemies.Values
                .Select(e => DataAsset<EnemyData>($"{DataDir}/Enemies/{e.id}.asset", a => a.def = e)).ToList();
            EditorUtility.SetDirty(db);
            return db;
        }

        /// <summary>Create the asset with default values only if it does not exist yet.</summary>
        static T DataAsset<T>(string path, System.Action<T> fill) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            EnsureDir(Path.GetDirectoryName(path).Replace('\\', '/'));
            var a = ScriptableObject.CreateInstance<T>();
            fill(a);
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        // ───────────────────────── Prefabs ─────────────────────────

        static Material spriteMat, addMat;

        static void LoadMaterials()
        {
            spriteMat = MaterialAsset("DiverseSprite", "Shaders/DiverseSprite");
            addMat = MaterialAsset("DiverseSpriteAdd", "Shaders/DiverseSpriteAdd");
        }

        /// <summary>Shared material assets for prefab renderers (runtime code still swaps in Art.SpriteMat/AddMat where it needs to).</summary>
        static Material MaterialAsset(string name, string shader)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            EnsureDir(Root + "/Materials");
            m = new Material(Resources.Load<Shader>(shader)) { name = name };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static SpriteRenderer Renderer(Transform parent, string name, int order = 0, bool additive = false, Vector3 local = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = additive ? addMat : spriteMat;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Actor layout shared by Player / Enemy / Clone: root → shadow, root → visual → body.</summary>
        static GameObject ActorRoot<T>(string name, out T actor, out Transform visual, out SpriteRenderer body) where T : Actor
        {
            var root = new GameObject(name);
            var shadow = Renderer(root.transform, "shadow");
            visual = new GameObject("visual").transform;
            visual.SetParent(root.transform, false);
            body = Renderer(visual, "body");
            actor = root.AddComponent<T>();
            var so = new SerializedObject(actor);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("shadow").objectReferenceValue = shadow;
            so.FindProperty("visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }


        /// <summary>Save a new prefab, or return the existing one untouched (the editor copy is the source of truth).</summary>
        static T Save<T>(GameObject root, string path) where T : Component
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { Object.DestroyImmediate(root); return existing; }
            EnsureDir(Path.GetDirectoryName(path).Replace('\\', '/'));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<T>();
        }

        /// <summary>Baked PNG for a sprite key, for editor previews in prefabs (runtime code still picks the real one).</summary>
        internal static Sprite Baked(string key) => AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteDir}/{key}.png");

        static void BuildPrefabs(GameDatabase db)
        {
            LoadMaterials();

            // Player: weapon on a pivot at the hand, shield (sword & shield only), raft (shown on water).
            // Preview sprites (white rabbit + greatsword) so the prefab is visible in the editor.
            var pr = ActorRoot<Player>("Player", out var player, out var pv, out var pbody);
            player.team = Team.Player;
            player.radius = 0.35f;
            pbody.sprite = Baked("rabbit_white_Idle0");
            var pivot = new GameObject("weaponPivot").transform;
            pivot.SetParent(pv, false);
            pivot.localPosition = Art.HandAnchor;
            pivot.localRotation = Quaternion.Euler(0, 0, 60);
            var weapon = Renderer(pivot, "weapon");
            weapon.sprite = Baked("weapon_Greatsword");
            weapon.sortingOrder = 1;
            var shield = Renderer(pv, "shield", local: new Vector3(-0.15f, 0.42f, 0));
            shield.sprite = Baked("shield");
            shield.sortingOrder = -1;
            var raft = Renderer(pr.transform, "raft", local: new Vector3(0, 0.1f, 0));
            raft.sprite = Baked("prop_raft");
            raft.enabled = false;
            Set(player, "weaponPivot", pivot);
            Set(player, "weaponSr", weapon);
            Set(player, "shieldSr", shield);
            Set(player, "raftSr", raft);
            SetShadow(pr, 14);
            db.playerPrefab = Save<Player>(pr, PrefabDir + "/Actors/Player.prefab");

            // Enemy: attack telegraph ring under the feet (preview: mushroom)
            var er = ActorRoot<Enemy>("Enemy", out var enemy, out _, out var ebody);
            enemy.team = Team.Enemy;
            ebody.sprite = Baked("enemy_shroom_0");
            var warn = Renderer(er.transform, "warn", -29000);
            warn.enabled = false;
            Set(enemy, "warn", warn);
            SetShadow(er, 14);
            db.enemyPrefab = Save<Enemy>(er, PrefabDir + "/Actors/Enemy.prefab");

            // Clone: translucent copy of the player (additive), holding a phantom weapon
            var cr = ActorRoot<Clone>("Clone", out var clone, out var cv, out var cbody);
            clone.team = Team.Neutral;
            cbody.sharedMaterial = addMat;
            cbody.sprite = Baked("rabbit_white_Idle0");
            cbody.color = new Color(0.65f, 0.8f, 1f, 0.75f);
            var cw = Renderer(cv, "weapon", additive: true, local: Art.HandAnchor);
            cw.sprite = Baked("weapon_Greatsword");
            cw.color = cbody.color;
            Set(clone, "weapon", cw);
            db.clonePrefab = Save<Clone>(cr, PrefabDir + "/Actors/Clone.prefab");

            // Summon (turret / orb / zone share one prefab; the sprite is chosen at spawn)
            var sr = new GameObject("Summon");
            var ssr = sr.AddComponent<SpriteRenderer>();
            ssr.sharedMaterial = addMat;
            Set(sr.AddComponent<Summon>(), "sr", ssr);
            db.summonPrefab = Save<Summon>(sr, PrefabDir + "/Actors/Summon.prefab");

            var proj = new GameObject("Projectile");
            var psr = proj.AddComponent<SpriteRenderer>();
            psr.sharedMaterial = spriteMat;
            psr.sprite = Baked("bolt");
            Set(proj.AddComponent<Projectile>(), "sr", psr);
            db.projectilePrefab = Save<Projectile>(proj, PrefabDir + "/Combat/Projectile.prefab");

            var pick = new GameObject("Pickup");
            var pksr = pick.AddComponent<SpriteRenderer>();
            pksr.sharedMaterial = spriteMat;
            pksr.sprite = Baked("item_gold");
            Set(pick.AddComponent<Pickup>(), "sr", pksr);
            db.pickupPrefab = Save<Pickup>(pick, PrefabDir + "/World/Pickup.prefab");

            var it = new GameObject("Interactable");
            var isr = it.AddComponent<SpriteRenderer>();
            isr.sharedMaterial = spriteMat;
            isr.sprite = Baked("prop_chest");
            var inter = it.AddComponent<Interactable>();
            inter.sr = isr;
            var ish = Renderer(it.transform, "shadow", -30000);
            ish.sprite = Baked("shadow_14");
            Set(inter, "shadow", ish);
            db.interactablePrefab = Save<Interactable>(it, PrefabDir + "/World/Interactable.prefab");

            var fire = new GameObject("Campfire");
            var ffx = fire.AddComponent<FireFx>();
            Set(ffx, "glow", Renderer(fire.transform, "glow", 31000, true));
            Set(ffx, "flame", Renderer(fire.transform, "flame", 0, true));
            Save<FireFx>(fire, PrefabDir + "/World/Campfire.prefab");

            var chunk = new GameObject("Chunk");
            var view = chunk.AddComponent<ChunkView>();
            Set(view, "ground", Renderer(chunk.transform, "ground", -32000));
            Save<ChunkView>(chunk, PrefabDir + "/World/Chunk.prefab");

            EditorUtility.SetDirty(db);
        }

        static void SetShadow(GameObject root, int px)
        {
            var sh = root.transform.Find("shadow")?.GetComponent<SpriteRenderer>();
            if (sh != null) { sh.sprite = Baked($"shadow_{px}"); sh.sortingOrder = -2; }
        }

        internal static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) throw new System.Exception($"{target.GetType().Name} has no serialized field '{field}'");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static VolumeProfile PostFxProfile()
        {
            string path = DataDir + "/PostFx.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null) return existing;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            // Components must be sub-assets of the profile so they serialize
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.75f);
            bloom.threshold.Override(0.82f);
            bloom.scatter.Override(0.55f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(Color.black);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(8f);
            ca.contrast.Override(6f);
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);
            return profile;
        }


        // ───────────────────────── Sprites ─────────────────────────

        /// <summary>
        /// Bake every static sprite the game draws into Resources/Sprites/&lt;key&gt;.png (16 PPU, point filter, original pivot).
        /// Art.Get loads these first, so an artist can repaint any of them. Colors-in-key sprites (orbs, tinted shots) and
        /// animation strips stay procedural.
        /// </summary>
        public static void BakeSprites()
        {
            EnsureDir(SpriteDir);
            var jobs = new List<(string key, System.Func<Sprite> make)>();
            void Add(string key, System.Func<Sprite> make) => jobs.Add((key, make));

            foreach (var c in DefaultData.Costumes)
            {
                foreach (Pose pose in System.Enum.GetValues(typeof(Pose))) { var p = pose; Add($"rabbit_{c.id}_{p}", () => Art.Rabbit(c, p)); }
                Add($"elder_{c.id}", () => Art.ElderRabbit(c));
            }
            foreach (WeaponKind k in System.Enum.GetValues(typeof(WeaponKind))) { var kk = k; Add($"weapon_{kk}", () => Art.Weapon(kk)); }
            Add("shield", Art.Shield);
            Add("bolt", Art.Bolt);
            Add("knife", Art.Knife);
            foreach (var key in DefaultData.Enemies.Values.Select(e => e.sprite).Distinct())
                for (int f = 0; f < 3; f++) { var ff = f; var kk = key; Add($"enemy_{kk}_{ff}", () => Art.Enemy(kk, ff)); }
            foreach (Biome b in System.Enum.GetValues(typeof(Biome)))
            {
                var bb = b;
                for (int v = 0; v < 4; v++) { var vv = v; Add($"tree_{bb}_{vv}", () => Art.Tree(bb, vv)); }
                for (int v = 0; v < 3; v++) { var vv = v; Add($"rock_{bb}_{vv}", () => Art.Rock(bb, vv)); }
                for (int v = 0; v < 2; v++) { var vv = v; Add($"bush_{bb}_{vv}", () => Art.Bush(bb, vv)); Add($"tuft_{bb}_{vv}", () => Art.Tuft(bb, vv)); }
            }
            for (int v = 0; v < 5; v++) { var vv = v; Add($"flower_{vv}", () => Art.Flower(vv)); }
            Add("tower", Art.Tower);
            Add("shadow_14", () => Art.Shadow(14));        // prefab preview size (other sizes stay procedural)
            foreach (var id in new[] { "board", "well", "lamp", "fence", "tent", "raft", "obelisk", "wall", "banner", "campfire", "chest", "chest_open", "grave", "statue",
                                       "shrine", "pillar", "pillar_broken", "crate", "barrel", "altar", "anvil", "stall", "gate", "cage" })
            { var ii = id; Add($"prop_{ii}", () => Art.Prop(ii)); }
            foreach (var kind in new[] { "owl", "cat", "bear", "otter" })
                for (int f = 0; f < 2; f++) { var kk = kind; var ff = f; Add($"npc_{kk}_{ff}", () => Art.Npc(kk, ff)); }
            foreach (var id in new[] { "gold", "xp", "heart", "shard", "potion", "raft" }) { var ii = id; Add($"item_{ii}", () => Art.Item(ii)); }

            int written = 0;
            var pivots = new Dictionary<string, Vector2>();
            foreach (var (key, make) in jobs)
            {
                string path = $"{SpriteDir}/{key}.png";
                if (File.Exists(path)) continue;
                var s = make();
                if (s == null) continue;
                var r = s.rect;
                var src = s.texture;
                var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                tex.SetPixels(src.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height));
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                pivots[path] = new Vector2(s.pivot.x / r.width, s.pivot.y / r.height);
                written++;
            }
            AssetDatabase.Refresh();
            foreach (var kv in pivots)
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(kv.Key);
                if (imp == null) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = Art.PPU;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.isReadable = true;                    // Art.Silhouette reads pixels for dash afterimages
                imp.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings();
                imp.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                imp.SetTextureSettings(settings);
                imp.spritePivot = kv.Value;
                imp.SaveAndReimport();
            }
            Debug.Log($"[Diverse] 스프라이트 {written}개를 {SpriteDir}에 구웠다 (기존 {jobs.Count - written}개는 유지)");
        }

        internal static void EnsureDir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
