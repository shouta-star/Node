using UnityEngine;
using System.Collections.Generic;

public class Player : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 5f;          // 移動速度
    public float cellSize = 1f;           // 1マスの大きさ
    public float rayDistance = 1f;        // 壁検知距離
    public LayerMask wallLayer;           // 壁のレイヤー

    [Header("初期設定")]
    public Vector3 startDirection = Vector3.forward;
    public GameObject nodePrefab;         // NodeのPrefab

    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;
    private bool prevLeftHit;
    private bool prevRightHit;

    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position;
    }

    void Update()
    {
        if (!isMoving)
        {
            TryMove();  // 次のマスへ進む処理
        }
        else
        {
            MoveToTarget();  // 移動中の処理
        }
    }

    void TryMove()
    {
        // Raycast方向
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        // Raycast判定
        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

        Debug.DrawRay(transform.position, moveDir * rayDistance, Color.red);
        Debug.DrawRay(transform.position, leftDir * rayDistance, Color.blue);
        Debug.DrawRay(transform.position, rightDir * rayDistance, Color.green);

        // Ray変化を検知したらNodeを設置
        if (leftHit != prevLeftHit || rightHit != prevRightHit)
        {
            Instantiate(nodePrefab, transform.position, Quaternion.identity);

            // ランダムに方向変更（前・左・右から選択）
            List<Vector3> possibleDirs = new List<Vector3>();
            if (!frontHit) possibleDirs.Add(moveDir);
            if (!leftHit) possibleDirs.Add(leftDir);
            if (!rightHit) possibleDirs.Add(rightDir);

            if (possibleDirs.Count > 0)
            {
                moveDir = possibleDirs[Random.Range(0, possibleDirs.Count)];
            }
        }

        prevLeftHit = leftHit;
        prevRightHit = rightHit;

        // 前が空いていたら次のマスへ進む
        if (!frontHit)
        {
            targetPos = transform.position + moveDir * cellSize;
            isMoving = true;
        }
        else
        {
            // 前が壁なら方向変更を試みる
            List<Vector3> openDirs = new List<Vector3>();
            if (!leftHit) openDirs.Add(leftDir);
            if (!rightHit) openDirs.Add(rightDir);

            if (openDirs.Count > 0)
            {
                moveDir = openDirs[Random.Range(0, openDirs.Count)];
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
}
