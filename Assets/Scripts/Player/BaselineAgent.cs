using System.Collections.Generic;
using UnityEngine;

public class BaselineAgent : MonoBehaviour
{
    public enum BaselineMode
    {
        AStarOracle,
        RandomWalk
    }

    [Header("Mode")]
    public BaselineMode mode = BaselineMode.RandomWalk;

    [Header("Movement")]
    public float cellSize = 1f;
    public float moveSpeed = 3.5f;
    public float arriveEps = 0.05f;

    [Header("Randomness (RandomWalk)")]
    [Tooltip("0=完全にA*に従う, 1=完全ランダムに近い。A*方向に偏ったランダム。")]
    [Range(0f, 1f)]
    public float randomBlend = 0.35f;

    [Header("Walls")]
    public LayerMask wallLayer;
    public float wallCheckMargin = 0.45f;

    [Header("Goal")]
    [Tooltip("未指定なら Tag=Goal を探す")]
    public Transform goal;

    [Header("Damage Stop (like CellFromStart)")]
    public bool stopOnDamage = true;
    public float stopDurationSec = 2.0f;
    public bool rebuildPathOnResume = true;
    public bool debugDamageStop = false;

    private PlayerHealth _health;
    private int _lastHP = int.MinValue;
    private float _stopUntilTime = -1f;
    private bool _wasStopped = false;

    [Header("Node logging")]
    public bool enableNodeVisitLog = true;
    public GameObject nodePrefab;
    public int playerId = 1;

    // ★ PlayerID を「スポーン順 1,2,3...」にするためのカウンタ
    private static int nextPlayerId = 1;
 
    // ★ RestartManager から呼ぶ：Runごとに 1 から振り直す
    public static void ResetPlayerIdCounter()
    {
        nextPlayerId = 1;
    }

    // internal
    private Vector2Int currentTargetCell;
    private Vector3 currentTargetWorld;
    private bool hasTarget = false;

    // A* path (oracle)
    private List<Vector2Int> pathCells;
    private int pathIndex = 0;

    // Logging step index
    private int stepIndex = 0;

    // Restart guard
    private bool restartTriggered = false;

    // --- convenience ---
    Vector2Int CurrentCell
    {
        get
        {
            Vector3 p = transform.position;
            int x = Mathf.RoundToInt(p.x / cellSize);
            int z = Mathf.RoundToInt(p.z / cellSize);
            return new Vector2Int(x, z);
        }
    }

    Vector2Int GoalCell
    {
        get
        {
            if (goal == null) return new Vector2Int(int.MinValue, int.MinValue);
            Vector3 g = goal.position;
            int x = Mathf.RoundToInt(g.x / cellSize);
            int z = Mathf.RoundToInt(g.z / cellSize);
            return new Vector2Int(x, z);
        }
    }

    void Awake()
    {
        // ★ スポーン順に 1,2,3... を自動採番（Inspectorで0のPrefabでも確実）
        if (playerId <= 0)
            playerId = nextPlayerId++;
    }

    void Start()
    {
        if (goal == null)
        {
            GameObject g = GameObject.FindGameObjectWithTag("Goal");
            if (g != null) goal = g.transform;
        }

        _health = GetComponent<PlayerHealth>();
        if (_health != null)
            _lastHP = _health.currentHP;

        // 初期セルのログ
        if (enableNodeVisitLog)
            LogVisitAtCell(CurrentCell);

        if (mode == BaselineMode.AStarOracle)
            BuildAStarPath();
    }

    void Update()
    {
        if (restartTriggered) return;

        if (HandleDamageStop())
            return;

        // ★ 先にゴール到達を確実に拾う（下の各モード内の早期returnで取り逃がさない）
        if (goal != null && CurrentCell == GoalCell)
        {
            LogVisitAtCell(CurrentCell);
            return;
        }

        if (mode == BaselineMode.AStarOracle)
            UpdateAStarMove();
        else
            UpdateRandomMove();
    }

    // =========================
    // Logging & Node placement
    // =========================

    void LogVisitAtCell(Vector2Int cell)
    {
        // ★ ゴール判定はログON/OFFに関わらず行う（ログOFFだとリロードされない事故を防ぐ）
        if (goal != null && cell == GoalCell)
        {
            TriggerRestartIfNeeded(cell);
        }

        if (!enableNodeVisitLog) return;

        MapNode node = EnsureNodeForCell(cell);
        if (node == null) return;

        // MapNode 側で passCount++ されるので、ここでは OnPassed() だけ呼ぶ
        node.OnPassed();

        EvaluationLogger.LogNodeVisit(
            playerId,
            Time.frameCount,
            node,
            "Baseline",
            mode.ToString(),
            "None",
            0,
            stepIndex
        );
        stepIndex++;

        // 念のため：ログを書いた後にも判定（同フレームでゴール到達したとき用）
        if (goal != null && cell == GoalCell)
        {
            TriggerRestartIfNeeded(cell);
        }
    }

    void TriggerRestartIfNeeded(Vector2Int reachedCell)
    {
        if (restartTriggered) return;

        restartTriggered = true;
        Debug.Log($"[BaselineAgent] GOAL reached. PlayerID={playerId} cell={reachedCell} frame={Time.frameCount}");

        // RestartManager が静的参照を持っていない/壊れている場合も拾う
        var rm = (RestartManager.Instance != null) ? RestartManager.Instance : FindObjectOfType<RestartManager>();
        if (rm != null)
            rm.StartRestart();
        else
            Debug.LogWarning("[BaselineAgent] RestartManager not found in scene.");
    }

    MapNode EnsureNodeForCell(Vector2Int cell)
    {
        // 既存ノード探す
        MapNode[] all = FindObjectsOfType<MapNode>();
        foreach (var n in all)
        {
            if (n.cell.x == cell.x && n.cell.y == cell.y)
                return n;
        }

        if (nodePrefab == null)
        {
            Debug.LogWarning("[BaselineAgent] nodePrefab is null. Cannot place node.");
            return null;
        }

        Vector3 world = new Vector3(cell.x * cellSize, 0f, cell.y * cellSize);
        GameObject go = Instantiate(nodePrefab, world, Quaternion.identity);
        MapNode mn = go.GetComponent<MapNode>();
        if (mn != null)
        {
            mn.cell = new Vector2Int(cell.x, cell.y);
            mn.name = $"Node_{cell.x}_{cell.y}";
        }
        return mn;
    }

    // =========================
    // A* Oracle move
    // =========================

    void BuildAStarPath()
    {
        Vector2Int start = CurrentCell;
        Vector2Int goalCell = GoalCell;

        // A* (grid)
        pathCells = AStar(start, goalCell);
        if (pathCells == null || pathCells.Count == 0)
        {
            Debug.LogWarning($"[{name}] A* path not found. Check bounds or wall settings.");
            pathIndex = 0;
            return;
        }

        pathIndex = 0;
        if (pathCells[0] == start && pathCells.Count > 1) pathIndex = 1;
    }

    void UpdateAStarMove()
    {
        if (pathCells == null || pathCells.Count == 0) return;
        if (pathIndex >= pathCells.Count) return;

        Vector2Int nextCell = pathCells[pathIndex];

        // move towards next cell world center
        Vector3 target = new Vector3(nextCell.x * cellSize, transform.position.y, nextCell.y * cellSize);
        transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);

