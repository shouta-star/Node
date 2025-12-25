using System.IO;
using UnityEngine;

/// <summary>
/// s] CSV ɒǋL鋤ʃK[
/// EXNvgƂ CSV  1 쐬
/// ERunID ̓AvN̘Aԁistaticj
/// E1 s 1 񕪂̕]ʂǉ
/// Et@C Application.persistentDataPath ɕۑ
/// </summary>
public static class EvaluationLogger
{
    // NĂ̘AԁiSXNvgʁj
    private static int runCounter = 0;

    //  Player ʉ߂ Node L^ CSV piwb_ݍς݃tOj
    private static bool nodeVisitHeaderWritten = false;

    // NodeBOp̃t@CpXi1Run1j
    private static string nodeVisitFilePath = null;

    private static int nodeVisitFrameBase = 0;

    // RunIDi1,2,3,...j
    private static int currentRunId = 0;

    // ŌɋL^ꂽ[hiSUMMARYsɎgꍇj
    private static string lastUnknownSelectMode = "";
    private static string lastTargetUpdateMode = "";

    /// <summary>
    /// ]ʂ CSV ɒǋL
    /// </summary>
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
        // RunID Z
        runCounter++;

        //  ۑpX
        string fileName = $"Evaluation_{scriptName}.csv";
        //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
        string baseDir = @"D:\GitHub\Node\CSV";
        string path = Path.Combine(baseDir, fileName);

        //  CSV ΃wb_s
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

        //  CSV 1 s̃f[^쐬
        string line =
            runCounter + "," +
            nodesCreated + "," +
            shortestPathLen + "," +
            timeToGoal.ToString("F3") + "," +
            totalNodeVisits + "," +
            totalProcessMs + "," +            //  C
            avgProcessMs.ToString("F3") + "," +  //  C
            maxProcessMs.ToString("F3");         //  C
                                                 //heavyFrameCount + "," +
                                                 //avgFrameTime.ToString("F3") + "," +
                                                 //worstFrameTime.ToString("F3");

        //  ǋL
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
            //  o͐tH_ĩpX͂̂܂܎gj
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
            string baseDir = @"D:\GitHub\Node\CSV\Test";
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            //  t@CpX܂܂ĂȂ΁AŌ߂
            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeUnknown = (unknownSelectMode ?? "Unknown").Replace(",", "_").Replace(" ", "");
                string safeTarget = (targetUpdateMode ?? "None").Replace(",", "_").Replace(" ", "");
                string fileName = $"{timestamp}.csv";   // ̎dl̂܂܂OK
                nodeVisitFilePath = Path.Combine(baseDir, fileName);
            }

            //  t@C܂ or wb_݂Ȃwb_s
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
                    "GoalCellX," +     //  GoalCell ͂Œ`Ă
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

            //  Node ̍W擾
            Vector3 nodePos = node.transform.position;

            // ObhWiMapNode ŎĂ cellj
            int cellX = node.cell.x;
            int cellZ = node.cell.y;

            // _̃tH[}bgiJ}ƏՓ˂Ȃ悤Ɂj
            var ci = System.Globalization.CultureInfo.InvariantCulture;

            // Runt[
            int localFrame = frame - nodeVisitFrameBase;

            // RowType  VISIT Œ
            string rowType = "VISIT";

            // [h͌SUMMARYsɂĝŕۑ
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

            //  ǋL
            File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EvaluationLogger] LogNodeVisit failed: {ex}");
        }
    }


    /// <summary>
    ///  Run ̃T}uRowType=SUMMARYvƂ
    /// NodeVisit p CSV  1 sǋL
    /// </summary>
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
            //  o͐tH_iCSVƑj
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
            string baseDir = @"D:\GitHub\Node\CSV\Test";
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            //  t@CpX܂܂ĂȂ΁AŌ߂
            // iRun VISIT s 1 oĂȂP[XJo[j
            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeUnknown = (unknownSelectMode ?? "Unknown").Replace(",", "_").Replace(" ", "");
                string safeTarget = (targetUpdateMode ?? "None").Replace(",", "_").Replace(" ", "");
                string fileName = $"{timestamp}_{safeUnknown}_{safeTarget}.csv";
                nodeVisitFilePath = Path.Combine(baseDir, fileName);
            }

            //  GoalNode ̃ObhW擾
            int goalCellX = 0;
            int goalCellZ = 0;
            if (MapNode.GoalNode != null)
            {
                goalCellX = MapNode.GoalNode.cell.x;
                goalCellZ = MapNode.GoalNode.cell.y;
            }

            //  wb_܂ĂȂȂAŏ
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

            //  ̃XNVۑ͍̂܂܂OK
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
        try
        {
            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                Debug.LogWarning("[EvaluationLogger] Screenshot skipped: nodeVisitFilePath is null or empty.");
                return;
            }

            string pngPath = Path.ChangeExtension(nodeVisitFilePath, ".png");
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

        //  ̃^C~O Run ̊JnƂ݂Ȃ
        nodeVisitFrameBase = Time.frameCount;

        //  RunID CNg
        currentRunId++;

        lastUnknownSelectMode = "";
        lastTargetUpdateMode = "";
    }
}


