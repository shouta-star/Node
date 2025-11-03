using UnityEngine;
using System.Collections.Generic;

public class Player : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 3f;          // 移動速度
    public float cellSize = 1f;           // グリッド1マスの大きさ
    public float rayDistance = 1f;        // Rayの距離（1マス分）
    public LayerMask wallLayer;           // 壁レイヤー

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward;

    [Header("Node設定")]
    public GameObject nodePrefab;
    public Vector3 gridOrigin = Vector3.zero; // グリッド原点
    public bool debugLog = true;

    // 内部状態
    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;

    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position;

        // スナップして初期位置を補正
        transform.position = SnapToGrid(transform.position);
    }

    void Update()
    {
        if (!isMoving) TryMove();
        else MoveToTarget();
    }

    // =====================================================
    // 次の移動先を決定して移動を開始
    // =====================================================
    void TryMove()
    {
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

        // Debug用のRay可視化
        Debug.DrawRay(transform.position, moveDir * rayDistance, Color.red);
        Debug.DrawRay(transform.position, leftDir * rayDistance, Color.blue);
        Debug.DrawRay(transform.position, rightDir * rayDistance, Color.green);

        // 進行可能方向数を数える
        int openCount = 0;
        if (!frontHit) openCount++;
        if (!leftHit) openCount++;
        if (!rightHit) openCount++;

        // =============================
        // Node設置条件（前が壁 or 分岐点）
        // =============================
        if (frontHit || openCount >= 2)
        {
            TryPlaceNode(transform.position);
        }

        // =============================
        // 移動先を決定
        // =============================

        // 前方が開いているなら直進
        if (!frontHit)
        {
            Vector3 snappedPos = SnapToGrid(transform.position);
            targetPos = SnapToGrid(snappedPos + moveDir * cellSize);
            isMoving = true;

            if (debugLog)
                Debug.Log($"[Player:{name}] Move forward -> {WorldToCell(targetPos)}");
        }
        else
        {
            // 前が壁なら左右の空き方向を探索
            var openDirs = new List<Vector3>();
            if (!leftHit) openDirs.Add(leftDir);
            if (!rightHit) openDirs.Add(rightDir);

            // 開いている方向があればランダムで選択
            if (openDirs.Count > 0)
            {
                moveDir = openDirs[Random.Range(0, openDirs.Count)];
                Vector3 snappedPos = SnapToGrid(transform.position);
                targetPos = SnapToGrid(snappedPos + moveDir * cellSize);
                isMoving = true;

                if (debugLog)
                    Debug.Log($"[Player:{name}] Turn -> {moveDir}, target={WorldToCell(targetPos)}");
            }
            else
            {
                // 完全に行き止まり
                if (debugLog)
                    Debug.Log($"[Player:{name}] Dead end @ {WorldToCell(transform.position)}");
            }
        }
    }

    // =====================================================
    // 滑らかにターゲットまで移動
    // =====================================================
    void MoveToTarget()
    {
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
        {
            transform.position = targetPos;
            isMoving = false;
        }
    }

    // =====================================================
    // Node設置（重複防止＋スナップ＋共有登録）
    // =====================================================
    void TryPlaceNode(Vector3 pos)
    {
        // 1. スナップしてセル座標を取得
        Vector2Int cell = WorldToCell(SnapToGrid(pos));
        Vector3 placePos = CellToWorld(cell);

        // 2. すでに設置済みならスキップ
        if (MapNode.allNodeCells.Contains(cell))
            return;

        // 3. 新規登録
        MapNode.allNodeCells.Add(cell);

        // 4. Nodeを生成
        Instantiate(nodePrefab, placePos, Quaternion.identity);

        if (debugLog)
            Debug.Log($"[Player:{name}] Node placed @ {cell}");
    }

    // =====================================================
    // グリッド補助関数群
    // =====================================================
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

//public class Player : MonoBehaviour
//{
//    [Header("移動設定")]
//    public float moveSpeed;
//    public float cellSize = 1f;
//    public float rayDistance = 1f;
//    public LayerMask wallLayer;

//    [Header("初期設定")]
//    public Vector3 startDirection = Vector3.forward;

//    [Header("Node")]
//    public GameObject nodePrefab;
//    public Vector3 gridOrigin = Vector3.zero; // グリッド原点（必要なら調整）

//    private Vector3 moveDir;
//    private bool isMoving = false;
//    private Vector3 targetPos;

//    void Start()
//    {
//        moveDir = startDirection.normalized;
//        targetPos = transform.position;
//    }

//    void Update()
//    {
//        if (!isMoving) TryMove();
//        else MoveToTarget();
//    }

//    void TryMove()
//    {
//        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
//        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

//        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
//        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
//        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

//        Debug.DrawRay(transform.position, moveDir * rayDistance, Color.red);
//        Debug.DrawRay(transform.position, leftDir * rayDistance, Color.blue);
//        Debug.DrawRay(transform.position, rightDir * rayDistance, Color.green);

//        int openCount = 0;
//        if (!frontHit) openCount++;
//        if (!leftHit) openCount++;
//        if (!rightHit) openCount++;

//        // 曲がり角 or 前が壁ならNode設置
//        if (frontHit || openCount >= 2)
//        {
//            TryPlaceNode(transform.position);
//        }

//        // 前が空いていればそのまま進行
//        if (!frontHit)
//        {
//            targetPos = transform.position + moveDir * cellSize;
//            isMoving = true;
//        }
//        else
//        {
//            // 前が壁なら左右方向へランダム転換
//            var open = new List<Vector3>(2);
//            if (!leftHit) open.Add(leftDir);
//            if (!rightHit) open.Add(rightDir);

//            if (open.Count > 0)
//            {
//                moveDir = open[Random.Range(0, open.Count)];
//                targetPos = transform.position + moveDir * cellSize;
//                isMoving = true;
//            }
//        }
//    }

//    void MoveToTarget()
//    {
//        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
//        if (Vector3.Distance(transform.position, targetPos) < 0.001f)
//        {
//            transform.position = targetPos;
//            isMoving = false;
//        }
//    }

//    // =====================================================
//    // Node設置（共有チェック＋スナップ＋即登録）
//    // =====================================================
//    void TryPlaceNode(Vector3 pos)
//    {
//        // 1 スナップ（浮動小数誤差防止）
//        Vector2Int cell = WorldToCell(pos);
//        Vector3 placePos = CellToWorld(cell);

//        // 2 すでに共有リストに存在するか確認
//        if (MapNode.allNodeCells.Contains(cell))
//            return;

//        // 3 即座に共有リストに登録（他プレイヤーも認識可能）
//        MapNode.allNodeCells.Add(cell);

//        // 4 Nodeを生成
//        Instantiate(nodePrefab, placePos, Quaternion.identity);
//    }

//    // =====================================================
//    // ワールド→セル変換
//    // =====================================================
//    Vector2Int WorldToCell(Vector3 worldPos)
//    {
//        Vector3 p = worldPos - gridOrigin;
//        int cx = Mathf.RoundToInt(p.x / cellSize);
//        int cz = Mathf.RoundToInt(p.z / cellSize);
//        return new Vector2Int(cx, cz);
//    }

//    // =====================================================
//    // セル→ワールド変換
//    // =====================================================
//    Vector3 CellToWorld(Vector2Int cell)
//    {
//        return new Vector3(cell.x * cellSize, 0f, cell.y * cellSize) + gridOrigin;
//    }
//}
