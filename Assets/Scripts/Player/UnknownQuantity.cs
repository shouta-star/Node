// ======================================================
// PlayerはNodeの未知数が多い方向へ進む。
// 最短経路はGoalに到達した際、各NodeがGoalからどれだけ離れているかで決定。
// ======================================================

//毎フレーム Update
//↓
//isMoving=false ?
//↓ YES
//    CanPlaceNodeHere？
//      ↓NO → MoveForward（直進）
//      ↓YES → TryExploreMove（方向選択）

//TryExploreMove の中
//↓
//① Node を置く
//② Goal 到達？ → 最短経路モードへ移行
//③ 終端？ → 未知方向探索
//④ unknown を BFS で探し最寄り Unknown へ向かう
//⑤ fallback（unknownなし）→ランダムリンク
//↓
//MoveForward
//↓
//MoveToTarget（1マス進む）

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;

public class UnknownQuantity: MonoBehaviour
{
    // ======================================================
    // パラメータ設定
    // ======================================================

    [Header("移動設定")]
    public float moveSpeed = 3f;            // プレイヤーの移動速度
    public float cellSize = 1f;             // 1マスのサイズ
    public float rayDistance = 1f;          // 壁判定のためのレイ距離
    public LayerMask wallLayer;             // 壁レイヤー
    public LayerMask nodeLayer;             // ノードレイヤー

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward; // 初期移動方向
    public Vector3 gridOrigin = Vector3.zero;        // グリッド原点
    public MapNode goalNode;                         // ゴールノード参照
    public GameObject nodePrefab;                    // ノードプレハブ

    [Header("行動傾向")]
    [Range(0f, 1f)] public float exploreBias = 0.6f; // 探索（未知方向）に進む確率

    [Header("探索パラメータ")]
    public int unknownReferenceDepth; // 🔹何ノード先まで未知数を参照するか（履歴の長さ）

    [Header("リンク探索")]
    public int linkRayMaxSteps = 100; // ノード間リンクを探す際の最大レイ長

    [Header("デバッグ")]
    public bool debugLog = true;      // ログ出力ON/OFF
    public bool debugRay = true;      // レイ表示ON/OFF
    [SerializeField] private Renderer bodyRenderer;  // 見た目
    [SerializeField] private Material exploreMaterial; // 探索時マテリアル

    public static bool hasLearnedGoal = false; // ゴール情報を学習済みかどうか
    public static int shortestModeArrivalCount = 0;
    private bool spawnedAsShortest = false;

    // ======================================================
    // 内部状態変数
    // ======================================================

    private Vector3 moveDir;          // 現在の移動方向
    private bool isMoving = false;    // 移動中フラグ
    private Vector3 targetPos;        // 移動目標座標
    private MapNode currentNode;      // 現在いるノード
    private bool reachedGoal = false; // ゴール到達フラグ
    private bool isFollowingShortest = false; // 最短経路追従中フラグ

    private const float EPS = 1e-4f;  // 浮動小数誤差対策

    // 直近の通過ノード履歴（最大 unknownReferenceDepth 個）
    private List<MapNode> recentNodes = new List<MapNode>();

    bool canMove;
    public MapNode CurrentNode => currentNode;

    // ★ CPU負荷測定
    private static float algTotalMs = 0f;
    private static float algMaxMs = 0f;
    private static int algFrameCount = 0;
    public static float AlgTotalMs => algTotalMs;
    public static int AlgFrameCount => algFrameCount;
    public static float AlgMaxMs => algMaxMs;


