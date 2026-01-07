using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
    public static List<MapNode> allNodes = new List<MapNode>();

    private static int nodeCreateCount = 0;  // ★ 追加：生成カウンタ
    private static int totalPassCount = 0;  // ★ 追加：全Node合計の訪問数（通過回数
    private static float peakAvgPass = 1f;  // ★ 追加：平均訪問回数の最大（正規化用）

    // ★ スクショ直前の一斉色更新を「1回だけ」にするため
    private static bool colorDirtyThisRun = false;   // passCountが動いたら true
    private static bool colorAppliedThisRun = false; // 一斉反映済みなら true

    // ★ Start距離の再計算を間引く用
    private static bool startDistanceDirty = false;

    [Header("基本情報")]
    public Vector2Int cell;
    public List<MapNode> links = new List<MapNode>();

    [Header("Node情報")]
    public bool isExcluded = false;
    public bool isMustPass = false;

    [Header("Goal関連情報")]
    public float DistanceFromGoal = Mathf.Infinity;
    public float value = 0f;

    [Header("最短距離（Startから）")]
    public int distanceFromStart = 0;
    public static MapNode StartNode; // ★ 最初に作られたNodeをStart地点として保持

    public static MapNode GoalNode;

    [Header("探索状態")]
    public int unknownCount = 0;
    public int wallCount = 0;

    public int passCount = 0;

    [Header("見た目（通過で色変化）")]
    public int maxPassForColor;   // 何回通ったら色変化MAXにするか
    public Color startColor = Color.white;  // 0回通過時の色
    public Color endColor = Color.red;      // maxPassForColor回通過時の色
    public Renderer _renderer;        // この Node の見た目用 Renderer

    [Header("色（総訪問数/Node数ベース）")]
    [Tooltip("平均訪問回数(総訪問数/Node数) × この倍率で最大色に到達")]
    public float avgPassScale = 1.0f;

    [Tooltip("色を変えたい Node が使っているマテリアル（通常Node用）")]
    public Material colorChangeTargetMaterial;
    // この Node が「色変更対象かどうか」
    private bool _enableColorChange = true;

    [Header("グリッド設定")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Header("デバッグ")]
    public bool debugLog = true;

    void Start()
    {
        if (CompareTag("Goal"))
        {
            GoalNode = this;
        }
    }

    private void Awake()
    {
        // ★ 追加：生成順で名前を設定
        nodeCreateCount++;
        this.name = "Node_" + nodeCreateCount;

        cell = WorldToCell(transform.position);

        if (!isExcluded && NodePointMarker.IsMustPassCell(cell))
        {
            isMustPass = true;
            // 見た目確認用（不要なら消してOK）
            name += "_MP";
        }

        if (!allNodeCells.Contains(cell))
            allNodeCells.Add(cell);

        if (!allNodes.Contains(this))
            allNodes.Add(this);

        RecalculateUnknownAndWall();

        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            // ★ Goal は色変更しない（マテリアルもいじらない）
            if (CompareTag("Goal"))
            {
                _enableColorChange = false;
            }
            else
            {
                // ★ 通常の Node は色変更対象
                _renderer.material = Instantiate(_renderer.material);
                _renderer.material.color = startColor;
                _enableColorChange = true;
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
            return;
        }

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

        if (!added) return;

        // 双方再計算（リンク確定後）
        RecalculateUnknownAndWall();
        other.RecalculateUnknownAndWall();

        // ★ Start距離は即時再計算しない（重いので間引く）
        RequestRecalculateStartDistance();
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
                int dx = Mathf.Abs(node.cell.x - link.cell.x);
                int dy = Mathf.Abs(node.cell.y - link.cell.y);
                int cost = dx + dy;

                int newDist = node.distanceFromStart + cost;

                if (newDist < link.distanceFromStart)
                {
                    link.distanceFromStart = newDist;
                    q.Enqueue(link);
                }
            }
        }
    }

    public static void RequestRecalculateStartDistance()
    {
        startDistanceDirty = true;
    }

    public static void ProcessRecalculateStartDistanceIfNeeded()
    {
        if (!startDistanceDirty) return;
        startDistanceDirty = false;
        RecalculateStartDistance();
    }

    public static void ForceRecalculateStartDistanceNow()
    {
        startDistanceDirty = false;
        RecalculateStartDistance();
    }

    // ======================================================
    // ★ dir -> cell差分（4方向限定）
    // ======================================================
    private static Vector2Int DirToCellDelta(Vector3 dir)
    {
        if (dir == Vector3.forward) return new Vector2Int(0, 1);
        if (dir == Vector3.back) return new Vector2Int(0, -1);
        if (dir == Vector3.left) return new Vector2Int(-1, 0);
        if (dir == Vector3.right) return new Vector2Int(1, 0);
        return Vector2Int.zero;
    }

    // ======================================================
    // Linkベースでの未知数・壁数再計算
    // ★ isExcluded は Unknown ではなく「壁扱い」
    // ★ さらに「隣セルがExcludedか」を NodePointMarker で判定（隣Nodeがまだ無くても壁扱い）
    // ======================================================
    public void RecalculateUnknownAndWall()
    {
        unknownCount = 0;
        wallCount = 0;

        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 dir = dirs[i];

            bool linkedInDir = false;
            bool blockedByExcludedLink = false;

            // ---- 方向にリンクしているか（角度チェック）----
            foreach (var link in links)
            {
                if (link == null) continue;

                Vector3 delta = (link.transform.position - transform.position).normalized;
                float dot = Vector3.Dot(delta, dir);

                if (dot > 0.95f)
                {
                    // ★ リンク先が Excluded なら「壁方向」
                    if (link.isExcluded)
                    {
                        blockedByExcludedLink = true;
                        break;
                    }

                    linkedInDir = true;
                    break;
                }
            }

            if (blockedByExcludedLink)
            {
                wallCount++;
                continue;
            }

            if (linkedInDir)
                continue;

            // ★ 隣セルが Excluded 指定なら、Node がまだ無くても「壁扱い」
            Vector2Int neighborCell = cell + DirToCellDelta(dir);
            if (NodePointMarker.IsExcludedCell(neighborCell))
            {
                wallCount++;
                continue;
            }

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
                continue;
            }

            // ---- Node チェック（短めの距離で誤検知を防ぐ）----
            RaycastHit hitInfo;
            bool hitNode = Physics.Raycast(
                transform.position + Vector3.up * 0.05f,
                dir,
                out hitInfo,
                cellSize * 0.6f,
                LayerMask.GetMask("Node")
            );

            if (hitNode)
            {
                MapNode hitNodeObj = hitInfo.collider.GetComponent<MapNode>();

                if (hitNodeObj != null)
                {
                    // ★ 未リンクの Excluded Node を検知したら「壁扱い」
                    if (hitNodeObj.isExcluded)
                    {
                        wallCount++;
                        continue;
                    }

                    // ★ GoalNode の場合だけ “未リンクなら Unknown 扱い”
                    if (hitNodeObj.CompareTag("Goal"))
                    {
                        bool linkedToGoal = links.Contains(hitNodeObj);
                        if (!linkedToGoal)
                        {
                            unknownCount++;
                            continue;
                        }
                    }

                    // 通常Nodeがある方向は Unknown ではない
                    continue;
                }

                // Nodeレイヤーに当たったが MapNode じゃない → 壁扱い
                wallCount++;
                continue;
            }

            // ---- 有効な Unknown ----
            unknownCount++;
        }
    }

    public float EdgeCost(MapNode other)
    {
        if (other == null) return Mathf.Infinity;
        return Vector3.Distance(transform.position, other.transform.position);
    }

    public static MapNode FindByCell(Vector2Int cell)
    {
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
            return;
        }

        allNodeCells.Remove(cell);
        allNodes.Remove(this);
    }

    // ======================================================
    // ★ Unknown 方向（リンクなし & 壁なし）を返す
    // ★ isExcluded は Unknown ではなく「壁扱い」
    // ★ 隣セルが Excluded 指定なら Node がまだ無くても「壁扱い」
    // ======================================================
    public Vector3? GetUnknownDirection()
    {
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 dir = dirs[i];

            // (1) その方向にリンクがある場合は除外（Excludedリンクも通れない）
            foreach (var link in links)
            {
                if (link == null) continue;

                Vector3 delta = (link.transform.position - transform.position).normalized;
                float dot = Vector3.Dot(delta, dir);
                if (dot > 0.95f)
                {
                    // link.isExcluded でも、通常リンクでも「未知方向ではない」
                    // （Excludedは壁扱いで“行けない”が、ここでは候補から外すだけでOK）
                    goto NEXT_DIR;
                }
            }

            // ★ 隣セルが Excluded 指定なら、Node がまだ無くても「壁扱い」
            Vector2Int neighborCell = cell + DirToCellDelta(dir);
            if (NodePointMarker.IsExcludedCell(neighborCell))
            {
                goto NEXT_DIR;
            }

            // (2) 壁がある場合は除外
            if (Physics.Raycast(transform.position + Vector3.up * 0.05f, dir, cellSize * 1.2f, LayerMask.GetMask("Wall")))
            {
                goto NEXT_DIR;
            }

            // (3) Nodeがあるなら基本 Unknown にしない（ただし Goal は例外）
            RaycastHit hit;
            bool hitNode = Physics.Raycast(
                transform.position + Vector3.up * 0.05f,
                dir,
                out hit,
                cellSize * 0.6f,
                LayerMask.GetMask("Node")
            );

            if (hitNode)
            {
                MapNode n = hit.collider.GetComponent<MapNode>();
                if (n != null)
                {
                    // ★ Excluded Node は「壁扱い」
                    if (n.isExcluded)
                        goto NEXT_DIR;

                    // ★ Goal だけは「未リンクなら Unknown 扱い」
                    if (n.CompareTag("Goal"))
                        return dir;

                    goto NEXT_DIR;
                }

                // Nodeレイヤーだが MapNode じゃない → 掘れない扱い
                goto NEXT_DIR;
            }

            // (4) 何も無い → Unknown
            return dir;

        NEXT_DIR:
            continue;
        }

        return null;
    }

    // ======================================================
    // ★ Player がこの Node を通過したときに呼ぶ
    // ======================================================
    public void OnPassed()
    {
        passCount++;
        totalPassCount++;

        // ★ 色は通過中は変えない（スクショ直前に一斉反映）
        colorDirtyThisRun = true;
    }

    public static void ApplyColorsOnceBeforeScreenshot()
    {
        if (colorAppliedThisRun) return;

        if (!colorDirtyThisRun)
        {
            colorAppliedThisRun = true;
            return;
        }

        UpdateAllNodesColor_ByGlobalAverage();
        colorAppliedThisRun = true;
    }

    private static void UpdateAllNodesColor_ByGlobalAverage()
    {
        int nodeCount = 0;
        int maxPass = 0;

        for (int i = 0; i < allNodes.Count; i++)
        {
            var n = allNodes[i];
            if (n != null && n._enableColorChange && n._renderer != null)
            {
                nodeCount++;
                if (n.passCount > maxPass) maxPass = n.passCount;
            }
        }
        if (nodeCount <= 0) return;

        maxPass = Mathf.Max(1, maxPass);

        for (int i = 0; i < allNodes.Count; i++)
        {
            var n = allNodes[i];
            if (n == null || !n._enableColorChange || n._renderer == null) continue;

            float t = Mathf.Clamp01((float)n.passCount / maxPass);
            int s = Mathf.Clamp(Mathf.RoundToInt(t * 510f), 0, 510);

            int gInt = 255 - Mathf.Min(s, 255);
            int bInt = 255 - Mathf.Max(s - 255, 0);

            byte g = (byte)Mathf.Clamp(gInt, 0, 255);
            byte b = (byte)Mathf.Clamp(bInt, 0, 255);

            n._renderer.material.color = new Color32(255, g, b, 255);
        }
    }

    public static void ClearAllNodes()
    {
        allNodes.Clear();
        allNodeCells.Clear();
        StartNode = null;
        GoalNode = null;
        nodeCreateCount = 0;
        totalPassCount = 0;
        peakAvgPass = 1f;

        colorDirtyThisRun = false;
        colorAppliedThisRun = false;

        startDistanceDirty = false;
    }
}


