using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// C版：LINQ完全撤廃・TempAllocゼロ・高速迷路探索AI
/// ・Unknown + DistanceFromStart のハイブリッド探索
/// ・終端は Unknown最優先、複数なら Distance優先
/// ・壁方向へ絶対進まない
/// ・毎フレームのGC Alloc = 0
/// </summary>
public class CellFromStart : MonoBehaviour
{
    // ======================================================
    // パラメータ
    // ======================================================

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
    public int unknownReferenceDepth = 3;

    [Header("スコア重み")]
    public float weightUnknown = 1f;
    public float weightDistance = 1f;

    [Header("Ray設定")]
    public int linkRayMaxSteps = 100;

    [Header("デバッグ")]
    public bool debugLog = false;
    public bool debugRay = false;

    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Material exploreMaterial;


    // ======================================================
    // 内部状態
    // ======================================================

    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private MapNode currentNode;

    private readonly List<MapNode> recentNodes = new List<MapNode>(8);

    // 再利用方向リスト（GCゼロ）
    private static readonly Vector3[] BaseDirs =
    {
        Vector3.forward,
        Vector3.back,
        Vector3.left,
        Vector3.right
    };

    private readonly List<Vector3> tmpDirs1 = new List<Vector3>(4);
    private readonly List<Vector3> tmpDirs2 = new List<Vector3>(4);
    private readonly List<Vector3> tmpDirs3 = new List<Vector3>(4);


    // ======================================================
    // Start / Update
    // ======================================================

    private void Start()
    {
        moveDir = startDirection.normalized;
        transform.position = SnapToGrid(transform.position);
        targetPos = transform.position;

        if (bodyRenderer && exploreMaterial)
            bodyRenderer.material = exploreMaterial;

        currentNode = TryPlaceNode(transform.position);
        RegisterCurrentNode(currentNode);
    }

    private void Update()
    {
        if (!isMoving)
        {
            if (CanPlaceNodeHere())
                TryExploreMove();
            else
                SafeMove(moveDir);
        }
        else
        {
            MoveToTarget();
        }
    }


    // ======================================================
    // Node 設置
    // ======================================================

