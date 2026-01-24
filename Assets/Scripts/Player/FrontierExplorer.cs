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

    // Header 属性は enum には付けられないため、コメントで区切ります。
    // ---- Start Direction ----
    public enum StartDirectionMode
    {
        Manual,
        AutoFromSpawnRelativeToOrigin,
        AutoByPlayerId
    }

    [Tooltip("startDirectionMode が Manual のときのみ使用。Auto のときは起動時に上書きされます。")]
    public Vector3 startDirection = Vector3.forward;

    [Tooltip("Auto の基準点（Transform が入っていればそれを優先）")]
    public Vector3 gridOrigin = Vector3.zero;

    [Tooltip("AutoFromSpawnRelativeToOrigin の基準点。ここが入っている場合は gridOrigin より優先してこのTransform位置を基準にします（PlayerSpawner / spawnPoint を想定）。")]
    public Transform startDirectionOrigin;

    [Tooltip("Auto で決めた方向を反転（外向き⇔内向き）したい場合にON。")]
    public bool invertAutoStartDirection = false;

    public StartDirectionMode startDirectionMode = StartDirectionMode.AutoFromSpawnRelativeToOrigin;

    // TryPickUnknownNeighborCell の方向探索優先度（StartDirection を先頭にする）
    private int[] dirOrder = new int[4] { 0, 2, 3, 1 };

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

    [Header("Damage Stop (CellFromStart style)")]
    public bool stopOnDamage = true;
    public float stopDurationSec = 2.0f;
    public bool debugDamageStop = false;

    private PlayerHealth _health;
    private int _lastHP = int.MinValue;
    private float _stopUntilTime = -1f;
    private bool _wasStopped = false;

    [Header("Chase Enemy If Near (like Baseline)")]
    public bool chaseEnemyIfNear = true;
    public string enemyTag = "Enemy";
    public float chaseStartRange = 8f;
    public float chaseStopRange = 10f; // startより大きく
    public float enemyScanInterval = 0.2f;

    private Transform _chaseEnemy;
    private bool _isChasingEnemy = false;
    private float _nextEnemyScanTime = 0f;

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

        _health = GetComponent<PlayerHealth>();
        if (_health != null) _lastHP = _health.currentHP;

        currentCell = FrontierNode.WorldToCell(transform.position, cellSize);
        transform.position = CellCenterWorld(currentCell);

        // スポーン位置に合わせて StartDirection と方向優先度を決める
        ApplyAutoStartDirection(transform.position);
        BuildDirOrderFromStartDirection();

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


    // =============================
    // StartDirection auto assignment
    // =============================
    private void ApplyAutoStartDirection(Vector3 snappedWorldPos)
    {
        if (startDirectionMode == StartDirectionMode.Manual)
            return;

        Vector3 dir = Vector3.forward;

        if (startDirectionMode == StartDirectionMode.AutoByPlayerId)
        {
            dir = DirectionFromPlayerId(playerId);
        }
        else
        {
            Vector3 originPos = (startDirectionOrigin != null) ? startDirectionOrigin.position : gridOrigin;
            Vector3 delta = snappedWorldPos - originPos;
            dir = CardinalFromDeltaXZ(delta);

            // もし原点スポーン等で delta=0 なら playerId でフォールバック
            if (dir.sqrMagnitude < 1e-6f)
                dir = DirectionFromPlayerId(playerId);
        }

        if (invertAutoStartDirection)
            dir = -dir;

        startDirection = dir;
        Debug.Log($"[FE][STARTDIR] playerId={playerId} mode={startDirectionMode} dir={startDirection}");
    }

    private void BuildDirOrderFromStartDirection()
    {
        int start = DirIndexFromVector3(startDirection);
        int opp = Opp[start];

        int perpA, perpB;
        if (start == 0 || start == 1)
        {
            // F/B のときは L/R
            perpA = 2;
            perpB = 3;
        }
        else
        {
            // L/R のときは F/B
            perpA = 0;
            perpB = 1;
        }

        // 優先順：Start → 直角2方向 → 反対
        dirOrder[0] = start;
        dirOrder[1] = perpA;
        dirOrder[2] = perpB;
        dirOrder[3] = opp;
    }

    private int DirIndexFromVector3(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return 0;

        // XZ の大きい方で 4方向に落とす
        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.z))
            return (dir.x >= 0f) ? 3 : 2; // R or L
        else
            return (dir.z >= 0f) ? 0 : 1; // F or B
    }

    private Vector3 DirectionFromPlayerId(int id)
    {
        int m = (id - 1) % 4;
        if (m == 0) return Vector3.forward;
        if (m == 1) return Vector3.back;
        if (m == 2) return Vector3.left;
        return Vector3.right;
    }

    private Vector3 CardinalFromDeltaXZ(Vector3 delta)
    {
        delta.y = 0f;
        if (delta.sqrMagnitude < 1e-6f) return Vector3.zero;

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.z))
            return (delta.x >= 0f) ? Vector3.right : Vector3.left;
        else
            return (delta.z >= 0f) ? Vector3.forward : Vector3.back;
    }

    private void Update()
    {
        frameCount++;

        if (HandleDamageStop())
            return;

        if (isMoving)
        {
            StepMove();
            return;
        }

        if (currentNode == null) return;

        // 追跡状態の更新
        UpdateChaseEnemyState();

        // 追跡中なら「敵に近づく1手」を優先
        if (_isChasingEnemy && _chaseEnemy != null)
        {
            if (TryPickChaseStepCell(out var chaseStep))
            {
                StartMoveToCell(chaseStep);
                return;
            }
            // 追いかけられない（壁で詰むなど）なら、通常の探索ロジックへフォールバック
        }

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
        for (int oi = 0; oi < 4; oi++)
        {
            int i = dirOrder[oi];
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

    private bool HandleDamageStop()
    {
        if (!stopOnDamage) return false;
        if (_health == null) return false;

        int hp = _health.currentHP;
        if (_lastHP == int.MinValue) _lastHP = hp;

        // HPが減ったら停止時間を更新（停止中にさらに減ったら延長）
        if (hp < _lastHP)
        {
            _stopUntilTime = Time.time + Mathf.Max(0.01f, stopDurationSec);
            _wasStopped = true;

            if (debugDamageStop)
                Debug.Log($"[FE][DMG] stop until={_stopUntilTime:F2} hp={hp}/{_health.maxHP} playerId={playerId}");
        }

        _lastHP = hp;

        // 停止中
        if (Time.time < _stopUntilTime)
            return true;

        // 停止終了の瞬間に一度だけログ（必要ならここで計画リセットも可）
        if (_wasStopped)
        {
            _wasStopped = false;

            if (debugDamageStop)
                Debug.Log($"[FE][RESUME] hp={hp}/{_health.maxHP} playerId={playerId}");

            // フロンティア探索は到着時に target/path を都度クリアしてるので、
            // ここでの特別なリセットは基本不要。必要なら：
            // target = null; path.Clear();
        }

        return false;
    }

    private void UpdateChaseEnemyState()
    {
        if (!chaseEnemyIfNear)
        {
            _isChasingEnemy = false;
            _chaseEnemy = null;
            return;
        }

        if (Time.time < _nextEnemyScanTime) return;
        _nextEnemyScanTime = Time.time + Mathf.Max(0.01f, enemyScanInterval);

        var enemies = GameObject.FindGameObjectsWithTag(enemyTag);
        if (enemies == null || enemies.Length == 0)
        {
            _isChasingEnemy = false;
            _chaseEnemy = null;
            return;
        }

        Transform best = null;
        float bestD2 = float.PositiveInfinity;
        Vector3 me = transform.position;

        foreach (var e in enemies)
        {
            if (e == null) continue;
            float d2 = (e.transform.position - me).sqrMagnitude;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = e.transform;
            }
        }

        float bestD = Mathf.Sqrt(bestD2);

        if (!_isChasingEnemy)
        {
            if (best != null && bestD <= chaseStartRange)
            {
                _isChasingEnemy = true;
                _chaseEnemy = best;
            }
        }
        else
        {
            if (best == null || bestD >= chaseStopRange)
            {
                _isChasingEnemy = false;
                _chaseEnemy = null;
            }
            else
            {
                _chaseEnemy = best;
            }
        }
    }

    private bool TryPickChaseStepCell(out Vector2Int destCell)
    {
        destCell = default;
        if (_chaseEnemy == null) return false;

        Vector2Int enemyCell = FrontierNode.WorldToCell(_chaseEnemy.position, cellSize);

        int curDist = Mathf.Abs(enemyCell.x - currentCell.x) + Mathf.Abs(enemyCell.y - currentCell.y);

        Vector2Int best = default;
        int bestDist = curDist;
        bool found = false;

        for (int i = 0; i < 4; i++)
        {
            var nb = currentCell + DirC[i];

            // 壁セル/辺ブロックは既存判定を使う
            if (IsWallCell(CellCenterWorld(nb))) continue;
            if (IsEdgeBlocked(currentCell, nb, out _)) continue;

            int d = Mathf.Abs(enemyCell.x - nb.x) + Mathf.Abs(enemyCell.y - nb.y);

            // より近づく手を優先
            if (d < bestDist)
            {
                bestDist = d;
                best = nb;
                found = true;
            }
        }

        if (found)
        {
            destCell = best;
            return true;
        }

        // 近づける手が無い（袋小路など）→ false で通常探索に戻す
        return false;
    }
}


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

