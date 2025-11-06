using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    // ============================================================
    // 静的共有データ
    // ============================================================
    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
    public static List<MapNode> allNodes = new List<MapNode>();

    [Header("基本情報")]
    public Vector2Int cell;
    public List<MapNode> links = new List<MapNode>();

    [Header("学習パラメータ")]
    public float value = 0f;
    public int visits = 0;
    public int DistanceFromGoal = int.MaxValue;

    [Range(0f, 1f)] public float alpha = 0.3f;  // 学習率
    [Range(0f, 1f)] public float gamma = 0.9f;  // 割引率
    [Range(0f, 1f)] public float rho = 0.02f;   // 蒸発率
    [Range(0f, 1f)] public float beta = 0.5f;   // 未探索ボーナス

    [Header("グリッド設定")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Header("動作設定")]
    public bool debugLog = false;

    // ============================================================
    // プロパティ：未知数（未接続方向の数）
    // ============================================================
    public int UnknownCount
    {
        get
        {
            int max = 4; // 上下左右
            return Mathf.Clamp(max - links.Count, 0, 4);
        }
    }

    // ============================================================
    // 起動時登録
    // ============================================================
    private void Awake()
    {
        cell = WorldToCell(transform.position);
        if (!allNodeCells.Contains(cell))
            allNodeCells.Add(cell);

        if (!allNodes.Contains(this))
            allNodes.Add(this);
    }

    // ============================================================
    // リンク追加（双方向）
    // ============================================================
    public void AddLink(MapNode other)
    {
        if (other == null || other == this) return;

        if (!links.Contains(other))
            links.Add(other);

        if (!other.links.Contains(this))
            other.links.Add(this);

        if (debugLog)
            Debug.Log($"[MapNode] Linked: {name} ↔ {other.name}");
    }

    // ============================================================
    // Value初期化（Goal距離ベース）
    // ============================================================
    public void InitializeValue(Vector3 goalPos)
    {
        float dist = Vector3.Distance(transform.position, goalPos);
        value = 1f / (dist + 1f);
    }

    // ============================================================
    // Value更新（報酬伝播＋未知補正＋蒸発）
    // ============================================================
    public void UpdateValue(MapNode goal)
    {
        if (goal == null) return;

        float dist = Vector3.Distance(transform.position, goal.transform.position);
        float reward = 1f / (dist + 1f);
        float neighborMax = (links.Count > 0) ? links.Max(n => n.value) : 0f;

        value = (1 - rho) * value
              + alpha * (reward + gamma * neighborMax - value)
              + beta / (visits + 1);

        visits++;
    }

    // ============================================================
    // 静的関数：セル検索
    // ============================================================
    public static MapNode FindByCell(Vector2Int cell)
    {
        return allNodes.FirstOrDefault(n => n.cell == cell);
    }

    // ============================================================
    // 静的関数：位置に最も近いNodeを取得
    // ============================================================
    public static MapNode FindNearest(Vector3 position)
    {
        if (allNodes.Count == 0) return null;
        return allNodes.OrderBy(n => Vector3.Distance(n.transform.position, position)).FirstOrDefault();
    }

    // ============================================================
    // グリッド変換
    // ============================================================
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        int cx = Mathf.RoundToInt(p.x / cellSize);
        int cz = Mathf.RoundToInt(p.z / cellSize);
        return new Vector2Int(cx, cz);
    }

    // ============================================================
    // Gizmos可視化
    // ============================================================
    private void OnDrawGizmos()
    {
        float intensity = Mathf.Clamp01(value);
        Gizmos.color = Color.Lerp(Color.blue, Color.red, intensity);
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

        Gizmos.color = Color.yellow;
        foreach (var node in links)
        {
            if (node != null)
                Gizmos.DrawLine(transform.position, node.transform.position);
        }

        // 未接続方向数の可視化
        Gizmos.color = Color.white;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, $"{UnknownCount}");
    }

    // ============================================================
    // クリーンアップ
    // ============================================================
    private void OnDestroy()
    {
        allNodeCells.Remove(cell);
        allNodes.Remove(this);
    }
}