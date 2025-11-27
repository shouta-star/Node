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
    private bool arrivedThisNode = false;
    private bool blockTryExploreThisFrame = false;

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


    //void Update()
    //{
    //    if (blockTryExploreThisFrame)
    //    {
    //        blockTryExploreThisFrame = false;
    //        return;
    //    }

    //    if (!isMoving)
    //    {
    //        if (CanPlaceNodeHere())
    //            TryExploreMove();
    //        else
    //            MoveForward();
    //    }
    //    else
    //    {
    //        MoveToTarget();
    //    }
    //}
    void Update()
    {
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
            Debug.Log("[UPDATE] blockTryExploreThisFrame → skip TryExploreMove");
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
                Debug.Log("[UPDATE] Node到達直後 → TryExploreMove()");
                arrivedThisNode = false;
                TryExploreMove();
                return;
            }
            else
            {
                Debug.Log("[UPDATE] Node中心だが到達直後でない → MoveForward()");
                MoveForward();
                return;
            }
        }
        //else
        {
            Debug.Log("[UPDATE] CanPlaceNodeHere()=false → MoveForward()");
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

    //private void MoveToTarget()
    //{
    //    if (Vector3.Distance(transform.position, targetPos) > 0.01f)
    //        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
    //    else
    //    {
    //        transform.position = targetPos;
    //        isMoving = false;
    //    }
    //}
    //private void MoveToTarget()
    //{
    //    // ★ 許容誤差を広げる　（重要）
    //    const float arriveThreshold = 0.1f;

    //    if (Vector3.Distance(transform.position, targetPos) > arriveThreshold)
    //    {
    //        transform.position = Vector3.MoveTowards(
    //            transform.position,
    //            targetPos,
    //            moveSpeed * Time.deltaTime);
    //    }
    //    else
    //    {
    //        // 誤差吸収
    //        transform.position = targetPos;
    //        isMoving = false;
    //    }
    //}
    //private void MoveToTarget()
    //{
    //    if (Vector3.Distance(transform.position, targetPos) > 0.01f)
    //    {
    //        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
    //    }
    //    else
    //    {
    //        // ★ 到達
    //        transform.position = targetPos;
    //        isMoving = false;

    //        // EveryNode → Node 到達時にだけ bestTarget 再計算する
    //        if (targetUpdateMode == TargetUpdateMode.EveryNode)
    //        {
    //            lastBestTarget = null;     // 常に更新するのでリセット
    //            Debug.Log("[EVERY NODE] Node 到達 → bestTarget を再計算します");
    //        }
    //    }
    //}
    //private void MoveToTarget()
    //{
    //    if (!isMoving) return;

    //    const float arriveThreshold = 0.001f;

    //    if (Vector3.Distance(transform.position, targetPos) > arriveThreshold)
    //    {
    //        transform.position = Vector3.MoveTowards(
    //            transform.position,
    //            targetPos,
    //            moveSpeed * Time.deltaTime
    //        );
    //    }
    //    else
    //    {
    //        transform.position = targetPos;
    //        isMoving = false;

    //        // ★ Node 到達は1回だけ
    //        if (!arrivedThisNode)
    //        {
    //            arrivedThisNode = true;

    //            if (targetUpdateMode == TargetUpdateMode.EveryNode)
    //            {
    //                lastBestTarget = null;
    //                Debug.Log("[EVERY NODE] Node 到達 → bestTarget 再計算");
    //            }
    //        }

    //        blockTryExploreThisFrame = true;
    //    }
    //}
    //private void MoveToTarget()
    //{
    //    if (!isMoving) return;

    //    const float arriveThreshold = 0.05f;

    //    if (Vector3.Distance(transform.position, targetPos) > arriveThreshold)
    //    {
    //        transform.position = Vector3.MoveTowards(
    //            transform.position,
    //            targetPos,
    //            moveSpeed * Time.deltaTime
    //        );
    //    }
    //    else
    //    {
    //        // ★ 完全到達
    //        transform.position = targetPos;
    //        isMoving = false;

    //        // ★ Node へ「初めて」到達した瞬間だけ実行
    //        if (!arrivedThisNode)
    //        {
    //            arrivedThisNode = true;

    //            if (targetUpdateMode == TargetUpdateMode.EveryNode)
    //            {
    //                lastBestTarget = null;
    //                Debug.Log("[EVERY NODE] Node 到達 → bestTarget を再計算します");
    //            }
    //        }

    //        // このフレームの TryExploreMove はブロック
    //        blockTryExploreThisFrame = true;
    //    }
    //}
    //private void MoveToTarget()
    //{
    //    if (!isMoving) return;

    //    const float arriveThreshold = 0.01f;

    //    if (Vector3.Distance(transform.position, targetPos) > arriveThreshold)
    //    {
    //        transform.position = Vector3.MoveTowards(
    //            transform.position,
    //            targetPos,
    //            moveSpeed * Time.deltaTime
    //        );
    //        return;
    //    }
    //    else
    //    {
    //        // ★ 完全到達
    //        transform.position = targetPos;
    //        isMoving = false;

    //        // ★ Node到達後、このフレームで再び TryExploreMove が走らないようにする
    //        blockTryExploreThisFrame = true;

    //        // ★ arrivedThisNode フラグ（Nodeについた瞬間だけ true）
    //        if (!arrivedThisNode)
    //        {
    //            arrivedThisNode = true;

    //            //if (targetUpdateMode == TargetUpdateMode.EveryNode)
    //            //{
    //            //    lastBestTarget = null;
    //            //    Debug.Log("[EVERY NODE] Node 到達 → bestTarget を再計算します");
    //            //}
    //        }
    //    }
    //}
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

        Debug.Log(
            $"[CHECK-ARRIVE] Arrived at targetPos={targetPos} | " +
            $"actualPos={transform.position} | " +
            $"arrivedThisNode={arrivedThisNode}"
        );


        isMoving = false;

        // ★ Nodeに「初めて」到達した瞬間だけ実行する
        if (!arrivedThisNode)
        {
            arrivedThisNode = true;  // 次フレーム以降は無効

            // ★ここが今回の追加ポイント
            currentNode = MapNode.FindByCell(WorldToCell(targetPos));

            // ★ EveryNode：Nodeに到達した瞬間だけ bestTarget をクリア
            //if (targetUpdateMode == TargetUpdateMode.EveryNode)
            //{
            //    lastBestTarget = null;
            //    Debug.Log("[EVERY NODE] Node 到達 → bestTarget をクリアします");
            //}
        }

        //------------------------------------------------------
        // ★ このフレームに TryExploreMove() を呼ばせない
        //------------------------------------------------------
        blockTryExploreThisFrame = true;
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
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（ここで currentNode が確定する）
    //    //------------------------------------------------------
    //    currentNode = TryPlaceNode(transform.position);
    //    currentNode.RecalculateUnknownAndWall();
    //    RegisterCurrentNode(currentNode);

    //    ////------------------------------------------------------
    //    //// ② bestTarget が既に決まっている場合（案B：到達するまで再計算しない）
    //    ////------------------------------------------------------
    //    //if (lastBestTarget != null && currentNode != lastBestTarget)
    //    //{
    //    //    Debug.Log($"[FOLLOW] toward lastBestTarget={lastBestTarget.name}");

    //    //    var path = BuildShortestPath(currentNode, lastBestTarget);

    //    //    if (path != null && path.Count >= 2)
    //    //    {
    //    //        MapNode nextNode = path[1];
    //    //        Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //    //        dir.y = 0;

    //    //        moveDir = dir;
    //    //        MoveForward();
    //    //        return;
    //    //    }
    //    //    else
    //    //    {
    //    //        Debug.LogWarning($"[FOLLOW] path to {lastBestTarget.name} not found → fallback");
    //    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //    //        MoveForward();
    //    //        return;
    //    //    }
    //    //}
    //    //------------------------------------------------------
    //    // ② bestTarget が既に決まっている場合（更新方式：EveryNode / OnArrival）
    //    //------------------------------------------------------

    //    // EveryNode の場合 → FOLLOW を無効化（必ず再計算へ進む）
    //    // OnArrival の場合 → 最後のターゲットに到達するまで FOLLOW 継続
    //    bool followMode =
    //        (targetUpdateMode == TargetUpdateMode.OnArrival) &&
    //        (lastBestTarget != null && currentNode != lastBestTarget);

    //    if (followMode)
    //    {
    //        Debug.Log($"[FOLLOW] toward lastBestTarget={lastBestTarget.name}");

    //        var path = BuildShortestPath(currentNode, lastBestTarget);

    //        if (path != null && path.Count >= 2)
    //        {
    //            MapNode nextNode = path[1];
    //            Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //            dir.y = 0;

    //            moveDir = dir;
    //            MoveForward();
    //            return;   // ★ FOLLOW 継続（OnArrival のみ）
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"[FOLLOW] path to {lastBestTarget.name} not found → fallback");
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
    //        Debug.Log($"[REACHED] Target reached={lastBestTarget.name}");

    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            Debug.Log("[REACHED] Unknown dig");
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        // Unknown が無いなら target 再計算へ
    //        lastBestTarget = null;
    //    }

    //    //------------------------------------------------------
    //    // ④ Unknown & Start最遠 判定フェーズ（ターゲット未決定状態）
    //    //------------------------------------------------------

    //    // ■ 探索範囲のノードを取得
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);

    //    // ■ 探索範囲内 Unknown ノード
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    //------------------------------------------------------
    //    // ④-1 探索範囲内 Unknown の current 最遠ノード
    //    //------------------------------------------------------
    //    //MapNode unknownFarthest = null;
    //    //if (unknownNodes.Count > 0)
    //    //{
    //    //    unknownFarthest = unknownNodes
    //    //        .OrderByDescending(n => Distance(currentNode, n))
    //    //        .First();

    //    //    Debug.Log($"[UN-CUR] farthest Unknown in range = {unknownFarthest.name}");
    //    //}
    //    // === Unknown 選択切り替え対応 ===
    //    MapNode unknownTarget = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        unknownTarget = SelectUnknownNode(unknownNodes, currentNode);

    //        Debug.Log($"[UN-CUR] Selected Unknown = {unknownTarget.name}  mode={unknownSelectMode}");
    //    }


    //    //------------------------------------------------------
    //    // ④-2 探索範囲内 StartNode から最遠ノード
    //    //------------------------------------------------------
    //    MapNode localFarthestFromStart = nearNodes
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    if (localFarthestFromStart != null)
    //        Debug.Log($"[LOCAL-FAR] Start最遠 in range = {localFarthestFromStart.name}");

    //    //------------------------------------------------------
    //    // ④-3 bestTarget 決定（コアロジック）
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    bool existsStartFarthestInRange = localFarthestFromStart != null;

    //    if (existsStartFarthestInRange)
    //    {
    //        // Start最遠が探索範囲に存在する時 → Unknown と Start最遠 の current距離で比較
    //        if (unknownTarget != null)
    //        {
    //            float dU = Distance(currentNode, unknownTarget);
    //            float dS = Distance(currentNode, localFarthestFromStart);

    //            Debug.Log($"[COMPARE] distToUnknown={dU}, distToStartFar={dS}");

    //            bestTarget = (dU > dS) ? unknownTarget : localFarthestFromStart;
    //        }
    //        else
    //        {
    //            // Unknownがない → Start最遠のみ
    //            bestTarget = localFarthestFromStart;
    //        }
    //    }
    //    else
    //    {
    //        // Start最遠が探索範囲にいない → Unknown最遠のみを採用
    //        bestTarget = unknownTarget;
    //    }

    //    //------------------------------------------------------
    //    // ④-4 bestTarget が null → fallback
    //    //------------------------------------------------------
    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] bestTarget NULL → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    Debug.Log($"[BEST] Selected bestTarget={bestTarget.name}");

    //    //------------------------------------------------------
    //    // ④-5 経路作成
    //    //------------------------------------------------------
    //    var path2 = BuildShortestPath(currentNode, bestTarget);

    //    if (path2 == null || path2.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name} → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ④-6 次ノードへ進む
    //    //------------------------------------------------------
    //    MapNode nextNode2 = path2[1];
    //    Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
    //    nextDir.y = 0;

    //    moveDir = nextDir;

    //    Debug.Log($"[PATH] Go to {nextNode2.name} (target={bestTarget.name})");

    //    //------------------------------------------------------
    //    // ④-7 lastBestTarget のセット（重要）
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;

    //    //------------------------------------------------------
    //    // ⑤ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    arrivedThisNode = false;

    //    //------------------------------------------------------
    //    // ① Node生成・更新（ここで currentNode が確定する）
    //    //------------------------------------------------------
    //    var oldNode = currentNode;
    //    currentNode = TryPlaceNode(transform.position);
    //    currentNode.RecalculateUnknownAndWall();
    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ★ EveryNode：Nodeに到達した瞬間のみ target 破棄
    //    //------------------------------------------------------
    //    bool nodeJustArrived = (oldNode != currentNode);

    //    //if (targetUpdateMode == TargetUpdateMode.EveryNode && nodeJustArrived)
    //    //{
    //    //    lastBestTarget = null;
    //    //    Debug.Log("[MODE] EveryNode → Node到達時に bestTarget を再計算します");
    //    //}

    //    //------------------------------------------------------
    //    // ② FOLLOW（OnArrival の時だけ）
    //    //------------------------------------------------------
    //    bool followMode =
    //        (targetUpdateMode == TargetUpdateMode.OnArrival) &&
    //        (lastBestTarget != null && currentNode != lastBestTarget);

    //    if (followMode)
    //    {
    //        Debug.Log($"[FOLLOW] toward lastBestTarget={lastBestTarget.name}");

    //        var path = BuildShortestPath(currentNode, lastBestTarget);

    //        if (path != null && path.Count >= 2)
    //        {
    //            MapNode nextNode = path[1];
    //            Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //            dir.y = 0;

    //            moveDir = dir;
    //            MoveForward();
    //            return;
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"[FOLLOW] path to {lastBestTarget.name} not found → fallback");
    //            moveDir = ChooseRandomValidDirection(currentNode).Value;
    //            MoveForward();
    //            return;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ bestTarget に到達した場合
    //    //  ★ EveryNode では完全無効化！
    //    //------------------------------------------------------
    //    if (targetUpdateMode == TargetUpdateMode.OnArrival &&
    //        currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[REACHED] Target reached={lastBestTarget.name}");

    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            Debug.Log("[REACHED] Unknown dig");
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        lastBestTarget = null;
    //    }

    //    //------------------------------------------------------
    //    // ④ Unknown & Start最遠 判定フェーズ（ターゲット未決定状態）
    //    //------------------------------------------------------

    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    //------------------------------------------------------
    //    // ④-1 Unknown ノード選択
    //    //------------------------------------------------------
    //    MapNode unknownTarget = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        unknownTarget = SelectUnknownNode(unknownNodes, currentNode);
    //        Debug.Log($"[UN-CUR] Selected Unknown = {unknownTarget.name}  mode={unknownSelectMode}");
    //    }

    //    //------------------------------------------------------
    //    // ④-2 Start から最遠ノード
    //    //------------------------------------------------------
    //    MapNode localFarthestFromStart = nearNodes
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    if (localFarthestFromStart != null)
    //        Debug.Log($"[LOCAL-FAR] Start最遠 in range = {localFarthestFromStart.name}");

    //    //------------------------------------------------------
    //    // ④-3 bestTarget 決定
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (localFarthestFromStart != null)
    //    {
    //        if (unknownTarget != null)
    //        {
    //            float dU = Distance(currentNode, unknownTarget);
    //            float dS = Distance(currentNode, localFarthestFromStart);

    //            Debug.Log($"[COMPARE] distToUnknown={dU}, distToStartFar={dS}");

    //            bestTarget = (dU > dS) ? unknownTarget : localFarthestFromStart;
    //        }
    //        else
    //        {
    //            bestTarget = localFarthestFromStart;
    //        }
    //    }
    //    else
    //    {
    //        bestTarget = unknownTarget;
    //    }

    //    //------------------------------------------------------
    //    // ④-4 fallback
    //    //------------------------------------------------------
    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] bestTarget NULL → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    Debug.Log($"[BEST] Selected bestTarget={bestTarget.name}");

    //    //------------------------------------------------------
    //    // ④-5 経路作成
    //    //------------------------------------------------------
    //    var path2 = BuildShortestPath(currentNode, bestTarget);

    //    if (path2 == null || path2.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name} → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ④-6 次ノードへ進む
    //    //------------------------------------------------------
    //    MapNode nextNode2 = path2[1];
    //    Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
    //    nextDir.y = 0;

    //    moveDir = nextDir;

    //    Debug.Log($"[PATH] Go to {nextNode2.name} (target={bestTarget.name})");

    //    //------------------------------------------------------
    //    // ④-7 lastBestTarget のセット
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;

    //    //------------------------------------------------------
    //    // ⑤ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    // ★ 到達直後のフレームでは実行しない
    //    if (blockTryExploreThisFrame)
    //    {
    //        blockTryExploreThisFrame = false;
    //        return;
    //    }

    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（Nodeスナップ後に呼ばれる前提）
    //    //------------------------------------------------------
    //    //MapNode oldNode = currentNode;

    //    currentNode = TryPlaceNode(transform.position);
    //    currentNode.RecalculateUnknownAndWall();
    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ★ Node到達判定（MoveToTarget と連携して正しく動作）
    //    //------------------------------------------------------
    //    //bool nodeJustArrived = (oldNode != currentNode);

    //    //// ★ EveryNode：Node到達時のみターゲット再計算
    //    //if (targetUpdateMode == TargetUpdateMode.EveryNode && nodeJustArrived)
    //    //{
    //    //    lastBestTarget = null;
    //    //    Debug.Log("[EVERY NODE] Node 判定 → bestTarget をクリア");
    //    //}

    //    //------------------------------------------------------
    //    // ② FOLLOW：OnArrival時のみ
    //    //------------------------------------------------------
    //    bool followMode =
    //        (targetUpdateMode == TargetUpdateMode.OnArrival) &&
    //        (lastBestTarget != null && currentNode != lastBestTarget);

    //    if (followMode)
    //    {
    //        Debug.Log($"[FOLLOW] toward lastBestTarget={lastBestTarget.name}");

    //        var path = BuildShortestPath(currentNode, lastBestTarget);

    //        if (path != null && path.Count >= 2)
    //        {
    //            MapNode nextNode = path[1];
    //            Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //            dir.y = 0;

    //            moveDir = dir;
    //            MoveForward();
    //            return;
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"[FOLLOW] path to {lastBestTarget.name} not found → fallback");
    //            moveDir = ChooseRandomValidDirection(currentNode).Value;
    //            MoveForward();
    //            return;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ bestTargetに到達した処理（OnArrival）
    //    //------------------------------------------------------
    //    if (currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[REACHED] Target reached={lastBestTarget.name}");

    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        lastBestTarget = null;
    //    }

    //    //------------------------------------------------------
    //    // ④ Unknown & Start最遠 ノード検索
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    MapNode unknownTarget = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        unknownTarget = SelectUnknownNode(unknownNodes, currentNode);
    //        Debug.Log($"[UN-CUR] Selected Unknown = {unknownTarget.name}");
    //    }

    //    MapNode localFarthestFromStart = nearNodes
    //        .OrderByDescending(n => n.distanceFromStart)
    //        .FirstOrDefault();

    //    MapNode bestTarget = null;

    //    if (unknownTarget != null && localFarthestFromStart != null)
    //    {
    //        float dU = Distance(currentNode, unknownTarget);
    //        float dS = Distance(currentNode, localFarthestFromStart);

    //        bestTarget = (dU > dS) ? unknownTarget : localFarthestFromStart;
    //    }
    //    else
    //    {
    //        bestTarget = unknownTarget ?? localFarthestFromStart;
    //    }

    //    if (bestTarget == null)
    //    {
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    var path2 = BuildShortestPath(currentNode, bestTarget);
    //    if (path2 == null || path2.Count < 2)
    //    {
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    MapNode nextNode2 = path2[1];
    //    Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
    //    nextDir.y = 0;

    //    moveDir = nextDir;

    //    lastBestTarget = bestTarget;

    //    MoveForward();
    //}
    //private void TryExploreMove()
    //{
    //    // ★ Node到達直後のフレームでは実行しない（この1行が超重要）
    //    if (blockTryExploreThisFrame)
    //    {
    //        //blockTryExploreThisFrame = false;
    //        return;
    //    }
    //    //else
    //    //{
    //    //    // このフレームだけ TryExploreMove を実行する
    //    //    arrivedThisNode = false;
    //    //}

    //    Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, pos={transform.position}");
    //    Debug.Log($"[TryExploreMove] lastBestTarget={lastBestTarget?.name}");

    //    //------------------------------------------------------
    //    // ① Node生成・更新（Nodeスナップ後に呼ばれる前提）
    //    //------------------------------------------------------
    //    //MapNode oldNode = currentNode;

    //    currentNode = TryPlaceNode(transform.position);
    //    currentNode.RecalculateUnknownAndWall();
    //    RegisterCurrentNode(currentNode);

    //    //------------------------------------------------------
    //    // ② FOLLOW（OnArrival の時だけ機能させる）
    //    //------------------------------------------------------
    //    //bool nodeJustArrived = (oldNode != currentNode);

    //    bool followMode =
    //        (targetUpdateMode == TargetUpdateMode.OnArrival) &&
    //        (lastBestTarget != null && currentNode != lastBestTarget);

    //    if (followMode)
    //    {
    //        Debug.Log($"[FOLLOW] toward lastBestTarget={lastBestTarget.name}");

    //        var path = BuildShortestPath(currentNode, lastBestTarget);

    //        if (path != null && path.Count >= 2)
    //        {
    //            MapNode nextNode = path[1];
    //            Vector3 dir = (nextNode.transform.position - currentNode.transform.position).normalized;
    //            dir.y = 0;

    //            moveDir = dir;
    //            MoveForward();
    //            return;
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"[FOLLOW] path to {lastBestTarget.name} not found → fallback");
    //            moveDir = ChooseRandomValidDirection(currentNode).Value;
    //            MoveForward();
    //            return;
    //        }
    //    }

    //    //------------------------------------------------------
    //    // ③ bestTargetへ到達した時（OnArrival）
    //    //------------------------------------------------------
    //    if (currentNode == lastBestTarget)
    //    {
    //        Debug.Log($"[REACHED] Target reached={lastBestTarget.name}");

    //        Vector3? udir = currentNode.GetUnknownDirection();
    //        if (udir.HasValue)
    //        {
    //            moveDir = udir.Value.normalized;
    //            MoveForward();
    //            return;
    //        }

    //        lastBestTarget = null;
    //    }

    //    //------------------------------------------------------
    //    // ④ Unknown & Start最遠 の探索
    //    //------------------------------------------------------
    //    var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
    //    var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

    //    MapNode unknownTarget = null;
    //    if (unknownNodes.Count > 0)
    //    {
    //        unknownTarget = SelectUnknownNode(unknownNodes, currentNode);
    //        Debug.Log($"[UN-CUR] Selected Unknown = {unknownTarget.name}");
    //    }

    //    MapNode localFarthestFromStart =
    //        nearNodes.OrderByDescending(n => n.distanceFromStart).FirstOrDefault();

    //    //------------------------------------------------------
    //    // ⑤ ターゲット決定
    //    //------------------------------------------------------
    //    MapNode bestTarget = null;

    //    if (unknownTarget != null && localFarthestFromStart != null)
    //    {
    //        float dU = Distance(currentNode, unknownTarget);
    //        float dS = Distance(currentNode, localFarthestFromStart);

    //        bestTarget = (dU > dS) ? unknownTarget : localFarthestFromStart;
    //    }
    //    else
    //    {
    //        bestTarget = unknownTarget ?? localFarthestFromStart;
    //    }

    //    if (bestTarget == null)
    //    {
    //        Debug.LogWarning("[BEST] bestTarget NULL → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    //------------------------------------------------------
    //    // ⑥ 経路を構築して次ノードへ進む
    //    //------------------------------------------------------
    //    var path2 = BuildShortestPath(currentNode, bestTarget);

    //    if (path2 == null || path2.Count < 2)
    //    {
    //        Debug.LogWarning($"[PATH] Cannot reach bestTarget={bestTarget.name} → fallback");
    //        moveDir = ChooseRandomValidDirection(currentNode).Value;
    //        MoveForward();
    //        return;
    //    }

    //    MapNode nextNode2 = path2[1];
    //    Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
    //    nextDir.y = 0;

    //    moveDir = nextDir;

    //    //------------------------------------------------------
    //    // ⑦ bestTarget のセット
    //    //------------------------------------------------------
    //    lastBestTarget = bestTarget;

    //    //------------------------------------------------------
    //    // ⑧ 移動
    //    //------------------------------------------------------
    //    MoveForward();
    //}
    private void TryExploreMove()
    {
        Debug.Log(
            $"[CHECK-START TEMPOS] TryExploreMove() CALLED | " +
            $"pos={transform.position} | " +
            $"snap={SnapToGrid(transform.position)} | " +
            $"isMoving={isMoving} | " +
            $"block={blockTryExploreThisFrame}"
        );

        // ★ 到達直後のフレームでは実行しない（MoveToTarget との連携）
        if (blockTryExploreThisFrame)
        {
            blockTryExploreThisFrame = false;
            return;
        }

        Debug.Log($"[TryExploreMove] Start currentNode={currentNode?.name}, pos={transform.position}");
        Debug.Log($"[TryExploreMove] Start lastBestTarget={lastBestTarget?.name}");

        //------------------------------------------------------
        // ① Node生成・更新（Nodeスナップ後に呼ばれる前提）
        //------------------------------------------------------
        MapNode oldNode = currentNode;

        //Debug.Log(
        //    $"[CHECK-NODE] oldNode={oldNode?.name} ({oldNode?.transform.position}) | " +
        //    $"currentNode={currentNode?.name} ({currentNode?.transform.position}) | " +
        //    $"nodeJustArrived={(oldNode != currentNode)}"
        //);
        Debug.Log(
            $"[CHECK-NODE] oldNode={(oldNode ? oldNode.name : "null")}, " +
            $"oldPos={(oldNode ? oldNode.transform.position.ToString() : "null")}, " +
            $"currentNode={(currentNode ? currentNode.name : "null")}, " +
            $"currentPos={(currentNode ? currentNode.transform.position.ToString() : "null")}, " +
            $"nodeJustArrived={(oldNode != currentNode)}"
        );



        currentNode = TryPlaceNode(transform.position);
        currentNode.RecalculateUnknownAndWall();
        RegisterCurrentNode(currentNode);

        //------------------------------------------------------
        // ② Node到達判定（EveryNode 用）
        //------------------------------------------------------
        bool nodeJustArrived = (oldNode != currentNode);

        //if (targetUpdateMode == TargetUpdateMode.EveryNode && nodeJustArrived)
        //{
        //    lastBestTarget = null;
        //    Debug.Log("[EVERY NODE] Node 到達 → bestTarget クリア");
        //}

        //------------------------------------------------------
        // ③ FOLLOW（OnArrival 専用）
        //------------------------------------------------------
        bool followMode =
            (targetUpdateMode == TargetUpdateMode.OnArrival) &&
            (lastBestTarget != null && currentNode != lastBestTarget);

        if (followMode)
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
                Debug.LogWarning($"[FOLLOW] path lost → fallback");
                moveDir = ChooseRandomValidDirection(currentNode).Value;
                MoveForward();
                return;
            }
        }

        //------------------------------------------------------
        // ④ bestTarget に到達したか（OnArrival 用）
        //------------------------------------------------------
        if (currentNode == lastBestTarget)
        //if (targetUpdateMode == TargetUpdateMode.OnArrival && currentNode == lastBestTarget)
        {
            Debug.Log($"[REACHED] Target reached={lastBestTarget.name}");

            Vector3? udir = currentNode.GetUnknownDirection();
            if (udir.HasValue)
            {
                moveDir = udir.Value.normalized;
                MoveForward();
                return;
            }

            lastBestTarget = null;
        }

        //------------------------------------------------------
        // ⑤ Unknown & Start 最遠 ノード探索
        //------------------------------------------------------
        var nearNodes = BFS_NearNodes(currentNode, unknownReferenceDepth);
        var unknownNodes = nearNodes.Where(n => n.unknownCount > 0).ToList();

        MapNode unknownTarget = null;
        if (unknownNodes.Count > 0)
        {
            unknownTarget = SelectUnknownNode(unknownNodes, currentNode);
            Debug.Log($"[UN-CUR] UnknownTarget = {unknownTarget.name}");
        }

        MapNode farFromStart = nearNodes
            .OrderByDescending(n => n.distanceFromStart)
            .FirstOrDefault();

        //------------------------------------------------------
        // ⑥ bestTarget の決定
        //------------------------------------------------------
        MapNode bestTarget = null;

        if (unknownTarget != null && farFromStart != null)
        {
            float dU = Distance(currentNode, unknownTarget);
            float dS = Distance(currentNode, farFromStart);

            bestTarget = (dU > dS) ? unknownTarget : farFromStart;
        }
        else
        {
            bestTarget = unknownTarget ?? farFromStart;
        }

        if (bestTarget == null)
        {
            Debug.LogWarning("[BEST] no bestTarget → fallback random");
            moveDir = ChooseRandomValidDirection(currentNode).Value;
            MoveForward();
            return;
        }

        //------------------------------------------------------
        // ⑦ 最短経路の作成
        //------------------------------------------------------
        var path2 = BuildShortestPath(currentNode, bestTarget);
        if (path2 == null || path2.Count < 2)
        {
            Debug.LogWarning("[PATH] unreachable → fallback random");
            moveDir = ChooseRandomValidDirection(currentNode).Value;
            MoveForward();
            return;
        }

        //------------------------------------------------------
        // ⑧ 次ノードへ進む
        //------------------------------------------------------
        MapNode nextNode2 = path2[1];
        Vector3 nextDir = (nextNode2.transform.position - currentNode.transform.position).normalized;
        nextDir.y = 0;

        moveDir = nextDir;

        // 次の探索ターゲット確定
        lastBestTarget = bestTarget;

        //------------------------------------------------------
        // ⑨ 移動実行
        //------------------------------------------------------
        MoveForward();
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