using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class MapNode : MonoBehaviour
{
    [Header("リンク情報")]
    public List<MapNode> links = new List<MapNode>();
    public float value = 0f;

    [Header("探索設定")]
    public float cellSize = 1f;            // グリッド1マスの長さ
    public int maxSteps = 20;              // 何マス先まで探索するか
    public LayerMask wallLayer;            // 壁レイヤー
    public LayerMask nodeLayer;            // Nodeレイヤー

    private void Start()
    {
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

    /// <summary>
    /// グリッドベースでRayを段階的に伸ばし、壁 or Node に当たるまで探索
    /// </summary>
    public void FindNeighbors()
    {
        links.Clear();
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in directions)
        {
            bool hitSomething = false;

            // 1マスずつ伸ばして調べる
            for (int step = 1; step <= maxSteps; step++)
            {
                float distance = step * cellSize;
                Vector3 origin = transform.position;

                // Rayを飛ばす
                if (Physics.Raycast(origin, dir, out RaycastHit hit, distance))
                {
                    GameObject obj = hit.collider.gameObject;
                    int layerMask = 1 << obj.layer;

                    // 壁なら探索終了（リンクしない）
                    if ((wallLayer.value & layerMask) != 0)
                    {
                        hitSomething = true;
                        break;
                    }

                    // Nodeならリンク確定
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

            // 何にも当たらなかった方向は無視
            if (!hitSomething)
                continue;
        }
    }

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
