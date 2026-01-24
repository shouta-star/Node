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
            //name += "_MP";
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

    //public static void ApplyColorsOnceBeforeScreenshot()
    //{
    //    if (colorAppliedThisRun) return;

    //    if (!colorDirtyThisRun)
    //    {
    //        colorAppliedThisRun = true;
    //        return;
    //    }

    //    UpdateAllNodesColor_ByGlobalAverage();
    //    colorAppliedThisRun = true;
    //}
    public static void ApplyColorsOnceBeforeScreenshot()
    {
        if (colorAppliedThisRun) return;

        ApplyColorsBeforeScreenshot(force: false); // dirtyなら更新、そうでなければ何もしない
        colorAppliedThisRun = true;                // ★ このRunでは二度とやらない
    }


    // ★ 10秒ごとのスクショ用：色を更新できる版
    public static void ApplyColorsBeforeScreenshot(bool force = false)
    {
        // 変化がないなら何もしない（force=trueなら毎回更新）
        if (!force && !colorDirtyThisRun) return;

        UpdateAllNodesColor_ByGlobalAverage();

        // ★ 次の差分検知のためにリセット（OnPassed でまた true になる）
        colorDirtyThisRun = false;
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