//    [Header("Goal / Restart")]
//    public bool useGoalTag = true;          // currentNode.tag == "Goal" で判定
//    public Transform goalTransform = null;  // 併用可
//    public bool triggerRestartOnGoal = true;

//    [Header("Evaluation")]
//    public int playerId = 0;
//    private static int nextPlayerId = 0;

//    [Header("Damage Stop (CellFromStart style)")]
//    public bool stopOnDamage = true;
//    public float stopDurationSec = 2.0f;
//    public bool debugDamageStop = false;

//    private PlayerHealth _health;
//    private int _lastHP = int.MinValue;
//    private float _stopUntilTime = -1f;
//    private bool _wasStopped = false;

//    [Header("Chase Enemy If Near (like Baseline)")]
//    public bool chaseEnemyIfNear = true;
//    public string enemyTag = "Enemy";
//    public float chaseStartRange = 8f;
//    public float chaseStopRange = 10f; // startより大きく
//    public float enemyScanInterval = 0.2f;

//    private Transform _chaseEnemy;
//    private bool _isChasingEnemy = false;
//    private float _nextEnemyScanTime = 0f;

//    [Header("Goal (CellFromStart style)")]
//    public string goalNodeTag = "Goal";
//    public bool autoFindGoalTransform = true; // GoalオブジェクトをTagで自動取得

