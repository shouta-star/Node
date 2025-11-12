using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
    public static List<MapNode> allNodes = new List<MapNode>();

    [Header("基本情報")]
    public Vector2Int cell;
    public List<MapNode> links = new List<MapNode>();

    [Header("Goal関連情報")]
    public float DistanceFromGoal = Mathf.Infinity;
    public float value = 0f;

    [Header("探索状態")]
    public int unknownCount = 0;
    public int wallCount = 0;

    [Header("グリッド設定")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Header("デバッグ")]
    public bool debugLog = true;

    private void Awake()
    {
        cell = WorldToCell(transform.position);

        if (!allNodeCells.Contains(cell))
            allNodeCells.Add(cell);

        if (!allNodes.Contains(this))
            allNodes.Add(this);

        RecalculateUnknownAndWall();
    }

    public void UpdateValueByGoal(MapNode goal)
    {
        if (goal == null) return;
        float dist = Vector3.Distance(transform.position, goal.transform.position);
        value = -dist;
    }

    public void AddLink(MapNode other)
    {
        if (other == null || other == this) return;

        bool added = false;

        if (!links.Contains(other))
        {
            links.Add(other);
            added = true;
        }

        if (!other.links.Contains(this))
        {
            other.links.Add(this);
            added = true;
        }

        if (debugLog && added)
            Debug.Log($"[MapNode] Linked: {name} ↔ {other.name}");

        // 双方再計算（リンク確定後）
        RecalculateUnknownAndWall();
        other.RecalculateUnknownAndWall();
    }

    //    public void RecalculateUnknownAndWall()
    //    {
    //        int oldUnknown = unknownCount;
    //        int oldWall = wallCount;

    //        Debug.Log($"[MapNode] Recalc START name={name}, before U={unknownCount}, W={wallCount}, linkCount={links.Count}");

    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
    //        int newWall = 0;
    //        int newUnknown = 0;

    //        foreach (var dir in dirs)
    //        {
    //            Vector3 origin = transform.position + Vector3.up * 0.1f;
    //            string dirName = dir == Vector3.forward ? "F" :
    //                             dir == Vector3.back ? "B" :
    //                             dir == Vector3.left ? "L" : "R";

    //            Vector3 nextPos = transform.position + dir * cellSize;
    //            Vector2Int nextCell = WorldToCell(nextPos);
    //            MapNode neighbor = FindByCell(nextCell);

    //            // 🔹既にリンク済みならスキップ（U/W変化に影響させない）
    //            if (neighbor != null && links.Contains(neighbor))
    //            {
    //                Debug.Log($"[MapNode] {name} dir={dirName}: Linked to {neighbor.name} (existing link)");
    //                continue;
    //            }

    //            // ノードがあるが未リンクなら未知扱い
    //            if (neighbor != null && !links.Contains(neighbor))
    //            {
    //                Debug.Log($"[MapNode] {name} dir={dirName}: Found neighbor (unlinked)");
    //                newUnknown++;
    //                continue;
    //            }

    //            // 壁チェック
    //            bool wallHit = Physics.Raycast(origin, dir, cellSize, LayerMask.GetMask("Wall"));
    //            if (wallHit)
    //            {
    //                Debug.Log($"[MapNode] {name} dir={dirName}: HIT Wall");
    //                newWall++;
    //                continue;
    //            }

    //            // 未知領域
    //            Debug.Log($"[MapNode] {name} dir={dirName}: Unknown (no node)");
    //            newUnknown++;
    //        }

    //        wallCount = newWall;
    //        unknownCount = newUnknown;

    //        if (debugLog && (oldUnknown != newUnknown || oldWall != newWall))
    //            Debug.Log($"[MapNode][U/W CHANGED] {name}  U: {oldUnknown} -> {newUnknown},  W: {oldWall} -> {newWall}");

    //        Debug.Log($"[MapNode] Recalc END name={name}, after U={unknownCount}, W={wallCount}, linkCount={links.Count}");

    //#if UNITY_EDITOR
    //        UnityEditor.SceneView.RepaintAll();
    //#endif
    //    }
    // ======================================================
    // Linkベースでの未知数・壁数再計算
    // ======================================================
    public void RecalculateUnknownAndWall()
    {
        //Debug.Log($"[MapNode] Recalc START name={name}, before U={unknownCount}, W={wallCount}, linkCount={links.Count}");

        int prevU = unknownCount;
        int prevW = wallCount;
        unknownCount = 0;
        wallCount = 0;

        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        string[] dirNames = { "F", "B", "L", "R" };

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 dir = dirs[i];
            string dirName = dirNames[i];

            bool linkedInDir = false;

            // Linkから方向判定（角度許容を緩くする）
            foreach (var link in links)
            {
                Vector3 delta = (link.transform.position - transform.position).normalized;
                float dot = Vector3.Dot(delta, dir);
                if (dot > 0.7f)  // ← 0.9 → 0.7 に緩和
                {
                    linkedInDir = true;
                    if (debugLog)
                        Debug.Log($"[MapNode] {name} dir={dirName}: Linked with {link.name} (dot={dot:F2})");
                    break;
                }
            }

            if (linkedInDir)
                continue; // 既知方向（リンク済み）

            // リンクなし方向 → 壁 or Unknown
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, cellSize, LayerMask.GetMask("Wall")))
            {
                wallCount++;
                if (debugLog)
                    Debug.Log($"[MapNode] {name} dir={dirName}: HIT Wall");
            }
            else
            {
                unknownCount++;
                if (debugLog)
                    Debug.Log($"[MapNode] {name} dir={dirName}: Unknown (no link)");
            }
        }

        //if (prevU != unknownCount || prevW != wallCount)
            //Debug.Log($"[MapNode][U/W CHANGED] {name}  U: {prevU} -> {unknownCount},  W: {prevW} -> {wallCount}");

        //Debug.Log($"[MapNode] Recalc END name={name}, after U={unknownCount}, W={wallCount}, linkCount={links.Count}");
    }

    public float EdgeCost(MapNode other)
    {
        if (other == null) return Mathf.Infinity;
        return Vector3.Distance(transform.position, other.transform.position);
    }

    public static MapNode FindByCell(Vector2Int cell)
    {
        return allNodes.FirstOrDefault(n => n.cell == cell);
    }

    public static MapNode FindNearest(Vector3 pos)
    {
        if (allNodes.Count == 0) return null;
        return allNodes.OrderBy(n => Vector3.Distance(n.transform.position, pos)).FirstOrDefault();
    }

    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        int cx = Mathf.RoundToInt(p.x / cellSize);
        int cz = Mathf.RoundToInt(p.z / cellSize);
        return new Vector2Int(cx, cz);
    }

    private void OnDrawGizmos()
    {
        float normalized = Mathf.Clamp01(1f - Mathf.Abs(value) / 20f);
        Gizmos.color = Color.Lerp(Color.blue, Color.red, normalized);
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

        Gizmos.color = Color.yellow;
        foreach (var node in links)
        {
            if (node != null)
                Gizmos.DrawLine(transform.position, node.transform.position);
        }

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.3f,
            $"V:{value:F2}\nU:{unknownCount} W:{wallCount}"
        );