        // arrived?
        if (Vector3.Distance(transform.position, target) <= arriveEps)
        {
            transform.position = target;
            LogVisitAtCell(nextCell);

            pathIndex++;
        }
    }

    // =========================
    // Random walk
    // =========================

    void UpdateRandomMove()
    {
        // target cell not set? decide a new one
        if (!hasTarget)
        {
            DecideNextRandomBiasedByAStar();
        }

        if (!hasTarget) return;

        // move
        transform.position = Vector3.MoveTowards(transform.position, currentTargetWorld, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, currentTargetWorld) <= arriveEps)
        {
            transform.position = currentTargetWorld;
            LogVisitAtCell(currentTargetCell);

            hasTarget = false; // decide next
        }
    }

    void DecideNextRandomBiasedByAStar()
    {
        Vector2Int from = CurrentCell;

        // candidates: 4-neighborhood (non-wall)
        List<Vector2Int> cand = new List<Vector2Int>();
        Vector2Int[] dirs = new Vector2Int[]
        {
            new Vector2Int(0,1),
            new Vector2Int(0,-1),
            new Vector2Int(-1,0),
            new Vector2Int(1,0)
        };

        foreach (var d in dirs)
        {
            Vector2Int to = from + d;
            if (!IsWallBetween(from, to))
                cand.Add(to);
        }

        if (cand.Count == 0)
        {
            hasTarget = false;
            return;
        }

        // if possible, compute A* direction (first step) as "preferred"
        Vector2Int preferred = cand[Random.Range(0, cand.Count)];
        if (goal != null)
        {
            var p = AStar(from, GoalCell);
            if (p != null && p.Count >= 2)
            {
                preferred = p[1];
            }
        }

        // choose between preferred and random, based on randomBlend
        Vector2Int chosen;
        if (Random.value < (1f - randomBlend))
        {
            chosen = preferred;
        }
        else
        {
            chosen = cand[Random.Range(0, cand.Count)];
        }

        currentTargetCell = chosen;
        currentTargetWorld = new Vector3(chosen.x * cellSize, transform.position.y, chosen.y * cellSize);
        hasTarget = true;
    }

    bool IsWallBetween(Vector2Int from, Vector2Int to)
    {
        // simple wall test by raycast at midpoint (you said walls are on wallLayer and block penetration)
        Vector3 a = new Vector3(from.x * cellSize, transform.position.y + 0.5f, from.y * cellSize);
        Vector3 b = new Vector3(to.x * cellSize, transform.position.y + 0.5f, to.y * cellSize);
        Vector3 dir = (b - a);
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return false;
        dir /= dist;

        // slightly shorter to avoid hitting floor etc.
        float castDist = Mathf.Max(0f, dist - wallCheckMargin);
        return Physics.Raycast(a, dir, castDist, wallLayer);
    }

    // =========================
    // A* (grid) implementation
    // =========================

    List<Vector2Int> AStar(Vector2Int start, Vector2Int goalCell)
    {
        // NOTE: bounds are not known; this uses a simple open set with hash + no explicit bounds.
        // It assumes walls are represented by colliders and we can move freely where no wall blocks.
        HashSet<Vector2Int> closed = new HashSet<Vector2Int>();
        List<Vector2Int> open = new List<Vector2Int> { start };

        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();

        Dictionary<Vector2Int, int> gScore = new Dictionary<Vector2Int, int>();
        Dictionary<Vector2Int, int> fScore = new Dictionary<Vector2Int, int>();

        gScore[start] = 0;
        fScore[start] = Heuristic(start, goalCell);

        Vector2Int[] dirs = new Vector2Int[]
        {
            new Vector2Int(0,1),
            new Vector2Int(0,-1),
            new Vector2Int(-1,0),
            new Vector2Int(1,0)
        };

        int safety = 0;
        const int SAFETY_LIMIT = 50000;

        while (open.Count > 0)
        {
            safety++;
            if (safety > SAFETY_LIMIT)
            {
                Debug.LogWarning("[BaselineAgent] A* safety limit reached.");
                break;
            }

            // pick node with lowest f
            Vector2Int current = open[0];
            int bestF = GetScore(fScore, current, int.MaxValue);
            for (int i = 1; i < open.Count; i++)
            {
                var c = open[i];
                int f = GetScore(fScore, c, int.MaxValue);
                if (f < bestF)
                {
                    bestF = f;
                    current = c;
                }
            }

            if (current == goalCell)
                return ReconstructPath(cameFrom, current);

            open.Remove(current);
            closed.Add(current);

            foreach (var d in dirs)
            {
                Vector2Int nb = current + d;
                if (closed.Contains(nb)) continue;
                if (IsWallBetween(current, nb)) continue;

                int tentative = GetScore(gScore, current, int.MaxValue) + 1;

                if (!open.Contains(nb))
                    open.Add(nb);
                else if (tentative >= GetScore(gScore, nb, int.MaxValue))
                    continue;

                cameFrom[nb] = current;
                gScore[nb] = tentative;
                fScore[nb] = tentative + Heuristic(nb, goalCell);
            }
        }

        return null;
    }

    int Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    int GetScore(Dictionary<Vector2Int, int> dict, Vector2Int key, int fallback)
    {
        int v;
        if (dict.TryGetValue(key, out v)) return v;
        return fallback;
    }

    List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        var total = new List<Vector2Int> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            total.Add(current);
        }
        total.Reverse();
        return total;
    }

    /// <summary>
    /// HPが減ったら一定時間停止し、一定期間減らなければ復帰する（CellFromStartと同じ系）
    /// true を返した場合：今フレームは停止中なので Update を抜ける
    /// </summary>
    private bool HandleDamageStop()
    {
        if (!stopOnDamage) return false;
        if (_health == null) return false;

        int hp = _health.currentHP;

        // 初回だけ lastHP を合わせる
        if (_lastHP == int.MinValue)
            _lastHP = hp;

        // HP減少を検知したら「停止タイマー」を更新（停止中にさらに減っても延長される）
        if (hp < _lastHP)
        {
            _stopUntilTime = Time.time + Mathf.Max(0.01f, stopDurationSec);
            _wasStopped = true;

            // 移動中の状態をリセット（復帰後に変な途中状態で続かないように）
            hasTarget = false;       // RandomWalk用
            pathIndex = 0;           // A*Oracle用（復帰時に組み直す）

            if (debugDamageStop)
                Debug.Log($"[BaselineAgent] DAMAGED -> STOP until={_stopUntilTime:F2} HP {hp}/{_health.maxHP} PlayerID={playerId}");
        }

        _lastHP = hp;

        // 停止中
        if (Time.time < _stopUntilTime)
            return true;

        // 停止が終わった瞬間に一度だけ復帰処理
        if (_wasStopped)
        {
            _wasStopped = false;

            if (debugDamageStop)
                Debug.Log($"[BaselineAgent] RESUME HP={hp}/{_health.maxHP} PlayerID={playerId}");

            if (mode == BaselineMode.AStarOracle)
            {
                if (rebuildPathOnResume)
                    BuildAStarPath();
            }
            else
            {
                // RandomWalk: 次のターゲットを選び直す
                hasTarget = false;
            }
        }

        return false;
    }

}


//using System.Collections.Generic;
//using UnityEngine;

//public class BaselineAgent : MonoBehaviour
//{
//    public enum BaselineMode
//    {
//        AStarOracle,  // 純A*（開始時に一度計算→追従。詰まれば再計算）
//        RandomWalk    // ※名前はそのまま：実体は「A*ベース + εランダム」（A*+ε）
//    }

//    [Header("Mode")]
//    public BaselineMode mode = BaselineMode.RandomWalk;

//    [Header("Grid Settings")]
//    public float cellSize = 1f;

//    [Header("Collision (no penetration)")]
//    public LayerMask wallLayer;          // 壁レイヤー（めり込み禁止）
//    public float agentRadius = 0.35f;     // 通路幅に合わせて調整
//    public float agentHeight = 1.8f;      // カプセル高さ

//    [Header("World Bounds (A*が探索できる範囲)")]
//    public int minX = -50;
//    public int maxX = 50;
//    public int minZ = -50;
//    public int maxZ = 50;