//using System.IO;
//using UnityEngine;

///// <summary>
///// 実行評価を CSV に追記する共通ロガー
///// ・スクリプトごとに CSV を 1 個作成
///// ・RunID はアプリ起動からの連番（static）
///// ・1 行に 1 回分の評価結果を追加する
///// ・ファイルは Application.persistentDataPath に保存
///// </summary>
//public static class EvaluationLogger
//{
//    // 起動してからの連番（全スクリプト共通）
//    private static int runCounter = 0;

//    // ★ Player が通過した Node を記録する CSV 用（ヘッダ書き込み済みフラグ）
//    private static bool nodeVisitHeaderWritten = false;

//    // Node到達ログ用のファイルパス（1Runで1つ）
//    private static string nodeVisitFilePath = null;

//    private static int nodeVisitFrameBase = 0;

//    // このRunのID（1,2,3,...）
//    private static int currentRunId = 0;

//    // 最後に記録されたモード（SUMMARY行に使いたい場合）
//    private static string lastUnknownSelectMode = "";
//    private static string lastTargetUpdateMode = "";

//    /// <summary>
//    /// 評価結果を CSV に追記する
//    /// </summary>
//    public static void Record(
//        string scriptName,
//        int nodesCreated,
//        int shortestPathLen,
//        float timeToGoal,
//        int totalNodeVisits,
//        int totalProcessMs,
//        float avgProcessMs,
//        float maxProcessMs
//    //int heavyFrameCount,
//    //float avgFrameTime,
//    //float worstFrameTime
//    )
//    {
//        // RunID を加算
//        runCounter++;

//        // ▼ 保存先パス
//        string fileName = $"Evaluation_{scriptName}.csv";
//        //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
//        string baseDir = @"D:\GitHub\Node\CSV";
//        string path = Path.Combine(baseDir, fileName);

//        // ▼ CSV が無ければヘッダ行を書く
//        if (!File.Exists(path))
//        {
//            string header =
//                "RunID," +
//                "NodesCreated," +
//                "ShortestPathLen," +
//                "TimeToGoal," +
//                "TotalNodeVisits," +
//                "TotalProcessMs," +
//                "AvgProcessMs," +
//                "MaxProcessMs";
//                //"HeavyFrameCount," +
//                //"AvgFrame," +
//                //"WorstFrame";

//            File.AppendAllText(path, header + "\n");
//        }

//        // ▼ CSV 1 行分のデータを作成
//        string line =
//            runCounter + "," +
//            nodesCreated + "," +
//            shortestPathLen + "," +
//            timeToGoal.ToString("F3") + "," +
//            totalNodeVisits + "," +
//            totalProcessMs + "," +            // ★ 修正済
//            avgProcessMs.ToString("F3") + "," +  // ★ 修正済
//            maxProcessMs.ToString("F3");         // ★ 修正済
//                                             //heavyFrameCount + "," +
//                                             //avgFrameTime.ToString("F3") + "," +
//                                             //worstFrameTime.ToString("F3");

//        // ▼ 追記
//        File.AppendAllText(path, line + "\n");

//        Debug.Log($"[EvaluationLogger] Log appended → {path}");
//    }


