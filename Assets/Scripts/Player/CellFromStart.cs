using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class CellFromStart : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 3f;
    public float cellSize = 1f;
    public float rayDistance = 1f;
    public LayerMask wallLayer;
    public LayerMask nodeLayer;

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward;
    public Vector3 gridOrigin = Vector3.zero;
    public GameObject nodePrefab;

    [Header("探索パラメータ")]
    public int unknownReferenceDepth = 3; // ★ BFS探索深さとして使用

    [Header("スコア重み（A：現状の式）")]
    public float weightUnknown = 1f;
    public float weightDistance = 1f;

    [Header("Ray設定")]
    public int linkRayMaxSteps = 100;

    [Header("デバッグ")]
    public bool debugLog = true;
    public bool debugRay = true;
    public Renderer bodyRenderer;
    public Material exploreMaterial;

    // 内部状態
    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private MapNode currentNode;

    private List<MapNode> recentNodes = new List<MapNode>();

    private MapNode lastBestTarget = null;

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

            Debug.Log($"[SET STARTNODE] StartNode = {nodeAtStart.name}");
        }

        // ------------------------------------------------
        // ② currentNode の設定（StartNode 設定後）
        // ------------------------------------------------
        currentNode = nodeAtStart;
        RegisterCurrentNode(currentNode);

        Debug.Log($"[SET CURRENTNODE] currentNode = {currentNode.name}");
    }


    void Update()
    {
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

    private void ApplyVisual()
    {
        if (bodyRenderer != null && exploreMaterial != null)
            bodyRenderer.material = exploreMaterial;
    }

    private bool CanPlaceNodeHere()
    {
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
        bool leftWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
        bool rightWall = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

        int openings = (!frontWall ? 1 : 0) + (!leftWall ? 1 : 0) + (!rightWall ? 1 : 0);

        return frontWall || openings >= 2;
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
            if (debugLog)
                Debug.Log("[Block] Wall ahead → stop movement");

            isMoving = false;
            return;
        }

        targetPos = SnapToGrid(next);
        isMoving = true;
    }

    private void MoveToTarget()
    {
        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        else
        {
            transform.position = targetPos;
            isMoving = false;
        }
    }

    private void RegisterCurrentNode(MapNode node)
    {
        if (node == null) return;

        if (recentNodes.Count == 0 || recentNodes[^1] != node)
            recentNodes.Add(node);

        while (recentNodes.Count > unknownReferenceDepth)
            recentNodes.RemoveAt(0);
    }

    // =============================
    // ★★★ メイン探索ルーチン ★★★
    // =============================
    //private void TryExploreMove()
    //{
    //    Debug.Log(
    //        $"[TryExploreMove] Start " +
    //        $"currentNode={(currentNode ? currentNode.name : "null")}, " +
    //        $"StartNode={(MapNode.StartNode ? MapNode.StartNode.name : "null")}, " +
    //        $"pos={transform.position}");

    //    // ★ 先に Node を生成・更新する（これが超重要）
    //    currentNode = TryPlaceNode(transform.position);
    //    RegisterCurrentNode(currentNode);

    //    // ★ ここで初めて StartNode 判定を行う
    //    if (currentNode == MapNode.StartNode && currentNode.links.Count == 0)
    //    {
    //        if (!IsWall(currentNode, startDirection))
    //        {
    //            moveDir = startDirection.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        List<Vector3> dirs = new()
    //        {
    //            Vector3.forward,
    //            Vector3.back,
    //            Vector3.left,
    //            Vector3.right
    //        };

    //        dirs = dirs.Where(d => !IsWall(currentNode, d)).ToList();

    //        if (dirs.Count > 0)
    //            moveDir = dirs[Random.Range(0, dirs.Count)];
    //        else
    //            moveDir = -startDirection;

    //        MoveForward();
    //        return;
    //    }

    //    Log($"Node placed → {currentNode.name}");

    //    // ① 終端ノードは特別処理
    //    if (IsTerminalNode(currentNode))
    //    {
    //        Vector3? dir = ChooseTerminalDirection(currentNode);
    //        if (dir.HasValue)
    //            moveDir = dir.Value;
    //        else
    //            moveDir = -moveDir;

    //        MoveForward();
    //        return;
    //    }

    //    // =============================
    //    // ② 近場BFS(depth=N)を実行
    //    // =============================
    //    List<MapNode> nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    List<MapNode> unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    // =============================
    //    // ③ targetUnknown（近場未知）
    //    // =============================
    //    MapNode targetUnknown = null;

    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderBy(n => Distance(currentNode, n))
    //            .First();
    //    }

    //    // =============================
    //    // ④ targetFarthest（全体最遠）
    //    // =============================
    //    MapNode targetFarthest = MapNode.allNodes
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .First();

    //    // =============================
    //    // ⑤ スコアを比較して最適ターゲットを決定
    //    // =============================
    //    MapNode bestTarget = ChooseBestTarget(targetUnknown, targetFarthest);

    //    if (bestTarget == null)
    //    {
    //        Debug.LogError("bestTarget == null");
    //        return;
    //    }

    //    // =============================
    //    // ⑥ 最短ルート（リンクBFS）を生成
    //    // =============================
    //    List<MapNode> path = BuildShortestPath(currentNode, bestTarget);

    //    if (path == null || path.Count < 2)
    //    {
    //        Debug.LogError("Path error");
    //        return;
    //    }

    //    MapNode nextNode = path[1];

    //    moveDir = (nextNode.transform.position - currentNode.transform.position).normalized;

    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log(
    //        $"[TryExploreMove] Start " +
    //        $"currentNode={(currentNode ? currentNode.name : "null")}, " +
    //        $"StartNode={(MapNode.StartNode ? MapNode.StartNode.name : "null")}, " +
    //        $"pos={transform.position}");

    //    // ★ 先に Node を生成・更新する
    //    currentNode = TryPlaceNode(transform.position);
    //    RegisterCurrentNode(currentNode);

    //    // ★ StartNode の初回特別処理
    //    if (currentNode == MapNode.StartNode && currentNode.links.Count == 0)
    //    {
    //        if (!IsWall(currentNode, startDirection))
    //        {
    //            moveDir = startDirection.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        List<Vector3> dirs = new()
    //        {
    //            Vector3.forward,
    //            Vector3.back,
    //            Vector3.left,
    //            Vector3.right
    //        };

    //        dirs = dirs.Where(d => !IsWall(currentNode, d)).ToList();

    //        if (dirs.Count > 0)
    //            moveDir = dirs[Random.Range(0, dirs.Count)];
    //        else
    //            moveDir = -startDirection;

    //        MoveForward();
    //        return;
    //    }

    //    Log($"Node placed → {currentNode.name}");

    //    // ① 終端ノードは特別処理
    //    if (IsTerminalNode(currentNode))
    //    {
    //        Vector3? dir = ChooseTerminalDirection(currentNode);
    //        if (dir.HasValue)
    //            moveDir = dir.Value;
    //        else
    //            moveDir = -moveDir;

    //        MoveForward();
    //        return;
    //    }

    //    // ② 近場 BFS（depth=N）
    //    List<MapNode> nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    List<MapNode> unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    // ③ targetUnknown（近場未知）
    //    MapNode targetUnknown = null;

    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderBy(n => Distance(currentNode, n))
    //            .First();
    //    }

    //    // ④ targetFarthest（全体最遠）
    //    //MapNode targetFarthest = MapNode.allNodes
    //    //    .OrderByDescending(n => n.distanceFromStart)
    //    //    .First();
    //    // ★ リンク BFS で到達できるノードだけに限定
    //    var reachable = BFS_ReachableNodes(currentNode);

    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();


    //    // ⑤ スコアを比較して最適ターゲットを決定
    //    MapNode bestTarget = ChooseBestTarget(targetUnknown, targetFarthest);

    //    // ★ 追加：ターゲットに到達したら Unknown に進む
    //    if (bestTarget != null && currentNode == bestTarget)
    //    {
    //        // Unknown 方向が存在するなら必ず Unknown を掘りに行く
    //        Vector3? unknownDir = currentNode.GetUnknownDirection();
    //        if (unknownDir.HasValue)
    //        {
    //            moveDir = unknownDir.Value.normalized;
    //            MoveForward();
    //            return; // ★ Unknown へ進むので終了
    //        }
    //    }

    //    // =========================================================
    //    // ★ 修正①：backward target（距離が戻る方向）を拒否
    //    // =========================================================
    //    if (bestTarget != null && bestTarget.distanceFromStart < currentNode.distanceFromStart)
    //    {
    //        Debug.LogWarning($"[TargetReject] {bestTarget.name} is backward. Reject.");
    //        bestTarget = targetUnknown;
    //    }

    //    // bestTarget が決まらない場合 → fallback（リンク先 or ランダム）
    //    if (bestTarget == null)
    //    {
    //        Debug.LogError("bestTarget == null (after reject)");

    //        // フォールバック①：リンクされてる方向へ進む
    //        var fallback = currentNode.links.FirstOrDefault();
    //        if (fallback != null)
    //        {
    //            moveDir = (fallback.transform.position - currentNode.transform.position).normalized;
    //            MoveForward();
    //            return;
    //        }

    //        // フォールバック②：未知方向へランダム
    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
    //        var avail = dirs.Where(d => !IsWall(currentNode, d)).ToList();
    //        if (avail.Count > 0)
    //        {
    //            moveDir = avail[Random.Range(0, avail.Count)];
    //            MoveForward();
    //            return;
    //        }

    //        return;
    //    }

    //    // ⑥ 最短ルート（リンク BFS）
    //    List<MapNode> path = BuildShortestPath(currentNode, bestTarget);

    //    // =========================================================
    //    // ★ 修正②：path=null の場合に fallback を追加
    //    // =========================================================
    //    if (path == null || path.Count < 2)
    //    {
    //        Debug.LogWarning($"[PathFallback] Cannot reach {bestTarget.name} from {currentNode.name}");

    //        // フォールバック①：リンク方向へ進む
    //        var fallback = currentNode.links.FirstOrDefault();
    //        if (fallback != null)
    //        {
    //            moveDir = (fallback.transform.position - currentNode.transform.position).normalized;
    //            MoveForward();
    //            return;
    //        }

    //        // フォールバック②：未知方向へ進む
    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
    //        var avail = dirs.Where(d => !IsWall(currentNode, d)).ToList();

    //        if (avail.Count > 0)
    //        {
    //            moveDir = avail[Random.Range(0, avail.Count)];
    //            MoveForward();
    //            return;
    //        }

    //        // フォールバック③：なければ停止
    //        return;
    //    }

    //    // NextNode（最短ルートの 1 つ先）
    //    MapNode nextNode = path[1];

    //    moveDir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log(
    //        $"[TryExploreMove] Start " +
    //        $"currentNode={(currentNode ? currentNode.name : "null")}, " +
    //        $"StartNode={(MapNode.StartNode ? MapNode.StartNode.name : "null")}, " +
    //        $"pos={transform.position}");

    //    // ★ 先に Node を生成・更新する
    //    currentNode = TryPlaceNode(transform.position);
    //    RegisterCurrentNode(currentNode);

    //    // ★ StartNode の初回特別処理
    //    if (currentNode == MapNode.StartNode && currentNode.links.Count == 0)
    //    {
    //        if (!IsWall(currentNode, startDirection))
    //        {
    //            moveDir = startDirection.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        List<Vector3> dirs = new()
    //        {
    //            Vector3.forward,
    //            Vector3.back,
    //            Vector3.left,
    //            Vector3.right
    //        };

    //        dirs = dirs.Where(d => !IsWall(currentNode, d)).ToList();

    //        if (dirs.Count > 0)
    //            moveDir = dirs[Random.Range(0, dirs.Count)];
    //        else
    //            moveDir = -startDirection;

    //        MoveForward();
    //        return;
    //    }

    //    Log($"Node placed → {currentNode.name}");

    //    // ① 終端ノードは特別処理
    //    if (IsTerminalNode(currentNode))
    //    {
    //        Vector3? dir = ChooseTerminalDirection(currentNode);
    //        if (dir.HasValue)
    //            moveDir = dir.Value;
    //        else
    //            moveDir = -moveDir;

    //        MoveForward();
    //        return;
    //    }

    //    // ② 近場 BFS（depth=N）
    //    List<MapNode> nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    List<MapNode> unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    // ③ targetUnknown（近場未知）
    //    MapNode targetUnknown = null;

    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderBy(n => Distance(currentNode, n))
    //            .First();
    //    }

    //    // ④ targetFarthest（到達可能な中の最遠）
    //    var reachable = BFS_ReachableNodes(currentNode);

    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    // ⑤ スコアを比較して最適ターゲットを決定
    //    MapNode bestTarget = ChooseBestTarget(targetUnknown, targetFarthest);

    //    // ★ bestTarget に到達したら Unknown 開拓
    //    if (bestTarget != null && currentNode == bestTarget)
    //    {
    //        Vector3? unknownDir = currentNode.GetUnknownDirection();
    //        if (unknownDir.HasValue)
    //        {
    //            Debug.Log($"[UnknownExplore] Arrived {bestTarget.name} → go Unknown");
    //            moveDir = unknownDir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }
    //    }

    //    // =========================================================
    //    // ★ 修正①：backward target（距離が戻る方向）を拒否
    //    // =========================================================
    //    if (bestTarget != null && bestTarget.distanceFromStart < currentNode.distanceFromStart)
    //    {
    //        Debug.LogWarning($"[TargetReject] {bestTarget.name} is backward. Reject.");
    //        bestTarget = targetUnknown;
    //    }

    //    // bestTarget が決まらない場合 → fallback（リンク先 or ランダム）
    //    if (bestTarget == null)
    //    {
    //        Debug.LogError("bestTarget == null (after reject)");

    //        var fallback = currentNode.links.FirstOrDefault();
    //        if (fallback != null)
    //        {
    //            moveDir = (fallback.transform.position - currentNode.transform.position).normalized;
    //            MoveForward();
    //            return;
    //        }

    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
    //        var avail = dirs.Where(d => !IsWall(currentNode, d)).ToList();
    //        if (avail.Count > 0)
    //        {
    //            moveDir = avail[Random.Range(0, avail.Count)];
    //            MoveForward();
    //            return;
    //        }

    //        return;
    //    }

    //    // ⑥ 最短ルート（リンク BFS）
    //    List<MapNode> path = BuildShortestPath(currentNode, bestTarget);

    //    // =========================================================
    //    // ★ 修正②：path=null の場合に fallback を追加
    //    // =========================================================
    //    if (path == null || path.Count < 2)
    //    {
    //        Debug.LogWarning($"[PathFallback] Cannot reach {bestTarget.name} from {currentNode.name}");

    //        var fallback = currentNode.links.FirstOrDefault();
    //        if (fallback != null)
    //        {
    //            moveDir = (fallback.transform.position - currentNode.transform.position).normalized;
    //            MoveForward();
    //            return;
    //        }

    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
    //        var avail = dirs.Where(d => !IsWall(currentNode, d)).ToList();

    //        if (avail.Count > 0)
    //        {
    //            moveDir = avail[Random.Range(0, avail.Count)];
    //            MoveForward();
    //            return;
    //        }

    //        return;
    //    }

    //    // NextNode（最短ルートの 1 つ先）
    //    MapNode nextNode = path[1];

    //    moveDir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={(currentNode ? currentNode.name : "null")} pos={transform.position}");

    //    Debug.Log($"[REACH] Arrived at {currentNode.name}, " +
    //      $"lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);
    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ② bestTarget に到達していたら Unknown を必ず掘る
    //    //------------------------------------------------------
    //    Debug.Log($"[CHECK-UNKNOWN] current={currentNode.name}, lastBestTarget={lastBestTarget?.name}");
    //    if (currentNode == lastBestTarget)
    //    {
    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            Debug.Log($"[CHECK-UNKNOWN] moving to unknownDir={udir.Value}");
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }
    //        else
    //        {
    //            Debug.Log("[CHECK-UNKNOWN] no unknown dir found!!!");
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ 終端ノードなら Unknown 優先で処理
    //    //------------------------------------------------------
    //    if (IsTerminalNode(currentNode))
    //    {
    //        Vector3? tdir = ChooseTerminalDirection(currentNode);
    //        if (tdir.HasValue) moveDir = tdir.Value;
    //        else moveDir = -moveDir;

    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ④ BFSで探索範囲ノード取得
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    //------------------------------------------------------
    //    // ⑤ Unknown がある場合：currentNodeから最も近い Unknown
    //    //------------------------------------------------------
    //    MapNode targetUnknown = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderBy(n => Distance(currentNode, n))
    //            .First();
    //    }

    //    //------------------------------------------------------
    //    // ⑥ Unknown が無い場合：Startから最遠ノード
    //    //------------------------------------------------------
    //    var reachable = BFS_ReachableNodes(currentNode);
    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    //------------------------------------------------------
    //    // ⑦ Unknown と Farthest の“距離差”を比較して bestTarget を決定
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (targetUnknown != null && targetFarthest != null)
    //    {
    //        int du = (int)Distance(currentNode, targetUnknown);   // 近い未知
    //        int df = (int)Distance(currentNode, targetFarthest);  // 最遠

    //        // ★遠い方を選ぶ：未知優先だが遠さで逆転も起こる
    //        bestTarget = (df - du > 0) ? targetFarthest : targetUnknown;
    //    }
    //    else
    //    {
    //        bestTarget = targetUnknown ?? targetFarthest;
    //    }

    //    lastBestTarget = bestTarget; // ★到達後Unknown掘りに使う

    //    //------------------------------------------------------
    //    // ⑧ 最短ルートをリンクBFSで生成
    //    //------------------------------------------------------
    //    var path = BuildShortestPath(currentNode, bestTarget);

    //    if (path == null || path.Count < 2)
    //    {
    //        Debug.LogWarning($"[PathFallback] path null {currentNode.name} → {bestTarget?.name}");
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ⑨ path[1]（1手先）へ移動
    //    //------------------------------------------------------
    //    MapNode next = path[1];
    //    moveDir = (next.transform.position - currentNode.transform.position).normalized;
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={(currentNode ? currentNode.name : "null")} pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（ここで currentNode が確定する）
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);

    //    // ★リンクが0でも Unknown/WALL を更新する
    //    currentNode.RecalculateUnknownAndWall();

    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ② bestTarget に到達したら Unknown を掘る（ここが最優先）
    //    //------------------------------------------------------
    //    if (currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[UNKNOWN] Reached bestTarget={lastBestTarget.name}, try unknown dig");

    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }
    //        else
    //        {
    //            Debug.Log("[UNKNOWN] No unknown direction at bestTarget");
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ 終端ノードなら Unknown を優先
    //    //------------------------------------------------------
    //    if (IsTerminalNode(currentNode))
    //    {
    //        Vector3? tdir = ChooseTerminalDirection(currentNode);
    //        if (tdir.HasValue) moveDir = tdir.Value;
    //        else moveDir = -moveDir; // 仕方なく戻る

    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ④ 探索範囲内のノードを BFS で取得
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    //------------------------------------------------------
    //    // ⑤ Unknown がある場合 → currentNode から最も近い Unknown
    //    //------------------------------------------------------
    //    MapNode targetUnknown = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderBy(n => Distance(currentNode, n))
    //            .First();
    //    }

    //    //------------------------------------------------------
    //    // ⑥ Unknown が無い → Start から最遠ノード
    //    //------------------------------------------------------
    //    var reachable = BFS_ReachableNodes(currentNode);
    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    //------------------------------------------------------
    //    // ⑦ Unknown と Farthest の“距離差”で bestTarget を決定
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (targetUnknown != null && targetFarthest != null)
    //    {
    //        int distU = (int)Distance(currentNode, targetUnknown);
    //        int distF = (int)Distance(currentNode, targetFarthest);

    //        Debug.Log($"[BEST] distU={distU}, distF={distF}");

    //        // ★遠い方を選択（未知優先だが逆転する可能性あり）
    //        bestTarget = (distF - distU > 0) ? targetFarthest : targetUnknown;
    //    }
    //    else
    //    {
    //        bestTarget = targetUnknown ?? targetFarthest;
    //    }

    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] bestTarget NULL");
    //        return;
    //    }

    //    Debug.Log($"[BEST] Selected bestTarget={bestTarget.name}");

    //    //------------------------------------------------------
    //    // ⑧ 最短ルート（リンク BFS）を構築
    //    //------------------------------------------------------
    //    var path = BuildShortestPath(currentNode, bestTarget);

    //    if (path == null || path.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name}");
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ⑨ nextNode を決定（パスの1つ先）
    //    //------------------------------------------------------
    //    MapNode nextNode = path[1];
    //    moveDir = (nextNode.transform.position - currentNode.transform.position).normalized;

    //    //------------------------------------------------------
    //    // ⑩ ★ここで初めて lastBestTarget を更新する（重要）
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;
    //    Debug.Log($"[BEST-SET] lastBestTarget={lastBestTarget.name}");

    //    //------------------------------------------------------
    //    // ⑪ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={(currentNode ? currentNode.name : "null")} pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（ここで currentNode が確定）
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);

    //    // ★Unknown/WALL は毎回更新（代案A用）
    //    currentNode.RecalculateUnknownAndWall();

    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ② bestTarget に到達 → Unknown を必ず掘る（代案B）
    //    //------------------------------------------------------
    //    if (currentNode == lastBestTarget && lastBestTarget != null)
    //    {
    //        Debug.Log($"[UNKNOWN] Reached bestTarget={lastBestTarget.name}, try unknown dig");

    //        Vector3? dig = currentNode.GetUnknownDirection(); // 掘れる Unknown のみ
    //        if (dig.HasValue)
    //        {
    //            moveDir = dig.Value.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        Debug.Log("[UNKNOWN] No diggable unknown → fallback to random direction");

    //        // 掘れない Unknown ばかりだった場合（false positive対策）
    //        Vector3? rnd = ChooseRandomValidDirection(currentNode);
    //        if (rnd.HasValue)
    //        {
    //            moveDir = rnd.Value;
    //            MoveForward();
    //            return;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ 終端ノード（リンク 0 or 1）なら Unknown 優先で処理
    //    //------------------------------------------------------
    //    if (IsTerminalNode(currentNode))
    //    {
    //        // 掘れる Unknown を優先（代案A）
    //        Vector3? tdir = currentNode.GetUnknownDirection();
    //        if (tdir.HasValue)
    //        {
    //            Debug.Log("[TERMINAL] Dig unknown direction");
    //            moveDir = tdir.Value;
    //            MoveForward();
    //            return;
    //        }

    //        // Unknown が無い → ランダムな有効方向へ（代案B/C）
    //        Vector3? rdir = ChooseRandomValidDirection(currentNode);
    //        if (rdir.HasValue)
    //        {
    //            Debug.Log("[TERMINAL] No unknown → random dig fallback");
    //            moveDir = rdir.Value;
    //            MoveForward();
    //            return;
    //        }

    //        // 進める方向が本当に無い場合
    //        Debug.LogWarning("[TERMINAL] No available direction");
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ④ 探索範囲ノード取得（BFS）
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);

    //    // ★掘れる Unknown ノードのみに絞る（代案A）
    //    var unknownNodes = nearNodes
    //        .Where(n => n.GetUnknownDirection().HasValue)
    //        .ToList();

    //    //------------------------------------------------------
    //    // ⑤ currentNode から最も近い Unknown を targetUnknown にする
    //    //------------------------------------------------------
    //    MapNode targetUnknown = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderBy(n => Distance(currentNode, n))
    //            .First();
    //    }

    //    //------------------------------------------------------
    //    // ⑥ Unknown がない → Startから最遠ノード
    //    //------------------------------------------------------
    //    var reachable = BFS_ReachableNodes(currentNode);

    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    //------------------------------------------------------
    //    // ⑦ Unknown と Farthest の距離差で bestTarget を決定（代案B）
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (targetUnknown != null && targetFarthest != null)
    //    {
    //        int distU = (int)Distance(currentNode, targetUnknown);
    //        int distF = (int)Distance(currentNode, targetFarthest);

    //        Debug.Log($"[BEST] distU={distU}, distF={distF}");

    //        bestTarget = (distF - distU > 0) ? targetFarthest : targetUnknown;
    //    }
    //    else
    //    {
    //        bestTarget = targetUnknown ?? targetFarthest;
    //    }

    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] bestTarget NULL → random fallback");
    //        Vector3? rnd = ChooseRandomValidDirection(currentNode);
    //        if (rnd.HasValue)
    //        {
    //            moveDir = rnd.Value;
    //            MoveForward();
    //        }
    //        return;
    //    }

    //    Debug.Log($"[BEST] Selected bestTarget={bestTarget.name}");

    //    //------------------------------------------------------
    //    // ⑧ パス構築（リンク BFS）
    //    //------------------------------------------------------
    //    var path = BuildShortestPath(currentNode, bestTarget);

    //    //------------------------------------------------------
    //    // ⑨ パス不正 → fallback（代案C）
    //    //------------------------------------------------------
    //    if (path == null || path.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Invalid path to {bestTarget.name} → fallback random");

    //        Vector3? rnd = ChooseRandomValidDirection(currentNode);
    //        if (rnd.HasValue)
    //        {
    //            moveDir = rnd.Value;
    //            MoveForward();
    //        }
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ⑩ nextNode を決定
    //    //------------------------------------------------------
    //    MapNode nextNode = path[1];
    //    moveDir = (nextNode.transform.position - currentNode.transform.position).normalized;

    //    //------------------------------------------------------
    //    // ⑪ ★ここで初めて lastBestTarget に保存する（代案Bで重要）
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;
    //    Debug.Log($"[BEST-SET] lastBestTarget={lastBestTarget.name}");

    //    //------------------------------------------------------
    //    // ⑫ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={(currentNode ? currentNode.name : "null")} pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（ここで currentNode が確定する）
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);

    //    // リンク数 0/1 でも Unknown/WALL を必ず再計算
    //    currentNode.RecalculateUnknownAndWall();

    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ② bestTarget に到達したら Unknown を掘る（最優先）
    //    //------------------------------------------------------
    //    if (currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[UNKNOWN] Reached bestTarget={lastBestTarget.name}, try unknown dig");

    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }
    //        else Debug.Log("[UNKNOWN] No unknown direction at bestTarget");
    //    }

    //    //------------------------------------------------------
    //    // ③ 終端ノードなら Unknown を優先（代案 A）
    //    //------------------------------------------------------
    //    if (IsTerminalNode(currentNode))
    //    {
    //        Debug.Log($"[TERMINAL] {currentNode.name} is terminal");

    //        Vector3? tdir = ChooseTerminalDirection(currentNode);
    //        if (tdir.HasValue) moveDir = tdir.Value;
    //        else moveDir = ChooseRandomValidDirection(currentNode).Value;

    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ④ 探索範囲内のノードを BFS で取得（代案 B）
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    //------------------------------------------------------
    //    // ⑤ Unknown がある場合 → currentNode から最も遠い Unknown
    //    //------------------------------------------------------
    //    MapNode targetUnknown = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderByDescending(n => Distance(currentNode, n))   // ★修正：最遠の Unknown
    //            .First();

    //        Debug.Log($"[UN] Selected farthest Unknown = {targetUnknown.name}");
    //    }

    //    //------------------------------------------------------
    //    // ⑥ Unknown が無い → リンク BFS で到達可能なノード中から Start最遠
    //    //------------------------------------------------------
    //    var reachable = BFS_ReachableNodes(currentNode);
    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    //------------------------------------------------------
    //    // ⑦ Unknown と Farthest の距離差で bestTarget を決定
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (targetUnknown != null && targetFarthest != null)
    //    {
    //        int distU = (int)Distance(currentNode, targetUnknown);
    //        int distF = (int)Distance(currentNode, targetFarthest);

    //        Debug.Log($"[BEST] distU={distU}, distF={distF}");

    //        // ★遠い方を優先
    //        bestTarget = (distF - distU > 0) ? targetFarthest : targetUnknown;
    //    }
    //    else
    //    {
    //        bestTarget = targetUnknown ?? targetFarthest;
    //    }

    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] bestTarget NULL → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    Debug.Log($"[BEST] Selected bestTarget={bestTarget.name}");

    //    //------------------------------------------------------
    //    // ⑧ 最短ルート（リンク BFS）を構築
    //    //------------------------------------------------------
    //    var path = BuildShortestPath(currentNode, bestTarget);

    //    if (path == null || path.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name} → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ⑨ nextNode を決定（パスの1つ先）
    //    //------------------------------------------------------
    //    MapNode nextNode = path[1];
    //    moveDir = (nextNode.transform.position - currentNode.transform.position).normalized;

    //    //------------------------------------------------------
    //    // ⑩ lastBestTarget を更新（重要）
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;
    //    Debug.Log($"[BEST-SET] lastBestTarget={lastBestTarget.name}");

    //    // ★ BFS で bestTarget までの経路を構築
    //    List<MapNode> path = BuildShortestPath(currentNode, bestTarget);

    //    if (path != null && path.Count >= 2)
    //    {
    //        // path[0] は currentNode、path[1] が次のノード
    //        MapNode nextNode = path[1];

    //        Debug.Log($"[PATH] Next step toward {bestTarget.name} = {nextNode.name}");

    //        // 方向ベクトルを求める
    //        Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //        dir.y = 0;

    //        // その方向へ移動
    //        MoveInDirection(dir);
    //        return;
    //    }
    //    else
    //    {
    //        Debug.LogWarning($"[PATH] bestTarget={bestTarget.name} に到達できる経路が見つかりませんでした");
    //    }


    //    //------------------------------------------------------
    //    // ⑪ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}

    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={(currentNode ? currentNode.name : "null")} pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（ここで currentNode が確定する）
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);

    //    currentNode.RecalculateUnknownAndWall();
    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ② bestTarget に到達したら Unknown を掘る（最優先）
    //    //------------------------------------------------------
    //    if (currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[UNKNOWN] Reached bestTarget={lastBestTarget.name}, try unknown dig");

    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }
    //        else Debug.Log("[UNKNOWN] No unknown direction at bestTarget");
    //    }

    //    //------------------------------------------------------
    //    // ③ 終端ノードなら Unknown を優先
    //    //------------------------------------------------------
    //    if (IsTerminalNode(currentNode))
    //    {
    //        Debug.Log($"[TERMINAL] {currentNode.name} is terminal");

    //        Vector3? tdir = ChooseTerminalDirection(currentNode);
    //        if (tdir.HasValue) moveDir = tdir.Value;
    //        else moveDir = ChooseRandomValidDirection(currentNode).Value;

    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ④ 探索範囲内 BFS
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    //------------------------------------------------------
    //    // ⑤ 最遠の Unknown
    //    //------------------------------------------------------
    //    MapNode targetUnknown = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderByDescending(n => Distance(currentNode, n))
    //            .First();

    //        Debug.Log($"[UN] Selected farthest Unknown = {targetUnknown.name}");
    //    }

    //    //------------------------------------------------------
    //    // ⑥ Unknown が無い → Start から最遠
    //    //------------------------------------------------------
    //    var reachable = BFS_ReachableNodes(currentNode);
    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    //------------------------------------------------------
    //    // ⑦ bestTarget 決定
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (targetUnknown != null && targetFarthest != null)
    //    {
    //        int distU = (int)Distance(currentNode, targetUnknown);
    //        int distF = (int)Distance(currentNode, targetFarthest);

    //        Debug.Log($"[BEST] distU={distU}, distF={distF}");

    //        bestTarget = (distF - distU > 0) ? targetFarthest : targetUnknown;
    //    }
    //    else
    //    {
    //        bestTarget = targetUnknown ?? targetFarthest;
    //    }

    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] bestTarget NULL → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    Debug.Log($"[BEST] Selected bestTarget={bestTarget.name}");

    //    //------------------------------------------------------
    //    // ⑧ BFS で経路作成
    //    //------------------------------------------------------
    //    var path = BuildShortestPath(currentNode, bestTarget);

    //    if (path == null || path.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name} → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ⑨ path[1] へ進む
    //    //------------------------------------------------------
    //    MapNode nextNode = path[1];

    //    moveDir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //    moveDir.y = 0;

    //    Debug.Log($"[PATH] Next step toward {bestTarget.name} = {nextNode.name}");

    //    //------------------------------------------------------
    //    // ⑩ lastBestTarget 更新
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;

    //    //------------------------------------------------------
    //    // ⑪ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={(currentNode ? currentNode.name : "null")} pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（ここで currentNode が確定する）
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);
    //    currentNode.RecalculateUnknownAndWall();
    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ② すでに bestTarget が決まっている場合（案Bの本質）
    //    //   → bestTarget に到達するまで絶対に再計算しない
    //    //------------------------------------------------------
    //    if (lastBestTarget != null && currentNode != lastBestTarget)
    //    {
    //        Debug.Log($"[FOLLOW] Move toward lastBestTarget={lastBestTarget.name}");

    //        // path を再構築
    //        var path = BuildShortestPath(currentNode, lastBestTarget);

    //        if (path != null && path.Count >= 2)
    //        {
    //            MapNode nextNode = path[1];
    //            Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //            dir.y = 0;

    //            Debug.Log($"[FOLLOW] Next={nextNode.name} (→ {lastBestTarget.name})");

    //            moveDir = dir;
    //            MoveForward();
    //            return;
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"[FOLLOW] path to lastBestTarget={lastBestTarget.name} not found → fallback");
    //            moveDir = ChooseRandomValidDirection(currentNode).Value;
    //            MoveForward();
    //            return;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ bestTarget に到達した場合
    //    //------------------------------------------------------
    //    if (currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[REACHED] Reached target={lastBestTarget.name}");

    //        // Unknown を優先して掘る
    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            Debug.Log("[REACHED] Unknown dig");
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        // Unknownが無い場合はターゲット再計算へ進む
    //        Debug.Log("[REACHED] No unknown → target recompute");
    //        lastBestTarget = null; // ★新規再計算トリガー
    //    }

    //    //------------------------------------------------------
    //    // ④ ここから先は「ターゲット未決定状態」のみ
    //    //   Unknown探索または最遠ノード探索を行う
    //    //------------------------------------------------------

    //    //------------------------------------------------------
    //    // ④-1 Unknown ノード探索（最遠 Unknown）
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    MapNode targetUnknown = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderByDescending(n => Distance(currentNode, n))
    //            .First();

    //        Debug.Log($"[UN] farthest Unknown = {targetUnknown.name}");
    //    }

    //    //------------------------------------------------------
    //    // ④-2 Unknown が無ければ Start から最遠（リンク BFS）
    //    //------------------------------------------------------
    //    var reachable = BFS_ReachableNodes(currentNode);
    //    MapNode targetFarthest = reachable.OrderByDescending(n => n.distanceFromStart).FirstOrDefault();

    //    //------------------------------------------------------
    //    // ④-3 bestTarget 決定
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (targetUnknown != null && targetFarthest != null)
    //    {
    //        int distU = (int)Distance(currentNode, targetUnknown);
    //        int distF = (int)Distance(currentNode, targetFarthest);

    //        Debug.Log($"[BEST] distU={distU}, distF={distF}");

    //        bestTarget = (distF - distU > 0) ? targetFarthest : targetUnknown;
    //    }
    //    else
    //    {
    //        bestTarget = targetUnknown ?? targetFarthest;
    //    }

    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] No target → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    Debug.Log($"[BEST] New bestTarget={bestTarget.name}");

    //    //------------------------------------------------------
    //    // ④-4 経路構築
    //    //------------------------------------------------------
    //    var newPath = BuildShortestPath(currentNode, bestTarget);

    //    if (newPath == null || newPath.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name} → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ④-5 nextNode へ進む
    //    //------------------------------------------------------
    //    MapNode nextNode2 = newPath[1];
    //    Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
    //    nextDir.y = 0;

    //    Debug.Log($"[PATH] Start new target={bestTarget.name}, next={nextNode2.name}");

    //    moveDir = nextDir;

    //    //------------------------------------------------------
    //    // ④-6 lastBestTarget を「ここで初めて」セット
    //    //   → 次回以降はターゲット固定モード
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;

    //    //------------------------------------------------------
    //    // ⑤ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={(currentNode ? currentNode.name : "null")} pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);
    //    currentNode.RecalculateUnknownAndWall();
    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ② bestTarget 追従中 → bestTarget に着くまで再計算しない
    //    //------------------------------------------------------
    //    if (lastBestTarget != null && currentNode != lastBestTarget)
    //    {
    //        Debug.Log($"[FOLLOW] toward {lastBestTarget.name}");

    //        var path = BuildShortestPath(currentNode, lastBestTarget);

    //        if (path != null && path.Count >= 2)
    //        {
    //            MapNode next = path[1];
    //            Vector3 dir = (next.transform.position - currentNode.transform.position).normalized;
    //            dir.y = 0;

    //            Debug.Log($"[FOLLOW] Next = {next.name}");

    //            moveDir = dir;
    //            MoveForward();
    //            return;
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"[FOLLOW] path not found → reset target");
    //            lastBestTarget = null;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ bestTarget に到達した → Unknown 優先
    //    //------------------------------------------------------
    //    if (lastBestTarget != null && currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[REACHED] reached={lastBestTarget.name}");

    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            Debug.Log("[REACHED] dig Unknown");
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            lastBestTarget = null;    // 掘り終わったら再計算へ
    //            return;
    //        }

    //        // Unknownがない → 再計算開始
    //        Debug.Log("[REACHED] no Unknown → recalc");
    //        lastBestTarget = null;
    //    }

    //    //------------------------------------------------------
    //    // ④ Unknown探索（最遠の Unknown を優先）
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    MapNode targetUnknown = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderByDescending(n => Distance(currentNode, n))
    //            .First();

    //        Debug.Log($"[UN] farthest Unknown = {targetUnknown.name}");
    //    }

    //    //------------------------------------------------------
    //    // ⑤ Unknown が無ければ Start から最遠
    //    //------------------------------------------------------
    //    var reachable = BFS_ReachableNodes(currentNode);
    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    //------------------------------------------------------
    //    // ⑥ bestTarget 決定（Unknown優先＋距離差補正）
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (targetUnknown != null && targetFarthest != null)
    //    {
    //        int distU = (int)Distance(currentNode, targetUnknown);
    //        int distF = (int)Distance(currentNode, targetFarthest);

    //        bestTarget = (distF - distU > 0) ? targetFarthest : targetUnknown;
    //    }
    //    else
    //    {
    //        bestTarget = targetUnknown ?? targetFarthest;
    //    }

    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] no target → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    Debug.Log($"[BEST] choose {bestTarget.name}");

    //    //------------------------------------------------------
    //    // ⑦ 経路生成
    //    //------------------------------------------------------
    //    var path2 = BuildShortestPath(currentNode, bestTarget);

    //    if (path2 == null || path2.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] no path → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ⑧ 次の１ノードへ移動
    //    //------------------------------------------------------
    //    MapNode next2 = path2[1];
    //    Vector3 nextDir = (next2.transform.position - currentNode.transform.position).normalized;
    //    nextDir.y = 0;

    //    moveDir = nextDir;

    //    //------------------------------------------------------
    //    // ⑨ ここで初めて lastBestTarget をロックする
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;

    //    //------------------------------------------------------
    //    // ⑩ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={(currentNode ? currentNode.name : "null")} pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（ここで currentNode が確定する）
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);
    //    currentNode.RecalculateUnknownAndWall();
    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ★ ② Unknown が近くに見えたらターゲット固定モード解除（重要）
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    bool foundNearbyUnknown = nearNodes.Any(n => n.unknownCount > 0);

    //    if (foundNearbyUnknown)
    //    {
    //        // Unknown の方が優先度が高いので固定モード解除
    //        if (lastBestTarget != null)
    //        {
    //            Debug.Log($"[OVERRIDE] Nearby Unknown detected → cancel lastBestTarget={lastBestTarget.name}");
    //            lastBestTarget = null;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ bestTarget に到達した場合
    //    //------------------------------------------------------
    //    if (currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[REACHED] Reached target={lastBestTarget.name}");

    //        // Unknown があればそっちを掘る
    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            Debug.Log("[REACHED] Unknown dig");
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        Debug.Log("[REACHED] No unknown → target recompute");
    //        lastBestTarget = null;
    //    }

    //    //------------------------------------------------------
    //    // ④ lastBestTarget がある → 経路に沿って進む（ただし Unknown override されていない時のみ）
    //    //------------------------------------------------------
    //    if (lastBestTarget != null)
    //    {
    //        Debug.Log($"[FOLLOW] toward lastBestTarget={lastBestTarget.name}");

    //        var pathFollow = BuildShortestPath(currentNode, lastBestTarget);

    //        if (pathFollow != null && pathFollow.Count >= 2)
    //        {
    //            MapNode nextNode = pathFollow[1];
    //            Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //            dir.y = 0;

    //            Debug.Log($"[FOLLOW] Next={nextNode.name}");
    //            moveDir = dir;
    //            MoveForward();
    //            return;
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"[FOLLOW] Path broken → cancel target");
    //            lastBestTarget = null;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ★ Unknown があるなら最優先で Unknown 方向に進む
    //    //------------------------------------------------------
    //    if (currentNode.unknownCount > 0)
    //    {
    //        Vector3? udirNow = currentNode.GetUnknownDirection();
    //        if (udirNow.HasValue)
    //        {
    //            Debug.Log($"[FORCE-UNKNOWN] Dig Unknown at {currentNode.name}");
    //            lastBestTarget = null;   // FOLLOWを強制キャンセル
    //            moveDir = udirNow.Value.normalized;
    //            MoveForward();
    //            return;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ⑤ Unknown の最遠を探す（Unknown が優先）
    //    //------------------------------------------------------
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();
    //    MapNode targetUnknown = null;

    //    if (unknownNodes.Count > 0)
    //    {
    //        targetUnknown = unknownNodes
    //            .OrderByDescending(n => Distance(currentNode, n))
    //            .First();

    //        Debug.Log($"[UN] farthest Unknown = {targetUnknown.name}");
    //    }

    //    //------------------------------------------------------
    //    // ⑥ Unknown が無ければ Start から最遠ノード
    //    //------------------------------------------------------
    //    var reachable = BFS_ReachableNodes(currentNode);
    //    MapNode targetFarthest = reachable
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    //------------------------------------------------------
    //    // ⑦ bestTarget 決定
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (targetUnknown != null && targetFarthest != null)
    //    {
    //        int distU = (int)Distance(currentNode, targetUnknown);
    //        int distF = (int)Distance(currentNode, targetFarthest);

    //        Debug.Log($"[BEST] distU={distU}, distF={distF}");

    //        bestTarget = (distF - distU > 0) ? targetFarthest : targetUnknown;
    //    }
    //    else
    //    {
    //        bestTarget = targetUnknown ?? targetFarthest;
    //    }

    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] No target → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    Debug.Log($"[BEST] New bestTarget={bestTarget.name}");

    //    //------------------------------------------------------
    //    // ⑧ 経路構築
    //    //------------------------------------------------------
    //    var newPath = BuildShortestPath(currentNode, bestTarget);

    //    if (newPath == null || newPath.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name} → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ⑨ nextNode に進む
    //    //------------------------------------------------------
    //    MapNode nextNode2 = newPath[1];
    //    Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
    //    nextDir.y = 0;

    //    Debug.Log($"[PATH] Go to {nextNode2.name} (target={bestTarget.name})");

    //    moveDir = nextDir;

    //    //------------------------------------------------------
    //    // ⑩ bestTarget を確定
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;

    //    //------------------------------------------------------
    //    // ⑪ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    private void TryExploreMove()
    {
        Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, pos={transform.position}");
        Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

        //------------------------------------------------------
        // ① Node生成・更新（ここで currentNode が確定する）
        //------------------------------------------------------
        currentNode = TryPlaceNode(transform.position);
        currentNode.RecalculateUnknownAndWall();
        RegisterCurrentNode(currentNode);

        //------------------------------------------------------
        // ② bestTarget が既に決まっている場合（案B：到達するまで再計算しない）
        //------------------------------------------------------
        if (lastBestTarget != null && currentNode != lastBestTarget)
        {
            Debug.Log($"[FOLLOW] toward lastBestTarget={lastBestTarget.name}");

            var path = BuildShortestPath(currentNode, lastBestTarget);

            if (path != null && path.Count >= 2)
            {
                MapNode nextNode = path[1];
                Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
                dir.y = 0;

                moveDir = dir;
                MoveForward();
                return;
            }
            else
            {
                Debug.LogWarning($"[FOLLOW] path to {lastBestTarget.name} not found → fallback");
                moveDir = ChooseRandomValidDirection(currentNode).Value;
                MoveForward();
                return;
            }
        }

        //------------------------------------------------------
        // ③ bestTarget に到達した場合
        //------------------------------------------------------
        if (currentNode == lastBestTarget)
        {
            Debug.Log($"[REACHED] Target reached={lastBestTarget.name}");

            Vector3? udir = currentNode.GetUnknownDirection();
            if (udir.HasValue)
            {
                Debug.Log("[REACHED] Unknown dig");
                moveDir = udir.Value.normalized;
                MoveForward();
                return;
            }

            // Unknown が無いなら target 再計算へ
            lastBestTarget = null;
        }

        //------------------------------------------------------
        // ④ Unknown & Start最遠 判定フェーズ（ターゲット未決定状態）
        //------------------------------------------------------

        // ■ 探索範囲のノードを取得
        var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);

        // ■ 探索範囲内 Unknown ノード
        var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

        //------------------------------------------------------
        // ④-1 探索範囲内 Unknown の current 最遠ノード
        //------------------------------------------------------
        MapNode unknownFarthest = null;
        if (unknownNodes.Count > 0)
        {
            unknownFarthest = unknownNodes
                .OrderByDescending(n => Distance(currentNode, n))
                .First();

            Debug.Log($"[UN-CUR] farthest Unknown in range = {unknownFarthest.name}");
        }

        //------------------------------------------------------
        // ④-2 探索範囲内 StartNode から最遠ノード
        //------------------------------------------------------
        MapNode localFarthestFromStart = nearNodes
            .OrderByDescending(n => n.distanceFromStart)
            .FirstOrDefault();

        if (localFarthestFromStart != null)
            Debug.Log($"[LOCAL-FAR] Start最遠 in range = {localFarthestFromStart.name}");

        //------------------------------------------------------
        // ④-3 bestTarget 決定（コアロジック）
        //------------------------------------------------------
        MapNode bestTarget = null;

        bool existsStartFarthestInRange = localFarthestFromStart != null;

        if (existsStartFarthestInRange)
        {
            // Start最遠が探索範囲に存在する時 → Unknown と Start最遠 の current距離で比較
            if (unknownFarthest != null)
            {
                float dU = Distance(currentNode, unknownFarthest);
                float dS = Distance(currentNode, localFarthestFromStart);

                Debug.Log($"[COMPARE] distToUnknown={dU}, distToStartFar={dS}");

                bestTarget = (dU > dS) ? unknownFarthest : localFarthestFromStart;
            }
            else
            {
                // Unknownがない → Start最遠のみ
                bestTarget = localFarthestFromStart;
            }
        }
        else
        {
            // Start最遠が探索範囲にいない → Unknown最遠のみを採用
            bestTarget = unknownFarthest;
        }

        //------------------------------------------------------
        // ④-4 bestTarget が null → fallback
        //------------------------------------------------------
        if (bestTarget == null)
        {
            Debug.LogWarning("[BEST] bestTarget NULL → fallback");
            moveDir = ChooseRandomValidDirection(currentNode).Value;
            MoveForward();
            return;
        }

        Debug.Log($"[BEST] Selected bestTarget={bestTarget.name}");

        //------------------------------------------------------
        // ④-5 経路作成
        //------------------------------------------------------
        var path2 = BuildShortestPath(currentNode, bestTarget);

        if (path2 == null || path2.Count < 2)
        {
            Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name} → fallback");
            moveDir = ChooseRandomValidDirection(currentNode).Value;
            MoveForward();
            return;
        }

        //------------------------------------------------------
        // ④-6 次ノードへ進む
        //------------------------------------------------------
        MapNode nextNode2 = path2[1];
        Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
        nextDir.y = 0;

        moveDir = nextDir;

        Debug.Log($"[PATH] Go to {nextNode2.name} (target={bestTarget.name})");

        //------------------------------------------------------
        // ④-7 lastBestTarget のセット（重要）
        //------------------------------------------------------
        lastBestTarget = bestTarget;

        //------------------------------------------------------
        // ⑤ 移動
        //------------------------------------------------------
        MoveForward();
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
    //private MapNode ChooseBestTarget(MapNode targetUnknown, MapNode targetFarthest)
    //{
    //    float scoreU = float.MinValue;
    //    float scoreF = float.MinValue;

    //    if (targetUnknown != null)
    //        scoreU = Score(targetUnknown);

    //    if (targetFarthest != null)
    //        scoreF = Score(targetFarthest);

    //    return (scoreU >= scoreF) ? targetUnknown : targetFarthest;
    //}
    //private MapNode ChooseBestTarget(MapNode targetUnknown, MapNode targetFarthest)
    //{
    //    // ① 自身は除外
    //    if (targetUnknown == currentNode) targetUnknown = null;
    //    if (targetFarthest == currentNode) targetFarthest = null;

    //    // ★ ここにログを入れる
    //    Debug.Log(
    //        $"[BestTarget-Input] current={currentNode.name}, " +
    //        $"targetUnknown={(targetUnknown ? targetUnknown.name : "null")}, " +
    //        $"targetFarthest={(targetFarthest ? targetFarthest.name : "null")}"
    //    );

    //    List<MapNode> candidates = new List<MapNode>();

    //    // ② Unknown を最優先（到達可能性チェックはしない！）
    //    if (targetUnknown != null)
    //        candidates.Add(targetUnknown);

    //    // ③ Unknown が無い場合は Farthest
    //    if (candidates.Count == 0 && targetFarthest != null)
    //        candidates.Add(targetFarthest);

    //    // ★ 候補確認ログ
    //    Debug.Log(
    //        $"[BestTarget-Candidates] count={candidates.Count} | " +
    //        $"{string.Join(", ", candidates.Select(n => n.name))}"
    //    );

    //    if (candidates.Count == 0)
    //    {
    //        Debug.LogWarning("[BestTarget] No valid target");
    //        return null;
    //    }

    //    // ★ スコアをログ出力
    //    foreach (var n in candidates)
    //        Debug.Log($"[BestTarget-Score] {n.name} score={Score(n)}");

    //    //// ④ スコア最大
    //    //return candidates
    //    //    .OrderByDescending(n => Score(n))
    //    //    .First();
    //    MapNode best = candidates
    //        .OrderByDescending(n => Score(n))
    //        .First();

    //    Debug.Log(
    //        $"[BestTarget] return={best.name} | " +
    //        $"Unknown={targetUnknown?.name}, Farthest={targetFarthest?.name}, " +
    //        $"Score(best)={Score(best)}"
    //    );


    //    return best;
    //}
    private MapNode ChooseBestTarget(MapNode targetUnknown, MapNode targetFarthest)
    {
        // Unknown だけ
        if (targetUnknown != null && targetFarthest == null)
        {
            Debug.Log($"[BEST] UnknownOnly → return {targetUnknown.name}");
            return targetUnknown;
        }

        // Farthest だけ
        if (targetUnknown == null && targetFarthest != null)
        {
            Debug.Log($"[BEST] FarthestOnly → return {targetFarthest.name}");
            return targetFarthest;
        }

        // 両方ある場合：距離差で勝負
        int distUnknown = Mathf.Abs(targetUnknown.distanceFromStart - currentNode.distanceFromStart);
        int distFarthest = Mathf.Abs(targetFarthest.distanceFromStart - currentNode.distanceFromStart);

        MapNode best =
            (distUnknown >= distFarthest) ? targetUnknown : targetFarthest;

        Debug.Log($"[CHECK-BEST] bestTarget={best?.name}, " +
          $"current={currentNode?.name}, " +
          $"targetUnknown={targetUnknown?.name}, " +
          $"targetFarthest={targetFarthest?.name}");

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
    //private List<MapNode> BuildShortestPath(MapNode start, MapNode goal)
    //{
    //    Queue<MapNode> q = new();
    //    Dictionary<MapNode, MapNode> parent = new();

    //    q.Enqueue(start);
    //    parent[start] = null;

    //    while (q.Count > 0)
    //    {
    //        var node = q.Dequeue();
    //        if (node == goal) break;

    //        foreach (var next in node.links)
    //        {
    //            if (next == null) continue;
    //            if (!parent.ContainsKey(next))
    //            {
    //                parent[next] = node;
    //                q.Enqueue(next);
    //            }
    //        }
    //    }

    //    if (!parent.ContainsKey(goal)) return null;

    //    List<MapNode> path = new();
    //    MapNode cur = goal;
    //    while (cur != null)
    //    {
    //        path.Add(cur);
    //        cur = parent[cur];
    //    }
    //    path.Reverse();
    //    return path;
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

    //        foreach (var next in node.links)
    //        {
    //            if (!prev.ContainsKey(next))
    //            {
    //                prev[next] = node;
    //                q.Enqueue(next);

    //                if (next == goal)
    //                {
    //                    // reconstruct
    //                    List<MapNode> path = new List<MapNode>();
    //                    MapNode cur = goal;

    //                    while (cur != null)
    //                    {
    //                        path.Add(cur);
    //                        cur = prev[cur];
    //                    }

    //                    path.Reverse();
    //                    return path;
    //                }
    //            }
    //        }
    //    }

    //    return null;
    //}
    private List<MapNode> BuildShortestPath(MapNode start, MapNode goal)
    {
        if (start == null || goal == null) return null;
        if (start == goal) return new List<MapNode>() { start };

        Queue<MapNode> q = new Queue<MapNode>();
        Dictionary<MapNode, MapNode> prev = new Dictionary<MapNode, MapNode>();

        q.Enqueue(start);
        prev[start] = null;

        while (q.Count > 0)
        {
            var node = q.Dequeue();

            // ★ 修正ポイント：リンクが片方向でも双方向扱いにする
            foreach (var next in node.links)
            {
                if (!prev.ContainsKey(next))
                {
                    prev[next] = node;
                    q.Enqueue(next);

                    if (next == goal)
                        return Rebuild(prev, start, goal);
                }
            }

            // ★ 追加：逆リンクも救済（“node → X” ではなく “X → node” のみ存在するケース）
            foreach (var other in MapNode.allNodes)
            {
                if (other.links.Contains(node)) // ← 逆リンク
                {
                    if (!prev.ContainsKey(other))
                    {
                        prev[other] = node;
                        q.Enqueue(other);

                        if (other == goal)
                            return Rebuild(prev, start, goal);
                    }
                }
            }
        }

        return null; // 到達不可
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
        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        return Physics.Raycast(origin, dir, cellSize, wallLayer);
    }

    private MapNode TryPlaceNode(Vector3 pos)
    {
        Vector3 snapped = SnapToGrid(pos);
        Vector2Int cell = WorldToCell(snapped);

        Debug.Log($"[TP0] TryPlaceNode START | pos={pos} | snapped={snapped} | cell={cell}");

        MapNode node;

        if (MapNode.allNodeCells.Contains(cell))
        {
            node = MapNode.FindByCell(cell);
            Debug.Log($"[TP1] Existing Node FOUND | node={node.name} | links={node.links.Count}");
        }
        else
        {
            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);

            Debug.Log($"[TP2] NEW Node CREATED | node={node.name} | cell={cell}");
        }

        Debug.Log($"[TP3] Before LinkBackward | node={node.name}");
        LinkBackward(node);
        Debug.Log($"[TP4] After LinkBackward | node={node.name} | links={node.links.Count}");
        return node;
    }

    private void LinkBackward(MapNode node)
    {
        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        Vector3 dir = -moveDir;

        Debug.Log($"[LB0] LinkBackward START | from={node.name} | dir={dir}");

        LayerMask mask = wallLayer | nodeLayer;

        for (int i = 1; i <= linkRayMaxSteps; i++)
        {
            float dist = cellSize * i;

            if (debugRay)
                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);


            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
            {
                int layer = hit.collider.gameObject.layer;

                Debug.Log($"[LB1] Ray Hit | dist={dist} | hit={hit.collider.name}");

                if ((wallLayer.value & (1 << layer)) != 0)
                {
                    Debug.Log($"[LB2] Hit WALL → stop");
                    return;
                }

                if ((nodeLayer.value & (1 << layer)) != 0)
                {
                    var hitNode = hit.collider.GetComponent<MapNode>();
                    if (hitNode != null && hitNode != node)
                    {
                        Debug.Log($"[LB3] Linking {node.name} ↔ {hitNode.name}");

                        node.AddLink(hitNode);
                        node.RecalculateUnknownAndWall();
                        hitNode.RecalculateUnknownAndWall();
                    }
                    else
                    {
                        Debug.Log($"[LB4] Hit self or null");
                    }
                    return;
                }
            }
        }

        Debug.Log($"[LB5] No hit up to {linkRayMaxSteps} steps");
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
        if (debugLog) Debug.Log("[CellFS] " + msg);
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