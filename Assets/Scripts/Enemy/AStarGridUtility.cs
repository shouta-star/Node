using System.Collections.Generic;
using UnityEngine;

public static class AStarGridUtility
{
    public static Vector2Int WorldToCell(Vector3 worldPos, float cellSize, Vector3 gridOrigin)
    {
        Vector3 p = worldPos - gridOrigin;
        return new Vector2Int(
            Mathf.RoundToInt(p.x / cellSize),
            Mathf.RoundToInt(p.z / cellSize)
        );
    }

    public static Vector3 CellToWorld(Vector2Int cell, float cellSize, Vector3 gridOrigin, float y)
    {
        return new Vector3(
            gridOrigin.x + cell.x * cellSize,
            y,
            gridOrigin.z + cell.y * cellSize
        );
    }

    public static int Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    public static bool IsWallBetween(
        Vector2Int from,
        Vector2Int to,
        float cellSize,
        Vector3 gridOrigin,
        float rayY,
        float wallCheckMargin,
        LayerMask wallLayer)
    {
        Vector3 a = new Vector3(gridOrigin.x + from.x * cellSize, rayY, gridOrigin.z + from.y * cellSize);
        Vector3 b = new Vector3(gridOrigin.x + to.x * cellSize, rayY, gridOrigin.z + to.y * cellSize);

        Vector3 dir = b - a;
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return false;
        dir /= dist;

        float castDist = Mathf.Max(0f, dist - wallCheckMargin);
        return Physics.Raycast(a, dir, castDist, wallLayer);
    }

    public static List<Vector2Int> AStar(
        Vector2Int start,
        Vector2Int goal,
        float cellSize,
        Vector3 gridOrigin,
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
        fScore[start] = Heuristic(start, goal);

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
                Vector2Int c = open[i];
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

                if (IsWallBetween(current, nb, cellSize, gridOrigin, rayY, wallCheckMargin, wallLayer))
                    continue;

                int tentative = GetScore(gScore, current, int.MaxValue) + 1;

                if (!open.Contains(nb))
                    open.Add(nb);
                else if (tentative >= GetScore(gScore, nb, int.MaxValue))
                    continue;

                cameFrom[nb] = current;
                gScore[nb] = tentative;
                fScore[nb] = tentative + Heuristic(nb, goal);
            }
        }

        return null;
    }

    public static int PathLen(List<Vector2Int> path)
    {
        if (path == null || path.Count == 0) return int.MaxValue;
        return path.Count;
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