//    public static void LogNodeVisit(
//    int playerId,
//    int frame,
//    MapNode node,
//    string unknownSelectMode,
//    string targetUpdateMode,
//    string noUnknownFallbackMode,
//    int unknownReferenceDepth,
//    int stepIndex)
//    {
//        if (node == null)
//        {
//            Debug.LogWarning("[EvaluationLogger] LogNodeVisit called with null node.");
//            return;
//        }

//        try
//        {
//            // ★ 出力先フォルダ（今のパスはそのまま使う）
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
//            //string baseDir = @"D:\GitHub\Node\CSV\Random_EveryNode_FarthestFromStart";
//            //string baseDir = @"D:\GitHub\Node\CSV\Random_OnArrival_FarthestFromStart";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_OnArrival_NewestNode";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_OnArrival_FarthestFromStart";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_EveryNode_NewestNode";
//            //string baseDir = @"D:\GitHub\Node\CSV\Nearest_OnArrival_FarthestFromStart";
//            string baseDir = @"D:\GitHub\Node\CSV\Nearest_OnArrival_NewestNode";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Test";
//            if (!Directory.Exists(baseDir))
//            {
//                Directory.CreateDirectory(baseDir);
//            }

//            // ★ ファイルパスがまだ決まっていなければ、ここで決める
//            if (string.IsNullOrEmpty(nodeVisitFilePath))
//            {
//                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
//                string safeUnknown = (unknownSelectMode ?? "Unknown").Replace(",", "_").Replace(" ", "");
//                string safeTarget = (targetUpdateMode ?? "None").Replace(",", "_").Replace(" ", "");
//                string fileName = $"{timestamp}.csv";   // ここも今の仕様のままでOK
//                nodeVisitFilePath = Path.Combine(baseDir, fileName);
//            }

//            // ★ ファイルがまだ無い or ヘッダ未書き込みならヘッダ行を書く
//            if (!nodeVisitHeaderWritten || !File.Exists(nodeVisitFilePath))
//            {
//                string header =
//                    "RunID," +
//                    "RowType," +
//                    "PlayerID," +
//                    "UnknownSelectMode," +
//                    "TargetUpdateMode," +
//                    "UnknownReferenceDepth," +
//                    "NoUnknownFallbackMode," +
//                    "StepIndex," +
//                    "Frame," +
//                    "NodeName," +
//                    "NodePosX," +
//                    "NodePosY," +
//                    "NodePosZ," +
//                    "CellX," +
//                    "CellZ," +
//                    "GoalCellX," +     // ★ GoalCell 列はここで定義しておく
//                    "GoalCellZ," +
//                    "NodesCreated," +
//                    "ShortestPathLen," +
//                    "TimeToGoal," +
//                    "TotalNodeVisits," +
//                    "TotalProcessMs," +
//                    "AvgProcessMs," +
//                    "MaxProcessMs";

//                File.AppendAllText(nodeVisitFilePath, header + System.Environment.NewLine);
//                nodeVisitHeaderWritten = true;
//            }

//            // ★ Node の座標を取得
//            Vector3 nodePos = node.transform.position;

//            // グリッド座標（MapNode 側で持っている cell）
//            int cellX = node.cell.x;
//            int cellZ = node.cell.y;

//            // 小数点のフォーマット（カンマと衝突しないように）
//            var ci = System.Globalization.CultureInfo.InvariantCulture;

//            // Run内フレーム
//            int localFrame = frame - nodeVisitFrameBase;

//            // RowType は VISIT 固定
//            string rowType = "VISIT";

//            // モード名は後でSUMMARY行にも使いたいので保存
//            lastUnknownSelectMode = unknownSelectMode;
//            lastTargetUpdateMode = targetUpdateMode;

