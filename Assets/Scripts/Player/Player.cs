using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

public class Player : MonoBehaviour
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
    public int unknownReferenceDepth = 2; // 何ノード先まで未知数を参照するか

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
    private const float EPS = 1e-4f;

    // ======================================================
    // 各プレイヤー専用の通過履歴（順回路回避用）
    // ======================================================
    private Dictionary<MapNode, int> nodeVisitCount = new(); // ノードごとの通過回数
    private HashSet<MapNode> visitedNodes = new();            // 一度でも通過したノード記録

    // ======================================================
    // Start
    // ======================================================
    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position = SnapToGrid(transform.position);
        ApplyVisual();
        currentNode = TryPlaceNode(transform.position);

        if (goalNode == null)
        {
            GameObject goalObj = GameObject.Find("Goal");
            if (goalObj != null)
                goalNode = goalObj.GetComponent<MapNode>();
        }

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
    // Update
    // ======================================================
    void Update()
    {
        if (isFollowingShortest)
        {
            if (isMoving) MoveToTarget();
            return;
        }

        if (!isMoving)
        {
            if (CanPlaceNodeHere())
                TryExploreMove();
            else
                MoveForward();
        }
        else
        {
            MoveToTarget();
        }
    }

    // ======================================================
    // ApplyVisual
    // ======================================================
    private void ApplyVisual()
    {
        if (bodyRenderer == null) return;
        bodyRenderer.material = exploreMaterial
            ? exploreMaterial
            : new Material(Shader.Find("Standard")) { color = Color.cyan };
    }

    // ======================================================
    // CanPlaceNodeHere
    // ======================================================
    bool CanPlaceNodeHere()
    {
        if (isFollowingShortest) return false;

        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

        int openCount = 0;
        if (!frontHit) openCount++;
        if (!leftHit) openCount++;
        if (!rightHit) openCount++;

        return (frontHit || openCount >= 2);
    }

    // ======================================================
    // MoveForward
    // ======================================================
    void MoveForward()
    {
        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
        targetPos = nextPos;
        isMoving = true;
    }

    // ======================================================
    // MoveToTarget
    // ======================================================
    private void MoveToTarget()
    {
        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        }
        else
        {
            transform.position = targetPos;
            isMoving = false;
        }
    }

    // ======================================================
    // TryExploreMove：順回路回避・終端ノード対応版
    // ======================================================
    void TryExploreMove()
    {
        if (isFollowingShortest) return;

        currentNode = TryPlaceNode(transform.position);
        if (currentNode == null) return;

        // 通過履歴更新
        if (!visitedNodes.Contains(currentNode))
            visitedNodes.Add(currentNode);
        if (!nodeVisitCount.ContainsKey(currentNode))
            nodeVisitCount[currentNode] = 0;
        nodeVisitCount[currentNode]++;

        if (debugLog)
            Debug.Log($"[EXP] Node {currentNode.name} passCount={nodeVisitCount[currentNode]}");

        // 🔹 終端ノードなら未接続方向を探索
        if (IsTerminalNode(currentNode))
        {
            if (debugLog)
                Debug.Log($"[EXP] Terminal node detected ({currentNode.name}) → TryMoveToUnlinkedDirection()");
            TryMoveToUnlinkedDirection();
            return;
        }

        // 🔹 リンクが無い場合は新規探索
        if (currentNode.links.Count == 0)
        {
            TryMoveToUnlinkedDirection();
            return;
        }

        // --- 次ノード選択 ---
        MapNode next = null;

        // ① 未訪問ノードを優先
        var unvisited = currentNode.links.Where(n => !visitedNodes.Contains(n)).ToList();
        if (unvisited.Count > 0)
        {
            next = unvisited[Random.Range(0, unvisited.Count)];
        }
        else
        {
            // ② 全通過済み → 通過回数が最小のノードへ
            next = currentNode.links
                .OrderBy(n => nodeVisitCount.ContainsKey(n) ? nodeVisitCount[n] : 0)
                .ThenBy(x => Random.value)
                .FirstOrDefault();
        }

        if (next != null)
        {
            moveDir = (next.transform.position - transform.position).normalized;
            MoveForward();

            if (debugLog)
                Debug.Log($"[EXP] Move to {next.name} (pass={nodeVisitCount[next]})");
        }
        else
        {
            if (debugLog)
                Debug.Log("[EXP] No valid next node → stop");
        }
    }

    // ======================================================
    // IsTerminalNode：リンクが1方向のみのノードを終端と判定
    // ======================================================
    private bool IsTerminalNode(MapNode node)
    {
        return node != null && node.links != null && node.links.Count == 1;
    }

    // ======================================================
    // TryMoveToUnlinkedDirection：未リンク方向の探索（往復防止）
    // ======================================================
    private void TryMoveToUnlinkedDirection()
    {
        if (currentNode == null)
        {
            MoveForward();
            return;
        }

        List<Vector3> allDirs = new List<Vector3>
        {
            Vector3.forward,
            Vector3.back,
            Vector3.left,
            Vector3.right
        };

        Vector3 backDir = (-moveDir).normalized;

        // 戻る方向を除外
        var candidates = allDirs.Where(d => Vector3.Dot(d, backDir) < 0.7f).ToList();

        // 既にリンク済み or 壁方向を除外
        List<Vector3> validDirs = new();
        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
        foreach (var d in candidates)
        {
            bool linked = currentNode.links.Any(link =>
                Vector3.Dot((link.transform.position - currentNode.transform.position).normalized, d.normalized) > 0.7f);
            if (linked) continue;

            if (Physics.Raycast(origin, d, out RaycastHit hit, cellSize, wallLayer))
                continue;

            validDirs.Add(d);
        }

        if (validDirs.Count == 0)
        {
            if (debugLog) Debug.Log("[EXP] Dead-end → stop (avoid loop)");
            return;
        }

        moveDir = validDirs[Random.Range(0, validDirs.Count)];
        MoveForward();

        if (debugLog)
            Debug.Log($"[EXP] Selected new direction: {moveDir}");
    }

    // ======================================================
    // FollowShortestPath（既存）
    // ======================================================
    private IEnumerator FollowShortestPath()
    {
        if (currentNode == null)
        {
            Debug.LogWarning("[FollowSP] currentNode is null → 経路追従不可");
            isFollowingShortest = false;
            yield break;
        }

        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
        if (debugLog) Debug.Log($"[FollowSP] === Start === current={currentNode.name}, Dist={currentNode.DistanceFromGoal}");

        isFollowingShortest = true;

        while (currentNode != null && currentNode.DistanceFromGoal > EPS)
        {
            float currentDist = currentNode.DistanceFromGoal;

            var nextNode = currentNode.links
                .Where(n => n != null && n.DistanceFromGoal < currentDist - EPS)
                .OrderBy(n => n.DistanceFromGoal)
                .FirstOrDefault();

            if (nextNode == null) break;

            Vector3 target = nextNode.transform.position;
            moveDir = (target - transform.position).normalized;

            while (Vector3.Distance(transform.position, target) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
                yield return null;
            }

            currentNode = nextNode;
            transform.position = currentNode.transform.position;

            if (currentNode.DistanceFromGoal <= EPS || (goalNode != null && currentNode == goalNode))
            {
                reachedGoal = true;
                if (currentNode != null) LinkBackWithRay(currentNode);
                RecalculateGoalDistance();
                hasLearnedGoal = true;
                isFollowingShortest = false;
                Destroy(gameObject);
                yield break;
            }
        }

        isFollowingShortest = false;
    }

    // ======================================================
    // LinkBackWithRay（既存）
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
                if ((wallLayer.value & (1 << hitLayer)) != 0) return;

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
    // RecalculateGoalDistance（既存）
    // ======================================================
    void RecalculateGoalDistance()
    {
        if (goalNode == null) return;

        foreach (var n in FindObjectsOfType<MapNode>())
            n.DistanceFromGoal = Mathf.Infinity;

        goalNode.DistanceFromGoal = 0f;
        var frontier = new List<MapNode> { goalNode };

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
    // TryPlaceNode
    // ======================================================
    MapNode TryPlaceNode(Vector3 pos)
    {
        if (isFollowingShortest)
        {
            Vector2Int c = WorldToCell(SnapToGrid(pos));
            return MapNode.FindByCell(c);
        }

        Vector2Int cell = WorldToCell(SnapToGrid(pos));
        MapNode node;

        if (MapNode.allNodeCells.Contains(cell))
        {
            node = MapNode.FindByCell(cell);
            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
        }
        else
        {
            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);
            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
        }

        if (node != null)
            LinkBackWithRay(node);

        return node;
    }

    // ======================================================
    // 座標変換ユーティリティ
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

