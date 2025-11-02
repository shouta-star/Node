using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Player : MonoBehaviour
{
    [Header("モード設定")]
    [Range(0f, 1f)] public float nodeModeChance = 0.5f; // Node学習モードを採用する確率
    private bool useNodeMode = false; // 現在のモード（false=Ray探索, true=Node学習）

    [Header("移動設定")]
    public float moveSpeed = 3f;
    public float cellSize = 1f;
    public float rayDistance = 1f;
    public float waitTime = 0.3f;
    public LayerMask wallLayer;
    public LayerMask nodeLayer;

    [Header("学習設定")]
    public float explorationRate = 0.2f;
    public float newRouteChance = 0.1f;
    public float baseReward = 1f;
    public float decayRate = 0.9f;
    public float updateRate = 0.5f;

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward;

    [Header("Node設定")]
    public GameObject nodePrefab;
    public Vector3 gridOrigin = Vector3.zero;

    [Header("Debug")]
    public bool debugLog = true;

    // 内部状態
    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;

    // Node関連
    private MapNode currentNode;
    private MapNode previousNode;
    public MapNode goalNode;

    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position;

        // 起動時、現在位置をスナップしてNodeを生成（重複防止）
        Vector3 snapped = SnapToGrid(transform.position);
        transform.position = snapped;

        Vector2Int cell = WorldToCell(snapped);
        if (!MapNode.allNodeCells.Contains(cell))
        {
            MapNode.allNodeCells.Add(cell);
            GameObject nodeObj = Instantiate(nodePrefab, snapped, Quaternion.identity);
            currentNode = nodeObj.GetComponent<MapNode>();
        }
        else
        {
            currentNode = FindNearestNode(snapped);
        }

        LinkWithExistingNodes(currentNode);
        currentNode.FindNeighbors();

        // 最初のモード決定
        useNodeMode = Random.value < nodeModeChance;

        if (debugLog)
            Debug.Log($"[Player:{name}] Start -> Mode={(useNodeMode ? "Node" : "Ray")}");
    }

    void Update()
    {
        if (!isMoving && currentNode != null)
            StartCoroutine(NextAction());
    }

    // ===========================================================
    // 次の行動（現在モードに応じて処理）
    // ===========================================================
    IEnumerator NextAction()
    {
        if (debugLog)
        {
            var cellHere = WorldToCell(transform.position);
            Debug.Log($"[Player:{name}] NextAction Mode={(useNodeMode ? "Node" : "Ray")} @ {cellHere}");
        }

        if (useNodeMode)
            yield return StartCoroutine(MoveByNode());
        else
            TryMoveByRay();
    }

    // ===========================================================
    // RAY 探索移動（誤差完全除去版）
    // ===========================================================
    void TryMoveByRay()
    {
        if (isMoving) return;
        isMoving = true;

        transform.position = SnapToGrid(transform.position);
        moveDir = new Vector3(Mathf.Round(moveDir.x), 0f, Mathf.Round(moveDir.z)).normalized;

        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

        int openCount = 0;
        if (!frontHit) openCount++;
        if (!leftHit) openCount++;
        if (!rightHit) openCount++;

        if (debugLog)
            Debug.Log($"[Player:{name}] RayCheck: front={frontHit}, left={leftHit}, right={rightHit}, open={openCount}, dir={moveDir}");

        if (frontHit || openCount >= 2)
            TryPlaceNode(transform.position);

        if (!frontHit)
        {
            Vector3 snappedPos = SnapToGrid(transform.position);
            targetPos = SnapToGrid(snappedPos + moveDir * cellSize);
            if (debugLog) Debug.Log($"[Player:{name}] RayMove: forward -> {targetPos}");
            StartCoroutine(MoveTo(targetPos));
        }
        else
        {
            var open = new List<Vector3>();
            if (!leftHit) open.Add(leftDir);
            if (!rightHit) open.Add(rightDir);

            if (open.Count > 0)
            {
                moveDir = open[Random.Range(0, open.Count)];
                moveDir = new Vector3(Mathf.Round(moveDir.x), 0f, Mathf.Round(moveDir.z)).normalized;

                Vector3 snappedPos = SnapToGrid(transform.position);
                targetPos = SnapToGrid(snappedPos + moveDir * cellSize);

                if (debugLog) Debug.Log($"[Player:{name}] RayMove: turn -> {moveDir}, target={targetPos}");
                StartCoroutine(MoveTo(targetPos));
            }
            else
            {
                if (debugLog) Debug.Log($"[Player:{name}] RayMove: blocked");
                isMoving = false;
            }
        }
    }

    // ===========================================================
    // NODE 学習型移動
    // ===========================================================
    IEnumerator MoveByNode()
    {
        if (isMoving) yield break;
        isMoving = true;

        bool createNewRoute = Random.value < newRouteChance;

        if (createNewRoute)
        {
            if (debugLog) Debug.Log($"[Player:{name}] NodeMode: try create new route");
            TryCreateNewRoute();
        }
        else if (currentNode.links.Count > 0)
        {
            var candidates = currentNode.links.Where(n => n != previousNode).ToList();
            if (candidates.Count == 0)
                candidates = currentNode.links.ToList();

            MapNode nextNode = candidates.OrderByDescending(n => n.value).First();
            bool explore = false;
            if (Random.value < explorationRate)
            {
                nextNode = candidates[Random.Range(0, candidates.Count)];
                explore = true;
            }

            if (debugLog)
            {
                var here = WorldToCell(currentNode.transform.position);
                var next = WorldToCell(nextNode.transform.position);
                Debug.Log($"[Player:{name}] NodeMove: {(explore ? "explore" : "greedy")} from {here} -> {next}");
            }

            yield return StartCoroutine(MoveTo(nextNode.transform.position));

            previousNode = currentNode;
            currentNode = nextNode;

            UpdateNodeValue(currentNode);
        }

        yield return new WaitForSeconds(waitTime);
        isMoving = false;
    }

    // ===========================================================
    // 新ルート開拓（Node生成時にスナップ追加）
    // ===========================================================
    void TryCreateNewRoute()
    {
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in directions.OrderBy(_ => Random.value))
        {
            Vector3 origin = currentNode.transform.position + Vector3.up * 0.05f;
            float distance = cellSize;

            if (!Physics.Raycast(origin, dir, out RaycastHit hit, distance, wallLayer | nodeLayer))
            {
                Vector3 newPos = currentNode.transform.position + dir * cellSize;
                newPos = SnapToGrid(newPos);

                Vector2Int cell = WorldToCell(newPos);
                if (MapNode.allNodeCells.Contains(cell))
                    continue;

                MapNode.allNodeCells.Add(cell);

                GameObject newNodeObj = Instantiate(nodePrefab, newPos, Quaternion.identity);
                MapNode newNode = newNodeObj.GetComponent<MapNode>();

                currentNode.links.Add(newNode);
                newNode.links.Add(currentNode);

                if (debugLog)
                    Debug.Log($"[Player:{name}] NodeMode: created new route dir={dir} -> {cell}");

                // ✅ isMovingがtrueでもMoveToを許可する
                StartCoroutine(MoveTo(newNode.transform.position));

                previousNode = currentNode;
                currentNode = newNode;

                UpdateNodeValue(currentNode);
                return;
            }
        }

        if (debugLog)
            Debug.Log($"[Player:{name}] NodeMode: could not create new route (blocked or duplicate)");
    }

    // ===========================================================
    // 共通：移動処理＋モード切替（Node到達時）
    // ===========================================================
    IEnumerator MoveTo(Vector3 target)
    {
        // ❌ if (isMoving) yield break; ← 削除済み！
        isMoving = true;

        while (Vector3.Distance(transform.position, target) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }

        MapNode nearest = FindNearestNode(transform.position);
        if (nearest != null)
        {
            transform.position = nearest.transform.position;
            currentNode = nearest;
        }
        else
        {
            Vector2Int cell = WorldToCell(target);
            transform.position = CellToWorld(cell);
        }

        useNodeMode = Random.value < nodeModeChance;

        if (debugLog)
        {
            var cellHere = WorldToCell(transform.position);
            Debug.Log($"[Player:{name}] Reached Node @ {cellHere} -> NextMode={(useNodeMode ? "Node" : "Ray")}");
        }

        isMoving = false;
        yield return null; // ✅ フレーム待機
    }

    // ===========================================================
    // Node設置（共有チェック＋スナップ）
    // ===========================================================
    void TryPlaceNode(Vector3 pos)
    {
        Vector2Int cell = WorldToCell(pos);
        if (MapNode.allNodeCells.Contains(cell))
            return;

        MapNode.allNodeCells.Add(cell);
        Vector3 placePos = CellToWorld(cell);
        Instantiate(nodePrefab, placePos, Quaternion.identity);

        if (debugLog)
            Debug.Log($"[Player:{name}] RayMode: place node @ {cell}");
    }

    // ===========================================================
    // Nodeリンク共有
    // ===========================================================
    void LinkWithExistingNodes(MapNode selfNode)
    {
        MapNode[] allNodes = FindObjectsOfType<MapNode>();
        foreach (var node in allNodes)
        {
            if (node == selfNode) continue;
            if (Vector3.Distance(node.transform.position, selfNode.transform.position) < 0.05f)
            {
                if (!selfNode.links.Contains(node))
                    selfNode.links.Add(node);
                if (!node.links.Contains(selfNode))
                    node.links.Add(selfNode);
            }
        }
    }

    // ===========================================================
    // Node価値更新
    // ===========================================================
    void UpdateNodeValue(MapNode node)
    {
        if (goalNode == null) return;

        float dist = Vector3.Distance(node.transform.position, goalNode.transform.position);
        float reward = baseReward / (dist + 1f);
        float newValue = Mathf.Lerp(node.value, node.value + reward, updateRate);
        node.value = newValue * decayRate;
    }

    // ===========================================================
    // 補助関数群
    // ===========================================================
    MapNode FindNearestNode(Vector3 pos)
    {
        MapNode[] allNodes = FindObjectsOfType<MapNode>();
        return allNodes.OrderBy(n => Vector3.Distance(n.transform.position, pos)).FirstOrDefault();
    }

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

