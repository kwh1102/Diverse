using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Marks an editor-only stand-in (e.g. a baked picture of the procedural world) that should vanish the moment play starts.
    /// Tag the object EditorOnly as well so builds strip it.
    /// </summary>
    public class EditorOnlyPreview : MonoBehaviour
    {
        void Awake() => Destroy(gameObject);
    }
}