//using UnityEngine;
//using System.Collections.Generic;
//using System.Linq;
//using System.Collections;

//public class Player : MonoBehaviour
//{
//    // ======================================================
//    // パラメータ設定
//    // ======================================================

//    [Header("移動設定")]
//    public float moveSpeed = 3f;            // プレイヤーの移動速度
//    public float cellSize = 1f;             // 1マスのサイズ
//    public float rayDistance = 1f;          // 壁判定のためのレイ距離
//    public LayerMask wallLayer;             // 壁レイヤー
//    public LayerMask nodeLayer;             // ノードレイヤー

//    [Header("初期設定")]
//    public Vector3 startDirection = Vector3.forward; // 初期移動方向
//    public Vector3 gridOrigin = Vector3.zero;        // グリッド原点
//    public MapNode goalNode;                         // ゴールノード参照
//    public GameObject nodePrefab;                    // ノードプレハブ

//    [Header("行動傾向")]
//    [Range(0f, 1f)] public float exploreBias = 0.6f; // 探索（未知方向）に進む確率

//    [Header("探索パラメータ")]
//    public int unknownReferenceDepth = 2; // 🔹何ノード先まで未知数を参照するか

//    [Header("リンク探索")]
//    public int linkRayMaxSteps = 100; // ノード間リンクを探す際の最大レイ長

//    [Header("デバッグ")]
//    public bool debugLog = true;      // ログ出力ON/OFF
//    public bool debugRay = true;      // レイ表示ON/OFF
//    [SerializeField] private Renderer bodyRenderer;  // 見た目
//    [SerializeField] private Material exploreMaterial; // 探索時マテリアル

//    public static bool hasLearnedGoal = false; // ゴール情報を学習済みかどうか

//    // ======================================================
//    // 内部状態変数
//    // ======================================================

//    private Vector3 moveDir;          // 現在の移動方向
//    private bool isMoving = false;    // 移動中フラグ
//    private Vector3 targetPos;        // 移動目標座標
//    private MapNode currentNode;      // 現在いるノード
//    private bool reachedGoal = false; // ゴール到達フラグ
//    private bool isFollowingShortest = false; // 最短経路追従中フラグ

//    private const float EPS = 1e-4f;  // 浮動小数誤差対策

//    // ======================================================
//    // Start：初期化処理
//    // ======================================================
//    void Start()
//    {
//        moveDir = startDirection.normalized;         // 初期移動方向設定
//        targetPos = transform.position = SnapToGrid(transform.position); // グリッド位置にスナップ
//        ApplyVisual();                               // プレイヤーの見た目設定
//        currentNode = TryPlaceNode(transform.position); // 現在位置にノード設置 or 取得

//        // ゴールノードが未設定なら自動検索
//        if (goalNode == null)
//        {
//            GameObject goalObj = GameObject.Find("Goal");
//            if (goalObj != null)
//                goalNode = goalObj.GetComponent<MapNode>();
//        }

//        // ゴール学習済みなら最短経路追従を開始
//        if (hasLearnedGoal)
//        {
//            isFollowingShortest = true;
//            StopAllCoroutines();
//            StartCoroutine(FollowShortestPath());
//            return;
//        }

//        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
//    }

//    // ======================================================
//    // Update：毎フレームの動作制御
//    // ======================================================
//    void Update()
//    {
//        // 最短経路追従中は移動処理のみ
//        if (isFollowingShortest)
//        {
//            if (isMoving) MoveToTarget();
//            return;
//        }

//        // 通常探索モード時の分岐
//        if (!isMoving)
//        {
//            if (CanPlaceNodeHere())     // 十字路 or 壁前ならノードを設置して方向選択
//                TryExploreMove();
//            else                        // 通常前進
//                MoveForward();
//        }
//        else
//        {
//            MoveToTarget();
//        }
//    }

//    // ======================================================
//    // ApplyVisual：プレイヤーの見た目設定
//    // ======================================================
//    private void ApplyVisual()
//    {
//        if (bodyRenderer == null) return;
//        bodyRenderer.material = exploreMaterial
//            ? exploreMaterial
//            : new Material(Shader.Find("Standard")) { color = Color.cyan };
//    }

//    // ======================================================
//    // CanPlaceNodeHere：ノードを設置すべき位置かを判定
//    // ======================================================
//    bool CanPlaceNodeHere()
//    {
//        if (isFollowingShortest) return false;

//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        // 周囲に壁があるかを判定
//        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
//        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
//        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

//        int openCount = 0;
//        if (!frontHit) openCount++;
//        if (!leftHit) openCount++;
//        if (!rightHit) openCount++;

//        // 壁前 or 分岐点ならノード設置対象
//        return (frontHit || openCount >= 2);
//    }

//    // ======================================================
//    // MoveForward：現在方向に1マス進む
//    // ======================================================
//    void MoveForward()
//    {
//        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
//        targetPos = nextPos;
//        isMoving = true;
//    }

//    // ======================================================
//    // MoveToTarget：ターゲット位置に向かって移動し、到達時に停止
//    // ======================================================
//    private void MoveToTarget()
//    {
//        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
//        {
//            // 移動中：ターゲットに向かって移動
//            transform.position = Vector3.MoveTowards(
//                transform.position,
//                targetPos,
//                moveSpeed * Time.deltaTime
//            );
//        }
//        else
//        {
//            // 目標地点に到達
//            transform.position = targetPos;
//            isMoving = false;
//        }
//    }

//    //// ======================================================
//    //// TryExploreMove：未知数(U)に基づいて次のノードを決定
//    //// ======================================================
//    //void TryExploreMove()
//    //{
//    //    if (isFollowingShortest) return;

//    //    currentNode = TryPlaceNode(transform.position);
//    //    if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

//    //    if (currentNode == null || currentNode.links.Count == 0)
//    //    {
//    //        MoveForward();
//    //        return;
//    //    }

//    //    // 🔹未知数(U)の多い方向を優先して移動先を決定
//    //    MapNode next = ChooseNextNodeByUnknown(currentNode);

//    //    if (next != null)
//    //    {
//    //        moveDir = (next.transform.position - transform.position).normalized;
//    //        MoveForward();

//    //        if (debugLog)
//    //            Debug.Log($"[EXP-SELECT] {currentNode.name} → {next.name} (U={next.unknownCount})");
//    //    }
//    //    else
//    //    {
//    //        // リンクが無い場合は単純前進
//    //        MoveForward();
//    //    }
//    //}
//    // ======================================================
//    // TryExploreMove：未知数(U)に基づいて次のノードを決定
//    // ======================================================
//    //void TryExploreMove()
//    //{
//    //    if (isFollowingShortest) return;

//    //    currentNode = TryPlaceNode(transform.position);
//    //    if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

//    //    // 現在ノードが存在しない or リンクが全くない場合は新規探索
//    //    if (currentNode == null || currentNode.links.Count == 0)
//    //    {
//    //        TryMoveToUnlinkedDirection(); // ← 新規探索処理へ
//    //        return;
//    //    }

//    //    // 通常の未知数Uベース探索
//    //    MapNode next = ChooseNextNodeByUnknown(currentNode);

//    //    if (next != null)
//    //    {
//    //        moveDir = (next.transform.position - transform.position).normalized;
//    //        MoveForward();

