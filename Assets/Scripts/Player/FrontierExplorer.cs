////using System.Collections.Generic;
////using UnityEngine;

////public class FrontierExplorer : MonoBehaviour
////{
////    [Header("Grid / World")]
////    public float cellSize = 1f;
////    public LayerMask wallLayer;

////    [Header("Node Prefab")]
////    public FrontierNode nodePrefab;

////    [Header("Move (cell-to-cell)")]
////    public float moveSpeed = 4f;
////    public float arriveEpsilon = 0.02f;

////    [Header("Edge Block Check (between cells)")]
////    public float probeHeight = 0.3f;
////    public float probeRadius = 0.25f;
////    public float probePadding = 0.02f;

////    [Header("Wall Cell Check (no node on wall cell)")]
////    public float cellCheckY = 0.5f;
////    public float cellCheckHalfY = 1.0f;

////    [Header("Plan")]
////    public int recalcEveryFrames = 10;
////    public bool avoidImmediateBacktrack = true;

////    [Header("Stop / Destroy")]
////    public int destroyAfterNewNodes = 10;
////    public bool destroySelfOnLimit = true;

////    // 0=F,1=B,2=L,3=R
////    private static readonly Vector2Int[] DirC = { new(0, 1), new(0, -1), new(-1, 0), new(1, 0) };
////    private static readonly int[] Opp = { 1, 0, 3, 2 };

////    private int frameCount;

////    private Vector2Int currentCell;
////    private Vector2Int nextCell;
////    private bool isMoving;
////    private Vector3 moveTargetWorld;

////    private FrontierNode currentNode;
////    private FrontierNode lastNode;

////    private FrontierNode target;
////    private List<FrontierNode> path = new();

////    private int newNodesPlaced = 0; // スポーンNodeは除外して数える

////    private void Start()
////    {
////        currentCell = FrontierNode.WorldToCell(transform.position, cellSize);
////        transform.position = CellCenterWorld(currentCell);

////        currentNode = EnsureNodeAtCell(currentCell, false);
////        if (currentNode == null)
////        {
////            Debug.LogError("[FrontierExplorer] Start cell is wall? Node could not be created.");
////            enabled = false;
////            return;
////        }

////        RecomputeAllNodeStates();
////        target = null;
////        path.Clear();
////    }

////    private void Update()
////    {
////        frameCount++;

////        if (isMoving)
////        {
////            StepMove();
////            return;
////        }

////        if (currentNode == null) return;

////        // ① 今いるセルがフロンティアなら未知セルへ1歩（Nodeは到着後に置く）
////        if (TryPickUnknownNeighborCell(currentNode, out var exploreTo))
////        {
////            StartMoveToCell(exploreTo);
////            return;
////        }

////        // ② フロンティアまで移動（訪問済みセル上だけ）
////        if (target == null || frameCount % Mathf.Max(1, recalcEveryFrames) == 0)
////        {
////            RecomputeAllNodeStates();
////            target = ChooseFrontierTarget(currentNode);
////            path = (target != null) ? BuildPathBFS(currentNode, target) : new List<FrontierNode>();
////        }

////        var nextNode = GetNextStepFromPath(currentNode, path);

////        // パスが無い/作れない → リンクで戻る（待機しない）
////        if (nextNode == null)
////        {
////            if (TryPickFallbackByLinks(out var fallback))
////                StartMoveToCell(fallback);
////            return;
////        }

////        // 「戻るしかないのに戻り禁止」で停止するのを回避
////        if (avoidImmediateBacktrack && lastNode != null && nextNode == lastNode && path.Count <= 2)
////        {
////            bool hasAlt = false;
////            foreach (var n in currentNode.links)
////            {
////                if (n != null && n != lastNode) { hasAlt = true; break; }
////            }

////            if (hasAlt)
////            {
////                if (TryPickFallbackByLinks(out var fallback))
////                    StartMoveToCell(fallback);
////                return;
////            }
////            // lastしか無いなら、そのまま戻りを許可（下へ落とす）
////        }

////        StartMoveToCell(nextNode.cell);
////    }

////    // =========================
////    // Fallback (no waiting)
////    // =========================

////    private bool TryPickFallbackByLinks(out Vector2Int destCell)
////    {
////        destCell = default;

////        if (currentNode == null || currentNode.links == null || currentNode.links.Count == 0)
////            return false;

////        FrontierNode best = null;

////        // まずは last 以外を優先
////        foreach (var n in currentNode.links)
////        {
////            if (n == null) continue;
////            if (lastNode != null && n == lastNode) continue;

////            best = n;
////            break;
////        }

////        // last 以外が無いなら last に戻る
////        if (best == null && lastNode != null && currentNode.links.Contains(lastNode))
////            best = lastNode;

////        if (best == null)
////            best = currentNode.links[0];

////        destCell = best.cell;
////        return true;
////    }

////    // =========================
////    // Move (cell-to-cell)
////    // =========================

////    private void StartMoveToCell(Vector2Int destCell)
////    {
////        // 壁セルには行かない
////        if (IsWallCell(CellCenterWorld(destCell)))
////        {
////            MarkBlockedBetween(currentNode, destCell);
////            RecomputeAllNodeStates();
////            target = null;
////            path.Clear();
////            return;
////        }

////        // 通路が塞がれているなら進まない（止まらず blocked 記録）
////        if (IsEdgeBlocked(currentCell, destCell, out _))
////        {
////            MarkBlockedBetween(currentNode, destCell);
////            RecomputeAllNodeStates();
////            target = null;
////            path.Clear();
////            return;
////        }

////        nextCell = destCell;
////        moveTargetWorld = CellCenterWorld(destCell);
////        isMoving = true;
////    }

////    private void StepMove()
////    {
////        transform.position = Vector3.MoveTowards(transform.position, moveTargetWorld, moveSpeed * Time.deltaTime);

////        if (Vector3.Distance(transform.position, moveTargetWorld) > arriveEpsilon)
////            return;

////        isMoving = false;

////        lastNode = currentNode;
////        currentCell = nextCell;

