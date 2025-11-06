using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class Player : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 3f;
    public float cellSize = 1f;
    public float rayDistance = 1f;
    public LayerMask wallLayer;
    public LayerMask nodeLayer;      // ← 追加：Nodeレイヤー

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward;
    public Vector3 gridOrigin = Vector3.zero;
    public MapNode goalNode;
    public GameObject nodePrefab;

    [Header("行動傾向")]
    [Range(0f, 1f)] public float exploreBias = 0.6f;

    [Header("リンク探索")]
    public int linkRayMaxSteps = 100; // ← 追加：当たるまで伸ばす上限

    [Header("デバッグ")]
    public bool debugLog = true;
    public bool debugRay = true;
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Material exploreMaterial;

    // 内部状態
    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private MapNode currentNode;
    private bool reachedGoal = false;

    private Queue<MapNode> recentNodes = new Queue<MapNode>();
    private int recentLimit = 8;

    // ======================================================
    // 起動
    // ======================================================
    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position = SnapToGrid(transform.position);

        ApplyVisual();

        currentNode = TryPlaceNode(transform.position);
        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
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

    // ======================================================
    // 見た目
    // ======================================================
    private void ApplyVisual()
    {
        if (bodyRenderer == null) return;
        bodyRenderer.material = exploreMaterial
            ? exploreMaterial
            : new Material(Shader.Find("Standard")) { color = Color.cyan };
    }

    // ======================================================
    // 通路中は直進だけ行う
    // ======================================================
    void MoveForward()
    {
        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
        targetPos = nextPos;
        isMoving = true;
    }

    // ======================================================
    // Node設置可能かどうか判定（分岐 or 前が壁）
    // ======================================================
    bool CanPlaceNodeHere()
    {
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
    // 探索行動（Node設置後にのみ次方向を決定）
    // ======================================================
    void TryExploreMove()
    {
        currentNode = TryPlaceNode(transform.position); // 設置 or 取得（内部でLinkBackWithRayを実行）
        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

        var dirs = ScanAroundDirections();
        if (dirs.Count == 0)
        {
            if (debugLog) Debug.Log("[Player] No available directions");
            return;
        }

        // 終端Nodeなら未知方向へ
        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
        if (isDeadEnd)
        {
            var unknownDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
            if (unknownDirs.Count > 0)
            {
                var chosen = unknownDirs[Random.Range(0, unknownDirs.Count)];
                moveDir = chosen.dir;
                MoveForward();
                if (debugLog) Debug.Log("[Player] Dead-end → move unexplored");
                return;
            }
        }

        // 通常はBiasで既知/未知を選択
        bool chooseUnexplored = Random.value < exploreBias;

        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
        var knownDirs = dirs.Where(d => d.node != null && d.hasLink).ToList();

        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

        if (chooseUnexplored && unexploredDirs.Count > 0)
            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
        else if (knownDirs.Count > 0)
            chosenDir = knownDirs[Random.Range(0, knownDirs.Count)];
        else if (unexploredDirs.Count > 0)
            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];

        if (chosenDir.HasValue)
        {
            moveDir = chosenDir.Value.dir;
            MoveForward();
            if (debugLog)
                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
        }
    }

    // ======================================================
    // 周囲方向の情報取得（Node有無とリンク状態）
    // ======================================================
    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
    {
        List<(Vector3, MapNode, bool)> found = new();
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in dirs)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
                continue; // 壁なら除外

            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
            Vector2Int nextCell = WorldToCell(nextPos);

            MapNode nextNode = MapNode.FindByCell(nextCell);
            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));

            found.Add((dir, nextNode, linked));
        }

        return found;
    }

    // ======================================================
    // 移動処理
    // ======================================================
    void MoveToTarget()
    {
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
        {
            transform.position = targetPos;
            isMoving = false;

            // 最寄りNodeを現在Nodeに
            MapNode nearest = MapNode.FindNearest(transform.position);
            if (nearest != null)
            {
                currentNode = nearest;

                // 既存Nodeに到達時も背面リンク確認
                LinkBackWithRay(currentNode);
            }

            if (!reachedGoal && goalNode != null && currentNode == goalNode)
            {
                reachedGoal = true;
                Debug.Log($"[Player:{name}] Reached GOAL → start distance learning");
                RecalculateGoalDistance();
            }
        }
    }

    // ======================================================
    // 背面方向へのレイキャスト：壁 or Node に当たったら停止（Nodeならリンク）
    // ======================================================
    private void LinkBackWithRay(MapNode node)
    {
        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        Vector3 backDir = (-moveDir).normalized;
        LayerMask mask = wallLayer | nodeLayer;

        for (int step = 1; step <= linkRayMaxSteps; step++)
        {
            float len = cellSize * step;

            if (debugRay) Debug.DrawRay(origin, backDir * len, Color.yellow, 0.25f);
            if (debugLog) Debug.Log($"[LINK-RAY] step={step} len={len:F2}");

            if (Physics.Raycast(origin, backDir, out RaycastHit hit, len, mask))
            {
                string hitName = hit.collider.name;
                int hitLayer = hit.collider.gameObject.layer;
                string layerName = LayerMask.LayerToName(hitLayer);
                if (debugLog) Debug.Log($"[LINK-HIT] node={node.name} hit={hitName} layer={layerName} dist={hit.distance:F2}");

                // 壁に当たったら終了（リンクしない）
                if ((wallLayer.value & (1 << hitLayer)) != 0)
                {
                    if (debugLog) Debug.Log($"[LINK-BLOCK] Wall hit before Node ({hitName})");
                    return;
                }

                // Nodeに当たったらリンクして終了
                if ((nodeLayer.value & (1 << hitLayer)) != 0)
                {
                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
                    if (hitNode != null && hitNode != node)
                    {
                        node.AddLink(hitNode);
                        if (debugLog) Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name} (dist={hit.distance:F2})");
                    }
                    return;
                }
            }
        }

        if (debugLog) Debug.Log($"[LINK-NONE] node={node.name} no Node found behind (maxSteps={linkRayMaxSteps})");
    }

    // ======================================================
    // Goalから距離を再計算（BFS）
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
    // Node生成
    // ======================================================
    MapNode TryPlaceNode(Vector3 pos)
    {
        Vector2Int cell = WorldToCell(SnapToGrid(pos));
        MapNode node;

        if (MapNode.allNodeCells.Contains(cell))
        {
            node = MapNode.FindByCell(cell);
        }
        else
        {
            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);
            if (debugLog) Debug.Log($"[Player] New Node placed @ {cell}");
        }

        // 新規/既存問わず、設置直後に背面リンク確認
        LinkBackWithRay(node);
        return node;
    }

    // ======================================================
    // 座標変換系
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

//public class Player : MonoBehaviour
//{
//    [Header("移動設定")]
//    public float moveSpeed = 3f;
//    public float cellSize = 1f;
//    public float rayDistance = 1f;
//    public LayerMask wallLayer;

//    [Header("初期設定")]
//    public Vector3 startDirection = Vector3.forward;
//    public Vector3 gridOrigin = Vector3.zero;
//    public MapNode goalNode;
//    public GameObject nodePrefab;

//    [Header("行動傾向")]
//    [Range(0f, 1f)] public float exploreBias = 0.6f; // 未知方向を優先する確率

//    [Header("デバッグ")]
//    public bool debugLog = true;
//    public bool debugRay = true;
//    [SerializeField] private Renderer bodyRenderer;
//    [SerializeField] private Material exploreMaterial;

//    // 内部状態
//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;
//    private MapNode currentNode;
//    private bool reachedGoal = false;

//    // 履歴（直近訪問Node）
//    private Queue<MapNode> recentNodes = new Queue<MapNode>();
//    private int recentLimit = 8;

//    // ======================================================
//    // 起動
//    // ======================================================
//    void Start()
//    {
//        moveDir = startDirection.normalized;
//        targetPos = transform.position = SnapToGrid(transform.position);

//        ApplyVisual();

//        // 初期位置にNodeを設置
//        currentNode = TryPlaceNode(transform.position);
//        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
//    }

//    void Update()
//    {
//        if (!isMoving)
//        {
//            // Nodeを設置できる位置に来たら方向検討、それ以外は直進
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

//    // ======================================================
//    // 見た目
//    // ======================================================
//    private void ApplyVisual()
//    {
//        if (bodyRenderer == null) return;
//        bodyRenderer.material = exploreMaterial
//            ? exploreMaterial
//            : new Material(Shader.Find("Standard")) { color = Color.cyan };
//    }

//    // ======================================================
//    // 通路中は直進だけ行う
//    // ======================================================
//    void MoveForward()
//    {
//        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
//        targetPos = nextPos;
//        isMoving = true;
//    }

//    // ======================================================
//    // Node設置可能かどうか判定（分岐 or 前が壁）
//    // ======================================================
//    bool CanPlaceNodeHere()
//    {
//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
//        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
//        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

//        int openCount = 0;
//        if (!frontHit) openCount++;
//        if (!leftHit) openCount++;
//        if (!rightHit) openCount++;

//        return (frontHit || openCount >= 2);
//    }

//    // ======================================================
//    // 探索行動（Node設置後にのみ次方向を決定）
//    // ======================================================
//    void TryExploreMove()
//    {
//        // Node設置
//        currentNode = TryPlaceNode(transform.position);
//        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

//        var dirs = ScanAroundDirections();
//        if (dirs.Count == 0)
//        {
//            if (debugLog) Debug.Log("[Player] No available directions");
//            return;
//        }

//        // === 終端Nodeなら未知方向へ ===
//        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
//        if (isDeadEnd)
//        {
//            var unknownDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
//            if (unknownDirs.Count > 0)
//            {
//                var chosen = unknownDirs[Random.Range(0, unknownDirs.Count)];
//                moveDir = chosen.dir;
//                MoveForward();
//                if (debugLog) Debug.Log("[Player] Dead-end → move unexplored");
//                return;
//            }
//        }

//        // === 通常はexploreBiasに従って既知／未知を選ぶ ===
//        bool chooseUnexplored = Random.value < exploreBias;

//        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
//        var knownDirs = dirs.Where(d => d.node != null && d.hasLink).ToList();

//        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

//        if (chooseUnexplored && unexploredDirs.Count > 0)
//            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
//        else if (knownDirs.Count > 0)
//            chosenDir = knownDirs[Random.Range(0, knownDirs.Count)];
//        else if (unexploredDirs.Count > 0)
//            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];

//        if (chosenDir.HasValue)
//        {
//            moveDir = chosenDir.Value.dir;
//            MoveForward();
//            if (debugLog)
//                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
//        }
//    }

//    // ======================================================
//    // 周囲方向の情報取得（Nodeの有無とリンク状態を確認）
//    // ======================================================
//    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
//    {
//        List<(Vector3, MapNode, bool)> found = new();
//        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//        foreach (var dir in dirs)
//        {
//            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
//                continue; // 壁は除外

//            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
//            Vector2Int nextCell = WorldToCell(nextPos);

//            MapNode nextNode = MapNode.FindByCell(nextCell);
//            bool linked = false;

//            if (currentNode != null && nextNode != null)
//                linked = currentNode.links.Contains(nextNode);

//            found.Add((dir, nextNode, linked));
//        }

//        return found;
//    }

//    // ======================================================
//    // 移動処理
//    // ======================================================
//    void MoveToTarget()
//    {
//        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//        {
//            transform.position = targetPos;
//            isMoving = false;

//            // 最寄りNodeを現在Nodeとして記録
//            MapNode nearest = MapNode.FindNearest(transform.position);
//            if (nearest != null)
//            {
//                currentNode = nearest;
//                if (!recentNodes.Contains(nearest))
//                {
//                    recentNodes.Enqueue(nearest);
//                    if (recentNodes.Count > recentLimit)
//                        recentNodes.Dequeue();
//                }

//                // ★既存Nodeに到達したときもリンク確認
//                LinkBackWithRay(currentNode);
//            }

//            // ゴール到達判定
//            if (!reachedGoal && goalNode != null && currentNode == goalNode)
//            {
//                reachedGoal = true;
//                Debug.Log($"[Player:{name}] Reached GOAL → start distance learning");
//                RecalculateGoalDistance();
//            }
//        }
//    }

//    // ======================================================
//    // 背面方向へのレイキャストでNodeリンクを確認・作成
//    // ======================================================
//    //private void LinkBackWithRay(MapNode node, int maxStep = 3)
//    //{
//    //    Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//    //    Vector3 backDir = (-moveDir).normalized;

//    //    for (int step = 1; step <= maxStep; step++)
//    //    {
//    //        float len = cellSize * step;

//    //        if (debugRay) Debug.DrawRay(origin, backDir * len, Color.yellow, 0.25f);
//    //        if (debugLog) Debug.Log($"[LINK-RAY] node={node.name} step={step} len={len:F2}");

//    //        if (Physics.Raycast(origin, backDir, out RaycastHit hit, len))
//    //        {
//    //            if (debugLog) Debug.Log($"[LINK-HIT] node={node.name} hit={hit.collider.name} dist={hit.distance:F2}");

//    //            MapNode hitNode = hit.collider.GetComponent<MapNode>();
//    //            if (hitNode != null && hitNode != node)
//    //            {
//    //                node.AddLink(hitNode);
//    //                if (debugLog) Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
//    //                return; // 最初のNodeで確定
//    //            }
//    //        }
//    //    }

//    //    if (debugLog) Debug.Log($"[LINK-NONE] node={node.name} no Node found behind");
//    //}
//    private void LinkBackWithRay(MapNode node, int maxStep = 3)
//    {
//        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//        Vector3 backDir = (-moveDir).normalized;

//        MapNode nearestNode = null;
//        float nearestDist = float.MaxValue;

//        for (int step = 1; step <= maxStep; step++)
//        {
//            float len = cellSize * step;

//            if (debugRay) Debug.DrawRay(origin, backDir * len, Color.yellow, 0.25f);
//            if (debugLog) Debug.Log($"[LINK-RAY] node={node.name} step={step} len={len:F2}");

//            // Nodeだけを拾うRaycast（壁は無視したい場合は nodeLayer に限定する）
//            if (Physics.Raycast(origin, backDir, out RaycastHit hit, len))
//            {
//                var hitNode = hit.collider.GetComponent<MapNode>();
//                if (hitNode != null && hitNode != node)
//                {
//                    float dist = Vector3.Distance(node.transform.position, hitNode.transform.position);

//                    // 最も近いNodeのみ採用
//                    if (dist < nearestDist)
//                    {
//                        nearestDist = dist;
//                        nearestNode = hitNode;
//                    }
//                }
//            }
//        }

//        // 最も近いNodeが見つかったらリンク
//        if (nearestNode != null)
//        {
//            node.AddLink(nearestNode);
//            if (debugLog)
//                Debug.Log($"[LINK-OK] {node.name} ↔ {nearestNode.name} (nearestDist={nearestDist:F2})");
//        }
//        else
//        {
//            if (debugLog) Debug.Log($"[LINK-NONE] node={node.name} no Node found behind");
//        }
//    }


//    // ======================================================
//    // Goalから距離を再計算（BFS）
//    // ======================================================
//    void RecalculateGoalDistance()
//    {
//        if (goalNode == null) return;

//        Queue<MapNode> queue = new Queue<MapNode>();
//        foreach (var n in FindObjectsOfType<MapNode>())
//            n.DistanceFromGoal = int.MaxValue;

//        goalNode.DistanceFromGoal = 0;
//        queue.Enqueue(goalNode);

//        while (queue.Count > 0)
//        {
//            var node = queue.Dequeue();
//            foreach (var link in node.links)
//            {
//                int newDist = node.DistanceFromGoal + 1;
//                if (newDist < link.DistanceFromGoal)
//                {
//                    link.DistanceFromGoal = newDist;
//                    queue.Enqueue(link);
//                }
//            }
//        }

//        Debug.Log("[Player] Distance learning complete (Goal-based BFS)");
//    }

//    // ======================================================
//    // Node生成・補助関数
//    // ======================================================
//    MapNode TryPlaceNode(Vector3 pos)
//    {
//        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//        MapNode node = null;

//        if (MapNode.allNodeCells.Contains(cell))
//        {
//            node = MapNode.FindByCell(cell);
//        }
//        else
//        {
//            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//            node = obj.GetComponent<MapNode>();
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//            if (debugLog) Debug.Log($"[Player] New Node placed @ {cell}");
//        }

//        // 新規・既存どちらでもリンクチェックを行う
//        LinkBackWithRay(node);
//        return node;
//    }

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

////public class Player : MonoBehaviour
////{
////    [Header("移動設定")]
////    public float moveSpeed = 3f;
////    public float cellSize = 1f;
////    public float rayDistance = 1f;
////    public LayerMask wallLayer;