//            // ★ 1行ぶんを組み立て（23列）
//            string line = string.Format(
//                ci,
//                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23}",
//                currentRunId,          // 0: RunID
//                rowType,               // 1: RowType = VISIT
//                playerId,              // 2: PlayerID
//                unknownSelectMode,     // 3: UnknownSelectMode
//                targetUpdateMode,      // 4: TargetUpdateMode
//                unknownReferenceDepth, // 5: UnknownReferenceDepth
//                noUnknownFallbackMode,
//                stepIndex,             // 6: StepIndex
//                localFrame,            // 7: Frame
//                node.name,             // 8: NodeName
//                nodePos.x,             // 9: NodePosX
//                nodePos.y,             //10: NodePosY
//                nodePos.z,             //11: NodePosZ
//                cellX,                 //12: CellX
//                cellZ,                 //13: CellZ
//                "",                    //14: GoalCellX（VISITでは空欄）
//                "",                    //15: GoalCellZ（VISITでは空欄）
//                "",                    //16: NodesCreated
//                "",                    //17: ShortestPathLen
//                "",                    //18: TimeToGoal
//                "",                    //19: TotalNodeVisits
//                "",                    //20: TotalProcessMs
//                "",                    //21: AvgProcessMs
//                ""                     //22: MaxProcessMs
//            );

//            // ★ 追記
//            File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);
//        }
//        catch (System.Exception ex)
//        {
//            Debug.LogError($"[EvaluationLogger] LogNodeVisit failed: {ex}");
//        }
//    }


//    /// <summary>
//    /// この Run のサマリ情報を「RowType=SUMMARY」として
//    /// NodeVisit 用 CSV に 1 行だけ追記する
//    /// </summary>
//    public static void LogSummaryRowForCurrentRun(
//        string unknownSelectMode,
//        string targetUpdateMode,
//        string noUnknownFallbackMode,
//        int nodesCreated,
//        int shortestPathLen,
//        float timeToGoal,
//        int totalNodeVisits,
//        int totalProcessMs,
//        float avgProcessMs,
//        float maxProcessMs)
//    {
//        try
//        {
//            // ★ 出力先フォルダ（他のCSVと揃える）
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
//            //string baseDir = @"D:\GitHub\Node\CSV\Random_EveryNode_FarthestFromStart";
//            //string baseDir = @"D:\GitHub\Node\CSV\Random_OnArrival_FarthestFromStart";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_OnArrival_NewestNode";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_OnArrival_FarthestFromStart";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Random_EveryNode_NewestNode";
//            //string baseDir = @"D:\GitHub\Node\CSV\Nearest_OnArrival_FarthestFromStart";
//            string baseDir = @"D:\GitHub\Node\CSV\Nearest_OnArrival_NewestNode";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Farthest_OnArrival_NewestNode";
//            //string baseDir = @"D:\GitHub\NodeGitHub\CSV\Test";
//            if (!Directory.Exists(baseDir))
//            {
//                Directory.CreateDirectory(baseDir);
//            }

//            // ★ ファイルパスがまだ決まっていなければ、ここで決める
//            // （このRunで VISIT 行が 1 回も出ていないケースもカバー）
//            if (string.IsNullOrEmpty(nodeVisitFilePath))
//            {
//                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
//                string safeUnknown = (unknownSelectMode ?? "Unknown").Replace(",", "_").Replace(" ", "");
//                string safeTarget = (targetUpdateMode ?? "None").Replace(",", "_").Replace(" ", "");
//                string fileName = $"{timestamp}_{safeUnknown}_{safeTarget}.csv";
//                nodeVisitFilePath = Path.Combine(baseDir, fileName);
//            }

//            // ★ GoalNode のグリッド座標を取得
//            int goalCellX = 0;
//            int goalCellZ = 0;
//            if (MapNode.GoalNode != null)
//            {
//                goalCellX = MapNode.GoalNode.cell.x;
//                goalCellZ = MapNode.GoalNode.cell.y;
//            }

//            // ★ ヘッダがまだ書かれていないなら、ここで書く
//            if (!nodeVisitHeaderWritten || !File.Exists(nodeVisitFilePath))
//            {
//                string header =
//                    "RunID," +
//                    "RowType," +
//                    "PlayerID," +
//                    "UnknownSelectMode," +
//                    "TargetUpdateMode," +
//                    "UnknownReferenceDepth," +
//                    "NoUnknownFallbackMode," +
//                    "StepIndex," +
//                    "Frame," +
//                    "NodeName," +
//                    "NodePosX," +
//                    "NodePosY," +
//                    "NodePosZ," +
//                    "CellX," +
//                    "CellZ," +
//                    "GoalCellX," +
//                    "GoalCellZ," +
//                    "NodesCreated," +
//                    "ShortestPathLen," +
//                    "TimeToGoal," +
//                    "TotalNodeVisits," +
//                    "TotalProcessMs," +
//                    "AvgProcessMs," +
//                    "MaxProcessMs";

