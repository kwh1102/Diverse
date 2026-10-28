using System.Collections.Generic;
using System.Linq;

namespace Diverse
{
    /// <summary>
    /// Free, offline prompt reader (no API key, AI off, or the call failed): Korean keywords → the same closed-set spec
    /// the AI writes. Rougher than the AI, but every prompt still changes the world and nothing here can touch balance.
    /// </summary>
    public static class DesignLocal
    {
        static readonly (string[] words, string hex)[] Colors =
        {
            (new[] { "빨강", "빨간", "붉은", "레드", "적색" }, "#e8584f"),
            (new[] { "주황", "오렌지" }, "#ff9a3d"),
            (new[] { "노랑", "노란", "황금", "금색", "금빛" }, "#f4c95d"),
            (new[] { "초록", "녹색", "연두", "풀색" }, "#5fae64"),
            (new[] { "하늘색", "하늘빛" }, "#8fd3ff"),
            (new[] { "파랑", "파란", "푸른", "블루", "청색" }, "#4f7bd9"),
            (new[] { "남색" }, "#3a3f8f"),
            (new[] { "보라", "보랏", "자주" }, "#9b6bd9"),
            (new[] { "분홍", "핑크", "벚꽃" }, "#ff9ec4"),
            (new[] { "검정", "검은", "까만", "흑색", "블랙" }, "#3a3340"),
            (new[] { "흰", "하얀", "흰색", "백색", "화이트" }, "#fff8ef"),
            (new[] { "회색", "잿빛", "은회색" }, "#9a96a6"),
            (new[] { "갈색", "밤색", "나무색" }, "#8a5a3c"),
            (new[] { "은색", "은빛" }, "#d9e1ea"),
            (new[] { "민트", "청록" }, "#7fe0c8"),
            (new[] { "베이지", "크림" }, "#e8d9b8"),
        };

        static readonly (string[] words, string hex, string core)[] Elements =
        {
            (new[] { "불", "화염", "용암", "불꽃" }, "#ff8a3d", "#fff36b"),
            (new[] { "얼음", "서리", "냉기", "눈꽃" }, "#8fe3ff", "#ffffff"),
            (new[] { "번개", "전기", "벼락" }, "#fff36b", "#ffffff"),
            (new[] { "어둠", "그림자", "암흑" }, "#b18cff", "#e6dcff"),
            (new[] { "신성", "천사", "빛나" }, "#fff2b3", "#ffffff"),
            (new[] { "독", "맹독" }, "#9be05a", "#e8ffb8"),
            (new[] { "바람", "돌풍" }, "#b8ffcf", "#ffffff"),
            (new[] { "핏빛", "피의", "선혈" }, "#ff5a6e", "#ffd0d6"),
        };

        static bool Any(string s, params string[] words) => words.Any(s.Contains);

        /// <summary>Colors in the order they appear in the text.</summary>
        static List<(int at, string hex)> FindColors(string s)
        {
            var found = new List<(int, string)>();
            foreach (var (words, hex) in Colors)
                foreach (var w in words)
                {
                    int i = s.IndexOf(w, System.StringComparison.Ordinal);
                    if (i >= 0) { found.Add((i, hex)); break; }
                }
            return found.OrderBy(f => f.Item1).ToList();
        }

        /// <summary>The color word closest before any of the nouns ("보라색 숲" → foliage purple).</summary>
        static string ColorBefore(string s, List<(int at, string hex)> colors, params string[] nouns)
        {
            foreach (var n in nouns)
            {
                int i = s.IndexOf(n, System.StringComparison.Ordinal);
                if (i < 0) continue;
                var c = colors.Where(x => x.at < i && i - x.at <= 8).OrderByDescending(x => x.at).FirstOrDefault();
                if (c.hex != null) return c.hex;
            }
            return null;
        }

        public static DesignPack Generate(DesignPack current, bool[] sections, bool lockChassis)
        {
            var p = current.Clone();
            if (sections[0]) Hero(p, p.prompts[0], lockChassis);
            if (sections[1]) Weapon(p, p.prompts[1], lockChassis);
            if (sections[2]) World(p.world, p.prompts[2]);
            if (sections[3]) Enemies(p.enemies, p.prompts[3]);
            p.source = "local";
            p.summary = "로컬 해석: " + string.Join(" / ", Enumerable.Range(0, 4).Where(i => sections[i] && p.prompts[i].Length > 0)
                .Select(i => p.prompts[i].Length > 14 ? p.prompts[i].Substring(0, 14) + "…" : p.prompts[i]));
            DesignGuard.Sanitize(p);
            return p;
        }

