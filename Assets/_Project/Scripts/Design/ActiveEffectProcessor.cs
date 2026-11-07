using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Four frames per row; preserve the cell centre and one shared scale for each animation.</summary>
    public static class ActiveEffectProcessor
    {
        public static PixelCanvas[] Read(Texture2D sheet, int[] rows, int steps, ActiveRules cfg, Action<string> trace, out string why, ActiveSkillData skill = null)
        {
            why = "이펙트 시트 형식 오류 · 4열 시트와 행 정보를 읽지 못함";
            if (sheet == null || !sheet.isReadable || rows == null || rows.Length == 0 || sheet.width % 4 != 0 ||
                rows.Distinct().Count() != rows.Length || rows.Any(r => r < 0 || r >= steps * 3)) return null;
            int w = sheet.width / 4, h = sheet.height / rows.Length, pixels = cfg.effectPixels;
            if (w < pixels || h < pixels || pixels <= 6) { why = "이펙트 셀 해상도 부족 · 프레임을 구분할 수 없음"; return null; }
            var source = sheet.GetPixels32();
            var result = new PixelCanvas[steps * 12];
            int accepted = 0;
            for (int row = 0; row < rows.Length; row++)
            {
                int key = rows[row], motif = key % 3;
                var step = skill?.steps[key / 3];
                bool flight = motif == 2 && step?.delivery == "projectile";
                var frames = ReadRow(sheet.width, sheet.height, source, row, w, h, cfg, motif, flight, out string reason);
                if (frames != null && step != null)
                {
                    SnapPalette(frames);
                    if (!Usable(frames.Select(f => f.Px).ToArray(), pixels, step, motif, cfg, out reason))
                        frames = null;
                }
                string label = $"이펙트 {row + 1}행 (단계 {key / 3 + 1}, {(motif == 0 ? "동작" : motif == 1 ? "명중" : "궤적")})";
                if (frames == null)
                {
                    why = $"{label}: {reason}";
                    if (skill == null) return null;
                    trace?.Invoke(why + " · 내장 모션 적용");
                    continue;
                }
                Array.Copy(frames, 0, result, key * 4, 4);
                accepted++;
                trace?.Invoke($"{label}: {reason}");
            }
            if (accepted == 0)
            {
                why = "사용 가능한 전용 이펙트 행 없음 · 기존 스킬 유지";
                return null;
            }
            // The legacy basic-attack sheet supplies all four rows and uses the original layout.
            if (skill == null)
                for (int step = 0; step < steps; step++)
                {
                    if (result[step * 12] == null) { why = $"단계 {step + 1}: 필수 동작 행 누락"; return null; }
                    for (int frame = 0; frame < 4; frame++) result[step * 12 + 4 + frame] ??= result[step * 12 + frame];
                    for (int frame = 0; frame < 4; frame++) result[step * 12 + 8 + frame] ??= result[step * 12 + frame];
                }
            why = $"전용 이펙트 {accepted}/{rows.Length}행 적용 · 대체가 필요한 행은 내장 모션 사용";
            return result;
        }

        static PixelCanvas[] ReadRow(int width, int height, Color32[] source, int row, int w, int h,
            ActiveRules cfg, int motif, bool flight, out string why)
        {
            int pixels = cfg.effectPixels;
            var cells = new Color32[4][]; var present = new bool[4];
            float extentX = 0, extentY = 0; int cleaned = 0;
            for (int frame = 0; frame < 4; frame++)
            {
                var px = cells[frame] = new Color32[w * h];
                int ox = frame * w, oy = height - (row + 1) * h;
                for (int y = 0; y < h; y++) Array.Copy(source, (oy + y) * width + ox, px, y * w, w);
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
                { why = $"{frame + 1}프레임: 배경 제거 후에도 불투명 면적 {solid * 100f / (w * h):0}% · 배경 또는 셀 배치 확인 필요"; return null; }
                if (edge > (w + h - 2) * 2 * cfg.effectEdgeLimit)
                { why = $"{frame + 1}프레임: 경계 접촉 {edge}px · 잘린 그림 또는 이웃 셀 침범 의심"; return null; }
                present[frame] = solid >= cfg.effectMinPixels;
            }
            if (present.Count(v => v) < (flight ? 4 : 2))
            { why = $"유효 프레임 {present.Count(v => v)}/4 · 애니메이션에 필요한 그림 부족"; return null; }
            // A single scale about the original cell centre retains intentional motion and registration.
            float scale = (pixels - 6) / (2 * Mathf.Max(extentX, extentY));
            var frames = new PixelCanvas[4]; var hashes = new HashSet<uint>(); var silhouettes = new HashSet<uint>();
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
                    int area = Mathf.Max(1,(x1-x0)*(y1-y0));
                    // Max-alpha sampling turned isolated bright source pixels into opaque chunky noise.
                    byte coverage = (byte)Mathf.Min(alpha, Mathf.RoundToInt(weight / (float)area));
                    if (coverage >= 64) cv.Px[y * pixels + x] = new Color32((byte)(red / weight), (byte)(green / weight), (byte)(blue / weight), coverage);
                }
                if (cv.Px.Count(p => p.a >= 32) < cfg.effectMinPixels)
                { why = $"{frame + 1}프레임: 도트 변환 후 그림이 너무 작음"; return null; }
                uint hash = 2166136261;
                foreach (var p in cv.Px) hash = (hash ^ p.r ^ ((uint)p.g << 8) ^ ((uint)p.b << 16) ^ ((uint)p.a << 24)) * 16777619;
                hashes.Add(hash);
                uint silhouette=2166136261;
                foreach(var p in cv.Px)silhouette=(silhouette^(p.a>=128?1u:0u))*16777619;
                silhouettes.Add(silhouette);
            }
            if (hashes.Count < 2) { why = $"유효 프레임이 같은 정지 그림 · 다른 동작 프레임 필요"; return null; }
            if (motif != 2 && silhouettes.Count < 2)
            {why=$"색만 바뀌는 정지 아이콘 · 형태가 움직이는 동작 프레임 필요";return null;}
            // Preserve faded entrances and exits. Non-flight rows can reuse the nearest middle pose.
            for (int frame = 0; frame < 4; frame++) if (!present[frame] && (motif == 2 || frame == 1 || frame == 2))
            {
                int nearest = Enumerable.Range(0, 4).Where(i => present[i]).OrderBy(i => Math.Abs(i - frame)).First();
                frames[frame] = frames[nearest];
            }
            why = $"배경 {cleaned}px 정리 · 유효 {present.Count(v => v)}/4프레임 · 공통 중심·크기 유지";
            return frames;
        }

        static void SnapPalette(PixelCanvas[] frames)
        {
            // One palette for the WHOLE row avoids frame-to-frame colour flicker. No geometry is regenerated.
            var palette=frames.SelectMany(f=>f.Px).Where(p=>p.a>=96)
                .GroupBy(p=>(p.r/24,p.g/24,p.b/24)).OrderByDescending(g=>g.Count()).Take(12).Select(g=>g.First()).ToArray();
            if(palette.Length==0)return;
            foreach(var frame in frames)for(int i=0;i<frame.Px.Length;i++)
            {
                var p=frame.Px[i];if(p.a<96){frame.Px[i]=default;continue;}
                var nearest=palette.OrderBy(c=>(c.r-p.r)*(c.r-p.r)+(c.g-p.g)*(c.g-p.g)+(c.b-p.b)*(c.b-p.b)).First();
                nearest.a=255;frame.Px[i]=nearest;
            }
        }

        internal static bool Usable(Color32[][] frames, int size, ActiveStep step, int motif, ActiveRules cfg, out string reason)
        {
            bool flight = motif == 2 && step.delivery == "projectile";
            bool strike = motif == 0 && step.delivery == "arc";
            bool line = motif == 0 && step.delivery == "line";
            int peak = 0, present = 0, peakLeft = 0, peakWidth = 0, peakHeight = 0;
            Vector2 peakCenter = Vector2.zero, flightCenter = Vector2.zero;
            Color32[] first = null;
            bool changesShape = false;
            foreach (var frame in frames)
            {
                int count = 0, connected = 0, left = 0;
                int minX = size, maxX = -1, minY = size, maxY = -1;
                var sum = Vector2.zero;
                bool core = false;
                for (int i = 0; i < frame.Length; i++)
                {
                    if (frame[i].a < 128) continue;
                    int x = i % size, y = i / size;
                    count++;
                    sum += new Vector2(x + .5f, y + .5f);
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    if (x < size / 2) left++;
                    bool right = x + 1 < size && frame[i + 1].a >= 128;
                    bool above = y + 1 < size && frame[i + size].a >= 128;
                    if (right || above || (x > 0 && frame[i - 1].a >= 128) || (y > 0 && frame[i - size].a >= 128))
                        connected++;
                    if (right && above && frame[i + size + 1].a >= 128) core = true;
                }
                if (count > size * size * cfg.effectMaxNativeCoverage)
                { reason = "도트 변환 후 화면을 가리는 불투명 덩어리"; return false; }
                if (count < cfg.effectMinPixels)
                {
                    if (flight) { reason = "비행 중 탄 본체가 사라지는 프레임"; return false; }
                    continue;
                }
                present++;
                var center = sum / count - Vector2.one * (size * .5f);
                if (connected * 3 < count * 2)
                { reason = "연결된 형태보다 고립된 픽셀 노이즈가 많음"; return false; }
                if (flight)
                {
                    if (present == 1) flightCenter = center;
                    if (!core || center.magnitude > size * cfg.effectAnchorTolerance ||
                        Vector2.Distance(center, flightCenter) > size * cfg.effectAnchorTolerance * .5f)
                    { reason = "비행 본체가 가늘거나 충돌 중심에서 흔들림"; return false; }
                }
                if (count > peak)
                {
                    peak = count; peakLeft = left; peakCenter = center;
                    peakWidth = maxX - minX + 1; peakHeight = maxY - minY + 1;
                }
                if (first == null) first = frame;
                else if (!flight && !changesShape)
                {
                    int changed = 0;
                    for (int i = 0; i < frame.Length; i++)
                        if ((first[i].a >= 128) != (frame[i].a >= 128)) changed++;
                    changesShape = changed >= cfg.effectMinPixels;
                }
            }
            if (present < 2 || (!flight && !changesShape))
            { reason = "도트 변환 후 구분되는 동작 프레임 부족"; return false; }
            if (strike ? peakLeft > peak * .57f : peakCenter.magnitude > size * cfg.effectAnchorTolerance)
            { reason = "주요 획이 지정된 기준점 또는 공격 방향을 벗어남"; return false; }
            if (line && peakWidth < peakHeight * 1.25f)
            { reason = "직선 타격 실루엣이 가로 방향과 맞지 않음"; return false; }
            reason = "네이티브 픽셀 크기에서 형태와 기준점 검사 통과";
            return true;
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
