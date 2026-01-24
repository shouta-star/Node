using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class CellFromStart : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 3f;
    public float cellSize = 1f;
    public float rayDistance = 1f;
    public LayerMask wallLayer;
    public LayerMask nodeLayer;

    ////[Header("初期設定")]
    //public enum StartDirectionMode
    //{
    //    Manual,
    //    AutoFromSpawnRelativeToOrigin,
    //    AutoFromSpawnRelativeToStartNode,
    //    AutoByPlayerId
    //}

    [Tooltip("startDirectionMode が Manual のときのみ使用。Auto のときは起動時に上書きされます。")]
    public Vector3 startDirection = Vector3.forward;

    [Tooltip("Auto で決めた方向を反転（外向き⇔内向き）したい場合にON。")]
    public bool invertAutoStartDirection = false;

    //public StartDirectionMode startDirectionMode = StartDirectionMode.AutoFromSpawnRelativeToOrigin;

    [Tooltip("AutoFromSpawnRelativeToOrigin の基準点。ここが入っている場合は gridOrigin より優先してこのTransform位置を基準にします（PlayerSpawner / spawnPoint を想定）。")]
    public Transform startDirectionOrigin;

    public Vector3 gridOrigin = Vector3.zero;
    public GameObject nodePrefab;

    [Header("Damage Stop (No-damage resume)")]
    public bool stopMoveWhileRecentlyDamaged = true;

    [Tooltip("Movement is paused until Time.time >= damageStopUntilTime")]
    private float damageStopUntilTime = -1f;

    [Header("探索パラメータ")]
    public int unknownReferenceDepth = 3; // ★ BFS探索深さとして使用

    [Header("スコア重み（A：現状の式）")]
    public float weightUnknown = 1f;
    public float weightDistance = 1f;

    [Header("Local Next-Step (no full path)")]
    [Tooltip("If true, next node is chosen only from neighbors using node weights (no full path search).")]
    public bool useLocalNextStepOnly = true;

    [Tooltip("Target proximity weight. Higher means stronger pull toward bestTarget (smaller distance).")]
    public float weightToTarget = 1f;

    [Tooltip("Large penalty to avoid immediate backtrack to the previous node.")]
    public float backtrackPenalty = 1000f;

    [Tooltip("If true, when from.links is empty, also treat reverse links (other.links contains from) as candidates.")]
    public bool localUseReverseLinkRescue = true;

    [Tooltip("If true, write computed weight to MapNode.value and distance-to-target to MapNode.DistanceFromGoal (debug/visualization).")]
    public bool localWriteWeightToNode = true;

    [Tooltip("MustPass score scale when using local next-step. (Uses normalEnterCost - GetEnterCost(node)) * scale")]
    public float mustPassScoreScale = 10f;

    [Tooltip("Penalty applied per prior visit to a candidate node (per player). Higher = avoids revisiting.")]
    public float revisitPenaltyPerVisit = 5f;

    [Tooltip("MustPass field multiplier. Set so (mustPassFieldBase * multiplier) > targetFieldMax to dominate.")]
    public float mustPassFieldMultiplier = 6f; // 200*6=1200 > 1000

    // -----------------------------
    // Weight Field Params (Manhattan)
    // -----------------------------
    [Header("Weight Field (Manhattan)")]
    [Tooltip("BestTarget weight at the target (the maximum).")]
    //public float targetFieldMax = 1000f;
    public float targetFieldMax = 100f;

    [Tooltip("How much the BestTarget weight decreases per Manhattan step.")]
    public float targetFieldSlope = 10f;

    [Tooltip("MustPass peak bonus at MustPass node (keep smaller than targetFieldMax).")]
    public float mustPassFieldBase = 200f;
    //public float mustPassFieldBase = 1000f;

    [Tooltip("MustPass bonus decay per Manhattan step (0-1). Higher = longer reach.")]
    [Range(0.5f, 0.99f)]
    //public float mustPassFieldDecay = 0.85f;
    public float mustPassFieldDecay = 0.95f;

    [Tooltip("How often (frames) to rebuild MustPass cache. (For 10+ MustPass, 30 is a good start.)")]
    public int mustPassCacheRefreshFrames = 30;
    [Header("Ray設定")]
    public int linkRayMaxSteps = 100;

    [Header("Point Influence (Colliderなし)")]
    public Vector3 pointGridOrigin = Vector3.zero;
    public float pointCellSize = 1f;
    public bool debugPointLog = true;

    [SerializeField] private bool dbgExclude = true;
    [SerializeField] private int dbgEveryNFrames = 30;

    private void DLog(string msg)
    {
        if (!dbgExclude) return;
        Debug.Log(msg);
    }

    [Header("デバッグ")]
    public bool debugLog = true;
    public bool debugRay = true;
    public Renderer bodyRenderer;
    public Material exploreMaterial;

    // =============================
    // ★ Player ID（CellFromStart 用）
    // =============================
    private static int nextPlayerId = 1;  // 全 CellFromStart 共通のカウンタ
    public int playerId;                  // このインスタンス固有の ID

    // 内部状態
    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private MapNode currentNode;

    private List<MapNode> recentNodes = new List<MapNode>();

    private MapNode lastBestTarget = null;
    private bool arrivedThisNode = false;
    private bool blockTryExploreThisFrame = false;

    [Header("Damage Stop")]
    [Tooltip("If true, once this player takes damage, it will stop moving permanently (until it is destroyed).")]
    public bool stopForeverWhenDamaged = true;
    private bool movementLockedForever = false;

    // ★ 追加：今の lastBestTarget が「Start最遠由来」かどうか
    private bool lastTargetIsFarthest = false;

    [Header("循環回避：Unknown=0 フォールバックのクールダウン")]
    public int fallbackCooldownSteps = 6;  // 例：6歩の間、同じノードをフォールバック候補にしない

    // MapNode → 「このstepIndex未満なら選ばない」
    private Dictionary<MapNode, int> fallbackCooldownUntil = new Dictionary<MapNode, int>();

    // ★ Unknown=0 フェーズで既に選んだフォールバックターゲット
    private HashSet<MapNode> usedFallbackTargets = new HashSet<MapNode>();

    // ------------------------------
    // Excluded(=通行不可) / 経路再構築・ループ対策用
    // ------------------------------
    private bool needRepathToBestTarget = false;
    private MapNode blockedExcludedFrom = null;
    private MapNode blockedExcludedNode = null;

    // 「このターゲットを今は追わない（しばらく別行動する）」用
    private MapNode tempAvoidTarget = null;
    private int tempAvoidUntilSteps = -1;
    [SerializeField] private int tempAvoidSteps = 20;

    // 2点間往復検知（同じ2ノードで backtrack が続いたら一旦ターゲットを手放す）
    private MapNode oscillA = null;
    private MapNode oscillB = null;
    private MapNode oscillTarget = null;
    private int oscillCount = 0;
    [SerializeField] private int oscillLimit = 4;

    // BFS に「この1本のエッジは使わない」を混ぜる（即時の別ルート探索用）
    private bool hasTempForbidEdge = false;
    private MapNode tempForbidFrom = null;
    private MapNode tempForbidTo = null;

    // 直近2ノード（backtrack 判定用）
    private MapNode lastVisitedNode = null;
    private MapNode prevVisitedNode = null;


    // --- Per-player visit cost (revisit penalty) ---
    // Key: MapNode instance, Value: how many times THIS player has reached the node.
    private Dictionary<MapNode, int> nodeVisitCounts = new Dictionary<MapNode, int>();

    // 「今のターゲットに対して、行き止まりっぽい遷移は一旦避ける」用（ターゲットが変わったらクリア）
    private struct EdgeKey
    {
        public int fromId;
        public int toId;

        public EdgeKey(int fromId, int toId)
        {
            this.fromId = fromId;
            this.toId = toId;
        }
    }

    private MapNode tabooTarget = null;
    private HashSet<long> tabooEdges = new HashSet<long>(); // (fromId,toId) を 64bit に詰める

    // ★ Playerごとの寿命設定：新規Nodeを何個置いたら消えるか
    [Header("寿命設定")]
    [Tooltip("このプレイヤーが新規に設置できるNode数の上限")]
    public int destroyAfterNewNodes;

    // ★ 今までにこのPlayerが新規に作ったNode数
    private int newNodeCreatedCount = 0;

    [Header("MustPass (時間失効)")]
    [SerializeField] private float mustPassLifetimeSec = 8f;          // 例：8秒で失効（0以下なら失効なし）
    [SerializeField] private float mustPassScanIntervalSec = 0.25f;   // 例：0.25秒ごとに全ノードを軽くスキャン
    private float _nextMustPassScanTime = 0f;

    [SerializeField] private bool useMustPassAsWeight = true;

    // ノードに「入る」コスト（小さいほど通りたくなる）
    [SerializeField] private float normalEnterCost = 1f;
    [SerializeField] private float mustPassEnterCost = 0.25f;   // 0.1〜0.5くらいで調整

    // cell -> 失効時刻(Time.time)
    private readonly Dictionary<Vector2Int, float> mustPassExpireAt = new Dictionary<Vector2Int, float>();
    private readonly List<Vector2Int> _tmpExpiredMustPassCells = new List<Vector2Int>();

    // =============================
    // ★ 評価ログ用（CellFromStart 単体）
    // =============================
    [Header("評価ログ（CellFromStart）")]
    public int stepsWalked = 0;          // 歩いたマス数
    public int uniqueNodesVisited = 0;   // 一度でも訪れた Node 種類数
    public int deadEndEnterCount = 0;    // 行き止まり Node に入った回数
    public bool goalReached = false;     // Goal に到達したか
    public int frameToGoal = -1;         // 到達フレーム（未到達は -1）

    // 内部管理用：訪問済み Node 集合
    private HashSet<MapNode> visitedNodes = new HashSet<MapNode>();

    private int stepIndex = 0;  // このPlayerが何回目のNode到達か

    public enum UnknownSelectMode
    {
        Random,
        Nearest,
        Farthest,
        MostUnknown
    }

    [Header("探索方針①：Unknownの選択方式")]
    public UnknownSelectMode unknownSelectMode = UnknownSelectMode.Farthest;

    public enum TargetUpdateMode
    {
        EveryNode,      // 現状の動作：毎回再計算
        OnArrival       // 到達したときだけ再計算
    }

    [Header("探索方針②：targetNode 更新方式")]
    public TargetUpdateMode targetUpdateMode = TargetUpdateMode.EveryNode;

    // ★ 追加：Unknown が 0 のときのフォールバック先
    public enum NoUnknownFallbackMode
    {
        FarthestFromStart,   // Start から最遠 Node（今まで通り）
        NewestNode           // 一番最近できた Node（allNodes の末尾）
    }

    [Header("探索方針③：Unknown=0 のときのターゲット")]
    public NoUnknownFallbackMode noUnknownFallbackMode = NoUnknownFallbackMode.FarthestFromStart;

    // ===== Excluded / Repath loop fix helpers =====
    [SerializeField] private int oscillationMemorySize = 6;      // 直近ノード記憶数
    [SerializeField] private int avoidTargetFrames = 90;          // 一時回避の持続フレーム
    [SerializeField] private int tabooEdgeFrames = 120;           // 辺の一時禁止の持続フレーム

    private readonly Queue<MapNode> recentNodeMemory = new Queue<MapNode>();
    private readonly Dictionary<MapNode, int> avoidedTargetsUntil = new Dictionary<MapNode, int>();
    private readonly Dictionary<ulong, int> tabooEdgesUntil = new Dictionary<ulong, int>();

    private static FieldInfo _fiIsMustPass;
    private static MethodInfo _miIsMustPassCell;
    // ★ Playerごと：一度踏んだ MustPass（セル）を記録
    private HashSet<Vector2Int> visitedMustPassCells = new HashSet<Vector2Int>();

    private System.Random dirRng;

    void Start()
    {
        playerId = nextPlayerId;
        nextPlayerId++;
        gameObject.name = $"Player_{playerId}";

        dirRng = new System.Random(playerId * 10007 + 12345);
        SyncScoringParamsToMapNode();


        // プレイヤー座標をスナップ
        Vector3 snapped = SnapToGrid(transform.position);
        transform.position = snapped;
        targetPos = snapped;

        // スポーン位置に合わせて StartDirection を自動設定（必要な場合のみ）
        ApplyAutoStartDirection(snapped);
        moveDir = startDirection.normalized;

        ApplyVisual();

        // ------------------------------------------------
        // ① StartNode の設定（最初に行う）
        // ------------------------------------------------
        Vector2Int cell = WorldToCell(snapped);

        MapNode nodeAtStart = null;

        if (MapNode.allNodeCells.Contains(cell))
        {
            nodeAtStart = MapNode.FindByCell(cell);
        }
        else
        {
            GameObject obj = Instantiate(nodePrefab, snapped, Quaternion.identity);
            nodeAtStart = obj.GetComponent<MapNode>();
            nodeAtStart.cell = cell;
            MapNode.allNodeCells.Add(cell);
        }

        // StartNode が未設定なら “ここで” 設定する
        if (MapNode.StartNode == null)
        {
            MapNode.StartNode = nodeAtStart;
            nodeAtStart.distanceFromStart = 0;

            //Debug.Log($"[SET STARTNODE] StartNode = {nodeAtStart.name}");
        }

        // ------------------------------------------------
        // ② currentNode の設定（StartNode 設定後）
        // ------------------------------------------------
        currentNode = nodeAtStart;
        RegisterCurrentNode(currentNode);

        //Debug.Log($"[SET CURRENTNODE] currentNode = {currentNode.name}");

        // ★ 評価ログ：スタート時点での訪問情報を初期化
        visitedNodes.Clear();
        if (currentNode != null)
        {
            visitedNodes.Add(currentNode);
            uniqueNodesVisited = visitedNodes.Count;
        }

        goalReached = false;
        frameToGoal = -1;
        stepsWalked = 0;
        deadEndEnterCount = 0;
    }

    private void SyncScoringParamsToMapNode()
    {
        // Move Target/MustPass/Unknown scoring to MapNode side (shared functions & caches)
        MapNode.SetScoringParams(
            targetFieldMax,
            targetFieldSlope,
            useMustPassAsWeight,
            mustPassFieldBase,
            mustPassFieldDecay,
            weightUnknown,
            revisitPenaltyPerVisit,
            mustPassCacheRefreshFrames,
            backtrackPenalty);
    }



    // =============================
    // StartDirection auto assignment
    // =============================
    //private void ApplyAutoStartDirection(Vector3 snappedWorldPos)
    //{
    //    if (startDirectionMode == StartDirectionMode.Manual)
    //        return;

    //    Vector3 dir = Vector3.forward;

    //    if (startDirectionMode == StartDirectionMode.AutoByPlayerId)
    //    {
    //        dir = DirectionFromPlayerId(playerId);
    //    }
    //    else
    //    {
    //        Vector3 refPos = (startDirectionOrigin != null) ? startDirectionOrigin.position : gridOrigin;

    //        if (startDirectionMode == StartDirectionMode.AutoFromSpawnRelativeToStartNode)
    //        {
    //            if (MapNode.StartNode != null)
    //                refPos = MapNode.StartNode.transform.position;
    //        }

    //        Vector3 delta = snappedWorldPos - refPos;
    //        dir = CardinalFromDeltaXZ(delta);

    //        // スポーンが基準と同じ位置（delta=0）になった場合の保険
    //        if (dir.sqrMagnitude < 1e-6f)
    //            dir = DirectionFromPlayerId(playerId);
    //    }

    //    if (invertAutoStartDirection)
    //        dir = -dir;

    //    startDirection = dir;

    //    if (debugLog)
    //        Debug.Log($"[CFS][STARTDIR] playerId={playerId} mode={startDirectionMode} dir={startDirection}");
    //}
    private void ApplyAutoStartDirection(Vector3 snappedWorldPos)
    {
        // PlayerSpawner から渡される想定の基準点
        Vector3 refPos = (startDirectionOrigin != null) ? startDirectionOrigin.position : gridOrigin;

        Vector3 delta = snappedWorldPos - refPos;
        Vector3 dir = CardinalFromDeltaXZ(delta);

        // 基準点と同位置になった保険（ゼロ方向回避）
        if (dir.sqrMagnitude < 1e-6f)
            dir = DirectionFromPlayerId(playerId);

        if (invertAutoStartDirection)
            dir = -dir;

        startDirection = dir;

        if (debugLog)
            Debug.Log($"[CFS][STARTDIR] playerId={playerId} origin={(startDirectionOrigin ? startDirectionOrigin.name : "gridOrigin")} dir={startDirection}");
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

    void Update()
    {
        // 被弾後、一定時間ノーダメなら再開（その間は完全停止）
        if (stopMoveWhileRecentlyDamaged && Time.time < damageStopUntilTime)
        {
            return;
        }

        if (movementLockedForever) return;

        //------------------------------------------------------
        // ① 移動中ならまず位置を更新（★最優先）
        //------------------------------------------------------
        if (isMoving)
        {
            //Debug.Log("[UPDATE] isMoving → MoveToTarget()");
            MoveToTarget();
            return; // ← TryExploreMove を先に呼ばないため必須
        }

        //------------------------------------------------------
        // ② 移動完了直後の1フレームだけ TryExploreMove をブロック
        //------------------------------------------------------
        if (blockTryExploreThisFrame)
        {
            //Debug.Log("[UPDATE] blockTryExploreThisFrame → skip TryExploreMove");
            blockTryExploreThisFrame = false;
            return;
        }

        //------------------------------------------------------
        // ③ Node設置 or 通路進行
        //------------------------------------------------------
        if (CanPlaceNodeHere())
        // ★ Node 中心にいるかどうかを判定してから TryExploreMove を呼ぶ
        //if (IsExactlyOnNodeCenter())
        {
            //Debug.Log("[UPDATE] CanPlaceNodeHere()=true → TryExploreMove()");
            //TryExploreMove();
            if (arrivedThisNode)
            {
                //Debug.Log("[UPDATE] Node到達直後 → TryExploreMove()");
                arrivedThisNode = false;
                TryExploreMove();
                return;
            }
            else
            {
                //Debug.Log("[UPDATE] Node中心だが到達直後でない → MoveForward()");
                MoveForward();
                return;
            }
        }
        //else
        {
            //Debug.Log("[UPDATE] CanPlaceNodeHere()=false → MoveForward()");
            MoveForward();
        }
    }

    private void ApplyVisual()
    {
        if (bodyRenderer != null && exploreMaterial != null)
            bodyRenderer.material = exploreMaterial;
    }

    private bool CanPlaceNodeHere()
    {
        // ★ Node中心に近いかどうか（これが最重要）
        Vector3 snapped = SnapToGrid(transform.position);
        float dist = Vector3.Distance(transform.position, snapped);

        if (dist > 0.2f)
            return false;  // 中心にいない → Node置けない

        // ★ 壁・開放方向の判定（既存ロジックをそのまま使用）
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
        bool leftWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
        bool rightWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

        int openings = (!frontWall ? 1 : 0) + (!leftWall ? 1 : 0) + (!rightWall ? 1 : 0);

        return frontWall || openings >= 2;

        //// Nodeを置くのは「意思決定点」だけにする

        //// 行き止まり（前も左右も壁）
        //bool deadEnd = frontWall && leftWall && rightWall;

        //// 曲がり角（前が壁で、左右どちらかだけ空き）
        //bool corner = frontWall && (leftWall != rightWall);

        //// 分岐（左右が両方空き：ここは判断点）
        //bool split = (!leftWall && !rightWall);

        //return deadEnd || corner || split;

    }

    private void MoveForward()
    {
        Vector3 next = transform.position + moveDir * cellSize;

        // 壁チェック
        if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
                            moveDir,
                            cellSize,
                            wallLayer))
        {
            isMoving = false;

            // BestTarget追従中なら「別ルートが無いか」次フレームで再探索する
            if (lastBestTarget != null)
            {
                needRepathToBestTarget = true;
                blockedExcludedFrom = null;
                blockedExcludedNode = null;
            }

            return;
        }

        Vector3 nextSnap = SnapToGrid(next);
        Vector2Int nextCell = WorldToCell(nextSnap);

        // ★ 次セルに「既存の Excluded Node があるか？」だけを見る（DangerPointセル判定はしない）
        MapNode nextNode = MapNode.FindByCell(nextCell);

        if (nextNode != null && nextNode.isExcluded)
        {
            if (debugLog)
                Debug.Log($"[MOVE][BLOCK_EX_NODE] {currentNode?.name} -> {nextNode.name}");

            // 「この一手は塞がれた」→ BestTargetへ向かう別ルートを次フレームで探す
            needRepathToBestTarget = true;
            blockedExcludedFrom = currentNode;
            blockedExcludedNode = nextNode;

            isMoving = false;
            return;
        }

        targetPos = nextSnap;
        isMoving = true;

        // ★ 評価ログ：1マス分の移動が確定したので歩数を加算
        stepsWalked++;
    }


    private void MoveToTarget()
    {
        if (!isMoving) return;

        const float arriveThreshold = 0.05f;

        // ★ まだ到達していない場合は移動し続ける
        if (Vector3.Distance(transform.position, targetPos) > arriveThreshold)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                moveSpeed * Time.deltaTime
            );
            return;
        }

        //------------------------------------------------------
        // ★ Nodeへ完全到達した瞬間（1回だけ実行）
        //------------------------------------------------------
        transform.position = targetPos;

        //Debug.Log(
        //    $"[CHECK-ARRIVE] Arrived at targetPos={targetPos} | " +
        //    $"actualPos={transform.position} | " +
        //    $"arrivedThisNode={arrivedThisNode}"
        //);

        isMoving = false;

        // ★ 到達したのでフラグON（次フレーム、Update側で使う）
        arrivedThisNode = true;

        // ★ Node参照を確定
        currentNode = MapNode.FindByCell(WorldToCell(targetPos));

        if (currentNode != null)
        {
            currentNode.passCount++;
            currentNode.OnPassed();
        }

        // ===== 評価ログ更新ここから =====
        if (currentNode != null)
        {
            // ① 訪問済み Node 集合を更新
            if (visitedNodes.Add(currentNode))
            {
                uniqueNodesVisited = visitedNodes.Count;
            }

            // ② 行き止まり Node に入った回数（links=1）
            if (currentNode.links != null && currentNode.links.Count == 1)
            {
                deadEndEnterCount++;
            }

            // ③ Goal 到達判定：Tag が "Goal" の Node に来たら
            if (!goalReached && currentNode.gameObject.CompareTag("Goal"))
            {
                //Debug.Log("[GOAL] GoalNode に到達しました");
                goalReached = true;
                frameToGoal = Time.frameCount;

                if (RestartManager.Instance != null)
                {
                    RestartManager.Instance.WriteCellFromStartPlayerCsv(this);
                    RestartManager.Instance.WriteRunSummaryCsv(this);

                    RestartManager.Instance.StartRestart();
                }
            }

            //EvaluationLogger.LogNodeVisit(playerId, Time.frameCount, currentNode);
            EvaluationLogger.LogNodeVisit(
                playerId,
                Time.frameCount,
                currentNode,
                unknownSelectMode.ToString(),
                targetUpdateMode.ToString(),
                noUnknownFallbackMode.ToString(),
                unknownReferenceDepth,
                stepIndex
            );

            // この行のあとでインクリメント
            stepIndex++;
        }
        else
        {
            //Debug.LogWarning($"[GOAL-DEBUG] currentNode が null です。targetPos={targetPos}");
        }
        // ===== 評価ログ更新ここまで =====

        //------------------------------------------------------
        // ★ このフレームに TryExploreMove() を呼ばせない
        //------------------------------------------------------
        blockTryExploreThisFrame = true;
    }


    private void RegisterCurrentNode(MapNode node)
    {
        if (node == null) return;

        // ★ 直近2ノード更新（往復検知・別ルート探索に使う）
        if (lastVisitedNode != node)
        {
            prevVisitedNode = lastVisitedNode;
            lastVisitedNode = node;

            // ★ ここで「このPlayerがこのNodeに到達した回数」を加算（=以後のコストが上がる）
            IncrementVisitCount(node);
        }

        // 直近ノード記録
        recentNodes.Add(node);
        //if (recentNodes.Count > recentNodeMemory)
        if (recentNodes.Count > oscillationMemorySize)
            recentNodes.RemoveAt(0);
    }

    private bool IsFallbackOnCooldown(MapNode n)
    {
        if (n == null) return false;
        if (fallbackCooldownSteps <= 0) return false;

        if (fallbackCooldownUntil.TryGetValue(n, out int until))
        {
            if (stepIndex < until) return true;

            // 期限切れは掃除
            fallbackCooldownUntil.Remove(n);
        }
        return false;
    }

    private void MarkFallbackCooldown(MapNode n)
    {
        if (n == null) return;
        if (fallbackCooldownSteps <= 0) return;

        fallbackCooldownUntil[n] = stepIndex + fallbackCooldownSteps;
    }

    // =============================
    // ★★★ メイン探索ルーチン ★★★
    // =============================
    private void TryExploreMove()
    {
        // ★ Node到達直後のフレームでは実行しない（この1行が超重要）
        if (blockTryExploreThisFrame)
        {
            return;
        }

        //------------------------------------------------------
        // ① Node生成・更新（Nodeスナップ後に呼ばれる前提）
        //------------------------------------------------------
        currentNode = TryPlaceNode(transform.position);
        currentNode.RecalculateUnknownAndWall();
        RegisterCurrentNode(currentNode);

        // ------------------------------------------------------
        // ①-2 MustPass（重み運用）：
        //   ・MustPassを「範囲内なら強制追跡」しない
        //   ・踏んだら踏破済みにする（同じMustPassへ吸い付くのを防ぐ）
        // ------------------------------------------------------
        if (currentNode != null && IsMustPassNode(currentNode))
        {
            MarkVisitedMustPass(currentNode);
        }

        Vector3? localUnknownDir = GetLocalUnknownDirection(currentNode);
        if (localUnknownDir.HasValue)
        {
            lastBestTarget = null;
            lastTargetIsFarthest = false;

            moveDir = localUnknownDir.Value.normalized;
            MoveForward();
            return;
        }



        // ★ Excluded(=壁扱いNode) などで止められた場合：
        //   「同じBestTargetを別ルートで目指す」→ それも無理なら一旦別行動へ
        if (needRepathToBestTarget && lastBestTarget != null && currentNode != null && currentNode != lastBestTarget)
        {
            var target = lastBestTarget;
            needRepathToBestTarget = false;

            // Excludedノードで止められたなら、その一手をタブーにして別ルートを探す
            if (blockedExcludedFrom == currentNode && blockedExcludedNode != null)
            {
                EnsureTabooTarget(target);
                AddTabooEdge(currentNode, blockedExcludedNode);
            }
            blockedExcludedFrom = null;
            blockedExcludedNode = null;

            MapNode repathNext;
            if (TryGetNextNodeToward(currentNode, target, out repathNext) && repathNext != null)
            {
                // backtrack が続くなら一旦ターゲットを手放して往復を切る
                if (!UpdateOscillationIfBacktrack(target, repathNext))
                {
                    moveDir = DirToNode(currentNode, repathNext);
                    MoveForward();
                    return;
                }
                // UpdateOscillationIfBacktrack() 内で TemporarilyAvoidTarget されている場合は通常フローへ落とす
            }
            else
            {
                TemporarilyAvoidTarget(target, "repath failed");
            }
        }// 近場探索（リンクBFS）
        var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
        var unknownNodes = nearNodes.Where(n => n.unknownCount > 0 && !IsAvoidedTarget(n)).ToList();

        //------------------------------------------------------
        // ② FOLLOW（OnArrival の時だけ機能させる）
        //------------------------------------------------------
        bool followMode =
            (targetUpdateMode == TargetUpdateMode.OnArrival) &&
            (lastBestTarget != null && currentNode != lastBestTarget);

        // ★ フォールバック追従中に、近場に Unknown が出てきたら乗り換え
        if (followMode && lastTargetIsFarthest && unknownNodes.Count > 0)
        {
            followMode = false;
            lastBestTarget = null;
            lastTargetIsFarthest = false;
        }


        if (followMode)
        {
            MapNode nextNode;
            if (TryGetNextNodeToward(currentNode, lastBestTarget, out nextNode) && nextNode != null)
            {
                // backtrack が続くなら一旦ターゲットを手放して往復を切る
                if (!UpdateOscillationIfBacktrack(lastBestTarget, nextNode))
                {
                    moveDir = DirToNode(currentNode, nextNode);
                    MoveForward();
                    return;
                }
                // UpdateOscillationIfBacktrack() 内で TemporarilyAvoidTarget されている場合は通常フローへ落とす
            }
            else
            {
                // 追従できない＝今は届かない → 一旦別行動へ
                var t = lastBestTarget;
                TemporarilyAvoidTarget(t, "follow path not found");

                moveDir = ChooseRandomValidDirection(currentNode).Value;
                MoveForward();
                return;
            }
        }
        //------------------------------------------------------
        // ③ bestTargetへ到達した時（OnArrival）
        //------------------------------------------------------
        if (currentNode == lastBestTarget)
        {
            lastBestTarget = null;
            lastTargetIsFarthest = false;

            // 追従が終わったのでタブー／往復検知をリセット
            tabooTarget = null;
            tabooEdges.Clear();
            oscillA = null;
            oscillB = null;
            oscillTarget = null;
            oscillCount = 0;
        }

        //------------------------------------------------------
        // ④ Unknown（近場） or Global Frontier（全体フロンティア）を決める
        //------------------------------------------------------
        // ④-1: 近場に Unknown があればいつも通り Unknown を優先
        MapNode unknownTarget = null;
        if (unknownNodes.Count > 0)
            unknownTarget = SelectUnknownNode(unknownNodes, currentNode);

        // ④-2: 近場に Unknown が無い → 「到達可能なノード」の中から
        //       unknownCount > 0（＝掘れる場所が残るノード）だけを集めてターゲットにする
        MapNode globalFrontierTarget = null;

        if (unknownTarget == null)
        {
            // currentNode からリンクで到達可能なノードだけ
            var reachable = BFS_ReachableNodes(currentNode);

            // ★ Unknown 判定の取りこぼしを減らしたいので、ここで再計算（重いなら後で最適化可）
            foreach (var n in reachable)
                if (n != null) n.RecalculateUnknownAndWall();

            var globalFrontiers = reachable
                .Where(n => n != null &&
                            n != currentNode &&
                            n.distanceFromStart < int.MaxValue &&
                            n.unknownCount > 0 && !IsAvoidedTarget(n))          // ★ここが最重要：unknown>0 のみ
                .ToList();

            if (globalFrontiers.Count == 0)
            {
                // ★ 完全探索済み（unknown がどこにも無い）→ ここで終了動作
                // 循環させたくないなら「動かない」が一番確実
                lastBestTarget = null;
                lastTargetIsFarthest = false;
                isMoving = false;
                return;
            }

            switch (noUnknownFallbackMode)
            {
                case NoUnknownFallbackMode.FarthestFromStart:
                    // 「フロンティアの中で」Startから最遠
                    globalFrontierTarget = globalFrontiers
                        .OrderByDescending(n => n.distanceFromStart)
                        .FirstOrDefault();
                    break;

                case NoUnknownFallbackMode.NewestNode:
                    // 「フロンティアの中で」一番最近できたノード
                    // allNodes は生成順なので、末尾から探す
                    var frontierSet = new HashSet<MapNode>(globalFrontiers);
                    for (int i = MapNode.allNodes.Count - 1; i >= 0; i--)
                    {
                        var n = MapNode.allNodes[i];
                        if (n == null) continue;
                        if (n == currentNode) continue;
                        if (!frontierSet.Contains(n)) continue;

                        globalFrontierTarget = n;
                        break;
                    }

                    // 保険：見つからないとき
                    if (globalFrontierTarget == null)
                    {
                        globalFrontierTarget = globalFrontiers
                            .OrderByDescending(n => n.distanceFromStart)
                            .FirstOrDefault();
                    }
                    break;
            }
        }

        //------------------------------------------------------
        // ⑤ ターゲット決定（Unknown優先 → 無ければ GlobalFrontier）
        //------------------------------------------------------
        MapNode bestTarget = unknownTarget ?? globalFrontierTarget;

        if (bestTarget == null)
        {
            lastBestTarget = null;
            lastTargetIsFarthest = false;

            moveDir = ChooseRandomValidDirection(currentNode).Value;
            MoveForward();
            return;
        }

        //------------------------------------------------------
        // ⑥ 経路を構築して次ノードへ進む
        //------------------------------------------------------

        MapNode nextNode2;
        if (!TryGetNextNodeToward(currentNode, bestTarget, out nextNode2) || nextNode2 == null)
        {
            // BestTargetに届かない（Excludedの壁で分断されている等）
            TemporarilyAvoidTarget(bestTarget, "bestTarget path not found");

            moveDir = ChooseRandomValidDirection(currentNode).Value;
            MoveForward();
            return;
        }

        // backtrack が続くなら一旦ターゲットを手放して往復を切る
        if (UpdateOscillationIfBacktrack(bestTarget, nextNode2))
        {
            moveDir = ChooseRandomValidDirection(currentNode).Value;
            MoveForward();
            return;
        }

        moveDir = DirToNode(currentNode, nextNode2);
        //------------------------------------------------------
        // ⑦ bestTarget のセット
        //------------------------------------------------------
        lastBestTarget = bestTarget;

        // ★ unknownTarget が null のときは「近場Unknownが無かったのでグローバルフロンティアへ」
        //    FOLLOW中に近場Unknownが出たら乗り換え対象にしたいので true 扱いにする
        lastTargetIsFarthest = (unknownTarget == null);

        //------------------------------------------------------
        // ⑧ 移動
        //------------------------------------------------------
        MoveForward();
    }

    // ===== Helpers =====
    private Vector3 DirToNode(MapNode from, MapNode to)
    {
        if (from == null || to == null) return Vector3.zero;
        Vector3 d = to.transform.position - from.transform.position;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
    }

    private void RememberRecentNode(MapNode node)
    {
        if (node == null) return;
        recentNodeMemory.Enqueue(node);
        while (recentNodeMemory.Count > oscillationMemorySize)
            recentNodeMemory.Dequeue();
    }

    // 直近4ステップが A->B->A->B なら「2点間往復」とみなす（最小の検知）
    private bool IsOscillatingAB()
    {
        if (recentNodeMemory.Count < 4) return false;
        var a = recentNodeMemory.ToArray();
        int n = a.Length;
        return a[n - 1] == a[n - 3] && a[n - 2] == a[n - 4] && a[n - 1] != a[n - 2];
    }

    private void TemporarilyAvoidTarget(MapNode t, string reason = "", int frames = -1)
    {
        if (t == null) return;
        if (frames <= 0) frames = avoidTargetFrames;
        avoidedTargetsUntil[t] = Time.frameCount + frames;

        if (debugLog)
            Debug.Log($"[AVOID_TARGET] {t.name} frames={frames} reason={reason}");
    }


    private bool IsAvoidedTarget(MapNode t)
    {
        if (t == null) return false;
        if (!avoidedTargetsUntil.TryGetValue(t, out int until)) return false;
        if (Time.frameCount > until)
        {
            avoidedTargetsUntil.Remove(t);
            return false;
        }
        return true;
    }

    // directed edge key: from->to
    private ulong PackEdgeKey(MapNode from, MapNode to)
    {
        unchecked
        {
            uint a = (uint)from.GetInstanceID();
            uint b = (uint)to.GetInstanceID();
            return ((ulong)a << 32) | b;
        }
    }

    private void AddTabooEdge(MapNode from, MapNode to, int frames = -1)
    {
        if (from == null || to == null) return;
        if (frames <= 0) frames = tabooEdgeFrames;
        tabooEdgesUntil[PackEdgeKey(from, to)] = Time.frameCount + frames;
    }

    private bool IsTabooEdge(MapNode from, MapNode to)
    {
        if (from == null || to == null) return false;
        ulong key = PackEdgeKey(from, to);

        if (!tabooEdgesUntil.TryGetValue(key, out int until)) return false;
        if (Time.frameCount > until)
        {
            tabooEdgesUntil.Remove(key);
            return false;
        }
        return true;
    }


    // ★ Unknown方向を選ぶ（Excluded Node 方向は「壁扱い」で除外）
    //private Vector3? GetLocalUnknownDirection(MapNode node)
    //{
    //    if (node == null) return null;

    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

    //    foreach (var dir in dirs)
    //    {
    //        // 既にリンクがあるなら Unknown ではない
    //        if (IsLinkedDirection(node, dir))
    //            continue;

    //        // 壁 or Excluded Node なら掘れない（Unknownにしない）
    //        if (IsWall(node, dir))
    //            continue;

    //        return dir;
    //    }

    //    return null;
    //}
    private Vector3? GetLocalUnknownDirection(MapNode node)
    {
        // 4方向（順番はもう意味を持たない）
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        // 条件を満たす候補を集める
        List<Vector3> candidates = new List<Vector3>(4);
        foreach (var dir in dirs)
        {
            if (IsLinkedDirection(node, dir)) continue;
            if (IsWall(node, dir)) continue;
            candidates.Add(dir);
        }

        if (candidates.Count == 0) return null;

        // 候補からランダムに1つ
        int idx = dirRng.Next(candidates.Count);
        return candidates[idx];
    }


    private MapNode SelectUnknownNode(List<MapNode> unknownNodes, MapNode current)
    {
        switch (unknownSelectMode)
        {
            case UnknownSelectMode.Random:
                return unknownNodes[Random.Range(0, unknownNodes.Count)];

            case UnknownSelectMode.Nearest:
                return unknownNodes
                    .OrderBy(n => Distance(current, n))
                    .First();

            case UnknownSelectMode.Farthest:
                return unknownNodes
                    .OrderByDescending(n => Distance(current, n))
                    .First();

            case UnknownSelectMode.MostUnknown:
                return unknownNodes
                    .OrderByDescending(n => n.unknownCount)
                    .First();
        }

        return unknownNodes[0];
    }



    // =============================
    // ★ 有効なランダム方向を返す（A/B/C 共通処理）
    // =============================
    private Vector3? ChooseRandomValidDirection(MapNode node)
    {
        List<Vector3> dirs = new()
        {
            Vector3.forward,
            Vector3.back,
            Vector3.left,
            Vector3.right
        };

        // ★背後方向を除外（無限ループ防止）
        Vector3 backDir = -moveDir;
        dirs = dirs.Where(d => Vector3.Dot(d.normalized, backDir.normalized) < 0.7f).ToList();

        // ★リンク方向は除外
        dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

        // ★壁方向も除外
        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

        // ★追加：Excluded 方向（次セルが Excluded）も除外
        //dirs = dirs.Where(d => !IsExcludedNeighbor(node, d)).ToList();
        dirs = dirs.Where(d =>
        {
            Vector2Int c = node.cell + DirToCellDelta(d);
            MapNode n = MapNode.FindByCell(c);
            return n == null || !n.isExcluded;
        }).ToList();

        if (dirs.Count == 0)
            return null;

        return dirs[Random.Range(0, dirs.Count)];
    }


    // ==========================================================
    // ★ リンクベースで到達可能な Node を BFS で列挙
    // ==========================================================
    private List<MapNode> BFS_ReachableNodes(MapNode start)
    {
        Queue<MapNode> q = new Queue<MapNode>();
        HashSet<MapNode> visited = new HashSet<MapNode>();

        q.Enqueue(start);
        visited.Add(start);

        while (q.Count > 0)
        {
            var n = q.Dequeue();

            foreach (var next in n.links)
            {
                if (next == null) continue;

                // ★ 追加
                if (next.isExcluded) continue;

                if (!visited.Contains(next))
                {
                    visited.Add(next);
                    q.Enqueue(next);
                }
            }
        }

        return visited.ToList();
    }


    // =============================
    // ★ 終端ノード D（ランダム）方式
    // =============================
    private Vector3? ChooseTerminalDirection(MapNode node)
    {
        List<Vector3> dirs = AllMovesExceptBack();

        dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();
        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

        // ★追加：Excluded 方向（次セルが Excluded）も除外
        //dirs = dirs.Where(d => !IsExcludedNeighbor(node, d)).ToList();
        dirs = dirs.Where(d =>
        {
            Vector2Int c = node.cell + DirToCellDelta(d);
            MapNode n = MapNode.FindByCell(c);
            return n == null || !n.isExcluded;
        }).ToList();


        if (dirs.Count == 0) return null;
        if (dirs.Count == 1) return dirs[0];

        return dirs[Random.Range(0, dirs.Count)];
    }


    private bool IsTerminalNode(MapNode node)
        => node != null && node.links.Count == 1;

    // =============================
    // ★ BFS(depth=N) 近場探索
    // =============================
    private List<MapNode> BFS_NearNodes(MapNode start, int depth)
    {
        Queue<(MapNode node, int dist)> q = new();
        HashSet<MapNode> visited = new();

        q.Enqueue((start, 0));
        visited.Add(start);

        //List<MapNode> results = new() { start };
        List<MapNode> results = new();

        while (q.Count > 0)
        {
            var (node, d) = q.Dequeue();
            //if (d >= depth) continue;
            if (d > depth) continue;

            foreach (var link in node.links)
            {
                if (link == null) continue;

                // ★ 追加
                if (link.isExcluded) continue;

                if (visited.Contains(link)) continue;

                visited.Add(link);
                //results.Add(link);
                // ★ currentNode(start)以外だけ追加
                if (link != start)
                    results.Add(link);
                q.Enqueue((link, d + 1));
            }
        }
        return results;
    }


    // =============================
    // ★ 最遠 & 近場未知 のスコア判定
    // =============================
    private MapNode ChooseBestTarget(MapNode targetUnknown, MapNode targetFarthest)
    {
        // Unknown だけ
        if (targetUnknown != null && targetFarthest == null)
        {
            //Debug.Log($"[BEST] UnknownOnly → return {targetUnknown.name}");
            return targetUnknown;
        }

        // Farthest だけ
        if (targetUnknown == null && targetFarthest != null)
        {
            //Debug.Log($"[BEST] FarthestOnly → return {targetFarthest.name}");
            return targetFarthest;
        }

        // 両方ある場合：距離差で勝負
        int distUnknown = Mathf.Abs(targetUnknown.distanceFromStart - currentNode.distanceFromStart);
        int distFarthest = Mathf.Abs(targetFarthest.distanceFromStart - currentNode.distanceFromStart);

        MapNode best =
            (distUnknown >= distFarthest) ? targetUnknown : targetFarthest;

        //Debug.Log($"[CHECK-BEST] bestTarget={best?.name}, " +
        //  $"current={currentNode?.name}, " +
        //  $"targetUnknown={targetUnknown?.name}, " +
        //  $"targetFarthest={targetFarthest?.name}");

        if (distUnknown >= distFarthest)
            return targetUnknown;
        else
            return targetFarthest;
    }

    private float Score(MapNode n)
    {
        // Unknown 優先、Unknown=0 なら距離で評価
        return weightUnknown * n.unknownCount
             + weightDistance * (-n.distanceFromStart);
    }

    List<MapNode> BuildShortestPath(MapNode start, MapNode goal)
    {
        if (start == null || goal == null) return null;
        if (start == goal) return new List<MapNode>() { start };

        // Dijkstra: dist と prev
        Dictionary<MapNode, float> dist = new Dictionary<MapNode, float>();
        Dictionary<MapNode, MapNode> prev = new Dictionary<MapNode, MapNode>();

        // open は簡易実装（ノード数が多いなら優先度付きキューにしてもいい）
        List<MapNode> open = new List<MapNode>();
        HashSet<MapNode> closed = new HashSet<MapNode>();

        dist[start] = 0f;
        prev[start] = null;
        open.Add(start);

        int expanded = 0;
        int skipEx = 0;

        while (open.Count > 0)
        {
            // open から dist 最小のノードを取り出す
            MapNode node = null;
            float best = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                var n = open[i];
                if (n == null) continue;
                if (dist.TryGetValue(n, out float d) && d < best)
                {
                    best = d;
                    node = n;
                }
            }

            if (node == null) break;

            open.Remove(node);
            if (closed.Contains(node)) continue;
            closed.Add(node);

            expanded++;

            if (node == goal)
                return Rebuild(prev, start, goal);

            // --- 隣接展開（正リンク）---
            if (node.links != null)
            {
                for (int i = 0; i < node.links.Count; i++)
                {
                    MapNode nb = node.links[i];
                    if (nb == null) continue;

                    if (nb.isExcluded && nb != goal)
                    {
                        skipEx++;
                        continue;
                    }

                    float nd = best + GetEnterCost(nb);

                    if (!dist.TryGetValue(nb, out float old) || nd < old)
                    {
                        dist[nb] = nd;
                        prev[nb] = node;

                        if (!closed.Contains(nb) && !open.Contains(nb))
                            open.Add(nb);
                    }
                }
            }

            // --- 逆リンク救済展開（あなたの仕様を維持） ---
            foreach (var other in MapNode.allNodes)
            {
                if (other == null) continue;
                if (other.links == null) continue;

                if (other.links.Contains(node))
                {
                    if (other.isExcluded && other != goal)
                    {
                        skipEx++;
                        continue;
                    }

                    float nd = best + GetEnterCost(other);

                    if (!dist.TryGetValue(other, out float old) || nd < old)
                    {
                        dist[other] = nd;
                        prev[other] = node;

                        if (!closed.Contains(other) && !open.Contains(other))
                            open.Add(other);
                    }
                }
            }
        }

        return null;
    }


    // ------------------------------
    // 通行不可(Excluded) / ループ対策ユーティリティ
    // ------------------------------
    private long PackEdgeKey(int fromId, int toId)
    {
        unchecked
        {
            return ((long)fromId << 32) ^ (uint)toId;
        }
    }

    private void EnsureTabooTarget(MapNode target)
    {
        if (target == null) return;

        if (tabooTarget != target)
        {
            tabooTarget = target;
            tabooEdges.Clear();
        }
    }

    private void AddTabooEdge(MapNode from, MapNode to)
    {
        if (from == null || to == null) return;

        // tabooTarget は TryGetNextNodeToward() 側でセットされる前提
        long key = PackEdgeKey(from.GetInstanceID(), to.GetInstanceID());
        tabooEdges.Add(key);
    }

    private bool TryGetNextNodeToward(MapNode from, MapNode target, out MapNode nextNode)
    {
        nextNode = null;
        if (from == null || target == null) return false;

        // Dijkstra / BuildShortestPath は一切使わない
        // 常に「隣接ノード評価のみ」で1手を決める

        List<MapNode> candidates = new List<MapNode>();

        if (from.links != null && from.links.Count > 0)
        {
            candidates.AddRange(from.links);
        }
        else if (localUseReverseLinkRescue)
        {
            // 逆リンク救済：from.links が空のときだけ走らせてコストを抑える
            foreach (var other in MapNode.allNodes)
            {
                if (other == null) continue;
                if (other.links == null) continue;
                if (other.links.Contains(from))
                    candidates.Add(other);
            }
        }

        if (candidates.Count == 0) return false;

        // MustPass が10個以上になる想定なのでキャッシュを使う
        MapNode.RefreshMustPassCacheIfNeeded();

        MapNode best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            MapNode c = candidates[i];
            if (c == null) continue;
            if (c == from) continue;

            // Excluded（壁扱い）は通れない（ただし target は例外で通す）
            if (c.isExcluded && c != target) continue;

            // Avoid / Taboo / TempForbid は候補から除外
            if (IsAvoidedTarget(c)) continue;
            if (IsTabooEdge(from, c)) continue;
            if (hasTempForbidEdge && from == tempForbidFrom && c == tempForbidTo) continue;

            float score = ComputeLocalStepScore(from, c, target);
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        if (best == null) return false;

        nextNode = best;
        return true;
    }






    private int GetVisitCount(MapNode node)
    {
        if (node == null) return 0;
        if (nodeVisitCounts.TryGetValue(node, out int c)) return c;
        return 0;
    }

    private void IncrementVisitCount(MapNode node)
    {
        if (node == null) return;
        if (nodeVisitCounts.TryGetValue(node, out int c))
            nodeVisitCounts[node] = c + 1;
        else
            nodeVisitCounts[node] = 1;
    }

    private int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private float ComputeLocalStepScore(MapNode from, MapNode candidate, MapNode target)
    {
        // Score(Value) の合成は MapNode 側で実施（CellFromStart から計算を排除）
        int visits = GetVisitCount(candidate);
        return MapNode.CalculateLocalStepScore(
            candidate,
            target,
            visits,
            visitedMustPassCells,
            prevVisitedNode,
            localWriteWeightToNode);
    }

    private bool UpdateOscillationIfBacktrack(MapNode target, MapNode nextNode)
    {
        // nextNode が直前ノードに戻る動きなら、往復カウントを更新
        if (target == null || nextNode == null) return false;
        if (prevVisitedNode == null) return false;
        if (nextNode != prevVisitedNode)
        {
            // backtrack でないならリセット
            oscillA = null;
            oscillB = null;
            oscillTarget = null;
            oscillCount = 0;
            return false;
        }

        if (oscillTarget != target || oscillA != lastVisitedNode || oscillB != prevVisitedNode)
        {
            oscillTarget = target;
            oscillA = lastVisitedNode;
            oscillB = prevVisitedNode;
            oscillCount = 1;
        }
        else
        {
            oscillCount++;
        }

        if (debugLog)
            Debug.Log($"[OSC] target={target.name} pair=({oscillA?.name},{oscillB?.name}) count={oscillCount}");

        if (oscillCount >= oscillLimit)
        {
            TemporarilyAvoidTarget(target, $"oscillation>={oscillLimit}");
            return true;
        }

        return false;
    }

    private List<MapNode> Rebuild(Dictionary<MapNode, MapNode> prev,
                                  MapNode start, MapNode goal)
    {
        List<MapNode> path = new List<MapNode>();
        MapNode cur = goal;

        while (cur != null)
        {
            path.Add(cur);
            cur = prev[cur];
        }

        path.Reverse();
        return path;
    }



    // =============================
    // その他ユーティリティ
    // =============================
    private float Distance(MapNode a, MapNode b)
        => Vector3.Distance(a.transform.position, b.transform.position);

    private List<Vector3> AllMovesExceptBack()
    {
        List<Vector3> dirs = new()
        {
            Vector3.forward,
            Vector3.back,
            Vector3.left,
            Vector3.right
        };

        Vector3 back = -moveDir;
        return dirs.Where(d => Vector3.Dot(d.normalized, back.normalized) < 0.7f).ToList();
    }

    private bool IsLinkedDirection(MapNode node, Vector3 dir)
    {
        foreach (var link in node.links)
        {
            Vector3 diff = (link.transform.position - node.transform.position).normalized;
            if (Vector3.Dot(diff, dir.normalized) > 0.7f)
                return true;
        }
        return false;
    }

    private bool IsWall(MapNode node, Vector3 dir)
    {
        // ① 物理的な壁
        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        if (Physics.Raycast(origin, dir, cellSize, wallLayer))
            return true;

        // ② Excluded Node は「壁扱い」：次セルに Excluded Node があれば進めない
        Vector3 nextWorld = SnapToGrid(node.transform.position + dir.normalized * cellSize);
        Vector2Int nextCell = WorldToCell(nextWorld);
        MapNode nextNode = MapNode.FindByCell(nextCell);

        if (nextNode != null && nextNode.isExcluded)
            return true;

        return false;
    }

    private bool IsMustPassNode(MapNode node)
    {
        if (node == null) return false;

        // ① まず “現時点でMustPassか” を既存ロジックで判定（時間は見ない）
        bool raw = IsMustPassNodeRaw(node);

        // rawが外れているなら、追跡も解除して終了
        if (!raw)
        {
            mustPassExpireAt.Remove(node.cell);
            return false;
        }

        // ② raw=true の場合：時間失効チェック
        if (mustPassLifetimeSec > 0f)
        {
            float now = Time.time;

            // 初回検知なら、寿命スタート
            if (!mustPassExpireAt.TryGetValue(node.cell, out float expAt))
            {
                expAt = now + mustPassLifetimeSec;
                mustPassExpireAt[node.cell] = expAt;
            }

            // 期限切れなら MustPass 無効化
            if (now >= expAt)
            {
                ClearMustPassFlag(node);      // 可能ならフラグも落とす
                mustPassExpireAt.Remove(node.cell);
                return false;
            }
        }

        return true;
    }

    private float GetEnterCost(MapNode node)
    {
        if (node == null) return 999999f;

        // MustPassは「未踏なら」軽くする（踏破済みなら通常コストに戻す）
        if (useMustPassAsWeight && IsMustPassNode(node) && !IsVisitedMustPass(node))
            return mustPassEnterCost;

        return normalEnterCost;
    }

    private bool IsMustPassNodeRaw(MapNode node)
    {
        if (node == null) return false;

        // (A) MapNode.isMustPass があればそれを使う
        if (_fiIsMustPass == null)
        {
            _fiIsMustPass = typeof(MapNode).GetField("isMustPass",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (_fiIsMustPass != null && _fiIsMustPass.FieldType == typeof(bool))
        {
            return (bool)_fiIsMustPass.GetValue(node);
        }

        // (B) NodePointMarker.IsMustPassCell(Vector2Int) があればそれを使う
        if (_miIsMustPassCell == null)
        {
            _miIsMustPassCell = typeof(NodePointMarker).GetMethod("IsMustPassCell",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        }

        if (_miIsMustPassCell != null)
        {
            object ret = _miIsMustPassCell.Invoke(null, new object[] { node.cell });
            if (ret is bool b) return b;
        }

        return false;
    }

    private void ClearMustPassFlag(MapNode node)
    {
        if (node == null) return;

        if (_fiIsMustPass == null)
        {
            _fiIsMustPass = typeof(MapNode).GetField("isMustPass",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        if (_fiIsMustPass != null && _fiIsMustPass.FieldType == typeof(bool))
        {
            _fiIsMustPass.SetValue(node, false);
        }
    }


    private MapNode FindMustPassTargetInRange(MapNode current, int depth)
    {
        if (current == null) return null;

        // UnknownReference と同じ「リンクBFSの深さ」で範囲判定
        var nearNodes = BFS_NearNodes(current, depth);

        // Excluded は BFS_NearNodes 側で除外済み
        var candidates = nearNodes
            //.Where(n => n != null && IsMustPassNode(n) && !IsAvoidedTarget(n))
            .Where(n => n != null
            && IsMustPassNode(n)
            && !IsVisitedMustPass(n)      // ★ 追加：このPlayerが未踏のみ
            && !IsAvoidedTarget(n))
            .ToList();

        if (candidates.Count == 0) return null;

        // 最短Hop（グラフ距離）で一番近い MustPass を選ぶ
        return SelectMustPassNode_ByHopDistance(candidates, current, depth);
    }

    private MapNode SelectMustPassNode_ByHopDistance(List<MapNode> candidates, MapNode current, int maxDepth)
    {
        MapNode best = null;
        int bestHop = int.MaxValue;
        int bestStartDist = int.MaxValue; // tie-breaker

        foreach (var c in candidates)
        {
            if (c == null) continue;

            int hop = GetHopDistanceWithinDepth(current, c, maxDepth);
            if (hop < bestHop)
            {
                best = c;
                bestHop = hop;
                bestStartDist = c.distanceFromStart;
            }
            else if (hop == bestHop)
            {
                // 同距離なら、Startから近い方を優先（挙動を安定させる）
                if (c.distanceFromStart < bestStartDist)
                {
                    best = c;
                    bestStartDist = c.distanceFromStart;
                }
            }
        }

        return best;
    }

    private int GetHopDistanceWithinDepth(MapNode start, MapNode target, int maxDepth)
    {
        if (start == null || target == null) return int.MaxValue;
        if (start == target) return 0;

        Queue<(MapNode node, int d)> q = new();
        HashSet<MapNode> visited = new();

        q.Enqueue((start, 0));
        visited.Add(start);

        while (q.Count > 0)
        {
            var (n, d) = q.Dequeue();
            if (d >= maxDepth) continue;

            if (n.links == null) continue;

            for (int i = 0; i < n.links.Count; i++)
            {
                var nb = n.links[i];
                if (nb == null) continue;
                if (nb.isExcluded) continue;
                if (visited.Contains(nb)) continue;

                int nd = d + 1;
                if (nb == target) return nd;

                visited.Add(nb);
                q.Enqueue((nb, nd));
            }
        }

        return int.MaxValue;
    }

    private bool IsVisitedMustPass(MapNode node)
    {
        if (node == null) return false;
        return visitedMustPassCells.Contains(node.cell);
    }

    private void MarkVisitedMustPass(MapNode node)
    {
        if (node == null) return;
        visitedMustPassCells.Add(node.cell);
    }

    // ★ 追加：被弾した瞬間に居たNodeをMustPassにする（外部から呼ぶ用）
    // ★追加：被弾位置を受け取って MustPass を立てる（ログもここで出す）
    public void SetMustPassAtHitWorldPos(Vector3 hitWorldPos)
    {
        Vector3 snapped = SnapToGrid(hitWorldPos);
        Vector2Int cell = WorldToCell(snapped);

        MapNode byCell = MapNode.FindByCell(cell);

        // ★重要：被弾セルにNodeが無いことがある（通路セル等）
        // → currentNode（到達済みの分岐Node）を優先して fallback
        MapNode final = byCell;
        if (final == null)
            final = (currentNode != null) ? currentNode : MapNode.FindNearest(hitWorldPos);

        Debug.Log(
            $"[MustPass][Damage][IN] hitWorldPos={hitWorldPos} snapped={snapped} cell={cell} " +
            $"FindByCell={(byCell != null ? byCell.name : "null")} final={(final != null ? final.name : "null")}");

        if (final == null) return;

        // 念のため：Excluded なら MustPass にしない（好みで外してOK）
        if (final.isExcluded) return;

        if (!final.isMustPass)
        {
            final.isMustPass = true;
            //if (!final.name.Contains("_MP")) final.name += "_MP";
        }

        // このPlayerは「そのMustPassは経由済み」にしておく（吸い付き防止）
        MarkVisitedMustPass(final);

        Debug.Log($"[MustPass][Damage][OUT] MustPassSet={final.name} cell={final.cell}");
    }

    // 互換用：既存呼び出しがあるなら残す（EnemyAttack 側を直した後でもOK）
    public void SetMustPassAtCurrentNode_Damaged()
    {
        SetMustPassAtHitWorldPos(transform.position);
    }

    public void LockMovementUntilNoDamage(float noDamageSeconds)
    {
        if (!stopMoveWhileRecentlyDamaged) return;

        noDamageSeconds = Mathf.Max(0f, noDamageSeconds);

        // 今この瞬間から「noDamageSeconds」の間は停止
        float until = Time.time + noDamageSeconds;

        // すでに停止中なら、解除時刻を後ろに伸ばす（被弾が続くほど停止が延長される）
        if (until > damageStopUntilTime)
            damageStopUntilTime = until;
    }

    /// <summary>
    /// Called from PlayerHealth when this player takes damage.
    /// Stops this player's movement permanently (until death) to prevent MustPass from exploding.
    /// </summary>
    public void LockMovementForever_OnDamaged()
    {
        if (!stopForeverWhenDamaged) return;

        if (movementLockedForever) return;
        movementLockedForever = true;

        // Immediately stop any in-progress move
        isMoving = false;
        blockTryExploreThisFrame = false;
        arrivedThisNode = false;

        Debug.Log($"[MOVE][LOCK] PlayerId={playerId} movement locked forever (damaged).");
    }

    private MapNode TryPlaceNode(Vector3 pos)
    {
        Vector3 snapped = SnapToGrid(pos);
        Vector2Int cell = WorldToCell(snapped);

        //Debug.Log($"[TP0] TryPlaceNode START | pos={pos} | snapped={snapped} | cell={cell}");

        MapNode node;

        if (MapNode.allNodeCells.Contains(cell))
        {
            node = MapNode.FindByCell(cell);
            //Debug.Log($"[TP1] Existing Node FOUND | node={node.name} | links={node.links.Count}");
        }
        else
        {
            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);

            // ★ 新規Nodeを作ったのでカウントを増やす
            newNodeCreatedCount++;
            //Debug.Log($"[TP2] NEW Node CREATED | node={node.name} | cell={cell} | createdCount={newNodeCreatedCount}");

            // ★ ここで「生成直後に一度だけ」ポイント判定
            ApplyPointInfluence_Once(node);

            // ★ 規定数に達したらこのPlayerをDestroy
            if (newNodeCreatedCount >= destroyAfterNewNodes)
            {
                //Debug.Log($"[PLAYER-END] 新規Nodeを {newNodeCreatedCount} 個設置したので Player_{playerId} を Destroy");
                Destroy(gameObject);   // このフレームの終わりに破棄される
            }

            //Debug.Log($"[TP2] NEW Node CREATED | node={node.name} | cell={cell}");
        }

        //Debug.Log($"[TP3] Before LinkBackward | node={node.name}");
        LinkBackward(node);
        //Debug.Log($"[TP4] After LinkBackward | node={node.name} | links={node.links.Count}");
        return node;
    }

    private void ApplyPointInfluence_Once(MapNode node)
    {
        if (node == null) return;

        // 事故防止：Goalは除外しない
        if (node.CompareTag("Goal")) return;

        // node.cell は既に設定されている前提
        if (!NodePointMarker.IsExcludedCell(node.cell))
            return;

        // 既に除外済みなら二重処理しない（名前も増殖させない）
        bool becameExcludedNow = false;

        if (!node.isExcluded)
        {
            node.isExcluded = true;   // 永続
            becameExcludedNow = true;

            // 目視確認用（不要なら消してOK）
            if (!node.name.EndsWith("_EX"))
                node.name += "_EX";

            if (debugPointLog)
                Debug.Log($"[POINT] Excluded set (no-collider): {node.name} cell={node.cell}");
        }
        else
        {
            if (debugPointLog)
                Debug.Log($"[POINT] Excluded already: {node.name} cell={node.cell}");
        }

        // ★重要：
        // isExcluded は「後から」立つので、既に計算済みの unknown/wall が古いままになる。
        // 自分＆リンク先（＝隣接ノード）を再計算して、Excluded方向を壁扱いに反映する。
        // ※新しく置かれる側が「隣がExcludedか分からない」問題は、
        //   後から置かれるノードが RecalculateUnknownAndWall() するときに
        //   Raycastで Excludedノードを検知して壁扱いにできる前提。
        //   ここで必要なのは「既存リンクがあるノードの再計算」。
        if (becameExcludedNow)
        {
            // 自分
            node.RecalculateUnknownAndWall();

            // 直接リンクしている隣接ノード（両方向のunknown/wallが古い可能性がある）
            if (node.links != null)
            {
                for (int i = 0; i < node.links.Count; i++)
                {
                    var nb = node.links[i];
                    if (nb == null) continue;
                    nb.RecalculateUnknownAndWall();
                }
            }

            if (debugPointLog)
                Debug.Log($"[POINT] Recalc unknown/wall due to exclude: self={node.name} links={(node.links != null ? node.links.Count : 0)}");
        }
    }


    private void LinkBackward(MapNode node)
    {
        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        Vector3 dir = -moveDir;

        //Debug.Log($"[LB0] LinkBackward START | from={node.name} | dir={dir}");

        LayerMask mask = wallLayer | nodeLayer;

        for (int i = 1; i <= linkRayMaxSteps; i++)
        {
            float dist = cellSize * i;

            if (debugRay)
                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);


            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
            {
                int layer = hit.collider.gameObject.layer;

                //Debug.Log($"[LB1] Ray Hit | dist={dist} | hit={hit.collider.name}");

                if ((wallLayer.value & (1 << layer)) != 0)
                {
                    //Debug.Log($"[LB2] Hit WALL → stop");
                    return;
                }

                if ((nodeLayer.value & (1 << layer)) != 0)
                {
                    var hitNode = hit.collider.GetComponent<MapNode>();
                    if (hitNode != null && hitNode != node)
                    {
                        //Debug.Log($"[LB3] Linking {node.name} ↔ {hitNode.name}");

                        node.AddLink(hitNode);
                        node.RecalculateUnknownAndWall();
                        hitNode.RecalculateUnknownAndWall();
                    }
                    else
                    {
                        //Debug.Log($"[LB4] Hit self or null");
                    }
                    return;
                }
            }
        }

        //Debug.Log($"[LB5] No hit up to {linkRayMaxSteps} steps");
    }

    private Vector3 SnapToGrid(Vector3 pos)
    {
        int x = Mathf.RoundToInt((pos.x - gridOrigin.x) / cellSize);
        int z = Mathf.RoundToInt((pos.z - gridOrigin.z) / cellSize);
        return new Vector3(x * cellSize, 0, z * cellSize) + gridOrigin;
    }

    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        return new Vector2Int(
            Mathf.RoundToInt(p.x / cellSize),
            Mathf.RoundToInt(p.z / cellSize)
        );
    }

    private Vector3 CellToWorld(Vector2Int cell)
        => new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;

    private void Log(string msg)
    {
        //if (debugLog) Debug.Log("[CellFS] " + msg);
    }

    // ★ 追加：Vector3方向 → セル差分（x,z）
    private static Vector2Int DirToCellDelta(Vector3 dir)
    {
        if (Vector3.Dot(dir, Vector3.forward) > 0.9f) return new Vector2Int(0, 1);
        if (Vector3.Dot(dir, Vector3.back) > 0.9f) return new Vector2Int(0, -1);
        if (Vector3.Dot(dir, Vector3.right) > 0.9f) return new Vector2Int(1, 0);
        if (Vector3.Dot(dir, Vector3.left) > 0.9f) return new Vector2Int(-1, 0);
        return Vector2Int.zero;
    }

    // ★ 追加：このセルは Excluded セルか？（Collider不要）
    private bool IsExcludedCell(Vector2Int cell)
    {
        return NodePointMarker.IsExcludedCell(cell);
    }

    // ★ 追加：node から dir に1マス進む先が Excluded か？
    private bool IsExcludedNeighbor(MapNode node, Vector3 dir)
    {
        if (node == null) return false;
        Vector2Int nextCell = node.cell + DirToCellDelta(dir);
        return IsExcludedCell(nextCell);
    }
}

//using UnityEngine;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reflection;

//public class CellFromStart : MonoBehaviour
//{
//    [Header("移動設定")]
//    public float moveSpeed = 3f;
//    public float cellSize = 1f;
//    public float rayDistance = 1f;
//    public LayerMask wallLayer;
//    public LayerMask nodeLayer;

//    ////[Header("初期設定")]
//    //public enum StartDirectionMode
//    //{
//    //    Manual,
//    //    AutoFromSpawnRelativeToOrigin,
//    //    AutoFromSpawnRelativeToStartNode,
//    //    AutoByPlayerId
//    //}

//    [Tooltip("startDirectionMode が Manual のときのみ使用。Auto のときは起動時に上書きされます。")]
//    public Vector3 startDirection = Vector3.forward;

//    [Tooltip("Auto で決めた方向を反転（外向き⇔内向き）したい場合にON。")]
//    public bool invertAutoStartDirection = false;

//    //public StartDirectionMode startDirectionMode = StartDirectionMode.AutoFromSpawnRelativeToOrigin;

//    [Tooltip("AutoFromSpawnRelativeToOrigin の基準点。ここが入っている場合は gridOrigin より優先してこのTransform位置を基準にします（PlayerSpawner / spawnPoint を想定）。")]
//    public Transform startDirectionOrigin;

//    public Vector3 gridOrigin = Vector3.zero;
//    public GameObject nodePrefab;

//    [Header("Damage Stop (No-damage resume)")]
//    public bool stopMoveWhileRecentlyDamaged = true;

//    [Tooltip("Movement is paused until Time.time >= damageStopUntilTime")]
//    private float damageStopUntilTime = -1f;

//    [Header("探索パラメータ")]
//    public int unknownReferenceDepth = 3; // ★ BFS探索深さとして使用

//    [Header("スコア重み（A：現状の式）")]
//    public float weightUnknown = 1f;
//    public float weightDistance = 1f;

//    [Header("Local Next-Step (no full path)")]
//    [Tooltip("If true, next node is chosen only from neighbors using node weights (no full path search).")]
//    public bool useLocalNextStepOnly = true;

//    [Tooltip("Target proximity weight. Higher means stronger pull toward bestTarget (smaller distance).")]
//    public float weightToTarget = 1f;

//    [Tooltip("Large penalty to avoid immediate backtrack to the previous node.")]
//    public float backtrackPenalty = 1000f;

//    [Tooltip("If true, when from.links is empty, also treat reverse links (other.links contains from) as candidates.")]
//    public bool localUseReverseLinkRescue = true;

//    [Tooltip("If true, write computed weight to MapNode.value and distance-to-target to MapNode.DistanceFromGoal (debug/visualization).")]
//    public bool localWriteWeightToNode = true;

//    [Tooltip("MustPass score scale when using local next-step. (Uses normalEnterCost - GetEnterCost(node)) * scale")]
//    public float mustPassScoreScale = 10f;

//    [Tooltip("Penalty applied per prior visit to a candidate node (per player). Higher = avoids revisiting.")]
//    public float revisitPenaltyPerVisit = 5f;

//    [Tooltip("MustPass field multiplier. Set so (mustPassFieldBase * multiplier) > targetFieldMax to dominate.")]
//    public float mustPassFieldMultiplier = 6f; // 200*6=1200 > 1000

//    // -----------------------------
//    // Weight Field Params (Manhattan)
//    // -----------------------------
//    [Header("Weight Field (Manhattan)")]
//    [Tooltip("BestTarget weight at the target (the maximum).")]
//    public float targetFieldMax = 1000f;

//    [Tooltip("How much the BestTarget weight decreases per Manhattan step.")]
//    public float targetFieldSlope = 10f;

//    [Tooltip("MustPass peak bonus at MustPass node (keep smaller than targetFieldMax).")]
//    public float mustPassFieldBase = 200f;

//    [Tooltip("MustPass bonus decay per Manhattan step (0-1). Higher = longer reach.")]
//    [Range(0.5f, 0.99f)]
//    public float mustPassFieldDecay = 0.85f;
//    //public float mustPassFieldDecay = 0.95f;

//    [Tooltip("How often (frames) to rebuild MustPass cache. (For 10+ MustPass, 30 is a good start.)")]
//    public int mustPassCacheRefreshFrames = 30;

//    // MustPass cache
//    private readonly List<MapNode> _mustPassCache = new List<MapNode>(64);
//    private int _mustPassCacheLastFrame = -999999;
//    private int _mustPassCacheLastAllNodesCount = -1;

//    [Header("Ray設定")]
//    public int linkRayMaxSteps = 100;

//    [Header("Point Influence (Colliderなし)")]
//    public Vector3 pointGridOrigin = Vector3.zero;
//    public float pointCellSize = 1f;
//    public bool debugPointLog = true;

//    [SerializeField] private bool dbgExclude = true;
//    [SerializeField] private int dbgEveryNFrames = 30;

//    private void DLog(string msg)
//    {
//        if (!dbgExclude) return;
//        Debug.Log(msg);
//    }

//    [Header("デバッグ")]
//    public bool debugLog = true;
//    public bool debugRay = true;
//    public Renderer bodyRenderer;
//    public Material exploreMaterial;

//    // =============================
//    // ★ Player ID（CellFromStart 用）
//    // =============================
//    private static int nextPlayerId = 1;  // 全 CellFromStart 共通のカウンタ
//    public int playerId;                  // このインスタンス固有の ID

//    // 内部状態
//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;
//    private MapNode currentNode;

//    private List<MapNode> recentNodes = new List<MapNode>();

//    private MapNode lastBestTarget = null;
//    private bool arrivedThisNode = false;
//    private bool blockTryExploreThisFrame = false;

//    [Header("Damage Stop")]
//    [Tooltip("If true, once this player takes damage, it will stop moving permanently (until it is destroyed).")]
//    public bool stopForeverWhenDamaged = true;
//    private bool movementLockedForever = false;

//    // ★ 追加：今の lastBestTarget が「Start最遠由来」かどうか
//    private bool lastTargetIsFarthest = false;

//    [Header("循環回避：Unknown=0 フォールバックのクールダウン")]
//    public int fallbackCooldownSteps = 6;  // 例：6歩の間、同じノードをフォールバック候補にしない

//    // MapNode → 「このstepIndex未満なら選ばない」
//    private Dictionary<MapNode, int> fallbackCooldownUntil = new Dictionary<MapNode, int>();

//    // ★ Unknown=0 フェーズで既に選んだフォールバックターゲット
//    private HashSet<MapNode> usedFallbackTargets = new HashSet<MapNode>();

//    // ------------------------------
//    // Excluded(=通行不可) / 経路再構築・ループ対策用
//    // ------------------------------
//    private bool needRepathToBestTarget = false;
//    private MapNode blockedExcludedFrom = null;
//    private MapNode blockedExcludedNode = null;

//    // 「このターゲットを今は追わない（しばらく別行動する）」用
//    private MapNode tempAvoidTarget = null;
//    private int tempAvoidUntilSteps = -1;
//    [SerializeField] private int tempAvoidSteps = 20;

//    // 2点間往復検知（同じ2ノードで backtrack が続いたら一旦ターゲットを手放す）
//    private MapNode oscillA = null;
//    private MapNode oscillB = null;
//    private MapNode oscillTarget = null;
//    private int oscillCount = 0;
//    [SerializeField] private int oscillLimit = 4;

//    // BFS に「この1本のエッジは使わない」を混ぜる（即時の別ルート探索用）
//    private bool hasTempForbidEdge = false;
//    private MapNode tempForbidFrom = null;
//    private MapNode tempForbidTo = null;

//    // 直近2ノード（backtrack 判定用）
//    private MapNode lastVisitedNode = null;
//    private MapNode prevVisitedNode = null;


//    // --- Per-player visit cost (revisit penalty) ---
//    // Key: MapNode instance, Value: how many times THIS player has reached the node.
//    private Dictionary<MapNode, int> nodeVisitCounts = new Dictionary<MapNode, int>();

//    // 「今のターゲットに対して、行き止まりっぽい遷移は一旦避ける」用（ターゲットが変わったらクリア）
//    private struct EdgeKey
//    {
//        public int fromId;
//        public int toId;

//        public EdgeKey(int fromId, int toId)
//        {
//            this.fromId = fromId;
//            this.toId = toId;
//        }
//    }

//    private MapNode tabooTarget = null;
//    private HashSet<long> tabooEdges = new HashSet<long>(); // (fromId,toId) を 64bit に詰める

//    // ★ Playerごとの寿命設定：新規Nodeを何個置いたら消えるか
//    [Header("寿命設定")]
//    [Tooltip("このプレイヤーが新規に設置できるNode数の上限")]
//    public int destroyAfterNewNodes;

//    // ★ 今までにこのPlayerが新規に作ったNode数
//    private int newNodeCreatedCount = 0;

//    [Header("MustPass (時間失効)")]
//    [SerializeField] private float mustPassLifetimeSec = 8f;          // 例：8秒で失効（0以下なら失効なし）
//    [SerializeField] private float mustPassScanIntervalSec = 0.25f;   // 例：0.25秒ごとに全ノードを軽くスキャン
//    private float _nextMustPassScanTime = 0f;

//    [SerializeField] private bool useMustPassAsWeight = true;

//    // ノードに「入る」コスト（小さいほど通りたくなる）
//    [SerializeField] private float normalEnterCost = 1f;
//    [SerializeField] private float mustPassEnterCost = 0.25f;   // 0.1〜0.5くらいで調整

//    // cell -> 失効時刻(Time.time)
//    private readonly Dictionary<Vector2Int, float> mustPassExpireAt = new Dictionary<Vector2Int, float>();
//    private readonly List<Vector2Int> _tmpExpiredMustPassCells = new List<Vector2Int>();

//    // =============================
//    // ★ 評価ログ用（CellFromStart 単体）
//    // =============================
//    [Header("評価ログ（CellFromStart）")]
//    public int stepsWalked = 0;          // 歩いたマス数
//    public int uniqueNodesVisited = 0;   // 一度でも訪れた Node 種類数
//    public int deadEndEnterCount = 0;    // 行き止まり Node に入った回数
//    public bool goalReached = false;     // Goal に到達したか
//    public int frameToGoal = -1;         // 到達フレーム（未到達は -1）

//    // 内部管理用：訪問済み Node 集合
//    private HashSet<MapNode> visitedNodes = new HashSet<MapNode>();

//    private int stepIndex = 0;  // このPlayerが何回目のNode到達か

//    public enum UnknownSelectMode
//    {
//        Random,
//        Nearest,
//        Farthest,
//        MostUnknown
//    }

//    [Header("探索方針①：Unknownの選択方式")]
//    public UnknownSelectMode unknownSelectMode = UnknownSelectMode.Farthest;

//    public enum TargetUpdateMode
//    {
//        EveryNode,      // 現状の動作：毎回再計算
//        OnArrival       // 到達したときだけ再計算
//    }

//    [Header("探索方針②：targetNode 更新方式")]
//    public TargetUpdateMode targetUpdateMode = TargetUpdateMode.EveryNode;

//    // ★ 追加：Unknown が 0 のときのフォールバック先
//    public enum NoUnknownFallbackMode
//    {
//        FarthestFromStart,   // Start から最遠 Node（今まで通り）
//        NewestNode           // 一番最近できた Node（allNodes の末尾）
//    }

//    [Header("探索方針③：Unknown=0 のときのターゲット")]
//    public NoUnknownFallbackMode noUnknownFallbackMode = NoUnknownFallbackMode.FarthestFromStart;

//    // ===== Excluded / Repath loop fix helpers =====
//    [SerializeField] private int oscillationMemorySize = 6;      // 直近ノード記憶数
//    [SerializeField] private int avoidTargetFrames = 90;          // 一時回避の持続フレーム
//    [SerializeField] private int tabooEdgeFrames = 120;           // 辺の一時禁止の持続フレーム

//    private readonly Queue<MapNode> recentNodeMemory = new Queue<MapNode>();
//    private readonly Dictionary<MapNode, int> avoidedTargetsUntil = new Dictionary<MapNode, int>();
//    private readonly Dictionary<ulong, int> tabooEdgesUntil = new Dictionary<ulong, int>();

//    private static FieldInfo _fiIsMustPass;
//    private static MethodInfo _miIsMustPassCell;
//    // ★ Playerごと：一度踏んだ MustPass（セル）を記録
//    private HashSet<Vector2Int> visitedMustPassCells = new HashSet<Vector2Int>();

//    private System.Random dirRng;

//    void Start()
//    {
//        playerId = nextPlayerId;
//        nextPlayerId++;
//        gameObject.name = $"Player_{playerId}";

//        dirRng = new System.Random(playerId * 10007 + 12345);

//        // プレイヤー座標をスナップ
//        Vector3 snapped = SnapToGrid(transform.position);
//        transform.position = snapped;
//        targetPos = snapped;

//        // スポーン位置に合わせて StartDirection を自動設定（必要な場合のみ）
//        ApplyAutoStartDirection(snapped);
//        moveDir = startDirection.normalized;

//        ApplyVisual();

//        // ------------------------------------------------
//        // ① StartNode の設定（最初に行う）
//        // ------------------------------------------------
//        Vector2Int cell = WorldToCell(snapped);

//        MapNode nodeAtStart = null;

//        if (MapNode.allNodeCells.Contains(cell))
//        {
//            nodeAtStart = MapNode.FindByCell(cell);
//        }
//        else
//        {
//            GameObject obj = Instantiate(nodePrefab, snapped, Quaternion.identity);
//            nodeAtStart = obj.GetComponent<MapNode>();
//            nodeAtStart.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//        }

//        // StartNode が未設定なら “ここで” 設定する
//        if (MapNode.StartNode == null)
//        {
//            MapNode.StartNode = nodeAtStart;
//            nodeAtStart.distanceFromStart = 0;

//            //Debug.Log($"[SET STARTNODE] StartNode = {nodeAtStart.name}");
//        }

//        // ------------------------------------------------
//        // ② currentNode の設定（StartNode 設定後）
//        // ------------------------------------------------
//        currentNode = nodeAtStart;
//        RegisterCurrentNode(currentNode);

//        //Debug.Log($"[SET CURRENTNODE] currentNode = {currentNode.name}");

//        // ★ 評価ログ：スタート時点での訪問情報を初期化
//        visitedNodes.Clear();
//        if (currentNode != null)
//        {
//            visitedNodes.Add(currentNode);
//            uniqueNodesVisited = visitedNodes.Count;
//        }

//        goalReached = false;
//        frameToGoal = -1;
//        stepsWalked = 0;
//        deadEndEnterCount = 0;
//    }


//    // =============================
//    // StartDirection auto assignment
//    // =============================
//    //private void ApplyAutoStartDirection(Vector3 snappedWorldPos)
//    //{
//    //    if (startDirectionMode == StartDirectionMode.Manual)
//    //        return;

//    //    Vector3 dir = Vector3.forward;

//    //    if (startDirectionMode == StartDirectionMode.AutoByPlayerId)
//    //    {
//    //        dir = DirectionFromPlayerId(playerId);
//    //    }
//    //    else
//    //    {
//    //        Vector3 refPos = (startDirectionOrigin != null) ? startDirectionOrigin.position : gridOrigin;

//    //        if (startDirectionMode == StartDirectionMode.AutoFromSpawnRelativeToStartNode)
//    //        {
//    //            if (MapNode.StartNode != null)
//    //                refPos = MapNode.StartNode.transform.position;
//    //        }

//    //        Vector3 delta = snappedWorldPos - refPos;
//    //        dir = CardinalFromDeltaXZ(delta);

//    //        // スポーンが基準と同じ位置（delta=0）になった場合の保険
//    //        if (dir.sqrMagnitude < 1e-6f)
//    //            dir = DirectionFromPlayerId(playerId);
//    //    }

//    //    if (invertAutoStartDirection)
//    //        dir = -dir;

//    //    startDirection = dir;

//    //    if (debugLog)
//    //        Debug.Log($"[CFS][STARTDIR] playerId={playerId} mode={startDirectionMode} dir={startDirection}");
//    //}
//    private void ApplyAutoStartDirection(Vector3 snappedWorldPos)
//    {
//        // PlayerSpawner から渡される想定の基準点
//        Vector3 refPos = (startDirectionOrigin != null) ? startDirectionOrigin.position : gridOrigin;

//        Vector3 delta = snappedWorldPos - refPos;
//        Vector3 dir = CardinalFromDeltaXZ(delta);

//        // 基準点と同位置になった保険（ゼロ方向回避）
//        if (dir.sqrMagnitude < 1e-6f)
//            dir = DirectionFromPlayerId(playerId);

//        if (invertAutoStartDirection)
//            dir = -dir;

//        startDirection = dir;

//        if (debugLog)
//            Debug.Log($"[CFS][STARTDIR] playerId={playerId} origin={(startDirectionOrigin ? startDirectionOrigin.name : "gridOrigin")} dir={startDirection}");
//    }


//    private Vector3 DirectionFromPlayerId(int id)
//    {
//        int m = (id - 1) % 4;
//        if (m == 0) return Vector3.forward;
//        if (m == 1) return Vector3.back;
//        if (m == 2) return Vector3.left;
//        return Vector3.right;
//    }

//    private Vector3 CardinalFromDeltaXZ(Vector3 delta)
//    {
//        delta.y = 0f;
//        if (delta.sqrMagnitude < 1e-6f) return Vector3.zero;

//        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.z))
//            return (delta.x >= 0f) ? Vector3.right : Vector3.left;
//        else
//            return (delta.z >= 0f) ? Vector3.forward : Vector3.back;
//    }

//    void Update()
//    {
//        // 被弾後、一定時間ノーダメなら再開（その間は完全停止）
//        if (stopMoveWhileRecentlyDamaged && Time.time < damageStopUntilTime)
//        {
//            return;
//        }

//        if (movementLockedForever) return;

//        //------------------------------------------------------
//        // ① 移動中ならまず位置を更新（★最優先）
//        //------------------------------------------------------
//        if (isMoving)
//        {
//            //Debug.Log("[UPDATE] isMoving → MoveToTarget()");
//            MoveToTarget();
//            return; // ← TryExploreMove を先に呼ばないため必須
//        }

//        //------------------------------------------------------
//        // ② 移動完了直後の1フレームだけ TryExploreMove をブロック
//        //------------------------------------------------------
//        if (blockTryExploreThisFrame)
//        {
//            //Debug.Log("[UPDATE] blockTryExploreThisFrame → skip TryExploreMove");
//            blockTryExploreThisFrame = false;
//            return;
//        }

//        //------------------------------------------------------
//        // ③ Node設置 or 通路進行
//        //------------------------------------------------------
//        if (CanPlaceNodeHere())
//        // ★ Node 中心にいるかどうかを判定してから TryExploreMove を呼ぶ
//        //if (IsExactlyOnNodeCenter())
//        {
//            //Debug.Log("[UPDATE] CanPlaceNodeHere()=true → TryExploreMove()");
//            //TryExploreMove();
//            if (arrivedThisNode)
//            {
//                //Debug.Log("[UPDATE] Node到達直後 → TryExploreMove()");
//                arrivedThisNode = false;
//                TryExploreMove();
//                return;
//            }
//            else
//            {
//                //Debug.Log("[UPDATE] Node中心だが到達直後でない → MoveForward()");
//                MoveForward();
//                return;
//            }
//        }
//        //else
//        {
//            //Debug.Log("[UPDATE] CanPlaceNodeHere()=false → MoveForward()");
//            MoveForward();
//        }
//    }

//    private void ApplyVisual()
//    {
//        if (bodyRenderer != null && exploreMaterial != null)
//            bodyRenderer.material = exploreMaterial;
//    }

//    private bool CanPlaceNodeHere()
//    {
//        // ★ Node中心に近いかどうか（これが最重要）
//        Vector3 snapped = SnapToGrid(transform.position);
//        float dist = Vector3.Distance(transform.position, snapped);

//        if (dist > 0.2f)
//            return false;  // 中心にいない → Node置けない

//        // ★ 壁・開放方向の判定（既存ロジックをそのまま使用）
//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        bool frontWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
//        bool leftWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
//        bool rightWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

//        int openings = (!frontWall ? 1 : 0) + (!leftWall ? 1 : 0) + (!rightWall ? 1 : 0);

//        return frontWall || openings >= 2;

//        //// Nodeを置くのは「意思決定点」だけにする

//        //// 行き止まり（前も左右も壁）
//        //bool deadEnd = frontWall && leftWall && rightWall;

//        //// 曲がり角（前が壁で、左右どちらかだけ空き）
//        //bool corner = frontWall && (leftWall != rightWall);

//        //// 分岐（左右が両方空き：ここは判断点）
//        //bool split = (!leftWall && !rightWall);

//        //return deadEnd || corner || split;

//    }

//    private void MoveForward()
//    {
//        Vector3 next = transform.position + moveDir * cellSize;

//        // 壁チェック
//        if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
//                            moveDir,
//                            cellSize,
//                            wallLayer))
//        {
//            isMoving = false;

//            // BestTarget追従中なら「別ルートが無いか」次フレームで再探索する
//            if (lastBestTarget != null)
//            {
//                needRepathToBestTarget = true;
//                blockedExcludedFrom = null;
//                blockedExcludedNode = null;
//            }

//            return;
//        }

//        Vector3 nextSnap = SnapToGrid(next);
//        Vector2Int nextCell = WorldToCell(nextSnap);

//        // ★ 次セルに「既存の Excluded Node があるか？」だけを見る（DangerPointセル判定はしない）
//        MapNode nextNode = MapNode.FindByCell(nextCell);

//        if (nextNode != null && nextNode.isExcluded)
//        {
//            if (debugLog)
//                Debug.Log($"[MOVE][BLOCK_EX_NODE] {currentNode?.name} -> {nextNode.name}");

//            // 「この一手は塞がれた」→ BestTargetへ向かう別ルートを次フレームで探す
//            needRepathToBestTarget = true;
//            blockedExcludedFrom = currentNode;
//            blockedExcludedNode = nextNode;

//            isMoving = false;
//            return;
//        }

//        targetPos = nextSnap;
//        isMoving = true;

//        // ★ 評価ログ：1マス分の移動が確定したので歩数を加算
//        stepsWalked++;
//    }


//    private void MoveToTarget()
//    {
//        if (!isMoving) return;

//        const float arriveThreshold = 0.05f;

//        // ★ まだ到達していない場合は移動し続ける
//        if (Vector3.Distance(transform.position, targetPos) > arriveThreshold)
//        {
//            transform.position = Vector3.MoveTowards(
//                transform.position,
//                targetPos,
//                moveSpeed * Time.deltaTime
//            );
//            return;
//        }

//        //------------------------------------------------------
//        // ★ Nodeへ完全到達した瞬間（1回だけ実行）
//        //------------------------------------------------------
//        transform.position = targetPos;

//        //Debug.Log(
//        //    $"[CHECK-ARRIVE] Arrived at targetPos={targetPos} | " +
//        //    $"actualPos={transform.position} | " +
//        //    $"arrivedThisNode={arrivedThisNode}"
//        //);

//        isMoving = false;

//        // ★ 到達したのでフラグON（次フレーム、Update側で使う）
//        arrivedThisNode = true;

//        // ★ Node参照を確定
//        currentNode = MapNode.FindByCell(WorldToCell(targetPos));

//        if (currentNode != null)
//        {
//            currentNode.passCount++;
//            currentNode.OnPassed();
//        }

//        // ===== 評価ログ更新ここから =====
//        if (currentNode != null)
//        {
//            // ① 訪問済み Node 集合を更新
//            if (visitedNodes.Add(currentNode))
//            {
//                uniqueNodesVisited = visitedNodes.Count;
//            }

//            // ② 行き止まり Node に入った回数（links=1）
//            if (currentNode.links != null && currentNode.links.Count == 1)
//            {
//                deadEndEnterCount++;
//            }

//            // ③ Goal 到達判定：Tag が "Goal" の Node に来たら
//            if (!goalReached && currentNode.gameObject.CompareTag("Goal"))
//            {
//                //Debug.Log("[GOAL] GoalNode に到達しました");
//                goalReached = true;
//                frameToGoal = Time.frameCount;

//                if (RestartManager.Instance != null)
//                {
//                    RestartManager.Instance.WriteCellFromStartPlayerCsv(this);
//                    RestartManager.Instance.WriteRunSummaryCsv(this);

//                    RestartManager.Instance.StartRestart();
//                }
//            }

//            //EvaluationLogger.LogNodeVisit(playerId, Time.frameCount, currentNode);
//            EvaluationLogger.LogNodeVisit(
//                playerId,
//                Time.frameCount,
//                currentNode,
//                unknownSelectMode.ToString(),
//                targetUpdateMode.ToString(),
//                noUnknownFallbackMode.ToString(),
//                unknownReferenceDepth,
//                stepIndex
//            );

//            // この行のあとでインクリメント
//            stepIndex++;
//        }
//        else
//        {
//            //Debug.LogWarning($"[GOAL-DEBUG] currentNode が null です。targetPos={targetPos}");
//        }
//        // ===== 評価ログ更新ここまで =====

//        //------------------------------------------------------
//        // ★ このフレームに TryExploreMove() を呼ばせない
//        //------------------------------------------------------
//        blockTryExploreThisFrame = true;
//    }


//    private void RegisterCurrentNode(MapNode node)
//    {
//        if (node == null) return;

//        // ★ 直近2ノード更新（往復検知・別ルート探索に使う）
//        if (lastVisitedNode != node)
//        {
//            prevVisitedNode = lastVisitedNode;
//            lastVisitedNode = node;

//            // ★ ここで「このPlayerがこのNodeに到達した回数」を加算（=以後のコストが上がる）
//            IncrementVisitCount(node);
//        }

//        // 直近ノード記録
//        recentNodes.Add(node);
//        //if (recentNodes.Count > recentNodeMemory)
//        if (recentNodes.Count > oscillationMemorySize)
//            recentNodes.RemoveAt(0);
//    }

//    private bool IsFallbackOnCooldown(MapNode n)
//    {
//        if (n == null) return false;
//        if (fallbackCooldownSteps <= 0) return false;

//        if (fallbackCooldownUntil.TryGetValue(n, out int until))
//        {
//            if (stepIndex < until) return true;

//            // 期限切れは掃除
//            fallbackCooldownUntil.Remove(n);
//        }
//        return false;
//    }

//    private void MarkFallbackCooldown(MapNode n)
//    {
//        if (n == null) return;
//        if (fallbackCooldownSteps <= 0) return;

//        fallbackCooldownUntil[n] = stepIndex + fallbackCooldownSteps;
//    }

//    // =============================
//    // ★★★ メイン探索ルーチン ★★★
//    // =============================
//    private void TryExploreMove()
//    {
//        // ★ Node到達直後のフレームでは実行しない（この1行が超重要）
//        if (blockTryExploreThisFrame)
//        {
//            return;
//        }

//        //------------------------------------------------------
//        // ① Node生成・更新（Nodeスナップ後に呼ばれる前提）
//        //------------------------------------------------------
//        currentNode = TryPlaceNode(transform.position);
//        currentNode.RecalculateUnknownAndWall();
//        RegisterCurrentNode(currentNode);

//        // ------------------------------------------------------
//        // ①-2 MustPass（重み運用）：
//        //   ・MustPassを「範囲内なら強制追跡」しない
//        //   ・踏んだら踏破済みにする（同じMustPassへ吸い付くのを防ぐ）
//        // ------------------------------------------------------
//        if (currentNode != null && IsMustPassNode(currentNode))
//        {
//            MarkVisitedMustPass(currentNode);
//        }

//        Vector3? localUnknownDir = GetLocalUnknownDirection(currentNode);
//        if (localUnknownDir.HasValue)
//        {
//            lastBestTarget = null;
//            lastTargetIsFarthest = false;

//            moveDir = localUnknownDir.Value.normalized;
//            MoveForward();
//            return;
//        }



//        // ★ Excluded(=壁扱いNode) などで止められた場合：
//        //   「同じBestTargetを別ルートで目指す」→ それも無理なら一旦別行動へ
//        if (needRepathToBestTarget && lastBestTarget != null && currentNode != null && currentNode != lastBestTarget)
//        {
//            var target = lastBestTarget;
//            needRepathToBestTarget = false;

//            // Excludedノードで止められたなら、その一手をタブーにして別ルートを探す
//            if (blockedExcludedFrom == currentNode && blockedExcludedNode != null)
//            {
//                EnsureTabooTarget(target);
//                AddTabooEdge(currentNode, blockedExcludedNode);
//            }
//            blockedExcludedFrom = null;
//            blockedExcludedNode = null;

//            MapNode repathNext;
//            if (TryGetNextNodeToward(currentNode, target, out repathNext) && repathNext != null)
//            {
//                // backtrack が続くなら一旦ターゲットを手放して往復を切る
//                if (!UpdateOscillationIfBacktrack(target, repathNext))
//                {
//                    moveDir = DirToNode(currentNode, repathNext);
//                    MoveForward();
//                    return;
//                }
//                // UpdateOscillationIfBacktrack() 内で TemporarilyAvoidTarget されている場合は通常フローへ落とす
//            }
//            else
//            {
//                TemporarilyAvoidTarget(target, "repath failed");
//            }
//        }// 近場探索（リンクBFS）
//        var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
//        var unknownNodes = nearNodes.Where(n => n.unknownCount > 0 && !IsAvoidedTarget(n)).ToList();

//        //------------------------------------------------------
//        // ② FOLLOW（OnArrival の時だけ機能させる）
//        //------------------------------------------------------
//        bool followMode =
//            (targetUpdateMode == TargetUpdateMode.OnArrival) &&
//            (lastBestTarget != null && currentNode != lastBestTarget);

//        // ★ フォールバック追従中に、近場に Unknown が出てきたら乗り換え
//        if (followMode && lastTargetIsFarthest && unknownNodes.Count > 0)
//        {
//            followMode = false;
//            lastBestTarget = null;
//            lastTargetIsFarthest = false;
//        }


//        if (followMode)
//        {
//            MapNode nextNode;
//            if (TryGetNextNodeToward(currentNode, lastBestTarget, out nextNode) && nextNode != null)
//            {
//                // backtrack が続くなら一旦ターゲットを手放して往復を切る
//                if (!UpdateOscillationIfBacktrack(lastBestTarget, nextNode))
//                {
//                    moveDir = DirToNode(currentNode, nextNode);
//                    MoveForward();
//                    return;
//                }
//                // UpdateOscillationIfBacktrack() 内で TemporarilyAvoidTarget されている場合は通常フローへ落とす
//            }
//            else
//            {
//                // 追従できない＝今は届かない → 一旦別行動へ
//                var t = lastBestTarget;
//                TemporarilyAvoidTarget(t, "follow path not found");

//                moveDir = ChooseRandomValidDirection(currentNode).Value;
//                MoveForward();
//                return;
//            }
//        }
//        //------------------------------------------------------
//        // ③ bestTargetへ到達した時（OnArrival）
//        //------------------------------------------------------
//        if (currentNode == lastBestTarget)
//        {
//            lastBestTarget = null;
//            lastTargetIsFarthest = false;

//            // 追従が終わったのでタブー／往復検知をリセット
//            tabooTarget = null;
//            tabooEdges.Clear();
//            oscillA = null;
//            oscillB = null;
//            oscillTarget = null;
//            oscillCount = 0;
//        }

//        //------------------------------------------------------
//        // ④ Unknown（近場） or Global Frontier（全体フロンティア）を決める
//        //------------------------------------------------------
//        // ④-1: 近場に Unknown があればいつも通り Unknown を優先
//        MapNode unknownTarget = null;
//        if (unknownNodes.Count > 0)
//            unknownTarget = SelectUnknownNode(unknownNodes, currentNode);

//        // ④-2: 近場に Unknown が無い → 「到達可能なノード」の中から
//        //       unknownCount > 0（＝掘れる場所が残るノード）だけを集めてターゲットにする
//        MapNode globalFrontierTarget = null;

//        if (unknownTarget == null)
//        {
//            // currentNode からリンクで到達可能なノードだけ
//            var reachable = BFS_ReachableNodes(currentNode);

//            // ★ Unknown 判定の取りこぼしを減らしたいので、ここで再計算（重いなら後で最適化可）
//            foreach (var n in reachable)
//                if (n != null) n.RecalculateUnknownAndWall();

//            var globalFrontiers = reachable
//                .Where(n => n != null &&
//                            n != currentNode &&
//                            n.distanceFromStart < int.MaxValue &&
//                            n.unknownCount > 0 && !IsAvoidedTarget(n))          // ★ここが最重要：unknown>0 のみ
//                .ToList();

//            if (globalFrontiers.Count == 0)
//            {
//                // ★ 完全探索済み（unknown がどこにも無い）→ ここで終了動作
//                // 循環させたくないなら「動かない」が一番確実
//                lastBestTarget = null;
//                lastTargetIsFarthest = false;
//                isMoving = false;
//                return;
//            }

//            switch (noUnknownFallbackMode)
//            {
//                case NoUnknownFallbackMode.FarthestFromStart:
//                    // 「フロンティアの中で」Startから最遠
//                    globalFrontierTarget = globalFrontiers
//                        .OrderByDescending(n => n.distanceFromStart)
//                        .FirstOrDefault();
//                    break;

//                case NoUnknownFallbackMode.NewestNode:
//                    // 「フロンティアの中で」一番最近できたノード
//                    // allNodes は生成順なので、末尾から探す
//                    var frontierSet = new HashSet<MapNode>(globalFrontiers);
//                    for (int i = MapNode.allNodes.Count - 1; i >= 0; i--)
//                    {
//                        var n = MapNode.allNodes[i];
//                        if (n == null) continue;
//                        if (n == currentNode) continue;
//                        if (!frontierSet.Contains(n)) continue;

//                        globalFrontierTarget = n;
//                        break;
//                    }

//                    // 保険：見つからないとき
//                    if (globalFrontierTarget == null)
//                    {
//                        globalFrontierTarget = globalFrontiers
//                            .OrderByDescending(n => n.distanceFromStart)
//                            .FirstOrDefault();
//                    }
//                    break;
//            }
//        }

//        //------------------------------------------------------
//        // ⑤ ターゲット決定（Unknown優先 → 無ければ GlobalFrontier）
//        //------------------------------------------------------
//        MapNode bestTarget = unknownTarget ?? globalFrontierTarget;

//        if (bestTarget == null)
//        {
//            lastBestTarget = null;
//            lastTargetIsFarthest = false;

//            moveDir = ChooseRandomValidDirection(currentNode).Value;
//            MoveForward();
//            return;
//        }

//        //------------------------------------------------------
//        // ⑥ 経路を構築して次ノードへ進む
//        //------------------------------------------------------

//        MapNode nextNode2;
//        if (!TryGetNextNodeToward(currentNode, bestTarget, out nextNode2) || nextNode2 == null)
//        {
//            // BestTargetに届かない（Excludedの壁で分断されている等）
//            TemporarilyAvoidTarget(bestTarget, "bestTarget path not found");

//            moveDir = ChooseRandomValidDirection(currentNode).Value;
//            MoveForward();
//            return;
//        }

//        // backtrack が続くなら一旦ターゲットを手放して往復を切る
//        if (UpdateOscillationIfBacktrack(bestTarget, nextNode2))
//        {
//            moveDir = ChooseRandomValidDirection(currentNode).Value;
//            MoveForward();
//            return;
//        }

//        moveDir = DirToNode(currentNode, nextNode2);
//        //------------------------------------------------------
//        // ⑦ bestTarget のセット
//        //------------------------------------------------------
//        lastBestTarget = bestTarget;

//        // ★ unknownTarget が null のときは「近場Unknownが無かったのでグローバルフロンティアへ」
//        //    FOLLOW中に近場Unknownが出たら乗り換え対象にしたいので true 扱いにする
//        lastTargetIsFarthest = (unknownTarget == null);

//        //------------------------------------------------------
//        // ⑧ 移動
//        //------------------------------------------------------
//        MoveForward();
//    }

//    // ===== Helpers =====
//    private Vector3 DirToNode(MapNode from, MapNode to)
//    {
//        if (from == null || to == null) return Vector3.zero;
//        Vector3 d = to.transform.position - from.transform.position;
//        d.y = 0f;
//        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
//    }

//    private void RememberRecentNode(MapNode node)
//    {
//        if (node == null) return;
//        recentNodeMemory.Enqueue(node);
//        while (recentNodeMemory.Count > oscillationMemorySize)
//            recentNodeMemory.Dequeue();
//    }

//    // 直近4ステップが A->B->A->B なら「2点間往復」とみなす（最小の検知）
//    private bool IsOscillatingAB()
//    {
//        if (recentNodeMemory.Count < 4) return false;
//        var a = recentNodeMemory.ToArray();
//        int n = a.Length;
//        return a[n - 1] == a[n - 3] && a[n - 2] == a[n - 4] && a[n - 1] != a[n - 2];
//    }

//    private void TemporarilyAvoidTarget(MapNode t, string reason = "", int frames = -1)
//    {
//        if (t == null) return;
//        if (frames <= 0) frames = avoidTargetFrames;
//        avoidedTargetsUntil[t] = Time.frameCount + frames;

//        if (debugLog)
//            Debug.Log($"[AVOID_TARGET] {t.name} frames={frames} reason={reason}");
//    }


//    private bool IsAvoidedTarget(MapNode t)
//    {
//        if (t == null) return false;
//        if (!avoidedTargetsUntil.TryGetValue(t, out int until)) return false;
//        if (Time.frameCount > until)
//        {
//            avoidedTargetsUntil.Remove(t);
//            return false;
//        }
//        return true;
//    }

//    // directed edge key: from->to
//    private ulong PackEdgeKey(MapNode from, MapNode to)
//    {
//        unchecked
//        {
//            uint a = (uint)from.GetInstanceID();
//            uint b = (uint)to.GetInstanceID();
//            return ((ulong)a << 32) | b;
//        }
//    }

//    private void AddTabooEdge(MapNode from, MapNode to, int frames = -1)
//    {
//        if (from == null || to == null) return;
//        if (frames <= 0) frames = tabooEdgeFrames;
//        tabooEdgesUntil[PackEdgeKey(from, to)] = Time.frameCount + frames;
//    }

//    private bool IsTabooEdge(MapNode from, MapNode to)
//    {
//        if (from == null || to == null) return false;
//        ulong key = PackEdgeKey(from, to);

//        if (!tabooEdgesUntil.TryGetValue(key, out int until)) return false;
//        if (Time.frameCount > until)
//        {
//            tabooEdgesUntil.Remove(key);
//            return false;
//        }
//        return true;
//    }


//    // ★ Unknown方向を選ぶ（Excluded Node 方向は「壁扱い」で除外）
//    //private Vector3? GetLocalUnknownDirection(MapNode node)
//    //{
//    //    if (node == null) return null;

//    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//    //    foreach (var dir in dirs)
//    //    {
//    //        // 既にリンクがあるなら Unknown ではない
//    //        if (IsLinkedDirection(node, dir))
//    //            continue;

//    //        // 壁 or Excluded Node なら掘れない（Unknownにしない）
//    //        if (IsWall(node, dir))
//    //            continue;

//    //        return dir;
//    //    }

//    //    return null;
//    //}
//    private Vector3? GetLocalUnknownDirection(MapNode node)
//    {
//        // 4方向（順番はもう意味を持たない）
//        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//        // 条件を満たす候補を集める
//        List<Vector3> candidates = new List<Vector3>(4);
//        foreach (var dir in dirs)
//        {
//            if (IsLinkedDirection(node, dir)) continue;
//            if (IsWall(node, dir)) continue;
//            candidates.Add(dir);
//        }

//        if (candidates.Count == 0) return null;

//        // 候補からランダムに1つ
//        int idx = dirRng.Next(candidates.Count);
//        return candidates[idx];
//    }


//    private MapNode SelectUnknownNode(List<MapNode> unknownNodes, MapNode current)
//    {
//        switch (unknownSelectMode)
//        {
//            case UnknownSelectMode.Random:
//                return unknownNodes[Random.Range(0, unknownNodes.Count)];

//            case UnknownSelectMode.Nearest:
//                return unknownNodes
//                    .OrderBy(n => Distance(current, n))
//                    .First();

//            case UnknownSelectMode.Farthest:
//                return unknownNodes
//                    .OrderByDescending(n => Distance(current, n))
//                    .First();

//            case UnknownSelectMode.MostUnknown:
//                return unknownNodes
//                    .OrderByDescending(n => n.unknownCount)
//                    .First();
//        }

//        return unknownNodes[0];
//    }



//    // =============================
//    // ★ 有効なランダム方向を返す（A/B/C 共通処理）
//    // =============================
//    private Vector3? ChooseRandomValidDirection(MapNode node)
//    {
//        List<Vector3> dirs = new()
//        {
//            Vector3.forward,
//            Vector3.back,
//            Vector3.left,
//            Vector3.right
//        };

//        // ★背後方向を除外（無限ループ防止）
//        Vector3 backDir = -moveDir;
//        dirs = dirs.Where(d => Vector3.Dot(d.normalized, backDir.normalized) < 0.7f).ToList();

//        // ★リンク方向は除外
//        dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

//        // ★壁方向も除外
//        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

//        // ★追加：Excluded 方向（次セルが Excluded）も除外
//        //dirs = dirs.Where(d => !IsExcludedNeighbor(node, d)).ToList();
//        dirs = dirs.Where(d =>
//        {
//            Vector2Int c = node.cell + DirToCellDelta(d);
//            MapNode n = MapNode.FindByCell(c);
//            return n == null || !n.isExcluded;
//        }).ToList();

//        if (dirs.Count == 0)
//            return null;

//        return dirs[Random.Range(0, dirs.Count)];
//    }


//    // ==========================================================
//    // ★ リンクベースで到達可能な Node を BFS で列挙
//    // ==========================================================
//    private List<MapNode> BFS_ReachableNodes(MapNode start)
//    {
//        Queue<MapNode> q = new Queue<MapNode>();
//        HashSet<MapNode> visited = new HashSet<MapNode>();

//        q.Enqueue(start);
//        visited.Add(start);

//        while (q.Count > 0)
//        {
//            var n = q.Dequeue();

//            foreach (var next in n.links)
//            {
//                if (next == null) continue;

//                // ★ 追加
//                if (next.isExcluded) continue;

//                if (!visited.Contains(next))
//                {
//                    visited.Add(next);
//                    q.Enqueue(next);
//                }
//            }
//        }

//        return visited.ToList();
//    }


//    // =============================
//    // ★ 終端ノード D（ランダム）方式
//    // =============================
//    private Vector3? ChooseTerminalDirection(MapNode node)
//    {
//        List<Vector3> dirs = AllMovesExceptBack();

//        dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();
//        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

//        // ★追加：Excluded 方向（次セルが Excluded）も除外
//        //dirs = dirs.Where(d => !IsExcludedNeighbor(node, d)).ToList();
//        dirs = dirs.Where(d =>
//        {
//            Vector2Int c = node.cell + DirToCellDelta(d);
//            MapNode n = MapNode.FindByCell(c);
//            return n == null || !n.isExcluded;
//        }).ToList();


//        if (dirs.Count == 0) return null;
//        if (dirs.Count == 1) return dirs[0];

//        return dirs[Random.Range(0, dirs.Count)];
//    }


//    private bool IsTerminalNode(MapNode node)
//        => node != null && node.links.Count == 1;

//    // =============================
//    // ★ BFS(depth=N) 近場探索
//    // =============================
//    private List<MapNode> BFS_NearNodes(MapNode start, int depth)
//    {
//        Queue<(MapNode node, int dist)> q = new();
//        HashSet<MapNode> visited = new();

//        q.Enqueue((start, 0));
//        visited.Add(start);

//        //List<MapNode> results = new() { start };
//        List<MapNode> results = new();

//        while (q.Count > 0)
//        {
//            var (node, d) = q.Dequeue();
//            //if (d >= depth) continue;
//            if (d > depth) continue;

//            foreach (var link in node.links)
//            {
//                if (link == null) continue;

//                // ★ 追加
//                if (link.isExcluded) continue;

//                if (visited.Contains(link)) continue;

//                visited.Add(link);
//                //results.Add(link);
//                // ★ currentNode(start)以外だけ追加
//                if (link != start)
//                    results.Add(link);
//                q.Enqueue((link, d + 1));
//            }
//        }
//        return results;
//    }


//    // =============================
//    // ★ 最遠 & 近場未知 のスコア判定
//    // =============================
//    private MapNode ChooseBestTarget(MapNode targetUnknown, MapNode targetFarthest)
//    {
//        // Unknown だけ
//        if (targetUnknown != null && targetFarthest == null)
//        {
//            //Debug.Log($"[BEST] UnknownOnly → return {targetUnknown.name}");
//            return targetUnknown;
//        }

//        // Farthest だけ
//        if (targetUnknown == null && targetFarthest != null)
//        {
//            //Debug.Log($"[BEST] FarthestOnly → return {targetFarthest.name}");
//            return targetFarthest;
//        }

//        // 両方ある場合：距離差で勝負
//        int distUnknown = Mathf.Abs(targetUnknown.distanceFromStart - currentNode.distanceFromStart);
//        int distFarthest = Mathf.Abs(targetFarthest.distanceFromStart - currentNode.distanceFromStart);

//        MapNode best =
//            (distUnknown >= distFarthest) ? targetUnknown : targetFarthest;

//        //Debug.Log($"[CHECK-BEST] bestTarget={best?.name}, " +
//        //  $"current={currentNode?.name}, " +
//        //  $"targetUnknown={targetUnknown?.name}, " +
//        //  $"targetFarthest={targetFarthest?.name}");

//        if (distUnknown >= distFarthest)
//            return targetUnknown;
//        else
//            return targetFarthest;
//    }

//    private float Score(MapNode n)
//    {
//        // Unknown 優先、Unknown=0 なら距離で評価
//        return weightUnknown * n.unknownCount
//             + weightDistance * (-n.distanceFromStart);
//    }

//    List<MapNode> BuildShortestPath(MapNode start, MapNode goal)
//    {
//        if (start == null || goal == null) return null;
//        if (start == goal) return new List<MapNode>() { start };

//        // Dijkstra: dist と prev
//        Dictionary<MapNode, float> dist = new Dictionary<MapNode, float>();
//        Dictionary<MapNode, MapNode> prev = new Dictionary<MapNode, MapNode>();

//        // open は簡易実装（ノード数が多いなら優先度付きキューにしてもいい）
//        List<MapNode> open = new List<MapNode>();
//        HashSet<MapNode> closed = new HashSet<MapNode>();

//        dist[start] = 0f;
//        prev[start] = null;
//        open.Add(start);

//        int expanded = 0;
//        int skipEx = 0;

//        while (open.Count > 0)
//        {
//            // open から dist 最小のノードを取り出す
//            MapNode node = null;
//            float best = float.MaxValue;
//            for (int i = 0; i < open.Count; i++)
//            {
//                var n = open[i];
//                if (n == null) continue;
//                if (dist.TryGetValue(n, out float d) && d < best)
//                {
//                    best = d;
//                    node = n;
//                }
//            }

//            if (node == null) break;

//            open.Remove(node);
//            if (closed.Contains(node)) continue;
//            closed.Add(node);

//            expanded++;

//            if (node == goal)
//                return Rebuild(prev, start, goal);

//            // --- 隣接展開（正リンク）---
//            if (node.links != null)
//            {
//                for (int i = 0; i < node.links.Count; i++)
//                {
//                    MapNode nb = node.links[i];
//                    if (nb == null) continue;

//                    if (nb.isExcluded && nb != goal)
//                    {
//                        skipEx++;
//                        continue;
//                    }

//                    float nd = best + GetEnterCost(nb);

//                    if (!dist.TryGetValue(nb, out float old) || nd < old)
//                    {
//                        dist[nb] = nd;
//                        prev[nb] = node;

//                        if (!closed.Contains(nb) && !open.Contains(nb))
//                            open.Add(nb);
//                    }
//                }
//            }

//            // --- 逆リンク救済展開（あなたの仕様を維持） ---
//            foreach (var other in MapNode.allNodes)
//            {
//                if (other == null) continue;
//                if (other.links == null) continue;

//                if (other.links.Contains(node))
//                {
//                    if (other.isExcluded && other != goal)
//                    {
//                        skipEx++;
//                        continue;
//                    }

//                    float nd = best + GetEnterCost(other);

//                    if (!dist.TryGetValue(other, out float old) || nd < old)
//                    {
//                        dist[other] = nd;
//                        prev[other] = node;

//                        if (!closed.Contains(other) && !open.Contains(other))
//                            open.Add(other);
//                    }
//                }
//            }
//        }

//        return null;
//    }


//    // ------------------------------
//    // 通行不可(Excluded) / ループ対策ユーティリティ
//    // ------------------------------
//    private long PackEdgeKey(int fromId, int toId)
//    {
//        unchecked
//        {
//            return ((long)fromId << 32) ^ (uint)toId;
//        }
//    }

//    private void EnsureTabooTarget(MapNode target)
//    {
//        if (target == null) return;

//        if (tabooTarget != target)
//        {
//            tabooTarget = target;
//            tabooEdges.Clear();
//        }
//    }

//    private void AddTabooEdge(MapNode from, MapNode to)
//    {
//        if (from == null || to == null) return;

//        // tabooTarget は TryGetNextNodeToward() 側でセットされる前提
//        long key = PackEdgeKey(from.GetInstanceID(), to.GetInstanceID());
//        tabooEdges.Add(key);
//    }

//    private bool TryGetNextNodeToward(MapNode from, MapNode target, out MapNode nextNode)
//    {
//        nextNode = null;
//        if (from == null || target == null) return false;

//        // Dijkstra / BuildShortestPath は一切使わない
//        // 常に「隣接ノード評価のみ」で1手を決める

//        List<MapNode> candidates = new List<MapNode>();

//        if (from.links != null && from.links.Count > 0)
//        {
//            candidates.AddRange(from.links);
//        }
//        else if (localUseReverseLinkRescue)
//        {
//            // 逆リンク救済：from.links が空のときだけ走らせてコストを抑える
//            foreach (var other in MapNode.allNodes)
//            {
//                if (other == null) continue;
//                if (other.links == null) continue;
//                if (other.links.Contains(from))
//                    candidates.Add(other);
//            }
//        }

//        if (candidates.Count == 0) return false;

//        // MustPass が10個以上になる想定なのでキャッシュを使う
//        RefreshMustPassCacheIfNeeded();

//        MapNode best = null;
//        float bestScore = float.NegativeInfinity;

//        for (int i = 0; i < candidates.Count; i++)
//        {
//            MapNode c = candidates[i];
//            if (c == null) continue;
//            if (c == from) continue;

//            // Excluded（壁扱い）は通れない（ただし target は例外で通す）
//            if (c.isExcluded && c != target) continue;

//            // Avoid / Taboo / TempForbid は候補から除外
//            if (IsAvoidedTarget(c)) continue;
//            if (IsTabooEdge(from, c)) continue;
//            if (hasTempForbidEdge && from == tempForbidFrom && c == tempForbidTo) continue;

//            float score = ComputeLocalStepScore(from, c, target);

//            // 即時の往復は強く抑制
//            if (c == prevVisitedNode)
//                score -= backtrackPenalty;

//            if (score > bestScore)
//            {
//                bestScore = score;
//                best = c;
//            }
//        }

//        if (best == null) return false;

//        nextNode = best;
//        return true;
//    }


//    private void RefreshMustPassCacheIfNeeded()
//    {
//        int frame = Time.frameCount;
//        int allCount = (MapNode.allNodes != null) ? MapNode.allNodes.Count : 0;

//        bool needRebuild =
//            _mustPassCache.Count == 0 ||
//            _mustPassCacheLastAllNodesCount != allCount ||
//            (frame - _mustPassCacheLastFrame) >= mustPassCacheRefreshFrames;

//        if (!needRebuild) return;

//        _mustPassCache.Clear();
//        if (MapNode.allNodes != null)
//        {
//            for (int i = 0; i < MapNode.allNodes.Count; i++)
//            {
//                var n = MapNode.allNodes[i];
//                if (n == null) continue;
//                if (IsMustPassNode(n)) _mustPassCache.Add(n);
//            }
//        }

//        _mustPassCacheLastAllNodesCount = allCount;
//        _mustPassCacheLastFrame = frame;
//    }

//    private int GetNearestUnvisitedMustPassDistance(Vector2Int fromCell)
//    {
//        int best = int.MaxValue;

//        for (int i = 0; i < _mustPassCache.Count; i++)
//        {
//            var m = _mustPassCache[i];
//            if (m == null) continue;
//            if (IsVisitedMustPass(m)) continue; // このPlayerが既に踏破済みなら除外

//            int d = ManhattanDistance(fromCell, m.cell);
//            if (d < best) best = d;
//            if (best == 0) break;
//        }

//        return best;
//    }

//    private int GetVisitCount(MapNode node)
//    {
//        if (node == null) return 0;
//        if (nodeVisitCounts.TryGetValue(node, out int c)) return c;
//        return 0;
//    }

//    private void IncrementVisitCount(MapNode node)
//    {
//        if (node == null) return;
//        if (nodeVisitCounts.TryGetValue(node, out int c))
//            nodeVisitCounts[node] = c + 1;
//        else
//            nodeVisitCounts[node] = 1;
//    }

//    private int ManhattanDistance(Vector2Int a, Vector2Int b)
//    {
//        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
//    }

//    private float ComputeLocalStepScore(MapNode from, MapNode candidate, MapNode target)
//    {
//        if (candidate == null || target == null) return float.NegativeInfinity;

//        // ------------------------------------------------------------
//        // score = TargetField(Manhattan to BestTarget) + MustPassField(Manhattan to nearest unvisited MustPass)
//        //        - revisit penalty
//        //        + unknown bonus (optional)
//        // ------------------------------------------------------------

//        // 1) BestTarget field（最強）：近いほど高い / BestTargetが最大
//        int dT = ManhattanDistance(candidate.cell, target.cell);
//        float score = targetFieldMax - targetFieldSlope * dT;

//        // 2) MustPass field（弱めだが集まる）：最寄り未踏MustPassが近いほどボーナス
//        if (useMustPassAsWeight && _mustPassCache.Count > 0)
//        {
//            int dM = GetNearestUnvisitedMustPassDistance(candidate.cell);
//            if (dM != int.MaxValue)
//            {
//                float mustBonus = mustPassFieldBase * Mathf.Pow(mustPassFieldDecay, dM);
//                score += mustBonus;
//                //score += mustBonus * mustPassFieldMultiplier;
//            }
//        }

//        // 3) Revisit penalty（Playerごと）
//        int visits = GetVisitCount(candidate);
//        if (visits > 0)
//            score -= revisitPenaltyPerVisit * visits;

//        // 4) Optional: Unknown bonus
//        score += weightUnknown * candidate.unknownCount;

//        // Debug visualization
//        if (localWriteWeightToNode)
//        {
//            candidate.DistanceFromGoal = dT;
//            candidate.value = score;
//        }

//        return score;
//    }


//    private bool UpdateOscillationIfBacktrack(MapNode target, MapNode nextNode)
//    {
//        // nextNode が直前ノードに戻る動きなら、往復カウントを更新
//        if (target == null || nextNode == null) return false;
//        if (prevVisitedNode == null) return false;
//        if (nextNode != prevVisitedNode)
//        {
//            // backtrack でないならリセット
//            oscillA = null;
//            oscillB = null;
//            oscillTarget = null;
//            oscillCount = 0;
//            return false;
//        }

//        if (oscillTarget != target || oscillA != lastVisitedNode || oscillB != prevVisitedNode)
//        {
//            oscillTarget = target;
//            oscillA = lastVisitedNode;
//            oscillB = prevVisitedNode;
//            oscillCount = 1;
//        }
//        else
//        {
//            oscillCount++;
//        }

//        if (debugLog)
//            Debug.Log($"[OSC] target={target.name} pair=({oscillA?.name},{oscillB?.name}) count={oscillCount}");

//        if (oscillCount >= oscillLimit)
//        {
//            TemporarilyAvoidTarget(target, $"oscillation>={oscillLimit}");
//            return true;
//        }

//        return false;
//    }

//    private List<MapNode> Rebuild(Dictionary<MapNode, MapNode> prev,
//                                  MapNode start, MapNode goal)
//    {
//        List<MapNode> path = new List<MapNode>();
//        MapNode cur = goal;

//        while (cur != null)
//        {
//            path.Add(cur);
//            cur = prev[cur];
//        }

//        path.Reverse();
//        return path;
//    }



//    // =============================
//    // その他ユーティリティ
//    // =============================
//    private float Distance(MapNode a, MapNode b)
//        => Vector3.Distance(a.transform.position, b.transform.position);

//    private List<Vector3> AllMovesExceptBack()
//    {
//        List<Vector3> dirs = new()
//        {
//            Vector3.forward,
//            Vector3.back,
//            Vector3.left,
//            Vector3.right
//        };

//        Vector3 back = -moveDir;
//        return dirs.Where(d => Vector3.Dot(d.normalized, back.normalized) < 0.7f).ToList();
//    }

//    private bool IsLinkedDirection(MapNode node, Vector3 dir)
//    {
//        foreach (var link in node.links)
//        {
//            Vector3 diff = (link.transform.position - node.transform.position).normalized;
//            if (Vector3.Dot(diff, dir.normalized) > 0.7f)
//                return true;
//        }
//        return false;
//    }

//    private bool IsWall(MapNode node, Vector3 dir)
//    {
//        // ① 物理的な壁
//        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//        if (Physics.Raycast(origin, dir, cellSize, wallLayer))
//            return true;

//        // ② Excluded Node は「壁扱い」：次セルに Excluded Node があれば進めない
//        Vector3 nextWorld = SnapToGrid(node.transform.position + dir.normalized * cellSize);
//        Vector2Int nextCell = WorldToCell(nextWorld);
//        MapNode nextNode = MapNode.FindByCell(nextCell);

//        if (nextNode != null && nextNode.isExcluded)
//            return true;

//        return false;
//    }

//    private bool IsMustPassNode(MapNode node)
//    {
//        if (node == null) return false;

//        // ① まず “現時点でMustPassか” を既存ロジックで判定（時間は見ない）
//        bool raw = IsMustPassNodeRaw(node);

//        // rawが外れているなら、追跡も解除して終了
//        if (!raw)
//        {
//            mustPassExpireAt.Remove(node.cell);
//            return false;
//        }

//        // ② raw=true の場合：時間失効チェック
//        if (mustPassLifetimeSec > 0f)
//        {
//            float now = Time.time;

//            // 初回検知なら、寿命スタート
//            if (!mustPassExpireAt.TryGetValue(node.cell, out float expAt))
//            {
//                expAt = now + mustPassLifetimeSec;
//                mustPassExpireAt[node.cell] = expAt;
//            }

//            // 期限切れなら MustPass 無効化
//            if (now >= expAt)
//            {
//                ClearMustPassFlag(node);      // 可能ならフラグも落とす
//                mustPassExpireAt.Remove(node.cell);
//                return false;
//            }
//        }

//        return true;
//    }

//    private float GetEnterCost(MapNode node)
//    {
//        if (node == null) return 999999f;

//        // MustPassは「未踏なら」軽くする（踏破済みなら通常コストに戻す）
//        if (useMustPassAsWeight && IsMustPassNode(node) && !IsVisitedMustPass(node))
//            return mustPassEnterCost;

//        return normalEnterCost;
//    }

//    private bool IsMustPassNodeRaw(MapNode node)
//    {
//        if (node == null) return false;

//        // (A) MapNode.isMustPass があればそれを使う
//        if (_fiIsMustPass == null)
//        {
//            _fiIsMustPass = typeof(MapNode).GetField("isMustPass",
//                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
//        }

//        if (_fiIsMustPass != null && _fiIsMustPass.FieldType == typeof(bool))
//        {
//            return (bool)_fiIsMustPass.GetValue(node);
//        }

//        // (B) NodePointMarker.IsMustPassCell(Vector2Int) があればそれを使う
//        if (_miIsMustPassCell == null)
//        {
//            _miIsMustPassCell = typeof(NodePointMarker).GetMethod("IsMustPassCell",
//                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
//        }

//        if (_miIsMustPassCell != null)
//        {
//            object ret = _miIsMustPassCell.Invoke(null, new object[] { node.cell });
//            if (ret is bool b) return b;
//        }

//        return false;
//    }

//    private void ClearMustPassFlag(MapNode node)
//    {
//        if (node == null) return;

//        if (_fiIsMustPass == null)
//        {
//            _fiIsMustPass = typeof(MapNode).GetField("isMustPass",
//                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
//        }

//        if (_fiIsMustPass != null && _fiIsMustPass.FieldType == typeof(bool))
//        {
//            _fiIsMustPass.SetValue(node, false);
//        }
//    }


//    private MapNode FindMustPassTargetInRange(MapNode current, int depth)
//    {
//        if (current == null) return null;

//        // UnknownReference と同じ「リンクBFSの深さ」で範囲判定
//        var nearNodes = BFS_NearNodes(current, depth);

//        // Excluded は BFS_NearNodes 側で除外済み
//        var candidates = nearNodes
//            //.Where(n => n != null && IsMustPassNode(n) && !IsAvoidedTarget(n))
//            .Where(n => n != null
//            && IsMustPassNode(n)
//            && !IsVisitedMustPass(n)      // ★ 追加：このPlayerが未踏のみ
//            && !IsAvoidedTarget(n))
//            .ToList();

//        if (candidates.Count == 0) return null;

//        // 最短Hop（グラフ距離）で一番近い MustPass を選ぶ
//        return SelectMustPassNode_ByHopDistance(candidates, current, depth);
//    }

//    private MapNode SelectMustPassNode_ByHopDistance(List<MapNode> candidates, MapNode current, int maxDepth)
//    {
//        MapNode best = null;
//        int bestHop = int.MaxValue;
//        int bestStartDist = int.MaxValue; // tie-breaker

//        foreach (var c in candidates)
//        {
//            if (c == null) continue;

//            int hop = GetHopDistanceWithinDepth(current, c, maxDepth);
//            if (hop < bestHop)
//            {
//                best = c;
//                bestHop = hop;
//                bestStartDist = c.distanceFromStart;
//            }
//            else if (hop == bestHop)
//            {
//                // 同距離なら、Startから近い方を優先（挙動を安定させる）
//                if (c.distanceFromStart < bestStartDist)
//                {
//                    best = c;
//                    bestStartDist = c.distanceFromStart;
//                }
//            }
//        }

//        return best;
//    }

//    private int GetHopDistanceWithinDepth(MapNode start, MapNode target, int maxDepth)
//    {
//        if (start == null || target == null) return int.MaxValue;
//        if (start == target) return 0;

//        Queue<(MapNode node, int d)> q = new();
//        HashSet<MapNode> visited = new();

//        q.Enqueue((start, 0));
//        visited.Add(start);

//        while (q.Count > 0)
//        {
//            var (n, d) = q.Dequeue();
//            if (d >= maxDepth) continue;

//            if (n.links == null) continue;

//            for (int i = 0; i < n.links.Count; i++)
//            {
//                var nb = n.links[i];
//                if (nb == null) continue;
//                if (nb.isExcluded) continue;
//                if (visited.Contains(nb)) continue;

//                int nd = d + 1;
//                if (nb == target) return nd;

//                visited.Add(nb);
//                q.Enqueue((nb, nd));
//            }
//        }

//        return int.MaxValue;
//    }

//    private bool IsVisitedMustPass(MapNode node)
//    {
//        if (node == null) return false;
//        return visitedMustPassCells.Contains(node.cell);
//    }

//    private void MarkVisitedMustPass(MapNode node)
//    {
//        if (node == null) return;
//        visitedMustPassCells.Add(node.cell);
//    }

//    // ★ 追加：被弾した瞬間に居たNodeをMustPassにする（外部から呼ぶ用）
//    // ★追加：被弾位置を受け取って MustPass を立てる（ログもここで出す）
//    public void SetMustPassAtHitWorldPos(Vector3 hitWorldPos)
//    {
//        Vector3 snapped = SnapToGrid(hitWorldPos);
//        Vector2Int cell = WorldToCell(snapped);

//        MapNode byCell = MapNode.FindByCell(cell);

//        // ★重要：被弾セルにNodeが無いことがある（通路セル等）
//        // → currentNode（到達済みの分岐Node）を優先して fallback
//        MapNode final = byCell;
//        if (final == null)
//            final = (currentNode != null) ? currentNode : MapNode.FindNearest(hitWorldPos);

//        Debug.Log(
//            $"[MustPass][Damage][IN] hitWorldPos={hitWorldPos} snapped={snapped} cell={cell} " +
//            $"FindByCell={(byCell != null ? byCell.name : "null")} final={(final != null ? final.name : "null")}");

//        if (final == null) return;

//        // 念のため：Excluded なら MustPass にしない（好みで外してOK）
//        if (final.isExcluded) return;

//        if (!final.isMustPass)
//        {
//            final.isMustPass = true;
//            //if (!final.name.Contains("_MP")) final.name += "_MP";
//        }

//        // このPlayerは「そのMustPassは経由済み」にしておく（吸い付き防止）
//        MarkVisitedMustPass(final);

//        Debug.Log($"[MustPass][Damage][OUT] MustPassSet={final.name} cell={final.cell}");
//    }

//    // 互換用：既存呼び出しがあるなら残す（EnemyAttack 側を直した後でもOK）
//    public void SetMustPassAtCurrentNode_Damaged()
//    {
//        SetMustPassAtHitWorldPos(transform.position);
//    }

//    public void LockMovementUntilNoDamage(float noDamageSeconds)
//    {
//        if (!stopMoveWhileRecentlyDamaged) return;

//        noDamageSeconds = Mathf.Max(0f, noDamageSeconds);

//        // 今この瞬間から「noDamageSeconds」の間は停止
//        float until = Time.time + noDamageSeconds;

//        // すでに停止中なら、解除時刻を後ろに伸ばす（被弾が続くほど停止が延長される）
//        if (until > damageStopUntilTime)
//            damageStopUntilTime = until;
//    }

//    /// <summary>
//    /// Called from PlayerHealth when this player takes damage.
//    /// Stops this player's movement permanently (until death) to prevent MustPass from exploding.
//    /// </summary>
//    public void LockMovementForever_OnDamaged()
//    {
//        if (!stopForeverWhenDamaged) return;

//        if (movementLockedForever) return;
//        movementLockedForever = true;

//        // Immediately stop any in-progress move
//        isMoving = false;
//        blockTryExploreThisFrame = false;
//        arrivedThisNode = false;

//        Debug.Log($"[MOVE][LOCK] PlayerId={playerId} movement locked forever (damaged).");
//    }

//    private MapNode TryPlaceNode(Vector3 pos)
//    {
//        Vector3 snapped = SnapToGrid(pos);
//        Vector2Int cell = WorldToCell(snapped);

//        //Debug.Log($"[TP0] TryPlaceNode START | pos={pos} | snapped={snapped} | cell={cell}");

//        MapNode node;

//        if (MapNode.allNodeCells.Contains(cell))
//        {
//            node = MapNode.FindByCell(cell);
//            //Debug.Log($"[TP1] Existing Node FOUND | node={node.name} | links={node.links.Count}");
//        }
//        else
//        {
//            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//            node = obj.GetComponent<MapNode>();
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);