//    [Header("Movement")]
//    public float moveSpeed = 3.0f;
//    public float arriveEps = 0.05f;

//    [Header("Goal (optional)")]
//    public Transform goal;            // 直接アサインしてもOK
//    public string goalTag = "Goal";   // goal未設定ならTagから探す

//    [Header("A* + epsilon (RandomWalk) Settings")]
//    [Range(0f, 1f)]
//    [Tooltip("この確率で A*最善手 以外へ逸れる（A* + ε）")]
//    public float epsilon = 0.2f;
//    public bool avoidImmediateBacktrack = true;

//    [Header("A* Debug")]
//    public bool drawPathGizmos = true;

//    // ---- runtime (AStarOracle) ----
//    private List<Vector2Int> pathCells;
//    private int pathIndex;

//    // ---- runtime (A*+epsilon) ----
//    private Vector2Int lastCell;
//    private bool hasLast;
//    private Vector2Int targetCell;
//    private bool hasTarget;

//    // =============================================================
//    // NodeVisit Logging (EvaluationLogger) : Baseline用（影ノード）
//    // =============================================================
//    [Header("NodeVisit Logging")]
//    public bool enableNodeVisitLog = true;
//    [Tooltip("MapNode 付きの Node prefab（CellFromStart が使っているものと同じ）")]
//    public GameObject nodePrefab;
//    [Tooltip("Node を置くY座標（床が0なら0）")]
//    public float nodeSpawnY = 0f;
//    [Tooltip("0ならスポーン順に 1,2,3... を自動採番")]
//    public int playerId = 0;

//    private static int nextPlayerId = 1;
//    private int stepIndex = 0;
//    private bool restartTriggered = false;

//    public static void ResetPlayerIdCounter()
//    {
//        nextPlayerId = 1;
//    }

//    private Vector2Int CurrentCell => WorldToCell(transform.position);
//    private Vector2Int GoalCell => goal ? WorldToCell(goal.position) : Vector2Int.zero;

//    void Start()
//    {
//        // ★ PlayerID（スポーン順に1から）
//        if (playerId <= 0) playerId = nextPlayerId++;
//        restartTriggered = false;

//        // goal未設定なら Tag で拾う（Prefab運用向け）
//        if (goal == null && !string.IsNullOrEmpty(goalTag))
//        {
//            var g = GameObject.FindGameObjectWithTag(goalTag);
//            if (g != null) goal = g.transform;
//        }

//        if (goal == null)
//        {
//            Debug.LogError($"[{name}] Goal is not found. Set goal in Inspector or add Tag '{goalTag}' to the goal object.");
//            enabled = false;
//            return;
//        }

//        // ★ ログ：開始セルを1回だけ記録
//        LogVisitAtCell(CurrentCell);

//        if (mode == BaselineMode.AStarOracle)
//        {
//            BuildAStarPath();
//        }
//        else
//        {
//            // RandomWalk = A* + ε
//            lastCell = CurrentCell;
//            hasLast = true;
//            hasTarget = false;
//            PickNextTarget_AStarEpsilon();
//        }
//    }

//    void Update()
//    {
//        if (mode == BaselineMode.AStarOracle)
//            UpdateAStarMove();
//        else
//            UpdateAStarEpsilonMove();
//    }

//    // =============================================================
//    // NodeVisit: セル到達時に MapNode を確保して LogNodeVisit を書く
//    // =============================================================
//    void LogVisitAtCell(Vector2Int cell)
//    {
//        if (!enableNodeVisitLog) return;

//        MapNode node = EnsureNodeForCell(cell);
//        if (node == null) return;

//        // MapNode 側で passCount++ される前提なので、ここでは OnPassed() のみ
//        node.OnPassed();

//        EvaluationLogger.LogNodeVisit(
//            playerId,
//            Time.frameCount,
//            node,
//            "Baseline",
//            mode.ToString(),
//            "None",
//            0,
//            stepIndex
//        );
//        stepIndex++;

//        // ゴール到達なら Run 終了をトリガー
//        if (!restartTriggered && goal != null && cell == GoalCell)
//        {
//            restartTriggered = true;
//            if (RestartManager.Instance != null)
//                RestartManager.Instance.StartRestart();
//        }
//    }

//    MapNode EnsureNodeForCell(Vector2Int cell)
//    {
//        // 既存があれば再利用
//        MapNode existing = MapNode.FindByCell(cell);
//        if (existing != null) return existing;

//        Vector3 pos = new Vector3(cell.x * cellSize, nodeSpawnY, cell.y * cellSize);

//        GameObject go;
//        if (nodePrefab != null)
//        {
//            go = Instantiate(nodePrefab, pos, Quaternion.identity);
//        }
//        else
//        {
//            // Prefab未指定でも最低限動くように（ただし見た目は無い）
//            go = new GameObject("BaselineNode");
//            go.transform.position = pos;
//            go.AddComponent<MapNode>();
//        }

//        MapNode node = go.GetComponent<MapNode>();
//        if (node == null)
//        {
//            Debug.LogWarning($"[{name}] nodePrefab に MapNode が付いていません: {nodePrefab}");
//            return null;
//        }

//        // 念のため：セルがズレた場合は補正（HashSet/リストも補正）
//        if (node.cell != cell)
//        {
//            MapNode.allNodeCells.Remove(node.cell);
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//        }
//        if (!MapNode.allNodes.Contains(node)) MapNode.allNodes.Add(node);

//        node.cellSize = cellSize;
//        return node;
//    }

//    // =========================
//    // Common helpers
//    // =========================

//    Vector2Int WorldToCell(Vector3 world)
//    {
//        int x = Mathf.RoundToInt(world.x / cellSize);
//        int z = Mathf.RoundToInt(world.z / cellSize);
//        return new Vector2Int(x, z);
//    }

//    Vector3 CellToWorld(Vector2Int cell)
//    {
//        return new Vector3(cell.x * cellSize, transform.position.y, cell.y * cellSize);
//    }

//    bool InBounds(Vector2Int c)
//    {
//        return (c.x >= minX && c.x <= maxX && c.y >= minZ && c.y <= maxZ);
//    }

//    // ★「近傍判定」と「実移動」を同じ基準に統一（コメントアウトしてた方へ戻す）
//    // 到達地点が壁の中なら不可 + 移動途中で壁に当たるなら不可
//    bool CanTraverseEdge(Vector3 fromPos, Vector3 toPos)
//    {
//        Vector3 delta = toPos - fromPos;
//        float dist = delta.magnitude;
//        if (dist <= 0.0001f) return true;

//        Vector3 dir = delta / dist;

//        Vector3 fromP1 = fromPos + Vector3.up * 0.1f;
//        Vector3 fromP2 = fromPos + Vector3.up * Mathf.Max(0.2f, agentHeight - 0.1f);

//        Vector3 toP1 = toPos + Vector3.up * 0.1f;
//        Vector3 toP2 = toPos + Vector3.up * Mathf.Max(0.2f, agentHeight - 0.1f);

//        // 到達地点が壁の中
//        if (Physics.CheckCapsule(toP1, toP2, agentRadius, wallLayer, QueryTriggerInteraction.Ignore))
//            return false;

//        // 移動中に壁へ衝突
//        if (Physics.CapsuleCast(fromP1, fromP2, agentRadius, dir, dist, wallLayer, QueryTriggerInteraction.Ignore))
//            return false;

//        return true;
//    }

//    List<Vector2Int> Get4Neighbors(Vector2Int c)
//    {
//        var list = new List<Vector2Int>(4);
//        var cand = new Vector2Int[]
//        {
//            new Vector2Int(c.x + 1, c.y),
//            new Vector2Int(c.x - 1, c.y),
//            new Vector2Int(c.x, c.y + 1),
//            new Vector2Int(c.x, c.y - 1),
//        };