////        // 到着セルにだけNode設置（ここはカウント対象）
////        var arrivedNode = EnsureNodeAtCell(currentCell, true);

////        // Destroy/disable が走った場合は以降触らない
////        if (this == null || !enabled) return;

////        if (arrivedNode == null) return;

////        currentNode = arrivedNode;

////        // ★リンクは「実際に移動した方向」だけ（ここでのみ張る）
////        if (lastNode != null && currentNode != null && lastNode != currentNode)
////        {
////            if (IsEdgeBlocked(lastNode.cell, currentNode.cell, out _))
////            {
////                int dirIndex = DirIndexFromDelta(currentNode.cell - lastNode.cell);
////                if (dirIndex >= 0)
////                {
////                    lastNode.MarkBlockedIndex(dirIndex);
////                    currentNode.MarkBlockedIndex(Opp[dirIndex]);
////                }
////                lastNode.UnlinkWith(currentNode);
////            }
////            else
////            {
////                lastNode.LinkWith(currentNode);
////            }
////        }

////        RecomputeAllNodeStates();
////        target = null;
////        path.Clear();
////    }

////    // =========================
////    // Node placement (NO auto-link)
////    // =========================

////    private FrontierNode EnsureNodeAtCell(Vector2Int cell, bool countTowardLimit)
////    {
////        if (FrontierNode.CellMap.TryGetValue(cell, out var exist) && exist != null)
////            return exist;

////        var world = CellCenterWorld(cell);

////        if (IsWallCell(world))
////            return null;

////        if (nodePrefab == null)
////        {
////            Debug.LogError("[FrontierExplorer] nodePrefab is not assigned.");
////            return null;
////        }

////        var n = Instantiate(nodePrefab, world, Quaternion.identity);

////        // ★Prefab側に残ってても事故らないようにクリア
////        if (n.links != null) n.links.Clear();

////        // ★リンク可視化のRaycast用に、同じWallLayerを渡す
////        n.debugWallLayer = wallLayer;

////        n.RegisterCell(cellSize);

////        // 新規生成カウント（スポーン位置は false で呼んでいる）
////        if (countTowardLimit)
////        {
////            newNodesPlaced++;

////            if (newNodesPlaced >= destroyAfterNewNodes)
////            {
////                Debug.Log($"[FrontierExplorer] New nodes placed reached {newNodesPlaced}. Finish.");

////                if (destroySelfOnLimit) Destroy(gameObject);
////                else enabled = false;
////            }
////        }

////        // ★重要：ここでは隣接Nodeへ自動リンクしない
////        return n;
////    }

////    // =========================
////    // Frontier logic
////    // =========================

////    private void RecomputeAllNodeStates()
////    {
////        foreach (var n in FrontierNode.All)
////            RecomputeNodeState(n);
////    }

////    private void RecomputeNodeState(FrontierNode n)
////    {
////        if (n == null) return;

////        n.unknownCount = 0;
////        n.wallCount = 0;

////        for (int i = 0; i < 4; i++)
////        {
////            if (n.IsBlockedIndex(i))
////            {
////                n.wallCount++;
////                continue;
////            }

////            var nbCell = n.cell + DirC[i];
////            var nbWorld = CellCenterWorld(nbCell);

////            // 壁セルなら blocked
////            if (IsWallCell(nbWorld))
////            {
////                n.MarkBlockedIndex(i);
////                n.wallCount++;
////                continue;
////            }

////            // 訪問済みなら unknown ではない（ただし塞がれなら blocked へ）
////            if (FrontierNode.CellMap.TryGetValue(nbCell, out var nb) && nb != null)
////            {
////                if (IsEdgeBlocked(n.cell, nbCell, out _))
////                {
////                    n.MarkBlockedIndex(i);
////                    nb.MarkBlockedIndex(Opp[i]);
////                    n.UnlinkWith(nb); // 既にリンクがあれば切る（安全側）
////                    n.wallCount++;
////                }
////                continue;
////            }

////            // 未訪問セル：通路が塞がれてなければ unknown
////            if (IsEdgeBlocked(n.cell, nbCell, out _))
////            {
////                n.MarkBlockedIndex(i);
////                n.wallCount++;
////                continue;
////            }

////            n.unknownCount++;
////        }
////    }

////    private FrontierNode ChooseFrontierTarget(FrontierNode start)
////    {
////        var dist = BFSDistanceMap(start);

////        FrontierNode best = null;
////        int bestDist = int.MaxValue;

////        foreach (var n in FrontierNode.All)
////        {
////            if (n == null) continue;
////            if (n.unknownCount <= 0) continue;
////            if (!dist.TryGetValue(n, out int d)) continue;

////            if (d < bestDist)
////            {
////                bestDist = d;
////                best = n;
////            }
////        }

////        return best;
////    }

////    private bool TryPickUnknownNeighborCell(FrontierNode from, out Vector2Int destCell)
////    {
////        for (int i = 0; i < 4; i++)
////        {
////            if (from.IsBlockedIndex(i)) continue;

////            var nbCell = from.cell + DirC[i];
////            var nbWorld = CellCenterWorld(nbCell);

////            if (IsWallCell(nbWorld))
////            {
////                from.MarkBlockedIndex(i);
////                continue;
////            }

////            // 訪問済みなら未知ではない
////            if (FrontierNode.CellMap.ContainsKey(nbCell))
////                continue;

////            // 通路が塞がれていたら blocked
////            if (IsEdgeBlocked(from.cell, nbCell, out _))
////            {
////                from.MarkBlockedIndex(i);
////                continue;
////            }

////            destCell = nbCell;
////            return true;
////        }

////        destCell = default;
////        return false;
////    }

////    // =========================
////    // Path (BFS on visited nodes)
////    // =========================

////    private Dictionary<FrontierNode, int> BFSDistanceMap(FrontierNode start)
////    {
////        var dist = new Dictionary<FrontierNode, int>();
////        var q = new Queue<FrontierNode>();

////        dist[start] = 0;
////        q.Enqueue(start);