//            // ★ 新規Nodeを作ったのでカウントを増やす
//            newNodeCreatedCount++;
//            //Debug.Log($"[TP2] NEW Node CREATED | node={node.name} | cell={cell} | createdCount={newNodeCreatedCount}");

//            // ★ ここで「生成直後に一度だけ」ポイント判定
//            ApplyPointInfluence_Once(node);

//            // ★ 規定数に達したらこのPlayerをDestroy
//            if (newNodeCreatedCount >= destroyAfterNewNodes)
//            {
//                //Debug.Log($"[PLAYER-END] 新規Nodeを {newNodeCreatedCount} 個設置したので Player_{playerId} を Destroy");
//                Destroy(gameObject);   // このフレームの終わりに破棄される
//            }

//            //Debug.Log($"[TP2] NEW Node CREATED | node={node.name} | cell={cell}");
//        }

//        //Debug.Log($"[TP3] Before LinkBackward | node={node.name}");
//        LinkBackward(node);
//        //Debug.Log($"[TP4] After LinkBackward | node={node.name} | links={node.links.Count}");
//        return node;
//    }

//    private void ApplyPointInfluence_Once(MapNode node)
//    {
//        if (node == null) return;

//        // 事故防止：Goalは除外しない
//        if (node.CompareTag("Goal")) return;

