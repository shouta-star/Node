using UnityEngine;
using System.Collections.Generic;

public class NodePointMarker : MonoBehaviour
{
    public enum PointType
    {
        Exclude,     // 経由しない（isExcluded=true）
        // 将来拡張：Strategic / Resource / Cover など
    }

    [Header("Point Settings")]
    public PointType pointType = PointType.Exclude;

    [Header("Grid (CellFromStart と同じ値に合わせる)")]
    public Vector3 gridOrigin = Vector3.zero;
    public float cellSize = 1f;

    // --- 登録済みセル（全ポイント共通）---
    private static readonly HashSet<Vector2Int> excludeCells = new HashSet<Vector2Int>();

    public static bool IsExcludedCell(Vector2Int cell) => excludeCells.Contains(cell);

    private void Awake()
    {
        var cell = WorldToCell(transform.position);
        if (pointType == PointType.Exclude)
        {
            excludeCells.Add(cell);
        }
    }

    private void OnDestroy()
    {
        // シーン内でポイントを消した時に反映したいなら外す
        var cell = WorldToCell(transform.position);
        if (pointType == PointType.Exclude)
        {
            excludeCells.Remove(cell);
        }
    }

    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        return new Vector2Int(
            Mathf.RoundToInt(p.x / cellSize),
            Mathf.RoundToInt(p.z / cellSize)
        );
    }
}