        static void Hero(DesignPack p, string s, bool lockChassis)
        {
            var h = p.hero;
            var colors = FindColors(s);
            h.fur = ColorBefore(s, colors, "털", "토끼") ?? h.fur;
            var rest = colors.Where(c => c.hex != h.fur).Select(c => c.hex).ToList();
            if (rest.Count > 0) { h.outfit = rest[0]; h.outfitShade = null; }
            if (rest.Count > 1) h.accent = rest[1];
            h.hat = Any(s, "왕관") ? "crown" : Any(s, "마법사", "고깔") ? "wizard" : Any(s, "모자", "캡") ? "cap" : Any(s, "머리띠", "두건") ? "band"
                  : Any(s, "뿔") ? "horns" : Any(s, "꽃") ? "flower" : Any(s, "투구", "헬멧", "기사") ? "helmet" : Any(s, "후광", "천사") ? "halo"
                  : Any(s, "리본") ? "ribbon" : Any(s, "새싹", "나뭇잎") ? "leaf" : Any(s, "안경") ? "glasses" : h.hat;
            if (Any(s, "망토")) h.style = 3;
            else if (Any(s, "목도리", "스카프")) h.style = 0;
            if (!lockChassis)
            {
                // Theme → which rabbit's perk is inherited (look-driven, never stat-driven)
                h.basePerk = Any(s, "도적", "닌자", "그림자", "검은") ? "black" : Any(s, "기사", "갑옷", "전사") ? "gray"
                           : Any(s, "왕", "부자", "황금", "상인") ? "gold" : Any(s, "요정", "꽃", "치유", "분홍") ? "pink"
                           : Any(s, "사냥", "야생", "숲") ? "brown" : h.basePerk;
            }
            var named = s.Split(' ', ',', '.').FirstOrDefault(w => w.EndsWith("토끼") || w.EndsWith("기사") || w.EndsWith("장인") || w.EndsWith("마법사"));
            if (named != null && named.Length >= 2) h.name = named;
        }