//        // node.cell は既に設定されている前提
//        if (!NodePointMarker.IsExcludedCell(node.cell))
//            return;

//        // 既に除外済みなら二重処理しない（名前も増殖させない）
//        bool becameExcludedNow = false;

//        if (!node.isExcluded)
//        {
//            node.isExcluded = true;   // 永続
//            becameExcludedNow = true;

//            // 目視確認用（不要なら消してOK）
//            if (!node.name.EndsWith("_EX"))
//                node.name += "_EX";

//            if (debugPointLog)
//                Debug.Log($"[POINT] Excluded set (no-collider): {node.name} cell={node.cell}");
//        }
//        else
//        {
//            if (debugPointLog)
//                Debug.Log($"[POINT] Excluded already: {node.name} cell={node.cell}");
//        }

//        // ★重要：
//        // isExcluded は「後から」立つので、既に計算済みの unknown/wall が古いままになる。
//        // 自分＆リンク先（＝隣接ノード）を再計算して、Excluded方向を壁扱いに反映する。
//        // ※新しく置かれる側が「隣がExcludedか分からない」問題は、
//        //   後から置かれるノードが RecalculateUnknownAndWall() するときに
//        //   Raycastで Excludedノードを検知して壁扱いにできる前提。
//        //   ここで必要なのは「既存リンクがあるノードの再計算」。
//        if (becameExcludedNow)
//        {
//            // 自分
//            node.RecalculateUnknownAndWall();

