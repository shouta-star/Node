using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class MapNode : MonoBehaviour
{
    [Header("�����N���")]
    public List<MapNode> links = new List<MapNode>(); // �א�Node
    public float value = 0f;                          // ���Ғl�i�w�K�p�j

    [Header("�T���ݒ�")]
    public float linkCheckDistance = 1.1f;            // �א�Node�T������
    public LayerMask nodeLayer;                       // NodePrefab�̃��C���[

    private void Start()
    {
        if (Application.isPlaying)
        {
            FindNeighbors();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Editor���Prefab�z�u����ɂ��אڍX�V�ł���悤�ɂ���
        if (!Application.isPlaying)
            FindNeighbors();
    }
#endif

    /// <summary>
    /// �㉺���E�ɒZ����Raycast���΂��ėא�Node��T��
    /// </summary>
    public void FindNeighbors()
    {
        links.Clear();
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in directions)
        {
            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, linkCheckDistance, nodeLayer))
            {
                MapNode neighbor = hit.collider.GetComponent<MapNode>();
                if (neighbor != null && !links.Contains(neighbor))
                {
                    links.Add(neighbor);

                    // �o���������N�i���肩���������o�^�j
                    if (!neighbor.links.Contains(this))
                    {
                        neighbor.links.Add(this);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gizmos�Ń����N���������i�Ԃ����C���j
    /// </summary>
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