//public class Player : MonoBehaviour
//{
//    [Header("移動設定")]
//    public float moveSpeed;
//    public float cellSize = 1f;
//    public float rayDistance = 1f;
//    public LayerMask wallLayer;

//    [Header("初期設定")]
//    public Vector3 startDirection = Vector3.forward;

//    [Header("Node")]
//    public GameObject nodePrefab;
//    public Vector3 gridOrigin = Vector3.zero; // グリッド原点（必要なら調整）

//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;

//    void Start()
//    {
//        moveDir = startDirection.normalized;
//        targetPos = transform.position;
//    }

//    void Update()
//    {
//        if (!isMoving) TryMove();
//        else MoveToTarget();
//    }

//    void TryMove()
//    {
//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
//        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
//        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

//        Debug.DrawRay(transform.position, moveDir * rayDistance, Color.red);
//        Debug.DrawRay(transform.position, leftDir * rayDistance, Color.blue);
//        Debug.DrawRay(transform.position, rightDir * rayDistance, Color.green);

//        int openCount = 0;
//        if (!frontHit) openCount++;
//        if (!leftHit) openCount++;
//        if (!rightHit) openCount++;

//        // 曲がり角 or 前が壁ならNode設置
//        if (frontHit || openCount >= 2)
//        {
//            TryPlaceNode(transform.position);
//        }