        static void Weapon(DesignPack p, string s, bool lockChassis)
        {
            var w = p.weapon;
            var a = p.attack;
            var colors = FindColors(s);
            (string shape, string name, int len, int th)? obj =
                Any(s, "이쑤시개") ? ("needle", "이쑤시개", 12, 1) : Any(s, "젓가락") ? ("needle", "젓가락", 18, 1) : Any(s, "바늘", "침") ? ("needle", "바늘", 12, 1)
                : Any(s, "꼬챙이", "꼬치") ? ("needle", "꼬챙이", 16, 1) : Any(s, "요술봉", "완드", "지팡이") ? ("wand", "지팡이", 18, 1)
                : Any(s, "몽둥이", "방망이") ? ("club", "몽둥이", 20, 3) : Any(s, "빗자루", "막대", "봉") ? ("stick", "막대", 26, 2)
                : Any(s, "삼지창") ? ("spear", "삼지창", 30, 1) : Any(s, "창", "작살") ? ("spear", "창", 30, 1)
                : Any(s, "도끼") ? ("axe", "도끼", 22, 1) : Any(s, "망치", "해머") ? ("hammer", "망치", 22, 2)
                : Any(s, "활", "석궁") ? ("bow", "활", 18, 2) : Any(s, "카타나", "곡도", "낫") ? ("curved", "곡도", 28, 2)
                : Any(s, "단검", "나이프", "식칼") ? ("blade", "단검", 14, 2) : Any(s, "대검") ? ("blade", "대검", 32, 4)
                : Any(s, "검", "칼", "소드") ? ("blade", "검", 24, 3) : Any(s, "맨손", "주먹") ? ("builtin", "맨손", 0, 0)
                : ((string, string, int, int)?)null;
            if (obj != null)
            {
                w.shape = obj.Value.shape; w.name = obj.Value.name; w.length = obj.Value.len; w.thickness = obj.Value.th;
                w.tip = w.shape == "wand" ? "orb" : w.name == "삼지창" ? "fork" : "point";
                if (w.name == "맨손") w.hold = "Hidden";
                if (w.name == "이쑤시개" || w.name == "젓가락" || w.name == "꼬챙이") { w.main = "#e8cf9a"; w.shade = "#b8955e"; }
            }
            if (Any(s, "입에", "입으로", "물고", "뱉")) { a.origin = "Mouth"; if (w.shape == "needle") w.hold = "Mouth"; }
            else if (Any(s, "눈에서", "눈빛", "레이저")) a.origin = "Eyes";
            else if (Any(s, "머리 위", "하늘에서")) a.origin = "Above";

            if (colors.Count > 0) { w.main = colors[0].hex; w.shade = null; }
            if (colors.Count > 1) w.accent = colors[1].hex;
            var el = Elements.FirstOrDefault(e => Any(s, e.words));
            if (el.hex != null) { a.slash = el.hex; a.slashCore = el.core; a.trail = el.hex; w.glow = el.hex; }
            else if (colors.Count > 0) { a.slash = colors[0].hex; a.trail = colors[0].hex; }

            bool shoots = Any(s, "쏘", "쏜", "발사", "던지", "던져", "날리", "뱉", "화살", "총");
            string shot = Any(s, "별") ? "star" : Any(s, "하트") ? "heart" : Any(s, "나뭇잎", "잎사귀") ? "leaf" : Any(s, "음표", "노래") ? "note"
                        : Any(s, "비눗방울", "거품") ? "bubble" : Any(s, "구슬", "탄환", "공을") ? "orb" : Any(s, "결정", "수정", "얼음") ? "shard"
                        : Any(s, "고리", "링") ? "ring" : Any(s, "검기", "파동", "참격") ? "wave" : null;
            if (shot != null) a.projectile.shape = shot;
            else if (shoots && obj != null && w.shape != "builtin" && w.shape != "bow") a.projectile.shape = "weapon";
            if (colors.Count > 0) a.projectile.main = el.hex ?? colors[0].hex;

            if (!lockChassis)
            {
                w.baseWeapon = Any(s, "마법", "주문", "마력") || w.shape == "wand" ? nameof(WeaponKind.Staff)
                    : shoots || w.shape == "bow" ? nameof(WeaponKind.Crossbow)
                    : Any(s, "방패") ? nameof(WeaponKind.SwordShield)
                    : w.shape is "axe" or "hammer" or "club" || Any(s, "묵직", "대검", "거대") ? nameof(WeaponKind.Greatsword)
                    : w.shape == "curved" || Any(s, "베기", "베어") ? nameof(WeaponKind.Katana)
                    : w.shape is "needle" or "spear" || Any(s, "찌르", "단검", "재빠") ? nameof(WeaponKind.Dagger)
                    : w.baseWeapon;
                // Attack parts = that fighting style's combo (DesignCombat rebalances it like any designed weapon)
                var style = DB.W(System.Enum.TryParse(w.baseWeapon, out WeaponKind k) ? k : WeaponKind.Greatsword);
                w.moves = style.combo.Select(DesignCombat.Classify).Take(4).ToList();
                w.weight = "medium";
                w.skills = style.skills;
            }
        }

