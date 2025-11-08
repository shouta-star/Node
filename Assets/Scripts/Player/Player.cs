using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

public class Player : MonoBehaviour
{
    // ======================================================
    // ■ フィールド宣言
    // ======================================================

    [Header("移動設定")]
    public float moveSpeed = 3f;         // プレイヤーの移動速度
    public float cellSize = 1f;          // 1マスあたりの距離（グリッド間隔）
    public float rayDistance = 1f;       // 分岐判定などで使う短距離レイの距離
    public LayerMask wallLayer;          // 壁のレイヤー
    public LayerMask nodeLayer;          // Nodeのレイヤー

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward;   // 開始時の進行方向
    public Vector3 gridOrigin = Vector3.zero;          // グリッドの原点座標
    public MapNode goalNode;                           // 目標となるGoal Node
    public GameObject nodePrefab;                      // Nodeのプレハブ参照

    [Header("行動傾向")]
    [Range(0f, 1f)] public float exploreBias = 0.6f;   // 未探索方向を選ぶ確率（探索傾向）

    [Header("リンク探索")]
    public int linkRayMaxSteps = 100;   // Node間リンク探索での最大距離ステップ（セル単位）

    [Header("デバッグ")]
    public bool debugLog = true;        // コンソール出力ON/OFF
    public bool debugRay = true;        // レイをSceneビューに描画するか
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Material exploreMaterial;

    public static bool hasLearnedGoal = false; // 全プレイヤー共通の学習完了フラグ

    // 内部状態
    private Vector3 moveDir;            // 現在の進行方向
    private bool isMoving = false;      // 移動中フラグ
    private Vector3 targetPos;          // 現在の移動目標地点
    private MapNode currentNode;        // 現在立っているNode
    private bool reachedGoal = false;   // ゴール到達フラグ

    private Queue<MapNode> recentNodes = new Queue<MapNode>();
    private int recentLimit = 8;        // 直近訪問履歴の上限（※今は未使用）

    // ======================================================
    // ■ Start() : 初期化処理
    // ======================================================
    void Start()
    {
        // 初期方向と位置のスナップ
        moveDir = startDirection.normalized;
        targetPos = transform.position = SnapToGrid(transform.position);

        // 見た目初期化（探索中カラーなど）
        ApplyVisual();

        // 開始地点にNodeを設置 or 取得
        currentNode = TryPlaceNode(transform.position);

        if (goalNode == null)
        {
            // Goalという名前のオブジェクトを直接探す（Scene上の実体）
            GameObject goalObj = GameObject.Find("Goal");
            if (goalObj != null)
            {
                goalNode = goalObj.GetComponent<MapNode>();
                Debug.Log($"[Player] GoalNode assigned from Scene object: {goalNode.name}");
            }
            else
            {
                Debug.LogWarning("[Player] Goal object not found in scene!");
            }
        }

        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
    }

    // ======================================================
    // ■ Update() : 毎フレーム呼ばれるメインループ
    // ======================================================
    void Update()
    {
        if (!isMoving)
        {
            // 移動していない時：分岐点または前方が壁ならNode設置・探索へ
            if (CanPlaceNodeHere())
                TryExploreMove();
            else
                MoveForward(); // 通路なら前進
        }
        else
        {
            // 移動中：目標座標に向けて移動処理
            MoveToTarget();
        }
    }

    // ======================================================
    // ■ ApplyVisual() : プレイヤーの見た目設定
    // ======================================================
    private void ApplyVisual()
    {
        if (bodyRenderer == null) return;
        bodyRenderer.material = exploreMaterial
            ? exploreMaterial
            : new Material(Shader.Find("Standard")) { color = Color.cyan };
    }

    // ======================================================
    // ■ MoveForward() : 現在の方向に1マス分前進をセット
    // ======================================================
    void MoveForward()
    {
        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
        targetPos = nextPos;
        isMoving = true;
    }