//        Vector3 fromWorld = CellToWorld(c);

//        foreach (var n in cand)
//        {
//            if (!InBounds(n)) continue;

//            Vector3 toWorld = CellToWorld(n);
//            if (!CanTraverseEdge(fromWorld, toWorld)) continue;

//            list.Add(n);
//        }
//        return list;
//    }

//    // true: 進めた / false: 壁で止まった
//    bool MoveTowardsWorld_NoPenetration(Vector3 targetWorld)
//    {
//        Vector3 p = transform.position;
//        Vector3 t = new Vector3(targetWorld.x, p.y, targetWorld.z);

//        Vector3 next = Vector3.MoveTowards(p, t, moveSpeed * Time.deltaTime);

//        if (CanTraverseEdge(p, next))
//        {
//            transform.position = next;
//            return true;
//        }

//        return false;
//    }

//    // =========================
//    // A* Oracle
//    // =========================

//    void BuildAStarPath()
//    {
//        Vector2Int start = CurrentCell;
//        Vector2Int goalC = GoalCell;

//        pathCells = AStar(start, goalC);

//        if (pathCells == null || pathCells.Count == 0)
//        {
//            Debug.LogWarning($"[{name}] A* path not found. Check bounds or wall settings.");
//            pathIndex = 0;
//            return;
//        }

//        pathIndex = 0;
//        if (pathCells[0] == start && pathCells.Count > 1) pathIndex = 1;
//    }

//    void UpdateAStarMove()
//    {
//        if (CurrentCell == GoalCell) return;
//        if (pathCells == null || pathCells.Count == 0) return;
//        if (pathIndex >= pathCells.Count) return;

//        Vector3 targetWorld = CellToWorld(pathCells[pathIndex]);

//        bool moved = MoveTowardsWorld_NoPenetration(targetWorld);
//        if (!moved)
//        {
//            // 壁で止まった：現状位置から再探索
//            BuildAStarPath();
//            return;
//        }

//        if (Vector3.Distance(transform.position, targetWorld) <= arriveEps)
//        {
//            LogVisitAtCell(pathCells[pathIndex]);
//            pathIndex++;
//        }
//    }

//    // =========================
//    // A* core
//    // =========================

//    int Heuristic(Vector2Int a, Vector2Int b)
//    {
//        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); // マンハッタン
//    }

//    List<Vector2Int> AStar(Vector2Int start, Vector2Int goalC)
//    {
//        var open = new List<Vector2Int>();
//        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
//        var gScore = new Dictionary<Vector2Int, int>();
//        var fScore = new Dictionary<Vector2Int, int>();

//        open.Add(start);
//        gScore[start] = 0;
//        fScore[start] = Heuristic(start, goalC);

//        int safety = 0;
//        int safetyMax = (maxX - minX + 1) * (maxZ - minZ + 1) + 1000;

//        while (open.Count > 0)
//        {
//            safety++;
//            if (safety > safetyMax) break;

//            // open内のf最小（最小構成：線形探索）
//            Vector2Int current = open[0];
//            int bestF = fScore.ContainsKey(current) ? fScore[current] : int.MaxValue;

//            for (int i = 1; i < open.Count; i++)
//            {
//                var c = open[i];
//                int f = fScore.ContainsKey(c) ? fScore[c] : int.MaxValue;
//                if (f < bestF)
//                {
//                    bestF = f;
//                    current = c;
//                }
//            }

//            if (current == goalC)
//                return ReconstructPath(cameFrom, current);

//            open.Remove(current);

//            foreach (var n in Get4Neighbors(current))
//            {
//                int curG = gScore.ContainsKey(current) ? gScore[current] : int.MaxValue;
//                if (curG == int.MaxValue) continue;

//                int tentativeG = curG + 1;
//                int nG = gScore.ContainsKey(n) ? gScore[n] : int.MaxValue;

//                if (tentativeG < nG)
//                {
//                    cameFrom[n] = current;
//                    gScore[n] = tentativeG;
//                    fScore[n] = tentativeG + Heuristic(n, goalC);

//                    if (!open.Contains(n))
//                        open.Add(n);
//                }
//            }
//        }

//        return null;
//    }

//    List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
//    {
//        var total = new List<Vector2Int> { current };
//        while (cameFrom.ContainsKey(current))
//        {
//            current = cameFrom[current];
//            total.Add(current);
//        }
//        total.Reverse();
//        return total;
//    }

//    // =========================
//    // RandomWalk (A* + epsilon)
//    // =========================

//    void UpdateAStarEpsilonMove()
//    {
//        if (CurrentCell == GoalCell) return;

//        // ★ 直前セルは「移動前のセル」
//        Vector2Int prevCell = CurrentCell;

//        if (!hasTarget)
//        {
//            PickNextTarget_AStarEpsilon();
//            if (!hasTarget) return;
//        }

//        Vector3 targetWorld = CellToWorld(targetCell);

//        bool moved = MoveTowardsWorld_NoPenetration(targetWorld);
//        if (!moved)
//        {
//            // 壁で止まった：ターゲット破棄して選び直し
//            hasTarget = false;
//            return;
//        }

//        if (Vector3.Distance(transform.position, targetWorld) <= arriveEps)
//        {
//            // 到達ログ
//            LogVisitAtCell(targetCell);

//            // ★ 直前セルを「移動前セル」で更新（戻り抑制が壊れない）
//            lastCell = prevCell;
//            hasLast = true;

//            hasTarget = false;
//        }
//    }

//    void PickNextTarget_AStarEpsilon()
//    {
//        Vector2Int cur = CurrentCell;

//        List<Vector2Int> neigh = Get4Neighbors(cur);
//        if (neigh.Count == 0)
//        {
//            hasTarget = false;
//            return;
//        }

//        // A*最善手（次の1セル）
//        Vector2Int bestNext = Vector2Int.zero;
//        bool hasBest = false;

//        var p = AStar(cur, GoalCell);
//        if (p != null && p.Count >= 2)
//        {
//            bestNext = p[1];
//            hasBest = neigh.Contains(bestNext);
//        }

//        // 最善手が無い/近傍に無いなら従来のランダム
//        if (!hasBest)
//        {
//            if (avoidImmediateBacktrack && hasLast && neigh.Count >= 2)
//            {
//                neigh.RemoveAll(n => n == lastCell);
//                if (neigh.Count == 0)
//                    neigh = Get4Neighbors(cur);
//            }

//            targetCell = neigh[Random.Range(0, neigh.Count)];
//            hasTarget = true;
//            return;
//        }

//        // εの確率で逸れる（最善手以外がある時だけ）
//        bool deviate = (Random.value < epsilon);

//        if (deviate && neigh.Count >= 2)
//        {
//            var alts = new List<Vector2Int>(neigh);
//            alts.Remove(bestNext);

//            // 逸れるときだけ即戻りを避ける（候補が残るなら）
//            if (avoidImmediateBacktrack && hasLast && alts.Count >= 2)
//            {
//                alts.RemoveAll(n => n == lastCell);
//                if (alts.Count == 0)
//                {
//                    // 避けすぎて候補が無いなら戻りも許可
//                    alts = new List<Vector2Int>(neigh);
//                    alts.Remove(bestNext);
//                }
//            }

//            if (alts.Count > 0)
//            {
//                targetCell = alts[Random.Range(0, alts.Count)];
//                hasTarget = true;
//                return;
//            }
//        }

//        // 通常はA*最善手
//        targetCell = bestNext;
//        hasTarget = true;
//    }

//    // =========================
//    // Gizmos
//    // =========================

//    void OnDrawGizmos()
//    {
//        if (!drawPathGizmos) return;
//        if (mode != BaselineMode.AStarOracle) return;
//        if (pathCells == null) return;