////    [Header("初期設定")]
////    public Vector3 startDirection = Vector3.forward;
////    public Vector3 gridOrigin = Vector3.zero;
////    public MapNode goalNode;
////    public GameObject nodePrefab;

////    [Header("行動傾向")]
////    [Range(0f, 1f)] public float exploreBias = 0.6f;
////    // 1に近いほど未知方向を優先（探索寄り）

////    [Header("デバッグ")]
////    public bool debugLog = true;
////    [SerializeField] private Renderer bodyRenderer;
////    [SerializeField] private Material exploreMaterial;

////    // 内部状態
////    private Vector3 moveDir;
////    private bool isMoving = false;
////    private Vector3 targetPos;
////    private MapNode currentNode;
////    private bool reachedGoal = false;

////    // 履歴（直近訪問Node）
////    private Queue<MapNode> recentNodes = new Queue<MapNode>();
////    private int recentLimit = 8;

////    // ======================================================
////    // 起動
////    // ======================================================
////    void Start()
////    {
////        moveDir = startDirection.normalized;
////        targetPos = transform.position = SnapToGrid(transform.position);

////        ApplyVisual();

////        // 初期位置にNodeを設置
////        currentNode = TryPlaceNode(transform.position);
////        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
////    }

////    void Update()
////    {
////        if (!isMoving)
////        {
////            // Nodeを設置できる位置に来たら方向検討、それ以外は直進
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
////    // 見た目
////    // ======================================================
////    private void ApplyVisual()
////    {
////        if (bodyRenderer == null) return;
////        bodyRenderer.material = exploreMaterial
////            ? exploreMaterial
////            : new Material(Shader.Find("Standard")) { color = Color.cyan };
////    }

////    // ======================================================
////    // 通路中は直進だけ行う
////    // ======================================================
////    void MoveForward()
////    {
////        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
////        targetPos = nextPos;
////        isMoving = true;
////    }

////    // ======================================================
////    // Node設置可能かどうか判定（分岐 or 前が壁）
////    // ======================================================
////    bool CanPlaceNodeHere()
////    {
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
////    // 探索行動（Node設置後にのみ次方向を決定）
////    // ======================================================
////    void TryExploreMove()
////    {
////        // Node設置
////        TryPlaceNode(transform.position);
////        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

////        var dirs = ScanAroundDirections();
////        if (dirs.Count == 0)
////        {
////            if (debugLog) Debug.Log("[Player] No available directions");
////            return;
////        }

////        // === 終端Nodeなら未知方向へ ===
////        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
////        if (isDeadEnd)
////        {
////            var unknownDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
////            if (unknownDirs.Count > 0)
////            {
////                var chosen = unknownDirs[Random.Range(0, unknownDirs.Count)];
////                moveDir = chosen.dir;
////                MoveForward();
////                if (debugLog) Debug.Log("[Player] Dead-end → move unexplored");
////                return;
////            }
////        }

////        // === 通常はexploreBiasに従って既知／未知を選ぶ ===
////        bool chooseUnexplored = Random.value < exploreBias;

////        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
////        var knownDirs = dirs.Where(d => d.node != null && d.hasLink).ToList();

////        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

////        if (chooseUnexplored && unexploredDirs.Count > 0)
////            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////        else if (knownDirs.Count > 0)
////            chosenDir = knownDirs[Random.Range(0, knownDirs.Count)];
////        else if (unexploredDirs.Count > 0)
////            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];

////        if (chosenDir.HasValue)
////        {
////            moveDir = chosenDir.Value.dir;
////            MoveForward();
////            if (debugLog)
////                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
////        }
////    }

////    // ======================================================
////    // 周囲方向の情報取得（Nodeの有無とリンク状態を確認）
////    // ======================================================
////    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
////    {
////        List<(Vector3, MapNode, bool)> found = new();
////        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

////        foreach (var dir in dirs)
////        {
////            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
////                continue; // 壁は除外

////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////            Vector2Int nextCell = WorldToCell(nextPos);

////            MapNode nextNode = MapNode.FindByCell(nextCell);
////            bool linked = false;

////            if (currentNode != null && nextNode != null)
////                linked = currentNode.links.Contains(nextNode);

////            found.Add((dir, nextNode, linked));
////        }

////        return found;
////    }

////    // ======================================================
////    // 移動処理
////    // ======================================================
////    void MoveToTarget()
////    {
////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////        {
////            transform.position = targetPos;
////            isMoving = false;

////            // 最寄りNodeを現在Nodeとして記録
////            MapNode nearest = MapNode.FindNearest(transform.position);
////            if (nearest != null)
////            {
////                currentNode = nearest;
////                if (!recentNodes.Contains(nearest))
////                {
////                    recentNodes.Enqueue(nearest);
////                    if (recentNodes.Count > recentLimit)
////                        recentNodes.Dequeue();
////                }
////            }

////            // ゴール到達判定
////            if (!reachedGoal && goalNode != null && currentNode == goalNode)
////            {
////                reachedGoal = true;
////                Debug.Log($"[Player:{name}] Reached GOAL → start distance learning");
////                RecalculateGoalDistance();
////            }
////        }
////    }

////    // ======================================================
////    // Goalから距離を再計算（BFS）
////    // ======================================================
////    void RecalculateGoalDistance()
////    {
////        if (goalNode == null) return;

////        Queue<MapNode> queue = new Queue<MapNode>();
////        foreach (var n in FindObjectsOfType<MapNode>())
////            n.DistanceFromGoal = int.MaxValue;

////        goalNode.DistanceFromGoal = 0;
////        queue.Enqueue(goalNode);

////        while (queue.Count > 0)
////        {
////            var node = queue.Dequeue();
////            foreach (var link in node.links)
////            {
////                int newDist = node.DistanceFromGoal + 1;
////                if (newDist < link.DistanceFromGoal)
////                {
////                    link.DistanceFromGoal = newDist;
////                    queue.Enqueue(link);
////                }
////            }
////        }

////        Debug.Log("[Player] Distance learning complete (Goal-based BFS)");
////    }

////    // ======================================================
////    // Node生成・補助関数
////    // ======================================================
////    //MapNode TryPlaceNode(Vector3 pos)
////    //{
////    //    Vector2Int cell = WorldToCell(SnapToGrid(pos));
////    //    if (MapNode.allNodeCells.Contains(cell))
////    //        return MapNode.FindByCell(cell);

////    //    GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////    //    MapNode node = obj.GetComponent<MapNode>();
////    //    MapNode.allNodeCells.Add(cell);
////    //    node.cell = cell;
////    //    return node;
////    //}
////    //MapNode TryPlaceNode(Vector3 pos)
////    //{
////    //    Vector2Int cell = WorldToCell(SnapToGrid(pos));
////    //    if (MapNode.allNodeCells.Contains(cell))
////    //        return MapNode.FindByCell(cell);

////    //    // === 新しいNode生成 ===
////    //    GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////    //    MapNode newNode = obj.GetComponent<MapNode>();
////    //    newNode.cell = cell;
////    //    MapNode.allNodeCells.Add(cell);

////    //    // === レイキャストで接続処理 ===
////    //    Vector3 origin = newNode.transform.position + Vector3.up * 0.1f;
////    //    Vector3 backDir = -moveDir; // 来た方向
////    //    int maxStep = 3;
////    //    bool linked = false;

////    //    for (int step = 1; step <= maxStep; step++)
////    //    {
////    //        float rayLen = cellSize * step;

////    //        if (Physics.Raycast(origin, backDir, out RaycastHit hit, rayLen))
////    //        {
////    //            MapNode hitNode = hit.collider.GetComponent<MapNode>();
////    //            if (hitNode != null && hitNode != newNode)
////    //            {
////    //                newNode.AddLink(hitNode);
////    //                if (debugLog)
////    //                    Debug.Log($"[Player] Linked {newNode.name} ↔ {hitNode.name} (distance={rayLen:F1})");
////    //                linked = true;
////    //                break;
////    //            }
////    //        }
////    //    }

////    //    if (!linked && debugLog)
////    //        Debug.Log($"[Player] No link found from {newNode.name}");

////    //    return newNode;
////    //}
////    MapNode TryPlaceNode(Vector3 pos)
////    {
////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////        MapNode node = null;
////        bool isNew = false;

////        // Nodeが存在するか確認
////        if (MapNode.allNodeCells.Contains(cell))
////        {
////            node = MapNode.FindByCell(cell);
////        }
////        else
////        {
////            // === 新しいNodeを生成 ===
////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////            node = obj.GetComponent<MapNode>();
////            node.cell = cell;
////            MapNode.allNodeCells.Add(cell);
////            isNew = true;

////            if (debugLog)
////                Debug.Log($"[Player] New Node placed @ {cell}");
////        }

////        // === 新規 or 既存に関係なくレイキャスト接続を実行 ===
////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
////        Vector3 backDir = -moveDir;
////        int maxStep = 3;
////        bool linked = false;

////        for (int step = 1; step <= maxStep; step++)
////        {
////            float rayLen = cellSize * step;

////            if (Physics.Raycast(origin, backDir, out RaycastHit hit, rayLen))
////            {
////                MapNode hitNode = hit.collider.GetComponent<MapNode>();
////                if (hitNode != null && hitNode != node)
////                {
////                    node.AddLink(hitNode);
////                    if (debugLog)
////                        Debug.Log($"[Player] Linked {node.name} ↔ {hitNode.name} (via Ray, step={step})");
////                    linked = true;
////                    break;
////                }
////            }
////        }

////        if (!linked && debugLog)
////            Debug.Log($"[Player] No Node found behind {node.name}");

////        return node;
////    }


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

//////public class Player : MonoBehaviour
//////{
//////    public enum Mode { Discover, Optimize } // 探索・最適化

//////    [Header("移動設定")]
//////    public float moveSpeed = 3f;
//////    public float cellSize = 1f;
//////    public float rayDistance = 1f;
//////    public LayerMask wallLayer;

//////    [Header("初期設定")]
//////    public Vector3 startDirection = Vector3.forward;
//////    public Vector3 gridOrigin = Vector3.zero;
//////    public MapNode goalNode;
//////    public GameObject nodePrefab;

//////    [Header("モード制御")]
//////    [Range(0f, 1f)] public float discoverChance = 0.6f; // 生成時の初期モード確率

//////    [Header("デバッグ")]
//////    public bool debugLog = true;
//////    [SerializeField] private Renderer bodyRenderer;
//////    [SerializeField] private Material discoverMaterial;
//////    [SerializeField] private Material optimizeMaterial;

//////    // 内部状態
//////    private Vector3 moveDir;
//////    private bool isMoving = false;
//////    private Vector3 targetPos;
//////    private Mode currentMode;
//////    private MapNode currentNode;
//////    private bool reachedGoal = false;

//////    // 履歴（直近訪問Node）
//////    private Queue<MapNode> recentNodes = new Queue<MapNode>();
//////    private int recentLimit = 8;

//////    // ======================================================
//////    // 起動
//////    // ======================================================
//////    void Start()
//////    {
//////        moveDir = startDirection.normalized;
//////        targetPos = transform.position = SnapToGrid(transform.position);

//////        currentMode = (Random.value < discoverChance) ? Mode.Discover : Mode.Optimize;
//////        ApplyModeVisual();

//////        // 初期位置にNodeを設置（起点として）
//////        currentNode = TryPlaceNode(transform.position);
//////        if (debugLog) Debug.Log($"[Player:{name}] Start in {currentMode} mode @ {currentNode}");
//////    }

//////    void Update()
//////    {
//////        if (!isMoving)
//////        {
//////            if (currentMode == Mode.Discover) TryDiscoverMove();
//////            else TryOptimizeMove();
//////        }
//////        else MoveToTarget();
//////    }

//////    // ======================================================
//////    // モードごとのマテリアル
//////    // ======================================================
//////    private void ApplyModeVisual()
//////    {
//////        if (bodyRenderer == null) return;

//////        if (currentMode == Mode.Discover)
//////            bodyRenderer.material = discoverMaterial ? discoverMaterial : new Material(Shader.Find("Standard")) { color = Color.red };
//////        else
//////            bodyRenderer.material = optimizeMaterial ? optimizeMaterial : new Material(Shader.Find("Standard")) { color = Color.blue };
//////    }

//////    // ======================================================
//////    // DISCOVERモード（未知方向を優先）
//////    // ======================================================
//////    void TryDiscoverMove()
//////    {
//////        PlaceNodeHereIfJunctionOrWall(); // ★ 追加：分岐または壁手前でNode設置

//////        if (currentNode == null) return;

//////        var candidates = ScanAroundNodes();

//////        if (candidates.Count == 0)
//////        {
//////            currentMode = Mode.Optimize;
//////            ApplyModeVisual();
//////            if (debugLog) Debug.Log("[Player] No new paths → switch to OPTIMIZE");
//////            return;
//////        }

//////        float avgUnknown = (recentNodes.Count > 0)
//////            ? (float)recentNodes.Average(n => n.UnknownCount)
//////            : 2f;

//////        var higher = candidates.Where(c => c.node.UnknownCount > avgUnknown).ToList();

//////        MapNode next = (higher.Count > 0)
//////            ? higher[Random.Range(0, higher.Count)].node
//////            : candidates[Random.Range(0, candidates.Count)].node;

//////        MoveToNode(next);
//////    }
//////    //// ======================================================
//////    //// DISCOVERモード（未知方向を優先）
//////    //// ======================================================
//////    //void TryDiscoverMove()
//////    //{
//////    //    PlaceNodeHereIfJunctionOrWall(); // 分岐または壁手前でNode設置

//////    //    if (currentNode == null) return;

//////    //    var candidates = ScanAroundNodes();
//////    //    if (candidates.Count == 0)
//////    //    {
//////    //        // 進める方向がまったく無い → Optimizeモードへ
//////    //        currentMode = Mode.Optimize;
//////    //        ApplyModeVisual();
//////    //        if (debugLog) Debug.Log("[Player] Dead end → switch to OPTIMIZE");
//////    //        return;
//////    //    }

//////    //    // 未探索方向（UnknownCount > 0）のみ抽出
//////    //    var unexplored = candidates.Where(c => c.node.UnknownCount > 0).ToList();

//////    //    MapNode next = null;

//////    //    if (unexplored.Count == 1)
//////    //    {
//////    //        // ★ 未探索方向が1つだけならそれを選ぶ
//////    //        next = unexplored[0].node;
//////    //        if (debugLog) Debug.Log("[Player] Single unexplored direction → move forward");
//////    //    }
//////    //    else if (unexplored.Count > 1)
//////    //    {
//////    //        // ★ 複数あるならランダムで進む
//////    //        next = unexplored[Random.Range(0, unexplored.Count)].node;
//////    //        if (debugLog) Debug.Log("[Player] Multiple unexplored directions → random choose");
//////    //    }
//////    //    else
//////    //    {
//////    //        // ★ 未探索が無ければ既知方向へ（終端Node）
//////    //        next = candidates[Random.Range(0, candidates.Count)].node;
//////    //        if (debugLog) Debug.Log("[Player] No unexplored directions → continue existing path");
//////    //    }

//////    //    MoveToNode(next);
//////    //}

//////    // ======================================================
//////    // OPTIMIZEモード（既知方向を優先）
//////    // ======================================================
//////    void TryOptimizeMove()
//////    {
//////        PlaceNodeHereIfJunctionOrWall(); // ★ 同じく分岐/壁手前でNode設置

//////        if (currentNode == null) return;

//////        var candidates = ScanAroundNodes();

//////        if (candidates.Count == 0)
//////        {
//////            currentMode = Mode.Discover;
//////            ApplyModeVisual();
//////            if (debugLog) Debug.Log("[Player] Dead end → switch to DISCOVER");
//////            return;
//////        }

//////        float avgUnknown = (recentNodes.Count > 0)
//////            ? (float)recentNodes.Average(n => n.UnknownCount)
//////            : 2f;

//////        var lower = candidates.Where(c => c.node.UnknownCount <= avgUnknown).ToList();

//////        MapNode next = (lower.Count > 0)
//////            ? lower[Random.Range(0, lower.Count)].node
//////            : candidates[Random.Range(0, candidates.Count)].node;

//////        MoveToNode(next);
//////    }

//////    // ======================================================
//////    // Node設置条件：分岐点 or 前が壁
//////    // ======================================================
//////    private void PlaceNodeHereIfJunctionOrWall()
//////    {
//////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//////        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
//////        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
//////        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

//////        int openCount = 0;
//////        if (!frontHit) openCount++;
//////        if (!leftHit) openCount++;
//////        if (!rightHit) openCount++;

//////        if (frontHit || openCount >= 2)
//////        {
//////            TryPlaceNode(transform.position);
//////            if (debugLog) Debug.Log("[Player] Node placed (junction or wall ahead)");
//////        }
//////    }