    // ======================================================
    // ■ CanPlaceNodeHere() : Node設置可能か（分岐点 or 壁前）を判定
    // ======================================================
    bool CanPlaceNodeHere()
    {
        // 左右と前方の壁をチェックして分岐判定
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

        int openCount = 0;
        if (!frontHit) openCount++;
        if (!leftHit) openCount++;
        if (!rightHit) openCount++;

        // 前が壁 or 分岐方向が2つ以上ならNode設置対象
        return (frontHit || openCount >= 2);
    }

    // ======================================================
    // ■ TryExploreMove() : Node設置＋次の進行方向を決定
    // ======================================================
    //void TryExploreMove()
    //{
    //    // 現在地にNodeを設置 or 再取得（内部でLinkBackWithRayも実行）
    //    currentNode = TryPlaceNode(transform.position);
    //    if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

    //    // 進める方向候補を調べる
    //    var dirs = ScanAroundDirections();
    //    if (dirs.Count == 0)
    //    {
    //        if (debugLog) Debug.Log("[Player] No available directions");
    //        return;
    //    }

    //    // --- ① 終端Nodeなら未知方向を優先 ---
    //    bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
    //    if (isDeadEnd)
    //    {
    //        var unknownDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
    //        if (unknownDirs.Count > 0)
    //        {
    //            var chosen = unknownDirs[Random.Range(0, unknownDirs.Count)];
    //            moveDir = chosen.dir;
    //            MoveForward();
    //            if (debugLog) Debug.Log("[Player] Dead-end → move unexplored");
    //            return;
    //        }
    //    }

    //    // --- ② 通常時は探索傾向（exploreBias）に従って選択 ---
    //    bool chooseUnexplored = Random.value < exploreBias;
    //    var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
    //    var knownDirs = dirs.Where(d => d.node != null && d.hasLink).ToList();

    //    (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

    //    if (chooseUnexplored && unexploredDirs.Count > 0)
    //        chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
    //    else if (knownDirs.Count > 0)
    //        chosenDir = knownDirs[Random.Range(0, knownDirs.Count)];
    //    else if (unexploredDirs.Count > 0)
    //        chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];