//            // 直接リンクしている隣接ノード（両方向のunknown/wallが古い可能性がある）
//            if (node.links != null)
//            {
//                for (int i = 0; i < node.links.Count; i++)
//                {
//                    var nb = node.links[i];
//                    if (nb == null) continue;
//                    nb.RecalculateUnknownAndWall();
//                }
//            }

//            if (debugPointLog)
//                Debug.Log($"[POINT] Recalc unknown/wall due to exclude: self={node.name} links={(node.links != null ? node.links.Count : 0)}");
//        }
//    }


//    private void LinkBackward(MapNode node)
//    {
//        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//        Vector3 dir = -moveDir;

//        //Debug.Log($"[LB0] LinkBackward START | from={node.name} | dir={dir}");

//        LayerMask mask = wallLayer | nodeLayer;

//        for (int i = 1; i <= linkRayMaxSteps; i++)
//        {
//            float dist = cellSize * i;

//            if (debugRay)
//                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);


//            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
//            {
//                int layer = hit.collider.gameObject.layer;

//                //Debug.Log($"[LB1] Ray Hit | dist={dist} | hit={hit.collider.name}");

//                if ((wallLayer.value & (1 << layer)) != 0)
//                {
//                    //Debug.Log($"[LB2] Hit WALL → stop");
//                    return;
//                }

