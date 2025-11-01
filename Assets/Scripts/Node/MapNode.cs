using System.Collections.Generic;
using UnityEngine;

//[ExecuteAlways]
public class MapNode : MonoBehaviour
{
    // =========================================
    // 全プレイヤー共有のNode座標リスト
    // =========================================
    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();

    [Header("リンク情報")]
    public List<MapNode> links = new List<MapNode>();
    public float value = 0f;

    [Header("探索設定")]
    public float cellSize = 1f;            // グリッド1マスの長さ
    public int maxSteps = 20;              // 何マス先まで探索するか
    public LayerMask wallLayer;            // 壁レイヤー
    public LayerMask nodeLayer;            // Nodeレイヤー
    public Vector3 gridOrigin = Vector3.zero; // グリッド原点（オフセット補正用）

    private void Start()
    {
        // 起動時に自身の位置を登録（重複は自動スキップ）
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
    // 静的関数：指定位置にNodeが存在するかをチェック
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
    // ワールド座標 → グリッド座標変換
    // =====================================================
    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        int cx = Mathf.RoundToInt(p.x / cellSize);
        int cz = Mathf.RoundToInt(p.z / cellSize);
        return new Vector2Int(cx, cz);
    }

    // =====================================================
    // グリッドベースでRayを段階的に伸ばし、壁 or Node に当たるまで探索
    // =====================================================
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

    // =====================================================
    // Gizmos：リンク線を赤で表示
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