        static void World(WorldLook w, string s)
        {
            var colors = FindColors(s);
            void Preset(string grass, string path, string water, string foliage, float sat = 1, float bright = 1, string tint = null, float amt = 0)
            { w.grass = grass; w.path = path; w.water = water; w.foliage = foliage; w.saturation = sat; w.brightness = bright; w.tint = tint; w.tintAmount = amt; w.hueShift = 0; }

            if (Any(s, "사탕", "과자", "디저트", "케이크")) { Preset("#ffc8dd", "#fff2b3", "#9fe0ff", "#ff9ec4"); w.mood = "과자 나라"; }
            else if (Any(s, "겨울", "눈 내", "설원", "눈밭")) { Preset("#eaf2fb", "#c9d6e6", "#8fc6f2", "#cfe0f0"); w.mood = "겨울"; }
            else if (Any(s, "가을", "단풍")) { Preset("#c9a24f", "#d8b47e", "#5a92dc", "#e0823d"); w.mood = "가을"; }
            else if (Any(s, "사막", "모래")) { Preset("#e3c685", "#d8b47e", "#5aa0e0", "#9aa05a"); w.mood = "사막"; }
            else if (Any(s, "화산", "용암", "지옥")) { Preset("#6e6372", "#8a5a4a", "#ff7a3d", "#8a4a3a", 1, 0.9f, "#ff5a3d", 0.12f); w.mood = "화산"; }
            else if (Any(s, "네온", "사이버", "미래")) { Preset("#2b2a4a", "#3d3d6b", "#39f2ff", "#ff4fd8"); w.mood = "네온"; }
            else if (Any(s, "늪", "독")) { Preset("#6f7f5a", "#7a6a4a", "#7aa05a", "#4a5a33"); w.mood = "늪"; }
            else if (Any(s, "벚꽃", "봄")) { Preset("#8fd67a", "#e5c793", "#8cc6f2", "#f2a7c3"); w.mood = "봄"; }
            else if (Any(s, "흑백", "모노", "옛날 영화")) { Preset(null, null, null, null, 0.35f); w.mood = "흑백"; }
            else if (Any(s, "우주", "별빛")) { Preset("#2a2340", "#4a3d6b", "#1a1a3a", "#7b5cff", 1, 0.85f); w.mood = "우주"; }
            if (Any(s, "밤", "어두운", "달빛")) { w.brightness = 0.72f; w.tint = "#2a2a5a"; w.tintAmount = 0.25f; w.mood ??= "밤"; }

            w.foliage = ColorBefore(s, colors, "숲", "나무", "잎") ?? w.foliage;
            w.water = ColorBefore(s, colors, "물", "바다", "강", "호수") ?? w.water;
            w.path = ColorBefore(s, colors, "길") ?? w.path;
            w.grass = ColorBefore(s, colors, "풀", "땅", "들판", "잔디", "초원") ?? w.grass;
            if (w.IsDefault && colors.Count > 0) { w.tint = colors[0].hex; w.tintAmount = 0.3f; }
        }

        static void Enemies(EnemyLook e, string s)
        {
            var colors = FindColors(s);
            (string[] words, string theme, string tint, float amt, float sat, float bright)[] themes =
            {
                (new[] { "과자", "사탕", "디저트" }, "과자", "#ff9ec4", 0.4f, 1, 1.05f),
                (new[] { "좀비", "언데드", "해골" }, "좀비", "#7aa05a", 0.4f, 0.7f, 0.9f),
                (new[] { "유령", "귀신" }, "유령", "#c9d6ff", 0.35f, 0.45f, 1.15f),
                (new[] { "악마", "지옥", "불" }, "지옥", "#ff5a3d", 0.35f, 1.1f, 1),
                (new[] { "얼음", "눈", "겨울" }, "얼음", "#8fe3ff", 0.35f, 0.9f, 1.05f),
                (new[] { "기계", "로봇", "강철" }, "기계", "#9aa7b8", 0.35f, 0.45f, 1),
                (new[] { "황금", "보물" }, "황금", "#f4c95d", 0.4f, 1, 1.05f),
                (new[] { "그림자", "어둠" }, "그림자", "#3a2a5a", 0.35f, 0.8f, 0.75f),
                (new[] { "독", "늪" }, "독", "#9be05a", 0.35f, 1, 1),
            };
            var t = themes.FirstOrDefault(x => Any(s, x.words));
            if (t.theme != null)
            {
                e.theme = t.theme; e.tint = t.tint; e.tintAmount = t.amt; e.saturation = t.sat; e.brightness = t.bright; e.hueShift = 0;
                // Rename every enemy with the theme as a prefix ("과자 버섯 꼬마")
                e.names = DB.Enemies.Values.Select(d => new NameEntry { id = d.id, name = (t.theme + " " + d.name).Length <= DesignGuard.MaxName ? t.theme + " " + d.name : t.theme + " " + d.name.Split(' ').Last() }).ToList();
            }
            else if (colors.Count > 0) { e.tint = colors[0].hex; e.tintAmount = 0.35f; }
        }
    }
}