//////    // ======================================================
//////    // 移動候補Node探索（上下左右）
//////    // ======================================================
//////    List<(MapNode node, Vector3 dir)> ScanAroundNodes()
//////    {
//////        List<(MapNode node, Vector3 dir)> found = new List<(MapNode, Vector3)>();

//////        Vector3[] dirs = new Vector3[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//////        foreach (var dir in dirs)
//////        {
//////            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
//////                continue; // 壁がある方向はスキップ

//////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
//////            Vector2Int nextCell = WorldToCell(nextPos);

//////            // ★変更：隣接セルに新しいNodeを自動生成しない
//////            MapNode nextNode = MapNode.FindByCell(nextCell);
//////            if (nextNode != null)
//////                found.Add((nextNode, dir));
//////        }

//////        return found;
//////    }

//////    // ======================================================
//////    // 移動処理
//////    // ======================================================
//////    void MoveToNode(MapNode next)
//////    {
//////        if (next == null || next == currentNode) return;

//////        // 相互リンク確立
//////        currentNode.AddLink(next);

//////        moveDir = (next.transform.position - transform.position).normalized;
//////        targetPos = next.transform.position;
//////        isMoving = true;

//////        if (debugLog)
//////            Debug.Log($"[Player] Move {currentMode} -> {next.name} (Unknown={next.UnknownCount})");
//////    }

//////    void MoveToTarget()
//////    {
//////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//////        {
//////            transform.position = targetPos;
//////            isMoving = false;

//////            MapNode nearest = MapNode.FindNearest(transform.position);
//////            if (nearest != null)
//////            {
//////                currentNode = nearest;
//////                if (!recentNodes.Contains(nearest))
//////                {
//////                    recentNodes.Enqueue(nearest);
//////                    if (recentNodes.Count > recentLimit)
//////                        recentNodes.Dequeue();
//////                }
//////            }

//////            // Goal判定
//////            if (!reachedGoal && goalNode != null && currentNode == goalNode)
//////            {
//////                reachedGoal = true;
//////                Debug.Log($"[Player:{name}] Reached GOAL → start distance learning");
//////                RecalculateGoalDistance();
//////            }
//////        }
//////    }

//////    // ======================================================
//////    // Goalから距離を再計算（BFS）
//////    // ======================================================
//////    void RecalculateGoalDistance()
//////    {
//////        if (goalNode == null) return;

//////        Queue<MapNode> queue = new Queue<MapNode>();
//////        foreach (var n in FindObjectsOfType<MapNode>())
//////            n.DistanceFromGoal = int.MaxValue;

//////        goalNode.DistanceFromGoal = 0;
//////        queue.Enqueue(goalNode);

//////        while (queue.Count > 0)
//////        {
//////            var node = queue.Dequeue();
//////            foreach (var link in node.links)
//////            {
//////                int newDist = node.DistanceFromGoal + 1;
//////                if (newDist < link.DistanceFromGoal)
//////                {
//////                    link.DistanceFromGoal = newDist;
//////                    queue.Enqueue(link);
//////                }
//////            }
//////        }

//////        Debug.Log("[Player] Distance learning complete (Goal-based BFS)");
//////    }

//////    // ======================================================
//////    // Node生成・補助関数
//////    // ======================================================
//////    MapNode TryPlaceNode(Vector3 pos)
//////    {
//////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//////        if (MapNode.allNodeCells.Contains(cell))
//////            return MapNode.FindByCell(cell);

//////        GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//////        MapNode node = obj.GetComponent<MapNode>();
//////        MapNode.allNodeCells.Add(cell);
//////        node.cell = cell;
//////        return node;
//////    }

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

////////using UnityEngine;
////////using System.Collections.Generic;
////////using System.Linq;

////////public class Player : MonoBehaviour
////////{
////////    public enum Mode { Discover, Optimize } // 探索・最適化

////////    [Header("移動設定")]
////////    public float moveSpeed = 3f;
////////    public float cellSize = 1f;
////////    public float rayDistance = 1f;
////////    public LayerMask wallLayer;

////////    [Header("初期設定")]
////////    public Vector3 startDirection = Vector3.forward;
////////    public Vector3 gridOrigin = Vector3.zero;
////////    public MapNode goalNode;
////////    public GameObject nodePrefab;

////////    [Header("モード制御")]
////////    [Range(0f, 1f)] public float discoverChance = 0.6f; // 生成時の初期モード確率

////////    [Header("デバッグ")]
////////    public bool debugLog = true;
////////    [SerializeField] private Renderer bodyRenderer;
////////    [SerializeField] private Material discoverMaterial;
////////    [SerializeField] private Material optimizeMaterial;

////////    // 内部状態
////////    private Vector3 moveDir;
////////    private bool isMoving = false;
////////    private Vector3 targetPos;
////////    private Mode currentMode;
////////    private MapNode currentNode;
////////    private bool reachedGoal = false;

////////    // 履歴（直近訪問Node）
////////    private Queue<MapNode> recentNodes = new Queue<MapNode>();
////////    private int recentLimit = 8;

////////    // ======================================================
////////    // 起動
////////    // ======================================================
////////    void Start()
////////    {
////////        moveDir = startDirection.normalized;
////////        targetPos = transform.position = SnapToGrid(transform.position);

////////        currentMode = (Random.value < discoverChance) ? Mode.Discover : Mode.Optimize;
////////        ApplyModeVisual();

////////        // 初期位置にNodeを設置
////////        currentNode = TryPlaceNode(transform.position);
////////        if (debugLog) Debug.Log($"[Player:{name}] Start in {currentMode} mode @ {currentNode}");
////////    }

////////    void Update()
////////    {
////////        if (!isMoving)
////////        {
////////            if (currentMode == Mode.Discover) TryDiscoverMove();
////////            else TryOptimizeMove();
////////        }
////////        else MoveToTarget();
////////    }

////////    // ======================================================
////////    // モードごとのマテリアル
////////    // ======================================================
////////    private void ApplyModeVisual()
////////    {
////////        if (bodyRenderer == null) return;

////////        if (currentMode == Mode.Discover)
////////            bodyRenderer.material = discoverMaterial ? discoverMaterial : new Material(Shader.Find("Standard")) { color = Color.red };
////////        else
////////            bodyRenderer.material = optimizeMaterial ? optimizeMaterial : new Material(Shader.Find("Standard")) { color = Color.blue };
////////    }

////////    // ======================================================
////////    // DISCOVERモード（未知方向を優先）
////////    // ======================================================
////////    void TryDiscoverMove()
////////    {
////////        if (currentNode == null) return;

////////        var candidates = ScanAroundNodes();

////////        if (candidates.Count == 0)
////////        {
////////            currentMode = Mode.Optimize;
////////            ApplyModeVisual();
////////            if (debugLog) Debug.Log("[Player] No new paths → switch to OPTIMIZE");
////////            return;
////////        }

////////        float avgUnknown = (recentNodes.Count > 0)
////////            ? (float)recentNodes.Average(n => n.UnknownCount)  // ★ 明示キャスト
////////            : 2f;

////////        var higher = candidates.Where(c => c.node.UnknownCount > avgUnknown).ToList();

////////        MapNode next = (higher.Count > 0)
////////            ? higher[Random.Range(0, higher.Count)].node
////////            : candidates[Random.Range(0, candidates.Count)].node;

////////        MoveToNode(next);
////////    }

////////    // ======================================================
////////    // OPTIMIZEモード（既知方向を優先）
////////    // ======================================================
////////    void TryOptimizeMove()
////////    {
////////        if (currentNode == null) return;

////////        var candidates = ScanAroundNodes();

////////        if (candidates.Count == 0)
////////        {
////////            currentMode = Mode.Discover;
////////            ApplyModeVisual();
////////            if (debugLog) Debug.Log("[Player] Dead end → switch to DISCOVER");
////////            return;
////////        }

////////        float avgUnknown = (recentNodes.Count > 0)
////////            ? (float)recentNodes.Average(n => n.UnknownCount)  // ★ 明示キャスト
////////            : 2f;

////////        var lower = candidates.Where(c => c.node.UnknownCount <= avgUnknown).ToList();

////////        MapNode next = (lower.Count > 0)
////////            ? lower[Random.Range(0, lower.Count)].node
////////            : candidates[Random.Range(0, candidates.Count)].node;

////////        MoveToNode(next);
////////    }

////////    // ======================================================
////////    // 移動候補Node探索（上下左右）
////////    // ======================================================
////////    List<(MapNode node, Vector3 dir)> ScanAroundNodes()
////////    {
////////        List<(MapNode node, Vector3 dir)> found = new List<(MapNode, Vector3)>();

////////        Vector3[] dirs = new Vector3[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

////////        foreach (var dir in dirs)
////////        {
////////            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
////////                continue; // 壁がある方向はスキップ

////////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////////            Vector2Int nextCell = WorldToCell(nextPos);

////////            MapNode nextNode = MapNode.FindByCell(nextCell);
////////            if (nextNode == null)
////////                nextNode = TryPlaceNode(nextPos);

////////            if (nextNode != null)
////////                found.Add((nextNode, dir));
////////        }

////////        return found;
////////    }

////////    // ======================================================
////////    // 移動処理
////////    // ======================================================
////////    void MoveToNode(MapNode next)
////////    {
////////        if (next == null || next == currentNode) return;

////////        // 相互リンク確立
////////        currentNode.AddLink(next);

////////        moveDir = (next.transform.position - transform.position).normalized;
////////        targetPos = next.transform.position;
////////        isMoving = true;

////////        if (debugLog)
////////            Debug.Log($"[Player] Move {currentMode} -> {next.name} (Unknown={next.UnknownCount})");
////////    }

////////    void MoveToTarget()
////////    {
////////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////////        {
////////            transform.position = targetPos;
////////            isMoving = false;

////////            MapNode nearest = MapNode.FindNearest(transform.position);
////////            if (nearest != null)
////////            {
////////                currentNode = nearest;
////////                if (!recentNodes.Contains(nearest))
////////                {
////////                    recentNodes.Enqueue(nearest);
////////                    if (recentNodes.Count > recentLimit)
////////                        recentNodes.Dequeue();
////////                }
////////            }

////////            // Goal判定
////////            if (!reachedGoal && goalNode != null && currentNode == goalNode)
////////            {
////////                reachedGoal = true;
////////                Debug.Log($"[Player:{name}] Reached GOAL → start distance learning");
////////                RecalculateGoalDistance();
////////            }
////////        }
////////    }

////////    // ======================================================
////////    // Goalから距離を再計算（BFS）
////////    // ======================================================
////////    void RecalculateGoalDistance()
////////    {
////////        if (goalNode == null) return;

////////        Queue<MapNode> queue = new Queue<MapNode>();
////////        foreach (var n in FindObjectsOfType<MapNode>())
////////            n.DistanceFromGoal = int.MaxValue;

////////        goalNode.DistanceFromGoal = 0;
////////        queue.Enqueue(goalNode);

////////        while (queue.Count > 0)
////////        {
////////            var node = queue.Dequeue();
////////            foreach (var link in node.links)
////////            {
////////                int newDist = node.DistanceFromGoal + 1;
////////                if (newDist < link.DistanceFromGoal)
////////                {
////////                    link.DistanceFromGoal = newDist;
////////                    queue.Enqueue(link);
////////                }
////////            }
////////        }

////////        Debug.Log("[Player] Distance learning complete (Goal-based BFS)");
////////    }

////////    // ======================================================
////////    // Node生成・補助関数
////////    // ======================================================
////////    MapNode TryPlaceNode(Vector3 pos)
////////    {
////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////////        if (MapNode.allNodeCells.Contains(cell))
////////            return MapNode.FindByCell(cell);

////////        GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////////        MapNode node = obj.GetComponent<MapNode>();
////////        MapNode.allNodeCells.Add(cell);
////////        node.cell = cell;
////////        return node;
////////    }

////////    Vector2Int WorldToCell(Vector3 worldPos)
////////    {
////////        Vector3 p = worldPos - gridOrigin;
////////        int cx = Mathf.RoundToInt(p.x / cellSize);
////////        int cz = Mathf.RoundToInt(p.z / cellSize);
////////        return new Vector2Int(cx, cz);
////////    }

////////    Vector3 CellToWorld(Vector2Int cell)
////////    {
////////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
////////    }

////////    Vector3 SnapToGrid(Vector3 worldPos)
////////    {
////////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
////////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
////////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
////////    }
////////}

//////////using UnityEngine;
//////////using System.Collections.Generic;
//////////using System.Linq;

//////////public class Player : MonoBehaviour
//////////{
//////////    public enum Mode { Explore, Learn }

//////////    [Header("移動設定")]
//////////    public float moveSpeed = 3f;
//////////    public float cellSize = 1f;
//////////    public float rayDistance = 1f;
//////////    public LayerMask wallLayer;

//////////    [Header("初期設定")]
//////////    public Vector3 startDirection = Vector3.forward;

//////////    [Header("Node設定")]
//////////    public GameObject nodePrefab;
//////////    public Vector3 gridOrigin = Vector3.zero;
//////////    public MapNode goalNode;
//////////    [Range(0f, 1f)] public float exploreModeChance = 0.5f;

//////////    [Header("Debug")]
//////////    public bool debugLog = true;

//////////    // 内部状態
//////////    private Vector3 moveDir;
//////////    private bool isMoving = false;
//////////    private Vector3 targetPos;
//////////    private Mode currentMode;
//////////    private MapNode currentNode;
//////////    private bool reachedGoal = false;

//////////    [SerializeField] private Renderer bodyRenderer;
//////////    [SerializeField] private Material exploreMaterial;
//////////    [SerializeField] private Material learnMaterial;

//////////    // 直近訪問履歴（位置＋方向）
//////////    private Queue<(Vector2Int cell, Vector3 dir)> recentVisited = new Queue<(Vector2Int, Vector3)>();
//////////    private int recentLimit = 10;

//////////    // ===== Debug helpers =====
//////////    const string LG = "[EXP-DBG]";
//////////    int decideTick = 0;
//////////    string CellStr(Vector2Int c) => $"({c.x},{c.y})";
//////////    string DirStr(Vector3 d)
//////////    {
//////////        var f = moveDir;
//////////        var l = Quaternion.Euler(0, -90, 0) * moveDir;
//////////        var r = Quaternion.Euler(0, 90, 0) * moveDir;
//////////        var b = -moveDir;
//////////        float df = Vector3.Dot(d, f), dl = Vector3.Dot(d, l), dr = Vector3.Dot(d, r), db = Vector3.Dot(d, b);
//////////        float m = Mathf.Max(df, Mathf.Max(dl, Mathf.Max(dr, db)));
//////////        if (m == df) return "F";
//////////        if (m == dl) return "L";
//////////        if (m == dr) return "R";
//////////        return "B";
//////////    }
//////////    string DumpRecent() => string.Join(" -> ", recentVisited.Select(r => $"{CellStr(r.cell)}:{DirStr(r.dir)}"));

//////////    [Header("Ray settings")]
//////////    [SerializeField] private LayerMask nodeLayer;

//////////    void Start()
//////////    {
//////////        moveDir = startDirection.normalized;
//////////        targetPos = transform.position;
//////////        transform.position = SnapToGrid(transform.position);

//////////        currentMode = (Random.value < exploreModeChance) ? Mode.Explore : Mode.Learn;
//////////        if (debugLog) Debug.Log($"[Player:{name}] Spawned in {currentMode} mode");
//////////        ApplyModeVisual();
//////////        currentNode = GetNearestNode();
//////////    }

//////////    void Update()
//////////    {
//////////        if (!isMoving)
//////////        {
//////////            if (currentMode == Mode.Explore) TryExploreMove();
//////////            else TryLearnMove();
//////////        }
//////////        else MoveToTarget();
//////////    }

//////////    private void ApplyModeVisual()
//////////    {
//////////        if (bodyRenderer == null) return;
//////////        if (currentMode == Mode.Explore)
//////////            bodyRenderer.material = exploreMaterial ? exploreMaterial : new Material(Shader.Find("Standard")) { color = Color.red };
//////////        else
//////////            bodyRenderer.material = learnMaterial ? learnMaterial : new Material(Shader.Find("Standard")) { color = Color.blue };
//////////    }

//////////    // =====================================================
//////////    // 探索モード
//////////    // =====================================================
//////////    void TryExploreMove()
//////////    {
//////////        decideTick++;
//////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;
//////////        Vector3 backDir = -moveDir;

//////////        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
//////////        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
//////////        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

//////////        var curCell = WorldToCell(SnapToGrid(transform.position));
//////////        if (debugLog)
//////////            Debug.Log($"{LG} T#{decideTick} pos={CellStr(curCell)} dir={DirStr(moveDir)} hits F:{frontHit} L:{leftHit} R:{rightHit} recent[{recentVisited.Count}] {DumpRecent()}");