//    //        if (debugLog)
//    //            Debug.Log($"[EXP-SELECT] {currentNode.name} → {next.name} (U={next.unknownCount})");
//    //    }
//    //    else
//    //    {
//    //        // 🔹 終端Node（リンク方向なし）→ 未接続方向を探索
//    //        TryMoveToUnlinkedDirection();
//    //    }
//    //}
//    // ======================================================
//    // TryExploreMove：未知数(U)に基づいて次のノードを決定
//    // ======================================================
//    void TryExploreMove()
//    {
//        if (isFollowingShortest) return;

//        currentNode = TryPlaceNode(transform.position);
//        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

//        // 🔹 終端ノード（リンクが1つだけ）なら新しい方向を探索
//        if (IsTerminalNode(currentNode))
//        {
//            if (debugLog) Debug.Log($"[EXP] Terminal node detected ({currentNode.name}) → TryMoveToUnlinkedDirection()");
//            TryMoveToUnlinkedDirection();
//            return;
//        }

//        // 現在ノードが存在しない or リンクが全くない場合は新規探索
//        if (currentNode == null || currentNode.links.Count == 0)
//        {
//            TryMoveToUnlinkedDirection(); // ← 新規探索処理へ
//            return;
//        }

//        // 通常の未知数Uベース探索
//        MapNode next = ChooseNextNodeByUnknown(currentNode);

//        if (next != null)
//        {
//            moveDir = (next.transform.position - transform.position).normalized;
//            MoveForward();

//            if (debugLog)
//                Debug.Log($"[EXP-SELECT] {currentNode.name} → {next.name} (U={next.unknownCount})");
//        }
//        else
//        {
//            // 🔹 終端Node（リンク方向なし）→ 未接続方向を探索
//            TryMoveToUnlinkedDirection();
//        }
//    }

//    // ======================================================
//    // IsTerminalNode：リンクが1方向のみのノードを終端と判定
//    // ======================================================
//    private bool IsTerminalNode(MapNode node)
//    {
//        return node != null && node.links != null && node.links.Count == 1;
//    }


//    // ==============================
//    // 方向ベクトル → ラベル化 (F/B/L/R)
//    // ==============================
//    private string DirToName(Vector3 dir)
//    {
//        Vector3 n = dir.normalized;
//        if (Vector3.Dot(n, Vector3.forward) > 0.7f) return "F";
//        if (Vector3.Dot(n, Vector3.back) > 0.7f) return "B";
//        if (Vector3.Dot(n, Vector3.left) > 0.7f) return "L";
//        if (Vector3.Dot(n, Vector3.right) > 0.7f) return "R";
//        return $"({n.x:0.##},{n.y:0.##},{n.z:0.##})";
//    }

//    // ==============================
//    // 候補配列 → "F,R,..." 形式の文字列
//    // ==============================
//    private string DirListToString(List<Vector3> list)
//    {
//        if (list == null || list.Count == 0) return "(none)";
//        System.Text.StringBuilder sb = new System.Text.StringBuilder();
//        for (int i = 0; i < list.Count; i++)
//        {
//            if (i > 0) sb.Append(", ");
//            sb.Append(DirToName(list[i]));
//        }
//        return sb.ToString();
//    }

//    // ======================================================
//    // TryMoveToUnlinkedDirection
//    // ------------------------------------------------------
//    // ・終端Node（リンクが少ない/未探索方向がある）で進行方向を決定
//    // ・戻る(back)方向は除外
//    // ・リンク済み・壁方向は除外
//    // ・候補が無い場合はback方向を避けて停止（往復防止）
//    // ======================================================
//    private void TryMoveToUnlinkedDirection()
//    {
//        // 現在Nodeが存在しない場合は前進
//        if (currentNode == null)
//        {
//            if (debugLog) Debug.Log("[EXP-DBG] currentNode=null → MoveForward()");
//            MoveForward();
//            return;
//        }

//        // ① 全方向初期化
//        List<Vector3> allDirs = new List<Vector3>
//    {
//        Vector3.forward,
//        Vector3.back,
//        Vector3.left,
//        Vector3.right
//    };
//        if (debugLog) Debug.Log($"[EXP-DBG] All dirs: {DirListToString(allDirs)}");

//        Vector3 backDir = (-moveDir).normalized;

//        // ② 戻る(back)方向を除外
//        List<Vector3> afterBack = new List<Vector3>();
//        foreach (var d in allDirs)
//        {
//            if (Vector3.Dot(d.normalized, backDir) > 0.7f) continue;
//            afterBack.Add(d);
//        }
//        if (debugLog) Debug.Log($"[EXP-DBG] After remove BACK ({DirToName(backDir)}): {DirListToString(afterBack)}");

//        // ③ 既にリンク済みの方向を除外
//        List<Vector3> afterLinked = new List<Vector3>();
//        foreach (var d in afterBack)
//        {
//            bool linked = false;
//            foreach (var link in currentNode.links)
//            {
//                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
//                if (Vector3.Dot(diff, d.normalized) > 0.7f)
//                {
//                    linked = true;
//                    if (debugLog) Debug.Log($"[EXP-DBG] LINKED dir removed: {DirToName(d)} (→ {link.name})");
//                    break;
//                }
//            }
//            if (!linked) afterLinked.Add(d);
//        }
//        if (debugLog) Debug.Log($"[EXP-DBG] After remove LINKED: {DirListToString(afterLinked)}");

//        // ④ 壁方向を除外（Raycastで壁チェック）
//        List<Vector3> validDirs = new List<Vector3>();
//        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
//        foreach (var d in afterLinked)
//        {
//            if (Physics.Raycast(origin, d, out RaycastHit hit, cellSize, wallLayer))
//            {
//                if (debugLog) Debug.Log($"[EXP-DBG] BLOCKED by Wall: {DirToName(d)} ({hit.collider.name})");
//                continue;
//            }
//            validDirs.Add(d);
//        }
//        if (debugLog) Debug.Log($"[EXP-DBG] Final candidates: {DirListToString(validDirs)}");

//        // ⑤ 候補が無い場合（往復防止処理）
//        if (validDirs.Count == 0)
//        {
//            bool canContinue = false;
//            Vector3 nextDir = Vector3.zero;

//            // 現Nodeのリンク情報からback以外を探す
//            foreach (var link in currentNode.links)
//            {
//                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
//                if (Vector3.Dot(diff, backDir) < 0.7f) // back方向ではない
//                {
//                    canContinue = true;
//                    nextDir = diff;
//                    break;
//                }
//            }

//            if (canContinue)
//            {
//                moveDir = nextDir;
//                if (debugLog) Debug.Log($"[EXP-RESULT] No unlinked dirs → Follow existing link {DirToName(moveDir)}");
//                MoveForward();
//            }
//            else
//            {
//                if (debugLog) Debug.Log("[EXP-RESULT] Only back dir left → Stop to avoid loop");
//                // 往復防止のため停止
//                return;
//            }

//            return;
//        }

//        // ⑥ 候補からランダムに選択
//        moveDir = validDirs[UnityEngine.Random.Range(0, validDirs.Count)];

//        if (debugLog)
//        {
//            string all = DirListToString(validDirs);
//            string chosen = DirToName(moveDir);
//            Debug.Log($"[EXP-RESULT] Selected direction: {chosen}  /  Candidates: {all}  /  Node={currentNode.name}");
//        }

//        // ⑦ 実際に前進
//        MoveForward();
//    }

//    //// ======================================================
//    //// TryMoveToUnlinkedDirection：リンクされていない方向に進む
//    ////  - 終端Nodeで呼ばれる
//    ////  - 戻る方向（back）は除外
//    ////  - 壁がある方向も除外（Raycastでチェック）
//    //// ======================================================
//    //private void TryMoveToUnlinkedDirection()
//    //{
//    //    if (currentNode == null)
//    //    {
//    //        MoveForward();
//    //        return;
//    //    }

//    //    Vector3[] directions =
//    //    {
//    //    Vector3.forward,
//    //    Vector3.back,
//    //    Vector3.left,
//    //    Vector3.right
//    //};

