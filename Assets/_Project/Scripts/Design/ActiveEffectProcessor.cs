using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Four frames per row; preserve the cell centre and one shared scale for each animation.</summary>
    public static class ActiveEffectProcessor
    {
        public static PixelCanvas[] Read(Texture2D sheet, int[] rows, int steps, ActiveRules cfg, Action<string> trace, out string why)
        {
            why = "이펙트 시트 형식 오류 · 4열 시트와 행 정보를 읽지 못함";
            if (sheet == null || !sheet.isReadable || rows == null || rows.Length == 0 ||
                rows.Distinct().Count() != rows.Length || rows.Any(r => r < 0 || r >= steps * 3)) return null;
            int w = sheet.width / 4, h = sheet.height / rows.Length, pixels = cfg.effectPixels;
            if (w < pixels || h < pixels || pixels <= 6) { why = "이펙트 셀 해상도 부족 · 프레임을 구분할 수 없음"; return null; }
            var source = sheet.GetPixels32(); var result = new PixelCanvas[steps * 12];
            for (int row = 0; row < rows.Length; row++)
            {
                int key = rows[row]; string label = $"이펙트 {row + 1}행 (단계 {key / 3 + 1}, {(key % 3 == 0 ? "동작" : key % 3 == 1 ? "명중" : "궤적")})";
                var cells = new Color32[4][]; var present = new bool[4];
                float extentX = 0, extentY = 0; int cleaned = 0;
                for (int frame = 0; frame < 4; frame++)
                {
                    var px = cells[frame] = new Color32[w * h];
                    int ox = frame * w, oy = sheet.height - (row + 1) * h;
                    for (int y = 0; y < h; y++) Array.Copy(source, (oy + y) * sheet.width + ox, px, y * w, w);
                    cleaned += RemoveMatte(px, w, h, cfg);
                    int solid = 0, edge = 0;
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    {
                        if (px[y * w + x].a < 32) continue;
                        solid++;
                        if (x == 0 || y == 0 || x == w - 1 || y == h - 1) edge++;
                        extentX = Mathf.Max(extentX, Mathf.Abs(x + .5f - w * .5f) + .5f);
                        extentY = Mathf.Max(extentY, Mathf.Abs(y + .5f - h * .5f) + .5f);
                    }
                    if (solid > w * h * cfg.effectSolidLimit)
                    { why = $"{label} / {frame + 1}프레임: 배경 제거 후에도 불투명 면적 {solid * 100f / (w * h):0}% · 배경 또는 셀 배치 확인 필요"; return null; }
                    if (edge > (w + h - 2) * 2 * cfg.effectEdgeLimit)
                    { why = $"{label} / {frame + 1}프레임: 경계 접촉 {edge}px · 잘린 그림 또는 이웃 셀 침범 의심"; return null; }
                    present[frame] = solid >= cfg.effectMinPixels;
                }
                if (present.Count(v => v) < 2)
                { why = $"{label}: 유효 프레임 {present.Count(v => v)}/4 · 애니메이션에 필요한 그림 부족"; return null; }
                // A single scale about the original cell centre retains intentional motion and registration.
                float scale = (pixels - 6) / (2 * Mathf.Max(extentX, extentY));
                var frames = new PixelCanvas[4]; var hashes = new HashSet<uint>();
                for (int frame = 0; frame < 4; frame++)
                {
                    var cv = frames[frame] = new PixelCanvas(pixels, pixels);
                    if (!present[frame]) continue; // Faded entrance/exit frames are legitimate.
                    for (int y = 3; y < pixels - 3; y++) for (int x = 3; x < pixels - 3; x++)
                    {
                        int x0 = Mathf.Max(0, Mathf.FloorToInt((x - pixels * .5f) / scale + w * .5f));
                        int x1 = Mathf.Min(w, Mathf.CeilToInt((x + 1 - pixels * .5f) / scale + w * .5f));
                        int y0 = Mathf.Max(0, Mathf.FloorToInt((y - pixels * .5f) / scale + h * .5f));
                        int y1 = Mathf.Min(h, Mathf.CeilToInt((y + 1 - pixels * .5f) / scale + h * .5f));
                        // Coverage sampling keeps thin arcs that nearest-neighbour sampling would erase.
                        long red = 0, green = 0, blue = 0, weight = 0; byte alpha = 0;
                        for (int sy = y0; sy < y1; sy++) for (int sx = x0; sx < x1; sx++)
                        {
                            var p = cells[frame][sy * w + sx]; if (p.a < 32) continue;
                            red += p.r * p.a; green += p.g * p.a; blue += p.b * p.a; weight += p.a; alpha = Math.Max(alpha, p.a);
                        }
                        if (weight > 0) cv.Px[y * pixels + x] = new Color32((byte)(red / weight), (byte)(green / weight), (byte)(blue / weight), alpha);
                    }
                    if (cv.Px.Count(p => p.a >= 32) < cfg.effectMinPixels)
                    { why = $"{label} / {frame + 1}프레임: 도트 변환 후 그림이 너무 작음"; return null; }
                    uint hash = 2166136261;
                    foreach (var p in cv.Px) hash = (hash ^ p.r ^ ((uint)p.g << 8) ^ ((uint)p.b << 16) ^ ((uint)p.a << 24)) * 16777619;
                    hashes.Add(hash);
                }
                if (hashes.Count < 2) { why = $"{label}: 유효 프레임이 같은 정지 그림 · 다른 동작 프레임 필요"; return null; }
                // Preserve faded entrances/exits, but repair missing middle/loop frames from this same AI animation.
                for (int frame = 0; frame < 4; frame++) if (!present[frame] && (key % 3 == 2 || frame == 1 || frame == 2))
                {
                    int nearest = Enumerable.Range(0, 4).Where(i => present[i]).OrderBy(i => Math.Abs(i - frame)).First();
                    frames[frame] = frames[nearest];
                }
                for (int frame = 0; frame < 4; frame++) result[key * 4 + frame] = frames[frame];
                trace?.Invoke($"{label}: 배경 {cleaned}px 정리 · 유효 {present.Count(v => v)}/4프레임 · 공통 중심·크기 유지");
            }
            for (int step = 0; step < steps; step++)
            {
                if (result[step * 12] == null) { why = $"단계 {step + 1}: 필수 동작 행 누락"; return null; }
                for (int frame = 0; frame < 4; frame++) result[step * 12 + 4 + frame] ??= result[step * 12 + frame];
                // A non-projectile step needs no separate flying body. Reuse its AI action frames if requested by an older renderer.
                for (int frame = 0; frame < 4; frame++) result[step * 12 + 8 + frame] ??= result[step * 12 + frame];
            }
            why = $"전용 이펙트 {rows.Length}개 애니메이션 처리 완료 · 동작/명중 분리, 필요 시 투사체·궤적 포함";
            return result;
        }

        static int RemoveMatte(Color32[] px, int w, int h, ActiveRules cfg)
        {
            var border = new List<int>();
            for (int x = 0; x < w; x++) { border.Add(x); border.Add((h - 1) * w + x); }
            for (int y = 1; y < h - 1; y++) { border.Add(y * w); border.Add(y * w + w - 1); }
            // Only opaque-border cells need matte removal; preserve genuine translucent glow on alpha sheets.
            if (border.Count(i => px[i].a > 128) <= border.Count / 2) return 0;
            var keys = border.Where(i => px[i].a > 128).GroupBy(i => (px[i].r / 24, px[i].g / 24, px[i].b / 24))
                .Where(g => g.Count() >= border.Count * cfg.iconMatteBorderShare).OrderByDescending(g => g.Count()).Take(3)
                .Select(g => px[g.First()]).ToArray();
            if (keys.Length == 0) return 0;
            var seen = new bool[px.Length]; var todo = new Stack<int>(border); int removed = 0;
            while (todo.Count > 0)
            {
                int i = todo.Pop(); if (seen[i]) continue; seen[i] = true; var p = px[i];
                if (p.a >= 32 && !keys.Any(c => Math.Abs(c.r - p.r) + Math.Abs(c.g - p.g) + Math.Abs(c.b - p.b) <= cfg.effectMatteTolerance)) continue;
                if (p.a > 0) removed++; px[i] = default;
                int x = i % w, y = i / w;
                if (x > 0) todo.Push(i - 1); if (x + 1 < w) todo.Push(i + 1);
                if (y > 0) todo.Push(i - w); if (y + 1 < h) todo.Push(i + w);
            }
            return removed;
        }
    }
}
