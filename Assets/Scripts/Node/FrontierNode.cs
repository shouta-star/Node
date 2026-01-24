using System.Collections.Generic;
using UnityEngine;

public class FrontierNode : MonoBehaviour
{
    // =========================
    // Static registry
    // =========================
    public static readonly List<FrontierNode> All = new List<FrontierNode>();
    public static readonly Dictionary<Vector2Int, FrontierNode> CellMap = new Dictionary<Vector2Int, FrontierNode>();

    public static int totalPassCount = 0;

    // =========================
    // Data
    // =========================
    [Header("Grid")]
    public Vector2Int cell;

    [Header("Links (only where player actually moved)")]
    public List<FrontierNode> links = new List<FrontierNode>();

    [Header("Exploration state (computed by Explorer)")]
    public int unknownCount = 0;
    public int wallCount = 0;

    [Header("Visits")]
    public int passCount = 0;

    [Header("Debug")]
    public bool drawLinksGizmos = true;
    public LayerMask debugWallLayer;

    // 0:F, 1:B, 2:L, 3:R (Explorer と同じ並びにすること)
    [SerializeField] private int blockedMask = 0;

    private void Awake()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDestroy()
    {
        All.Remove(this);
        if (CellMap.TryGetValue(cell, out var n) && n == this)
            CellMap.Remove(cell);
    }

    public void RegisterCell(float cellSize)
    {
        cell = WorldToCell(transform.position, cellSize);

        if (CellMap.TryGetValue(cell, out var exist))
        {
            if (exist != null && exist != this)
                CellMap[cell] = this;
        }
        else
        {
            CellMap.Add(cell, this);
        }
    }

    public static Vector2Int WorldToCell(Vector3 worldPos, float cellSize)
    {
        int x = Mathf.RoundToInt(worldPos.x / cellSize);
        int z = Mathf.RoundToInt(worldPos.z / cellSize);
        return new Vector2Int(x, z);
    }

    public bool IsBlockedIndex(int dirIndex) => (blockedMask & (1 << dirIndex)) != 0;
    public void MarkBlockedIndex(int dirIndex) => blockedMask |= (1 << dirIndex);
    public void ClearBlockedIndex(int dirIndex) => blockedMask &= ~(1 << dirIndex);

    public bool HasLinkInDir(int dirIndex)
    {
        if (links == null || links.Count == 0) return false;

        Vector3 dir = DirIndexToWorld(dirIndex);

        for (int i = 0; i < links.Count; i++)
        {
            var other = links[i];
            if (other == null) continue;

            Vector3 delta = (other.transform.position - transform.position).normalized;
            float dot = Vector3.Dot(delta, dir);
            if (dot > 0.95f) return true;
        }

        return false;
    }

    public static Vector3 DirIndexToWorld(int dirIndex)
    {
        switch (dirIndex)
        {
            case 0: return Vector3.forward;
            case 1: return Vector3.back;
            case 2: return Vector3.left;
            case 3: return Vector3.right;
        }
        return Vector3.zero;
    }

    public void OnPassed()
    {
        passCount++;
        totalPassCount++;
    }

    public static void ApplyColorsOnceBeforeScreenshot()
    {
        int max = 1;
        for (int i = 0; i < All.Count; i++)
        {
            var n = All[i];
            if (n == null) continue;
            if (n.passCount > max) max = n.passCount;
        }

        for (int i = 0; i < All.Count; i++)
        {
            var n = All[i];
            if (n == null) continue;
            n.ApplyColorByPassCount(max);
        }
    }

    private void ApplyColorByPassCount(int maxPass)
    {
        if (CompareTag("Goal")) return;

        float t = maxPass <= 0 ? 0f : Mathf.Clamp01((float)passCount / maxPass);
        Color c = new Color(1f, 1f - t, 1f - t);

        var r = GetComponentInChildren<Renderer>();
        if (r != null && r.material != null)
            r.material.color = c;
    }

    public static void ClearAllNodes()
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var n = All[i];
            if (n != null) Object.Destroy(n.gameObject);
        }

        All.Clear();
        CellMap.Clear();
        totalPassCount = 0;
    }

    private void OnDrawGizmos()
    {
        if (!drawLinksGizmos) return;
        if (links == null) return;

        Vector3 origin = transform.position + Vector3.up * 0.1f;

        for (int i = 0; i < links.Count; i++)
        {
            var other = links[i];
            if (other == null) continue;

            Vector3 target = other.transform.position + Vector3.up * 0.1f;
            Vector3 dir = (target - origin);
            float dist = dir.magnitude;
            if (dist <= 0.001f) continue;

            Vector3 dirN = dir / dist;

            bool hitWall = false;
            if (debugWallLayer.value != 0)
                hitWall = Physics.Raycast(origin, dirN, dist, debugWallLayer);

            Gizmos.color = hitWall ? Color.red : Color.green;
            Gizmos.DrawLine(origin, target);
            Gizmos.DrawSphere(origin + dirN * Mathf.Min(0.2f, dist), 0.03f);
        }
    }

    // 追加：Cell -> World
    public static Vector3 CellToWorld(Vector2Int c, float cellSize, float y = 0f)
    {
        return new Vector3(c.x * cellSize, y, c.y * cellSize);
    }

    // 追加：相互リンク（Playerが通った方向だけ繋ぐ用途）
    public void LinkWith(FrontierNode other)
    {
        if (other == null || other == this) return;

        if (links == null) links = new List<FrontierNode>();
        if (other.links == null) other.links = new List<FrontierNode>();

        if (!links.Contains(other)) links.Add(other);
        if (!other.links.Contains(this)) other.links.Add(this);
    }

}
