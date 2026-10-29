using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diverse
{
    /// <summary>Frame recorder for HitchLog (created by HitchLog.Boot only when asked for on the command line).</summary>
    public class HitchLogRunner : MonoBehaviour
    {
        struct Frame
        {
            public float t, ms;
            public int sprites, chunks, sounds, pools, spawned, gc;
            public bool font;
            public string scene;
        }

        readonly List<Frame> frames = new List<Frame>(8192);
        int sprites, chunks, sounds, pools, spawned, gc;
        bool font;
        float sceneT;
        bool written;

        void OnFont(Font f) => font = true;

        void Awake()
        {
            Font.textureRebuilt += OnFont;
            SceneManager.sceneLoaded += (s, m) => sceneT = Time.realtimeSinceStartup;
            gc = System.GC.CollectionCount(0);
        }

        void OnDestroy() => Font.textureRebuilt -= OnFont;

        IEnumerator Start()
        {
            if (!HitchLog.Autoplay) yield break;
            // Menu → continue the life of the last used slot (as a player would), or a new life in Normal slot 1
            // → walk east for a while → write the log and quit
            for (int i = 0; i < 30; i++) yield return null;
            if (Game.I == null)
            {
                Session.EnsureLoaded();
                if (Session.HasRunToContinue) Session.ContinueLife();
                else
                {
                    Session.OpenSlot(GameMode.Normal, 1);
                    Session.NewLife(DB.Costumes[0], (WeaponKind)0);
                }
            }
            while (Game.I == null || Game.I.Player == null) yield return null;
            // Walk right away: the first moments after loading are what players notice
            float t0 = Time.realtimeSinceStartup;
            int k = 0;
            while (Time.realtimeSinceStartup - t0 < 25f)
            {
                var p = Game.I.Player;
                if (p != null && k++ % 20 == 0) p.SetDestination(p.Pos + new Vector2(10, Mathf.Sin(k * 0.05f) * 6), false);
                yield return null;
            }
            Write();
            Application.Quit();
        }

        void Update()
        {
            int g = System.GC.CollectionCount(0);
            frames.Add(new Frame
            {
                t = Time.realtimeSinceStartup - sceneT, ms = Time.unscaledDeltaTime * 1000f,
                sprites = HitchLog.SpritesMade - sprites, chunks = HitchLog.ChunksBuilt - chunks, sounds = HitchLog.SoundsMade - sounds,
                pools = HitchLog.PoolGrew - pools, spawned = HitchLog.Spawned - spawned, gc = g - gc, font = font,
                scene = SceneManager.GetActiveScene().name,
            });
            sprites = HitchLog.SpritesMade; chunks = HitchLog.ChunksBuilt; sounds = HitchLog.SoundsMade;
            pools = HitchLog.PoolGrew; spawned = HitchLog.Spawned; gc = g; font = false;
        }

        void OnApplicationQuit() => Write();

        void Write()
        {
            if (written) return;
            written = true;
            var sb = new StringBuilder();
            sb.AppendLine($"Diverse hitch log · {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) · {SystemInfo.processorType} · {Screen.width}x{Screen.height}");
            sb.AppendLine("Frames slower than 18 ms (a missed 60 Hz frame) (t = seconds since the scene loaded; what ran for the first time in that frame):");
            foreach (var f in frames)
            {
                if (f.ms < 18f) continue;
                var what = new List<string>();
                if (f.sprites > 0) what.Add($"sprites {f.sprites}");
                if (f.chunks > 0) what.Add($"chunks {f.chunks}");
                if (f.sounds > 0) what.Add($"sounds {f.sounds}");
                if (f.pools > 0) what.Add($"fx pool +{f.pools}");
                if (f.spawned > 0) what.Add($"spawned {f.spawned}");
                if (f.gc > 0) what.Add("GC");
                if (f.font) what.Add("FONT ATLAS REBUILT");
                sb.AppendLine($"  {f.scene,-9} t={f.t,6:0.00}s  {f.ms,6:0.0} ms  {(what.Count > 0 ? string.Join(", ", what) : "(nothing counted: GPU / shader / engine)")}");
            }
            var dir = System.Environment.GetEnvironmentVariable("DIVERSE_SAVE_DIR") is { Length: > 0 } d ? d : Application.persistentDataPath;
            try { File.WriteAllText(Path.Combine(dir, "hitchlog.txt"), sb.ToString()); } catch { }
        }
    }
}