#endif
    }

    private void OnDestroy()
    {
        allNodeCells.Remove(cell);
        allNodes.Remove(this);
    }
}

//using System.Collections.Generic;
//using System.Linq;
//using UnityEngine;

//public class MapNode : MonoBehaviour
//{
//    // ==============================
//    // 静的情報（全ノード共有）
//    // ==============================
//    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
//    public static List<MapNode> allNodes = new List<MapNode>();

//    // ==============================
//    // ノード情報
//    // ==============================
//    [Header("基本情報")]
//    public Vector2Int cell;                  // グリッド上の座標
//    public List<MapNode> links = new List<MapNode>(); // 隣接ノードとのリンク

//    [Header("Goal関連情報")]
//    public float DistanceFromGoal = Mathf.Infinity; // Dijkstra再計算用
//    public float value = 0f;                        // Goalまでの距離（負符号つき）

//    [Header("グリッド設定")]
//    public float cellSize = 1f;
//    public Vector3 gridOrigin = Vector3.zero;

//    [Header("デバッグ")]
//    public bool debugLog = false;

//    // ==============================
//    // 初期化
//    // ==============================
//    private void Awake()
//    {
//        cell = WorldToCell(transform.position);

//        if (!allNodeCells.Contains(cell))
//            allNodeCells.Add(cell);

