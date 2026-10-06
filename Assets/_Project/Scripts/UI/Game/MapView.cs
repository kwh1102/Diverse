using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// World map overlay: drag to pan, wheel to zoom (anchored on the cursor), C to recenter.
    /// Only explored chunks are drawn (fog of war); frontier rings, structures, graves and quest targets are markers.
    /// </summary>
    public class MapView : MonoBehaviour
    {
        [SerializeField] MapCanvas map;
        [SerializeField] Text closeHint, footerLeft, footerRight;
        [SerializeField] Image playerIcon;
        [SerializeField] float minZoom = 0.5f, maxZoom = 24f;

        float zoom = 3f;
        bool dragging;
        Vector2 dragStart, dragCenter;
        Game g;

        public void Init(Game game) => g = game;

        public void Opened() { if (g.Player != null) map.Center = g.Player.Pos; }

        void Update()
        {
            if (g == null) return;
            var view = map.Rect;
            bool inside = UIKit.Hovered(view);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(view, Controls.MouseScreen, null, out var mouse);

            // Zoom anchored on the mouse position
            float scroll = Controls.Scroll;
            if (inside && Mathf.Abs(scroll) > 0.01f)
            {
                var before = map.LocalToWorld(mouse);
                zoom = Mathf.Clamp(zoom * (scroll < 0 ? 0.8f : 1.25f), minZoom, maxZoom);
                map.PixelsPerUnit = zoom;
                map.Center += before - map.LocalToWorld(mouse);
            }
            if (Controls.LeftDown && inside) { dragging = true; dragStart = mouse; dragCenter = map.Center; }
            if (dragging && Controls.LeftHeld) map.Center = dragCenter - (mouse - dragStart) / zoom;
            if (!Controls.LeftHeld) dragging = false;
            if (Controls.KeyDown(UnityEngine.InputSystem.Key.C) && g.Player != null) map.Center = g.Player.Pos;

            map.PixelsPerUnit = zoom;
            var explored = new HashSet<string>(g.World.exploredChunks);
            map.Paint((cx, cy) => explored.Contains(cx + "," + cy));
            Markers();

            UIKit.Set(closeHint, $"{Controls.KeyName(Act.Map)}/Esc 닫기");
            var mw = map.LocalToWorld(mouse);
            UIKit.Set(footerLeft, $"확대 ×{zoom:0.0} · 커서 ({mw.x:0}, {mw.y:0}) · 탐험한 구역 {g.World.exploredChunks.Count}");
            UIKit.Set(footerRight, g.Player != null ? $"<color=#ff8a9a>— 안개 경계</color>  <color=#c9bcd8>{g.FrontierHint()}</color>" : "");
        }

        void Markers()
        {
            var w = g.World;
            // Frontier rings as dotted circles: open ones faint, the mist boundary red
            if (g.Player != null)
            {
                int open = WorldGen.OpenRing(g.Player.Level, w.bossKills);
                for (int i = 0; i < WorldGen.Rings.Length - 1; i++)
                {
                    float radius = WorldGen.Rings[i].radius;
                    if (radius <= 0) continue;
                    var col = i == open ? new Color(1f, 0.45f, 0.55f, 0.8f) : i < open ? new Color(0.8f, 0.75f, 1f, 0.25f) : new Color(0.6f, 0.55f, 0.7f, 0.35f);
                    int n = Mathf.Clamp(Mathf.RoundToInt(radius * zoom / 6f), 24, 360);
                    for (int k = 0; k < n; k++) map.Marker(MathX.Dir(k * 360f / n) * radius, "·", col, null, false);
                }
            }

            map.Marker(WorldGen.TowerPos, "▲", Pal.Hex("c9b8ff"), "기억의 탑", true);

            var view = map.Rect.rect;
            var a = map.LocalToWorld(view.min); var b = map.LocalToWorld(view.max);
            int N = WorldGen.Chunk, R = WorldGen.RegionChunks;
            var disc = new HashSet<string>(w.discovered);
            for (int ry = MathX.FloorDiv(Mathf.FloorToInt(a.y / N), R); ry <= MathX.FloorDiv(Mathf.FloorToInt(b.y / N), R); ry++)
                for (int rx = MathX.FloorDiv(Mathf.FloorToInt(a.x / N), R); rx <= MathX.FloorDiv(Mathf.FloorToInt(b.x / N), R); rx++)
                    foreach (var s in World.I.Gen.StructuresInRegion(rx, ry))
                    {
                        if (!disc.Contains(s.id)) continue;
                        bool cleared = w.clearedCamps.Contains(s.id);
                        string icon = s.kind switch
                        {
                            StructKind.Town => "⌂", StructKind.Camp => cleared ? "○" : "✕", StructKind.Ruins => "♜", StructKind.Shrine => "✧", StructKind.Lair => cleared ? "○" : "☠",
                            StructKind.Graveyard => "†", StructKind.Chest => "▣", StructKind.Wanderer => "?",
                            StructKind.Ferry => "≈", StructKind.Obelisk => cleared ? "○" : "◆", StructKind.Fortress => cleared ? "○" : "♛", _ => "•",
                        };
                        Color32 col = cleared ? Pal.Hex("8bff9a") : s.kind is StructKind.Camp or StructKind.Lair or StructKind.Graveyard or StructKind.Obelisk or StructKind.Fortress ? Pal.Hex("ff8a8a") : Pal.Hex("fff2b3");
                        map.Marker(s.center, icon, col, zoom > 2.5f ? s.name : null, false);
                    }
            foreach (var gr in w.graves)
                map.Marker(new Vector2(gr.x, gr.y), gr.statue ? "♔" : "✝", gr.recovered ? Pal.Hex("8c7aa8") : Pal.Hex("e6dcff"), zoom > 4 ? gr.heroName : null, false);
            foreach (var q in w.quests.Where(x => x.status == 1 && x.kind != "hunt"))
                map.Marker(new Vector2(q.tx, q.ty), "!", Pal.Gold, q.title, true);
            map.EndMarkers();

            UIKit.Show(playerIcon, g.Player != null);
            if (g.Player != null)
            {
                UIKit.Sprite(playerIcon, Art.Rabbit(g.Player.Costume, Pose.Idle0));
                playerIcon.rectTransform.localPosition = map.WorldToLocal(g.Player.Pos);
            }
        }
    }
}
