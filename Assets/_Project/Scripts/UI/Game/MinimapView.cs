using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>HUD minimap: 48 world units around the player, enemies as dots, quest targets and the tower on the edge.</summary>
    public class MinimapView : MonoBehaviour
    {
        [SerializeField] MapCanvas map;
        [SerializeField] float viewUnits = 48;
        [SerializeField] float refreshInterval = 0.1f;
        float t;

        public void Tick(Game g)
        {
            t -= Time.unscaledDeltaTime;
            if (t > 0) return;
            t = refreshInterval;
            var p = g.Player.Pos;
            map.Center = p;
            map.PixelsPerUnit = map.Rect.rect.width / viewUnits;
            map.Paint((cx, cy) => true);
            foreach (var a in Actor.All)
            {
                if (a == null || !a.Alive || a.team != Team.Enemy) continue;
                map.Marker(a.Pos, "■", (a as Enemy).def.boss ? Color.magenta : new Color(1, 0.3f, 0.3f), null, false);
            }
            foreach (var q in g.World.quests.Where(x => x.status == 1 && x.kind != "hunt")) map.Marker(new Vector2(q.tx, q.ty), "!", Pal.Gold, null, true);
            map.Marker(WorldGen.TowerPos, "▲", Pal.Hex("c9b8ff"), null, true);
            map.Marker(p, "●", Color.white, null, false);
            map.EndMarkers();
        }
    }
}