//        if (!allNodes.Contains(this))
//            allNodes.Add(this);
//    }

//    // ==============================
//    // ValueをGoalとの距離（負符号つき）で更新
//    // ==============================
//    public void UpdateValueByGoal(MapNode goal)
//    {
//        if (goal == null) return;

//        float dist = Vector3.Distance(transform.position, goal.transform.position);
//        value = -dist; // 距離が短いほど高Value（=Goal方向を優先）
//    }

//    // ==============================
//    // ノードリンク形成
//    // ==============================
//    public void AddLink(MapNode other)
//    {
//        if (other == null || other == this) return;

//        if (!links.Contains(other)) links.Add(other);
//        if (!other.links.Contains(this)) other.links.Add(this);

//        if (debugLog) Debug.Log($"[MapNode] Linked: {name} ↔ {other.name}");
//    }

//    // ==============================
//    // ノード間距離
//    // ==============================
//    public float EdgeCost(MapNode other)
//    {
//        if (other == null) return Mathf.Infinity;
//        return Vector3.Distance(transform.position, other.transform.position);
//    }

//    // ==============================
//    // グローバル検索ユーティリティ
//    // ==============================
//    public static MapNode FindByCell(Vector2Int cell)
//    {
//        return allNodes.FirstOrDefault(n => n.cell == cell);
//    }

//    public static MapNode FindNearest(Vector3 pos)
//    {
//        if (allNodes.Count == 0) return null;
//        return allNodes.OrderBy(n => Vector3.Distance(n.transform.position, pos)).FirstOrDefault();
//    }

//    // ==============================
//    // グリッド座標変換
//    // ==============================
//    public Vector2Int WorldToCell(Vector3 worldPos)
//    {
//        Vector3 p = worldPos - gridOrigin;
//        int cx = Mathf.RoundToInt(p.x / cellSize);
//        int cz = Mathf.RoundToInt(p.z / cellSize);
//        return new Vector2Int(cx, cz);
//    }

//    // ==============================
//    // デバッグ描画
//    // ==============================
//    private void OnDrawGizmos()
//    {
//        // Value（距離の負符号）に基づいて色を可視化
//        // Goalに近いほどValueが高い→赤、遠いほど青
//        float normalized = Mathf.Clamp01(1f - Mathf.Abs(value) / 20f); // 距離20を基準
//        Gizmos.color = Color.Lerp(Color.blue, Color.red, normalized);
//        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

//        // リンク表示
//        Gizmos.color = Color.yellow;
//        foreach (var node in links)
//        {
//            if (node != null)
//                Gizmos.DrawLine(transform.position, node.transform.position);
//        }

//#if UNITY_EDITOR
//        // デバッグラベルにValue表示
//        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, $"{value:F2}");
//#endif
//    }

//    // ==============================
//    // 削除時クリーンアップ
//    // ==============================
//    private void OnDestroy()
//    {
//        allNodeCells.Remove(cell);
//        allNodes.Remove(this);
//    }
//}

////using System.Collections.Generic;
////using System.Linq;
////using UnityEngine;

////public class MapNode : MonoBehaviour
////{
////    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
////    public static List<MapNode> allNodes = new List<MapNode>();