//    public int StepIndex => stepIndex;
//    public float ElapsedTime => Time.time - runStartTime;
//    public int NewNodesPlaced => newNodesPlaced;

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

//    private int newNodesPlaced = 0;

//    private int stepIndex = 0;
//    private float runStartTime = 0f;
//    private bool goalReached = false;
//    private Vector2Int goalCell;

//    private void Awake()
//    {
//        playerId = ++nextPlayerId; // ★スポーン順に 1,2,3...
//        Debug.Log($"[FE][SPAWN] playerId={playerId} name={gameObject.name}");
//    }

//    private void Start()
//    {
//        BootstrapExistingNodesInScene(); // ★これを最初に呼ぶ

//        // GoalTransform を自動取得（Inspector未設定でも動くように）
//        if (autoFindGoalTransform && goalTransform == null)
//        {
//            var go = GameObject.FindGameObjectWithTag(goalNodeTag);
//            if (go != null) goalTransform = go.transform;
//        }

//        if (goalTransform != null)
//            goalCell = FrontierNode.WorldToCell(goalTransform.position, cellSize);

//        Debug.Log($"[FE][START] goalTransform={(goalTransform != null)} goalCell={goalCell} " +
//          $"startCell={currentCell} manager={(FrontierRestartManager.Instance != null)}");


//        FrontierEvaluationLogger.SetGoalCell(goalCell);
//        runStartTime = Time.time;

//        _health = GetComponent<PlayerHealth>();
//        if (_health != null) _lastHP = _health.currentHP;

//        currentCell = FrontierNode.WorldToCell(transform.position, cellSize);
//        transform.position = CellCenterWorld(currentCell);

//        if (goalTransform != null)
//            goalCell = FrontierNode.WorldToCell(goalTransform.position, cellSize);


//        FrontierEvaluationLogger.SetGoalCell(goalCell);
//        // スポーン位置に最初のNode
//        currentNode = EnsureNodeAtCell(currentCell, false);
//        if (currentNode == null)
//        {
//            Debug.LogError("[FrontierExplorer] Start cell is wall? Node could not be created.");
//            enabled = false;
//            return;
//        }