//using System.Collections.Generic;
//using System.Linq;
//using UnityEngine;

//public class MapNode : MonoBehaviour
//{
//    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();
//    public static List<MapNode> allNodes = new List<MapNode>();

//    private static int nodeCreateCount = 0;  // ★ 追加：生成カウンタ
//    private static int totalPassCount = 0;  // ★ 追加：全Node合計の訪問数（通過回数
//    private static float peakAvgPass = 1f;  // ★ 追加：平均訪問回数の最大（正規化用）

//    // ★ スクショ直前の一斉色更新を「1回だけ」にするため
//    private static bool colorDirtyThisRun = false;   // passCountが動いたら true
//    private static bool colorAppliedThisRun = false; // 一斉反映済みなら true

//    // ★ Start距離の再計算を間引く用
//    private static bool startDistanceDirty = false;

//    [Header("基本情報")]
//    public Vector2Int cell;
//    public List<MapNode> links = new List<MapNode>();

//    [Header("Node情報")]
//    public bool isExcluded = false;

//    [Header("Goal関連情報")]
//    public float DistanceFromGoal = Mathf.Infinity;
//    public float value = 0f;

//    [Header("最短距離（Startから）")]
//    public int distanceFromStart = 0;
//    public static MapNode StartNode; // ★ 最初に作られたNodeをStart地点として保持

