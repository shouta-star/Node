using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
    public static List<MapNode> allNodes = new List<MapNode>();

    private static int nodeCreateCount = 0;  // ★ 追加：生成カウンタ

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

    public int passCount = 0;

    [Header("見た目（通過で色変化）")]
    public float redStep;      // 1回通るごとに減らす量
    public Renderer _renderer;        // この Node の見た目用 Renderer

    [Tooltip("色を変えたい Node が使っているマテリアル（通常Node用）")]
    public Material colorChangeTargetMaterial;
    // この Node が「色変更対象かどうか」
    private bool _enableColorChange = true;

    [Header("グリッド設定")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Header("デバッグ")]
    public bool debugLog = true;

    private void Awake()
    {
        // ★ 追加：生成順で名前を設定
        nodeCreateCount++;
        this.name = "Node_" + nodeCreateCount;

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

        Debug.Log($"[DEBUG-STARTNODE] Awake(): StartNode={MapNode.StartNode?.name}");

        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            //_renderer.material.color = Color.white;
            // colorChangeTargetMaterial が未設定なら、全部の Node を色変更対象にする（今まで通り）
            if (colorChangeTargetMaterial == null)
            {
                // 各 Node ごとにマテリアルを複製して色を独立させる
                _renderer.material = Instantiate(_renderer.material);
                _renderer.material.color = Color.white;
                _enableColorChange = true;
            }
            // 指定されたマテリアルを使っている Node だけ色変更対象にする
            else if (_renderer.sharedMaterial == colorChangeTargetMaterial)
            {
                _renderer.material = Instantiate(_renderer.sharedMaterial);
                _renderer.material.color = Color.white;
                _enableColorChange = true;
            }
            else
            {
                // それ以外（＝Node_1 用マテリアルなど）は色を変えない
                _enableColorChange = false;
            }
        }
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
        //n.distanceFromStart = 0;

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
        if (other == null || other == this)
        {
            Debug.LogError($"[MN-AddLink ERROR] other == null");
            return;
        }

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
    // Goal を起点に DistanceFromGoal を全 Node に再計算
    // ======================================================
    public static void RecalculateGoalDistance(MapNode goal)
    {
        if (goal == null) return;

        // 全ノード初期化
        foreach (var n in allNodes)
            n.DistanceFromGoal = float.PositiveInfinity;

        goal.DistanceFromGoal = 0f;

        // BFS
        Queue<MapNode> q = new Queue<MapNode>();
        q.Enqueue(goal);

        while (q.Count > 0)
        {
            MapNode node = q.Dequeue();

            foreach (var link in node.links)
            {
                // コスト = セル距離（マンハッタン距離）
                int dx = Mathf.Abs(node.cell.x - link.cell.x);
                int dy = Mathf.Abs(node.cell.y - link.cell.y);
                float cost = dx + dy;

                float newDist = node.DistanceFromGoal + cost;

                if (newDist < link.DistanceFromGoal)
                {
                    link.DistanceFromGoal = newDist;
                    q.Enqueue(link);
                }
            }
        }
    }


    // ======================================================
    // StartNode を起点に距離(distanceFromStart)を再計算
    // DistanceFromGoal と同じ Dijkstra 法
    // ======================================================
    public static void RecalculateStartDistance()
    {
        if (StartNode == null) return;

        // 全ノード初期化
        foreach (var n in allNodes)
            n.distanceFromStart = int.MaxValue;

        StartNode.distanceFromStart = 0;

        Queue<MapNode> q = new Queue<MapNode>();
        q.Enqueue(StartNode);

        while (q.Count > 0)
        {
            var node = q.Dequeue();

            foreach (var link in node.links)
            {
                // ★ セル距離（マンハッタン距離）
                int dx = Mathf.Abs(node.cell.x - link.cell.x);
                int dy = Mathf.Abs(node.cell.y - link.cell.y);
                int cost = dx + dy;   // ← これが加算距離

                int newDist = node.distanceFromStart + cost;

                if (newDist < link.distanceFromStart)
                {
                    link.distanceFromStart = newDist;
                    q.Enqueue(link);
                }
            }
        }
    }

    // ======================================================
    // Linkベースでの未知数・壁数再計算
    // ======================================================
    //public void RecalculateUnknownAndWall()
    //{
    //    //Debug.Log($"[MapNode] Recalc START name={name}, before U={unknownCount}, W={wallCount}, linkCount={links.Count}");

    //    int prevU = unknownCount;
    //    int prevW = wallCount;
    //    unknownCount = 0;
    //    wallCount = 0;

    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
    //    string[] dirNames = { "F", "B", "L", "R" };

    //    for (int i = 0; i < dirs.Length; i++)
    //    {
    //        Vector3 dir = dirs[i];
    //        string dirName = dirNames[i];

    //        bool linkedInDir = false;

    //        // Linkから方向判定（角度許容を緩くする）
    //        foreach (var link in links)
    //        {
    //            Vector3 delta = (link.transform.position - transform.position).normalized;
    //            float dot = Vector3.Dot(delta, dir);
    //            if (dot > 0.95f)  // ← 0.9 → 0.7 に緩和
    //            {
    //                linkedInDir = true;
    //                //if (debugLog)
    //                    Debug.Log($"[MapNode] {name} dir={dirName}: Linked with {link.name} (dot={dot:F2})");
    //                break;
    //            }
    //        }

    //        if (linkedInDir)
    //            continue; // 既知方向（リンク済み）

    //        // リンクなし方向 → 壁 or Unknown
    //        if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, cellSize, LayerMask.GetMask("Wall")))
    //        //if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, cellSize, wallLayer))

    //        {
    //            wallCount++;
    //            //if (debugLog)
    //                Debug.Log($"[MapNode] {name} dir={dirName}: HIT Wall");
    //        }
    //        else
    //        {
    //            unknownCount++;
    //            //if (debugLog)
    //                Debug.Log($"[MapNode] {name} dir={dirName}: Unknown (no link)");
    //        }
    //    }

    //    //if (prevU != unknownCount || prevW != wallCount)
    //    //Debug.Log($"[MapNode][U/W CHANGED] {name}  U: {prevU} -> {unknownCount},  W: {prevW} -> {wallCount}");

    //    //Debug.Log($"[MapNode] Recalc END name={name}, after U={unknownCount}, W={wallCount}, linkCount={links.Count}");
    //}
    //public void RecalculateUnknownAndWall()
    //{
    //    int prevU = unknownCount;
    //    int prevW = wallCount;
    //    unknownCount = 0;
    //    wallCount = 0;

    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
    //    string[] dirNames = { "F", "B", "L", "R" };

    //    for (int i = 0; i < dirs.Length; i++)
    //    {
    //        Vector3 dir = dirs[i];
    //        string dirName = dirNames[i];

    //        bool linkedInDir = false;

    //        // ---- 方向にリンクしているか（角度チェック）----
    //        foreach (var link in links)
    //        {
    //            Vector3 delta = (link.transform.position - transform.position).normalized;
    //            float dot = Vector3.Dot(delta, dir);
    //            if (dot > 0.95f)
    //            {
    //                linkedInDir = true;
    //                Debug.Log($"[MapNode] {name} dir={dirName}: Linked with {link.name} (dot={dot:F2})");
    //                break;
    //            }
    //        }

    //        if (linkedInDir)
    //            continue; // 既にリンク済み方向は Unknown/WALL の対象外

    //        // ---- 壁チェック ----
    //        bool hitWall = Physics.Raycast(
    //            transform.position + Vector3.up * 0.1f,
    //            dir,
    //            cellSize,
    //            LayerMask.GetMask("Wall")
    //        );

    //        if (hitWall)
    //        {
    //            wallCount++;
    //            Debug.Log($"[MapNode] {name} dir={dirName}: HIT Wall");
    //            continue;
    //        }

    //        // ---- ★ Node がその方向に存在するかチェック ----
    //        bool hitNode = Physics.Raycast(
    //            transform.position + Vector3.up * 0.1f,
    //            dir,
    //            cellSize,
    //            LayerMask.GetMask("Node")
    //        );

    //        if (hitNode)
    //        {
    //            // 壁ではないし Node でもある → これは Unknown ではない
    //            // 未リンク方向だが「進めない Unknown 」なので無視
    //            Debug.Log($"[MapNode] {name} dir={dirName}: Found Node but not linked");
    //            continue;
    //        }

    //        // ---- ★ 本当に進める Unknown ----
    //        unknownCount++;
    //        Debug.Log($"[MapNode] {name} dir={dirName}: Valid Unknown");
    //    }
    //}
    public void RecalculateUnknownAndWall()
    {
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

            // ---- 方向にリンクしているか（角度チェック）----
            foreach (var link in links)
            {
                Vector3 delta = (link.transform.position - transform.position).normalized;
                float dot = Vector3.Dot(delta, dir);
                if (dot > 0.95f)
                {
                    linkedInDir = true;
                    Debug.Log($"[MapNode] {name} dir={dirName}: Linked with {link.name} (dot={dot:F2})");
                    break;
                }
            }

            if (linkedInDir)
                continue;

            // ---- 壁チェック（距離を少し伸ばす）----
            bool hitWall = Physics.Raycast(
                transform.position + Vector3.up * 0.05f,
                dir,
                cellSize * 1.2f,
                LayerMask.GetMask("Wall")
            );

            if (hitWall)
            {
                wallCount++;
                Debug.Log($"[MapNode] {name} dir={dirName}: HIT Wall");
                continue;
            }

            // ---- Node チェック（短めの距離で誤検知を防ぐ）----
            bool hitNode = Physics.Raycast(
                transform.position + Vector3.up * 0.05f,
                dir,
                cellSize * 0.6f,
                LayerMask.GetMask("Node")
            );

            //if (hitNode)
            //{
            //    Debug.Log($"[MapNode] {name} dir={dirName}: Found Node but not linked");
            //    continue;
            //}
            if (hitNode)
            {
                // ★ hit した Node を取得
                RaycastHit hitInfo;
                Physics.Raycast(
                    transform.position + Vector3.up * 0.05f,
                    dir,
                    out hitInfo,
                    cellSize * 0.6f,
                    LayerMask.GetMask("Node")
                );

                MapNode hitNodeObj = hitInfo.collider.GetComponent<MapNode>();

                // ★ GoalNode の場合だけ “未リンクなら Unknown 扱い”
                if (hitNodeObj != null && hitNodeObj.CompareTag("Goal"))
                {
                    bool linkedToGoal = links.Contains(hitNodeObj);

                    if (!linkedToGoal)
                    {
                        unknownCount++;
                        Debug.Log($"[MapNode] {name} dir={dirName}: Unknown (Goal not linked)");
                        continue; // ← Unknown 扱いして次へ
                    }
                }

                // ★ 通常の Node は Unknown にしない（現状のまま）
                Debug.Log($"[MapNode] {name} dir={dirName}: Found Node but not linked");
                continue;
            }


            // ---- 有効な Unknown ----
            unknownCount++;
            Debug.Log($"[MapNode] {name} dir={dirName}: Valid Unknown");
        }
    }


    public float EdgeCost(MapNode other)
    {
        if (other == null) return Mathf.Infinity;
        return Vector3.Distance(transform.position, other.transform.position);
    }

    //public static MapNode FindByCell(Vector2Int cell)
    //{
    //    return allNodes.FirstOrDefault(n => n.cell == cell);
    //}
    public static MapNode FindByCell(Vector2Int cell)
    {
        // LINQ撤廃：GC発生ゼロ
        for (int i = 0; i < allNodes.Count; i++)
        {
            if (allNodes[i].cell == cell)
                return allNodes[i];
        }
        return null;
    }

    public static MapNode FindNearest(Vector3 pos)
    {
        if (allNodes.Count == 0) return null;

        MapNode nearest = allNodes[0];
        float bestDist = (nearest.transform.position - pos).sqrMagnitude;

        // LINQ撤廃：手動で最小距離探索
        for (int i = 1; i < allNodes.Count; i++)
        {
            MapNode n = allNodes[i];
            float dist = (n.transform.position - pos).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                nearest = n;
            }
        }

        return nearest;
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

