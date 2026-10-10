using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// World map overlay: drag to pan, wheel to zoom (anchored on the cursor), C to recenter.
    /// Right-click an explored spot to walk there. Click a warp stone (while standing at another) to travel instantly.
    /// Only explored chunks are drawn (fog of war); frontier rings, structures, graves and quest targets are markers.
    /// </summary>
    public class MapView : MonoBehaviour
    {
        [SerializeField] MapCanvas map;
        [SerializeField] Text closeHint, footerLeft, footerRight;
        [SerializeField] Image playerIcon;
        [SerializeField] float minZoom = 0.5f, maxZoom = 24f;
        [Tooltip("Pulsing ring drawn under the player icon so the hero is easy to find.")]
        [SerializeField] Color playerHighlight = new Color(1f, 0.92f, 0.3f, 1f);
        [Tooltip("How close (canvas px) a click must be to a warp stone icon to pick it.")]
        [SerializeField] float warpPickRadius = 10f;

        float zoom = 3f;
        bool dragging, dragMoved;
        Vector2 dragStart, dragCenter;
        Game g;
        readonly HashSet<string> explored = new HashSet<string>();
        int exploredCount = -1;

        public void Init(Game game) => g = game;

        public void Opened() { if (g.Player != null) map.Center = g.Player.Pos; }

        /// <summary>Current zoom (canvas px per world unit), clamped to the inspector range.</summary>
        public float Zoom { get => zoom; set => zoom = Mathf.Clamp(value, minZoom, maxZoom); }

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
            // Left: drag to pan; a click without dragging picks a warp stone
            if (Controls.LeftDown && inside) { dragging = true; dragMoved = false; dragStart = mouse; dragCenter = map.Center; }
            if (dragging && Controls.LeftHeld)
            {
                if ((mouse - dragStart).sqrMagnitude > 16) dragMoved = true;
                map.Center = dragCenter - (mouse - dragStart) / zoom;
            }
            if (dragging && !Controls.LeftHeld)
            {
                dragging = false;
                if (!dragMoved && inside) ClickAt(mouse, false);
            }
            // Right: walk there (or travel, on a warp stone)
            if (inside && Controls.Down(Act.Move)) ClickAt(mouse, true);
            if (Controls.KeyDown(UnityEngine.InputSystem.Key.C) && g.Player != null) map.Center = g.Player.Pos;
            if (g.State != GameState.Map) return;   // a click above closed the map

            map.PixelsPerUnit = zoom;
            if (exploredCount != g.World.exploredChunks.Count)
            {
                explored.Clear();
                foreach (var k in g.World.exploredChunks) explored.Add(k);
                exploredCount = g.World.exploredChunks.Count;
            }
            map.Paint((cx, cy) => explored.Contains(cx + "," + cy));
            map.DrawRoute(g.Player);
            Markers();

            UIKit.Set(closeHint, $"{Controls.KeyName(Act.Map)}/Esc 닫기");
            var mw = map.LocalToWorld(mouse);
            var hoverWarp = inside ? WarpAt(mouse) : null;
            string hint = hoverWarp != null ? $"<color=#8fe3ff>워프 스톤: {hoverWarp.name}</color> — 클릭해 이동"
                        : inside ? (IsExplored(mw) ? "우클릭: 여기로 이동" : "<color=#8c7aa8>밝혀지지 않은 곳</color>") : "";
            UIKit.Set(footerLeft, $"확대 ×{zoom:0.0} · ({mw.x:0}, {mw.y:0}) · {hint}");
            UIKit.Set(footerRight, g.Player != null ? $"<color=#ff8a9a>— 안개 경계</color>  <color=#c9bcd8>{g.FrontierHint()}</color>" : "");
        }

        bool IsExplored(Vector2 world)
        {
            int N = WorldGen.Chunk;
            return explored.Contains(MathX.FloorDiv(Mathf.FloorToInt(world.x), N) + "," + MathX.FloorDiv(Mathf.FloorToInt(world.y), N));
        }

        WarpStone WarpAt(Vector2 local)
        {
            WarpStone best = null; float bd = warpPickRadius;
            foreach (var w in g.WarpStones())
            {
                float d = Vector2.Distance(map.WorldToLocal(w.Pos), local);
                if (d < bd) { bd = d; best = w; }
            }
            return best;
        }

        void ClickAt(Vector2 local, bool right)
        {
            if (g.Player == null) return;
            var warp = WarpAt(local);
            if (warp != null)
            {
                if (!g.TryWarp(warp, out var why)) { g.Ui.Toast(why); Sfx.Play("ui", 0.4f, 0.7f); }
                else g.InputConsumedFrame = Time.frameCount;
                return;
            }
            if (!right) return;
            var world = map.LocalToWorld(local);
            if (!IsExplored(world)) { g.Ui.Toast("아직 밝혀지지 않은 곳이다"); Sfx.Play("ui", 0.4f, 0.7f); return; }
            g.Player.SetTravelTarget(world);
            g.InputConsumedFrame = Time.frameCount;   // the same right-click must not also act in the world
            g.CloseOverlay();
            g.Ui.Toast($"이동 목표: ({world.x:0}, {world.y:0}) · {Vector2.Distance(world, g.Player.Pos):0}m");
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
                        // Only glyphs that exist in Galmuri: a missing glyph falls back to an OS font with other metrics
                        // and is drawn off-center from its spot
                        string icon = s.kind switch
                        {
                            StructKind.Town => "◘", StructKind.Camp => cleared ? "○" : "×", StructKind.Ruins => "▥", StructKind.Shrine => "◇", StructKind.Lair => cleared ? "○" : "♠",
                            StructKind.Graveyard => "†", StructKind.Chest => "▣", StructKind.Wanderer => "?",
                            StructKind.Ferry => "≈", StructKind.Obelisk => cleared ? "○" : "◆", StructKind.Fortress => cleared ? "○" : "▦", _ => "•",
                        };
                        Color32 col = cleared ? Pal.Hex("8bff9a") : s.kind is StructKind.Camp or StructKind.Lair or StructKind.Graveyard or StructKind.Obelisk or StructKind.Fortress ? Pal.Hex("ff8a8a") : Pal.Hex("fff2b3");
                        map.Marker(s.center, icon, col, zoom > 2.5f ? s.name : null, false);
                    }
            foreach (var gr in w.graves)
                map.Marker(new Vector2(gr.x, gr.y), gr.statue ? "Ω" : "†", gr.recovered ? Pal.Hex("8c7aa8") : Pal.Hex("e6dcff"), zoom > 4 ? gr.heroName : null, false);
            foreach (var q in w.quests.Where(x => x.status == 1 && x.kind != "hunt"))
                map.Marker(new Vector2(q.tx, q.ty), "!", Pal.Gold, q.title, true);
            // Warp stones: bright when the hero can travel from here
            var here = g.WarpStoneInReach();
            foreach (var ws in g.WarpStones())
                map.Marker(ws.Pos, "◈", here != null && here.id != ws.id ? Pal.Hex("8fe3ff") : Pal.Hex("5a8aa8"), zoom > 1.5f || here != null ? ws.name : null, false, 13);
            // Travel target
            if (g.Player != null && g.Player.TravelTarget.HasValue)
                map.Marker(g.Player.TravelTarget.Value, "×", Pal.Gold, "목표", true, 13);

            // The hero: a pulsing ring + arrow on top of everything, then the portrait
            if (g.Player != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                var ring = playerHighlight; ring.a = 0.55f + 0.45f * pulse;
                map.Marker(g.Player.Pos, "◎", ring, null, true, 20 + Mathf.RoundToInt(pulse * 4));
                map.Marker(g.Player.Pos + new Vector2(0, 12f / zoom), "▼", playerHighlight, "나", true, 11);
            }
            map.EndMarkers();

            UIKit.Show(playerIcon, g.Player != null);
            if (g.Player != null)
            {
                UIKit.Sprite(playerIcon, Art.Rabbit(g.Player.Costume, Pose.Idle0));
                var p = map.WorldToLocal(g.Player.Pos);
                var r = map.Rect.rect;
                p.x = Mathf.Clamp(p.x, r.xMin + 6, r.xMax - 6); p.y = Mathf.Clamp(p.y, r.yMin + 6, r.yMax - 6);
                playerIcon.rectTransform.localPosition = p;
                playerIcon.transform.SetAsLastSibling();   // above every marker
            }
        }
    }
}
