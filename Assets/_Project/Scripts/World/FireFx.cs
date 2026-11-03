using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>Campfire flicker (pixel sparks + light).</summary>
    public class FireFx : MonoBehaviour
    {
        [SerializeField] SpriteRenderer glow, flame;
        float t;
        void Start()
        {
            Art.Setup(glow, Art.Glow(20), 31000, true);
            Art.Setup(flame, Art.Orb(Pal.Hex("fff36b"), Pal.Hex("ff8a3d"), 8), Art.SortY(transform.position.y) + 1, true);
        }
        void Update()
        {
            if (WorldStreamer.I != null && WorldStreamer.I.Environment != null && WorldStreamer.I.Environment.Active)
            {
                // The campfire's shared sheet and capped smoke pool replace the old frame-rate-dependent spark bursts.
                flame.enabled = false;
                if (!glow.isVisible || GameTime.Paused) return;
                float pulse = .9f + Mathf.Sin(WorldStreamer.I.Environment.Clock * 5) * .1f;
                glow.color = new Color(1, .6f, .3f, .35f * pulse);
                return;
            }
            if (GameTime.Paused || !glow.isVisible) return;
            t += Time.deltaTime;
            float k = 0.85f + Mathf.PerlinNoise(t * 6, 0.3f) * 0.3f;
            glow.color = new Color(1f, 0.6f, 0.3f, 0.35f * k);
            flame.transform.localScale = new Vector3(1, k * 1.2f, 1);
            if (Random.value < 0.15f) Fx.I?.Burst((Vector2)transform.position + Vector2.up * 0.3f, Random.value < 0.5f ? Pal.Fire : Pal.Lightning, 1, 1.5f, 0.6f, -2f, 1, 40, 90, false, true);
        }
    }
}
