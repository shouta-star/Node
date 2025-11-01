using UnityEngine;
using System.Collections.Generic;

public class Player : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed;
    public float cellSize = 1f;
    public float rayDistance = 1f;
    public LayerMask wallLayer;

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward;

    [Header("Node")]
    public GameObject nodePrefab;
    public Vector3 gridOrigin = Vector3.zero; // グリッド原点（必要なら調整）

    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;

    // ★ 追加：設置済みセル集合
    private readonly HashSet<Vector2Int> placedNodeCells = new();

    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position;

        // 初期位置にNodeを置きたい場合は以下を有効化
        // TryPlaceNode(transform.position);
    }

    void Update()
    {
        if (!isMoving) TryMove();
        else MoveToTarget();
    }

    void TryMove()
    {
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

        Debug.DrawRay(transform.position, moveDir * rayDistance, Color.red);
        Debug.DrawRay(transform.position, leftDir * rayDistance, Color.blue);
        Debug.DrawRay(transform.position, rightDir * rayDistance, Color.green);

        // ★ 曲がり角 or 分岐点でNode設置（未設置セルのみ）
        int openCount = 0;
        if (!frontHit) openCount++;
        if (!leftHit) openCount++;
        if (!rightHit) openCount++;

        // 前が壁、または通れる方向が2つ以上（＝分岐・曲がり角）
        if (frontHit || openCount >= 2)
        {
            TryPlaceNode(transform.position);
        }

        // 前が空いていればそのまま進行
        if (!frontHit)
        {
            targetPos = transform.position + moveDir * cellSize;
            isMoving = true;
        }
        else
        {
            // 前が壁なら左右のどちらか空いている方向へ方向転換
            var open = new List<Vector3>(2);
            if (!leftHit) open.Add(leftDir);
            if (!rightHit) open.Add(rightDir);

            if (open.Count > 0)
            {
                moveDir = open[Random.Range(0, open.Count)];
                targetPos = transform.position + moveDir * cellSize;
                isMoving = true;
            }
        }
    }

    void MoveToTarget()
    {
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
        {
            transform.position = targetPos;
            isMoving = false;
        }
    }

    // ★ グリッド重複チェック付きNode設置
    void TryPlaceNode(Vector3 worldPos)
    {
        Vector2Int cell = WorldToCell(worldPos);
        if (placedNodeCells.Add(cell)) // 未登録セルならtrue
        {
            Vector3 placePos = CellToWorld(cell);
            Instantiate(nodePrefab, placePos, Quaternion.identity);
        }
        // 登録済みセルならスキップ
    }

    // ★ 座標変換（原点・セルサイズ対応）
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
}
