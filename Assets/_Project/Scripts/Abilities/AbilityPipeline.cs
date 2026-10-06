using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// ④ Build analysis → ⑤ Stat preprocessing → ⑥ Generation Context → ⑦ Primitive Retrieval.
    /// Produces a compact snapshot of the player's state to hand to the AI (never the whole primitive DB).
    /// </summary>
    public class GenerationContext
    {
        public string weapon, weaponTags, costume, costumeTags;
        public List<string> activeSources = new List<string>();     // mechanics the player can already trigger
        public List<string> seededSources = new List<string>();     // meta amplifies a tag nothing generates yet
        public List<Telemetry.Pattern> patterns;
        public List<string> unused;
        public Dictionary<Attr, int> attrs = new Dictionary<Attr, int>();
        public Dictionary<Attr, int> invest = new Dictionary<Attr, int>();
        public List<AbilityDef> owned;
        public List<KeyValuePair<string, int>> intent;
        public List<Prim> retrieved = new List<Prim>();
        public List<Prim> exploration = new List<Prim>();
        public int level, maxTier;
        public string region;
        public HashSet<string> relevantTags = new HashSet<string>();
        public BuildContext build;
        public Dictionary<string, float> rates = new Dictionary<string, float>();

        public static GenerationContext Build(Player p, Telemetry tel, System.Random rnd, int level = -1)
        {
            var b = BuildContext.Of(p, level);
            var c = new GenerationContext
            {
                weapon = p.Weapon.name,
                weaponTags = p.Weapon.tags,
                costume = p.Costume.name,
                costumeTags = p.Costume.tags,
                owned = new List<AbilityDef>(p.Abilities.Owned),
                level = b.level,
                maxTier = b.MaxTier,
                region = World.I != null ? World.I.Gen.RegionName(p.Pos) : "",
                build = b,
            };
            var f = tel.Extract();
            c.patterns = tel.Patterns(f);
            c.unused = tel.Unused(f);
            c.intent = tel.TopIntent(4).ToList();
            foreach (Attr a in System.Enum.GetValues(typeof(Attr)))
            {
                c.attrs[a] = p.Attrs[(int)a];
                c.invest[a] = tel.investRecent.TryGetValue(a, out var n) ? n : 0;
            }
            foreach (var name in new[] { "Attack", "Hit", "Kill", "Dash", "PerfectDodge", "Damaged", "SkillCast" })
            {
                float r = tel.ObservedRate(name, b.cfg, out float trust);
                if (trust > 0) c.rates[name] = r;
            }

            // ④ Active vs Seeded: what the build can already produce
            c.activeSources.AddRange(p.Weapon.tags.Split(' '));
            c.activeSources.Add("DODGE");
            var generators = new HashSet<string>();
            foreach (var g in c.owned)
                foreach (var r in g.rules.Where(r => r.kind == RuleKind.Trigger))
                    foreach (var o in r.ops)
                    {
                        if (o.op == "Spawn" && !string.IsNullOrEmpty(o.entity)) generators.Add(o.entity.ToUpper());
                        if (RuleLanguage.IsElement(o.element)) generators.Add(o.element.ToUpper());
                        if (o.op == "Projectile") generators.Add("PROJECTILE");
                        if (!string.IsNullOrEmpty(o.status)) { generators.Add("STATUS"); generators.Add(o.status.ToUpper()); }
                    }
            c.activeSources.AddRange(generators);
            foreach (var g in c.owned)
                foreach (var o in g.rules.Where(r => r.kind == RuleKind.Meta).SelectMany(r => r.ops))
                    if (!string.IsNullOrEmpty(o.tag) && !generators.Contains(o.tag) && !c.activeSources.Contains(o.tag) && !c.seededSources.Contains(o.tag))
                        c.seededSources.Add(o.tag);

            foreach (var t in p.Weapon.tags.Split(' ')) c.relevantTags.Add(t);
            foreach (var t in p.Costume.tags.Split(' ')) c.relevantTags.Add(t);
            foreach (var pt in c.patterns.Take(3)) foreach (var t in pt.tags.Split(' ')) c.relevantTags.Add(t);
            foreach (var kv in c.intent) c.relevantTags.Add(kv.Key);
            foreach (var s in c.seededSources) c.relevantTags.Add(s);
            foreach (var s in generators) c.relevantTags.Add(s);

            // ⑦ Primitive Retrieval: grammar (selectors/positions/conditions/values/state ops) always; triggers and effect ops
            // by relevance; meta/system only when the tier allows; plus a few distant primitives (Exploration Pool)
            // Element affinity: the elements the player leans on bias which ops/statuses are retrieved (a hint, not a law)
            var affinity = new HashSet<string>();
            foreach (var el in RuleLanguage.Elements)
                if (c.relevantTags.Contains(el.ToUpperInvariant()) && RuleLanguage.ElementAffinity.TryGetValue(el, out var aff)) foreach (var w in aff.Split(' ')) affinity.Add(w);
            foreach (var pr in RuleLanguage.All)
            {
                if (pr.cat is "Selector" or "Position" or "Condition" or "Value") { c.retrieved.Add(pr); continue; }
                if (pr.cat == "Op" && pr.tier > c.maxTier) continue;
                if (pr.cat == "Op" && (pr.kinds.IndexOfAny(new[] { 'M', 'S', 'C', 'R', 'K' }) >= 0 || pr.tags.Contains("STATE"))) { c.retrieved.Add(pr); continue; }
                if (affinity.Contains(pr.id)) { c.retrieved.Add(pr); continue; }
                if (pr.tags.Any(t => RuleLanguage.TagMatches(new[] { t }, "ATTACK") || c.relevantTags.Any(r => RuleLanguage.TagMatches(new[] { t }, r) || RuleLanguage.TagMatches(new[] { r }, t)))) c.retrieved.Add(pr);
            }
            var far = RuleLanguage.All.Where(x => !c.retrieved.Contains(x) && (x.cat == "Trigger" || x.cat == "Op" && x.tier <= c.maxTier))
                .OrderBy(_ => rnd.Next()).Take(6).ToList();
            c.exploration = far;
            return c;
        }

        static string Level(int v, int avg)
        {
            float r = (v - avg) / Mathf.Max(1f, avg);
            if (r > 0.6f) return "VERY_HIGH";
            if (r > 0.2f) return "HIGH";
            if (r < -0.6f) return "VERY_LOW";
            if (r < -0.2f) return "LOW";
            return "AVERAGE";
        }

        /// <summary>⑥ The player-state text sent to the LLM (not raw logs — a compressed representation).</summary>
        public string PlayerProfile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("EQUIPMENT");
            sb.AppendLine($"- {weapon} (tags: {weaponTags})");
            sb.AppendLine($"- Costume: {costume} (tags: {costumeTags})");
            sb.AppendLine("ACTIVE SOURCES: " + string.Join(", ", activeSources.Distinct()));
            if (seededSources.Count > 0)
                sb.AppendLine("SEEDED SOURCES (amplified but nothing generates it yet — player deliberately chose it): " + string.Join(", ", seededSources));
            sb.AppendLine("DOMINANT BEHAVIOR");
            if (patterns.Count == 0) sb.AppendLine("- (not enough data)");
            foreach (var p in patterns.Take(4)) sb.AppendLine($"- {p.id}: {p.desc} (strength {p.confidence:0.00})");
            if (rates.Count > 0) sb.AppendLine("OBSERVED RATES (/s): " + string.Join(", ", rates.Select(kv => $"{kv.Key}={kv.Value:0.##}")));
            if (unused.Count > 0) sb.AppendLine("UNUSED: " + string.Join(", ", unused));
            int avg = Mathf.Max(1, (int)attrs.Values.Average());
            sb.AppendLine("STATS: " + string.Join(", ", attrs.Select(kv => $"{kv.Key}={Level(kv.Value, avg)}")));
            var inv = invest.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ToList();
            if (inv.Count > 0) sb.AppendLine("STAT INVESTMENT INTENT: favoring " + string.Join(" > ", inv.Select(kv => kv.Key)));
            sb.AppendLine("CURRENT ABILITIES");
            if (owned.Count == 0) sb.AppendLine("- (none)");
            foreach (var g in owned) sb.AppendLine($"- {g.name} [tier {g.tier}, tags: {string.Join(" ", g.tags)}]: {g.Explain().Replace("\n", " / ")}");
            if (intent.Count > 0) sb.AppendLine("PLAYER INTENT (tags picked before): " + string.Join(", ", intent.Select(kv => $"{kv.Key}x{kv.Value}")));
            sb.AppendLine($"LEVEL {level} — complexity tiers allowed: 0..{maxTier} (0 numeric, 1 reactive, 2 stateful, 3 meta, 4 system)");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Candidate pipeline: validate (RuleValidator) → score → pick. Numbers are decided by the validator's balancer, not the AI.
    /// </summary>
    public static class AbilityPipeline
    {
        /// <summary>Validate a batch; returns accepted (with ids) and writes a compact log.</summary>
        public static List<AbilityDef> ValidateAll(List<AbilityDef> candidates, BuildContext b, List<string> log, out int ok, List<(AbilityDef a, RuleValidator.Result r)> rejected = null)
        {
            var valid = new List<AbilityDef>();
            int rej = 0, repaired = 0;
            ok = 0;
            if (candidates == null) return valid;
            var before = PowerModel.Evaluate(b.owned, b);
            foreach (var g in candidates)
            {
                if (g == null) continue;
                var res = RuleValidator.Validate(g, b);
                if (!res.ok)
                {
                    rej++;
                    rejected?.Add((g, res));
                    log?.Add($"   ✗ {g.mechanic ?? g.concept ?? "?"}: {res.Summary()}");
                    continue;
                }
                if (res.Repairs.Any()) { repaired++; log?.Add($"   ⚙ {g.mechanic}: {string.Join(", ", res.Repairs.Select(x => x.code))}"); }
                if (valid.Any(v => v.Signature() == g.Signature())) continue;
                g.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                log?.Add($"   ✓ {g.mechanic} — 티어 {g.tier}, {res.Summary()} ({res.vector})");
                valid.Add(g);
            }
            ok = valid.Count;
            log?.Add($"⑩⑪ 검증: 통과 {valid.Count} / 거부 {rej} / 수리 {repaired}");
            return valid;
        }

        /// <summary>⑬ Mechanic evaluation (behavior relevance, synergy, intent, novelty, complexity, generation bias).</summary>
        public static float Score(AbilityDef g, GenerationContext c, Player p)
        {
            float rel = 0, syn = 0, intent = 0, novelty = 1;
            foreach (var pt in c.patterns.Take(3))
                foreach (var t in pt.tags.Split(' '))
                    if (RuleLanguage.TagMatches(g.tags, t)) rel += pt.confidence;
            foreach (var o in c.owned)
                foreach (var t in g.tags)
                    if (o.tags.Contains(t)) syn += 0.25f;
            foreach (var s in c.seededSources) if (g.tags.Contains(s)) syn += 1.2f;
            // Meta abilities are only interesting when they touch what the player owns
            foreach (var o in g.rules.Where(r => r.kind == RuleKind.Meta).SelectMany(r => r.ops))
                syn += 0.4f * c.owned.Count(x => RuleLanguage.TagMatches(x.tags, o.tag));
            foreach (var kv in c.intent) if (g.tags.Contains(kv.Key)) intent += 0.2f * kv.Value;
            foreach (var o in c.owned) if (o.mechanic == g.mechanic) novelty -= 0.6f;
            foreach (var w in c.weaponTags.Split(' ')) if (g.tags.Contains(w)) rel += 0.3f;
            float weird = 0.15f * g.tier + 0.1f * g.rules.Count;          // structurally richer abilities are what we want to show
            float bias = p != null ? p.Abilities.TagWeight(g.tags) : 1;
            return (rel * 1.0f + syn * 0.9f + intent * 0.7f + novelty * 0.6f + weird + Random.Range(0, 0.4f)) * bias;
        }

        /// <summary>Pick n cards: AI first, at most one plain numeric card, varied primary triggers and tiers.</summary>
        public static List<AbilityDef> PickTop(List<AbilityDef> valid, GenerationContext ctx, Player p, int n)
        {
            var scored = valid.Select(g => (g, s: Score(g, ctx, p) + (g.source == "ai" ? 0.8f : 0))).OrderByDescending(x => x.s).ToList();
            var pick = new List<AbilityDef>();
            foreach (var (g, _) in scored)
            {
                if (pick.Count >= n) break;
                if (g.tier == 0 && pick.Any(x => x.tier == 0)) continue;
                if (g.PrimaryTrigger != null && pick.Any(x => x.PrimaryTrigger == g.PrimaryTrigger)) continue;
                if (pick.Count(x => x.tier == g.tier) >= 2) continue;
                pick.Add(g);
            }
            foreach (var (g, _) in scored) { if (pick.Count >= n) break; if (!pick.Contains(g)) pick.Add(g); }
            return pick;
        }
    }
}
