using UnityEngine;
using System.Collections.Generic;

public class PlayerMoveGrid : MonoBehaviour
{
    [Header("�ړ��ݒ�")]
    public float moveSpeed = 5f;
    public float cellSize = 1f;
    public float rayDistance = 1f;
    public LayerMask wallLayer;

    [Header("�����ݒ�")]
    public Vector3 startDirection = Vector3.forward;

    [Header("Node")]
    public GameObject nodePrefab;
    public Vector3 gridOrigin = Vector3.zero; // �O���b�h���_�i�K�v�Ȃ璲���j

    private Vector3 moveDir;
    private bool isMoving = false;
    private Vector3 targetPos;

    private bool prevLeftHit;
    private bool prevRightHit;

    // �� �ǉ��F�ݒu�ς݃Z���W��
    private readonly HashSet<Vector2Int> placedNodeCells = new();

    void Start()
    {
        moveDir = startDirection.normalized;
        targetPos = transform.position;

        // �J�n�Z���ɉ����u�������Ȃ�ꍇ�ɔ����ď����o�^�������Ă����ꍇ�̓R�����g����
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

        // �� Ray�̕ω���Node�ݒu�i�d���h�~�t���j
        if (leftHit != prevLeftHit || rightHit != prevRightHit)
        {
            TryPlaceNode(transform.position);

            // �����_�������ύX�i�O�E���E�E�̂����ʂ������j
            var options = new List<Vector3>(3);
            if (!frontHit) options.Add(moveDir);
            if (!leftHit) options.Add(leftDir);
            if (!rightHit) options.Add(rightDir);
            if (options.Count > 0) moveDir = options[Random.Range(0, options.Count)];
        }

        prevLeftHit = leftHit;
        prevRightHit = rightHit;

        // �O���󂢂Ă���ΑO�ցA�_���Ȃ獶�E�󂫂��烉���_��
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

    // �� �ǉ��F�O���b�h�d���`�F�b�N�t��Node�ݒu
    void TryPlaceNode(Vector3 worldPos)
    {
        Vector2Int cell = WorldToCell(worldPos);
        if (placedNodeCells.Add(cell)) // ���o�^�Z���Ȃ�true
        {
            // �Z�������ɒu�������ꍇ�� CellToWorld(cell) ���g��
            Vector3 placePos = CellToWorld(cell);
            Instantiate(nodePrefab, placePos, Quaternion.identity);
        }
        // �o�^�ς݂Ȃ�X�L�b�v
    }

    // �� �ǉ��F���W�ϊ��i���_�E�Z���T�C�Y�Ή��j
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
