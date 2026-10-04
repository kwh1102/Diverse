using System.Collections.Generic;
using System.Text;

namespace Diverse
{
    /// <summary>
    /// The "Ability Language" — the DSL the AI uses to talk to the game engine.
    /// Four-layer structure from the design doc:
    ///   Layer 1 nouns (Entity / Form / Element)
    ///   Layer 2 verbs & relations (Action / Relation)
    ///   Layer 3 grammar (Event / Condition / Temporal)
    ///   Layer 4 base rules (WorldRule) — rules the AI can break only with an explicit Rule Modifier
    /// Instead of listing finished skills, only atoms are provided. Each atom carries tags/cost/description,
    /// and those are used for retrieval (Primitive Retrieval), power budget, and validation.
    /// </summary>
    public class Atom
    {
        public string id;          // identifier the AI uses in output
        public string layer;       // Entity, Event, Action, Relation, Form, Temporal, Condition, Property, Element, Rule
        public string desc;        // meaning (included in the prompt)
        public string[] tags;      // retrieval relevance tags
        public float cost;         // power budget cost (0~100)
        public string[] parameters;// parameters it may take
        public Atom(string id, string layer, string desc, float cost, string tags, params string[] parameters)
        {
            this.id = id; this.layer = layer; this.desc = desc; this.cost = cost;
            this.tags = tags.Split(' ');
            this.parameters = parameters;
        }
    }

    public static class AbilityLanguage
    {
        public static readonly List<Atom> Atoms = new List<Atom>();
        public static readonly Dictionary<string, Atom> ById = new Dictionary<string, Atom>();

        static AbilityLanguage()
        {
            // ── Layer 3: Event (when it fires) ── matches the Trig enum 1:1
            E("Attack", "Basic attack starts", "MELEE RANGED ATTACK");
            E("Hit", "Deals damage to an enemy", "ATTACK MELEE RANGED");
            E("Crit", "Lands a critical hit", "CRIT");
            E("Kill", "Kills an enemy", "KILL");
            E("Dash", "Dash starts", "DODGE MOBILITY");
            E("DashEnd", "Dash ends", "DODGE MOBILITY");
            E("PerfectDodge", "Dodges right before an enemy attack lands", "DODGE");
            E("Damaged", "Takes damage", "GUARD DEFENSE");
            E("SkillCast", "Uses a skill (QWER)", "SKILL MAGIC");
            E("ComboFinish", "Lands the last hit of a basic-attack combo", "MELEE COMBO");
            E("LowHealth", "Health drops to 30% or below", "DEFENSE");
            E("Interval", "Every N seconds", "PASSIVE");
            E("CloneSpawn", "A clone is created", "CLONE");
            E("CloneExpire", "A clone disappears", "CLONE");
            E("StatusApplied", "A status effect is applied to an enemy", "STATUS");
            E("Guard", "Succeeds at guarding/parrying", "SHIELD GUARD");

            // ── Layer 2: Action (what it does) ──
            A("Damage", "Deals instant damage to the target/area", 30, "ATTACK", "power", "radius");
            A("Projectile", "Fires a projectile", 35, "RANGED PROJECTILE", "count", "power", "pierce");
            A("Nova", "Shockwave around a point", 40, "AREA MAGIC", "radius", "power");
            A("Lightning", "Strikes a target with lightning", 35, "LIGHTNING MAGIC", "power", "chains");
            A("Spawn", "Creates an entity (Clone/Turret/Orb/Zone)", 55, "SUMMON CLONE", "duration", "count");
            A("Heal", "Restores health", 30, "DEFENSE", "power");
            A("Shield", "Grants a temporary shield", 30, "DEFENSE GUARD", "power", "duration");
            A("Buff", "Temporarily boosts a stat", 25, "BUFF", "stat", "power", "duration");
            A("ApplyStatus", "Applies a status effect (burn/chill/poison/mark)", 20, "STATUS", "element", "duration");
            A("Pull", "Pulls enemies toward a point", 30, "CONTROL", "radius");
            A("Push", "Pushes enemies away", 20, "CONTROL", "radius", "power");
            A("Blink", "Teleports a short distance", 35, "MOBILITY", "distance");
            A("ResetCooldown", "Shortens skill/dash cooldowns", 40, "SKILL MOBILITY", "power");
            A("Store", "Accumulates something (damage/charges)", 15, "CHARGE", "max");
            A("Release", "Releases what has been stored", 25, "CHARGE AREA", "radius");

            // ── Layer 2: Relation (how things connect) — the more there are, the more unexpected combos become possible ──
            R("Copy", "Copies the player's last action", 25, "CLONE MIMIC");
            R("Inherit", "Inherits the player's tags/modifiers as-is", 15, "CLONE SYNERGY");
            R("Repeat", "Repeats the same effect once more after a delay", 30, "ECHO");
            R("Transfer", "Moves a status/effect from the target to another target", 20, "STATUS");
            R("Chain", "Jumps to a nearby target", 25, "LIGHTNING CHAIN");
            R("Consume", "Consumes a status/charge to amplify", 20, "STATUS CHARGE");
            R("Orbit", "Orbits around the player", 20, "SUMMON ORB");
            R("Mark", "Marks the target so the next hit is amplified", 15, "MARK CRIT");
            R("SwapPosition", "Swaps positions with the target/clone", 20, "MOBILITY CLONE");
            R("Follow", "Follows the player", 5, "SUMMON");

            // ── Layer 1: Entity / Form / Element ──
            N("Clone", "Entity", "The player's afterimage clone. Grip=0 → cannot hold weapons (PhantomWeapon is allowed)", 0, "CLONE");
            N("Turret", "Entity", "A fixed turret that auto-fires at enemies", 0, "SUMMON RANGED");
            N("Orb", "Entity", "An orb of light that orbits the player", 0, "ORB MAGIC");
            N("Zone", "Entity", "A field on the ground that lingers for a while", 0, "AREA");
            N("PhantomWeapon", "Entity", "A ghost of the weapon (needs no grip)", 0, "CLONE SWORD");
            N("AtTarget", "Form", "Target position", 0, "ATTACK");
            N("AtSelf", "Form", "Player position", 0, "DEFENSE");
            N("DashOrigin", "Form", "The point where the dash started", 0, "DODGE");
            N("DashPath", "Form", "Along the dash path", 0, "DODGE");
            N("Forward", "Form", "Toward the cursor", 0, "RANGED");
            N("Ring", "Form", "In a circle around self", 0, "AREA");
            foreach (var el in new[] { "Fire", "Frost", "Lightning", "Shadow", "Holy", "Poison", "Wind" })
                N(el, "Element", el + " element", 5, el.ToUpper());

            // ── Layer 3: Temporal / Condition ──
            T("Immediate", "Immediately", 0);
            T("Delayed", "After a delay (delay seconds)", -5);
            T("Duration", "Lasts for N seconds", 5);
            T("UntilNextAttack", "Until the next attack", 0);
            T("EveryNth", "Only on every Nth occurrence", -10);
            C("Chance", "Fires with probability p", -10, "chance");
            C("TargetHasStatus", "Only when the target has a status", -5, "status");
            C("HealthBelow", "Only when own health is X% or below", -5, "value");
            C("WithinRange", "Only when the target is within N", 0, "value");
            C("Cooldown", "Internal cooldown (seconds)", -10, "seconds");

            // ── Layer 4: Base world rules (WorldRule) ──
            W("Grip", "The player has 2 grips; a two-handed weapon uses both. Clone grip = 0.");
            W("OneSelf", "The player exists only once (clones are not the self, so they cannot be killed instead)");
            W("Gravity", "Projectiles fly only along the ground (no 3D)");
            W("NoTime", "Time cannot be rewound");
            W("Budget", "One ability adds at most one major new mechanic");
        }