//        // 前が空いていればそのまま進行
//        if (!frontHit)
//        {
//            targetPos = transform.position + moveDir * cellSize;
//            isMoving = true;
//        }
//        else
//        {
//            // 前が壁なら左右方向へランダム転換
//            var open = new List<Vector3>(2);
//            if (!leftHit) open.Add(leftDir);
//            if (!rightHit) open.Add(rightDir);

//            if (open.Count > 0)
//            {
//                moveDir = open[Random.Range(0, open.Count)];
//                targetPos = transform.position + moveDir * cellSize;
//                isMoving = true;
//            }
//        }
//    }

//    void MoveToTarget()
//    {
//        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//        {
//            transform.position = targetPos;
//            isMoving = false;
//        }
//    }

//    // =====================================================
//    // Node設置（共有チェック＋スナップ＋即登録）
//    // =====================================================
//    void TryPlaceNode(Vector3 pos)
//    {
//        // 1 スナップ（浮動小数誤差防止）
//        Vector2Int cell = WorldToCell(pos);
//        Vector3 placePos = CellToWorld(cell);

//        // 2 すでに共有リストに存在するか確認
//        if (MapNode.allNodeCells.Contains(cell))
//            return;

//        // 3 即座に共有リストに登録（他プレイヤーも認識可能）
//        MapNode.allNodeCells.Add(cell);

//        // 4 Nodeを生成
//        Instantiate(nodePrefab, placePos, Quaternion.identity);
//    }

//    // =====================================================
//    // ワールド→セル変換
//    // =====================================================
//    Vector2Int WorldToCell(Vector3 worldPos)
//    {
//        Vector3 p = worldPos - gridOrigin;
//        int cx = Mathf.RoundToInt(p.x / cellSize);
//        int cz = Mathf.RoundToInt(p.z / cellSize);
//        return new Vector2Int(cx, cz);
//    }

//    // =====================================================
//    // セル→ワールド変換
//    // =====================================================
//    Vector3 CellToWorld(Vector2Int cell)
//    {
//        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
//    }
//}
