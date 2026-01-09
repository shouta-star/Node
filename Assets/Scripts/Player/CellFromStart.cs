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

    [Header("初期設定")]
    public Vector3 startDirection;
    public Vector3 gridOrigin = Vector3.zero;
    public GameObject nodePrefab;

    [Header("探索パラメータ")]
    public int unknownReferenceDepth = 3; // ★ BFS探索深さとして使用

    [Header("スコア重み（A：現状の式）")]
    public float weightUnknown = 1f;
    public float weightDistance = 1f;

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

    //void Start()
    //{
    //    moveDir = startDirection.normalized;
    //    transform.position = SnapToGrid(transform.position);
    //    targetPos = transform.position;

    //    ApplyVisual();

    //    currentNode = TryPlaceNode(transform.position);
    //    if (MapNode.StartNode == null)
    //    {
    //        MapNode.StartNode = currentNode;
    //        currentNode.distanceFromStart = 0;
    //    }

    //    RegisterCurrentNode(currentNode);

    //    Log($"Start @ Node={currentNode.name}");
    //}
    void Start()
    {
        playerId = nextPlayerId;
        nextPlayerId++;
        gameObject.name = $"Player_{playerId}";

        moveDir = startDirection.normalized;

        // プレイヤー座標をスナップ
        Vector3 snapped = SnapToGrid(transform.position);
        transform.position = snapped;
        targetPos = snapped;

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

    void Update()
    {
        // MustPass 時間失効
        //TrackMustPassFlags();
        //CleanupExpiredMustPasses();

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

    //private bool IsExactlyOnNodeCenter()
    //{
    //    Vector3 snapped = SnapToGrid(transform.position);
    //    return Vector3.Distance(transform.position, snapped) < 0.05f;
    //}

    private void ApplyVisual()
    {
        if (bodyRenderer != null && exploreMaterial != null)
            bodyRenderer.material = exploreMaterial;
    }

    //private bool CanPlaceNodeHere()
    //{
    //    Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
    //    Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

    //    bool frontWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
    //    bool leftWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
    //    bool rightWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

    //    int openings = (!frontWall ? 1 : 0) + (!leftWall ? 1 : 0) + (!rightWall ? 1 : 0);

    //    return frontWall || openings >= 2;
    //}
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

    //private void MoveForward()
    //{
    //    Vector3 next = transform.position + moveDir * cellSize;

    //    // 壁チェック
    //    if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
    //                        moveDir,
    //                        cellSize,
    //                        wallLayer))
    //    {
    //        if (debugLog)
    //            //Debug.Log("[Block] Wall ahead → stop movement");

    //        isMoving = false;
    //        return;
    //    }

    //    targetPos = SnapToGrid(next);
    //    isMoving = true;

    //    // ★ 評価ログ：1マス分の移動が確定したので歩数を加算
    //    stepsWalked++;
    //}

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
        // ①-2 MustPass（必ず経由）：
        //   ・一度でも範囲内に入った MustPass は、到達するまで最優先で追う
        //   ・範囲判定は UnknownReference と同じ（unknownReferenceDepth Hop）
        // ------------------------------------------------------
        // 既に MustPass を追跡中なら、範囲外に出ても到達するまで継続
        if (lastBestTarget != null && IsMustPassNode(lastBestTarget))
        {
            if (currentNode == lastBestTarget)
            {
                // ★ MustPassなら、このPlayerだけ「踏破済み」にする
                if (IsMustPassNode(currentNode))
                    MarkVisitedMustPass(currentNode);

                // 到達したので解除
                lastBestTarget = null;
                lastTargetIsFarthest = false;
            }
            else
            {
                MapNode nextMustPass;
                if (TryGetNextNodeToward(currentNode, lastBestTarget, out nextMustPass) && nextMustPass != null)
                {
                    moveDir = DirToNode(currentNode, nextMustPass);
                    MoveForward();
                    return;
                }
                else
                {
                    // 到達不能なら一旦解除（分断されている等）
                    TemporarilyAvoidTarget(lastBestTarget, "mustPass unreachable");
                    lastBestTarget = null;
                    lastTargetIsFarthest = false;
                }
            }
        }

        // 新しく範囲内に MustPass が入ったら、その瞬間から追跡を開始
        MapNode mustPassTarget = FindMustPassTargetInRange(currentNode, unknownReferenceDepth);
        if (mustPassTarget != null && mustPassTarget != currentNode)
        {
            lastBestTarget = mustPassTarget;
            lastTargetIsFarthest = false;

            MapNode nextMustPass;
            if (TryGetNextNodeToward(currentNode, mustPassTarget, out nextMustPass) && nextMustPass != null)
            {
                moveDir = DirToNode(currentNode, nextMustPass);
                MoveForward();
                return;
            }
            else
            {
                // 近場に居るのに届かない＝リンクが途切れている等
                TemporarilyAvoidTarget(mustPassTarget, "mustPass unreachable");
                lastBestTarget = null;
                lastTargetIsFarthest = false;
            }
        }


        // ① 現在の Node に Unknown が残っているなら、まずその場で掘る
        //if (currentNode.unknownCount > 0)
        //{
        //    Vector3? localUnknownDir = currentNode.GetUnknownDirection();
        //    if (localUnknownDir.HasValue)
        //    {
        //        // ここで一旦ターゲットをリセット
        //        lastBestTarget = null;
        //        lastTargetIsFarthest = false;

        //        moveDir = localUnknownDir.Value.normalized;
        //        MoveForward();
        //        return;
        //    }
        //}
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

    //private void TemporarilyAvoidTarget(MapNode t, int frames = -1)
    //{
    //    if (t == null) return;
    //    if (frames <= 0) frames = avoidTargetFrames;
    //    avoidedTargetsUntil[t] = Time.frameCount + frames;
    //}
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
    private Vector3? GetLocalUnknownDirection(MapNode node)
    {
        if (node == null) return null;

        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in dirs)
        {
            // 既にリンクがあるなら Unknown ではない
            if (IsLinkedDirection(node, dir))
                continue;

            // 壁 or Excluded Node なら掘れない（Unknownにしない）
            if (IsWall(node, dir))
                continue;

            return dir;
        }

        return null;
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
    //private Vector3? ChooseRandomValidDirection(MapNode node)
    //{
    //    List<Vector3> dirs = new()
    //    {
    //        Vector3.forward,
    //        Vector3.back,
    //        Vector3.left,
    //        Vector3.right
    //    };

    //    // ★背後方向を除外（無限ループ防止）
    //    Vector3 backDir = -moveDir;
    //    dirs = dirs.Where(d => Vector3.Dot(d.normalized, backDir.normalized) < 0.7f).ToList();

    //    // ★リンク方向は除外
    //    dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

    //    // ★壁方向も除外
    //    dirs = dirs.Where(d => !IsWall(node, d)).ToList();

    //    if (dirs.Count == 0)
    //        return null;

    //    return dirs[Random.Range(0, dirs.Count)];
    //}
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
    //private Vector3? ChooseTerminalDirection(MapNode node)
    //{
    //    List<Vector3> dirs = AllMovesExceptBack();

    //    dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();
    //    dirs = dirs.Where(d => !IsWall(node, d)).ToList();

    //    if (dirs.Count == 0) return null;
    //    if (dirs.Count == 1) return dirs[0];

    //    return dirs[Random.Range(0, dirs.Count)];
    //}
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


    //private float Score(MapNode n)
    //{
    //    float u = n.unknownCount;
    //    float d = n.distanceFromStart;

    //    return weightUnknown * u + weightDistance * (-d);
    //}
    private float Score(MapNode n)
    {
        // Unknown 優先、Unknown=0 なら距離で評価
        return weightUnknown * n.unknownCount
             + weightDistance * (-n.distanceFromStart);
    }


    // =============================
    // ★ リンクを使った最短ルートBFS
    // =============================
    private List<MapNode> BuildShortestPath(MapNode start, MapNode goal)
    {
        if (start == null || goal == null) return null;
        if (start == goal) return new List<MapNode>() { start };

        Queue<MapNode> q = new Queue<MapNode>();
        Dictionary<MapNode, MapNode> prev = new Dictionary<MapNode, MapNode>();

        q.Enqueue(start);
        prev[start] = null;

        int expanded = 0;
        int skipEx = 0;

        while (q.Count > 0)
        {
            var node = q.Dequeue();
            expanded++;

            // --- 正リンク展開 ---
            foreach (var next in node.links)
            {
                if (next == null) continue;

                if (IsTabooEdge(node, next)) continue;
                if (hasTempForbidEdge && node == tempForbidFrom && next == tempForbidTo) continue;

                // ★除外ノードは経由しない（goal は事故防止で例外）
                if (next.isExcluded && next != goal)
                {
                    skipEx++;
                    // ログ出しすぎ防止：たまにだけ
                    if (dbgExclude && (Time.frameCount % dbgEveryNFrames == 0))
                        Debug.Log($"[BFS][SKIP_EX] {node.name}->{next.name}");
                    continue;
                }

                if (!prev.ContainsKey(next))
                {
                    prev[next] = node;
                    q.Enqueue(next);

                    if (next == goal)
                    {
                        if (dbgExclude)
                            Debug.Log($"[BFS][FOUND] expanded={expanded} skipEx={skipEx} start={start.name} goal={goal.name}");
                        return Rebuild(prev, start, goal);
                    }
                }
            }

            // --- 逆リンク救済展開（あなたの仕様） ---
            foreach (var other in MapNode.allNodes)
            {
                if (other == null) continue;

                if (other.links.Contains(node))
                {
                    // ★ここは other を見る（next は存在しない）
                    if (other.isExcluded && other != goal)
                    {
                        skipEx++;
                        if (dbgExclude && (Time.frameCount % dbgEveryNFrames == 0))
                            Debug.Log($"[BFS][SKIP_EX_REV] {node.name}<-{other.name}");
                        continue;
                    }

                    if (!prev.ContainsKey(other))
                    {
                        prev[other] = node;
                        q.Enqueue(other);

                        if (other == goal)
                        {
                            if (dbgExclude)
                                Debug.Log($"[BFS][FOUND_REV] expanded={expanded} skipEx={skipEx} start={start.name} goal={goal.name}");
                            return Rebuild(prev, start, goal);
                        }
                    }
                }
            }
        }

        if (dbgExclude)
            Debug.Log($"[BFS][FAIL] expanded={expanded} skipEx={skipEx} start={start.name} goal={goal.name}");

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

        // ② 通常の最短経路
        var p2 = BuildShortestPath(from, target);
        if (p2 != null && p2.Count >= 2)
        {
            nextNode = p2[1];
            return true;
        }

        return false;
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
    //private bool UpdateOscillationIfBacktrack(MapNode target, MapNode repathNext)
    //{
    //    // “次に行くノード” が “直前にいたノード” なら「往復（バックトラック）」とみなす
    //    if (repathNext == null || prevVisitedNode == null || lastVisitedNode == null)
    //        return false;

    //    bool isBacktrack = (repathNext == prevVisitedNode);

    //    if (!isBacktrack)
    //    {
    //        // 往復じゃない → 振動カウンタをリセット
    //        oscillCount = 0;
    //        oscillA = null;
    //        oscillB = null;
    //        oscillTarget = null;
    //        return false;
    //    }

    //    // ここから「往復」ケース
    //    // A↔B の行ったり来たりは、方向（A→B / B→A）や target の違いでリセットされないよう、
    //    // “unordered pair” として同一ペア扱いにする
    //    MapNode pair0 = lastVisitedNode;
    //    MapNode pair1 = prevVisitedNode;

    //    // InstanceID で並びを固定（順序を揃える）
    //    if (pair0.GetInstanceID() > pair1.GetInstanceID())
    //    {
    //        MapNode tmp = pair0;
    //        pair0 = pair1;
    //        pair1 = tmp;
    //    }

    //    // 直前と同じペアならカウント継続、違うペアならリセットして 1 から
    //    if (oscillA == pair0 && oscillB == pair1)
    //        oscillCount++;
    //    else
    //    {
    //        oscillA = pair0;
    //        oscillB = pair1;
    //        oscillCount = 1;
    //    }

    //    oscillTarget = target;

    //    Debug.Log($"[OSC] target={(target != null ? target.name : "null")} pair=({pair0.name},{pair1.name}) count={oscillCount}");

    //    if (oscillCount >= oscillLimit)
    //    {
    //        // この target を一時的に避けて、別の target を選ばせる
    //        TemporarilyAvoidTarget(target, $"oscillation>={oscillLimit}");
    //        return true; // このフレームはここで止める
    //    }

    //    return false; // まだ許容回数内 → そのままバックトラックを許す
    //}


    //private List<MapNode> BuildShortestPath(MapNode start, MapNode goal)
    //{
    //    if (start == null || goal == null) return null;
    //    if (start == goal) return new List<MapNode>() { start };

    //    Queue<MapNode> q = new Queue<MapNode>();
    //    Dictionary<MapNode, MapNode> prev = new Dictionary<MapNode, MapNode>();

    //    q.Enqueue(start);
    //    prev[start] = null;

    //    while (q.Count > 0)
    //    {
    //        var node = q.Dequeue();

    //        // ★ 修正ポイント：リンクが片方向でも双方向扱いにする
    //        foreach (var next in node.links)
    //        {
    //            if (next == null) continue;

    //            if (next.isExcluded && next != goal)
    //            {
    //                Debug.Log($"[BFS][SKIP_EX] {node.name}->{next.name}");
    //                continue;
    //            }
    //            // ★ 追加：除外ノードは経路に含めない
    //            //if (next.isExcluded) continue;
    //            // ★追加：除外ノードは経由しない
    //            if (next.isExcluded && next != goal) continue;

    //            if (!prev.ContainsKey(next))
    //            {
    //                prev[next] = node;
    //                q.Enqueue(next);

    //                if (next == goal)
    //                    return Rebuild(prev, start, goal);
    //            }
    //        }

    //        // ★ 追加：逆リンクも救済（“node → X” ではなく “X → node” のみ存在するケース）
    //        foreach (var other in MapNode.allNodes)
    //        {
    //            if (other.links.Contains(node)) // ← 逆リンク
    //            {
    //                if (next.isExcluded && next != goal)
    //                {
    //                    Debug.Log($"[BFS][SKIP_EX] {node.name}->{next.name}");
    //                    continue;
    //                }
    //                // ★ 追加：除外ノードは経路に含めない
    //                //if (other.isExcluded) continue;
    //                // ★追加：除外ノードは経由しない
    //                if (other.isExcluded && other != goal) continue;

    //                if (!prev.ContainsKey(other))
    //                {
    //                    prev[other] = node;
    //                    q.Enqueue(other);

    //                    if (other == goal)
    //                        return Rebuild(prev, start, goal);
    //                }
    //            }
    //        }
    //    }

    //    return null; // 到達不可
    //}

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

    //private bool IsWall(MapNode node, Vector3 dir)
    //{
    //    Vector3 origin = node.transform.position + Vector3.up * 0.1f;
    //    return Physics.Raycast(origin, dir, cellSize, wallLayer);
    //}
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

    //private bool IsMustPassNode(MapNode node)
    //{
    //    if (node == null) return false;

    //    // (A) MapNode.isMustPass があればそれを使う
    //    if (_fiIsMustPass == null)
    //    {
    //        _fiIsMustPass = typeof(MapNode).GetField("isMustPass",
    //            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    //    }

    //    if (_fiIsMustPass != null && _fiIsMustPass.FieldType == typeof(bool))
    //    {
    //        return (bool)_fiIsMustPass.GetValue(node);
    //    }

    //    // (B) NodePointMarker.IsMustPassCell(Vector2Int) があればそれを使う
    //    if (_miIsMustPassCell == null)
    //    {
    //        _miIsMustPassCell = typeof(NodePointMarker).GetMethod("IsMustPassCell",
    //            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    //    }

    //    if (_miIsMustPassCell != null)
    //    {
    //        object ret = _miIsMustPassCell.Invoke(null, new object[] { node.cell });
    //        if (ret is bool b) return b;
    //    }

    //    return false;
    //}
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

    //public void SetMustPassAtCurrentNode_Damaged()
    //{
    //    // ① 被弾位置（worldPos）
    //    Vector3 hitWorldPos = transform.position;

    //    // ② 計算した cell（Player側と同じ変換）
    //    Vector3 snapped = SnapToGrid(hitWorldPos);
    //    Vector2Int cell = WorldToCell(snapped);

    //    // ③ FindByCell の結果（node名 / null）
    //    MapNode byCell = MapNode.FindByCell(cell);

    //    // ★ここが重要：通路上で被弾すると byCell が null になりやすいので currentNode をフォールバック
    //    MapNode final = (byCell != null) ? byCell : currentNode;

    //    if (debugPointLog)
    //    {
    //        Debug.Log(
    //            $"[MustPass][Damage][IN] " +
    //            $"hitWorldPos={hitWorldPos} snapped={snapped} cell={cell} " +
    //            $"FindByCell={(byCell != null ? byCell.name : "null")} " +
    //            $"final={(final != null ? final.name : "null")}"
    //        );
    //    }

    //    if (final == null) return;
    //    if (final.isExcluded) return;

    //    if (!final.isMustPass)
    //    {
    //        final.isMustPass = true;
    //        if (!final.name.Contains("_MP")) final.name += "_MP";
    //    }

    //    // 自分が自分のMustPassに吸われ続けるのを防ぐ（既存の仕組み）
    //    MarkVisitedMustPass(final);

    //    // ④ 最終的に MustPass にした node名
    //    if (debugPointLog)
    //        Debug.Log($"[MustPass][Damage][OUT] MustPassSet={final.name} cell={final.cell}");
    //}
    //public void SetMustPassAtCurrentNode_Damaged()
    //{
    //    // Playerの「今のセル」を CellFromStart と同じ方式で求める
    //    Vector3 snapped = SnapToGrid(transform.position);
    //    Vector2Int cell = WorldToCell(snapped);

    //    MapNode n = MapNode.FindByCell(cell);
    //    if (n == null) return;

    //    // 念のため：Excluded なら MustPass にしない（好みで外してOK）
    //    if (n.isExcluded) return;

    //    // MustPass を立てる（CellFromStart は MapNode.isMustPass を参照する :contentReference[oaicite:4]{index=4}）
    //    if (!n.isMustPass)
    //    {
    //        n.isMustPass = true;

    //        // 目視確認用（不要なら消してOK）
    //        if (!n.name.Contains("_MP")) n.name += "_MP";
    //    }

    //    // ★ このPlayerは「そのMustPassは経由済み」にしておく
    //    // 　→ 自分が自分のいるNodeに吸われ続けるのを防ぐ（仕組みは既にある :contentReference[oaicite:5]{index=5}）
    //    MarkVisitedMustPass(n);

    //    // 任意ログ
    //    // Debug.Log($"[MustPass][Damage] set true: {n.name} cell={n.cell}");
    //}


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

    //private void ApplyPointInfluence_Once(MapNode node)
    //{
    //    if (node == null) return;

    //    // 事故防止：Goalは除外しない
    //    if (node.CompareTag("Goal")) return;

    //    // node.cell は既に設定されている前提
    //    if (NodePointMarker.IsExcludedCell(node.cell))
    //    {
    //        node.isExcluded = true; // 永続
    //        node.name += "_EX";     // 目視確認用（不要なら消してOK）

    //        if (debugPointLog)
    //            Debug.Log($"[POINT] Excluded set (no-collider): {node.name} cell={node.cell}");
    //    }
    //}
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


///// <summary>
///// CellFromStart（改良版）　B版
///// UnknownCount・DistanceFromStart を用いた探索＋最適化ハイブリッドAI
///// 終端では Unknown最優先＋複数候補なら Distance を採用
///// </summary>
//using UnityEngine;
//using System.Collections.Generic;
//using System.Linq;

//public class CellFromStart : MonoBehaviour
//{
//    [Header("移動設定")]
//    public float moveSpeed = 3f;
//    public float cellSize = 1f;
//    public float rayDistance = 1f;
//    public LayerMask wallLayer;
//    public LayerMask nodeLayer;

//    [Header("初期設定")]
//    public Vector3 startDirection = Vector3.forward;
//    public Vector3 gridOrigin = Vector3.zero;
//    public GameObject nodePrefab;

//    [Header("探索パラメータ")]
//    public int unknownReferenceDepth = 3;

//    [Header("スコア重み")]
//    public float weightUnknown = 1f;
//    public float weightDistance = 1f;

//    [Header("Ray設定")]
//    public int linkRayMaxSteps = 100;

//    [Header("デバッグ")]
//    public bool debugLog = true;
//    public bool debugRay = true;
//    public Renderer bodyRenderer;
//    public Material exploreMaterial;

//    // 内部状態
//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;
//    private MapNode currentNode;

//    private List<MapNode> recentNodes = new List<MapNode>();

//    void Start()
//    {
//        moveDir = startDirection.normalized;
//        transform.position = SnapToGrid(transform.position);
//        targetPos = transform.position;

//        ApplyVisual();

//        currentNode = TryPlaceNode(transform.position);
//        RegisterCurrentNode(currentNode);

//        Log($"Start @ Node={currentNode.name}");
//    }

//    void Update()
//    {
//        if (!isMoving)
//        {
//            if (CanPlaceNodeHere())
//                TryExploreMove();
//            else
//                MoveForward();
//        }
//        else
//        {
//            MoveToTarget();
//        }
//    }

//    private void ApplyVisual()
//    {
//        if (bodyRenderer != null && exploreMaterial != null)
//            bodyRenderer.material = exploreMaterial;
//    }

//    private bool CanPlaceNodeHere()
//    {
//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        bool frontWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
//        bool leftWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
//        bool rightWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

//        int openings = (!frontWall ? 1 : 0) + (!leftWall ? 1 : 0) + (!rightWall ? 1 : 0);

//        return frontWall || openings >= 2;
//    }

//    //private void MoveForward()
//    //{
//    //    targetPos = SnapToGrid(transform.position + moveDir * cellSize);
//    //    isMoving = true;
//    //}
//    private void MoveForward()
//    {
//        Vector3 next = transform.position + moveDir * cellSize;

//        // ★ 壁チェック追加
//        if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
//                            moveDir,
//                            cellSize,
//                            wallLayer))
//        {
//            // 壁なら進まない
//            if (debugLog)
//                Debug.Log("[Block] Wall ahead → stop movement");

//            isMoving = false;
//            return;
//        }

//        targetPos = SnapToGrid(next);
//        isMoving = true;
//    }


//    private void MoveToTarget()
//    {
//        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
//            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//        else
//        {
//            transform.position = targetPos;
//            isMoving = false;
//        }
//    }

//    private void RegisterCurrentNode(MapNode node)
//    {
//        if (node == null) return;

//        if (recentNodes.Count == 0 || recentNodes[^1] != node)
//            recentNodes.Add(node);

//        while (recentNodes.Count > unknownReferenceDepth)
//            recentNodes.RemoveAt(0);
//    }

//    private void TryExploreMove()
//    {
//        MapNode before = currentNode;

//        currentNode = TryPlaceNode(transform.position);
//        Debug.Log($"[TryExploreMove] currentNode = {currentNode?.name} (was {before?.name})");

//        Vector3 back = -moveDir;
//        Debug.Log($"[TryExploreMove] moveDir={moveDir}, backDir={back}");

//        currentNode = TryPlaceNode(transform.position);
//        RegisterCurrentNode(currentNode);

//        Log($"Node placed → {currentNode.name}");

//        // ========== ① 終端Nodeの場合特別処理 ==========
//        if (IsTerminalNode(currentNode))
//        {
//            Vector3? dir = ChooseTerminalDirection(currentNode);
//            if (dir.HasValue)
//            {
//                moveDir = dir.Value;
//                MoveForward();
//            }
//            else
//            {
//                moveDir = moveDir; // fallback
//                MoveForward();
//            }
//            return;
//        }

//        // ========== ② リンクが無い → 未知方向へ ==========
//        if (currentNode.links.Count == 0)
//        {
//            MoveToUnlinked();
//            return;
//        }

//        // ========== ③ 通常：Unknown + Distance のハイブリッドスコア ==========
//        MapNode next = ChooseNextByScore(currentNode);
//        if (next != null)
//        {
//            moveDir = (next.transform.position - transform.position).normalized;
//            MoveForward();
//            return;
//        }

//        // ========== ④ fallback ==========
//        MoveToUnlinked();
//    }

//    //private Vector3? ChooseTerminalDirection(MapNode node)
//    //{
//    //    List<Vector3> dirs = AllMovesExceptBack();

//    //    // リンク方向を除外
//    //    dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

//    //    // 壁方向を除外
//    //    List<Vector3> unknownDirs = dirs.Where(d => !IsWall(node, d)).ToList();

//    //    if (unknownDirs.Count == 0)
//    //        return null;

//    //    if (unknownDirs.Count == 1)
//    //        return unknownDirs[0];

//    //    // 複数ある場合 → DistanceFromStart を使う
//    //    int bestScore = int.MinValue;
//    //    Vector3 best = unknownDirs[0];

//    //    // ★ Distance候補にも壁チェックを追加
//    //    unknownDirs = unknownDirs.Where(d => !IsWall(node, d)).ToList();

//    //    foreach (var d in unknownDirs)
//    //    {
//    //        Vector2Int cell = WorldToCell(node.transform.position + d * cellSize);
//    //        MapNode near = MapNode.FindByCell(cell);
//    //        if (near == null) continue;

//    //        int score = -near.distanceFromStart; // Startから遠いほど高評価
//    //        if (score > bestScore)
//    //        {
//    //            bestScore = score;
//    //            best = d;
//    //        }
//    //    }

//    //    return best;
//    //}
//    private Vector3? ChooseTerminalDirection(MapNode node)
//    {
//        List<Vector3> dirs = AllMovesExceptBack();

//        // リンク方向を除外
//        dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

//        // 壁方向を除外
//        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

//        if (dirs.Count == 0)
//            return null;

//        if (dirs.Count == 1)
//            return dirs[0];

//        // ★Distanceスコア決定にも壁チェックを入れる
//        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

//        int bestScore = int.MinValue;
//        Vector3 bestDir = dirs[0];

//        foreach (var d in dirs)
//        {
//            Vector2Int cell = WorldToCell(node.transform.position + d * cellSize);
//            MapNode near = MapNode.FindByCell(cell);
//            if (near == null) continue;

//            int score = -near.distanceFromStart;
//            if (score > bestScore)
//            {
//                bestScore = score;
//                bestDir = d;
//            }
//        }

//        return bestDir;
//    }


//    private bool IsTerminalNode(MapNode node)
//        => node != null && node.links.Count == 1;

//    //private MapNode ChooseNextByScore(MapNode current)
//    //{
//    //    // 履歴中の未知方向が最も多いNodeが current なら未知探索へ
//    //    if (recentNodes.Count > 0)
//    //    {
//    //        MapNode best = recentNodes.OrderByDescending(n => n.unknownCount).First();
//    //        if (best == current)
//    //            return null;
//    //    }

//    //    return current.links
//    //        .OrderByDescending(n => Score(n))
//    //        .ThenBy(_ => Random.value)
//    //        .FirstOrDefault();
//    //}
//    //private MapNode ChooseNextByScore(MapNode current)
//    //{
//    //    // 履歴のunknownが最大で current と同じなら未知方向へ
//    //    if (recentNodes.Count > 0)
//    //    {
//    //        MapNode best = recentNodes.OrderByDescending(n => n.unknownCount).First();
//    //        if (best == current)
//    //            return null; // 未知方向優先
//    //    }

//    //    // ★リンク方向でも必ず壁チェックする
//    //    var candidates = current.links
//    //        .Where(n => n != null)
//    //        .Where(n =>
//    //        {
//    //            Vector3 dir = (n.transform.position - current.transform.position).normalized;
//    //            return !IsWall(current, dir);
//    //        })
//    //        .ToList();

//    //    if (candidates.Count == 0)
//    //        return null;

//    //    return candidates
//    //        .OrderByDescending(n => Score(n))
//    //        .ThenBy(_ => Random.value)
//    //        .FirstOrDefault();
//    //}
//    private MapNode ChooseNextByScore(MapNode current)
//    {
//        //Debug.Log($"[Score] ----------");
//        Debug.Log($"[Score] currentNode = {current.name}");
//        Debug.Log($"[Score] recentNodes = {string.Join(", ", recentNodes.Select(n => n.name))}");

//        // 履歴のunknownが最大で current と同じなら未知方向へ
//        if (recentNodes.Count > 0)
//        {
//            MapNode bestHist = recentNodes.OrderByDescending(n => n.unknownCount).First();
//            Debug.Log($"[Score] bestHist = {bestHist.name}, U={bestHist.unknownCount}");

//            if (bestHist == current)
//            {
//                Debug.Log($"[Score] → 履歴の最大未知数が current と一致 → 未知方向探索に切り替え（return null）");
//                return null; // 未知方向優先
//            }
//        }

//        // --------------------------
//        // ★ 壁チェック込みのリンク候補抽出
//        // --------------------------

//        List<(MapNode node, Vector3 dir, bool isWall, float score)> logs
//            = new List<(MapNode, Vector3, bool, float)>();

//        foreach (var n in current.links)
//        {
//            if (n == null) continue;

//            Vector3 dir = (n.transform.position - current.transform.position).normalized;
//            bool wall = IsWall(current, dir);
//            float sc = Score(n);

//            logs.Add((n, dir, wall, sc));
//        }

//        foreach (var L in logs)
//        {
//            Debug.Log($"[Score] link: {L.node.name}, dir={L.dir}, isWall={L.isWall}, score={L.score}");
//        }

//        var candidates = logs
//            .Where(L => !L.isWall)
//            .Select(L => L.node)
//            .ToList();

//        Debug.Log($"[Score] candidates = {string.Join(", ", candidates.Select(n => n.name))}");

//        if (candidates.Count == 0)
//        {
//            Debug.Log("[Score] → 候補ゼロ → return null");
//            return null;
//        }

//        var selected = candidates
//            .OrderByDescending(n => Score(n))
//            .ThenBy(_ => Random.value)
//            .FirstOrDefault();

//        Debug.Log($"[Score] SELECTED = {selected.name}");
//        return selected;
//    }
//    //private MapNode ChooseNextByScore(MapNode current)
//    //{
//    //    Debug.Log($"[Score] currentNode = {current.name}");
//    //    Debug.Log($"[Score] recentNodes = {string.Join(", ", recentNodes.Select(n => n.name))}");

//    //    // ======================================================
//    //    // ① 履歴（recentNodes）の中で unknownCount が最大の Node を調べる
//    //    // ======================================================
//    //    if (recentNodes.Count > 0)
//    //    {
//    //        MapNode bestHist = recentNodes
//    //            .OrderByDescending(n => n.unknownCount)
//    //            .First();

//    //        Debug.Log($"[Score] bestHist = {bestHist.name}, U={bestHist.unknownCount}");

//    //        // → 同じなら未知方向へ（＝リンク以外を見るため return null）
//    //        if (bestHist == current)
//    //        {
//    //            Debug.Log("[Score] → 履歴最大未知数が current と一致 → 未知方向探索（return null）");
//    //            return null;
//    //        }
//    //    }

//    //    // ======================================================
//    //    // ② リンク方向のログ収集（壁判定もログに記録）
//    //    // ======================================================
//    //    List<(MapNode node, Vector3 dir, bool isWall, float score)> logs
//    //        = new();

//    //    foreach (var n in current.links)
//    //    {
//    //        if (n == null) continue;

//    //        Vector3 dir = (n.transform.position - current.transform.position).normalized;

//    //        bool wall = IsWall(current, dir);
//    //        float sc = Score(n);

//    //        logs.Add((n, dir, wall, sc));
//    //    }

//    //    foreach (var L in logs)
//    //    {
//    //        Debug.Log($"[Score] link: {L.node.name}, dir={L.dir}, isWall={L.isWall}, score={L.score}");
//    //    }

//    //    // ======================================================
//    //    // ③ 候補抽出：壁方向・背後方向(prevNode) を除外
//    //    // ======================================================

//    //    MapNode prevNode = (recentNodes.Count >= 2 ? recentNodes[^2] : null);

//    //    var candidates = logs
//    //        .Where(L => !L.isWall)              // ★ 壁方向は除外
//    //        //.Where(L => L.node != prevNode)     // ★ 背後の Node を除外
//    //        .Select(L => L.node)
//    //        .ToList();

//    //    Debug.Log($"[Score] candidates = {string.Join(", ", candidates.Select(n => n.name))}");

//    //    // 候補が無い場合 → 未知方向へ移行
//    //    if (candidates.Count == 0)
//    //    {
//    //        Debug.Log("[Score] → 候補ゼロ → return null");
//    //        return null;
//    //    }

//    //    // ======================================================
//    //    // ④ 評価の高いリンク方向へ進む
//    //    // ======================================================
//    //    var selected = candidates
//    //        .OrderByDescending(n => Score(n))
//    //        .ThenBy(_ => Random.value)   // スコアが同じときランダム
//    //        .First();

//    //    Debug.Log($"[Score] SELECTED = {selected.name}");
//    //    return selected;
//    //}


//    private float Score(MapNode n)
//    {
//        float u = n.unknownCount;
//        float d = n.distanceFromStart;

//        return weightUnknown * u + weightDistance * (-d);
//    }

//    private void MoveToUnlinked()
//    {
//        List<Vector3> dirs = AllMovesExceptBack();

//        dirs = dirs.Where(d => !IsLinkedDirection(currentNode, d)).ToList();
//        dirs = dirs.Where(d => !IsWall(currentNode, d)).ToList();

//        if (dirs.Count == 0)
//        {
//            // 仕方なく戻る
//            moveDir = -moveDir;
//            MoveForward();
//            return;
//        }

//        moveDir = dirs[Random.Range(0, dirs.Count)];
//        MoveForward();
//    }

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
//        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//        return Physics.Raycast(origin, dir, cellSize, wallLayer);
//    }
//    //private bool IsWall(MapNode from, Vector3 dir)
//    //{
//    //    Vector3 origin = from.transform.position + Vector3.up;

//    //    float dist = cellSize * 0.9f;

//    //    if (Physics.Raycast(origin, dir, dist, wallLayer))
//    //        return true;

//    //    return false;
//    //}



//    //private MapNode TryPlaceNode(Vector3 pos)
//    //{
//    //    Vector2Int cell = WorldToCell(SnapToGrid(pos));
//    //    MapNode node;

//    //    if (MapNode.allNodeCells.Contains(cell))
//    //        node = MapNode.FindByCell(cell);
//    //    else
//    //    {
//    //        GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//    //        node = obj.GetComponent<MapNode>();
//    //        node.cell = cell;
//    //        MapNode.allNodeCells.Add(cell);
//    //    }

//    //    if (MapNode.StartNode == null)
//    //    {
//    //        MapNode.StartNode = node;
//    //        node.distanceFromStart = 0;
//    //    }

//    //    LinkBackward(node);

//    //    return node;
//    //}
//    private MapNode TryPlaceNode(Vector3 pos)
//    {
//        Vector3 snapped = SnapToGrid(pos);
//        Vector2Int cell = WorldToCell(snapped);

//        Debug.Log($"[TryPlaceNode] pos={pos}, snapped={snapped}, cell={cell}");

//        // 壁チェック
//        bool isWall = Physics.Raycast(snapped + Vector3.up * 0.1f, Vector3.down, 1f, wallLayer);
//        Debug.Log($"[TryPlaceNode] isWall={isWall}");

//        if (isWall)
//        {
//            MapNode exist = MapNode.FindByCell(cell);
//            Debug.Log($"[TryPlaceNode] WALL → existing={exist}");
//            return exist;
//        }

//        MapNode node;

//        if (MapNode.allNodeCells.Contains(cell))
//        {
//            node = MapNode.FindByCell(cell);
//            Debug.Log($"[TryPlaceNode] Reuse node={node.name}");
//        }
//        else
//        {
//            Debug.Log($"[TryPlaceNode] New Node @ {cell}");
//            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//            node = obj.GetComponent<MapNode>();
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//        }

//        Debug.Log($"[TryPlaceNode] RETURN node={node.name}");

//        LinkBackward(node);
//        return node;
//    }


//    private void LinkBackward(MapNode node)
//    {
//        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//        Vector3 dir = -moveDir;

//        LayerMask mask = wallLayer | nodeLayer;

//        for (int i = 1; i <= linkRayMaxSteps; i++)
//        {
//            float dist = cellSize * i;

//            if (debugRay)
//                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);

//            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
//            {
//                int layer = hit.collider.gameObject.layer;

//                if ((wallLayer.value & (1 << layer)) != 0)
//                    return;

//                if ((nodeLayer.value & (1 << layer)) != 0)
//                {
//                    var hitNode = hit.collider.GetComponent<MapNode>();
//                    if (hitNode != null && hitNode != node)
//                    {
//                        node.AddLink(hitNode);
//                        node.RecalculateUnknownAndWall();
//                        hitNode.RecalculateUnknownAndWall();
//                    }
//                    return;
//                }
//            }
//        }
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
//        if (debugLog) Debug.Log("[CellFS] " + msg);
//    }
//}

































//using UnityEngine;
//using System.Collections.Generic;
//using System.Linq;

//public class CellFromStart : MonoBehaviour
//{
//    [Header("移動設定")]
//    public float moveSpeed = 3f;
//    public float cellSize = 1f;
//    public float rayDistance = 1f;
//    public LayerMask wallLayer;
//    public LayerMask nodeLayer;

//    [Header("初期設定")]
//    public Vector3 startDirection;
//    public Vector3 gridOrigin = Vector3.zero;
//    public GameObject nodePrefab;

//    [Header("探索パラメータ")]
//    public int unknownReferenceDepth = 3; // ★ BFS探索深さとして使用

//    [Header("スコア重み（A：現状の式）")]
//    public float weightUnknown = 1f;
//    public float weightDistance = 1f;

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

//    // ★ 追加：今の lastBestTarget が「Start最遠由来」かどうか
//    private bool lastTargetIsFarthest = false;

//    [Header("循環回避：Unknown=0 フォールバックのクールダウン")]
//    public int fallbackCooldownSteps = 6;  // 例：6歩の間、同じノードをフォールバック候補にしない

//    // MapNode → 「このstepIndex未満なら選ばない」
//    private Dictionary<MapNode, int> fallbackCooldownUntil = new Dictionary<MapNode, int>();

//    // ★ Unknown=0 フェーズで既に選んだフォールバックターゲット
//    private HashSet<MapNode> usedFallbackTargets = new HashSet<MapNode>();

//    // ★ Playerごとの寿命設定：新規Nodeを何個置いたら消えるか
//    [Header("寿命設定")]
//    [Tooltip("このプレイヤーが新規に設置できるNode数の上限")]
//    public int destroyAfterNewNodes;

//    // ★ 今までにこのPlayerが新規に作ったNode数
//    private int newNodeCreatedCount = 0;

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

//    //void Start()
//    //{
//    //    moveDir = startDirection.normalized;
//    //    transform.position = SnapToGrid(transform.position);
//    //    targetPos = transform.position;

//    //    ApplyVisual();

//    //    currentNode = TryPlaceNode(transform.position);
//    //    if (MapNode.StartNode == null)
//    //    {
//    //        MapNode.StartNode = currentNode;
//    //        currentNode.distanceFromStart = 0;
//    //    }

//    //    RegisterCurrentNode(currentNode);

//    //    Log($"Start @ Node={currentNode.name}");
//    //}
//    void Start()
//    {
//        playerId = nextPlayerId;
//        nextPlayerId++;
//        gameObject.name = $"Player_{playerId}";

//        moveDir = startDirection.normalized;

//        // プレイヤー座標をスナップ
//        Vector3 snapped = SnapToGrid(transform.position);
//        transform.position = snapped;
//        targetPos = snapped;

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

//    void Update()
//    {
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

//    //private bool IsExactlyOnNodeCenter()
//    //{
//    //    Vector3 snapped = SnapToGrid(transform.position);
//    //    return Vector3.Distance(transform.position, snapped) < 0.05f;
//    //}

//    private void ApplyVisual()
//    {
//        if (bodyRenderer != null && exploreMaterial != null)
//            bodyRenderer.material = exploreMaterial;
//    }

//    //private bool CanPlaceNodeHere()
//    //{
//    //    Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//    //    Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//    //    bool frontWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
//    //    bool leftWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
//    //    bool rightWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

//    //    int openings = (!frontWall ? 1 : 0) + (!leftWall ? 1 : 0) + (!rightWall ? 1 : 0);

//    //    return frontWall || openings >= 2;
//    //}
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

//    //private void MoveForward()
//    //{
//    //    Vector3 next = transform.position + moveDir * cellSize;

//    //    // 壁チェック
//    //    if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
//    //                        moveDir,
//    //                        cellSize,
//    //                        wallLayer))
//    //    {
//    //        if (debugLog)
//    //            //Debug.Log("[Block] Wall ahead → stop movement");

//    //        isMoving = false;
//    //        return;
//    //    }

//    //    targetPos = SnapToGrid(next);
//    //    isMoving = true;

//    //    // ★ 評価ログ：1マス分の移動が確定したので歩数を加算
//    //    stepsWalked++;
//    //}
//    private void MoveForward()
//    {
//        Vector3 next = transform.position + moveDir * cellSize;

//        // 壁チェック
//        if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
//                            moveDir,
//                            cellSize,
//                            wallLayer))
//        {
//            if (debugLog)
//                ; // Debug.Log("[Block] Wall ahead → stop movement");

//            isMoving = false;
//            return;
//        }

//        Vector3 nextSnap = SnapToGrid(next);
//        Vector2Int nextCell = WorldToCell(nextSnap);

//        // ★ 次セルに「既存の Excluded Node があるか？」だけを見る
//        MapNode nextNode = MapNode.FindByCell(nextCell);

//        if (nextNode != null && nextNode.isExcluded)
//        {
//            if (debugLog)
//                Debug.Log($"[MOVE][BLOCK_EX_NODE] {currentNode?.name} -> {nextNode.name}");

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

//        if (recentNodes.Count == 0 || recentNodes[^1] != node)
//            recentNodes.Add(node);

//        while (recentNodes.Count > unknownReferenceDepth)
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

//        // ① 現在の Node に Unknown が残っているなら、まずその場で掘る
//        //if (currentNode.unknownCount > 0)
//        //{
//        //    Vector3? localUnknownDir = currentNode.GetUnknownDirection();
//        //    if (localUnknownDir.HasValue)
//        //    {
//        //        // ここで一旦ターゲットをリセット
//        //        lastBestTarget = null;
//        //        lastTargetIsFarthest = false;

//        //        moveDir = localUnknownDir.Value.normalized;
//        //        MoveForward();
//        //        return;
//        //    }
//        //}
//        Vector3? localUnknownDir = GetLocalUnknownDirection(currentNode);
//        if (localUnknownDir.HasValue)
//        {
//            lastBestTarget = null;
//            lastTargetIsFarthest = false;

//            moveDir = localUnknownDir.Value.normalized;
//            MoveForward();
//            return;
//        }

//        // 近場探索（リンクBFS）
//        var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
//        var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

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
//            var path = BuildShortestPath(currentNode, lastBestTarget);

//            if (path != null && path.Count >= 2)
//            {
//                MapNode nextNode = path[1];
//                Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
//                dir.y = 0;

//                moveDir = dir;
//                MoveForward();
//                return;
//            }
//            else
//            {
//                lastBestTarget = null;
//                lastTargetIsFarthest = false;

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
//                            n.unknownCount > 0)          // ★ここが最重要：unknown>0 のみ
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
//        var path2 = BuildShortestPath(currentNode, bestTarget);

//        if (path2 != null)
//        {
//            var ex = path2.FirstOrDefault(n => n != null && n.isExcluded);
//            if (ex != null) Debug.Log($"[PATH][HAS_EX] target={bestTarget?.name} ex={ex.name} len={path2.Count}");
//            else Debug.Log($"[PATH][OK] target={bestTarget?.name} len={path2.Count}");
//        }
//        else
//        {
//            Debug.Log($"[PATH][NULL] target={bestTarget?.name}");
//        }


//        if (path2 == null || path2.Count < 2)
//        {
//            moveDir = ChooseRandomValidDirection(currentNode).Value;
//            MoveForward();
//            return;
//        }

//        MapNode nextNode2 = path2[1];
//        Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
//        nextDir.y = 0;

//        moveDir = nextDir;

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

//    // ★ Unknown方向を選ぶ（Excluded Node 方向は「壁扱い」で除外）
//    private Vector3? GetLocalUnknownDirection(MapNode node)
//    {
//        if (node == null) return null;

//        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//        foreach (var dir in dirs)
//        {
//            // 既にリンクがあるなら Unknown ではない
//            if (IsLinkedDirection(node, dir))
//                continue;

//            // 壁 or Excluded Node なら掘れない（Unknownにしない）
//            if (IsWall(node, dir))
//                continue;

//            return dir;
//        }

//        return null;
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
//    //private Vector3? ChooseRandomValidDirection(MapNode node)
//    //{
//    //    List<Vector3> dirs = new()
//    //    {
//    //        Vector3.forward,
//    //        Vector3.back,
//    //        Vector3.left,
//    //        Vector3.right
//    //    };

//    //    // ★背後方向を除外（無限ループ防止）
//    //    Vector3 backDir = -moveDir;
//    //    dirs = dirs.Where(d => Vector3.Dot(d.normalized, backDir.normalized) < 0.7f).ToList();

//    //    // ★リンク方向は除外
//    //    dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

//    //    // ★壁方向も除外
//    //    dirs = dirs.Where(d => !IsWall(node, d)).ToList();

//    //    if (dirs.Count == 0)
//    //        return null;

//    //    return dirs[Random.Range(0, dirs.Count)];
//    //}
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
//    //private Vector3? ChooseTerminalDirection(MapNode node)
//    //{
//    //    List<Vector3> dirs = AllMovesExceptBack();

//    //    dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();
//    //    dirs = dirs.Where(d => !IsWall(node, d)).ToList();

//    //    if (dirs.Count == 0) return null;
//    //    if (dirs.Count == 1) return dirs[0];

//    //    return dirs[Random.Range(0, dirs.Count)];
//    //}
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


//    //private float Score(MapNode n)
//    //{
//    //    float u = n.unknownCount;
//    //    float d = n.distanceFromStart;

//    //    return weightUnknown * u + weightDistance * (-d);
//    //}
//    private float Score(MapNode n)
//    {
//        // Unknown 優先、Unknown=0 なら距離で評価
//        return weightUnknown * n.unknownCount
//             + weightDistance * (-n.distanceFromStart);
//    }


//    // =============================
//    // ★ リンクを使った最短ルートBFS
//    // =============================
//    private List<MapNode> BuildShortestPath(MapNode start, MapNode goal)
//    {
//        if (start == null || goal == null) return null;
//        if (start == goal) return new List<MapNode>() { start };

//        Queue<MapNode> q = new Queue<MapNode>();
//        Dictionary<MapNode, MapNode> prev = new Dictionary<MapNode, MapNode>();

//        q.Enqueue(start);
//        prev[start] = null;

//        int expanded = 0;
//        int skipEx = 0;

//        while (q.Count > 0)
//        {
//            var node = q.Dequeue();
//            expanded++;

//            // --- 正リンク展開 ---
//            foreach (var next in node.links)
//            {
//                if (next == null) continue;

//                // ★除外ノードは経由しない（goal は事故防止で例外）
//                if (next.isExcluded && next != goal)
//                {
//                    skipEx++;
//                    // ログ出しすぎ防止：たまにだけ
//                    if (dbgExclude && (Time.frameCount % dbgEveryNFrames == 0))
//                        Debug.Log($"[BFS][SKIP_EX] {node.name}->{next.name}");
//                    continue;
//                }

//                if (!prev.ContainsKey(next))
//                {
//                    prev[next] = node;
//                    q.Enqueue(next);

//                    if (next == goal)
//                    {
//                        if (dbgExclude)
//                            Debug.Log($"[BFS][FOUND] expanded={expanded} skipEx={skipEx} start={start.name} goal={goal.name}");
//                        return Rebuild(prev, start, goal);
//                    }
//                }
//            }

//            // --- 逆リンク救済展開（あなたの仕様） ---
//            foreach (var other in MapNode.allNodes)
//            {
//                if (other == null) continue;

//                if (other.links.Contains(node))
//                {
//                    // ★ここは other を見る（next は存在しない）
//                    if (other.isExcluded && other != goal)
//                    {
//                        skipEx++;
//                        if (dbgExclude && (Time.frameCount % dbgEveryNFrames == 0))
//                            Debug.Log($"[BFS][SKIP_EX_REV] {node.name}<-{other.name}");
//                        continue;
//                    }

//                    if (!prev.ContainsKey(other))
//                    {
//                        prev[other] = node;
//                        q.Enqueue(other);

//                        if (other == goal)
//                        {
//                            if (dbgExclude)
//                                Debug.Log($"[BFS][FOUND_REV] expanded={expanded} skipEx={skipEx} start={start.name} goal={goal.name}");
//                            return Rebuild(prev, start, goal);
//                        }
//                    }
//                }
//            }
//        }

//        if (dbgExclude)
//            Debug.Log($"[BFS][FAIL] expanded={expanded} skipEx={skipEx} start={start.name} goal={goal.name}");

//        return null;
//    }

//    //private List<MapNode> BuildShortestPath(MapNode start, MapNode goal)
//    //{
//    //    if (start == null || goal == null) return null;
//    //    if (start == goal) return new List<MapNode>() { start };

//    //    Queue<MapNode> q = new Queue<MapNode>();
//    //    Dictionary<MapNode, MapNode> prev = new Dictionary<MapNode, MapNode>();

//    //    q.Enqueue(start);
//    //    prev[start] = null;

//    //    while (q.Count > 0)
//    //    {
//    //        var node = q.Dequeue();

//    //        // ★ 修正ポイント：リンクが片方向でも双方向扱いにする
//    //        foreach (var next in node.links)
//    //        {
//    //            if (next == null) continue;

//    //            if (next.isExcluded && next != goal)
//    //            {
//    //                Debug.Log($"[BFS][SKIP_EX] {node.name}->{next.name}");
//    //                continue;
//    //            }
//    //            // ★ 追加：除外ノードは経路に含めない
//    //            //if (next.isExcluded) continue;
//    //            // ★追加：除外ノードは経由しない
//    //            if (next.isExcluded && next != goal) continue;

//    //            if (!prev.ContainsKey(next))
//    //            {
//    //                prev[next] = node;
//    //                q.Enqueue(next);

//    //                if (next == goal)
//    //                    return Rebuild(prev, start, goal);
//    //            }
//    //        }

//    //        // ★ 追加：逆リンクも救済（“node → X” ではなく “X → node” のみ存在するケース）
//    //        foreach (var other in MapNode.allNodes)
//    //        {
//    //            if (other.links.Contains(node)) // ← 逆リンク
//    //            {
//    //                if (next.isExcluded && next != goal)
//    //                {
//    //                    Debug.Log($"[BFS][SKIP_EX] {node.name}->{next.name}");
//    //                    continue;
//    //                }
//    //                // ★ 追加：除外ノードは経路に含めない
//    //                //if (other.isExcluded) continue;
//    //                // ★追加：除外ノードは経由しない
//    //                if (other.isExcluded && other != goal) continue;

//    //                if (!prev.ContainsKey(other))
//    //                {
//    //                    prev[other] = node;
//    //                    q.Enqueue(other);

//    //                    if (other == goal)
//    //                        return Rebuild(prev, start, goal);
//    //                }
//    //            }
//    //        }
//    //    }

//    //    return null; // 到達不可
//    //}

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

//    //private bool IsWall(MapNode node, Vector3 dir)
//    //{
//    //    Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//    //    return Physics.Raycast(origin, dir, cellSize, wallLayer);
//    //}
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
//        if (NodePointMarker.IsExcludedCell(node.cell))
//        {
//            node.isExcluded = true; // 永続
//            node.name += "_EX";     // 目視確認用（不要なら消してOK）

//            if (debugPointLog)
//                Debug.Log($"[POINT] Excluded set (no-collider): {node.name} cell={node.cell}");
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


/////// <summary>
/////// CellFromStart（改良版）　B版
/////// UnknownCount・DistanceFromStart を用いた探索＋最適化ハイブリッドAI
/////// 終端では Unknown最優先＋複数候補なら Distance を採用
/////// </summary>
////using UnityEngine;
////using System.Collections.Generic;
////using System.Linq;

////public class CellFromStart : MonoBehaviour
////{
////    [Header("移動設定")]
////    public float moveSpeed = 3f;
////    public float cellSize = 1f;
////    public float rayDistance = 1f;
////    public LayerMask wallLayer;
////    public LayerMask nodeLayer;

////    [Header("初期設定")]
////    public Vector3 startDirection = Vector3.forward;
////    public Vector3 gridOrigin = Vector3.zero;
////    public GameObject nodePrefab;

////    [Header("探索パラメータ")]
////    public int unknownReferenceDepth = 3;

////    [Header("スコア重み")]
////    public float weightUnknown = 1f;
////    public float weightDistance = 1f;

////    [Header("Ray設定")]
////    public int linkRayMaxSteps = 100;

////    [Header("デバッグ")]
////    public bool debugLog = true;
////    public bool debugRay = true;
////    public Renderer bodyRenderer;
////    public Material exploreMaterial;

////    // 内部状態
////    private Vector3 moveDir;
////    private bool isMoving = false;
////    private Vector3 targetPos;
////    private MapNode currentNode;

////    private List<MapNode> recentNodes = new List<MapNode>();

////    void Start()
////    {
////        moveDir = startDirection.normalized;
////        transform.position = SnapToGrid(transform.position);
////        targetPos = transform.position;

////        ApplyVisual();

////        currentNode = TryPlaceNode(transform.position);
////        RegisterCurrentNode(currentNode);

////        Log($"Start @ Node={currentNode.name}");
////    }

////    void Update()
////    {
////        if (!isMoving)
////        {
////            if (CanPlaceNodeHere())
////                TryExploreMove();
////            else
////                MoveForward();
////        }
////        else
////        {
////            MoveToTarget();
////        }
////    }

////    private void ApplyVisual()
////    {
////        if (bodyRenderer != null && exploreMaterial != null)
////            bodyRenderer.material = exploreMaterial;
////    }

////    private bool CanPlaceNodeHere()
////    {
////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////        bool frontWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
////        bool leftWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
////        bool rightWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

////        int openings = (!frontWall ? 1 : 0) + (!leftWall ? 1 : 0) + (!rightWall ? 1 : 0);

////        return frontWall || openings >= 2;
////    }

////    //private void MoveForward()
////    //{
////    //    targetPos = SnapToGrid(transform.position + moveDir * cellSize);
////    //    isMoving = true;
////    //}
////    private void MoveForward()
////    {
////        Vector3 next = transform.position + moveDir * cellSize;

////        // ★ 壁チェック追加
////        if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
////                            moveDir,
////                            cellSize,
////                            wallLayer))
////        {
////            // 壁なら進まない
////            if (debugLog)
////                Debug.Log("[Block] Wall ahead → stop movement");

////            isMoving = false;
////            return;
////        }

////        targetPos = SnapToGrid(next);
////        isMoving = true;
////    }


////    private void MoveToTarget()
////    {
////        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
////            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////        else
////        {
////            transform.position = targetPos;
////            isMoving = false;
////        }
////    }

////    private void RegisterCurrentNode(MapNode node)
////    {
////        if (node == null) return;

////        if (recentNodes.Count == 0 || recentNodes[^1] != node)
////            recentNodes.Add(node);

////        while (recentNodes.Count > unknownReferenceDepth)
////            recentNodes.RemoveAt(0);
////    }

////    private void TryExploreMove()
////    {
////        MapNode before = currentNode;

////        currentNode = TryPlaceNode(transform.position);
////        Debug.Log($"[TryExploreMove] currentNode = {currentNode?.name} (was {before?.name})");

////        Vector3 back = -moveDir;
////        Debug.Log($"[TryExploreMove] moveDir={moveDir}, backDir={back}");

////        currentNode = TryPlaceNode(transform.position);
////        RegisterCurrentNode(currentNode);

////        Log($"Node placed → {currentNode.name}");

////        // ========== ① 終端Nodeの場合特別処理 ==========
////        if (IsTerminalNode(currentNode))
////        {
////            Vector3? dir = ChooseTerminalDirection(currentNode);
////            if (dir.HasValue)
////            {
////                moveDir = dir.Value;
////                MoveForward();
////            }
////            else
////            {
////                moveDir = moveDir; // fallback
////                MoveForward();
////            }
////            return;
////        }

////        // ========== ② リンクが無い → 未知方向へ ==========
////        if (currentNode.links.Count == 0)
////        {
////            MoveToUnlinked();
////            return;
////        }

////        // ========== ③ 通常：Unknown + Distance のハイブリッドスコア ==========
////        MapNode next = ChooseNextByScore(currentNode);
////        if (next != null)
////        {
////            moveDir = (next.transform.position - transform.position).normalized;
////            MoveForward();
////            return;
////        }

////        // ========== ④ fallback ==========
////        MoveToUnlinked();
////    }

////    //private Vector3? ChooseTerminalDirection(MapNode node)
////    //{
////    //    List<Vector3> dirs = AllMovesExceptBack();

////    //    // リンク方向を除外
////    //    dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

////    //    // 壁方向を除外
////    //    List<Vector3> unknownDirs = dirs.Where(d => !IsWall(node, d)).ToList();

////    //    if (unknownDirs.Count == 0)
////    //        return null;

////    //    if (unknownDirs.Count == 1)
////    //        return unknownDirs[0];

////    //    // 複数ある場合 → DistanceFromStart を使う
////    //    int bestScore = int.MinValue;
////    //    Vector3 best = unknownDirs[0];

////    //    // ★ Distance候補にも壁チェックを追加
////    //    unknownDirs = unknownDirs.Where(d => !IsWall(node, d)).ToList();

////    //    foreach (var d in unknownDirs)
////    //    {
////    //        Vector2Int cell = WorldToCell(node.transform.position + d * cellSize);
////    //        MapNode near = MapNode.FindByCell(cell);
////    //        if (near == null) continue;

////    //        int score = -near.distanceFromStart; // Startから遠いほど高評価
////    //        if (score > bestScore)
////    //        {
////    //            bestScore = score;
////    //            best = d;
////    //        }
////    //    }

////    //    return best;
////    //}
////    private Vector3? ChooseTerminalDirection(MapNode node)
////    {
////        List<Vector3> dirs = AllMovesExceptBack();

////        // リンク方向を除外
////        dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

////        // 壁方向を除外
////        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

////        if (dirs.Count == 0)
////            return null;

////        if (dirs.Count == 1)
////            return dirs[0];

////        // ★Distanceスコア決定にも壁チェックを入れる
////        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

////        int bestScore = int.MinValue;
////        Vector3 bestDir = dirs[0];

////        foreach (var d in dirs)
////        {
////            Vector2Int cell = WorldToCell(node.transform.position + d * cellSize);
////            MapNode near = MapNode.FindByCell(cell);
////            if (near == null) continue;

////            int score = -near.distanceFromStart;
////            if (score > bestScore)
////            {
////                bestScore = score;
////                bestDir = d;
////            }
////        }

////        return bestDir;
////    }


////    private bool IsTerminalNode(MapNode node)
////        => node != null && node.links.Count == 1;

////    //private MapNode ChooseNextByScore(MapNode current)
////    //{
////    //    // 履歴中の未知方向が最も多いNodeが current なら未知探索へ
////    //    if (recentNodes.Count > 0)
////    //    {
////    //        MapNode best = recentNodes.OrderByDescending(n => n.unknownCount).First();
////    //        if (best == current)
////    //            return null;
////    //    }

////    //    return current.links
////    //        .OrderByDescending(n => Score(n))
////    //        .ThenBy(_ => Random.value)
////    //        .FirstOrDefault();
////    //}
////    //private MapNode ChooseNextByScore(MapNode current)
////    //{
////    //    // 履歴のunknownが最大で current と同じなら未知方向へ
////    //    if (recentNodes.Count > 0)
////    //    {
////    //        MapNode best = recentNodes.OrderByDescending(n => n.unknownCount).First();
////    //        if (best == current)
////    //            return null; // 未知方向優先
////    //    }

////    //    // ★リンク方向でも必ず壁チェックする
////    //    var candidates = current.links
////    //        .Where(n => n != null)
////    //        .Where(n =>
////    //        {
////    //            Vector3 dir = (n.transform.position - current.transform.position).normalized;
////    //            return !IsWall(current, dir);
////    //        })
////    //        .ToList();

////    //    if (candidates.Count == 0)
////    //        return null;

////    //    return candidates
////    //        .OrderByDescending(n => Score(n))
////    //        .ThenBy(_ => Random.value)
////    //        .FirstOrDefault();
////    //}
////    private MapNode ChooseNextByScore(MapNode current)
////    {
////        //Debug.Log($"[Score] ----------");
////        Debug.Log($"[Score] currentNode = {current.name}");
////        Debug.Log($"[Score] recentNodes = {string.Join(", ", recentNodes.Select(n => n.name))}");

////        // 履歴のunknownが最大で current と同じなら未知方向へ
////        if (recentNodes.Count > 0)
////        {
////            MapNode bestHist = recentNodes.OrderByDescending(n => n.unknownCount).First();
////            Debug.Log($"[Score] bestHist = {bestHist.name}, U={bestHist.unknownCount}");

////            if (bestHist == current)
////            {
////                Debug.Log($"[Score] → 履歴の最大未知数が current と一致 → 未知方向探索に切り替え（return null）");
////                return null; // 未知方向優先
////            }
////        }

////        // --------------------------
////        // ★ 壁チェック込みのリンク候補抽出
////        // --------------------------

////        List<(MapNode node, Vector3 dir, bool isWall, float score)> logs
////            = new List<(MapNode, Vector3, bool, float)>();

////        foreach (var n in current.links)
////        {
////            if (n == null) continue;

////            Vector3 dir = (n.transform.position - current.transform.position).normalized;
////            bool wall = IsWall(current, dir);
////            float sc = Score(n);

////            logs.Add((n, dir, wall, sc));
////        }

////        foreach (var L in logs)
////        {
////            Debug.Log($"[Score] link: {L.node.name}, dir={L.dir}, isWall={L.isWall}, score={L.score}");
////        }

////        var candidates = logs
////            .Where(L => !L.isWall)
////            .Select(L => L.node)
////            .ToList();

////        Debug.Log($"[Score] candidates = {string.Join(", ", candidates.Select(n => n.name))}");

////        if (candidates.Count == 0)
////        {
////            Debug.Log("[Score] → 候補ゼロ → return null");
////            return null;
////        }

////        var selected = candidates
////            .OrderByDescending(n => Score(n))
////            .ThenBy(_ => Random.value)
////            .FirstOrDefault();

////        Debug.Log($"[Score] SELECTED = {selected.name}");
////        return selected;
////    }
////    //private MapNode ChooseNextByScore(MapNode current)
////    //{
////    //    Debug.Log($"[Score] currentNode = {current.name}");
////    //    Debug.Log($"[Score] recentNodes = {string.Join(", ", recentNodes.Select(n => n.name))}");

////    //    // ======================================================
////    //    // ① 履歴（recentNodes）の中で unknownCount が最大の Node を調べる
////    //    // ======================================================
////    //    if (recentNodes.Count > 0)
////    //    {
////    //        MapNode bestHist = recentNodes
////    //            .OrderByDescending(n => n.unknownCount)
////    //            .First();

////    //        Debug.Log($"[Score] bestHist = {bestHist.name}, U={bestHist.unknownCount}");

////    //        // → 同じなら未知方向へ（＝リンク以外を見るため return null）
////    //        if (bestHist == current)
////    //        {
////    //            Debug.Log("[Score] → 履歴最大未知数が current と一致 → 未知方向探索（return null）");
////    //            return null;
////    //        }
////    //    }

////    //    // ======================================================
////    //    // ② リンク方向のログ収集（壁判定もログに記録）
////    //    // ======================================================
////    //    List<(MapNode node, Vector3 dir, bool isWall, float score)> logs
////    //        = new();

////    //    foreach (var n in current.links)
////    //    {
////    //        if (n == null) continue;

////    //        Vector3 dir = (n.transform.position - current.transform.position).normalized;

////    //        bool wall = IsWall(current, dir);
////    //        float sc = Score(n);

////    //        logs.Add((n, dir, wall, sc));
////    //    }

////    //    foreach (var L in logs)
////    //    {
////    //        Debug.Log($"[Score] link: {L.node.name}, dir={L.dir}, isWall={L.isWall}, score={L.score}");
////    //    }

////    //    // ======================================================
////    //    // ③ 候補抽出：壁方向・背後方向(prevNode) を除外
////    //    // ======================================================

////    //    MapNode prevNode = (recentNodes.Count >= 2 ? recentNodes[^2] : null);

////    //    var candidates = logs
////    //        .Where(L => !L.isWall)              // ★ 壁方向は除外
////    //        //.Where(L => L.node != prevNode)     // ★ 背後の Node を除外
////    //        .Select(L => L.node)
////    //        .ToList();

////    //    Debug.Log($"[Score] candidates = {string.Join(", ", candidates.Select(n => n.name))}");

////    //    // 候補が無い場合 → 未知方向へ移行
////    //    if (candidates.Count == 0)
////    //    {
////    //        Debug.Log("[Score] → 候補ゼロ → return null");
////    //        return null;
////    //    }

////    //    // ======================================================
////    //    // ④ 評価の高いリンク方向へ進む
////    //    // ======================================================
////    //    var selected = candidates
////    //        .OrderByDescending(n => Score(n))
////    //        .ThenBy(_ => Random.value)   // スコアが同じときランダム
////    //        .First();

////    //    Debug.Log($"[Score] SELECTED = {selected.name}");
////    //    return selected;
////    //}


////    private float Score(MapNode n)
////    {
////        float u = n.unknownCount;
////        float d = n.distanceFromStart;

////        return weightUnknown * u + weightDistance * (-d);
////    }

////    private void MoveToUnlinked()
////    {
////        List<Vector3> dirs = AllMovesExceptBack();

////        dirs = dirs.Where(d => !IsLinkedDirection(currentNode, d)).ToList();
////        dirs = dirs.Where(d => !IsWall(currentNode, d)).ToList();

////        if (dirs.Count == 0)
////        {
////            // 仕方なく戻る
////            moveDir = -moveDir;
////            MoveForward();
////            return;
////        }

////        moveDir = dirs[Random.Range(0, dirs.Count)];
////        MoveForward();
////    }

////    private List<Vector3> AllMovesExceptBack()
////    {
////        List<Vector3> dirs = new()
////        {
////            Vector3.forward,
////            Vector3.back,
////            Vector3.left,
////            Vector3.right
////        };

////        Vector3 back = -moveDir;

////        return dirs.Where(d => Vector3.Dot(d.normalized, back.normalized) < 0.7f).ToList();
////    }

////    private bool IsLinkedDirection(MapNode node, Vector3 dir)
////    {
////        foreach (var link in node.links)
////        {
////            Vector3 diff = (link.transform.position - node.transform.position).normalized;
////            if (Vector3.Dot(diff, dir.normalized) > 0.7f)
////                return true;
////        }
////        return false;
////    }

////    private bool IsWall(MapNode node, Vector3 dir)
////    {
////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
////        return Physics.Raycast(origin, dir, cellSize, wallLayer);
////    }
////    //private bool IsWall(MapNode from, Vector3 dir)
////    //{
////    //    Vector3 origin = from.transform.position + Vector3.up;

////    //    float dist = cellSize * 0.9f;

////    //    if (Physics.Raycast(origin, dir, dist, wallLayer))
////    //        return true;

////    //    return false;
////    //}



////    //private MapNode TryPlaceNode(Vector3 pos)
////    //{
////    //    Vector2Int cell = WorldToCell(SnapToGrid(pos));
////    //    MapNode node;

////    //    if (MapNode.allNodeCells.Contains(cell))
////    //        node = MapNode.FindByCell(cell);
////    //    else
////    //    {
////    //        GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////    //        node = obj.GetComponent<MapNode>();
////    //        node.cell = cell;
////    //        MapNode.allNodeCells.Add(cell);
////    //    }

////    //    if (MapNode.StartNode == null)
////    //    {
////    //        MapNode.StartNode = node;
////    //        node.distanceFromStart = 0;
////    //    }

////    //    LinkBackward(node);

////    //    return node;
////    //}
////    private MapNode TryPlaceNode(Vector3 pos)
////    {
////        Vector3 snapped = SnapToGrid(pos);
////        Vector2Int cell = WorldToCell(snapped);

////        Debug.Log($"[TryPlaceNode] pos={pos}, snapped={snapped}, cell={cell}");

////        // 壁チェック
////        bool isWall = Physics.Raycast(snapped + Vector3.up * 0.1f, Vector3.down, 1f, wallLayer);
////        Debug.Log($"[TryPlaceNode] isWall={isWall}");

////        if (isWall)
////        {
////            MapNode exist = MapNode.FindByCell(cell);
////            Debug.Log($"[TryPlaceNode] WALL → existing={exist}");
////            return exist;
////        }

////        MapNode node;

////        if (MapNode.allNodeCells.Contains(cell))
////        {
////            node = MapNode.FindByCell(cell);
////            Debug.Log($"[TryPlaceNode] Reuse node={node.name}");
////        }
////        else
////        {
////            Debug.Log($"[TryPlaceNode] New Node @ {cell}");
////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////            node = obj.GetComponent<MapNode>();
////            node.cell = cell;
////            MapNode.allNodeCells.Add(cell);
////        }

////        Debug.Log($"[TryPlaceNode] RETURN node={node.name}");

////        LinkBackward(node);
////        return node;
////    }


////    private void LinkBackward(MapNode node)
////    {
////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
////        Vector3 dir = -moveDir;

////        LayerMask mask = wallLayer | nodeLayer;

////        for (int i = 1; i <= linkRayMaxSteps; i++)
////        {
////            float dist = cellSize * i;

////            if (debugRay)
////                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);

////            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
////            {
////                int layer = hit.collider.gameObject.layer;

////                if ((wallLayer.value & (1 << layer)) != 0)
////                    return;

////                if ((nodeLayer.value & (1 << layer)) != 0)
////                {
////                    var hitNode = hit.collider.GetComponent<MapNode>();
////                    if (hitNode != null && hitNode != node)
////                    {
////                        node.AddLink(hitNode);
////                        node.RecalculateUnknownAndWall();
////                        hitNode.RecalculateUnknownAndWall();
////                    }
////                    return;
////                }
////            }
////        }
////    }

////    private Vector3 SnapToGrid(Vector3 pos)
////    {
////        int x = Mathf.RoundToInt((pos.x - gridOrigin.x) / cellSize);
////        int z = Mathf.RoundToInt((pos.z - gridOrigin.z) / cellSize);
////        return new Vector3(x * cellSize, 0, z * cellSize) + gridOrigin;
////    }

////    private Vector2Int WorldToCell(Vector3 worldPos)
////    {
////        Vector3 p = worldPos - gridOrigin;
////        return new Vector2Int(
////            Mathf.RoundToInt(p.x / cellSize),
////            Mathf.RoundToInt(p.z / cellSize)
////        );
////    }

////    private Vector3 CellToWorld(Vector2Int cell)
////        => new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;

////    private void Log(string msg)
////    {
////        if (debugLog) Debug.Log("[CellFS] " + msg);
////    }
////}