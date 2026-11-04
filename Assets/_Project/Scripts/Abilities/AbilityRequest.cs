using System;
using System.Collections.Generic;
using System.Linq;

namespace Diverse
{
    /// <summary>Request vocabulary retrieves the closed language and checks executable IR, never the AI's flavor prose.</summary>
    public sealed class AbilityRequest
    {
        readonly List<(string label,Func<AbilityDef,bool> match)> requirements=new List<(string,Func<AbilityDef,bool>)>();
        public bool HasRequirements=>requirements.Count>0;
        public string Labels=>string.Join(" · ",requirements.Select(r=>r.label));
        static bool Has(AbilityDef a,Func<OpDef,bool> predicate)=>a.rules.SelectMany(r=>r.ops).Any(predicate);
        public static AbilityRequest Parse(string text)
        {
            var result=new AbilityRequest(); string t=(text??"").ToLowerInvariant();
            int instead=Math.Max(t.LastIndexOf("대신",StringComparison.Ordinal),t.LastIndexOf("말고",StringComparison.Ordinal));
            if(instead>=0) t=t.Substring(instead+2);
            void Want(string label,string[] words,Func<AbilityDef,bool> predicate)
            {
                if(!words.Any(t.Contains)) return;
                bool remove=words.Any(w=> { int p=t.IndexOf(w,StringComparison.Ordinal); if(p<0) return false; int end=p+w.Length; string tail=t.Substring(end,Math.Min(t.Length-end,10)); return new[]{"없애","제거","빼줘","빼 줘","without","remove"}.Any(tail.Contains); });
                Func<AbilityDef,bool> match=predicate;
                if(remove) match=a=>!predicate(a);
                result.requirements.Add((remove?label+" 제거":label,match));
            }
            Want("보호막",new[]{"보호막","실드","쉴드","shield","barrier"},a=>Has(a,o=>o.op=="Shield" || o.op=="Convert"&&o.to=="Shield" || o.op=="ReplaceWith"&&o.mode=="Shield"));
            Want("회복",new[]{"회복","치유","healing","heal","lifesteal","흡혈"},a=>Has(a,o=>o.op is "Heal" or "Drain" || o.op=="Convert"&&o.to=="Heal"));
            Want("분신",new[]{"분신","clone"},a=>Has(a,o=>o.op=="Spawn"&&o.entity=="Clone"));
            Want("정화",new[]{"정화","cleanse","상태이상 제거","상태 이상 제거"},a=>Has(a,o=>o.op=="Cleanse"));
            Want("재사용 단축",new[]{"쿨타임","쿨다운","재사용","cooldown"},a=>Has(a,o=>o.op=="ResetCooldowns" || o.stat=="CooldownReduction"));
            Want("치명타",new[]{"치명타","critical"},a=>Has(a,o=>o.stat is "CritChance" or "CritDamage") || a.rules.Any(r=>r.trigger=="Crit"));
            foreach(string element in RuleLanguage.Elements)
            {
                string[] words=element switch { "Fire"=>new[]{"화염","불꽃","fire"},"Frost"=>new[]{"냉기","서리","얼음","frost"},"Lightning"=>new[]{"번개","전기","lightning"},"Poison"=>new[]{"독 피해","독을","독으로","독성","맹독","poison"},"Wind"=>new[]{"바람","wind"},"Shadow"=>new[]{"그림자","shadow"},_=>new[]{element.ToLowerInvariant()} };
                string el=element; Want(element,words,a=>Has(a,o=>o.element==el || o.status==RuleLanguage.StatusOf(el)&&!string.IsNullOrEmpty(o.status)));
            }
            return result;
        }
        public bool Matches(AbilityDef a)=>a!=null && requirements.All(r=>r.match(a));
        public static void ExpandLanguage(GenerationContext context)
        {
            // Conversation can ask for a new direction even when the current build has no affinity for it.
            foreach(var primitive in RuleLanguage.All.Where(p=>p.cat=="Trigger" || p.cat=="Op"&&p.tier<=context.maxTier))
                if(!context.retrieved.Contains(primitive)) context.retrieved.Add(primitive);
        }
    }
}