//    public static MapNode GoalNode;

//    [Header("探索状態")]
//    public int unknownCount = 0;
//    public int wallCount = 0;

//    public int passCount = 0;

//    [Header("見た目（通過で色変化）")]
//    //public float redStep;      // 1回通るごとに減らす量
//    public int maxPassForColor;   // 何回通ったら色変化MAXにするか
//    public Color startColor = Color.white;  // 0回通過時の色
//    public Color endColor = Color.red;      // maxPassForColor回通過時の色
//    public Renderer _renderer;        // この Node の見た目用 Renderer

//    //[Tooltip("平均訪問回数(全体訪問数/Node数)×この倍率で endColor に到達")]
//    //public float avgPassColorScale = 1f;

//    [Header("色（総訪問数/Node数ベース）")]
//    [Tooltip("平均訪問回数(総訪問数/Node数) × この倍率で最大色に到達")]
//    public float avgPassScale = 1.0f;

//    [Tooltip("色を変えたい Node が使っているマテリアル（通常Node用）")]
//    public Material colorChangeTargetMaterial;
//    // この Node が「色変更対象かどうか」
//    private bool _enableColorChange = true;

//    [Header("グリッド設定")]
//    public float cellSize = 1f;
//    public Vector3 gridOrigin = Vector3.zero;

//    [Header("デバッグ")]
//    public bool debugLog = true;

//    void Start()
//    {
//        // もともと Start で cell を計算しているなら、
//        // その処理の「後」でこれをやるのが安全
//        if (CompareTag("Goal"))
//        {
//            GoalNode = this;
//            //Debug.Log($"[MapNode] GoalNode set: {name} cell=({cell.x},{cell.y})");
//        }
//    }

//    private void Awake()
//    {
//        // ★ 追加：生成順で名前を設定
//        nodeCreateCount++;
//        this.name = "Node_" + nodeCreateCount;

//        cell = WorldToCell(transform.position);

//        if (!allNodeCells.Contains(cell))
//            allNodeCells.Add(cell);

//        if (!allNodes.Contains(this))
//            allNodes.Add(this);

//        //// ★ StartNode判定 ----------------------
//        //if (StartNode == null)
//        //{
//        //    // 最初に生成されたNodeをStartとして扱う
//        //    StartNode = this;
//        //    distanceFromStart = 0;
//        //}
//        //// ---------------------------------------------------

//        //Debug.Log($"[StartNode Debug] cell={cell}, pos={transform.position}, links={links.Count}");

//        RecalculateUnknownAndWall();

//        //Debug.Log($"[DEBUG-STARTNODE] Awake(): StartNode={MapNode.StartNode?.name}");

//        _renderer = GetComponent<Renderer>();
//        if (_renderer != null)
//        {
//            // ★ Goal は色変更しない（マテリアルもいじらない）
//            if (CompareTag("Goal"))
//            {
//                _enableColorChange = false;
//            }
//            else
//            {
//                // ★ 通常の Node は色変更対象
//                //   各 Node ごとにマテリアルを複製して独立させる
//                _renderer.material = Instantiate(_renderer.material);

//                // 開始色を startColor にしておく（Inspector で白などに設定）
//                _renderer.material.color = startColor;

//                _enableColorChange = true;
//            }
//        }

//        // ★ Node数が増えた瞬間にも「全体色」を更新（＝増えるほど薄くなる）
//        //UpdateAllNodesColor_ByGlobalAverage();
//    }

//    // ======================================================
//    // ★ Startからの距離を更新（小さい値のみ受け付ける）
//    // ======================================================
//    public void UpdateDistanceFromStart(int newDist)
//    {
//        if (newDist < distanceFromStart)
//            distanceFromStart = newDist;
//    }

//    // ======================================================
//    // ★ 全ノードの距離をリセット + startノードを0にする
//    // ======================================================
//    public static void ResetAllDistanceFromStart(MapNode start)
//    {
//        foreach (var n in allNodes)
//            n.distanceFromStart = int.MaxValue;
//        //n.distanceFromStart = 0;

//        if (start != null)
//            start.distanceFromStart = 0;
//    }


//    public void UpdateValueByGoal(MapNode goal)
//    {
//        if (goal == null) return;
//        float dist = Vector3.Distance(transform.position, goal.transform.position);
//        value = -dist;
//    }

//    public void AddLink(MapNode other)
//    {
//        if (other == null || other == this)
//        {
//            //Debug.LogError($"[MN-AddLink ERROR] other == null");
//            return;
//        }

//        bool added = false;

//        if (!links.Contains(other))
//        {
//            links.Add(other);
//            added = true;
//           //Debug.Log($"[AddLink] Add {name} → {other.name}");
//        }

//        if (!other.links.Contains(this))
//        {
//            other.links.Add(this);
//            added = true;
//            //Debug.Log($"[AddLink] Add {name} → {other.name}");
//        }

//        if (debugLog && added)
//            //Debug.Log($"[MapNode] Linked: {name} ↔ {other.name}");

//        if (!added) return;

//        // 双方再計算（リンク確定後）
//        RecalculateUnknownAndWall();
//        other.RecalculateUnknownAndWall();

