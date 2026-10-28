using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// A world's look, written by the player's prompts (AI or the local keyword parser) and drawn locally by DesignArt.
    /// It holds ONLY appearance: colors, closed-set shapes, optional tiny pixel grids and names.
    /// Gameplay comes from an existing chassis (baseWeapon / basePerk), so a prompt can never change balance.
    /// Self-contained JSON (designs/&lt;id&gt;.json) so it can later be uploaded to a server as-is.
    /// </summary>
    [Serializable]
    public class DesignPack
    {
        public const int Version = 1;
        public int version = Version;
        public string id;                 // file name; a committed pack is never edited, a change makes a new id
        public string parent;             // the pack this one was edited from
        public long created;
        public string summary;            // one line shown in the studio
        public string source;             // "ai" | "local" | "default"
        public HeroLook hero = new HeroLook();
        public WeaponLook weapon = new WeaponLook();
        public AttackLook attack = new AttackLook();
        public WorldLook world = new WorldLook();
        public EnemyLook enemies = new EnemyLook();
        public List<string> prompts = new List<string> { "", "", "", "" };   // per studio tab
        public List<string> ignored = new List<string>();                     // power requests that were not applied

        public static DesignPack Default() => new DesignPack { source = "default", summary = "기본 디자인" };

        public DesignPack Clone()
        {
            var c = JsonUtility.FromJson<DesignPack>(JsonUtility.ToJson(this));
            DesignGuard.Sanitize(c);
            return c;
        }

        /// <summary>Copy one studio section from another pack (0 hero, 1 weapon+attack, 2 world, 3 enemies).</summary>
        public void CopySection(DesignPack from, int section)
        {
            var src = from.Clone();
            switch (section)
            {
                case 0: hero = src.hero; break;
                case 1: weapon = src.weapon; attack = src.attack; break;
                case 2: world = src.world; break;
                case 3: enemies = src.enemies; break;
            }
            prompts[section] = src.prompts[section];
        }
    }

    /// <summary>
    /// The hero. With an API key it is a fresh picture (image) of anything the player described — not necessarily a rabbit.
    /// Without one, the rabbit is recolored from the fields below (local fallback). The starting passive (perk) is generated
    /// once from perkConcept by the Rule IR pipeline (validated + power budget), then kept with the pack.
    /// </summary>
    [Serializable]
    public class HeroLook
    {
        public string name;               // shown as the costume name (e.g. "마법사 문어")
        public string species;            // Korean, e.g. "문어"
        public string imagePrompt;        // English description for the image model
        public string image;              // DesignStore image id: the idle pose (pixelized PNG)
        public string[] frames;           // 6 poses from one sheet: idle, step A, step B, attack wind-up, attack strike, hurt (older packs: 4)
        public string perkConcept;        // English: what the starting passive should express
        public AbilityRecord perk;        // generated starting passive (filled on the first life)
        public string basePerk = "white"; // local fallback only: which rabbit colors to start from
        public string fur, furShade, outfit, outfitShade, accent;   // hex; empty = the base costume's color
        public int style = -1;            // 0..5 (Art.Rabbit outfit styles); -1 = the base costume's
        public string hat = "none";       // DesignArt.Hats
        public string hatColor;
    }

    /// <summary>
    /// The held object and how it fights. Attack parts (moves + weight) and QWER skill ids are turned into a WeaponDef by
    /// DesignCombat with numbers from the existing weapon assets, normalized to the built-in power budget.
    /// image = a fresh picture; shape/pixels = the local fallback drawing.
    /// </summary>
    [Serializable]
    public class WeaponLook
    {
        public string name;
        public string imagePrompt;                 // English (image model)
        public string image;                       // DesignStore image id
        public List<string> moves = new List<string>();   // DesignCombat.Moves, 1..4 = the basic attack combo
        public string weight = "medium";           // light | medium | heavy
        public string[] skills;                    // QWER ids from DesignCombat.AllSkills (R = an ultimate)
        public string[] skillNames;                // Korean display names for them
        public string baseWeapon = "Greatsword";   // WeaponKind (local fallback without attack parts)
        public string shape = "builtin";           // DesignArt.WeaponShapes
        public int length;                         // px (0 = shape default)
        public int thickness;                      // px (0 = shape default)
        public string tip = "point";               // DesignArt.Tips
        public string main, shade, accent, grip, glow;
        public string hold = "Hand";               // Hand | Mouth | Hidden
        public string attackPose;                  // English: how the hero attacks (shown in the hero sheet's attack cells; bare hands: punches, kicks…)
        public string[] pixels;                    // optional grid ('.' clear, '1'..'9' palette index), pointing right
        public string[] palette;
    }

    [Serializable]
    public class ProjectileLook
    {
        public string imagePrompt;                 // English (image model)
        public string image;                       // DesignStore image id
        public string shape = "builtin";           // DesignArt.ProjectileShapes ("weapon" = a small copy of the weapon)
        public int size;                           // px (0 = shape default)
        public string main, accent;
        public bool spin;
        public string[] pixels;
        public string[] palette;
    }

    /// <summary>How attacks look: where they come out, projectile art, slash/trail colors.</summary>
    [Serializable]
    public class AttackLook
    {
        public string origin = "Hand";             // Hand | Mouth | Eyes | Above
        public ProjectileLook projectile = new ProjectileLook();
        public string slash, slashCore, trail;     // hex; empty = the chassis' colors
        public string[] effectPrompts;             // English: swing, impact, trail, muzzle (one 2x2 effect sheet)
        public string[] effectImages;              // DesignStore image ids in the same order
    }

    /// <summary>Whole-palette transform (world vegetation/ground, enemies).</summary>
    [Serializable]
    public class PaletteShift
    {
        public float hueShift;                     // degrees
        public float saturation = 1;
        public float brightness = 1;
        public string tint;
        public float tintAmount;

        public bool Identity => Mathf.Abs(hueShift) < 0.5f && Mathf.Abs(saturation - 1) < 0.01f && Mathf.Abs(brightness - 1) < 0.01f
                                && (string.IsNullOrEmpty(tint) || tintAmount <= 0.001f);
    }

    [Serializable]
    public class WorldLook : PaletteShift
    {
        public string mood;                        // one Korean word for the studio summary
        public string themePrompt;                 // English, short: what this world is made of (keeps every picture in one theme)
        public string grass, path, water, rough, foliage; // hex overrides (rough = sand/snow/swamp/ash; empty = shifted original)
        public string layout = "wild";             // DesignLayout.Styles: how roads, buildings and scenery are arranged
        public string[] tilePrompts;               // English ground textures, 2 variants each: ground A/B, path A/B, liquid A/B, rough A/B (two opaque sheets)
        public string[] tileImages;                // 32x32 swatches (8), tiled mirrored, variants mixed in patches
        public string[] decorPrompts;              // English, 12 scenery objects of this world
        public string[] decorSizes;                // per object: large | medium | small | tiny
        public string[] decorPlaces;               // per object: road | cluster | scatter | rows | rare (DesignLayout)
        public string[] decorImages;
        public string[] townPrompts;               // English: house A, house B, street light, landmark (well)
        public string[] townImages;
        public string ambient = "none";            // DesignArt.Ambients: drifting particles over the world
        public string ambientColor;

        static bool Full(string[] a, int n) => a != null && a.Length == n && a.All(s => !string.IsNullOrEmpty(s));
        public bool HasTiles => Full(tileImages, 8);
        public bool HasDecor => decorImages != null && decorImages.Length == 12 && decorImages.Count(s => !string.IsNullOrEmpty(s)) >= 6;
        public bool HasTown => Full(townImages, 4);
        public bool IsDefault => Identity && string.IsNullOrEmpty(grass) && string.IsNullOrEmpty(path) && string.IsNullOrEmpty(rough)
                                 && string.IsNullOrEmpty(water) && string.IsNullOrEmpty(foliage) && !HasTiles && !HasDecor && !HasTown && ambient == "none";
    }

    [Serializable]
    public class NameEntry { public string id, name; }

    /// <summary>A redrawn enemy: same behaviour and hitbox, new picture (drawn at the original sprite height).</summary>
    [Serializable]
    public class EnemyArt { public string id, imagePrompt, image, image2, image3; }   // idle, moving, attacking

    /// <summary>A kind of enemy in the theme; the engine turns families into one design per enemy id by role.</summary>
    [Serializable]
    public class EnemyFamily { public string name, imagePrompt; }

    [Serializable]
    public class EnemyLook : PaletteShift
    {
        public string theme;
        public List<EnemyFamily> families = new List<EnemyFamily>();
        public List<NameEntry> names = new List<NameEntry>();
        public List<EnemyArt> art = new List<EnemyArt>();

        public string ImageOf(string id)
        {
            foreach (var a in art) if (a != null && a.id == id && !string.IsNullOrEmpty(a.image)) return a.image;
            return null;
        }

        public string NameOf(string id)
        {
            foreach (var n in names) if (n != null && n.id == id && !string.IsNullOrWhiteSpace(n.name)) return n.name;
            return null;
        }
    }
}