//    //    Vector3 backDir = (-moveDir).normalized;
//    //    List<Vector3> validDirs = new List<Vector3>();

//    //    foreach (var dir in directions)
//    //    {
//    //        // --- ① 戻る方向は除外 ---
//    //        if (Vector3.Dot(dir.normalized, backDir) > 0.7f)
//    //            continue;

//    //        // --- ② 既にリンク済み方向は除外 ---
//    //        bool linked = false;
//    //        foreach (var link in currentNode.links)
//    //        {
//    //            Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
//    //            if (Vector3.Dot(diff, dir.normalized) > 0.7f)
//    //            {
//    //                linked = true;
//    //                break;
//    //            }
//    //        }
//    //        if (linked) continue;

//    //        // --- ③ 壁判定：Raycastで1セル先に壁があるか確認 ---
//    //        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
//    //        if (Physics.Raycast(origin, dir, out RaycastHit hit, cellSize, wallLayer))
//    //        {
//    //            if (debugLog)
//    //                Debug.Log($"[EXP-BLOCK] Direction {dir} blocked by wall ({hit.collider.name})");
//    //            continue; // 壁に当たる方向は除外
//    //        }

//    //        // --- ④ 壁がなく、リンクされていない方向のみ有効候補に ---
//    //        validDirs.Add(dir);
//    //    }

//    //    // --- 候補が無ければ、現方向で前進継続 ---
//    //    if (validDirs.Count == 0)
//    //    {
//    //        if (debugLog)
//    //            Debug.Log("[EXP-END] No valid unlinked direction found, continue current dir.");
//    //        MoveForward();
//    //        return;
//    //    }

//    //    // --- 候補からランダム選択 ---
//    //    moveDir = validDirs[Random.Range(0, validDirs.Count)];

//    //    if (debugLog)
//    //        Debug.Log($"[EXP-NEW] Selected new unlinked dir (no wall/back): {moveDir}");

//    //    MoveForward();
//    //}

//    // ======================================================
//    // ChooseNextNodeByUnknown：未知数Uに基づいて最適ノードを選択
//    // ======================================================
//    private MapNode ChooseNextNodeByUnknown(MapNode current)
//    {
//        if (current == null || current.links.Count == 0) return null;

//        // 🔸unknownReferenceDepth分だけ先まで探索し、平均未知数が最も多い方向を選ぶ
//        MapNode next = current.links
//            .OrderByDescending(n => GetAverageUnknown(n, unknownReferenceDepth))
//            .ThenBy(x => Random.value)
//            .FirstOrDefault();

//        return next;
//    }

//    // ======================================================
//    // GetAverageUnknown：指定深度まで再帰的に未知数を平均化
//    // ======================================================
//    private float GetAverageUnknown(MapNode node, int depth)
//    {
//        if (node == null || depth <= 0) return 0f;

//        HashSet<MapNode> visited = new();
//        (float total, int count) = GetUnknownRecursive(node, depth, visited);

//        return count > 0 ? total / count : 0f;
//    }

//    // ======================================================
//    // GetUnknownRecursive：再帰的に未知数(U)を合計してノード数をカウント
//    // ======================================================
//    private (float, int) GetUnknownRecursive(MapNode node, int depth, HashSet<MapNode> visited)
//    {
//        if (node == null || depth <= 0 || visited.Contains(node))
//            return (0f, 0);

//        visited.Add(node);

//        float total = node.unknownCount;
//        int count = 1;

//        foreach (var link in node.links)
//        {
//            (float sub, int subCount) = GetUnknownRecursive(link, depth - 1, visited);
//            total += sub;
//            count += subCount;
//        }

//        return (total, count);
//    }

//    // ======================================================
//    // FollowShortestPath：学習後に最短経路をたどる処理
//    // ======================================================
//    private IEnumerator FollowShortestPath()
//    {
//        if (currentNode == null)
//        {
//            Debug.LogWarning("[FollowSP] currentNode is null → 経路追従不可");
//            isFollowingShortest = false;
//            yield break;
//        }

//        // 見た目を赤に変更（防衛モード）
//        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
//        if (debugLog) Debug.Log($"[FollowSP] === Start === current={currentNode.name}, Dist={currentNode.DistanceFromGoal}");

//        isFollowingShortest = true;
//        int stepCount = 0;

//        while (currentNode != null && currentNode.DistanceFromGoal > EPS)
//        {
//            stepCount++;
//            float currentDist = currentNode.DistanceFromGoal;

//            // 距離が短くなる方向のノードを選択
//            var nextNode = currentNode.links
//                .Where(n => n != null && n.DistanceFromGoal < currentDist - EPS)
//                .OrderBy(n => n.DistanceFromGoal)
//                .FirstOrDefault();

//            if (nextNode == null) break;

//            Vector3 target = nextNode.transform.position;
//            Vector3 dir = (target - transform.position); dir.y = 0f;
//            moveDir = dir.normalized;

//            // 実際に移動
//            while (Vector3.Distance(transform.position, target) > 0.01f)
//            {
//                transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
//                yield return null;
//            }

//            currentNode = nextNode;
//            transform.position = currentNode.transform.position;

//            // ゴール到達時の処理
//            if (currentNode.DistanceFromGoal <= EPS || (goalNode != null && currentNode == goalNode))
//            {
//                reachedGoal = true;
//                if (currentNode != null)
//                    LinkBackWithRay(currentNode);

//                RecalculateGoalDistance();
//                hasLearnedGoal = true;

//                isFollowingShortest = false;
//                Destroy(gameObject); // 自身を削除
//                yield break;
//            }
//        }

//        isFollowingShortest = false;
//    }

//    // ======================================================
//    // LinkBackWithRay：後方方向にノードを探してリンクを追加
//    // ======================================================
//    private void LinkBackWithRay(MapNode node)
//    {
//        if (node == null) return;

//        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//        Vector3 backDir = -moveDir.normalized;
//        LayerMask mask = wallLayer | nodeLayer;

//        for (int step = 1; step <= linkRayMaxSteps; step++)
//        {
//            float maxDist = cellSize * step;
//            if (debugRay)
//                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

//            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
//            {
//                int hitLayer = hit.collider.gameObject.layer;

//                if ((wallLayer.value & (1 << hitLayer)) != 0)
//                    return;

//                if ((nodeLayer.value & (1 << hitLayer)) != 0)
//                {
//                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
//                    if (hitNode != null && hitNode != node)
//                    {
//                        node.AddLink(hitNode);
//                        node.RecalculateUnknownAndWall();
//                        hitNode.RecalculateUnknownAndWall();

//                        if (debugLog)
//                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
//                    }
//                    return;
//                }
//            }
//        }
//    }

//    // ======================================================
//    // RecalculateGoalDistance：ゴールからの距離を再計算（Dijkstra法）
//    // ======================================================
//    void RecalculateGoalDistance()
//    {
//        if (goalNode == null) return;

//        // 全ノードを初期化
//        foreach (var n in FindObjectsOfType<MapNode>())
//            n.DistanceFromGoal = Mathf.Infinity;

//        goalNode.DistanceFromGoal = 0f;
//        var frontier = new List<MapNode> { goalNode };

//        // Dijkstra探索で全ノード距離更新
//        while (frontier.Count > 0)
//        {
//            frontier.Sort((a, b) => a.DistanceFromGoal.CompareTo(b.DistanceFromGoal));
//            var node = frontier[0];
//            frontier.RemoveAt(0);

//            foreach (var link in node.links)
//            {
//                if (link == null) continue;

//                float newDist = node.DistanceFromGoal + node.EdgeCost(link);
//                if (newDist < link.DistanceFromGoal)
//                {
//                    link.DistanceFromGoal = newDist;
//                    if (!frontier.Contains(link))
//                        frontier.Add(link);
//                }
//            }
//        }
//    }

//    // ======================================================
//    // TryPlaceNode：現在位置にノードを設置または再利用
//    // ======================================================
//    MapNode TryPlaceNode(Vector3 pos)
//    {
//        // 最短経路中は既存ノードを再利用
//        if (isFollowingShortest)
//        {
//            Vector2Int c = WorldToCell(SnapToGrid(pos));
//            return MapNode.FindByCell(c);
//        }

