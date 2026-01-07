using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy mover that behaves like CellFromStart:
/// - Moves one cell at a time (center -> center)
/// - While moving: MoveTowards(targetPos)
/// - On arrival: snap to targetPos, then advance to next cell
/// 
/// IMPORTANT: uses selector.gridOrigin / selector.cellSize (match Player's CellFromStart).
/// </summary>
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
        // ① パスが無いときだけは即作る（これは必要）
        if (pathCells == null || pathCells.Count == 0)
        {
            BuildPathToCell(targetCell);
            lastTargetCell = targetCell;
            stepsSinceRepath = 0;
        }

        // ② 移動中は、ターゲットセルが変わっても “今の1マス移動” を優先する
        if (isMoving)
        {
            MoveToTarget(targetCell);
            return;
        }

        // ③ 移動してない状態でターゲットセルが変わったら再探索
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


//using System.Collections.Generic;
//using UnityEngine;

///// <summary>
///// Enemy grid mover (CellFromStart-style):
///// - Move 1 cell at a time (center -> center)
///// - While moving: MoveTowards to targetPos
///// - On arrival: snap exactly to targetPos, then advance pathIndex
///// </summary>
//[RequireComponent(typeof(EnemyTargetSelector))]
//public class EnemyMover : MonoBehaviour
//{
//    [Header("Movement")]
//    public float moveSpeed = 3.5f;
//    public float arriveEps = 0.05f;

//    [Header("Repathing")]
//    [Tooltip("Rebuild path every N arrived cells while chasing (0 disables).")]
//    public int repathEverySteps = 3;

//    [Header("Debug")]
//    public bool debugLog = false;

//    private EnemyTargetSelector selector;

//    private List<Vector2Int> pathCells;
//    private int pathIndex = 0;
//    private int stepsSinceRepath = 0;
//    private Vector2Int lastTargetCell = new Vector2Int(int.MinValue, int.MinValue);

//    // ★ CellFromStart と同じ “1マス単位の移動状態”
//    private bool isMoving = false;
//    private Vector3 targetPos;

//    private Vector2Int CurrentCell => AStarGridUtility.WorldToCell(transform.position, selector.cellSize);
//    private float RayY => transform.position.y + selector.rayHeightOffset;

//    private void Awake()
//    {
//        selector = GetComponent<EnemyTargetSelector>();
//    }

//    private void Start()
//    {
//        // ★ CellFromStart の SnapToGrid 相当：開始時にセル中心へ寄せる
//        Vector2Int c = CurrentCell;
//        Vector3 center = AStarGridUtility.CellToWorld(c, selector.cellSize, transform.position.y);
//        transform.position = center;
//        targetPos = center;
//        isMoving = false;
//    }

//    private void Update()
//    {
//        if (selector.CurrentTarget == null)
//        {
//            pathCells = null;
//            pathIndex = 0;
//            stepsSinceRepath = 0;
//            isMoving = false;
//            return;
//        }

//        Vector2Int targetCell = AStarGridUtility.WorldToCell(selector.CurrentTarget.position, selector.cellSize);

//        // Rebuild if target moved to new cell
//        if (targetCell != lastTargetCell || pathCells == null || pathCells.Count == 0)
//        {
//            BuildPathToCell(targetCell);
//            lastTargetCell = targetCell;
//            stepsSinceRepath = 0;

//            // ★ 経路が更新されたら、次の1マス移動のセットからやり直す
//            isMoving = false;
//        }

//        // ★ CellFromStart と同じ：移動中は targetPos へ進むだけ
//        if (isMoving)
//        {
//            MoveToTarget(targetCell);
//            return;
//        }

//        // ★ 1マスぶんの targetPos をセット（＝CellFromStart の MoveForward と同じ役割）
//        SetupNextStepTarget(targetCell);
//    }

//    private void BuildPathToCell(Vector2Int goalCell)
//    {
//        Vector2Int start = CurrentCell;

//        pathCells = AStarGridUtility.AStar(
//            start, goalCell,
//            selector.cellSize, RayY, selector.wallCheckMargin, selector.wallLayer);

