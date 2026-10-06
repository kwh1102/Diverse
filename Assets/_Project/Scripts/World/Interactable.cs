using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Interactable objects in the world (NPCs, chests, graves, statues, shrines, cages, the tower door...).
    /// Interact by right-clicking or with the F key when close.
    /// </summary>
    public class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> All = new List<Interactable>();
        public string id;
        public string kind;              // npc_storyteller, npc_merchant, npc_smith, npc_villager, chest, grave, statue, shrine, cage, tower, board, wanderer, well, bank
        public string label;
        public float range = 1.4f;
        public SpriteRenderer sr;
        [SerializeField] SpriteRenderer shadow;
        [System.NonSerialized] public Sprite[] idleFrames;
        public object data;
        float animT;
        Vector2 basePos;

        public static Interactable Create(Transform parent, string kind, string id, string label, Vector2 pos, Sprite sprite, ChunkView chunk)
        {
            var it = Instantiate(DB.Asset.interactablePrefab, parent, false);
            it.name = kind;
            it.transform.position = pos;
            it.kind = kind; it.id = id; it.label = label;
            it.basePos = pos;
            Art.Setup(it.sr, sprite, Art.SortY(pos.y));
            Art.Setup(it.shadow, Art.Shadow(Mathf.Max(10, Mathf.RoundToInt(sprite.rect.width * 0.6f))), -30000);
            All.Add(it);
            chunk?.Register(it);
            return it;
        }

        void OnDestroy() => All.Remove(this);

        void Update()
        {
            if (idleFrames != null && idleFrames.Length > 1)
            {
                animT += Time.deltaTime;
                sr.sprite = idleFrames[(int)(animT * 2) % idleFrames.Length];
            }
            if (kind == "shrine" || kind == "tower_gate")
                transform.position = basePos + Vector2.up * Mathf.Sin(Time.time * 2) * 0.03f;
        }

        public Vector2 Pos => basePos;

        public static Interactable Nearest(Vector2 p, float extra = 0)
        {
            Interactable best = null; float bd = float.MaxValue;
            foreach (var i in All)
            {
                if (i == null) continue;
                float d = Vector2.Distance(i.basePos, p);
                if (d <= i.range + extra && d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public static Interactable At(Vector2 worldPoint)
        {
            Interactable best = null; float bd = 1.1f;
            foreach (var i in All)
            {
                if (i == null) continue;
                float d = Vector2.Distance(i.basePos + Vector2.up * 0.5f, worldPoint);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }
    }
}