//        // ★到達扱い（色/ログ）
//        currentNode.OnPassed();
//        FrontierEvaluationLogger.LogNodeVisit(GetRunIndexSafe(), playerId, Time.frameCount, stepIndex, currentNode);
//        stepIndex++;

//        RecomputeAllNodeStates();
//        target = null;
//        path.Clear();
//    }

//    private void Update()
//    {
//        frameCount++;

//        if (HandleDamageStop())
//            return;

//        if (isMoving)
//        {
//            StepMove();
//            return;
//        }

//        if (currentNode == null) return;

//        // 追跡状態の更新
//        UpdateChaseEnemyState();

//        // 追跡中なら「敵に近づく1手」を優先
//        if (_isChasingEnemy && _chaseEnemy != null)
//        {
//            if (TryPickChaseStepCell(out var chaseStep))
//            {
//                StartMoveToCell(chaseStep);
//                return;
//            }
//            // 追いかけられない（壁で詰むなど）なら、通常の探索ロジックへフォールバック
//        }

//        // ① 今いるNodeから「未リンクで通れそう」な方向があれば、そこへ1歩（既知化=リンク化しに行く）
//        if (TryPickUnknownNeighborCell(currentNode, out var exploreTo))
//        {
//            StartMoveToCell(exploreTo);
//            return;
//        }

//        // ② フロンティアまで移動（訪問済みセル上=links上だけ）
//        if (target == null || frameCount % Mathf.Max(1, recalcEveryFrames) == 0)
//        {
//            RecomputeAllNodeStates();
//            target = ChooseFrontierTarget(currentNode);
//            path = (target != null) ? BuildPathBFS(currentNode, target) : new List<FrontierNode>();
//        }

//        var nextNode = GetNextStepFromPath(currentNode, path);

//        // パスが無い/作れない → linksで戻る（待機しない）
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
//        }

//        StartMoveToCell(nextNode.cell);
//    }

//    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
//    private static void ResetPlayerIdCounter()
//    {
//        nextPlayerId = 0;
//    }

//    private void BootstrapExistingNodesInScene()
//    {
//        var existing = FindObjectsOfType<FrontierNode>();

//        foreach (var n in existing)
//        {
//            if (n == null) continue;

//            // ★ cell / CellMap 登録（GoalNode もここで登録される）
//            n.RegisterCell(cellSize);

//            // Gizmos Raycast 色分け用
//            n.debugWallLayer = wallLayer;
//        }

//        // goalTransform が未指定なら、まず Tag から探す（Inspector の goalNodeTag を優先）
//        if (goalTransform == null)
//        {
//            try
//            {
//                //var go = GameObject.FindGameObjectWithTag("Goal");
//                var go = GameObject.FindGameObjectWithTag(goalNodeTag);
//                if (go != null) goalTransform = go.transform;
//            }
//            catch
//            {
//                // Tag が未登録でも落とさない（あとでセル一致判定に回す）
//            }
//        }

//        // それでも無ければ名前でフォールバック（置き物が "GoalNode" などの場合）
//        if (goalTransform == null)
//        {
//            var goByName = GameObject.Find("GoalNode") ?? GameObject.Find("Goal");
//            if (goByName != null) goalTransform = goByName.transform;
//        }

//        if (goalTransform != null)
//        {
//            goalCell = FrontierNode.WorldToCell(goalTransform.position, cellSize);

//            FrontierEvaluationLogger.SetGoalCell(goalCell);
//            FrontierEvaluationLogger.SetGoalCell(goalCell);

//            // Goal が FrontierNode ならセル登録 & タグ付けも確実にしておく
//            var goalAsNode = goalTransform.GetComponent<FrontierNode>();
//            if (goalAsNode != null)
//            {
//                goalAsNode.RegisterCell(cellSize);
//                ApplyGoalTagIfNeeded(goalAsNode, goalCell);
//                goalAsNode.debugWallLayer = wallLayer;
//            }
//        }

//        Debug.Log($"[BOOT] existingNodes={existing.Length} goalTransform={(goalTransform != null ? goalTransform.name : "null")} goalCell={goalCell}");
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

//        foreach (var n in currentNode.links)
//        {
//            if (n == null) continue;
//            if (lastNode != null && n == lastNode) continue;
//            best = n;
//            break;
//        }

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

