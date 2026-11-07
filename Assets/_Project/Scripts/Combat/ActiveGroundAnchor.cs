using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>A visible ground anchor owned by the cast. The trigger uses the same footprint as the renderer.</summary>
    public sealed class ActiveGroundAnchor : MonoBehaviour
    {
        [SerializeField] SpriteRenderer footprint, body;
        Func<bool> valid;
        Player trapOwner;
        Color armedColor;
        float armAt;
        static readonly List<ActiveGroundAnchor> traps = new();
        public Vector2 Position => transform.position;
        public float Radius { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTraps() => traps.Clear();

        public static ActiveGroundAnchor PlaceTrap(Player owner, Vector2 position, float radius, Color color, Func<bool> valid)
        {
            var placed = traps.Where(t => t.trapOwner == owner).ToArray();
            int limit = Mathf.Max(1, ActiveSkillGenerator.Rules.trapLimit);
            for (int i = 0; i <= placed.Length - limit; i++) placed[i].Dismiss();
            var trap = Spawn(position, radius, true, color, valid);
            trap.trapOwner = owner;
            trap.armAt = Time.time + ActiveSkillGenerator.Rules.trapArmTime;
            trap.armedColor = trap.footprint.color;
            trap.footprint.color = new Color(trap.armedColor.r, trap.armedColor.g, trap.armedColor.b, trap.armedColor.a * .45f);
            trap.body.color = new Color(.8f, .77f, .72f);
            traps.Add(trap);
            return trap;
        }

        void OnDisable() { if (trapOwner != null) traps.Remove(this); }

        public static ActiveGroundAnchor Spawn(Vector2 position, float radius, bool trap, Color color, Func<bool> valid)
        {
            var anchor = Instantiate(DB.Asset.activeGroundAnchorPrefab, position, Quaternion.identity);
            anchor.valid = valid;
            anchor.Radius = radius;
            var bounds = anchor.footprint.sprite.bounds.size;
            float diameter = radius * 2;
            anchor.footprint.transform.localScale = new Vector3(diameter / bounds.x, diameter / bounds.y, 1);
            var groundColor = CombatVisuals.Tint(color);
            groundColor.a = anchor.footprint.color.a;
            anchor.footprint.color = groundColor;
            anchor.body.enabled = trap;
            anchor.MoveTo(position);
            return anchor;
        }

        public void MoveTo(Vector2 position)
        {
            transform.position = position;
            int order = Art.SortY(position.y);
            Art.Setup(footprint, footprint.sprite, order - 3);
            Art.Setup(body, body.sprite, order);
        }

        void Update()
        {
            if (valid != null && !valid()) { Dismiss(); return; }
            if (armAt > 0 && Time.time >= armAt)
            {
                armAt = 0;
                footprint.color = armedColor;
                body.color = Color.white;
            }
        }

        public void Dismiss()
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