//////////        int openCount = 0;
//////////        if (!frontHit) openCount++;
//////////        if (!leftHit) openCount++;
//////////        if (!rightHit) openCount++;

//////////        // 分岐点 or 壁手前のみNode設置
//////////        if (frontHit || openCount >= 2)
//////////        {
//////////            MapNode newNode = TryPlaceNode(transform.position);
//////////            if (newNode && goalNode)
//////////                newNode.InitializeValue(goalNode.transform.position);
//////////        }

//////////        List<Vector3> openDirs = new List<Vector3>();
//////////        if (!frontHit) openDirs.Add(moveDir);
//////////        if (!leftHit) openDirs.Add(leftDir);
//////////        if (!rightHit) openDirs.Add(rightDir);

//////////        openDirs.RemoveAll(d => Vector3.Dot(d, backDir) > 0.9f);
//////////        if (openDirs.Count == 0) openDirs.Add(backDir);

//////////        // === 未探索方向＋履歴回避＋リンク参照 ===
//////////        List<Vector3> unexploredDirs = new List<Vector3>();
//////////        foreach (var dir in openDirs)
//////////        {
//////////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
//////////            Vector2Int nextCell = WorldToCell(nextPos);

//////////            bool inRecentSameDir = recentVisited.Any(r => r.cell == nextCell && Vector3.Dot(r.dir, dir) > 0.9f);
//////////            if (inRecentSameDir) continue;

//////////            bool knownNode = MapNode.allNodeCells.Contains(nextCell);
//////////            bool directionLinked = IsDirectionLinkedFromCurrent(dir);

//////////            if (!knownNode && !directionLinked)
//////////                unexploredDirs.Add(dir);
//////////        }

//////////        // === 打ち切り ===
//////////        if (unexploredDirs.Count == 0 && openDirs.Count == 0)
//////////        {
//////////            if (debugLog) Debug.Log($"{LG} No moves -> switch to LEARN");
//////////            currentMode = Mode.Learn;
//////////            ApplyModeVisual();
//////////            return;
//////////        }

//////////        // === 移動方向決定 ===
//////////        Vector3 chosenDir = unexploredDirs.Count > 0
//////////            ? unexploredDirs[Random.Range(0, unexploredDirs.Count)]
//////////            : openDirs[Random.Range(0, openDirs.Count)];

//////////        moveDir = chosenDir;
//////////        targetPos = SnapToGrid(transform.position + chosenDir * cellSize);
//////////        isMoving = true;

//////////        var departCell = curCell;
//////////        recentVisited.Enqueue((departCell, moveDir));
//////////        if (recentVisited.Count > recentLimit) recentVisited.Dequeue();
//////////    }

//////////    // =====================================================
//////////    // 学習モード
//////////    // =====================================================
//////////    void TryLearnMove()
//////////    {
//////////        currentNode = GetNearestNode();
//////////        if (currentNode == null || goalNode == null)
//////////            return;

//////////        if (currentNode.links == null || currentNode.links.Count == 0)
//////////        {
//////////            currentMode = Mode.Explore;
//////////            ApplyModeVisual();
//////////            return;
//////////        }

//////////        MapNode bestNext = currentNode.links
//////////            .OrderByDescending(n => n.value)
//////////            .FirstOrDefault();

//////////        if (bestNext == null)
//////////        {
//////////            currentMode = Mode.Explore;
//////////            ApplyModeVisual();
//////////            return;
//////////        }

//////////        targetPos = SnapToGrid(bestNext.transform.position);
//////////        moveDir = (targetPos - transform.position).normalized;
//////////        isMoving = true;
//////////    }

//////////    // =====================================================
//////////    // 移動
//////////    // =====================================================
//////////    void MoveToTarget()
//////////    {
//////////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

//////////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//////////        {
//////////            transform.position = targetPos;
//////////            isMoving = false;

//////////            MapNode nearest = GetNearestNode();
//////////            if (nearest)
//////////            {
//////////                nearest.UpdateValue(goalNode);
//////////                currentNode = nearest;

//////////                Vector2Int cellArrived = WorldToCell(nearest.transform.position);
//////////                if (!recentVisited.Any(r => r.cell == cellArrived))
//////////                {
//////////                    recentVisited.Enqueue((cellArrived, moveDir));
//////////                    if (recentVisited.Count > recentLimit) recentVisited.Dequeue();
//////////                }
//////////            }
//////////        }

//////////        if (!reachedGoal && goalNode != null)
//////////        {
//////////            float distToGoal = Vector3.Distance(transform.position, goalNode.transform.position);
//////////            if (distToGoal < 0.1f)
//////////            {
//////////                reachedGoal = true;
//////////                Debug.Log($"[Player:{name}] Reached Goal! Destroy.");
//////////                Destroy(gameObject);
//////////                return;
//////////            }
//////////        }
//////////    }

//////////    // =====================================================
//////////    // 現Nodeのlinksを使って「その方向に既知経路があるか」を確認
//////////    // =====================================================
//////////    bool IsDirectionLinkedFromCurrent(Vector3 dir)
//////////    {
//////////        if (currentNode == null) return false;

//////////        foreach (var neighbor in currentNode.links)
//////////        {
//////////            if (neighbor == null) continue;
//////////            Vector3 toNeighbor = (neighbor.transform.position - currentNode.transform.position).normalized;
//////////            float dot = Vector3.Dot(toNeighbor, dir);
//////////            if (dot > 0.9f) return true; // ほぼ同方向にリンクあり
//////////        }
//////////        return false;
//////////    }

//////////    // =====================================================
//////////    // Node生成
//////////    // =====================================================
//////////    //MapNode TryPlaceNode(Vector3 pos)
//////////    //{
//////////    //    Vector2Int cell = WorldToCell(SnapToGrid(pos));
//////////    //    if (MapNode.allNodeCells.Contains(cell)) return null;
//////////    //    GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//////////    //    MapNode node = obj.GetComponent<MapNode>();
//////////    //    MapNode.allNodeCells.Add(cell);
//////////    //    return node;
//////////    //}
//////////    MapNode TryPlaceNode(Vector3 pos)
//////////    {
//////////        // グリッド位置を計算
//////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));

//////////        // 既に同じ座標にNodeがあれば新規生成しない
//////////        if (MapNode.allNodeCells.Contains(cell))
//////////            return null;

//////////        // 新しいNodeを生成
//////////        GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//////////        MapNode newNode = obj.GetComponent<MapNode>();
//////////        MapNode.allNodeCells.Add(cell);

//////////        // ここから段階的レイキャストによる接続処理
//////////        Vector3 origin = newNode.transform.position + Vector3.up * 0.1f;
//////////        Vector3 backDir = -moveDir; // ←来た方向
//////////        int mask = nodeLayer;

//////////        bool linked = false;
//////////        Color[] stepColors = { Color.red, Color.yellow, Color.green };

//////////        // 1 → 2 → 3マス分の距離でRayを飛ばす
//////////        for (int step = 1; step <= 3; step++)
//////////        {
//////////            float rayLen = cellSize * step;

//////////            // Sceneビューで確認できるように色分けして描画
//////////            Debug.DrawRay(origin, backDir * rayLen, stepColors[step - 1], 2f);

//////////            // Raycast実行
//////////            if (Physics.Raycast(origin, backDir, out RaycastHit hit, rayLen, mask))
//////////            {
//////////                MapNode hitNode = hit.collider.GetComponent<MapNode>();
//////////                if (hitNode != null && hitNode != newNode)
//////////                {
//////////                    // 双方向リンク作成
//////////                    newNode.AddLink(hitNode);
//////////                    linked = true;

//////////                    if (debugLog)
//////////                        Debug.Log($"[Player] Linked new {newNode.name} ↔ {hitNode.name} (distance={rayLen:F2})");

//////////                    break; // ヒットしたら終了
//////////                }
//////////            }
//////////        }

//////////        // ヒットしなかった場合のログ
//////////        if (!linked && debugLog)
//////////            Debug.Log($"[Player] No Node hit from {newNode.name} (max 3 steps)");

//////////        return newNode;
//////////    }

//////////    MapNode GetNearestNode()
//////////    {
//////////        MapNode[] nodes = FindObjectsOfType<MapNode>();
//////////        return nodes.OrderBy(n => Vector3.Distance(transform.position, n.transform.position)).FirstOrDefault();
//////////    }

//////////    // =====================================================
//////////    // グリッド変換
//////////    // =====================================================
//////////    Vector2Int WorldToCell(Vector3 worldPos)
//////////    {
//////////        Vector3 p = worldPos - gridOrigin;
//////////        int cx = Mathf.RoundToInt(p.x / cellSize);
//////////        int cz = Mathf.RoundToInt(p.z / cellSize);
//////////        return new Vector2Int(cx, cz);
//////////    }

//////////    Vector3 CellToWorld(Vector2Int cell)
//////////    {
//////////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
//////////    }

//////////    Vector3 SnapToGrid(Vector3 worldPos)
//////////    {
//////////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
//////////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
//////////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
//////////    }
//////////}

////////////using UnityEngine;
////////////using System.Collections.Generic;
////////////using System.Linq;

////////////public class Player : MonoBehaviour
////////////{
////////////    public enum Mode { Explore, Learn }

////////////    [Header("移動設定")]
////////////    public float moveSpeed = 3f;
////////////    public float cellSize = 1f;
////////////    public float rayDistance = 1f;
////////////    public LayerMask wallLayer;

////////////    [Header("初期設定")]
////////////    public Vector3 startDirection = Vector3.forward;

////////////    [Header("Node設定")]
////////////    public GameObject nodePrefab;
////////////    public Vector3 gridOrigin = Vector3.zero;
////////////    public MapNode goalNode;
////////////    [Range(0f, 1f)] public float exploreModeChance = 0.5f;

////////////    [Header("Debug")]
////////////    public bool debugLog = true;

////////////    // 内部状態
////////////    private Vector3 moveDir;
////////////    private bool isMoving = false;
////////////    private Vector3 targetPos;
////////////    private Mode currentMode;
////////////    private MapNode currentNode;
////////////    private bool reachedGoal = false;

////////////    [SerializeField] private Renderer bodyRenderer;     // キャラのRendererをアサイン
////////////    [SerializeField] private Material exploreMaterial;  // 探索モード用（赤系）
////////////    [SerializeField] private Material learnMaterial;    // 学習モード用（青系）

////////////    // === 直近の通過Node記録 ===
////////////    private Queue<(Vector2Int cell, Vector3 dir)> recentVisited = new Queue<(Vector2Int, Vector3)>();
////////////    private int recentLimit = 10; // 記憶する最大数

////////////    // ===== Debug helpers =====
////////////    const string LG = "[EXP-DBG]";
////////////    const string NG = "[NODE-DBG]";
////////////    int decideTick = 0;

////////////    string CellStr(Vector2Int c) => $"({c.x},{c.y})";
////////////    string DirStr(Vector3 d)
////////////    {
////////////        var f = moveDir;
////////////        var l = Quaternion.Euler(0, -90, 0) * moveDir;
////////////        var r = Quaternion.Euler(0, 90, 0) * moveDir;
////////////        var b = -moveDir;
////////////        float df = Vector3.Dot(d, f), dl = Vector3.Dot(d, l), dr = Vector3.Dot(d, r), db = Vector3.Dot(d, b);
////////////        float m = Mathf.Max(df, Mathf.Max(dl, Mathf.Max(dr, db)));
////////////        if (m == df) return "F";
////////////        if (m == dl) return "L";
////////////        if (m == dr) return "R";
////////////        return "B";
////////////    }
////////////    string DumpRecent()
////////////    {
////////////        return string.Join(" -> ", recentVisited.Select(r => $"{CellStr(r.cell)}:{DirStr(r.dir)}"));
////////////    }

////////////    void Start()
////////////    {
////////////        moveDir = startDirection.normalized;
////////////        targetPos = transform.position;
////////////        transform.position = SnapToGrid(transform.position);

////////////        currentMode = (Random.value < exploreModeChance) ? Mode.Explore : Mode.Learn;
////////////        if (debugLog)
////////////            Debug.Log($"[Player:{name}] Spawned in {currentMode} mode");

////////////        ApplyModeVisual();
////////////        currentNode = GetNearestNode();
////////////    }

////////////    void Update()
////////////    {
////////////        if (!isMoving)
////////////        {
////////////            switch (currentMode)
////////////            {
////////////                case Mode.Explore:
////////////                    TryExploreMove();
////////////                    break;
////////////                case Mode.Learn:
////////////                    TryLearnMove();
////////////                    break;
////////////            }
////////////        }
////////////        else
////////////        {
////////////            MoveToTarget();
////////////        }
////////////    }

////////////    private void ApplyModeVisual()
////////////    {
////////////        if (bodyRenderer == null) return;

////////////        if (currentMode == Mode.Explore)
////////////        {
////////////            if (exploreMaterial != null) bodyRenderer.material = exploreMaterial;
////////////            else bodyRenderer.material.color = Color.red;
////////////        }
////////////        else
////////////        {
////////////            if (learnMaterial != null) bodyRenderer.material = learnMaterial;
////////////            else bodyRenderer.material.color = Color.blue;
////////////        }
////////////    }

////////////    // =====================================================
////////////    // 探索モード（詳細デバッグ＋未探索チェック追加）
////////////    // =====================================================
////////////    void TryExploreMove()
////////////    {
////////////        decideTick++;
////////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;
////////////        Vector3 backDir = -moveDir;

////////////        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
////////////        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
////////////        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

////////////        var curCell = WorldToCell(SnapToGrid(transform.position));
////////////        Debug.Log($"{LG} T#{decideTick} pos={CellStr(curCell)} dir={DirStr(moveDir)} hits F:{frontHit} L:{leftHit} R:{rightHit} recent[{recentVisited.Count}] {DumpRecent()}");

////////////        int openCount = 0;
////////////        if (!frontHit) openCount++;
////////////        if (!leftHit) openCount++;
////////////        if (!rightHit) openCount++;

////////////        // Node設置（前が壁 or 分岐点）
////////////        if (frontHit || openCount >= 2)
////////////        {
////////////            var beforeCount = MapNode.allNodeCells.Count;
////////////            MapNode newNode = TryPlaceNode(transform.position);
////////////            var afterCount = MapNode.allNodeCells.Count;
////////////            if (newNode)
////////////                Debug.Log($"{LG} T#{decideTick} placeNode @ {CellStr(WorldToCell(newNode.transform.position))} (total {afterCount} / was {beforeCount})");
////////////        }

////////////        List<Vector3> openDirs = new List<Vector3>();
////////////        if (!frontHit) openDirs.Add(moveDir);
////////////        if (!leftHit) openDirs.Add(leftDir);
////////////        if (!rightHit) openDirs.Add(rightDir);

////////////        var beforeBackCull = openDirs.Select(d => DirStr(d)).ToArray();
////////////        openDirs.RemoveAll(d => Vector3.Dot(d, backDir) > 0.9f);
////////////        var afterBackCull = openDirs.Select(d => DirStr(d)).ToArray();
////////////        if (beforeBackCull.Length != afterBackCull.Length)
////////////            Debug.Log($"{LG} T#{decideTick} back-cull {string.Join(",", beforeBackCull)} -> {string.Join(",", afterBackCull)}");

////////////        if (openDirs.Count == 0)
////////////        {
////////////            openDirs.Add(backDir);
////////////            Debug.Log($"{LG} T#{decideTick} dead-end -> allow back");
////////////        }

////////////        List<Vector3> unexploredDirs = new List<Vector3>();
////////////        foreach (var dir in openDirs.ToList())
////////////        {
////////////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////////////            Vector2Int ncell = WorldToCell(nextPos);

////////////            bool inRecentSameDir = recentVisited.Any(r => r.cell == ncell && Vector3.Dot(r.dir, dir) > 0.9f);
////////////            bool knownNode = MapNode.allNodeCells.Contains(ncell);

////////////            Debug.Log($"{LG} T#{decideTick} Check next={nextPos:F2} (cell {ncell}) known={knownNode} totalNodes={MapNode.allNodeCells.Count}");

////////////            if (inRecentSameDir)
////////////            {
////////////                Debug.Log($"{LG} T#{decideTick} filter REJECT by recent: next={CellStr(ncell)} dir={DirStr(dir)}");
////////////            }
////////////            else
////////////            {
////////////                if (!knownNode)
////////////                {
////////////                    unexploredDirs.Add(dir);
////////////                    Debug.Log($"{LG} T#{decideTick} candidate UNEXPLORED: next={CellStr(ncell)} dir={DirStr(dir)}");
////////////                }
////////////                else
////////////                {
////////////                    Debug.Log($"{LG} T#{decideTick} candidate KNOWN: next={CellStr(ncell)} dir={DirStr(dir)}");
////////////                }
////////////            }
////////////        }

