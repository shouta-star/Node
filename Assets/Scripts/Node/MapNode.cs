using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class MapNode : MonoBehaviour
{
    [Header("リンク情報")]
    public List<MapNode> links = new List<MapNode>(); // 隣接Node
    public float value = 0f;                          // 期待値（学習用）

    [Header("探索設定")]
    public float linkCheckDistance = 1.1f;            // 隣接Node探索距離
    public LayerMask nodeLayer;                       // NodePrefabのレイヤー

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
        // Editor上でPrefab配置直後にも隣接更新できるようにする
        if (!Application.isPlaying)
            FindNeighbors();
    }
#endif

    /// <summary>
    /// 上下左右に短距離Raycastを飛ばして隣接Nodeを探す
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

                    // 双方向リンク（相手からも自分を登録）
                    if (!neighbor.links.Contains(this))
                    {
                        neighbor.links.Add(this);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gizmosでリンク線を可視化（赤いライン）
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
