////using System;
////using System.IO;
////using System.Text;
////using UnityEngine;

////public static class FrontierEvaluationLogger
////{
////    private static string baseDir = null;
////    private static string nodeVisitFilePath = null;

////    public static void EnsureBaseDir(string customBaseDir = null)
////    {
////        if (!string.IsNullOrEmpty(customBaseDir))
////        {
////            baseDir = customBaseDir;
////        }
////        else if (string.IsNullOrEmpty(baseDir))
////        {
////            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
////            baseDir = Path.Combine(projectRoot, "CSV", "Frontier");
////        }

////        if (!Directory.Exists(baseDir))
////            Directory.CreateDirectory(baseDir);
////    }

////    public static void ResetNodeVisitLog(string prefix, int runIndex)
////    {
////        EnsureBaseDir();

////        string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
////        string safePrefix = (prefix ?? "Frontier").Replace(",", "_").Replace(" ", "");
////        string fileName = $"{ts}_{safePrefix}_Run{runIndex:000}.csv";

////        nodeVisitFilePath = Path.Combine(baseDir, fileName);

////        using (var fs = new FileStream(nodeVisitFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
////        using (var sw = new StreamWriter(fs, Encoding.UTF8))
////        {
////            sw.WriteLine("Timestamp,RunIndex,PlayerId,Frame,StepIndex,NodeName,CellX,CellY,PosX,PosZ,PassCount,UnknownCount,WallCount,LinkCount");
////        }
////    }

////    public static void LogNodeVisit(int runIndex, int playerId, int frame, int stepIndex, FrontierNode node)
////    {
////        if (node == null) return;
////        if (string.IsNullOrEmpty(nodeVisitFilePath))
////            ResetNodeVisitLog("Frontier", runIndex);

////        try
////        {
////            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss.fff");
////            string line =
////                $"{ts},{runIndex},{playerId},{frame},{stepIndex}," +
////                $"{node.name},{node.cell.x},{node.cell.y}," +
////                $"{node.transform.position.x:F3},{node.transform.position.z:F3}," +
////                $"{node.passCount},{node.unknownCount},{node.wallCount},{(node.links != null ? node.links.Count : 0)}";

////            using (var fs = new FileStream(nodeVisitFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
////            using (var sw = new StreamWriter(fs, Encoding.UTF8))
////            {
////                sw.WriteLine(line);
////            }
////        }
////        catch (Exception ex)
////        {
////            Debug.LogError($"[FrontierEvaluationLogger] LogNodeVisit failed: {ex}");
////        }
////    }

////    public static string GetCurrentVisitLogPath() => nodeVisitFilePath;
////}

//using System.Globalization;
//using System.IO;
//using UnityEngine;

///// <summary>
///// Frontier 用：EvaluationLogger と同じ列・同じRowType構造の CSV を出力する logger。
///// さらに、既存の FrontierRestartManager / FrontierExplorer が呼んでいる旧APIも互換で用意。
/////
///// CSV列（EvaluationLogger同等）:
///// RunID,RowType,PlayerID,UnknownSelectMode,TargetUpdateMode,UnknownReferenceDepth,StepIndex,Frame,
///// NodeName,NodePosX,NodePosY,NodePosZ,CellX,CellZ,GoalCellX,GoalCellZ,NodesCreated,ShortestPathLen,
///// TimeToGoal,TotalNodeVisits,TotalProcessMs,AvgProcessMs,MaxProcessMs
/////
///// RowType:
/////  - VISIT
/////  - SUMMARY
///// </summary>
//public static class FrontierEvaluationLogger
//{
//    // ===== 出力先 =====
//    private static string baseDir = null;

//    // ===== 現在Run =====
//    private static int currentRunId = 0;
//    private static int nodeVisitFrameBase = 0;

//    // ===== CSVファイル =====
//    private static string nodeVisitFilePath = null;
//    private static bool headerWritten = false;

//    // =========================
//    // 旧API互換（エラーを消すため）
//    // =========================

//    /// <summary>
//    /// 旧: EnsureBaseDir(string)
//    /// </summary>
//    public static void EnsureBaseDir(string dir)
//    {
//        if (!string.IsNullOrEmpty(dir))
//            baseDir = dir;

//        if (string.IsNullOrEmpty(baseDir))
//        {
//            // 指定無しならプロジェクト直下 /CSV/Frontier
//            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
//            baseDir = Path.Combine(projectRoot, "CSV", "Frontier");
//        }

//        if (!Directory.Exists(baseDir))
//            Directory.CreateDirectory(baseDir);
//    }