    // ======================================================
    // Start：初期化処理
    // ======================================================
    void Start()
    {
        UnityEngine.Debug.Log($"[CHECK] hasLearnedGoal={hasLearnedGoal} isFollowingShortest={isFollowingShortest}");
        //Debug.Log($"[Start] Player Start() called. frame={Time.frameCount} name={name}");

        // ★最優先：ノード設置
        Vector3 snappedPos = SnapToGrid(transform.position);
        currentNode = TryPlaceNode(snappedPos);

        // ここで StartNode が必ず設定される
        RegisterCurrentNode(currentNode);

        // --- 以下は後でOK ---
        moveDir = startDirection.normalized;
        targetPos = transform.position = snappedPos;
        ApplyVisual();

        // ゴールノードが未設定なら自動検索
        if (goalNode == null)
        {
            GameObject goalObj = GameObject.FindGameObjectWithTag("Goal");
            if (goalObj != null)
                goalNode = goalObj.GetComponent<MapNode>();

            UnityEngine.Debug.Log("[INIT] Goal re-found: " + goalNode);
        }

        UnityEngine.Debug.Log($"[DEBUG-STARTNODE] UnknownQuantity.Start(): StartNode = {MapNode.StartNode?.name}");

        // ★★★ 最重要 ★★★
        // Goal に向かうための距離を全 Node に再計算する
        if (goalNode != null)
        {
            MapNode.RecalculateGoalDistance(goalNode);
            UnityEngine.Debug.Log("[INIT] RecalculateGoalDistance DONE");
        }

        // ゴール学習済みなら最短経路追従を開始
        if (hasLearnedGoal)
        {
            spawnedAsShortest = true;
            isFollowingShortest = true;
            StopAllCoroutines();
            StartCoroutine(FollowShortestPath());
            return;
        }

        // ★ Start直後だけ壁チェック → 壁なら前進を停止（初期壁抜け防止）
        Vector3 origin = transform.position + Vector3.up;
        if (Physics.Raycast(origin, moveDir, rayDistance, wallLayer))
        {
            UnityEngine.Debug.Log("[INIT] Front is WALL → Stop first move");
            moveDir = Vector3.zero;     // ← ここが最重要
        }

        if (debugLog) UnityEngine.Debug.Log($"[Player:{name}] Start @ {currentNode}");

        canMove = false;
        StartCoroutine(EnableMoveNextFrame());

        UnityEngine.Debug.Log("=== [DEBUG-U] Start 時点 全Node unknownCount ===");
        foreach (var n in MapNode.allNodes)
        {
            UnityEngine.Debug.Log($"[DEBUG-U] {n.name} U={n.unknownCount} W={n.wallCount} links={n.links.Count}");
        }
    }

    // ======================================================
    // Update：毎フレームの動作制御
    // ======================================================
    void Update()
    {
        Stopwatch sw = Stopwatch.StartNew();

        // 最短経路追従中は移動処理のみ
        if (isFollowingShortest)
        {
            if (isMoving) MoveToTarget();
            return;
        }

        // 通常探索モード時の分岐
        if (!isMoving)
        {
            if (CanPlaceNodeHere())     // 十字路 or 壁前ならノードを設置して方向選択
                TryExploreMove();
            else                        // 通常前進
                MoveForward();
        }
        else
        {
            MoveToTarget();
        }

        if (!canMove) return;

        sw.Stop();
        float ms = sw.ElapsedMilliseconds;

        algTotalMs += ms;
        algMaxMs = Mathf.Max(algMaxMs, ms);
        algFrameCount++;
    }

    // 再スタート時初期化用（RestartManager から呼ぶ）
    public static void ResetAlgorithmMetrics()
    {
        algTotalMs = 0f;
        algMaxMs = 0f;
        algFrameCount = 0;
    }

    private IEnumerator EnableMoveNextFrame()
    {
        yield return null; // 1フレーム待つ
        canMove = true;
    }

    // ======================================================
    // ApplyVisual：プレイヤーの見た目設定
    // ======================================================
    private void ApplyVisual()
    {
        if (bodyRenderer == null) return;
        bodyRenderer.material = exploreMaterial
            ? exploreMaterial
            : new Material(Shader.Find("Standard")) { color = Color.cyan };
    }

    // ======================================================
    // CanPlaceNodeHere：ノードを設置すべき位置かを判定
    // ======================================================
    bool CanPlaceNodeHere()
    {
        if (isFollowingShortest) return false;

        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        // 周囲に壁があるかを判定
        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

        int openCount = 0;
        if (!frontHit) openCount++;
        if (!leftHit) openCount++;
        if (!rightHit) openCount++;

        // 壁前 or 分岐点ならノード設置対象
        return (frontHit || openCount >= 2);
    }

    // ======================================================
    // MoveForward：現在方向に1マス進む
    // ======================================================
    void MoveForward()
    {
        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
        targetPos = nextPos;
        isMoving = true;
    }