//        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//        MapNode node;

//        // 既に存在するノードなら再利用
//        if (MapNode.allNodeCells.Contains(cell))
//        {
//            node = MapNode.FindByCell(cell);
//            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
//        }
//        else
//        {
//            // 新規ノードを生成
//            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//            node = obj.GetComponent<MapNode>();
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
//        }

//        // 周囲とのリンクを更新
//        if (node != null)
//            LinkBackWithRay(node);

//        return node;
//    }

//    // ======================================================
//    // 座標変換ユーティリティ群
//    // ======================================================
//    Vector2Int WorldToCell(Vector3 worldPos)
//    {
//        Vector3 p = worldPos - gridOrigin;
//        int cx = Mathf.RoundToInt(p.x / cellSize);
//        int cz = Mathf.RoundToInt(p.z / cellSize);
//        return new Vector2Int(cx, cz);
//    }

//    Vector3 CellToWorld(Vector2Int cell)
//    {
//        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
//    }

//    Vector3 SnapToGrid(Vector3 worldPos)
//    {
//        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
//        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
//        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
//    }
//}

////using UnityEngine;
////using System.Collections.Generic;
////using System.Linq;
////using System.Collections;

////public class Player : MonoBehaviour
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
////    public MapNode goalNode;
////    public GameObject nodePrefab;

////    [Header("行動傾向")]
////    [Range(0f, 1f)] public float exploreBias = 0.6f;

////    [Header("リンク探索")]
////    public int linkRayMaxSteps = 100;

////    [Header("デバッグ")]
////    public bool debugLog = true;
////    public bool debugRay = true;
////    [SerializeField] private Renderer bodyRenderer;
////    [SerializeField] private Material exploreMaterial;

////    public static bool hasLearnedGoal = false;

////    // 内部状態
////    private Vector3 moveDir;
////    private bool isMoving = false;
////    private Vector3 targetPos;
////    private MapNode currentNode;
////    private bool reachedGoal = false;
////    private bool isFollowingShortest = false;

////    private const float EPS = 1e-4f;

////    // ======================================================
////    // Start
////    // ======================================================
////    void Start()
////    {
////        moveDir = startDirection.normalized;
////        targetPos = transform.position = SnapToGrid(transform.position);
////        ApplyVisual();
////        currentNode = TryPlaceNode(transform.position);

////        if (goalNode == null)
////        {
////            GameObject goalObj = GameObject.Find("Goal");
////            if (goalObj != null)
////                goalNode = goalObj.GetComponent<MapNode>();
////        }

////        // すでにGoal学習済みなら最短経路モードへ
////        if (hasLearnedGoal)
////        {
////            isFollowingShortest = true;
////            StopAllCoroutines();
////            StartCoroutine(FollowShortestPath());
////            return;
////        }

////        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
////    }

////    // ======================================================
////    // Update
////    // ======================================================
////    void Update()
////    {
////        if (isFollowingShortest)
////        {
////            if (isMoving) MoveToTarget();
////            return;
////        }

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

////    // ======================================================
////    // Visual
////    // ======================================================
////    private void ApplyVisual()
////    {
////        if (bodyRenderer == null) return;
////        bodyRenderer.material = exploreMaterial
////            ? exploreMaterial
////            : new Material(Shader.Find("Standard")) { color = Color.cyan };
////    }

////    // ======================================================
////    // CanPlaceNodeHere
////    // ======================================================
////    bool CanPlaceNodeHere()
////    {
////        if (isFollowingShortest) return false;

////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
////        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
////        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

////        int openCount = 0;
////        if (!frontHit) openCount++;
////        if (!leftHit) openCount++;
////        if (!rightHit) openCount++;

////        return (frontHit || openCount >= 2);
////    }

////    // ======================================================
////    // MoveForward
////    // ======================================================
////    void MoveForward()
////    {
////        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
////        targetPos = nextPos;
////        isMoving = true;
////    }

////    // ======================================================
////    // TryExploreMove
////    // ======================================================
////    void TryExploreMove()
////    {
////        if (isFollowingShortest) return;

////        currentNode = TryPlaceNode(transform.position);
////        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

////        var dirs = ScanAroundDirections();
////        if (dirs.Count == 0) return;

////        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
////        bool chooseUnexplored = Random.value < exploreBias;

////        // 壁方向は除外して未知/既知を判定
////        var unexploredDirs = dirs.Where(d => !d.isWall && (d.node == null || !d.hasLink)).ToList();
////        var knownDirs2 = dirs.Where(d => d.node != null && d.hasLink).ToList();

////        (Vector3 dir, MapNode node, bool hasLink, bool isWall)? chosenDir = null;

////        if (isDeadEnd && unexploredDirs.Count > 0)
////            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////        else
////        {
////            if (chooseUnexplored && unexploredDirs.Count > 0)
////                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////            else if (knownDirs2.Count > 0)
////                chosenDir = knownDirs2[Random.Range(0, knownDirs2.Count)];
////            else if (unexploredDirs.Count > 0)
////                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////        }

////        if (chosenDir.HasValue)
////        {
////            moveDir = chosenDir.Value.dir;
////            MoveForward();
////            if (debugLog)
////                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
////        }
////    }

////    // ======================================================
////    // ScanAroundDirections（壁は rayDistance=1 のときのみ判定）
////    // ======================================================
////    List<(Vector3 dir, MapNode node, bool hasLink, bool isWall)> ScanAroundDirections()
////    {
////        List<(Vector3 dir, MapNode node, bool hasLink, bool isWall)> found = new();
////        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

////        int wallCountLocal = 0;
////        bool doWallCheck = Mathf.Approximately(rayDistance, 1f);

////        foreach (var dir in dirs)
////        {
////            bool wallHit = false;
////            if (doWallCheck)
////            {
////                wallHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer);
////            }

////            if (wallHit)
////            {
////                wallCountLocal++;
////                found.Add((dir, null, false, true));
////                continue;
////            }

////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////            Vector2Int nextCell = WorldToCell(nextPos);
////            MapNode nextNode = MapNode.FindByCell(nextCell);
////            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));
////            found.Add((dir, nextNode, linked, false));
////        }

////        //if (currentNode != null)
////        //{
////        //    currentNode.wallCount = wallCountLocal;                          // 要: MapNodeにwallCount
////        //    currentNode.unknownCount = found.Count(d => d.node == null && !d.isWall); // 要: MapNodeにunknownCount
////        //}

////        return found;
////    }

////    // ======================================================
////    // FollowShortestPath（最短経路コルーチン）
////    // ======================================================
////    private IEnumerator FollowShortestPath()
////    {
////        if (currentNode == null)
////        {
////            Debug.LogWarning("[FollowSP] currentNode is null → 経路追従不可");
////            isFollowingShortest = false;
////            yield break;
////        }

////        // 見た目：赤
////        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
////        if (debugLog) Debug.Log($"[FollowSP] === Start === current={currentNode.name}, Dist={currentNode.DistanceFromGoal}");

////        isFollowingShortest = true;
////        int stepCount = 0;

////        while (currentNode != null && currentNode.DistanceFromGoal > EPS)
////        {
////            stepCount++;
////            float currentDist = currentNode.DistanceFromGoal;

////            if (debugLog)
////            {
////                Debug.Log($"[FollowSP][Step#{stepCount}] current={currentNode.name}, dist={currentDist}, links={currentNode.links.Count}");
////                string linkInfo = string.Join(", ", currentNode.links.Select(n => n ? $"{n.name}:{n.DistanceFromGoal:F2}" : "null"));
////                Debug.Log($"[FollowSP][Links] {linkInfo}");
////            }

////            var nextNode = currentNode.links
////                .Where(n => n != null && n.DistanceFromGoal < currentDist - EPS)
////                .OrderBy(n => n.DistanceFromGoal)
////                .FirstOrDefault();