////////////        if (unexploredDirs.Count == 0 && openDirs.Count == 0)
////////////        {
////////////            Debug.Log($"{LG} T#{decideTick} No moves -> switch to LEARN");
////////////            currentMode = Mode.Learn;
////////////            ApplyModeVisual();
////////////            return;
////////////        }

////////////        Vector3 chosenDir = unexploredDirs.Count > 0
////////////            ? unexploredDirs[Random.Range(0, unexploredDirs.Count)]
////////////            : openDirs[Random.Range(0, openDirs.Count)];

////////////        var nextChosenCell = WorldToCell(SnapToGrid(transform.position + chosenDir * cellSize));
////////////        Debug.Log($"{LG} T#{decideTick} CHOOSE {(unexploredDirs.Count > 0 ? "UNEXPLORED" : "KNOWN")} dir={DirStr(chosenDir)} -> {CellStr(nextChosenCell)}");

////////////        moveDir = chosenDir;
////////////        targetPos = SnapToGrid(transform.position + chosenDir * cellSize);
////////////        isMoving = true;

////////////        var departCell = curCell;
////////////        recentVisited.Enqueue((departCell, moveDir));
////////////        if (recentVisited.Count > recentLimit)
////////////        {
////////////            var popped = recentVisited.Dequeue();
////////////            Debug.Log($"{LG} T#{decideTick} recent pop {CellStr(popped.cell)}:{DirStr(popped.dir)} (limit={recentLimit})");
////////////        }
////////////    }

////////////    // =====================================================
////////////    // 学習モード
////////////    // =====================================================
////////////    void TryLearnMove()
////////////    {
////////////        currentNode = GetNearestNode();
////////////        if (currentNode == null || goalNode == null)
////////////            return;

////////////        if (currentNode.links == null || currentNode.links.Count == 0)
////////////        {
////////////            currentMode = Mode.Explore;
////////////            ApplyModeVisual();
////////////            if (debugLog) Debug.Log($"[Player:{name}] Dead end → switch to EXPLORE");
////////////            return;
////////////        }

////////////        MapNode bestNext = currentNode.links
////////////            .OrderByDescending(n => n.value)
////////////            .FirstOrDefault();

////////////        if (bestNext == null)
////////////        {
////////////            currentMode = Mode.Explore;
////////////            ApplyModeVisual();
////////////            return;
////////////        }

////////////        targetPos = SnapToGrid(bestNext.transform.position);
////////////        moveDir = (targetPos - transform.position).normalized;
////////////        isMoving = true;

////////////        if (debugLog)
////////////            Debug.Log($"[Player:{name}] Learn move -> {WorldToCell(targetPos)}");
////////////    }

////////////    // =====================================================
////////////    // 滑らか移動
////////////    // =====================================================
////////////    void MoveToTarget()
////////////    {
////////////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

////////////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////////////        {
////////////            transform.position = targetPos;
////////////            isMoving = false;

////////////            MapNode nearest = GetNearestNode();
////////////            if (nearest)
////////////            {
////////////                nearest.UpdateValue(goalNode);
////////////                currentNode = nearest;

////////////                Vector2Int cellArrived = WorldToCell(nearest.transform.position);
////////////                if (!recentVisited.Any(r => r.cell == cellArrived))
////////////                {
////////////                    recentVisited.Enqueue((cellArrived, moveDir));
////////////                    if (recentVisited.Count > recentLimit)
////////////                    {
////////////                        var popped = recentVisited.Dequeue();
////////////                        Debug.Log($"{LG} ARRIVE pop {CellStr(popped.cell)}:{DirStr(popped.dir)}");
////////////                    }
////////////                    Debug.Log($"{LG} ARRIVE push {CellStr(cellArrived)}:{DirStr(moveDir)}");
////////////                }
////////////                else
////////////                {
////////////                    Debug.Log($"{LG} ARRIVE skip (already in recent) {CellStr(cellArrived)}");
////////////                }
////////////            }
////////////        }

////////////        if (!reachedGoal && goalNode != null)
////////////        {
////////////            float distToGoal = Vector3.Distance(transform.position, goalNode.transform.position);
////////////            if (distToGoal < 0.1f)
////////////            {
////////////                reachedGoal = true;
////////////                Debug.Log($"[Player:{name}] Reached Goal (dist={distToGoal:F3}). Destroy.");
////////////                Destroy(gameObject);
////////////                return;
////////////            }
////////////        }
////////////    }

////////////    // =====================================================
////////////    // Node設置（重複防止＋生成＋返却）＋ログ追加
////////////    // =====================================================
////////////    MapNode TryPlaceNode(Vector3 pos)
////////////    {
////////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////////////        Vector3 placePos = CellToWorld(cell);
////////////        Debug.Log($"{NG} TryPlaceNode cell={cell} world={placePos}");
////////////        if (MapNode.allNodeCells.Contains(cell))
////////////            return null;

////////////        MapNode.allNodeCells.Add(cell);
////////////        GameObject obj = Instantiate(nodePrefab, placePos, Quaternion.identity);
////////////        return obj.GetComponent<MapNode>();
////////////    }

////////////    MapNode GetNearestNode()
////////////    {
////////////        MapNode[] nodes = FindObjectsOfType<MapNode>();
////////////        return nodes.OrderBy(n => Vector3.Distance(transform.position, n.transform.position)).FirstOrDefault();
////////////    }

////////////    // =====================================================
////////////    // グリッド補助関数
////////////    // =====================================================
////////////    Vector2Int WorldToCell(Vector3 worldPos)
////////////    {
////////////        Vector3 p = worldPos - gridOrigin;
////////////        int cx = Mathf.RoundToInt(p.x / cellSize);
////////////        int cz = Mathf.RoundToInt(p.z / cellSize);
////////////        return new Vector2Int(cx, cz);
////////////    }

////////////    Vector3 CellToWorld(Vector2Int cell)
////////////    {
////////////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
////////////    }

////////////    Vector3 SnapToGrid(Vector3 worldPos)
////////////    {
////////////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
////////////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
////////////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
////////////    }
////////////}

//////////////using UnityEngine;
//////////////using System.Collections.Generic;
//////////////using System.Linq;

//////////////public class Player : MonoBehaviour
//////////////{
//////////////    public enum Mode { Explore, Learn }

//////////////    [Header("移動設定")]
//////////////    public float moveSpeed = 3f;
//////////////    public float cellSize = 1f;
//////////////    public float rayDistance = 1f;
//////////////    public LayerMask wallLayer;

//////////////    [Header("初期設定")]
//////////////    public Vector3 startDirection = Vector3.forward;

//////////////    [Header("Node設定")]
//////////////    public GameObject nodePrefab;
//////////////    public Vector3 gridOrigin = Vector3.zero;
//////////////    public MapNode goalNode;
//////////////    [Range(0f, 1f)] public float exploreModeChance = 0.5f;

//////////////    [Header("Debug")]
//////////////    public bool debugLog = true;

//////////////    // 内部状態
//////////////    private Vector3 moveDir;
//////////////    private bool isMoving = false;
//////////////    private Vector3 targetPos;
//////////////    private Mode currentMode;
//////////////    private MapNode currentNode;
//////////////    private bool reachedGoal = false;

//////////////    [SerializeField] private Renderer bodyRenderer;     // キャラのRendererをアサイン
//////////////    [SerializeField] private Material exploreMaterial;  // 探索モード用（赤系）
//////////////    [SerializeField] private Material learnMaterial;    // 学習モード用（青系）

//////////////    // === 直近の通過Node記録 ===
//////////////    private Queue<(Vector2Int cell, Vector3 dir)> recentVisited = new Queue<(Vector2Int, Vector3)>();
//////////////    private int recentLimit = 10; // 記憶する最大数

//////////////    // ===== Debug helpers =====
//////////////    const string LG = "[EXP-DBG]";
//////////////    const string NG = "[NODE-DBG]";
//////////////    int decideTick = 0;

//////////////    string CellStr(Vector2Int c) => $"({c.x},{c.y})";
//////////////    string DirStr(Vector3 d)
//////////////    {
//////////////        var f = moveDir;
//////////////        var l = Quaternion.Euler(0, -90, 0) * moveDir;
//////////////        var r = Quaternion.Euler(0, 90, 0) * moveDir;
//////////////        var b = -moveDir;
//////////////        float df = Vector3.Dot(d, f), dl = Vector3.Dot(d, l), dr = Vector3.Dot(d, r), db = Vector3.Dot(d, b);
//////////////        float m = Mathf.Max(df, Mathf.Max(dl, Mathf.Max(dr, db)));
//////////////        if (m == df) return "F";
//////////////        if (m == dl) return "L";
//////////////        if (m == dr) return "R";
//////////////        return "B";
//////////////    }
//////////////    string DumpRecent()
//////////////    {
//////////////        return string.Join(" -> ", recentVisited.Select(r => $"{CellStr(r.cell)}:{DirStr(r.dir)}"));
//////////////    }

//////////////    void Start()
//////////////    {
//////////////        moveDir = startDirection.normalized;
//////////////        targetPos = transform.position;
//////////////        transform.position = SnapToGrid(transform.position);

//////////////        currentMode = (Random.value < exploreModeChance) ? Mode.Explore : Mode.Learn;
//////////////        if (debugLog)
//////////////            Debug.Log($"[Player:{name}] Spawned in {currentMode} mode");

//////////////        ApplyModeVisual();
//////////////        currentNode = GetNearestNode();
//////////////    }

//////////////    void Update()
//////////////    {
//////////////        if (!isMoving)
//////////////        {
//////////////            switch (currentMode)
//////////////            {
//////////////                case Mode.Explore:
//////////////                    TryExploreMove();
//////////////                    break;
//////////////                case Mode.Learn:
//////////////                    TryLearnMove();
//////////////                    break;
//////////////            }
//////////////        }
//////////////        else
//////////////        {
//////////////            MoveToTarget();
//////////////        }
//////////////    }

//////////////    private void ApplyModeVisual()
//////////////    {
//////////////        if (bodyRenderer == null) return;

//////////////        if (currentMode == Mode.Explore)
//////////////        {
//////////////            if (exploreMaterial != null) bodyRenderer.material = exploreMaterial;
//////////////            else bodyRenderer.material.color = Color.red;
//////////////        }
//////////////        else
//////////////        {
//////////////            if (learnMaterial != null) bodyRenderer.material = learnMaterial;
//////////////            else bodyRenderer.material.color = Color.blue;
//////////////        }
//////////////    }

//////////////    // =====================================================
//////////////    // 探索モード（詳細デバッグ＋未探索チェック追加）
//////////////    // =====================================================
//////////////    void TryExploreMove()
//////////////    {
//////////////        decideTick++;
//////////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;
//////////////        Vector3 backDir = -moveDir;

//////////////        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
//////////////        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
//////////////        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

//////////////        var curCell = WorldToCell(SnapToGrid(transform.position));
//////////////        Debug.Log($"{LG} T#{decideTick} pos={CellStr(curCell)} dir={DirStr(moveDir)} hits F:{frontHit} L:{leftHit} R:{rightHit} recent[{recentVisited.Count}] {DumpRecent()}");

//////////////        int openCount = 0;
//////////////        if (!frontHit) openCount++;
//////////////        if (!leftHit) openCount++;
//////////////        if (!rightHit) openCount++;

//////////////        // Node設置（前が壁 or 分岐点）
//////////////        if (frontHit || openCount >= 2)
//////////////        {
//////////////            var beforeCount = MapNode.allNodeCells.Count;
//////////////            MapNode newNode = TryPlaceNode(transform.position);
//////////////            var afterCount = MapNode.allNodeCells.Count;
//////////////            if (newNode)
//////////////                Debug.Log($"{LG} T#{decideTick} placeNode @ {CellStr(WorldToCell(newNode.transform.position))} (total {afterCount} / was {beforeCount})");
//////////////        }

//////////////        List<Vector3> openDirs = new List<Vector3>();
//////////////        if (!frontHit) openDirs.Add(moveDir);
//////////////        if (!leftHit) openDirs.Add(leftDir);
//////////////        if (!rightHit) openDirs.Add(rightDir);

//////////////        var beforeBackCull = openDirs.Select(d => DirStr(d)).ToArray();
//////////////        openDirs.RemoveAll(d => Vector3.Dot(d, backDir) > 0.9f);
//////////////        var afterBackCull = openDirs.Select(d => DirStr(d)).ToArray();
//////////////        if (beforeBackCull.Length != afterBackCull.Length)
//////////////            Debug.Log($"{LG} T#{decideTick} back-cull {string.Join(",", beforeBackCull)} -> {string.Join(",", afterBackCull)}");

//////////////        if (openDirs.Count == 0)
//////////////        {
//////////////            openDirs.Add(backDir);
//////////////            Debug.Log($"{LG} T#{decideTick} dead-end -> allow back");
//////////////        }

//////////////        List<Vector3> unexploredDirs = new List<Vector3>();
//////////////        foreach (var dir in openDirs.ToList())
//////////////        {
//////////////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
//////////////            Vector2Int ncell = WorldToCell(nextPos);

//////////////            bool inRecentSameDir = recentVisited.Any(r => r.cell == ncell && Vector3.Dot(r.dir, dir) > 0.9f);
//////////////            bool knownNode = MapNode.allNodeCells.Contains(ncell);

//////////////            Debug.Log($"{LG} T#{decideTick} Check next={nextPos:F2} (cell {ncell}) known={knownNode} totalNodes={MapNode.allNodeCells.Count}");

//////////////            if (inRecentSameDir)
//////////////            {
//////////////                Debug.Log($"{LG} T#{decideTick} filter REJECT by recent: next={CellStr(ncell)} dir={DirStr(dir)}");
//////////////            }
//////////////            else
//////////////            {
//////////////                if (!knownNode)
//////////////                {
//////////////                    unexploredDirs.Add(dir);
//////////////                    Debug.Log($"{LG} T#{decideTick} candidate UNEXPLORED: next={CellStr(ncell)} dir={DirStr(dir)}");
//////////////                }
//////////////                else
//////////////                {
//////////////                    Debug.Log($"{LG} T#{decideTick} candidate KNOWN: next={CellStr(ncell)} dir={DirStr(dir)}");
//////////////                }
//////////////            }
//////////////        }

//////////////        if (unexploredDirs.Count == 0 && openDirs.Count == 0)
//////////////        {
//////////////            Debug.Log($"{LG} T#{decideTick} No moves -> switch to LEARN");
//////////////            currentMode = Mode.Learn;
//////////////            ApplyModeVisual();
//////////////            return;
//////////////        }

//////////////        Vector3 chosenDir = unexploredDirs.Count > 0
//////////////            ? unexploredDirs[Random.Range(0, unexploredDirs.Count)]
//////////////            : openDirs[Random.Range(0, openDirs.Count)];

//////////////        var nextChosenCell = WorldToCell(SnapToGrid(transform.position + chosenDir * cellSize));
//////////////        Debug.Log($"{LG} T#{decideTick} CHOOSE {(unexploredDirs.Count > 0 ? "UNEXPLORED" : "KNOWN")} dir={DirStr(chosenDir)} -> {CellStr(nextChosenCell)}");

//////////////        moveDir = chosenDir;
//////////////        targetPos = SnapToGrid(transform.position + chosenDir * cellSize);
//////////////        isMoving = true;

//////////////        var departCell = curCell;
//////////////        recentVisited.Enqueue((departCell, moveDir));
//////////////        if (recentVisited.Count > recentLimit)
//////////////        {
//////////////            var popped = recentVisited.Dequeue();
//////////////            Debug.Log($"{LG} T#{decideTick} recent pop {CellStr(popped.cell)}:{DirStr(popped.dir)} (limit={recentLimit})");
//////////////        }
//////////////    }

//////////////    // =====================================================
//////////////    // 学習モード
//////////////    // =====================================================
//////////////    void TryLearnMove()
//////////////    {
//////////////        currentNode = GetNearestNode();
//////////////        if (currentNode == null || goalNode == null)
//////////////            return;

//////////////        if (currentNode.links == null || currentNode.links.Count == 0)
//////////////        {
//////////////            currentMode = Mode.Explore;
//////////////            ApplyModeVisual();
//////////////            if (debugLog) Debug.Log($"[Player:{name}] Dead end → switch to EXPLORE");
//////////////            return;
//////////////        }

//////////////        MapNode bestNext = currentNode.links
//////////////            .OrderByDescending(n => n.value)
//////////////            .FirstOrDefault();