////    [Header("基本情報")]
////    public Vector2Int cell;
////    public List<MapNode> links = new List<MapNode>();

////    [Header("Goalからの距離")]
////    // ★修正：int → float に変更（実距離格納用）
////    public float DistanceFromGoal = Mathf.Infinity;

////    [Header("学習パラメータ")]
////    public float value = 0f;
////    public int visits = 0;

////    [Range(0f, 1f)] public float alpha = 0.3f;
////    [Range(0f, 1f)] public float gamma = 0.9f;
////    [Range(0f, 1f)] public float rho = 0.02f;
////    [Range(0f, 1f)] public float beta = 0.5f;

////    [Header("グリッド設定")]
////    public float cellSize = 1f;
////    public Vector3 gridOrigin = Vector3.zero;

////    [Header("動作設定")]
////    public bool debugLog = false;

////    public int UnknownCount
////    {
////        get
////        {
////            int max = 4;
////            return Mathf.Clamp(max - links.Count, 0, 4);
////        }
////    }

////    private void Awake()
////    {
////        cell = WorldToCell(transform.position);
////        if (!allNodeCells.Contains(cell))
////            allNodeCells.Add(cell);

////        if (!allNodes.Contains(this))
////            allNodes.Add(this);
////    }

////    public void AddLink(MapNode other)
////    {
////        if (other == null || other == this) return;

////        if (!links.Contains(other))
////            links.Add(other);

////        if (!other.links.Contains(this))
////            other.links.Add(this);

////        if (debugLog)
////            Debug.Log($"[MapNode] Linked: {name} ↔ {other.name}");
////    }

////    // ★追加：実際のノード間距離を返す
////    public float EdgeCost(MapNode other)
////    {
////        if (other == null) return Mathf.Infinity;
////        return Vector3.Distance(transform.position, other.transform.position);
////    }

////    public void InitializeValue(Vector3 goalPos)
////    {
////        float dist = Vector3.Distance(transform.position, goalPos);
////        value = 1f / (dist + 1f);
////    }

////    public void UpdateValue(MapNode goal)
////    {
////        if (goal == null) return;

////        float dist = Vector3.Distance(transform.position, goal.transform.position);
////        float reward = 1f / (dist + 1f);
////        float neighborMax = (links.Count > 0) ? links.Max(n => n.value) : 0f;

////        value = (1 - rho) * value
////              + alpha * (reward + gamma * neighborMax - value)
////              + beta / (visits + 1);

////        visits++;
////    }

////    public static MapNode FindByCell(Vector2Int cell)
////    {
////        return allNodes.FirstOrDefault(n => n.cell == cell);
////    }

////    public static MapNode FindNearest(Vector3 position)
////    {
////        if (allNodes.Count == 0) return null;
////        return allNodes.OrderBy(n => Vector3.Distance(n.transform.position, position)).FirstOrDefault();
////    }

////    public Vector2Int WorldToCell(Vector3 worldPos)
////    {
////        Vector3 p = worldPos - gridOrigin;
////        int cx = Mathf.RoundToInt(p.x / cellSize);
////        int cz = Mathf.RoundToInt(p.z / cellSize);
////        return new Vector2Int(cx, cz);
////    }

////    private void OnDrawGizmos()
////    {
////        float intensity = Mathf.Clamp01(value);
////        Gizmos.color = Color.Lerp(Color.blue, Color.red, intensity);
////        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

////        Gizmos.color = Color.yellow;
////        foreach (var node in links)
////        {
////            if (node != null)
////                Gizmos.DrawLine(transform.position, node.transform.position);
////        }

////#if UNITY_EDITOR
////        Gizmos.color = Color.white;
////        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, $"{UnknownCount}");
////#endif
////    }

////    private void OnDestroy()
////    {
////        allNodeCells.Remove(cell);
////        allNodes.Remove(this);
////    }
////}