//        // ★ Startからの距離も更新（Goalと同じDijkstra方式）
//        //RecalculateStartDistance();

//        // ★ Start距離は即時再計算しない（重いので間引く）
//        RequestRecalculateStartDistance();
//    }

//    // ======================================================
//    // Goal を起点に DistanceFromGoal を全 Node に再計算
//    // ======================================================
//    public static void RecalculateGoalDistance(MapNode goal)
//    {
//        if (goal == null) return;

//        // 全ノード初期化
//        foreach (var n in allNodes)
//            n.DistanceFromGoal = float.PositiveInfinity;

//        goal.DistanceFromGoal = 0f;

//        // BFS
//        Queue<MapNode> q = new Queue<MapNode>();
//        q.Enqueue(goal);

//        while (q.Count > 0)
//        {
//            MapNode node = q.Dequeue();

//            foreach (var link in node.links)
//            {
//                // コスト = セル距離（マンハッタン距離）
//                int dx = Mathf.Abs(node.cell.x - link.cell.x);
//                int dy = Mathf.Abs(node.cell.y - link.cell.y);
//                float cost = dx + dy;

//                float newDist = node.DistanceFromGoal + cost;

//                if (newDist < link.DistanceFromGoal)
//                {
//                    link.DistanceFromGoal = newDist;
//                    q.Enqueue(link);
//                }
//            }
//        }
//    }


//    // ======================================================
//    // StartNode を起点に距離(distanceFromStart)を再計算
//    // DistanceFromGoal と同じ Dijkstra 法
//    // ======================================================
//    public static void RecalculateStartDistance()
//    {
//        if (StartNode == null) return;

//        // 全ノード初期化
//        foreach (var n in allNodes)
//            n.distanceFromStart = int.MaxValue;

//        StartNode.distanceFromStart = 0;

//        Queue<MapNode> q = new Queue<MapNode>();
//        q.Enqueue(StartNode);

//        while (q.Count > 0)
//        {
//            var node = q.Dequeue();

//            foreach (var link in node.links)
//            {
//                // ★ セル距離（マンハッタン距離）
//                int dx = Mathf.Abs(node.cell.x - link.cell.x);
//                int dy = Mathf.Abs(node.cell.y - link.cell.y);
//                int cost = dx + dy;   // ← これが加算距離

//                int newDist = node.distanceFromStart + cost;

//                if (newDist < link.distanceFromStart)
//                {
//                    link.distanceFromStart = newDist;
//                    q.Enqueue(link);
//                }
//            }
//        }
//    }

//    // ★ 再計算が必要になったことだけ記録
//    public static void RequestRecalculateStartDistance()
//    {
//        startDistanceDirty = true;
//    }

//    // ★ dirtyなら1回だけ実行（Schedulerから呼ぶ）
//    public static void ProcessRecalculateStartDistanceIfNeeded()
//    {
//        if (!startDistanceDirty) return;
//        startDistanceDirty = false;
//        RecalculateStartDistance();
//    }

//    // ★ スクショ直前など「必ず最新にしたい」時用
//    public static void ForceRecalculateStartDistanceNow()
//    {
//        startDistanceDirty = false;
//        RecalculateStartDistance();
//    }


//    // ======================================================
//    // Linkベースでの未知数・壁数再計算
//    // ======================================================
//    //public void RecalculateUnknownAndWall()
//    //{
//    //    //Debug.Log($"[MapNode] Recalc START name={name}, before U={unknownCount}, W={wallCount}, linkCount={links.Count}");

//    //    int prevU = unknownCount;
//    //    int prevW = wallCount;
//    //    unknownCount = 0;
//    //    wallCount = 0;

//    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
//    //    string[] dirNames = { "F", "B", "L", "R" };

//    //    for (int i = 0; i < dirs.Length; i++)
//    //    {
//    //        Vector3 dir = dirs[i];
//    //        string dirName = dirNames[i];

//    //        bool linkedInDir = false;

//    //        // Linkから方向判定（角度許容を緩くする）
//    //        foreach (var link in links)
//    //        {
//    //            Vector3 delta = (link.transform.position - transform.position).normalized;
//    //            float dot = Vector3.Dot(delta, dir);
//    //            if (dot > 0.95f)  // ← 0.9 → 0.7 に緩和
//    //            {
//    //                linkedInDir = true;
//    //                //if (debugLog)
//    //                    Debug.Log($"[MapNode] {name} dir={dirName}: Linked with {link.name} (dot={dot:F2})");
//    //                break;
//    //            }
//    //        }

//    //        if (linkedInDir)
//    //            continue; // 既知方向（リンク済み）

//    //        // リンクなし方向 → 壁 or Unknown
//    //        if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, cellSize, LayerMask.GetMask("Wall")))
//    //        //if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, cellSize, wallLayer))

//    //        {
//    //            wallCount++;
//    //            //if (debugLog)
//    //                Debug.Log($"[MapNode] {name} dir={dirName}: HIT Wall");
//    //        }
//    //        else
//    //        {
//    //            unknownCount++;
//    //            //if (debugLog)
//    //                Debug.Log($"[MapNode] {name} dir={dirName}: Unknown (no link)");
//    //        }
//    //    }

//    //    //if (prevU != unknownCount || prevW != wallCount)
//    //    //Debug.Log($"[MapNode][U/W CHANGED] {name}  U: {prevU} -> {unknownCount},  W: {prevW} -> {wallCount}");

//    //    //Debug.Log($"[MapNode] Recalc END name={name}, after U={unknownCount}, W={wallCount}, linkCount={links.Count}");
//    //}
//    //public void RecalculateUnknownAndWall()
//    //{
//    //    int prevU = unknownCount;
//    //    int prevW = wallCount;
//    //    unknownCount = 0;
//    //    wallCount = 0;

//    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
//    //    string[] dirNames = { "F", "B", "L", "R" };

//    //    for (int i = 0; i < dirs.Length; i++)
//    //    {
//    //        Vector3 dir = dirs[i];
//    //        string dirName = dirNames[i];

//    //        bool linkedInDir = false;

