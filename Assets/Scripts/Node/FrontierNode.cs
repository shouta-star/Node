//////using System.Collections.Generic;
//////using UnityEngine;

//////public class FrontierNode : MonoBehaviour
//////{
//////    public static readonly List<FrontierNode> All = new();
//////    public static readonly Dictionary<Vector2Int, FrontierNode> CellMap = new();

//////    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
//////    private static void ResetStatics()
//////    {
//////        All.Clear();
//////        CellMap.Clear();
//////    }

//////    [Header("Graph (visited cells only)")]
//////    public List<FrontierNode> links = new();

//////    [Header("State (computed by Explorer)")]
//////    public int unknownCount;
//////    public int wallCount;

//////    [HideInInspector] public Vector2Int cell;

//////    // bit: 0=F, 1=B, 2=L, 3=R
//////    [SerializeField] private int blockedMask = 0;

//////    // =========================
//////    // Debug Draw Links (Raycast + Gizmos)
//////    // =========================
//////    [Header("Debug Draw Links")]
//////    public bool drawLinks = true;
//////    public float drawHeight = 0.2f;
//////    public float endSphereRadius = 0.07f;

//////    [Tooltip("Explorer側から wallLayer を流し込みます（未設定なら手動で入れてOK）")]
//////    public LayerMask debugWallLayer;

//////    [Tooltip("Raycastで壁に当たったら赤、当たらなければ緑")]
//////    public bool colorByWallHit = true;

//////    private void OnEnable()
//////    {
//////        if (!All.Contains(this)) All.Add(this);
//////    }

//////    private void OnDisable()
//////    {
//////        All.Remove(this);
//////        if (CellMap.TryGetValue(cell, out var n) && n == this)
//////            CellMap.Remove(cell);
//////    }

//////    public void RegisterCell(float cellSize)
//////    {
//////        cell = WorldToCell(transform.position, cellSize);
//////        CellMap[cell] = this;
//////    }

//////    public static Vector2Int WorldToCell(Vector3 pos, float cellSize)
//////    {
//////        int x = Mathf.RoundToInt(pos.x / cellSize);
//////        int z = Mathf.RoundToInt(pos.z / cellSize);
//////        return new Vector2Int(x, z);
//////    }

//////    public static Vector3 CellToWorld(Vector2Int c, float cellSize, float y = 0f)
//////    {
//////        return new Vector3(c.x * cellSize, y, c.y * cellSize);
//////    }

//////    public void LinkWith(FrontierNode other)
//////    {
//////        if (other == null || other == this) return;
//////        if (!links.Contains(other)) links.Add(other);
//////        if (!other.links.Contains(this)) other.links.Add(this);
//////    }

//////    public void UnlinkWith(FrontierNode other)
//////    {
//////        if (other == null) return;
//////        links.Remove(other);
//////        other.links.Remove(this);
//////    }

//////    public bool IsBlockedIndex(int i) => (blockedMask & (1 << i)) != 0;

//////    public void MarkBlockedIndex(int i) => blockedMask |= (1 << i);

//////    private void OnDrawGizmos()
//////    {
//////        if (!drawLinks) return;
//////        if (links == null) return;

//////        Vector3 a = transform.position + Vector3.up * drawHeight;

//////        foreach (var bNode in links)
//////        {
//////            if (bNode == null) continue;

//////            Vector3 b = bNode.transform.position + Vector3.up * drawHeight;
//////            Vector3 delta = b - a;
//////            float dist = delta.magnitude;
//////            if (dist <= 0.0001f) continue;

//////            Vector3 dir = delta / dist;

//////            // デフォ色：緑（通っている）
//////            Color c = Color.green;

//////            // Raycast で壁に当たるなら赤
//////            if (colorByWallHit && debugWallLayer.value != 0)
//////            {
//////                if (Physics.Raycast(a, dir, dist, debugWallLayer, QueryTriggerInteraction.Ignore))
//////                    c = Color.red;
//////            }

//////            Gizmos.color = c;
//////            Gizmos.DrawLine(a, b);
//////            Gizmos.DrawSphere(b, endSphereRadius);
//////        }
//////    }
//////}

////using System.Collections.Generic;
////using UnityEngine;

////public class FrontierNode : MonoBehaviour
////{
////    public static readonly List<FrontierNode> All = new();
////    public static readonly Dictionary<Vector2Int, FrontierNode> CellMap = new();

////    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
////    private static void ResetStatics()
////    {
////        All.Clear();
////        CellMap.Clear();
////    }

////    [Header("Graph (visited cells only)")]
////    public List<FrontierNode> links = new();

////    [Header("State (computed by Explorer)")]
////    public int unknownCount;
////    public int wallCount;

////    [HideInInspector] public Vector2Int cell;

////    // bit: 0=F, 1=B, 2=L, 3=R
////    [SerializeField] private int blockedMask = 0;

////    // =========================
////    // Debug Draw Links (Raycast + Gizmos)
////    // =========================
////    [Header("Debug Draw Links")]
////    public bool drawLinks = true;
////    public float drawHeight = 0.2f;
////    public float endSphereRadius = 0.07f;

////    [Tooltip("Explorer側から wallLayer を流し込みます（未設定なら手動で入れてOK）")]
////    public LayerMask debugWallLayer;

////    [Tooltip("Raycastで壁に当たったら赤、当たらなければ緑")]
////    public bool colorByWallHit = true;

////    private void OnEnable()
////    {
////        if (!All.Contains(this)) All.Add(this);
////    }