//        Gizmos.color = Color.cyan;
//        for (int i = 0; i < pathCells.Count; i++)
//        {
//            Vector3 w = new Vector3(pathCells[i].x * cellSize, transform.position.y + 0.1f, pathCells[i].y * cellSize);
//            Gizmos.DrawSphere(w, 0.1f);

//            if (i + 1 < pathCells.Count)
//            {
//                Vector3 w2 = new Vector3(pathCells[i + 1].x * cellSize, transform.position.y + 0.1f, pathCells[i + 1].y * cellSize);
//                Gizmos.DrawLine(w, w2);
//            }
//        }
//    }
//}


////using System.Collections.Generic;
////using UnityEngine;

////public class BaselineAgent : MonoBehaviour
////{
////    public enum BaselineMode
////    {
////        AStarOracle,  // n}OŊJnA*vZ
////        RandomWalk    // Ǐǔ肾Ń_ړ
////    }

////    [Header("Mode")]
////    public BaselineMode mode = BaselineMode.RandomWalk;

////    [Header("Grid Settings")]
////    public float cellSize = 1f;
////    public LayerMask wallLayer;
////    public float rayHeight = 1.0f;

////    [Header("World Bounds (A*STł͈)")]
////    public int minX = -50;
////    public int maxX = 50;
////    public int minZ = -50;
////    public int maxZ = 50;

////    [Header("Movement")]
////    public float moveSpeed = 3.0f;
////    public float arriveEps = 0.05f;

////    [Header("Goal (optional)")]
////    public Transform goal;            // ڃATCĂOK
////    public string goalTag = "Goal";   // goalݒȂTagT

////    [Header("Collision (no penetration)")]
////    public float agentRadius = 0.35f; // ʘHɍ킹Ē
////    public float agentHeight = 1.8f;

////    [Header("Random Settings")]
////    public bool avoidImmediateBacktrack = true;

////    [Header("A* Debug")]
////    public bool drawPathGizmos = true;

////    // ---- runtime ----
////    private List<Vector2Int> pathCells;
////    private int pathIndex;

////    private Vector2Int lastCell;
////    private bool hasLast;
////    private Vector2Int targetCell;
////    private bool hasTarget;

////    // =============================================================
////    // NodeVisit Logging (EvaluationLogger) : Baseline用（影ノード）
////    // =============================================================
////    [Header("NodeVisit Logging")]
////    public bool enableNodeVisitLog = true;
////    [Tooltip("MapNode 付きの Node prefab（CellFromStart が使っているものと同じ）")]
////    public GameObject nodePrefab;
////    [Tooltip("Node を置くY座標（床が0なら0）")]
////    public float nodeSpawnY = 0f;
////    [Tooltip("0ならスポーン順に 1,2,3... を自動採番")]
////    public int playerId = 0;

////    private static int nextPlayerId = 1;
////    private int stepIndex = 0;
////    private bool restartTriggered = false;

////    public static void ResetPlayerIdCounter()
////    {
////        nextPlayerId = 1;
////    }

////    private Vector2Int CurrentCell => WorldToCell(transform.position);
////    private Vector2Int GoalCell => goal ? WorldToCell(goal.position) : Vector2Int.zero;

////    void Start()
////    {
////        // ★ PlayerID（スポーン順に1から）
////        if (playerId <= 0) playerId = nextPlayerId++;
////        restartTriggered = false;

////        //  goalݒȂ Tag ŏEiPrefab^pj
////        if (goal == null && !string.IsNullOrEmpty(goalTag))
////        {
////            var g = GameObject.FindGameObjectWithTag(goalTag);
////            if (g != null) goal = g.transform;
////        }

////        if (goal == null)
////        {
////            Debug.LogError($"[{name}] Goal is not found. Set goal in Inspector or add Tag '{goalTag}' to the goal object.");
////            enabled = false;
////            return;
////        }


////        // ★ ログ：開始セルを1回だけ記録
////        LogVisitAtCell(CurrentCell);

////        if (mode == BaselineMode.AStarOracle)
////        {
////            BuildAStarPath();
////        }
////        else
////        {
////            lastCell = CurrentCell;
////            hasLast = true;
////            hasTarget = false;
////            PickNextTarget();
////        }
////    }

////    void Update()
////    {
////        if (mode == BaselineMode.AStarOracle)
////            UpdateAStarMove();
////        else
////            UpdateRandomMove();
////    }

////    // =============================================================
////    // NodeVisit: セル到達時に MapNode を確保して LogNodeVisit を書く
////    // =============================================================
////    void LogVisitAtCell(Vector2Int cell)
////    {
////        if (!enableNodeVisitLog) return;

////        MapNode node = EnsureNodeForCell(cell);
////        if (node == null) return;

////        // MapNode 側で passCount++ されるので、ここでは OnPassed() だけ呼ぶ
////        node.OnPassed();

////        EvaluationLogger.LogNodeVisit(
////            playerId,
////            Time.frameCount,
////            node,
////            "Baseline",
////            mode.ToString(),
////            "None",
////            0,
////            stepIndex
////        );
////        stepIndex++;

////        // ゴール到達なら Run 終了をトリガー
////        if (!restartTriggered && goal != null && cell == GoalCell)
////        {
////            restartTriggered = true;
////            if (RestartManager.Instance != null)
////                RestartManager.Instance.StartRestart();
////        }
////    }

////    MapNode EnsureNodeForCell(Vector2Int cell)
////    {
////        // 既存があれば再利用
////        MapNode existing = MapNode.FindByCell(cell);
////        if (existing != null) return existing;

////        Vector3 pos = new Vector3(cell.x * cellSize, nodeSpawnY, cell.y * cellSize);

////        GameObject go;
////        if (nodePrefab != null)
////        {
////            go = Instantiate(nodePrefab, pos, Quaternion.identity);
////        }
////        else
////        {
////            // Prefab未指定でも最低限動くように（ただし見た目は無い）
////            go = new GameObject("BaselineNode");
////            go.transform.position = pos;
////            go.AddComponent<MapNode>();
////        }

////        MapNode node = go.GetComponent<MapNode>();
////        if (node == null)
////        {
////            Debug.LogWarning($"[{name}] nodePrefab に MapNode が付いていません: {nodePrefab}");
////            return null;
////        }

////        // 念のため：セルがズレた場合は補正（HashSet/リストも補正）
////        if (node.cell != cell)
////        {
////            MapNode.allNodeCells.Remove(node.cell);
////            node.cell = cell;
////            MapNode.allNodeCells.Add(cell);
////        }
////        if (!MapNode.allNodes.Contains(node)) MapNode.allNodes.Add(node);

////        // cellSize を揃える（prefab側が正しいなら不要だが、安全側）
////        node.cellSize = cellSize;

////        return node;
////    }

////    // =========================
////    // Common helpers
////    // =========================

////    Vector2Int WorldToCell(Vector3 world)
////    {
////        int x = Mathf.RoundToInt(world.x / cellSize);
////        int z = Mathf.RoundToInt(world.z / cellSize);
////        return new Vector2Int(x, z);
////    }

////    Vector3 CellToWorld(Vector2Int cell)
////    {
////        return new Vector3(cell.x * cellSize, transform.position.y, cell.y * cellSize);
////    }

////    bool InBounds(Vector2Int c)
////    {
////        return (c.x >= minX && c.x <= maxX && c.y >= minZ && c.y <= maxZ);
////    }

////    // from -> to ̊Ԃɕǂ邩iʍs\FObhאڂ̉ہj
////    bool IsBlocked(Vector2Int from, Vector2Int to)
////    {
////        Vector3 a = new Vector3(from.x * cellSize, 0f, from.y * cellSize);
////        Vector3 b = new Vector3(to.x * cellSize, 0f, to.y * cellSize);

