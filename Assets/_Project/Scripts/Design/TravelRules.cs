using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>Ability-specific terrain rules, independent of the biome's visual water treatment.</summary>
    public static class TravelRules
    {
        public const float ExitAllowance = 1.5f;
        public const float ProbeStep = 0.1f;

        public static bool CanCrossLiquid(TravelType type, Ground ground) =>
            WorldGen.IsLiquid(ground) && (type == TravelType.Flight || type == TravelType.Swim && ground != Ground.Lava);

        /// <summary>Shortest reachable way out, in any direction, across the loaded world. No distance cutoff.</summary>
        public static bool TryFindNearestExit(Vector2 from, Func<Vector2, bool> canOccupy,
            Func<Vector2, bool> canLand, List<Vector2> path)
        {
            path.Clear();
            if (canLand(from)) return false;
            const float grid = 0.5f;
            int serial = 0;
            var frontier = new SortedSet<(float cost, int serial, Vector2Int cell)>(
                Comparer<(float cost, int serial, Vector2Int cell)>.Create((a, b) =>
                { int order = a.cost.CompareTo(b.cost); return order != 0 ? order : a.serial.CompareTo(b.serial); }));
            var costs = new Dictionary<Vector2Int, float> { [Vector2Int.zero] = 0 };
            var parents = new Dictionary<Vector2Int, Vector2Int>();
            frontier.Add((0, serial++, Vector2Int.zero));
            while (frontier.Count > 0)
            {
                var current = frontier.Min; frontier.Remove(current);
                if (current.cost > costs[current.cell] + 0.0001f) continue;
                Vector2 point = from + (Vector2)current.cell * grid;
                if (current.cell != Vector2Int.zero && canLand(point))
                {
                    for (var cell = current.cell; cell != Vector2Int.zero; cell = parents[cell])
                        path.Add(from + (Vector2)cell * grid);
                    path.Reverse();
                    return true;
                }
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0) continue;
                    var cell = current.cell + new Vector2Int(x, y);
                    float distance = grid * (x != 0 && y != 0 ? 1.41421356f : 1);
                    float cost = current.cost + distance;
                    if (costs.TryGetValue(cell, out float best) && best <= cost) continue;
                    Vector2 next = from + (Vector2)cell * grid;
                    int probes = Mathf.CeilToInt(distance / ProbeStep);
                    bool clear = true;
                    for (int i = 1; i <= probes; i++)
                        if (!canOccupy(Vector2.Lerp(point, next, i / (float)probes))) { clear = false; break; }
                    if (!clear) continue;
                    costs[cell] = cost; parents[cell] = current.cell;
                    frontier.Add((cost, serial++, cell));
                }
            }
            return false;
        }

        /// <summary>Legacy short probe for editor tools. Gameplay uses TryFindNearestExit without this distance limit.</summary>
        public static bool TryFindExit(Vector2 from, Vector2 direction, Func<Vector2, bool> canOccupy,
            Func<Vector2, bool> canLand, out Vector2 exit)
        {
            exit = from;
            if (direction.sqrMagnitude < 0.001f || canLand(from)) return false;
            direction.Normalize();
            int steps = Mathf.RoundToInt(ExitAllowance / ProbeStep);
            for (int i = 1; i <= steps; i++)
            {
                var next = from + direction * (i * ProbeStep);
                if (!canOccupy(next)) return false;
                if (!canLand(next)) continue;
                exit = next;
                return true;
            }
            return false;
        }
    }
}
