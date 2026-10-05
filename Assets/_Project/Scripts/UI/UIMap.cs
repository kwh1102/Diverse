using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// World map: drag to pan, mouse wheel to zoom. Only explored chunks are shown (fog of war).
    /// Map textures are generated as one pixel per tile per chunk and cached.
    /// </summary>
    public partial class UI
    {
        readonly Dictionary<string, Texture2D> mapTiles = new Dictionary<string, Texture2D>();
        Vector2 mapCenter;          // world coordinates
        float mapZoom = 3f;         // screen px per world unit (before scaling)
        bool dragging;
        Vector2 dragStart, dragCenter;

        void MapOpened()
        {
            if (g.Player != null) mapCenter = g.Player.Pos;
        }

        Texture2D MapTile(int cx, int cy)
        {
            string k = cx + "," + cy;
            if (mapTiles.TryGetValue(k, out var t)) return t;
            int N = WorldGen.Chunk;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            var ground = WorldStreamer.I.GroundOf(new Vector2Int(cx, cy));
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Art.GroundColors(ground[x + 1, y + 1], out var a, out var b, out _);
                    px[y * N + x] = Hash.F(cx * N + x, cy * N + y, 9) > 0.8f ? b : a;
                }
            tex.SetPixels32(px);
            tex.Apply();
            if (mapTiles.Count > 3000) { foreach (var v in mapTiles.Values) Destroy(v); mapTiles.Clear(); }
            mapTiles[k] = tex;
            return tex;
        }

        void DrawMap()
        {
            Dim(0.85f);
            var area = new Rect(U(16), U(16), Screen.width - U(32), Screen.height - U(32));
            GUI.Box(area, GUIContent.none, panel);
            var view = new Rect(area.x + U(8), area.y + U(26), area.width - U(16), area.height - U(34));
            Shadowed(new Rect(area.x, area.y + U(6), area.width, U(16)), "세계 지도", labelC, Pal.Hex("fff2b3"));
            Shadowed(new Rect(area.x + U(10), area.y + U(6), area.width - U(20), U(16)), "<color=#8c7aa8>드래그: 이동 · 휠: 확대 · C: 내 위치</color>", small);
            Shadowed(new Rect(area.x, area.y + U(6), area.width - U(10), U(16)), $"<color=#8c7aa8>{Controls.KeyName(Act.Map)}/Esc 닫기</color>", new GUIStyle(small) { alignment = TextAnchor.UpperRight });

            // Input
            var e = Event.current;
            float px = mapZoom * scale;
            if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition))
            {
                // Zoom anchored on the mouse position
                Vector2 before = ScreenToMap(e.mousePosition, view, px);
                mapZoom = Mathf.Clamp(mapZoom * (e.delta.y > 0 ? 0.8f : 1.25f), 0.5f, 24f);
                px = mapZoom * scale;
                Vector2 after = ScreenToMap(e.mousePosition, view, px);
                mapCenter += before - after;
                e.Use();
            }
            if (e.type == EventType.MouseDown && view.Contains(e.mousePosition)) { dragging = true; dragStart = e.mousePosition; dragCenter = mapCenter; e.Use(); }
            if (e.type == EventType.MouseDrag && dragging) { var d = e.mousePosition - dragStart; mapCenter = dragCenter + new Vector2(-d.x, d.y) / px; e.Use(); }
            if (e.type == EventType.MouseUp) dragging = false;
            if (Controls.KeyDown(UnityEngine.InputSystem.Key.C) && g.Player != null) mapCenter = g.Player.Pos;

            GUI.BeginGroup(view);
            var local = new Rect(0, 0, view.width, view.height);
            GUI.DrawTexture(local, whiteTex, ScaleMode.StretchToFill, true, 0, new Color(0.08f, 0.06f, 0.11f), 0, 0);

            // Explored chunks
            int N = WorldGen.Chunk;
            float halfW = view.width / px / 2, halfH = view.height / px / 2;
            int cx0 = Mathf.FloorToInt((mapCenter.x - halfW) / N), cx1 = Mathf.FloorToInt((mapCenter.x + halfW) / N);
            int cy0 = Mathf.FloorToInt((mapCenter.y - halfH) / N), cy1 = Mathf.FloorToInt((mapCenter.y + halfH) / N);
            var explored = new HashSet<string>(g.World.exploredChunks);
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    if (!explored.Contains(cx + "," + cy)) continue;
                    var r = WorldRect(new Vector2(cx * N, (cy + 1) * N), N, N, view, px);
                    GUI.DrawTexture(r, MapTile(cx, cy));
                }

            // Frontier rings: open ones faint, the mist boundary red
            if (g.Player != null)
            {
                int open = WorldGen.OpenRing(g.Player.Level, g.World.bossKills);
                for (int i = 0; i < WorldGen.Rings.Length - 1; i++)
                    DrawRingCircle(WorldGen.Rings[i].radius, view, px, i == open ? new Color(1f, 0.45f, 0.55f, 0.8f) : i < open ? new Color(0.8f, 0.75f, 1f, 0.25f) : new Color(0.6f, 0.55f, 0.7f, 0.35f));
            }

            // Tower (always visible — the long-term goal on the horizon)
            DrawMarker(WorldGen.TowerPos, view, px, "▲", Pal.Hex("c9b8ff"), "기억의 탑", true);

            // Discovered structures
            foreach (var s in DiscoveredStructures(cx0, cx1, cy0, cy1))
            {
                bool cleared = g.World.clearedCamps.Contains(s.id);
                string icon = s.kind switch
                {
                    StructKind.Town => "⌂", StructKind.Camp => cleared ? "○" : "✕", StructKind.Ruins => "♜", StructKind.Shrine => "✧", StructKind.Lair => cleared ? "○" : "☠",
                    StructKind.Graveyard => "†", StructKind.Chest => "▣", StructKind.Wanderer => "?",
                    StructKind.Ferry => "≈", StructKind.Obelisk => cleared ? "○" : "◆", StructKind.Fortress => cleared ? "○" : "♛", _ => "•",
                };
                Color32 col = cleared ? Pal.Hex("8bff9a") : s.kind is StructKind.Camp or StructKind.Lair or StructKind.Graveyard or StructKind.Obelisk or StructKind.Fortress ? Pal.Hex("ff8a8a") : Pal.Hex("fff2b3");
                DrawMarker(s.center, view, px, icon, col, mapZoom > 2.5f ? s.name : null, false);
            }
            // Graves / statues
            foreach (var gr in g.World.graves)
                DrawMarker(new Vector2(gr.x, gr.y), view, px, gr.statue ? "♔" : "✝", gr.recovered ? Pal.Hex("8c7aa8") : Pal.Hex("e6dcff"), mapZoom > 4 ? gr.heroName : null, false);
            // Quest targets
            foreach (var q in g.World.quests.Where(x => x.status == 1 && x.kind != "hunt"))
                DrawMarker(new Vector2(q.tx, q.ty), view, px, "!", Pal.Gold, q.title, true);
            // Player
            if (g.Player != null)
            {
                var sp = WorldToMap(g.Player.Pos, view, px);
                float s = U(12);
                Sprite(new Rect(sp.x - s / 2, sp.y - s, s, s), Art.Rabbit(g.Player.Costume, Pose.Idle0));
            }
            GUI.EndGroup();

            // Scale / coordinates
            var mouseWorld = ScreenToMap(e.mousePosition, view, px);
            Shadowed(new Rect(area.x + U(10), area.yMax - U(18), area.width, U(14)), $"<color=#8c7aa8>확대 ×{mapZoom:0.0} · 커서 ({mouseWorld.x:0}, {mouseWorld.y:0}) · 탐험한 구역 {g.World.exploredChunks.Count}</color>", small);
            if (g.Player != null)
                Shadowed(new Rect(area.x, area.yMax - U(18), area.width - U(10), U(14)), $"<color=#ff8a9a>— 안개 경계</color>  <color=#c9bcd8>{g.FrontierHint()}</color>", new GUIStyle(small) { alignment = TextAnchor.UpperRight });
        }

        /// <summary>A dotted circle around the origin (frontier ring).</summary>
        void DrawRingCircle(float radius, Rect view, float px, Color col)
        {
            if (radius <= 0) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(radius * px / U(6)), 24, 720);
            float dot = Mathf.Max(scale, U(1.5f));
            for (int i = 0; i < n; i++)
            {
                var p = WorldToMap(MathX.Dir(i * 360f / n) * radius, view, px);
                if (p.x < 0 || p.y < 0 || p.x > view.width || p.y > view.height) continue;
                GUI.DrawTexture(new Rect(p.x - dot / 2, p.y - dot / 2, dot, dot), whiteTex, ScaleMode.StretchToFill, true, 0, col, 0, 0);
            }
        }

        IEnumerable<StructureSpec> DiscoveredStructures(int cx0, int cx1, int cy0, int cy1)
        {
            var disc = new HashSet<string>(g.World.discovered);
            int R = WorldGen.RegionChunks;
            for (int ry = MathX.FloorDiv(cy0, R); ry <= MathX.FloorDiv(cy1, R); ry++)
                for (int rx = MathX.FloorDiv(cx0, R); rx <= MathX.FloorDiv(cx1, R); rx++)
                    foreach (var s in World.I.Gen.StructuresInRegion(rx, ry))
                        if (disc.Contains(s.id)) yield return s;
        }

        Vector2 WorldToMap(Vector2 w, Rect view, float px) =>
            new Vector2(view.width / 2 + (w.x - mapCenter.x) * px, view.height / 2 - (w.y - mapCenter.y) * px);

        Vector2 ScreenToMap(Vector2 screen, Rect view, float px) =>
            new Vector2(mapCenter.x + (screen.x - view.x - view.width / 2) / px, mapCenter.y - (screen.y - view.y - view.height / 2) / px);

        Rect WorldRect(Vector2 topLeft, float w, float h, Rect view, float px)
        {
            var p = WorldToMap(topLeft, view, px);
            return new Rect(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Ceil(w * px) + 1, Mathf.Ceil(h * px) + 1);
        }

        void DrawMarker(Vector2 w, Rect view, float px, string icon, Color32 col, string name, bool clampEdge)
        {
            var p = WorldToMap(w, view, px);
            bool outside = p.x < 0 || p.y < 0 || p.x > view.width || p.y > view.height;
            if (outside && !clampEdge) return;
            if (outside) { p.x = Mathf.Clamp(p.x, U(8), view.width - U(8)); p.y = Mathf.Clamp(p.y, U(8), view.height - U(8)); }
            var r = new Rect(p.x - U(8), p.y - U(8), U(16), U(16));
            Shadowed(r, icon, labelC, col);
            if (name != null && !outside) Shadowed(new Rect(p.x - U(60), p.y + U(6), U(120), U(12)), name, smallC, col);
        }

        // ───────────────────────── Minimap ─────────────────────────

        void DrawMinimap(Rect r)
        {
            GUI.Box(r, GUIContent.none, box);
            var inner = new Rect(r.x + U(3), r.y + U(3), r.width - U(6), r.height - U(6));
            var p = g.Player.Pos;
            float px = inner.width / 48f;    // 48 world units visible
            GUI.BeginGroup(inner);
            int N = WorldGen.Chunk;
            int cx0 = Mathf.FloorToInt((p.x - 24) / N), cx1 = Mathf.FloorToInt((p.x + 24) / N);
            int cy0 = Mathf.FloorToInt((p.y - 24) / N), cy1 = Mathf.FloorToInt((p.y + 24) / N);
            var saved = mapCenter; mapCenter = p;
            var view = new Rect(0, 0, inner.width, inner.height);
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                    GUI.DrawTexture(WorldRect(new Vector2(cx * N, (cy + 1) * N), N, N, view, px), MapTile(cx, cy));
            foreach (var a in Actor.All)
            {
                if (a == null || !a.Alive || a.team != Team.Enemy) continue;
                var mp = WorldToMap(a.Pos, view, px);
                if (mp.x < 0 || mp.y < 0 || mp.x > view.width || mp.y > view.height) continue;
                GUI.DrawTexture(new Rect(mp.x - scale, mp.y - scale, scale * 2, scale * 2), whiteTex, ScaleMode.StretchToFill, true, 0, (a as Enemy).def.boss ? Color.magenta : new Color(1, 0.3f, 0.3f), 0, 0);
            }
            foreach (var q in g.World.quests.Where(x => x.status == 1 && x.kind != "hunt")) DrawMarker(new Vector2(q.tx, q.ty), view, px, "!", Pal.Gold, null, true);
            DrawMarker(WorldGen.TowerPos, view, px, "▲", Pal.Hex("c9b8ff"), null, true);
            var c = WorldToMap(p, view, px);
            GUI.DrawTexture(new Rect(c.x - scale * 1.5f, c.y - scale * 1.5f, scale * 3, scale * 3), whiteTex, ScaleMode.StretchToFill, true, 0, Color.white, 0, 0);
            mapCenter = saved;
            GUI.EndGroup();
        }
    }
}
