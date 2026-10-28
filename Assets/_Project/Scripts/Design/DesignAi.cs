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
    /// 1) the text model writes the spec: English picture prompts (hero, weapon, effects, ground, scenery, buildings, enemy
    ///    families), attack parts + QWER picks, colours, names;
    /// 2) the image model draws sheets in parallel (poses of one subject share a sheet so they match), Pixelizer turns them
    ///    into sprites with a shared palette per subject.
    /// A new prompt is a NEW design; only a short "a bit more / only the colour" prompt refines the previous one.
    /// Numbers still come only from DesignCombat (asset-derived, power-normalized).
    /// </summary>
    public static class DesignAi
    {
        public static readonly string[] SectionKeys = { "hero", "weapon", "world", "enemies" };

        /// <summary>Added to every sprite prompt so all pictures share one look.</summary>
        const string Style = "Cute pixel art video game sprite, 16-bit style, chunky readable silhouette, bold dark outline, flat cel shading, " +
                             "limited soft colour palette, no text, no ground, no cast shadow, isolated on a fully transparent background. ";
        const string EffectStyle = "Pixel art video game visual effects, 16-bit style, bright glowing colours, no characters, no text, " +
                                   "isolated on a fully transparent background. ";
        const string TileStyle = "Top-down pixel art video game terrain textures, 16-bit style, flat lighting, no objects, no shadows, no perspective, no text. ";

        const string SystemPrompt =
@"You are the art director of a 2D top-down pixel-art action roguelite. The player describes the world THEY want: any hero
(an octopus, a robot, a cake...), any weapon (a pistol, a bell, bare fists...), any world and any enemies. NOTHING has to look like
the default game (rabbits, swords, grass, trees, mushrooms): design it fresh and boldly from their words. Your English picture
prompts go to an image model and become sprites, textures and effects; your attack parts become the combat.

HARD RULES
- Appearance and attack STYLE only. No fields for damage, health, speed, range or cooldowns exist; never invent them.
  Requests for power (x10 damage, invincible, more range, more gold...) are ignored: add one short Korean note per request to ""ignored"".
- A section marked NEW must be designed from scratch from the player's words — do not reuse anything from before.
  Only a section marked TWEAK refines its PREVIOUS design.
- Picture prompts: English, a concrete description of ONE subject (shape, colours, material, details), 8-35 words.
  No art-style words (the engine adds them), no text or letters, no real brands, no real people.
- Keep every picture in ONE theme: the world's themePrompt (WORLD CONTEXT below when the world isn't being redesigned).
- Only output the sections listed in REQUESTED. Korean for name/species/summary/ignored/theme/mood/skillNames (max 10 characters).

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
world: {mood, themePrompt, layout, grass, path, water, rough, tilePrompts, decorPrompts, decorSizes, decorPlaces, townPrompts, ambient, ambientColor, tint, tintAmount(0-0.5)}
  themePrompt = English, max 15 words: what this world is made of. grass/path/water/rough = hex average colours of the tiles.
  layout = how the land is ARRANGED, so the world reads as one place: city (street grid, buildings lining the streets) | village (winding
  lanes, houses along them) | forest (dense groves and clearings) | farm (fields of crops in rows) | ruins (broken paving, scattered remains)
  | wasteland (empty land, rare landmarks) | wild (open land, small groves).
  tilePrompts = 8 ground textures, 2 variants each: [ground A, ground B, path A, path B, liquid A, liquid B, rough A, rough B] —
  real materials of THIS world (asphalt, candy floor, lava, cloud...). Ground must be SUBTLE and low-contrast (one main colour, fine detail,
  no big patterns, no grid lines, no checkerboard): characters and objects stand on it.
  decorPrompts = 12 DIFFERENT scenery objects that belong in this layout (a city: buildings, shops, street lamps, benches, hydrants, signs...;
  not trees and rocks unless they fit). decorSizes = 12 of large | medium | small | tiny (about 4 large, 3 medium, 3 small, 2 tiny).
  decorPlaces = 12 of road (stands along roads: buildings, lamps) | cluster (grows in groups) | scatter (anywhere) | rows (field rows) | rare (unique landmark).
  townPrompts = 4: [house A, house B, street light, village landmark] in the world's architecture.
  ambient = none|snow|petals|leaves|bubbles|embers|sparkles|rain|dust|notes (what drifts in the air), ambientColor = hex.
