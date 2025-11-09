using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

public class Player : MonoBehaviour
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

    [Header("行動傾向")]
    [Range(0f, 1f)] public float exploreBias = 0.6f;

    [Header("リンク探索")]
    public int linkRayMaxSteps = 100;

    [Header("デバッグ")]
    public bool debugLog = true;
    public bool debugRay = true;
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Material exploreMaterial;

    public static bool hasLearnedGoal = false; // ★全プレイヤー共通（Goal学習完了フラグ）

    // 内部状態
    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private MapNode currentNode;
    private bool reachedGoal = false;

    // ★修正A: 最短経路モード中フラグを追加
    private bool isFollowingShortest = false;

    private const float EPS = 1e-4f; // ★追加: float比較用の微小量

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
            {
                goalNode = goalObj.GetComponent<MapNode>();
                //Debug.Log($"[Player] GoalNode assigned from Scene object: {goalNode.name}");
            }
            else
            {
                //Debug.LogWarning("[Player] Goal object not found in scene!");
            }
        }

        // ★修正B: 生成時に最短経路モードへ入る場合はフラグONして探索を完全停止
        if (hasLearnedGoal)
        {
            if (Random.value < 1.0f)
            {
                //Debug.Log($"[Player:{name}] Spawned as shortest-path follower");
                isFollowingShortest = true; // ★追加
                StopAllCoroutines();
                StartCoroutine(FollowShortestPath());
                return; // ★探索ロジックに入らない
            }
            else
            {
                //Debug.Log($"[Player:{name}] Spawned as explorer (continue exploring)");
            }
        }

        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
    }

    // ======================================================
    // Update
    // ======================================================
    void Update()
    {
        // ★修正C: 最短経路モード中は探索系を一切走らせない
        if (isFollowingShortest)
        {
            // コルーチンが MoveForward → 到着待機 → 次手… を制御する。
            // ここで勝手にTryExploreMoveやMoveForwardを呼ばない。
            if (isMoving)
            {
                MoveToTarget();
            }
            return;
        }

        // 以降は通常（探索）モード
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
    // Visual
    // ======================================================
    private void ApplyVisual()
    {
        if (bodyRenderer == null) return;
        bodyRenderer.material = exploreMaterial
            ? exploreMaterial
            : new Material(Shader.Find("Standard")) { color = Color.cyan };
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
    // CanPlaceNodeHere
    // ======================================================
    bool CanPlaceNodeHere()
    {
        // ★修正D: 最短経路モード中は常にNode設置不可扱い
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
    // TryExploreMove
    // ======================================================
    void TryExploreMove()
    {
        // ★安全弁: 念のため
        if (isFollowingShortest) return;

        currentNode = TryPlaceNode(transform.position);
        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

        var dirs = ScanAroundDirections();
        if (dirs.Count == 0)
        {
            if (debugLog) Debug.Log("[Player] No available directions");
            return;
        }

        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
        bool chooseUnexplored = Random.value < exploreBias;

        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
        var knownDirs2 = dirs.Where(d => d.node != null && d.hasLink).ToList();

        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

        if (isDeadEnd && unexploredDirs.Count > 0)
            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
        else
        {
            if (chooseUnexplored && unexploredDirs.Count > 0)
                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
            else if (knownDirs2.Count > 0)
                chosenDir = knownDirs2[Random.Range(0, knownDirs2.Count)];
            else if (unexploredDirs.Count > 0)
                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
        }

        if (chosenDir.HasValue)
        {
            moveDir = chosenDir.Value.dir;
            MoveForward();

            if (debugLog)
                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
        }
    }

    // ======================================================
    // ScanAroundDirections
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
    // FollowShortestPath
    // ======================================================
    //private IEnumerator FollowShortestPath()
    //{
    //    if (currentNode == null)
    //    {
    //        Debug.LogWarning("[FollowSP] currentNode is null → 経路追従不可");
    //        isFollowingShortest = false;
    //        yield break;
    //    }

    //    // 見た目（赤）
    //    if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
    //    Debug.Log($"[FollowSP] === Start === current={currentNode.name}, Dist={currentNode.DistanceFromGoal}");

    //    isFollowingShortest = true;
    //    int stepCount = 0;

    //    // GoalNodeまで距離がある限り進む
    //    while (currentNode != null && currentNode.DistanceFromGoal > 0)
    //    {
    //        stepCount++;
    //        int currentDist = currentNode.DistanceFromGoal;

    //        Debug.Log($"[FollowSP][Step#{stepCount}] current={currentNode.name}, dist={currentDist}, links={currentNode.links.Count}");
    //        string linkInfo = string.Join(", ", currentNode.links.Select(n => n ? $"{n.name}:{n.DistanceFromGoal}" : "null"));
    //        Debug.Log($"[FollowSP][Links] {linkInfo}");

    //        var nextNode = currentNode.links
    //            .Where(n => n != null && n.DistanceFromGoal < currentDist)
    //            .OrderBy(n => n.DistanceFromGoal)
    //            .FirstOrDefault();

    //        if (nextNode == null)
    //        {
    //            Debug.LogWarning($"[FollowSP][STOP] No closer link found (dist={currentDist}) → 経路終了");
    //            break;
    //        }

    //        Debug.Log($"[FollowSP][Move] {currentNode.name}({currentDist}) → {nextNode.name}({nextNode.DistanceFromGoal})");

    //        // リンク先Nodeへ直線移動
    //        Vector3 targetPos = nextNode.transform.position;
    //        Vector3 dir = (targetPos - transform.position); dir.y = 0f;
    //        moveDir = dir.normalized;

    //        while (Vector3.Distance(transform.position, targetPos) > 0.01f)
    //        {
    //            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
    //            yield return null;
    //        }

    //        // 到着：現在ノード差し替え＆スナップ
    //        currentNode = nextNode;
    //        transform.position = currentNode.transform.position;

    //        Debug.Log($"[FollowSP][Arrived] now={currentNode.name}, dist={currentNode.DistanceFromGoal}");

    //        // =============================
    //        // ★Goal到達時は MoveToTarget と同じ手順でDestroy
    //        // =============================
    //        // ・背面リンク確立（LinkBackWithRay）
    //        // ・Goal距離BFS（RecalculateGoalDistance）
    //        // ・全体フラグ hasLearnedGoal = true
    //        // ・Destroy(gameObject)
    //        // ※ goalNode 参照がある前提。なくても Distance==0 で動くように二重条件。
    //        if (currentNode.DistanceFromGoal == 0 ||
    //            (goalNode != null && currentNode == goalNode))
    //        {
    //            reachedGoal = true;
    //            Debug.Log($"[FollowSP] GOAL到達: node={currentNode.name} → link & destroy");

    //            if (currentNode != null)
    //                LinkBackWithRay(currentNode);             // ★同じ

    //            RecalculateGoalDistance();                      // ★同じ
    //            hasLearnedGoal = true;                          // ★同じ

    //            isFollowingShortest = false;                    // 保険
    //            Destroy(gameObject);                            // ★同じ
    //            yield break;                                    // ここで終了
    //        }
    //    }

    //    isFollowingShortest = false;
    //    Debug.Log("[FollowSP] === Exit shortest-path mode ===");
    //}
    private IEnumerator FollowShortestPath()
    {
        if (currentNode == null)
        {
            Debug.LogWarning("[FollowSP] currentNode is null → 経路追従不可");
            isFollowingShortest = false;
            yield break;
        }

        // 見た目（赤）
        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
        Debug.Log($"[FollowSP] === Start === current={currentNode.name}, Dist={currentNode.DistanceFromGoal}");

        isFollowingShortest = true;
        int stepCount = 0;

        // ★修正：float型Distanceに対応（EPSで誤差吸収）
        while (currentNode != null && currentNode.DistanceFromGoal > EPS)
        {
            stepCount++;

            // ★修正：int → float
            float currentDist = currentNode.DistanceFromGoal;

            Debug.Log($"[FollowSP][Step#{stepCount}] current={currentNode.name}, dist={currentDist}, links={currentNode.links.Count}");
            string linkInfo = string.Join(", ", currentNode.links.Select(n => n ? $"{n.name}:{n.DistanceFromGoal:F2}" : "null"));
            Debug.Log($"[FollowSP][Links] {linkInfo}");

            // ★修正：float比較（EPS分だけ小さいノードを選ぶ）
            var nextNode = currentNode.links
                .Where(n => n != null && n.DistanceFromGoal < currentDist - EPS)
                .OrderBy(n => n.DistanceFromGoal)
                .FirstOrDefault();

            if (nextNode == null)
            {
                Debug.LogWarning($"[FollowSP][STOP] No closer link found (dist={currentDist:F3}) → 経路終了");
                break;
            }

            Debug.Log($"[FollowSP][Move] {currentNode.name}({currentDist:F2}) → {nextNode.name}({nextNode.DistanceFromGoal:F2})");

            // リンク先Nodeへ直線移動
            Vector3 targetPos = nextNode.transform.position;
            Vector3 dir = (targetPos - transform.position);
            dir.y = 0f;
            moveDir = dir.normalized;

            while (Vector3.Distance(transform.position, targetPos) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
                yield return null;
            }

            // 到着：現在ノード更新＆スナップ
            currentNode = nextNode;
            transform.position = currentNode.transform.position;

            Debug.Log($"[FollowSP][Arrived] now={currentNode.name}, dist={currentNode.DistanceFromGoal:F3}");

            // ★修正：float距離比較でGoal判定
            if (currentNode.DistanceFromGoal <= EPS ||
                (goalNode != null && currentNode == goalNode))
            {
                reachedGoal = true;
                Debug.Log($"[FollowSP] GOAL到達: node={currentNode.name} → link & destroy");

                if (currentNode != null)
                    LinkBackWithRay(currentNode);  // MoveToTarget()と同じ処理

                RecalculateGoalDistance();         // 実距離ベースDijkstra
                hasLearnedGoal = true;

                isFollowingShortest = false;
                Destroy(gameObject);               // 同様に破棄
                yield break;
            }
        }

        isFollowingShortest = false;
        Debug.Log("[FollowSP] === Exit shortest-path mode ===");
    }


    // ======================================================
    // ■ Snapで該当ノードが見つからない場合のフォールバック
    //   プレイヤー位置に最も近い MapNode を返す（なければ null）
    // ======================================================
    private MapNode FindNearestNode(Vector3 pos)
    {
        float minDist = float.MaxValue;
        MapNode nearest = null;

        // シーン上の全 MapNode を走査（必要ならキャッシュ化も可）
        foreach (var node in FindObjectsOfType<MapNode>())
        {
            if (node == null) continue;
            float dist = Vector3.Distance(pos, node.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = node;
            }
        }

        return nearest;
    }

    // ======================================================
    // MoveToTarget
    // ======================================================
    void MoveToTarget()
    {
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
        {
            transform.position = targetPos;
            isMoving = false;

            if (debugLog)
                Debug.Log($"[MOVE][Arrived] pos={transform.position}");

            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
            MapNode node = MapNode.FindByCell(cell);
            currentNode = node;

            // ゴール判定
            if (!reachedGoal && goalNode != null)
            {
                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));

                if (playerCell == goalCell)
                {
                    reachedGoal = true;
                    //Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link & destroy");

                    if (currentNode != null)
                        LinkBackWithRay(currentNode);

                    RecalculateGoalDistance();
                    hasLearnedGoal = true; // 全体へ共有
                    //Debug.Log("[GLOBAL] Goal reached → all players now know the shortest path.");

                    Destroy(gameObject);
                    return;
                }
            }
        }
    }

    // ======================================================
    // LinkBackWithRay
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
                        if (debugLog)
                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
                    }
                    return;
                }
            }
        }
    }

    // ======================================================
    // RecalculateGoalDistance (BFS)
    // ======================================================
    //void RecalculateGoalDistance()
    //{
    //    if (goalNode == null) return;

    //    Queue<MapNode> queue = new Queue<MapNode>();
    //    foreach (var n in FindObjectsOfType<MapNode>())
    //        n.DistanceFromGoal = int.MaxValue;

    //    goalNode.DistanceFromGoal = 0;
    //    queue.Enqueue(goalNode);

    //    while (queue.Count > 0)
    //    {
    //        var node = queue.Dequeue();
    //        foreach (var link in node.links)
    //        {
    //            int newDist = node.DistanceFromGoal + 1;
    //            if (newDist < link.DistanceFromGoal)
    //            {
    //                link.DistanceFromGoal = newDist;
    //                queue.Enqueue(link);
    //            }
    //        }
    //    }

    //    //Debug.Log("[Player] Distance learning complete (Goal-based BFS)");
    //}
    void RecalculateGoalDistance()
    {
        if (goalNode == null) return;

        // 全ノード初期化
        foreach (var n in FindObjectsOfType<MapNode>())
            n.DistanceFromGoal = Mathf.Infinity;

        goalNode.DistanceFromGoal = 0f;
        var frontier = new List<MapNode> { goalNode };

        while (frontier.Count > 0)
        {
            // 距離が最小のノードを取り出す
            frontier.Sort((a, b) => a.DistanceFromGoal.CompareTo(b.DistanceFromGoal));
            var node = frontier[0];
            frontier.RemoveAt(0);

            // すべてのリンク先を評価
            foreach (var link in node.links)
            {
                if (link == null) continue;

                // ★変更：隣接ノード間の距離を実距離で加算
                float newDist = node.DistanceFromGoal + node.EdgeCost(link);

                if (newDist < link.DistanceFromGoal)
                {
                    link.DistanceFromGoal = newDist;
                    if (!frontier.Contains(link))
                        frontier.Add(link);
                }
            }
        }

        //Debug.Log("[Player] Distance learning complete (Goal-based Dijkstra)");
    }

    // ======================================================
    // TryPlaceNode
    // ======================================================
    MapNode TryPlaceNode(Vector3 pos)
    {
        // ★修正E: 最短経路モード中は新規Nodeを一切置かない（安全弁）
        if (isFollowingShortest)
        {
            // 既存があれば参照だけ返す。無ければnullのまま。
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
        {
            if (debugLog) Debug.Log($"[LINK] Check back connection for Node={node.name}");
            LinkBackWithRay(node);
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