////        Vector3 origin = a + Vector3.up * rayHeight;
////        Vector3 dir = (b - a).normalized;
////        float dist = Vector3.Distance(a, b);

////        return Physics.Raycast(origin, dir, dist, wallLayer, QueryTriggerInteraction.Ignore);
////    }

////    List<Vector2Int> Get4Neighbors(Vector2Int c)
////     {
////        var list = new List<Vector2Int>(4);
////        var cand = new Vector2Int[]
////        {
////            new Vector2Int(c.x + 1, c.y),
////            new Vector2Int(c.x - 1, c.y),
////            new Vector2Int(c.x, c.y + 1),
////            new Vector2Int(c.x, c.y - 1),
////        };

////        foreach (var n in cand)
////        {
////            if (!InBounds(n)) continue;
////            if (IsBlocked(c, n)) continue;
////            list.Add(n);
////        }
////        return list;
////     }

////    // ǂ߂荞݋֎~FCapsuleCastŁüʒuɐi߂邩v`FbN
////    bool CanMoveNoPenetration(Vector3 from, Vector3 to)
////    {
////        Vector3 dir = to - from;
////        float dist = dir.magnitude;
////        if (dist <= 0.0001f) return true;

////        dir /= dist;

////        Vector3 p1 = from + Vector3.up * 0.1f;
////        Vector3 p2 = from + Vector3.up * Mathf.Max(0.2f, agentHeight - 0.1f);

////        return !Physics.CapsuleCast(
////            p1, p2, agentRadius, dir, dist,
////            wallLayer,
////            QueryTriggerInteraction.Ignore
////        );
////    }

////    // true: i߂ / false: ǂŎ~܂
////    bool MoveTowardsWorld_NoPenetration(Vector3 targetWorld)
////    {
////        Vector3 p = transform.position;
////        Vector3 t = new Vector3(targetWorld.x, p.y, targetWorld.z);

////        Vector3 next = Vector3.MoveTowards(p, t, moveSpeed * Time.deltaTime);

////        if (CanMoveNoPenetration(p, next))
////        {
////            transform.position = next;
////            return true;
////        }

////        return false;
////    }

////    // =========================
////    // A* Oracle
////    // =========================

////    void BuildAStarPath()
////    {
////        Vector2Int start = CurrentCell;
////        Vector2Int goalC = GoalCell;

////        pathCells = AStar(start, goalC);

////        if (pathCells == null || pathCells.Count == 0)
////        {
////            Debug.LogWarning($"[{name}] A* path not found. Check bounds or wall settings.");
////            pathIndex = 0;
////            return;
////        }

////        pathIndex = 0;
////        if (pathCells[0] == start && pathCells.Count > 1) pathIndex = 1;
////    }

////    void UpdateAStarMove()
////    {
////        if (CurrentCell == GoalCell) return;
////        if (pathCells == null || pathCells.Count == 0) return;
////        if (pathIndex >= pathCells.Count) return;

////        Vector3 targetWorld = CellToWorld(pathCells[pathIndex]);

////        bool moved = MoveTowardsWorld_NoPenetration(targetWorld);
////        if (!moved)
////        {
////            // ǂŎ~܂FʒuĒTiǔ⋫EݒY΍j
////            BuildAStarPath();
////            return;
////        }

////        if (Vector3.Distance(transform.position, targetWorld) <= arriveEps)
////        {
////            // uZBvOݓ_iA*j
////            // Debug.Log($"[A*] Visit {pathCells[pathIndex]}");

////            LogVisitAtCell(pathCells[pathIndex]);
////            pathIndex++;
////        }
////    }

////    int Heuristic(Vector2Int a, Vector2Int b)
////    {
////        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); // }nb^
////    }

////    List<Vector2Int> AStar(Vector2Int start, Vector2Int goalC)
////    {
////        var open = new List<Vector2Int>();
////        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
////        var gScore = new Dictionary<Vector2Int, int>();
////        var fScore = new Dictionary<Vector2Int, int>();

////        open.Add(start);
////        gScore[start] = 0;
////        fScore[start] = Heuristic(start, goalC);

////        int safety = 0;
////        int safetyMax = (maxX - minX + 1) * (maxZ - minZ + 1) + 1000;

////        while (open.Count > 0)
////        {
////            safety++;
////            if (safety > safetyMax) break;

////            // openfŏiŏ\F`Tj
////            Vector2Int current = open[0];
////            int bestF = fScore.ContainsKey(current) ? fScore[current] : int.MaxValue;

////            for (int i = 1; i < open.Count; i++)
////            {
////                var c = open[i];
////                int f = fScore.ContainsKey(c) ? fScore[c] : int.MaxValue;
////                if (f < bestF)
////                {
////                    bestF = f;
////                    current = c;
////                }
////            }

////            if (current == goalC)
////                return ReconstructPath(cameFrom, current);

////            open.Remove(current);

////            foreach (var n in Get4Neighbors(current))
////            {
////                int curG = gScore.ContainsKey(current) ? gScore[current] : int.MaxValue;
////                if (curG == int.MaxValue) continue;

////                int tentativeG = curG + 1;
////                int nG = gScore.ContainsKey(n) ? gScore[n] : int.MaxValue;

////                if (tentativeG < nG)
////                {
////                    cameFrom[n] = current;
////                    gScore[n] = tentativeG;
////                    fScore[n] = tentativeG + Heuristic(n, goalC);

////                    if (!open.Contains(n))
////                        open.Add(n);
////                }
////            }
////        }

////        return null;
////    }

////    List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
////    {
////        var total = new List<Vector2Int> { current };
////        while (cameFrom.ContainsKey(current))
////        {
////            current = cameFrom[current];
////            total.Add(current);
////        }
////        total.Reverse();
////        return total;
////    }

////    // =========================
////    // Random walk
////    // =========================

////    void UpdateRandomMove()
////    {
////        if (CurrentCell == GoalCell) return;

////        if (!hasTarget)
////        {
////            PickNextTarget();
////            if (!hasTarget) return;
////        }

////        Vector3 targetWorld = CellToWorld(targetCell);

////        bool moved = MoveTowardsWorld_NoPenetration(targetWorld);
////        if (!moved)
////        {
////            // ǂŎ~܂F̃^[Qbĝ͎ĂđIђ
////            hasTarget = false;
////            return;
////        }

////        if (Vector3.Distance(transform.position, targetWorld) <= arriveEps)
////        {
////            // uZBvOݓ_iRandomj
////            // Debug.Log($"[Random] Visit {targetCell}");

////            lastCell = CurrentCell;
////            hasLast = true;
////            LogVisitAtCell(targetCell);
////            hasTarget = false;
////        }
////    }

////    void PickNextTarget()
////    {
////        Vector2Int cur = CurrentCell;
////        List<Vector2Int> neigh = Get4Neighbors(cur);

////        if (neigh.Count == 0)
////        {
////            hasTarget = false;
////            return;
////        }

////        if (avoidImmediateBacktrack && hasLast && neigh.Count >= 2)
////        {
////            neigh.RemoveAll(n => n == lastCell);
////            if (neigh.Count == 0)
////                neigh = Get4Neighbors(cur);
////        }

////        targetCell = neigh[Random.Range(0, neigh.Count)];
////        hasTarget = true;
////    }

////    // =========================
////    // Gizmos
////    // =========================

////    void OnDrawGizmos()
////    {
////        if (!drawPathGizmos) return;
////        if (mode != BaselineMode.AStarOracle) return;
////        if (pathCells == null) return;

////        Gizmos.color = Color.cyan;
////        for (int i = 0; i < pathCells.Count; i++)
////        {
////            Vector3 w = new Vector3(pathCells[i].x * cellSize, transform.position.y + 0.1f, pathCells[i].y * cellSize);
////            Gizmos.DrawSphere(w, 0.1f);

