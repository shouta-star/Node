using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    // ==============================
    // 静的情報（全ノード共有）
    // ==============================
    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
    public static List<MapNode> allNodes = new List<MapNode>();

    // ==============================
    // ノード情報
    // ==============================
    [Header("基本情報")]
    public Vector2Int cell;                  // グリッド上の座標
    public List<MapNode> links = new List<MapNode>(); // 隣接ノードとのリンク

    [Header("Goal関連情報")]
    public float DistanceFromGoal = Mathf.Infinity; // Dijkstra再計算用
    public float value = 0f;                        // Goalまでの距離（負符号つき）

    [Header("グリッド設定")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Header("デバッグ")]
    public bool debugLog = false;

    // ==============================
    // 初期化
    // ==============================
    private void Awake()
    {
        cell = WorldToCell(transform.position);

        if (!allNodeCells.Contains(cell))
            allNodeCells.Add(cell);

        if (!allNodes.Contains(this))
            allNodes.Add(this);
    }

    // ==============================
    // ValueをGoalとの距離（負符号つき）で更新
    // ==============================
    public void UpdateValueByGoal(MapNode goal)
    {
        if (goal == null) return;

        float dist = Vector3.Distance(transform.position, goal.transform.position);
        value = -dist; // 距離が短いほど高Value（=Goal方向を優先）
    }

    // ==============================
    // ノードリンク形成
    // ==============================
    public void AddLink(MapNode other)
    {
        if (other == null || other == this) return;

        if (!links.Contains(other)) links.Add(other);
        if (!other.links.Contains(this)) other.links.Add(this);

        if (debugLog) Debug.Log($"[MapNode] Linked: {name} ↔ {other.name}");
    }

    // ==============================
    // ノード間距離
    // ==============================
    public float EdgeCost(MapNode other)
    {
        if (other == null) return Mathf.Infinity;
        return Vector3.Distance(transform.position, other.transform.position);
    }

    // ==============================
    // グローバル検索ユーティリティ
    // ==============================
    public static MapNode FindByCell(Vector2Int cell)
    {
        return allNodes.FirstOrDefault(n => n.cell == cell);
    }

    public static MapNode FindNearest(Vector3 pos)
    {
        if (allNodes.Count == 0) return null;
        return allNodes.OrderBy(n => Vector3.Distance(n.transform.position, pos)).FirstOrDefault();
    }

    // ==============================
    // グリッド座標変換
    // ==============================
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        int cx = Mathf.RoundToInt(p.x / cellSize);
        int cz = Mathf.RoundToInt(p.z / cellSize);
        return new Vector2Int(cx, cz);
    }

    // ==============================
    // デバッグ描画
    // ==============================
    private void OnDrawGizmos()
    {
        // Value（距離の負符号）に基づいて色を可視化
        // Goalに近いほどValueが高い→赤、遠いほど青
        float normalized = Mathf.Clamp01(1f - Mathf.Abs(value) / 20f); // 距離20を基準
        Gizmos.color = Color.Lerp(Color.blue, Color.red, normalized);
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

        // リンク表示
        Gizmos.color = Color.yellow;
        foreach (var node in links)
        {
            if (node != null)
                Gizmos.DrawLine(transform.position, node.transform.position);
        }

#if UNITY_EDITOR
        // デバッグラベルにValue表示
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, $"{value:F2}");
#endif
    }

    // ==============================
    // 削除時クリーンアップ
    // ==============================
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
//    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
//    public static List<MapNode> allNodes = new List<MapNode>();

//    [Header("基本情報")]
//    public Vector2Int cell;
//    public List<MapNode> links = new List<MapNode>();

//    [Header("Goalからの距離")]
//    // ★修正：int → float に変更（実距離格納用）
//    public float DistanceFromGoal = Mathf.Infinity;

//    [Header("学習パラメータ")]
//    public float value = 0f;
//    public int visits = 0;

//    [Range(0f, 1f)] public float alpha = 0.3f;
//    [Range(0f, 1f)] public float gamma = 0.9f;
//    [Range(0f, 1f)] public float rho = 0.02f;
//    [Range(0f, 1f)] public float beta = 0.5f;

//    [Header("グリッド設定")]
//    public float cellSize = 1f;
//    public Vector3 gridOrigin = Vector3.zero;

//    [Header("動作設定")]
//    public bool debugLog = false;

//    public int UnknownCount
//    {
//        get
//        {
//            int max = 4;
//            return Mathf.Clamp(max - links.Count, 0, 4);
//        }
//    }

//    private void Awake()
//    {
//        cell = WorldToCell(transform.position);
//        if (!allNodeCells.Contains(cell))
//            allNodeCells.Add(cell);

//        if (!allNodes.Contains(this))
//            allNodes.Add(this);
//    }

//    public void AddLink(MapNode other)
//    {
//        if (other == null || other == this) return;

//        if (!links.Contains(other))
//            links.Add(other);

//        if (!other.links.Contains(this))
//            other.links.Add(this);

//        if (debugLog)
//            Debug.Log($"[MapNode] Linked: {name} ↔ {other.name}");
//    }

//    // ★追加：実際のノード間距離を返す
//    public float EdgeCost(MapNode other)
//    {
//        if (other == null) return Mathf.Infinity;
//        return Vector3.Distance(transform.position, other.transform.position);
//    }

//    public void InitializeValue(Vector3 goalPos)
//    {
//        float dist = Vector3.Distance(transform.position, goalPos);
//        value = 1f / (dist + 1f);
//    }

//    public void UpdateValue(MapNode goal)
//    {
//        if (goal == null) return;

//        float dist = Vector3.Distance(transform.position, goal.transform.position);
//        float reward = 1f / (dist + 1f);
//        float neighborMax = (links.Count > 0) ? links.Max(n => n.value) : 0f;

//        value = (1 - rho) * value
//              + alpha * (reward + gamma * neighborMax - value)
//              + beta / (visits + 1);

//        visits++;
//    }

//    public static MapNode FindByCell(Vector2Int cell)
//    {
//        return allNodes.FirstOrDefault(n => n.cell == cell);
//    }

//    public static MapNode FindNearest(Vector3 position)
//    {
//        if (allNodes.Count == 0) return null;
//        return allNodes.OrderBy(n => Vector3.Distance(n.transform.position, position)).FirstOrDefault();
//    }

//    public Vector2Int WorldToCell(Vector3 worldPos)
//    {
//        Vector3 p = worldPos - gridOrigin;
//        int cx = Mathf.RoundToInt(p.x / cellSize);
//        int cz = Mathf.RoundToInt(p.z / cellSize);
//        return new Vector2Int(cx, cz);
//    }

//    private void OnDrawGizmos()
//    {
//        float intensity = Mathf.Clamp01(value);
//        Gizmos.color = Color.Lerp(Color.blue, Color.red, intensity);
//        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

//        Gizmos.color = Color.yellow;
//        foreach (var node in links)
//        {
//            if (node != null)
//                Gizmos.DrawLine(transform.position, node.transform.position);
//        }

//#if UNITY_EDITOR
//        Gizmos.color = Color.white;
//        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, $"{UnknownCount}");
//#endif
//    }

//    private void OnDestroy()
//    {
//        allNodeCells.Remove(cell);
//        allNodes.Remove(this);
//    }
//}