    //    // --- ③ 実際に方向を確定して前進 ---
    //    if (chosenDir.HasValue)
    //    {
    //        moveDir = chosenDir.Value.dir;
    //        MoveForward();
    //        if (debugLog)
    //            Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
    //    }
    //}
    void TryExploreMove()
    {
        // 現在地にNodeを設置 or 再取得
        currentNode = TryPlaceNode(transform.position);
        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

        // 進める方向候補を調べる
        var dirs = ScanAroundDirections();
        if (dirs.Count == 0)
        {
            if (debugLog) Debug.Log("[Player] No available directions");
            return;
        }

        //// =====================================================
        //// 【③ 最短経路フェーズ】GoalNodeに一度でも到達した後
        //// =====================================================
        //if (reachedGoal)
        //{
        //    if (bodyRenderer != null)
        //        bodyRenderer.material.color = Color.red;

        //    // 既知ノードのみを対象に、Goalまでの距離が小さい方向を選ぶ
        //    var knownDirs = dirs.Where(d => d.node != null).ToList();
        //    if (knownDirs.Count > 0)
        //    {
        //        // DistanceFromGoalが最小のノード方向を選ぶ
        //        var best = knownDirs.OrderBy(d => d.node.DistanceFromGoal).First();
        //        moveDir = best.dir;
        //        MoveForward();

        //        if (debugLog)
        //            Debug.Log($"[Player] Follow shortest path → {best.dir} (Dist={best.node.DistanceFromGoal})");

        //        return;
        //    }
        //    else
        //    {
        //        // 近くにNodeが無い場合は通常探索へフォールバック
        //        if (debugLog) Debug.Log("[Player] No linked node found → fallback to explore mode");
        //    }
        //}
        //// =====================================================
        //// 【③ 最短経路フェーズ】GoalNodeに一度でも到達した後（全体共有）
        //// =====================================================
        //if (hasLearnedGoal)
        //{
        //    // ✅ Materialを赤に変更
        //    if (bodyRenderer != null)
        //        bodyRenderer.material.color = Color.red;

        //    // 既知ノードのみを対象に、Goalまでの距離が小さい方向を選ぶ
        //    var knownDirs = dirs.Where(d => d.node != null).ToList();
        //    if (knownDirs.Count > 0)
        //    {
        //        var best = knownDirs.OrderBy(d => d.node.DistanceFromGoal).First();
        //        moveDir = best.dir;
        //        MoveForward();

        //        if (debugLog)
        //            Debug.Log($"[Player] Follow shortest path → {best.dir} (Dist={best.node.DistanceFromGoal})");

        //        return;
        //    }
        //}
        //// =====================================================
        //// 【③ 最短経路フェーズ】GoalNodeに一度でも到達した後（全体共有）
        //// =====================================================
        //if (hasLearnedGoal && currentNode != null)
        //{
        //    // ✅ Materialを赤に変更
        //    if (bodyRenderer != null)
        //        bodyRenderer.material.color = Color.red;

        //    var knownDirs = dirs.Where(d => d.node != null).ToList();
        //    if (knownDirs.Count > 0)
        //    {
        //        int currentDist = currentNode.DistanceFromGoal;

        //        // ✅ 自分より小さい距離を持つノードだけに絞る
        //        var closerNodes = knownDirs
        //            .Where(d => d.node.DistanceFromGoal < currentDist)
        //            .ToList();

        //        if (closerNodes.Count > 0)
        //        {
        //            // その中で最も距離が小さい方向を選ぶ
        //            var best = closerNodes.OrderBy(d => d.node.DistanceFromGoal).First();
        //            moveDir = best.dir;
        //            MoveForward();

        //            if (debugLog)
        //                Debug.Log($"[Player] Move closer → {best.dir} (Now {currentDist} → Next {best.node.DistanceFromGoal})");
        //            return;
        //        }
        //        else
        //        {
        //            // ✅ すべて同距離または遠ざかる場合 → 終点または袋小路
        //            if (debugLog)
        //                Debug.Log($"[Player] No closer node (current {currentDist}) → stay or stop");
        //            return;
        //        }
        //    }
        //}
        // =====================================================
        // 【③ 最短経路フェーズ】GoalNodeに一度でも到達した後（全体共有）
        // =====================================================
        if (hasLearnedGoal && currentNode != null)
        {
            // すでに別の最短経路コルーチンが動作中ならスキップ
            StopAllCoroutines();

            // Goalまで自動で距離が減少する方向へ進む
            StartCoroutine(FollowShortestPath());
            return;
        }


        // =====================================================
        // 【① 探索フェーズ】GoalNode未発見時
        // =====================================================
        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
        bool chooseUnexplored = Random.value < exploreBias;

        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
        var knownDirs2 = dirs.Where(d => d.node != null && d.hasLink).ToList();

        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

        // 終端なら未知方向優先
        if (isDeadEnd && unexploredDirs.Count > 0)
        {
            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
        }
        else
        {
            if (chooseUnexplored && unexploredDirs.Count > 0)
                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
            else if (knownDirs2.Count > 0)
                chosenDir = knownDirs2[Random.Range(0, knownDirs2.Count)];
            else if (unexploredDirs.Count > 0)
                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
        }

        // --- 実際に前進 ---
        if (chosenDir.HasValue)
        {
            moveDir = chosenDir.Value.dir;
            MoveForward();

            if (debugLog)
                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
        }
    }