////            if (i + 1 < pathCells.Count)
////            {
////                Vector3 w2 = new Vector3(pathCells[i + 1].x * cellSize, transform.position.y + 0.1f, pathCells[i + 1].y * cellSize);
////                Gizmos.DrawLine(w, w2);
////            }
////        }
////    }
////}

//////using System.Collections.Generic;
//////using UnityEngine;

//////public class BaselineAgent : MonoBehaviour
//////{
//////    public enum BaselineMode
//////    {
//////        AStarOracle,     // 純A*: 開始時に一度だけパス計算→追従（詰まれば再計算）
//////        AStarEpsilon     // A*ベース＋εでランダムに逸れる（毎手A*で最善手を参照）
//////    }

//////    [Header("Mode")]
//////    public BaselineMode mode = BaselineMode.AStarEpsilon;

//////    [Header("Grid Settings")]
//////    public float cellSize = 1f;

//////    [Header("Collision (no penetration)")]
//////    public LayerMask wallLayer;          // 壁レイヤー（めり込み禁止の対象）
//////    public float agentRadius = 0.35f;     // 通路幅に合わせて調整
//////    public float agentHeight = 1.8f;      // カプセル高さ

//////    [Header("World Bounds (A*が全域探索できる範囲)")]
//////    public int minX = -50;
//////    public int maxX = 50;
//////    public int minZ = -50;
//////    public int maxZ = 50;

//////    [Header("Movement")]
//////    public float moveSpeed = 3.0f;
//////    public float arriveEps = 0.05f;

//////    [Header("Goal (optional)")]
//////    public Transform goal;              // 直接アサインしてもOK
//////    public string goalTag = "Goal";     // goal未設定ならTagから探す

//////    [Header("A*+Random (epsilon) Settings")]
//////    [Range(0f, 1f)]
//////    public float epsilon = 0.2f;        // εの確率で「A*最善手以外」を選ぶ
//////    public bool avoidImmediateBacktrack = true;

//////    [Header("A* Debug")]
//////    public bool drawPathGizmos = true;

//////    // ---- runtime (AStarOracle) ----
//////    private List<Vector2Int> pathCells;
//////    private int pathIndex;

//////    // ---- runtime (AStarEpsilon) ----
//////    private Vector2Int lastCell;
//////    private bool hasLast;
//////    private Vector2Int targetCell;
//////    private bool hasTarget;

//////    private Vector2Int CurrentCell => WorldToCell(transform.position);
//////    private Vector2Int GoalCell => goal ? WorldToCell(goal.position) : Vector2Int.zero;

//////    void Start()
//////    {
//////        // goal未設定なら Tag で拾う（Prefab運用向け）
//////        if (goal == null && !string.IsNullOrEmpty(goalTag))
//////        {
//////            var g = GameObject.FindGameObjectWithTag(goalTag);
//////            if (g != null) goal = g.transform;
//////        }

//////        if (goal == null)
//////        {
//////            Debug.LogError($"[{name}] Goal is not found. Set goal in Inspector or add Tag '{goalTag}' to the goal object.");
//////            enabled = false;
//////            return;
//////        }

//////        if (mode == BaselineMode.AStarOracle)
//////        {
//////            BuildAStarPath_Oracle();
//////        }
//////        else
//////        {
//////            hasLast = false;
//////            hasTarget = false;
//////        }
//////    }

//////    void Update()
//////    {
//////        if (mode == BaselineMode.AStarOracle)
//////            UpdateAStarMove_Oracle();
//////        else
//////            UpdateAStarMove_Epsilon();
//////    }

//////    // =========================
//////    // Common helpers
//////    // =========================

//////    Vector2Int WorldToCell(Vector3 world)
//////    {
//////        int x = Mathf.RoundToInt(world.x / cellSize);
//////        int z = Mathf.RoundToInt(world.z / cellSize);
//////        return new Vector2Int(x, z);
//////    }

//////    Vector3 CellToWorld(Vector2Int cell)
//////    {
//////        return new Vector3(cell.x * cellSize, transform.position.y, cell.y * cellSize);
//////    }

//////    bool InBounds(Vector2Int c)
//////    {
//////        return (c.x >= minX && c.x <= maxX && c.y >= minZ && c.y <= maxZ);
//////    }

//////    // 「移動と同じ基準」で通行可能か判定（A*の近傍生成も実移動もこれに統一）
//////    bool CanTraverseEdge(Vector3 fromPos, Vector3 toPos)
//////    {
//////        Vector3 delta = toPos - fromPos;
//////        float dist = delta.magnitude;
//////        if (dist <= 0.0001f) return true;

//////        Vector3 dir = delta / dist;

//////        Vector3 fromP1 = fromPos + Vector3.up * 0.1f;
//////        Vector3 fromP2 = fromPos + Vector3.up * Mathf.Max(0.2f, agentHeight - 0.1f);

//////        Vector3 toP1 = toPos + Vector3.up * 0.1f;
//////        Vector3 toP2 = toPos + Vector3.up * Mathf.Max(0.2f, agentHeight - 0.1f);

//////        if (Physics.CheckCapsule(toP1, toP2, agentRadius, wallLayer, QueryTriggerInteraction.Ignore))
//////            return false;

//////        if (Physics.CapsuleCast(fromP1, fromP2, agentRadius, dir, dist, wallLayer, QueryTriggerInteraction.Ignore))
//////            return false;

//////        return true;
//////    }

//////    List<Vector2Int> Get4Neighbors(Vector2Int c)
//////    {
//////        var list = new List<Vector2Int>(4);
//////        var cand = new Vector2Int[]
//////        {
//////            new Vector2Int(c.x + 1, c.y),
//////            new Vector2Int(c.x - 1, c.y),
//////            new Vector2Int(c.x, c.y + 1),
//////            new Vector2Int(c.x, c.y - 1),
//////        };

//////        Vector3 fromWorld = CellToWorld(c);

//////        foreach (var n in cand)
//////        {
//////            if (!InBounds(n)) continue;

//////            Vector3 toWorld = CellToWorld(n);
//////            if (!CanTraverseEdge(fromWorld, toWorld)) continue;

//////            list.Add(n);
//////        }
//////        return list;
//////    }

//////    // true: 進めた / false: 壁で止まった
//////    bool MoveTowardsWorld_NoPenetration(Vector3 targetWorld)
//////    {
//////        Vector3 p = transform.position;
//////        Vector3 t = new Vector3(targetWorld.x, p.y, targetWorld.z);

//////        Vector3 next = Vector3.MoveTowards(p, t, moveSpeed * Time.deltaTime);

//////        if (CanTraverseEdge(p, next))
//////        {
//////            transform.position = next;
//////            return true;
//////        }

//////        return false;
//////    }

//////    // =========================
//////    // A* Oracle (pure A*)
//////    // =========================

//////    void BuildAStarPath_Oracle()
//////    {
//////        Vector2Int start = CurrentCell;
//////        Vector2Int goalC = GoalCell;

//////        pathCells = AStar(start, goalC);

//////        if (pathCells == null || pathCells.Count == 0)
//////        {
//////            Debug.LogWarning($"[{name}] A* path not found. Check bounds or wall settings.");
//////            pathIndex = 0;
//////            return;
//////        }

//////        pathIndex = 0;
//////        if (pathCells[0] == start && pathCells.Count > 1) pathIndex = 1;
//////    }

//////    void UpdateAStarMove_Oracle()
//////    {
//////        if (CurrentCell == GoalCell) return;
//////        if (pathCells == null || pathCells.Count == 0) return;
//////        if (pathIndex >= pathCells.Count) return;

//////        Vector3 targetWorld = CellToWorld(pathCells[pathIndex]);