    // ======================================================
    // MoveToTarget：ターゲット位置に向かって移動し、到達時に停止
    // ======================================================
    private void MoveToTarget()
    {
        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
        {
            // 移動中：ターゲットに向かって移動
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                moveSpeed * Time.deltaTime
            );
        }
        else
        {
            // 目標地点に到達
            transform.position = targetPos;
            isMoving = false;
        }
    }

    // ======================================================
    // 履歴登録：直近 unknownReferenceDepth 個だけ保持
    // ======================================================
    private void RegisterCurrentNode(MapNode node)
    {
        if (node == null) return;

        // 直前と同じノードならスキップ
        if (recentNodes.Count > 0 && recentNodes[recentNodes.Count - 1] == node)
            return;

        recentNodes.Add(node);

        int maxDepth = Mathf.Max(unknownReferenceDepth, 1);
        while (recentNodes.Count > maxDepth)
        {
            recentNodes.RemoveAt(0);
        }

        if (debugLog)
        {
            string hist = string.Join(" -> ", recentNodes.Select(n => n != null ? n.name : "null"));
            UnityEngine.Debug.Log($"[HIST] {hist}");
        }
    }

    // ======================================================
    // TryExploreMove：未知数(U)に基づいて次のノードを決定
    // ======================================================
    void TryExploreMove()
    {
        if (isFollowingShortest) return;

        currentNode = TryPlaceNode(transform.position);
        if (debugLog) UnityEngine.Debug.Log("[Player] Node placed → decide next direction");

        // 履歴に現在ノードを登録
        RegisterCurrentNode(currentNode);

        // GoalNode に到達したかチェック
        if (goalNode != null && currentNode == goalNode)
        {
            reachedGoal = true;
            if (debugLog) UnityEngine.Debug.Log("[GOAL] Player reached GoalNode. Recalculate distances & follow shortest path.");

            // ★Goal到達距離を ShortestPathJudge に通知
            int dist = currentNode.distanceFromStart;   // または DistanceFromGoal のほうが正確
            ShortestPathJudge.Instance?.OnGoalReached(dist);

            // 現在の向きの逆方向でリンクを補完
            if (currentNode != null)
                LinkBackWithRay(currentNode);

            // Goal を起点に全ノードの DistanceFromGoal を再計算
            RecalculateGoalDistance();

            // 以降スポーンする Player は最短経路モードへ
            hasLearnedGoal = true;

            // この Player 自身も、そのまま最短経路をたどって最後に Destroy
            isFollowingShortest = true;
            StopAllCoroutines();
            StartCoroutine(FollowShortestPath());
            return;
        }

        // 終端ノード（リンクが1つだけ）なら新しい方向を探索
        if (IsTerminalNode(currentNode))
        {
            if (debugLog) UnityEngine.Debug.Log($"[EXP] Terminal node detected ({currentNode.name}) → TryMoveToUnlinkedDirection()");
            TryMoveToUnlinkedDirection();
            return;
        }

        // 現在ノードが存在しない or リンクが全くない場合は新規探索
        if (currentNode == null || currentNode.links.Count == 0)
        {
            TryMoveToUnlinkedDirection(); // ← 新規探索処理へ
            return;
        }

        // 履歴に基づき「未知度が高い方へ戻る or 現在で枝を伸ばす」ノードを選ぶ
        MapNode next = ChooseNextNodeByUnknown(currentNode);

        if (next != null)
        {
            // 履歴上で「未知度の高い方向に戻る」ための一歩（1ノード分戻る）
            moveDir = (next.transform.position - transform.position).normalized;
            MoveForward();

            if (debugLog)
                UnityEngine.Debug.Log($"[EXP-SELECT] {currentNode.name} → {next.name} (U={next.unknownCount})");
        }
        else
        {
            // ベストなノードが「現在ノード自身」だった場合など：
            // ここで新しい未リンク方向へ進む（結果的に新規Node開拓へ）
            if (debugLog)
                UnityEngine.Debug.Log("[EXP-SELECT] best node is current → TryMoveToUnlinkedDirection()");
            TryMoveToUnlinkedDirection();
        }
    }

    // ======================================================
    // IsTerminalNode：リンクが1方向のみのノードを終端と判定
    // ======================================================
    private bool IsTerminalNode(MapNode node)
    {
        return node != null && node.links != null && node.links.Count == 1;
    }


    // ==============================
    // 方向ベクトル → ラベル化 (F/B/L/R)
    // ==============================
    private string DirToName(Vector3 dir)
    {
        Vector3 n = dir.normalized;
        if (Vector3.Dot(n, Vector3.forward) > 0.7f) return "F";
        if (Vector3.Dot(n, Vector3.back) > 0.7f) return "B";
        if (Vector3.Dot(n, Vector3.left) > 0.7f) return "L";
        if (Vector3.Dot(n, Vector3.right) > 0.7f) return "R";
        return $"({n.x:0.##},{n.y:0.##},{n.z:0.##})";
    }

    // ==============================
    // 候補配列 → "F,R,..." 形式の文字列
    // ==============================
    private string DirListToString(List<Vector3> list)
    {
        if (list == null || list.Count == 0) return "(none)";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(DirToName(list[i]));
        }
        return sb.ToString();
    }

    // ======================================================
    // TryMoveToUnlinkedDirection
    // ------------------------------------------------------
    // ・終端Node（リンクが少ない/未探索方向がある）で進行方向を決定
    // ・戻る(back)方向は除外
    // ・リンク済み・壁方向は除外
    // ・候補が無い場合はback方向を避けて停止（往復防止）
    // ======================================================
    //private void TryMoveToUnlinkedDirection()
    //{
    //    // 現在Nodeが存在しない場合は前進
    //    if (currentNode == null)
    //    {
    //        if (debugLog) Debug.Log("[EXP-DBG] currentNode=null → MoveForward()");
    //        MoveForward();
    //        return;
    //    }

    //    // ① 全方向初期化
    //    List<Vector3> allDirs = new List<Vector3>
    //    {
    //        Vector3.forward,
    //        Vector3.back,
    //        Vector3.left,
    //        Vector3.right
    //    };
    //    if (debugLog) Debug.Log($"[EXP-DBG] All dirs: {DirListToString(allDirs)}");

    //    Vector3 backDir = (-moveDir).normalized;

    //    // ② 戻る(back)方向を除外
    //    List<Vector3> afterBack = new List<Vector3>();
    //    foreach (var d in allDirs)
    //    {
    //        if (Vector3.Dot(d.normalized, backDir) > 0.7f) continue;
    //        afterBack.Add(d);
    //    }
    //    if (debugLog) Debug.Log($"[EXP-DBG] After remove BACK ({DirToName(backDir)}): {DirListToString(afterBack)}");

    //    // ③ 既にリンク済みの方向を除外
    //    List<Vector3> afterLinked = new List<Vector3>();
    //    foreach (var d in afterBack)
    //    {
    //        bool linked = false;
    //        foreach (var link in currentNode.links)
    //        {
    //            Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
    //            if (Vector3.Dot(diff, d.normalized) > 0.7f)
    //            {
    //                linked = true;
    //                if (debugLog) Debug.Log($"[EXP-DBG] LINKED dir removed: {DirToName(d)} (→ {link.name})");
    //                break;
    //            }
    //        }
    //        if (!linked) afterLinked.Add(d);
    //    }
    //    if (debugLog) Debug.Log($"[EXP-DBG] After remove LINKED: {DirListToString(afterLinked)}");

    //    // ④ 壁方向を除外（Raycastで壁チェック）
    //    List<Vector3> validDirs = new List<Vector3>();
    //    Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
    //    foreach (var d in afterLinked)
    //    {
    //        if (Physics.Raycast(origin, d, out RaycastHit hit, cellSize, wallLayer))
    //        {
    //            if (debugLog) Debug.Log($"[EXP-DBG] BLOCKED by Wall: {DirToName(d)} ({hit.collider.name})");
    //            continue;
    //        }
    //        validDirs.Add(d);
    //    }
    //    if (debugLog) Debug.Log($"[EXP-DBG] Final candidates: {DirListToString(validDirs)}");

    //    // ⑤ 候補が無い場合（往復防止処理）
    //    if (validDirs.Count == 0)
    //    {
    //        bool canContinue = false;
    //        Vector3 nextDir = Vector3.zero;

    //        // 現Nodeのリンク情報からback以外を探す
    //        foreach (var link in currentNode.links)
    //        {
    //            Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
    //            if (Vector3.Dot(diff, backDir) < 0.7f) // back方向ではない
    //            {
    //                canContinue = true;
    //                nextDir = diff;
    //                break;
    //            }
    //        }

    //        if (canContinue)
    //        {
    //            moveDir = nextDir;
    //            if (debugLog) Debug.Log($"[EXP-RESULT] No unlinked dirs → Follow existing link {DirToName(moveDir)}");
    //            MoveForward();
    //        }
    //        else
    //        {
    //            if (debugLog) Debug.Log("[EXP-RESULT] Only back dir left → Stop to avoid loop");
    //            // 往復防止のため停止
    //            return;
    //        }

    //        return;
    //    }

    //    // ⑥ 候補からランダムに選択
    //    moveDir = validDirs[UnityEngine.Random.Range(0, validDirs.Count)];

    //    if (debugLog)
    //    {
    //        string all = DirListToString(validDirs);
    //        string chosen = DirToName(moveDir);
    //        Debug.Log($"[EXP-RESULT] Selected direction: {chosen}  /  Candidates: {all}  /  Node={currentNode.name}");
    //    }

    //    // ⑦ 実際に前進
    //    MoveForward();
    //}
    private void TryMoveToUnlinkedDirection()
    {
        // 現在Nodeが存在しない場合は前進
        if (currentNode == null)
        {
            if (debugLog) UnityEngine.Debug.Log("[EXP-DBG] currentNode=null → MoveForward()");
            MoveForward();
            return;
        }

        // ① 全方向初期化
        List<Vector3> allDirs = new List<Vector3>
    {
        Vector3.forward,
        Vector3.back,
        Vector3.left,
        Vector3.right
    };
        if (debugLog) UnityEngine.Debug.Log($"[EXP-DBG] All dirs: {DirListToString(allDirs)}");

        Vector3 backDir = (-moveDir).normalized;

        // 行き止まり判定（リンクが1つだけ）
        bool isDeadEnd = (currentNode.links.Count == 1);

        // ② 戻る(back)方向を除外（※行き止まりなら除外しない）
        List<Vector3> afterBack = new List<Vector3>();
        foreach (var d in allDirs)
        {
            if (!isDeadEnd && Vector3.Dot(d.normalized, backDir) > 0.7f)
                continue;

            afterBack.Add(d);
        }
        if (debugLog) UnityEngine.Debug.Log($"[EXP-DBG] After remove BACK ({DirToName(backDir)}): {DirListToString(afterBack)}");

        // ③ 既にリンク済みの方向を除外（※行き止まりなら back は除外しない）
        List<Vector3> afterLinked = new List<Vector3>();
        foreach (var d in afterBack)
        {
            bool linked = false;

            foreach (var link in currentNode.links)
            {
                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;

                // 🔵 行き止まりなら back 方向はリンク除外の対象にしない
                if (isDeadEnd && Vector3.Dot(d.normalized, backDir) > 0.7f)
                    continue;

                if (Vector3.Dot(diff, d.normalized) > 0.7f)
                {
                    linked = true;
                    if (debugLog) UnityEngine.Debug.Log($"[EXP-DBG] LINKED dir removed: {DirToName(d)} (→ {link.name})");
                    break;
                }
            }

            if (!linked)
                afterLinked.Add(d);
        }
        if (debugLog) UnityEngine.Debug.Log($"[EXP-DBG] After remove LINKED: {DirListToString(afterLinked)}");

        // ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        // ★ 追加：recentNodes に含まれるノード方向を除外
        // ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        List<Vector3> afterRecent = new List<Vector3>();

        foreach (var d in afterLinked)
        {
            MapNode next = null;

            // d 方向のリンクノードを取得
            foreach (var link in currentNode.links)
            {
                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
                if (Vector3.Dot(diff, d.normalized) > 0.7f)
                {
                    next = link;
                    break;
                }
            }

            // recentNodes に含まれるノード方向 → 除外（行き止まりは除外しない）
            if (next != null && recentNodes.Contains(next) && !isDeadEnd)
            {
                if (debugLog)
                    UnityEngine.Debug.Log($"[EXP-DBG] RECENT removed: {DirToName(d)} ({next?.name})");
                continue; // ★追加
            }

            afterRecent.Add(d);
        }

        if (debugLog) UnityEngine.Debug.Log($"[EXP-DBG] After remove RECENT: {DirListToString(afterRecent)}");
        // ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★


        // ④ 壁方向を除外（Raycastで壁チェック）
        List<Vector3> validDirs = new List<Vector3>();
        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;

        foreach (var d in afterLinked)
        {
            if (Physics.Raycast(origin, d, out RaycastHit hit, cellSize, wallLayer))
            {
                if (debugLog) UnityEngine.Debug.Log($"[EXP-DBG] BLOCKED by Wall: {DirToName(d)} ({hit.collider.name})");
                continue;
            }
            validDirs.Add(d);
        }
        if (debugLog) UnityEngine.Debug.Log($"[EXP-DBG] Final candidates: {DirListToString(validDirs)}");

        // ⑤ 候補が無い場合
        if (validDirs.Count == 0)
        {
            bool canContinue = false;
            Vector3 nextDir = Vector3.zero;

            foreach (var link in currentNode.links)
            {
                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;

                if (Vector3.Dot(diff, backDir) < 0.7f)
                {
                    canContinue = true;
                    nextDir = diff;
                    break;
                }
            }

            if (canContinue)
            {
                moveDir = nextDir;
                if (debugLog) UnityEngine.Debug.Log($"[EXP-RESULT] No unlinked dirs → Follow existing link {DirToName(moveDir)}");
                MoveForward();
            }
            else
            {
                // 🔵 行き止まりでは back が最終手段 → back で進む
                if (isDeadEnd)
                {
                    moveDir = backDir;
                    if (debugLog) UnityEngine.Debug.Log("[EXP-RESULT] DeadEnd: back is only direction → Move back");
                    MoveForward();
                    return;
                }

                if (debugLog) UnityEngine.Debug.Log("[EXP-RESULT] Only back dir left → Stop to avoid loop");
                return;
            }

            return;
        }

        // ⑥ 候補からランダムに選択
        moveDir = validDirs[UnityEngine.Random.Range(0, validDirs.Count)];

        if (debugLog)
        {
            string all = DirListToString(validDirs);
            string chosen = DirToName(moveDir);
            UnityEngine.Debug.Log($"[EXP-RESULT] Selected direction: {chosen}  /  Candidates: {all}  /  Node={currentNode.name}");
        }

        // ⑦ 実際に前進
        MoveForward();
    }


    private MapNode ChooseNextNodeByUnknown(MapNode current)
    {
        if (current == null || current.links == null || current.links.Count == 0)
            return null;

        // ======================================================
        // 1. 履歴ベース（unknownReferenceDepth）による backtrack
        // ======================================================
        if (unknownReferenceDepth > 0 && recentNodes.Count > 0)
        {
            MapNode bestHistNode = null;
            int bestU = -1;

            foreach (var n in recentNodes)
            {
                if (n == null) continue;
                if (n.unknownCount > bestU)
                {
                    bestU = n.unknownCount;
                    bestHistNode = n;
                }
            }

            // 履歴の中に unknown > 0 があるならそっち優先
            if (bestHistNode != null && bestU > 0)
            {
                // current = bestHistNode → 新しい方向を探す
                if (bestHistNode == current)
                    return null;

                int curIndex = recentNodes.LastIndexOf(current);
                if (curIndex > 0)
                {
                    MapNode prevNode = recentNodes[curIndex - 1];
                    if (prevNode != null && current.links.Contains(prevNode))
                    {
                        //if (debugLog)
                        UnityEngine.Debug.Log($"[EXP-HIST] Backtrack {current.cell} → {prevNode.cell}");
                        //return prevNode;
                        return null;
                    }
                }
            }
        }


        // ======================================================
        // 2. current を起点に BFS：すべての「unknown > 0」の Node を収集
        // ======================================================

        List<MapNode> unknownList = new();
        Queue<MapNode> q = new();
        HashSet<MapNode> visited = new();

        q.Enqueue(current);
        visited.Add(current);

        while (q.Count > 0)
        {
            var node = q.Dequeue();

            if (node.unknownCount > 0)
                unknownList.Add(node);

            foreach (var link in node.links)
            {
                if (link == null) continue;
                if (visited.Contains(link)) continue;

                visited.Add(link);
                q.Enqueue(link);
            }
        }

        // unknown が全く無い → fallback
        if (unknownList.Count == 0)
        {
            //if (debugLog)
            UnityEngine.Debug.Log("[U] No unknown anywhere → fallback");

            return current.links
                .OrderByDescending(n => n.unknownCount)
                .ThenBy(_ => Random.value)
                .FirstOrDefault();
        }


        // ======================================================
        // 3. current から最短セル距離の unknown ノードを選ぶ
        // ======================================================

        int CellDist(MapNode a, MapNode b)
            => Mathf.Abs(a.cell.x - b.cell.x) + Mathf.Abs(a.cell.y - b.cell.y);

        int bestDist = int.MaxValue;
        List<MapNode> bestUnknowns = new();

        foreach (var unk in unknownList)
        {
            int dist = CellDist(current, unk);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestUnknowns.Clear();
                bestUnknowns.Add(unk);
            }
            else if (dist == bestDist)
            {
                bestUnknowns.Add(unk);
            }
        }

        // 同距離が複数ならランダムで unknown を選ぶ
        MapNode targetUnknown = bestUnknowns[Random.Range(0, bestUnknowns.Count)];

        //if (debugLog)
        UnityEngine.Debug.Log($"[U] Target UNKNOWN = {targetUnknown.cell}  dist={bestDist}");


        // ======================================================
        // 4. current.links の中で「targetUnknown に最も近づく一歩」を選ぶ
        // ======================================================

        MapNode bestNext = null;
        int bestNextDist = int.MaxValue;

        foreach (var link in current.links)
        {
            if (link == null) continue;

            // ★ recentNodes に含まれる方向は除外（行き止まりの場合は許可）
            if (!IsTerminalNode(current) && recentNodes.Contains(link))
            {
                if (debugLog)
                    UnityEngine.Debug.Log($"[U] Skip recent direction: {link.cell}");
                continue;
            }

            int dist = CellDist(link, targetUnknown);

            if (dist < bestNextDist)
            {
                bestNextDist = dist;
                bestNext = link;
            }
        }

        // ★★★ Nullチェックを追加（ここ必須）★★★
        if (bestNext == null)
        {
            if (debugLog)
                UnityEngine.Debug.Log("[U] bestNext is null → return null (fallback)");
            return null;
        }

        //if (debugLog)
        UnityEngine.Debug.Log($"[U] Next Step = {bestNext.cell} (toward {targetUnknown.cell})");

        //return bestNext;
        return GetNextByLinkBFS(current, targetUnknown);
    }
    private MapNode GetNextByLinkBFS(MapNode start, MapNode target)
    {
        if (start == null || target == null) return null;
        if (start == target) return null;

        Queue<MapNode> q = new();
        Dictionary<MapNode, MapNode> parent = new();
        q.Enqueue(start);
        parent[start] = null;

        while (q.Count > 0)
        {
            var node = q.Dequeue();
            foreach (var link in node.links)
            {
                if (link == null) continue;
                if (parent.ContainsKey(link)) continue;

                parent[link] = node;
                q.Enqueue(link);

                // target に到達したら BFS を終了
                if (link == target)
                    return BacktrackFirstStep(start, target, parent);
            }
        }

        return null; // 到達できない
    }
    private MapNode BacktrackFirstStep(MapNode start, MapNode target, Dictionary<MapNode, MapNode> parent)
    {
        MapNode cur = target;

        while (parent[cur] != start)
            cur = parent[cur];

        return cur; // start から見た最初の一歩
    }


    // ======================================================
    // ※ 以下2つは現在未使用だが、必要なら再利用可能な U 集計ユーティリティ
    // ======================================================
    private float GetAverageUnknown(MapNode node, int depth)
    {
        if (node == null || depth <= 0) return 0f;

        HashSet<MapNode> visited = new();
        (float total, int count) = GetUnknownRecursive(node, depth, visited);

        return count > 0 ? total / count : 0f;
    }

    private (float, int) GetUnknownRecursive(MapNode node, int depth, HashSet<MapNode> visited)
    {
        if (node == null || depth <= 0 || visited.Contains(node))
            return (0f, 0);

        visited.Add(node);

        float total = node.unknownCount;
        int count = 1;

        foreach (var link in node.links)
        {
            (float sub, int subCount) = GetUnknownRecursive(link, depth - 1, visited);
            total += sub;
            count += subCount;
        }

        return (total, count);
    }

    // ======================================================
    // FollowShortestPath：学習後に最短経路をたどる処理
    // ======================================================
    private IEnumerator FollowShortestPath()
    {
        if (currentNode == null)
        {
            UnityEngine.Debug.LogWarning("[FollowSP] currentNode is null → 経路追従不可");
            isFollowingShortest = false;
            yield break;
        }

        // 見た目を赤に変更（防衛モード／最短経路モード）
        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
        if (debugLog) UnityEngine.Debug.Log($"[FollowSP] === Start === current={currentNode.name}, Dist={currentNode.DistanceFromGoal}");

        isFollowingShortest = true;
        int stepCount = 0;

        while (currentNode != null && currentNode.DistanceFromGoal > EPS)
        {
            stepCount++;
            float currentDist = currentNode.DistanceFromGoal;

            // 距離が短くなる方向のノードを選択
            var nextNode = currentNode.links
                .Where(n => n != null && n.DistanceFromGoal < currentDist - EPS)
                .OrderBy(n => n.DistanceFromGoal)
                .FirstOrDefault();

            if (nextNode == null) break;

            Vector3 target = nextNode.transform.position;
            Vector3 dir = (target - transform.position); dir.y = 0f;
            moveDir = dir.normalized;

            // 実際に移動
            while (Vector3.Distance(transform.position, target) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
                yield return null;
            }

            currentNode = nextNode;
            transform.position = currentNode.transform.position;

            // ゴール到達時の処理
            if (currentNode.DistanceFromGoal <= EPS || (goalNode != null && currentNode == goalNode))
            {
                reachedGoal = true;
                if (currentNode != null)
                    LinkBackWithRay(currentNode);

                RecalculateGoalDistance();
                hasLearnedGoal = true;

                if (spawnedAsShortest)
                {
                    UnknownQuantity.shortestModeArrivalCount++;
                    UnityEngine.Debug.Log($"[COUNT] shortest-mode arrived = {UnknownQuantity.shortestModeArrivalCount}");
                }

                isFollowingShortest = false;
                Destroy(gameObject); // 自身を削除
                yield break;
            }
        }

        isFollowingShortest = false;
    }

    // ======================================================
    // LinkBackWithRay：後方方向にノードを探してリンクを追加
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
                UnityEngine.Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
            {
                int hitLayer = hit.collider.gameObject.layer;

                if ((wallLayer.value & (1 << hitLayer)) != 0)
                    return;

                if ((nodeLayer.value & (1 << hitLayer)) != 0)
                {
                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
                    if (hitNode != null && hitNode != node)
                    {
                        node.AddLink(hitNode);
                        node.RecalculateUnknownAndWall();
                        hitNode.RecalculateUnknownAndWall();

                        if (debugLog)
                            UnityEngine.Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
                    }
                    return;
                }
            }
        }
    }

    // ======================================================
    // RecalculateGoalDistance：ゴールからの距離を再計算（Dijkstra法）
    // ======================================================
    void RecalculateGoalDistance()
    {
        if (goalNode == null) return;

        // 全ノードを初期化
        foreach (var n in FindObjectsOfType<MapNode>())
            n.DistanceFromGoal = Mathf.Infinity;

        goalNode.DistanceFromGoal = 0f;
        var frontier = new List<MapNode> { goalNode };

        // Dijkstra探索で全ノード距離更新
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
    // TryPlaceNode：現在位置にノードを設置または再利用
    // ======================================================
    MapNode TryPlaceNode(Vector3 pos)
    {
        // 最短経路中は既存ノードを再利用
        if (isFollowingShortest)
        {
            Vector2Int c = WorldToCell(SnapToGrid(pos));
            return MapNode.FindByCell(c);
        }

        Vector2Int cell = WorldToCell(SnapToGrid(pos));
        MapNode node;

        // 既に存在するノードなら再利用
        if (MapNode.allNodeCells.Contains(cell))
        {
            node = MapNode.FindByCell(cell);
            if (debugLog) UnityEngine.Debug.Log($"[Node] Reuse existing Node @ {cell}");
        }
        else
        {
            // 新規ノードを生成
            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);
            if (debugLog) UnityEngine.Debug.Log($"[Node] New Node placed @ {cell}");

            // ★ ShortestPathJudge に「新規Node追加」を通知（ここ！）
            ShortestPathJudge.Instance?.OnNodeAdded();
        }

        // StartNodeの初期設定 — ここだけが StartNode を決める
        if (MapNode.StartNode == null)
        {
            MapNode.StartNode = node;
            node.distanceFromStart = 0;
            //Debug.Log($"[StartNode] StartNode set to cell={node.cell}, pos={node.transform.position}");
        }

        // 周囲とのリンクを更新
        if (node != null)
            LinkBackWithRay(node);

        return node;
    }

    // ======================================================
    // 座標変換ユーティリティ群
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