//    /// <summary>
//    /// 旧: ResetNodeVisitLog(prefix, runIndex)
//    /// - runIndex を RunID として使う
//    /// - frameBase を更新
//    /// - ファイル名を作る（timestamp_prefix_RunXXX.csv）
//    /// </summary>
//    public static void ResetNodeVisitLog(string prefix, int runIndex)
//    {
//        EnsureBaseDir(baseDir);

//        currentRunId = runIndex;
//        nodeVisitFrameBase = Time.frameCount;

//        headerWritten = false;

//        string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
//        string safePrefix = string.IsNullOrEmpty(prefix) ? "Frontier" : prefix.Replace(",", "_").Replace(" ", "");
//        string fileName = $"{ts}_{safePrefix}_Run{runIndex:000}.csv";

//        nodeVisitFilePath = Path.Combine(baseDir, fileName);

//        // 先にヘッダを書いておく（旧実装と同様）
//        WriteHeaderIfNeeded();
//    }

//    /// <summary>
//    /// 旧: GetCurrentVisitLogPath()
//    /// </summary>
//    public static string GetCurrentVisitLogPath() => nodeVisitFilePath;

//    /// <summary>
//    /// 旧: LogNodeVisit(runIndex, playerId, frame, stepIndex, node)
//    /// → UnknownSelectMode/TargetUpdateMode/UnknownReferenceDepth は固定値で埋める
//    /// （必要なら FrontierExplorer 側で別オーバーロードを使って上書き可能）
//    /// </summary>
//    public static void LogNodeVisit(int runIndex, int playerId, int frame, int stepIndex, FrontierNode node)
//    {
//        // 旧呼び出しは runIndex を渡してくるので、ここで currentRunId を合わせる
//        currentRunId = runIndex;

//        // 互換用の固定値
//        const string unknownSelectMode = "Frontier";
//        const string targetUpdateMode = "OnArrival"; // どれでもOK（CSV互換のために文字を埋める）
//        const int unknownReferenceDepth = 0;

//        LogNodeVisit(playerId, frame, node, unknownSelectMode, targetUpdateMode, unknownReferenceDepth, stepIndex);
//    }

//    // =========================
//    // 新API（本体）
//    // =========================

//    public static int GetCurrentRunId() => currentRunId;
//    public static string GetNodeVisitFilePath() => nodeVisitFilePath;

//    /// <summary>
//    /// 新: Run開始（RunID++ して新規ファイルにしたい場合に使える）
//    /// ※既存コードは ResetNodeVisitLog(prefix, runIndex) を使ってるので不要
//    /// </summary>
//    public static void ResetNodeVisitLog()
//    {
//        EnsureBaseDir(baseDir);
//        nodeVisitFilePath = null;
//        headerWritten = false;
//        nodeVisitFrameBase = Time.frameCount;
//        currentRunId++;
//    }

//    /// <summary>
//    /// 新: VISIT 行
//    /// </summary>
//    public static void LogNodeVisit(
//        int playerId,
//        int frame,
//        FrontierNode node,
//        string unknownSelectMode,
//        string targetUpdateMode,
//        int unknownReferenceDepth,
//        int stepIndex)
//    {
//        if (node == null) return;

//        EnsureBaseDir(baseDir);

//        // ファイルがまだ無いならデフォルト名で作る（保険）
//        if (string.IsNullOrEmpty(nodeVisitFilePath))
//        {
//            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
//            nodeVisitFilePath = Path.Combine(baseDir, $"{ts}_Frontier_Run{currentRunId:000}.csv");
//        }

//        WriteHeaderIfNeeded();

//        var ci = CultureInfo.InvariantCulture;

//        Vector3 p = node.transform.position;
//        int cellX = node.cell.x;
//        int cellZ = node.cell.y;

//        int localFrame = frame - nodeVisitFrameBase;

//        // VISIT（23列、後半は空欄）
//        string line = string.Format(
//            ci,
//            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22}",
//            currentRunId,                 // 0: RunID
//            "VISIT",                      // 1: RowType
//            playerId,                     // 2
//            unknownSelectMode ?? "",      // 3
//            targetUpdateMode ?? "",       // 4
//            unknownReferenceDepth,        // 5
//            stepIndex,                    // 6
//            localFrame,                   // 7
//            node.name,                    // 8
//            p.x,                          // 9
//            p.y,                          //10
//            p.z,                          //11
//            cellX,                        //12
//            cellZ,                        //13
//            "", "", "", "", "", "", "", "", "" // 14..22 空欄
//        );

//        File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);
//    }

