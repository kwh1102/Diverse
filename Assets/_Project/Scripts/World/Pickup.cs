using UnityEngine;

namespace Diverse
{
    /// <summary>드롭 아이템. 튀어오른 뒤 가까이 가면 빨려 들어온다.</summary>
    public class Pickup : MonoBehaviour
    {
        public string kind;     // gold, xp, heart, shard
        public float value;
        Vector2 pos, vel;
        float z, vz, t;
        [SerializeField] SpriteRenderer sr;
        bool magnet;

        public static void Drop(string kind, float value, Vector2 at, int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Instantiate(DB.Asset.pickupPrefab, at, Quaternion.identity);
                p.name = "pickup_" + kind;
                p.kind = kind; p.value = value;
                p.pos = at;
                p.vel = Random.insideUnitCircle * Random.Range(1.5f, 3.5f);
                p.vz = Random.Range(3f, 6f);
                Art.Setup(p.sr, Art.Item(kind), 0, kind == "xp" || kind == "shard");
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            t += dt;
            var pl = Player.I;
            if (pl == null) return;
            float d = Vector2.Distance(pl.Pos, pos);
            if (t > 0.45f && d < 2.8f) magnet = true;
            if (magnet)
            {
                vel = Vector2.Lerp(vel, (pl.Pos + Vector2.up * 0.3f - pos).normalized * 14f, 1 - Mathf.Exp(-10 * dt));
                z = Mathf.Lerp(z, 0.2f, dt * 10);
                if (d < 0.45f) { Collect(pl); return; }
            }
            else
            {
                vel *= Mathf.Exp(-5 * dt);
                vz -= 22 * dt; z += vz * dt;
                if (z < 0) { z = 0; vz = -vz * 0.4f; }
            }
            pos += vel * dt;
            float bob = magnet ? 0 : Mathf.Sin(t * 5) * 0.04f;
            transform.position = new Vector3(pos.x, pos.y + z + 0.15f + bob, 0);
            sr.sortingOrder = Art.SortY(pos.y);
            if (t > 40) Destroy(gameObject);
        }

        void Collect(Player p)
        {
            switch (kind)
            {
                case "gold": p.GainGold((int)value); Sfx.Play("coin", 0.4f); break;
                case "xp": p.GainXp(value); Sfx.Play("pickup", 0.3f, 1.3f); break;
                case "heart": p.Heal(value); Sfx.Play("pickup", 0.5f, 0.8f); break;
                case "shard":
                    Game.I.World.memoryShards += (int)value;
                    Fx.I?.Number(p.Pos + Vector2.up * 1f, $"기억의 조각 +{value}", new Color(0.8f, 0.7f, 1f), 0.9f);
                    Sfx.Play("evolve", 0.4f, 2f);
                    break;
            }
            Fx.I?.Burst(pos, kind == "gold" ? Pal.Gold : kind == "xp" ? Pal.Frost : Pal.ShadowEl, 3, 2, 0.25f);
            p.Abilities?.Raise("Pickup", pos, kind);
            Destroy(gameObject);
        }
    }
}
