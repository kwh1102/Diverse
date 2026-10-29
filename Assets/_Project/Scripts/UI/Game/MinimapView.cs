using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace Diverse
{
    /// <summary>HUD minimap: 48 world units around the player, enemies as dots, quest targets and the tower on the edge.</summary>
    public class MinimapView : MonoBehaviour
    {
        [SerializeField] MapCanvas map;
        [SerializeField] float viewUnits = 48;
        [SerializeField] float refreshInterval = 0.1f;
        [SerializeField, Min(1)] int enemyMarkerSize = 3, bossMarkerSize = 6;
        float t;

        static readonly ProfilerMarker MinimapMarker = new ProfilerMarker("Diverse.Minimap");   // read by DesignPerf / PerfProbe

        public void Tick(Game g)
        {
            using var _perf = MinimapMarker.Auto();
            t -= Time.unscaledDeltaTime;
            if (t > 0) return;
            t = refreshInterval;
            var p = g.Player.Pos;
            map.Center = p;
            map.PixelsPerUnit = map.Rect.rect.width / viewUnits;
            map.Paint((cx, cy) => true);
            map.DrawRoute(g.Player);
            if (g.Player.TravelTarget.HasValue)
                map.Marker(g.Player.TravelTarget.Value, "×", Pal.Gold, null, true, 13);
            foreach (var a in Actor.All)
            {
                if (a == null || !a.Alive || a.team != Team.Enemy) continue;
                bool boss = a is Enemy enemy && enemy.def.boss;
                map.Marker(a.Pos, "■", boss ? Color.magenta : new Color(1, 0.3f, 0.3f), null, false, boss ? bossMarkerSize : enemyMarkerSize);
            }
            foreach (var q in g.World.quests.Where(x => x.status == 1 && x.kind != "hunt")) map.Marker(new Vector2(q.tx, q.ty), "!", Pal.Gold, null, true);
            map.Marker(WorldGen.TowerPos, "▲", Pal.Hex("c9b8ff"), null, true);
            map.Marker(p, "●", Color.white, null, false);
            map.EndMarkers();
        }
    }
}