////        while (q.Count > 0)
////        {
////            var v = q.Dequeue();
////            int dv = dist[v];

////            foreach (var u in v.links)
////            {
////                if (u == null) continue;
////                if (dist.ContainsKey(u)) continue;
////                dist[u] = dv + 1;
////                q.Enqueue(u);
////            }
////        }

////        return dist;
////    }

////    private List<FrontierNode> BuildPathBFS(FrontierNode start, FrontierNode goal)
////    {
////        var prev = new Dictionary<FrontierNode, FrontierNode>();
////        var q = new Queue<FrontierNode>();
////        var visited = new HashSet<FrontierNode>();

////        visited.Add(start);
////        q.Enqueue(start);

////        while (q.Count > 0)
////        {
////            var v = q.Dequeue();
////            if (v == goal) break;

////            foreach (var u in v.links)
////            {
////                if (u == null) continue;
////                if (visited.Contains(u)) continue;
////                visited.Add(u);
////                prev[u] = v;
////                q.Enqueue(u);
////            }
////        }

////        if (!visited.Contains(goal)) return new List<FrontierNode>();

////        var result = new List<FrontierNode>();
////        var cur = goal;
////        result.Add(cur);

////        while (cur != start)
////        {
////            cur = prev[cur];
////            result.Add(cur);
////        }

////        result.Reverse();
////        return result;
////    }

////    private FrontierNode GetNextStepFromPath(FrontierNode cur, List<FrontierNode> p)
////    {
////        if (p == null || p.Count == 0) return null;
////        if (p[0] != cur) return null;
////        if (p.Count == 1) return null;
////        return p[1];
////    }

////    // =========================
////    // Wall checks
////    // =========================

////    private Vector3 CellCenterWorld(Vector2Int c)
////    {
////        return FrontierNode.CellToWorld(c, cellSize, transform.position.y);
////    }

////    private bool IsWallCell(Vector3 cellCenterWorld)
////    {
////        Vector3 center = cellCenterWorld + Vector3.up * cellCheckY;
////        Vector3 halfExt = new Vector3(cellSize * 0.45f, cellCheckHalfY, cellSize * 0.45f);

////        return Physics.CheckBox(center, halfExt, Quaternion.identity, wallLayer, QueryTriggerInteraction.Ignore);
////    }

////    private bool IsEdgeBlocked(Vector2Int fromCell, Vector2Int toCell, out RaycastHit hit)
////    {
////        Vector3 from = CellCenterWorld(fromCell);
////        Vector3 to = CellCenterWorld(toCell);

////        Vector3 delta = to - from;
////        float dist = delta.magnitude;

////        if (dist <= 0.0001f)
////        {
////            hit = default;
////            return false;
////        }

////        Vector3 dir = delta / dist;
////        Vector3 origin = from + Vector3.up * probeHeight;
////        float checkDist = Mathf.Max(dist - probePadding, 0f);

////        return Physics.SphereCast(origin, probeRadius, dir, out hit, checkDist, wallLayer, QueryTriggerInteraction.Ignore);
////    }

////    private void MarkBlockedBetween(FrontierNode fromNode, Vector2Int destCell)
////    {
////        if (fromNode == null) return;

////        Vector2Int delta = destCell - fromNode.cell;
////        int idx = DirIndexFromDelta(delta);
////        if (idx < 0) return;

////        fromNode.MarkBlockedIndex(idx);

////        if (FrontierNode.CellMap.TryGetValue(destCell, out var toNode) && toNode != null)
////            toNode.MarkBlockedIndex(Opp[idx]);
////    }

////    private int DirIndexFromDelta(Vector2Int d)
////    {
////        if (d == DirC[0]) return 0;
////        if (d == DirC[1]) return 1;
////        if (d == DirC[2]) return 2;
////        if (d == DirC[3]) return 3;
////        return -1;
////    }
////}


//using System.Collections.Generic;
//using UnityEngine;

//public class FrontierExplorer : MonoBehaviour
//{
//    [Header("Grid / World")]
//    public float cellSize = 1f;
//    public LayerMask wallLayer;

//    [Header("Node Prefab")]
//    public FrontierNode nodePrefab;

//    [Header("Move (cell-to-cell)")]
//    public float moveSpeed = 4f;
//    public float arriveEpsilon = 0.02f;

//    [Header("Edge Block Check (between cells)")]
//    public float probeHeight = 0.3f;
//    public float probeRadius = 0.25f;
//    public float probePadding = 0.02f;

//    [Header("Wall Cell Check (no node on wall cell)")]
//    public float cellCheckY = 0.5f;
//    public float cellCheckHalfY = 1.0f;

//    [Header("Plan")]
//    public int recalcEveryFrames = 10;
//    public bool avoidImmediateBacktrack = true;

//    [Header("Stop / Destroy")]
//    public int destroyAfterNewNodes = 10;
//    public bool destroySelfOnLimit = true;

//    // 0=F,1=B,2=L,3=R
//    private static readonly Vector2Int[] DirC = { new(0, 1), new(0, -1), new(-1, 0), new(1, 0) };
//    private static readonly int[] Opp = { 1, 0, 3, 2 };

//    private int frameCount;

//    private Vector2Int currentCell;
//    private Vector2Int nextCell;
//    private bool isMoving;
//    private Vector3 moveTargetWorld;

//    private FrontierNode currentNode;
//    private FrontierNode lastNode;

//    private FrontierNode target;
//    private List<FrontierNode> path = new();

//    private int newNodesPlaced = 0; // スポーンNodeは除外して数える

//    private void Start()
//    {
//        currentCell = FrontierNode.WorldToCell(transform.position, cellSize);
//        transform.position = CellCenterWorld(currentCell);

//        currentNode = EnsureNodeAtCell(currentCell, false);
//        if (currentNode == null)
//        {
//            Debug.LogError("[FrontierExplorer] Start cell is wall? Node could not be created.");
//            enabled = false;
//            return;
//        }

//        RecomputeAllNodeStates();
//        target = null;
//        path.Clear();
//    }