//    /// <summary>
//    /// 新: SUMMARY 行
//    /// </summary>
//    public static void LogSummaryRowForCurrentRun(
//        string unknownSelectMode,
//        string targetUpdateMode,
//        int goalCellX,
//        int goalCellZ,
//        int nodesCreated,
//        int shortestPathLen,
//        float timeToGoal,
//        int totalNodeVisits,
//        int totalProcessMs,
//        float avgProcessMs,
//        float maxProcessMs)
//    {
//        EnsureBaseDir(baseDir);

//        if (string.IsNullOrEmpty(nodeVisitFilePath))
//        {
//            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
//            nodeVisitFilePath = Path.Combine(baseDir, $"{ts}_Frontier_Run{currentRunId:000}.csv");
//        }

//        WriteHeaderIfNeeded();

//        var ci = CultureInfo.InvariantCulture;

//        string line = string.Format(
//            ci,
//            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22}",
//            currentRunId,                               // 0
//            "SUMMARY",                                  // 1
//            "",                                         // 2
//            unknownSelectMode ?? "",                     // 3
//            targetUpdateMode ?? "",                      // 4
//            "", "", "", "", "", "", "", "", "",          // 5..13
//            goalCellX,                                   // 14
//            goalCellZ,                                   // 15
//            nodesCreated,                                // 16
//            shortestPathLen,                             // 17
//            timeToGoal.ToString("F3", ci),                // 18
//            totalNodeVisits,                              // 19
//            totalProcessMs,                               // 20
//            avgProcessMs.ToString("F3", ci),              // 21
//            maxProcessMs.ToString("F3", ci)               // 22
//        );

//        File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);
//    }

//    public static void CaptureRunScreenshot()
//    {
//        try
//        {
//            if (string.IsNullOrEmpty(nodeVisitFilePath))
//            {
//                Debug.LogWarning("[FrontierEvaluationLogger] Screenshot skipped: nodeVisitFilePath is empty.");
//                return;
//            }

//            string pngPath = Path.ChangeExtension(nodeVisitFilePath, ".png");
//            ScreenCapture.CaptureScreenshot(pngPath);
//            Debug.Log($"[FrontierEvaluationLogger] Screenshot saved: {pngPath}");
//        }
//        catch (System.Exception ex)
//        {
//            Debug.LogError($"[FrontierEvaluationLogger] Screenshot capture failed: {ex}");
//        }
//    }

//    // =========================
//    // 内部
//    // =========================

//    private static void WriteHeaderIfNeeded()
//    {
//        if (headerWritten && File.Exists(nodeVisitFilePath)) return;

//        string header =
//            "RunID," +
//            "RowType," +
//            "PlayerID," +
//            "UnknownSelectMode," +
//            "TargetUpdateMode," +
//            "UnknownReferenceDepth," +
//            "StepIndex," +
//            "Frame," +
//            "NodeName," +
//            "NodePosX," +
//            "NodePosY," +
//            "NodePosZ," +
//            "CellX," +
//            "CellZ," +
//            "GoalCellX," +
//            "GoalCellZ," +
//            "NodesCreated," +
//            "ShortestPathLen," +
//            "TimeToGoal," +
//            "TotalNodeVisits," +
//            "TotalProcessMs," +
//            "AvgProcessMs," +
//            "MaxProcessMs";

//        File.AppendAllText(nodeVisitFilePath, header + System.Environment.NewLine);
//        headerWritten = true;
//    }
//}