//        // セル間が塞がれているなら進まない
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

//        var arrivedNode = EnsureNodeAtCell(currentCell, true);
//        if (this == null || !enabled) return; // Destroy/disable対策
//        if (arrivedNode == null) return;

//        currentNode = arrivedNode;

//        Debug.Log($"[FE][ARRIVE] cell={currentCell} node={(currentNode ? currentNode.name : "null")} tag={(currentNode ? currentNode.tag : "null")} " +
//          $"goalCell={goalCell} goalTransform={(goalTransform != null)} " +
//          $"isGoal={IsGoalReached()} trigger={triggerRestartOnGoal} manager={(FrontierRestartManager.Instance != null)}");

//        // ★「到着できた」＝その辺は通れる（通行実績を最優先）→ ここで確定リンク
//        if (lastNode != null && currentNode != null && lastNode != currentNode)
//        {
//            int dirIndex = DirIndexFromDelta(currentNode.cell - lastNode.cell);
//            if (dirIndex >= 0)
//            {
//                lastNode.ClearBlockedIndex(dirIndex);
//                currentNode.ClearBlockedIndex(Opp[dirIndex]);
//            }
//            lastNode.LinkWith(currentNode);
//        }

//        // ★到達扱い（色/ログ）
//        currentNode.OnPassed();
//        FrontierEvaluationLogger.LogNodeVisit(GetRunIndexSafe(), playerId, Time.frameCount, stepIndex, currentNode);
//        stepIndex++;

//        RecomputeAllNodeStates();
//        target = null;
//        path.Clear();

//        // Goal 到達 → 色確定→CSV→Reload（Managerがいるときだけ）
//        if (!goalReached && IsGoalReached())
//        {
//            Debug.Log($"[FE][GOAL] reached! trigger={triggerRestartOnGoal} manager={(FrontierRestartManager.Instance != null)}");

//            goalReached = true;

//            if (triggerRestartOnGoal && FrontierRestartManager.Instance != null)
//                FrontierRestartManager.Instance.StartRestart(this);
//            else
//                Debug.LogWarning("[FE][GOAL] Not restarting (trigger off or manager null)");
//        }
//    }

//    private bool IsGoalReached()
//    {
//        // 1) Node に Goal タグが付いている（GoalNode が FrontierNode で置かれている想定）
//        //if (currentNode != null && currentNode.CompareTag("Goal"))
//        if (currentNode != null && currentNode.CompareTag(goalNodeTag))
//            return true;

//        // 2) GoalTransform があるなら「セル一致」でも判定（タグ付けに失敗しても落とさない）
//        if (goalTransform != null && currentCell == goalCell)
//            return true;

//        return false;
//    }


//    // =========================
//    // Node placement (NO auto-link)
//    // =========================

//    private FrontierNode EnsureNodeAtCell(Vector2Int cell, bool countTowardLimit)
//    {
//        if (FrontierNode.CellMap.TryGetValue(cell, out var exist) && exist != null)
//        {
//            ApplyGoalTagIfNeeded(exist, cell);

//            return exist;
//        }

//        // ★ GoalTransform があるのに CellMap に入っていない場合（Goal が Node じゃない/未登録）でも、
//        //   Goal が FrontierNode を持っているなら「それを使う」ことで GoalCell への新規生成を防ぐ
//        if (goalTransform != null && cell == goalCell)
//        {
//            var goalAsNode = goalTransform.GetComponent<FrontierNode>();
//            if (goalAsNode != null)
//            {
//                goalAsNode.RegisterCell(cellSize);
//                goalAsNode.debugWallLayer = wallLayer;
//                ApplyGoalTagIfNeeded(goalAsNode, cell);
//                return goalAsNode;
//            }
//        }

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

//        ApplyGoalTagIfNeeded(n, cell);

//        // Gizmos Raycast 色分け用
//        n.debugWallLayer = wallLayer;

//        n.RegisterCell(cellSize);

//        if (countTowardLimit)
//        {
//            newNodesPlaced++;

//            if (newNodesPlaced >= destroyAfterNewNodes)
//            {
//                Debug.Log($"[FrontierExplorer] New nodes placed reached {newNodesPlaced}. Finish.");

//                if (destroySelfOnLimit) Destroy(gameObject);
//                else enabled = false;