//    private void Update()
//    {
//        frameCount++;

//        if (isMoving)
//        {
//            StepMove();
//            return;
//        }

//        if (currentNode == null) return;

//        // ① 今いるセルがフロンティアなら未知セルへ1歩（Nodeは到着後に置く）
//        if (TryPickUnknownNeighborCell(currentNode, out var exploreTo))
//        {
//            StartMoveToCell(exploreTo);
//            return;
//        }

//        // ② フロンティアまで移動（訪問済みセル上だけ）
//        if (target == null || frameCount % Mathf.Max(1, recalcEveryFrames) == 0)
//        {
//            RecomputeAllNodeStates();
//            target = ChooseFrontierTarget(currentNode);
//            path = (target != null) ? BuildPathBFS(currentNode, target) : new List<FrontierNode>();
//        }

//        var nextNode = GetNextStepFromPath(currentNode, path);

//        // パスが無い/作れない → リンクで戻る（待機しない）
//        if (nextNode == null)
//        {
//            if (TryPickFallbackByLinks(out var fallback))
//                StartMoveToCell(fallback);
//            return;
//        }

//        // 「戻るしかないのに戻り禁止」で停止するのを回避
//        if (avoidImmediateBacktrack && lastNode != null && nextNode == lastNode && path.Count <= 2)
//        {
//            bool hasAlt = false;
//            foreach (var n in currentNode.links)
//            {
//                if (n != null && n != lastNode) { hasAlt = true; break; }
//            }

//            if (hasAlt)
//            {
//                if (TryPickFallbackByLinks(out var fallback))
//                    StartMoveToCell(fallback);
//                return;
//            }
//            // lastしか無いなら、そのまま戻りを許可（下へ落とす）
//        }

//        StartMoveToCell(nextNode.cell);
//    }

//    // =========================
//    // Fallback (no waiting)
//    // =========================

//    private bool TryPickFallbackByLinks(out Vector2Int destCell)
//    {
//        destCell = default;

//        if (currentNode == null || currentNode.links == null || currentNode.links.Count == 0)
//            return false;

//        FrontierNode best = null;

//        // まずは last 以外を優先
//        foreach (var n in currentNode.links)
//        {
//            if (n == null) continue;
//            if (lastNode != null && n == lastNode) continue;

//            best = n;
//            break;
//        }

//        // last 以外が無いなら last に戻る
//        if (best == null && lastNode != null && currentNode.links.Contains(lastNode))
//            best = lastNode;

//        if (best == null)
//            best = currentNode.links[0];

//        destCell = best.cell;
//        return true;
//    }

//    // =========================
//    // Move (cell-to-cell)
//    // =========================

//    private void StartMoveToCell(Vector2Int destCell)
//    {
//        // 壁セルには行かない
//        if (IsWallCell(CellCenterWorld(destCell)))
//        {
//            MarkBlockedBetween(currentNode, destCell);
//            RecomputeAllNodeStates();
//            target = null;
//            path.Clear();
//            return;
//        }

//        // 通路が塞がれているなら進まない（止まらず blocked 記録）
//        if (IsEdgeBlocked(currentCell, destCell, out _))
//        {
//            MarkBlockedBetween(currentNode, destCell);
//            RecomputeAllNodeStates();
//            target = null;
//            path.Clear();
//            return;
//        }

//        nextCell = destCell;
//        moveTargetWorld = CellCenterWorld(destCell);
//        isMoving = true;
//    }

//    private void StepMove()
//    {
//        transform.position = Vector3.MoveTowards(transform.position, moveTargetWorld, moveSpeed * Time.deltaTime);

//        if (Vector3.Distance(transform.position, moveTargetWorld) > arriveEpsilon)
//            return;

//        isMoving = false;

//        lastNode = currentNode;
//        currentCell = nextCell;

//        // 到着セルにだけNode設置（ここはカウント対象）
//        var arrivedNode = EnsureNodeAtCell(currentCell, true);

//        // Destroy/disable が走った場合は以降触らない
//        if (this == null || !enabled) return;

//        if (arrivedNode == null) return;

//        currentNode = arrivedNode;

//        // ★リンクは「実際に移動した方向」だけ（ここでのみ張る）
//        if (lastNode != null && currentNode != null && lastNode != currentNode)
//        {
//            int dirIndex = DirIndexFromDelta(currentNode.cell - lastNode.cell);
//            if (dirIndex >= 0)
//            {
//                // ★移動成功＝通行実績。blockedは必ず解除しておく（最優先の真実）
//                lastNode.ClearBlockedIndex(dirIndex);
//                currentNode.ClearBlockedIndex(Opp[dirIndex]);
//            }

//            // 念のため：もしここで塞がれている判定が出ても、実際に通れたならリンク優先にしたいなら
//            // IsEdgeBlockedチェック自体を外す手もある。今回は「動的に壁が出る」可能性も考えて残す。
//            if (IsEdgeBlocked(lastNode.cell, currentNode.cell, out _))
//            {
//                if (dirIndex >= 0)
//                {
//                    lastNode.MarkBlockedIndex(dirIndex);
//                    currentNode.MarkBlockedIndex(Opp[dirIndex]);
//                }
//                lastNode.UnlinkWith(currentNode);
//            }
//            else
//            {
//                lastNode.LinkWith(currentNode);
//            }
//        }

//        RecomputeAllNodeStates();
//        target = null;
//        path.Clear();
//    }

//    // =========================
//    // Node placement (NO auto-link)
//    // =========================

//    private FrontierNode EnsureNodeAtCell(Vector2Int cell, bool countTowardLimit)
//    {
//        if (FrontierNode.CellMap.TryGetValue(cell, out var exist) && exist != null)
//            return exist;

//        var world = CellCenterWorld(cell);

//        if (IsWallCell(world))
//            return null;

//        if (nodePrefab == null)
//        {
//            Debug.LogError("[FrontierExplorer] nodePrefab is not assigned.");
//            return null;
//        }

//        var n = Instantiate(nodePrefab, world, Quaternion.identity);

