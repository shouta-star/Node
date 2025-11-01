using System.Collections.Generic;
using UnityEngine;

//[ExecuteAlways]
public class MapNode : MonoBehaviour
{
    // =========================================
    // �S�v���C���[���L��Node���W���X�g
    // =========================================
    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();

    [Header("�����N���")]
    public List<MapNode> links = new List<MapNode>();
    public float value = 0f;

    [Header("�T���ݒ�")]
    public float cellSize = 1f;            // �O���b�h1�}�X�̒���
    public int maxSteps = 20;              // ���}�X��܂ŒT�����邩
    public LayerMask wallLayer;            // �ǃ��C���[
    public LayerMask nodeLayer;            // Node���C���[
    public Vector3 gridOrigin = Vector3.zero; // �O���b�h���_�i�I�t�Z�b�g�␳�p�j

    private void Start()
    {
        // �N�����Ɏ��g�̈ʒu��o�^�i�d���͎����X�L�b�v�j
        Vector2Int cell = WorldToCell(transform.position);
        allNodeCells.Add(cell);

        if (Application.isPlaying)
            FindNeighbors();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            FindNeighbors();
    }
#endif

    // =====================================================
    // �ÓI�֐��F�w��ʒu��Node�����݂��邩���`�F�b�N
    // =====================================================
    public static bool NodeExistsAt(Vector3 worldPos, float cellSize, Vector3 origin)
    {
        Vector3 p = worldPos - origin;
        Vector2Int cell = new Vector2Int(
            Mathf.RoundToInt(p.x / cellSize),
            Mathf.RoundToInt(p.z / cellSize)
        );
        return allNodeCells.Contains(cell);
    }

    // =====================================================
    // ���[���h���W �� �O���b�h���W�ϊ�
    // =====================================================
    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        int cx = Mathf.RoundToInt(p.x / cellSize);
        int cz = Mathf.RoundToInt(p.z / cellSize);
        return new Vector2Int(cx, cz);
    }

    // =====================================================
    // �O���b�h�x�[�X��Ray��i�K�I�ɐL�΂��A�� or Node �ɓ�����܂ŒT��
    // =====================================================
    public void FindNeighbors()
    {
        links.Clear();
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in directions)
        {
            bool hitSomething = false;

            // 1�}�X���L�΂��Ē��ׂ�
            for (int step = 1; step <= maxSteps; step++)
            {
                float distance = step * cellSize;
                Vector3 origin = transform.position;

                // Ray���΂�
                if (Physics.Raycast(origin, dir, out RaycastHit hit, distance))
                {
                    GameObject obj = hit.collider.gameObject;
                    int layerMask = 1 << obj.layer;

                    // �ǂȂ�T���I���i�����N���Ȃ��j
                    if ((wallLayer.value & layerMask) != 0)
                    {
                        hitSomething = true;
                        break;
                    }

                    // Node�Ȃ烊���N�m��
                    if ((nodeLayer.value & layerMask) != 0)
                    {
                        MapNode neighbor = obj.GetComponent<MapNode>();
                        if (neighbor != null && neighbor != this)
                        {
                            if (!links.Contains(neighbor))
                            {
                                links.Add(neighbor);
                                if (!neighbor.links.Contains(this))
                                    neighbor.links.Add(this);
                            }
                        }
                        hitSomething = true;
                        break;
                    }
                }
            }

            // ���ɂ�������Ȃ����������͖���
            if (!hitSomething)
                continue;
        }
    }

    // =====================================================
    // Gizmos�F�����N����Ԃŕ\��
    // =====================================================
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        foreach (var node in links)
        {
            if (node != null)
            {
                Gizmos.DrawLine(transform.position, node.transform.position);
            }
        }
    }
}
