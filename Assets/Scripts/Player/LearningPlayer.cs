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
//using UnityEngine;
//using System.Collections.Generic;

///// <summary>
///// CellFromStart（D版）
///// ・Unknown が近場にあれば Unknown/Distance スコア方式
///// ・Unknown が無ければ「Start から最も遠いノード」へ向かう
///// ・TryPlaceNode は「Node に到達したフレームのみ」呼ぶ（余計な補正をしない）
///// ・DistanceFromStart は “セル数距離（マンハッタン距離）”
///// ・NextNodeToward は BFS 全探索→逆引きで安定
///// ・LinkBackward は来た方向へ Raycast して自動リンク
///// </summary>
//public class CellFromStart : MonoBehaviour
//{
//    // ======================================================
//    // パラメータ
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

//    [Header("探索パラメータ")]
//    public int unknownReferenceDepth = 3;

//    [Header("スコア重み")]
//    public float weightUnknown = 1f;
//    public float weightDistance = 1f;

//    [Header("Ray設定")]
//    public int linkRayMaxSteps = 100;

//    [Header("デバッグ")]
//    public bool debugLog = false;
//    public bool debugRay = false;

//    [SerializeField] private Renderer bodyRenderer;
//    [SerializeField] private Material exploreMaterial;


//    // ======================================================
//    // 内部状態
//    // ======================================================

//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;
//    private MapNode currentNode;

//    private readonly List<MapNode> recentNodes = new List<MapNode>(8);

//    private static readonly Vector3[] BaseDirs =
//    {
//        Vector3.forward, Vector3.back, Vector3.left, Vector3.right
//    };

//    private readonly List<Vector3> tmpDirs1 = new List<Vector3>(4);
//    private readonly List<Vector3> tmpDirs2 = new List<Vector3>(4);
//    private readonly List<Vector3> tmpDirs3 = new List<Vector3>(4);


//    // ======================================================
//    // Start / Update
//    // ======================================================

//    private void Start()
//    {
//        moveDir = startDirection.normalized;
//        transform.position = SnapToGrid(transform.position);
//        targetPos = transform.position;

//        if (bodyRenderer && exploreMaterial)
//            bodyRenderer.material = exploreMaterial;

//        // Node 作成
//        currentNode = TryPlaceNode(transform.position);
//        RegisterCurrentNode(currentNode);
//    }


//    private void Update()
//    {
//        // 移動中 → MoveToTarget のみ
//        if (isMoving)
//        {
//            MoveToTarget();
//            return;
//        }

//        // 移動中でない → Node の中心にいる場合のみ TryExploreMove が呼ばれる
//        if (CanPlaceNodeHere())
//        {
//            TryExploreMove();
//        }
//        else
//        {
//            // Node 以外の場所ではただ進む
//            SafeMove(moveDir);
//        }
//    }


//    // ======================================================
//    // Node 設置（Node 中心に到達したときだけ実行される）
//    // ======================================================

//    private MapNode TryPlaceNode(Vector3 pos)
//    {
//        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//        MapNode node;

//        // 既存 Node？
//        if (MapNode.allNodeCells.Contains(cell))
//        {
//            node = MapNode.FindByCell(cell);
//        }
//        else
//        {
//            // 新規 Node
//            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//            node = obj.GetComponent<MapNode>();
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//            MapNode.allNodes.Add(node);
//        }

//        // StartNode 初期化
//        if (MapNode.StartNode == null)
//        {
//            MapNode.StartNode = node;
//            node.distanceFromStart = 0;
//        }

//        // 来た方向に Ray を飛ばしてリンク
//        LinkBackward(node);

//        return node;
//    }

//    // ======================================================
//    // TryExploreMove（探索ロジックの本体）
//    // ======================================================

//    private void TryExploreMove()
//    {
//        Debug.Log($"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}");

//        // Node 再確認（毎フレームではなく Node 中心に居る時のみ呼ばれる）
//        currentNode = TryPlaceNode(transform.position);
//        RegisterCurrentNode(currentNode);

//        // --- ① 終端 Node ---
//        if (IsTerminalNode(currentNode))
//        {
//            Vector3? d = ChooseTerminalDirection(currentNode);
//            if (d.HasValue)
//                SafeMove(d.Value);
//            else
//                MoveToUnlinked();
//            return;
//        }

//        // --- ② リンク無し（孤立）---
//        if (currentNode.links.Count == 0)
//        {
//            MoveToUnlinked();
//            return;
//        }

//        // ======================================================
//        // ☆ A方式（決定版）
//        //    1. unknown が近場にある → Score方式で探索
//        //    2. unknown が近場に全くない → Start から最も遠いNodeへ
//        // ======================================================

//        bool noUnknown = NoUnknownInReference();

//        // ---（１） unknown がまだある → Score方式 ---
//        if (!noUnknown)
//        {
//            MapNode next = ChooseNextByScore(currentNode);

//            if (next != null)
//            {
//                Vector3 dir = (next.transform.position - transform.position).normalized;
//                SafeMove(dir);
//                return;
//            }
//        }

//        // ---（２） unknown が一切ない → 最遠 Node 探索 ---
//        MapNode far = FindFarthestFromStart();

//        if (far != null)
//        {
//            MapNode step = NextNodeToward(far);
//            if (step != null)
//            {
//                Vector3 dir = (step.transform.position - transform.position).normalized;
//                Debug.Log($"[Farthest] {currentNode.cell} → target {far.cell} → step {step.cell}");
//                SafeMove(dir);
//                return;
//            }
//        }

//        // --- fallback（未知方向へ）---
//        MoveToUnlinked();
//    }



//    // ======================================================
//    // Unknown 参照（BFS depth 指定）
//    // ======================================================

//    private bool NoUnknownInReference()
//    {
//        if (currentNode == null) return false;

//        Queue<(MapNode node, int depth)> q = new();
//        HashSet<MapNode> visited = new();

//        q.Enqueue((currentNode, 0));
//        visited.Add(currentNode);

//        while (q.Count > 0)
//        {
//            var (node, depth) = q.Dequeue();

//            // ★ unknown が一つでも見つかったら「unknown あり」
//            if (node.unknownCount > 0)
//                return false;

//            if (depth >= unknownReferenceDepth)
//                continue;

//            foreach (var next in node.links)
//            {
//                if (!visited.Contains(next))
//                {
//                    visited.Add(next);
//                    q.Enqueue((next, depth + 1));
//                }
//            }
//        }

//        // ★ すべてのチェック範囲で unknown=0 だった
//        return true;
//    }

//    // ======================================================
//    // 目標ノードに向かう「1 手目」を返す
//    // ======================================================

//    private MapNode NextNodeToward(MapNode target)
//    {
//        if (target == null || currentNode == null)
//            return null;

//        Queue<MapNode> q = new Queue<MapNode>();
//        Dictionary<MapNode, MapNode> parent = new Dictionary<MapNode, MapNode>();

//        q.Enqueue(currentNode);
//        parent[currentNode] = null;

//        bool found = false;

//        // BFS を最後まで走らせる
//        while (q.Count > 0)
//        {
//            MapNode node = q.Dequeue();

//            foreach (var next in node.links)
//            {
//                if (!parent.ContainsKey(next))
//                {
//                    parent[next] = node;
//                    q.Enqueue(next);

//                    if (next == target)
//                        found = true;
//                }
//            }
//        }

//        // 到達できない
//        if (!found)
//            return null;

//        // currentNode の次の 1 手目を決定
//        MapNode step = target;

//        while (parent[step] != currentNode)
//        {
//            step = parent[step];

//            if (step == null)
//                return null;    // 安全対策
//        }

//        return step;
//    }

//    // ======================================================
//    // スコア方式で次のリンク先を決定
//    // ======================================================

//    private MapNode ChooseNextByScore(MapNode current)
//    {
//        // --- recentNodes 内の最大 unknownCount ノードを抽出 ---
//        MapNode bestHist = null;
//        int maxU = -1;

//        for (int i = 0; i < recentNodes.Count; i++)
//        {
//            MapNode n = recentNodes[i];
//            if (n != null && n.unknownCount > maxU)
//            {
//                maxU = n.unknownCount;
//                bestHist = n;
//            }
//        }

//        // ★ 修正：
//        // 「current も recent も unknown=0」なら skip しない（farthest へ行くため）
//        if (!(maxU == 0 && current.unknownCount == 0))
//        {
//            if (bestHist == current)
//            {
//                Debug.Log("[ScoreCheck] bestHist == current → skip");
//                return null;
//            }
//        }

//        // --- links からスコア計算 ---
//        MapNode best = null;
//        float bestScore = float.NegativeInfinity;

//        foreach (var n in current.links)
//        {
//            Vector3 dir = (n.transform.position - transform.position).normalized;

//            float score =
//                weightUnknown * n.unknownCount +
//                weightDistance * (-n.distanceFromStart);

//            // 後退方向はペナルティ
//            float dot = Vector3.Dot(dir, moveDir);
//            if (dot < -0.9f)
//            {
//                score -= 0.5f;
//                Debug.Log($"[ScoreCheck] BackPenalty {current.cell} → {n.cell} Score={score}");
//            }
//            else
//            {
//                Debug.Log($"[ScoreCheck] {current.cell} → {n.cell} U={n.unknownCount} D={n.distanceFromStart} Score={score}");
//            }

//            if (score > bestScore)
//            {
//                bestScore = score;
//                best = n;
//            }
//        }

//        return best;
//    }

//    // ======================================================
//    // 安全移動（壁チェックは MoveToTarget 側で行う）
//    // ======================================================

//    private void SafeMove(Vector3 dir)
//    {
//        moveDir = dir;
//        Vector3 nextPos = transform.position + dir * cellSize;

//        // 壁判定は MoveToTarget に任せる
//        targetPos = nextPos;
//    }

//    // ======================================================
//    // 目的地まで移動（毎フレーム壁チェックあり）
//    // ======================================================

//    private void MoveToTarget()
//    {
//        // ★ 壁チェック（移動途中で壁にぶつかったら停止）
//        Vector3 dir = (targetPos - transform.position).normalized;

//        if (!CanMove(dir))
//        {
//            Debug.Log($"[MoveBlock] STOP at {transform.position} DIR={dir} (Wall detected)");
//            isMoving = false;
//            transform.position = SnapToGrid(transform.position);
//            return;
//        }

//        // 通常の移動
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
//    // LinkBackward（逆方向レイでリンクを張る）
//    // ======================================================

//    private void LinkBackward(MapNode node)
//    {
//        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//        Vector3 dir = -moveDir;
//        LayerMask mask = wallLayer | nodeLayer;

//        Debug.Log($"[LinkBackward] node={node.cell} moveDir={moveDir} → dir={dir}");

//        bool linked = false;

//        for (int step = 1; step <= linkRayMaxSteps; step++)
//        {
//            float dist = cellSize * step;

//            if (debugRay)
//                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);

//            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
//            {
//                int layer = hit.collider.gameObject.layer;

//                Debug.Log($"[RayHit] {node.cell} hit={hit.collider.name} layer={layer} dist={dist}");

//                // --- 壁に当たったら中断 ---
//                if ((wallLayer.value & (1 << layer)) != 0)
//                {
//                    Debug.Log($"[LinkBackward] STOP: wall hit at dist={dist}");
//                    return;
//                }

//                // --- Node に当たった ---
//                if ((nodeLayer.value & (1 << layer)) != 0)
//                {
//                    MapNode hitNode = hit.collider.GetComponent<MapNode>();

//                    if (hitNode != null && hitNode != node)
//                    {
//                        Debug.Log($"[LinkBackward] LINK SUCCESS {node.cell} <--> {hitNode.cell}");

//                        node.AddLink(hitNode);
//                        node.RecalculateUnknownAndWall();
//                        hitNode.RecalculateUnknownAndWall();
//                        linked = true;
//                    }
//                    return;
//                }
//            }
//        }

//        if (!linked)
//            Debug.Log($"[LinkBackward] NO LINK: {node.cell} (no node hit)");
//    }

//    private bool IsLinkedDirection(MapNode node, Vector3 dir)
//    {
//        for (int i = 0; i < node.links.Count; i++)
//        {
//            Vector3 diff = (node.links[i].transform.position - node.transform.position).normalized;
//            if (Vector3.Dot(diff, dir) > 0.7f)
//                return true;
//        }
//        return false;
//    }

//    private bool IsWall(Vector3 pos, Vector3 dir)
//    {
//        Vector3 origin =
//            transform.position +
//            Vector3.up * 0.1f +
//            dir * (cellSize * 0.45f);

//        float dist = cellSize * 0.55f;

//        return Physics.Raycast(origin, dir, dist, wallLayer);
//    }

//    private bool IsTerminalNode(MapNode node)
//    {
//        return node != null && node.links.Count == 1;
//    }

//    private void RegisterCurrentNode(MapNode node)
//    {
//        if (recentNodes.Count == 0 || recentNodes[recentNodes.Count - 1] != node)
//            recentNodes.Add(node);

//        while (recentNodes.Count > unknownReferenceDepth)
//            recentNodes.RemoveAt(0);
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
//    {
//        return new Vector3(cell.x * cellSize, 0, cell.y * cellSize) + gridOrigin;
//    }

//    private bool CanPlaceNodeHere()
//    {
//        Vector3 origin = transform.position + Vector3.up * 0.1f;

//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        bool frontHit = Physics.Raycast(origin, moveDir, rayDistance, wallLayer);
//        bool leftHit = Physics.Raycast(origin, leftDir, rayDistance, wallLayer);
//        bool rightHit = Physics.Raycast(origin, rightDir, rayDistance, wallLayer);

//        int open = (frontHit ? 0 : 1) + (leftHit ? 0 : 1) + (rightHit ? 0 : 1);

//        return frontHit || open >= 2;
//    }

//    private Vector3? ChooseTerminalDirection(MapNode node)
//    {
//        tmpDirs1.Clear();
//        tmpDirs2.Clear();
//        tmpDirs3.Clear();

//        Vector3 back = -moveDir;

//        // 1. backを除外
//        for (int i = 0; i < 4; i++)
//        {
//            Vector3 d = BaseDirs[i];
//            if (Vector3.Dot(d, back) < 0.7f)
//                tmpDirs1.Add(d);
//        }

//        // 2. リンク方向を除外
//        for (int i = 0; i < tmpDirs1.Count; i++)
//        {
//            Vector3 d = tmpDirs1[i];
//            if (!IsLinkedDirection(node, d))
//                tmpDirs2.Add(d);
//        }

//        // 3. 壁方向を除外
//        for (int i = 0; i < tmpDirs2.Count; i++)
//        {
//            Vector3 d = tmpDirs2[i];
//            if (!IsWall(transform.position, d))
//                tmpDirs3.Add(d);
//        }

//        if (tmpDirs3.Count == 0)
//            return null;

//        if (tmpDirs3.Count == 1)
//            return tmpDirs3[0];

//        // 距離評価
//        int bestScore = int.MinValue;
//        Vector3 bestDir = tmpDirs3[0];

//        for (int i = 0; i < tmpDirs3.Count; i++)
//        {
//            Vector3 d = tmpDirs3[i];
//            Vector2Int c = WorldToCell(transform.position + d * cellSize);
//            MapNode near = MapNode.FindByCell(c);

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
//    private void MoveToUnlinked()
//    {
//        tmpDirs1.Clear();
//        tmpDirs2.Clear();
//        tmpDirs3.Clear();

//        Vector3 back = -moveDir;

//        // 1. back除外
//        for (int i = 0; i < 4; i++)
//        {
//            Vector3 d = BaseDirs[i];
//            if (Vector3.Dot(d, back) < 0.7f)
//                tmpDirs1.Add(d);
//        }

//        // 2. リンク除外
//        for (int i = 0; i < tmpDirs1.Count; i++)
//        {
//            Vector3 d = tmpDirs1[i];
//            if (!IsLinkedDirection(currentNode, d))
//                tmpDirs2.Add(d);
//        }

//        // 3. 壁除外
//        for (int i = 0; i < tmpDirs2.Count; i++)
//        {
//            Vector3 d = tmpDirs2[i];

//            Vector3 origin =
//                transform.position +
//                Vector3.up * 0.1f +
//                d * (cellSize * 0.45f);

//            float dist = cellSize * 0.55f;

//            if (!Physics.Raycast(origin, d, dist, wallLayer))
//                tmpDirs3.Add(d);
//        }

//        // 全滅 → 後退
//        if (tmpDirs3.Count == 0)
//        {
//            Debug.Log($"[Unlinked] No valid dirs → BACK at node {currentNode.cell}");
//            moveDir = -moveDir;
//            SafeMove(moveDir);
//            return;
//        }

//        Vector3 chosen = tmpDirs3[Random.Range(0, tmpDirs3.Count)];
//        Debug.Log($"[Unlinked] Choose={chosen} at node {currentNode.cell}");
//        SafeMove(chosen);
//    }
//    private MapNode FindFarthestFromStart()
//    {
//        MapNode farthest = null;
//        int best = -1;

//        foreach (var n in MapNode.allNodes)
//        {
//            if (n.distanceFromStart == int.MaxValue)
//                continue;