//    //        // ---- 方向にリンクしているか（角度チェック）----
//    //        foreach (var link in links)
//    //        {
//    //            Vector3 delta = (link.transform.position - transform.position).normalized;
//    //            float dot = Vector3.Dot(delta, dir);
//    //            if (dot > 0.95f)
//    //            {
//    //                linkedInDir = true;
//    //                Debug.Log($"[MapNode] {name} dir={dirName}: Linked with {link.name} (dot={dot:F2})");
//    //                break;
//    //            }
//    //        }

//    //        if (linkedInDir)
//    //            continue; // 既にリンク済み方向は Unknown/WALL の対象外

//    //        // ---- 壁チェック ----
//    //        bool hitWall = Physics.Raycast(
//    //            transform.position + Vector3.up * 0.1f,
//    //            dir,
//    //            cellSize,
//    //            LayerMask.GetMask("Wall")
//    //        );

//    //        if (hitWall)
//    //        {
//    //            wallCount++;
//    //            Debug.Log($"[MapNode] {name} dir={dirName}: HIT Wall");
//    //            continue;
//    //        }

//    //        // ---- ★ Node がその方向に存在するかチェック ----
//    //        bool hitNode = Physics.Raycast(
//    //            transform.position + Vector3.up * 0.1f,
//    //            dir,
//    //            cellSize,
//    //            LayerMask.GetMask("Node")
//    //        );

//    //        if (hitNode)
//    //        {
//    //            // 壁ではないし Node でもある → これは Unknown ではない
//    //            // 未リンク方向だが「進めない Unknown 」なので無視
//    //            Debug.Log($"[MapNode] {name} dir={dirName}: Found Node but not linked");
//    //            continue;
//    //        }

//    //        // ---- ★ 本当に進める Unknown ----
//    //        unknownCount++;
//    //        Debug.Log($"[MapNode] {name} dir={dirName}: Valid Unknown");
//    //    }
//    //}
//    //public void RecalculateUnknownAndWall()
//    //{
//    //    int prevU = unknownCount;
//    //    int prevW = wallCount;
//    //    unknownCount = 0;
//    //    wallCount = 0;

//    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
//    //    string[] dirNames = { "F", "B", "L", "R" };

//    //    for (int i = 0; i < dirs.Length; i++)
//    //    {
//    //        Vector3 dir = dirs[i];
//    //        string dirName = dirNames[i];

//    //        bool linkedInDir = false;

//    //        // ---- 方向にリンクしているか（角度チェック）----
//    //        foreach (var link in links)
//    //        {
//    //            Vector3 delta = (link.transform.position - transform.position).normalized;
//    //            float dot = Vector3.Dot(delta, dir);
//    //            if (dot > 0.95f)
//    //            {
//    //                linkedInDir = true;
//    //                //Debug.Log($"[MapNode] {name} dir={dirName}: Linked with {link.name} (dot={dot:F2})");
//    //                break;
//    //            }
//    //        }

//    //        if (linkedInDir)
//    //            continue;

//    //        // ---- 壁チェック（距離を少し伸ばす）----
//    //        bool hitWall = Physics.Raycast(
//    //            transform.position + Vector3.up * 0.05f,
//    //            dir,
//    //            cellSize * 1.2f,
//    //            LayerMask.GetMask("Wall")
//    //        );

//    //        if (hitWall)
//    //        {
//    //            wallCount++;
//    //            //Debug.Log($"[MapNode] {name} dir={dirName}: HIT Wall");
//    //            continue;
//    //        }

//    //        // ---- Node チェック（短めの距離で誤検知を防ぐ）----
//    //        bool hitNode = Physics.Raycast(
//    //            transform.position + Vector3.up * 0.05f,
//    //            dir,
//    //            cellSize * 0.6f,
//    //            LayerMask.GetMask("Node")
//    //        );

//    //        //if (hitNode)
//    //        //{
//    //        //    Debug.Log($"[MapNode] {name} dir={dirName}: Found Node but not linked");
//    //        //    continue;
//    //        //}
//    //        if (hitNode)
//    //        {
//    //            // ★ hit した Node を取得
//    //            RaycastHit hitInfo;
//    //            Physics.Raycast(
//    //                transform.position + Vector3.up * 0.05f,
//    //                dir,
//    //                out hitInfo,
//    //                cellSize * 0.6f,
//    //                LayerMask.GetMask("Node")
//    //            );

//    //            MapNode hitNodeObj = hitInfo.collider.GetComponent<MapNode>();

//    //            if (hitNodeObj != null && hitNodeObj.isExcluded)
//    //            {
//    //                wallCount++;
//    //                //Debug.Log($"[MapNode] {name} dir={dirName}: HIT Excluded Node = wall");
//    //                continue;
//    //            }

//    //            // ★ GoalNode の場合だけ “未リンクなら Unknown 扱い”
//    //            if (hitNodeObj != null && hitNodeObj.CompareTag("Goal"))
//    //            {
//    //                bool linkedToGoal = links.Contains(hitNodeObj);

//    //                if (!linkedToGoal)
//    //                {
//    //                    unknownCount++;
//    //                    Debug.Log($"[MapNode] {name} dir={dirName}: Unknown (Goal not linked)");
//    //                    continue; // ← Unknown 扱いして次へ
//    //                }
//    //            }

//    //            // ★ 通常の Node は Unknown にしない（現状のまま）
//    //            //Debug.Log($"[MapNode] {name} dir={dirName}: Found Node but not linked");
//    //            continue;
//    //        }


//    //        // ---- 有効な Unknown ----
//    //        unknownCount++;
//    //        //Debug.Log($"[MapNode] {name} dir={dirName}: Valid Unknown");
//    //    }
//    //}
//    //public void RecalculateUnknownAndWall()
//    //{
//    //    int prevU = unknownCount;
//    //    int prevW = wallCount;
//    //    unknownCount = 0;
//    //    wallCount = 0;

//    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
//    //    string[] dirNames = { "F", "B", "L", "R" };

//    //    for (int i = 0; i < dirs.Length; i++)
//    //    {
//    //        Vector3 dir = dirs[i];
//    //        string dirName = dirNames[i];

//    //        bool blockedByExcludedLink = false;
//    //        bool linkedInDir = false;

//    //        // ---- 方向にリンクしているか（角度チェック）----
//    //        foreach (var link in links)
//    //        {
//    //            if (link == null) continue;

//    //            Vector3 delta = (link.transform.position - transform.position).normalized;
//    //            float dot = Vector3.Dot(delta, dir);

