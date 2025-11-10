using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class LearningPlayer : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 3f;
    public float cellSize = 1f;
    public float rayDistance = 1f;
    public LayerMask wallLayer;
    public LayerMask nodeLayer;

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward;
    public Vector3 gridOrigin = Vector3.zero;
    public MapNode goalNode;
    public GameObject nodePrefab;

    [Header("探索設定")]
    [Range(0f, 1f)] public float epsilon = 0.2f;
    public int linkRayMaxSteps = 100;

    [Header("デバッグ")]
    public bool debugLog = true;
    public bool debugRay = true;

    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private MapNode currentNode;
    private MapNode prevNode;
    private bool reachedGoal = false;

    private const float EPS = 1e-4f;

    // ======================================================
    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position = SnapToGrid(transform.position);
        currentNode = TryPlaceNode(transform.position);
        prevNode = null;

        if (goalNode == null)
        {
            GameObject goalObj = GameObject.Find("Goal");
            if (goalObj != null)
                goalNode = goalObj.GetComponent<MapNode>();
        }

        if (goalNode != null && currentNode != null)
            currentNode.UpdateValueByGoal(goalNode);

        if (debugLog)
            Debug.Log($"[LearningPlayer:{name}] Start @ {currentNode}");
    }

    void Update()
    {
        if (!isMoving)
        {
            if (CanPlaceNodeHere())
                TryExploreMove();
            else
                MoveForward();
        }
        else
        {
            MoveToTarget();
        }
    }

    // ======================================================
    bool CanPlaceNodeHere()
    {
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

        int openCount = (!frontHit ? 1 : 0) + (!leftHit ? 1 : 0) + (!rightHit ? 1 : 0);
        return (frontHit || openCount >= 2);
    }

    // ======================================================
    //void TryExploreMove()
    //{
    //    currentNode = TryPlaceNode(transform.position);

    //    var dirs = ScanAroundDirections();
    //    if (dirs.Count == 0)
    //    {
    //        Debug.Log("[EXP] dirs.Count == 0 (進行候補なし)");
    //        return;
    //    }

    //    // 直前方向を計算
    //    Vector3? backDirOpt = null;
    //    if (prevNode != null && currentNode != null)
    //    {
    //        Vector3 fromPrev = (currentNode.transform.position - prevNode.transform.position);
    //        if (Mathf.Abs(fromPrev.x) > Mathf.Abs(fromPrev.z))
    //            backDirOpt = (fromPrev.x > 0f) ? Vector3.right : Vector3.left;
    //        else if (Mathf.Abs(fromPrev.z) > 0f)
    //            backDirOpt = (fromPrev.z > 0f) ? Vector3.forward : Vector3.back;
    //    }

    //    string N(MapNode n) => n != null ? $"{n.name}({n.cell.x},{n.cell.y})#{n.GetInstanceID()}" : "null";
    //    Debug.Log($"[EXP] prev={N(prevNode)} curr={N(currentNode)} backDir={(backDirOpt.HasValue ? backDirOpt.Value.ToString() : "none")}");

    //    foreach (var d in dirs)
    //    {
    //        if (d.node != null && goalNode != null)
    //            d.node.UpdateValueByGoal(goalNode);
    //    }

    //    var unexplored = dirs.Where(d => d.node == null || !d.hasLink).ToList();
    //    var known = dirs.Where(d => d.node != null && d.hasLink && d.node != prevNode).ToList();

    //    // ★直前ベクトル方向の除外
    //    if (backDirOpt.HasValue)
    //    {
    //        Vector3 backDir = backDirOpt.Value;
    //        unexplored = unexplored.Where(d => d.dir != backDir).ToList();
    //        known = known.Where(d => d.dir != backDir).ToList();
    //    }

    //    if (debugLog)
    //    {
    //        int knownBefore = dirs.Count(d => d.node != null && d.hasLink);
    //        bool hasPrevObj = dirs.Any(d => d.node == prevNode);
    //        bool hasBackDir = backDirOpt.HasValue && dirs.Any(d => d.dir == backDirOpt.Value);
    //        Debug.Log($"[EXP] unexplored={unexplored.Count}, known(before)={knownBefore}, known(filtered)={known.Count}, hasPrevObj={hasPrevObj}, hasBackDir={hasBackDir}");
    //    }

    //    // ★デッドエンド時フォールバック
    //    if (known.Count == 0 && unexplored.Count == 0 && backDirOpt.HasValue)
    //    {
    //        var back = dirs.FirstOrDefault(d => d.dir == backDirOpt.Value);
    //        if (back.dir != Vector3.zero)
    //        {
    //            known.Add(back);
    //            Debug.LogWarning($"[EXP] dead-end fallback: allow back to prev via dir={back.dir}");
    //        }
    //    }

    //    (Vector3 dir, MapNode node, bool hasLink)? chosen = null;

    //    float r = Random.value;
    //    bool doExplore = (r < epsilon);
    //    if (debugLog) Debug.Log($"[EXP] epsilon={epsilon:F3}, rand={r:F3} -> explore={doExplore}");

    //    if (doExplore)
    //    {
    //        if (unexplored.Count > 0)
    //            chosen = unexplored[Random.Range(0, unexplored.Count)];
    //        else if (known.Count > 0)
    //            chosen = known[Random.Range(0, known.Count)];
    //    }
    //    else
    //    {
    //        if (known.Count > 0)
    //            chosen = known.OrderByDescending(d => d.node.value).First();
    //        else if (unexplored.Count > 0)
    //            chosen = unexplored.OrderByDescending(d => (d.node != null ? d.node.value : float.NegativeInfinity)).First();
    //    }

    //    if (chosen.HasValue)
    //    {
    //        string chosenStr = (chosen.Value.node != null)
    //            ? $"{N(chosen.Value.node)} v={chosen.Value.node.value:F3}"
    //            : "null";
    //        if (debugLog)
    //            Debug.Log($"[EXP] chosen dir={chosen.Value.dir} node={chosenStr} hasLink={chosen.Value.hasLink}");

    //        moveDir = chosen.Value.dir;
    //        MoveForward();
    //    }
    //    else
    //    {
    //        Debug.LogWarning("[EXP] chosen is null (移動候補なし)。prev除外/壁で詰んだ可能性。");
    //    }
    //}
    void TryExploreMove()
    {
        // 現在位置にNodeを配置または取得
        currentNode = TryPlaceNode(transform.position);

        string CurrCell(MapNode n) => n != null ? n.cell.ToString() : "null";
        if (debugLog)
            Debug.Log($"[EXP] === TryExploreMove Start === current={currentNode?.name ?? "null"}{CurrCell(currentNode)} prev={prevNode?.name ?? "null"}{CurrCell(prevNode)}");

        // --------------------------------------------------
        // 周囲方向のスキャン
        // --------------------------------------------------
        var dirs = ScanAroundDirections();
        if (dirs.Count == 0)
        {
            Debug.Log("[EXP] dirs.Count == 0 (進行候補なし)");
            return;
        }

        // --------------------------------------------------
        // 直前方向を推定（prevNodeがある場合）
        // --------------------------------------------------
        Vector3? backDirOpt = null;
        if (prevNode != null && currentNode != null)
        {
            Vector3 fromPrev = (currentNode.transform.position - prevNode.transform.position);
            if (Mathf.Abs(fromPrev.x) > Mathf.Abs(fromPrev.z))
                backDirOpt = (fromPrev.x > 0f) ? Vector3.right : Vector3.left;
            else if (Mathf.Abs(fromPrev.z) > 0f)
                backDirOpt = (fromPrev.z > 0f) ? Vector3.forward : Vector3.back;
        }

        // --------------------------------------------------
        // スキャン結果出力
        // --------------------------------------------------
        if (debugLog)
        {
            foreach (var d in dirs)
            {
                string n = d.node != null ? $"{d.node.name}{CurrCell(d.node)}" : "null";
                Debug.Log($"[EXP] dir={d.dir} node={n} hasLink={d.hasLink}");
            }
        }

        // --------------------------------------------------
        // 候補を分類・prevNode除外
        // --------------------------------------------------
        var unexplored = dirs.Where(d => d.node == null || !d.hasLink).ToList();
        var known = dirs.Where(d => d.node != null && d.hasLink && d.node != prevNode).ToList(); // ★ prevNode除外

        // --------------------------------------------------
        // 戻り方向(backDir)も除外
        // --------------------------------------------------
        if (backDirOpt.HasValue)
        {
            Vector3 backDir = backDirOpt.Value;
            unexplored = unexplored.Where(d => d.dir != backDir).ToList();
            known = known.Where(d => d.dir != backDir).ToList();

            if (debugLog)
                Debug.Log($"[EXP] backDir={backDir} を除外 (prevNode={prevNode?.name ?? "null"}{CurrCell(prevNode)})");
        }

        // --------------------------------------------------
        // 現在の候補数を出力
        // --------------------------------------------------
        if (debugLog)
            Debug.Log($"[EXP] unexplored.Count={unexplored.Count}, known.Count={known.Count} (current={CurrCell(currentNode)} prev={CurrCell(prevNode)})");

        // --------------------------------------------------
        // 袋小路対策：戻りを一時的に許可
        // --------------------------------------------------
        if (known.Count == 0 && unexplored.Count == 0 && backDirOpt.HasValue)
        {
            var back = dirs.FirstOrDefault(d => d.dir == backDirOpt.Value);
            if (back.dir != Vector3.zero)
            {
                known.Add(back);
                Debug.LogWarning($"[EXP] dead-end fallback: allow back to prev via dir={back.dir}");
            }
        }

        // --------------------------------------------------
        // ε-greedy探索方針決定
        // --------------------------------------------------
        float r = Random.value;
        bool doExplore = (r < epsilon);
        if (debugLog)
            Debug.Log($"[EXP] epsilon={epsilon:F3}, rand={r:F3} → explore={doExplore}");

        (Vector3 dir, MapNode node, bool hasLink)? chosen = null;

        if (doExplore)
        {
            if (unexplored.Count > 0)
            {
                chosen = unexplored[Random.Range(0, unexplored.Count)];
                if (debugLog) Debug.Log($"[EXP] [Explore] unexploredから選択 dir={chosen.Value.dir}");
            }
            else if (known.Count > 0)
            {
                chosen = known[Random.Range(0, known.Count)];
                if (debugLog) Debug.Log($"[EXP] [Explore] knownから代替選択 dir={chosen.Value.dir}");
            }
        }
        else
        {
            if (known.Count > 0)
            {
                chosen = known.OrderByDescending(d => d.node?.value ?? -9999f).First();
                if (debugLog) Debug.Log($"[EXP] [Exploit] known中で最大value選択 dir={chosen.Value.dir}");
            }
            else if (unexplored.Count > 0)
            {
                chosen = unexplored.OrderByDescending(d => d.node?.value ?? -9999f).First();
                if (debugLog) Debug.Log($"[EXP] [Exploit] unexplored中で最大value選択 dir={chosen.Value.dir}");
            }
        }

        // --------------------------------------------------
        // 結果適用
        // --------------------------------------------------
        if (chosen.HasValue)
        {
            string chosenStr = (chosen.Value.node != null)
                ? $"{chosen.Value.node.name}{CurrCell(chosen.Value.node)}(v={chosen.Value.node.value:F3})"
                : "null";

            Debug.Log($"[EXP] chosen dir={chosen.Value.dir}, node={chosenStr}, hasLink={chosen.Value.hasLink}, current={CurrCell(currentNode)}, prev={CurrCell(prevNode)}");

            moveDir = chosen.Value.dir;
            MoveForward();
        }
        else
        {
            Debug.LogWarning("[EXP] chosen is null (移動候補なし) → 停止");
        }

        Debug.Log("[EXP] === TryExploreMove End ===");
    }

    // ======================================================
    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
    {
        List<(Vector3, MapNode, bool)> found = new();
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in dirs)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
                continue;

            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
            Vector2Int nextCell = WorldToCell(nextPos);
            MapNode nextNode = MapNode.FindByCell(nextCell);
            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));

            found.Add((dir, nextNode, linked));
        }

        return found;
    }

    // ======================================================
    void MoveForward()
    {
        targetPos = SnapToGrid(transform.position + moveDir * cellSize);
        isMoving = true;
    }

    //// ======================================================
    //void MoveToTarget()
    //{
    //    transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
    //    if (Vector3.Distance(transform.position, targetPos) < EPS)
    //    {
    //        transform.position = targetPos;
    //        isMoving = false;

    //        Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
    //        MapNode nextNode = MapNode.FindByCell(cell);

    //        if (currentNode != null && nextNode != null)
    //        {
    //            currentNode.AddLink(nextNode);
    //            LinkBackWithRay(currentNode);
    //            if (goalNode != null)
    //                nextNode.UpdateValueByGoal(goalNode);
    //        }

    //        // prevNode の更新をここで確実に行う
    //        prevNode = currentNode;
    //        currentNode = nextNode;

    //        if (!reachedGoal && goalNode != null)
    //        {
    //            Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
    //            Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));
    //            if (playerCell == goalCell)
    //            {
    //                reachedGoal = true;
    //                LinkBackWithRay(currentNode);
    //                RecalculateGoalDistance();
    //                Destroy(gameObject);
    //            }
    //        }
    //    }
    //}
    void MoveToTarget()
    {
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.position, targetPos) < EPS)
        {
            transform.position = targetPos;
            isMoving = false;

            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
            MapNode nextNode = MapNode.FindByCell(cell);

            // === リンク確立処理 ===
            if (currentNode != null && nextNode != null)
            {
                // 双方向リンクを張る
                currentNode.AddLink(nextNode);
                LinkBackWithRay(currentNode);

                // 目標ノードへの距離更新
                if (goalNode != null)
                    nextNode.UpdateValueByGoal(goalNode);

                // ★ここがポイント：リンク確立後にprevNodeを更新
                prevNode = currentNode;
            }

            // ★最後にcurrentNodeを切り替える
            currentNode = nextNode;

            // === ゴール判定 ===
            if (!reachedGoal && goalNode != null)
            {
                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));
                if (playerCell == goalCell)
                {
                    reachedGoal = true;
                    LinkBackWithRay(currentNode);
                    RecalculateGoalDistance();
                    Destroy(gameObject);
                }
            }
        }
    }

    // ======================================================
    private void LinkBackWithRay(MapNode node)
    {
        if (node == null) return;

        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        Vector3 backDir = -moveDir.normalized;
        LayerMask mask = wallLayer | nodeLayer;

        for (int step = 1; step <= linkRayMaxSteps; step++)
        {
            float maxDist = cellSize * step;
            if (debugRay)
                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
            {
                int hitLayer = hit.collider.gameObject.layer;

                // 壁なら打ち切り
                if ((wallLayer.value & (1 << hitLayer)) != 0)
                    return;

                // Nodeなら接続
                if ((nodeLayer.value & (1 << hitLayer)) != 0)
                {
                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
                    if (hitNode != null && hitNode != node)
                    {
                        node.AddLink(hitNode);
                        if (debugLog)
                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
                    }
                    return;
                }
            }
        }
    }


    // ======================================================
    void RecalculateGoalDistance()
    {
        if (goalNode == null) return;
        foreach (var n in FindObjectsOfType<MapNode>())
            n.DistanceFromGoal = Mathf.Infinity;

        goalNode.DistanceFromGoal = 0f;
        var frontier = new List<MapNode> { goalNode };

        while (frontier.Count > 0)
        {
            frontier.Sort((a, b) => a.DistanceFromGoal.CompareTo(b.DistanceFromGoal));
            var node = frontier[0];
            frontier.RemoveAt(0);

            foreach (var link in node.links)
            {
                if (link == null) continue;
                float newDist = node.DistanceFromGoal + node.EdgeCost(link);
                if (newDist < link.DistanceFromGoal)
                {
                    link.DistanceFromGoal = newDist;
                    if (!frontier.Contains(link))
                        frontier.Add(link);
                }
            }
        }
    }

    // ======================================================
    MapNode TryPlaceNode(Vector3 pos)
    {
        Vector2Int cell = WorldToCell(SnapToGrid(pos));
        MapNode node;

        if (MapNode.allNodeCells.Contains(cell))
        {
            node = MapNode.FindByCell(cell);
            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
        }
        else
        {
            // ★ prevNode をここで更新する
            //if (currentNode != null)
            //    prevNode = currentNode;

            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);
            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
        }

        if (node != null)
        {
            LinkBackWithRay(node);
            if (goalNode != null)
                node.UpdateValueByGoal(goalNode);
        }

        return node;
    }

    // ======================================================
    Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        int cx = Mathf.RoundToInt(p.x / cellSize);
        int cz = Mathf.RoundToInt(p.z / cellSize);
        return new Vector2Int(cx, cz);
    }

    Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
    }

    Vector3 SnapToGrid(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
    }
}