//            if (n.distanceFromStart > best)
//            {
//                best = n.distanceFromStart;
//                farthest = n;
//            }
//        }

//        if (farthest != null)
//            Debug.Log($"[FarthestNode] selected {farthest.cell} dist={best}");

//        return farthest;
//    }
//    private bool CanMove(Vector3 dir)
//    {
//        Vector3 origin =
//            transform.position +
//            Vector3.up * 0.1f +
//            dir * (cellSize * 0.45f);

//        float dist = cellSize * 0.55f;

//        return !Physics.Raycast(origin, dir, dist, wallLayer);
//    }

//}

////using UnityEngine;
////using System.Collections.Generic;

////public class CellFromStart : MonoBehaviour
////{
////    [Header("Node Settings")]
////    public GameObject nodePrefab;

////    [Header("Move Settings")]
////    public float moveSpeed = 4f;
////    public float cellSize = 1f;
////    public LayerMask wallLayer;
////    public LayerMask nodeLayer;
////    public bool debugRay = true;
////    public int linkRayMaxSteps = 3;

////    private Vector3 moveDir = Vector3.zero;
////    private bool isMoving = false;
////    private Vector3 targetPos;

////    private MapNode currentNode;

////    private MapNode previousNode = null;

////    // ======================================================
////    // Start
////    // ======================================================
////    private void Start()
////    {
////        // スタート地点のセル
////        Vector2Int startCell = WorldToCell(transform.position);
////        currentNode = TryPlaceNode(CellToWorld(startCell));

////        Debug.Log($"[Player] Start at node {currentNode.cell}");

////        // Node到達時のみ実行
////        TryExploreMove();
////    }

////    // ======================================================
////    // Update
////    // ======================================================
////    private void Update()
////    {
////        MoveStep();
////    }

////    // ======================================================
////    // グリッド変換
////    // ======================================================
////    private Vector2Int WorldToCell(Vector3 pos)
////    {
////        return new Vector2Int(Mathf.RoundToInt(pos.x / cellSize),
////                              Mathf.RoundToInt(pos.z / cellSize));
////    }

////    private Vector3 CellToWorld(Vector2Int cell)
////    {
////        return new Vector3(cell.x * cellSize, 0, cell.y * cellSize);
////    }

////    //// ======================================================
////    //// SafeMove：座標補正を完全廃止、方向だけ設定
////    //// ======================================================
////    //private void SafeMove(Vector3 dir)
////    //{
////    //    if (isMoving) return;

////    //    moveDir = dir.normalized;
////    //    targetPos = transform.position + moveDir * cellSize;
////    //    isMoving = true;
////    //}

////    private void MoveStep()
////    {
////        if (!isMoving) return;

////        transform.position = Vector3.MoveTowards(
////            transform.position,
////            targetPos,
////            moveSpeed * Time.deltaTime
////        );

////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////        {
////            isMoving = false;

////            // ここで初めてセルに到着
////            Vector2Int cell = WorldToCell(transform.position);
////            OnArrivedCell(cell);
////        }
////    }

////    // ======================================================
////    // Nodeに到達した瞬間に呼ばれる
////    // ======================================================
////    private void OnArrivedCell(Vector2Int cell)
////    {
////        MapNode node = TryPlaceNode(CellToWorld(cell));
////        currentNode = node;

////        Debug.Log($"[Arrived] node={node.cell}");

////        TryExploreMove();
////    }

////    // ======================================================
////    // Node生成＋LinkBackward（到達時のみ）
////    // ======================================================
////    private MapNode TryPlaceNode(Vector3 pos)
////    {
////        Vector2Int cell = WorldToCell(pos);
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

////        if (MapNode.StartNode == null)
////        {
////            MapNode.StartNode = node;
////            node.distanceFromStart = 0;
////        }

////        LinkBackward(node);
////        return node;
////    }

////    // ======================================================
////    // Unknown が近場にあるか？
////    // ======================================================
////    private bool NoUnknownInReference()
////    {
////        if (currentNode == null) return false;

////        foreach (var link in currentNode.links)
////        {
////            if (link.unknownCount > 0)
////                return false;
////        }
////        if (currentNode.unknownCount > 0)
////            return false;

////        return true;
////    }

////    // ======================================================
////    // 終端ノード？
////    // ======================================================
////    private bool IsTerminalNode(MapNode node)
////    {
////        return node.links.Count == 1;
////    }

////    // ======================================================
////    // 終端ノードでの分岐
////    // ======================================================
////    private Vector3? ChooseTerminalDirection(MapNode node)
////    {
////        MapNode only = node.links[0];
////        Vector3 dir = (only.transform.position - transform.position).normalized;
////        return dir;
////    }

////    // ======================================================
////    // 未リンク方向へ（fallback）
////    // ======================================================
////    private void MoveToUnlinked()
////    {
////        // 上下左右にRay → Hit無し方向に進む
////        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

////        foreach (var d in dirs)
////        {
////            Vector3 origin = transform.position + Vector3.up * 0.1f;
////            if (!Physics.Raycast(origin, d, cellSize * 0.9f, wallLayer | nodeLayer))
////            {
////                Debug.Log($"[Unlinked] Choose={d} at node {currentNode.cell}");
////                SafeMove(d);
////                return;
////            }
////        }

////        // 全部塞がれていたら何もしない（実質停止）
////        Debug.Log("[Unlinked] No available direction");
////    }

////    // ======================================================
////    // メイン探索処理
////    // ======================================================
////    private void TryExploreMove()
////    {
////        Debug.Log($"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}");

////        // --- 終端ノード ---
////        if (IsTerminalNode(currentNode))
////        {
////            Vector3? d = ChooseTerminalDirection(currentNode);
////            if (d.HasValue)
////            {
////                SafeMove(d.Value);
////                return;
////            }
////            MoveToUnlinked();
////            return;
////        }

////        // --- リンクが無ければ ---
////        if (currentNode.links.Count == 0)
////        {
////            MoveToUnlinked();
////            return;
////        }

////        bool noUnknown = NoUnknownInReference();

////        if (!noUnknown)
////        {
////            // --- 未知がある → スコア方式 ---
////            MapNode next = ChooseNextByScore(currentNode);
////            if (next != null)
////            {
////                Vector3 dir = (next.transform.position - transform.position).normalized;
////                SafeMove(dir);
////                return;
////            }
////        }
////        else
////        {
////            // --- 未知がない → 最遠Nodeへ向かう ---
////            MapNode target = FindFarthestFromStart();

////            if (target != null)
////            {
////                MapNode step = NextNodeToward(target);
////                if (step != null)
////                {
////                    Vector3 dir = (step.transform.position - transform.position).normalized;
////                    Debug.Log($"[Farthest] {currentNode.cell} → target {target.cell}, step {step.cell}");
////                    SafeMove(dir);
////                    return;
////                }
////            }
////        }

////        // --- fallback ---
////        MoveToUnlinked();
////    }

////    // ======================================================
////    // スコア方式（未知数あり時のみ）
////    // ======================================================
////    private MapNode ChooseNextByScore(MapNode node)
////    {
////        MapNode best = null;
////        float bestScore = -99999f;

////        foreach (var next in node.links)
////        {
////            float score = 0;

////            // 未知方向を高評価
////            score += next.unknownCount * 1.0f;

////            // 距離FromStartは低いほうが「反対側」なので悪いスコア
////            score -= next.distanceFromStart * 0.5f;

////            // 後戻りペナルティ
////            if (previousNode != null && next == previousNode)
////                score -= 1.0f;

////            Debug.Log($"[ScoreCheck] {node.cell} → {next.cell} U={next.unknownCount} D={next.distanceFromStart} Score={score}");

////            if (score > bestScore)
////            {
////                bestScore = score;
////                best = next;
////            }
////        }

////        return best;
////    }

////    // ======================================================
////    // 距離が最大の Node を探す
////    // ======================================================
////    private MapNode FindFarthestFromStart()
////    {
////        MapNode far = null;
////        int maxDist = -1;

////        foreach (var n in MapNode.allNodes)
////        {
////            if (n.distanceFromStart > maxDist)
////            {
////                maxDist = n.distanceFromStart;
////                far = n;
////            }
////        }

////        Debug.Log($"[FarthestNode] selected {far.cell} dist={maxDist}");
////        return far;
////    }

////    // ======================================================
////    // target までの BFS → currentNode の次の Node を返す
////    // ======================================================
////    private MapNode NextNodeToward(MapNode target)
////    {
////        if (target == null || currentNode == null)
////            return null;

////        Queue<MapNode> q = new Queue<MapNode>();
////        Dictionary<MapNode, MapNode> parent = new Dictionary<MapNode, MapNode>();

////        q.Enqueue(currentNode);
////        parent[currentNode] = null;

////        // BFS
////        while (q.Count > 0)
////        {
////            var node = q.Dequeue();

////            if (node == target)
////                break;

////            foreach (var next in node.links)
////            {
////                if (!parent.ContainsKey(next))
////                {
////                    parent[next] = node;
////                    q.Enqueue(next);
////                }
////            }
////        }

////        // 到達不可
////        if (!parent.ContainsKey(target))
////            return null;

////        // 親を辿って currentNode の次を求める
////        MapNode step = target;
////        while (parent[step] != currentNode)
////        {
////            step = parent[step];
////            if (step == null)
////                return null;
////        }

////        return step;
////    }

////    // ======================================================
////    // LinkBackward：来た方向の逆にレイキャストしてリンク
////    // ======================================================
////    private void LinkBackward(MapNode node)
////    {
////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
////        Vector3 dir = -moveDir;

////        Debug.Log($"[LinkBackward] node={node.cell} moveDir={moveDir} → dir={dir}");

////        LayerMask mask = wallLayer | nodeLayer;

////        for (int step = 1; step <= linkRayMaxSteps; step++)
////        {
////            float dist = cellSize * step;

////            if (debugRay)
////                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);

////            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
////            {
////                int layer = hit.collider.gameObject.layer;

////                // 壁ヒット
////                if ((wallLayer.value & (1 << layer)) != 0)
////                {
////                    Debug.Log($"[RayHit] {node.cell} hit=Wall layer={layer} dist={step}");
////                    Debug.Log("[LinkBackward] STOP: wall hit");
////                    return;
////                }

////                // Nodeヒット
////                if ((nodeLayer.value & (1 << layer)) != 0)
////                {
////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();

////                    Debug.Log($"[RayHit] {node.cell} hit={hitNode.cell} layer={layer} dist={step}");

////                    if (hitNode != null && hitNode != node)
////                    {
////                        node.AddLink(hitNode);
////                        hitNode.AddLink(node);
////                        node.RecalculateUnknownAndWall();
////                        hitNode.RecalculateUnknownAndWall();
////                        Debug.Log($"[LinkBackward] LINK SUCCESS {node.cell} <--> {hitNode.cell}");
////                    }
////                    return;
////                }
////            }
////        }
////    }

////    // ======================================================
////    // SafeMove：一歩だけ「方向」に動く
////    // ======================================================
////    private void SafeMove(Vector3 dir)
////    {
////        moveDir = dir;  // ★ここが LinkBackward の逆方向になる

////        Vector3 next = transform.position + dir * cellSize;
////        targetPos = next;

////        Debug.Log($"[SafeMove] Move {dir} from {transform.position}");
////        isMoving = true;
////    }

////    // ======================================================
////    // MoveToTarget：壁なら停止、セル到達で node 生成
////    // ======================================================
////    private void MoveToTarget()
////    {
////        if (!isMoving) return;

////        Vector3 dir = (targetPos - transform.position).normalized;

////        // 壁チェック
////        Vector3 origin = transform.position + Vector3.up * 0.1f;
////        if (Physics.Raycast(origin, dir, 0.5f, wallLayer))
////        {
////            Debug.Log($"[MoveBlock] STOP at {transform.position + dir * 0.5f} DIR={dir} (Wall detected)");
////            isMoving = false;
////            return;
////        }

////        // 移動
////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

////        // 到着
////        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
////        {
////            isMoving = false;

////            // ★ この瞬間のみ Node を扱う
////            Vector3 snapped = SnapToGrid(transform.position);
////            transform.position = snapped;

////            MapNode node = TryPlaceNode(transform.position);
////            previousNode = currentNode;
////            currentNode = node;

////            // 再探索
////            TryExploreMove();
////        }
////    }

////    private Vector3 SnapToGrid(Vector3 pos)
////    {
////        int x = Mathf.RoundToInt(pos.x / cellSize);
////        int z = Mathf.RoundToInt(pos.z / cellSize);
////        return new Vector3(x * cellSize, 0, z * cellSize);
////    }
////}


//////using UnityEngine;
//////using System.Collections.Generic;

///////// <summary>
///////// C版：LINQ完全撤廃・TempAllocゼロ・高速迷路探索AI
///////// ・Unknown + DistanceFromStart のハイブリッド探索
///////// ・終端は Unknown最優先、複数なら Distance優先
///////// ・壁方向へ絶対進まない
///////// ・毎フレームのGC Alloc = 0
///////// </summary>
//////public class CellFromStart : MonoBehaviour
//////{
//////    // ======================================================
//////    // パラメータ
//////    // ======================================================

//////    [Header("移動設定")]
//////    public float moveSpeed = 3f;
//////    public float cellSize = 1f;
//////    public float rayDistance = 1f;
//////    public LayerMask wallLayer;
//////    public LayerMask nodeLayer;

//////    [Header("初期設定")]
//////    public Vector3 startDirection = Vector3.forward;
//////    public Vector3 gridOrigin = Vector3.zero;
//////    public GameObject nodePrefab;

//////    [Header("探索パラメータ")]
//////    public int unknownReferenceDepth = 3;

//////    [Header("スコア重み")]
//////    public float weightUnknown = 1f;
//////    public float weightDistance = 1f;

//////    [Header("Ray設定")]
//////    public int linkRayMaxSteps = 100;

//////    [Header("デバッグ")]
//////    public bool debugLog = false;
//////    public bool debugRay = false;

//////    [SerializeField] private Renderer bodyRenderer;
//////    [SerializeField] private Material exploreMaterial;


//////    // ======================================================
//////    // 内部状態
//////    // ======================================================

//////    private Vector3 moveDir;
//////    private bool isMoving = false;
//////    private Vector3 targetPos;
//////    private MapNode currentNode;

//////    private readonly List<MapNode> recentNodes = new List<MapNode>(8);

//////    // 再利用方向リスト（GCゼロ）
//////    private static readonly Vector3[] BaseDirs =
//////    {
//////        Vector3.forward,
//////        Vector3.back,
//////        Vector3.left,
//////        Vector3.right
//////    };

//////    private readonly List<Vector3> tmpDirs1 = new List<Vector3>(4);
//////    private readonly List<Vector3> tmpDirs2 = new List<Vector3>(4);
//////    private readonly List<Vector3> tmpDirs3 = new List<Vector3>(4);


//////    // ======================================================
//////    // Start / Update
//////    // ======================================================

//////    private void Start()
//////    {
//////        moveDir = startDirection.normalized;
//////        transform.position = SnapToGrid(transform.position);
//////        targetPos = transform.position;

//////        if (bodyRenderer && exploreMaterial)
//////            bodyRenderer.material = exploreMaterial;

//////        currentNode = TryPlaceNode(transform.position);
//////        RegisterCurrentNode(currentNode);
//////    }

//////    private void Update()
//////    {
//////        if (!isMoving)
//////        {
//////            if (CanPlaceNodeHere())
//////                TryExploreMove();
//////            else
//////                SafeMove(moveDir);
//////        }
//////        else
//////        {
//////            MoveToTarget();
//////        }
//////    }


//////    // ======================================================
//////    // Node 設置
//////    // ======================================================

//////    private MapNode TryPlaceNode(Vector3 pos)
//////    {
//////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//////        MapNode node;

//////        if (MapNode.allNodeCells.Contains(cell))
//////        {
//////            node = MapNode.FindByCell(cell);
//////        }
//////        else
//////        {
//////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//////            node = obj.GetComponent<MapNode>();
//////            node.cell = cell;
//////            MapNode.allNodeCells.Add(cell);
//////        }

//////        if (MapNode.StartNode == null)
//////        {
//////            MapNode.StartNode = node;
//////            node.distanceFromStart = 0;
//////        }

//////        LinkBackward(node);
//////        return node;
//////    }


//////    // ======================================================
//////    // TryExploreMove（探索中心）
//////    // ======================================================

//////    //private void TryExploreMove()
//////    //{
//////    //    Debug.Log(
//////    //        $"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}"
//////    //    );

//////    //    currentNode = TryPlaceNode(transform.position);
//////    //    RegisterCurrentNode(currentNode);

//////    //    // ① 終端Node
//////    //    if (IsTerminalNode(currentNode))
//////    //    {
//////    //        Vector3? dir = ChooseTerminalDirection(currentNode);
//////    //        if (dir.HasValue)
//////    //            SafeMove(dir.Value);
//////    //        else
//////    //            MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    // ② 孤立 Node（リンクゼロ）
//////    //    if (currentNode.links.Count == 0)
//////    //    {
//////    //        MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    // ③ スコアで選択
//////    //    MapNode next = ChooseNextByScore(currentNode);
//////    //    if (next != null)
//////    //    {
//////    //        Vector3 dir = (next.transform.position - transform.position).normalized;
//////    //        SafeMove(dir);
//////    //        return;
//////    //    }

