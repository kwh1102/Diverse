using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diverse
{
    /// <summary>One paid sheet for a selected skill. Frames retain a common cell canvas and pivot.</summary>
    public static class ActiveEffectArt
    {
        class Entry { public Sprite[] frames; public bool ownsTextures; }
        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Clear(); SceneManager.sceneUnloaded -= Unloaded; SceneManager.sceneUnloaded += Unloaded; }
        static void Unloaded(Scene scene) => Clear();
        static void Clear() { foreach (var entry in cache.Values) Release(entry,0); cache.Clear(); }
        static void Release(Entry entry, float delay)
        {
            foreach (var s in entry.frames) if (s != null)
            { if (entry.ownsTextures) UnityEngine.Object.Destroy(s.texture,delay); UnityEngine.Object.Destroy(s,delay); }
        }
        public static bool Ready(ActiveSkillData a) => a != null && a.effectRevision == 1 && a.effectFrames == 4 &&
            a.effectImages?.Length == a.steps.Count * 3 * a.effectFrames && a.effectImages.All(id => DesignStore.Image(id) != null);
        const int ProcessorRevision = 2;
        static string SourceSignature(ActiveSkillData a) => a.input + ":" + string.Join("|", a.steps.Select(s => JsonUtility.ToJson(s)));
        static bool HasSource(ActiveSkillData a) => a != null && a.effectSourceSignature == SourceSignature(a) &&
            a.effectSourceRows?.Length > 0 && DesignStore.ImagePersisted(a.effectSourceImage);
        public static bool CanReprocess(ActiveSkillData a) => a != null && a.effectProcessedRevision != ProcessorRevision && HasSource(a);
        public static bool ReusesSavedArt(ActiveSkillData a) => Ready(a) || CanReprocess(a);
        static int[] Plan(ActiveSkillData a)
        {
            var rows = new List<int>();
            for (int i = 0; i < a.steps.Count; i++)
            {
                var step = a.steps[i]; rows.Add(i * 3);
                if (step.effect != "shield" && step.effect != "heal" && step.effect != "block") rows.Add(i * 3 + 1);
                if (step.delivery == "projectile" || ActiveSkillGenerator.Moving(step)) rows.Add(i * 3 + 2);
            }
            return rows.ToArray();
        }
        public static IEnumerator Prepare(ActiveSkillData a, Action<string> trace, Action<bool> done, Action persist = null, bool fresh = false)
        {
            if (Ready(a)) { trace?.Invoke("저장된 전용 이펙트 재사용 · 이미지 요청 없음"); done(true); yield break; }
            var cfg = ActiveSkillGenerator.Rules;
            Texture2D sheet = null; bool ownsSheet = false;
            int[] rows = null;
            if (!fresh && HasSource(a))
            {
                try { sheet = DesignStore.Image(a.effectSourceImage); rows = a.effectSourceRows; }
                catch (Exception error) { trace?.Invoke("원본 시트 읽기 실패: " + error.Message); }
                if (sheet == null)
                {
                    a.effectProcessedRevision = ProcessorRevision; persist?.Invoke();
                    trace?.Invoke("저장된 원본 시트가 손상되어 재처리 불가 · 자동 유료 재요청 없음"); done(false); yield break;
                }
                trace?.Invoke("저장된 원본 이펙트 시트 재처리 · 이미지 API 비용 없음");
            }
            if (sheet == null)
            {
                if (!AiClient.Enabled) { trace?.Invoke("AI 연결이 없어 전용 이펙트를 제작할 수 없음 · 현재 스킬 유지"); done(false); yield break; }
                rows = Plan(a);
                string size = "1024x1024";
                string prompt = $"Transparent PNG pixel animation sheet, exactly FOUR columns and {rows.Length} equal-height rows, all cells used. Canvas {size}. No grid lines, text, background, checkerboard or badges. " +
                    "Each ROW is ONE four-frame animation, read LEFT TO RIGHT: anticipation, expansion, peak, dissipation. First/last frames may fade to transparency; middle frames must show distinct animation. " +
                    "Use consistent centered pivot and apparent scale within each row, keep all art inside its cell with 15 percent transparent gutters. Motion faces RIGHT. Cells may be rectangular; preserve natural effect proportions and fit inside the shorter cell dimension. " +
                    "Polished cute top-down pixel action RPG effects, chunky silhouettes, 3-4 tones, muted purple edges, crisp highlights, restrained sparks. No skill icons, scenery or painted light blobs. " +
                    string.Join(" ", rows.Select((r, i) => {
                        var s = a.steps[r / 3]; string motif = r % 3 == 0 ? "casting/action" : r % 3 == 1 ? "impact/activation burst" : s.delivery == "projectile" ? "flying projectile body with short trail, visible in ALL four looping frames" : "movement trail";
                        return $"Row {i + 1}: {motif} for step {r / 3 + 1}: {s.delivery}/{s.pattern}/{s.effect}/{s.element}.";
                    })) + $" Executable skill: {ActiveSkillGenerator.VisualSummary(a)}.";
                var world = Game.I?.World;
                trace?.Invoke($"선택한 [{a.name}] 전용 이펙트 {rows.Length}행×4프레임 1장 제작 · {cfg.imageModel}/{cfg.effectQuality}/{size} · 필요한 동작만 요청");
                yield return ImageClient.Generate(prompt, t => sheet = t, size: size, model: cfg.imageModel, quality: cfg.effectQuality, usage: usage =>
                {
                    float? cost = usage?.output_tokens > 0 ? usage.CostUsd(cfg.imageModel) : null;
                    bool measured = cost.HasValue; cost ??= ImageClient.PriceUsd(cfg.imageModel, cfg.effectQuality);
                    if (world != null) { world.designCalls++; if (cost.HasValue) world.designCostUsd += cost.Value; else world.designUnknownCosts++; }
                    trace?.Invoke(cost.HasValue ? $"이펙트 비용 {(measured ? "usage" : "추정")} ${cost.Value:0.00000}" : "이펙트 비용 미상 · 무료로 집계하지 않음");
                });
                if (sheet == null) { trace?.Invoke("이펙트 요청 실패: " + ImageClient.LastStatus + " · 자동 유료 재시도 없음"); done(false); yield break; }
                ownsSheet = true;
            }
            bool success = false;
            try
            {
                if (ownsSheet)
                {
                    a.effectSourceImage = DesignStore.SaveSourceImage(sheet);
                    a.effectSourceSignature = SourceSignature(a); a.effectSourceRows = rows; a.effectProcessedRevision = 0;
                    persist?.Invoke();
                    trace?.Invoke(DesignStore.ImagePersisted(a.effectSourceImage) ? "유료 응답 원본 시트 저장 완료 · 처리 규칙 변경 시 무료 재처리 가능" : "원본 시트 저장 실패 · 재실행 시 재사용 불가");
                }
                a.effectProcessedRevision = 0;
                var canvases = ActiveEffectProcessor.Read(sheet, rows, a.steps.Count, cfg, trace, out string why);
                trace?.Invoke(why);
                if (canvases != null)
                {
                    // Keep the canonical 12 frames per step so existing saves/renderers remain compatible.
                    var saved = new Dictionary<PixelCanvas, string>();
                    var ids = canvases.Select(cv => { if (!saved.TryGetValue(cv, out string id)) saved[cv] = id = DesignStore.SaveImage(cv); return id; }).ToArray();
                    if (ids.All(DesignStore.ImagePersisted))
                    {
                        a.effectImages = ids; a.effectFrames = 4; a.effectRevision = 1;
                        a.effectProcessedRevision = ProcessorRevision;
                        a.artStatus = "전용 아이콘·AI 전투 이펙트 준비 완료"; success = Ready(a);
                    }
                    else trace?.Invoke("전용 이미지 파일 저장 실패 · 스킬 장착 보류");
                }
                else a.effectProcessedRevision = ProcessorRevision;
            }
            catch (Exception error) { trace?.Invoke("이펙트 처리 실패 · 현재 스킬 유지: " + error.Message); }
            finally { if (ownsSheet) UnityEngine.Object.Destroy(sheet); persist?.Invoke(); }
            done(success);
        }
        public static Sprite[] Frames(ActiveSkillData a, ActiveStep step, int motif, int outputPixels = 0)
        {
            if (a.effectRevision != 1 || a.effectFrames != 4 || a.effectImages?.Length != a.steps.Count * 12) return null;
            int index = a.steps.IndexOf(step); if (index < 0) return null;
            motif = motif == 1 ? 1 : motif == 2 ? 2 : 0;
            string key = a.id + ":" + a.effectImages[0] + ":" + index + ":" + motif + ":" + outputPixels;
            if (cache.TryGetValue(key, out var found)) return found.frames;
            var frames = a.effectImages.Skip(index * 12 + motif * 4).Take(4).Select(id => DesignStore.Sprite(id, new Vector2(.5f,.5f))).ToArray();
            if (frames.Any(s => s == null)) { foreach (var s in frames) if (s != null) UnityEngine.Object.Destroy(s); return null; }
            if (outputPixels > 0)
                for (int i = 0; i < frames.Length; i++)
                {
                    var tex = frames[i].texture; var source = tex.GetPixels32(); var cv = new PixelCanvas(outputPixels, outputPixels);
                    for (int y = 0; y < outputPixels; y++) for (int x = 0; x < outputPixels; x++) cv.Set(x,y,source[(y * tex.height / outputPixels) * tex.width + x * tex.width / outputPixels]);
                    UnityEngine.Object.Destroy(frames[i]); frames[i] = cv.ToSpriteCentered();
                }
            if (cache.Count >= Mathf.Max(1, ActiveSkillGenerator.Rules.visualCacheLimit))
            {
                var oldest = cache.First(); cache.Remove(oldest.Key);
                // Existing casts/particles can finish using their captured frames before eviction destroys them.
                Release(oldest.Value, ActiveSkillGenerator.Rules.maxCastDuration + ActiveSkillGenerator.Rules.effectDuration);
            }
            cache[key] = new Entry { frames = frames, ownsTextures = outputPixels > 0 };
            return frames;
        }
    }
}