////            if (nextNode == null)
////            {
////                Debug.LogWarning($"[FollowSP][STOP] No closer link found (dist={currentDist:F3}) → 経路終了");
////                break;
////            }

////            if (debugLog)
////                Debug.Log($"[FollowSP][Move] {currentNode.name}({currentDist:F2}) → {nextNode.name}({nextNode.DistanceFromGoal:F2})");

////            // 直線移動
////            Vector3 target = nextNode.transform.position;
////            Vector3 dir = (target - transform.position); dir.y = 0f;
////            moveDir = dir.normalized;

////            while (Vector3.Distance(transform.position, target) > 0.01f)
////            {
////                transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
////                yield return null;
////            }

////            currentNode = nextNode;
////            transform.position = currentNode.transform.position;

////            if (debugLog)
////                Debug.Log($"[FollowSP][Arrived] now={currentNode.name}, dist={currentNode.DistanceFromGoal:F3}");

////            // Goal到達判定
////            if (currentNode.DistanceFromGoal <= EPS ||
////                (goalNode != null && currentNode == goalNode))
////            {
////                reachedGoal = true;
////                if (debugLog) Debug.Log($"[FollowSP] GOAL到達: node={currentNode.name} → link & destroy");

////                if (currentNode != null)
////                    LinkBackWithRay(currentNode);

////                RecalculateGoalDistance(); // 実距離ベースDijkstra
////                hasLearnedGoal = true;

////                isFollowingShortest = false;
////                Destroy(gameObject);
////                yield break;
////            }
////        }

////        isFollowingShortest = false;
////        if (debugLog) Debug.Log("[FollowSP] === Exit shortest-path mode ===");
////    }

////    // ======================================================
////    // MoveToTarget
////    // ======================================================
////    void MoveToTarget()
////    {
////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////        {
////            transform.position = targetPos;
////            isMoving = false;

////            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////            currentNode = MapNode.FindByCell(cell);

////            // ゴール判定
////            if (!reachedGoal && goalNode != null)
////            {
////                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
////                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));

////                if (playerCell == goalCell)
////                {
////                    reachedGoal = true;

////                    if (currentNode != null)
////                        LinkBackWithRay(currentNode);

////                    RecalculateGoalDistance();
////                    hasLearnedGoal = true;
////                    Destroy(gameObject);
////                    return;
////                }
////            }
////        }
////    }

////    //// ======================================================
////    //// LinkBackWithRay
////    //// ======================================================
////    //private void LinkBackWithRay(MapNode node)
////    //{
////    //    if (node == null) return;

////    //    Vector3 origin = node.transform.position + Vector3.up * 0.1f;
////    //    Vector3 backDir = -moveDir.normalized;
////    //    LayerMask mask = wallLayer | nodeLayer;

////    //    for (int step = 1; step <= linkRayMaxSteps; step++)
////    //    {
////    //        float maxDist = cellSize * step;
////    //        if (debugRay)
////    //            Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

////    //        if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
////    //        {
////    //            int hitLayer = hit.collider.gameObject.layer;

////    //            if ((wallLayer.value & (1 << hitLayer)) != 0)
////    //                return;

////    //            if ((nodeLayer.value & (1 << hitLayer)) != 0)
////    //            {
////    //                MapNode hitNode = hit.collider.GetComponent<MapNode>();
////    //                if (hitNode != null && hitNode != node)
////    //                {
////    //                    node.AddLink(hitNode);
////    //                    if (debugLog)
////    //                        Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
////    //                }
////    //                return;
////    //            }
////    //        }
////    //    }
////    //}
////    // ======================================================
////    // LinkBackWithRay
////    // ======================================================
////    private void LinkBackWithRay(MapNode node)
////    {
////        if (node == null) return;

////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
////        Vector3 backDir = -moveDir.normalized;
////        LayerMask mask = wallLayer | nodeLayer;

////        for (int step = 1; step <= linkRayMaxSteps; step++)
////        {
////            float maxDist = cellSize * step;
////            if (debugRay)
////                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

////            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
////            {
////                int hitLayer = hit.collider.gameObject.layer;

////                // 壁に当たったら中断
////                if ((wallLayer.value & (1 << hitLayer)) != 0)
////                    return;

////                // ノードに当たった場合
////                if ((nodeLayer.value & (1 << hitLayer)) != 0)
////                {
////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
////                    if (hitNode != null && hitNode != node)
////                    {
////                        // 双方向リンクを追加
////                        node.AddLink(hitNode);

////                        if (debugLog)
////                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");

////                        // 🔹リンク確定後に両ノードの未知数・壁数を更新
////                        node.RecalculateUnknownAndWall();
////                        hitNode.RecalculateUnknownAndWall();
////                    }
////                    return;
////                }
////            }
////        }
////    }

////    // ======================================================
////    // RecalculateGoalDistance（Dijkstra 実距離）
////    // ======================================================
////    void RecalculateGoalDistance()
////    {
////        if (goalNode == null) return;

////        foreach (var n in FindObjectsOfType<MapNode>())
////            n.DistanceFromGoal = Mathf.Infinity;

////        goalNode.DistanceFromGoal = 0f;
////        var frontier = new List<MapNode> { goalNode };

////        while (frontier.Count > 0)
////        {
////            frontier.Sort((a, b) => a.DistanceFromGoal.CompareTo(b.DistanceFromGoal));
////            var node = frontier[0];
////            frontier.RemoveAt(0);

////            foreach (var link in node.links)
////            {
////                if (link == null) continue;

////                float newDist = node.DistanceFromGoal + node.EdgeCost(link);
////                if (newDist < link.DistanceFromGoal)
////                {
////                    link.DistanceFromGoal = newDist;
////                    if (!frontier.Contains(link))
////                        frontier.Add(link);
////                }
////            }
////        }
////    }

////    // ======================================================
////    // TryPlaceNode
////    // ======================================================
////    MapNode TryPlaceNode(Vector3 pos)
////    {
////        if (isFollowingShortest)
////        {
////            Vector2Int c = WorldToCell(SnapToGrid(pos));
////            return MapNode.FindByCell(c);
////        }

////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////        MapNode node;

////        if (MapNode.allNodeCells.Contains(cell))
////        {
////            node = MapNode.FindByCell(cell);
////            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
////        }
////        else
////        {
////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////            node = obj.GetComponent<MapNode>();
////            node.cell = cell;
////            MapNode.allNodeCells.Add(cell);
////            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
////        }

////        if (node != null)
////            LinkBackWithRay(node);
////            //node.RecalculateUnknownAndWall();

////        return node;
////    }

////    // ======================================================
////    // 座標変換ユーティリティ
////    // ======================================================
////    Vector2Int WorldToCell(Vector3 worldPos)
////    {
////        Vector3 p = worldPos - gridOrigin;
////        int cx = Mathf.RoundToInt(p.x / cellSize);
////        int cz = Mathf.RoundToInt(p.z / cellSize);
////        return new Vector2Int(cx, cz);
////    }

////    Vector3 CellToWorld(Vector2Int cell)
////    {
////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
////    }

////    Vector3 SnapToGrid(Vector3 worldPos)
////    {
////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
////    }
////}

//////using UnityEngine;
//////using System.Collections.Generic;
//////using System.Linq;
//////using System.Collections;

//////public class Player : MonoBehaviour
//////{
//////    [Header("移動設定")]
//////    public float moveSpeed = 3f;
//////    public float cellSize = 1f;
//////    public float rayDistance = 1f;
//////    public LayerMask wallLayer;
//////    public LayerMask nodeLayer;

//////    [Header("初期設定")]
//////    public Vector3 startDirection = Vector3.forward;
//////    public Vector3 gridOrigin = Vector3.zero;
//////    public MapNode goalNode;
//////    public GameObject nodePrefab;

//////    [Header("行動傾向")]
//////    [Range(0f, 1f)] public float exploreBias = 0.6f;

//////    [Header("リンク探索")]
//////    public int linkRayMaxSteps = 100;

