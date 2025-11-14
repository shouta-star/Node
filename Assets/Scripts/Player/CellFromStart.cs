//// ======================================================
// CellFromStart.cs
// ・UnknownCount（未知数）＋ DistanceFromStart（Startからの距離）を使った
//   ハイブリッドスコアで方向選択
// ・weightUnknown = 1.0, weightDistance = 1.0
//// ======================================================

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

public class CellFromStart : MonoBehaviour
{
    // ======================================================
    // パラメータ設定
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

    [Header("行動傾向")]
    [Range(0f, 1f)] public float exploreBias = 0.6f;

    [Header("探索パラメータ")]
    public int unknownReferenceDepth;

    [Header("リンク探索")]
    public int linkRayMaxSteps = 100;

    [Header("スコア重み")]
    public float weightUnknown = 1.0f;
    public float weightDistance = 1.0f;

    [Header("デバッグ")]
    public bool debugLog = true;
    public bool debugRay = true;
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Material exploreMaterial;

    // ======================================================
    // 内部状態変数
    // ======================================================

    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private MapNode currentNode;

    private const float EPS = 1e-4f;

    private List<MapNode> recentNodes = new List<MapNode>();

    // ======================================================
    // Start
    // ======================================================
    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position = SnapToGrid(transform.position);
        ApplyVisual();
        currentNode = TryPlaceNode(transform.position);

        RegisterCurrentNode(currentNode);