//////    //    if (NoUnknownInReference())
//////    //    {
//////    //        MapNode target = FindNearestUnknown(currentNode);

//////    //        if (target != null)
//////    //        {
//////    //            MapNode step = NextNodeToward(target);
//////    //            if (step != null)
//////    //            {
//////    //                Vector3 dir = (step.transform.position - transform.position).normalized;
//////    //                Debug.Log($"[Fallback] Toward Unknown {target.cell} → step {step.cell}");
//////    //                SafeMove(dir);
//////    //                return;
//////    //            }
//////    //        }
//////    //    }

//////    //    // ④ fallback
//////    //    MoveToUnlinked();
//////    //}
//////    //private void TryExploreMove()
//////    //{
//////    //    Debug.Log(
//////    //        $"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}"
//////    //    );

//////    //    currentNode = TryPlaceNode(transform.position);
//////    //    RegisterCurrentNode(currentNode);

//////    //    // ① 終端Node
//////    //    if (IsTerminalNode(currentNode))
//////    //    {
//////    //        Vector3? dir = ChooseTerminalDirection(currentNode);
//////    //        if (dir.HasValue)
//////    //            SafeMove(dir.Value);
//////    //        else
//////    //            MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    // ② 孤立 Node（リンクゼロ）
//////    //    if (currentNode.links.Count == 0)
//////    //    {
//////    //        MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    // ③ スコアで選択
//////    //    MapNode next = ChooseNextByScore(currentNode);
//////    //    if (next != null)
//////    //    {
//////    //        Vector3 dir = (next.transform.position - transform.position).normalized;

//////    //        // ★★ 修正：prevNode がないので「戻り方向」を moveDir で判定する
//////    //        if (Vector3.Dot(dir, moveDir) < -0.9f)
//////    //        {
//////    //            Debug.Log($"[FallbackTrigger] Back direction suggested → fallback at node {currentNode.cell}");
//////    //            goto FALLBACK;
//////    //        }

//////    //        SafeMove(dir);
//////    //        return;
//////    //    }

//////    //FALLBACK:

//////    //    // fallback条件：unknownReferenceDepth すべて U=0
//////    //    //if (NoUnknownInReference())
//////    //    //{
//////    //    //    MapNode target = FindNearestUnknown(currentNode);

//////    //    //    if (target != null)
//////    //    //    {
//////    //    //        MapNode step = NextNodeToward(target);
//////    //    //        if (step != null)
//////    //    //        {
//////    //    //            Vector3 dir = (step.transform.position - transform.position).normalized;
//////    //    //            Debug.Log($"[Fallback] Toward Unknown {target.cell} → step {step.cell}");
//////    //    //            SafeMove(dir);
//////    //    //            return;
//////    //    //        }
//////    //    //    }
//////    //    //}
//////    //    // ★ Start から最も遠い Node を fallback ターゲットにする
//////    //    if (NoUnknownInReference())
//////    //    {
//////    //        MapNode target = FindFarthestFromStart();   // ← ここが修正ポイント！

//////    //        if (target != null)
//////    //        {
//////    //            MapNode step = NextNodeToward(target);  // 最短経路から1歩先を取得
//////    //            if (step != null)
//////    //            {
//////    //                Vector3 dir = (step.transform.position - transform.position).normalized;
//////    //                Debug.Log($"[Fallback-Farthest] Toward farthest {target.cell} → step {step.cell}");
//////    //                SafeMove(dir);
//////    //                return;
//////    //            }
//////    //        }
//////    //    }

//////    //    // ④ fallback (未探索方向へ)
//////    //    MoveToUnlinked();
//////    //}
//////    //private void TryExploreMove()
//////    //{
//////    //    Debug.Log($"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}");

//////    //    // --- 現状維持 ---
//////    //    currentNode = TryPlaceNode(transform.position);
//////    //    RegisterCurrentNode(currentNode);

//////    //    // --- 現状維持：終端ノード処理 ---
//////    //    if (IsTerminalNode(currentNode))
//////    //    {
//////    //        Vector3? d = ChooseTerminalDirection(currentNode);
//////    //        if (d.HasValue)
//////    //            SafeMove(d.Value);
//////    //        else
//////    //            MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    // --- 現状維持：孤立ノード ---
//////    //    if (currentNode.links.Count == 0)
//////    //    {
//////    //        MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    // --- ★追加：unknown が “再び” 現れていればスコア探索へ戻る ---
//////    //    // --- ★修正：unknown があるならスコア探索、無ければ最遠ノード探索 ---
//////    //    if (!NoUnknownInReference())
//////    //    {
//////    //        MapNode next = ChooseNextByScore(currentNode);
//////    //        if (next != null)
//////    //        {
//////    //            Vector3 dir = (next.transform.position - transform.position).normalized;
//////    //            SafeMove(dir);
//////    //            return;
//////    //        }
//////    //    }
//////    //    else   // ★ unknown=0 のときはここが必ず実行される
//////    //    {
//////    //        MapNode farthest = FindFarthestFromStart();
//////    //        if (farthest != null)
//////    //        {
//////    //            MapNode step = NextNodeToward(farthest);
//////    //            if (step != null)
//////    //            {
//////    //                Vector3 dir = (step.transform.position - transform.position).normalized;
//////    //                Debug.Log($"[Farthest] {currentNode.cell} → target {farthest.cell}, step {step.cell}");
//////    //                SafeMove(dir);
//////    //                return;
//////    //            }
//////    //        }
//////    //    }

//////    //    //if (!NoUnknownInReference())
//////    //    //{
//////    //    //    MapNode next = ChooseNextByScore(currentNode);
//////    //    //    if (next != null)
//////    //    //    {
//////    //    //        Vector3 dir = (next.transform.position - transform.position).normalized;
//////    //    //        SafeMove(dir);
//////    //    //        return;
//////    //    //    }
//////    //    //}
//////    //    //else
//////    //    //{
//////    //    //    MapNode farthest = FindFarthestFromStart();
//////    //    //    if (farthest != null)
//////    //    //    {
//////    //    //        MapNode step = NextNodeToward(farthest);
//////    //    //        if (step != null)
//////    //    //        {
//////    //    //            Vector3 dir = (step.transform.position - transform.position).normalized;
//////    //    //            Debug.Log($"[Farthest] {currentNode.cell} → target {farthest.cell}, step {step.cell}");
//////    //    //            SafeMove(dir);
//////    //    //            return;
//////    //    //        }
//////    //    //    }
//////    //    //}

//////    //    //// --- ★ここが本修正：unknown=0 → Start から最も遠いノードへ向かう ---
//////    //    //if (NoUnknownInReference())
//////    //    //{
//////    //    //    MapNode farthest = FindFarthestFromStart();
//////    //    //    if (farthest != null)
//////    //    //    {
//////    //    //        MapNode step = NextNodeToward(farthest);
//////    //    //        if (step != null)
//////    //    //        {
//////    //    //            Vector3 dir = (step.transform.position - transform.position).normalized;
//////    //    //            Debug.Log($"[Farthest] {currentNode.cell} → target {farthest.cell}, step {step.cell}");
//////    //    //            SafeMove(dir);
//////    //    //            return;
//////    //    //        }
//////    //    //    }
//////    //    //}

//////    //    // --- fallback（現状維持） ---
//////    //    MoveToUnlinked();
//////    //}
//////    //private void TryExploreMove()
//////    //{
//////    //    Debug.Log($"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}");

//////    //    currentNode = TryPlaceNode(transform.position);
//////    //    RegisterCurrentNode(currentNode);

//////    //    // --- 終端ノード ---
//////    //    if (IsTerminalNode(currentNode))
//////    //    {
//////    //        Vector3? d = ChooseTerminalDirection(currentNode);
//////    //        if (d.HasValue) SafeMove(d.Value);
//////    //        else MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    // --- リンクゼロ ---
//////    //    if (currentNode.links.Count == 0)
//////    //    {
//////    //        MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    // ======================================================
//////    //    // ★ A 方式の本体（unknown があるか → スコア / 無い → 最遠へ）
//////    //    // ======================================================
//////    //    bool noUnknown = NoUnknownInReference();

//////    //    if (!noUnknown)
//////    //    {
//////    //        // ---（１）未知がある → スコア方式 ---
//////    //        MapNode next = ChooseNextByScore(currentNode);
//////    //        if (next != null)
//////    //        {
//////    //            Vector3 dir = (next.transform.position - transform.position).normalized;
//////    //            SafeMove(dir);
//////    //            return;
//////    //        }
//////    //    }
//////    //    else
//////    //    {
//////    //        // ---（２）未知が無い → 最遠Nodeへ ---
//////    //        MapNode target = FindFarthestFromStart();

//////    //        if (target != null)
//////    //        {
//////    //            MapNode step = NextNodeToward(target);
//////    //            if (step != null)
//////    //            {
//////    //                Vector3 dir = (step.transform.position - transform.position).normalized;
//////    //                Debug.Log($"[Farthest] {currentNode.cell} → target {target.cell}, step {step.cell}");
//////    //                SafeMove(dir);
//////    //                return;
//////    //            }
//////    //        }
//////    //    }

//////    //    // --- fallback ---
//////    //    MoveToUnlinked();
//////    //}
//////    private void TryExploreMove()
//////    {
//////        Debug.Log($"[TryExploreMove] Node={currentNode.cell} U={currentNode.unknownCount} D={currentNode.distanceFromStart}");

//////        currentNode = TryPlaceNode(transform.position);
//////        RegisterCurrentNode(currentNode);

//////        // --- 終端ノード ---
//////        if (IsTerminalNode(currentNode))
//////        {
//////            Vector3? d = ChooseTerminalDirection(currentNode);
//////            if (d.HasValue) SafeMove(d.Value);
//////            else MoveToUnlinked();
//////            return;
//////        }

//////        // --- リンク無し ---
//////        if (currentNode.links.Count == 0)
//////        {
//////            MoveToUnlinked();
//////            return;
//////        }

//////        // ======================================================
//////        // ☆ A方式：
//////        // 1. 近場の unknown があるなら → スコア方式
//////        // 2. 近場に unknown が無いなら → Startから最も遠いNodeへ
//////        // （どちらに移動した後も、次フレームで必ず再計算される）
//////        // ======================================================

//////        if (!NoUnknownInReference())
//////        {
//////            // ---（１）未知がある：スコア方式 ---
//////            MapNode next = ChooseNextByScore(currentNode);
//////            if (next != null)
//////            {
//////                Vector3 dir = (next.transform.position - transform.position).normalized;
//////                SafeMove(dir);
//////                return;
//////            }
//////        }

//////        // ---（２）未知が無い：Startから最も遠いNodeへ ---
//////        MapNode target = FindFarthestFromStart();

//////        if (target != null)
//////        {
//////            MapNode step = NextNodeToward(target);
//////            if (step != null)
//////            {
//////                Vector3 dir = (step.transform.position - transform.position).normalized;
//////                Debug.Log($"[Farthest] {currentNode.cell} → target {target.cell}, step {step.cell}");
//////                SafeMove(dir);
//////                return;
//////            }
//////        }

//////        // --- fallback ---
//////        MoveToUnlinked();
//////    }




//////    //private bool NoUnknownInReference()
//////    //{
//////    //    for (int i = 0; i < recentNodes.Count; i++)
//////    //    {
//////    //        if (recentNodes[i] != null && recentNodes[i].unknownCount > 0)
//////    //            return false;
//////    //    }
//////    //    return true;
//////    //}
//////    // ------------------------------------------------------
//////    //  周囲リンク（currentNode.links）に unknown が存在するか？
//////    //  → true なら「unknown が無い」
//////    //  → false なら「unknown が1つでもある」
//////    // ------------------------------------------------------
//////    //private bool NoUnknownInReference()
//////    //{
//////    //    if (currentNode == null) return false;

//////    //    // currentNode と隣接ノード全ての unknownCount を確認
//////    //    foreach (var link in currentNode.links)
//////    //    {
//////    //        if (link.unknownCount > 0)
//////    //        {
//////    //            // どれか1つでも unknown > 0 があれば探索すべき
//////    //            return false;
//////    //        }
//////    //    }

//////    //    // current も調べる（初期ノードなどで必要）
//////    //    if (currentNode.unknownCount > 0)
//////    //        return false;

//////    //    return true; // 近場のunknownは本当に全部0
//////    //}
//////    private bool NoUnknownInReference()
//////    {
//////        if (currentNode == null) return false;

//////        // BFS で unknownReferenceDepth まで探索
//////        Queue<(MapNode node, int depth)> q = new Queue<(MapNode, int)>();
//////        HashSet<MapNode> visited = new HashSet<MapNode>();

//////        q.Enqueue((currentNode, 0));
//////        visited.Add(currentNode);

//////        while (q.Count > 0)
//////        {
//////            var (node, depth) = q.Dequeue();

//////            // unknown が見つかったら false（＝Unknown はまだある）
//////            if (node.unknownCount > 0)
//////                return false;

//////            if (depth >= unknownReferenceDepth)
//////                continue;

//////            foreach (var next in node.links)
//////            {
//////                if (!visited.Contains(next))
//////                {
//////                    visited.Add(next);
//////                    q.Enqueue((next, depth + 1));
//////                }
//////            }
//////        }

//////        // すべての探索深度で unknown=0 だった
//////        return true;
//////    }

//////    //private MapNode FindFarthestFromStart()
//////    //{
//////    //    MapNode farthest = null;
//////    //    int bestDist = -1;

//////    //    for (int i = 0; i < MapNode.allNodes.Count; i++)
//////    //    {
//////    //        MapNode n = MapNode.allNodes[i];
//////    //        if (n.distanceFromStart > bestDist)
//////    //        {
//////    //            bestDist = n.distanceFromStart;
//////    //            farthest = n;
//////    //        }
//////    //    }

//////    //    Debug.Log($"[Farthest] farthest={farthest.cell} dist={bestDist}");
//////    //    return farthest;
//////    //}


//////    //private MapNode FindNearestUnknown(MapNode start)
//////    //{
//////    //    Queue<MapNode> q = new Queue<MapNode>();
//////    //    HashSet<MapNode> visited = new HashSet<MapNode>();

//////    //    q.Enqueue(start);
//////    //    visited.Add(start);

//////    //    while (q.Count > 0)
//////    //    {
//////    //        MapNode node = q.Dequeue();

//////    //        if (node.unknownCount > 0)
//////    //            return node;

//////    //        for (int i = 0; i < node.links.Count; i++)
//////    //        {
//////    //            MapNode next = node.links[i];
//////    //            if (!visited.Contains(next))
//////    //            {
//////    //                visited.Add(next);
//////    //                q.Enqueue(next);
//////    //            }
//////    //        }
//////    //    }
//////    //    return null;
//////    //}

//////    // ★ Start から最も距離が大きい Node を探す
//////    //private MapNode FindFarthestFromStart()
//////    //{
//////    //    MapNode farthest = null;
//////    //    int maxDist = int.MinValue;

//////    //    foreach (var n in MapNode.allNodes)
//////    //    {
//////    //        if (n.distanceFromStart > maxDist)
//////    //        {
//////    //            maxDist = n.distanceFromStart;
//////    //            farthest = n;
//////    //        }
//////    //    }

//////    //    Debug.Log($"[FarthestNode] selected {farthest?.cell} dist={maxDist}");
//////    //    return farthest;
//////    //}
//////    private MapNode FindFarthestFromStart()
//////    {
//////        MapNode farthest = null;
//////        int best = -1;

//////        foreach (var n in MapNode.allNodes)
//////        {
//////            // 到達不能ノード（int.MaxValue）は除外
//////            if (n.distanceFromStart == int.MaxValue)
//////                continue;

//////            if (n.distanceFromStart > best)
//////            {
//////                best = n.distanceFromStart;
//////                farthest = n;
//////            }
//////        }

//////        if (farthest != null)
//////            Debug.Log($"[FarthestNode] selected {farthest.cell} dist={best}");

//////        return farthest;
//////    }


//////    //private MapNode NextNodeToward(MapNode target)
//////    //{
//////    //    Queue<MapNode> q = new Queue<MapNode>();
//////    //    Dictionary<MapNode, MapNode> parent = new Dictionary<MapNode, MapNode>();

//////    //    q.Enqueue(currentNode);
//////    //    parent[currentNode] = null;

//////    //    while (q.Count > 0)
//////    //    {
//////    //        MapNode node = q.Dequeue();

//////    //        if (node == target)
//////    //        {
//////    //            MapNode cur = target;
//////    //            MapNode prev = parent[cur];

//////    //            while (prev != currentNode)
//////    //            {
//////    //                cur = prev;
//////    //                prev = parent[cur];
//////    //            }

//////    //            return cur;
//////    //        }

//////    //        for (int i = 0; i < node.links.Count; i++)
//////    //        {
//////    //            MapNode next = node.links[i];
//////    //            if (!parent.ContainsKey(next))
//////    //            {
//////    //                parent[next] = node;
//////    //                q.Enqueue(next);
//////    //            }
//////    //        }
//////    //    }

