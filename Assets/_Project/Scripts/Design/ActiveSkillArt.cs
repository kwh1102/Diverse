using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>One dedicated six-cell sheet per skill. No weapon-global overrides are used.</summary>
    public static class ActiveSkillArt
    {
        static readonly Dictionary<string, Sprite[]> frames = new Dictionary<string, Sprite[]>();
        public static IEnumerator Prepare(ActiveSkillData skill, Action<string> progress = null)
        {
            if (skill.ArtReady) yield break;
            progress?.Invoke($"{"QWER"[skill.slot]} [{skill.name}] 전용 아이콘·투사체·이펙트 생성 중…");
            var rules = ActiveSkillGenerator.Rules;
            Texture2D sheet = null;
            yield return ImageClient.Generate("Cute 16-bit pixel-art game sprites with crisp stepped edges, chunky readable shapes, dark purple outlines, limited palette, transparent background, no text or labels. " +
                "Exactly 3 columns and 2 rows, six isolated cells with generous transparent gutters, each subject centered inside its cell. " +
                "Top-left: one skill icon, " + skill.iconPrompt + ". Top-middle: dedicated projectile flying RIGHT, " + skill.projectilePrompt +
                ". Top-right: cast/swing effect facing RIGHT, " + skill.effectPrompt + ". Bottom-left: matching impact explosion. Bottom-middle: matching short projectile trail. Bottom-right: matching muzzle flash. " +
                "One coherent motif ONLY for this skill. Executable effect: " + ActiveSkillGenerator.Describe(skill), t => sheet = t, model: rules.imageModel);
            if (sheet == null) { progress?.Invoke("전용 이미지 생성 실패 — 이 스킬의 임시 도트로 표시하며 인벤토리에서 다시 생성할 수 있어."); yield break; }
            try
            {
                string Save(int i, int pixels, bool outline)
                {
                    int w = sheet.width / 3, h = sheet.height / 2;
                    var cv = Pixelizer.ConvertSet(sheet, new[] { new RectInt((i % 3) * w, sheet.height - (i / 3 + 1) * h, w, h) }, pixels, byHeight: false, colors: 12, outline: outline)[0];
                    return cv == null ? null : DesignStore.SaveImage(cv);
                }
                skill.iconImage = Save(0, rules.iconPixels, true);
                skill.projectileImage = Save(1, rules.projectilePixels, true);
                skill.effectImages = new[] { Save(2, rules.effectPixels, false), Save(3, rules.effectPixels, false), Save(4, rules.effectPixels, false), Save(5, rules.effectPixels, false) };
                if (Game.I?.World != null) { Game.I.World.designCalls++; Game.I.World.designCostUsd += ImageClient.PriceUsd(rules.imageModel) ?? 0; }
            }
            catch (Exception error) { Debug.LogWarning("[Active skill art] " + error.Message); }
            finally { UnityEngine.Object.Destroy(sheet); }
        }
        public static Sprite Icon(ActiveSkillData a) => Stored(a?.iconImage) ?? FallbackIcon(a);
        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        static Sprite Stored(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string key = "image:" + id;
            if (icons.TryGetValue(key, out var sprite) && sprite != null) return sprite;
            sprite = DesignStore.Sprite(id, new Vector2(.5f, .5f));
            if (sprite != null) icons[key] = sprite;
            return sprite;
        }
        public static void Restore(ActiveSkillData a)
        {
            if (a == null) return;
            if (DesignStore.Image(a.iconImage) == null) a.iconImage = null;
            if (DesignStore.Image(a.projectileImage) == null) a.projectileImage = null;
            if (a.effectImages != null)
                for (int i = 0; i < a.effectImages.Length; i++) if (DesignStore.Image(a.effectImages[i]) == null) a.effectImages[i] = null;
        }
        static Sprite FallbackIcon(ActiveSkillData a)
        {
            if (a == null) return null;
            if (icons.TryGetValue(a.id, out var result)) return result;
            var cv = new PixelCanvas(24, 24); var color = Pal.Hex(a.color); uint hash = PartArt.Hash(a.id);
            for (int y = 4; y < 20; y++) for (int x = 4; x < 20; x++)
                if (Mathf.Abs(x - 12) + Mathf.Abs(y - 12) < 10 && ((hash >> ((x + y * 3) % 24)) & 1) != 0) cv.Rect(x, y, 2, 2, color);
            cv.Outline(Pal.Hex("30263e")); return icons[a.id] = cv.ToSpriteCentered();
        }
        public static Sprite Shot(ActiveSkillData a) => Stored(a.projectileImage) ?? FallbackIcon(a);
        public static Sprite[] Effect(ActiveSkillData a, int cell, int size)
        {
            string id = a.effectImages != null && cell < a.effectImages.Length ? a.effectImages[cell] : null;
            string key = id + ":" + size;
            if (id != null && frames.TryGetValue(key, out var cached)) return cached;
            var tex = DesignStore.Image(id);
            // Use the skill's own icon as a temporary effect; never inherit the equipped weapon's art.
            if (tex == null) return new[] { FallbackIcon(a) };
            return frames[key] = DesignArt.Effect(tex, cell == 0 ? DesignArt.EffectKind.Swing : cell == 2 ? DesignArt.EffectKind.Trail : cell == 3 ? DesignArt.EffectKind.Muzzle : DesignArt.EffectKind.Impact, size);
        }
    }
}