//////////////        if (bestNext == null)
//////////////        {
//////////////            currentMode = Mode.Explore;
//////////////            ApplyModeVisual();
//////////////            return;
//////////////        }

//////////////        targetPos = SnapToGrid(bestNext.transform.position);
//////////////        moveDir = (targetPos - transform.position).normalized;
//////////////        isMoving = true;

//////////////        if (debugLog)
//////////////            Debug.Log($"[Player:{name}] Learn move -> {WorldToCell(targetPos)}");
//////////////    }

//////////////    // =====================================================
//////////////    // 滑らか移動
//////////////    // =====================================================
//////////////    void MoveToTarget()
//////////////    {
//////////////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

//////////////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//////////////        {
//////////////            transform.position = targetPos;
//////////////            isMoving = false;

//////////////            MapNode nearest = GetNearestNode();
//////////////            if (nearest)
//////////////            {
//////////////                nearest.UpdateValue(goalNode);
//////////////                currentNode = nearest;

//////////////                Vector2Int cellArrived = WorldToCell(nearest.transform.position);
//////////////                if (!recentVisited.Any(r => r.cell == cellArrived))
//////////////                {
//////////////                    recentVisited.Enqueue((cellArrived, moveDir));
//////////////                    if (recentVisited.Count > recentLimit)
//////////////                    {
//////////////                        var popped = recentVisited.Dequeue();
//////////////                        Debug.Log($"{LG} ARRIVE pop {CellStr(popped.cell)}:{DirStr(popped.dir)}");
//////////////                    }
//////////////                    Debug.Log($"{LG} ARRIVE push {CellStr(cellArrived)}:{DirStr(moveDir)}");
//////////////                }
//////////////                else
//////////////                {
//////////////                    Debug.Log($"{LG} ARRIVE skip (already in recent) {CellStr(cellArrived)}");
//////////////                }
//////////////            }
//////////////        }

//////////////        if (!reachedGoal && goalNode != null)
//////////////        {
//////////////            float distToGoal = Vector3.Distance(transform.position, goalNode.transform.position);
//////////////            if (distToGoal < 0.1f)
//////////////            {
//////////////                reachedGoal = true;
//////////////                Debug.Log($"[Player:{name}] Reached Goal (dist={distToGoal:F3}). Destroy.");
//////////////                Destroy(gameObject);
//////////////                return;
//////////////            }
//////////////        }
//////////////    }

//////////////    // =====================================================
//////////////    // Node設置（重複防止＋生成＋返却）＋ログ追加
//////////////    // =====================================================
//////////////    MapNode TryPlaceNode(Vector3 pos)
//////////////    {
//////////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//////////////        Vector3 placePos = CellToWorld(cell);
//////////////        Debug.Log($"{NG} TryPlaceNode cell={cell} world={placePos}");
//////////////        if (MapNode.allNodeCells.Contains(cell))
//////////////            return null;

//////////////        MapNode.allNodeCells.Add(cell);
//////////////        GameObject obj = Instantiate(nodePrefab, placePos, Quaternion.identity);
//////////////        return obj.GetComponent<MapNode>();
//////////////    }

//////////////    MapNode GetNearestNode()
//////////////    {
//////////////        MapNode[] nodes = FindObjectsOfType<MapNode>();
//////////////        return nodes.OrderBy(n => Vector3.Distance(transform.position, n.transform.position)).FirstOrDefault();
//////////////    }

//////////////    // =====================================================
//////////////    // グリッド補助関数
//////////////    // =====================================================
//////////////    Vector2Int WorldToCell(Vector3 worldPos)
//////////////    {
//////////////        Vector3 p = worldPos - gridOrigin;
//////////////        int cx = Mathf.RoundToInt(p.x / cellSize);
//////////////        int cz = Mathf.RoundToInt(p.z / cellSize);
//////////////        return new Vector2Int(cx, cz);
//////////////    }

//////////////    Vector3 CellToWorld(Vector2Int cell)
//////////////    {
//////////////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
//////////////    }

//////////////    Vector3 SnapToGrid(Vector3 worldPos)
//////////////    {
//////////////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
//////////////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
//////////////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
//////////////    }
//////////////}


////////////////using UnityEngine;
////////////////using System.Collections.Generic;
////////////////using System.Linq;

////////////////public class Player : MonoBehaviour
////////////////{
////////////////    public enum Mode { Explore, Learn }

////////////////    [Header("移動設定")]
////////////////    public float moveSpeed = 3f;
////////////////    public float cellSize = 1f;
////////////////    public float rayDistance = 1f;
////////////////    public LayerMask wallLayer;

////////////////    [Header("初期設定")]
////////////////    public Vector3 startDirection = Vector3.forward;

////////////////    [Header("Node設定")]
////////////////    public GameObject nodePrefab;
////////////////    public Vector3 gridOrigin = Vector3.zero;
////////////////    public MapNode goalNode;
////////////////    [Range(0f, 1f)] public float exploreModeChance = 0.5f;

////////////////    [Header("Debug")]
////////////////    public bool debugLog = true;

////////////////    // 内部状態
////////////////    private Vector3 moveDir;
////////////////    private bool isMoving = false;
////////////////    private Vector3 targetPos;
////////////////    private Mode currentMode;
////////////////    private MapNode currentNode;
////////////////    private bool reachedGoal = false;

////////////////    [SerializeField] private Renderer bodyRenderer;     // キャラのRendererをアサイン
////////////////    [SerializeField] private Material exploreMaterial;  // 探索モード用（赤系）
////////////////    [SerializeField] private Material learnMaterial;    // 学習モード用（青系）

////////////////    // === 直近の通過Node記録 ===
////////////////    private Queue<(Vector2Int cell, Vector3 dir)> recentVisited = new Queue<(Vector2Int, Vector3)>();
////////////////    private int recentLimit = 10; // 記憶する最大数

////////////////    // ===== Debug helpers =====
////////////////    const string LG = "[EXP-DBG]";        // ログ識別タグ
////////////////    int decideTick = 0;                    // 意思決定の通番

////////////////    string CellStr(Vector2Int c) => $"({c.x},{c.y})";
////////////////    string DirStr(Vector3 d)
////////////////    {
////////////////        // 視覚的に分かるよう 4方向判定（前/左/右/後）
////////////////        var f = moveDir;
////////////////        var l = Quaternion.Euler(0, -90, 0) * moveDir;
////////////////        var r = Quaternion.Euler(0, 90, 0) * moveDir;
////////////////        var b = -moveDir;
////////////////        float df = Vector3.Dot(d, f), dl = Vector3.Dot(d, l), dr = Vector3.Dot(d, r), db = Vector3.Dot(d, b);
////////////////        float m = Mathf.Max(df, Mathf.Max(dl, Mathf.Max(dr, db)));
////////////////        if (m == df) return "F";
////////////////        if (m == dl) return "L";
////////////////        if (m == dr) return "R";
////////////////        return "B";
////////////////    }
////////////////    string DumpRecent()
////////////////    {
////////////////        // recentVisited は (cell, dir) のキュー
////////////////        return string.Join(" -> ", recentVisited.Select(r => $"{CellStr(r.cell)}:{DirStr(r.dir)}"));
////////////////    }


////////////////    void Start()
////////////////    {
////////////////        moveDir = startDirection.normalized;
////////////////        targetPos = transform.position;
////////////////        transform.position = SnapToGrid(transform.position);

////////////////        currentMode = (Random.value < exploreModeChance) ? Mode.Explore : Mode.Learn;
////////////////        if (debugLog)
////////////////            Debug.Log($"[Player:{name}] Spawned in {currentMode} mode");

////////////////        ApplyModeVisual();

////////////////        currentNode = GetNearestNode();
////////////////    }

////////////////    void Update()
////////////////    {
////////////////        if (!isMoving)
////////////////        {
////////////////            switch (currentMode)
////////////////            {
////////////////                case Mode.Explore:
////////////////                    TryExploreMove();
////////////////                    break;
////////////////                case Mode.Learn:
////////////////                    TryLearnMove();
////////////////                    break;
////////////////            }
////////////////        }
////////////////        else
////////////////        {
////////////////            MoveToTarget();
////////////////        }
////////////////    }

////////////////    private void ApplyModeVisual()
////////////////    {
////////////////        if (bodyRenderer == null) return;

////////////////        if (currentMode == Mode.Explore)
////////////////        {
////////////////            if (exploreMaterial != null) bodyRenderer.material = exploreMaterial;
////////////////            else bodyRenderer.material.color = Color.red;
////////////////        }
////////////////        else // Mode.Learn
////////////////        {
////////////////            if (learnMaterial != null) bodyRenderer.material = learnMaterial;
////////////////            else bodyRenderer.material.color = Color.blue;
////////////////        }
////////////////    }

////////////////    // =====================================================
////////////////    // 探索モード
////////////////    // =====================================================
////////////////    //void TryExploreMove()
////////////////    //{
////////////////    //    Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////////////////    //    Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;
////////////////    //    Vector3 backDir = -moveDir;

////////////////    //    bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
////////////////    //    bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
////////////////    //    bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

////////////////    //    int openCount = 0;
////////////////    //    if (!frontHit) openCount++;
////////////////    //    if (!leftHit) openCount++;
////////////////    //    if (!rightHit) openCount++;

////////////////    //    // Node設置（前が壁 or 分岐点）
////////////////    //    if (frontHit || openCount >= 2)
////////////////    //    {
////////////////    //        MapNode newNode = TryPlaceNode(transform.position);
////////////////    //        if (newNode && goalNode)
////////////////    //            newNode.InitializeValue(goalNode.transform.position);
////////////////    //    }

////////////////    //    // --- 移動候補を収集 ---
////////////////    //    List<Vector3> openDirs = new List<Vector3>();
////////////////    //    if (!frontHit) openDirs.Add(moveDir);
////////////////    //    if (!leftHit) openDirs.Add(leftDir);
////////////////    //    if (!rightHit) openDirs.Add(rightDir);

////////////////    //    // 元来た方向を除外（ただし行き止まり時は許可）
////////////////    //    openDirs.RemoveAll(d => Vector3.Dot(d, backDir) > 0.9f);
////////////////    //    if (openDirs.Count == 0)
////////////////    //        openDirs.Add(backDir);

////////////////    //    // === 未探索方向＋履歴回避 ===
////////////////    //    List<Vector3> unexploredDirs = new List<Vector3>();
////////////////    //    foreach (var dir in openDirs)
////////////////    //    {
////////////////    //        Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////////////////    //        Vector2Int nextCell = WorldToCell(nextPos);

////////////////    //        // --- 直近訪問(位置+方向)チェック ---
////////////////    //        if (recentVisited.Any(r =>
////////////////    //            r.cell == nextCell && Vector3.Dot(r.dir, dir) > 0.9f))
////////////////    //            continue;

////////////////    //        // 未探索Nodeを優先
////////////////    //        if (!MapNode.allNodeCells.Contains(nextCell))
////////////////    //            unexploredDirs.Add(dir);
////////////////    //    }

////////////////    //    // === 探索打ち切り条件 ===
////////////////    //    if (unexploredDirs.Count == 0 && openDirs.Count == 0)
////////////////    //    {
////////////////    //        if (debugLog)
////////////////    //            Debug.Log($"[Player:{name}] All known → switch to LEARN mode");
////////////////    //        currentMode = Mode.Learn;
////////////////    //        ApplyModeVisual();
////////////////    //        return;
////////////////    //    }

////////////////    //    // === 移動方向を決定 ===
////////////////    //    Vector3 chosenDir = unexploredDirs.Count > 0
////////////////    //        ? unexploredDirs[Random.Range(0, unexploredDirs.Count)]
////////////////    //        : openDirs[Random.Range(0, openDirs.Count)];

////////////////    //    moveDir = chosenDir;
////////////////    //    targetPos = SnapToGrid(transform.position + chosenDir * cellSize);
////////////////    //    isMoving = true;

////////////////    //    // === 履歴に記録 ===
////////////////    //    Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////////////////    //    recentVisited.Enqueue((cell, moveDir));
////////////////    //    if (recentVisited.Count > recentLimit)
////////////////    //        recentVisited.Dequeue();

////////////////    //    if (debugLog)
////////////////    //        Debug.Log($"[Player:{name}] Explore move dir={chosenDir} -> {WorldToCell(targetPos)}");
////////////////    //}
////////////////    void TryExploreMove()
////////////////    {
////////////////        decideTick++;
////////////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////////////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;
////////////////        Vector3 backDir = -moveDir;

////////////////        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
////////////////        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
////////////////        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

////////////////        var curCell = WorldToCell(SnapToGrid(transform.position));
////////////////        Debug.Log($"{LG} T#{decideTick} pos={CellStr(curCell)} dir={DirStr(moveDir)} hits F:{frontHit} L:{leftHit} R:{rightHit} recent[{recentVisited.Count}] {DumpRecent()}");

////////////////        int openCount = 0;
////////////////        if (!frontHit) openCount++;
////////////////        if (!leftHit) openCount++;
////////////////        if (!rightHit) openCount++;

////////////////        // Node設置（前が壁 or 分岐点）
////////////////        if (frontHit || openCount >= 2)
////////////////        {
////////////////            var beforeCount = MapNode.allNodeCells.Count;
////////////////            MapNode newNode = TryPlaceNode(transform.position);
////////////////            var afterCount = MapNode.allNodeCells.Count;
////////////////            if (newNode)
////////////////                Debug.Log($"{LG} T#{decideTick} placeNode @ {CellStr(WorldToCell(newNode.transform.position))} (total {afterCount} / was {beforeCount})");
////////////////        }

////////////////        // 候補収集
////////////////        List<Vector3> openDirs = new List<Vector3>();
////////////////        if (!frontHit) openDirs.Add(moveDir);
////////////////        if (!leftHit) openDirs.Add(leftDir);
////////////////        if (!rightHit) openDirs.Add(rightDir);

////////////////        // 後退除外（事後ログ用に一旦バックアップ）
////////////////        var beforeBackCull = openDirs.Select(d => DirStr(d)).ToArray();
////////////////        openDirs.RemoveAll(d => Vector3.Dot(d, backDir) > 0.9f);
////////////////        var afterBackCull = openDirs.Select(d => DirStr(d)).ToArray();
////////////////        if (beforeBackCull.Length != afterBackCull.Length)
////////////////            Debug.Log($"{LG} T#{decideTick} back-cull {string.Join(",", beforeBackCull)} -> {string.Join(",", afterBackCull)}");

////////////////        if (openDirs.Count == 0)
////////////////        {
////////////////            openDirs.Add(backDir);
////////////////            Debug.Log($"{LG} T#{decideTick} dead-end -> allow back");
////////////////        }

////////////////        // 未探索 + 履歴回避のフィルタ過程を詳細ログ
////////////////        List<Vector3> unexploredDirs = new List<Vector3>();
////////////////        foreach (var dir in openDirs.ToList())
////////////////        {
////////////////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////////////////            Vector2Int ncell = WorldToCell(nextPos);

////////////////            bool inRecentSameDir = recentVisited.Any(r => r.cell == ncell && Vector3.Dot(r.dir, dir) > 0.9f);
////////////////            bool knownNode = MapNode.allNodeCells.Contains(ncell);

////////////////            if (inRecentSameDir)
////////////////            {
////////////////                Debug.Log($"{LG} T#{decideTick} filter REJECT by recent: next={CellStr(ncell)} dir={DirStr(dir)}");
////////////////                // ここでは openDirs は残したまま（“既知選択”に残すため）
////////////////                // unexplored には入れない
////////////////            }
////////////////            else
////////////////            {
////////////////                if (!knownNode)
////////////////                {
////////////////                    unexploredDirs.Add(dir);
////////////////                    Debug.Log($"{LG} T#{decideTick} candidate UNEXPLORED: next={CellStr(ncell)} dir={DirStr(dir)}");
////////////////                }
////////////////                else
////////////////                {
////////////////                    Debug.Log($"{LG} T#{decideTick} candidate KNOWN: next={CellStr(ncell)} dir={DirStr(dir)}");
////////////////                }
////////////////            }
////////////////        }

////////////////        // 打ち切り判定ログ
////////////////        if (unexploredDirs.Count == 0 && openDirs.Count == 0)
////////////////        {
////////////////            Debug.Log($"{LG} T#{decideTick} No moves -> switch to LEARN");
////////////////            currentMode = Mode.Learn;
////////////////            ApplyModeVisual();
////////////////            return;
////////////////        }

////////////////        // 選択理由のログ
////////////////        Vector3 chosenDir = unexploredDirs.Count > 0
////////////////            ? unexploredDirs[Random.Range(0, unexploredDirs.Count)]
////////////////            : openDirs[Random.Range(0, openDirs.Count)];