    // ======================================================
    // ■ FollowShortestPath() : GoalNodeまで最短経路を自動で辿る
    // ======================================================
    private System.Collections.IEnumerator FollowShortestPath()
    {
        if (currentNode == null)
        {
            Debug.LogWarning("[Player] Cannot follow shortest path: currentNode is null");
            yield break;
        }

        // 見た目：最短経路モード（赤）
        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;

        Debug.Log("[Player] Start following shortest path");

        while (currentNode != null && currentNode.DistanceFromGoal > 0)
        {
            int currentDist = currentNode.DistanceFromGoal;

            // ★ 物理スキャンではなく、リンクされたノードから選ぶ
            var closer = currentNode.links
                .Where(n => n != null && n.DistanceFromGoal < currentDist)
                .OrderBy(n => n.DistanceFromGoal)
                .FirstOrDefault();

            if (closer == null)
            {
                Debug.Log($"[Player] No closer linked node from {currentDist} → stop");
                yield break; // 近づけない＝リンク切れ
            }

            // 次ノード方向へ1マス進む（グリッド向けに正規化）
            Vector3 dir = (closer.transform.position - transform.position);
            dir.y = 0f;
            // 方向をグリッド軸にスナップ（誤差対策）
            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.z)) dir = new Vector3(Mathf.Sign(dir.x), 0, 0);
            else dir = new Vector3(0, 0, Mathf.Sign(dir.z));

            moveDir = dir.normalized;
            MoveForward();

            // 到着を待つ
            while (isMoving) yield return null;

            // 現在ノード更新（今いるセルのMapNode）
            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
            currentNode = MapNode.FindByCell(cell);

            if (currentNode == null)
            {
                Debug.LogWarning("[Player] Lost current node while following path");
                yield break;
            }