//                if ((nodeLayer.value & (1 << layer)) != 0)
//                {
//                    var hitNode = hit.collider.GetComponent<MapNode>();
//                    if (hitNode != null && hitNode != node)
//                    {
//                        //Debug.Log($"[LB3] Linking {node.name} ↔ {hitNode.name}");

//                        node.AddLink(hitNode);
//                        node.RecalculateUnknownAndWall();
//                        hitNode.RecalculateUnknownAndWall();
//                    }
//                    else
//                    {
//                        //Debug.Log($"[LB4] Hit self or null");
//                    }
//                    return;
//                }
//            }
//        }

//        //Debug.Log($"[LB5] No hit up to {linkRayMaxSteps} steps");
//    }

//    private Vector3 SnapToGrid(Vector3 pos)
//    {
//        int x = Mathf.RoundToInt((pos.x - gridOrigin.x) / cellSize);
//        int z = Mathf.RoundToInt((pos.z - gridOrigin.z) / cellSize);
//        return new Vector3(x * cellSize, 0, z * cellSize) + gridOrigin;
//    }

//    private Vector2Int WorldToCell(Vector3 worldPos)
//    {
//        Vector3 p = worldPos - gridOrigin;
//        return new Vector2Int(
//            Mathf.RoundToInt(p.x / cellSize),
//            Mathf.RoundToInt(p.z / cellSize)
//        );
//    }

