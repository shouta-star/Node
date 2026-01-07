using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Selects which Player to chase.
/// "Closest" is defined as the shortest A* path length (grid distance) from Enemy -> Player.
/// </summary>
public class EnemyTargetSelector : MonoBehaviour
{
    [Header("Grid")]
    public float cellSize = 1f;

    [Header("Walls")]
    public LayerMask wallLayer;
    public float wallCheckMargin = 0.45f;
    public float rayHeightOffset = 0.5f;

    [Header("Targeting")]
    public string playerTag = "Player";
    public float retargetIntervalSec = 0.75f;

    [Header("Debug")]
    public bool debugLog = false;

    public Transform CurrentTarget { get; private set; } = null;
    public Vector2Int CurrentTargetCell { get; private set; } = new Vector2Int(int.MinValue, int.MinValue);

    private float nextRetargetTime = 0f;

    private Vector2Int CurrentCell => AStarGridUtility.WorldToCell(transform.position, cellSize);

    private float RayY => transform.position.y + rayHeightOffset;

    private void Update()
    {
        if (Time.time < nextRetargetTime) return;
        nextRetargetTime = Time.time + Mathf.Max(0.05f, retargetIntervalSec);

        SelectTargetByAStarDistance();
    }

    private void SelectTargetByAStarDistance()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);
        if (players == null || players.Length == 0)
        {
            CurrentTarget = null;
            CurrentTargetCell = new Vector2Int(int.MinValue, int.MinValue);
            return;
        }

        Vector2Int start = CurrentCell;

        Transform bestT = null;
        int bestLen = int.MaxValue;

        for (int i = 0; i < players.Length; i++)
        {
            Transform t = players[i].transform;
            Vector2Int goal = AStarGridUtility.WorldToCell(t.position, cellSize);

            List<Vector2Int> path = AStarGridUtility.AStar(
                start, goal,
                cellSize, RayY, wallCheckMargin, wallLayer);

            int len = AStarGridUtility.PathLength(path);
            if (len < bestLen)
            {
                bestLen = len;
                bestT = t;
            }
        }

        if (bestT == null)
        {
            CurrentTarget = null;
            CurrentTargetCell = new Vector2Int(int.MinValue, int.MinValue);
            return;
        }

        bool changed = (CurrentTarget != bestT);
        CurrentTarget = bestT;
        CurrentTargetCell = AStarGridUtility.WorldToCell(CurrentTarget.position, cellSize);

        if (debugLog && changed)
        {
            Debug.Log($"[EnemyTargetSelector] Target set to {CurrentTarget.name} (A*Len={bestLen})");
        }
    }
}