////////////////        var nextChosenCell = WorldToCell(SnapToGrid(transform.position + chosenDir * cellSize));
////////////////        Debug.Log($"{LG} T#{decideTick} CHOOSE {(unexploredDirs.Count > 0 ? "UNEXPLORED" : "KNOWN")} dir={DirStr(chosenDir)} -> {CellStr(nextChosenCell)}");

////////////////        moveDir = chosenDir;
////////////////        targetPos = SnapToGrid(transform.position + chosenDir * cellSize);
////////////////        isMoving = true;

////////////////        // 出発時に履歴へ積む
////////////////        var departCell = curCell;
////////////////        recentVisited.Enqueue((departCell, moveDir));
////////////////        if (recentVisited.Count > recentLimit)
////////////////        {
////////////////            var popped = recentVisited.Dequeue();
////////////////            Debug.Log($"{LG} T#{decideTick} recent pop {CellStr(popped.cell)}:{DirStr(popped.dir)} (limit={recentLimit})");
////////////////        }
////////////////    }


////////////////    // =====================================================
////////////////    // 学習モード
////////////////    // =====================================================
////////////////    void TryLearnMove()
////////////////    {
////////////////        currentNode = GetNearestNode();
////////////////        if (currentNode == null || goalNode == null)
////////////////            return;

////////////////        // 終端Nodeなら探索モードへ
////////////////        if (currentNode.links == null || currentNode.links.Count == 0)
////////////////        {
////////////////            currentMode = Mode.Explore;
////////////////            ApplyModeVisual();
////////////////            if (debugLog) Debug.Log($"[Player:{name}] Dead end → switch to EXPLORE");
////////////////            return;
////////////////        }

////////////////        // 隣接NodeのValue合計最大方向を仮定
////////////////        MapNode bestNext = currentNode.links
////////////////            .OrderByDescending(n => n.value)
////////////////            .FirstOrDefault();

////////////////        if (bestNext == null)
////////////////        {
////////////////            currentMode = Mode.Explore;
////////////////            ApplyModeVisual();
////////////////            return;
////////////////        }

////////////////        targetPos = SnapToGrid(bestNext.transform.position);
////////////////        moveDir = (targetPos - transform.position).normalized;
////////////////        isMoving = true;

////////////////        if (debugLog)
////////////////            Debug.Log($"[Player:{name}] Learn move -> {WorldToCell(targetPos)}");
////////////////    }

////////////////    // =====================================================
////////////////    // 滑らか移動
////////////////    // =====================================================
////////////////    void MoveToTarget()
////////////////    {
////////////////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

////////////////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////////////////        {
////////////////            transform.position = targetPos;
////////////////            isMoving = false;

////////////////            // Node更新
////////////////            MapNode nearest = GetNearestNode();
////////////////            if (nearest)
////////////////            {
////////////////                nearest.UpdateValue(goalNode);
////////////////                currentNode = nearest;

////////////////                //// === 直近訪問Nodeを記録（位置＋方向） ===
////////////////                //Vector2Int cell = WorldToCell(nearest.transform.position);
////////////////                //if (!recentVisited.Any(r => r.cell == cell))
////////////////                //{
////////////////                //    recentVisited.Enqueue((cell, moveDir));
////////////////                //    if (recentVisited.Count > recentLimit)
////////////////                //        recentVisited.Dequeue(); // 古いものから削除
////////////////                //}
////////////////                // === 直近訪問Nodeを記録（到着時のセルも積む場合） ===
////////////////                Vector2Int cellArrived = WorldToCell(nearest.transform.position);
////////////////                if (!recentVisited.Any(r => r.cell == cellArrived))
////////////////                {
////////////////                    recentVisited.Enqueue((cellArrived, moveDir));
////////////////                    if (recentVisited.Count > recentLimit)
////////////////                    {
////////////////                        var popped = recentVisited.Dequeue();
////////////////                        Debug.Log($"{LG} ARRIVE pop {CellStr(popped.cell)}:{DirStr(popped.dir)}");
////////////////                    }
////////////////                    Debug.Log($"{LG} ARRIVE push {CellStr(cellArrived)}:{DirStr(moveDir)}");
////////////////                }
////////////////                else
////////////////                {
////////////////                    Debug.Log($"{LG} ARRIVE skip (already in recent) {CellStr(cellArrived)}");
////////////////                }
////////////////            }
////////////////        }

////////////////        // === ゴール到達判定 ===
////////////////        if (!reachedGoal && goalNode != null)
////////////////        {
////////////////            float distToGoal = Vector3.Distance(transform.position, goalNode.transform.position);
////////////////            if (distToGoal < 0.1f)
////////////////            {
////////////////                reachedGoal = true;
////////////////                Debug.Log($"[Player:{name}] Reached Goal (dist={distToGoal:F3}). Destroy.");
////////////////                Destroy(gameObject);
////////////////                return;
////////////////            }
////////////////        }
////////////////    }

////////////////    // =====================================================
////////////////    // Node設置（重複防止＋生成＋返却）
////////////////    // =====================================================
////////////////    MapNode TryPlaceNode(Vector3 pos)
////////////////    {
////////////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////////////////        Vector3 placePos = CellToWorld(cell);
////////////////        if (MapNode.allNodeCells.Contains(cell))
////////////////            return null;

////////////////        MapNode.allNodeCells.Add(cell);
////////////////        GameObject obj = Instantiate(nodePrefab, placePos, Quaternion.identity);
////////////////        return obj.GetComponent<MapNode>();
////////////////    }

////////////////    // =====================================================
////////////////    // 近傍Node探索
////////////////    // =====================================================
////////////////    MapNode GetNearestNode()
////////////////    {
////////////////        MapNode[] nodes = FindObjectsOfType<MapNode>();
////////////////        return nodes.OrderBy(n => Vector3.Distance(transform.position, n.transform.position)).FirstOrDefault();
////////////////    }

////////////////    // =====================================================
////////////////    // グリッド補助関数
////////////////    // =====================================================
////////////////    Vector2Int WorldToCell(Vector3 worldPos)
////////////////    {
////////////////        Vector3 p = worldPos - gridOrigin;
////////////////        int cx = Mathf.RoundToInt(p.x / cellSize);
////////////////        int cz = Mathf.RoundToInt(p.z / cellSize);
////////////////        return new Vector2Int(cx, cz);
////////////////    }

////////////////    Vector3 CellToWorld(Vector2Int cell)
////////////////    {
////////////////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
////////////////    }

////////////////    Vector3 SnapToGrid(Vector3 worldPos)
////////////////    {
////////////////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
////////////////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
////////////////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
////////////////    }
////////////////}

//////////////////using UnityEngine;
//////////////////using System.Collections.Generic;
//////////////////using System.Linq;

//////////////////public class Player : MonoBehaviour
//////////////////{
//////////////////    public enum Mode { Explore, Learn }

//////////////////    [Header("移動設定")]
//////////////////    public float moveSpeed = 3f;
//////////////////    public float cellSize = 1f;
//////////////////    public float rayDistance = 1f;
//////////////////    public LayerMask wallLayer;

//////////////////    [Header("初期設定")]
//////////////////    public Vector3 startDirection = Vector3.forward;

//////////////////    [Header("Node設定")]
//////////////////    public GameObject nodePrefab;
//////////////////    public Vector3 gridOrigin = Vector3.zero;
//////////////////    public MapNode goalNode;
//////////////////    [Range(0f, 1f)] public float exploreModeChance = 0.5f;

//////////////////    [Header("Debug")]
//////////////////    public bool debugLog = true;

//////////////////    // 内部状態
//////////////////    private Vector3 moveDir;
//////////////////    private bool isMoving = false;
//////////////////    private Vector3 targetPos;
//////////////////    private Mode currentMode;
//////////////////    private MapNode currentNode;

//////////////////    private bool reachedGoal = false;

//////////////////    [SerializeField] private Renderer bodyRenderer;     // キャラのRendererをアサイン
//////////////////    [SerializeField] private Material exploreMaterial;  // 探索モード用（赤系）
//////////////////    [SerializeField] private Material learnMaterial;    // 学習モード用（青系）

//////////////////    void Start()
//////////////////    {
//////////////////        moveDir = startDirection.normalized;
//////////////////        targetPos = transform.position;
//////////////////        transform.position = SnapToGrid(transform.position);

//////////////////        currentMode = (Random.value < exploreModeChance) ? Mode.Explore : Mode.Learn;
//////////////////        if (debugLog)
//////////////////            Debug.Log($"[Player:{name}] Spawned in {currentMode} mode");

//////////////////        ApplyModeVisual();

//////////////////        currentNode = GetNearestNode();
//////////////////    }

//////////////////    void Update()
//////////////////    {
//////////////////        if (!isMoving)
//////////////////        {
//////////////////            switch (currentMode)
//////////////////            {
//////////////////                case Mode.Explore:
//////////////////                    TryExploreMove();
//////////////////                    break;
//////////////////                case Mode.Learn:
//////////////////                    TryLearnMove();
//////////////////                    break;
//////////////////            }
//////////////////        }
//////////////////        else
//////////////////        {
//////////////////            MoveToTarget();
//////////////////        }
//////////////////    }

//////////////////    private void ApplyModeVisual()
//////////////////    {
//////////////////        if (bodyRenderer == null) return;

//////////////////        if (currentMode == Mode.Explore)
//////////////////        {
//////////////////            if (exploreMaterial != null) bodyRenderer.material = exploreMaterial;
//////////////////            else bodyRenderer.material.color = Color.red;
//////////////////        }
//////////////////        else // Mode.Learn
//////////////////        {
//////////////////            if (learnMaterial != null) bodyRenderer.material = learnMaterial;
//////////////////            else bodyRenderer.material.color = Color.blue;
//////////////////        }
//////////////////    }


//////////////////    //// =====================================================
//////////////////    //// 探索モード
//////////////////    //// =====================================================
//////////////////    //void TryExploreMove()
//////////////////    //{
//////////////////    //    Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////////////////    //    Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//////////////////    //    bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
//////////////////    //    bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
//////////////////    //    bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

//////////////////    //    int openCount = 0;
//////////////////    //    if (!frontHit) openCount++;
//////////////////    //    if (!leftHit) openCount++;
//////////////////    //    if (!rightHit) openCount++;

//////////////////    //    // Node設置（前が壁 or 分岐点）
//////////////////    //    if (frontHit || openCount >= 2)
//////////////////    //    {
//////////////////    //        MapNode newNode = TryPlaceNode(transform.position);
//////////////////    //        if (newNode && goalNode)
//////////////////    //            newNode.InitializeValue(goalNode.transform.position);
//////////////////    //    }

//////////////////    //    // 移動方向選択
//////////////////    //    List<Vector3> openDirs = new List<Vector3>();
//////////////////    //    if (!frontHit) openDirs.Add(moveDir);
//////////////////    //    if (!leftHit) openDirs.Add(leftDir);
//////////////////    //    if (!rightHit) openDirs.Add(rightDir);

//////////////////    //    // 未探索方向優先
//////////////////    //    List<Vector3> unexploredDirs = new List<Vector3>();
//////////////////    //    foreach (var dir in openDirs)
//////////////////    //    {
//////////////////    //        Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
//////////////////    //        Vector2Int nextCell = WorldToCell(nextPos);
//////////////////    //        if (!MapNode.allNodeCells.Contains(nextCell))
//////////////////    //            unexploredDirs.Add(dir);
//////////////////    //    }

//////////////////    //    if (unexploredDirs.Count == 0 && openDirs.Count == 0)
//////////////////    //    {
//////////////////    //        if (debugLog)
//////////////////    //            Debug.Log($"[Player:{name}] All known → switch to LEARN mode");
//////////////////    //        currentMode = Mode.Learn;
//////////////////    //        ApplyModeVisual();
//////////////////    //        return;
//////////////////    //    }

//////////////////    //    Vector3 chosenDir = unexploredDirs.Count > 0
//////////////////    //        ? unexploredDirs[Random.Range(0, unexploredDirs.Count)]
//////////////////    //        : openDirs[Random.Range(0, openDirs.Count)];

//////////////////    //    moveDir = chosenDir;
//////////////////    //    targetPos = SnapToGrid(transform.position + chosenDir * cellSize);
//////////////////    //    isMoving = true;

//////////////////    //    if (debugLog)
//////////////////    //        Debug.Log($"[Player:{name}] Explore move dir={chosenDir} -> {WorldToCell(targetPos)}");
//////////////////    //}
//////////////////    // =====================================================
//////////////////    // 探索モード（修正版：後ろ方向を除外）
//////////////////    // =====================================================
//////////////////    void TryExploreMove()
//////////////////    {
//////////////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////////////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;
//////////////////        Vector3 backDir = -moveDir; // ★元来た道を記録

//////////////////        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
//////////////////        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
//////////////////        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

//////////////////        int openCount = 0;
//////////////////        if (!frontHit) openCount++;
//////////////////        if (!leftHit) openCount++;
//////////////////        if (!rightHit) openCount++;

//////////////////        // Node設置（前が壁 or 分岐点）
//////////////////        if (frontHit || openCount >= 2)
//////////////////        {
//////////////////            MapNode newNode = TryPlaceNode(transform.position);
//////////////////            if (newNode && goalNode)
//////////////////                newNode.InitializeValue(goalNode.transform.position);
//////////////////        }

//////////////////        // 移動方向選択
//////////////////        List<Vector3> openDirs = new List<Vector3>();
//////////////////        if (!frontHit) openDirs.Add(moveDir);
//////////////////        if (!leftHit) openDirs.Add(leftDir);
//////////////////        if (!rightHit) openDirs.Add(rightDir);

//////////////////        // ★元来た道を除外（ただし行き止まり時は許可）
//////////////////        openDirs.RemoveAll(d => Vector3.Dot(d, backDir) > 0.9f);
//////////////////        if (openDirs.Count == 0)
//////////////////        {
//////////////////            // 完全に行き止まりなら一度だけ戻る
//////////////////            openDirs.Add(backDir);
//////////////////        }

//////////////////        // 未探索方向優先
//////////////////        List<Vector3> unexploredDirs = new List<Vector3>();
//////////////////        foreach (var dir in openDirs)
//////////////////        {
//////////////////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
//////////////////            Vector2Int nextCell = WorldToCell(nextPos);
//////////////////            if (!MapNode.allNodeCells.Contains(nextCell))
//////////////////                unexploredDirs.Add(dir);
//////////////////        }

//////////////////        // 移動先がない場合 → 学習モードへ
//////////////////        if (unexploredDirs.Count == 0 && openDirs.Count == 0)
//////////////////        {
//////////////////            if (debugLog)
//////////////////                Debug.Log($"[Player:{name}] All known → switch to LEARN mode");
//////////////////            currentMode = Mode.Learn;
//////////////////            ApplyModeVisual();
//////////////////            return;
//////////////////        }

//////////////////        Vector3 chosenDir = unexploredDirs.Count > 0
//////////////////            ? unexploredDirs[Random.Range(0, unexploredDirs.Count)]
//////////////////            : openDirs[Random.Range(0, openDirs.Count)];

//////////////////        moveDir = chosenDir;
//////////////////        targetPos = SnapToGrid(transform.position + chosenDir * cellSize);
//////////////////        isMoving = true;

//////////////////        if (debugLog)
//////////////////            Debug.Log($"[Player:{name}] Explore move dir={chosenDir} -> {WorldToCell(targetPos)}");
//////////////////    }


//////////////////    // =====================================================
//////////////////    // 学習モード
//////////////////    // =====================================================
//////////////////    void TryLearnMove()
//////////////////    {
//////////////////        currentNode = GetNearestNode();
//////////////////        if (currentNode == null || goalNode == null)
//////////////////            return;

//////////////////        // 終端Nodeなら探索モードへ
//////////////////        if (currentNode.links == null || currentNode.links.Count == 0)
//////////////////        {
//////////////////            currentMode = Mode.Explore;
//////////////////            ApplyModeVisual();
//////////////////            if (debugLog) Debug.Log($"[Player:{name}] Dead end → switch to EXPLORE");
//////////////////            return;
//////////////////        }

//////////////////        // 隣接NodeのValue合計最大方向を仮定
//////////////////        MapNode bestNext = currentNode.links
//////////////////            .OrderByDescending(n => n.value)
//////////////////            .FirstOrDefault();

//////////////////        if (bestNext == null)
//////////////////        {
//////////////////            currentMode = Mode.Explore;
//////////////////            ApplyModeVisual();
//////////////////            return;
//////////////////        }

//////////////////        targetPos = SnapToGrid(bestNext.transform.position);
//////////////////        moveDir = (targetPos - transform.position).normalized;
//////////////////        isMoving = true;

//////////////////        if (debugLog)
//////////////////            Debug.Log($"[Player:{name}] Learn move -> {WorldToCell(targetPos)}");
//////////////////    }

