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

    [Header("Goal / Restart")]
    public bool useGoalTag = true;          // currentNode.tag == "Goal" で判定
    public Transform goalTransform = null;  // 併用可
    public bool triggerRestartOnGoal = true;

    [Header("Evaluation")]
    public int playerId = 0;
    private static int nextPlayerId = 0;

    [Header("Goal (CellFromStart style)")]
    public string goalNodeTag = "Goal";
    public bool autoFindGoalTransform = true; // GoalオブジェクトをTagで自動取得

    public int StepIndex => stepIndex;
    public float ElapsedTime => Time.time - runStartTime;
    public int NewNodesPlaced => newNodesPlaced;

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

    private int newNodesPlaced = 0;

    private int stepIndex = 0;
    private float runStartTime = 0f;
    private bool goalReached = false;
    private Vector2Int goalCell;

    private void Awake()
    {
        playerId = ++nextPlayerId; // ★スポーン順に 1,2,3...
        Debug.Log($"[FE][SPAWN] playerId={playerId} name={gameObject.name}");
    }

    private void Start()
    {
        BootstrapExistingNodesInScene(); // ★これを最初に呼ぶ

        // GoalTransform を自動取得（Inspector未設定でも動くように）
        if (autoFindGoalTransform && goalTransform == null)
        {
            var go = GameObject.FindGameObjectWithTag(goalNodeTag);
            if (go != null) goalTransform = go.transform;
        }

        if (goalTransform != null)
            goalCell = FrontierNode.WorldToCell(goalTransform.position, cellSize);

        Debug.Log($"[FE][START] goalTransform={(goalTransform != null)} goalCell={goalCell} " +
          $"startCell={currentCell} manager={(FrontierRestartManager.Instance != null)}");


        FrontierEvaluationLogger.SetGoalCell(goalCell);
        runStartTime = Time.time;

        currentCell = FrontierNode.WorldToCell(transform.position, cellSize);
        transform.position = CellCenterWorld(currentCell);

        if (goalTransform != null)
            goalCell = FrontierNode.WorldToCell(goalTransform.position, cellSize);


        FrontierEvaluationLogger.SetGoalCell(goalCell);
        // スポーン位置に最初のNode
        currentNode = EnsureNodeAtCell(currentCell, false);
        if (currentNode == null)
        {
            Debug.LogError("[FrontierExplorer] Start cell is wall? Node could not be created.");
            enabled = false;
            return;
        }

        // ★到達扱い（色/ログ）
        currentNode.OnPassed();
        FrontierEvaluationLogger.LogNodeVisit(GetRunIndexSafe(), playerId, Time.frameCount, stepIndex, currentNode);
        stepIndex++;

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
        }

        StartMoveToCell(nextNode.cell);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ResetPlayerIdCounter()
    {
        nextPlayerId = 0;
    }

    private void BootstrapExistingNodesInScene()
    {
        var existing = FindObjectsOfType<FrontierNode>();

        foreach (var n in existing)
        {
            if (n == null) continue;

            // ★ cell / CellMap 登録（GoalNode もここで登録される）
            n.RegisterCell(cellSize);

            // Gizmos Raycast 色分け用
            n.debugWallLayer = wallLayer;
        }

        // goalTransform が未指定なら、まず Tag から探す（Inspector の goalNodeTag を優先）
        if (goalTransform == null)
        {
            try
            {
                //var go = GameObject.FindGameObjectWithTag("Goal");
                var go = GameObject.FindGameObjectWithTag(goalNodeTag);
                if (go != null) goalTransform = go.transform;
            }
            catch
            {
                // Tag が未登録でも落とさない（あとでセル一致判定に回す）
            }
        }

        // それでも無ければ名前でフォールバック（置き物が "GoalNode" などの場合）
        if (goalTransform == null)
        {
            var goByName = GameObject.Find("GoalNode") ?? GameObject.Find("Goal");
            if (goByName != null) goalTransform = goByName.transform;
        }

        if (goalTransform != null)
        {
            goalCell = FrontierNode.WorldToCell(goalTransform.position, cellSize);

            FrontierEvaluationLogger.SetGoalCell(goalCell);
            FrontierEvaluationLogger.SetGoalCell(goalCell);

            // Goal が FrontierNode ならセル登録 & タグ付けも確実にしておく
            var goalAsNode = goalTransform.GetComponent<FrontierNode>();
            if (goalAsNode != null)
            {
                goalAsNode.RegisterCell(cellSize);
                ApplyGoalTagIfNeeded(goalAsNode, goalCell);
                goalAsNode.debugWallLayer = wallLayer;
            }
        }

        Debug.Log($"[BOOT] existingNodes={existing.Length} goalTransform={(goalTransform != null ? goalTransform.name : "null")} goalCell={goalCell}");
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

        foreach (var n in currentNode.links)
        {
            if (n == null) continue;
            if (lastNode != null && n == lastNode) continue;
            best = n;
            break;
        }

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

        var arrivedNode = EnsureNodeAtCell(currentCell, true);
        if (this == null || !enabled) return; // Destroy/disable対策
        if (arrivedNode == null) return;

        currentNode = arrivedNode;

        Debug.Log($"[FE][ARRIVE] cell={currentCell} node={(currentNode ? currentNode.name : "null")} tag={(currentNode ? currentNode.tag : "null")} " +
          $"goalCell={goalCell} goalTransform={(goalTransform != null)} " +
          $"isGoal={IsGoalReached()} trigger={triggerRestartOnGoal} manager={(FrontierRestartManager.Instance != null)}");

        // ★「到着できた」＝その辺は通れる（通行実績を最優先）→ ここで確定リンク
        if (lastNode != null && currentNode != null && lastNode != currentNode)
        {
            int dirIndex = DirIndexFromDelta(currentNode.cell - lastNode.cell);
            if (dirIndex >= 0)
            {
                lastNode.ClearBlockedIndex(dirIndex);
                currentNode.ClearBlockedIndex(Opp[dirIndex]);
            }
            lastNode.LinkWith(currentNode);
        }

        // ★到達扱い（色/ログ）
        currentNode.OnPassed();
        FrontierEvaluationLogger.LogNodeVisit(GetRunIndexSafe(), playerId, Time.frameCount, stepIndex, currentNode);
        stepIndex++;

        RecomputeAllNodeStates();
        target = null;
        path.Clear();

        // Goal 到達 → 色確定→CSV→Reload（Managerがいるときだけ）
        if (!goalReached && IsGoalReached())
        {
            Debug.Log($"[FE][GOAL] reached! trigger={triggerRestartOnGoal} manager={(FrontierRestartManager.Instance != null)}");

            goalReached = true;

            if (triggerRestartOnGoal && FrontierRestartManager.Instance != null)
                FrontierRestartManager.Instance.StartRestart(this);
            else
                Debug.LogWarning("[FE][GOAL] Not restarting (trigger off or manager null)");
        }
    }

    private bool IsGoalReached()
    {
        // 1) Node に Goal タグが付いている（GoalNode が FrontierNode で置かれている想定）
        //if (currentNode != null && currentNode.CompareTag("Goal"))
        if (currentNode != null && currentNode.CompareTag(goalNodeTag))
            return true;

        // 2) GoalTransform があるなら「セル一致」でも判定（タグ付けに失敗しても落とさない）
        if (goalTransform != null && currentCell == goalCell)
            return true;

        return false;
    }


    // =========================
    // Node placement (NO auto-link)
    // =========================

    private FrontierNode EnsureNodeAtCell(Vector2Int cell, bool countTowardLimit)
    {
        if (FrontierNode.CellMap.TryGetValue(cell, out var exist) && exist != null)
        {
            ApplyGoalTagIfNeeded(exist, cell);

            return exist;
        }

        // ★ GoalTransform があるのに CellMap に入っていない場合（Goal が Node じゃない/未登録）でも、
        //   Goal が FrontierNode を持っているなら「それを使う」ことで GoalCell への新規生成を防ぐ
        if (goalTransform != null && cell == goalCell)
        {
            var goalAsNode = goalTransform.GetComponent<FrontierNode>();
            if (goalAsNode != null)
            {
                goalAsNode.RegisterCell(cellSize);
                goalAsNode.debugWallLayer = wallLayer;
                ApplyGoalTagIfNeeded(goalAsNode, cell);
                return goalAsNode;
            }
        }

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

        ApplyGoalTagIfNeeded(n, cell);

        // Gizmos Raycast 色分け用
        n.debugWallLayer = wallLayer;

        n.RegisterCell(cellSize);

        if (countTowardLimit)
        {
            newNodesPlaced++;

            if (newNodesPlaced >= destroyAfterNewNodes)
            {
                Debug.Log($"[FrontierExplorer] New nodes placed reached {newNodesPlaced}. Finish.");

                if (destroySelfOnLimit) Destroy(gameObject);
                else enabled = false;

                return n;
            }
        }

        return n;
    }

    private void ApplyGoalTagIfNeeded(FrontierNode node, Vector2Int cell)
    {
        if (node == null) return;

        // goalTransform があるときだけ「ゴールセル」を判定できる
        if (goalTransform != null && cell == goalCell)
        {
            // 注意：UnityのTag一覧に goalNodeTag が登録されていないと例外になります
            if (!node.CompareTag(goalNodeTag))
                node.gameObject.tag = goalNodeTag;
        }
    }


    // =========================
    // Frontier logic  ★動く本体
    // =========================

    private void RecomputeAllNodeStates()
    {
        foreach (var n in FrontierNode.All)
            RecomputeNodeState(n);
    }

    /// <summary>
    /// ★既知 = linksがある方向のみ
    /// 未リンクで通れそうなら unknownCount++（フロンティア候補）
    /// 4近傍全部blockedなら unknownCount=0 → frontierではない
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

            // links がある方向は既知（証明済み）
            if (n.HasLinkInDir(i))
            {
                // links があるのに blocked が立ってたら矛盾なので解除
                if (n.IsBlockedIndex(i))
                {
                    n.ClearBlockedIndex(i);
                    if (nbNode != null) nbNode.ClearBlockedIndex(Opp[i]);
                }
                continue;
            }

            // ここから「未リンク方向」＝ 未知候補
            var nbWorld = CellCenterWorld(nbCell);

            // ★ 以前 blocked を立てた方向でも、今の判定で通れるなら復活させる（誤検知で詰むのを防ぐ）
            if (n.IsBlockedIndex(i))
            {
                bool stillWall = IsWallCell(nbWorld);
                bool stillEdgeBlocked = IsEdgeBlocked(n.cell, nbCell, out _);

                if (!stillWall && !stillEdgeBlocked)
                {
                    n.ClearBlockedIndex(i);
                    if (nbNode != null) nbNode.ClearBlockedIndex(Opp[i]);
                }
                else
                {
                    n.wallCount++;
                    continue;
                }
            }

            // 壁セルなら blocked
            if (IsWallCell(nbWorld))
            {
                n.MarkBlockedIndex(i);
                n.wallCount++;
                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
                continue;
            }

            // 辺が塞がれているなら blocked
            if (IsEdgeBlocked(n.cell, nbCell, out _))
            {
                n.MarkBlockedIndex(i);
                n.wallCount++;
                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
                continue;
            }

            // 通れそうだが links が無い = 未確定（フロンティア）
            n.unknownCount++;
        }
    }


    private FrontierNode ChooseFrontierTarget(FrontierNode start)
    {
        var dist = BFSDistanceMap(start);

        FrontierNode best = null;
        int bestDist = int.MaxValue;

        foreach (var n in FrontierNode.All)
        {
            if (n == null) continue;
            if (n.unknownCount <= 0) continue;     // frontierのみ
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

            // linksがある方向は既知なので探索対象外
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

            // 未リンクで通れそう → 行ってリンク（既知化）しに行く
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

    private int GetRunIndexSafe()
    {
        return FrontierRestartManager.Instance != null ? FrontierRestartManager.Instance.GetRunIndex() : 1;
    }
}
