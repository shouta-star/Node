//using System.Collections.Generic;
//using UnityEngine;

//public class FrontierNode : MonoBehaviour
//{
//    public static readonly List<FrontierNode> All = new();
//    public static readonly Dictionary<Vector2Int, FrontierNode> CellMap = new();

//    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
//    private static void ResetStatics()
//    {
//        All.Clear();
//        CellMap.Clear();
//    }

//    [Header("Graph (visited cells only)")]
//    public List<FrontierNode> links = new();

//    [Header("State (computed by Explorer)")]
//    public int unknownCount;
//    public int wallCount;

//    [HideInInspector] public Vector2Int cell;

//    // bit: 0=F, 1=B, 2=L, 3=R
//    [SerializeField] private int blockedMask = 0;

//    // =========================
//    // Debug Draw Links (Raycast + Gizmos)
//    // =========================
//    [Header("Debug Draw Links")]
//    public bool drawLinks = true;
//    public float drawHeight = 0.2f;
//    public float endSphereRadius = 0.07f;

//    [Tooltip("Explorer側から wallLayer を流し込みます（未設定なら手動で入れてOK）")]
//    public LayerMask debugWallLayer;

//    [Tooltip("Raycastで壁に当たったら赤、当たらなければ緑")]
//    public bool colorByWallHit = true;

//    private void OnEnable()
//    {
//        if (!All.Contains(this)) All.Add(this);
//    }

//    private void OnDisable()
//    {
//        All.Remove(this);
//        if (CellMap.TryGetValue(cell, out var n) && n == this)
//            CellMap.Remove(cell);
//    }

//    public void RegisterCell(float cellSize)
//    {
//        cell = WorldToCell(transform.position, cellSize);
//        CellMap[cell] = this;
//    }

//    public static Vector2Int WorldToCell(Vector3 pos, float cellSize)
//    {
//        int x = Mathf.RoundToInt(pos.x / cellSize);
//        int z = Mathf.RoundToInt(pos.z / cellSize);
//        return new Vector2Int(x, z);
//    }

//    public static Vector3 CellToWorld(Vector2Int c, float cellSize, float y = 0f)
//    {
//        return new Vector3(c.x * cellSize, y, c.y * cellSize);
//    }

//    public void LinkWith(FrontierNode other)
//    {
//        if (other == null || other == this) return;
//        if (!links.Contains(other)) links.Add(other);
//        if (!other.links.Contains(this)) other.links.Add(this);
//    }

//    public void UnlinkWith(FrontierNode other)
//    {
//        if (other == null) return;
//        links.Remove(other);
//        other.links.Remove(this);
//    }

//    public bool IsBlockedIndex(int i) => (blockedMask & (1 << i)) != 0;

//    public void MarkBlockedIndex(int i) => blockedMask |= (1 << i);

//    private void OnDrawGizmos()
//    {
//        if (!drawLinks) return;
//        if (links == null) return;

//        Vector3 a = transform.position + Vector3.up * drawHeight;

//        foreach (var bNode in links)
//        {
//            if (bNode == null) continue;

//            Vector3 b = bNode.transform.position + Vector3.up * drawHeight;
//            Vector3 delta = b - a;
//            float dist = delta.magnitude;
//            if (dist <= 0.0001f) continue;

//            Vector3 dir = delta / dist;

//            // デフォ色：緑（通っている）
//            Color c = Color.green;

//            // Raycast で壁に当たるなら赤
//            if (colorByWallHit && debugWallLayer.value != 0)
//            {
//                if (Physics.Raycast(a, dir, dist, debugWallLayer, QueryTriggerInteraction.Ignore))
//                    c = Color.red;
//            }

//            Gizmos.color = c;
//            Gizmos.DrawLine(a, b);
//            Gizmos.DrawSphere(b, endSphereRadius);
//        }
//    }
//}

using System.Collections.Generic;
using UnityEngine;

public class FrontierNode : MonoBehaviour
{
    public static readonly List<FrontierNode> All = new();
    public static readonly Dictionary<Vector2Int, FrontierNode> CellMap = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
        CellMap.Clear();
    }

    [Header("Graph (visited cells only)")]
    public List<FrontierNode> links = new();

    [Header("State (computed by Explorer)")]
    public int unknownCount;
    public int wallCount;

    [HideInInspector] public Vector2Int cell;

    // bit: 0=F, 1=B, 2=L, 3=R
    [SerializeField] private int blockedMask = 0;

    // =========================
    // Debug Draw Links (Raycast + Gizmos)
    // =========================
    [Header("Debug Draw Links")]
    public bool drawLinks = true;
    public float drawHeight = 0.2f;
    public float endSphereRadius = 0.07f;

    [Tooltip("Explorer側から wallLayer を流し込みます（未設定なら手動で入れてOK）")]
    public LayerMask debugWallLayer;

    [Tooltip("Raycastで壁に当たったら赤、当たらなければ緑")]
    public bool colorByWallHit = true;

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (CellMap.TryGetValue(cell, out var n) && n == this)
            CellMap.Remove(cell);
    }

    public void RegisterCell(float cellSize)
    {
        cell = WorldToCell(transform.position, cellSize);
        CellMap[cell] = this;
    }

    public static Vector2Int WorldToCell(Vector3 pos, float cellSize)
    {
        int x = Mathf.RoundToInt(pos.x / cellSize);
        int z = Mathf.RoundToInt(pos.z / cellSize);
        return new Vector2Int(x, z);
    }

    public static Vector3 CellToWorld(Vector2Int c, float cellSize, float y = 0f)
    {
        return new Vector3(c.x * cellSize, y, c.y * cellSize);
    }

    public void LinkWith(FrontierNode other)
    {
        if (other == null || other == this) return;
        if (!links.Contains(other)) links.Add(other);
        if (!other.links.Contains(this)) other.links.Add(this);
    }

    public void UnlinkWith(FrontierNode other)
    {
        if (other == null) return;
        links.Remove(other);
        other.links.Remove(this);
    }

    public bool IsBlockedIndex(int i) => (blockedMask & (1 << i)) != 0;

    public void MarkBlockedIndex(int i) => blockedMask |= (1 << i);

    // ★追加：blocked解除（「通った方向はblockedにしない」ため）
    public void ClearBlockedIndex(int i) => blockedMask &= ~(1 << i);

    private void OnDrawGizmos()
    {
        if (!drawLinks) return;
        if (links == null) return;

        Vector3 a = transform.position + Vector3.up * drawHeight;

        foreach (var bNode in links)
        {
            if (bNode == null) continue;

            Vector3 b = bNode.transform.position + Vector3.up * drawHeight;
            Vector3 delta = b - a;
            float dist = delta.magnitude;
            if (dist <= 0.0001f) continue;

            Vector3 dir = delta / dist;

            Color c = Color.green;

            if (colorByWallHit && debugWallLayer.value != 0)
            {
                if (Physics.Raycast(a, dir, dist, debugWallLayer, QueryTriggerInteraction.Ignore))
                    c = Color.red;
            }

            Gizmos.color = c;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(b, endSphereRadius);
        }
    }
}