//////    [Header("デバッグ")]
//////    public bool debugLog = true;
//////    public bool debugRay = true;
//////    [SerializeField] private Renderer bodyRenderer;
//////    [SerializeField] private Material exploreMaterial;

//////    public static bool hasLearnedGoal = false; // ★全プレイヤー共通（Goal学習完了フラグ）

//////    // 内部状態
//////    private Vector3 moveDir;
//////    private bool isMoving = false;
//////    private Vector3 targetPos;
//////    private MapNode currentNode;
//////    private bool reachedGoal = false;

//////    // ★修正A: 最短経路モード中フラグを追加
//////    private bool isFollowingShortest = false;

//////    private const float EPS = 1e-4f; // ★追加: float比較用の微小量

//////    // ======================================================
//////    // Start
//////    // ======================================================
//////    void Start()
//////    {
//////        moveDir = startDirection.normalized;
//////        targetPos = transform.position = SnapToGrid(transform.position);
//////        ApplyVisual();
//////        currentNode = TryPlaceNode(transform.position);

//////        if (goalNode == null)
//////        {
//////            GameObject goalObj = GameObject.Find("Goal");
//////            if (goalObj != null)
//////            {
//////                goalNode = goalObj.GetComponent<MapNode>();
//////                //Debug.Log($"[Player] GoalNode assigned from Scene object: {goalNode.name}");
//////            }
//////            else
//////            {
//////                //Debug.LogWarning("[Player] Goal object not found in scene!");
//////            }
//////        }

//////        // ★修正B: 生成時に最短経路モードへ入る場合はフラグONして探索を完全停止
//////        if (hasLearnedGoal)
//////        {
//////            if (Random.value < 1.0f)
//////            {
//////                //Debug.Log($"[Player:{name}] Spawned as shortest-path follower");
//////                isFollowingShortest = true; // ★追加
//////                StopAllCoroutines();
//////                StartCoroutine(FollowShortestPath());
//////                return; // ★探索ロジックに入らない
//////            }
//////            else
//////            {
//////                //Debug.Log($"[Player:{name}] Spawned as explorer (continue exploring)");
//////            }
//////        }

//////        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
//////    }

//////    // ======================================================
//////    // Update
//////    // ======================================================
//////    void Update()
//////    {
//////        // ★修正C: 最短経路モード中は探索系を一切走らせない
//////        if (isFollowingShortest)
//////        {
//////            // コルーチンが MoveForward → 到着待機 → 次手… を制御する。
//////            // ここで勝手にTryExploreMoveやMoveForwardを呼ばない。
//////            if (isMoving)
//////            {
//////                MoveToTarget();
//////            }
//////            return;
//////        }

//////        // 以降は通常（探索）モード
//////        if (!isMoving)
//////        {
//////            if (CanPlaceNodeHere())
//////                TryExploreMove();
//////            else
//////                MoveForward();
//////        }
//////        else
//////        {
//////            MoveToTarget();
//////        }
//////    }

//////    // ======================================================
//////    // Visual
//////    // ======================================================
//////    private void ApplyVisual()
//////    {
//////        if (bodyRenderer == null) return;
//////        bodyRenderer.material = exploreMaterial
//////            ? exploreMaterial
//////            : new Material(Shader.Find("Standard")) { color = Color.cyan };
//////    }

//////    // ======================================================
//////    // MoveForward
//////    // ======================================================
//////    void MoveForward()
//////    {
//////        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
//////        targetPos = nextPos;
//////        isMoving = true;
//////    }

//////    // ======================================================
//////    // CanPlaceNodeHere
//////    // ======================================================
//////    bool CanPlaceNodeHere()
//////    {
//////        // ★修正D: 最短経路モード中は常にNode設置不可扱い
//////        if (isFollowingShortest) return false;

//////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//////        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
//////        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
//////        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

//////        int openCount = 0;
//////        if (!frontHit) openCount++;
//////        if (!leftHit) openCount++;
//////        if (!rightHit) openCount++;

//////        return (frontHit || openCount >= 2);
//////    }

//////    // ======================================================
//////    // TryExploreMove
//////    // ======================================================
//////    void TryExploreMove()
//////    {
//////        // ★安全弁: 念のため
//////        if (isFollowingShortest) return;

//////        currentNode = TryPlaceNode(transform.position);
//////        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

//////        var dirs = ScanAroundDirections();
//////        if (dirs.Count == 0)
//////        {
//////            if (debugLog) Debug.Log("[Player] No available directions");
//////            return;
//////        }

//////        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
//////        bool chooseUnexplored = Random.value < exploreBias;

//////        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
//////        var knownDirs2 = dirs.Where(d => d.node != null && d.hasLink).ToList();

//////        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

//////        if (isDeadEnd && unexploredDirs.Count > 0)
//////            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
//////        else
//////        {
//////            if (chooseUnexplored && unexploredDirs.Count > 0)
//////                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
//////            else if (knownDirs2.Count > 0)
//////                chosenDir = knownDirs2[Random.Range(0, knownDirs2.Count)];
//////            else if (unexploredDirs.Count > 0)
//////                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
//////        }

//////        if (chosenDir.HasValue)
//////        {
//////            moveDir = chosenDir.Value.dir;
//////            MoveForward();

//////            if (debugLog)
//////                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
//////        }
//////    }

//////    // ======================================================
//////    // ScanAroundDirections
//////    // ======================================================
//////    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
//////    {
//////        List<(Vector3, MapNode, bool)> found = new();
//////        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//////        foreach (var dir in dirs)
//////        {
//////            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
//////                continue;

//////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
//////            Vector2Int nextCell = WorldToCell(nextPos);
//////            MapNode nextNode = MapNode.FindByCell(nextCell);
//////            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));
//////            found.Add((dir, nextNode, linked));
//////        }
//////        return found;
//////    }

//////    // ======================================================
//////    // FollowShortestPath
//////    // ======================================================
//////    private IEnumerator FollowShortestPath()
//////    {
//////        if (currentNode == null)
//////        {
//////            Debug.LogWarning("[FollowSP] currentNode is null → 経路追従不可");
//////            isFollowingShortest = false;
//////            yield break;
//////        }

//////        // 見た目（赤）
//////        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
//////        Debug.Log($"[FollowSP] === Start === current={currentNode.name}, Dist={currentNode.DistanceFromGoal}");

//////        isFollowingShortest = true;
//////        int stepCount = 0;

//////        // ★修正：float型Distanceに対応（EPSで誤差吸収）
//////        while (currentNode != null && currentNode.DistanceFromGoal > EPS)
//////        {
//////            stepCount++;

//////            // ★修正：int → float
//////            float currentDist = currentNode.DistanceFromGoal;

//////            Debug.Log($"[FollowSP][Step#{stepCount}] current={currentNode.name}, dist={currentDist}, links={currentNode.links.Count}");
//////            string linkInfo = string.Join(", ", currentNode.links.Select(n => n ? $"{n.name}:{n.DistanceFromGoal:F2}" : "null"));
//////            Debug.Log($"[FollowSP][Links] {linkInfo}");

//////            // ★修正：float比較（EPS分だけ小さいノードを選ぶ）
//////            var nextNode = currentNode.links
//////                .Where(n => n != null && n.DistanceFromGoal < currentDist - EPS)
//////                .OrderBy(n => n.DistanceFromGoal)
//////                .FirstOrDefault();

//////            if (nextNode == null)
//////            {
//////                Debug.LogWarning($"[FollowSP][STOP] No closer link found (dist={currentDist:F3}) → 経路終了");
//////                break;
//////            }

//////            Debug.Log($"[FollowSP][Move] {currentNode.name}({currentDist:F2}) → {nextNode.name}({nextNode.DistanceFromGoal:F2})");

//////            // リンク先Nodeへ直線移動
//////            Vector3 targetPos = nextNode.transform.position;
//////            Vector3 dir = (targetPos - transform.position);
//////            dir.y = 0f;
//////            moveDir = dir.normalized;

//////            while (Vector3.Distance(transform.position, targetPos) > 0.01f)
//////            {
//////                transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//////                yield return null;
//////            }