//        if (pathCells == null || pathCells.Count == 0)
//        {
//            pathIndex = 0;
//            if (debugLog) Debug.LogWarning("[EnemyMover] Path not found.");
//            return;
//        }

//        pathIndex = 0;
//        if (pathCells.Count > 1 && pathCells[0] == start)
//            pathIndex = 1;

//        if (debugLog) Debug.Log($"[EnemyMover] Path rebuilt: {pathCells.Count} cells");
//    }

//    private void SetupNextStepTarget(Vector2Int targetCell)
//    {
//        if (pathCells == null || pathCells.Count == 0) return;
//        if (pathIndex >= pathCells.Count) return;

//        Vector2Int nextCell = pathCells[pathIndex];

//        // ★ “次セル中心” をターゲットにする（中心->中心）
//        Vector3 nextWorld = AStarGridUtility.CellToWorld(nextCell, selector.cellSize, transform.position.y);

//        // ★ 念のため：1手開始時点でもセル中心にスナップ（ブレ残り対策）
//        Vector2Int c = CurrentCell;
//        Vector3 curCenter = AStarGridUtility.CellToWorld(c, selector.cellSize, transform.position.y);
//        transform.position = curCenter;

//        targetPos = nextWorld;
//        isMoving = true;
//    }

//    // ★ CellFromStart の MoveToTarget をそのまま持ってきた形
//    private void MoveToTarget(Vector2Int targetCell)
//    {
//        if (Vector3.Distance(transform.position, targetPos) > arriveEps)
//        {
//            transform.position = Vector3.MoveTowards(
//                transform.position,
//                targetPos,
//                moveSpeed * Time.deltaTime
//            );
//            return;
//        }

//        // ★ セル中心へ完全到達した瞬間（1回だけ実行）
//        transform.position = targetPos;
//        isMoving = false;

//        // 1セルぶん進んだので index 更新
//        pathIndex++;
//        stepsSinceRepath++;

//        // 一定セルごとに再探索（Player側の「3グリッド進んだら再探索」と同じ）
//        if (repathEverySteps > 0 && stepsSinceRepath >= repathEverySteps)
//        {
//            stepsSinceRepath = 0;
//            BuildPathToCell(targetCell);
//            lastTargetCell = targetCell;
//            isMoving = false;
//        }
//    }
//}


////using System.Collections.Generic;
////using UnityEngine;

/////// <summary>
/////// Moves the enemy toward the current target selected by EnemyTargetSelector, using A* path on the grid.
/////// </summary>
////[RequireComponent(typeof(EnemyTargetSelector))]
////public class EnemyMover : MonoBehaviour
////{
////    [Header("Movement")]
////    public float moveSpeed = 3.5f;
////    public float arriveEps = 0.05f;

////    [Header("Repathing")]
////    [Tooltip("Rebuild path every N arrived cells while chasing (0 disables).")]
////    public int repathEverySteps = 3;

////    [Header("Debug")]
////    public bool debugLog = false;

////    private EnemyTargetSelector selector;

////    private List<Vector2Int> pathCells;
////    private int pathIndex = 0;
////    private int stepsSinceRepath = 0;
////    private Vector2Int lastTargetCell = new Vector2Int(int.MinValue, int.MinValue);

////    private Vector2Int CurrentCell => AStarGridUtility.WorldToCell(transform.position, selector.cellSize);

////    private float RayY => transform.position.y + selector.rayHeightOffset;

////    private void Awake()
////    {
////        selector = GetComponent<EnemyTargetSelector>();
////    }

////    private void Start()
////    {
////        // ★ 初期位置をセル中心にスナップ（セル中心のみ移動の前提）
////        Vector2Int c = CurrentCell;
////        Vector3 center = AStarGridUtility.CellToWorld(c, selector.cellSize, transform.position.y);
////        transform.position = center;
////    }

