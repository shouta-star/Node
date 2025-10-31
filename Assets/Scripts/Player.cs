using UnityEngine;
using System.Collections.Generic;

public class Player : MonoBehaviour
{
    [Header("�ړ��ݒ�")]
    public float moveSpeed = 5f;          // �ړ����x
    public float cellSize = 1f;           // 1�}�X�̑傫��
    public float rayDistance = 1f;        // �ǌ��m����
    public LayerMask wallLayer;           // �ǂ̃��C���[

    [Header("�����ݒ�")]
    public Vector3 startDirection = Vector3.forward;
    public GameObject nodePrefab;         // Node��Prefab

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
            TryMove();  // ���̃}�X�֐i�ޏ���
        }
        else
        {
            MoveToTarget();  // �ړ����̏���
        }
    }

    void TryMove()
    {
        // Raycast����
        Vector3 leftDir = Quaternion.Euler(0, -90, 0) * moveDir;
        Vector3 rightDir = Quaternion.Euler(0, 90, 0) * moveDir;

        // Raycast����
        bool frontHit = Physics.Raycast(transform.position, moveDir, rayDistance, wallLayer);
        bool leftHit = Physics.Raycast(transform.position, leftDir, rayDistance, wallLayer);
        bool rightHit = Physics.Raycast(transform.position, rightDir, rayDistance, wallLayer);

        Debug.DrawRay(transform.position, moveDir * rayDistance, Color.red);
        Debug.DrawRay(transform.position, leftDir * rayDistance, Color.blue);
        Debug.DrawRay(transform.position, rightDir * rayDistance, Color.green);

        // Ray�ω������m������Node��ݒu
        if (leftHit != prevLeftHit || rightHit != prevRightHit)
        {
            Instantiate(nodePrefab, transform.position, Quaternion.identity);

            // �����_���ɕ����ύX�i�O�E���E�E����I���j
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

        // �O���󂢂Ă����玟�̃}�X�֐i��
        if (!frontHit)
        {
            targetPos = transform.position + moveDir * cellSize;
            isMoving = true;
        }
        else
        {
            // �O���ǂȂ�����ύX�����݂�
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