        static void Add(Atom a) { Atoms.Add(a); ById[a.id] = a; }
        static void E(string id, string d, string tags) => Add(new Atom(id, "Event", d, 0, tags));
        static void A(string id, string d, float cost, string tags, params string[] p) => Add(new Atom(id, "Action", d, cost, tags, p));
        static void R(string id, string d, float cost, string tags) => Add(new Atom(id, "Relation", d, cost, tags));
        static void N(string id, string layer, string d, float cost, string tags) => Add(new Atom(id, layer, d, cost, tags));
        static void T(string id, string d, float cost) => Add(new Atom(id, "Temporal", d, cost, "TIME"));
        static void C(string id, string d, float cost, string tags, params string[] p) => Add(new Atom(id, "Condition", d, cost, "COND " + tags, p));
        static void W(string id, string d) => Add(new Atom(id, "Rule", d, 0, "RULE"));

        public static bool Exists(string id, string layer = null) =>
            id != null && ById.TryGetValue(id, out var a) && (layer == null || a.layer == layer);

        /// <summary>For the prompt: lists atoms compactly by layer.</summary>
        public static string Describe(IEnumerable<Atom> atoms)
        {
            var sb = new StringBuilder();
            string last = null;
            foreach (var a in atoms)
            {
                if (a.layer != last) { sb.Append("\n[").Append(a.layer).Append("]\n"); last = a.layer; }
                sb.Append("- ").Append(a.id).Append(": ").Append(a.desc);
                if (a.parameters != null && a.parameters.Length > 0) sb.Append(" (params: ").Append(string.Join(", ", a.parameters)).Append(")");
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