////    private void Update()
////    {
////        if (selector.CurrentTarget == null)
////        {
////            pathCells = null;
////            pathIndex = 0;
////            stepsSinceRepath = 0;
////            return;
////        }

////        Vector2Int targetCell = AStarGridUtility.WorldToCell(selector.CurrentTarget.position, selector.cellSize);

////        // Rebuild if target moved to new cell
////        if (targetCell != lastTargetCell || pathCells == null || pathCells.Count == 0)
////        {
////            BuildPathToCell(targetCell);
////            lastTargetCell = targetCell;
////            stepsSinceRepath = 0;
////        }

////        FollowPath(targetCell);
////    }

////    private void BuildPathToCell(Vector2Int goalCell)
////    {
////        Vector2Int start = CurrentCell;

////        pathCells = AStarGridUtility.AStar(
////            start, goalCell,
////            selector.cellSize, RayY, selector.wallCheckMargin, selector.wallLayer);

////        if (pathCells == null || pathCells.Count == 0)
////        {
////            pathIndex = 0;
////            if (debugLog) Debug.LogWarning("[EnemyMover] Path not found.");
////            return;
////        }

////        pathIndex = 0;
////        if (pathCells.Count > 1 && pathCells[0] == start)
////            pathIndex = 1;

////        if (debugLog) Debug.Log($"[EnemyMover] Path rebuilt: {pathCells.Count} cells");
////    }

////    private void FollowPath(Vector2Int targetCell)
////    {
////        if (pathCells == null || pathCells.Count == 0) return;
////        if (pathIndex >= pathCells.Count) return;

////        Vector2Int nextCell = pathCells[pathIndex];

////        // ★ 現在セル（fromCell）は path から確定させる（丸め誤差対策）
////        Vector2Int fromCell = (pathIndex > 0) ? pathCells[pathIndex - 1] : CurrentCell;

////        Vector3 fromCenter = AStarGridUtility.CellToWorld(fromCell, selector.cellSize, transform.position.y);
////        Vector3 nextWorld = AStarGridUtility.CellToWorld(nextCell, selector.cellSize, transform.position.y);

////        // ★ 軸固定：左右移動ならZ固定／上下移動ならX固定
////        Vector2Int delta = nextCell - fromCell; // 4近傍なら (±1,0) or (0,±1)
////        if (delta.x != 0) nextWorld.z = fromCenter.z;
////        if (delta.y != 0) nextWorld.x = fromCenter.x;

////        transform.position = Vector3.MoveTowards(transform.position, nextWorld, moveSpeed * Time.deltaTime);

////        if (Vector3.Distance(transform.position, nextWorld) <= arriveEps)
////        {
////            // ★ 到達時に必ず中心へスナップ
////            transform.position = nextWorld;

////            pathIndex++;
////            stepsSinceRepath++;

////            if (repathEverySteps > 0 && stepsSinceRepath >= repathEverySteps)
////            {
////                stepsSinceRepath = 0;
////                BuildPathToCell(targetCell);
////                lastTargetCell = targetCell;
////            }
////        }
////    }
////    //private void FollowPath(Vector2Int targetCell)
////    //{
////    //    if (pathCells == null || pathCells.Count == 0) return;
////    //    if (pathIndex >= pathCells.Count) return;

////    //    Vector2Int nextCell = pathCells[pathIndex];
////    //    Vector3 nextWorld = AStarGridUtility.CellToWorld(nextCell, selector.cellSize, transform.position.y);

////    //    transform.position = Vector3.MoveTowards(transform.position, nextWorld, moveSpeed * Time.deltaTime);

////    //    if (Vector3.Distance(transform.position, nextWorld) <= arriveEps)
////    //    {
////    //        transform.position = nextWorld;
////    //        pathIndex++;
////    //        stepsSinceRepath++;

////    //        if (repathEverySteps > 0 && stepsSinceRepath >= repathEverySteps)
////    //        {
////    //            stepsSinceRepath = 0;
////    //            BuildPathToCell(targetCell);
////    //            lastTargetCell = targetCell;
////    //        }
////    //    }
////    //}
////}