//////    //    return null;
//////    //}
//////    //private MapNode NextNodeToward(MapNode target)
//////    //{
//////    //    if (target == null || currentNode == null) return null;

//////    //    Queue<MapNode> q = new Queue<MapNode>();
//////    //    Dictionary<MapNode, MapNode> parent = new Dictionary<MapNode, MapNode>();

//////    //    q.Enqueue(currentNode);
//////    //    parent[currentNode] = null;

//////    //    while (q.Count > 0)
//////    //    {
//////    //        MapNode node = q.Dequeue();

//////    //        if (node == target)
//////    //            break;

//////    //        foreach (var next in node.links)
//////    //        {
//////    //            if (!parent.ContainsKey(next))
//////    //            {
//////    //                parent[next] = node;
//////    //                q.Enqueue(next);
//////    //            }
//////    //        }
//////    //    }

//////    //    // 辿れない
//////    //    if (!parent.ContainsKey(target))
//////    //        return null;

//////    //    // 逆戻りして currentNode の次の1ステップを探す
//////    //    MapNode step = target;
//////    //    while (parent[step] != currentNode)
//////    //    {
//////    //        step = parent[step];
//////    //        if (step == null) return null;  // 安全対策
//////    //    }

//////    //    return step;   // これが1ステップ先
//////    //}
//////    //private MapNode NextNodeToward(MapNode target)
//////    //{
//////    //    if (target == null || currentNode == null)
//////    //        return null;

//////    //    Queue<MapNode> q = new Queue<MapNode>();
//////    //    Dictionary<MapNode, MapNode> parent = new Dictionary<MapNode, MapNode>();

//////    //    q.Enqueue(currentNode);
//////    //    parent[currentNode] = null;

//////    //    // --- BFS で target まで到達するまで全部探索 ---
//////    //    while (q.Count > 0)
//////    //    {
//////    //        MapNode node = q.Dequeue();

//////    //        if (node == target)
//////    //            break;

//////    //        foreach (var next in node.links)
//////    //        {
//////    //            if (!parent.ContainsKey(next))
//////    //            {
//////    //                parent[next] = node;
//////    //                q.Enqueue(next);
//////    //            }
//////    //        }
//////    //    }

//////    //    // たどれなかった
//////    //    if (!parent.ContainsKey(target))
//////    //        return null;

//////    //    // --- 親を辿って currentNode の1つ先を特定 ---
//////    //    MapNode step = target;

//////    //    while (parent[step] != currentNode)
//////    //    {
//////    //        step = parent[step];

//////    //        if (step == null)
//////    //            return null; // 安全対策
//////    //    }

//////    //    return step;
//////    //}
//////    private MapNode NextNodeToward(MapNode target)
//////    {
//////        if (target == null || currentNode == null)
//////            return null;

//////        Queue<MapNode> q = new Queue<MapNode>();
//////        Dictionary<MapNode, MapNode> parent = new Dictionary<MapNode, MapNode>();

//////        q.Enqueue(currentNode);
//////        parent[currentNode] = null;

//////        bool found = false;

//////        // --- BFS を最後まで実行する（途中 break しない） ---
//////        while (q.Count > 0)
//////        {
//////            MapNode node = q.Dequeue();

//////            foreach (var next in node.links)
//////            {
//////                if (!parent.ContainsKey(next))
//////                {
//////                    parent[next] = node;
//////                    q.Enqueue(next);

//////                    if (next == target)
//////                        found = true;
//////                }
//////            }
//////        }

//////        // 到達できない
//////        if (!found) return null;

//////        // --- 親チェーンをさかのぼり、currentNode の1歩先を求める ---
//////        MapNode step = target;
//////        while (parent[step] != currentNode)
//////        {
//////            step = parent[step];

//////            if (step == null)
//////                return null;
//////        }

//////        return step;
//////    }



//////    // ======================================================
//////    // 終端 Node の方向選択
//////    // ======================================================

//////    private Vector3? ChooseTerminalDirection(MapNode node)
//////    {
//////        tmpDirs1.Clear();
//////        tmpDirs2.Clear();
//////        tmpDirs3.Clear();

//////        Vector3 back = -moveDir;

//////        // 1. backを除外
//////        for (int i = 0; i < 4; i++)
//////        {
//////            Vector3 d = BaseDirs[i];
//////            if (Vector3.Dot(d, back) < 0.7f)
//////                tmpDirs1.Add(d);
//////        }

//////        // 2. リンク方向を除外
//////        for (int i = 0; i < tmpDirs1.Count; i++)
//////        {
//////            Vector3 d = tmpDirs1[i];
//////            if (!IsLinkedDirection(node, d))
//////                tmpDirs2.Add(d);
//////        }

//////        // 3. 壁方向を除外
//////        for (int i = 0; i < tmpDirs2.Count; i++)
//////        {
//////            Vector3 d = tmpDirs2[i];
//////            if (!IsWall(transform.position, d))
//////                tmpDirs3.Add(d);
//////        }

//////        if (tmpDirs3.Count == 0)
//////            return null;

//////        if (tmpDirs3.Count == 1)
//////            return tmpDirs3[0];

//////        // Distance評価
//////        int bestScore = int.MinValue;
//////        Vector3 bestDir = tmpDirs3[0];

//////        for (int i = 0; i < tmpDirs3.Count; i++)
//////        {
//////            Vector3 d = tmpDirs3[i];
//////            Vector2Int c = WorldToCell(transform.position + d * cellSize);
//////            MapNode near = MapNode.FindByCell(c);

//////            if (near == null) continue;

//////            int score = -near.distanceFromStart;
//////            if (score > bestScore)
//////            {
//////                bestScore = score;
//////                bestDir = d;
//////            }
//////        }

//////        return bestDir;
//////    }


//////    // ======================================================
//////    // MoveToUnlinked（未知方向へ）
//////    // ======================================================

//////    private void MoveToUnlinked()
//////    {
//////        tmpDirs1.Clear();
//////        tmpDirs2.Clear();
//////        tmpDirs3.Clear();

//////        Vector3 back = -moveDir;

//////        // 1. back除外
//////        for (int i = 0; i < 4; i++)
//////        {
//////            Vector3 d = BaseDirs[i];
//////            if (Vector3.Dot(d, back) < 0.7f)
//////                tmpDirs1.Add(d);
//////        }

//////        // 2. リンク除外
//////        for (int i = 0; i < tmpDirs1.Count; i++)
//////        {
//////            Vector3 d = tmpDirs1[i];
//////            if (!IsLinkedDirection(currentNode, d))
//////                tmpDirs2.Add(d);
//////        }

//////        // 3. 壁除外（★ここだけ修正）
//////        for (int i = 0; i < tmpDirs2.Count; i++)
//////        {
//////            Vector3 d = tmpDirs2[i];

//////            Vector3 origin =
//////                transform.position +
//////                Vector3.up * 0.1f +
//////                d * (cellSize * 0.45f);  // ← UnknownQuantity と同じ

//////            float dist = cellSize * 0.55f;

//////            if (!Physics.Raycast(origin, d, dist, wallLayer))
//////            {
//////                tmpDirs3.Add(d);
//////            }
//////        }

//////        // ★完全に行けない → 後退
//////        if (tmpDirs3.Count == 0)
//////        {
//////            Debug.Log($"[Unlinked] No valid dirs → BACK to {(-moveDir)} at node {currentNode.cell}");
//////            moveDir = -moveDir;
//////            MoveForward();
//////            return;
//////        }

//////        Vector3 chosen = tmpDirs3[Random.Range(0, tmpDirs3.Count)];
//////        Debug.Log($"[Unlinked] Choose={chosen} at node {currentNode.cell}");
//////        SafeMove(chosen);
//////    }


//////    // ======================================================
//////    // 通常スコア選択
//////    // ======================================================

//////    //private MapNode ChooseNextByScore(MapNode current)
//////    //{
//////    //    // 履歴の未知数最大Nodeが current なら未知探索優先
//////    //    MapNode bestHist = null;
//////    //    int maxU = -1;

//////    //    for (int i = 0; i < recentNodes.Count; i++)
//////    //    {
//////    //        MapNode n = recentNodes[i];
//////    //        if (n != null && n.unknownCount > maxU)
//////    //        {
//////    //            maxU = n.unknownCount;
//////    //            bestHist = n;
//////    //        }
//////    //    }

//////    //    if (bestHist == current)
//////    //        return null;

//////    //    // links からスコア最大を選ぶ
//////    //    MapNode best = null;
//////    //    float bestScore = float.NegativeInfinity;

//////    //    for (int i = 0; i < current.links.Count; i++)
//////    //    {
//////    //        MapNode n = current.links[i];
//////    //        float score =
//////    //            weightUnknown * n.unknownCount +
//////    //            weightDistance * (-n.distanceFromStart);

//////    //        Debug.Log($"[ScoreCheck] from {current.cell} → {n.cell}  U={n.unknownCount} D={n.distanceFromStart} Score={score}");

//////    //        if (score > bestScore)
//////    //        {
//////    //            bestScore = score;
//////    //            best = n;
//////    //        }
//////    //    }

//////    //    return best;
//////    //}
//////    private MapNode ChooseNextByScore(MapNode current)
//////    {
//////        // --- recentNodes の最大 unknownCount 判定（現状維持） ---
//////        MapNode bestHist = null;
//////        int maxU = -1;

//////        for (int i = 0; i < recentNodes.Count; i++)
//////        {
//////            MapNode n = recentNodes[i];
//////            if (n != null && n.unknownCount > maxU)
//////            {
//////                maxU = n.unknownCount;
//////                bestHist = n;
//////            }
//////        }

//////        // ★修正：現在と履歴が両方 unknown=0 のときは return null にしない
//////        if (!(maxU == 0 && current.unknownCount == 0))
//////        {
//////            if (bestHist == current)
//////            {
//////                Debug.Log("[ScoreCheck] bestHist == current → skip");
//////                return null;
//////            }
//////        }

//////        // --- スコア計算 ---
//////        MapNode best = null;
//////        float bestScore = float.NegativeInfinity;

//////        foreach (var n in current.links)
//////        {
//////            Vector3 dir = (n.transform.position - transform.position).normalized;

//////            float score =
//////                weightUnknown * n.unknownCount +
//////                weightDistance * (-n.distanceFromStart);

//////            // ★修正：後退方向は“禁止”ではなく“減点のみ”
//////            float dot = Vector3.Dot(dir, moveDir);
//////            if (dot < -0.9f)
//////            {
//////                score -= 0.5f;
//////                Debug.Log($"[ScoreCheck] BackPenalty {current.cell} → {n.cell} Score={score}");
//////            }
//////            else
//////            {
//////                Debug.Log($"[ScoreCheck] {current.cell} → {n.cell} U={n.unknownCount} D={n.distanceFromStart} Score={score}");
//////            }

//////            if (score > bestScore)
//////            {
//////                bestScore = score;
//////                best = n;
//////            }
//////        }

//////        return best;
//////    }


//////    // ======================================================
//////    // 安全移動（壁チェック）
//////    // ======================================================

//////    private bool CanMove(Vector3 dir)
//////    {
//////        // ★ UnknownQuantity と同じ壁検出方式（半歩前から Ray）
//////        Vector3 origin =
//////            transform.position +
//////            Vector3.up * 0.1f +
//////            dir * (cellSize * 0.45f);   // ← プレイヤー中心ではなく“前方0.45マス”が重要

//////        float dist = cellSize * 0.55f;

//////        return !Physics.Raycast(origin, dir, dist, wallLayer);
//////    }

//////    //private void SafeMove(Vector3 dir)
//////    //{
//////    //    if (!CanMove(dir))
//////    //    {
//////    //        Debug.Log($"[SafeMove] Cannot move {dir} from {transform.position} → fallback");
//////    //        MoveToUnlinked();
//////    //        return;
//////    //    }

//////    //    Debug.Log($"[SafeMove] Move {dir} from {transform.position}");
//////    //    moveDir = dir;
//////    //    MoveForward();
//////    //}
//////    private void SafeMove(Vector3 dir)
//////    {
//////        moveDir = dir;

//////        Vector3 nextPos = transform.position + dir * cellSize;

//////        // 壁チェック → MoveToTarget に任せる
//////        targetPos = nextPos;
//////    }


//////    // ======================================================
//////    // 移動処理
//////    // ======================================================

//////    private void MoveForward()
//////    {
//////        targetPos = SnapToGrid(transform.position + moveDir * cellSize);
//////        isMoving = true;
//////    }

//////    private void MoveToTarget()
//////    {
//////        // ★ 毎フレーム、次の方向が壁でないか確認
//////        Vector3 dir = (targetPos - transform.position).normalized;

//////        // もし進行方向が壁なら、強制停止
//////        if (!CanMove(dir))
//////        {
//////            Debug.Log($"[MoveBlock] STOP at {transform.position} DIR={dir} (Wall detected)");
//////            isMoving = false;
//////            transform.position = SnapToGrid(transform.position);
//////            return;
//////        }

//////        // 通常移動
//////        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
//////        {
//////            transform.position = Vector3.MoveTowards(
//////                transform.position,
//////                targetPos,
//////                moveSpeed * Time.deltaTime);
//////        }
//////        else
//////        {
//////            transform.position = targetPos;
//////            isMoving = false;
//////        }
//////    }


//////    // ======================================================
//////    // 各種ユーティリティ
//////    // ======================================================

//////    private bool IsLinkedDirection(MapNode node, Vector3 dir)
//////    {
//////        for (int i = 0; i < node.links.Count; i++)
//////        {
//////            Vector3 diff = (node.links[i].transform.position - node.transform.position).normalized;
//////            if (Vector3.Dot(diff, dir) > 0.7f)
//////                return true;
//////        }
//////        return false;
//////    }

//////    private bool IsWall(Vector3 pos, Vector3 dir)
//////    {
//////        // pos(currentNode) は使わない → transform.position を使う
//////        Vector3 origin =
//////            transform.position +
//////            Vector3.up * 0.1f +
//////            dir * (cellSize * 0.45f);

//////        float dist = cellSize * 0.55f;

//////        return Physics.Raycast(origin, dir, dist, wallLayer);
//////    }

//////    private bool IsTerminalNode(MapNode node)
//////    {
//////        return node != null && node.links.Count == 1;
//////    }

//////    private void RegisterCurrentNode(MapNode node)
//////    {
//////        if (recentNodes.Count == 0 || recentNodes[recentNodes.Count - 1] != node)
//////            recentNodes.Add(node);

//////        while (recentNodes.Count > unknownReferenceDepth)
//////            recentNodes.RemoveAt(0);
//////    }


//////    // ======================================================
//////    // 座標処理
//////    // ======================================================

//////    private Vector3 SnapToGrid(Vector3 pos)
//////    {
//////        int x = Mathf.RoundToInt((pos.x - gridOrigin.x) / cellSize);
//////        int z = Mathf.RoundToInt((pos.z - gridOrigin.z) / cellSize);
//////        return new Vector3(x * cellSize, 0, z * cellSize) + gridOrigin;
//////    }

//////    private Vector2Int WorldToCell(Vector3 worldPos)
//////    {
//////        Vector3 p = worldPos - gridOrigin;
//////        return new Vector2Int(
//////            Mathf.RoundToInt(p.x / cellSize),
//////            Mathf.RoundToInt(p.z / cellSize)
//////        );
//////    }

//////    private Vector3 CellToWorld(Vector2Int cell)
//////    {
//////        return new Vector3(cell.x * cellSize, 0, cell.y * cellSize) + gridOrigin;
//////    }

//////    //private void LinkBackward(MapNode node)
//////    //{
//////    //    Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//////    //    Vector3 dir = -moveDir;
//////    //    LayerMask mask = wallLayer | nodeLayer;

//////    //    for (int step = 1; step <= linkRayMaxSteps; step++)
//////    //    {
//////    //        float dist = cellSize * step;

//////    //        if (debugRay)
//////    //            Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);

//////    //        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
//////    //        {
//////    //            int layer = hit.collider.gameObject.layer;

//////    //            if ((wallLayer.value & (1 << layer)) != 0)
//////    //                return;

//////    //            if ((nodeLayer.value & (1 << layer)) != 0)
//////    //            {
//////    //                MapNode hitNode = hit.collider.GetComponent<MapNode>();
//////    //                if (hitNode != null && hitNode != node)
//////    //                {
//////    //                    node.AddLink(hitNode);
//////    //                    node.RecalculateUnknownAndWall();
//////    //                    hitNode.RecalculateUnknownAndWall();
//////    //                }
//////    //                return;
//////    //            }
//////    //        }
//////    //    }
//////    //}
//////    private void LinkBackward(MapNode node)
//////    {
//////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//////        Vector3 dir = -moveDir;
//////        LayerMask mask = wallLayer | nodeLayer;

//////        Debug.Log($"[LinkBackward] node={node.cell} moveDir={moveDir} → dir={dir}");

//////        bool linked = false;

//////        for (int step = 1; step <= linkRayMaxSteps; step++)
//////        {
//////            float dist = cellSize * step;

//////            if (debugRay)
//////                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);