//////            // 到着：現在ノード更新＆スナップ
//////            currentNode = nextNode;
//////            transform.position = currentNode.transform.position;

//////            Debug.Log($"[FollowSP][Arrived] now={currentNode.name}, dist={currentNode.DistanceFromGoal:F3}");

//////            // ★修正：float距離比較でGoal判定
//////            if (currentNode.DistanceFromGoal <= EPS ||
//////                (goalNode != null && currentNode == goalNode))
//////            {
//////                reachedGoal = true;
//////                Debug.Log($"[FollowSP] GOAL到達: node={currentNode.name} → link & destroy");

//////                if (currentNode != null)
//////                    LinkBackWithRay(currentNode);  // MoveToTarget()と同じ処理

//////                RecalculateGoalDistance();         // 実距離ベースDijkstra
//////                hasLearnedGoal = true;

//////                isFollowingShortest = false;
//////                Destroy(gameObject);               // 同様に破棄
//////                yield break;
//////            }
//////        }

//////        isFollowingShortest = false;
//////        Debug.Log("[FollowSP] === Exit shortest-path mode ===");
//////    }


//////    // ======================================================
//////    // ■ Snapで該当ノードが見つからない場合のフォールバック
//////    //   プレイヤー位置に最も近い MapNode を返す（なければ null）
//////    // ======================================================
//////    private MapNode FindNearestNode(Vector3 pos)
//////    {
//////        float minDist = float.MaxValue;
//////        MapNode nearest = null;

//////        // シーン上の全 MapNode を走査（必要ならキャッシュ化も可）
//////        foreach (var node in FindObjectsOfType<MapNode>())
//////        {
//////            if (node == null) continue;
//////            float dist = Vector3.Distance(pos, node.transform.position);
//////            if (dist < minDist)
//////            {
//////                minDist = dist;
//////                nearest = node;
//////            }
//////        }

//////        return nearest;
//////    }

//////    // ======================================================
//////    // MoveToTarget
//////    // ======================================================
//////    void MoveToTarget()
//////    {
//////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//////        {
//////            transform.position = targetPos;
//////            isMoving = false;

//////            if (debugLog)
//////                Debug.Log($"[MOVE][Arrived] pos={transform.position}");

//////            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
//////            MapNode node = MapNode.FindByCell(cell);
//////            currentNode = node;

//////            // ゴール判定
//////            if (!reachedGoal && goalNode != null)
//////            {
//////                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
//////                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));

//////                if (playerCell == goalCell)
//////                {
//////                    reachedGoal = true;
//////                    //Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link & destroy");

//////                    if (currentNode != null)
//////                        LinkBackWithRay(currentNode);

//////                    RecalculateGoalDistance();
//////                    hasLearnedGoal = true; // 全体へ共有
//////                    //Debug.Log("[GLOBAL] Goal reached → all players now know the shortest path.");

//////                    Destroy(gameObject);
//////                    return;
//////                }
//////            }
//////        }
//////    }

//////    // ======================================================
//////    // LinkBackWithRay
//////    // ======================================================
//////    private void LinkBackWithRay(MapNode node)
//////    {
//////        if (node == null) return;

//////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//////        Vector3 backDir = -moveDir.normalized;
//////        LayerMask mask = wallLayer | nodeLayer;

//////        for (int step = 1; step <= linkRayMaxSteps; step++)
//////        {
//////            float maxDist = cellSize * step;
//////            if (debugRay)
//////                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

//////            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
//////            {
//////                int hitLayer = hit.collider.gameObject.layer;

//////                if ((wallLayer.value & (1 << hitLayer)) != 0)
//////                    return;

//////                if ((nodeLayer.value & (1 << hitLayer)) != 0)
//////                {
//////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
//////                    if (hitNode != null && hitNode != node)
//////                    {
//////                        node.AddLink(hitNode);
//////                        if (debugLog)
//////                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
//////                    }
//////                    return;
//////                }
//////            }
//////        }
//////    }

//////    // ======================================================
//////    // RecalculateGoalDistance (BFS)
//////    // ======================================================
//////    //void RecalculateGoalDistance()
//////    //{
//////    //    if (goalNode == null) return;

//////    //    Queue<MapNode> queue = new Queue<MapNode>();
//////    //    foreach (var n in FindObjectsOfType<MapNode>())
//////    //        n.DistanceFromGoal = int.MaxValue;

//////    //    goalNode.DistanceFromGoal = 0;
//////    //    queue.Enqueue(goalNode);

//////    //    while (queue.Count > 0)
//////    //    {
//////    //        var node = queue.Dequeue();
//////    //        foreach (var link in node.links)
//////    //        {
//////    //            int newDist = node.DistanceFromGoal + 1;
//////    //            if (newDist < link.DistanceFromGoal)
//////    //            {
//////    //                link.DistanceFromGoal = newDist;
//////    //                queue.Enqueue(link);
//////    //            }
//////    //        }
//////    //    }

//////    //    //Debug.Log("[Player] Distance learning complete (Goal-based BFS)");
//////    //}
//////    void RecalculateGoalDistance()
//////    {
//////        if (goalNode == null) return;

//////        // 全ノード初期化
//////        foreach (var n in FindObjectsOfType<MapNode>())
//////            n.DistanceFromGoal = Mathf.Infinity;

//////        goalNode.DistanceFromGoal = 0f;
//////        var frontier = new List<MapNode> { goalNode };

//////        while (frontier.Count > 0)
//////        {
//////            // 距離が最小のノードを取り出す
//////            frontier.Sort((a, b) => a.DistanceFromGoal.CompareTo(b.DistanceFromGoal));
//////            var node = frontier[0];
//////            frontier.RemoveAt(0);

//////            // すべてのリンク先を評価
//////            foreach (var link in node.links)
//////            {
//////                if (link == null) continue;

//////                // ★変更：隣接ノード間の距離を実距離で加算
//////                float newDist = node.DistanceFromGoal + node.EdgeCost(link);

//////                if (newDist < link.DistanceFromGoal)
//////                {
//////                    link.DistanceFromGoal = newDist;
//////                    if (!frontier.Contains(link))
//////                        frontier.Add(link);
//////                }
//////            }
//////        }

//////        //Debug.Log("[Player] Distance learning complete (Goal-based Dijkstra)");
//////    }

//////    // ======================================================
//////    // TryPlaceNode
//////    // ======================================================
//////    MapNode TryPlaceNode(Vector3 pos)
//////    {
//////        // ★修正E: 最短経路モード中は新規Nodeを一切置かない（安全弁）
//////        if (isFollowingShortest)
//////        {
//////            // 既存があれば参照だけ返す。無ければnullのまま。
//////            Vector2Int c = WorldToCell(SnapToGrid(pos));
//////            return MapNode.FindByCell(c);
//////        }

//////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//////        MapNode node;

//////        if (MapNode.allNodeCells.Contains(cell))
//////        {
//////            node = MapNode.FindByCell(cell);
//////            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
//////        }
//////        else
//////        {
//////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//////            node = obj.GetComponent<MapNode>();
//////            node.cell = cell;
//////            MapNode.allNodeCells.Add(cell);
//////            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
//////        }

//////        if (node != null)
//////        {
//////            if (debugLog) Debug.Log($"[LINK] Check back connection for Node={node.name}");
//////            LinkBackWithRay(node);
//////        }

//////        return node;
//////    }

//////    // ======================================================
//////    // 座標変換ユーティリティ
//////    // ======================================================
//////    Vector2Int WorldToCell(Vector3 worldPos)
//////    {
//////        Vector3 p = worldPos - gridOrigin;
//////        int cx = Mathf.RoundToInt(p.x / cellSize);
//////        int cz = Mathf.RoundToInt(p.z / cellSize);
//////        return new Vector2Int(cx, cz);
//////    }

//////    Vector3 CellToWorld(Vector2Int cell)
//////    {
//////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
//////    }

//////    Vector3 SnapToGrid(Vector3 worldPos)
//////    {
//////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
//////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
//////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
//////    }
//////}