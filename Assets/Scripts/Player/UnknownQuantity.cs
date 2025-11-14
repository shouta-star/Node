// ======================================================
// PlayerはNodeの未知数が多い方向へ進む。
// 最短経路はGoalに到達した際、各NodeがGoalからどれだけ離れているかで決定。
// ======================================================

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

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

    // 🔹直近の通過ノード履歴（最大 unknownReferenceDepth 個）
    private List<MapNode> recentNodes = new List<MapNode>();

    // ======================================================
    // Start：初期化処理
    // ======================================================
    void Start()
    {
        moveDir = startDirection.normalized;         // 初期移動方向設定
        targetPos = transform.position = SnapToGrid(transform.position); // グリッド位置にスナップ
        ApplyVisual();                               // プレイヤーの見た目設定
        currentNode = TryPlaceNode(transform.position); // 現在位置にノード設置 or 取得

        // 🔹履歴に現在ノードを登録
        RegisterCurrentNode(currentNode);

        // ゴールノードが未設定なら自動検索
        if (goalNode == null)
        {
            GameObject goalObj = GameObject.Find("Goal");
            if (goalObj != null)
                goalNode = goalObj.GetComponent<MapNode>();
        }

        // ゴール学習済みなら最短経路追従を開始
        if (hasLearnedGoal)
        {
            isFollowingShortest = true;
            StopAllCoroutines();
            StartCoroutine(FollowShortestPath());
            return;
        }

        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
    }

    // ======================================================
    // Update：毎フレームの動作制御
    // ======================================================
    void Update()
    {
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
    // 🔹履歴登録：直近 unknownReferenceDepth 個だけ保持
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
            Debug.Log($"[HIST] {hist}");
        }
    }

    // ======================================================
    // TryExploreMove：未知数(U)に基づいて次のノードを決定
    // ======================================================
    void TryExploreMove()
    {
        if (isFollowingShortest) return;

        currentNode = TryPlaceNode(transform.position);
        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

        // 🔹履歴に現在ノードを登録
        RegisterCurrentNode(currentNode);

        // 🔥 GoalNode に到達したかチェック
        if (goalNode != null && currentNode == goalNode)
        {
            reachedGoal = true;
            if (debugLog) Debug.Log("[GOAL] Player reached GoalNode. Recalculate distances & follow shortest path.");

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

        // 🔹 終端ノード（リンクが1つだけ）なら新しい方向を探索
        if (IsTerminalNode(currentNode))
        {
            if (debugLog) Debug.Log($"[EXP] Terminal node detected ({currentNode.name}) → TryMoveToUnlinkedDirection()");
            TryMoveToUnlinkedDirection();
            return;
        }

        // 現在ノードが存在しない or リンクが全くない場合は新規探索
        if (currentNode == null || currentNode.links.Count == 0)
        {
            TryMoveToUnlinkedDirection(); // ← 新規探索処理へ
            return;
        }

        // 🔹履歴に基づき「未知度が高い方へ戻る or 現在で枝を伸ばす」ノードを選ぶ
        MapNode next = ChooseNextNodeByUnknown(currentNode);

        if (next != null)
        {
            // 履歴上で「未知度の高い方向に戻る」ための一歩（1ノード分戻る）
            moveDir = (next.transform.position - transform.position).normalized;
            MoveForward();

            if (debugLog)
                Debug.Log($"[EXP-SELECT] {currentNode.name} → {next.name} (U={next.unknownCount})");
        }
        else
        {
            // 🔹ベストなノードが「現在ノード自身」だった場合など：
            // ここで新しい未リンク方向へ進む（結果的に新規Node開拓へ）
            if (debugLog)
                Debug.Log("[EXP-SELECT] best node is current → TryMoveToUnlinkedDirection()");
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
    private void TryMoveToUnlinkedDirection()
    {
        // 現在Nodeが存在しない場合は前進
        if (currentNode == null)
        {
            if (debugLog) Debug.Log("[EXP-DBG] currentNode=null → MoveForward()");
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
        if (debugLog) Debug.Log($"[EXP-DBG] All dirs: {DirListToString(allDirs)}");

        Vector3 backDir = (-moveDir).normalized;

        // ② 戻る(back)方向を除外
        List<Vector3> afterBack = new List<Vector3>();
        foreach (var d in allDirs)
        {
            if (Vector3.Dot(d.normalized, backDir) > 0.7f) continue;
            afterBack.Add(d);
        }
        if (debugLog) Debug.Log($"[EXP-DBG] After remove BACK ({DirToName(backDir)}): {DirListToString(afterBack)}");

        // ③ 既にリンク済みの方向を除外
        List<Vector3> afterLinked = new List<Vector3>();
        foreach (var d in afterBack)
        {
            bool linked = false;
            foreach (var link in currentNode.links)
            {
                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
                if (Vector3.Dot(diff, d.normalized) > 0.7f)
                {
                    linked = true;
                    if (debugLog) Debug.Log($"[EXP-DBG] LINKED dir removed: {DirToName(d)} (→ {link.name})");
                    break;
                }
            }
            if (!linked) afterLinked.Add(d);
        }
        if (debugLog) Debug.Log($"[EXP-DBG] After remove LINKED: {DirListToString(afterLinked)}");

        // ④ 壁方向を除外（Raycastで壁チェック）
        List<Vector3> validDirs = new List<Vector3>();
        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
        foreach (var d in afterLinked)
        {
            if (Physics.Raycast(origin, d, out RaycastHit hit, cellSize, wallLayer))
            {
                if (debugLog) Debug.Log($"[EXP-DBG] BLOCKED by Wall: {DirToName(d)} ({hit.collider.name})");
                continue;
            }
            validDirs.Add(d);
        }
        if (debugLog) Debug.Log($"[EXP-DBG] Final candidates: {DirListToString(validDirs)}");

        // ⑤ 候補が無い場合（往復防止処理）
        if (validDirs.Count == 0)
        {
            bool canContinue = false;
            Vector3 nextDir = Vector3.zero;

            // 現Nodeのリンク情報からback以外を探す
            foreach (var link in currentNode.links)
            {
                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
                if (Vector3.Dot(diff, backDir) < 0.7f) // back方向ではない
                {
                    canContinue = true;
                    nextDir = diff;
                    break;
                }
            }

            if (canContinue)
            {
                moveDir = nextDir;
                if (debugLog) Debug.Log($"[EXP-RESULT] No unlinked dirs → Follow existing link {DirToName(moveDir)}");
                MoveForward();
            }
            else
            {
                if (debugLog) Debug.Log("[EXP-RESULT] Only back dir left → Stop to avoid loop");
                // 往復防止のため停止
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
            Debug.Log($"[EXP-RESULT] Selected direction: {chosen}  /  Candidates: {all}  /  Node={currentNode.name}");
        }

        // ⑦ 実際に前進
        MoveForward();
    }

    // ======================================================
    // ChooseNextNodeByUnknown：未知数Uに基づいて最適ノードを選択
    // ------------------------------------------------------
    // ・recentNodes（直近 unknownReferenceDepth 個）の中で
    //   unknownCount(U) が最大のノードを「開拓優先ノード」とみなす
    // ・現在ノード ≠ 開拓優先ノード のとき：
    //     → 履歴を1ノード分だけ巻き戻す（過去方向へ戻る）
    // ・現在ノード ＝ 開拓優先ノード のとき：
    //     → ここで新規方向を開拓したいので null を返し、
    //        呼び出し側で TryMoveToUnlinkedDirection() を呼ぶ
    // ======================================================
    private MapNode ChooseNextNodeByUnknown(MapNode current)
    {
        if (current == null || current.links == null || current.links.Count == 0)
            return null;

        // 履歴や depth が無効なら、単純にリンク先の U が高いものを選択
        if (unknownReferenceDepth <= 0 || recentNodes.Count == 0)
        {
            return current.links
                .OrderByDescending(n => n != null ? n.unknownCount : 0)
                .ThenBy(_ => Random.value)
                .FirstOrDefault();
        }

        // 履歴から「未知度が最も高いノード」を探す
        MapNode bestNode = null;
        int bestU = -1;
        foreach (var n in recentNodes)
        {
            if (n == null) continue;
            if (n.unknownCount > bestU)
            {
                bestU = n.unknownCount;
                bestNode = n;
            }
        }

        // 履歴上で有望なノードが無ければ、単純にリンク先Uで選ぶ
        if (bestNode == null || bestU <= 0)
        {
            return current.links
                .OrderByDescending(n => n != null ? n.unknownCount : 0)
                .ThenBy(_ => Random.value)
                .FirstOrDefault();
        }

        // 現在ノードが履歴のどこにいるか
        int curIndex = recentNodes.LastIndexOf(current);

        if (curIndex <= 0)
        {
            // 履歴上で位置が特定できない/先頭の場合はローカル判定にフォールバック
            return current.links
                .OrderByDescending(n => n != null ? n.unknownCount : 0)
                .ThenBy(_ => Random.value)
                .FirstOrDefault();
        }

        // 「開拓優先ノード」が今いるノードなら、ここで新規方向を開拓したい
        if (bestNode == current)
        {
            if (debugLog)
                Debug.Log($"[EXP-HIST] Reached best node {current.name} (U={bestU}) → will try new direction");
            // 呼び出し側で TryMoveToUnlinkedDirection() を呼ばせるため null
            return null;
        }

        // bestNode に近づくため、「1ノード分だけ過去へ」戻る
        MapNode prevNode = recentNodes[curIndex - 1];

        // そのノードが現在ノードのリンクとして存在していれば、そこへ戻る
        if (prevNode != null && current.links.Contains(prevNode))
        {
            if (debugLog)
                Debug.Log($"[EXP-HIST] Backtrack {current.name} → {prevNode.name} (best={bestNode.name}, U={bestU})");
            return prevNode;
        }

        // もし履歴上の1つ前がリンクしていなければ、ローカルな U 判定にフォールバック
        return current.links
            .OrderByDescending(n => n != null ? n.unknownCount : 0)
            .ThenBy(_ => Random.value)
            .FirstOrDefault();
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
            Debug.LogWarning("[FollowSP] currentNode is null → 経路追従不可");
            isFollowingShortest = false;
            yield break;
        }

        // 見た目を赤に変更（防衛モード／最短経路モード）
        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
        if (debugLog) Debug.Log($"[FollowSP] === Start === current={currentNode.name}, Dist={currentNode.DistanceFromGoal}");

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
                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

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
                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
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
            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
        }
        else
        {
            // 新規ノードを生成
            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);
            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
        }

        // StartNodeの初期設定 — ここだけが StartNode を決める
        if (MapNode.StartNode == null)
        {
            MapNode.StartNode = node;
            node.distanceFromStart = 0;
            Debug.Log($"[StartNode] StartNode set to cell={node.cell}, pos={node.transform.position}");
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