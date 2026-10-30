using System.Collections.Generic;
using System.Linq;

namespace Diverse.EditorTools
{
    /// <summary>
    /// The seed look of every zone: its own monsters (behaviour borrowed from a template species, a brand-new body),
    /// grounds, scenery, effects, and the English prompts ZoneArtBuilder sends to the image model.
    /// Copied into Data/Zones and Data/Enemies once; edit the assets (and redraw) afterwards, not this file.
    /// </summary>
    public static class ZoneArtDefaults
    {
        public class Monster
        {
            public BuildStyle zone;
            public string id, name, template, prompt;
        }

        /// <summary>Three regular monsters per zone. template = the species whose behaviour (brain, attack, numbers) it keeps.</summary>
        public static readonly List<Monster> Monsters = new List<Monster>
        {
            M(BuildStyle.MeleeBurst, "forge_beast", "용암 갑각수", "boar", "a hulking armadillo-like beast covered in black obsidian plates with glowing orange lava cracks between them"),
            M(BuildStyle.MeleeBurst, "forge_imp", "숯검댕 도깨비", "shroom", "a small chubby soot imp with glowing ember eyes holding a tiny blacksmith hammer"),
            M(BuildStyle.MeleeBurst, "forge_moth", "불티 나방", "bat", "a tiny moth with burning orange flame wings and a glowing ember body"),

            M(BuildStyle.MeleeSpeed, "jungle_panther", "핏빛 표범 전사", "fox", "a lean crimson panther warrior standing on two legs with bone daggers and white tribal paint"),
            M(BuildStyle.MeleeSpeed, "jungle_hummer", "가시부리 벌새", "bat", "a tiny bright red hummingbird with a long needle-sharp beak"),
            M(BuildStyle.MeleeSpeed, "jungle_frog", "독화살 개구리", "frog", "a bright red and black poison dart frog hunter wearing leaf armor"),

            M(BuildStyle.RangedBurst, "glacier_spirit", "빙정 정령", "frostling", "a floating jagged ice crystal spirit with a glowing pale blue core and tiny eyes"),
            M(BuildStyle.RangedBurst, "glacier_mammoth", "서리 매머드", "boar", "a small woolly white mammoth with frost-covered tusks and blue ice armor plates"),
            M(BuildStyle.RangedBurst, "glacier_lantern", "얼음 등불 유령", "wisp", "a hovering ghost lantern carved of ice with a pale blue flame inside"),

            M(BuildStyle.RangedSpeed, "bog_toad", "가시등 두꺼비", "frog", "a warty purple bog toad with a spiny thorny back and yellow eyes"),
            M(BuildStyle.RangedSpeed, "bog_slime", "독거품 슬라임", "jelly", "a bubbling bright green toxic slime blob with a tiny skull floating inside"),
            M(BuildStyle.RangedSpeed, "bog_shroom", "포자 걸음버섯", "shroom", "a walking toxic purple mushroom with glowing green spore sacs on its cap"),

            M(BuildStyle.Mobility, "dune_slinger", "사막여우 투석꾼", "raccoon", "a fennec fox bandit with big ears, a striped scarf and a leather sling"),
            M(BuildStyle.Mobility, "dune_lizard", "모래도마뱀 검사", "fox", "a fast sand-colored lizard warrior with a curved scimitar and a cloth wrap"),
            M(BuildStyle.Mobility, "dune_scarab", "황금 풍뎅이", "boar", "a heavy golden scarab beetle with a shiny metallic shell and horn"),

            M(BuildStyle.Tank, "grave_wraith", "역병 망령", "wisp", "a hooded plague ghost holding a smoking censer of purple fog"),
            M(BuildStyle.Tank, "grave_crow", "해골 까마귀", "bat", "a tiny skeletal crow with ragged black feathers and glowing purple eyes"),
            M(BuildStyle.Tank, "grave_ghoul", "무덤파기 구울", "shroom", "a shambling grey zombie gravedigger with a rusty shovel and torn clothes"),

            M(BuildStyle.Evasion, "star_sentinel", "별빛 수호석상", "raccoon", "a small white stone temple guardian statue with glowing star-shaped eyes"),
            M(BuildStyle.Evasion, "star_sprite", "별조각 요정", "frostling", "a floating five-pointed violet crystal star sprite with a cute glowing face"),
            M(BuildStyle.Evasion, "star_eye", "천구의 눈", "wisp", "a hovering astral eye orb surrounded by golden orbiting rings"),

            M(BuildStyle.Area, "hive_grub", "꿀 애벌레", "jelly", "a plump round amber honey larva grub with tiny legs"),
            M(BuildStyle.Area, "hive_wasp", "일벌 말벌", "bat", "a small yellow and black striped worker wasp with clear wings"),
            M(BuildStyle.Area, "hive_ant", "병정개미", "shroom", "an armored red soldier ant standing upright with huge mandibles"),
        };