            Debug.Log($"[Player] Step: {currentDist} → {currentNode.DistanceFromGoal}");
        }

        Debug.Log("[Player] Reached GoalNode via shortest path!");
    }



    // ======================================================
    // ■ ScanAroundDirections() : 周囲4方向のNode状況を取得
    // ======================================================
    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
    {
        List<(Vector3, MapNode, bool)> found = new();
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in dirs)
        {
            // 壁があればその方向は除外
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
                continue;

            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
            Vector2Int nextCell = WorldToCell(nextPos);

            // 隣のセルにNodeがあるか調べる
            MapNode nextNode = MapNode.FindByCell(nextCell);
            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));

            found.Add((dir, nextNode, linked));
        }

        return found;
    }

    // ======================================================
    // ■ MoveToTarget() : 通路移動処理（リンクは行わない）
    // ======================================================
    void MoveToTarget()
    {
        // 現在位置からtargetPosへ向かって移動
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

        // 到達判定
        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
        {
            transform.position = targetPos;
            isMoving = false;

            if (debugLog)
                Debug.Log($"[MOVE][Arrived] pos={transform.position}");

            // 現在セルにNodeが存在すれば更新
            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
            MapNode node = MapNode.FindByCell(cell);
            currentNode = node;

            // ゴール判定（goalNodeの座標に到達したらDestroy）
            if (!reachedGoal && goalNode != null)
            {
                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));

                //if (playerCell == goalCell)
                //{
                //    reachedGoal = true;
                //    Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link by ray & destroy");

                //    // ✅ 通常の隣接ノード接続と同じ処理を行う
                //    if (currentNode != null)
                //        LinkBackWithRay(currentNode);

                //    // ゴール距離学習
                //    RecalculateGoalDistance();

                //    // Player削除
                //    Destroy(gameObject);
                //    return;
                //}
                if (playerCell == goalCell)
                {
                    reachedGoal = true;
                    Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link by ray & destroy");

                    // ✅ 通常の隣接ノード接続
                    if (currentNode != null)
                        LinkBackWithRay(currentNode);

                    // ✅ ゴール距離学習
                    RecalculateGoalDistance();

                    // ✅ 全プレイヤーへ「学習完了」を通知
                    hasLearnedGoal = true;
                    Debug.Log("[GLOBAL] Goal reached → all players now know the shortest path.");

                    // Player削除
                    Destroy(gameObject);
                    return;
                }

            }
        }
    }

    // ======================================================
    // ■ LinkBackWithRay() : Node背面へのレイキャストで接続確認
    // ======================================================
    private void LinkBackWithRay(MapNode node)
    {
        if (node == null) return;

        // --- Nodeの情報取得 ---
        Vector3 nodePos = node.transform.position;
        Quaternion nodeRot = node.transform.rotation;
        Vector3 nodeScale = node.transform.localScale;

        // --- レイキャスト設定 ---
        Vector3 origin = nodePos + Vector3.up * 0.1f;  // Nodeの少し上から発射
        Vector3 rawBack = -moveDir;                    // 進行方向の逆向き
        Vector3 backDir = rawBack.normalized;          // 正規化方向ベクトル
        LayerMask mask = wallLayer | nodeLayer;        // 衝突対象は壁＋Node

        // --- デバッグ出力 ---
        //Debug.Log(
        //    $"[NODE-RAY][LinkBack] node={node.name} pos={nodePos} " +
        //    $"origin={origin} dir(back)={backDir} " +
        //    $"cellSize={cellSize:F3} maxSteps={linkRayMaxSteps}"
        //);

        // --- レイを段階的に伸ばしてNodeを探索 ---
        for (int step = 1; step <= linkRayMaxSteps; step++)
        {
            float maxDist = cellSize * step;
            if (debugRay)
                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
            {
                int hitLayer = hit.collider.gameObject.layer;
                string layerName = LayerMask.LayerToName(hitLayer);
                Debug.Log($"[RAY-HIT][LinkBack] step={step} dist={hit.distance:F3} hit={hit.collider.name} layer={layerName}");

                // 壁に当たったら中断
                if ((wallLayer.value & (1 << hitLayer)) != 0)
                {
                    if (debugLog) Debug.Log($"[LINK-BLOCK] Wall hit first (hit={hit.collider.name})");
                    return;
                }

                // Nodeに当たったらリンク確立
                if ((nodeLayer.value & (1 << hitLayer)) != 0)
                {
                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
                    if (hitNode != null && hitNode != node)
                    {
                        node.AddLink(hitNode);
                        if (debugLog)
                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name} (dist={hit.distance:F2})");
                    }
                    return;
                }
            }
        }

        Debug.Log($"[LINK-NONE] node={node.name} no Node found behind (maxSteps={linkRayMaxSteps})");
    }

    // ======================================================
    // ■ RecalculateGoalDistance() : Goalから全Nodeの距離を再計算（BFS）
    // ======================================================
    void RecalculateGoalDistance()
    {
        if (goalNode == null) return;

        Queue<MapNode> queue = new Queue<MapNode>();
        foreach (var n in FindObjectsOfType<MapNode>())
            n.DistanceFromGoal = int.MaxValue;

        goalNode.DistanceFromGoal = 0;
        queue.Enqueue(goalNode);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var link in node.links)
            {
                int newDist = node.DistanceFromGoal + 1;
                if (newDist < link.DistanceFromGoal)
                {
                    link.DistanceFromGoal = newDist;
                    queue.Enqueue(link);
                }
            }
        }

        Debug.Log("[Player] Distance learning complete (Goal-based BFS)");
    }

    // ======================================================
    // ■ TryPlaceNode() : Nodeを新規 or 既存で設置し、背面リンクを実行
    // ======================================================
    MapNode TryPlaceNode(Vector3 pos)
    {
        Vector2Int cell = WorldToCell(SnapToGrid(pos));
        MapNode node;

        // 既に存在するNodeを再利用
        if (MapNode.allNodeCells.Contains(cell))
        {
            node = MapNode.FindByCell(cell);
            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
        }
        else
        {
            // 新規Node生成
            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);
            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
        }

        // Node確定後：常に背面リンクを実行（既存Nodeでも新方向接続を確認）
        if (node != null)
        {
            if (debugLog) Debug.Log($"[LINK] Check back connection for Node={node.name}");
            LinkBackWithRay(node);
        }

        return node;
    }

    // ======================================================
    // ■ 座標変換系ユーティリティ
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