//////            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
//////            {
//////                int layer = hit.collider.gameObject.layer;

//////                Debug.Log($"[RayHit] {node.cell} hit={hit.collider.name} layer={layer} dist={dist}");

//////                // ―― 壁に当たった ――――――――――――――――――――――――
//////                if ((wallLayer.value & (1 << layer)) != 0)
//////                {
//////                    Debug.Log($"[LinkBackward] STOP: wall hit at dist={dist}");
//////                    return;
//////                }

//////                // ―― Node に当たった ――――――――――――――――――――――――
//////                if ((nodeLayer.value & (1 << layer)) != 0)
//////                {
//////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();

//////                    if (hitNode != null && hitNode != node)
//////                    {
//////                        Debug.Log($"[LinkBackward] LINK SUCCESS {node.cell} <--> {hitNode.cell}");

//////                        node.AddLink(hitNode);
//////                        node.RecalculateUnknownAndWall();
//////                        hitNode.RecalculateUnknownAndWall();
//////                        linked = true;
//////                    }
//////                    return;
//////                }
//////            }
//////        }

//////        if (!linked)
//////            Debug.Log($"[LinkBackward] NO LINK: {node.cell} (no node hit)");
//////    }



//////    private bool CanPlaceNodeHere()
//////    {
//////        Vector3 origin = transform.position + Vector3.up * 0.1f;

//////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//////        bool frontHit = Physics.Raycast(origin, moveDir, rayDistance, wallLayer);
//////        bool leftHit = Physics.Raycast(origin, leftDir, rayDistance, wallLayer);
//////        bool rightHit = Physics.Raycast(origin, rightDir, rayDistance, wallLayer);

//////        int open = (frontHit ? 0 : 1) + (leftHit ? 0 : 1) + (rightHit ? 0 : 1);

//////        return frontHit || open >= 2;
//////    }
//////}

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

//////////// ======================================================
////////// CellFromStart.cs A版
////////// ・UnknownCount（未知数）＋ DistanceFromStart（Startからの距離）を使った
//////////   ハイブリッドスコアで方向選択
////////// ・weightUnknown = 1.0, weightDistance = 1.0
//////////// ======================================================

////////using UnityEngine;
////////using System.Collections.Generic;
////////using System.Linq;
////////using System.Collections;

////////public class CellFromStart : MonoBehaviour
////////{
////////    // ======================================================
////////    // パラメータ設定
////////    // ======================================================

////////    [Header("移動設定")]
////////    public float moveSpeed = 3f;
////////    public float cellSize = 1f;
////////    public float rayDistance = 1f;
////////    public LayerMask wallLayer;
////////    public LayerMask nodeLayer;

////////    [Header("初期設定")]
////////    public Vector3 startDirection = Vector3.forward;
////////    public Vector3 gridOrigin = Vector3.zero;
////////    public GameObject nodePrefab;

////////    [Header("行動傾向")]
////////    [Range(0f, 1f)] public float exploreBias = 0.6f;

////////    [Header("探索パラメータ")]
////////    public int unknownReferenceDepth;

////////    [Header("リンク探索")]
////////    public int linkRayMaxSteps = 100;

////////    [Header("スコア重み")]
////////    public float weightUnknown = 1.0f;
////////    public float weightDistance = 1.0f;

////////    [Header("デバッグ")]
////////    public bool debugLog = true;
////////    public bool debugRay = true;
////////    [SerializeField] private Renderer bodyRenderer;
////////    [SerializeField] private Material exploreMaterial;

////////    // ======================================================
////////    // 内部状態変数
////////    // ======================================================

////////    private Vector3 moveDir;
////////    private bool isMoving = false;
////////    private Vector3 targetPos;
////////    private MapNode currentNode;

////////    private const float EPS = 1e-4f;

////////    private List<MapNode> recentNodes = new List<MapNode>();

////////    // ======================================================
////////    // Start
////////    // ======================================================
////////    void Start()
////////    {
////////        moveDir = startDirection.normalized;
////////        targetPos = transform.position = SnapToGrid(transform.position);
////////        ApplyVisual();
////////        currentNode = TryPlaceNode(transform.position);

////////        RegisterCurrentNode(currentNode);

////////        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
////////    }

////////    // ======================================================
////////    // Update
////////    // ======================================================
////////    void Update()
////////    {
////////        if (!isMoving)
////////        {
////////            if (CanPlaceNodeHere())
////////                TryExploreMove();
////////            else
////////                MoveForward();
////////        }
////////        else
////////        {
////////            MoveToTarget();
////////        }
////////    }

////////    // ======================================================
////////    // ApplyVisual
////////    // ======================================================
////////    private void ApplyVisual()
////////    {
////////        if (bodyRenderer == null) return;
////////        bodyRenderer.material = exploreMaterial
////////            ? exploreMaterial
////////            : new Material(Shader.Find("Standard")) { color = Color.cyan };
////////    }

////////    // ======================================================
////////    // CanPlaceNodeHere
////////    // ======================================================
////////    bool CanPlaceNodeHere()
////////    {
////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////////        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir,
////////                                        rayDistance, wallLayer);
////////        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir,
////////                                       rayDistance, wallLayer);
////////        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir,
////////                                        rayDistance, wallLayer);

////////        int openCount = 0;
////////        if (!frontHit) openCount++;
////////        if (!leftHit) openCount++;
////////        if (!rightHit) openCount++;

////////        return (frontHit || openCount >= 2);
////////    }

////////    // ======================================================
////////    // MoveForward
////////    // ======================================================
////////    void MoveForward()
////////    {
////////        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
////////        targetPos = nextPos;
////////        isMoving = true;
////////    }

////////    // ======================================================
////////    // MoveToTarget
////////    // ======================================================
////////    private void MoveToTarget()
////////    {
////////        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
////////        {
////////            transform.position = Vector3.MoveTowards(
////////                transform.position,
////////                targetPos,
////////                moveSpeed * Time.deltaTime
////////            );
////////        }
////////        else
////////        {
////////            transform.position = targetPos;
////////            isMoving = false;
////////        }
////////    }

////////    // ======================================================
////////    // Node 履歴管理
////////    // ======================================================
////////    private void RegisterCurrentNode(MapNode node)
////////    {
////////        if (node == null) return;

////////        if (recentNodes.Count > 0 && recentNodes[recentNodes.Count - 1] == node)
////////            return;

////////        recentNodes.Add(node);

////////        int maxDepth = Mathf.Max(unknownReferenceDepth, 1);
////////        while (recentNodes.Count > maxDepth)
////////            recentNodes.RemoveAt(0);

////////        if (debugLog)
////////        {
////////            string hist = string.Join(" -> ",
////////                           recentNodes.Select(n => n != null ? n.name : "null"));
////////            Debug.Log($"[HIST] {hist}");
////////        }
////////    }

////////    // ======================================================
////////    // TryExploreMove：方向選択
////////    // ======================================================
////////    void TryExploreMove()
////////    {
////////        currentNode = TryPlaceNode(transform.position);
////////        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

////////        RegisterCurrentNode(currentNode);

////////        // ========================================
////////        // ★ 終端Node（links=1）用の特別処理（改良版）
////////        // ========================================
////////        if (IsTerminalNode(currentNode))
////////        {
////////            var dir = ChooseDirectionAtTerminal(currentNode);
////////            if (dir.HasValue)
////////            {
////////                moveDir = dir.Value;
////////                MoveForward();
////////            }
////////            else
////////            {
////////                TryMoveToUnlinkedDirection();
////////            }
////////            return;
////////        }

////////        if (currentNode == null || currentNode.links.Count == 0)
////////        {
////////            TryMoveToUnlinkedDirection();
////////            return;
////////        }

////////        // ★ Unknown + Distance のスコアで選ぶ
////////        MapNode next = ChooseNextNodeByScore(currentNode);

////////        if (next != null)
////////        {
////////            moveDir = (next.transform.position - transform.position).normalized;
////////            MoveForward();
////////        }
////////        else
////////        {
////////            TryMoveToUnlinkedDirection();
////////        }
////////    }

////////    // ======================================================
////////    // スコア計算
////////    // ======================================================
////////    private float CalcNodeScore(MapNode node)
////////    {
////////        if (node == null) return -999999f;

////////        float u = node.unknownCount;
////////        float d = node.distanceFromStart;

////////        float score = weightUnknown * u + weightDistance * (-d);

////////        if (debugLog)
////////            Debug.Log($"[SCORE] {node.name}: U={u}, D={d} → score={score}");

////////        return score;
////////    }

////////    // ======================================================
////////    // ChooseNextNodeByScore：スコア方式
////////    // ======================================================
////////    private MapNode ChooseNextNodeByScore(MapNode current)
////////    {
////////        if (current == null || current.links.Count == 0)
////////            return null;

////////        if (unknownReferenceDepth > 0 && recentNodes.Count > 0)
////////        {
////////            MapNode bestNode = null;
////////            float bestU = -1;

////////            foreach (var n in recentNodes)
////////            {
////////                if (n == null) continue;
////////                if (n.unknownCount > bestU)
////////                {
////////                    bestU = n.unknownCount;
////////                    bestNode = n;
////////                }
////////            }

////////            if (bestNode == current)
////////                return null;
////////        }

////////        var best = current.links
////////            .OrderByDescending(n => CalcNodeScore(n))
////////            .ThenBy(_ => Random.value)
////////            .FirstOrDefault();

////////        if (best != null)
////////            Debug.Log($"[SCORE-SELECT] {current.name} → {best.name}");

////////        return best;
////////    }

////////    // ======================================================
////////    // 終端ノード判定
////////    // ======================================================
////////    private bool IsTerminalNode(MapNode node)
////////    {
////////        return node != null && node.links != null && node.links.Count == 1;
////////    }

////////    // ======================================================
////////    // ★ 終端ノード専用：Unknown最優先＋複数候補ならDistance評価（追加）
////////    // ======================================================
////////    private Vector3? ChooseDirectionAtTerminal(MapNode node)
////////    {
////////        List<Vector3> dirs = new List<Vector3>
////////        {
////////            Vector3.forward, Vector3.back, Vector3.left, Vector3.right
////////        };

////////        Vector3 backDir = (-moveDir).normalized;
////////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;

////////        // ① 逆走を除外
////////        dirs = dirs.Where(d => Vector3.Dot(d.normalized, backDir) < 0.7f).ToList();

////////        // ② 既存リンクを除外
////////        dirs = dirs.Where(d =>
////////        {
////////            foreach (var link in node.links)
////////            {
////////                Vector3 diff = (link.transform.position - node.transform.position).normalized;
////////                if (Vector3.Dot(diff, d.normalized) > 0.7f)
////////                    return false;
////////            }
////////            return true;
////////        }).ToList();

////////        // ③ 壁を除外
////////        List<Vector3> unknownDirs = dirs.Where(d =>
////////            !Physics.Raycast(origin, d, cellSize, wallLayer)
////////        ).ToList();

////////        if (unknownDirs.Count == 0)
////////            return null;

////////        // Unknown が1個ならそれで確定
////////        if (unknownDirs.Count == 1)
////////            return unknownDirs[0];

////////        // --- 複数ある場合：DistanceFromStartで最前進方向を選ぶ ---
////////        Vector3 bestDir = unknownDirs[0];
////////        int bestScore = int.MinValue;

////////        foreach (var d in unknownDirs)
////////        {
////////            Vector3 p = node.transform.position + d * cellSize;
////////            Vector2Int cell = WorldToCell(p);
////////            MapNode near = MapNode.FindByCell(cell);
////////            if (near == null) continue;

////////            int score = -near.distanceFromStart;
////////            if (score > bestScore)
////////            {
////////                bestScore = score;
////////                bestDir = d;
////////            }
////////        }

////////        return bestDir;
////////    }

////////    // ======================================================
////////    // TryMoveToUnlinkedDirection
////////    // ======================================================
////////    private void TryMoveToUnlinkedDirection()
////////    {
////////        if (currentNode == null)
////////        {
////////            MoveForward();
////////            return;
////////        }

////////        List<Vector3> allDirs = new List<Vector3>
////////        {
////////            Vector3.forward, Vector3.back, Vector3.left, Vector3.right
////////        };

////////        Vector3 backDir = (-moveDir).normalized;

////////        List<Vector3> afterBack = new List<Vector3>();
////////        foreach (var d in allDirs)
////////        {
////////            if (Vector3.Dot(d.normalized, backDir) > 0.7f) continue;
////////            afterBack.Add(d);
////////        }

////////        List<Vector3> afterLinked = new List<Vector3>();
////////        foreach (var d in afterBack)
////////        {
////////            bool linked = false;
////////            foreach (var link in currentNode.links)
////////            {
////////                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
////////                if (Vector3.Dot(diff, d.normalized) > 0.7f)
////////                {
////////                    linked = true;
////////                    break;
////////                }
////////            }
////////            if (!linked) afterLinked.Add(d);
////////        }

////////        List<Vector3> validDirs = new List<Vector3>();
////////        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
////////        foreach (var d in afterLinked)
////////        {
////////            if (!Physics.Raycast(origin, d, cellSize, wallLayer))
////////                validDirs.Add(d);
////////        }

////////        if (validDirs.Count == 0)
////////        {
////////            foreach (var link in currentNode.links)
////////            {
////////                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
////////                if (Vector3.Dot(diff, backDir) < 0.7f)
////////                {
////////                    moveDir = diff;
////////                    MoveForward();
////////                    return;
////////                }
////////            }
////////            return;
////////        }

////////        moveDir = validDirs[UnityEngine.Random.Range(0, validDirs.Count)];
////////        MoveForward();
////////    }

////////    // ======================================================
////////    // ノード設置処理
////////    // ======================================================
////////    MapNode TryPlaceNode(Vector3 pos)
////////    {
////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////////        MapNode node;

////////        if (MapNode.allNodeCells.Contains(cell))
////////        {
////////            node = MapNode.FindByCell(cell);
////////        }
////////        else
////////        {
////////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////////            node = obj.GetComponent<MapNode>();
////////            node.cell = cell;
////////            MapNode.allNodeCells.Add(cell);
////////        }

////////        if (MapNode.StartNode == null)
////////        {
////////            MapNode.StartNode = node;
////////            node.distanceFromStart = 0;
////////        }

////////        if (node != null)
////////            LinkBackWithRay(node);

////////        return node;
////////    }

////////    // ======================================================
////////    // LinkBackWithRay
////////    // ======================================================
////////    private void LinkBackWithRay(MapNode node)
////////    {
////////        if (node == null) return;

////////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
////////        Vector3 backDir = -moveDir.normalized;
////////        LayerMask mask = wallLayer | nodeLayer;

////////        for (int step = 1; step <= linkRayMaxSteps; step++)
////////        {
////////            float maxDist = cellSize * step;

////////            if (debugRay)
////////                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

////////            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
////////            {
////////                int hitLayer = hit.collider.gameObject.layer;

////////                if ((wallLayer.value & (1 << hitLayer)) != 0)
////////                    return;

////////                if ((nodeLayer.value & (1 << hitLayer)) != 0)
////////                {
////////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
////////                    if (hitNode != null && hitNode != node)
////////                    {
////////                        node.AddLink(hitNode);
////////                        node.RecalculateUnknownAndWall();
////////                        hitNode.RecalculateUnknownAndWall();
////////                    }
////////                    return;
////////                }
////////            }
////////        }
////////    }

////////    // ======================================================
////////    // 座標変換
////////    // ======================================================
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


////////////// ======================================================
//////////// CellFromStart.cs
//////////// ・UnknownCount（未知数）＋ DistanceFromStart（Startからの距離）を使った
////////////   ハイブリッドスコアで方向選択
//////////// ・weightUnknown = 1.0, weightDistance = 1.0
////////////// ======================================================

//////////using UnityEngine;
//////////using System.Collections.Generic;
//////////using System.Linq;
//////////using System.Collections;

//////////public class CellFromStart : MonoBehaviour
//////////{
//////////    // ======================================================
//////////    // パラメータ設定
//////////    // ======================================================

//////////    [Header("移動設定")]
//////////    public float moveSpeed = 3f;
//////////    public float cellSize = 1f;
//////////    public float rayDistance = 1f;
//////////    public LayerMask wallLayer;
//////////    public LayerMask nodeLayer;

//////////    [Header("初期設定")]
//////////    public Vector3 startDirection = Vector3.forward;
//////////    public Vector3 gridOrigin = Vector3.zero;
//////////    public GameObject nodePrefab;

//////////    [Header("行動傾向")]
//////////    [Range(0f, 1f)] public float exploreBias = 0.6f;

//////////    [Header("探索パラメータ")]
//////////    public int unknownReferenceDepth;

//////////    [Header("リンク探索")]
//////////    public int linkRayMaxSteps = 100;

//////////    [Header("スコア重み")]
//////////    public float weightUnknown = 1.0f;
//////////    public float weightDistance = 1.0f;

//////////    [Header("デバッグ")]
//////////    public bool debugLog = true;
//////////    public bool debugRay = true;
//////////    [SerializeField] private Renderer bodyRenderer;
//////////    [SerializeField] private Material exploreMaterial;