//#if UNITY_EDITOR
//        UnityEditor.Handles.Label(
//            transform.position + Vector3.up * 0.3f,
//            $"V:{value:F2}\nU:{unknownCount} W:{wallCount}"
//        );
//#endif
    }

    private void OnDestroy()
    {
        if (this == StartNode)
        {
            //Debug.LogError("[ERROR] StartNode が Destroy されました。StartNode は絶対に破棄してはいけません。");
            return;
        }

        allNodeCells.Remove(cell);
        allNodes.Remove(this);
    }

    // ======================================================
    // ★ Unknown 方向（リンクなし & 壁なし）を返す
    // ======================================================
    public Vector3? GetUnknownDirection()
    {
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in dirs)
        {
            // すでにリンク済み方向は除外
            bool linked = false;
            foreach (var link in links)
            {
                Vector3 delta = (link.transform.position - transform.position).normalized;
                if (Vector3.Dot(delta, dir) > 0.95f)
                {
                    linked = true;
                    break;
                }
            }
            if (linked) continue;

            // 壁方向は除外
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
                dir, cellSize, LayerMask.GetMask("Wall")))
                continue;

            // → Unknown 方向
            return dir;
        }

        // Unknown 無し
        return null;
    }

    // ======================================================
    // ★ Player がこの Node を通過したときに呼ぶ
    // ======================================================
    public void OnPassed()
    {
        passCount++;

        if (_renderer == null) return;

        // 今の色を取得
        Color c = _renderer.material.color;

        // R を 0.01 減らす（0 まで）
        //float newR = Mathf.Max(0f, c.r - redStep);
        //c.r = newR;
        if (c.r > 0f)
        {
            // まずは R を減らしていく
            float newR = Mathf.Max(0f, c.r - redStep);
            c.r = newR;
        }
        else
        {
            // R が 0 になったら、G を減らしていく
            float newG = Mathf.Max(0f, c.g - redStep);
            c.g = newG;
        }

        _renderer.material.color = c;
    }


    public static void ClearAllNodes()
    {
        allNodes.Clear();
        allNodeCells.Clear();
        StartNode = null;
    }
}