////    private void OnDisable()
////    {
////        All.Remove(this);
////        if (CellMap.TryGetValue(cell, out var n) && n == this)
////            CellMap.Remove(cell);
////    }

////    public void RegisterCell(float cellSize)
////    {
////        cell = WorldToCell(transform.position, cellSize);
////        CellMap[cell] = this;
////    }

////    public static Vector2Int WorldToCell(Vector3 pos, float cellSize)
////    {
////        int x = Mathf.RoundToInt(pos.x / cellSize);
////        int z = Mathf.RoundToInt(pos.z / cellSize);
////        return new Vector2Int(x, z);
////    }

////    public static Vector3 CellToWorld(Vector2Int c, float cellSize, float y = 0f)
////    {
////        return new Vector3(c.x * cellSize, y, c.y * cellSize);
////    }

////    public void LinkWith(FrontierNode other)
////    {
////        if (other == null || other == this) return;
////        if (!links.Contains(other)) links.Add(other);
////        if (!other.links.Contains(this)) other.links.Add(this);
////    }

////    public void UnlinkWith(FrontierNode other)
////    {
////        if (other == null) return;
////        links.Remove(other);
////        other.links.Remove(this);
////    }

////    public bool IsBlockedIndex(int i) => (blockedMask & (1 << i)) != 0;

////    public void MarkBlockedIndex(int i) => blockedMask |= (1 << i);

////    // ★追加：blocked解除（「通った方向はblockedにしない」ため）
////    public void ClearBlockedIndex(int i) => blockedMask &= ~(1 << i);

////    private void OnDrawGizmos()
////    {
////        if (!drawLinks) return;
////        if (links == null) return;

////        Vector3 a = transform.position + Vector3.up * drawHeight;

////        foreach (var bNode in links)
////        {
////            if (bNode == null) continue;

////            Vector3 b = bNode.transform.position + Vector3.up * drawHeight;
////            Vector3 delta = b - a;
////            float dist = delta.magnitude;
////            if (dist <= 0.0001f) continue;

////            Vector3 dir = delta / dist;

////            Color c = Color.green;

////            if (colorByWallHit && debugWallLayer.value != 0)
////            {
////                if (Physics.Raycast(a, dir, dist, debugWallLayer, QueryTriggerInteraction.Ignore))
////                    c = Color.red;
////            }

////            Gizmos.color = c;
////            Gizmos.DrawLine(a, b);
////            Gizmos.DrawSphere(b, endSphereRadius);
////        }
////    }
////}

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
//        totalPassCount = 0;
//        colorDirtyThisRun = false;
//    }

//    [Header("Graph (links = traversed edges only)")]
//    public List<FrontierNode> links = new();

//    [Header("State (computed by Explorer)")]
//    public int unknownCount;
//    public int wallCount;

//    [Header("Evaluation")]
//    public int passCount;
//    public static int totalPassCount = 0;

//    [Header("Color")]
//    [SerializeField] private bool enableColorChange = true;
//    private Renderer _renderer;
//    private static bool colorDirtyThisRun = false;

//    [HideInInspector] public Vector2Int cell;

//    // bit: 0=F, 1=B, 2=L, 3=R
//    [SerializeField] private int blockedMask = 0;

//    private void Awake()
//    {
//        _renderer = GetComponent<Renderer>();
//        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
//    }

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
//    public void ClearBlockedIndex(int i) => blockedMask &= ~(1 << i);

//    public void OnPassed()
//    {
//        passCount++;
//        totalPassCount++;
//        colorDirtyThisRun = true;
//    }

//    // MapNode と同じ「白→赤」方式で、Run末尾に一斉反映
//    public static void ApplyColorsOnceBeforeScreenshot()
//    {
//        if (!colorDirtyThisRun) return;

//        int maxPass = 1;
//        foreach (var n in All)
//        {
//            if (n == null) continue;
//            if (n.passCount > maxPass) maxPass = n.passCount;
//        }

//        foreach (var n in All)
//        {
//            if (n == null || !n.enableColorChange || n._renderer == null) continue;

//            float t = Mathf.Clamp01((float)n.passCount / maxPass);
//            int s = Mathf.Clamp(Mathf.RoundToInt(t * 510f), 0, 510);

//            int gInt = 255 - Mathf.Min(s, 255);
//            int bInt = 255 - Mathf.Max(s - 255, 0);

//            byte g = (byte)Mathf.Clamp(gInt, 0, 255);
//            byte b = (byte)Mathf.Clamp(bInt, 0, 255);

//            n._renderer.material.color = new Color32(255, g, b, 255);
//        }

//        colorDirtyThisRun = false;
//    }

//    public static void ClearAllNodes()
//    {
//        foreach (var n in new List<FrontierNode>(All))
//        {
//            if (n != null) Object.Destroy(n.gameObject);
//        }
//        All.Clear();
//        CellMap.Clear();
//        totalPassCount = 0;
//        colorDirtyThisRun = false;
//    }
//}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Frontier 用 Node
/// - cell: Grid 上のセル座標
/// - links: Player が実際に通過して確定したリンクのみ（自動リンクしない）
/// - blockedMask: 「壁セル/辺ブロック」で確定した方向（誤検知があれば Explorer 側で復活する）
/// - unknownCount / wallCount: Explorer が毎フレーム計算して更新
/// - passCount: Node を通過した回数（全体合計は totalPassCount）
/// </summary>
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