    private MapNode TryPlaceNode(Vector3 pos)
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
        }

        if (MapNode.StartNode == null)
        {
            MapNode.StartNode = node;
            node.distanceFromStart = 0;
        }

        LinkBackward(node);
        return node;
    }


    // ======================================================
    // TryExploreMove（探索中心）
    // ======================================================

    //private void TryExploreMove()
    //{
    //    Debug.Log(
    //        $"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}"
    //    );

    //    currentNode = TryPlaceNode(transform.position);
    //    RegisterCurrentNode(currentNode);

    //    // ① 終端Node
    //    if (IsTerminalNode(currentNode))
    //    {
    //        Vector3? dir = ChooseTerminalDirection(currentNode);
    //        if (dir.HasValue)
    //            SafeMove(dir.Value);
    //        else
    //            MoveToUnlinked();
    //        return;
    //    }

    //    // ② 孤立 Node（リンクゼロ）
    //    if (currentNode.links.Count == 0)
    //    {
    //        MoveToUnlinked();
    //        return;
    //    }

    //    // ③ スコアで選択
    //    MapNode next = ChooseNextByScore(currentNode);
    //    if (next != null)
    //    {
    //        Vector3 dir = (next.transform.position - transform.position).normalized;
    //        SafeMove(dir);
    //        return;
    //    }

    //    if (NoUnknownInReference())
    //    {
    //        MapNode target = FindNearestUnknown(currentNode);

    //        if (target != null)
    //        {
    //            MapNode step = NextNodeToward(target);
    //            if (step != null)
    //            {
    //                Vector3 dir = (step.transform.position - transform.position).normalized;
    //                Debug.Log($"[Fallback] Toward Unknown {target.cell} → step {step.cell}");
    //                SafeMove(dir);
    //                return;
    //            }
    //        }
    //    }

    //    // ④ fallback
    //    MoveToUnlinked();
    //}
    private void TryExploreMove()
    {
        Debug.Log(
            $"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}"
        );

        currentNode = TryPlaceNode(transform.position);
        RegisterCurrentNode(currentNode);

        // ① 終端Node
        if (IsTerminalNode(currentNode))
        {
            Vector3? dir = ChooseTerminalDirection(currentNode);
            if (dir.HasValue)
                SafeMove(dir.Value);
            else
                MoveToUnlinked();
            return;
        }

        // ② 孤立 Node（リンクゼロ）
        if (currentNode.links.Count == 0)
        {
            MoveToUnlinked();
            return;
        }

        // ③ スコアで選択
        MapNode next = ChooseNextByScore(currentNode);
        if (next != null)
        {
            Vector3 dir = (next.transform.position - transform.position).normalized;

            // ★★ 修正：prevNode がないので「戻り方向」を moveDir で判定する
            if (Vector3.Dot(dir, moveDir) < -0.9f)
            {
                Debug.Log($"[FallbackTrigger] Back direction suggested → fallback at node {currentNode.cell}");
                goto FALLBACK;
            }

            SafeMove(dir);
            return;
        }

    FALLBACK:

        // fallback条件：unknownReferenceDepth すべて U=0
        if (NoUnknownInReference())
        {
            MapNode target = FindNearestUnknown(currentNode);

            if (target != null)
            {
                MapNode step = NextNodeToward(target);
                if (step != null)
                {
                    Vector3 dir = (step.transform.position - transform.position).normalized;
                    Debug.Log($"[Fallback] Toward Unknown {target.cell} → step {step.cell}");
                    SafeMove(dir);
                    return;
                }
            }
        }

        // ④ fallback (未探索方向へ)
        MoveToUnlinked();
    }


    private bool NoUnknownInReference()
    {
        for (int i = 0; i < recentNodes.Count; i++)
        {
            if (recentNodes[i] != null && recentNodes[i].unknownCount > 0)
                return false;
        }
        return true;
    }

    private MapNode FindNearestUnknown(MapNode start)
    {
        Queue<MapNode> q = new Queue<MapNode>();
        HashSet<MapNode> visited = new HashSet<MapNode>();

        q.Enqueue(start);
        visited.Add(start);

        while (q.Count > 0)
        {
            MapNode node = q.Dequeue();

            if (node.unknownCount > 0)
                return node;

            for (int i = 0; i < node.links.Count; i++)
            {
                MapNode next = node.links[i];
                if (!visited.Contains(next))
                {
                    visited.Add(next);
                    q.Enqueue(next);
                }
            }
        }
        return null;
    }

    private MapNode NextNodeToward(MapNode target)
    {
        Queue<MapNode> q = new Queue<MapNode>();
        Dictionary<MapNode, MapNode> parent = new Dictionary<MapNode, MapNode>();

        q.Enqueue(currentNode);
        parent[currentNode] = null;

        while (q.Count > 0)
        {
            MapNode node = q.Dequeue();

            if (node == target)
            {
                MapNode cur = target;
                MapNode prev = parent[cur];

                while (prev != currentNode)
                {
                    cur = prev;
                    prev = parent[cur];
                }

                return cur;
            }

            for (int i = 0; i < node.links.Count; i++)
            {
                MapNode next = node.links[i];
                if (!parent.ContainsKey(next))
                {
                    parent[next] = node;
                    q.Enqueue(next);
                }
            }
        }

        return null;
    }

    // ======================================================
    // 終端 Node の方向選択
    // ======================================================

    private Vector3? ChooseTerminalDirection(MapNode node)
    {
        tmpDirs1.Clear();
        tmpDirs2.Clear();
        tmpDirs3.Clear();

        Vector3 back = -moveDir;

        // 1. backを除外
        for (int i = 0; i < 4; i++)
        {
            Vector3 d = BaseDirs[i];
            if (Vector3.Dot(d, back) < 0.7f)
                tmpDirs1.Add(d);
        }

        // 2. リンク方向を除外
        for (int i = 0; i < tmpDirs1.Count; i++)
        {
            Vector3 d = tmpDirs1[i];
            if (!IsLinkedDirection(node, d))
                tmpDirs2.Add(d);
        }

        // 3. 壁方向を除外
        for (int i = 0; i < tmpDirs2.Count; i++)
        {
            Vector3 d = tmpDirs2[i];
            if (!IsWall(transform.position, d))
                tmpDirs3.Add(d);
        }

        if (tmpDirs3.Count == 0)
            return null;

        if (tmpDirs3.Count == 1)
            return tmpDirs3[0];

        // Distance評価
        int bestScore = int.MinValue;
        Vector3 bestDir = tmpDirs3[0];

        for (int i = 0; i < tmpDirs3.Count; i++)
        {
            Vector3 d = tmpDirs3[i];
            Vector2Int c = WorldToCell(transform.position + d * cellSize);
            MapNode near = MapNode.FindByCell(c);

            if (near == null) continue;

            int score = -near.distanceFromStart;
            if (score > bestScore)
            {
                bestScore = score;
                bestDir = d;
            }
        }

        return bestDir;
    }


    // ======================================================
    // MoveToUnlinked（未知方向へ）
    // ======================================================

    private void MoveToUnlinked()
    {
        tmpDirs1.Clear();
        tmpDirs2.Clear();
        tmpDirs3.Clear();

        Vector3 back = -moveDir;

        // 1. back除外
        for (int i = 0; i < 4; i++)
        {
            Vector3 d = BaseDirs[i];
            if (Vector3.Dot(d, back) < 0.7f)
                tmpDirs1.Add(d);
        }

        // 2. リンク除外
        for (int i = 0; i < tmpDirs1.Count; i++)
        {
            Vector3 d = tmpDirs1[i];
            if (!IsLinkedDirection(currentNode, d))
                tmpDirs2.Add(d);
        }

        // 3. 壁除外（★ここだけ修正）
        for (int i = 0; i < tmpDirs2.Count; i++)
        {
            Vector3 d = tmpDirs2[i];

            Vector3 origin =
                transform.position +
                Vector3.up * 0.1f +
                d * (cellSize * 0.45f);  // ← UnknownQuantity と同じ

            float dist = cellSize * 0.55f;

            if (!Physics.Raycast(origin, d, dist, wallLayer))
            {
                tmpDirs3.Add(d);
            }
        }

        // ★完全に行けない → 後退
        if (tmpDirs3.Count == 0)
        {
            Debug.Log($"[Unlinked] No valid dirs → BACK to {(-moveDir)} at node {currentNode.cell}");
            moveDir = -moveDir;
            MoveForward();
            return;
        }

        Vector3 chosen = tmpDirs3[Random.Range(0, tmpDirs3.Count)];
        Debug.Log($"[Unlinked] Choose={chosen} at node {currentNode.cell}");
        SafeMove(chosen);
    }


    // ======================================================
    // 通常スコア選択
    // ======================================================

    private MapNode ChooseNextByScore(MapNode current)
    {
        // 履歴の未知数最大Nodeが current なら未知探索優先
        MapNode bestHist = null;
        int maxU = -1;

        for (int i = 0; i < recentNodes.Count; i++)
        {
            MapNode n = recentNodes[i];
            if (n != null && n.unknownCount > maxU)
            {
                maxU = n.unknownCount;
                bestHist = n;
            }
        }

        if (bestHist == current)
            return null;

        // links からスコア最大を選ぶ
        MapNode best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < current.links.Count; i++)
        {
            MapNode n = current.links[i];
            float score =
                weightUnknown * n.unknownCount +
                weightDistance * (-n.distanceFromStart);

            Debug.Log($"[ScoreCheck] from {current.cell} → {n.cell}  U={n.unknownCount} D={n.distanceFromStart} Score={score}");

            if (score > bestScore)
            {
                bestScore = score;
                best = n;
            }
        }

        return best;
    }


    // ======================================================
    // 安全移動（壁チェック）
    // ======================================================

    private bool CanMove(Vector3 dir)
    {
        // ★ UnknownQuantity と同じ壁検出方式（半歩前から Ray）
        Vector3 origin =
            transform.position +
            Vector3.up * 0.1f +
            dir * (cellSize * 0.45f);   // ← プレイヤー中心ではなく“前方0.45マス”が重要

        float dist = cellSize * 0.55f;

        return !Physics.Raycast(origin, dir, dist, wallLayer);
    }

    private void SafeMove(Vector3 dir)
    {
        if (!CanMove(dir))
        {
            Debug.Log($"[SafeMove] Cannot move {dir} from {transform.position} → fallback");
            MoveToUnlinked();
            return;
        }

        Debug.Log($"[SafeMove] Move {dir} from {transform.position}");
        moveDir = dir;
        MoveForward();
    }


    // ======================================================
    // 移動処理
    // ======================================================

    private void MoveForward()
    {
        targetPos = SnapToGrid(transform.position + moveDir * cellSize);
        isMoving = true;
    }

    private void MoveToTarget()
    {
        // ★ 毎フレーム、次の方向が壁でないか確認
        Vector3 dir = (targetPos - transform.position).normalized;

        // もし進行方向が壁なら、強制停止
        if (!CanMove(dir))
        {
            Debug.Log($"[MoveBlock] STOP at {transform.position} DIR={dir} (Wall detected)");
            isMoving = false;
            transform.position = SnapToGrid(transform.position);
            return;
        }

        // 通常移動
        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                moveSpeed * Time.deltaTime);
        }
        else
        {
            transform.position = targetPos;
            isMoving = false;
        }
    }


    // ======================================================
    // 各種ユーティリティ
    // ======================================================

    private bool IsLinkedDirection(MapNode node, Vector3 dir)
    {
        for (int i = 0; i < node.links.Count; i++)
        {
            Vector3 diff = (node.links[i].transform.position - node.transform.position).normalized;
            if (Vector3.Dot(diff, dir) > 0.7f)
                return true;
        }
        return false;
    }

    private bool IsWall(Vector3 pos, Vector3 dir)
    {
        // pos(currentNode) は使わない → transform.position を使う
        Vector3 origin =
            transform.position +
            Vector3.up * 0.1f +
            dir * (cellSize * 0.45f);

        float dist = cellSize * 0.55f;

        return Physics.Raycast(origin, dir, dist, wallLayer);
    }

    private bool IsTerminalNode(MapNode node)
    {
        return node != null && node.links.Count == 1;
    }

    private void RegisterCurrentNode(MapNode node)
    {
        if (recentNodes.Count == 0 || recentNodes[recentNodes.Count - 1] != node)
            recentNodes.Add(node);

        while (recentNodes.Count > unknownReferenceDepth)
            recentNodes.RemoveAt(0);
    }


    // ======================================================
    // 座標処理
    // ======================================================

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
    {
        return new Vector3(cell.x * cellSize, 0, cell.y * cellSize) + gridOrigin;
    }

    private void LinkBackward(MapNode node)
    {
        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        Vector3 dir = -moveDir;
        LayerMask mask = wallLayer | nodeLayer;

        for (int step = 1; step <= linkRayMaxSteps; step++)
        {
            float dist = cellSize * step;

            if (debugRay)
                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);

            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
            {
                int layer = hit.collider.gameObject.layer;

                if ((wallLayer.value & (1 << layer)) != 0)
                    return;

                if ((nodeLayer.value & (1 << layer)) != 0)
                {
                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
                    if (hitNode != null && hitNode != node)
                    {
                        node.AddLink(hitNode);
                        node.RecalculateUnknownAndWall();
                        hitNode.RecalculateUnknownAndWall();
                    }
                    return;
                }
            }
        }
    }


    private bool CanPlaceNodeHere()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;

        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(origin, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(origin, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(origin, rightDir, rayDistance, wallLayer);

        int open = (frontHit ? 0 : 1) + (leftHit ? 0 : 1) + (rightHit ? 0 : 1);

        return frontHit || open >= 2;
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

//    private void MoveForward()
//    {
//        targetPos = SnapToGrid(transform.position + moveDir * cellSize);
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

//    private Vector3? ChooseTerminalDirection(MapNode node)
//    {
//        List<Vector3> dirs = AllMovesExceptBack();

//        // リンク方向を除外
//        dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

//        // 壁方向を除外
//        List<Vector3> unknownDirs = dirs.Where(d => !IsWall(node, d)).ToList();

//        if (unknownDirs.Count == 0)
//            return null;

//        if (unknownDirs.Count == 1)
//            return unknownDirs[0];

//        // 複数ある場合 → DistanceFromStart を使う
//        int bestScore = int.MinValue;
//        Vector3 best = unknownDirs[0];

//        foreach (var d in unknownDirs)
//        {
//            Vector2Int cell = WorldToCell(node.transform.position + d * cellSize);
//            MapNode near = MapNode.FindByCell(cell);
//            if (near == null) continue;

//            int score = -near.distanceFromStart; // Startから遠いほど高評価
//            if (score > bestScore)
//            {
//                bestScore = score;
//                best = d;
//            }
//        }

//        return best;
//    }

//    private bool IsTerminalNode(MapNode node)
//        => node != null && node.links.Count == 1;

//    private MapNode ChooseNextByScore(MapNode current)
//    {
//        // 履歴中の未知方向が最も多いNodeが current なら未知探索へ
//        if (recentNodes.Count > 0)
//        {
//            MapNode best = recentNodes.OrderByDescending(n => n.unknownCount).First();
//            if (best == current)
//                return null;
//        }

//        return current.links
//            .OrderByDescending(n => Score(n))
//            .ThenBy(_ => Random.value)
//            .FirstOrDefault();
//    }

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

//    private MapNode TryPlaceNode(Vector3 pos)
//    {
//        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//        MapNode node;

//        if (MapNode.allNodeCells.Contains(cell))
//            node = MapNode.FindByCell(cell);
//        else
//        {
//            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//            node = obj.GetComponent<MapNode>();
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//        }

//        if (MapNode.StartNode == null)
//        {
//            MapNode.StartNode = node;
//            node.distanceFromStart = 0;
//        }

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

////// ======================================================
//// CellFromStart.cs A版
//// ・UnknownCount（未知数）＋ DistanceFromStart（Startからの距離）を使った
////   ハイブリッドスコアで方向選択
//// ・weightUnknown = 1.0, weightDistance = 1.0
////// ======================================================

//using UnityEngine;
//using System.Collections.Generic;
//using System.Linq;
//using System.Collections;

//public class CellFromStart : MonoBehaviour
//{
//    // ======================================================
//    // パラメータ設定
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
//    public GameObject nodePrefab;

//    [Header("行動傾向")]
//    [Range(0f, 1f)] public float exploreBias = 0.6f;

//    [Header("探索パラメータ")]
//    public int unknownReferenceDepth;

//    [Header("リンク探索")]
//    public int linkRayMaxSteps = 100;

//    [Header("スコア重み")]
//    public float weightUnknown = 1.0f;
//    public float weightDistance = 1.0f;

//    [Header("デバッグ")]
//    public bool debugLog = true;
//    public bool debugRay = true;
//    [SerializeField] private Renderer bodyRenderer;
//    [SerializeField] private Material exploreMaterial;

//    // ======================================================
//    // 内部状態変数
//    // ======================================================

//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;
//    private MapNode currentNode;

//    private const float EPS = 1e-4f;

//    private List<MapNode> recentNodes = new List<MapNode>();

//    // ======================================================
//    // Start
//    // ======================================================
//    void Start()
//    {
//        moveDir = startDirection.normalized;
//        targetPos = transform.position = SnapToGrid(transform.position);
//        ApplyVisual();
//        currentNode = TryPlaceNode(transform.position);

//        RegisterCurrentNode(currentNode);

//        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
//    }

//    // ======================================================
//    // Update
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
//    // ApplyVisual
//    // ======================================================
//    private void ApplyVisual()
//    {
//        if (bodyRenderer == null) return;
//        bodyRenderer.material = exploreMaterial
//            ? exploreMaterial
//            : new Material(Shader.Find("Standard")) { color = Color.cyan };
//    }

//    // ======================================================
//    // CanPlaceNodeHere
//    // ======================================================
//    bool CanPlaceNodeHere()
//    {
//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir,
//                                        rayDistance, wallLayer);
//        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir,
//                                       rayDistance, wallLayer);
//        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir,
//                                        rayDistance, wallLayer);

//        int openCount = 0;
//        if (!frontHit) openCount++;
//        if (!leftHit) openCount++;
//        if (!rightHit) openCount++;

//        return (frontHit || openCount >= 2);
//    }

//    // ======================================================
//    // MoveForward
//    // ======================================================
//    void MoveForward()
//    {
//        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
//        targetPos = nextPos;
//        isMoving = true;
//    }

//    // ======================================================
//    // MoveToTarget
//    // ======================================================
//    private void MoveToTarget()
//    {
//        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
//        {
//            transform.position = Vector3.MoveTowards(
//                transform.position,
//                targetPos,
//                moveSpeed * Time.deltaTime
//            );
//        }
//        else
//        {
//            transform.position = targetPos;
//            isMoving = false;
//        }
//    }

//    // ======================================================
//    // Node 履歴管理
//    // ======================================================
//    private void RegisterCurrentNode(MapNode node)
//    {
//        if (node == null) return;

//        if (recentNodes.Count > 0 && recentNodes[recentNodes.Count - 1] == node)
//            return;

//        recentNodes.Add(node);

//        int maxDepth = Mathf.Max(unknownReferenceDepth, 1);
//        while (recentNodes.Count > maxDepth)
//            recentNodes.RemoveAt(0);

//        if (debugLog)
//        {
//            string hist = string.Join(" -> ",
//                           recentNodes.Select(n => n != null ? n.name : "null"));
//            Debug.Log($"[HIST] {hist}");
//        }
//    }

//    // ======================================================
//    // TryExploreMove：方向選択
//    // ======================================================
//    void TryExploreMove()
//    {
//        currentNode = TryPlaceNode(transform.position);
//        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

//        RegisterCurrentNode(currentNode);

//        // ========================================
//        // ★ 終端Node（links=1）用の特別処理（改良版）
//        // ========================================
//        if (IsTerminalNode(currentNode))
//        {
//            var dir = ChooseDirectionAtTerminal(currentNode);
//            if (dir.HasValue)
//            {
//                moveDir = dir.Value;
//                MoveForward();
//            }
//            else
//            {
//                TryMoveToUnlinkedDirection();
//            }
//            return;
//        }

//        if (currentNode == null || currentNode.links.Count == 0)
//        {
//            TryMoveToUnlinkedDirection();
//            return;
//        }

//        // ★ Unknown + Distance のスコアで選ぶ
//        MapNode next = ChooseNextNodeByScore(currentNode);

//        if (next != null)
//        {
//            moveDir = (next.transform.position - transform.position).normalized;
//            MoveForward();
//        }
//        else
//        {
//            TryMoveToUnlinkedDirection();
//        }
//    }

//    // ======================================================
//    // スコア計算
//    // ======================================================
//    private float CalcNodeScore(MapNode node)
//    {
//        if (node == null) return -999999f;

//        float u = node.unknownCount;
//        float d = node.distanceFromStart;

//        float score = weightUnknown * u + weightDistance * (-d);

//        if (debugLog)
//            Debug.Log($"[SCORE] {node.name}: U={u}, D={d} → score={score}");

//        return score;
//    }

//    // ======================================================
//    // ChooseNextNodeByScore：スコア方式
//    // ======================================================
//    private MapNode ChooseNextNodeByScore(MapNode current)
//    {
//        if (current == null || current.links.Count == 0)
//            return null;

//        if (unknownReferenceDepth > 0 && recentNodes.Count > 0)
//        {
//            MapNode bestNode = null;
//            float bestU = -1;

//            foreach (var n in recentNodes)
//            {
//                if (n == null) continue;
//                if (n.unknownCount > bestU)
//                {
//                    bestU = n.unknownCount;
//                    bestNode = n;
//                }
//            }

//            if (bestNode == current)
//                return null;
//        }

//        var best = current.links
//            .OrderByDescending(n => CalcNodeScore(n))
//            .ThenBy(_ => Random.value)
//            .FirstOrDefault();

//        if (best != null)
//            Debug.Log($"[SCORE-SELECT] {current.name} → {best.name}");

//        return best;
//    }

//    // ======================================================
//    // 終端ノード判定
//    // ======================================================
//    private bool IsTerminalNode(MapNode node)
//    {
//        return node != null && node.links != null && node.links.Count == 1;
//    }

//    // ======================================================
//    // ★ 終端ノード専用：Unknown最優先＋複数候補ならDistance評価（追加）
//    // ======================================================
//    private Vector3? ChooseDirectionAtTerminal(MapNode node)
//    {
//        List<Vector3> dirs = new List<Vector3>
//        {
//            Vector3.forward, Vector3.back, Vector3.left, Vector3.right
//        };

//        Vector3 backDir = (-moveDir).normalized;
//        Vector3 origin = node.transform.position + Vector3.up * 0.1f;

//        // ① 逆走を除外
//        dirs = dirs.Where(d => Vector3.Dot(d.normalized, backDir) < 0.7f).ToList();

//        // ② 既存リンクを除外
//        dirs = dirs.Where(d =>
//        {
//            foreach (var link in node.links)
//            {
//                Vector3 diff = (link.transform.position - node.transform.position).normalized;
//                if (Vector3.Dot(diff, d.normalized) > 0.7f)
//                    return false;
//            }
//            return true;
//        }).ToList();

//        // ③ 壁を除外
//        List<Vector3> unknownDirs = dirs.Where(d =>
//            !Physics.Raycast(origin, d, cellSize, wallLayer)
//        ).ToList();

//        if (unknownDirs.Count == 0)
//            return null;

//        // Unknown が1個ならそれで確定
//        if (unknownDirs.Count == 1)
//            return unknownDirs[0];

//        // --- 複数ある場合：DistanceFromStartで最前進方向を選ぶ ---
//        Vector3 bestDir = unknownDirs[0];
//        int bestScore = int.MinValue;

//        foreach (var d in unknownDirs)
//        {
//            Vector3 p = node.transform.position + d * cellSize;
//            Vector2Int cell = WorldToCell(p);
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

//    // ======================================================
//    // TryMoveToUnlinkedDirection
//    // ======================================================
//    private void TryMoveToUnlinkedDirection()
//    {
//        if (currentNode == null)
//        {
//            MoveForward();
//            return;
//        }

//        List<Vector3> allDirs = new List<Vector3>
//        {
//            Vector3.forward, Vector3.back, Vector3.left, Vector3.right
//        };

//        Vector3 backDir = (-moveDir).normalized;

//        List<Vector3> afterBack = new List<Vector3>();
//        foreach (var d in allDirs)
//        {
//            if (Vector3.Dot(d.normalized, backDir) > 0.7f) continue;
//            afterBack.Add(d);
//        }

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
//                    break;
//                }
//            }
//            if (!linked) afterLinked.Add(d);
//        }

//        List<Vector3> validDirs = new List<Vector3>();
//        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
//        foreach (var d in afterLinked)
//        {
//            if (!Physics.Raycast(origin, d, cellSize, wallLayer))
//                validDirs.Add(d);
//        }

//        if (validDirs.Count == 0)
//        {
//            foreach (var link in currentNode.links)
//            {
//                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
//                if (Vector3.Dot(diff, backDir) < 0.7f)
//                {
//                    moveDir = diff;
//                    MoveForward();
//                    return;
//                }
//            }
//            return;
//        }

//        moveDir = validDirs[UnityEngine.Random.Range(0, validDirs.Count)];
//        MoveForward();
//    }

//    // ======================================================
//    // ノード設置処理
//    // ======================================================
//    MapNode TryPlaceNode(Vector3 pos)
//    {
//        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//        MapNode node;

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
//        }

//        if (MapNode.StartNode == null)
//        {
//            MapNode.StartNode = node;
//            node.distanceFromStart = 0;
//        }

//        if (node != null)
//            LinkBackWithRay(node);

//        return node;
//    }

//    // ======================================================
//    // LinkBackWithRay
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
//                    }
//                    return;
//                }
//            }
//        }
//    }

//    // ======================================================
//    // 座標変換
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


//////// ======================================================
////// CellFromStart.cs
////// ・UnknownCount（未知数）＋ DistanceFromStart（Startからの距離）を使った
//////   ハイブリッドスコアで方向選択
////// ・weightUnknown = 1.0, weightDistance = 1.0
//////// ======================================================

////using UnityEngine;
////using System.Collections.Generic;
////using System.Linq;
////using System.Collections;

////public class CellFromStart : MonoBehaviour
////{
////    // ======================================================
////    // パラメータ設定
////    // ======================================================

////    [Header("移動設定")]
////    public float moveSpeed = 3f;
////    public float cellSize = 1f;
////    public float rayDistance = 1f;
////    public LayerMask wallLayer;
////    public LayerMask nodeLayer;

////    [Header("初期設定")]
////    public Vector3 startDirection = Vector3.forward;
////    public Vector3 gridOrigin = Vector3.zero;
////    public GameObject nodePrefab;

////    [Header("行動傾向")]
////    [Range(0f, 1f)] public float exploreBias = 0.6f;

////    [Header("探索パラメータ")]
////    public int unknownReferenceDepth;

////    [Header("リンク探索")]
////    public int linkRayMaxSteps = 100;

////    [Header("スコア重み")]
////    public float weightUnknown = 1.0f;
////    public float weightDistance = 1.0f;

////    [Header("デバッグ")]
////    public bool debugLog = true;
////    public bool debugRay = true;
////    [SerializeField] private Renderer bodyRenderer;
////    [SerializeField] private Material exploreMaterial;

////    // ======================================================
////    // 内部状態変数
////    // ======================================================

////    private Vector3 moveDir;
////    private bool isMoving = false;
////    private Vector3 targetPos;
////    private MapNode currentNode;

////    private const float EPS = 1e-4f;

////    private List<MapNode> recentNodes = new List<MapNode>();

////    // ======================================================
////    // Start
////    // ======================================================
////    void Start()
////    {
////        moveDir = startDirection.normalized;
////        targetPos = transform.position = SnapToGrid(transform.position);
////        ApplyVisual();
////        currentNode = TryPlaceNode(transform.position);

////        RegisterCurrentNode(currentNode);

////        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
////    }

////    // ======================================================
////    // Update
////    // ======================================================
////    void Update()
////    {
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
////    // ApplyVisual
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
////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir,
////                                        rayDistance, wallLayer);
////        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir,
////                                       rayDistance, wallLayer);
////        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir,
////                                        rayDistance, wallLayer);

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
////    // MoveToTarget
////    // ======================================================
////    private void MoveToTarget()
////    {
////        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
////        {
////            transform.position = Vector3.MoveTowards(
////                transform.position,
////                targetPos,
////                moveSpeed * Time.deltaTime
////            );
////        }
////        else
////        {
////            transform.position = targetPos;
////            isMoving = false;
////        }
////    }

////    // ======================================================
////    // Node 履歴管理
////    // ======================================================
////    private void RegisterCurrentNode(MapNode node)
////    {
////        if (node == null) return;

////        if (recentNodes.Count > 0 && recentNodes[recentNodes.Count - 1] == node)
////            return;

////        recentNodes.Add(node);

////        int maxDepth = Mathf.Max(unknownReferenceDepth, 1);
////        while (recentNodes.Count > maxDepth)
////            recentNodes.RemoveAt(0);

////        if (debugLog)
////        {
////            string hist = string.Join(" -> ",
////                           recentNodes.Select(n => n != null ? n.name : "null"));
////            Debug.Log($"[HIST] {hist}");
////        }
////    }

////    // ======================================================
////    // TryExploreMove：方向選択
////    // ======================================================
////    void TryExploreMove()
////    {
////        currentNode = TryPlaceNode(transform.position);
////        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

////        //Debug.Log($"[EXP-DEBUG] Node placed: {currentNode.name}  U={currentNode.unknownCount}  D={currentNode.distanceFromStart}");

////        RegisterCurrentNode(currentNode);

////        // 終端なら未知方向探索へ
////        if (IsTerminalNode(currentNode))
////        {
////            //Debug.Log("[EXP] Terminal → TryMoveToUnlinkedDirection()");
////            TryMoveToUnlinkedDirection();
////            return;
////        }

////        if (currentNode == null || currentNode.links.Count == 0)
////        {
////            //Debug.Log("[EXP] No links → TryMoveToUnlinkedDirection()");
////            TryMoveToUnlinkedDirection();
////            return;
////        }

////        // ★ Unknown + Distance のスコアで選ぶ
////        MapNode next = ChooseNextNodeByScore(currentNode);

////        if (next != null)
////        {
////            moveDir = (next.transform.position - transform.position).normalized;
////            //Debug.Log($"[EXP-SELECT] Move to {next.name}  (U={next.unknownCount}, D={next.distanceFromStart})");
////            MoveForward();
////        }
////        else
////        {
////           // Debug.Log("[EXP-SELECT] NULL → TryMoveToUnlinkedDirection()");
////            TryMoveToUnlinkedDirection();
////        }
////    }

////    // ======================================================
////    // スコア計算
////    // ======================================================
////    private float CalcNodeScore(MapNode node)
////    {
////        if (node == null) return -999999f;

////        float u = node.unknownCount;
////        float d = node.distanceFromStart;

////        float score = weightUnknown * u + weightDistance * (-d);

////        if (debugLog)
////            Debug.Log($"[SCORE] {node.name}: U={u}, D={d} → score={score}");

////        return score;
////    }

////    // ======================================================
////    // ChooseNextNodeByScore：スコア方式
////    // ======================================================
////    private MapNode ChooseNextNodeByScore(MapNode current)
////    {
////        if (current == null || current.links.Count == 0)
////            return null;

////        //Debug.Log("=== [DEBUG] recentNodes 状況 ===");
////        //for (int i = 0; i < recentNodes.Count; i++)
////        //{
////        //    var n = recentNodes[i];
////        //    if (n != null)
////        //        Debug.Log($"  recent[{i}] = {n.name}  U={n.unknownCount}  D={n.distanceFromStart}");
////        //    else
////        //        Debug.Log($"  recent[{i}] = null");
////        //}

////        // 履歴を使うかどうかは従来部を再利用
////        if (unknownReferenceDepth > 0 && recentNodes.Count > 0)
////        {
////            MapNode bestNode = null;
////            float bestU = -1;

////            foreach (var n in recentNodes)
////            {
////                if (n == null) continue;
////                if (n.unknownCount > bestU)
////                {
////                    bestU = n.unknownCount;
////                    bestNode = n;
////                }
////            }

////            //Debug.Log($"[DEBUG] 履歴評価結果: bestNode={bestNode?.name}, bestU={bestU}");

////            // 履歴上で最も未知数が高いノードにいる場合 → 新規開拓
////            if (bestNode == current)
////                return null;
////            //Debug.Log("[DEBUG] bestNode が current → 新規方向開拓へ");
////        }

////        // ★ スコア方式でリンク先を選ぶ
////        var best = current.links
////            .OrderByDescending(n => CalcNodeScore(n))
////            .ThenBy(_ => Random.value)
////            .FirstOrDefault();

////        if (best != null)
////            Debug.Log($"[SCORE-SELECT] {current.name} → {best.name}");

////        return best;
////    }

////    // ======================================================
////    // 終端ノード判定
////    // ======================================================
////    private bool IsTerminalNode(MapNode node)
////    {
////        return node != null && node.links != null && node.links.Count == 1;
////    }

////    // ======================================================
////    // TryMoveToUnlinkedDirection（未知方向探索）
////    // ======================================================
////    private void TryMoveToUnlinkedDirection()
////    {
////        if (currentNode == null)
////        {
////            MoveForward();
////            return;
////        }

////        List<Vector3> allDirs = new List<Vector3>
////        {
////            Vector3.forward, Vector3.back, Vector3.left, Vector3.right
////        };

////        Vector3 backDir = (-moveDir).normalized;

////        // ① 戻る方向を除外
////        List<Vector3> afterBack = new List<Vector3>();
////        foreach (var d in allDirs)
////        {
////            if (Vector3.Dot(d.normalized, backDir) > 0.7f) continue;
////            afterBack.Add(d);
////        }

////        // ② 既存リンク除外
////        List<Vector3> afterLinked = new List<Vector3>();
////        foreach (var d in afterBack)
////        {
////            bool linked = false;
////            foreach (var link in currentNode.links)
////            {
////                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
////                if (Vector3.Dot(diff, d.normalized) > 0.7f)
////                {
////                    linked = true;
////                    break;
////                }
////            }
////            if (!linked) afterLinked.Add(d);
////        }

////        // ③ 壁除外
////        List<Vector3> validDirs = new List<Vector3>();
////        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
////        foreach (var d in afterLinked)
////        {
////            if (!Physics.Raycast(origin, d, cellSize, wallLayer))
////                validDirs.Add(d);
////        }

////        if (validDirs.Count == 0)
////        {
////            // 戻れない場合は停止
////            foreach (var link in currentNode.links)
////            {
////                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
////                if (Vector3.Dot(diff, backDir) < 0.7f)
////                {
////                    moveDir = diff;
////                    MoveForward();
////                    return;
////                }
////            }
////            return;
////        }

////        moveDir = validDirs[UnityEngine.Random.Range(0, validDirs.Count)];
////        MoveForward();
////    }

////    // ======================================================
////    // ノード設置処理
////    // ======================================================
////    MapNode TryPlaceNode(Vector3 pos)
////    {
////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////        MapNode node;

////        if (MapNode.allNodeCells.Contains(cell))
////        {
////            node = MapNode.FindByCell(cell);
////        }
////        else
////        {
////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////            node = obj.GetComponent<MapNode>();
////            node.cell = cell;
////            MapNode.allNodeCells.Add(cell);
////        }

////        // StartNode 設定
////        if (MapNode.StartNode == null)
////        {
////            MapNode.StartNode = node;
////            node.distanceFromStart = 0;
////        }

////        // リンク更新
////        if (node != null)
////            LinkBackWithRay(node);

////        return node;
////    }

////    // ======================================================
////    // LinkBackWithRay：後方リンク探索
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

////                if ((wallLayer.value & (1 << hitLayer)) != 0)
////                    return;

////                if ((nodeLayer.value & (1 << hitLayer)) != 0)
////                {
////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
////                    if (hitNode != null && hitNode != node)
////                    {
////                        node.AddLink(hitNode);
////                        node.RecalculateUnknownAndWall();
////                        hitNode.RecalculateUnknownAndWall();
////                    }
////                    return;
////                }
////            }
////        }
////    }

////    // ======================================================
////    // 座標変換
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
