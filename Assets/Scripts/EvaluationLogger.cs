using System.IO;
using UnityEngine;

public static class EvaluationLogger
{
    private static int runCounter = 0;

    private static bool nodeVisitHeaderWritten = false;

    private static string nodeVisitFilePath = null;

    private static int nodeVisitFrameBase = 0;

    private static int currentRunId = 0;

    private static string lastUnknownSelectMode = "";
    private static string lastTargetUpdateMode = "";

    public static void Record(
        string scriptName,
        int nodesCreated,
        int shortestPathLen,
        float timeToGoal,
        int totalNodeVisits,
        int totalProcessMs,
        float avgProcessMs,
        float maxProcessMs
    //int heavyFrameCount,
    //float avgFrameTime,
    //float worstFrameTime
    )
    {
        runCounter++;

        string fileName = $"Evaluation_{scriptName}.csv";
        //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
        string baseDir = @"D:\GitHub\Node\CSV";
        string path = Path.Combine(baseDir, fileName);

        if (!File.Exists(path))
        {
            string header =
                "RunID," +
                "NodesCreated," +
                "ShortestPathLen," +
                "TimeToGoal," +
                "TotalNodeVisits," +
                "TotalProcessMs," +
                "AvgProcessMs," +
                "MaxProcessMs";
            //"HeavyFrameCount," +
            //"AvgFrame," +
            //"WorstFrame";

            File.AppendAllText(path, header + "\n");
        }

        string line =
            runCounter + "," +
            nodesCreated + "," +
            shortestPathLen + "," +
            timeToGoal.ToString("F3") + "," +
            totalNodeVisits + "," +
            totalProcessMs + "," +            
            avgProcessMs.ToString("F3") + "," +  
            maxProcessMs.ToString("F3");         
                                                 //heavyFrameCount + "," +
                                                 //avgFrameTime.ToString("F3") + "," +
                                                 //worstFrameTime.ToString("F3");

        File.AppendAllText(path, line + "\n");

        Debug.Log($"[EvaluationLogger] Log appended  {path}");
    }


    public static void LogNodeVisit(
    int playerId,
    int frame,
    MapNode node,
    string unknownSelectMode,
    string targetUpdateMode,
    string noUnknownFallbackMode,
    int unknownReferenceDepth,
    int stepIndex)
    {
        if (node == null)
        {
            Debug.LogWarning("[EvaluationLogger] LogNodeVisit called with null node.");
            return;
        }

        try
        {
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
            //string baseDir = @"D:\GitHub\Node\CSV\Random_EveryNode_FarthestFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\Random_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_OnArrival_NewestNode";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_EveryNode_NewestNode";
            //string baseDir = @"D:\GitHub\Node\CSV\Nearest_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\Nearest_OnArrival_NewestNode";
            //string baseDir = @"D:\GitHub\Node\CSV\Farthest_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\Farthest_OnArrival_NewestNode";
            //string baseDir = @"D:\GitHub\Node\CSV\MostUnknown_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\Test";
            //string baseDir = @"D:\GitHub\Node\CSV\0120";
            //string baseDir = @"D:\GitHub\Node\CSV\NoWallNoDisappearingwallNoEnemy\CellFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\NoWallNoDisappearingwallNoEnemy\AStar";
            //string baseDir = @"D:\GitHub\Node\CSV\NoWallYesDisappearingwallYesEnemy\CellFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\NoWallYesDisappearingwallYesEnemy\AStar";
            //string baseDir = @"D:\GitHub\Node\CSV\YesWallNoDisappearingwallYesEnemy\CellFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\YesWallNoDisappearingwallYesEnemy\AStar";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\0127\MapA\NodeBase\Normal";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\0127\MapA\NodeBase\BestTarget";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\0127\MapA\NodeBase\Unknown";
            string baseDir = @"D:\GitHub\NodeGitHub\CSV\0127\MapA\AStar";
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeUnknown = (unknownSelectMode ?? "Unknown").Replace(",", "_").Replace(" ", "");
                string safeTarget = (targetUpdateMode ?? "None").Replace(",", "_").Replace(" ", "");
                string fileName = $"{timestamp}.csv";
                nodeVisitFilePath = Path.Combine(baseDir, fileName);
            }

            if (!nodeVisitHeaderWritten || !File.Exists(nodeVisitFilePath))
            {
                string header =
                    "RunID," +
                    "RowType," +
                    "PlayerID," +
                    "UnknownSelectMode," +
                    "TargetUpdateMode," +
                    "UnknownReferenceDepth," +
                    "NoUnknownFallbackMode," +
                    "StepIndex," +
                    "Frame," +
                    "NodeName," +
                    "NodePosX," +
                    "NodePosY," +
                    "NodePosZ," +
                    "CellX," +
                    "CellZ," +
                    "GoalCellX," +
                    "GoalCellZ," +
                    "NodesCreated," +
                    "ShortestPathLen," +
                    "TimeToGoal," +
                    "TotalNodeVisits," +
                    "TotalProcessMs," +
                    "AvgProcessMs," +
                    "MaxProcessMs";

                File.AppendAllText(nodeVisitFilePath, header + System.Environment.NewLine);
                nodeVisitHeaderWritten = true;
            }

            Vector3 nodePos = node.transform.position;

            int cellX = node.cell.x;
            int cellZ = node.cell.y;

            var ci = System.Globalization.CultureInfo.InvariantCulture;

            int localFrame = frame - nodeVisitFrameBase;

            string rowType = "VISIT";

            //lastUnknownSelectMode = unknownSelectMode;
            //lastTargetUpdateMode = targetUpdateMode;
            lastUnknownSelectMode = unknownSelectMode ?? "";
            lastTargetUpdateMode = targetUpdateMode ?? "";

            // ★ 1行ぶん（VISIT）を組み立て（24列：{0}〜{23}）
            string line = string.Format(
                ci,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23}",
                currentRunId,          // 0: RunID
                rowType,               // 1: RowType = "VISIT"
                playerId,              // 2: PlayerID
                unknownSelectMode,     // 3: UnknownSelectMode
                targetUpdateMode,      // 4: TargetUpdateMode
                unknownReferenceDepth, // 5: UnknownReferenceDepth
                noUnknownFallbackMode, // 6: NoUnknownFallbackMode
                stepIndex,             // 7: StepIndex
                localFrame,            // 8: Frame（Run内）
                node.name,             // 9: NodeName
                nodePos.x,             //10: NodePosX
                nodePos.y,             //11: NodePosY
                nodePos.z,             //12: NodePosZ
                cellX,                 //13: CellX
                cellZ,                 //14: CellZ
                "",                    //15: GoalCellX（VISITでは空欄）
                "",                    //16: GoalCellZ（VISITでは空欄）
                "",                    //17: NodesCreated
                "",                    //18: ShortestPathLen
                "",                    //19: TimeToGoal
                "",                    //20: TotalNodeVisits
                "",                    //21: TotalProcessMs
                "",                    //22: AvgProcessMs
                ""                     //23: MaxProcessMs
            );

