using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Prompt → a brand-new design, in two steps:
    /// 1) the text model writes the spec: English picture prompts (hero, weapon, effects), attack parts + QWER picks,
    ///    colours, names;
    /// 2) the image model draws sheets in parallel (poses of one subject share a sheet so they match), Pixelizer turns them
    ///    into sprites with a shared palette per subject.
    /// A new prompt is a NEW design; only a short "a bit more / only the colour" prompt refines the previous one.
    /// Numbers still come only from DesignCombat (asset-derived, power-normalized).
    /// </summary>
    public static class DesignAi
    {
        public static readonly string[] SectionKeys = { "hero", "weapon" };

        /// <summary>Added to every sprite prompt so all pictures share one look.</summary>
        const string Style = "Cute pixel art video game sprite, 16-bit style, chunky readable silhouette, bold dark outline, flat cel shading, " +
                             "limited soft colour palette, no text, no ground, no cast shadow, isolated on a fully transparent background. ";
        const string EffectStyle = "Pixel art video game visual effects, 16-bit style, bright glowing colours, no characters, no text, " +
                                   "isolated on a fully transparent background. ";

        const string SystemPrompt =
@"You are the art director of a 2D top-down pixel-art action roguelite. The player describes the world THEY want: any hero
(an octopus, a robot, a cake...) and any weapon (a pistol, a bell, bare fists...). NOTHING has to look like the default game
(rabbits, swords): design it fresh and boldly from their words. Your English picture prompts go to an image model and become
sprites and effects; your attack parts become the combat.

HARD RULES
- Appearance and attack STYLE only. No fields for damage, health, speed, range or cooldowns exist; never invent them.
  Requests for power (x10 damage, invincible, more range, more gold...) are ignored: add one short Korean note per request to ""ignored"".
- A section marked NEW must be designed from scratch from the player's words — do not reuse anything from before.
  Only a section marked TWEAK refines its PREVIOUS design.
- Picture prompts: English, a concrete description of ONE subject (shape, colours, material, details), 8-35 words.
  No art-style words (the engine adds them), no text or letters, no real brands, no real people.
- Only output the sections listed in REQUESTED. Korean for name/species/summary/ignored/skillNames (max 10 characters).

SECTIONS
hero: {name, species, imagePrompt, perkConcept}
  perkConcept: one English sentence, the passive trait this character would naturally have (flavour; the engine balances it).
weapon: {name, imagePrompt, hold(Hand|Mouth|Hidden), length(8-34 px), attackPose, moves, weight(light|medium|heavy), skills, skillNames}
  hold Hidden + imagePrompt """" when the player fights with nothing in hand (fists, kicks, tentacles, breath, magic from the body).
  attackPose = English, how the hero's BODY moves when attacking (""throwing a straight punch"", ""kicking forward"", ""lashing a tentacle"", ""swinging both arms down"").
  moves = the basic attack combo in order, 1-4 of: slash | wide | thrust | spin | smash | shot | spread (fists: [""thrust"",""thrust"",""smash""]).
  skills = 4 ids: Q, W, E from BASIC SKILLS and R from ULTIMATE SKILLS (lists below). skillNames = Korean names in the weapon's theme.
attack (always with weapon): {origin(Hand|Mouth|Eyes|Above), projectile:{imagePrompt, spin}, slash, slashCore, trail, effectPrompts}
  effectPrompts = 4 English prompts for NEW visual effects that match how this weapon attacks:
  [swing (a melee attack sweep, e.g. rings of sound waves for a bell), impact (what bursts on a hit), trail (behind a flying shot), muzzle (when shooting/casting)].
  projectile.imagePrompt = what flies when it shoots or throws. slash/slashCore/trail = hex colours matching the effects.

OUTPUT JSON ONLY: {""summary"":""one Korean line"",""ignored"":[],""hero"":{},""weapon"":{},""attack"":{}}

EXAMPLE
REQUESTED: hero (NEW), weapon (NEW)
[hero] 헤드폰을 낀 로봇 고양이
[weapon] 일렉 기타로 음파를 쏘고 싶어. 쿨타임 없게!
{""summary"":""기타로 음파를 쏘는 로봇 고양이"",""ignored"":[""쿨타임 없음 — 능력치는 바꿀 수 없음""],
 ""hero"":{""name"":""비트 냥"",""species"":""로봇 고양이"",""imagePrompt"":""a small round white robot cat with glowing cyan eyes, pink headphones and a little antenna, standing on two legs"",""perkConcept"":""a rhythm robot whose every third hit releases a sound wave""},
 ""weapon"":{""name"":""일렉 기타"",""imagePrompt"":""a red electric guitar with a white pickguard and silver strings"",""hold"":""Hand"",""length"":20,""moves"":[""wide"",""wide"",""shot""],""weight"":""medium"",
  ""skills"":[""st_cyclone"",""kt_crescent"",""gs_rush"",""st_thunder""],""skillNames"":[""하울링"",""파워 코드"",""무대 돌진"",""앙코르""]},
 ""attack"":{""origin"":""Hand"",""projectile"":{""imagePrompt"":""a glowing cyan musical note"",""spin"":true},""slash"":""#ff6b8a"",""slashCore"":""#fff0f4"",""trail"":""#7ff3ff"",
  ""effectPrompts"":[""three curved neon pink sound wave arcs spreading outward"",""a starburst of cyan musical notes and sparks"",""a short fading cyan ripple ring"",""a bright pink speaker blast ring""]}}";

        public static bool Available => AiClient.Enabled;

        /// <summary>Estimated USD of the last Generate (text + pictures).</summary>
        public static float LastCostUsd;

        static string SkillList(IEnumerable<string> ids) =>
            string.Join("\n", ids.Select(id => { var d = SkillDB.Get(id); return $"  {id}: {d.name} — {d.desc}"; }));

        static string Previous(DesignPack p, int section) => section == 0
            ? JsonUtility.ToJson(new HeroLook { name = p.hero.name, species = p.hero.species, imagePrompt = p.hero.imagePrompt, perkConcept = p.hero.perkConcept })
            : JsonUtility.ToJson(new WeaponLook { name = p.weapon.name, imagePrompt = p.weapon.imagePrompt, hold = p.weapon.hold, length = p.weapon.length, moves = p.weapon.moves, weight = p.weapon.weight, skills = p.weapon.skills, skillNames = p.weapon.skillNames })
                 + " attack: " + JsonUtility.ToJson(new AttackLook { origin = p.attack.origin, projectile = new ProjectileLook { imagePrompt = p.attack.projectile.imagePrompt }, effectPrompts = p.attack.effectPrompts });

        /// <summary>
        /// Generate the sections that have prompts. progress(text) reports picture progress; done(pack, status): pack = the merged
        /// result (null when even the spec failed). lockChassis: in-game edits keep the attack parts, skills and perk.
        /// </summary>
        public static IEnumerator Generate(MonoBehaviour host, DesignPack current, bool[] sections, bool lockChassis, Action<string> progress, Action<DesignPack, string> done)
        {
            var user = new StringBuilder();
            user.AppendLine("REQUESTED: " + string.Join(", ", Enumerable.Range(0, 2).Where(i => sections[i])
                .Select(i => (i == 1 ? "weapon, attack" : SectionKeys[i]) + (DesignGuard.IsTweak(current.prompts[i]) ? " (TWEAK)" : " (NEW)"))));
            user.AppendLine("PLAYER PROMPTS:");
            for (int i = 0; i < 2; i++) if (sections[i]) user.AppendLine($"[{SectionKeys[i]}] {current.prompts[i]}");
            for (int i = 0; i < 2; i++)
                if (sections[i] && DesignGuard.IsTweak(current.prompts[i])) user.AppendLine($"PREVIOUS {SectionKeys[i]} (refine it): {Previous(current, i)}");
            if (sections[1])
            {
                user.AppendLine("BASIC SKILLS:\n" + SkillList(DesignCombat.Basics));
                user.AppendLine("ULTIMATE SKILLS:\n" + SkillList(DesignCombat.Ultimates));
            }
            if (lockChassis) user.AppendLine("This hero is mid-life: keep moves, weight and skills as they are (names and looks may change): "
                                             + string.Join(",", current.weapon.moves) + " / " + current.weapon.weight + " / " + string.Join(",", current.weapon.skills ?? new string[0]));

            progress("AI가 디자인을 구상하는 중…");
            string json = null;
            yield return AiClient.Complete(SystemPrompt, user.ToString(), 0.9f, 2200, r => json = r);
            if (json == null) { done(null, AiClient.LastStatus); yield break; }
            int textIn = AiClient.LastPromptTokens, textOut = AiClient.LastCompletionTokens;

            DesignPack answer;
            try { answer = JsonUtility.FromJson<DesignPack>(json); }
            catch (Exception e) { done(null, "AI 응답 해석 실패 (" + e.Message + ")"); yield break; }
            if (answer == null) { done(null, "AI 응답 형식 오류"); yield break; }
            DesignGuard.Sanitize(answer);

            var result = current.Clone();
            // A requested section the model left out stays as it was (JsonUtility would hand back defaults)
            if (sections[0] && Has(json, "hero"))
            {
                var keepPerk = lockChassis ? current.hero.perk : null;
                result.hero = answer.hero;
                result.hero.perk = keepPerk;
            }
            if (sections[1] && Has(json, "weapon"))
            {
                result.weapon = answer.weapon;
                if (lockChassis) { result.weapon.moves = current.weapon.moves; result.weapon.weight = current.weapon.weight; result.weapon.skills = current.weapon.skills; }
            }
            if (sections[1] && Has(json, "attack")) result.attack = answer.attack;
            // Fists / tentacles / breath: nothing is held, whatever the model drew up
            if (sections[1] && DesignGuard.BareHanded(current.prompts[1])) { result.weapon.hold = "Hidden"; result.weapon.imagePrompt = null; }
            if (!string.IsNullOrWhiteSpace(answer.summary)) result.summary = answer.summary;
            result.ignored = answer.ignored ?? new List<string>();
            result.source = "ai";
            DesignGuard.Sanitize(result);

            // Pictures: every sheet runs at once; a failed sheet leaves those sprites on the local fallback drawing
            var jobs = new List<IEnumerator>();
            // The hero sheet holds the attack poses too: a new way of attacking (bare fists, a different weapon) redraws it
            bool newAttack = sections[1] && result.weapon.attackPose != current.weapon.attackPose;
            if ((sections[0] || newAttack) && result.hero.imagePrompt != null) jobs.Add(HeroJob(result));
            if (sections[1])
            {
                if (result.weapon.imagePrompt != null || result.attack.projectile.imagePrompt != null) jobs.Add(WeaponJob(result));
                else { result.weapon.image = null; result.attack.projectile.image = null; }
                if (result.attack.effectPrompts != null) jobs.Add(EffectsJob(result));
            }
            int finished = 0, failed = 0;
            foreach (var job in jobs)
                host.StartCoroutine(Run(job, ok => { finished++; if (!ok) failed++; progress($"그림 그리는 중… {finished}/{jobs.Count}"); }));
            progress($"그림 그리는 중… 0/{jobs.Count}");
            while (finished < jobs.Count) yield return null;
            DesignGuard.Sanitize(result);

            // The image model often ignores "facing right": one cheap look at all new sprites, then flip the ones facing left
            progress("방향 확인 중…");
            float visionCost = 0;
            yield return FixFacing(result, sections, c => visionCost = c);

            var textCost = (AiClient.CostUsd(Game.Settings.aiModel, textIn, textOut) ?? 0) + visionCost;
            // The hero sheet is drawn on a wide canvas: about 1.5x a square picture
            int wide = jobs.Count(j => j.GetType().Name.Contains("HeroJob"));
            var imageCost = (ImageClient.PriceUsd(ImageClient.Model) ?? 0) * (jobs.Count - failed + wide * 0.5f);
            LastCostUsd = textCost + imageCost;
            string status = $"새 디자인 완성 (글 {textIn}+{textOut}토큰, 그림 {jobs.Count - failed}장 · 약 ${LastCostUsd:0.000})";
            if (failed > 0) status += $"\n<color=#ffb36b>그림 {failed}장 실패: {ImageClient.LastStatus} — 그 부분은 기본 도트로 표시</color>";
            done(result, status);
        }

        static IEnumerator Run(IEnumerator job, Action<bool> finished)
        {
            bool ok = true;
            while (true)
            {
                object cur;
                try { if (!job.MoveNext()) break; cur = job.Current; }
                catch (Exception e) { Debug.LogWarning("[AI image] job failed: " + e); ok = false; break; }
                if (cur is bool b) { ok = b; break; }
                yield return cur;
            }
            finished(ok);
        }

        // ───────────────────────── Picture jobs (each yields false at the end on failure) ─────────────────────────

        static RectInt Cell(Texture2D t, int col, int row, int cols, int rows)
        {
            int w = t.width / cols, h = t.height / rows;
            return new RectInt(col * w, t.height - (row + 1) * h, w, h);   // row 0 = top (texture y grows upward)
        }

        static RectInt Quarter(Texture2D t, int i) => Cell(t, i % 2, i / 2, 2, 2);

        static string Save(PixelCanvas cv) => cv == null ? null : DesignStore.SaveImage(cv);

        static IEnumerator HeroJob(DesignPack p)
        {
            // Held weapons are drawn separately (on the hand): the sheet shows empty hands; bare-handed heroes show the strike itself
            var w = p.weapon;
            string attack = w.attackPose ?? (w.hold == "Hidden" ? "throwing a punch" : "swinging its arm");
            string hands = w.hold == "Hidden" ? "" : " Do not draw any weapon or held item; hands empty.";
            Texture2D tex = null;
            yield return ImageClient.Generate(Style + "A sprite sheet in a grid of 4 columns and 2 rows: the SAME character drawn eight times with identical " +
                "design, colours and size, full body, side view facing right, each pose centered in its own cell with empty space around it. " +
                "Top row, a smooth walk cycle: front foot touching down; passing (legs together, body slightly raised); back foot touching down; passing again. " +
                $"Bottom row: standing idle; winding up to attack ({attack}, pulled back); the attack striking forward ({attack}, fully extended); flinching backward after being hit." +
                hands + " Character: " + p.hero.imagePrompt, t => tex = t, size: ImageClient.WideSize);
            if (tex == null) { p.hero.image = null; p.hero.frames = null; yield return false; yield break; }
            // The idle pose (cell 4) goes first: it sets the scale every other pose keeps
            int[] order = { 4, 0, 1, 2, 3, 5, 6, 7 };
            var converted = Pixelizer.ConvertSet(tex, order.Select(i => Cell(tex, i % 4, i / 4, 4, 2)).ToArray(), 28, byHeight: true, alignFacing: true);
            var cells = new PixelCanvas[8];
            for (int k = 0; k < 8; k++) cells[order[k]] = converted[k];
            UnityEngine.Object.Destroy(tex);
            p.hero.frames = cells.Select(Save).ToArray();
            p.hero.image = p.hero.frames[4];
            if (p.hero.frames.Any(f => f == null)) p.hero.frames = null;
            yield return p.hero.image != null;
        }

        static IEnumerator WeaponJob(DesignPack p)
        {
            var w = p.weapon;
            var shot = p.attack.projectile;
            bool held = w.imagePrompt != null && w.hold != "Hidden";
            string weaponPart = held ? w.imagePrompt + ", lying horizontally, pointing to the right, handle on the left" : "nothing (leave empty)";
            string shotPart = shot.imagePrompt != null ? shot.imagePrompt + ", small, flying to the right" : "nothing (leave empty)";
            Texture2D tex = null;
            yield return ImageClient.Generate(Style + "Two separate items side by side with a wide empty gap between them. LEFT HALF: " + weaponPart + ". RIGHT HALF: " + shotPart + ".", t => tex = t);
            if (tex == null) { w.image = null; shot.image = null; yield return false; yield break; }
            w.image = held ? Save(Pixelizer.Convert(tex, Cell(tex, 0, 0, 2, 1), w.length > 0 ? w.length : 20, byHeight: false)) : null;
            shot.image = shot.imagePrompt != null ? Save(Pixelizer.Convert(tex, Cell(tex, 1, 0, 2, 1), 11, byHeight: false)) : null;
            UnityEngine.Object.Destroy(tex);
            yield return true;
        }

        static IEnumerator EffectsJob(DesignPack p)
        {
            var e = p.attack.effectPrompts;
            Texture2D tex = null;
            yield return ImageClient.Generate(EffectStyle + "A 2x2 grid of four separate effects, each centered in its own quarter with empty space around it. " +
                $"Top-left, a melee attack sweep shaped like a crescent on the right side: {e[0]}. Top-right, a hit impact burst: {e[1]}. " +
                $"Bottom-left, a small trail puff behind a flying shot: {e[2]}. Bottom-right, a muzzle flash / casting burst: {e[3]}.", t => tex = t);
            if (tex == null) { p.attack.effectImages = null; yield return false; yield break; }
            var ids = new string[4];
            for (int i = 0; i < 4; i++) ids[i] = e[i] == null ? null : Save(Pixelizer.ConvertSet(tex, new[] { Quarter(tex, i) }, 48, byHeight: false, colors: 10, outline: false)[0]);
            UnityEngine.Object.Destroy(tex);
            p.attack.effectImages = ids.All(s => s != null) ? ids : null;
            yield return p.attack.effectImages != null;
        }

        // ───────────────────────── Facing check ─────────────────────────

        [Serializable] class Dirs { public List<string> dirs; }

        /// <summary>
        /// Every sprite in the game faces / points / flies to the right (Unity flips it for the left). Put the new hero, weapon,
        /// shot and swing effect on one contact sheet, ask the text model (low-detail vision, ~$0.0005) which way each
        /// one faces, and mirror the ones that face left (all poses of a subject together).
        /// </summary>
        static IEnumerator FixFacing(DesignPack p, bool[] sections, Action<float> cost)
        {
            var subjects = new List<(string what, string id, Action<Func<string, string>> flip)>();
            if (sections[0] && p.hero.frames != null)
                subjects.Add(("a character: which way it faces", p.hero.image, f => { p.hero.frames = p.hero.frames.Select(f).ToArray(); p.hero.image = p.hero.frames[p.hero.frames.Length >= 8 ? 4 : 0]; }));
            if (sections[1])
            {
                var w = p.weapon; var a = p.attack;
                if (w.image != null) subjects.Add(("a weapon or tool: is its working end (gun barrel / muzzle, blade tip, hammer head, bow string side away from the grip) on the left or the right? The grip / stock is on the opposite side", w.image, f => w.image = f(w.image)));
                if (a.projectile.image != null) subjects.Add(("a flying shot: on which side is its front", a.projectile.image, f => a.projectile.image = f(a.projectile.image)));
            }
            if (subjects.Count == 0) { cost(0); yield break; }

            const int cell = 96;   // big enough that a gun's barrel and grip are clearly different
            int cols = Mathf.Min(8, subjects.Count), rows = Mathf.CeilToInt(subjects.Count / (float)cols);
            var sheet = new PixelCanvas(cols * cell, rows * cell);
            sheet.Rect(0, 0, sheet.W, sheet.H, new Color32(205, 205, 205, 255));
            for (int i = 0; i < subjects.Count; i++)
            {
                var tex = DesignStore.Image(subjects[i].id);
                if (tex == null) continue;
                var px = tex.GetPixels32();
                int k = Mathf.Max(1, 88 / Mathf.Max(tex.width, tex.height));
                int ox = (i % cols) * cell + (cell - tex.width * k) / 2, oy = (rows - 1 - i / cols) * cell + (cell - tex.height * k) / 2;
                for (int y = 0; y < tex.height * k; y++)
                    for (int x = 0; x < tex.width * k; x++)
                    {
                        var c = px[(y / k) * tex.width + x / k];
                        if (c.a > 0) sheet.Set(ox + x, oy + y, c);
                    }
            }
            var sheetTex = sheet.ToTexture();
            var png = sheetTex.EncodeToPNG();
            UnityEngine.Object.Destroy(sheetTex);
            var question = $"This image is a grid of {subjects.Count} cells ({cols} per row), read left to right, then top to bottom. " +
                           "For each cell answer \"left\" or \"right\":\n" + string.Join("\n", subjects.Select((s, i) => $"cell {i}: {s.what}")) +
                           $"\nReturn JSON {{\"dirs\":[...]}} with exactly {subjects.Count} entries.";
            string json = null;
            yield return AiClient.CompleteVision("You look at pixel-art game sprites and say which way they face or point. Look carefully at each cell. Output JSON only.", question, png, 400, r => json = r, "high");
            cost(AiClient.CostUsd(Game.Settings.aiModel, AiClient.LastPromptTokens, AiClient.LastCompletionTokens) ?? 0);
            if (json == null) yield break;
            List<string> dirs = null;
            try { dirs = JsonUtility.FromJson<Dirs>(json)?.dirs; } catch { }
            if (dirs == null) yield break;
            for (int i = 0; i < subjects.Count && i < dirs.Count; i++)
                if (string.Equals(dirs[i]?.Trim(), "left", StringComparison.OrdinalIgnoreCase))
                    subjects[i].flip(DesignStore.Mirrored);
            Debug.Log("[AI design] facing: " + string.Join(",", dirs));
        }

        static bool Has(string json, string key) => json.Contains("\"" + key + "\"");
    }
}
