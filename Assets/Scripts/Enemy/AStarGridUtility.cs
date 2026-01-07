using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared grid utilities + A* pathfinding (4-neighborhood).
/// Mirrors the style of BaselineAgent's grid A* (raycast walls between adjacent cells).
/// </summary>
public static class AStarGridUtility
{
    public static Vector2Int WorldToCell(Vector3 world, float cellSize)
    {
        int x = Mathf.RoundToInt(world.x / cellSize);
        int z = Mathf.RoundToInt(world.z / cellSize);
        return new Vector2Int(x, z);
    }

    public static Vector3 CellToWorld(Vector2Int cell, float cellSize, float y)
    {
        return new Vector3(cell.x * cellSize, y, cell.y * cellSize);
    }

    public static int HeuristicManhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    /// <summary>
    /// Raycast-based wall test between adjacent cells (same idea as BaselineAgent.IsWallBetween).
    /// </summary>
    public static bool IsWallBetween(
        Vector2Int from,
        Vector2Int to,
        float cellSize,
        float rayY,
        float wallCheckMargin,
        LayerMask wallLayer)
    {
        Vector3 a = new Vector3(from.x * cellSize, rayY, from.y * cellSize);
        Vector3 b = new Vector3(to.x * cellSize, rayY, to.y * cellSize);

        Vector3 dir = (b - a);
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return false;
        dir /= dist;

        float castDist = Mathf.Max(0f, dist - wallCheckMargin);
        return Physics.Raycast(a, dir, castDist, wallLayer);
    }

    /// <summary>
    /// A* path from start to goal on a 4-neighborhood grid.
    /// Returns a path list that includes start and goal cells.
    /// Returns null if not found.
    /// </summary>
    public static List<Vector2Int> AStar(
        Vector2Int start,
        Vector2Int goal,
        float cellSize,
        float rayY,
        float wallCheckMargin,
        LayerMask wallLayer,
        int safetyLimit = 50000)
    {
        HashSet<Vector2Int> closed = new HashSet<Vector2Int>();
        List<Vector2Int> open = new List<Vector2Int> { start };

        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, int> gScore = new Dictionary<Vector2Int, int>();
        Dictionary<Vector2Int, int> fScore = new Dictionary<Vector2Int, int>();

        gScore[start] = 0;
        fScore[start] = HeuristicManhattan(start, goal);

        Vector2Int[] dirs = new Vector2Int[]
        {
            new Vector2Int(0, 1),
            new Vector2Int(0,-1),
            new Vector2Int(-1,0),
            new Vector2Int(1, 0)
        };

        int safety = 0;

        while (open.Count > 0)
        {
            safety++;
            if (safety > safetyLimit)
            {
                Debug.LogWarning("[AStarGridUtility] A* safety limit reached.");
                break;
            }

            // pick node with lowest f
            Vector2Int current = open[0];
            int bestF = GetScore(fScore, current, int.MaxValue);
            for (int i = 1; i < open.Count; i++)
            {
                var c = open[i];
                int f = GetScore(fScore, c, int.MaxValue);
                if (f < bestF)
                {
                    bestF = f;
                    current = c;
                }
            }

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            open.Remove(current);
            closed.Add(current);

            foreach (var d in dirs)
            {
                Vector2Int nb = current + d;

                if (closed.Contains(nb)) continue;
                if (IsWallBetween(current, nb, cellSize, rayY, wallCheckMargin, wallLayer)) continue;

                int tentative = GetScore(gScore, current, int.MaxValue) + 1;

                if (!open.Contains(nb))
                    open.Add(nb);
                else if (tentative >= GetScore(gScore, nb, int.MaxValue))
                    continue;

                cameFrom[nb] = current;
                gScore[nb] = tentative;
                fScore[nb] = tentative + HeuristicManhattan(nb, goal);
            }
        }

        return null;
    }

    public static int PathLength(List<Vector2Int> path)
    {
        if (path == null || path.Count == 0) return int.MaxValue;
        return path.Count; // includes start
    }

    private static int GetScore(Dictionary<Vector2Int, int> dict, Vector2Int key, int fallback)
    {
        if (dict.TryGetValue(key, out int v)) return v;
        return fallback;
    }

    private static List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        var total = new List<Vector2Int> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            total.Add(current);
        }
        total.Reverse();
        return total;
    }
}