//                File.AppendAllText(nodeVisitFilePath, header + System.Environment.NewLine);
//                nodeVisitHeaderWritten = true;
//            }

//            var ci = System.Globalization.CultureInfo.InvariantCulture;

//            // ★ RowType = SUMMARY の 1 行を組み立てる（VISIT と同じ23列構造）
//            string line = string.Format(
//                ci,
//                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23}",
//                currentRunId,                  // 0: RunID
//                "SUMMARY",                     // 1: RowType
//                "",                            // 2: PlayerID（Run全体なので空欄）
//                unknownSelectMode,             // 3: UnknownSelectMode
//                targetUpdateMode,              // 4: TargetUpdateMode
//                noUnknownFallbackMode,
//                "",                            // 5: UnknownReferenceDepth（SUMMARYでは空欄でOK）
//                "",                            // 6: StepIndex
//                "",                            // 7: Frame
//                "",                            // 8: NodeName
//                "",                            // 9: NodePosX
//                "",                            //10: NodePosY
//                "",                            //11: NodePosZ
//                "",                            //12: CellX
//                "",                            //13: CellZ
//                goalCellX,                     //14: GoalCellX ★ここにゴールセルX
//                goalCellZ,                     //15: GoalCellZ ★ここにゴールセルZ
//                nodesCreated,                  //16: NodesCreated
//                shortestPathLen,               //17: ShortestPathLen
//                timeToGoal.ToString("F3", ci), //18: TimeToGoal
//                totalNodeVisits,               //19: TotalNodeVisits
//                totalProcessMs,                //20: TotalProcessMs
//                avgProcessMs.ToString("F3", ci), //21: AvgProcessMs
//                maxProcessMs.ToString("F3", ci)  //22: MaxProcessMs
//            );

//            File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);

//            // ★ ここから先のスクショ保存処理は今のままでOK
//            //try
//            //{
//            //    if (!string.IsNullOrEmpty(nodeVisitFilePath))
//            //    {
//            //        //string pngPath = System.IO.Path.ChangeExtension(nodeVisitFilePath, ".png");
//            //        //ScreenCapture.CaptureScreenshot(pngPath);
//            //        //Debug.Log($"[EvaluationLogger] Screenshot saved: {pngPath}");
//            //    }
//            //    else
//            //    {
//            //        Debug.LogWarning("[EvaluationLogger] Screenshot skipped: nodeVisitFilePath is null or empty.");
//            //    }
//            //}
//            //catch (System.Exception ex)
//            //{
//            //    Debug.LogError($"[EvaluationLogger] Screenshot capture failed: {ex}");
//            //}
//        }
//        catch (System.Exception ex)
//        {
//            Debug.LogError($"[EvaluationLogger] LogSummaryRowForCurrentRun failed: {ex}");
//        }
//    }

//    public static void CaptureRunScreenshot()
//    {
//        try
//        {
//            if (string.IsNullOrEmpty(nodeVisitFilePath))
//            {
//                Debug.LogWarning("[EvaluationLogger] Screenshot skipped: nodeVisitFilePath is null or empty.");
//                return;
//            }

//            string pngPath = Path.ChangeExtension(nodeVisitFilePath, ".png");
//            ScreenCapture.CaptureScreenshot(pngPath);
//            Debug.Log($"[EvaluationLogger] Screenshot saved: {pngPath}");
//        }
//        catch (System.Exception ex)
//        {
//            Debug.LogError($"[EvaluationLogger] Screenshot capture failed: {ex}");
//        }
//    }

//    public static void ResetNodeVisitLog()
//    {
//        nodeVisitFilePath = null;
//        nodeVisitHeaderWritten = false;

//        // ★ このタイミングを Run の開始とみなす
//        nodeVisitFrameBase = Time.frameCount;

//        // ★ RunID をインクリメント
//        currentRunId++;
//    }
//}