//    //            if (dot > 0.95f)
//    //            {
//    //                // ★リンク先が Excluded なら「壁扱い」
//    //                if (link.isExcluded)
//    //                {
//    //                    blockedByExcludedLink = true;
//    //                    break;
//    //                }

//    //                linkedInDir = true;
//    //                break;
//    //            }
//    //        }

//    //        // ★ Excludedリンクは壁方向としてカウント
//    //        if (blockedByExcludedLink)
//    //        {
//    //            wallCount++;
//    //            continue;
//    //        }

//    //        // 通常リンクは Unknown/Wall 判定対象外
//    //        if (linkedInDir)
//    //            continue;

//    //        // ---- 壁チェック（距離を少し伸ばす）----
//    //        bool hitWall = Physics.Raycast(
//    //            transform.position + Vector3.up * 0.05f,
//    //            dir,
//    //            cellSize * 1.2f,
//    //            LayerMask.GetMask("Wall")
//    //        );

//    //        if (hitWall)
//    //        {
//    //            wallCount++;
//    //            continue;
//    //        }

//    //        // ---- Node チェック（短めの距離で誤検知を防ぐ）----
//    //        RaycastHit hitInfo;
//    //        bool hitNode = Physics.Raycast(
//    //            transform.position + Vector3.up * 0.05f,
//    //            dir,
//    //            out hitInfo,
//    //            cellSize * 0.6f,
//    //            LayerMask.GetMask("Node")
//    //        );

//    //        if (hitNode)
//    //        {
//    //            MapNode hitNodeObj = hitInfo.collider.GetComponent<MapNode>();

//    //            if (hitNodeObj != null)
//    //            {
//    //                // ★ 未リンクの Excluded Node を検知したら「壁扱い」
//    //                if (hitNodeObj.isExcluded)
//    //                {
//    //                    wallCount++;
//    //                    continue;
//    //                }

//    //                // ★ GoalNode の場合だけ “未リンクなら Unknown 扱い”
//    //                if (hitNodeObj.CompareTag("Goal"))
//    //                {
//    //                    bool linkedToGoal = links.Contains(hitNodeObj);
//    //                    if (!linkedToGoal)
//    //                    {
//    //                        unknownCount++;
//    //                        continue;
//    //                    }
//    //                }

//    //                // 通常の Node は Unknown にしない
//    //                continue;
//    //            }

//    //            // Nodeレイヤーに何かあるが MapNode じゃない → 壁扱い寄りでブロック
//    //            wallCount++;
//    //            continue;
//    //        }

//    //        // ---- 有効な Unknown ----
//    //        unknownCount++;
//    //    }
//    //}
//    public void RecalculateUnknownAndWall()
//    {
//        int prevU = unknownCount;
//        int prevW = wallCount;
//        unknownCount = 0;
//        wallCount = 0;

//        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
//        string[] dirNames = { "F", "B", "L", "R" };

//        for (int i = 0; i < dirs.Length; i++)
//        {
//            Vector3 dir = dirs[i];
//            string dirName = dirNames[i];

//            bool linkedInDir = false;

//            // ---- 方向にリンクしているか（角度チェック）----
//            foreach (var link in links)
//            {
//                Vector3 delta = (link.transform.position - transform.position).normalized;
//                float dot = Vector3.Dot(delta, dir);
//                if (dot > 0.95f)
//                {
//                    linkedInDir = true;
//                    break;
//                }
//            }

//            if (linkedInDir)
//                continue;

//            // ---- 壁チェック（距離を少し伸ばす）----
//            bool hitWall = Physics.Raycast(
//                transform.position + Vector3.up * 0.05f,
//                dir,
//                cellSize * 1.2f,
//                LayerMask.GetMask("Wall")
//            );

//            if (hitWall)
//            {
//                wallCount++;
//                continue;
//            }

//            // ---- Node チェック（短めの距離で誤検知を防ぐ）----
//            bool hitNode = Physics.Raycast(
//                transform.position + Vector3.up * 0.05f,
//                dir,
//                cellSize * 0.60f,
//                LayerMask.GetMask("Node")
//            );

//            if (hitNode)
//            {
//                // ★ hit した Node を取得
//                RaycastHit hitInfo;
//                Physics.Raycast(
//                    transform.position + Vector3.up * 0.05f,
//                    dir,
//                    out hitInfo,
//                    cellSize * 0.6f,
//                    LayerMask.GetMask("Node")
//                );

//                MapNode hitNodeObj = hitInfo.collider.GetComponent<MapNode>();

//                // ★ GoalNode の場合だけ “未リンクなら Unknown 扱い”
//                if (hitNodeObj != null && hitNodeObj.CompareTag("Goal"))
//                {
//                    bool linkedToGoal = links.Contains(hitNodeObj);

//                    if (!linkedToGoal)
//                    {
//                        unknownCount++;
//                        Debug.Log($"[MapNode] {name} dir={dirName}: Unknown (Goal not linked)");
//                        continue; // ← Unknown 扱いして次へ
//                    }
//                }

//                // ★ 通常の Node は Unknown にしない
//                continue;
//            }

//            // ---- 有効な Unknown ----
//            // ★ ここだけ原因特定ログを入れる（isExcluded を Unknown に数えてしまう瞬間だけ）
//            {
//                // dir -> セル差分
//                Vector2Int dCell =
//                    (dir == Vector3.forward) ? new Vector2Int(0, 1) :
//                    (dir == Vector3.back) ? new Vector2Int(0, -1) :
//                    (dir == Vector3.left) ? new Vector2Int(-1, 0) :
//                    (dir == Vector3.right) ? new Vector2Int(1, 0) :
//                                               Vector2Int.zero;

//                Vector2Int neighborCell = cell + dCell;

//                // ① セル検索で「隣にNodeがいるか（特にExcludedか）」を見る
//                MapNode neighborByCell = MapNode.FindByCell(neighborCell);

//                // ② Ray距離違いで「0.6では拾えないが1.2なら拾える」を検出（距離不足の特定用）
//                int nodeMask = LayerMask.GetMask("Node");
//                Vector3 rayOrigin = transform.position + Vector3.up * 0.05f;