            File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EvaluationLogger] LogNodeVisit failed: {ex}");
        }
    }

    public static void LogSummaryRowForCurrentRun(
        string unknownSelectMode,
        string targetUpdateMode,
        string noUnknownFallbackMode,
        int nodesCreated,
        int shortestPathLen,
        float timeToGoal,
        int totalNodeVisits,
        int totalProcessMs,
        float avgProcessMs,
        float maxProcessMs)
    {
        try
        {
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
            //string baseDir = @"D:\GitHub\Node\CSV\Random_EveryNode_FarthestFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\Random_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_OnArrival_NewestNode";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_EveryNode_NewestNode";
            //string baseDir = @"D:\GitHub\Node\CSV\Nearest_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\Nearest_OnArrival_NewestNode";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Farthest_OnArrival_NewestNode";
            //string baseDir = @"D:\GitHub\Node\CSV\Farthest_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\Farthest_OnArrival_NewestNode";
            //string baseDir = @"D:\GitHub\Node\CSV\MostUnknown_OnArrival_FarthestFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\Test";
            //string baseDir = @"D:\GitHub\Node\CSV\NoWallNoDisappearingwallNoEnemy\CellFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\NoWallNoDisappearingwallNoEnemy\AStar";
            //string baseDir = @"D:\GitHub\Node\CSV\NoWallYesDisappearingwallYesEnemy\CellFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\NoWallYesDisappearingwallYesEnemy\AStar";
            //string baseDir = @"D:\GitHub\Node\CSV\YesWallNoDisappearingwallYesEnemy\CellFromStart";
            //string baseDir = @"D:\GitHub\Node\CSV\YesWallNoDisappearingwallYesEnemy\AStar";
            //string baseDir = @"D:\GitHub\Node\CSV\0126";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\0127\MapA\NodeBase\Normal";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\0127\MapA\NodeBase\BestTarget";
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\0127\MapA\NodeBase\Unknown";
            string baseDir = @"D:\GitHub\NodeGitHub\CSV\0127\MapA\AStar";
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeUnknown = (unknownSelectMode ?? "Unknown").Replace(",", "_").Replace(" ", "");
                string safeTarget = (targetUpdateMode ?? "None").Replace(",", "_").Replace(" ", "");
                string fileName = $"{timestamp}_{safeUnknown}_{safeTarget}.csv";
                nodeVisitFilePath = Path.Combine(baseDir, fileName);
            }

            int goalCellX = 0;
            int goalCellZ = 0;
            if (MapNode.GoalNode != null)
            {
                goalCellX = MapNode.GoalNode.cell.x;
                goalCellZ = MapNode.GoalNode.cell.y;
            }

            if (!nodeVisitHeaderWritten || !File.Exists(nodeVisitFilePath))
            {
                string header =
                    "RunID," +
                    "RowType," +
                    "PlayerID," +
                    "UnknownSelectMode," +
                    "TargetUpdateMode," +
                    "UnknownReferenceDepth," +
                    "NoUnknownFallbackMode," +
                    "StepIndex," +
                    "Frame," +
                    "NodeName," +
                    "NodePosX," +
                    "NodePosY," +
                    "NodePosZ," +
                    "CellX," +
                    "CellZ," +
                    "GoalCellX," +
                    "GoalCellZ," +
                    "NodesCreated," +
                    "ShortestPathLen," +
                    "TimeToGoal," +
                    "TotalNodeVisits," +
                    "TotalProcessMs," +
                    "AvgProcessMs," +
                    "MaxProcessMs";

                File.AppendAllText(nodeVisitFilePath, header + System.Environment.NewLine);
                nodeVisitHeaderWritten = true;
            }

            var ci = System.Globalization.CultureInfo.InvariantCulture;

            string summaryUnknown = !string.IsNullOrEmpty(lastUnknownSelectMode)
                ? lastUnknownSelectMode
                : (unknownSelectMode ?? "");

            string summaryTarget = !string.IsNullOrEmpty(lastTargetUpdateMode)
                ? lastTargetUpdateMode
                : (targetUpdateMode ?? "");

            string line = string.Format(
                ci,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23}",
                currentRunId,                  // 0: RunID
                "SUMMARY",                     // 1: RowType
                "",                            // 2: PlayerID（Run全体なので空欄）
                summaryUnknown,//unknownSelectMode,             // 3: UnknownSelectMode
                summaryTarget,//targetUpdateMode,              // 4: TargetUpdateMode
                "",                            // 5: UnknownReferenceDepth（SUMMARYでは空欄）
                noUnknownFallbackMode,         // 6: NoUnknownFallbackMode
                "",                            // 7: StepIndex
                "",                            // 8: Frame
                "",                            // 9: NodeName
                "",                            //10: NodePosX
                "",                            //11: NodePosY
                "",                            //12: NodePosZ
                "",                            //13: CellX
                "",                            //14: CellZ
                goalCellX,                     //15: GoalCellX
                goalCellZ,                     //16: GoalCellZ
                nodesCreated,                  //17: NodesCreated
                shortestPathLen,               //18: ShortestPathLen
                timeToGoal.ToString("F3", ci), //19: TimeToGoal
                totalNodeVisits,               //20: TotalNodeVisits
                totalProcessMs,                //21: TotalProcessMs
                avgProcessMs.ToString("F3", ci), //22: AvgProcessMs
                maxProcessMs.ToString("F3", ci)  //23: MaxProcessMs
            );


            File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);

            //try
            //{
            //    if (!string.IsNullOrEmpty(nodeVisitFilePath))
            //    {
            //        //string pngPath = System.IO.Path.ChangeExtension(nodeVisitFilePath, ".png");
            //        //ScreenCapture.CaptureScreenshot(pngPath);
            //        //Debug.Log($"[EvaluationLogger] Screenshot saved: {pngPath}");
            //    }
            //    else
            //    {
            //        Debug.LogWarning("[EvaluationLogger] Screenshot skipped: nodeVisitFilePath is null or empty.");
            //    }
            //}
            //catch (System.Exception ex)
            //{
            //    Debug.LogError($"[EvaluationLogger] Screenshot capture failed: {ex}");
            //}
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EvaluationLogger] LogSummaryRowForCurrentRun failed: {ex}");
        }
    }

    public static void CaptureRunScreenshot()
    {
        CaptureRunScreenshot(null);
    }

    /// <summary>
    /// Run中のスクショを保存する（tag を付けるとファイル名が上書きされない）
    /// 例: NodeVisit.csv → NodeVisit_S000.png
    /// </summary>
    public static void CaptureRunScreenshot(string tag)
    {
        try
        {
            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                Debug.LogWarning("[EvaluationLogger] Screenshot skipped: nodeVisitFilePath is null or empty.");
                return;
            }

            string basePath = Path.ChangeExtension(nodeVisitFilePath, null);

            string safeTag = "";
            if (!string.IsNullOrEmpty(tag))
            {
                string t = tag;
                foreach (char c in Path.GetInvalidFileNameChars())
                    t = t.Replace(c, '_');

                safeTag = "_" + t;
            }

            string pngPath = basePath + safeTag + ".png";
            ScreenCapture.CaptureScreenshot(pngPath);
            Debug.Log($"[EvaluationLogger] Screenshot saved: {pngPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EvaluationLogger] Screenshot capture failed: {ex}");
        }
    }

    public static void ResetNodeVisitLog()
    {
        nodeVisitFilePath = null;
        nodeVisitHeaderWritten = false;

        nodeVisitFrameBase = Time.frameCount;

        currentRunId++;

        lastUnknownSelectMode = "";
        lastTargetUpdateMode = "";
    }
}