//        // Prefab側に残ってても事故らないようにクリア
//        if (n.links != null) n.links.Clear();

//        // リンク可視化のRaycast用に、同じWallLayerを渡す
//        n.debugWallLayer = wallLayer;

//        n.RegisterCell(cellSize);

//        // 新規生成カウント（スポーン位置は false で呼んでいる）
//        if (countTowardLimit)
//        {
//            newNodesPlaced++;

//            if (newNodesPlaced >= destroyAfterNewNodes)
//            {
//                Debug.Log($"[FrontierExplorer] New nodes placed reached {newNodesPlaced}. Finish.");

//                if (destroySelfOnLimit) Destroy(gameObject);
//                else enabled = false;
//            }
//        }

//        // ★重要：ここでは隣接Nodeへ自動リンクしない
//        return n;
//    }

//    // =========================
//    // Frontier logic
//    // =========================

//    private void RecomputeAllNodeStates()
//    {
//        foreach (var n in FrontierNode.All)
//            RecomputeNodeState(n);
//    }

//    private void RecomputeNodeState(FrontierNode n)
//    {
//        if (n == null) return;

//        n.unknownCount = 0;
//        n.wallCount = 0;

//        for (int i = 0; i < 4; i++)
//        {
//            // ★linksがある方向は「通行実績」なので blocked より優先（境界判定でも既知扱い）
//            var nbCell = n.cell + DirC[i];
//            if (FrontierNode.CellMap.TryGetValue(nbCell, out var nbLinkCheck) && nbLinkCheck != null)
//            {
//                if (n.links != null && n.links.Contains(nbLinkCheck))
//                {
//                    n.ClearBlockedIndex(i);
//                    nbLinkCheck.ClearBlockedIndex(Opp[i]);
//                    continue;
//                }
//            }

//            if (n.IsBlockedIndex(i))
//            {
//                n.wallCount++;
//                continue;
//            }

//            var nbWorld = CellCenterWorld(nbCell);

//            // 壁セルなら blocked
//            if (IsWallCell(nbWorld))
//            {
//                n.MarkBlockedIndex(i);
//                n.wallCount++;
//                continue;
//            }

//            // 訪問済みなら unknown ではない（ただし未リンクなら塞がれ判定でblockedへ）
//            if (FrontierNode.CellMap.TryGetValue(nbCell, out var nb) && nb != null)
//            {
//                if (IsEdgeBlocked(n.cell, nbCell, out _))
//                {
//                    n.MarkBlockedIndex(i);
//                    nb.MarkBlockedIndex(Opp[i]);
//                    n.UnlinkWith(nb); // もしリンクがあれば切る
//                    n.wallCount++;
//                }
//                continue;
//            }

//            // 未訪問セル：通路が塞がれてなければ unknown
//            if (IsEdgeBlocked(n.cell, nbCell, out _))
//            {
//                n.MarkBlockedIndex(i);
//                n.wallCount++;
//                continue;
//            }

//            n.unknownCount++;
//        }
//    }

//    private FrontierNode ChooseFrontierTarget(FrontierNode start)
//    {
//        var dist = BFSDistanceMap(start);

//        FrontierNode best = null;
//        int bestDist = int.MaxValue;

//        foreach (var n in FrontierNode.All)
//        {
//            if (n == null) continue;
//            if (n.unknownCount <= 0) continue;
//            if (!dist.TryGetValue(n, out int d)) continue;

//            if (d < bestDist)
//            {
//                bestDist = d;
//                best = n;
//            }
//        }

//        return best;
//    }

//    private bool TryPickUnknownNeighborCell(FrontierNode from, out Vector2Int destCell)
//    {
//        for (int i = 0; i < 4; i++)
//        {
//            // linksがある方向は既知（未知に行かない）
//            var nbCell0 = from.cell + DirC[i];
//            if (FrontierNode.CellMap.TryGetValue(nbCell0, out var nb0) && nb0 != null)
//            {
//                if (from.links != null && from.links.Contains(nb0))
//                    continue;
//            }

//            if (from.IsBlockedIndex(i)) continue;

//            var nbCell = from.cell + DirC[i];
//            var nbWorld = CellCenterWorld(nbCell);

//            if (IsWallCell(nbWorld))
//            {
//                from.MarkBlockedIndex(i);
//                continue;
//            }

//            // 訪問済みなら未知ではない
//            if (FrontierNode.CellMap.ContainsKey(nbCell))
//                continue;

//            // 通路が塞がれていたら blocked
//            if (IsEdgeBlocked(from.cell, nbCell, out _))
//            {
//                from.MarkBlockedIndex(i);
//                continue;
//            }

//            destCell = nbCell;
//            return true;
//        }

//        destCell = default;
//        return false;
//    }

//    // =========================
//    // Path (BFS on visited nodes)
//    // =========================

//    private Dictionary<FrontierNode, int> BFSDistanceMap(FrontierNode start)
//    {
//        var dist = new Dictionary<FrontierNode, int>();
//        var q = new Queue<FrontierNode>();

//        dist[start] = 0;
//        q.Enqueue(start);

//        while (q.Count > 0)
//        {
//            var v = q.Dequeue();
//            int dv = dist[v];

//            foreach (var u in v.links)
//            {
//                if (u == null) continue;
//                if (dist.ContainsKey(u)) continue;
//                dist[u] = dv + 1;
//                q.Enqueue(u);
//            }
//        }

//        return dist;
//    }

//    private List<FrontierNode> BuildPathBFS(FrontierNode start, FrontierNode goal)
//    {
//        var prev = new Dictionary<FrontierNode, FrontierNode>();
//        var q = new Queue<FrontierNode>();
//        var visited = new HashSet<FrontierNode>();

//        visited.Add(start);
//        q.Enqueue(start);

//        while (q.Count > 0)
//        {
//            var v = q.Dequeue();
//            if (v == goal) break;

//            foreach (var u in v.links)
//            {
//                if (u == null) continue;
//                if (visited.Contains(u)) continue;
//                visited.Add(u);
//                prev[u] = v;
//                q.Enqueue(u);
//            }
//        }