//////////    // ======================================================
//////////    // 内部状態変数
//////////    // ======================================================

//////////    private Vector3 moveDir;
//////////    private bool isMoving = false;
//////////    private Vector3 targetPos;
//////////    private MapNode currentNode;

//////////    private const float EPS = 1e-4f;

//////////    private List<MapNode> recentNodes = new List<MapNode>();

//////////    // ======================================================
//////////    // Start
//////////    // ======================================================
//////////    void Start()
//////////    {
//////////        moveDir = startDirection.normalized;
//////////        targetPos = transform.position = SnapToGrid(transform.position);
//////////        ApplyVisual();
//////////        currentNode = TryPlaceNode(transform.position);

//////////        RegisterCurrentNode(currentNode);

//////////        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
//////////    }

//////////    // ======================================================
//////////    // Update
//////////    // ======================================================
//////////    void Update()
//////////    {
//////////        if (!isMoving)
//////////        {
//////////            if (CanPlaceNodeHere())
//////////                TryExploreMove();
//////////            else
//////////                MoveForward();
//////////        }
//////////        else
//////////        {
//////////            MoveToTarget();
//////////        }
//////////    }

//////////    // ======================================================
//////////    // ApplyVisual
//////////    // ======================================================
//////////    private void ApplyVisual()
//////////    {
//////////        if (bodyRenderer == null) return;
//////////        bodyRenderer.material = exploreMaterial
//////////            ? exploreMaterial
//////////            : new Material(Shader.Find("Standard")) { color = Color.cyan };
//////////    }

//////////    // ======================================================
//////////    // CanPlaceNodeHere
//////////    // ======================================================
//////////    bool CanPlaceNodeHere()
//////////    {
//////////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//////////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//////////        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir,
//////////                                        rayDistance, wallLayer);
//////////        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir,
//////////                                       rayDistance, wallLayer);
//////////        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir,
//////////                                        rayDistance, wallLayer);

//////////        int openCount = 0;
//////////        if (!frontHit) openCount++;
//////////        if (!leftHit) openCount++;
//////////        if (!rightHit) openCount++;

//////////        return (frontHit || openCount >= 2);
//////////    }

//////////    // ======================================================
//////////    // MoveForward
//////////    // ======================================================
//////////    void MoveForward()
//////////    {
//////////        Vector3 nextPos = SnapToGrid(transform.position + moveDir * cellSize);
//////////        targetPos = nextPos;
//////////        isMoving = true;
//////////    }

//////////    // ======================================================
//////////    // MoveToTarget
//////////    // ======================================================
//////////    private void MoveToTarget()
//////////    {
//////////        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
//////////        {
//////////            transform.position = Vector3.MoveTowards(
//////////                transform.position,
//////////                targetPos,
//////////                moveSpeed * Time.deltaTime
//////////            );
//////////        }
//////////        else
//////////        {
//////////            transform.position = targetPos;
//////////            isMoving = false;
//////////        }
//////////    }

//////////    // ======================================================
//////////    // Node 履歴管理
//////////    // ======================================================
//////////    private void RegisterCurrentNode(MapNode node)
//////////    {
//////////        if (node == null) return;

//////////        if (recentNodes.Count > 0 && recentNodes[recentNodes.Count - 1] == node)
//////////            return;

//////////        recentNodes.Add(node);

//////////        int maxDepth = Mathf.Max(unknownReferenceDepth, 1);
//////////        while (recentNodes.Count > maxDepth)
//////////            recentNodes.RemoveAt(0);

//////////        if (debugLog)
//////////        {
//////////            string hist = string.Join(" -> ",
//////////                           recentNodes.Select(n => n != null ? n.name : "null"));
//////////            Debug.Log($"[HIST] {hist}");
//////////        }
//////////    }

//////////    // ======================================================
//////////    // TryExploreMove：方向選択
//////////    // ======================================================
//////////    void TryExploreMove()
//////////    {
//////////        currentNode = TryPlaceNode(transform.position);
//////////        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

//////////        //Debug.Log($"[EXP-DEBUG] Node placed: {currentNode.name}  U={currentNode.unknownCount}  D={currentNode.distanceFromStart}");

//////////        RegisterCurrentNode(currentNode);

//////////        // 終端なら未知方向探索へ
//////////        if (IsTerminalNode(currentNode))
//////////        {
//////////            //Debug.Log("[EXP] Terminal → TryMoveToUnlinkedDirection()");
//////////            TryMoveToUnlinkedDirection();
//////////            return;
//////////        }

//////////        if (currentNode == null || currentNode.links.Count == 0)
//////////        {
//////////            //Debug.Log("[EXP] No links → TryMoveToUnlinkedDirection()");
//////////            TryMoveToUnlinkedDirection();
//////////            return;
//////////        }

//////////        // ★ Unknown + Distance のスコアで選ぶ
//////////        MapNode next = ChooseNextNodeByScore(currentNode);

//////////        if (next != null)
//////////        {
//////////            moveDir = (next.transform.position - transform.position).normalized;
//////////            //Debug.Log($"[EXP-SELECT] Move to {next.name}  (U={next.unknownCount}, D={next.distanceFromStart})");
//////////            MoveForward();
//////////        }
//////////        else
//////////        {
//////////           // Debug.Log("[EXP-SELECT] NULL → TryMoveToUnlinkedDirection()");
//////////            TryMoveToUnlinkedDirection();
//////////        }
//////////    }

//////////    // ======================================================
//////////    // スコア計算
//////////    // ======================================================
//////////    private float CalcNodeScore(MapNode node)
//////////    {
//////////        if (node == null) return -999999f;

//////////        float u = node.unknownCount;
//////////        float d = node.distanceFromStart;

//////////        float score = weightUnknown * u + weightDistance * (-d);

//////////        if (debugLog)
//////////            Debug.Log($"[SCORE] {node.name}: U={u}, D={d} → score={score}");

//////////        return score;
//////////    }

//////////    // ======================================================
//////////    // ChooseNextNodeByScore：スコア方式
//////////    // ======================================================
//////////    private MapNode ChooseNextNodeByScore(MapNode current)
//////////    {
//////////        if (current == null || current.links.Count == 0)
//////////            return null;

//////////        //Debug.Log("=== [DEBUG] recentNodes 状況 ===");
//////////        //for (int i = 0; i < recentNodes.Count; i++)
//////////        //{
//////////        //    var n = recentNodes[i];
//////////        //    if (n != null)
//////////        //        Debug.Log($"  recent[{i}] = {n.name}  U={n.unknownCount}  D={n.distanceFromStart}");
//////////        //    else
//////////        //        Debug.Log($"  recent[{i}] = null");
//////////        //}

//////////        // 履歴を使うかどうかは従来部を再利用
//////////        if (unknownReferenceDepth > 0 && recentNodes.Count > 0)
//////////        {
//////////            MapNode bestNode = null;
//////////            float bestU = -1;

//////////            foreach (var n in recentNodes)
//////////            {
//////////                if (n == null) continue;
//////////                if (n.unknownCount > bestU)
//////////                {
//////////                    bestU = n.unknownCount;
//////////                    bestNode = n;
//////////                }
//////////            }

//////////            //Debug.Log($"[DEBUG] 履歴評価結果: bestNode={bestNode?.name}, bestU={bestU}");

//////////            // 履歴上で最も未知数が高いノードにいる場合 → 新規開拓
//////////            if (bestNode == current)
//////////                return null;
//////////            //Debug.Log("[DEBUG] bestNode が current → 新規方向開拓へ");
//////////        }

//////////        // ★ スコア方式でリンク先を選ぶ
//////////        var best = current.links
//////////            .OrderByDescending(n => CalcNodeScore(n))
//////////            .ThenBy(_ => Random.value)
//////////            .FirstOrDefault();

//////////        if (best != null)
//////////            Debug.Log($"[SCORE-SELECT] {current.name} → {best.name}");

//////////        return best;
//////////    }

//////////    // ======================================================
//////////    // 終端ノード判定
//////////    // ======================================================
//////////    private bool IsTerminalNode(MapNode node)
//////////    {
//////////        return node != null && node.links != null && node.links.Count == 1;
//////////    }

//////////    // ======================================================
//////////    // TryMoveToUnlinkedDirection（未知方向探索）
//////////    // ======================================================
//////////    private void TryMoveToUnlinkedDirection()
//////////    {
//////////        if (currentNode == null)
//////////        {
//////////            MoveForward();
//////////            return;
//////////        }

//////////        List<Vector3> allDirs = new List<Vector3>
//////////        {
//////////            Vector3.forward, Vector3.back, Vector3.left, Vector3.right
//////////        };

//////////        Vector3 backDir = (-moveDir).normalized;

//////////        // ① 戻る方向を除外
//////////        List<Vector3> afterBack = new List<Vector3>();
//////////        foreach (var d in allDirs)
//////////        {
//////////            if (Vector3.Dot(d.normalized, backDir) > 0.7f) continue;
//////////            afterBack.Add(d);
//////////        }

//////////        // ② 既存リンク除外
//////////        List<Vector3> afterLinked = new List<Vector3>();
//////////        foreach (var d in afterBack)
//////////        {
//////////            bool linked = false;
//////////            foreach (var link in currentNode.links)
//////////            {
//////////                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
//////////                if (Vector3.Dot(diff, d.normalized) > 0.7f)
//////////                {
//////////                    linked = true;
//////////                    break;
//////////                }
//////////            }
//////////            if (!linked) afterLinked.Add(d);
//////////        }

//////////        // ③ 壁除外
//////////        List<Vector3> validDirs = new List<Vector3>();
//////////        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
//////////        foreach (var d in afterLinked)
//////////        {
//////////            if (!Physics.Raycast(origin, d, cellSize, wallLayer))
//////////                validDirs.Add(d);
//////////        }

//////////        if (validDirs.Count == 0)
//////////        {
//////////            // 戻れない場合は停止
//////////            foreach (var link in currentNode.links)
//////////            {
//////////                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
//////////                if (Vector3.Dot(diff, backDir) < 0.7f)
//////////                {
//////////                    moveDir = diff;
//////////                    MoveForward();
//////////                    return;
//////////                }
//////////            }
//////////            return;
//////////        }

//////////        moveDir = validDirs[UnityEngine.Random.Range(0, validDirs.Count)];
//////////        MoveForward();
//////////    }

//////////    // ======================================================
//////////    // ノード設置処理
//////////    // ======================================================
//////////    MapNode TryPlaceNode(Vector3 pos)
//////////    {
//////////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//////////        MapNode node;

//////////        if (MapNode.allNodeCells.Contains(cell))
//////////        {
//////////            node = MapNode.FindByCell(cell);
//////////        }
//////////        else
//////////        {
//////////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//////////            node = obj.GetComponent<MapNode>();
//////////            node.cell = cell;
//////////            MapNode.allNodeCells.Add(cell);
//////////        }

//////////        // StartNode 設定
//////////        if (MapNode.StartNode == null)
//////////        {
//////////            MapNode.StartNode = node;
//////////            node.distanceFromStart = 0;
//////////        }

//////////        // リンク更新
//////////        if (node != null)
//////////            LinkBackWithRay(node);

//////////        return node;
//////////    }

//////////    // ======================================================
//////////    // LinkBackWithRay：後方リンク探索
//////////    // ======================================================
//////////    private void LinkBackWithRay(MapNode node)
//////////    {
//////////        if (node == null) return;

//////////        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
//////////        Vector3 backDir = -moveDir.normalized;
//////////        LayerMask mask = wallLayer | nodeLayer;

//////////        for (int step = 1; step <= linkRayMaxSteps; step++)
//////////        {
//////////            float maxDist = cellSize * step;

//////////            if (debugRay)
//////////                Debug.DrawRay(origin, backDir * maxDist, Color.yellow, 0.25f);

//////////            if (Physics.Raycast(origin, backDir, out RaycastHit hit, maxDist, mask))
//////////            {
//////////                int hitLayer = hit.collider.gameObject.layer;

//////////                if ((wallLayer.value & (1 << hitLayer)) != 0)
//////////                    return;

//////////                if ((nodeLayer.value & (1 << hitLayer)) != 0)
//////////                {
//////////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
//////////                    if (hitNode != null && hitNode != node)
//////////                    {
//////////                        node.AddLink(hitNode);
//////////                        node.RecalculateUnknownAndWall();
//////////                        hitNode.RecalculateUnknownAndWall();
//////////                    }
//////////                    return;
//////////                }
//////////            }
//////////        }
//////////    }

//////////    // ======================================================
//////////    // 座標変換
//////////    // ======================================================
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




















































































//using UnityEngine;
//using System.Collections.Generic;
//using System.Linq;

//public class LearningPlayer : MonoBehaviour
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
//    public MapNode goalNode;
//    public GameObject nodePrefab;

//    [Header("探索設定")]
//    [Range(0f, 1f)] public float epsilon = 0.2f;
//    public int linkRayMaxSteps = 100;

//    [Header("デバッグ")]
//    public bool debugLog = true;
//    public bool debugRay = true;

//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;
//    private MapNode currentNode;
//    private MapNode prevNode;
//    private bool reachedGoal = false;

//    private const float EPS = 1e-4f;

//    // ★追加：順回路回避用 passCount
//    private Dictionary<MapNode, int> nodeVisitCount = new();

//    // ======================================================
//    void Start()
//    {
//        moveDir = startDirection.normalized;
//        targetPos = transform.position = SnapToGrid(transform.position);
//        currentNode = TryPlaceNode(transform.position);
//        prevNode = null;

//        if (goalNode == null)
//        {
//            GameObject goalObj = GameObject.Find("Goal");
//            if (goalObj != null)
//                goalNode = goalObj.GetComponent<MapNode>();
//        }

//        if (goalNode != null && currentNode != null)
//            currentNode.UpdateValueByGoal(goalNode);

//        if (debugLog)
//            Debug.Log($"[LearningPlayer:{name}] Start @ {currentNode}");
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

//    // ======================================================
//    bool CanPlaceNodeHere()
//    {
//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
//        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
//        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

//        int openCount = (!frontHit ? 1 : 0) + (!leftHit ? 1 : 0) + (!rightHit ? 1 : 0);
//        return (frontHit || openCount >= 2);
//    }

//    // ======================================================
//    // ★順回路回避統合版 TryExploreMove
//    // ======================================================
//    void TryExploreMove()
//    {
//        MapNode nextCandidate = TryPlaceNode(transform.position);

//        if (nextCandidate != currentNode)
//            currentNode = nextCandidate;

//        string CurrCell(MapNode n) => n != null ? n.cell.ToString() : "null";
//        if (debugLog)
//            Debug.Log($"[EXP] === TryExploreMove Start === current={currentNode?.name}{CurrCell(currentNode)} prev={prevNode?.name}{CurrCell(prevNode)}");

//        // --------------------------------------------------
//        // 周囲方向スキャン
//        // --------------------------------------------------
//        var dirs = ScanAroundDirections();
//        if (dirs.Count == 0)
//        {
//            Debug.Log("[EXP] dirs.Count == 0 (進行候補なし)");
//            return;
//        }

//        // --------------------------------------------------
//        // 戻り方向推定（prevNode がある場合）
//        // --------------------------------------------------
//        Vector3? backDirOpt = null;
//        if (prevNode != null && currentNode != null)
//        {
//            Vector3 fromPrev = (currentNode.transform.position - prevNode.transform.position);
//            if (Mathf.Abs(fromPrev.x) > Mathf.Abs(fromPrev.z))
//                backDirOpt = (fromPrev.x > 0f) ? Vector3.right : Vector3.left;
//            else if (Mathf.Abs(fromPrev.z) > 0f)
//                backDirOpt = (fromPrev.z > 0f) ? Vector3.forward : Vector3.back;
//        }

//        if (debugLog)
//        {
//            foreach (var d in dirs)
//            {
//                string n = d.node != null ? $"{d.node.name}{CurrCell(d.node)}" : "null";
//                Debug.Log($"[EXP] dir={d.dir} node={n} hasLink={d.hasLink}");
//            }
//        }

//        // --------------------------------------------------
//        // 候補分類（prevNode 除外）
//        // --------------------------------------------------
//        var unexplored = dirs.Where(d => d.node == null || !d.hasLink).ToList();
//        var known = dirs.Where(d => d.node != null && d.hasLink && d.node != prevNode).ToList();

//        // --------------------------------------------------
//        // 戻り方向(backDir)除外
//        // --------------------------------------------------
//        if (backDirOpt.HasValue)
//        {
//            Vector3 backDir = backDirOpt.Value;
//            unexplored = unexplored.Where(d => d.dir != backDir).ToList();
//            known = known.Where(d => d.dir != backDir).ToList();

//            if (debugLog)
//                Debug.Log($"[EXP] backDir={backDir} を除外");
//        }

//        if (debugLog)
//            Debug.Log($"[EXP] unexplored.Count={unexplored.Count}, known.Count={known.Count}");

//        // --------------------------------------------------
//        // 袋小路対策：戻る方向を解禁
//        // --------------------------------------------------
//        if (known.Count == 0 && unexplored.Count == 0 && backDirOpt.HasValue)
//        {
//            var back = dirs.FirstOrDefault(d => d.dir == backDirOpt.Value);
//            if (back.dir != Vector3.zero)
//            {
//                known.Add(back);
//                Debug.LogWarning($"[EXP] dead-end fallback: allow back={back.dir}");
//            }
//        }