        static Monster M(BuildStyle z, string id, string name, string template, string prompt) => new Monster { zone = z, id = id, name = name, template = template, prompt = prompt };

        /// <summary>The body each zone master borrowed before it got its own picture (its signature move follows it).</summary>
        public static readonly Dictionary<string, string> BossSignature = new Dictionary<string, string>
        {
            ["boss_forge"] = "boss_knight", ["boss_bloodtail"] = "boss_fox", ["boss_frostwarden"] = "boss_knight", ["boss_bogking"] = "boss_shroom",
            ["boss_dunefox"] = "boss_fox", ["boss_gravekeeper"] = "boss_shroom", ["boss_starknight"] = "boss_knight", ["boss_hivequeen"] = "boss_shroom",
        };

        public static string[] EnemiesOf(BuildStyle z) => Monsters.Where(m => m.zone == z).Select(m => m.id).ToArray();

        public static ZoneArtDef Art(BuildStyle z)
        {
            var a = z switch
            {
                BuildStyle.MeleeBurst => new ZoneArtDef
                {
                    theme = "a volcanic forge land of black obsidian, glowing lava cracks and iron anvils",
                    ground = Ground.Scorch, groundAccent = Ground.Magma, ambient = "embers",
                    tilePrompts = new[] { "warm reddish-brown volcanic rock ground with grey ash and faint orange specks, medium brightness", "reddish-brown volcanic gravel with light grey ash patches, medium brightness",
                                          "dark red cooled lava crust with glowing orange veins, medium brightness", "rust-red cracked lava rock with a few glowing cracks, medium brightness" },
                    decorPrompts = new[] { "a charred black dead tree with glowing ember cracks", "a tall jagged black obsidian crystal spire",
                                           "a smoking volcanic vent rock chimney with an orange glow", "an iron anvil on a stone block with a hammer leaning on it",
                                           "a black basalt boulder with glowing lava veins", "a pile of dark slag rocks", "a glowing hot magma rock",
                                           "a small cluster of red fire flowers", "a dry thorny ash shrub", "tiny glowing orange ember pebbles", "a small crack glowing with lava" },
                    bossPrompt = "a towering molten lava golem blacksmith with an anvil-shaped fist, an iron apron and a glowing magma core in its chest",
                    effectPrompts = new[] { "a small glowing molten lava ball with a short flame tail, flying to the right", "a burst of orange sparks and embers",
                                            "a big round fiery explosion of lava and dark smoke", "a single tiny glowing orange ember" },
                },
                BuildStyle.MeleeSpeed => new ZoneArtDef
                {
                    theme = "a dense crimson jungle of giant leaves, red flowers, vines and ancient roots",
                    ground = Ground.JungleFloor, groundAccent = Ground.JungleMoss, ambient = "leaves",
                    tilePrompts = new[] { "bright leafy green jungle floor with fallen leaves and small roots", "bright leafy green jungle ground with scattered red petals",
                                          "vivid medium green moss with tiny red flowers", "vivid medium green moss with small clover leaves" },
                    decorPrompts = new[] { "a huge jungle tree with broad leaves and hanging vines", "a giant tropical tree with crimson leaves",
                                           "a tall fern palm tree", "an ancient twisted tree with red flowers",
                                           "a mossy stone wrapped in vines", "an old carved jungle idol stone", "a fallen log covered in moss",
                                           "a big leafy green fern bush", "a bush of crimson jungle flowers", "tiny red mushrooms", "small jungle grass sprouts" },
                    bossPrompt = "a giant crimson nine-tailed fox lord with tribal war paint, a bone necklace and glowing yellow eyes",
                    effectPrompts = new[] { "a small spinning thorn dart, flying to the right", "a slash burst of red leaves and sparks",
                                            "a big round burst of crimson petals and leaves", "a single small falling red leaf" },
                },
                BuildStyle.RangedBurst => new ZoneArtDef
                {
                    theme = "a frozen glacier fortress of blue ice walls, snow drifts and frost crystals",
                    ground = Ground.Glacier, groundAccent = Ground.Frost, ambient = "snow",
                    tilePrompts = new[] { "packed white snow with soft blue shadows", "white snow with small footprint dents and sparkles",
                                          "pale blue cracked ice surface", "smooth pale blue frozen ice with white frost streaks" },
                    decorPrompts = new[] { "a snow-covered pine tree", "a tall jagged blue ice crystal pillar", "a frozen dead tree with icicles",
                                           "a broken pillar of an ice fortress wall", "a snowy boulder", "a cluster of blue ice crystals", "a frozen rock with icicles",
                                           "a frosty shrub with snow on top", "a small bush of blue ice flowers", "a tiny snow mound", "small scattered ice shards" },
                    bossPrompt = "a huge armored glacier knight with an ice tower shield, a frost crown and a cape of icicles",
                    effectPrompts = new[] { "a sharp glowing icicle shard, flying to the right", "a burst of ice shards and frost sparkles",
                                            "a big round frost explosion of ice crystals", "a single small snowflake" },
                },
                BuildStyle.RangedSpeed => new ZoneArtDef
                {
                    theme = "a toxic swamp of murky green water, giant mushrooms, reeds and bubbling poison",
                    ground = Ground.Bog, groundAccent = Ground.BogMoss, ambient = "spores",
                    tilePrompts = new[] { "murky dark green swamp mud with small puddles", "dark olive swamp mud with tiny bubbles",
                                          "slimy bright green moss with tiny bubbles", "green algae moss with small purple specks" },
                    decorPrompts = new[] { "a giant purple mushroom tree", "a twisted swamp tree with hanging moss", "a tall glowing green mushroom",
                                           "a dead mangrove tree on tangled roots", "a mossy rotten swamp stump", "a slimy green rock", "a cluster of small glowing mushrooms",
                                           "a clump of tall swamp reeds", "a bush with toxic purple berries", "tiny green bubbles in mud", "a small lily pad sprout" },
                    bossPrompt = "a giant toxic toadstool king with a mossy beard, a crown dripping poison and glowing green eyes",
                    effectPrompts = new[] { "a small glowing green poison glob, flying to the right", "a splash of bright green toxic goo",
                                            "a burst of puffy toxic green gas clouds with bright highlights, bubbles and drips flying outward", "a single small glowing green spore" },
                },
                BuildStyle.Mobility => new ZoneArtDef
                {
                    theme = "a stormy golden desert of sand dunes, sandstone ruins and dry cactus",
                    ground = Ground.Dune, groundAccent = Ground.DuneRidge, ambient = "dust",
                    tilePrompts = new[] { "smooth golden sand with fine wind ripples", "golden sand with a few small pebbles",
                                          "darker orange windswept sand ridges", "orange sand with long wavy ripples" },
                    decorPrompts = new[] { "a tall saguaro cactus", "a broken sandstone pillar", "a dead bare desert tree", "a sandstone obelisk ruin",
                                           "a sandstone boulder", "a half-buried animal skull", "a cluster of desert rocks",
                                           "a dry tumbleweed", "a small round cactus with a pink flower", "tiny sand pebbles", "a small dry grass tuft" },
                    bossPrompt = "a huge desert sand fox with a swirling whirlwind tail, golden jewelry and a red scarf",
                    effectPrompts = new[] { "a small spinning sandstone pebble, flying to the right", "a burst of sand and dust",
                                            "a big round explosion of swirling golden sand and dust clouds", "a small wisp of blowing sand dust" },
                },
                BuildStyle.Tank => new ZoneArtDef
                {
                    theme = "a cursed plague graveyard of tombstones, dead trees, purple fog and iron fences",
                    ground = Ground.Grave, groundAccent = Ground.GraveMoss, ambient = "mist",
                    tilePrompts = new[] { "medium grey-lilac graveyard soil with dry dead grass, medium brightness", "grey-lilac dirt with small pale stones, medium brightness",
                                          "dusty mauve withered moss with tiny pale bones, medium brightness", "faded lilac-grey dead grass with small pebbles, medium brightness" },
                    decorPrompts = new[] { "a twisted dead black tree", "a leaning stone tombstone with a cross", "a gothic stone crypt pillar",
                                           "a dead tree with a hanging lantern", "a cracked tombstone", "a pile of skulls and bones", "a broken stone angel statue",
                                           "a withered thorny purple bush", "a cluster of pale ghost flowers", "tiny bones on the ground", "small purple mushrooms" },
                    bossPrompt = "a giant hunched plague undertaker with a beaked mask, a glowing lantern and a coffin on its back",
                    effectPrompts = new[] { "a small purple cursed skull flame, flying to the right", "a burst of purple smoke and wisps",
                                            "a burst of swirling purple plague smoke wisps with tiny skulls and sparks flying outward", "a small drifting wisp of purple fog" },
                },
                BuildStyle.Evasion => new ZoneArtDef
                {
                    theme = "a celestial starlit temple of pale violet stone, glowing runes and floating crystals",
                    ground = Ground.Starstone, groundAccent = Ground.StarMoss, ambient = "sparkles",
                    tilePrompts = new[] { "pale violet temple stone tiles with faint star patterns", "smooth pale lavender marble floor",
                                          "soft indigo-blue moss with tiny glowing star specks, fine and even, no pattern", "soft indigo-blue moss with a few silver sparkles, fine and even, no pattern" },
                    decorPrompts = new[] { "a tall floating violet crystal over a stone base", "a white marble temple column with glowing runes",
                                           "a celestial tree with glowing star-shaped fruits", "an ancient star altar arch",
                                           "a rune-carved moonstone", "a cluster of glowing purple crystals", "a broken marble statue head",
                                           "a bush with glowing blue star flowers", "a shrub with silver leaves", "tiny glowing star specks", "a small crystal sprout" },
                    bossPrompt = "a celestial knight in silver armor with a starry night-sky cape and a sword made of constellations",
                    effectPrompts = new[] { "a small glowing violet star bolt, flying to the right", "a burst of white and violet star sparkles",
                                            "a big round starburst of violet light", "a single tiny twinkling star" },
                },
                _ => new ZoneArtDef
                {
                    theme = "a sprawling insect hive plain of amber honeycomb, waxy mounds and golden grass",
                    ground = Ground.Hive, groundAccent = Ground.Comb, ambient = "pollen",
                    tilePrompts = new[] { "amber waxy hive ground with small honey drips", "golden brown earth with tiny wax flakes",
                                          "golden honeycomb pattern texture", "amber honeycomb cells partly filled with honey" },
                    decorPrompts = new[] { "a tall waxy hive spire tower with holes", "a giant honey-dripping yellow flower", "a large round beehive mound",
                                           "a tree wrapped in honeycomb", "a chunk of amber with a bug inside", "a small dirt ant hill", "a broken honeycomb piece",
                                           "a bush of golden flowers", "a clump of tall yellow grass", "tiny honey droplets", "small golden pollen flowers" },
                    bossPrompt = "a giant hive queen wasp with an amber crown, translucent wings and a huge striped abdomen",
                    effectPrompts = new[] { "a small glowing amber honey stinger, flying to the right", "a splash of golden honey",
                                            "a big round burst of honey and tiny swarming bees", "a single small floating golden pollen speck" },
                },
            };
            a.monsterPrompts = Monsters.Where(m => m.zone == z).Select(m => m.prompt).ToArray();
            return a;
        }

