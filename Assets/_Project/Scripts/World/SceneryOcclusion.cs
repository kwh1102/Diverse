using UnityEngine;

namespace Diverse
{
    /// <summary>Large procedural scenery reveals the hero while keeping the trunk, ordering and collision unchanged.</summary>
    public class SceneryOcclusion : MonoBehaviour
    {
        SpriteRenderer target;
        SceneryProfile profile;
        public void Init(SpriteRenderer renderer, SceneryProfile settings) { target = renderer; profile = settings; }

        void LateUpdate()
        {
            var player = Game.I != null ? Game.I.Player : null;
            if (target == null || player == null) return;
            var p = player.Pos; var bounds = target.bounds;
            bool behind = p.y > transform.position.y + .2f && p.y < bounds.max.y &&
                p.x > bounds.min.x + .2f && p.x < bounds.max.x - .2f;
            var color = target.color;
            color.a = Mathf.MoveTowards(color.a, behind ? profile.occludedAlpha : 1, Time.unscaledDeltaTime * profile.fadeSpeed);
            target.color = color;
        }
    }
}
