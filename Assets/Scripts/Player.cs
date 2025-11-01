using UnityEngine;
using System.Collections.Generic;

public class PlayerMoveGrid : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 5f;
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

    private bool prevLeftHit;
    private bool prevRightHit;

    // ★ 追加：設置済みセル集合
    private readonly HashSet<Vector2Int> placedNodeCells = new();

    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position;

        // 開始セルに何か置きたくなる場合に備えて初期登録だけしておく場合はコメント解除
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

        // ★ Rayの変化でNode設置（重複防止付き）
        if (leftHit != prevLeftHit || rightHit != prevRightHit)
        {
            TryPlaceNode(transform.position);

            // ランダム方向変更（前・左・右のうち通れる方向）
            var options = new List<Vector3>(3);
            if (!frontHit) options.Add(moveDir);
            if (!leftHit) options.Add(leftDir);
            if (!rightHit) options.Add(rightDir);
            if (options.Count > 0) moveDir = options[Random.Range(0, options.Count)];
        }

        prevLeftHit = leftHit;
        prevRightHit = rightHit;

        // 前が空いていれば前へ、ダメなら左右空きからランダム
        if (!frontHit)
        {
            targetPos = transform.position + moveDir * cellSize;
            isMoving = true;
        }
        else
        {
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

    // ★ 追加：グリッド重複チェック付きNode設置
    void TryPlaceNode(Vector3 worldPos)
    {
        Vector2Int cell = WorldToCell(worldPos);
        if (placedNodeCells.Add(cell)) // 未登録セルならtrue
        {
            // セル中央に置きたい場合は CellToWorld(cell) を使う
            Vector3 placePos = CellToWorld(cell);
            Instantiate(nodePrefab, placePos, Quaternion.identity);
        }
        // 登録済みならスキップ
    }

    // ★ 追加：座標変換（原点・セルサイズ対応）
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