//                bool hitNodeNear = Physics.Raycast(rayOrigin, dir, out RaycastHit nearHit, cellSize * 0.6f, nodeMask);
//                bool hitNodeFar = Physics.Raycast(rayOrigin, dir, out RaycastHit farHit, cellSize * 1.2f, nodeMask);

//                MapNode nearNode = hitNodeNear ? nearHit.collider.GetComponentInParent<MapNode>() : null;
//                MapNode farNode = hitNodeFar ? farHit.collider.GetComponentInParent<MapNode>() : null;

//                bool excludedByCell = (neighborByCell != null && neighborByCell.isExcluded);
//                bool excludedByFarRay = (farNode != null && farNode.isExcluded);

//                // ★ 「Excluded が隣にいるのに Unknown として数える」時だけログ
//                if (excludedByCell || excludedByFarRay)
//                {
//                    Debug.LogWarning(
//                        $"[EX->UNKNOWN?] node={name} cell={cell} dir={dirName} -> nCell={neighborCell} | " +
//                        $"byCell={(neighborByCell ? neighborByCell.name : "null")} exCell={excludedByCell} | " +
//                        $"ray0.6={(nearNode ? nearNode.name : (hitNodeNear ? "hitNoMapNode" : "none"))} | " +
//                        $"ray1.2={(farNode ? farNode.name : (hitNodeFar ? "hitNoMapNode" : "none"))} exFar={excludedByFarRay}"
//                    );
//                }
//            }

//            unknownCount++;
//        }
//    }



//    public float EdgeCost(MapNode other)
//    {
//        if (other == null) return Mathf.Infinity;
//        return Vector3.Distance(transform.position, other.transform.position);
//    }

//    //public static MapNode FindByCell(Vector2Int cell)
//    //{
//    //    return allNodes.FirstOrDefault(n => n.cell == cell);
//    //}
//    public static MapNode FindByCell(Vector2Int cell)
//    {
//        // LINQ撤廃：GC発生ゼロ
//        for (int i = 0; i < allNodes.Count; i++)
//        {
//            if (allNodes[i].cell == cell)
//                return allNodes[i];
//        }
//        return null;
//    }

//    public static MapNode FindNearest(Vector3 pos)
//    {
//        if (allNodes.Count == 0) return null;

//        MapNode nearest = allNodes[0];
//        float bestDist = (nearest.transform.position - pos).sqrMagnitude;

//        // LINQ撤廃：手動で最小距離探索
//        for (int i = 1; i < allNodes.Count; i++)
//        {
//            MapNode n = allNodes[i];
//            float dist = (n.transform.position - pos).sqrMagnitude;
//            if (dist < bestDist)
//            {
//                bestDist = dist;
//                nearest = n;
//            }
//        }

//        return nearest;
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
//        float normalized = Mathf.Clamp01(1f - Mathf.Abs(value) / 20f);
//        Gizmos.color = Color.Lerp(Color.blue, Color.red, normalized);
//        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

//        Gizmos.color = Color.yellow;
//        foreach (var node in links)
//        {
//            if (node != null)
//                Gizmos.DrawLine(transform.position, node.transform.position);
//        }

//#if UNITY_EDITOR
//        UnityEditor.Handles.Label(
//            transform.position + Vector3.up * 0.3f,
//            $"V:{value:F2}\nU:{unknownCount} W:{wallCount}"
//        );
//#endif
//    }

//    private void OnDestroy()
//    {
//        if (this == StartNode)
//        {
//            //Debug.LogError("[ERROR] StartNode が Destroy されました。StartNode は絶対に破棄してはいけません。");
//            return;
//        }

//        allNodeCells.Remove(cell);
//        allNodes.Remove(this);
//    }

//    // ======================================================
//    // ★ Unknown 方向（リンクなし & 壁なし）を返す
//    // ======================================================
//    //public Vector3? GetUnknownDirection()
//    //{
//    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//    //    foreach (var dir in dirs)
//    //    {
//    //        // すでにリンク済み方向は除外
//    //        bool linked = false;
//    //        foreach (var link in links)
//    //        {
//    //            Vector3 delta = (link.transform.position - transform.position).normalized;
//    //            if (Vector3.Dot(delta, dir) > 0.95f)
//    //            {
//    //                linked = true;
//    //                break;
//    //            }
//    //        }
//    //        if (linked) continue;

//    //        // 壁方向は除外
//    //        if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
//    //            dir, cellSize, LayerMask.GetMask("Wall")))
//    //            continue;

//    //        // → Unknown 方向
//    //        return dir;
//    //    }

//    //    // Unknown 無し
//    //    return null;
//    //}
//    //public Vector3? GetUnknownDirection()
//    //{
//    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//    //    int wallMask = LayerMask.GetMask("Wall");
//    //    int nodeGoalMask = LayerMask.GetMask("Node", "Goal");

//    //    foreach (var dir in dirs)
//    //    {
//    //        // すでにリンク済み方向は除外
//    //        bool linked = false;
//    //        foreach (var link in links)
//    //        {
//    //            Vector3 delta = (link.transform.position - transform.position).normalized;
//    //            if (Vector3.Dot(delta, dir) > 0.95f)
//    //            {
//    //                linked = true;
//    //                break;
//    //            }
//    //        }
//    //        if (linked) continue;

//    //        // 壁方向は除外
//    //        if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, cellSize, wallMask))
//    //            continue;

//    //        // ★ 追加：Node/Goal がある方向も除外（特に isExcluded は壁扱い）
//    //        if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
//    //                            dir,
//    //                            out RaycastHit hitNode,
//    //                            cellSize,
//    //                            nodeGoalMask))
//    //        {
//    //            var n = hitNode.collider.GetComponent<MapNode>();
//    //            if (n != null)
//    //            {
//    //                // Excluded は「壁方向」
//    //                if (n.isExcluded) continue;

//    //                // 通常ノードがある方向は Unknown ではない
//    //                //if (!n.isGoal) continue;
//    //                if (!n.CompareTag("Goal")) continue;

//    //                // Goal だけは進める（現状仕様に合わせる）
//    //            }
//    //            else
//    //            {
//    //                // Node/Goal レイヤに当たったが MapNode じゃないなら壁扱い
//    //                continue;
//    //            }
//    //        }

//    //        // → Unknown 方向
//    //        return dir;
//    //    }