//        // --------------------------------------------------
//        // ε-greedy（探索 or 活用）
//        // --------------------------------------------------
//        float r = Random.value;
//        bool doExplore = (r < epsilon);

//        if (debugLog)
//            Debug.Log($"[EXP] epsilon={epsilon:F3}, rand={r:F3} → explore={doExplore}");

//        (Vector3 dir, MapNode node, bool hasLink)? chosen = null;

//        if (doExplore)
//        {
//            if (unexplored.Count > 0)
//                chosen = unexplored[Random.Range(0, unexplored.Count)];
//            else if (known.Count > 0)
//                chosen = known[Random.Range(0, known.Count)];
//        }
//        else
//        {
//            if (known.Count > 0)
//                chosen = known.OrderByDescending(d => d.node?.value ?? -9999f).First();
//            else if (unexplored.Count > 0)
//                chosen = unexplored.OrderByDescending(d => d.node?.value ?? -9999f).First();
//        }

//        // --------------------------------------------------
//        // ★順回路回避（passCount 最小ノードを選ぶ）
//        // --------------------------------------------------
//        if (chosen == null)
//        {
//            MapNode minNode = null;
//            Vector3 minDir = Vector3.zero;
//            int minCount = int.MaxValue;

//            foreach (var d in dirs)
//            {
//                if (d.node == null) continue;

//                int count = nodeVisitCount.ContainsKey(d.node) ? nodeVisitCount[d.node] : 0;
//                if (count < minCount)
//                {
//                    minCount = count;
//                    minNode = d.node;
//                    minDir = d.dir;
//                }
//            }

//            if (minNode != null)
//            {
//                chosen = (minDir, minNode, true);

//                if (debugLog)
//                    Debug.Log($"[EXP-PASS] selected least-visited node={minNode.name} pass={minCount}");
//            }
//        }

//        // --------------------------------------------------
//        // 最終移動
//        // --------------------------------------------------
//        if (chosen.HasValue)
//        {
//            moveDir = chosen.Value.dir;

//            // passCount 更新
//            if (chosen.Value.node != null)
//            {
//                if (!nodeVisitCount.ContainsKey(chosen.Value.node))
//                    nodeVisitCount[chosen.Value.node] = 0;

//                nodeVisitCount[chosen.Value.node]++;
//            }

//            MoveForward();
//            prevNode = currentNode;
//        }
//        else
//        {
//            Debug.LogWarning("[EXP] chosen is null → 停止");
//        }

//        if (debugLog)
//            Debug.Log("[EXP] === TryExploreMove End ===");
//    }

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
//    void MoveForward()
//    {
//        targetPos = SnapToGrid(transform.position + moveDir * cellSize);
//        isMoving = true;
//    }

//    // ======================================================
//    void MoveToTarget()
//    {
//        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//        if (Vector3.Distance(transform.position, targetPos) < EPS)
//        {
//            transform.position = targetPos;
//            isMoving = false;

//            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
//            MapNode nextNode = MapNode.FindByCell(cell);

//            if (currentNode != null && nextNode != null)
//            {
//                currentNode.AddLink(nextNode);
//                LinkBackWithRay(currentNode);

//                if (prevNode != currentNode)
//                    prevNode = currentNode;

//                if (goalNode != null)
//                    nextNode.UpdateValueByGoal(goalNode);
//            }

//            currentNode = nextNode;

//            if (!reachedGoal && goalNode != null)
//            {
//                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
//                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));
//                if (playerCell == goalCell)
//                {
//                    reachedGoal = true;
//                    LinkBackWithRay(currentNode);
//                    RecalculateGoalDistance();
//                    Destroy(gameObject);
//                }
//            }

//            if (debugLog)
//                Debug.Log($"[MOVE] prevNode={prevNode?.cell}, currentNode={currentNode?.cell}");
//        }
//    }

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
//                        if (debugLog)
//                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
//                    }
//                    return;
//                }
//            }
//        }
//    }

//    // ======================================================
//    void RecalculateGoalDistance()
//    {
//        if (goalNode == null) return;

//        foreach (var n in FindObjectsOfType<MapNode>())
//            n.DistanceFromGoal = Mathf.Infinity;

//        goalNode.DistanceFromGoal = 0f;
//        var frontier = new List<MapNode> { goalNode };

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
//    MapNode TryPlaceNode(Vector3 pos)
//    {
//        Vector2Int cell = WorldToCell(SnapToGrid(pos));
//        MapNode node;

//        if (MapNode.allNodeCells.Contains(cell))
//        {
//            node = MapNode.FindByCell(cell);
//            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
//        }
//        else
//        {
//            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
//            node = obj.GetComponent<MapNode>();
//            node.cell = cell;
//            MapNode.allNodeCells.Add(cell);
//            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
//        }

//        if (node != null)
//        {
//            LinkBackWithRay(node);
//            if (goalNode != null)
//                node.UpdateValueByGoal(goalNode);
//        }

//        return node;
//    }

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

////public class LearningPlayer : MonoBehaviour
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

////    [Header("探索設定")]
////    [Range(0f, 1f)] public float epsilon = 0.2f;
////    public int linkRayMaxSteps = 100;

////    [Header("デバッグ")]
////    public bool debugLog = true;
////    public bool debugRay = true;

////    private Vector3 moveDir;
////    private bool isMoving = false;
////    private Vector3 targetPos;
////    private MapNode currentNode;
////    private MapNode prevNode;
////    private bool reachedGoal = false;

////    private const float EPS = 1e-4f;

////    // ======================================================
////    void Start()
////    {
////        moveDir = startDirection.normalized;
////        targetPos = transform.position = SnapToGrid(transform.position);
////        currentNode = TryPlaceNode(transform.position);
////        prevNode = null;

////        if (goalNode == null)
////        {
////            GameObject goalObj = GameObject.Find("Goal");
////            if (goalObj != null)
////                goalNode = goalObj.GetComponent<MapNode>();
////        }

////        if (goalNode != null && currentNode != null)
////            currentNode.UpdateValueByGoal(goalNode);

////        if (debugLog)
////            Debug.Log($"[LearningPlayer:{name}] Start @ {currentNode}");
////    }

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
////    bool CanPlaceNodeHere()
////    {
////        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
////        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

////        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir, rayDistance, wallLayer);
////        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir, rayDistance, wallLayer);
////        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir, rayDistance, wallLayer);

////        int openCount = (!frontHit ? 1 : 0) + (!leftHit ? 1 : 0) + (!rightHit ? 1 : 0);
////        return (frontHit || openCount >= 2);
////    }

////    // ======================================================
////    //void TryExploreMove()
////    //{
////    //    currentNode = TryPlaceNode(transform.position);

////    //    var dirs = ScanAroundDirections();
////    //    if (dirs.Count == 0)
////    //    {
////    //        Debug.Log("[EXP] dirs.Count == 0 (進行候補なし)");
////    //        return;
////    //    }

////    //    // 直前方向を計算
////    //    Vector3? backDirOpt = null;
////    //    if (prevNode != null && currentNode != null)
////    //    {
////    //        Vector3 fromPrev = (currentNode.transform.position - prevNode.transform.position);
////    //        if (Mathf.Abs(fromPrev.x) > Mathf.Abs(fromPrev.z))
////    //            backDirOpt = (fromPrev.x > 0f) ? Vector3.right : Vector3.left;
////    //        else if (Mathf.Abs(fromPrev.z) > 0f)
////    //            backDirOpt = (fromPrev.z > 0f) ? Vector3.forward : Vector3.back;
////    //    }

////    //    string N(MapNode n) => n != null ? $"{n.name}({n.cell.x},{n.cell.y})#{n.GetInstanceID()}" : "null";
////    //    Debug.Log($"[EXP] prev={N(prevNode)} curr={N(currentNode)} backDir={(backDirOpt.HasValue ? backDirOpt.Value.ToString() : "none")}");

////    //    foreach (var d in dirs)
////    //    {
////    //        if (d.node != null && goalNode != null)
////    //            d.node.UpdateValueByGoal(goalNode);
////    //    }

////    //    var unexplored = dirs.Where(d => d.node == null || !d.hasLink).ToList();
////    //    var known = dirs.Where(d => d.node != null && d.hasLink && d.node != prevNode).ToList();

////    //    // ★直前ベクトル方向の除外
////    //    if (backDirOpt.HasValue)
////    //    {
////    //        Vector3 backDir = backDirOpt.Value;
////    //        unexplored = unexplored.Where(d => d.dir != backDir).ToList();
////    //        known = known.Where(d => d.dir != backDir).ToList();
////    //    }

////    //    if (debugLog)
////    //    {
////    //        int knownBefore = dirs.Count(d => d.node != null && d.hasLink);
////    //        bool hasPrevObj = dirs.Any(d => d.node == prevNode);
////    //        bool hasBackDir = backDirOpt.HasValue && dirs.Any(d => d.dir == backDirOpt.Value);
////    //        Debug.Log($"[EXP] unexplored={unexplored.Count}, known(before)={knownBefore}, known(filtered)={known.Count}, hasPrevObj={hasPrevObj}, hasBackDir={hasBackDir}");
////    //    }

////    //    // ★デッドエンド時フォールバック
////    //    if (known.Count == 0 && unexplored.Count == 0 && backDirOpt.HasValue)
////    //    {
////    //        var back = dirs.FirstOrDefault(d => d.dir == backDirOpt.Value);
////    //        if (back.dir != Vector3.zero)
////    //        {
////    //            known.Add(back);
////    //            Debug.LogWarning($"[EXP] dead-end fallback: allow back to prev via dir={back.dir}");
////    //        }
////    //    }

////    //    (Vector3 dir, MapNode node, bool hasLink)? chosen = null;

////    //    float r = Random.value;
////    //    bool doExplore = (r < epsilon);
////    //    if (debugLog) Debug.Log($"[EXP] epsilon={epsilon:F3}, rand={r:F3} -> explore={doExplore}");

////    //    if (doExplore)
////    //    {
////    //        if (unexplored.Count > 0)
////    //            chosen = unexplored[Random.Range(0, unexplored.Count)];
////    //        else if (known.Count > 0)
////    //            chosen = known[Random.Range(0, known.Count)];
////    //    }
////    //    else
////    //    {
////    //        if (known.Count > 0)
////    //            chosen = known.OrderByDescending(d => d.node.value).First();
////    //        else if (unexplored.Count > 0)
////    //            chosen = unexplored.OrderByDescending(d => (d.node != null ? d.node.value : float.NegativeInfinity)).First();
////    //    }

////    //    if (chosen.HasValue)
////    //    {
////    //        string chosenStr = (chosen.Value.node != null)
////    //            ? $"{N(chosen.Value.node)} v={chosen.Value.node.value:F3}"
////    //            : "null";
////    //        if (debugLog)
////    //            Debug.Log($"[EXP] chosen dir={chosen.Value.dir} node={chosenStr} hasLink={chosen.Value.hasLink}");

////    //        moveDir = chosen.Value.dir;
////    //        MoveForward();
////    //    }
////    //    else
////    //    {
////    //        Debug.LogWarning("[EXP] chosen is null (移動候補なし)。prev除外/壁で詰んだ可能性。");
////    //    }
////    //}
////    //void TryExploreMove()
////    //{
////    //    // 現在位置にNodeを配置または取得
////    //    currentNode = TryPlaceNode(transform.position);

////    //    string CurrCell(MapNode n) => n != null ? n.cell.ToString() : "null";
////    //    if (debugLog)
////    //        Debug.Log($"[EXP] === TryExploreMove Start === current={currentNode?.name ?? "null"}{CurrCell(currentNode)} prev={prevNode?.name ?? "null"}{CurrCell(prevNode)}");

////    //    // --------------------------------------------------
////    //    // 周囲方向のスキャン
////    //    // --------------------------------------------------
////    //    var dirs = ScanAroundDirections();
////    //    if (dirs.Count == 0)
////    //    {
////    //        Debug.Log("[EXP] dirs.Count == 0 (進行候補なし)");
////    //        return;
////    //    }

////    //    // --------------------------------------------------
////    //    // 直前方向を推定（prevNodeがある場合）
////    //    // --------------------------------------------------
////    //    Vector3? backDirOpt = null;
////    //    if (prevNode != null && currentNode != null)
////    //    {
////    //        Vector3 fromPrev = (currentNode.transform.position - prevNode.transform.position);
////    //        if (Mathf.Abs(fromPrev.x) > Mathf.Abs(fromPrev.z))
////    //            backDirOpt = (fromPrev.x > 0f) ? Vector3.right : Vector3.left;
////    //        else if (Mathf.Abs(fromPrev.z) > 0f)
////    //            backDirOpt = (fromPrev.z > 0f) ? Vector3.forward : Vector3.back;
////    //    }

////    //    // --------------------------------------------------
////    //    // スキャン結果出力
////    //    // --------------------------------------------------
////    //    if (debugLog)
////    //    {
////    //        foreach (var d in dirs)
////    //        {
////    //            string n = d.node != null ? $"{d.node.name}{CurrCell(d.node)}" : "null";
////    //            Debug.Log($"[EXP] dir={d.dir} node={n} hasLink={d.hasLink}");
////    //        }
////    //    }

////    //    // --------------------------------------------------
////    //    // 候補を分類・prevNode除外
////    //    // --------------------------------------------------
////    //    var unexplored = dirs.Where(d => d.node == null || !d.hasLink).ToList();
////    //    var known = dirs.Where(d => d.node != null && d.hasLink && d.node != prevNode).ToList(); // ★ prevNode除外

////    //    // --------------------------------------------------
////    //    // 戻り方向(backDir)も除外
////    //    // --------------------------------------------------
////    //    if (backDirOpt.HasValue)
////    //    {
////    //        Vector3 backDir = backDirOpt.Value;
////    //        unexplored = unexplored.Where(d => d.dir != backDir).ToList();
////    //        known = known.Where(d => d.dir != backDir).ToList();

////    //        if (debugLog)
////    //            Debug.Log($"[EXP] backDir={backDir} を除外 (prevNode={prevNode?.name ?? "null"}{CurrCell(prevNode)})");
////    //    }

////    //    // --------------------------------------------------
////    //    // 現在の候補数を出力
////    //    // --------------------------------------------------
////    //    if (debugLog)
////    //        Debug.Log($"[EXP] unexplored.Count={unexplored.Count}, known.Count={known.Count} (current={CurrCell(currentNode)} prev={CurrCell(prevNode)})");

////    //    // --------------------------------------------------
////    //    // 袋小路対策：戻りを一時的に許可
////    //    // --------------------------------------------------
////    //    if (known.Count == 0 && unexplored.Count == 0 && backDirOpt.HasValue)
////    //    {
////    //        var back = dirs.FirstOrDefault(d => d.dir == backDirOpt.Value);
////    //        if (back.dir != Vector3.zero)
////    //        {
////    //            known.Add(back);
////    //            Debug.LogWarning($"[EXP] dead-end fallback: allow back to prev via dir={back.dir}");
////    //        }
////    //    }

////    //    // --------------------------------------------------
////    //    // ε-greedy探索方針決定
////    //    // --------------------------------------------------
////    //    float r = Random.value;
////    //    bool doExplore = (r < epsilon);
////    //    if (debugLog)
////    //        Debug.Log($"[EXP] epsilon={epsilon:F3}, rand={r:F3} → explore={doExplore}");

////    //    (Vector3 dir, MapNode node, bool hasLink)? chosen = null;

////    //    if (doExplore)
////    //    {
////    //        if (unexplored.Count > 0)
////    //        {
////    //            chosen = unexplored[Random.Range(0, unexplored.Count)];
////    //            if (debugLog) Debug.Log($"[EXP] [Explore] unexploredから選択 dir={chosen.Value.dir}");
////    //        }
////    //        else if (known.Count > 0)
////    //        {
////    //            chosen = known[Random.Range(0, known.Count)];
////    //            if (debugLog) Debug.Log($"[EXP] [Explore] knownから代替選択 dir={chosen.Value.dir}");
////    //        }
////    //    }
////    //    else
////    //    {
////    //        if (known.Count > 0)
////    //        {
////    //            chosen = known.OrderByDescending(d => d.node?.value ?? -9999f).First();
////    //            if (debugLog) Debug.Log($"[EXP] [Exploit] known中で最大value選択 dir={chosen.Value.dir}");
////    //        }
////    //        else if (unexplored.Count > 0)
////    //        {
////    //            chosen = unexplored.OrderByDescending(d => d.node?.value ?? -9999f).First();
////    //            if (debugLog) Debug.Log($"[EXP] [Exploit] unexplored中で最大value選択 dir={chosen.Value.dir}");
////    //        }
////    //    }

////    //    // --------------------------------------------------
////    //    // 結果適用
////    //    // --------------------------------------------------
////    //    if (chosen.HasValue)
////    //    {
////    //        string chosenStr = (chosen.Value.node != null)
////    //            ? $"{chosen.Value.node.name}{CurrCell(chosen.Value.node)}(v={chosen.Value.node.value:F3})"
////    //            : "null";