//    private Vector3 CellToWorld(Vector2Int cell)
//        => new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;

//    private void Log(string msg)
//    {
//        //if (debugLog) Debug.Log("[CellFS] " + msg);
//    }

//    // ★ 追加：Vector3方向 → セル差分（x,z）
//    private static Vector2Int DirToCellDelta(Vector3 dir)
//    {
//        if (Vector3.Dot(dir, Vector3.forward) > 0.9f) return new Vector2Int(0, 1);
//        if (Vector3.Dot(dir, Vector3.back) > 0.9f) return new Vector2Int(0, -1);
//        if (Vector3.Dot(dir, Vector3.right) > 0.9f) return new Vector2Int(1, 0);
//        if (Vector3.Dot(dir, Vector3.left) > 0.9f) return new Vector2Int(-1, 0);
//        return Vector2Int.zero;
//    }

//    // ★ 追加：このセルは Excluded セルか？（Collider不要）
//    private bool IsExcludedCell(Vector2Int cell)
//    {
//        return NodePointMarker.IsExcludedCell(cell);
//    }

//    // ★ 追加：node から dir に1マス進む先が Excluded か？
//    private bool IsExcludedNeighbor(MapNode node, Vector3 dir)
//    {
//        if (node == null) return false;
//        Vector2Int nextCell = node.cell + DirToCellDelta(dir);
//        return IsExcludedCell(nextCell);
//    }
//}