//        if (!visited.Contains(goal)) return new List<FrontierNode>();

//        var result = new List<FrontierNode>();
//        var cur = goal;
//        result.Add(cur);

//        while (cur != start)
//        {
//            cur = prev[cur];
//            result.Add(cur);
//        }

//        result.Reverse();
//        return result;
//    }

//    private FrontierNode GetNextStepFromPath(FrontierNode cur, List<FrontierNode> p)
//    {
//        if (p == null || p.Count == 0) return null;
//        if (p[0] != cur) return null;
//        if (p.Count == 1) return null;
//        return p[1];
//    }

//    // =========================
//    // Wall checks
//    // =========================

//    private Vector3 CellCenterWorld(Vector2Int c)
//    {
//        return FrontierNode.CellToWorld(c, cellSize, transform.position.y);
//    }

//    private bool IsWallCell(Vector3 cellCenterWorld)
//    {
//        Vector3 center = cellCenterWorld + Vector3.up * cellCheckY;
//        Vector3 halfExt = new Vector3(cellSize * 0.45f, cellCheckHalfY, cellSize * 0.45f);

//        return Physics.CheckBox(center, halfExt, Quaternion.identity, wallLayer, QueryTriggerInteraction.Ignore);
//    }

//    private bool IsEdgeBlocked(Vector2Int fromCell, Vector2Int toCell, out RaycastHit hit)
//    {
//        Vector3 from = CellCenterWorld(fromCell);
//        Vector3 to = CellCenterWorld(toCell);

//        Vector3 delta = to - from;
//        float dist = delta.magnitude;

//        if (dist <= 0.0001f)
//        {
//            hit = default;
//            return false;
//        }

//        Vector3 dir = delta / dist;
//        Vector3 origin = from + Vector3.up * probeHeight;
//        float checkDist = Mathf.Max(dist - probePadding, 0f);

//        return Physics.SphereCast(origin, probeRadius, dir, out hit, checkDist, wallLayer, QueryTriggerInteraction.Ignore);
//    }

//    private void MarkBlockedBetween(FrontierNode fromNode, Vector2Int destCell)
//    {
//        if (fromNode == null) return;

//        Vector2Int delta = destCell - fromNode.cell;
//        int idx = DirIndexFromDelta(delta);
//        if (idx < 0) return;

//        fromNode.MarkBlockedIndex(idx);

//        if (FrontierNode.CellMap.TryGetValue(destCell, out var toNode) && toNode != null)
//            toNode.MarkBlockedIndex(Opp[idx]);
//    }

//    private int DirIndexFromDelta(Vector2Int d)
//    {
//        if (d == DirC[0]) return 0;
//        if (d == DirC[1]) return 1;
//        if (d == DirC[2]) return 2;
//        if (d == DirC[3]) return 3;
//        return -1;
//    }
//}

using System.Collections.Generic;
using UnityEngine;

public class FrontierExplorer : MonoBehaviour
{
    [Header("Grid / World")]
    public float cellSize = 1f;
    public LayerMask wallLayer;

    [Header("Node Prefab")]
    public FrontierNode nodePrefab;

    [Header("Move (cell-to-cell)")]
    public float moveSpeed = 4f;
    public float arriveEpsilon = 0.02f;

    [Header("Edge Block Check (between cells)")]
    public float probeHeight = 0.3f;
    public float probeRadius = 0.25f;
    public float probePadding = 0.02f;

    [Header("Wall Cell Check (no node on wall cell)")]
    public float cellCheckY = 0.5f;
    public float cellCheckHalfY = 1.0f;

    [Header("Plan")]
    public int recalcEveryFrames = 10;
    public bool avoidImmediateBacktrack = true;

    [Header("Stop / Destroy")]
    public int destroyAfterNewNodes = 10;
    public bool destroySelfOnLimit = true;

    // 0=F,1=B,2=L,3=R
    private static readonly Vector2Int[] DirC = { new(0, 1), new(0, -1), new(-1, 0), new(1, 0) };
    private static readonly int[] Opp = { 1, 0, 3, 2 };

    private int frameCount;

    private Vector2Int currentCell;
    private Vector2Int nextCell;
    private bool isMoving;
    private Vector3 moveTargetWorld;

    private FrontierNode currentNode;
    private FrontierNode lastNode;

    private FrontierNode target;
    private List<FrontierNode> path = new();

    private int newNodesPlaced = 0; // スポーンNodeは除外して数える

    private void Start()
    {
        currentCell = FrontierNode.WorldToCell(transform.position, cellSize);
        transform.position = CellCenterWorld(currentCell);

        currentNode = EnsureNodeAtCell(currentCell, false);
        if (currentNode == null)
        {
            Debug.LogError("[FrontierExplorer] Start cell is wall? Node could not be created.");
            enabled = false;
            return;
        }

        RecomputeAllNodeStates();
        target = null;
        path.Clear();
    }

    private void Update()
    {
        frameCount++;

        if (isMoving)
        {
            StepMove();
            return;
        }

        if (currentNode == null) return;

        // ① 今いるNodeから「未リンクで通れそう」な方向があれば、そこへ1歩（既知化=リンク化しに行く）
        if (TryPickUnknownNeighborCell(currentNode, out var exploreTo))
        {
            StartMoveToCell(exploreTo);
            return;
        }

        // ② フロンティアまで移動（訪問済みセル上=links上だけ）
        if (target == null || frameCount % Mathf.Max(1, recalcEveryFrames) == 0)
        {
            RecomputeAllNodeStates();
            target = ChooseFrontierTarget(currentNode);
            path = (target != null) ? BuildPathBFS(currentNode, target) : new List<FrontierNode>();
        }

        var nextNode = GetNextStepFromPath(currentNode, path);

        // パスが無い/作れない → linksで戻る（待機しない）
        if (nextNode == null)
        {
            if (TryPickFallbackByLinks(out var fallback))
                StartMoveToCell(fallback);
            return;
        }

        // 「戻るしかないのに戻り禁止」で停止するのを回避
        if (avoidImmediateBacktrack && lastNode != null && nextNode == lastNode && path.Count <= 2)
        {
            bool hasAlt = false;
            foreach (var n in currentNode.links)
            {
                if (n != null && n != lastNode) { hasAlt = true; break; }
            }

            if (hasAlt)
            {
                if (TryPickFallbackByLinks(out var fallback))
                    StartMoveToCell(fallback);
                return;
            }
            // lastしか無いなら、そのまま戻りを許可（下へ落とす）
        }

        StartMoveToCell(nextNode.cell);
    }

