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

    [Header("Chase Enemy If Near (Player behavior)")]
    public bool chaseEnemyIfNear = true;
    public string enemyTag = "Enemy";

    [Tooltip("この距離以内に敵がいれば追跡開始（ワールド距離）")]
    public float chaseStartRange = 8f;

    [Tooltip("追跡解除距離（開始より少し大きくしてバタつき防止）")]
    public float chaseStopRange = 10f;

    [Tooltip("敵検索の間隔（秒）")]
    public float enemyScanInterval = 0.2f;

    [Tooltip("追跡中、何フレームごとにA*を組み直すか（敵が動くなら有効）")]
    public int repathEveryFramesWhileChasing = 15;

    private Transform _chaseEnemy;
    private bool _isChasingEnemy = false;
    private float _nextEnemyScanTime = 0f;
    private Vector2Int _lastDesiredGoalCell = new Vector2Int(int.MaxValue, int.MaxValue);

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

        // 追跡状態の更新（近くに敵がいれば追跡）
        UpdateChaseEnemyState();

        // A*モードの場合：目的地が変わったら経路を作り直す
        if (mode == BaselineMode.AStarOracle)
        {
            Vector2Int desired = GetDesiredGoalCell();

            // 目的地セルが変わった（追跡ON/OFF切替、敵が別セルへ移動など）
            if (desired != _lastDesiredGoalCell)
            {
                BuildAStarPath(desired);
                _lastDesiredGoalCell = desired;
            }
            else if (_isChasingEnemy && repathEveryFramesWhileChasing > 0 &&
                     (Time.frameCount % repathEveryFramesWhileChasing == 0))
            {
                // 追跡中は定期的に組み直し（敵が動くなら）
                BuildAStarPath(desired);
            }
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

    //void BuildAStarPath()
    //{
    //    Vector2Int start = CurrentCell;
    //    Vector2Int goalCell = GoalCell;

    //    // A* (grid)
    //    pathCells = AStar(start, goalCell);
    //    if (pathCells == null || pathCells.Count == 0)
    //    {
    //        Debug.LogWarning($"[{name}] A* path not found. Check bounds or wall settings.");
    //        pathIndex = 0;
    //        return;
    //    }

    //    pathIndex = 0;
    //    if (pathCells[0] == start && pathCells.Count > 1) pathIndex = 1;
    //}
    void BuildAStarPath()
    {
        BuildAStarPath(GoalCell);
    }

    void BuildAStarPath(Vector2Int goalCell)
    {
        Vector2Int start = CurrentCell;

        // A* (grid)
        pathCells = AStar(start, goalCell);
        if (pathCells == null || pathCells.Count == 0)
        {
            Debug.LogWarning($"[{name}] A* path not found. Check bounds or wall settings.");
            pathIndex = 0;
            return;
        }

        pathIndex = 0;

        // 先頭が現在地セルならスキップ（すぐ同じセルに向かって足踏みするのを防ぐ）
        if (pathCells[0] == start && pathCells.Count > 1)
            pathIndex = 1;
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

    private Vector2Int GetDesiredGoalCell()
    {
        if (_isChasingEnemy && _chaseEnemy != null)
        {
            Vector3 p = _chaseEnemy.position;
            int x = Mathf.RoundToInt(p.x / cellSize);
            int z = Mathf.RoundToInt(p.z / cellSize);
            return new Vector2Int(x, z);
        }
        return GoalCell;
    }

    private void UpdateChaseEnemyState()
    {
        if (!chaseEnemyIfNear) { _isChasingEnemy = false; _chaseEnemy = null; return; }
        if (Time.time < _nextEnemyScanTime) return;
        _nextEnemyScanTime = Time.time + Mathf.Max(0.01f, enemyScanInterval);

        // 近い敵を探す（ワールド距離）
        GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTag);
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
            // 追跡開始
            if (best != null && bestD <= chaseStartRange)
            {
                _isChasingEnemy = true;
                _chaseEnemy = best;
            }
        }
        else
        {
            // 追跡解除（敵が消えた or 遠い）
            if (best == null || bestD >= chaseStopRange)
            {
                _isChasingEnemy = false;
                _chaseEnemy = null;
            }
            else
            {
                // 追跡継続中：対象を更新（最も近い敵）
                _chaseEnemy = best;
            }
        }
    }

}