using System;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class FrontierEvaluationLogger
{
    private static string baseDir = null;
    private static string nodeVisitFilePath = null;
    private static bool nodeVisitHeaderWritten = false;
    private static int nodeVisitFrameBase = 0;

    private static int goalCellX = 0;
    private static int goalCellZ = 0;

    public static void EnsureBaseDir(string dir)
    {
        baseDir = dir;
        if (!Directory.Exists(baseDir))
            Directory.CreateDirectory(baseDir);
    }

    public static void SetGoalCell(Vector2Int goalCell)
    {
        goalCellX = goalCell.x;
        goalCellZ = goalCell.y;
    }

    public static void ResetNodeVisitLog(string filePrefix, int runIndex)
    {
        nodeVisitHeaderWritten = false;
        nodeVisitFrameBase = Time.frameCount;

        string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{ts}.csv";
        nodeVisitFilePath = Path.Combine(baseDir ?? Application.persistentDataPath, fileName);
    }

    public static string GetCurrentVisitLogPath() => nodeVisitFilePath;

    // FrontierExplorer 互換（5引数）
    public static void LogNodeVisit(int runIndex, int playerId, int frame, int stepIndex, FrontierNode node)
    {
        LogNodeVisit(
            runIndex,
            playerId,
            frame,
            stepIndex,
            node,
            unknownSelectMode: "Frontier",
            targetUpdateMode: "Frontier",
            unknownReferenceDepth: 0
            //nodesCreated: FrontierNode.All != null ? FrontierNode.All.Count : 0,
            //shortestPathLen: -1,
            //timeToGoal: -1f,
            //totalNodeVisits: FrontierNode.totalPassCount,
            //totalProcessMs: -1,
            //avgProcessMs: -1f,
            //maxProcessMs: -1f
        );
    }

    // 23列（EvaluationLogger と同じ列数）
    public static void LogNodeVisit(
        int runIndex,
        int playerId,
        int frame,
        int stepIndex,
        FrontierNode node,
        string unknownSelectMode,
        string targetUpdateMode,
        int unknownReferenceDepth
        //int nodesCreated,
        //int shortestPathLen,
        //float timeToGoal,
        //int totalNodeVisits,
        //int totalProcessMs,
        //float avgProcessMs,
        //float maxProcessMs
    )
    {
        try
        {
            if (node == null) return;

            if (string.IsNullOrEmpty(baseDir))
                EnsureBaseDir(Path.Combine(Application.dataPath, "..", "CSV", "Frontier"));

            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                nodeVisitFilePath = Path.Combine(baseDir, $"{ts}.csv");
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

                File.AppendAllText(nodeVisitFilePath, header + Environment.NewLine);
                nodeVisitHeaderWritten = true;
            }

            var ci = CultureInfo.InvariantCulture;
            Vector3 pos = node.transform.position;
            int localFrame = frame - nodeVisitFrameBase;

            string line = string.Format(
                ci,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22}",
                runIndex,
                "VISIT",
                playerId,
                unknownSelectMode,
                targetUpdateMode,
                unknownReferenceDepth,
                stepIndex,
                localFrame,
                node.name,
                pos.x,
                pos.y,
                pos.z,
                node.cell.x,
                node.cell.y,
                "", "", "", "", "", "", "", "", ""
                //goalCellX,
                //goalCellZ,
                //nodesCreated,
                //shortestPathLen,
                //timeToGoal,
                //totalNodeVisits,
                //totalProcessMs,
                //avgProcessMs,
                //maxProcessMs
            );

            File.AppendAllText(nodeVisitFilePath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FrontierEvaluationLogger] LogNodeVisit failed: {ex}");
        }
    }

    // ★ 最後に1回だけ呼ぶ（EvaluationLoggerのSUMMARY行相当）
    public static void LogSummary(
        int runIndex,
        string unknownSelectMode,
        string targetUpdateMode,
        int unknownReferenceDepth,
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
            if (string.IsNullOrEmpty(baseDir))
                EnsureBaseDir(Path.Combine(Application.dataPath, "..", "CSV", "Frontier"));

            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                nodeVisitFilePath = Path.Combine(baseDir, $"{ts}.csv");
            }

            // ヘッダが無いなら書く（VISITが0回でもSUMMARYだけは出せる）
            if (!nodeVisitHeaderWritten || !File.Exists(nodeVisitFilePath))
            {
                string header =
                    "RunID,RowType,PlayerID,UnknownSelectMode,TargetUpdateMode,UnknownReferenceDepth,StepIndex,Frame," +
                    "NodeName,NodePosX,NodePosY,NodePosZ,CellX,CellZ," +
                    "GoalCellX,GoalCellZ,NodesCreated,ShortestPathLen,TimeToGoal,TotalNodeVisits,TotalProcessMs,AvgProcessMs,MaxProcessMs";
                AppendLine(header);
                nodeVisitHeaderWritten = true;
            }

            var ci = CultureInfo.InvariantCulture;

            // SUMMARY行：Node情報(8..13)は空欄、GoalCell〜MaxProcessMsだけ埋める
            string line = string.Format(
                ci,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22}",
                runIndex,
                "SUMMARY",
                "", // PlayerID空欄
                unknownSelectMode,
                targetUpdateMode,
                unknownReferenceDepth,
                "", // StepIndex空欄
                "", // Frame空欄
                "", "", "", "", "", "", // NodeName..CellZ 空欄
                goalCellX,
                goalCellZ,
                nodesCreated,
                shortestPathLen,
                timeToGoal.ToString("F3", ci),
                totalNodeVisits,
                totalProcessMs,
                avgProcessMs.ToString("F3", ci),
                maxProcessMs.ToString("F3", ci)
            );

            AppendLine(line);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FrontierEvaluationLogger] LogSummary failed: {ex}");
        }
    }

    private static void AppendLine(string line)
    {
        using (var fs = new FileStream(nodeVisitFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        using (var sw = new StreamWriter(fs))
        {
            sw.WriteLine(line);
        }
    }
}