//    //    return null;
//    //}
//    public Vector3? GetUnknownDirection()
//    {
//        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//        foreach (var dir in dirs)
//        {
//            // (1) その方向にリンクがある場合は除外
//            bool linkedInDir = false;

//            foreach (var link in links)
//            {
//                if (link == null) continue;

//                Vector3 delta = (link.transform.position - transform.position).normalized;
//                float dot = Vector3.Dot(delta, dir);

//                if (dot > 0.95f)
//                {
//                    // ★リンク先が Excluded なら「壁扱い」で候補から外す
//                    if (link.isExcluded)
//                    {
//                        linkedInDir = true; // この方向は通れない扱い
//                        break;
//                    }

//                    linkedInDir = true;
//                    break;
//                }
//            }

//            if (linkedInDir)
//                continue;

//            // (2) 壁がある場合は除外
//            bool hitWall = Physics.Raycast(
//                transform.position + Vector3.up * 0.05f,
//                dir,
//                cellSize * 1.2f,
//                LayerMask.GetMask("Wall")
//            );

//            if (hitWall)
//                continue;

//            // (3) Nodeがあるなら基本 Unknown にしない（ただし Goal は例外）
//            RaycastHit hit;
//            bool hitNode = Physics.Raycast(
//                transform.position + Vector3.up * 0.05f,
//                dir,
//                out hit,
//                cellSize * 0.6f,
//                LayerMask.GetMask("Node")
//            );

//            if (hitNode)
//            {
//                MapNode n = hit.collider.GetComponent<MapNode>();
//                if (n != null)
//                {
//                    // ★ Excluded Node は「壁扱い」
//                    if (n.isExcluded)
//                        continue;

//                    // ★ Goal だけは「未リンクなら Unknown 扱い」したい
//                    if (n.CompareTag("Goal"))
//                        return dir;

//                    // 通常Nodeは Unknown にしない
//                    continue;
//                }

//                // Nodeレイヤーだが MapNode じゃない → 掘れない扱い
//                continue;
//            }

//            // (4) 何も無い → Unknown
//            return dir;
//        }

//        return null;
//    }


//    // ======================================================
//    // ★ Player がこの Node を通過したときに呼ぶ
//    // ======================================================
//    //public void OnPassed()
//    //{
//    //    passCount++;

//    //    if (_renderer == null) return;

//    //    // 今の色を取得
//    //    Color c = _renderer.material.color;

//    //    // R を 0.01 減らす（0 まで）
//    //    //float newR = Mathf.Max(0f, c.r - redStep);
//    //    //c.r = newR;
//    //    if (c.r > 0f)
//    //    {
//    //        // まずは R を減らしていく
//    //        float newR = Mathf.Max(0f, c.r - redStep);
//    //        c.r = newR;
//    //    }
//    //    else
//    //    {
//    //        // R が 0 になったら、G を減らしていく
//    //        float newG = Mathf.Max(0f, c.g - redStep);
//    //        c.g = newG;
//    //    }

//    //    _renderer.material.color = c;
//    //}
//    public void OnPassed()
//    {
//        passCount++;
//        totalPassCount++;

//        //if (_renderer == null || !_enableColorChange) return;

//        // ★ 通過のたびに「全体色」を更新（＝探索が進むほど薄くなる）
//        //UpdateAllNodesColor_ByGlobalAverage();

//        // ★ 色は通過中は変えない（スクショ直前に一斉反映）
//        colorDirtyThisRun = true;
//    }

//    public static void ApplyColorsOnceBeforeScreenshot()
//    {
//        if (colorAppliedThisRun) return; // 2回目以降は何もしない

//        // 通過が無かったなら、もともと startColor のままなので固定だけして終わり
//        if (!colorDirtyThisRun)
//        {
//            colorAppliedThisRun = true;
//            return;
//        }

//        // ★ ここでだけ全ノード一斉更新
//        UpdateAllNodesColor_ByGlobalAverage();

//        // ★ このRunでは二度と色を変えない
//        colorAppliedThisRun = true;
//    }


//    // ======================================================
//    // ★ 全体の平均訪問回数（総訪問数 / Node数）から「全Node共通の色」を決める
//    //    Node数が少ない：平均が大 → 濃い
//    //    Node数が増える：平均が小 → 薄い
//    //    RGBは R=255固定、G/Bを510段階で増やして白に近づける
//    // ======================================================
//    private static void UpdateAllNodesColor_ByGlobalAverage()
//    {
//        // 色変更対象Node数（Goalは _enableColorChange=false なので除外）
//        int nodeCount = 0;
//        int maxPass = 0;

//        for (int i = 0; i < allNodes.Count; i++)
//        {
//            var n = allNodes[i];
//            if (n != null && n._enableColorChange && n._renderer != null)
//            {
//                nodeCount++;
//                if (n.passCount > maxPass) maxPass = n.passCount;
//            }
//        }
//        if (nodeCount <= 0) return;

//        maxPass = Mathf.Max(1, maxPass); // 0除算防止（全員0回でもOK）

//        // 全Nodeに適用（Nodeごとに色が違う）
//        for (int i = 0; i < allNodes.Count; i++)
//        {
//            var n = allNodes[i];
//            if (n == null || !n._enableColorChange || n._renderer == null) continue;

//            float t = Mathf.Clamp01((float)n.passCount / maxPass); // 0..1（多いほど1）

//            // 510段階（多いほど s が大きい）
//            int s = Mathf.Clamp(Mathf.RoundToInt(t * 510f), 0, 510);

//            // ★ まずGを減らす → 0になったらBを減らす
//            int gInt = 255 - Mathf.Min(s, 255);
//            int bInt = 255 - Mathf.Max(s - 255, 0);

//            byte g = (byte)Mathf.Clamp(gInt, 0, 255);
//            byte b = (byte)Mathf.Clamp(bInt, 0, 255);

//            n._renderer.material.color = new Color32(255, g, b, 255); // 白→赤
//        }
//    }


//    public static void ClearAllNodes()
//    {
//        allNodes.Clear();
//        allNodeCells.Clear();
//        StartNode = null;
//        GoalNode = null;
//        nodeCreateCount = 0;
//        totalPassCount = 0;
//        peakAvgPass = 1f;

//        colorDirtyThisRun = false;
//        colorAppliedThisRun = false;

//        startDistanceDirty = false;
//    }
//}