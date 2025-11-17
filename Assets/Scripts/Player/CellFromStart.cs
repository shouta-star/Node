/// <summary>
/// CellFromStart（改良版）　B版
/// UnknownCount・DistanceFromStart を用いた探索＋最適化ハイブリッドAI
/// 終端では Unknown最優先＋複数候補なら Distance を採用
/// </summary>
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
    public int unknownReferenceDepth = 3;

    [Header("スコア重み")]
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

    void Start()
    {
        moveDir = startDirection.normalized;
        transform.position = SnapToGrid(transform.position);
        targetPos = transform.position;

        ApplyVisual();

        currentNode = TryPlaceNode(transform.position);
        RegisterCurrentNode(currentNode);

        Log($"Start @ Node={currentNode.name}");
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

    //private void MoveForward()
    //{
    //    targetPos = SnapToGrid(transform.position + moveDir * cellSize);
    //    isMoving = true;
    //}
    private void MoveForward()
    {
        Vector3 next = transform.position + moveDir * cellSize;

        // ★ 壁チェック追加
        if (Physics.Raycast(transform.position + Vector3.up * 0.1f,
                            moveDir,
                            cellSize,
                            wallLayer))
        {
            // 壁なら進まない
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

    private void TryExploreMove()
    {
        MapNode before = currentNode;

        currentNode = TryPlaceNode(transform.position);
        Debug.Log($"[TryExploreMove] currentNode = {currentNode?.name} (was {before?.name})");

        Vector3 back = -moveDir;
        Debug.Log($"[TryExploreMove] moveDir={moveDir}, backDir={back}");

        currentNode = TryPlaceNode(transform.position);
        RegisterCurrentNode(currentNode);

        Log($"Node placed → {currentNode.name}");

        // ========== ① 終端Nodeの場合特別処理 ==========
        if (IsTerminalNode(currentNode))
        {
            Vector3? dir = ChooseTerminalDirection(currentNode);
            if (dir.HasValue)
            {
                moveDir = dir.Value;
                MoveForward();
            }
            else
            {
                moveDir = moveDir; // fallback
                MoveForward();
            }
            return;
        }

        // ========== ② リンクが無い → 未知方向へ ==========
        if (currentNode.links.Count == 0)
        {
            MoveToUnlinked();
            return;
        }

        // ========== ③ 通常：Unknown + Distance のハイブリッドスコア ==========
        MapNode next = ChooseNextByScore(currentNode);
        if (next != null)
        {
            moveDir = (next.transform.position - transform.position).normalized;
            MoveForward();
            return;
        }

        // ========== ④ fallback ==========
        MoveToUnlinked();
    }

    //private Vector3? ChooseTerminalDirection(MapNode node)
    //{
    //    List<Vector3> dirs = AllMovesExceptBack();

    //    // リンク方向を除外
    //    dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

    //    // 壁方向を除外
    //    List<Vector3> unknownDirs = dirs.Where(d => !IsWall(node, d)).ToList();

    //    if (unknownDirs.Count == 0)
    //        return null;

    //    if (unknownDirs.Count == 1)
    //        return unknownDirs[0];

    //    // 複数ある場合 → DistanceFromStart を使う
    //    int bestScore = int.MinValue;
    //    Vector3 best = unknownDirs[0];

    //    // ★ Distance候補にも壁チェックを追加
    //    unknownDirs = unknownDirs.Where(d => !IsWall(node, d)).ToList();

    //    foreach (var d in unknownDirs)
    //    {
    //        Vector2Int cell = WorldToCell(node.transform.position + d * cellSize);
    //        MapNode near = MapNode.FindByCell(cell);
    //        if (near == null) continue;

    //        int score = -near.distanceFromStart; // Startから遠いほど高評価
    //        if (score > bestScore)
    //        {
    //            bestScore = score;
    //            best = d;
    //        }
    //    }

    //    return best;
    //}
    private Vector3? ChooseTerminalDirection(MapNode node)
    {
        List<Vector3> dirs = AllMovesExceptBack();

        // リンク方向を除外
        dirs = dirs.Where(d => !IsLinkedDirection(node, d)).ToList();

        // 壁方向を除外
        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

        if (dirs.Count == 0)
            return null;

        if (dirs.Count == 1)
            return dirs[0];

        // ★Distanceスコア決定にも壁チェックを入れる
        dirs = dirs.Where(d => !IsWall(node, d)).ToList();

        int bestScore = int.MinValue;
        Vector3 bestDir = dirs[0];

        foreach (var d in dirs)
        {
            Vector2Int cell = WorldToCell(node.transform.position + d * cellSize);
            MapNode near = MapNode.FindByCell(cell);
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


    private bool IsTerminalNode(MapNode node)
        => node != null && node.links.Count == 1;

    //private MapNode ChooseNextByScore(MapNode current)
    //{
    //    // 履歴中の未知方向が最も多いNodeが current なら未知探索へ
    //    if (recentNodes.Count > 0)
    //    {
    //        MapNode best = recentNodes.OrderByDescending(n => n.unknownCount).First();
    //        if (best == current)
    //            return null;
    //    }

    //    return current.links
    //        .OrderByDescending(n => Score(n))
    //        .ThenBy(_ => Random.value)
    //        .FirstOrDefault();
    //}
    //private MapNode ChooseNextByScore(MapNode current)
    //{
    //    // 履歴のunknownが最大で current と同じなら未知方向へ
    //    if (recentNodes.Count > 0)
    //    {
    //        MapNode best = recentNodes.OrderByDescending(n => n.unknownCount).First();
    //        if (best == current)
    //            return null; // 未知方向優先
    //    }

    //    // ★リンク方向でも必ず壁チェックする
    //    var candidates = current.links
    //        .Where(n => n != null)
    //        .Where(n =>
    //        {
    //            Vector3 dir = (n.transform.position - current.transform.position).normalized;
    //            return !IsWall(current, dir);
    //        })
    //        .ToList();

    //    if (candidates.Count == 0)
    //        return null;

    //    return candidates
    //        .OrderByDescending(n => Score(n))
    //        .ThenBy(_ => Random.value)
    //        .FirstOrDefault();
    //}
    private MapNode ChooseNextByScore(MapNode current)
    {
        //Debug.Log($"[Score] ----------");
        Debug.Log($"[Score] currentNode = {current.name}");
        Debug.Log($"[Score] recentNodes = {string.Join(", ", recentNodes.Select(n => n.name))}");

        // 履歴のunknownが最大で current と同じなら未知方向へ
        if (recentNodes.Count > 0)
        {
            MapNode bestHist = recentNodes.OrderByDescending(n => n.unknownCount).First();
            Debug.Log($"[Score] bestHist = {bestHist.name}, U={bestHist.unknownCount}");

            if (bestHist == current)
            {
                Debug.Log($"[Score] → 履歴の最大未知数が current と一致 → 未知方向探索に切り替え（return null）");
                return null; // 未知方向優先
            }
        }

        // --------------------------
        // ★ 壁チェック込みのリンク候補抽出
        // --------------------------

        List<(MapNode node, Vector3 dir, bool isWall, float score)> logs
            = new List<(MapNode, Vector3, bool, float)>();

        foreach (var n in current.links)
        {
            if (n == null) continue;

            Vector3 dir = (n.transform.position - current.transform.position).normalized;
            bool wall = IsWall(current, dir);
            float sc = Score(n);

            logs.Add((n, dir, wall, sc));
        }

        foreach (var L in logs)
        {
            Debug.Log($"[Score] link: {L.node.name}, dir={L.dir}, isWall={L.isWall}, score={L.score}");
        }

        var candidates = logs
            .Where(L => !L.isWall)
            .Select(L => L.node)
            .ToList();

        Debug.Log($"[Score] candidates = {string.Join(", ", candidates.Select(n => n.name))}");

        if (candidates.Count == 0)
        {
            Debug.Log("[Score] → 候補ゼロ → return null");
            return null;
        }

        var selected = candidates
            .OrderByDescending(n => Score(n))
            .ThenBy(_ => Random.value)
            .FirstOrDefault();

        Debug.Log($"[Score] SELECTED = {selected.name}");
        return selected;
    }
    //private MapNode ChooseNextByScore(MapNode current)
    //{
    //    Debug.Log($"[Score] currentNode = {current.name}");
    //    Debug.Log($"[Score] recentNodes = {string.Join(", ", recentNodes.Select(n => n.name))}");

    //    // ======================================================
    //    // ① 履歴（recentNodes）の中で unknownCount が最大の Node を調べる
    //    // ======================================================
    //    if (recentNodes.Count > 0)
    //    {
    //        MapNode bestHist = recentNodes
    //            .OrderByDescending(n => n.unknownCount)
    //            .First();

    //        Debug.Log($"[Score] bestHist = {bestHist.name}, U={bestHist.unknownCount}");

    //        // → 同じなら未知方向へ（＝リンク以外を見るため return null）
    //        if (bestHist == current)
    //        {
    //            Debug.Log("[Score] → 履歴最大未知数が current と一致 → 未知方向探索（return null）");
    //            return null;
    //        }
    //    }

    //    // ======================================================
    //    // ② リンク方向のログ収集（壁判定もログに記録）
    //    // ======================================================
    //    List<(MapNode node, Vector3 dir, bool isWall, float score)> logs
    //        = new();

    //    foreach (var n in current.links)
    //    {
    //        if (n == null) continue;

    //        Vector3 dir = (n.transform.position - current.transform.position).normalized;

    //        bool wall = IsWall(current, dir);
    //        float sc = Score(n);

    //        logs.Add((n, dir, wall, sc));
    //    }

    //    foreach (var L in logs)
    //    {
    //        Debug.Log($"[Score] link: {L.node.name}, dir={L.dir}, isWall={L.isWall}, score={L.score}");
    //    }

    //    // ======================================================
    //    // ③ 候補抽出：壁方向・背後方向(prevNode) を除外
    //    // ======================================================

    //    MapNode prevNode = (recentNodes.Count >= 2 ? recentNodes[^2] : null);

    //    var candidates = logs
    //        .Where(L => !L.isWall)              // ★ 壁方向は除外
    //        //.Where(L => L.node != prevNode)     // ★ 背後の Node を除外
    //        .Select(L => L.node)
    //        .ToList();

    //    Debug.Log($"[Score] candidates = {string.Join(", ", candidates.Select(n => n.name))}");

    //    // 候補が無い場合 → 未知方向へ移行
    //    if (candidates.Count == 0)
    //    {
    //        Debug.Log("[Score] → 候補ゼロ → return null");
    //        return null;
    //    }

    //    // ======================================================
    //    // ④ 評価の高いリンク方向へ進む
    //    // ======================================================
    //    var selected = candidates
    //        .OrderByDescending(n => Score(n))
    //        .ThenBy(_ => Random.value)   // スコアが同じときランダム
    //        .First();

    //    Debug.Log($"[Score] SELECTED = {selected.name}");
    //    return selected;
    //}


    private float Score(MapNode n)
    {
        float u = n.unknownCount;
        float d = n.distanceFromStart;

        return weightUnknown * u + weightDistance * (-d);
    }

    private void MoveToUnlinked()
    {
        List<Vector3> dirs = AllMovesExceptBack();

        dirs = dirs.Where(d => !IsLinkedDirection(currentNode, d)).ToList();
        dirs = dirs.Where(d => !IsWall(currentNode, d)).ToList();

        if (dirs.Count == 0)
        {
            // 仕方なく戻る
            moveDir = -moveDir;
            MoveForward();
            return;
        }

        moveDir = dirs[Random.Range(0, dirs.Count)];
        MoveForward();
    }

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
    //private bool IsWall(MapNode from, Vector3 dir)
    //{
    //    Vector3 origin = from.transform.position + Vector3.up;

    //    float dist = cellSize * 0.9f;

    //    if (Physics.Raycast(origin, dir, dist, wallLayer))
    //        return true;

    //    return false;
    //}



    //private MapNode TryPlaceNode(Vector3 pos)
    //{
    //    Vector2Int cell = WorldToCell(SnapToGrid(pos));
    //    MapNode node;

    //    if (MapNode.allNodeCells.Contains(cell))
    //        node = MapNode.FindByCell(cell);
    //    else
    //    {
    //        GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
    //        node = obj.GetComponent<MapNode>();
    //        node.cell = cell;
    //        MapNode.allNodeCells.Add(cell);
    //    }

    //    if (MapNode.StartNode == null)
    //    {
    //        MapNode.StartNode = node;
    //        node.distanceFromStart = 0;
    //    }

    //    LinkBackward(node);

    //    return node;
    //}
    private MapNode TryPlaceNode(Vector3 pos)
    {
        Vector3 snapped = SnapToGrid(pos);
        Vector2Int cell = WorldToCell(snapped);

        Debug.Log($"[TryPlaceNode] pos={pos}, snapped={snapped}, cell={cell}");

        // 壁チェック
        bool isWall = Physics.Raycast(snapped + Vector3.up * 0.1f, Vector3.down, 1f, wallLayer);
        Debug.Log($"[TryPlaceNode] isWall={isWall}");

        if (isWall)
        {
            MapNode exist = MapNode.FindByCell(cell);
            Debug.Log($"[TryPlaceNode] WALL → existing={exist}");
            return exist;
        }

        MapNode node;

        if (MapNode.allNodeCells.Contains(cell))
        {
            node = MapNode.FindByCell(cell);
            Debug.Log($"[TryPlaceNode] Reuse node={node.name}");
        }
        else
        {
            Debug.Log($"[TryPlaceNode] New Node @ {cell}");
            GameObject obj = Instantiate(nodePrefab, CellToWorld(cell), Quaternion.identity);
            node = obj.GetComponent<MapNode>();
            node.cell = cell;
            MapNode.allNodeCells.Add(cell);
        }

        Debug.Log($"[TryPlaceNode] RETURN node={node.name}");

        LinkBackward(node);
        return node;
    }


    private void LinkBackward(MapNode node)
    {
        Vector3 origin = node.transform.position + Vector3.up * 0.1f;
        Vector3 dir = -moveDir;

        LayerMask mask = wallLayer | nodeLayer;

        for (int i = 1; i <= linkRayMaxSteps; i++)
        {
            float dist = cellSize * i;

            if (debugRay)
                Debug.DrawRay(origin, dir * dist, Color.yellow, 0.25f);

            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, mask))
            {
                int layer = hit.collider.gameObject.layer;

                if ((wallLayer.value & (1 << layer)) != 0)
                    return;

                if ((nodeLayer.value & (1 << layer)) != 0)
                {
                    var hitNode = hit.collider.GetComponent<MapNode>();
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