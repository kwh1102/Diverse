using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Diverse
{
    /// <summary>What an event (or a context) provides to the operations that react to it — the type system of the Rule IR.</summary>
    [Flags]
    public enum Ctx
    {
        None = 0,
        Target = 1,      // a live enemy (EventTarget is valid)
        Corpse = 2,      // a dead enemy whose statuses can still be read (Kill)
        Amount = 4,      // event.amount is meaningful
        Direction = 8,
        Entity = 16,     // an entity of yours is the subject (its position, its kind)
    }

    /// <summary>One primitive of the Rule Language. Runtime handlers for every id exist in AbilityRuntime — the space is closed.</summary>
    public class Prim
    {
        public string id, cat, ko, desc;
        public string[] tags = new string[0];
        public int tier;
        // Trigger
        public float freq;               // generic events per second in combat
        public Ctx provides;
        public string param;             // "status" | "state" | "entity" | "pickup" | null
        // Operation
        public string kinds;             // allowed rule kinds as letters: T C R K M S (Trigger Continuous Replace Constrain Meta System)
        public string needs = "";        // required fields: target at status entity stat state tag element pair
        public string scalable = "amount"; // which number the balancer scales: amount | count | duration | none
        public float def, min, max;      // magnitude default/bounds (for `scalable`) — overridable in AbilityRules.asset
        public float defRadius, defDuration = 3, defCount = 1;
        public int cost = 1;             // runtime cost per execution (≈ hit checks / spawned objects)
        public string causes = "";       // events this op can raise (cycle detection)
        public string risk = "";         // RECURSION ENTITY PROC MULTIPLICATIVE PERMANENT META SYSTEM REDEFINE
        public string relations = "";    // relations this op accepts (type constraints of the Relation layer)
        public string temporals = "";    // temporals this op accepts
        // Selector / position / value reference
        public Ctx requires;
        public float expected, peak;     // value references: expected and worst-case value
        public bool enemy;               // selector returns enemies
        // Relation / temporal
        public float powerMul = 1;       // relations: expected extra executions (Echo 0.5 = +50%)

        /// <summary>Whole-word check on space-separated lists ("status" must not match "at" or "state").</summary>
        public bool Needs(string field) => Has(needs, field);
        public bool Accepts(string relation) => Has(relations, relation);
        public bool AcceptsTime(string temporal) => Has(temporals, temporal);
        static bool Has(string list, string w) => !string.IsNullOrEmpty(w) && Array.IndexOf(list.Split(' '), w) >= 0;
    }

    /// <summary>
    /// The Rule Language registry: the closed set of primitives the AI composes (the "generators" of the ability space).
    /// No finished skills live here — only the vocabulary of the design doc:
    ///   Ontology (entities + capabilities, values, hierarchical tags, elements with affinities, state types)
    ///   Trigger · Selector (actor / entity / ability / rule) · Position/Form · Condition · Value
    ///   Operation (combat · movement · spawn · state · modifier · conversion · replacement · constraint · meta · system)
    ///   Relation (how an effect links to things) · Temporal (when / how long)
    /// each with the metadata the validator, the power model and the risk model need. Primitive numbers can be retuned
    /// in Data/AbilityRules.asset (prims table) without touching code.
    /// </summary>
    public static class RuleLanguage
    {
        public static readonly Dictionary<string, Prim> ById = new Dictionary<string, Prim>();
        public static readonly List<Prim> All = new List<Prim>();
        public static IEnumerable<Prim> Cat(string c) => All.Where(p => p.cat == c);
        public static Prim Get(string id) => id != null && ById.TryGetValue(id, out var p) ? p : null;
        public static bool Is(string id, string cat) => Get(id)?.cat == cat;

        // ───────────── Ontology ─────────────

        public static readonly string[] Statuses = { "burn", "chill", "poison", "mark", "bleed", "weaken" };
        public static readonly string[] Entities = { "Clone", "PhantomWeapon", "Turret", "Orb", "Zone", "Mine", "Totem", "Barrier", "Decoy" };
        public static readonly string[] BuffStats = { "Attack", "AttackSpeed", "MoveSpeed", "CritChance", "Armor" };
        public static readonly string[] StateTypes = { "Counter", "StoredValue", "Charge", "Flag", "Timer", "Position", "Target" };
        public static readonly string[] Actions = { "Dash", "Potion", "ComboFinisher", "Regen", "Skill" };
        public static readonly string[] StatModes = { "Add", "Mul", "Min", "Max", "Override" };

        /// <summary>Capabilities per entity. World rule "Grip": a clone has 0 grip → it can Copy an attack but never Equip a weapon.</summary>
        public static readonly Dictionary<string, string> Capabilities = new Dictionary<string, string>
        {
            ["Clone"] = "Move Attack Copy Inherit Follow Swap",
            ["PhantomWeapon"] = "Move Attack Inherit Orbit",
            ["Turret"] = "Shoot Inherit",
            ["Orb"] = "Orbit Contact Inherit",
            ["Zone"] = "Tick Area Follow Attach",
            ["Mine"] = "Trigger Area",
            ["Totem"] = "Tick Aura Heal",
            ["Barrier"] = "Block",
            ["Decoy"] = "Taunt Swap",
        };

        /// <summary>Element affinities: Fire is not "always burn" — it is LIKELY to burn. Used only to bias generation.</summary>
        public static readonly Dictionary<string, string> ElementAffinity = new Dictionary<string, string>
        {
            ["Fire"] = "burn Nova Detonate Spread", ["Frost"] = "chill Stun Slow Zone", ["Lightning"] = "Chain Projectile Crit",
            ["Shadow"] = "mark Clone Blink Execute", ["Holy"] = "Heal Shield Totem", ["Poison"] = "poison Spread Zone",
            ["Wind"] = "Push Pull MoveSpeed Projectile", ["Blood"] = "bleed Drain MaxHp LowHealth", ["Void"] = "weaken Consume Execute Pull",
            ["Arcane"] = "Echo Mirror Convert Repeat",
        };

        /// <summary>Allowed Convert pairs (from→to). Anything else is rejected — conversions redefine game rules, so they are whitelisted.</summary>
        public static readonly Dictionary<string, (string ko, float def, float min, float max, int tier)> ConvertPairs = new Dictionary<string, (string, float, float, float, int)>
        {
            ["Heal>Shield"] = ("회복이 보호막으로 바뀐다", 1f, 0.5f, 1.5f, 2),
            ["Overheal>Shield"] = ("넘치는 회복이 보호막이 된다", 0.8f, 0.3f, 1.5f, 2),
            ["DamageDealt>Heal"] = ("가한 피해 일부만큼 회복", 0.04f, 0.01f, 0.12f, 2),
            ["DamageTaken>Shield"] = ("받은 피해 일부가 보호막으로 돌아온다", 0.25f, 0.1f, 0.6f, 2),
            ["Gold>Heal"] = ("골드를 주우면 그만큼 회복", 0.3f, 0.1f, 1f, 2),
            ["Crit>Heal"] = ("치명타 피해 일부만큼 회복", 0.05f, 0.02f, 0.15f, 2),
            ["Overkill>Nova"] = ("남은 피해가 주변으로 터진다", 0.6f, 0.3f, 1.2f, 3),
            ["Shield>Damage"] = ("보호막이 클수록 공격이 강해진다", 1f, 0.3f, 2f, 3),
            ["MoveDistance>Damage"] = ("움직인 거리만큼 기본 공격 강화", 0.06f, 0.02f, 0.25f, 3),
            ["Xp>Gold"] = ("경험치 일부가 골드가 된다", 0.5f, 0.2f, 1f, 4),
        };

        /// <summary>Stat bounds (total magnitude incl. scaling), power points per unit, dimension, multiplicative.</summary>
        public static readonly Dictionary<string, (float min, float max, float points, string dim, bool mul)> StatInfo = new Dictionary<string, (float, float, float, string, bool)>
        {
            ["Attack"] = (-0.5f, 0.8f, 200, "Offense", true),
            ["AttackSpeed"] = (-0.4f, 0.6f, 180, "Offense", true),
            ["MoveSpeed"] = (-0.3f, 0.45f, 90, "Mobility", true),
            ["CritChance"] = (-0.2f, 0.4f, 260, "Offense", false),
            ["CritDamage"] = (-0.5f, 1.2f, 30, "Offense", false),
            ["CooldownReduction"] = (-0.2f, 0.35f, 120, "Utility", false),
            ["Armor"] = (-0.3f, 0.35f, 260, "Defense", false),
            ["MaxHp"] = (-0.6f, 0.8f, 140, "Defense", true),
            ["Regen"] = (-1f, 4f, 6, "Sustain", false),
            ["XpGain"] = (-0.5f, 0.8f, 40, "Economy", true),
            ["GoldGain"] = (-0.5f, 1.2f, 25, "Economy", true),
            ["DashCooldown"] = (-0.5f, 0.5f, -90, "Mobility", true),
            ["ElementPower"] = (-0.3f, 0.8f, 60, "Offense", false),
        };

        /// <summary>Hierarchical tags: child → parent. "ATTACK" selects MELEE/RANGED abilities too.</summary>
        public static readonly Dictionary<string, string> TagParent = new Dictionary<string, string>
        {
            ["MELEE"] = "ATTACK", ["RANGED"] = "ATTACK", ["PROJECTILE"] = "RANGED", ["CRIT"] = "ATTACK", ["COMBO"] = "MELEE", ["KILL"] = "ATTACK",
            ["SWORD"] = "MELEE", ["HEAVY"] = "MELEE", ["DAGGER"] = "MELEE", ["SWIFT"] = "MELEE", ["BLADE"] = "MELEE", ["IAI"] = "MELEE", ["STAFF"] = "MAGIC", ["BOW"] = "RANGED",
            ["AREA"] = "ATTACK",
            ["GUARD"] = "DEFENSE", ["SHIELD"] = "DEFENSE", ["HEAL"] = "DEFENSE",
            ["DODGE"] = "MOBILITY", ["BLINK"] = "MOBILITY", ["MOVEMENT"] = "MOBILITY",
            ["FIRE"] = "ELEMENT", ["FROST"] = "ELEMENT", ["LIGHTNING"] = "ELEMENT", ["SHADOW"] = "ELEMENT", ["HOLY"] = "ELEMENT", ["POISON"] = "ELEMENT", ["WIND"] = "ELEMENT",
            ["BLOOD"] = "ELEMENT", ["VOID"] = "ELEMENT", ["ARCANE"] = "ELEMENT",
            ["ELEMENT"] = "MAGIC", ["SKILL"] = "MAGIC",
            ["CLONE"] = "SUMMON", ["TURRET"] = "SUMMON", ["ORB"] = "SUMMON", ["ZONE"] = "SUMMON", ["PHANTOMWEAPON"] = "SUMMON",
            ["MINE"] = "SUMMON", ["TOTEM"] = "SUMMON", ["BARRIER"] = "SUMMON", ["DECOY"] = "SUMMON",
            ["STUN"] = "CONTROL", ["SLOW"] = "CONTROL", ["PUSH"] = "CONTROL", ["PULL"] = "CONTROL", ["TAUNT"] = "CONTROL",
            ["BURN"] = "STATUS", ["CHILL"] = "STATUS", ["MARK"] = "STATUS", ["BLEED"] = "STATUS", ["WEAKEN"] = "STATUS",
            ["CHARGE"] = "STATE", ["ECONOMY"] = "SYSTEM", ["GROWTH"] = "SYSTEM", ["CHOICE"] = "SYSTEM",
        };

        public static bool TagMatches(IEnumerable<string> tags, string tag)
        {
            if (tags == null || string.IsNullOrEmpty(tag)) return false;
            foreach (var t in tags)
                for (string x = t; x != null; x = TagParent.TryGetValue(x, out var p) ? p : null)
                    if (x == tag) return true;
            return false;
        }

        public static bool KnownTag(string tag) =>
            !string.IsNullOrEmpty(tag) && (TagParent.ContainsKey(tag) || TagParent.ContainsValue(tag) || tag is "STATE" or "SYSTEM" or "PASSIVE" or "EXPLORE" or "BOSS" or "STAT" or "BUFF");

        public static string StatusOf(string element) => element switch
        {
            "Fire" => "burn", "Frost" => "chill", "Poison" => "poison", "Shadow" => "mark", "Blood" => "bleed", "Void" => "weaken", _ => null,
        };

        public static Element ParseElement(string e) => Enum.TryParse<Element>(e, out var el) ? el : Element.None;
        public static bool IsElement(string e) => !string.IsNullOrEmpty(e) && e != "None" && Enum.TryParse<Element>(e, out _);
        public static readonly string[] Elements = Enum.GetNames(typeof(Element)).Where(e => e != "None").ToArray();

        // ───────────── Relation & Temporal layers ─────────────

        /// <summary>Relation: how an effect links to things. powerMul = expected extra value; ops declare which relations they accept.</summary>
        public static readonly Dictionary<string, (string ko, string desc, float powerMul, int tier, int cost, string risk)> Relations = new Dictionary<string, (string, string, float, int, int, string)>
        {
            ["Echo"] = ("메아리", "The effect happens again 0.5s later at 50% power", 0.5f, 2, 2, "MULTIPLICATIVE"),
            ["Mirror"] = ("거울", "A mirrored copy happens in the opposite direction/position", 0.8f, 2, 2, "MULTIPLICATIVE"),
            ["Chain"] = ("연쇄", "Jumps to `times` nearby enemies (−25% per jump)", 1.1f, 2, 2, "PROC"),
            ["Bounce"] = ("튕김", "Projectiles bounce off walls/enemies `times` times", 0.7f, 1, 1, ""),
            ["Return"] = ("귀환", "Projectiles come back to you and hit again", 0.8f, 1, 1, ""),
            ["Split"] = ("분열", "On impact, splits into `times` smaller pieces (40% each)", 0.9f, 2, 3, "PROC"),
            ["Pierce"] = ("관통", "Passes through `times` enemies", 0.6f, 1, 1, ""),
            ["Seek"] = ("유도", "Homes in on enemies", 0.25f, 1, 0, ""),
            ["Orbit"] = ("공전", "The entity circles you", 0.1f, 1, 0, ""),
            ["Follow"] = ("추종", "The entity/zone follows you", 0.2f, 1, 0, ""),
            ["Attach"] = ("부착", "The zone/effect sticks to the target enemy and moves with it", 0.3f, 2, 0, ""),
            ["Spread"] = ("전파", "The applied status also jumps to `times` nearest enemies", 0.9f, 2, 2, "PROC"),
            ["Cascade"] = ("연발", "If this kills, the effect repeats from the corpse (max `times`, depth-bounded)", 0.6f, 3, 3, "RECURSION"),
            ["Alternate"] = ("교대", "Every other execution swaps to the opposite element/status", 0.1f, 2, 0, ""),
            ["Copy"] = ("복제", "A clone copies your attacks while alive", 0.5f, 2, 0, ""),
            ["Inherit"] = ("계승", "The spawned entity inherits your weapon element and tag modifiers", 0.2f, 1, 0, ""),
            ["Link"] = ("연결", "Damage to the target is shared to `times` linked enemies (50%) for the duration", 0.8f, 3, 2, "PROC"),
        };

        /// <summary>Temporal: when / for how long. Every temporal must be bounded (max ticks / duration in AbilityRulesDef).</summary>
        public static readonly Dictionary<string, (string ko, string desc, int tier)> Temporals = new Dictionary<string, (string, string, int)>
        {
            ["Immediate"] = ("즉시", "Now (default)", 0),
            ["Delay"] = ("지연", "After op.delay seconds", 1),
            ["Periodic"] = ("주기", "Repeats `times` ticks across op.duration", 2),
            ["UntilHit"] = ("적중할 때까지", "Armed until your next hit lands, then fires on that target (expires after duration)", 2),
            ["UntilDamaged"] = ("피격될 때까지", "Armed until you are hit, then fires (expires after duration)", 2),
            ["UntilNextAttack"] = ("다음 공격까지", "Armed until your next basic attack, then fires at its target", 2),
            ["ForNextN"] = ("다음 N회", "Instead of now, the next `times` basic-attack hits carry this effect", 2),
            ["Refresh"] = ("갱신", "Re-applying refreshes duration (statuses/buffs/shields) — default for most", 1),
            ["Stack"] = ("누적", "Re-applying adds duration/strength up to 3 stacks", 2),
        };

        public static bool IsRelation(string r) => !string.IsNullOrEmpty(r) && Relations.ContainsKey(r);
        public static bool IsTemporal(string t) => !string.IsNullOrEmpty(t) && Temporals.ContainsKey(t);

        // Relation groups (type constraints)
        const string DMG_REL = "Echo Mirror Chain Cascade Alternate";
        const string PROJ_REL = "Echo Mirror Bounce Return Split Pierce Seek Alternate";
        const string STATUS_REL = "Spread Echo Alternate Link";
        const string SPAWN_REL = "Copy Inherit Orbit Follow Attach Mirror";
        const string TIMED = "Immediate Delay Periodic UntilHit UntilDamaged UntilNextAttack ForNextN";
        const string TIMED_DUR = "Immediate Delay Refresh Stack UntilDamaged";

        static RuleLanguage()
        {
            const Ctx T = Ctx.Target, A = Ctx.Amount, D = Ctx.Direction, E = Ctx.Entity;

            // ── Triggers (when) ── provides = what the event carries
            Trig("Attack", "공격할 때", "Basic attack starts", 1.6f, D, "ATTACK");
            Trig("Hit", "적중할 때", "You or your clone damages an enemy", 2.0f, T | A | D, "ATTACK");
            Trig("FirstHit", "처음 때린 적일 때", "First time you hit this particular enemy", 0.35f, T | A | D, "ATTACK");
            Trig("Crit", "치명타 때", "A critical hit lands", 0.25f, T | A, "CRIT");
            Trig("Kill", "처치할 때", "An enemy dies (corpse statuses readable, no live target)", 0.3f, Ctx.Corpse | A, "KILL");
            Trig("Overkill", "과잉 처치 때", "A kill where your damage exceeded the remaining HP by 50%+ (amount = excess ÷ attack power)", 0.08f, Ctx.Corpse | A, "KILL");
            Trig("EliteKill", "정예를 처치할 때", "Kills an elite or boss", 0.02f, Ctx.Corpse | A, "KILL");
            Trig("Dash", "대시할 때", "Dash starts", 0.35f, D, "DODGE");
            Trig("DashEnd", "대시가 끝날 때", "Dash ends", 0.35f, Ctx.None, "DODGE");
            Trig("PerfectDodge", "완벽 회피 때", "Dashes right before an enemy attack lands", 0.06f, D, "DODGE");
            Trig("Moved", "일정 거리를 걸을 때", "Every 6m walked (not dashing)", 0.4f, Ctx.None, "MOVEMENT");
            Trig("Damaged", "피격될 때", "Takes damage (target = attacker, amount = fraction of max HP)", 0.15f, T | A, "DEFENSE");
            Trig("Guard", "막아낼 때", "Guards/parries an attack (target = attacker)", 0.08f, T, "GUARD");
            Trig("ShieldBroken", "보호막이 깨질 때", "Your shield is depleted by damage", 0.03f, Ctx.None, "SHIELD");
            Trig("Healed", "회복할 때", "You are healed (amount = fraction of max HP; regen excluded)", 0.05f, A, "HEAL");
            Trig("LowHealth", "체력이 30% 아래로 떨어질 때", "Health drops to 30% or below", 0.01f, Ctx.None, "DEFENSE");
            Trig("SkillCast", "스킬을 쓸 때", "Uses a QWER skill", 0.2f, D, "SKILL");
            Trig("ComboFinish", "콤보를 마무리할 때", "Last hit of the basic-attack combo", 0.4f, D, "COMBO");
            Trig("CloneSpawn", "분신이 나타날 때", "A clone/phantom appears (position = clone)", 0.02f, E, "CLONE");
            Trig("CloneExpire", "분신이 사라질 때", "A clone/phantom disappears (position = where it vanished)", 0.02f, E, "CLONE");
            Trig("EntityExpired", "소환물이 사라질 때", "A turret/orb/zone/mine/totem/barrier/decoy of yours ends (param: entity or empty; position = where)", 0.02f, E, "SUMMON").param = "entity";
            Trig("StatusApplied", "상태이상을 걸 때", "A status is applied to an enemy (param: status id or empty = any)", 0.4f, T, "STATUS").param = "status";
            Trig("StatusExpired", "상태이상이 끝날 때", "A status you applied runs out on a living enemy (param: status)", 0.3f, T, "STATUS").param = "status";
            Trig("ProjectileEnd", "투사체가 사라질 때", "Your projectile hits, hits a wall or runs out (position = where)", 0.5f, Ctx.None, "PROJECTILE");
            Trig("Pickup", "무언가를 주울 때", "Picks up gold/xp/heart/shard (param: kind or empty)", 0.5f, Ctx.None, "ECONOMY").param = "pickup";
            Trig("Every", "주기적으로", "Every N seconds (rule.period)", 0, Ctx.None, "PASSIVE");
            Trig("StateFull", "상태가 가득 찰 때", "One of this ability's states reaches its max (param: state name)", 0, Ctx.None, "CHARGE").param = "state";
            Trig("StateEmpty", "상태가 바닥날 때", "One of this ability's states drops to 0 (param: state name) — Timer states fire this when they run out", 0, Ctx.None, "CHARGE").param = "state";
            Trig("CombatStart", "전투가 시작될 때", "An enemy comes within 8m after 5s without enemies", 0.03f, Ctx.None, "PASSIVE");
            Trig("CombatEnd", "전투가 끝날 때", "No enemy within 10m for 3s after a fight", 0.03f, Ctx.None, "PASSIVE");
            Trig("CampCleared", "적 군집을 소탕할 때", "Clears a monster camp", 0.004f, Ctx.None, "EXPLORE");
            Trig("BossKilled", "보스를 쓰러뜨릴 때", "Kills a boss", 0.001f, Ctx.None, "BOSS");
            Trig("LevelUp", "레벨이 오를 때", "Gains a level", 0.005f, Ctx.None, "GROWTH");
            Trig("AbilityAcquired", "능력을 얻을 때", "Gains a new ability (evolution / grave)", 0.004f, Ctx.None, "GROWTH");

            // ── Selectors (whom) ── actors
            Sel("Self", "자신", "The player", Ctx.None, false);
            Sel("EventTarget", "그 적", "The enemy of the event (needs a live target)", T, true);
            Sel("LastHitTarget", "마지막으로 때린 적", "The last enemy you hit (if still alive)", Ctx.None, true);
            Sel("LastAttacker", "마지막으로 나를 때린 적", "The last enemy that damaged you (if alive)", Ctx.None, true);
            Sel("NearestEnemy", "가장 가까운 적", "Nearest enemy within 6m of the position", Ctx.None, true);
            Sel("FarthestEnemy", "가장 먼 적", "Farthest enemy within 9m", Ctx.None, true);
            Sel("RandomEnemy", "무작위 적", "A random enemy within 6m", Ctx.None, true);
            Sel("LowestHpEnemy", "체력이 가장 낮은 적", "Lowest-HP enemy within 7m", Ctx.None, true);
            Sel("HighestHpEnemy", "체력이 가장 높은 적", "Highest-HP enemy within 7m", Ctx.None, true);
            Sel("EnemiesNear", "주변 적들", "Enemies within op.radius of the position (capped)", Ctx.None, true);
            Sel("EnemiesInCone", "전방 부채꼴의 적들", "Enemies in a 90° cone toward the aim (op.radius long)", Ctx.None, true);
            Sel("EnemiesInLine", "직선 위의 적들", "Enemies on the line from you toward the aim (op.radius long)", Ctx.None, true);
            Sel("MarkedEnemies", "표식이 있는 적들", "Enemies within 8m carrying 'mark'", Ctx.None, true);
            Sel("StatusedEnemies", "상태이상에 걸린 적들", "Enemies within 8m carrying op.status", Ctx.None, true);
            Sel("StoredTarget", "기억한 적", "The enemy remembered in a Target state (op.state)", Ctx.None, true);
            // entities (for MoveEntity / DetonateEntities / Swap)
            Sel("OwnClones", "내 분신들", "Your clones and phantom weapons", Ctx.None, false);
            Sel("OwnSummons", "내 소환물들", "Your turrets/orbs/zones/mines/totems/barriers/decoys", Ctx.None, false);
            Sel("LastSpawned", "마지막 소환물", "The entity this ability spawned last", Ctx.None, false);

            // ── Positions / forms (where) ──
            Pos("Self", "자신 위치", "Player position");
            Pos("EventPos", "사건 위치", "Where the event happened (death point, impact point, clone position…)");
            Pos("TargetPos", "대상 위치", "Event target position (or nearest enemy)");
            Pos("BehindTarget", "대상 뒤", "1m behind the event target, away from you");
            Pos("DashOrigin", "대시 시작점", "Where the last dash started");
            Pos("DashPath", "대시 경로", "Along the last dash path (area ops hit the whole line)");
            Pos("Cursor", "조준 지점", "Aim point");
            Pos("Forward", "전방", "2m in front of you toward the aim");
            Pos("Behind", "등 뒤", "1.5m behind the player");
            Pos("Ring", "주위 원형", "A ring around you (projectiles go all directions)");
            Pos("RandomNear", "주변 임의 위치", "A random point within 4m");
            Pos("LastPosition", "2초 전 위치", "Where you stood 2 seconds ago");
            Pos("Stored", "기억한 위치", "A Position state of this ability (op.state)");
            Pos("EntityPos", "소환물 위치", "Each of your entities picked by op.target (OwnClones/OwnSummons/LastSpawned)");

            // ── Conditions ──
            Cond("Chance", "확률", "Fires with probability value (0.05~0.9)");
            Cond("EveryNth", "N번째마다", "Only every value-th time");
            Cond("Compare", "비교", "a <cmp> value, a = value reference (see Values)");
            Cond("TargetHas", "대상이 상태이상", "Event/hit target has status text");
            Cond("TargetLacks", "대상에 상태이상 없음", "Event/hit target does NOT have status text");
            Cond("TargetIs", "대상 종류", "Event target is text: Elite | Boss | Normal | Ranged | Melee");
            Cond("EventTag", "사건 태그", "The event / hit carries tag text (e.g. MELEE, CLONE)");
            Cond("HasEntity", "소환물 존재", "You currently have entity text alive (or any if empty)");
            Cond("NoEntity", "소환물 없음", "You currently have no entity text alive");
            Cond("WithinTime", "직후", "Within value seconds after trigger text happened (e.g. Dash, Kill, Damaged)");
            Cond("SameTarget", "같은 대상", "The event target is the same enemy as last time");
            Cond("DifferentTarget", "다른 대상", "The event target differs from last time");
            Cond("DominantElement", "주력 원소", "Your most common element among owned abilities is text");

            // ── Value references (numbers a rule can read) ── expected / peak values feed the power model
            Val("self.hpPct", "내 체력 비율", "Own HP / max HP (0~1)", Ctx.None, 0.75f, 1);
            Val("self.missingHpPct", "잃은 체력 비율", "1 - HP ratio (0~1)", Ctx.None, 0.25f, 0.9f);
            Val("self.shieldPct", "보호막 비율", "Shield / max HP", Ctx.None, 0.05f, 0.5f);
            Val("self.moving", "이동 중", "1 while moving, else 0", Ctx.None, 0.6f, 1);
            Val("self.stillTime", "멈춰 있던 시간", "Seconds standing still (cap 10)", Ctx.None, 1, 10);
            Val("self.moveDistance", "최근 이동 거리", "Meters moved in the last 3 seconds", Ctx.None, 6, 20);
            Val("self.dashCharges", "대시 충전", "Dash charges left", Ctx.None, 1.2f, 3);
            Val("self.gold", "보유 골드 (100 단위)", "Gold ÷ 100", Ctx.None, 1, 10);
            Val("self.potions", "남은 물약", "Potions left", Ctx.None, 1.5f, 5);
            Val("self.comboStep", "콤보 단계", "Current basic-attack combo step (0 = none)", Ctx.None, 1, 3);
            Val("enemies.near", "주변 적 수", "Enemies within 4m", Ctx.None, 2, 8);
            Val("enemies.far", "멀리 있는 적 수", "Enemies 4~9m away", Ctx.None, 2, 8);
            Val("target.hpPct", "대상 체력 비율", "Event/hit target HP ratio", T, 0.5f, 1);
            Val("target.distance", "대상과의 거리", "Distance to the event target (m)", T, 2.5f, 8);
            Val("target.statuses", "대상의 상태이상 수", "Number of statuses on the event target", T, 0.6f, 4);
            Val("event.amount", "사건의 양", "Hit: damage ÷ attack power · Damaged/Healed: fraction of max HP · Overkill: excess ÷ attack power", A, 0, 0);
            Val("time.sinceDamaged", "피격 후 경과 시간", "Seconds since last damaged (cap 30)", Ctx.None, 5, 30);
            Val("time.sinceKill", "처치 후 경과 시간", "Seconds since last kill (cap 30)", Ctx.None, 4, 30);
            Val("time.inCombat", "전투 지속 시간", "Seconds since this fight started (cap 60)", Ctx.None, 10, 60);
            Val("entities.count", "내 소환물 수", "Your living clones + summons", Ctx.None, 0.5f, 8);
            Val("state:", "능력 상태", "state:<name> — a state of this ability", Ctx.None, 0, 0);
            Val("build.tag:", "보유 능력 수(태그)", "build.tag:<TAG> — owned abilities with tag (weapon counts)", Ctx.None, 0, 0);
            Val("build.element:", "보유 원소 능력 수", "build.element:<Element> — owned abilities using that element", Ctx.None, 0, 0);
            Val("build.elements", "보유 원소 종류", "Distinct elements among owned abilities", Ctx.None, 0, 0);
            Val("build.abilities", "보유 능력 수", "Number of owned abilities", Ctx.None, 0, 0);
            Val("build.tier:", "티어별 능력 수", "build.tier:<n> — owned abilities of complexity tier n", Ctx.None, 0, 0);
            Val("run.level", "레벨", "Current level", Ctx.None, 0, 0);
            Val("run.kills", "이번 생 처치 수 (100 단위)", "Kills this life ÷ 100", Ctx.None, 0, 0);
            Val("run.bossKills", "보스 처치 수", "Bosses defeated across lives", Ctx.None, 0, 0);

            // ── Operations ── kinds: T Trigger · C Continuous · R Replace · K Constrain · M Meta · S System
            // combat
            Op("Damage", "피해", "Damage the target: amount × attack power", "T", "target", 1, 0.8f, 0.2f, 4f, cost: 1, causes: "Hit Crit Kill", tags: "ATTACK", rel: DMG_REL, time: TIMED);
            Op("Nova", "충격파", "Shockwave at a position: radius, amount × attack power", "T", "at", 1, 0.6f, 0.15f, 3f, cost: 3, causes: "Hit Crit Kill", tags: "AREA", rel: DMG_REL, time: TIMED).defRadius = 2.2f;
            Op("Slash", "참격", "A sweeping slash from you toward the aim (cone, op.radius long)", "T", "", 1, 0.7f, 0.2f, 3f, cost: 2, causes: "Hit Crit Kill", tags: "MELEE", rel: DMG_REL, time: TIMED).defRadius = 2.5f;
            Op("Beam", "광선", "A line from you toward the aim (op.radius long) hitting everything on it", "T", "", 2, 0.6f, 0.15f, 3f, cost: 3, causes: "Hit Crit Kill", tags: "RANGED", rel: DMG_REL, time: TIMED).defRadius = 6;
            Op("Projectile", "투사체", "Fire count projectiles from a position (mode Seek|Ring|Fan)", "T", "at", 1, 0.4f, 0.1f, 2f, cost: 2, causes: "Hit Crit Kill ProjectileEnd", tags: "PROJECTILE", rel: PROJ_REL, time: TIMED).defCount = 3;
            Op("Chain", "연쇄 번개", "Lightning that jumps count times from the target", "T", "target", 1, 0.5f, 0.1f, 2f, cost: 2, causes: "Hit Crit Kill", tags: "LIGHTNING", rel: "Echo Alternate", time: TIMED).defCount = 3;
            Op("ApplyStatus", "상태이상", "Apply status (burn/poison/bleed: amount = dps × attack power; chill: slow; mark: +25% damage taken; weaken: +15%)", "T", "target status", 1, 0.3f, 0.05f, 1.5f, cost: 1, causes: "StatusApplied StatusExpired", tags: "STATUS", rel: STATUS_REL, time: TIMED_DUR + " Periodic UntilHit UntilNextAttack ForNextN");
            Op("Detonate", "기폭", "Consume status on the target(s): amount × attack power, +20% per remaining second", "T", "target status", 1, 1f, 0.2f, 4f, cost: 2, causes: "Hit Crit Kill", tags: "STATUS", rel: "Chain Cascade Echo", time: TIMED);
            Op("Spread", "전염", "Copy the target's status to count nearest enemies (works on corpses)", "T", "status", 1, 3, 1, 6, scalable: "count", cost: 2, causes: "StatusApplied StatusExpired", tags: "STATUS", time: "Immediate Delay").defRadius = 3.5f;
            Op("Transfer", "이전", "Move all your statuses from the target to count nearest enemies (target is cleansed)", "T", "", 2, 2, 1, 5, scalable: "count", cost: 2, causes: "StatusApplied", tags: "STATUS", time: "Immediate Delay").defRadius = 4;
            Op("Execute", "처형", "Kill a non-boss target below amount HP ratio (bosses take 1.5× attack power)", "T", "target", 1, 0.12f, 0.05f, 0.3f, cost: 1, causes: "Hit Kill", tags: "KILL", rel: "Chain Cascade", time: "Immediate Delay UntilHit");
            Op("Drain", "흡수", "Damage the target (amount × attack power) and heal 40% of it", "T", "target", 2, 0.5f, 0.1f, 2f, cost: 1, causes: "Hit Healed", tags: "BLOOD", rel: "Chain Echo", time: TIMED);
            Op("Stun", "기절", "Stun the target(s) for duration", "T", "target", 1, 0.6f, 0.2f, 2f, scalable: "duration", cost: 1, tags: "STUN", rel: "Chain Spread", time: "Immediate Delay UntilHit");
            Op("Slow", "둔화", "Chill the target(s): strong slow for duration", "T", "target", 1, 2f, 0.5f, 5f, scalable: "duration", cost: 1, causes: "StatusApplied", tags: "SLOW", rel: "Spread Chain", time: TIMED_DUR);
            Op("Taunt", "도발", "Enemies around the position chase the position instead of you for amount seconds", "T", "at", 2, 1.5f, 0.5f, 4f, cost: 2, tags: "TAUNT").defRadius = 4;
            Op("Push", "밀쳐내기", "Push enemies away from a position: amount = force", "T", "at", 1, 7, 3, 14, cost: 2, tags: "PUSH", rel: "Echo Mirror", time: TIMED).defRadius = 2.5f;
            Op("Pull", "끌어당기기", "Pull enemies toward a position: amount = force", "T", "at", 1, 7, 3, 14, cost: 2, tags: "PULL", rel: "Echo", time: TIMED).defRadius = 3f;
            Op("Cleanse", "정화", "Remove your debuffs / gain brief invulnerability (amount seconds)", "T", "", 2, 0.4f, 0.2f, 1f, cost: 1, tags: "HOLY DEFENSE");
            // defense / sustain
            Op("Heal", "회복", "Heal amount × max HP", "T", "", 1, 0.04f, 0.01f, 0.2f, cost: 1, causes: "Healed", tags: "HEAL", time: "Immediate Delay Periodic UntilDamaged");
            Op("Shield", "보호막", "Shield of amount × max HP for duration", "T", "", 1, 0.12f, 0.03f, 0.5f, cost: 1, tags: "SHIELD", time: TIMED_DUR);
            Op("Buff", "강화", "Temporary stat boost (Attack/AttackSpeed/MoveSpeed/CritChance/Armor) for duration", "T", "stat", 1, 0.2f, 0.05f, 0.8f, cost: 1, tags: "BUFF", time: TIMED_DUR + " UntilHit UntilNextAttack");
            Op("ResetCooldowns", "재사용 단축", "Cut skill and dash cooldowns by amount (fraction)", "T", "", 1, 0.2f, 0.05f, 0.6f, cost: 1, tags: "SKILL", time: "Immediate Delay");
            Op("GainDash", "대시 충전", "Restore count dash charges", "T", "", 1, 1, 1, 2, scalable: "count", cost: 1, tags: "DODGE");
            Op("EmpowerNext", "다음 공격 강화", "Your next count basic attacks deal +amount × damage (optional element)", "T", "", 2, 0.6f, 0.15f, 3f, cost: 1, causes: "Hit Crit Kill", tags: "ATTACK", rel: "Chain Echo");
            // movement
            Op("Blink", "순간이동", "Teleport next to the target, or to the position", "T", "", 1, 0, 0, 0, scalable: "none", cost: 1, tags: "BLINK", time: "Immediate Delay");
            Op("Swap", "위치 교환", "Swap places with the selected entity/enemy (op.target)", "T", "target", 2, 0, 0, 0, scalable: "none", cost: 1, tags: "BLINK");
            Op("Leap", "도약", "Dash toward the position (invulnerable while moving)", "T", "at", 1, 0, 0, 0, scalable: "none", cost: 1, tags: "MOBILITY");
            Op("MoveEntity", "소환물 이동", "Move your entities (op.target OwnClones/OwnSummons/LastSpawned) to the position", "T", "target at", 2, 0, 0, 0, scalable: "none", cost: 1, tags: "SUMMON");
            // spawn
            Op("Spawn", "소환", "Spawn entity at a position: amount = power, duration, count; relation Copy (Clone), Orbit, Follow, Attach, Inherit", "T", "entity at", 1, 0.6f, 0.2f, 2f, cost: 6, causes: "CloneSpawn CloneExpire EntityExpired Hit Kill", risk: "ENTITY", tags: "SUMMON", rel: SPAWN_REL, time: "Immediate Delay").defDuration = 4;
            Op("DetonateEntities", "소환물 폭파", "Your entities (op.target) burst for amount × attack power and disappear", "T", "target", 2, 0.8f, 0.2f, 3f, cost: 4, causes: "Hit Kill CloneExpire EntityExpired", tags: "SUMMON AREA").defRadius = 2;
            Op("ExtendEntities", "소환물 연장", "Your entities (op.target) live amount seconds longer", "T", "target", 2, 1.5f, 0.5f, 4f, cost: 1, tags: "SUMMON");
            // state
            Op("AddState", "상태 증가", "state += amount (+ per × scale); clamps to the state's max", "T", "state", 2, 1, 0, 0, scalable: "none", causes: "StateFull", tags: "STATE");
            Op("SetState", "상태 설정", "state = amount (0 = consume/reset); Timer: start counting down from amount", "T", "state", 2, 0, 0, 0, scalable: "none", causes: "StateEmpty", tags: "STATE");
            Op("StorePosition", "위치 기억", "Remember a position in a Position state", "T", "state at", 2, 0, 0, 0, scalable: "none", tags: "STATE");
            Op("StoreTarget", "대상 기억", "Remember the event/selected enemy in a Target state", "T", "state target", 2, 0, 0, 0, scalable: "none", tags: "STATE");
            // economy / growth
            Op("GainGold", "골드 획득", "Gain amount gold", "T", "", 1, 3, 1, 30, cost: 1, tags: "ECONOMY");
            Op("GainXp", "경험치 획득", "Gain amount experience", "T", "", 1, 2, 1, 20, cost: 1, tags: "GROWTH");
            Op("GainPotion", "물약 획득", "Gain count potions (max 5)", "T", "", 2, 1, 1, 2, scalable: "count", cost: 1, tags: "HEAL");
            Op("DropPickup", "보상 떨구기", "Drop a pickup (op.mode: heart|gold|xp) worth amount at the position", "T", "at", 1, 8, 2, 40, cost: 1, tags: "ECONOMY");
            // continuous: modifiers / scaling / conversion
            Op("ModifyStat", "능력치 변경", "Passive: stat +amount (+ per × scale). mode Add (default) | Mul | Min (floor) | Max (cap) | Override (tier 4). Negative = a price", "C", "stat", 0, 0.1f, 0, 0, cost: 0);
            Op("AmplifyDamage", "피해 증폭", "Passive: your damage ×(1+amount) while the rule's conditions hold (TargetHas/TargetIs/target.*/EventTag checked per hit)", "C", "", 1, 0.2f, 0.05f, 1f, cost: 0, tags: "ATTACK");
            Op("Resist", "피해 감소", "Passive: damage taken ×(1-amount) while the conditions hold", "C", "", 1, 0.15f, 0.05f, 0.5f, cost: 0, tags: "DEFENSE");
            Op("Convert", "전환", "Passive: redefine a rule — from→to pair from the whitelist, amount = ratio", "C", "pair", 2, 1, 0, 0, cost: 0, risk: "REDEFINE");
            Op("Aura", "오라", "Passive: enemies within op.radius take amount × attack power per second (optional status)", "C", "", 2, 0.25f, 0.05f, 1f, cost: 2, causes: "StatusApplied", tags: "AREA").defRadius = 2.5f;
            Op("StatusPotency", "상태이상 강화", "Passive: your op.status (or all) deals ×(1+amount) and lasts ×(1+amount)", "C", "", 1, 0.3f, 0.1f, 1f, cost: 0, tags: "STATUS");
            // replacement (TFT-style "the rules of your kit change")
            Op("ReplaceWith", "대체", "Replace rule: the player action (rule.replaces) does op.mode instead: Blink | Nova | Shield | Spawn (+entity) | Projectile", "R", "", 3, 0.5f, 0.1f, 2f, cost: 2, risk: "REDEFINE", tags: "MOBILITY");
            // constraints (prices)
            Op("Forbid", "봉인", "Constrain rule: the player can no longer use op.mode (Dash | Potion | Regen | Skill). Always a price", "K", "", 2, 0, 0, 0, scalable: "none", cost: 0, risk: "REDEFINE");
            Op("CapStat", "상한", "Constrain rule: stat can never exceed amount (e.g. MaxHp 1 = glass cannon). A price", "K", "stat", 3, 0.5f, 0, 0, scalable: "none", cost: 0, risk: "REDEFINE");
            // meta (other abilities / rules)
            Op("AmplifyTagged", "태그 증폭", "Meta: effects of selected abilities (and weapon attacks with the tag) ×(1+amount)", "M", "", 1, 0.2f, 0.05f, 0.5f, cost: 0, risk: "MULTIPLICATIVE");
            Op("RepeatTagged", "태그 반복", "Meta: trigger rules of selected OTHER abilities run again at amount power", "M", "", 3, 0.4f, 0.2f, 0.8f, cost: 0, risk: "MULTIPLICATIVE RECURSION META");
            Op("HasteTagged", "태그 가속", "Meta: selected abilities fire more often: cooldowns −amount, chances ×(1+amount)", "M", "", 3, 0.25f, 0.1f, 0.5f, cost: 0, risk: "META");
            Op("InfuseTagged", "원소 부여", "Meta: element-less effects of selected abilities gain op.element", "M", "element", 3, 0, 0, 0, scalable: "none", cost: 0, risk: "META");
            Op("ExtendTagged", "지속 연장", "Meta: durations of selected abilities ×(1+amount)", "M", "", 3, 0.3f, 0.1f, 1f, cost: 0, risk: "META");
            Op("EnlargeTagged", "범위 확대", "Meta: radii of selected abilities ×(1+amount)", "M", "", 3, 0.3f, 0.1f, 0.8f, cost: 0, risk: "META");
            Op("MultiplyTagged", "개수 증가", "Meta: projectile/spawn/chain counts of selected abilities +count", "M", "", 3, 1, 1, 2, scalable: "count", cost: 0, risk: "META ENTITY");
            Op("RetagAbilities", "태그 부여", "Meta: selected abilities also count as op.tag (feeds build.tag / other meta)", "M", "tag", 3, 0, 0, 0, scalable: "none", cost: 0, risk: "META");
            Op("GateTagged", "조건 추가", "Meta: selected abilities only fire while self.hpPct ≥ amount — but each fire is ×1.6 (risk/reward)", "M", "", 3, 0.5f, 0.3f, 0.8f, scalable: "none", cost: 0, risk: "META");
            Op("UnchainTagged", "조건 해제", "Meta (tier 4): selected abilities ignore Chance/EveryNth conditions", "M", "", 4, 0, 0, 0, scalable: "none", cost: 0, risk: "META MULTIPLICATIVE");
            Op("RetriggerTagged", "트리거 교체", "Meta (tier 4): selected abilities' trigger rules listen to op.from instead (whitelisted pairs)", "M", "pair", 4, 0, 0, 0, scalable: "none", cost: 0, risk: "META REDEFINE");
            // system (TFT augment style)
            Op("OfferCount", "선택지 수", "System: evolution cards +amount (−1 or +1; 2~4 cards)", "S", "", 4, -1, -1, 1, scalable: "none", cost: 0, risk: "SYSTEM", tags: "CHOICE");
            Op("TierBias", "희귀도 상승", "System: future offers are amount complexity tiers higher", "S", "", 4, 1, 1, 1, scalable: "none", cost: 0, risk: "SYSTEM", tags: "CHOICE");
            Op("BudgetBias", "보상 품질", "System: future abilities get ×(1+amount) power budget", "S", "", 4, 0.25f, 0.1f, 0.6f, cost: 0, risk: "SYSTEM", tags: "GROWTH");
            Op("RerollDiscount", "다시 뽑기 할인", "System: reroll cost ×(1-amount)", "S", "", 4, 0.5f, 0.2f, 1f, cost: 0, risk: "SYSTEM", tags: "ECONOMY");
            Op("TagBias", "진화 성향", "System: abilities with op.tag appear more often (weight ×(1+amount))", "S", "tag", 4, 1, 0.5f, 3f, cost: 0, risk: "SYSTEM", tags: "CHOICE");
            Op("ExtraEvolution", "추가 진화", "System: right now gain count extra evolution(s)", "S", "", 4, 1, 1, 1, scalable: "none", cost: 0, risk: "SYSTEM", tags: "GROWTH");
            Op("UpgradeRandom", "능력 강화", "System: right now, a random owned ability gets ×(1+amount) power permanently", "S", "", 4, 0.3f, 0.15f, 0.5f, cost: 0, risk: "SYSTEM PERMANENT", tags: "GROWTH");
            Op("DelayedReward", "지연 보상", "System: after `times` kills, gain amount gold and an extra evolution (TFT 'golden egg')", "S", "", 4, 100, 50, 300, cost: 0, risk: "SYSTEM", tags: "ECONOMY");

            ApplyTuning(null);
        }

        // ───────────── Registration ─────────────

        static Prim Add(Prim p) { All.Add(p); ById[p.id] = p; return p; }

        static Prim Trig(string id, string ko, string desc, float freq, Ctx provides, string tags) =>
            Add(new Prim { id = id, cat = "Trigger", ko = ko, desc = desc, freq = freq, provides = provides, tags = tags.Split(' '), tier = 1 });

        static void Sel(string id, string ko, string desc, Ctx req, bool enemy) =>
            Add(new Prim { id = "sel:" + id, cat = "Selector", ko = ko, desc = desc, requires = req, enemy = enemy });

        static void Pos(string id, string ko, string desc) => Add(new Prim { id = "pos:" + id, cat = "Position", ko = ko, desc = desc });

        static void Cond(string id, string ko, string desc) => Add(new Prim { id = "cond:" + id, cat = "Condition", ko = ko, desc = desc });

        static void Val(string id, string ko, string desc, Ctx req, float expected, float peak) =>
            Add(new Prim { id = "val:" + id, cat = "Value", ko = ko, desc = desc, requires = req, expected = expected, peak = peak });

        static Prim Op(string id, string ko, string desc, string kinds, string needs, int tier, float def, float min, float max,
            string scalable = "amount", int cost = 1, string causes = "", string risk = "", string tags = "", string rel = "", string time = "Immediate Delay") =>
            Add(new Prim
            {
                id = id, cat = "Op", ko = ko, desc = desc, kinds = kinds, needs = needs, tier = tier, def = def, min = min, max = max, scalable = scalable,
                cost = cost, causes = causes, risk = risk, tags = string.IsNullOrEmpty(tags) ? new string[0] : tags.Split(' '), relations = rel, temporals = time,
            });

        static readonly Dictionary<string, (float def, float min, float max, float freq)> codeDefaults = new Dictionary<string, (float, float, float, float)>();

        /// <summary>
        /// Apply Inspector overrides (AbilityRules.asset → prims). Called with null at startup to snapshot the code defaults,
        /// and by RuleConfig once the asset is loaded. Entries with id not found are ignored.
        /// </summary>
        public static void ApplyTuning(List<PrimTuning> tuning)
        {
            if (codeDefaults.Count == 0) foreach (var p in All) codeDefaults[p.id] = (p.def, p.min, p.max, p.freq);
            foreach (var p in All) { var d = codeDefaults[p.id]; p.def = d.def; p.min = d.min; p.max = d.max; p.freq = d.freq; }
            if (tuning == null) return;
            foreach (var t in tuning)
            {
                var p = Get(t.id);
                if (p == null) continue;
                if (p.cat == "Trigger") { if (t.freq > 0) p.freq = t.freq; continue; }
                if (t.max > 0 || t.min > 0 || t.def > 0) { p.def = t.def; p.min = t.min; p.max = t.max; }
            }
        }

        /// <summary>The code defaults as a tuning table (used to seed AbilityRules.asset).</summary>
        public static List<PrimTuning> DefaultTuning()
        {
            ApplyTuning(null);
            return All.Where(p => p.cat == "Trigger" || p.cat == "Op" && p.scalable != "none")
                .Select(p => new PrimTuning { id = p.id, def = p.def, min = p.min, max = p.max, freq = p.freq }).ToList();
        }

        public static Prim Selector(string id) => Get("sel:" + id);
        public static Prim Position(string id) => Get("pos:" + id);

        /// <summary>Resolve a value reference (with its prefix family, e.g. "state:pain" → "state:").</summary>
        public static Prim Value(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            int c = reference.IndexOf(':');
            return Get("val:" + (c >= 0 ? reference.Substring(0, c + 1) : reference));
        }

        static readonly Dictionary<string, char> KindLetter = new Dictionary<string, char>
        {
            [RuleKind.Trigger] = 'T', [RuleKind.Continuous] = 'C', [RuleKind.Replace] = 'R', [RuleKind.Constrain] = 'K', [RuleKind.Meta] = 'M', [RuleKind.System] = 'S',
        };

        public static bool KindAllows(Prim op, string kind) => op != null && KindLetter.TryGetValue(kind ?? "", out var c) && op.kinds.IndexOf(c) >= 0;

        /// <summary>Meta selectors over owned abilities.</summary>
        public static readonly string[] AbilitySelectors = { "ByTag", "ByElement", "ByTrigger", "Strongest", "Newest", "All" };

        /// <summary>Whitelisted trigger swaps for RetriggerTagged (from→to).</summary>
        public static readonly HashSet<string> RetriggerPairs = new HashSet<string>
        {
            "Dash>PerfectDodge", "Hit>Crit", "Kill>Overkill", "Attack>ComboFinish", "Damaged>Guard", "SkillCast>Dash", "Crit>Hit",
        };

        /// <summary>For the prompt: the primitives grouped by category, compactly, plus the relation/temporal layers.</summary>
        public static string Describe(IEnumerable<Prim> prims, bool layers = true)
        {
            var sb = new StringBuilder();
            foreach (var g in prims.GroupBy(p => p.cat))
            {
                sb.Append('[').Append(g.Key).Append("]\n");
                foreach (var p in g)
                {
                    string id = p.id.Contains(':') && p.cat != "Value" ? p.id.Substring(p.id.IndexOf(':') + 1) : p.id.StartsWith("val:") ? p.id.Substring(4) : p.id;
                    sb.Append("- ").Append(id).Append(": ").Append(p.desc);
                    if (p.cat == "Trigger") sb.Append(" {provides: ").Append(p.provides).Append('}');
                    if (p.cat == "Op")
                    {
                        sb.Append(" {kinds: ").Append(p.kinds).Append(string.IsNullOrEmpty(p.needs) ? "" : ", needs: " + p.needs).Append(", tier ").Append(p.tier);
                        if (!string.IsNullOrEmpty(p.relations)) sb.Append(", rel: ").Append(p.relations.Replace(' ', '|'));
                        if (p.kinds == "T" && p.temporals != "Immediate Delay") sb.Append(", time: ").Append(p.temporals.Replace(' ', '|'));
                        sb.Append('}');
                    }
                    if (p.requires != Ctx.None) sb.Append(" {requires ").Append(p.requires).Append('}');
                    sb.Append('\n');
                }
            }
            if (!layers) return sb.ToString();
            sb.Append("[Relation] (op.rel; op.times = jumps/pieces/bounces)\n");
            foreach (var kv in Relations) sb.Append("- ").Append(kv.Key).Append(": ").Append(kv.Value.desc).Append(" {tier ").Append(kv.Value.tier).Append("}\n");
            sb.Append("[Temporal] (op.time; op.times = ticks/uses, op.duration = window)\n");
            foreach (var kv in Temporals) sb.Append("- ").Append(kv.Key).Append(": ").Append(kv.Value.desc).Append('\n');
            sb.Append("[Entities] ").Append(string.Join(", ", Capabilities.Select(kv => $"{kv.Key}({kv.Value})"))).Append('\n');
            sb.Append("[Statuses] ").Append(string.Join(" ", Statuses)).Append("  [Elements] ").Append(string.Join(" ", Elements)).Append('\n');
            sb.Append("[Convert pairs] ").Append(string.Join(", ", ConvertPairs.Keys)).Append('\n');
            sb.Append("[Meta selectors] rule.select = ").Append(string.Join("|", AbilitySelectors)).Append(" + rule.selectArg   [Retrigger pairs] ").Append(string.Join(", ", RetriggerPairs)).Append('\n');
            sb.Append("[Replaceable actions] rule.replaces = Dash|Potion|ComboFinisher   [Forbid] Dash|Potion|Regen|Skill   [Stat modes] ").Append(string.Join("|", StatModes)).Append('\n');
            sb.Append("[State types] ").Append(string.Join(" ", StateTypes)).Append(" (Charge regenerates; Timer counts down and fires StateEmpty)\n");
            return sb.ToString();
        }
    }

    /// <summary>One row of the primitive tuning table in AbilityRules.asset (0 = keep the code default).</summary>
    [Serializable]
    public class PrimTuning
    {
        public string id;
        public float def, min, max;
        [UnityEngine.Tooltip("Triggers only: generic events per second in combat")]
        public float freq;
    }
}