enemies: {theme, families:[{name, imagePrompt}]}
  1-4 creature families that the player asked for (if they named two kinds, give exactly those two). The engine makes every enemy
  (grunts, shooters, casters, chargers, swarms, bosses) from these families in the world's theme.

OUTPUT JSON ONLY: {""summary"":""one Korean line"",""ignored"":[],""hero"":{},""weapon"":{},""attack"":{},""world"":{},""enemies"":{}}

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

        /// <summary>How an enemy's role reads in a picture prompt and in its Korean name.</summary>
        static (string en, string ko) Role(EnemyDef d) => d.boss ? ("a huge menacing boss version with a crown and armor", "대왕") : d.brain switch
        {
            EnemyBrain.Ranged => ("a ranged version that throws things", "사수"),
            EnemyBrain.Caster => ("a floating magical spell-casting version", "술사"),
            EnemyBrain.Charger => ("a big heavy charging version", "돌격대"),
            EnemyBrain.Swarm => ("a tiny flying version", "떼"),
            EnemyBrain.Hopper => ("a bouncy hopping version", "뜀꾼"),
            _ => ("a small walking grunt version", "졸개"),
        };

        static string Previous(DesignPack p, int section) => section switch
        {
            0 => JsonUtility.ToJson(new HeroLook { name = p.hero.name, species = p.hero.species, imagePrompt = p.hero.imagePrompt, perkConcept = p.hero.perkConcept }),
            1 => JsonUtility.ToJson(new WeaponLook { name = p.weapon.name, imagePrompt = p.weapon.imagePrompt, hold = p.weapon.hold, length = p.weapon.length, moves = p.weapon.moves, weight = p.weapon.weight, skills = p.weapon.skills, skillNames = p.weapon.skillNames })
                 + " attack: " + JsonUtility.ToJson(new AttackLook { origin = p.attack.origin, projectile = new ProjectileLook { imagePrompt = p.attack.projectile.imagePrompt }, effectPrompts = p.attack.effectPrompts }),
            2 => JsonUtility.ToJson(new WorldLook { mood = p.world.mood, themePrompt = p.world.themePrompt, tilePrompts = p.world.tilePrompts, decorPrompts = p.world.decorPrompts, townPrompts = p.world.townPrompts, ambient = p.world.ambient }),
            _ => JsonUtility.ToJson(new EnemyLook { theme = p.enemies.theme, families = p.enemies.families }),
        };

        /// <summary>
        /// Generate the sections that have prompts. progress(text) reports picture progress; done(pack, status): pack = the merged
        /// result (null when even the spec failed). lockChassis: in-game edits keep the attack parts, skills and perk.
        /// </summary>
        public static IEnumerator Generate(MonoBehaviour host, DesignPack current, bool[] sections, bool lockChassis, Action<string> progress, Action<DesignPack, string> done)
        {
            var user = new StringBuilder();
            user.AppendLine("REQUESTED: " + string.Join(", ", Enumerable.Range(0, 4).Where(i => sections[i])
                .Select(i => (i == 1 ? "weapon, attack" : SectionKeys[i]) + (DesignGuard.IsTweak(current.prompts[i]) ? " (TWEAK)" : " (NEW)"))));
            user.AppendLine("PLAYER PROMPTS:");
            for (int i = 0; i < 4; i++) if (sections[i]) user.AppendLine($"[{SectionKeys[i]}] {current.prompts[i]}");
            for (int i = 0; i < 4; i++)
                if (sections[i] && DesignGuard.IsTweak(current.prompts[i])) user.AppendLine($"PREVIOUS {SectionKeys[i]} (refine it): {Previous(current, i)}");
            if (sections[1])
            {
                user.AppendLine("BASIC SKILLS:\n" + SkillList(DesignCombat.Basics));
                user.AppendLine("ULTIMATE SKILLS:\n" + SkillList(DesignCombat.Ultimates));
            }
            if (!sections[2] && current.world.themePrompt != null) user.AppendLine("WORLD CONTEXT: " + current.world.themePrompt);
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
            if (sections[2] && Has(json, "world")) result.world = answer.world;
            if (sections[3] && Has(json, "enemies")) result.enemies = answer.enemies;
            if (sections[3]) FillEnemies(result);
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
            if (sections[2])
            {
                var w = result.world;
                if (w.tilePrompts != null) { w.tileImages = new string[8]; jobs.Add(TilesJob(w, 0)); jobs.Add(TilesJob(w, 4)); }
                if (w.decorPrompts != null)
                {
                    w.decorImages = new string[12];
                    var heights = Enumerable.Range(0, 12).Select(i => DecorHeight(w.decorSizes, i, w.decorPlaces)).ToArray();
                    for (int s = 0; s < 12; s += 4) jobs.Add(SheetJob(w.decorPrompts, w.decorImages, s, heights.Skip(s).Take(4).ToArray(), w.themePrompt));
                }
                if (w.townPrompts != null)
                {
                    w.townImages = new string[4];
                    jobs.Add(SheetJob(w.townPrompts, w.townImages, 0, new[] { 58, 58, 30, 26 }, w.themePrompt, "village buildings and objects, front view"));
                }
            }
            if (sections[3])
            {
                var arts = result.enemies.art.Where(a => a.imagePrompt != null).ToList();
                for (int i = 0; i < arts.Count; i += 2) jobs.Add(EnemySheetJob(arts.Skip(i).Take(2).ToList()));
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
            var imageCost = (ImageClient.PriceUsd(ImageClient.Model) ?? 0) * (jobs.Count - failed);
            LastCostUsd = textCost + imageCost;
            string status = $"새 디자인 완성 (글 {textIn}+{textOut}토큰, 그림 {jobs.Count - failed}장 · 약 ${LastCostUsd:0.000})";
            if (failed > 0) status += $"\n<color=#ffb36b>그림 {failed}장 실패: {ImageClient.LastStatus} — 그 부분은 기본 도트로 표시</color>";
            done(result, status);
        }

        /// <summary>
        /// Every enemy id gets a design: the model's families spread over the enemies by role (a boss becomes the family's
        /// king, a swarmer a tiny flying one), in the world's theme. Names follow the family.
        /// </summary>
        static void FillEnemies(DesignPack p)
        {
            var e = p.enemies;
            if (e.families.Count == 0) return;
            string theme = p.world.themePrompt != null ? ", belonging to a world that is " + p.world.themePrompt : "";
            var ids = DB.Enemies.Values.ToList();
            e.art = new List<EnemyArt>();
            e.names = new List<NameEntry>();
            for (int i = 0; i < ids.Count; i++)
            {
                var d = ids[i];
                var fam = e.families[i % e.families.Count];
                var (en, ko) = Role(d);
                e.art.Add(new EnemyArt { id = d.id, imagePrompt = $"{fam.imagePrompt}, {en}{theme}" });
                if (fam.name != null) e.names.Add(new NameEntry { id = d.id, name = fam.name + " " + ko });
            }
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
            yield return ImageClient.Generate(Style + "A sprite sheet in a grid of 3 columns and 2 rows: the SAME character drawn six times with identical " +
                "design, colours and size, full body, side view facing right, each pose centered in its own cell with empty space around it. " +
                "Top row: standing idle; walking with the front leg stepping forward; walking with the back leg stepping forward. " +
                $"Bottom row: winding up to attack ({attack}, pulled back); the attack striking forward ({attack}, fully extended); flinching backward after being hit." +
                hands + " Character: " + p.hero.imagePrompt, t => tex = t);
            if (tex == null) { p.hero.image = null; p.hero.frames = null; yield return false; yield break; }
            var cells = Pixelizer.ConvertSet(tex, Enumerable.Range(0, 6).Select(i => Cell(tex, i % 3, i / 3, 3, 2)).ToArray(), 28, byHeight: true);
            UnityEngine.Object.Destroy(tex);
            p.hero.frames = cells.Select(Save).ToArray();
            p.hero.image = p.hero.frames[0];
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

        /// <summary>Ground textures: 8 swatches (2 variants of ground / path / liquid / rough) over two opaque sheets.</summary>
        static IEnumerator TilesJob(WorldLook w, int first)
        {
            var t = w.tilePrompts;
            string[] cells = { "Top-left", "Top-right", "Bottom-left", "Bottom-right" };
            string[] what = { "walkable ground", "walkable ground (variant)", "a path", "a path (variant)", "a liquid", "a liquid (variant)", "rough ground", "rough ground (variant)" };
            var parts = Enumerable.Range(0, 4).Select(i => $"{cells[i]}, {what[first + i]}: {t[first + i] ?? t[(first + i) / 2 * 2] ?? "plain"}");
            Texture2D tex = null;
            yield return ImageClient.Generate(TileStyle + "A 2x2 grid of four square texture swatches filling the whole image edge to edge, no borders or gaps. " +
                "Each swatch is subtle and low contrast: one main colour with fine detail, no big shapes, no grid lines, no checkerboard. " +
                string.Join(". ", parts) + "." + (w.themePrompt != null ? " World: " + w.themePrompt + "." : ""), x => tex = x, transparent: false);
            if (tex == null) { yield return false; yield break; }
            for (int i = 0; i < 4; i++) w.tileImages[first + i] = Save(Pixelizer.Swatch(tex, Quarter(tex, i), 32));
            UnityEngine.Object.Destroy(tex);
            yield return true;
        }

        /// <summary>Picture height of a scenery object by its size class (buildings along a road stand taller).</summary>
        static int DecorHeight(string[] sizes, int i, string[] places)
        {
            string size = sizes != null && i < sizes.Length ? sizes[i] : DesignLayout.Sizes[Mathf.Min(3, i / 3)];
            bool road = places != null && i < places.Length && places[i] == "road";
            return size switch { "large" => road ? 48 : 40, "medium" => 18, "small" => 13, _ => 8 };
        }

        /// <summary>Four separate objects from prompts[first..first+3] into ids[first..], each shrunk to its height.</summary>
        static IEnumerator SheetJob(string[] prompts, string[] ids, int first, int[] heights, string theme, string what = "scenery objects for a top-down game world, standing upright, front view")
        {
            string[] cells = { "Top-left", "Top-right", "Bottom-left", "Bottom-right" };
            var parts = Enumerable.Range(0, 4).Select(i => $"{cells[i]}: {prompts[first + i] ?? "nothing (leave empty)"}");
            Texture2D tex = null;
            yield return ImageClient.Generate(Style + $"A 2x2 grid of four separate {what}, each centered in its own quarter with empty space around it. "
                                              + string.Join(". ", parts) + "." + (theme != null ? " World: " + theme + "." : ""), t => tex = t);
            if (tex == null) { yield return false; yield break; }
            for (int i = 0; i < 4; i++)
                if (prompts[first + i] != null) ids[first + i] = Save(Pixelizer.Convert(tex, Quarter(tex, i), heights[i], byHeight: true));
            UnityEngine.Object.Destroy(tex);
            yield return Enumerable.Range(first, 4).All(i => prompts[i] == null || ids[i] != null);
        }

        /// <summary>Two enemies per sheet, each in three poses (standing / moving / attacking) so they walk and strike.</summary>
        static IEnumerator EnemySheetJob(List<EnemyArt> group)
        {
            var layout = "A sprite sheet in a grid of 3 columns and 2 rows: two different monsters, each drawn three times with identical design, " +
                         "colours and size, all facing right, each pose centered in its own cell with empty space around it. Each row is one monster: " +
                         "standing; moving (mid-step); attacking (lunging forward). ";
            var parts = group.Select((a, i) => $"Row {i + 1}: {a.imagePrompt}");
            Texture2D tex = null;
            yield return ImageClient.Generate(Style + layout + string.Join(". ", parts) + ".", t => tex = t);
            if (tex == null) { foreach (var a in group) a.image = a.image2 = a.image3 = null; yield return false; yield break; }
            for (int i = 0; i < group.Count; i++)
            {
                var d = DB.Enemy(group[i].id);
                var poses = Pixelizer.ConvertSet(tex, Enumerable.Range(0, 3).Select(c => Cell(tex, c, i, 3, 2)).ToArray(), Art.EnemyHeight(d.sprite), byHeight: true);
                group[i].image = Save(poses[0]);
                group[i].image2 = Save(poses[1]);
                group[i].image3 = Save(poses[2]);
            }
            UnityEngine.Object.Destroy(tex);
            yield return group.All(a => a.image != null);
        }

        // ───────────────────────── Facing check ─────────────────────────

        [Serializable] class Dirs { public List<string> dirs; }

        /// <summary>
        /// Every sprite in the game faces / points / flies to the right (Unity flips it for the left). Put the new hero, weapon,
        /// shot, swing effect and enemies on one contact sheet, ask the text model (low-detail vision, ~$0.0005) which way each
        /// one faces, and mirror the ones that face left (all poses of a subject together).
        /// </summary>
        static IEnumerator FixFacing(DesignPack p, bool[] sections, Action<float> cost)
        {
            var subjects = new List<(string what, string id, Action<Func<string, string>> flip)>();
            if (sections[0] && p.hero.frames != null)
                subjects.Add(("a character: which way it faces", p.hero.frames[0], f => { p.hero.frames = p.hero.frames.Select(f).ToArray(); p.hero.image = p.hero.frames[0]; }));
            if (sections[1])
            {
                var w = p.weapon; var a = p.attack;
                if (w.image != null) subjects.Add(("a weapon or tool: on which side is the end that hits or shoots (tip, barrel, head)", w.image, f => w.image = f(w.image)));
                if (a.projectile.image != null) subjects.Add(("a flying shot: on which side is its front", a.projectile.image, f => a.projectile.image = f(a.projectile.image)));
                if (a.effectImages != null) subjects.Add(("an attack sweep effect: toward which side does it curve or spread", a.effectImages[0], f => a.effectImages[0] = f(a.effectImages[0])));
            }
            if (sections[3])
                foreach (var e in p.enemies.art.Where(e => e.image != null))
                    subjects.Add(("a monster: which way it faces", e.image, f => { e.image = f(e.image); e.image2 = f(e.image2); e.image3 = f(e.image3); }));
            if (subjects.Count == 0) { cost(0); yield break; }

            const int cell = 64;
            int cols = Mathf.Min(8, subjects.Count), rows = Mathf.CeilToInt(subjects.Count / (float)cols);
            var sheet = new PixelCanvas(cols * cell, rows * cell);
            sheet.Rect(0, 0, sheet.W, sheet.H, new Color32(205, 205, 205, 255));
            for (int i = 0; i < subjects.Count; i++)
            {
                var tex = DesignStore.Image(subjects[i].id);
                if (tex == null) continue;
                var px = tex.GetPixels32();
                int k = Mathf.Max(1, 56 / Mathf.Max(tex.width, tex.height));
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
            yield return AiClient.CompleteVision("You look at pixel-art game sprites and say which way they face. Output JSON only.", question, png, 300, r => json = r);
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