////    //        Debug.Log($"[EXP] chosen dir={chosen.Value.dir}, node={chosenStr}, hasLink={chosen.Value.hasLink}, current={CurrCell(currentNode)}, prev={CurrCell(prevNode)}");

////    //        moveDir = chosen.Value.dir;
////    //        MoveForward();
////    //    }
////    //    else
////    //    {
////    //        Debug.LogWarning("[EXP] chosen is null (移動候補なし) → 停止");
////    //    }

////    //    Debug.Log("[EXP] === TryExploreMove End ===");
////    //}
////    void TryExploreMove()
////    {
////        // --------------------------------------------------
////        // 現在位置に対応するNodeを取得（なければ生成）
////        // --------------------------------------------------
////        MapNode nextCandidate = TryPlaceNode(transform.position);

////        // prevNodeが未設定で、currentNodeが存在する場合は初期化
////        //if (prevNode == null && currentNode != null)
////        //    prevNode = currentNode;

////        // TryPlaceNode内でcurrentNodeが上書きされないように制御
////        if (nextCandidate != currentNode)
////            currentNode = nextCandidate;

////        string CurrCell(MapNode n) => n != null ? n.cell.ToString() : "null";
////        if (debugLog)
////            Debug.Log($"[EXP] === TryExploreMove Start === current={currentNode?.name ?? "null"}{CurrCell(currentNode)} prev={prevNode?.name ?? "null"}{CurrCell(prevNode)}");

////        // --------------------------------------------------
////        // 周囲方向のスキャン
////        // --------------------------------------------------
////        var dirs = ScanAroundDirections();
////        if (dirs.Count == 0)
////        {
////            Debug.Log("[EXP] dirs.Count == 0 (進行候補なし)");
////            return;
////        }

////        // --------------------------------------------------
////        // 直前方向(backDir)を推定（prevNodeが存在する場合のみ）
////        // --------------------------------------------------
////        Vector3? backDirOpt = null;
////        if (prevNode != null && currentNode != null)
////        {
////            Vector3 fromPrev = (currentNode.transform.position - prevNode.transform.position);
////            if (Mathf.Abs(fromPrev.x) > Mathf.Abs(fromPrev.z))
////                backDirOpt = (fromPrev.x > 0f) ? Vector3.right : Vector3.left;
////            else if (Mathf.Abs(fromPrev.z) > 0f)
////                backDirOpt = (fromPrev.z > 0f) ? Vector3.forward : Vector3.back;
////        }

////        // --------------------------------------------------
////        // スキャン結果をログ出力
////        // --------------------------------------------------
////        if (debugLog)
////        {
////            foreach (var d in dirs)
////            {
////                string n = d.node != null ? $"{d.node.name}{CurrCell(d.node)}" : "null";
////                Debug.Log($"[EXP] dir={d.dir} node={n} hasLink={d.hasLink}");
////            }
////        }

////        // --------------------------------------------------
////        // 候補を分類（prevNode除外）
////        // --------------------------------------------------
////        var unexplored = dirs.Where(d => d.node == null || !d.hasLink).ToList();
////        var known = dirs.Where(d => d.node != null && d.hasLink && d.node != prevNode).ToList();

////        // --------------------------------------------------
////        // 戻り方向(backDir)も除外
////        // --------------------------------------------------
////        if (backDirOpt.HasValue)
////        {
////            Vector3 backDir = backDirOpt.Value;
////            unexplored = unexplored.Where(d => d.dir != backDir).ToList();
////            known = known.Where(d => d.dir != backDir).ToList();

////            if (debugLog)
////                Debug.Log($"[EXP] backDir={backDir} を除外 (prevNode={prevNode?.name ?? "null"}{CurrCell(prevNode)})");
////        }

////        // --------------------------------------------------
////        // 候補数ログ出力
////        // --------------------------------------------------
////        if (debugLog)
////            Debug.Log($"[EXP] unexplored.Count={unexplored.Count}, known.Count={known.Count} (current={CurrCell(currentNode)} prev={CurrCell(prevNode)})");

////        // --------------------------------------------------
////        // 袋小路対策：戻りを許可（prevNodeへのリターン）
////        // --------------------------------------------------
////        if (known.Count == 0 && unexplored.Count == 0 && backDirOpt.HasValue)
////        {
////            var back = dirs.FirstOrDefault(d => d.dir == backDirOpt.Value);
////            if (back.dir != Vector3.zero)
////            {
////                known.Add(back);
////                Debug.LogWarning($"[EXP] dead-end fallback: allow back to prev via dir={back.dir}");
////            }
////        }

////        // --------------------------------------------------
////        // ε-greedyによる探索・活用判断
////        // --------------------------------------------------
////        float r = Random.value;
////        bool doExplore = (r < epsilon);
////        if (debugLog)
////            Debug.Log($"[EXP] epsilon={epsilon:F3}, rand={r:F3} → explore={doExplore}");

////        (Vector3 dir, MapNode node, bool hasLink)? chosen = null;

////        if (doExplore)
////        {
////            // 探索（ランダムな未探索方向を優先）
////            if (unexplored.Count > 0)
////            {
////                chosen = unexplored[Random.Range(0, unexplored.Count)];
////                if (debugLog) Debug.Log($"[EXP] [Explore] unexploredから選択 dir={chosen.Value.dir}");
////            }
////            else if (known.Count > 0)
////            {
////                chosen = known[Random.Range(0, known.Count)];
////                if (debugLog) Debug.Log($"[EXP] [Explore] knownから代替選択 dir={chosen.Value.dir}");
////            }
////        }
////        else
////        {
////            // 活用（valueが高いNodeを優先）
////            if (known.Count > 0)
////            {
////                chosen = known.OrderByDescending(d => d.node?.value ?? -9999f).First();
////                if (debugLog) Debug.Log($"[EXP] [Exploit] known中で最大value選択 dir={chosen.Value.dir}");
////            }
////            else if (unexplored.Count > 0)
////            {
////                chosen = unexplored.OrderByDescending(d => d.node?.value ?? -9999f).First();
////                if (debugLog) Debug.Log($"[EXP] [Exploit] unexplored中で最大value選択 dir={chosen.Value.dir}");
////            }
////        }

////        // --------------------------------------------------
////        // 移動実行
////        // --------------------------------------------------
////        if (chosen.HasValue)
////        {
////            string chosenStr = (chosen.Value.node != null)
////                ? $"{chosen.Value.node.name}{CurrCell(chosen.Value.node)}(v={chosen.Value.node.value:F3})"
////                : "null";

////            if (debugLog)
////                Debug.Log($"[EXP] chosen dir={chosen.Value.dir}, node={chosenStr}, hasLink={chosen.Value.hasLink}, current={CurrCell(currentNode)}, prev={CurrCell(prevNode)}");

////            moveDir = chosen.Value.dir;
////            MoveForward();
////        }
////        else
////        {
////            Debug.LogWarning("[EXP] chosen is null (移動候補なし) → 停止");
////        }

////        Debug.Log("[EXP] === TryExploreMove End ===");
////    }


////    // ======================================================
////    List<(Vector3 dir, MapNode node, bool hasLink)> ScanAroundDirections()
////    {
////        List<(Vector3, MapNode, bool)> found = new();
////        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

////        foreach (var dir in dirs)
////        {
////            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, dir, rayDistance, wallLayer))
////                continue;

////            Vector3 nextPos = SnapToGrid(transform.position + dir * cellSize);
////            Vector2Int nextCell = WorldToCell(nextPos);
////            MapNode nextNode = MapNode.FindByCell(nextCell);
////            bool linked = (currentNode != null && nextNode != null && currentNode.links.Contains(nextNode));

////            found.Add((dir, nextNode, linked));
////        }

////        return found;
////    }

////    // ======================================================
////    void MoveForward()
////    {
////        targetPos = SnapToGrid(transform.position + moveDir * cellSize);
////        isMoving = true;
////    }

////    //// ======================================================
////    //void MoveToTarget()
////    //{
////    //    transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////    //    if (Vector3.Distance(transform.position, targetPos) < EPS)
////    //    {
////    //        transform.position = targetPos;
////    //        isMoving = false;

////    //        Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////    //        MapNode nextNode = MapNode.FindByCell(cell);

////    //        if (currentNode != null && nextNode != null)
////    //        {
////    //            currentNode.AddLink(nextNode);
////    //            LinkBackWithRay(currentNode);
////    //            if (goalNode != null)
////    //                nextNode.UpdateValueByGoal(goalNode);
////    //        }

////    //        // prevNode の更新をここで確実に行う
////    //        prevNode = currentNode;
////    //        currentNode = nextNode;

////    //        if (!reachedGoal && goalNode != null)
////    //        {
////    //            Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
////    //            Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));
////    //            if (playerCell == goalCell)
////    //            {
////    //                reachedGoal = true;
////    //                LinkBackWithRay(currentNode);
////    //                RecalculateGoalDistance();
////    //                Destroy(gameObject);
////    //            }
////    //        }
////    //    }
////    //}
////    //void MoveToTarget()
////    //{
////    //    transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////    //    if (Vector3.Distance(transform.position, targetPos) < EPS)
////    //    {
////    //        transform.position = targetPos;
////    //        isMoving = false;

////    //        Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////    //        MapNode nextNode = MapNode.FindByCell(cell);

////    //        // === リンク確立処理 ===
////    //        if (currentNode != null && nextNode != null)
////    //        {
////    //            // 双方向リンクを張る
////    //            currentNode.AddLink(nextNode);
////    //            LinkBackWithRay(currentNode);

////    //            // 目標ノードへの距離更新
////    //            if (goalNode != null)
////    //                nextNode.UpdateValueByGoal(goalNode);

////    //            // ★ここがポイント：リンク確立後にprevNodeを更新
////    //            prevNode = currentNode;
////    //        }

////    //        // ★最後にcurrentNodeを切り替える
////    //        currentNode = nextNode;

////    //        // === ゴール判定 ===
////    //        if (!reachedGoal && goalNode != null)
////    //        {
////    //            Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
////    //            Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));
////    //            if (playerCell == goalCell)
////    //            {
////    //                reachedGoal = true;
////    //                LinkBackWithRay(currentNode);
////    //                RecalculateGoalDistance();
////    //                Destroy(gameObject);
////    //            }
////    //        }
////    //    }
////    //}
////    //void MoveToTarget()
////    //{
////    //    transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////    //    if (Vector3.Distance(transform.position, targetPos) < EPS)
////    //    {
////    //        transform.position = targetPos;
////    //        isMoving = false;

////    //        Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////    //        MapNode nextNode = MapNode.FindByCell(cell);

////    //        // === リンク確立処理 ===
////    //        if (currentNode != null && nextNode != null)
////    //        {
////    //            // 双方向リンクを張る
////    //            currentNode.AddLink(nextNode);
////    //            LinkBackWithRay(currentNode);

////    //            // 目標ノードへの距離更新
////    //            if (goalNode != null)
////    //                nextNode.UpdateValueByGoal(goalNode);

////    //            // ★ prevNode を「リンクされたNodeの中で最も近いNode」に設定
////    //            MapNode nearestLinked = nextNode.links
////    //                .Where(n => n != nextNode)
////    //                .OrderBy(n => Vector3.Distance(n.transform.position, nextNode.transform.position))
////    //                .FirstOrDefault();

////    //            prevNode = nearestLinked;

////    //            if (debugLog)
////    //                Debug.Log($"[MOVE] prevNode updated -> {(prevNode != null ? prevNode.name : "null")}");
////    //        }

////    //        // ★最後に currentNode を nextNode に切り替える
////    //        currentNode = nextNode;

////    //        // === ゴール判定 ===
////    //        if (!reachedGoal && goalNode != null)
////    //        {
////    //            Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
////    //            Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));
////    //            if (playerCell == goalCell)
////    //            {
////    //                reachedGoal = true;
////    //                LinkBackWithRay(currentNode);
////    //                RecalculateGoalDistance();
////    //                Destroy(gameObject);
////    //            }
////    //        }
////    //    }
////    //}
////    void MoveToTarget()
////    {
////        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
////        if (Vector3.Distance(transform.position, targetPos) < EPS)
////        {
////            transform.position = targetPos;
////            isMoving = false;

////            Vector2Int cell = WorldToCell(SnapToGrid(transform.position));
////            MapNode nextNode = MapNode.FindByCell(cell);

////            if (currentNode != null && nextNode != null)
////            {
////                // 双方向リンクを張る
////                currentNode.AddLink(nextNode);
////                LinkBackWithRay(currentNode);

////                // prevNodeを「リンクを張ったNode」として更新
////                if (prevNode != currentNode)
////                    prevNode = currentNode;

////                if (goalNode != null)
////                    nextNode.UpdateValueByGoal(goalNode);
////            }

////            // currentNode切り替え
////            currentNode = nextNode;

////            // ゴール判定
////            if (!reachedGoal && goalNode != null)
////            {
////                Vector2Int playerCell = WorldToCell(SnapToGrid(transform.position));
////                Vector2Int goalCell = WorldToCell(SnapToGrid(goalNode.transform.position));
////                if (playerCell == goalCell)
////                {
////                    reachedGoal = true;
////                    LinkBackWithRay(currentNode);
////                    RecalculateGoalDistance();
////                    Destroy(gameObject);
////                }
////            }

////            if (debugLog)
////                Debug.Log($"[MOVE] prevNode={prevNode?.cell}, currentNode={currentNode?.cell}");
////        }
////    }


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

////                // 壁なら打ち切り
////                if ((wallLayer.value & (1 << hitLayer)) != 0)
////                    return;

////                // Nodeなら接続
////                if ((nodeLayer.value & (1 << hitLayer)) != 0)
////                {
////                    MapNode hitNode = hit.collider.GetComponent<MapNode>();
////                    if (hitNode != null && hitNode != node)
////                    {
////                        node.AddLink(hitNode);
////                        if (debugLog)
////                            Debug.Log($"[LINK-OK] {node.name} ↔ {hitNode.name}");
////                    }
////                    return;
////                }
////            }
////        }
////    }


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
////    MapNode TryPlaceNode(Vector3 pos)
////    {
////        Vector2Int cell = WorldToCell(SnapToGrid(pos));
////        MapNode node;

////        if (MapNode.allNodeCells.Contains(cell))
////        {
////            node = MapNode.FindByCell(cell);
////            if (debugLog) Debug.Log($"[Node] Reuse existing Node @ {cell}");
////        }
////        else
////        {
////            // ★ prevNode をここで更新する
////            //if (currentNode != null)
////            //    prevNode = currentNode;

////            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
////            node = obj.GetComponent<MapNode>();
////            node.cell = cell;
////            MapNode.allNodeCells.Add(cell);
////            if (debugLog) Debug.Log($"[Node] New Node placed @ {cell}");
////        }

////        if (node != null)
////        {
////            LinkBackWithRay(node);
////            if (goalNode != null)
////                node.UpdateValueByGoal(goalNode);
////        }

////        return node;
////    }
////    //MapNode TryPlaceNode(Vector3 worldPos)
////    //{
////    //    // ==============================
////    //    // グリッド座標を算出
////    //    // ==============================
////    //    Vector2Int cell = WorldToCell(SnapToGrid(worldPos));

////    //    // 既存ノード探索
////    //    MapNode existing = MapNode.FindByCell(cell);
////    //    if (existing != null)
////    //    {
////    //        if (debugLog)
////    //            Debug.Log($"[Node] Reuse existing Node @ {cell}");

////    //        // ★ここではprevNodeを上書きしない（リンク形成時にのみ更新）
////    //        return existing;
////    //    }

////    //    // ==============================
////    //    // 新規ノード生成
////    //    // ==============================
////    //    Vector3 spawnPos = CellToWorld(cell);
////    //    GameObject obj = Instantiate(nodePrefab, spawnPos, Quaternion.identity);
////    //    MapNode newNode = obj.GetComponent<MapNode>();
////    //    if (newNode == null)
////    //    {
////    //        Debug.LogError("[Node] Missing MapNode component!");
////    //        return null;
////    //    }

////    //    newNode.name = $"Node({cell.x}, {cell.y})";

////    //    if (debugLog)
////    //        Debug.Log($"[Node] New Node placed @ {cell}");

////    //    // ==============================
////    //    // リンク確立処理
////    //    // ==============================
////    //    if (currentNode != null && newNode != null)
////    //    {
////    //        currentNode.AddLink(newNode);
////    //        LinkBackWithRay(currentNode);

////    //        if (goalNode != null)
////    //            newNode.UpdateValueByGoal(goalNode);

////    //        // ★追加：prevNodeをリンク形成直後に更新
////    //        if (prevNode != currentNode)
////    //        {
////    //            prevNode = currentNode;
////    //            if (debugLog)
////    //                Debug.Log($"[LINK] prevNode updated -> {prevNode.cell}, currentNode={currentNode.cell}, newNode={newNode.cell}");
////    //        }
////    //    }

////    //    // ==============================
////    //    // currentNodeを新ノードに更新
////    //    // ==============================
////    //    currentNode = newNode;

////    //    return newNode;
////    //}


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