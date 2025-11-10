using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class LearningPlayer : MonoBehaviour
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
    public MapNode goalNode;
    public GameObject nodePrefab;

    [Header("探索設定")]
    [Range(0f, 1f)] public float epsilon = 0.2f; // ランダム探索率
    public int linkRayMaxSteps = 100;

    [Header("デバッグ")]
    public bool debugLog = true;
    public bool debugRay = true;

    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private MapNode currentNode;
    private MapNode prevNode;
    private bool reachedGoal = false;

    private const float EPS = 1e-4f;

    // ======================================================
    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position = SnapToGrid(transform.position);
        currentNode = TryPlaceNode(transform.position);
        prevNode = null;

        if (goalNode == null)
        {
            GameObject goalObj = GameObject.Find("Goal");
            if (goalObj != null)
                goalNode = goalObj.GetComponent<MapNode>();
        }

        // ★ 初期Value計算（Goal距離ベース）
        if (goalNode != null && currentNode != null)
            currentNode.UpdateValueByGoal(goalNode);

        if (debugLog) Debug.Log($"[LearningPlayer:{name}] Start @ {currentNode}");
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
    bool CanPlaceNodeHere()
    {
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

        int openCount = (!frontHit ? 1 : 0) + (!leftHit ? 1 : 0) + (!rightHit ? 1 : 0);
        return (frontHit || openCount >= 2);
    }

    // ======================================================
    // ε-greedy探索
    // ======================================================
    void TryExploreMove()
    {
        currentNode = TryPlaceNode(transform.position);
        var dirs = ScanAroundDirections();
        if (dirs.Count == 0) return;

        // 各候補ノードのValueをGoal距離から更新
        foreach (var d in dirs)
        {
            if (d.node != null && goalNode != null)
                d.node.UpdateValueByGoal(goalNode);
        }

        var unexplored = dirs.Where(d => d.node == null || !d.hasLink).ToList();
        var known = dirs.Where(d => d.node != null && d.hasLink && d.node != prevNode).ToList();

        (Vector3 dir, MapNode node, bool hasLink)? chosen = null;

        // ε確率でランダム探索
        if (Random.value < epsilon)
        {
            if (unexplored.Count > 0)
                chosen = unexplored[Random.Range(0, unexplored.Count)];
            else if (known.Count > 0)
                chosen = known[Random.Range(0, known.Count)];
        }
        else
        {
            // Goalに近い（＝Valueが高い）ノードを優先
            if (known.Count > 0)
                chosen = known.OrderByDescending(d => d.node.value).First();
            else if (unexplored.Count > 0)
                chosen = unexplored.OrderByDescending(d =>
                    (d.node != null ? d.node.value : 0f)).First();
        }

        if (chosen.HasValue)
        {
            moveDir = chosen.Value.dir;
            MoveForward();
        }
    }

    // ======================================================
    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
    {
        List<(Vector3, MapNode, bool)> found = new();
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in dirs)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
                continue;

            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
            Vector2Int nextCell = WorldToCell(nextPos);
            MapNode nextNode = MapNode.FindByCell(nextCell);
            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));
            found.Add((dir, nextNode, linked));
        }
        return found;
    }

    // ======================================================
    void MoveForward()
    {
        targetPos = SnapToGrid(transform.position + moveDir * cellSize);
        isMoving = true;
    }

    // ======================================================
    // 移動完了後リンク形成とGoal判定
    // ======================================================
    void MoveToTarget()
    {
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.position, targetPos) < EPS)
        {
            transform.position = targetPos;
            isMoving = false;

            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
            MapNode nextNode = MapNode.FindByCell(cell);

            if (currentNode != null && nextNode != null)
            {
                // ノード間リンク形成
                currentNode.AddLink(nextNode);
                LinkBackWithRay(currentNode);

                // Value更新（Goal距離ベース）
                if (goalNode != null)
                    nextNode.UpdateValueByGoal(goalNode);
            }

            prevNode = currentNode;
            currentNode = nextNode;

            // Goal到達チェック
            if (!reachedGoal && goalNode != null)
            {
                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));

                if (playerCell == goalCell)
                {
                    reachedGoal = true;
                    LinkBackWithRay(currentNode);
                    RecalculateGoalDistance();
                    Destroy(gameObject);
                }
            }
        }
    }

    // ======================================================
    // Goal含むリンク形成処理（Player.csの方式）
    // ======================================================
    private void LinkBackWithRay(MapNode node)
    {
        if (node == null) return;

        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        LayerMask mask = wallLayer | nodeLayer;

        foreach (var dir in dirs)
        {
            for (int step = 1; step <= linkRayMaxSteps; step++)
            {
                float dist = cellSize * step;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
                {
                    int hitLayer = hit.collider.gameObject.layer;

                    if ((wallLayer.value & (1 << hitLayer)) != 0)
                        break;

                    if ((nodeLayer.value & (1 << hitLayer)) != 0)
                    {
                        MapNode hitNode = hit.collider.GetComponent<MapNode>();
                        if (hitNode != null && hitNode != node)
                        {
                            node.AddLink(hitNode);
                            if (debugRay)
                                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);
                        }
                        break;
                    }
                }
            }
        }

        // GoalNodeがnodeLayerで検出されない場合、距離で直接リンク
        if (goalNode != null && Vector3.Distance(node.transform.position, goalNode.transform.position) <= cellSize + 0.1f)
        {
            node.AddLink(goalNode);
            if (debugLog)
                Debug.Log($"[LINK-Goal] {node.name} ↔ {goalNode.name}");
        }
    }

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
    // ノード設置時リンク生成
    // ======================================================
    MapNode TryPlaceNode(Vector3 pos)
    {
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
        {
            LinkBackWithRay(node);
            if (goalNode != null)
                node.UpdateValueByGoal(goalNode);
        }

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
