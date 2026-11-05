using System;
using System.Collections;
using System.Collections.Generic;

namespace Diverse
{
    public static class PartPresentation
    {
        public static bool Named(AbilityDef a) => PartCatalog.Find(a?.catalogId) != null;
        public static bool Ready(AbilityDef a) => Named(a);
        public static string DisplayName(AbilityDef a) => PartCatalog.Find(a?.catalogId)?.name ?? "기억의 파츠";
        public static IEnumerator Prepare(UnityEngine.MonoBehaviour host, IList<AbilityDef> parts, Action<string> progress = null, IEnumerable<string> reservedNames = null)
        { progress?.Invoke("고정 파츠 준비 완료"); yield break; }
    }
}