        /// <summary>The neutral meadow around the spawn.</summary>
        public static readonly string MeadowTheme = "a gentle green meadow with wildflowers around a small peaceful village";
        public static readonly string[] MeadowTiles =
        {
            "soft green grass with tiny white and yellow flowers", "soft green grass with small clover patches",
            "darker green grass with short blades", "dark green grass with a few tiny blue flowers",
        };
        public static readonly string[] MeadowDecor =
        {
            "a round leafy green tree", "a pink cherry blossom tree", "a leafy green oak tree with a thick brown trunk and a round crown", "a small apple tree with red apples",
            "a grey mossy boulder", "a small pile of rocks", "an old tree stump", "a round green bush with pink flowers", "a leafy green bush",
            "a few small wildflowers", "a small tuft of grass",
        };

        /// <summary>Shared props (camps, ruins, chests, the town). Same sheet = drawn together, so pairs match.</summary>
        public static readonly (string id, string prompt)[][] PropSheets =
        {
            new[] { ("chest", "a closed wooden treasure chest with gold trim"), ("chest_open", "the same wooden treasure chest opened and empty"),
                    ("altar", "a small stone altar with a glowing blue memory crystal on top"), ("altar_empty", "the same small stone altar with nothing on top") },
            new[] { ("tent", "a small canvas camping tent with a wooden pole"), ("campfire", "unlit campfire logs in a ring of stones"),
                    ("crate", "a wooden supply crate"), ("barrel", "a wooden barrel with iron bands") },
            new[] { ("pillar", "a standing ancient carved stone pillar"), ("pillar_broken", "a broken ancient stone pillar stump"),
                    ("shrine", "a small stone shrine with a glowing blue rune"), ("grave", "a rounded mossy stone tombstone") },
            new[] { ("statue", "a stone statue of a small brave rabbit hero holding a sword"), ("obelisk", "a tall dark obelisk with glowing pink runes"),
                    ("banner", "a red cloth banner on a wooden pole"), ("gate", "an ornate magical stone archway gate with a glowing portal") },
            new[] { ("cage", "a wooden prisoner cage"), ("well", "a stone water well with a small wooden roof"),
                    ("lamp", "a wooden street lamp post with a hanging lantern"), ("board", "a wooden notice board with papers pinned to it") },
            new[] { ("stall", "a small merchant stall with a striped awning"), ("anvil", "a blacksmith iron anvil on a tree stump") },
        };
    }
}