        if (debugLog) Debug.Log($"[Player:{name}] Start @ {currentNode}");
    }

    // ======================================================
    // Update
    // ======================================================
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
    // ApplyVisual
    // ======================================================
    private void ApplyVisual()
    {
        if (bodyRenderer == null) return;
        bodyRenderer.material = exploreMaterial
            ? exploreMaterial
            : new Material(Shader.Find("Standard")) { color = Color.cyan };
    }

    // ======================================================
    // CanPlaceNodeHere
    // ======================================================
    bool CanPlaceNodeHere()
    {
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, moveDir,
                                        rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, leftDir,
                                       rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position + Vector3.up * 0.1f, rightDir,
                                        rayDistance, wallLayer);

        int openCount = 0;
        if (!frontHit) openCount++;
        if (!leftHit) openCount++;
        if (!rightHit) openCount++;

        return (frontHit || openCount >= 2);
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
    // MoveToTarget
    // ======================================================
    private void MoveToTarget()
    {
        if (Vector3.Distance(transform.position, targetPos) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                moveSpeed * Time.deltaTime
            );
        }
        else
        {
            transform.position = targetPos;
            isMoving = false;
        }
    }

    // ======================================================
    // Node 履歴管理
    // ======================================================
    private void RegisterCurrentNode(MapNode node)
    {
        if (node == null) return;

        if (recentNodes.Count > 0 && recentNodes[recentNodes.Count - 1] == node)
            return;

        recentNodes.Add(node);

        int maxDepth = Mathf.Max(unknownReferenceDepth, 1);
        while (recentNodes.Count > maxDepth)
            recentNodes.RemoveAt(0);

        if (debugLog)
        {
            string hist = string.Join(" -> ",
                           recentNodes.Select(n => n != null ? n.name : "null"));
            Debug.Log($"[HIST] {hist}");
        }
    }

    // ======================================================
    // TryExploreMove：方向選択
    // ======================================================
    void TryExploreMove()
    {
        currentNode = TryPlaceNode(transform.position);
        if (debugLog) Debug.Log("[Player] Node placed → decide next direction");

        //Debug.Log($"[EXP-DEBUG] Node placed: {currentNode.name}  U={currentNode.unknownCount}  D={currentNode.distanceFromStart}");

        RegisterCurrentNode(currentNode);

        // 終端なら未知方向探索へ
        if (IsTerminalNode(currentNode))
        {
            //Debug.Log("[EXP] Terminal → TryMoveToUnlinkedDirection()");
            TryMoveToUnlinkedDirection();
            return;
        }

        if (currentNode == null || currentNode.links.Count == 0)
        {
            //Debug.Log("[EXP] No links → TryMoveToUnlinkedDirection()");
            TryMoveToUnlinkedDirection();
            return;
        }

        // ★ Unknown + Distance のスコアで選ぶ
        MapNode next = ChooseNextNodeByScore(currentNode);

        if (next != null)
        {
            moveDir = (next.transform.position - transform.position).normalized;
            //Debug.Log($"[EXP-SELECT] Move to {next.name}  (U={next.unknownCount}, D={next.distanceFromStart})");
            MoveForward();
        }
        else
        {
           // Debug.Log("[EXP-SELECT] NULL → TryMoveToUnlinkedDirection()");
            TryMoveToUnlinkedDirection();
        }
    }

    // ======================================================
    // スコア計算
    // ======================================================
    private float CalcNodeScore(MapNode node)
    {
        if (node == null) return -999999f;

        float u = node.unknownCount;
        float d = node.distanceFromStart;

        float score = weightUnknown * u + weightDistance * (-d);

        if (debugLog)
            Debug.Log($"[SCORE] {node.name}: U={u}, D={d} → score={score}");

        return score;
    }

    // ======================================================
    // ChooseNextNodeByScore：スコア方式
    // ======================================================
    private MapNode ChooseNextNodeByScore(MapNode current)
    {
        if (current == null || current.links.Count == 0)
            return null;

        //Debug.Log("=== [DEBUG] recentNodes 状況 ===");
        //for (int i = 0; i < recentNodes.Count; i++)
        //{
        //    var n = recentNodes[i];
        //    if (n != null)
        //        Debug.Log($"  recent[{i}] = {n.name}  U={n.unknownCount}  D={n.distanceFromStart}");
        //    else
        //        Debug.Log($"  recent[{i}] = null");
        //}

        // 履歴を使うかどうかは従来部を再利用
        if (unknownReferenceDepth > 0 && recentNodes.Count > 0)
        {
            MapNode bestNode = null;
            float bestU = -1;

            foreach (var n in recentNodes)
            {
                if (n == null) continue;
                if (n.unknownCount > bestU)
                {
                    bestU = n.unknownCount;
                    bestNode = n;
                }
            }

            //Debug.Log($"[DEBUG] 履歴評価結果: bestNode={bestNode?.name}, bestU={bestU}");

            // 履歴上で最も未知数が高いノードにいる場合 → 新規開拓
            if (bestNode == current)
                return null;
            //Debug.Log("[DEBUG] bestNode が current → 新規方向開拓へ");
        }

        // ★ スコア方式でリンク先を選ぶ
        var best = current.links
            .OrderByDescending(n => CalcNodeScore(n))
            .ThenBy(_ => Random.value)
            .FirstOrDefault();

        if (best != null)
            Debug.Log($"[SCORE-SELECT] {current.name} → {best.name}");

        return best;
    }

    // ======================================================
    // 終端ノード判定
    // ======================================================
    private bool IsTerminalNode(MapNode node)
    {
        return node != null && node.links != null && node.links.Count == 1;
    }

    // ======================================================
    // TryMoveToUnlinkedDirection（未知方向探索）
    // ======================================================
    private void TryMoveToUnlinkedDirection()
    {
        if (currentNode == null)
        {
            MoveForward();
            return;
        }

        List<Vector3> allDirs = new List<Vector3>
        {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right
        };

        Vector3 backDir = (-moveDir).normalized;

        // ① 戻る方向を除外
        List<Vector3> afterBack = new List<Vector3>();
        foreach (var d in allDirs)
        {
            if (Vector3.Dot(d.normalized, backDir) > 0.7f) continue;
            afterBack.Add(d);
        }

        // ② 既存リンク除外
        List<Vector3> afterLinked = new List<Vector3>();
        foreach (var d in afterBack)
        {
            bool linked = false;
            foreach (var link in currentNode.links)
            {
                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
                if (Vector3.Dot(diff, d.normalized) > 0.7f)
                {
                    linked = true;
                    break;
                }
            }
            if (!linked) afterLinked.Add(d);
        }

        // ③ 壁除外
        List<Vector3> validDirs = new List<Vector3>();
        Vector3 origin = currentNode.transform.position + Vector3.up * 0.1f;
        foreach (var d in afterLinked)
        {
            if (!Physics.Raycast(origin, d, cellSize, wallLayer))
                validDirs.Add(d);
        }

        if (validDirs.Count == 0)
        {
            // 戻れない場合は停止
            foreach (var link in currentNode.links)
            {
                Vector3 diff = (link.transform.position - currentNode.transform.position).normalized;
                if (Vector3.Dot(diff, backDir) < 0.7f)
                {
                    moveDir = diff;
                    MoveForward();
                    return;
                }
            }
            return;
        }

        moveDir = validDirs[UnityEngine.Random.Range(0, validDirs.Count)];
        MoveForward();
    }

    // ======================================================
    // ノード設置処理
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
        }

        // StartNode 設定
        if (MapNode.StartNode == null)
        {
            MapNode.StartNode = node;
            node.distanceFromStart = 0;
        }

        // リンク更新
        if (node != null)
            LinkBackWithRay(node);

        return node;
    }

    // ======================================================
    // LinkBackWithRay：後方リンク探索
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
                        node.RecalculateUnknownAndWall();
                        hitNode.RecalculateUnknownAndWall();
                    }
                    return;
                }
            }
        }
    }

    // ======================================================
    // 座標変換
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
