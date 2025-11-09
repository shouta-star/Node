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

        // GoalNodeまで距離がある限り進む
        while (currentNode != null && currentNode.DistanceFromGoal > 0)
        {
            stepCount++;
            int currentDist = currentNode.DistanceFromGoal;

            Debug.Log($"[FollowSP][Step#{stepCount}] current={currentNode.name}, dist={currentDist}, links={currentNode.links.Count}");
            string linkInfo = string.Join(", ", currentNode.links.Select(n => n ? $"{n.name}:{n.DistanceFromGoal}" : "null"));
            Debug.Log($"[FollowSP][Links] {linkInfo}");

            var nextNode = currentNode.links
                .Where(n => n != null && n.DistanceFromGoal < currentDist)
                .OrderBy(n => n.DistanceFromGoal)
                .FirstOrDefault();

            if (nextNode == null)
            {
                Debug.LogWarning($"[FollowSP][STOP] No closer link found (dist={currentDist}) → 経路終了");
                break;
            }

            Debug.Log($"[FollowSP][Move] {currentNode.name}({currentDist}) → {nextNode.name}({nextNode.DistanceFromGoal})");

            // リンク先Nodeへ直線移動
            Vector3 targetPos = nextNode.transform.position;
            Vector3 dir = (targetPos - transform.position); dir.y = 0f;
            moveDir = dir.normalized;

            while (Vector3.Distance(transform.position, targetPos) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
                yield return null;
            }

            // 到着：現在ノード差し替え＆スナップ
            currentNode = nextNode;
            transform.position = currentNode.transform.position;

            Debug.Log($"[FollowSP][Arrived] now={currentNode.name}, dist={currentNode.DistanceFromGoal}");

            // =============================
            // ★Goal到達時は MoveToTarget と同じ手順でDestroy
            // =============================
            // ・背面リンク確立（LinkBackWithRay）
            // ・Goal距離BFS（RecalculateGoalDistance）
            // ・全体フラグ hasLearnedGoal = true
            // ・Destroy(gameObject)
            // ※ goalNode 参照がある前提。なくても Distance==0 で動くように二重条件。
            if (currentNode.DistanceFromGoal == 0 ||
                (goalNode != null && currentNode == goalNode))
            {
                reachedGoal = true;
                Debug.Log($"[FollowSP] GOAL到達: node={currentNode.name} → link & destroy");

                if (currentNode != null)
                    LinkBackWithRay(currentNode);             // ★同じ

                RecalculateGoalDistance();                      // ★同じ
                hasLearnedGoal = true;                          // ★同じ

                isFollowingShortest = false;                    // 保険
                Destroy(gameObject);                            // ★同じ
                yield break;                                    // ここで終了
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

        //Debug.Log("[Player] Distance learning complete (Goal-based BFS)");
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

//using UnityEngine;
//using System.Collections.Generic;
//using System.Linq;
//using System.Collections;

//public class Player : MonoBehaviour
//{
//    // ======================================================
//    // ■ フィールド宣言
//    // ======================================================

//    [Header("移動設定")]
//    public float moveSpeed = 3f;
//    public float cellSize = 1f;
//    public float rayDistance = 1f;
//    public LayerMask wallLayer;
//    public LayerMask nodeLayer;

//    [Header("初期設定")]
//    public Vector3 startDirection = Vector3.forward;
//    public Vector3 gridOrigin = Vector3.zero;
//    public MapNode goalNode;
//    public GameObject nodePrefab;

//    [Header("行動傾向")]
//    [Range(0f, 1f)] public float exploreBias = 0.6f;

//    [Header("リンク探索")]
//    public int linkRayMaxSteps = 100;

//    [Header("デバッグ")]
//    public bool debugLog = true;
//    public bool debugRay = true;
//    [SerializeField] private Renderer bodyRenderer;
//    [SerializeField] private Material exploreMaterial;

//    public static bool hasLearnedGoal = false; // ★全プレイヤー共通（Goal学習完了フラグ）

//    // 内部状態
//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;
//    private MapNode currentNode;
//    private bool reachedGoal = false;

//    // ======================================================
//    // ■ Start() : 初期化処理
//    // ======================================================
//    void Start()
//    {
//        moveDir = startDirection.normalized;
//        targetPos = transform.position = SnapToGrid(transform.position);
//        ApplyVisual();
//        currentNode = TryPlaceNode(transform.position);

//        if (goalNode == null)
//        {
//            GameObject goalObj = GameObject.Find("Goal");
//            if (goalObj != null)
//            {
//                goalNode = goalObj.GetComponent<MapNode>();
//                Debug.Log($"[Player] GoalNode assigned from Scene object: {goalNode.name}");
//            }
//            else
//            {
//                Debug.LogWarning("[Player] Goal object not found in scene!");
//            }
//        }

//        // ======================================================
//        // ★修正①：Goal発見済みなら生成時に一度だけモード判定
//        // ======================================================
//        if (hasLearnedGoal)
//        {
//            if (Random.value < 0.5f)
//            {
//                Debug.Log($"[Player:{name}] Spawned as shortest-path follower");
//                StopAllCoroutines();
//                StartCoroutine(FollowShortestPath());
//                return;
//            }
//            else
//            {
//                Debug.Log($"[Player:{name}] Spawned as explorer (continue exploring)");
//            }
//        }

//        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
//    }

//    // ======================================================
//    // ■ Update() : 毎フレーム呼ばれるメインループ
//    // ======================================================
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

//    // ======================================================
//    // ■ ApplyVisual()
//    // ======================================================
//    private void ApplyVisual()
//    {
//        if (bodyRenderer == null) return;
//        bodyRenderer.material = exploreMaterial
//            ? exploreMaterial
//            : new Material(Shader.Find("Standard")) { color = Color.cyan };
//    }

//    // ======================================================
//    // ■ MoveForward()
//    // ======================================================
//    void MoveForward()
//    {
//        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
//        targetPos = nextPos;
//        isMoving = true;
//    }

//    // ======================================================
//    // ■ CanPlaceNodeHere()
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
//    // ■ TryExploreMove()
//    // ======================================================
//    void TryExploreMove()
//    {
//        currentNode = TryPlaceNode(transform.position);
//        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

//        // ======================================================
//        // ★修正②：ここで hasLearnedGoal 判定は削除（以前はここで呼ばれていた）
//        // ======================================================

//        var dirs = ScanAroundDirections();
//        if (dirs.Count == 0)
//        {
//            if (debugLog) Debug.Log("[Player] No available directions");
//            return;
//        }

//        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
//        bool chooseUnexplored = Random.value < exploreBias;

//        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
//        var knownDirs2 = dirs.Where(d => d.node != null && d.hasLink).ToList();

//        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

//        if (isDeadEnd && unexploredDirs.Count > 0)
//        {
//            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
//        }
//        else
//        {
//            if (chooseUnexplored && unexploredDirs.Count > 0)
//                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
//            else if (knownDirs2.Count > 0)
//                chosenDir = knownDirs2[Random.Range(0, knownDirs2.Count)];
//            else if (unexploredDirs.Count > 0)
//                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
//        }

//        if (chosenDir.HasValue)
//        {
//            moveDir = chosenDir.Value.dir;
//            MoveForward();

//            if (debugLog)
//                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
//        }
//    }

//    // ======================================================
//    // ■ ScanAroundDirections()
//    // ======================================================
//    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
//    {
//        List<(Vector3, MapNode, bool)> found = new();
//        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//        foreach (var dir in dirs)
//        {
//            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
//                continue;

//            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
//            Vector2Int nextCell = WorldToCell(nextPos);
//            MapNode nextNode = MapNode.FindByCell(nextCell);
//            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));
//            found.Add((dir, nextNode, linked));
//        }
//        return found;
//    }

//    // ======================================================
//    // ■ FollowShortestPath()
//    // ======================================================
//    private IEnumerator FollowShortestPath()
//    {
//        if (currentNode == null)
//        {
//            Debug.LogWarning("[Player] Cannot follow shortest path: currentNode is null");
//            yield break;
//        }

//        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
//        Debug.Log("[Player] Start following shortest path");

//        while (currentNode != null && currentNode.DistanceFromGoal > 0)
//        {
//            int currentDist = currentNode.DistanceFromGoal;
//            var closer = currentNode.links
//                .Where(n => n != null && n.DistanceFromGoal < currentDist)
//                .OrderBy(n => n.DistanceFromGoal)
//                .FirstOrDefault();

//            if (closer == null)
//            {
//                Debug.Log($"[Player] No closer linked node from {currentDist} → stop");
//                yield break;
//            }

//            Vector3 dir = (closer.transform.position - transform.position);
//            dir.y = 0f;
//            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.z))
//                dir = new Vector3(Mathf.Sign(dir.x), 0, 0);
//            else
//                dir = new Vector3(0, 0, Mathf.Sign(dir.z));

//            moveDir = dir.normalized;
//            MoveForward();

//            while (isMoving) yield return null;

//            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
//            currentNode = MapNode.FindByCell(cell);
//            if (currentNode == null)
//            {
//                Debug.LogWarning("[Player] Lost current node while following path");
//                yield break;
//            }

//            Debug.Log($"[Player] Step: {currentDist} → {currentNode.DistanceFromGoal}");
//        }

//        Debug.Log("[Player] Reached GoalNode via shortest path!");
//    }

//    // ======================================================
//    // ■ MoveToTarget()
//    // ======================================================
//    void MoveToTarget()
//    {
//        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//        {
//            transform.position = targetPos;
//            isMoving = false;

//            if (debugLog)
//                Debug.Log($"[MOVE][Arrived] pos={transform.position}");

//            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
//            MapNode node = MapNode.FindByCell(cell);
//            currentNode = node;

//            // ======================================================
//            // ★修正③：Goal到達処理（staticフラグ更新のみ）
//            // ======================================================
//            if (!reachedGoal && goalNode != null)
//            {
//                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
//                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));

//                if (playerCell == goalCell)
//                {
//                    reachedGoal = true;
//                    Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link & destroy");

//                    if (currentNode != null)
//                        LinkBackWithRay(currentNode);

//                    RecalculateGoalDistance();
//                    hasLearnedGoal = true; // ★全プレイヤーへ通知
//                    Debug.Log("[GLOBAL] Goal reached → all players now know the shortest path.");

//                    Destroy(gameObject);
//                    return;
//                }
//            }
//        }
//    }

//    // ======================================================
//    // ■ LinkBackWithRay() / RecalculateGoalDistance() / TryPlaceNode() / 座標変換系
//    // ======================================================
//    private void LinkBackWithRay(MapNode node)
//    {
//        if (node == null) return;

//        // --- Nodeの情報取得 ---
//        Vector3 nodePos = node.transform.position;
//        Quaternion nodeRot = node.transform.rotation;
//        Vector3 nodeScale = node.transform.localScale;

//        // --- レイキャスト設定 ---
//        Vector3 origin = nodePos + Vector3.up * 0.1f;  // Nodeの少し上から発射
//        Vector3 rawBack = -moveDir;                    // 進行方向の逆向き
//        Vector3 backDir = rawBack.normalized;          // 正規化方向ベクトル
//        LayerMask mask = wallLayer | nodeLayer;        // 衝突対象は壁＋Node

//        // --- デバッグ出力 ---
//        //Debug.Log(
//        //    $"[NODE-RAY][LinkBack] node={node.name} pos={nodePos} " +
//        //    $"origin={origin} dir(back)={backDir} " +
//        //    $"cellSize={cellSize:F3} maxSteps={linkRayMaxSteps}"
//        //);

//        // --- レイを段階的に伸ばしてNodeを探索 ---
//        for (int step = 1; step <= linkRayMaxSteps; step++)
//        {
//            float maxDist = cellSize * step;
//            if (debugRay)
//                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

//            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
//            {
//                int hitLayer = hit.collider.gameObject.layer;
//                string layerName = LayerMask.LayerToName(hitLayer);
//                Debug.Log($"[RAY-HIT][LinkBack] step={step} dist={hit.distance:F3} hit={hit.collider.name} layer={layerName}");

//                // 壁に当たったら中断
//                if ((wallLayer.value & (1 << hitLayer)) != 0)
//                {
//                    if (debugLog) Debug.Log($"[LINK-BLOCK] Wall hit first (hit={hit.collider.name})");
//                    return;
//                }

//                // Nodeに当たったらリンク確立
//                if ((nodeLayer.value & (1 << hitLayer)) != 0)
//                {
//                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
//                    if (hitNode != null && hitNode != node)
//                    {
//                        node.AddLink(hitNode);
//                        if (debugLog)
//                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name} (dist={hit.distance:F2})");
//                    }
//                    return;
//                }
//            }
//        }

//        Debug.Log($"[LINK-NONE] node={node.name} no Node found behind (maxSteps={linkRayMaxSteps})");
//    }

//    // ======================================================
//    // ■ RecalculateGoalDistance() : Goalから全Nodeの距離を再計算（BFS）
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
//    // ■ TryPlaceNode() : Nodeを新規 or 既存で設置し、背面リンクを実行
//    // ======================================================
//    MapNode TryPlaceNode(Vector3 pos)
//    {
//        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//        MapNode node;

//        // 既に存在するNodeを再利用
//        if (MapNode.allNodeCells.Contains(cell))
//        {
//            node = MapNode.FindByCell(cell);
//            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
//        }
//        else
//        {
//            // 新規Node生成
//            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//            node = obj.GetComponent<MapNode>();
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
//        }

//        // Node確定後：常に背面リンクを実行（既存Nodeでも新方向接続を確認）
//        if (node != null)
//        {
//            if (debugLog) Debug.Log($"[LINK] Check back connection for Node={node.name}");
//            LinkBackWithRay(node);
//        }

//        return node;
//    }

//    // ======================================================
//    // ■ 座標変換系ユーティリティ
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
////    // ======================================================
////    // ■ フィールド宣言
////    // ======================================================

////    [Header("移動設定")]
////    public float moveSpeed = 3f;         // プレイヤーの移動速度
////    public float cellSize = 1f;          // 1マスあたりの距離（グリッド間隔）
////    public float rayDistance = 1f;       // 分岐判定などで使う短距離レイの距離
////    public LayerMask wallLayer;          // 壁のレイヤー
////    public LayerMask nodeLayer;          // Nodeのレイヤー

////    [Header("初期設定")]
////    public Vector3 startDirection = Vector3.forward;   // 開始時の進行方向
////    public Vector3 gridOrigin = Vector3.zero;          // グリッドの原点座標
////    public MapNode goalNode;                           // 目標となるGoal Node
////    public GameObject nodePrefab;                      // Nodeのプレハブ参照

////    [Header("行動傾向")]
////    [Range(0f, 1f)] public float exploreBias = 0.6f;   // 未探索方向を選ぶ確率（探索傾向）

////    [Header("リンク探索")]
////    public int linkRayMaxSteps = 100;   // Node間リンク探索での最大距離ステップ（セル単位）

////    [Header("デバッグ")]
////    public bool debugLog = true;        // コンソール出力ON/OFF
////    public bool debugRay = true;        // レイをSceneビューに描画するか
////    [SerializeField] private Renderer bodyRenderer;
////    [SerializeField] private Material exploreMaterial;

////    public static bool hasLearnedGoal = false; // 全プレイヤー共通の学習完了フラグ

////    // 内部状態
////    private Vector3 moveDir;            // 現在の進行方向
////    private bool isMoving = false;      // 移動中フラグ
////    private Vector3 targetPos;          // 現在の移動目標地点
////    private MapNode currentNode;        // 現在立っているNode
////    private bool reachedGoal = false;   // ゴール到達フラグ

////    private Queue<MapNode> recentNodes = new Queue<MapNode>();
////    private int recentLimit = 8;        // 直近訪問履歴の上限（※今は未使用）

////    // ======================================================
////    // ■ Start() : 初期化処理
////    // ======================================================
////    void Start()
////    {
////        // 初期方向と位置のスナップ
////        moveDir = startDirection.normalized;
////        targetPos = transform.position = SnapToGrid(transform.position);

////        // 見た目初期化（探索中カラーなど）
////        ApplyVisual();

////        // 開始地点にNodeを設置 or 取得
////        currentNode = TryPlaceNode(transform.position);

////        if (goalNode == null)
////        {
////            // Goalという名前のオブジェクトを直接探す（Scene上の実体）
////            GameObject goalObj = GameObject.Find("Goal");
////            if (goalObj != null)
////            {
////                goalNode = goalObj.GetComponent<MapNode>();
////                Debug.Log($"[Player] GoalNode assigned from Scene object: {goalNode.name}");
////            }
////            else
////            {
////                Debug.LogWarning("[Player] Goal object not found in scene!");
////            }
////        }

////        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
////    }

////    // ======================================================
////    // ■ Update() : 毎フレーム呼ばれるメインループ
////    // ======================================================
////    void Update()
////    {
////        if (!isMoving)
////        {
////            // 移動していない時：分岐点または前方が壁ならNode設置・探索へ
////            if (CanPlaceNodeHere())
////                TryExploreMove();
////            else
////                MoveForward(); // 通路なら前進
////        }
////        else
////        {
////            // 移動中：目標座標に向けて移動処理
////            MoveToTarget();
////        }
////    }

////    // ======================================================
////    // ■ ApplyVisual() : プレイヤーの見た目設定
////    // ======================================================
////    private void ApplyVisual()
////    {
////        if (bodyRenderer == null) return;
////        bodyRenderer.material = exploreMaterial
////            ? exploreMaterial
////            : new Material(Shader.Find("Standard")) { color = Color.cyan };
////    }

////    // ======================================================
////    // ■ MoveForward() : 現在の方向に1マス分前進をセット
////    // ======================================================
////    void MoveForward()
////    {
////        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
////        targetPos = nextPos;
////        isMoving = true;
////    }

////    // ======================================================
////    // ■ CanPlaceNodeHere() : Node設置可能か（分岐点 or 壁前）を判定
////    // ======================================================
////    bool CanPlaceNodeHere()
////    {
////        // 左右と前方の壁をチェックして分岐判定
////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
////        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
////        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

////        int openCount = 0;
////        if (!frontHit) openCount++;
////        if (!leftHit) openCount++;
////        if (!rightHit) openCount++;

////        // 前が壁 or 分岐方向が2つ以上ならNode設置対象
////        return (frontHit || openCount >= 2);
////    }

////    // ======================================================
////    // ■ TryExploreMove() : Node設置＋次の進行方向を決定
////    // ======================================================
////    void TryExploreMove()
////    {
////        // 現在地にNodeを設置 or 再取得
////        currentNode = TryPlaceNode(transform.position);
////        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

////        // 進める方向候補を調べる
////        var dirs = ScanAroundDirections();
////        if (dirs.Count == 0)
////        {
////            if (debugLog) Debug.Log("[Player] No available directions");
////            return;
////        }

////        // =====================================================
////        // 【③ 最短経路フェーズ】GoalNodeに一度でも到達した後（全体共有）
////        // =====================================================
////        //if (hasLearnedGoal && currentNode != null)
////        //{
////        //    // すでに別の最短経路コルーチンが動作中ならスキップ
////        //    StopAllCoroutines();

////        //    // Goalまで自動で距離が減少する方向へ進む
////        //    StartCoroutine(FollowShortestPath());
////        //    return;
////        //}
////        if (hasLearnedGoal && currentNode != null)
////        {
////            // 50%の確率で最短経路モードへ
////            //if (Random.value < 0.1f)
////            {
////                // すでに別の最短経路コルーチンが動作中ならスキップ
////                StopAllCoroutines();

////                // Goalまで自動で距離が減少する方向へ進む
////                StartCoroutine(FollowShortestPath());

////                if (debugLog)
////                    Debug.Log("[Player] Switch to shortest-path mode");

////                return;
////            }
////            //else
////            //{
////            //    // 残り半分は通常探索モードのまま動作
////            //    if (debugLog)
////            //        Debug.Log("[Player] Continue explore mode (did not switch)");
////            //    // ※returnしないので、この後の探索ロジックが実行される
////            //}
////        }

////        // =====================================================
////        // 【① 探索フェーズ】GoalNode未発見時
////        // =====================================================
////        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
////        bool chooseUnexplored = Random.value < exploreBias;

////        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
////        var knownDirs2 = dirs.Where(d => d.node != null && d.hasLink).ToList();

////        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

////        // 終端なら未知方向優先
////        if (isDeadEnd && unexploredDirs.Count > 0)
////        {
////            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////        }
////        else
////        {
////            if (chooseUnexplored && unexploredDirs.Count > 0)
////                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////            else if (knownDirs2.Count > 0)
////                chosenDir = knownDirs2[Random.Range(0, knownDirs2.Count)];
////            else if (unexploredDirs.Count > 0)
////                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////        }

////        // --- 実際に前進 ---
////        if (chosenDir.HasValue)
////        {
////            moveDir = chosenDir.Value.dir;
////            MoveForward();

////            if (debugLog)
////                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
////        }
////    }

////    // ======================================================
////    // ■ FollowShortestPath() : GoalNodeまで最短経路を自動で辿る
////    // ======================================================
////    private System.Collections.IEnumerator FollowShortestPath()
////    {
////        if (currentNode == null)
////        {
////            Debug.LogWarning("[Player] Cannot follow shortest path: currentNode is null");
////            yield break;
////        }

////        // 見た目：最短経路モード（赤）
////        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;

////        Debug.Log("[Player] Start following shortest path");

////        while (currentNode != null && currentNode.DistanceFromGoal > 0)
////        {
////            int currentDist = currentNode.DistanceFromGoal;

////            // ★ 物理スキャンではなく、リンクされたノードから選ぶ
////            var closer = currentNode.links
////                .Where(n => n != null && n.DistanceFromGoal < currentDist)
////                .OrderBy(n => n.DistanceFromGoal)
////                .FirstOrDefault();

////            if (closer == null)
////            {
////                Debug.Log($"[Player] No closer linked node from {currentDist} → stop");
////                yield break; // 近づけない＝リンク切れ
////            }

////            // 次ノード方向へ1マス進む（グリッド向けに正規化）
////            Vector3 dir = (closer.transform.position - transform.position);
////            dir.y = 0f;
////            // 方向をグリッド軸にスナップ（誤差対策）
////            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.z)) dir = new Vector3(Mathf.Sign(dir.x), 0, 0);
////            else dir = new Vector3(0, 0, Mathf.Sign(dir.z));

////            moveDir = dir.normalized;
////            MoveForward();

////            // 到着を待つ
////            while (isMoving) yield return null;

////            // 現在ノード更新（今いるセルのMapNode）
////            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////            currentNode = MapNode.FindByCell(cell);

////            if (currentNode == null)
////            {
////                Debug.LogWarning("[Player] Lost current node while following path");
////                yield break;
////            }

////            Debug.Log($"[Player] Step: {currentDist} → {currentNode.DistanceFromGoal}");
////        }

////        Debug.Log("[Player] Reached GoalNode via shortest path!");
////    }

////    // ======================================================
////    // ■ ScanAroundDirections() : 周囲4方向のNode状況を取得
////    // ======================================================
////    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
////    {
////        List<(Vector3, MapNode, bool)> found = new();
////        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

////        foreach (var dir in dirs)
////        {
////            // 壁があればその方向は除外
////            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
////                continue;

////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////            Vector2Int nextCell = WorldToCell(nextPos);

////            // 隣のセルにNodeがあるか調べる
////            MapNode nextNode = MapNode.FindByCell(nextCell);
////            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));

////            found.Add((dir, nextNode, linked));
////        }

////        return found;
////    }

////    // ======================================================
////    // ■ MoveToTarget() : 通路移動処理（リンクは行わない）
////    // ======================================================
////    void MoveToTarget()
////    {
////        // 現在位置からtargetPosへ向かって移動
////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

////        // 到達判定
////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////        {
////            transform.position = targetPos;
////            isMoving = false;

////            if (debugLog)
////                Debug.Log($"[MOVE][Arrived] pos={transform.position}");

////            // 現在セルにNodeが存在すれば更新
////            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////            MapNode node = MapNode.FindByCell(cell);
////            currentNode = node;

////            // ゴール判定（goalNodeの座標に到達したらDestroy）
////            if (!reachedGoal && goalNode != null)
////            {
////                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
////                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));

////                //if (playerCell == goalCell)
////                //{
////                //    reachedGoal = true;
////                //    Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link by ray & destroy");

////                //    // ✅ 通常の隣接ノード接続と同じ処理を行う
////                //    if (currentNode != null)
////                //        LinkBackWithRay(currentNode);

////                //    // ゴール距離学習
////                //    RecalculateGoalDistance();

////                //    // Player削除
////                //    Destroy(gameObject);
////                //    return;
////                //}
////                if (playerCell == goalCell)
////                {
////                    reachedGoal = true;
////                    Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link by ray & destroy");

////                    // ✅ 通常の隣接ノード接続
////                    if (currentNode != null)
////                        LinkBackWithRay(currentNode);

////                    // ✅ ゴール距離学習
////                    RecalculateGoalDistance();

////                    // ✅ 全プレイヤーへ「学習完了」を通知
////                    hasLearnedGoal = true;
////                    Debug.Log("[GLOBAL] Goal reached → all players now know the shortest path.");

////                    // Player削除
////                    Destroy(gameObject);
////                    return;
////                }
////                //if (playerCell == goalCell)
////                //{
////                //    reachedGoal = true;
////                //    Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link by ray & destroy");

////                //    // 通常の隣接ノード接続
////                //    if (currentNode != null)
////                //        LinkBackWithRay(currentNode);

////                //    // ゴール距離学習
////                //    RecalculateGoalDistance();

////                //    // 全プレイヤー共通フラグを立てる
////                //    hasLearnedGoal = true;
////                //    Debug.Log("[GLOBAL] Goal reached → all players now know the shortest path.");

////                //    // ✅ 50%の確率でこの到達個体だけ最短経路モードを発動
////                //    if (Random.value < 0.5f)
////                //    {
////                //        Debug.Log("[Player] 50% chance succeeded → Start FollowShortestPath() before destroy");
////                //        StopAllCoroutines();
////                //        StartCoroutine(FollowShortestPath());

////                //        // Destroyを遅らせてコルーチンを動かす（1秒後に破棄など）
////                //        Destroy(gameObject, 1.0f);
////                //    }
////                //    else
////                //    {
////                //        Debug.Log("[Player] 50% chance failed → no auto path follow");
////                //        Destroy(gameObject);
////                //    }

////                //    return;
////                //}


////            }
////        }
////    }
////    //using UnityEngine;
////    //using System.Collections.Generic;
////    //using System.Linq;
////    //using System.Collections;

////    //public class Player : MonoBehaviour
////    //{
////    //    // ======================================================
////    //    // ■ フィールド宣言
////    //    // ======================================================

////    //    [Header("移動設定")]
////    //    public float moveSpeed = 3f;
////    //    public float cellSize = 1f;
////    //    public float rayDistance = 1f;
////    //    public LayerMask wallLayer;
////    //    public LayerMask nodeLayer;

////    //    [Header("初期設定")]
////    //    public Vector3 startDirection = Vector3.forward;
////    //    public Vector3 gridOrigin = Vector3.zero;
////    //    public MapNode goalNode;
////    //    public GameObject nodePrefab;

////    //    [Header("行動傾向")]
////    //    [Range(0f, 1f)] public float exploreBias = 0.6f;

////    //    [Header("リンク探索")]
////    //    public int linkRayMaxSteps = 100;

////    //    [Header("デバッグ")]
////    //    public bool debugLog = true;
////    //    public bool debugRay = true;
////    //    [SerializeField] private Renderer bodyRenderer;
////    //    [SerializeField] private Material exploreMaterial;

////    //    public static bool hasLearnedGoal = false; // ★全プレイヤー共通（Goal学習完了フラグ）

////    //    // 内部状態
////    //    private Vector3 moveDir;
////    //    private bool isMoving = false;
////    //    private Vector3 targetPos;
////    //    private MapNode currentNode;
////    //    private bool reachedGoal = false;

////    //    // ======================================================
////    //    // ■ Start() : 初期化処理
////    //    // ======================================================
////    //    void Start()
////    //    {
////    //        moveDir = startDirection.normalized;
////    //        targetPos = transform.position = SnapToGrid(transform.position);
////    //        ApplyVisual();
////    //        currentNode = TryPlaceNode(transform.position);

////    //        if (goalNode == null)
////    //        {
////    //            GameObject goalObj = GameObject.Find("Goal");
////    //            if (goalObj != null)
////    //            {
////    //                goalNode = goalObj.GetComponent<MapNode>();
////    //                Debug.Log($"[Player] GoalNode assigned from Scene object: {goalNode.name}");
////    //            }
////    //            else
////    //            {
////    //                Debug.LogWarning("[Player] Goal object not found in scene!");
////    //            }
////    //        }

////    //        // ======================================================
////    //        // ★修正①：Goal発見済みなら生成時に一度だけモード判定
////    //        // ======================================================
////    //        if (hasLearnedGoal)
////    //        {
////    //            if (Random.value < 0.5f)
////    //            {
////    //                Debug.Log($"[Player:{name}] Spawned as shortest-path follower");
////    //                StopAllCoroutines();
////    //                StartCoroutine(FollowShortestPath());
////    //                return;
////    //            }
////    //            else
////    //            {
////    //                Debug.Log($"[Player:{name}] Spawned as explorer (continue exploring)");
////    //            }
////    //        }

////    //        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
////    //    }

////    //    // ======================================================
////    //    // ■ Update() : 毎フレーム呼ばれるメインループ
////    //    // ======================================================
////    //    void Update()
////    //    {
////    //        if (!isMoving)
////    //        {
////    //            if (CanPlaceNodeHere())
////    //                TryExploreMove();
////    //            else
////    //                MoveForward();
////    //        }
////    //        else
////    //        {
////    //            MoveToTarget();
////    //        }
////    //    }

////    //    // ======================================================
////    //    // ■ ApplyVisual()
////    //    // ======================================================
////    //    private void ApplyVisual()
////    //    {
////    //        if (bodyRenderer == null) return;
////    //        bodyRenderer.material = exploreMaterial
////    //            ? exploreMaterial
////    //            : new Material(Shader.Find("Standard")) { color = Color.cyan };
////    //    }

////    //    // ======================================================
////    //    // ■ MoveForward()
////    //    // ======================================================
////    //    void MoveForward()
////    //    {
////    //        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
////    //        targetPos = nextPos;
////    //        isMoving = true;
////    //    }

////    //    // ======================================================
////    //    // ■ CanPlaceNodeHere()
////    //    // ======================================================
////    //    bool CanPlaceNodeHere()
////    //    {
////    //        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////    //        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////    //        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
////    //        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
////    //        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

////    //        int openCount = 0;
////    //        if (!frontHit) openCount++;
////    //        if (!leftHit) openCount++;
////    //        if (!rightHit) openCount++;

////    //        return (frontHit || openCount >= 2);
////    //    }

////    //    // ======================================================
////    //    // ■ TryExploreMove()
////    //    // ======================================================
////    //    void TryExploreMove()
////    //    {
////    //        currentNode = TryPlaceNode(transform.position);
////    //        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

////    //        // ======================================================
////    //        // ★修正②：ここで hasLearnedGoal 判定は削除（以前はここで呼ばれていた）
////    //        // ======================================================

////    //        var dirs = ScanAroundDirections();
////    //        if (dirs.Count == 0)
////    //        {
////    //            if (debugLog) Debug.Log("[Player] No available directions");
////    //            return;
////    //        }

////    //        bool isDeadEnd = (currentNode == null || currentNode.links.Count <= 1);
////    //        bool chooseUnexplored = Random.value < exploreBias;

////    //        var unexploredDirs = dirs.Where(d => d.node == null || !d.hasLink).ToList();
////    //        var knownDirs2 = dirs.Where(d => d.node != null && d.hasLink).ToList();

////    //        (Vector3 dir, MapNode node, bool hasLink)? chosenDir = null;

////    //        if (isDeadEnd && unexploredDirs.Count > 0)
////    //        {
////    //            chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////    //        }
////    //        else
////    //        {
////    //            if (chooseUnexplored && unexploredDirs.Count > 0)
////    //                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////    //            else if (knownDirs2.Count > 0)
////    //                chosenDir = knownDirs2[Random.Range(0, knownDirs2.Count)];
////    //            else if (unexploredDirs.Count > 0)
////    //                chosenDir = unexploredDirs[Random.Range(0, unexploredDirs.Count)];
////    //        }

////    //        if (chosenDir.HasValue)
////    //        {
////    //            moveDir = chosenDir.Value.dir;
////    //            MoveForward();

////    //            if (debugLog)
////    //                Debug.Log($"[Player] Move {(chooseUnexplored ? "Unexplored" : "Known")} → {chosenDir.Value.dir}");
////    //        }
////    //    }

////    //    // ======================================================
////    //    // ■ ScanAroundDirections()
////    //    // ======================================================
////    //    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
////    //    {
////    //        List<(Vector3, MapNode, bool)> found = new();
////    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

////    //        foreach (var dir in dirs)
////    //        {
////    //            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
////    //                continue;

////    //            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////    //            Vector2Int nextCell = WorldToCell(nextPos);
////    //            MapNode nextNode = MapNode.FindByCell(nextCell);
////    //            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));
////    //            found.Add((dir, nextNode, linked));
////    //        }
////    //        return found;
////    //    }

////    //    // ======================================================
////    //    // ■ FollowShortestPath()
////    //    // ======================================================
////    //    private IEnumerator FollowShortestPath()
////    //    {
////    //        if (currentNode == null)
////    //        {
////    //            Debug.LogWarning("[Player] Cannot follow shortest path: currentNode is null");
////    //            yield break;
////    //        }

////    //        if (bodyRenderer != null) bodyRenderer.material.color = Color.red;
////    //        Debug.Log("[Player] Start following shortest path");

////    //        while (currentNode != null && currentNode.DistanceFromGoal > 0)
////    //        {
////    //            int currentDist = currentNode.DistanceFromGoal;
////    //            var closer = currentNode.links
////    //                .Where(n => n != null && n.DistanceFromGoal < currentDist)
////    //                .OrderBy(n => n.DistanceFromGoal)
////    //                .FirstOrDefault();

////    //            if (closer == null)
////    //            {
////    //                Debug.Log($"[Player] No closer linked node from {currentDist} → stop");
////    //                yield break;
////    //            }

////    //            Vector3 dir = (closer.transform.position - transform.position);
////    //            dir.y = 0f;
////    //            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.z))
////    //                dir = new Vector3(Mathf.Sign(dir.x), 0, 0);
////    //            else
////    //                dir = new Vector3(0, 0, Mathf.Sign(dir.z));

////    //            moveDir = dir.normalized;
////    //            MoveForward();

////    //            while (isMoving) yield return null;

////    //            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////    //            currentNode = MapNode.FindByCell(cell);
////    //            if (currentNode == null)
////    //            {
////    //                Debug.LogWarning("[Player] Lost current node while following path");
////    //                yield break;
////    //            }

////    //            Debug.Log($"[Player] Step: {currentDist} → {currentNode.DistanceFromGoal}");
////    //        }

////    //        Debug.Log("[Player] Reached GoalNode via shortest path!");
////    //    }

////    //    // ======================================================
////    //    // ■ MoveToTarget()
////    //    // ======================================================
////    //    void MoveToTarget()
////    //    {
////    //        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////    //        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////    //        {
////    //            transform.position = targetPos;
////    //            isMoving = false;

////    //            if (debugLog)
////    //                Debug.Log($"[MOVE][Arrived] pos={transform.position}");

////    //            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////    //            MapNode node = MapNode.FindByCell(cell);
////    //            currentNode = node;

////    //            // ======================================================
////    //            // ★修正③：Goal到達処理（staticフラグ更新のみ）
////    //            // ======================================================
////    //            if (!reachedGoal && goalNode != null)
////    //            {
////    //                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
////    //                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));

////    //                if (playerCell == goalCell)
////    //                {
////    //                    reachedGoal = true;
////    //                    Debug.Log($"[Player:{name}] Reached GOAL(cell={goalCell}) → link & destroy");

////    //                    if (currentNode != null)
////    //                        LinkBackWithRay(currentNode);

////    //                    RecalculateGoalDistance();
////    //                    hasLearnedGoal = true; // ★全プレイヤーへ通知
////    //                    Debug.Log("[GLOBAL] Goal reached → all players now know the shortest path.");

////    //                    Destroy(gameObject);
////    //                    return;
////    //                }
////    //            }
////    //        }
////    //    }
////    // ======================================================
////    // ■ LinkBackWithRay() : Node背面へのレイキャストで接続確認
////    // ======================================================
////    private void LinkBackWithRay(MapNode node)
////    {
////        if (node == null) return;

////        // --- Nodeの情報取得 ---
////        Vector3 nodePos = node.transform.position;
////        Quaternion nodeRot = node.transform.rotation;
////        Vector3 nodeScale = node.transform.localScale;

////        // --- レイキャスト設定 ---
////        Vector3 origin = nodePos + Vector3.up * 0.1f;  // Nodeの少し上から発射
////        Vector3 rawBack = -moveDir;                    // 進行方向の逆向き
////        Vector3 backDir = rawBack.normalized;          // 正規化方向ベクトル
////        LayerMask mask = wallLayer | nodeLayer;        // 衝突対象は壁＋Node

////        // --- デバッグ出力 ---
////        //Debug.Log(
////        //    $"[NODE-RAY][LinkBack] node={node.name} pos={nodePos} " +
////        //    $"origin={origin} dir(back)={backDir} " +
////        //    $"cellSize={cellSize:F3} maxSteps={linkRayMaxSteps}"
////        //);

////        // --- レイを段階的に伸ばしてNodeを探索 ---
////        for (int step = 1; step <= linkRayMaxSteps; step++)
////        {
////            float maxDist = cellSize * step;
////            if (debugRay)
////                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

////            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
////            {
////                int hitLayer = hit.collider.gameObject.layer;
////                string layerName = LayerMask.LayerToName(hitLayer);
////                Debug.Log($"[RAY-HIT][LinkBack] step={step} dist={hit.distance:F3} hit={hit.collider.name} layer={layerName}");

////                // 壁に当たったら中断
////                if ((wallLayer.value & (1 << hitLayer)) != 0)
////                {
////                    if (debugLog) Debug.Log($"[LINK-BLOCK] Wall hit first (hit={hit.collider.name})");
////                    return;
////                }

////                // Nodeに当たったらリンク確立
////                if ((nodeLayer.value & (1 << hitLayer)) != 0)
////                {
////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
////                    if (hitNode != null && hitNode != node)
////                    {
////                        node.AddLink(hitNode);
////                        if (debugLog)
////                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name} (dist={hit.distance:F2})");
////                    }
////                    return;
////                }
////            }
////        }

////        Debug.Log($"[LINK-NONE] node={node.name} no Node found behind (maxSteps={linkRayMaxSteps})");
////    }

////    // ======================================================
////    // ■ RecalculateGoalDistance() : Goalから全Nodeの距離を再計算（BFS）
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
////    // ■ TryPlaceNode() : Nodeを新規 or 既存で設置し、背面リンクを実行
////    // ======================================================
////    MapNode TryPlaceNode(Vector3 pos)
////    {
////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////        MapNode node;

////        // 既に存在するNodeを再利用
////        if (MapNode.allNodeCells.Contains(cell))
////        {
////            node = MapNode.FindByCell(cell);
////            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
////        }
////        else
////        {
////            // 新規Node生成
////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////            node = obj.GetComponent<MapNode>();
////            node.cell = cell;
////            MapNode.allNodeCells.Add(cell);
////            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
////        }

////        // Node確定後：常に背面リンクを実行（既存Nodeでも新方向接続を確認）
////        if (node != null)
////        {
////            if (debugLog) Debug.Log($"[LINK] Check back connection for Node={node.name}");
////            LinkBackWithRay(node);
////        }

////        return node;
////    }

////    // ======================================================
////    // ■ 座標変換系ユーティリティ
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