    // =========================
    // Fallback (no waiting)
    // =========================

    private bool TryPickFallbackByLinks(out Vector2Int destCell)
    {
        destCell = default;

        if (currentNode == null || currentNode.links == null || currentNode.links.Count == 0)
            return false;

        FrontierNode best = null;

        // まずは last 以外を優先
        foreach (var n in currentNode.links)
        {
            if (n == null) continue;
            if (lastNode != null && n == lastNode) continue;

            best = n;
            break;
        }

        // last 以外が無いなら last に戻る
        if (best == null && lastNode != null && currentNode.links.Contains(lastNode))
            best = lastNode;

        if (best == null)
            best = currentNode.links[0];

        destCell = best.cell;
        return true;
    }

    // =========================
    // Move (cell-to-cell)
    // =========================

    private void StartMoveToCell(Vector2Int destCell)
    {
        // 壁セルには行かない
        if (IsWallCell(CellCenterWorld(destCell)))
        {
            MarkBlockedBetween(currentNode, destCell);
            RecomputeAllNodeStates();
            target = null;
            path.Clear();
            return;
        }

        // セル間が塞がれているなら進まない
        if (IsEdgeBlocked(currentCell, destCell, out _))
        {
            MarkBlockedBetween(currentNode, destCell);
            RecomputeAllNodeStates();
            target = null;
            path.Clear();
            return;
        }

        nextCell = destCell;
        moveTargetWorld = CellCenterWorld(destCell);
        isMoving = true;
    }

    private void StepMove()
    {
        transform.position = Vector3.MoveTowards(transform.position, moveTargetWorld, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, moveTargetWorld) > arriveEpsilon)
            return;

        isMoving = false;

        lastNode = currentNode;
        currentCell = nextCell;

        // 到着セルにだけNode設置（新規生成だけカウント）
        var arrivedNode = EnsureNodeAtCell(currentCell, true);

        // Destroy/disable が走った場合は以降触らない
        if (this == null || !enabled) return;

        if (arrivedNode == null) return;

        currentNode = arrivedNode;

        // ★「到着できた」＝その辺は通れる（通行実績を最優先）→ ここで確定リンク
        if (lastNode != null && currentNode != null && lastNode != currentNode)
        {
            int dirIndex = DirIndexFromDelta(currentNode.cell - lastNode.cell);
            if (dirIndex >= 0)
            {
                // 移動成功した方向は blocked を必ず解除
                lastNode.ClearBlockedIndex(dirIndex);
                currentNode.ClearBlockedIndex(Opp[dirIndex]);
            }

            // ここでは IsEdgeBlocked でリンクを切らない（通行実績=真実）
            lastNode.LinkWith(currentNode);
        }

