using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Diverse
{
    /// <summary>AI-authored tangible names and individual Images API pictures, persisted with the ability.</summary>
    public static class PartPresentation
    {
        public const int Version = 2;
        [Serializable] class Entry { public int id; public string name, imagePrompt; }
        [Serializable] class Answer { public List<Entry> parts; }

        public static bool Named(AbilityDef a) => a != null && a.partVisualVersion >= Version && !string.IsNullOrWhiteSpace(a.partName);
        public static bool Ready(AbilityDef a) => Named(a) && !string.IsNullOrEmpty(a.partImage);
        public static string DisplayName(AbilityDef a) => Named(a) ? a.partName : "이름 없는 파츠 " + (a?.id ?? "").Substring(0,Math.Min(4,(a?.id ?? "").Length));

        public static IEnumerator Prepare(MonoBehaviour host, IList<AbilityDef> abilities, Action<string> progress = null, IEnumerable<string> reservedNames = null)
        {
            if (!AiClient.Enabled || abilities == null || abilities.Count == 0) yield break;
            var reserved = new HashSet<string>(reservedNames ?? Enumerable.Empty<string>());
            var needs = abilities.Where(a => !Named(a) || reserved.Contains(a.partName)).Distinct().ToList();
            foreach(var a in abilities.Except(needs)) reserved.Add(a.partName);
            for(int attempt=0;attempt<2 && needs.Count>0;attempt++)
            {
                progress?.Invoke("AI가 파츠의 이름과 모습을 구상하는 중…");
                var prompt=new StringBuilder("Create a DISTINCT tangible fantasy equipment object for EACH finalized ability. Korean name, short but concrete; name a physical object a player can hold. Do not name an abstract mechanic (awakening, power, training, shield gain, etc.). Invent silhouettes, materials and construction freely; there is NO fixed object list. Avoid repeating an object or prefix across this batch. imagePrompt: detailed English description of that exact physical object, its unique silhouette/materials and small mechanical details expressing the ability. No letters, numbers, frames or floating text.\nAlready used names (do not repeat): "+string.Join(", ",reserved)+"\n");
                for(int i=0;i<needs.Count;i++) prompt.AppendLine($"id={i}: concept={needs[i].concept}; finalized effect={needs[i].Explain()}");
                prompt.AppendLine("Return JSON {\"parts\":[{\"id\":0,\"name\":\"Korean tangible object name\",\"imagePrompt\":\"English appearance\"}]}");
                string json=null;
                yield return AiClient.Complete("You design unique physical loot for a pixel fantasy action game. Output JSON only. Never choose from a fixed list of item names.",prompt.ToString(),.9f,1600,s=>json=s);
                Answer answer=null;
                try { if(json!=null) answer=JsonUtility.FromJson<Answer>(json); } catch(Exception) { }
                var done=new HashSet<AbilityDef>();
                foreach(var e in answer?.parts ?? new List<Entry>())
                {
                    if(e==null || e.id<0 || e.id>=needs.Count || string.IsNullOrWhiteSpace(e.name) || string.IsNullOrWhiteSpace(e.imagePrompt)) continue;
                    string name=e.name.Trim();
                    if(name.Length>36 || !reserved.Add(name)) continue;
                    var a=needs[e.id];
                    if(done.Contains(a)) continue;
                    a.partName=name; a.partImagePrompt=e.imagePrompt.Trim(); a.partImage=null; a.partVisualVersion=Version; done.Add(a);
                }
                needs=needs.Where(a=>!done.Contains(a)).ToList();
            }
            var jobs=abilities.Where(a=>Named(a) && string.IsNullOrEmpty(a.partImage) && !string.IsNullOrWhiteSpace(a.partImagePrompt)).Distinct().ToList();
            int remaining=jobs.Count,finished=0;
            int batchSize=Math.Max(1,InventoryState.Rules.partImageParallel);
            for(int offset=0;offset<jobs.Count;offset+=batchSize)
            {
                int batchRemaining=Math.Min(batchSize,jobs.Count-offset);
                foreach(var a in jobs.Skip(offset).Take(batchRemaining))
                    host.StartCoroutine(Draw(a,()=> { remaining--;batchRemaining--;finished++;progress?.Invoke($"파츠 그림을 준비하는 중… {finished}/{jobs.Count}"); }));
                while(batchRemaining>0) yield return null;
            }
            progress?.Invoke(abilities.All(Ready) ? "파츠의 이름과 그림을 준비했어." : "일부 이름·그림 생성에 실패했어. 미생성 파츠는 가방에서 다시 요청할 수 있어.");
        }

        static IEnumerator Draw(AbilityDef ability,Action done)
        {
            Texture2D picture=null;
            yield return ImageClient.Generate("A single UNIQUE inventory equipment object, centered, full silhouette, generous transparent margin. Native pixel-art item sprite with crisp stepped outlines, readable chunky shapes, deliberate material shading, a restrained fantasy palette. No text, no numbers, no border, no grid, no background. NOT a generic gem recolor. Object: "+ability.partImagePrompt,p=>picture=p);
            try
            {
                if(picture==null) yield break;
                var cfg=InventoryState.Rules;
                var pixels=Pixelizer.Convert(picture,new RectInt(0,0,picture.width,picture.height),cfg.partIconPixels,false,cfg.partIconColors);
                if(pixels!=null)
                {
                    ability.partImage=DesignStore.SaveImage(pixels);
                    if(Game.I?.World!=null) { Game.I.World.designCalls++; Game.I.World.designCostUsd+=ImageClient.PriceUsd(ImageClient.Model) ?? 0; }
                }
            }
            finally { if(picture!=null) UnityEngine.Object.Destroy(picture); done(); }
        }
    }
}