//////        bool moved = MoveTowardsWorld_NoPenetration(targetWorld);
//////        if (!moved)
//////        {
//////            // 壁で止まった：現状位置から再探索（ズレ対策）
//////            BuildAStarPath_Oracle();
//////            return;
//////        }

//////        if (Vector3.Distance(transform.position, targetWorld) <= arriveEps)
//////        {
//////            // ★「セル到達」ログ差し込み点（A* Oracle）
//////            // Debug.Log($"[A*Oracle] Visit {pathCells[pathIndex]}");
//////            pathIndex++;
//////        }
//////    }

//////    // =========================
//////    // A* + epsilon random
//////    // =========================

//////    void UpdateAStarMove_Epsilon()
//////    {
//////        if (CurrentCell == GoalCell) return;

//////        Vector2Int prevCell = CurrentCell;

//////        if (!hasTarget)
//////        {
//////            PickNextTarget_AStarEpsilon();
//////            if (!hasTarget) return;
//////        }

//////        Vector3 targetWorld = CellToWorld(targetCell);

//////        bool moved = MoveTowardsWorld_NoPenetration(targetWorld);
//////        if (!moved)
//////        {
//////            // 壁で止まった：ターゲットを捨てて次フレームで再選択
//////            hasTarget = false;
//////            return;
//////        }

//////        if (Vector3.Distance(transform.position, targetWorld) <= arriveEps)
//////        {
//////            // ★「セル到達」ログ差し込み点（A*+ε）
//////            // Debug.Log($"[A*Epsilon] Visit {targetCell}");

//////            // prevCell -> targetCell に移動したので、直前セルを更新
//////            lastCell = prevCell;
//////            hasLast = true;

//////            hasTarget = false;
//////        }
//////    }

//////    void PickNextTarget_AStarEpsilon()
//////    {
//////        Vector2Int cur = CurrentCell;
//////        if (cur == GoalCell)
//////        {
//////            hasTarget = false;
//////            return;
//////        }

//////        List<Vector2Int> neigh = Get4Neighbors(cur);
//////        if (neigh.Count == 0)
//////        {
//////            hasTarget = false;
//////            return;
//////        }

//////        // まず A* の「最善手」（次の1セル）を取得
//////        Vector2Int bestNext = Vector2Int.zero;
//////        bool hasBest = false;

//////        var p = AStar(cur, GoalCell);
//////        if (p != null && p.Count >= 2)
//////        {
//////            bestNext = p[1];    // p[0]がcur、p[1]が次手
//////            hasBest = true;
//////        }

//////        // A*最善手が近傍に無い（稀）なら単純にランダム
//////        if (!hasBest || !neigh.Contains(bestNext))
//////        {
//////            targetCell = PickRandomNeighbor(neigh, exclude: null);
//////            hasTarget = true;
//////            return;
//////        }

//////        // εの確率で「最善手以外」へ逸れる（ただし候補があるときだけ）
//////        bool doDeviate = (Random.value < epsilon);

//////        if (doDeviate && neigh.Count >= 2)
//////        {
//////            // 逸れる候補：最善手以外
//////            var alts = new List<Vector2Int>(neigh);
//////            alts.Remove(bestNext);

//////            // 逸れるときだけ「直前セルへの即戻り」を避ける（ただし選択肢が残る場合）
//////            if (avoidImmediateBacktrack && hasLast && alts.Count >= 2)
//////            {
//////                alts.RemoveAll(n => n == lastCell);
//////                if (alts.Count == 0)
//////                {
//////                    // 避けすぎて候補が無いなら戻りも許可
//////                    alts = new List<Vector2Int>(neigh);
//////                    alts.Remove(bestNext);
//////                }
//////            }

//////            if (alts.Count > 0)
//////            {
//////                targetCell = alts[Random.Range(0, alts.Count)];
//////                hasTarget = true;
//////                return;
//////            }
//////        }

//////        // 通常：A*最善手
//////        targetCell = bestNext;
//////        hasTarget = true;
//////    }

//////    Vector2Int PickRandomNeighbor(List<Vector2Int> neigh, Vector2Int? exclude)
//////    {
//////        if (neigh == null || neigh.Count == 0) return Vector2Int.zero;

//////        if (exclude.HasValue && neigh.Count >= 2)
//////        {
//////            var list = new List<Vector2Int>(neigh);
//////            list.Remove(exclude.Value);
//////            if (list.Count > 0)
//////                return list[Random.Range(0, list.Count)];
//////        }

//////        return neigh[Random.Range(0, neigh.Count)];
//////    }

//////    // =========================
//////    // A* core
//////    // =========================

//////    int Heuristic(Vector2Int a, Vector2Int b)
//////    {
//////        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); // マンハッタン
//////    }

//////    List<Vector2Int> AStar(Vector2Int start, Vector2Int goalC)
//////    {
//////        var open = new List<Vector2Int>();
//////        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
//////        var gScore = new Dictionary<Vector2Int, int>();
//////        var fScore = new Dictionary<Vector2Int, int>();

//////        open.Add(start);
//////        gScore[start] = 0;
//////        fScore[start] = Heuristic(start, goalC);

//////        int safety = 0;
//////        int safetyMax = (maxX - minX + 1) * (maxZ - minZ + 1) + 1000;

//////        while (open.Count > 0)
//////        {
//////            safety++;
//////            if (safety > safetyMax) break;

//////            // open内のf最小（最小構成：線形探索）
//////            Vector2Int current = open[0];
//////            int bestF = fScore.ContainsKey(current) ? fScore[current] : int.MaxValue;

//////            for (int i = 1; i < open.Count; i++)
//////            {
//////                var c = open[i];
//////                int f = fScore.ContainsKey(c) ? fScore[c] : int.MaxValue;
//////                if (f < bestF)
//////                {
//////                    bestF = f;
//////                    current = c;
//////                }
//////            }

//////            if (current == goalC)
//////                return ReconstructPath(cameFrom, current);

//////            open.Remove(current);

//////            foreach (var n in Get4Neighbors(current))
//////            {
//////                int curG = gScore.ContainsKey(current) ? gScore[current] : int.MaxValue;
//////                if (curG == int.MaxValue) continue;

//////                int tentativeG = curG + 1;
//////                int nG = gScore.ContainsKey(n) ? gScore[n] : int.MaxValue;

//////                if (tentativeG < nG)
//////                {
//////                    cameFrom[n] = current;
//////                    gScore[n] = tentativeG;
//////                    fScore[n] = tentativeG + Heuristic(n, goalC);

//////                    if (!open.Contains(n))
//////                        open.Add(n);
//////                }
//////            }
//////        }

//////        return null;
//////    }

//////    List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
//////    {
//////        var total = new List<Vector2Int> { current };
//////        while (cameFrom.ContainsKey(current))
//////        {
//////            current = cameFrom[current];
//////            total.Add(current);
//////        }
//////        total.Reverse();
//////        return total;
//////    }

//////    // =========================
//////    // Gizmos
//////    // =========================

//////    void OnDrawGizmos()
//////    {
//////        if (!drawPathGizmos) return;
//////        if (mode != BaselineMode.AStarOracle) return;
//////        if (pathCells == null) return;

//////        Gizmos.color = Color.cyan;
//////        for (int i = 0; i < pathCells.Count; i++)
//////        {
//////            Vector3 w = new Vector3(pathCells[i].x * cellSize, transform.position.y + 0.1f, pathCells[i].y * cellSize);
//////            Gizmos.DrawSphere(w, 0.1f);

//////            if (i + 1 < pathCells.Count)
//////            {
//////                Vector3 w2 = new Vector3(pathCells[i + 1].x * cellSize, transform.position.y + 0.1f, pathCells[i + 1].y * cellSize);
//////                Gizmos.DrawLine(w, w2);
//////            }
//////        }
//////    }
//////}