        RecomputeAllNodeStates();
        target = null;
        path.Clear();
    }

    // =========================
    // Node placement (NO auto-link)
    // =========================

    private FrontierNode EnsureNodeAtCell(Vector2Int cell, bool countTowardLimit)
    {
        if (FrontierNode.CellMap.TryGetValue(cell, out var exist) && exist != null)
            return exist;

        var world = CellCenterWorld(cell);

        if (IsWallCell(world))
            return null;

        if (nodePrefab == null)
        {
            Debug.LogError("[FrontierExplorer] nodePrefab is not assigned.");
            return null;
        }

        var n = Instantiate(nodePrefab, world, Quaternion.identity);

        // Prefab側に残ってても事故らないようにクリア
        if (n.links != null) n.links.Clear();

        // リンク可視化のRaycast用に、同じWallLayerを渡す（FrontierNode側にdebugWallLayerがある場合）
        n.debugWallLayer = wallLayer;

        n.RegisterCell(cellSize);

        // 新規生成カウント（スポーン位置は false で呼んでいる）
        if (countTowardLimit)
        {
            newNodesPlaced++;

            if (newNodesPlaced >= destroyAfterNewNodes)
            {
                Debug.Log($"[FrontierExplorer] New nodes placed reached {newNodesPlaced}. Finish.");

                if (destroySelfOnLimit) Destroy(gameObject);
                else enabled = false;
            }
        }

        // ★重要：ここでは隣接Nodeへ自動リンクしない
        return n;
    }

    // =========================
    // Frontier logic
    // =========================

    private void RecomputeAllNodeStates()
    {
        foreach (var n in FrontierNode.All)
            RecomputeNodeState(n);
    }

    /// <summary>
    /// ★既知の定義を「linksがある方向のみ」に統一
    /// - linksがある方向：既知（確実に通れる）
    /// - linksが無い方向：未確定（通れそうなら unknownCount++ / 塞がれなら blocked）
    /// </summary>
    private void RecomputeNodeState(FrontierNode n)
    {
        if (n == null) return;

        n.unknownCount = 0;
        n.wallCount = 0;

        for (int i = 0; i < 4; i++)
        {
            var nbCell = n.cell + DirC[i];

            FrontierNode nbNode = null;
            FrontierNode.CellMap.TryGetValue(nbCell, out nbNode);

            // ★既知 = links がある方向だけ（Nodeがあるだけでは既知にしない）
            if (nbNode != null && n.links != null && n.links.Contains(nbNode))
            {
                n.ClearBlockedIndex(i);
                nbNode.ClearBlockedIndex(Opp[i]);
                continue;
            }

            // blocked は壁確定
            if (n.IsBlockedIndex(i))
            {
                n.wallCount++;
                continue;
            }

            // 壁セルなら blocked
            var nbWorld = CellCenterWorld(nbCell);
            if (IsWallCell(nbWorld))
            {
                n.MarkBlockedIndex(i);
                n.wallCount++;
                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
                continue;
            }

            // 辺が塞がれているなら blocked（隣にNodeがあっても未リンクならここで確定できる）
            if (IsEdgeBlocked(n.cell, nbCell, out _))
            {
                n.MarkBlockedIndex(i);
                n.wallCount++;
                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
                continue;
            }

            // ★通れそうだが links が無い = 未確定（境界/フロンティア候補）
            n.unknownCount++;
        }
    }

    private FrontierNode ChooseFrontierTarget(FrontierNode start)
    {
        // unknownCount>0 のノード（=フロンティア）を links BFS 距離最小で選ぶ
        var dist = BFSDistanceMap(start);

        FrontierNode best = null;
        int bestDist = int.MaxValue;

        foreach (var n in FrontierNode.All)
        {
            if (n == null) continue;
            if (n.unknownCount <= 0) continue;
            if (!dist.TryGetValue(n, out int d)) continue;

            if (d < bestDist)
            {
                bestDist = d;
                best = n;
            }
        }

        return best;
    }

    private bool TryPickUnknownNeighborCell(FrontierNode from, out Vector2Int destCell)
    {
        for (int i = 0; i < 4; i++)
        {
            var nbCell = from.cell + DirC[i];

            FrontierNode nbNode = null;
            FrontierNode.CellMap.TryGetValue(nbCell, out nbNode);

            // ★links がある方向だけ既知なので除外
            if (nbNode != null && from.links != null && from.links.Contains(nbNode))
                continue;

            if (from.IsBlockedIndex(i))
                continue;

            var nbWorld = CellCenterWorld(nbCell);

            // 壁セルなら blocked
            if (IsWallCell(nbWorld))
            {
                from.MarkBlockedIndex(i);
                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
                continue;
            }

            // 辺が塞がれていたら blocked
            if (IsEdgeBlocked(from.cell, nbCell, out _))
            {
                from.MarkBlockedIndex(i);
                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
                continue;
            }

            // ★未リンクで通れそう → 行ってリンク（既知化）しに行く
            destCell = nbCell;
            return true;
        }

        destCell = default;
        return false;
    }

    // =========================
    // Path (BFS on links only)
    // =========================

    private Dictionary<FrontierNode, int> BFSDistanceMap(FrontierNode start)
    {
        var dist = new Dictionary<FrontierNode, int>();
        var q = new Queue<FrontierNode>();

        dist[start] = 0;
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var v = q.Dequeue();
            int dv = dist[v];

            foreach (var u in v.links)
            {
                if (u == null) continue;
                if (dist.ContainsKey(u)) continue;
                dist[u] = dv + 1;
                q.Enqueue(u);
            }
        }

        return dist;
    }

    private List<FrontierNode> BuildPathBFS(FrontierNode start, FrontierNode goal)
    {
        var prev = new Dictionary<FrontierNode, FrontierNode>();
        var q = new Queue<FrontierNode>();
        var visited = new HashSet<FrontierNode>();

        visited.Add(start);
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var v = q.Dequeue();
            if (v == goal) break;

            foreach (var u in v.links)
            {
                if (u == null) continue;
                if (visited.Contains(u)) continue;
                visited.Add(u);
                prev[u] = v;
                q.Enqueue(u);
            }
        }

        if (!visited.Contains(goal)) return new List<FrontierNode>();

        var result = new List<FrontierNode>();
        var cur = goal;
        result.Add(cur);

        while (cur != start)
        {
            cur = prev[cur];
            result.Add(cur);
        }

        result.Reverse();
        return result;
    }

    private FrontierNode GetNextStepFromPath(FrontierNode cur, List<FrontierNode> p)
    {
        if (p == null || p.Count == 0) return null;
        if (p[0] != cur) return null;
        if (p.Count == 1) return null;
        return p[1];
    }

    // =========================
    // Wall checks
    // =========================

    private Vector3 CellCenterWorld(Vector2Int c)
    {
        return FrontierNode.CellToWorld(c, cellSize, transform.position.y);
    }

    private bool IsWallCell(Vector3 cellCenterWorld)
    {
        Vector3 center = cellCenterWorld + Vector3.up * cellCheckY;
        Vector3 halfExt = new Vector3(cellSize * 0.45f, cellCheckHalfY, cellSize * 0.45f);

        return Physics.CheckBox(center, halfExt, Quaternion.identity, wallLayer, QueryTriggerInteraction.Ignore);
    }

    private bool IsEdgeBlocked(Vector2Int fromCell, Vector2Int toCell, out RaycastHit hit)
    {
        Vector3 from = CellCenterWorld(fromCell);
        Vector3 to = CellCenterWorld(toCell);

        Vector3 delta = to - from;
        float dist = delta.magnitude;

        if (dist <= 0.0001f)
        {
            hit = default;
            return false;
        }

        Vector3 dir = delta / dist;
        Vector3 origin = from + Vector3.up * probeHeight;
        float checkDist = Mathf.Max(dist - probePadding, 0f);

        return Physics.SphereCast(origin, probeRadius, dir, out hit, checkDist, wallLayer, QueryTriggerInteraction.Ignore);
    }

    private void MarkBlockedBetween(FrontierNode fromNode, Vector2Int destCell)
    {
        if (fromNode == null) return;

        Vector2Int delta = destCell - fromNode.cell;
        int idx = DirIndexFromDelta(delta);
        if (idx < 0) return;

        fromNode.MarkBlockedIndex(idx);

        if (FrontierNode.CellMap.TryGetValue(destCell, out var toNode) && toNode != null)
            toNode.MarkBlockedIndex(Opp[idx]);
    }

    private int DirIndexFromDelta(Vector2Int d)
    {
        if (d == DirC[0]) return 0;
        if (d == DirC[1]) return 1;
        if (d == DirC[2]) return 2;
        if (d == DirC[3]) return 3;
        return -1;
    }
}
