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