using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Moves the enemy toward the current target selected by EnemyTargetSelector, using A* path on the grid.
/// </summary>
[RequireComponent(typeof(EnemyTargetSelector))]
public class EnemyMover : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 3.5f;
    public float arriveEps = 0.05f;

    [Header("Repathing")]
    [Tooltip("Rebuild path every N arrived cells while chasing (0 disables).")]
    public int repathEverySteps = 3;

    [Header("Debug")]
    public bool debugLog = false;

    private EnemyTargetSelector selector;

    private List<Vector2Int> pathCells;
    private int pathIndex = 0;
    private int stepsSinceRepath = 0;
    private Vector2Int lastTargetCell = new Vector2Int(int.MinValue, int.MinValue);

    private Vector2Int CurrentCell => AStarGridUtility.WorldToCell(transform.position, selector.cellSize);

    private float RayY => transform.position.y + selector.rayHeightOffset;

    private void Awake()
    {
        selector = GetComponent<EnemyTargetSelector>();
    }

    private void Update()
    {
        if (selector.CurrentTarget == null)
        {
            pathCells = null;
            pathIndex = 0;
            stepsSinceRepath = 0;
            return;
        }

        Vector2Int targetCell = AStarGridUtility.WorldToCell(selector.CurrentTarget.position, selector.cellSize);

        // Rebuild if target moved to new cell
        if (targetCell != lastTargetCell || pathCells == null || pathCells.Count == 0)
        {
            BuildPathToCell(targetCell);
            lastTargetCell = targetCell;
            stepsSinceRepath = 0;
        }

        FollowPath(targetCell);
    }

    private void BuildPathToCell(Vector2Int goalCell)
    {
        Vector2Int start = CurrentCell;

        pathCells = AStarGridUtility.AStar(
            start, goalCell,
            selector.cellSize, RayY, selector.wallCheckMargin, selector.wallLayer);

        if (pathCells == null || pathCells.Count == 0)
        {
            pathIndex = 0;
            if (debugLog) Debug.LogWarning("[EnemyMover] Path not found.");
            return;
        }

        pathIndex = 0;
        if (pathCells.Count > 1 && pathCells[0] == start)
            pathIndex = 1;

        if (debugLog) Debug.Log($"[EnemyMover] Path rebuilt: {pathCells.Count} cells");
    }

    private void FollowPath(Vector2Int targetCell)
    {
        if (pathCells == null || pathCells.Count == 0) return;
        if (pathIndex >= pathCells.Count) return;

        Vector2Int nextCell = pathCells[pathIndex];
        Vector3 nextWorld = AStarGridUtility.CellToWorld(nextCell, selector.cellSize, transform.position.y);

        transform.position = Vector3.MoveTowards(transform.position, nextWorld, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, nextWorld) <= arriveEps)
        {
            transform.position = nextWorld;
            pathIndex++;
            stepsSinceRepath++;

            if (repathEverySteps > 0 && stepsSinceRepath >= repathEverySteps)
            {
                stepsSinceRepath = 0;
                BuildPathToCell(targetCell);
                lastTargetCell = targetCell;
            }
        }
    }
}
