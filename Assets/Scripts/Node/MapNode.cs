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

    [Header("最短距離（Startから）")]
    public int distanceFromStart = 0;
    public static MapNode StartNode; // ★ 最初に作られたNodeをStart地点として保持

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

        //// ★ StartNode判定 ----------------------
        //if (StartNode == null)
        //{
        //    // 最初に生成されたNodeをStartとして扱う
        //    StartNode = this;
        //    distanceFromStart = 0;
        //}
        //// ---------------------------------------------------

        //Debug.Log($"[StartNode Debug] cell={cell}, pos={transform.position}, links={links.Count}");

        RecalculateUnknownAndWall();
    }

    // ======================================================
    // ★ Startからの距離を更新（小さい値のみ受け付ける）
    // ======================================================
    public void UpdateDistanceFromStart(int newDist)
    {
        if (newDist < distanceFromStart)
            distanceFromStart = newDist;
    }

    // ======================================================
    // ★ 全ノードの距離をリセット + startノードを0にする
    // ======================================================
    public static void ResetAllDistanceFromStart(MapNode start)
    {
        foreach (var n in allNodes)
            n.distanceFromStart = int.MaxValue;

        if (start != null)
            start.distanceFromStart = 0;
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
            Debug.Log($"[AddLink] Add {name} → {other.name}");
        }

        if (!other.links.Contains(this))
        {
            other.links.Add(this);
            added = true;
            Debug.Log($"[AddLink] Add {name} → {other.name}");
        }

        if (debugLog && added)
            Debug.Log($"[MapNode] Linked: {name} ↔ {other.name}");

        // 双方再計算（リンク確定後）
        RecalculateUnknownAndWall();
        other.RecalculateUnknownAndWall();

        // ★ Startからの距離も更新（Goalと同じDijkstra方式）
        RecalculateStartDistance();
    }

    // ======================================================
    // StartNode を起点に距離(distanceFromStart)を再計算
    // DistanceFromGoal と同じ Dijkstra 法
    // ======================================================
    //public static void RecalculateStartDistance()
    //{
    //    if (StartNode == null) return;

    //    // 全ノードの距離を初期化
    //    foreach (var n in allNodes)
    //        n.distanceFromStart = 0;

    //    // Startノードの距離を0に
    //    StartNode.distanceFromStart = 0;

    //    Queue<MapNode> q = new Queue<MapNode>();
    //    q.Enqueue(StartNode);

    //    // BFSで最短歩数を計算（1リンク = 1歩）
    //    while (q.Count > 0)
    //    {
    //        var node = q.Dequeue();

    //        Debug.Log("AAA");

    //        foreach (var link in node.links)
    //        {
    //            Debug.Log("BBB");
    //            if (link == null) continue;
    //            Debug.Log("CCC");
    //            int newDist = node.distanceFromStart + 1; // ★ float → int に変更

    //            if (newDist < link.distanceFromStart)
    //            {
    //                link.distanceFromStart = newDist;
    //                q.Enqueue(link);
    //            }
    //        }
    //    }
    //}
    //public static void RecalculateStartDistance()
    //{
    //    if (StartNode == null) return;

    //    // ★ 全ノードの距離を「未訪問の最大値」に初期化
    //    foreach (var n in allNodes)
    //        n.distanceFromStart = int.MaxValue;

    //    // ★ Startノードの距離を 0 に
    //    StartNode.distanceFromStart = 0;

    //    Queue<MapNode> q = new Queue<MapNode>();
    //    q.Enqueue(StartNode);

    //    // BFSで最短歩数を計算（1リンク = 1歩）
    //    while (q.Count > 0)
    //    {
    //        var node = q.Dequeue();

    //        Debug.Log("AAA");

    //        foreach (var link in node.links)
    //        {
    //            Debug.Log("BBB");
    //            if (link == null) continue;
    //            Debug.Log("CCC");

    //            int newDist = node.distanceFromStart + 1;

    //            // ★ここが成立するようになる
    //            if (newDist < link.distanceFromStart)
    //            {
    //                link.distanceFromStart = newDist;
    //                q.Enqueue(link);
    //            }
    //        }
    //    }
    //}
    //public static void RecalculateStartDistance()
    //{
    //    if (StartNode == null) return;

    //    // StartNode のセル座標を取得
    //    Vector2Int startCell = StartNode.cell;

    //    // ★ 全ノードについてセル座標から距離を計算（マンハッタン距離）
    //    foreach (var node in allNodes)
    //    {
    //        int dx = Mathf.Abs(node.cell.x - startCell.x);
    //        int dz = Mathf.Abs(node.cell.y - startCell.y);
    //        node.distanceFromStart = dx + dz;
    //    }
    //}
    //public static void RecalculateStartDistance()
    //{
    //    if (StartNode == null) return;

    //    // 全ノードの距離を最大値(未訪問)に
    //    foreach (var n in allNodes)
    //        n.distanceFromStart = int.MaxValue;

    //    // Start は 0
    //    StartNode.distanceFromStart = 0;

    //    Queue<MapNode> q = new Queue<MapNode>();
    //    q.Enqueue(StartNode);

    //    while (q.Count > 0)
    //    {
    //        var node = q.Dequeue();

    //        foreach (var link in node.links)
    //        {
    //            if (link == null) continue;

    //            int newDist = node.distanceFromStart + 1;

    //            // 短縮されたときのみ更新
    //            if (newDist < link.distanceFromStart)
    //            {
    //                link.distanceFromStart = newDist;
    //                q.Enqueue(link);
    //            }
    //        }
    //    }
    //}
    public static void RecalculateStartDistance()
    {
        if (StartNode == null) return;

        // 全ノードの距離を未訪問に
        foreach (var n in allNodes)
            n.distanceFromStart = int.MaxValue;

        // StartNode は 0
        StartNode.distanceFromStart = 0;

        // 優先度キュー（最短距離の Node を優先して取り出す）
        var pq = new List<MapNode>();
        pq.Add(StartNode);

        while (pq.Count > 0)
        {
            // ★ 最小距離の Node を取得
            pq = pq.OrderBy(n => n.distanceFromStart).ToList();
            MapNode node = pq[0];
            pq.RemoveAt(0);

            foreach (var link in node.links)
            {
                if (link == null) continue;

                // ★ マス数（cell距離）をコストとする
                int dx = Mathf.Abs(link.cell.x - node.cell.x);
                int dz = Mathf.Abs(link.cell.y - node.cell.y);
                int edgeCost = dx + dz;  // これが「マス数」

                int newDist = node.distanceFromStart + edgeCost;

                if (newDist < link.distanceFromStart)
                {
                    link.distanceFromStart = newDist;
                    pq.Add(link);
                }
            }
        }
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
        if (this == StartNode)
        {
            Debug.LogError("[ERROR] StartNode が Destroy されました。StartNode は絶対に破棄してはいけません。");
            return;
        }

        allNodeCells.Remove(cell);
        allNodes.Remove(this);
    }
}