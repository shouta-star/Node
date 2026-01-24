using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(EnemyTargetSelector))]
public class EnemyMover : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 3.5f;
    public float arriveEps = 0.05f;

    [Header("Repathing")]
    public int repathEverySteps = 3;

    [Header("Debug")]
    public bool debugLog = false;

    private EnemyTargetSelector selector;

    private List<Vector2Int> pathCells;
    private int pathIndex = 0;
    private int stepsSinceRepath = 0;
    private Vector2Int lastTargetCell = new Vector2Int(int.MinValue, int.MinValue);

    // CellFromStart-style state
    private bool isMoving = false;
    private Vector3 targetPos;

    private Vector2Int CurrentCell => AStarGridUtility.WorldToCell(transform.position, selector.cellSize, selector.gridOrigin);
    private float RayY => transform.position.y + selector.rayHeightOffset;

    private void Awake()
    {
        selector = GetComponent<EnemyTargetSelector>();
    }

    private void Start()
    {
        // Snap to current cell center once (same idea as CellFromStart.SnapToGrid)
        Vector2Int c = CurrentCell;
        Vector3 center = AStarGridUtility.CellToWorld(c, selector.cellSize, selector.gridOrigin, transform.position.y);
        transform.position = center;
        targetPos = center;
        isMoving = false;
    }

    private void Update()
    {
        if (selector.CurrentTarget == null)
        {
            pathCells = null;
            pathIndex = 0;
            stepsSinceRepath = 0;
            isMoving = false;
            return;
        }

        Vector2Int targetCell = AStarGridUtility.WorldToCell(selector.CurrentTarget.position, selector.cellSize, selector.gridOrigin);

        // Rebuild if target moved cell OR path missing
        // ‡@ ƒpƒX‚ª–³‚¢‚Æ‚«‚¾‚¯‚Í‘¦ì‚éi‚±‚ê‚Í•K—vj
        if (pathCells == null || pathCells.Count == 0)
        {
            BuildPathToCell(targetCell);
            lastTargetCell = targetCell;
            stepsSinceRepath = 0;
        }

        // ‡A ˆÚ“®’†‚ÍAƒ^[ƒQƒbƒgƒZƒ‹‚ª•Ï‚í‚Á‚Ä‚à g¡‚Ì1ƒ}ƒXˆÚ“®h ‚ð—Dæ‚·‚é
        if (isMoving)
        {
            MoveToTarget(targetCell);
            return;
        }

        // ‡B ˆÚ“®‚µ‚Ä‚È‚¢ó‘Ô‚Åƒ^[ƒQƒbƒgƒZƒ‹‚ª•Ï‚í‚Á‚½‚çÄ’Tõ
        if (targetCell != lastTargetCell)
        {
            BuildPathToCell(targetCell);
            lastTargetCell = targetCell;
            stepsSinceRepath = 0;
        }

        SetupNextStepTarget();

        //if (targetCell != lastTargetCell || pathCells == null || pathCells.Count == 0)
        //{
        //    BuildPathToCell(targetCell);
        //    lastTargetCell = targetCell;
        //    stepsSinceRepath = 0;
        //    isMoving = false;
        //}

        //if (isMoving)
        //{
        //    MoveToTarget(targetCell);
        //    return;
        //}

        //SetupNextStepTarget();
    }

    private void BuildPathToCell(Vector2Int goalCell)
    {
        Vector2Int start = CurrentCell;

        pathCells = AStarGridUtility.AStar(
            start, goalCell,
            selector.cellSize, selector.gridOrigin,
            RayY, selector.wallCheckMargin, selector.wallLayer);

        if (pathCells == null || pathCells.Count == 0)
        {
            pathIndex = 0;
            if (debugLog) Debug.LogWarning($"[EnemyMover] Path not found. start={start} goal={goalCell}");
            return;
        }

        pathIndex = 0;
        if (pathCells.Count > 1 && pathCells[0] == start)
            pathIndex = 1;

        if (debugLog) Debug.Log($"[EnemyMover] Path rebuilt count={pathCells.Count} start={start} goal={goalCell} pathIndex={pathIndex}");
    }

    private void SetupNextStepTarget()
    {
        if (pathCells == null || pathCells.Count == 0) return;
        if (pathIndex >= pathCells.Count) return;

        // If we are already at the goal cell (path length 1), do nothing.
        // (This happens when Enemy and Player are in the same cell.)
        if (pathCells.Count == 1)
            return;

        Vector2Int nextCell = pathCells[pathIndex];

        // Start each step from the current cell center (prevents diagonal-looking correction)
        Vector2Int c = CurrentCell;
        Vector3 curCenter = AStarGridUtility.CellToWorld(c, selector.cellSize, selector.gridOrigin, transform.position.y);
        transform.position = curCenter;

        targetPos = AStarGridUtility.CellToWorld(nextCell, selector.cellSize, selector.gridOrigin, transform.position.y);
        isMoving = true;
    }

    private void MoveToTarget(Vector2Int targetCell)
    {
        if (Vector3.Distance(transform.position, targetPos) > arriveEps)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
            return;
        }

        // Arrived: snap exactly
        transform.position = targetPos;
        isMoving = false;

        pathIndex++;
        stepsSinceRepath++;

        if (repathEverySteps > 0 && stepsSinceRepath >= repathEverySteps)
        {
            stepsSinceRepath = 0;
            BuildPathToCell(targetCell);
            lastTargetCell = targetCell;
            isMoving = false;
        }
    }
}