//                return n;
//            }
//        }

//        return n;
//    }

//    private void ApplyGoalTagIfNeeded(FrontierNode node, Vector2Int cell)
//    {
//        if (node == null) return;

//        // goalTransform があるときだけ「ゴールセル」を判定できる
//        if (goalTransform != null && cell == goalCell)
//        {
//            // 注意：UnityのTag一覧に goalNodeTag が登録されていないと例外になります
//            if (!node.CompareTag(goalNodeTag))
//                node.gameObject.tag = goalNodeTag;
//        }
//    }


//    // =========================
//    // Frontier logic  ★動く本体
//    // =========================

//    private void RecomputeAllNodeStates()
//    {
//        foreach (var n in FrontierNode.All)
//            RecomputeNodeState(n);
//    }

//    /// <summary>
//    /// ★既知 = linksがある方向のみ
//    /// 未リンクで通れそうなら unknownCount++（フロンティア候補）
//    /// 4近傍全部blockedなら unknownCount=0 → frontierではない
//    /// </summary>
//    private void RecomputeNodeState(FrontierNode n)
//    {
//        if (n == null) return;

//        n.unknownCount = 0;
//        n.wallCount = 0;

//        for (int i = 0; i < 4; i++)
//        {
//            var nbCell = n.cell + DirC[i];

//            FrontierNode nbNode = null;
//            FrontierNode.CellMap.TryGetValue(nbCell, out nbNode);

//            // links がある方向は既知（証明済み）
//            if (n.HasLinkInDir(i))
//            {
//                // links があるのに blocked が立ってたら矛盾なので解除
//                if (n.IsBlockedIndex(i))
//                {
//                    n.ClearBlockedIndex(i);
//                    if (nbNode != null) nbNode.ClearBlockedIndex(Opp[i]);
//                }
//                continue;
//            }

//            // ここから「未リンク方向」＝ 未知候補
//            var nbWorld = CellCenterWorld(nbCell);

//            // ★ 以前 blocked を立てた方向でも、今の判定で通れるなら復活させる（誤検知で詰むのを防ぐ）
//            if (n.IsBlockedIndex(i))
//            {
//                bool stillWall = IsWallCell(nbWorld);
//                bool stillEdgeBlocked = IsEdgeBlocked(n.cell, nbCell, out _);

//                if (!stillWall && !stillEdgeBlocked)
//                {
//                    n.ClearBlockedIndex(i);
//                    if (nbNode != null) nbNode.ClearBlockedIndex(Opp[i]);
//                }
//                else
//                {
//                    n.wallCount++;
//                    continue;
//                }
//            }

//            // 壁セルなら blocked
//            if (IsWallCell(nbWorld))
//            {
//                n.MarkBlockedIndex(i);
//                n.wallCount++;
//                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
//                continue;
//            }

//            // 辺が塞がれているなら blocked
//            if (IsEdgeBlocked(n.cell, nbCell, out _))
//            {
//                n.MarkBlockedIndex(i);
//                n.wallCount++;
//                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
//                continue;
//            }

//            // 通れそうだが links が無い = 未確定（フロンティア）
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
//            if (n.unknownCount <= 0) continue;     // frontierのみ
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
//            var nbCell = from.cell + DirC[i];

//            FrontierNode nbNode = null;
//            FrontierNode.CellMap.TryGetValue(nbCell, out nbNode);

//            // linksがある方向は既知なので探索対象外
//            if (nbNode != null && from.links != null && from.links.Contains(nbNode))
//                continue;

//            if (from.IsBlockedIndex(i))
//                continue;

//            var nbWorld = CellCenterWorld(nbCell);

//            // 壁セルなら blocked
//            if (IsWallCell(nbWorld))
//            {
//                from.MarkBlockedIndex(i);
//                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
//                continue;
//            }

//            // 辺が塞がれていたら blocked
//            if (IsEdgeBlocked(from.cell, nbCell, out _))
//            {
//                from.MarkBlockedIndex(i);
//                if (nbNode != null) nbNode.MarkBlockedIndex(Opp[i]);
//                continue;
//            }

//            // 未リンクで通れそう → 行ってリンク（既知化）しに行く
//            destCell = nbCell;
//            return true;
//        }

//        destCell = default;
//        return false;
//    }

