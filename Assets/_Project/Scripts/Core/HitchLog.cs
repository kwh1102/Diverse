using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Stutter diagnosis that also works in a release build (the Unity profiler doesn't): counts first-time work
    /// (sprites drawn, chunks built, sounds made, effect pools grown, enemies spawned) and HitchLogRunner writes every
    /// slow frame with what happened in it. Off unless the game is started with -diverseHitchLog
    /// (add -diverseAutoplay to start a new life in the Normal slot 1 and walk east by itself, then quit).
    /// The log goes to hitchlog.txt in the save folder.
    /// </summary>
    public static class HitchLog
    {
        public static int SpritesMade, ChunksBuilt, SoundsMade, PoolGrew, Spawned;

        public static bool Autoplay { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (!args.Contains("-diverseHitchLog")) return;
            Autoplay = args.Contains("-diverseAutoplay");
            var go = new GameObject("HitchLog");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<HitchLogRunner>();
        }
    }
}
