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
        static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Clear(); SceneManager.sceneUnloaded -= Unloaded; SceneManager.sceneUnloaded += Unloaded; }
        static void Unloaded(Scene scene) => Clear();
        static void Clear() { foreach (var entry in cache.Values) Release(entry,0); cache.Clear(); }
        static void Release(Sprite[] frames, float delay)
        {
            if (frames == null) return;
            foreach (var frame in frames) if (frame != null) UnityEngine.Object.Destroy(frame, delay);
        }
        public static bool Ready(ActiveSkillData a) => a != null && a.effectRevision == 1 && a.effectFrames == 4 &&
            a.effectImages?.Length == ArtStepCount(a) * 3 * a.effectFrames &&
            a.effectImages.Any(id => !string.IsNullOrEmpty(id)) &&
            a.effectImages.All(id => string.IsNullOrEmpty(id) || DesignStore.Image(id) != null);
        const int ProcessorRevision = 6;
        const int PromptRevision = 5;
        static int ArtStepCount(ActiveSkillData a) => a.HasTechnique ? 1 : a.steps.Count;
        static readonly ActiveStep techniqueArt = new() { delivery = "nova" };
        static ActiveSkillData TechniqueArtGraph() => new() { steps = { techniqueArt } };

        public static Sprite[] TechniqueAccent(ActiveSkillData a)
        {
            if (!a.HasTechnique) return null;
            var frames = Frames(a, techniqueArt, 0);
            return frames == null ? null : ActiveSkillPalette.Frames(a, frames, generated: true);
        }
        static string SourceSignature(ActiveSkillData a) => a.HasTechnique ? $"{a.technique}:{a.effectPrompt}" : a.input + ":" + string.Join("|", a.steps.Select(s => JsonUtility.ToJson(s)));
        static bool HasSource(ActiveSkillData a) => a != null && a.effectSourceSignature == SourceSignature(a) &&
            a.effectSourceRows?.Length > 0 && DesignStore.ImagePersisted(a.effectSourceImage);
        public static bool CanReprocess(ActiveSkillData a) => a != null && a.effectProcessedRevision != ProcessorRevision && HasSource(a);
        public static bool ReusesSavedArt(ActiveSkillData a) => Ready(a) || CanReprocess(a);
        static int[] Plan(ActiveSkillData a)
        {
            if (a.HasTechnique) return new[] { 0 };
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
            if (!fresh && Ready(a) && !CanReprocess(a)) { trace?.Invoke("저장된 전용 이펙트 재사용 · 이미지 요청 없음"); done(true); yield break; }
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
                    trace?.Invoke("저장된 원본 시트가 손상되어 재처리 불가 · 자동 유료 재요청 없음"); done(a.HasTechnique); yield break;
                }
                trace?.Invoke("저장된 원본 이펙트 시트 재처리 · 이미지 API 비용 없음");
            }
            if (sheet == null)
            {
                if (!AiClient.Enabled)
                {
                    if (a.HasTechnique) { a.artStatus = "전투 규칙 전용 내장 연출"; trace?.Invoke(a.artStatus); done(true); }
                    else { trace?.Invoke("AI 연결이 없어 전용 이펙트를 제작할 수 없음 · 현재 스킬 유지"); done(false); }
                    yield break;
                }
                rows = Plan(a);
                string size = "1024x1024";
                string prompt = a.HasTechnique
                    ? "Transparent overhead pixel action RPG sprite sheet, exactly four equal cells in one row. No border or text. " +
                      "One centred compact material accent with a fixed origin, four frames of ignition, bright peak, split fragments and fade. " +
                      "Keep the action silhouette, paths, devices and range edges clear for the engine to draw. " +
                      $"Movement language: {ActiveTechniques.Get(a.technique).Motif}. Material: {a.effectPrompt}. " + ActiveSkillStyle.Prompt(a)
                    : ActiveEffectPrompt.Build(a, rows, size);
                var world = Game.I?.World;
                trace?.Invoke($"선택한 [{a.name}] 전용 이펙트 {rows.Length}행×4프레임 1장 제작 · {cfg.imageModel}/{cfg.effectQuality}/{size} · 필요한 동작만 요청");
                yield return ImageClient.Generate(prompt, t => sheet = t, size: size, model: cfg.imageModel, quality: cfg.effectQuality, usage: usage =>
                {
                    float? cost = usage?.output_tokens > 0 ? usage.CostUsd(cfg.imageModel) : null;
                    bool measured = cost.HasValue; cost ??= ImageClient.PriceUsd(cfg.imageModel, cfg.effectQuality);
                    if (world != null) { world.designCalls++; if (cost.HasValue) world.designCostUsd += cost.Value; else world.designUnknownCosts++; }
                    trace?.Invoke(cost.HasValue ? $"이펙트 비용 {(measured ? "usage" : "추정")} ${cost.Value:0.00000}" : "이펙트 비용 미상 · 무료로 집계하지 않음");
                });
                if (sheet == null) { trace?.Invoke("이펙트 요청 실패: " + ImageClient.LastStatus + " · 자동 유료 재시도 없음"); done(a.HasTechnique); yield break; }
                ownsSheet = true;
            }
            bool success = false;
            try
            {
                if (ownsSheet)
                {
                    a.effectSourcePromptRevision = PromptRevision;
                    a.effectSourceImage = DesignStore.SaveSourceImage(sheet);
                    a.effectSourceSignature = SourceSignature(a); a.effectSourceRows = rows; a.effectProcessedRevision = 0;
                    persist?.Invoke();
                    trace?.Invoke(DesignStore.ImagePersisted(a.effectSourceImage) ? "유료 응답 원본 시트 저장 완료 · 처리 규칙 변경 시 무료 재처리 가능" : "원본 시트 저장 실패 · 재실행 시 재사용 불가");
                }
                a.effectProcessedRevision = 0;
                var canvases = ActiveEffectProcessor.Read(sheet, rows, ArtStepCount(a), cfg, trace, out string why, a.HasTechnique ? TechniqueArtGraph() : a);
                trace?.Invoke(why);
                if (canvases != null)
                {
                    // Keep the canonical 12 frames per step so existing saves/renderers remain compatible.
                    var saved = new Dictionary<PixelCanvas, string>();
                    var ids = canvases.Select(cv => {
                        if (cv == null) return null;
                        if (!saved.TryGetValue(cv, out string id)) saved[cv] = id = DesignStore.SaveImage(cv);
                        return id;
                    }).ToArray();
                    if (saved.Values.All(DesignStore.ImagePersisted))
                    {
                        a.effectImages = ids; a.effectFrames = 4; a.effectRevision = 1;
                        a.effectProcessedRevision = ProcessorRevision;
                        a.effectPromptRevision = a.effectSourcePromptRevision;
                        a.artStatus = ids.Count(id => !string.IsNullOrEmpty(id)) == rows.Length * 4 ?
                            "전용 아이콘·AI 전투 이펙트 준비 완료" : "AI 전투 이펙트 준비 완료 · 일부 내장 모션";
                        success = Ready(a);
                    }
                    else trace?.Invoke("전용 이미지 파일 저장 실패 · 스킬 장착 보류");
                }
                else a.effectProcessedRevision = ProcessorRevision;
            }
            catch (Exception error) { trace?.Invoke("이펙트 처리 실패 · 현재 스킬 유지: " + error.Message); }
            finally { if (ownsSheet) UnityEngine.Object.Destroy(sheet); persist?.Invoke(); }
            done(success || a.HasTechnique);
        }
        public static Sprite[] Frames(ActiveSkillData a, ActiveStep step, int motif)
        {
            if (a.effectRevision != 1 || a.effectFrames != 4 || a.effectImages?.Length != ArtStepCount(a) * 12) return null;
            int index = a.HasTechnique && step == techniqueArt ? 0 : a.steps.IndexOf(step); if (index < 0) return null;
            motif = motif == 1 ? 1 : motif == 2 ? 2 : 0;
            if (a.effectSourceRows?.Length > 0 && !a.effectSourceRows.Contains(index * 3 + motif)) return null;
            var ids = a.effectImages.Skip(index * 12 + motif * 4).Take(4).ToArray();
            if (ids.Any(string.IsNullOrEmpty)) return null;
            string key = a.id + ":" + string.Join(",", ids) + ":" + index + ":" + motif;
            if (cache.TryGetValue(key, out var found)) return found;
            var frames = ids.Select(id => DesignStore.Sprite(id, new Vector2(.5f,.5f))).ToArray();
            if (frames.Any(s => s == null)) { foreach (var s in frames) if (s != null) UnityEngine.Object.Destroy(s); return null; }
            if (a.effectPromptRevision < PromptRevision)
            {
                int size = frames[0].texture.width;
                bool sameCanvas = frames.All(f => f.texture.isReadable && f.texture.width == size && f.texture.height == size);
                if (!sameCanvas || !ActiveEffectProcessor.Usable(frames.Select(f => f.texture.GetPixels32()).ToArray(),
                    size, step, motif, ActiveSkillGenerator.Rules, out _))
                {
                    Release(frames, 0);
                    frames = null;
                }
            }
            if (cache.Count >= Mathf.Max(1, ActiveSkillGenerator.Rules.visualCacheLimit))
            {
                var oldest = cache.First(); cache.Remove(oldest.Key);
                // Existing casts/particles can finish using their captured frames before eviction destroys them.
                Release(oldest.Value, ActiveSkillGenerator.Rules.maxCastDuration + ActiveSkillGenerator.Rules.effectDuration);
            }
            return cache[key] = frames;
        }
    }
}
