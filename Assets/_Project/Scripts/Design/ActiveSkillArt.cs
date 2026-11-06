using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Candidate icons are batched; only a selected skill receives a dedicated combat sheet.</summary>
    public static class ActiveSkillArt
    {
        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        public static IEnumerator Prepare(ActiveSkillData skill, Action<string> progress = null)
        { yield return PrepareIcons(new[] { skill }, progress); }

        public static IEnumerator PrepareIcons(IList<ActiveSkillData> skills, Action<string> trace = null)
        {
            var pending = skills.Where(a => a != null && !a.ArtReady).Take(3).ToList();
            if (pending.Count == 0) { trace?.Invoke("기존 후보 아이콘 재사용 · 이미지 요청 없음"); yield break; }
            if (!AiClient.Enabled)
            {
                foreach (var a in pending) a.artStatus = "로컬 도트 아이콘 · AI 연결 없음";
                trace?.Invoke("AI 연결 없음 · 동작에 맞는 로컬 아이콘 사용"); yield break;
            }
            var rules = ActiveSkillGenerator.Rules;
            var world = Game.I?.World;
            Texture2D sheet = null;
            trace?.Invoke($"아이콘 {pending.Count}개를 2×2 시트 한 장으로 요청 · {rules.imageModel} / {rules.iconQuality}");
            string prompt = "A transparent PNG sprite sheet: exactly 2 equal columns and 2 equal rows, NO grid lines, borders, labels, text, scenery, tiles, cards or backgrounds. " +
                "Each cell has a SINGLE centered isolated skill symbol occupying at most 65% of its cell, generous completely transparent gutters on ALL sides. " +
                "Cozy cute top-down pixel RPG style. Moderately detailed chunky pixel art, readable at 32px, dark muted purple outlines, 3-4 flat shading tones, restrained warm palette. " +
                "Avoid gradients, photorealism, ornate emblems, excessive particles, bright halos, decorative frames and painted checkerboards. Transparent pixels must have alpha zero. " +
                string.Join(" ", pending.Select((a, i) => $"{new[] { "Top-left", "Top-right", "Bottom-left" }[i]} cell: {a.iconPrompt}. Main body colour #{a.color}, pale highlights and dark muted outlines. Behavior: {ActiveSkillGenerator.VisualSummary(a)}.")) +
                " All unused cells, including bottom-right, must be empty transparent pixels.";
            yield return ImageClient.Generate(prompt, t => sheet = t, model: rules.imageModel, quality: rules.iconQuality, usage: usage =>
            {
                float? cost = usage != null && usage.output_tokens > 0 ? usage.CostUsd(rules.imageModel) : null;
                bool measured = cost.HasValue;
                cost ??= ImageClient.PriceUsd(rules.imageModel, rules.iconQuality);
                if (world != null) { world.designCalls++; if (cost.HasValue) world.designCostUsd += cost.Value; else world.designUnknownCosts++; }
                trace?.Invoke(cost.HasValue ? $"이미지 비용 {(measured ? "usage 기준" : "추정")} ${cost.Value:0.00000}" : "이미지 비용 미상 · 무료로 집계하지 않음");
            });
            if (sheet == null)
            {
                foreach (var a in pending) a.artStatus = "아이콘 요청 실패 · 로컬 도트 사용";
                trace?.Invoke("이미지 요청 실패: " + ImageClient.LastStatus + " · 유료 자동 재시도 없음"); yield break;
            }
            try
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    var a = pending[i]; int w = sheet.width / 2, h = sheet.height / 2;
                    PixelCanvas cv = null; string why;
                    try { cv = ActiveIconProcessor.Read(sheet, new RectInt(i % 2 * w, sheet.height - (i / 2 + 1) * h, w, h), out why); }
                    catch (Exception error) { why = "아이콘 처리 실패 · 로컬 도트 사용"; Debug.LogWarning("[Active icon] " + error.Message); }
                    if (cv != null) { a.iconImage = DesignStore.SaveImage(cv); a.iconRevision = 2; }
                    else { a.iconImage = null; a.iconRevision = 0; }
                    a.artStatus = cv != null ? "전용 아이콘 준비 완료" : "배경 검사 실패 · 로컬 도트 사용";
                    a.visualPalette = null; ActiveSkillStyle.Ensure(a);
                    trace?.Invoke($"[{a.name}] {why}{(cv == null ? " · 유료 재시도 없이 로컬 아이콘" : "")}");
                }
            }
            finally { UnityEngine.Object.Destroy(sheet); }
            trace?.Invoke("아이콘 준비 완료 · 선택 후 해당 스킬의 전용 전투 시트를 제작합니다.");
        }
        public static Sprite Icon(ActiveSkillData a)
        {
            if (a == null) return null;
            ActiveSkillStyle.Ensure(a);
            if (a.ArtReady)
            {
                if (icons.TryGetValue(a.iconImage, out var cached) && cached != null) return cached;
                var sprite = DesignStore.Sprite(a.iconImage, new Vector2(.5f, .5f));
                if (sprite != null) return icons[a.iconImage] = sprite;
            }
            return ActiveSkillVisuals.Icon(a);
        }
        public static void Restore(ActiveSkillData a)
        {
            if (a == null) return;
            if (string.IsNullOrEmpty(a.input)) a.input = "tap";
            if (a.steps != null) foreach (var step in a.steps)
            {
                if (step == null) continue;
                if (string.IsNullOrEmpty(step.origin)) step.origin = "caster";
                if (string.IsNullOrEmpty(step.trigger)) step.trigger = "after";
            }
            var image = DesignStore.Image(a.iconImage);
            if (image == null) { a.iconImage = null; a.iconRevision = 0; }
            else if (a.iconRevision < 2)
            {
                var clean = ActiveIconProcessor.Read(image, new RectInt(0, 0, image.width, image.height), out string why);
                a.iconImage = clean != null ? DesignStore.SaveImage(clean) : null;
                a.iconRevision = clean != null ? 2 : 0;
                a.artStatus = "기존 아이콘 검사: " + why;
            }
            ActiveSkillStyle.Ensure(a);
        }
        public static Sprite[] FlyingFrames(ActiveSkillData a, ActiveStep step) => ActiveSkillPresentation.Effect(a, step, 2, ActiveSkillGenerator.Rules.projectilePixels);
        public static Sprite Shot(ActiveSkillData a, ActiveStep step) => FlyingFrames(a, step)?[0] ?? ActiveSkillVisuals.Shot(a, step);
        public static Sprite[] Effect(ActiveSkillData a, ActiveStep step, int cell, int size) => ActiveSkillPresentation.Effect(a, step, cell, size);
    }
}