//    // =========================
//    // Path (BFS on links only)
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

//    private int GetRunIndexSafe()
//    {
//        return FrontierRestartManager.Instance != null ? FrontierRestartManager.Instance.GetRunIndex() : 1;
//    }

//    private bool HandleDamageStop()
//    {
//        if (!stopOnDamage) return false;
//        if (_health == null) return false;

//        int hp = _health.currentHP;
//        if (_lastHP == int.MinValue) _lastHP = hp;

//        // HPが減ったら停止時間を更新（停止中にさらに減ったら延長）
//        if (hp < _lastHP)
//        {
//            _stopUntilTime = Time.time + Mathf.Max(0.01f, stopDurationSec);
//            _wasStopped = true;

//            if (debugDamageStop)
//                Debug.Log($"[FE][DMG] stop until={_stopUntilTime:F2} hp={hp}/{_health.maxHP} playerId={playerId}");
//        }

//        _lastHP = hp;

//        // 停止中
//        if (Time.time < _stopUntilTime)
//            return true;

//        // 停止終了の瞬間に一度だけログ（必要ならここで計画リセットも可）
//        if (_wasStopped)
//        {
//            _wasStopped = false;

//            if (debugDamageStop)
//                Debug.Log($"[FE][RESUME] hp={hp}/{_health.maxHP} playerId={playerId}");

//            // フロンティア探索は到着時に target/path を都度クリアしてるので、
//            // ここでの特別なリセットは基本不要。必要なら：
//            // target = null; path.Clear();
//        }

//        return false;
//    }

//    private void UpdateChaseEnemyState()
//    {
//        if (!chaseEnemyIfNear)
//        {
//            _isChasingEnemy = false;
//            _chaseEnemy = null;
//            return;
//        }

//        if (Time.time < _nextEnemyScanTime) return;
//        _nextEnemyScanTime = Time.time + Mathf.Max(0.01f, enemyScanInterval);

//        var enemies = GameObject.FindGameObjectsWithTag(enemyTag);
//        if (enemies == null || enemies.Length == 0)
//        {
//            _isChasingEnemy = false;
//            _chaseEnemy = null;
//            return;
//        }

//        Transform best = null;
//        float bestD2 = float.PositiveInfinity;
//        Vector3 me = transform.position;

//        foreach (var e in enemies)
//        {
//            if (e == null) continue;
//            float d2 = (e.transform.position - me).sqrMagnitude;
//            if (d2 < bestD2)
//            {
//                bestD2 = d2;
//                best = e.transform;
//            }
//        }

//        float bestD = Mathf.Sqrt(bestD2);

//        if (!_isChasingEnemy)
//        {
//            if (best != null && bestD <= chaseStartRange)
//            {
//                _isChasingEnemy = true;
//                _chaseEnemy = best;
//            }
//        }
//        else
//        {
//            if (best == null || bestD >= chaseStopRange)
//            {
//                _isChasingEnemy = false;
//                _chaseEnemy = null;
//            }
//            else
//            {
//                _chaseEnemy = best;
//            }
//        }
//    }

//    private bool TryPickChaseStepCell(out Vector2Int destCell)
//    {
//        destCell = default;
//        if (_chaseEnemy == null) return false;

//        Vector2Int enemyCell = FrontierNode.WorldToCell(_chaseEnemy.position, cellSize);

//        int curDist = Mathf.Abs(enemyCell.x - currentCell.x) + Mathf.Abs(enemyCell.y - currentCell.y);

//        Vector2Int best = default;
//        int bestDist = curDist;
//        bool found = false;

//        for (int i = 0; i < 4; i++)
//        {
//            var nb = currentCell + DirC[i];

//            // 壁セル/辺ブロックは既存判定を使う
//            if (IsWallCell(CellCenterWorld(nb))) continue;
//            if (IsEdgeBlocked(currentCell, nb, out _)) continue;

//            int d = Mathf.Abs(enemyCell.x - nb.x) + Mathf.Abs(enemyCell.y - nb.y);

//            // より近づく手を優先
//            if (d < bestDist)
//            {
//                bestDist = d;
//                best = nb;
//                found = true;
//            }
//        }

//        if (found)
//        {
//            destCell = best;
//            return true;
//        }

//        // 近づける手が無い（袋小路など）→ false で通常探索に戻す
//        return false;
//    }
//}