//////////////////    // =====================================================
//////////////////    // 滑らか移動
//////////////////    // =====================================================
//////////////////    void MoveToTarget()
//////////////////    {
//////////////////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

//////////////////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//////////////////        {
//////////////////            transform.position = targetPos;
//////////////////            isMoving = false;

//////////////////            // Node更新
//////////////////            MapNode nearest = GetNearestNode();
//////////////////            if (nearest)
//////////////////            {
//////////////////                nearest.UpdateValue(goalNode);
//////////////////                currentNode = nearest;
//////////////////            }
//////////////////        }

//////////////////        // === ゴール到達判定 ===
//////////////////        if (!reachedGoal && goalNode != null)
//////////////////        {
//////////////////            float distToGoal = Vector3.Distance(transform.position, goalNode.transform.position);

//////////////////            if (distToGoal < 0.1f) // ← 誤差許容
//////////////////            {
//////////////////                reachedGoal = true;
//////////////////                Debug.Log($"[Player:{name}] Reached Goal (dist={distToGoal:F3}). Destroy.");
//////////////////                Destroy(gameObject);
//////////////////                return;
//////////////////            }
//////////////////        }
//////////////////    }

//////////////////    // =====================================================
//////////////////    // Node設置（重複防止＋生成＋返却）
//////////////////    // =====================================================
//////////////////    MapNode TryPlaceNode(Vector3 pos)
//////////////////    {
//////////////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//////////////////        Vector3 placePos = CellToWorld(cell);
//////////////////        if (MapNode.allNodeCells.Contains(cell))
//////////////////            return null;

//////////////////        MapNode.allNodeCells.Add(cell);
//////////////////        GameObject obj = Instantiate(nodePrefab, placePos, Quaternion.identity);
//////////////////        return obj.GetComponent<MapNode>();
//////////////////    }

//////////////////    // =====================================================
//////////////////    // 近傍Node探索
//////////////////    // =====================================================
//////////////////    MapNode GetNearestNode()
//////////////////    {
//////////////////        MapNode[] nodes = FindObjectsOfType<MapNode>();
//////////////////        return nodes.OrderBy(n => Vector3.Distance(transform.position, n.transform.position)).FirstOrDefault();
//////////////////    }

//////////////////    // =====================================================
//////////////////    // グリッド補助関数
//////////////////    // =====================================================
//////////////////    Vector2Int WorldToCell(Vector3 worldPos)
//////////////////    {
//////////////////        Vector3 p = worldPos - gridOrigin;
//////////////////        int cx = Mathf.RoundToInt(p.x / cellSize);
//////////////////        int cz = Mathf.RoundToInt(p.z / cellSize);
//////////////////        return new Vector2Int(cx, cz);
//////////////////    }

//////////////////    Vector3 CellToWorld(Vector2Int cell)
//////////////////    {
//////////////////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
//////////////////    }

//////////////////    Vector3 SnapToGrid(Vector3 worldPos)
//////////////////    {
//////////////////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
//////////////////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
//////////////////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
//////////////////    }
//////////////////}

////////////////////using UnityEngine;
////////////////////using System.Collections.Generic;

////////////////////public class Player : MonoBehaviour
////////////////////{
////////////////////    [Header("移動設定")]
////////////////////    public float moveSpeed = 3f;          // 移動速度
////////////////////    public float cellSize = 1f;           // グリッド1マスの大きさ
////////////////////    public float rayDistance = 1f;        // Rayの距離（1マス分）
////////////////////    public LayerMask wallLayer;           // 壁レイヤー

////////////////////    [Header("初期設定")]
////////////////////    public Vector3 startDirection = Vector3.forward;

////////////////////    [Header("Node設定")]
////////////////////    public GameObject nodePrefab;
////////////////////    public Vector3 gridOrigin = Vector3.zero; // グリッド原点
////////////////////    public bool debugLog = true;

////////////////////    // 内部状態
////////////////////    private Vector3 moveDir;
////////////////////    private bool isMoving = false;
////////////////////    private Vector3 targetPos;

////////////////////    void Start()
////////////////////    {
////////////////////        moveDir = startDirection.normalized;
////////////////////        targetPos = transform.position;

////////////////////        // スナップして初期位置を補正
////////////////////        transform.position = SnapToGrid(transform.position);
////////////////////    }

////////////////////    void Update()
////////////////////    {
////////////////////        if (!isMoving) TryMove();
////////////////////        else MoveToTarget();
////////////////////    }

////////////////////    // =====================================================
////////////////////    // 次の移動先を決定して移動を開始
////////////////////    // =====================================================
////////////////////    //void TryMove()
////////////////////    //{
////////////////////    //    Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////////////////////    //    Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////////////////////    //    bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
////////////////////    //    bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
////////////////////    //    bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

////////////////////    //    // Debug用のRay可視化
////////////////////    //    Debug.DrawRay(transform.position, moveDir * rayDistance, Color.red);
////////////////////    //    Debug.DrawRay(transform.position, leftDir * rayDistance, Color.blue);
////////////////////    //    Debug.DrawRay(transform.position, rightDir * rayDistance, Color.green);

////////////////////    //    // 進行可能方向数を数える
////////////////////    //    int openCount = 0;
////////////////////    //    if (!frontHit) openCount++;
////////////////////    //    if (!leftHit) openCount++;
////////////////////    //    if (!rightHit) openCount++;

////////////////////    //    // =============================
////////////////////    //    // Node設置条件（前が壁 or 分岐点）
////////////////////    //    // =============================
////////////////////    //    if (frontHit || openCount >= 2)
////////////////////    //    {
////////////////////    //        TryPlaceNode(transform.position);
////////////////////    //    }

////////////////////    //    // =============================
////////////////////    //    // 移動先を決定
////////////////////    //    // =============================

////////////////////    //    // 前方が開いているなら直進
////////////////////    //    if (!frontHit)
////////////////////    //    {
////////////////////    //        Vector3 snappedPos = SnapToGrid(transform.position);
////////////////////    //        targetPos = SnapToGrid(snappedPos + moveDir * cellSize);
////////////////////    //        isMoving = true;

////////////////////    //        if (debugLog)
////////////////////    //            Debug.Log($"[Player:{name}] Move forward -> {WorldToCell(targetPos)}");
////////////////////    //    }
////////////////////    //    else
////////////////////    //    {
////////////////////    //        // 前が壁なら左右の空き方向を探索
////////////////////    //        var openDirs = new List<Vector3>();
////////////////////    //        if (!leftHit) openDirs.Add(leftDir);
////////////////////    //        if (!rightHit) openDirs.Add(rightDir);

////////////////////    //        // 開いている方向があればランダムで選択
////////////////////    //        if (openDirs.Count > 0)
////////////////////    //        {
////////////////////    //            moveDir = openDirs[Random.Range(0, openDirs.Count)];
////////////////////    //            Vector3 snappedPos = SnapToGrid(transform.position);
////////////////////    //            targetPos = SnapToGrid(snappedPos + moveDir * cellSize);
////////////////////    //            isMoving = true;

////////////////////    //            if (debugLog)
////////////////////    //                Debug.Log($"[Player:{name}] Turn -> {moveDir}, target={WorldToCell(targetPos)}");
////////////////////    //        }
////////////////////    //        else
////////////////////    //        {
////////////////////    //            // 完全に行き止まり
////////////////////    //            if (debugLog)
////////////////////    //                Debug.Log($"[Player:{name}] Dead end @ {WorldToCell(transform.position)}");
////////////////////    //        }
////////////////////    //    }
////////////////////    //}
////////////////////    void TryMove()
////////////////////    {
////////////////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////////////////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////////////////////        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
////////////////////        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
////////////////////        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

////////////////////        int openCount = 0;
////////////////////        if (!frontHit) openCount++;
////////////////////        if (!leftHit) openCount++;
////////////////////        if (!rightHit) openCount++;

////////////////////        // Node設置（前が壁 or 分岐点）
////////////////////        if (frontHit || openCount >= 2)
////////////////////            TryPlaceNode(transform.position);

////////////////////        // ================================
////////////////////        // 新しい方向を優先して選択する部分
////////////////////        // ================================
////////////////////        List<Vector3> openDirs = new List<Vector3>();
////////////////////        if (!frontHit) openDirs.Add(moveDir);
////////////////////        if (!leftHit) openDirs.Add(leftDir);
////////////////////        if (!rightHit) openDirs.Add(rightDir);

////////////////////        // 「未探索（Node未設置）」方向を抽出
////////////////////        List<Vector3> unexploredDirs = new List<Vector3>();
////////////////////        foreach (var dir in openDirs)
////////////////////        {
////////////////////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////////////////////            Vector2Int nextCell = WorldToCell(nextPos);
////////////////////            if (!MapNode.allNodeCells.Contains(nextCell))
////////////////////                unexploredDirs.Add(dir);
////////////////////        }

////////////////////        Vector3 chosenDir = moveDir;

////////////////////        if (unexploredDirs.Count > 0)
////////////////////        {
////////////////////            // 未探索方向があればその中からランダム
////////////////////            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////////////////////        }
////////////////////        else if (openDirs.Count > 0)
////////////////////        {
////////////////////            // すべて探索済みなら、既知方向からランダム
////////////////////            chosenDir = openDirs[Random.Range(0, openDirs.Count)];
////////////////////        }
////////////////////        else
////////////////////        {
////////////////////            // 完全に行き止まり
////////////////////            if (debugLog)
////////////////////                Debug.Log($"[Player:{name}] Dead end @ {WorldToCell(transform.position)}");
////////////////////            return;
////////////////////        }

////////////////////        // ================================
////////////////////        // 移動を開始
////////////////////        // ================================
////////////////////        moveDir = chosenDir;
////////////////////        targetPos = SnapToGrid(transform.position + chosenDir * cellSize);
////////////////////        isMoving = true;

////////////////////        if (debugLog)
////////////////////            Debug.Log($"[Player:{name}] Move dir={chosenDir} -> {WorldToCell(targetPos)}");
////////////////////    }


////////////////////    // =====================================================
////////////////////    // 滑らかにターゲットまで移動
////////////////////    // =====================================================
////////////////////    void MoveToTarget()
////////////////////    {
////////////////////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

////////////////////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////////////////////        {
////////////////////            transform.position = targetPos;
////////////////////            isMoving = false;
////////////////////        }
////////////////////    }

////////////////////    // =====================================================
////////////////////    // Node設置（重複防止＋スナップ＋共有登録）
////////////////////    // =====================================================
////////////////////    void TryPlaceNode(Vector3 pos)
////////////////////    {
////////////////////        // 1. スナップしてセル座標を取得
////////////////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////////////////////        Vector3 placePos = CellToWorld(cell);

////////////////////        // 2. すでに設置済みならスキップ
////////////////////        if (MapNode.allNodeCells.Contains(cell))
////////////////////            return;

////////////////////        // 3. 新規登録
////////////////////        MapNode.allNodeCells.Add(cell);

////////////////////        // 4. Nodeを生成
////////////////////        Instantiate(nodePrefab, placePos, Quaternion.identity);

////////////////////        if (debugLog)
////////////////////            Debug.Log($"[Player:{name}] Node placed @ {cell}");
////////////////////    }

////////////////////    // =====================================================
////////////////////    // グリッド補助関数群
////////////////////    // =====================================================
////////////////////    Vector2Int WorldToCell(Vector3 worldPos)
////////////////////    {
////////////////////        Vector3 p = worldPos - gridOrigin;
////////////////////        int cx = Mathf.RoundToInt(p.x / cellSize);
////////////////////        int cz = Mathf.RoundToInt(p.z / cellSize);
////////////////////        return new Vector2Int(cx, cz);
////////////////////    }

////////////////////    Vector3 CellToWorld(Vector2Int cell)
////////////////////    {
////////////////////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
////////////////////    }

////////////////////    Vector3 SnapToGrid(Vector3 worldPos)
////////////////////    {
////////////////////        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
////////////////////        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
////////////////////        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
////////////////////    }
////////////////////}

//////////////////////using UnityEngine;
//////////////////////using System.Collections.Generic;

//////////////////////public class Player : MonoBehaviour
//////////////////////{
//////////////////////    [Header("移動設定")]
//////////////////////    public float moveSpeed;
//////////////////////    public float cellSize = 1f;
//////////////////////    public float rayDistance = 1f;
//////////////////////    public LayerMask wallLayer;

//////////////////////    [Header("初期設定")]
//////////////////////    public Vector3 startDirection = Vector3.forward;

//////////////////////    [Header("Node")]
//////////////////////    public GameObject nodePrefab;
//////////////////////    public Vector3 gridOrigin = Vector3.zero; // グリッド原点（必要なら調整）

//////////////////////    private Vector3 moveDir;
//////////////////////    private bool isMoving = false;
//////////////////////    private Vector3 targetPos;

//////////////////////    void Start()
//////////////////////    {
//////////////////////        moveDir = startDirection.normalized;
//////////////////////        targetPos = transform.position;
//////////////////////    }

//////////////////////    void Update()
//////////////////////    {
//////////////////////        if (!isMoving) TryMove();
//////////////////////        else MoveToTarget();
//////////////////////    }

//////////////////////    void TryMove()
//////////////////////    {
//////////////////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////////////////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//////////////////////        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
//////////////////////        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
//////////////////////        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

//////////////////////        Debug.DrawRay(transform.position, moveDir * rayDistance, Color.red);
//////////////////////        Debug.DrawRay(transform.position, leftDir * rayDistance, Color.blue);
//////////////////////        Debug.DrawRay(transform.position, rightDir * rayDistance, Color.green);

//////////////////////        int openCount = 0;
//////////////////////        if (!frontHit) openCount++;
//////////////////////        if (!leftHit) openCount++;
//////////////////////        if (!rightHit) openCount++;

//////////////////////        // 曲がり角 or 前が壁ならNode設置
//////////////////////        if (frontHit || openCount >= 2)
//////////////////////        {
//////////////////////            TryPlaceNode(transform.position);
//////////////////////        }

//////////////////////        // 前が空いていればそのまま進行
//////////////////////        if (!frontHit)
//////////////////////        {
//////////////////////            targetPos = transform.position + moveDir * cellSize;
//////////////////////            isMoving = true;
//////////////////////        }
//////////////////////        else
//////////////////////        {
//////////////////////            // 前が壁なら左右方向へランダム転換
//////////////////////            var open = new List<Vector3>(2);
//////////////////////            if (!leftHit) open.Add(leftDir);
//////////////////////            if (!rightHit) open.Add(rightDir);

//////////////////////            if (open.Count > 0)
//////////////////////            {
//////////////////////                moveDir = open[Random.Range(0, open.Count)];
//////////////////////                targetPos = transform.position + moveDir * cellSize;
//////////////////////                isMoving = true;
//////////////////////            }
//////////////////////        }
//////////////////////    }

//////////////////////    void MoveToTarget()
//////////////////////    {
//////////////////////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//////////////////////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//////////////////////        {
//////////////////////            transform.position = targetPos;
//////////////////////            isMoving = false;
//////////////////////        }
//////////////////////    }

//////////////////////    // =====================================================
//////////////////////    // Node設置（共有チェック＋スナップ＋即登録）
//////////////////////    // =====================================================
//////////////////////    void TryPlaceNode(Vector3 pos)
//////////////////////    {
//////////////////////        // 1 スナップ（浮動小数誤差防止）
//////////////////////        Vector2Int cell = WorldToCell(pos);
//////////////////////        Vector3 placePos = CellToWorld(cell);

//////////////////////        // 2 すでに共有リストに存在するか確認
//////////////////////        if (MapNode.allNodeCells.Contains(cell))
//////////////////////            return;

//////////////////////        // 3 即座に共有リストに登録（他プレイヤーも認識可能）
//////////////////////        MapNode.allNodeCells.Add(cell);

//////////////////////        // 4 Nodeを生成
//////////////////////        Instantiate(nodePrefab, placePos, Quaternion.identity);
//////////////////////    }

//////////////////////    // =====================================================
//////////////////////    // ワールド→セル変換
//////////////////////    // =====================================================
//////////////////////    Vector2Int WorldToCell(Vector3 worldPos)
//////////////////////    {
//////////////////////        Vector3 p = worldPos - gridOrigin;
//////////////////////        int cx = Mathf.RoundToInt(p.x / cellSize);
//////////////////////        int cz = Mathf.RoundToInt(p.z / cellSize);
//////////////////////        return new Vector2Int(cx, cz);
//////////////////////    }

//////////////////////    // =====================================================
//////////////////////    // セル→ワールド変換
//////////////////////    // =====================================================
//////////////////////    Vector3 CellToWorld(Vector2Int cell)
//////////////////////    {
//////////////////////        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
//////////////////////    }
//////////////////////}
