using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Monsters live everywhere instead of in camps: small packs appear on a ring around the player just outside the screen,
    /// picked from the theme of the spot they appear on (ThemeDef.enemies — so the zones, and the mixed land near their
    /// borders, have their own monsters), stronger the farther from the spawn. Monsters left far behind vanish.
    /// Bosses stay at their landmarks, border sites and lairs (World). Values: Data/WorldRules.asset (spawning).
    /// Game.unity → World (this component next to World).
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        readonly List<Enemy> alive = new List<Enemy>();
        float timer;

        void Update()
        {
            var g = Game.I;
            if (g == null || g.State != GameState.Playing || g.Player == null || !g.Player.Alive || WorldStreamer.I == null) return;
            var r = DB.WorldRules;
            var p = g.Player.Pos;

            // Forget the dead, drop the ones left far behind
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                var e = alive[i];
                if (e == null || !e.Alive) { alive.RemoveAt(i); continue; }
                if (Vector2.Distance(e.Pos, p) > r.despawnRadius * (e.Aggro ? 1.6f : 1)) { e.Despawn(); alive.RemoveAt(i); }
            }

            timer -= Time.deltaTime;
            if (timer > 0) return;
            timer = r.spawnInterval;

            int ring = WorldGen.RingAt(p.magnitude);
            if (alive.Count >= r.maxAlive + ring * r.maxAlivePerRing) return;
            if (TryFindSpot(p, r, out var at)) SpawnPack(at, ring, r);
        }

        /// <summary>A walkable spot on the ring around the player, out of sight, away from the town and inside the frontier.</summary>
        bool TryFindSpot(Vector2 p, WorldRulesDef r, out Vector2 at)
        {
            var w = WorldStreamer.I;
            var cam = CameraRig.I != null ? CameraRig.I.Cam : null;
            Vector2 camPos = cam != null ? (Vector2)cam.transform.position : p;
            float halfH = cam != null ? cam.orthographicSize + 1.5f : 10, halfW = cam != null ? halfH * cam.aspect + 1.5f : 16;
            float bound = Game.I.Player.Bound;
            float min = Mathf.Max(r.spawnRadius.x, 2), max = Mathf.Max(min + 1, r.spawnRadius.y);
            for (int i = 0; i < 8; i++)
            {
                at = p + MathX.Dir(Random.Range(0f, 360f)) * Random.Range(min, max);
                if (Mathf.Abs(at.x - camPos.x) < halfW && Mathf.Abs(at.y - camPos.y) < halfH) continue;   // never pop up on screen
                if (at.magnitude < r.safeRadius) continue;
                if (bound > 0 && at.magnitude > bound - 1) continue;
                if (!w.IsLoaded(at) || !w.Free(at, 0.5f)) continue;
                return true;
            }
            at = default;
            return false;
        }

        void SpawnPack(Vector2 at, int ring, WorldRulesDef r)
        {
            var w = WorldStreamer.I;
            var theme = w.Gen.ThemeAt(at.x, at.y);
            if (theme.enemies == null || theme.enemies.Count == 0) return;
            string id = theme.enemies[Random.Range(0, theme.enemies.Count)];
            int count = Random.Range(r.packSize.x, r.packSize.y + 1);
            float tier = WorldGen.TierAt(at.magnitude);
            for (int i = 0; i < count; i++)
            {
                var pos = w.NearestWalkable(at + Random.insideUnitCircle * 1.6f, 0.4f);
                var def = DB.ScaledEnemy(id, tier);
                if (ring > 0 && Random.value < ring * r.eliteChancePerRing) def = DB.Elite(def);
                alive.Add(Enemy.Spawn(def, pos, null));
            }
        }

        /// <summary>A new run or world: the old monsters are gone (World.ResetForRun destroys them).</summary>
        public void Clear() { alive.Clear(); timer = 0; }
    }
}
