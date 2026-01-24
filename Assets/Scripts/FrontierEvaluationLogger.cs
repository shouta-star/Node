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

    public static void CaptureRunScreenshot()
    {
        CaptureRunScreenshot(null);
    }

    /// <summary>
    /// Run中のスクショを保存する（tag を付けるとファイル名が上書きされない）
    /// 例: 20260120_120001.csv → 20260120_120001_S000.png
    /// </summary>
    public static void CaptureRunScreenshot(string tag)
    {
        try
        {
            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                Debug.LogWarning("[FrontierEvaluationLogger] Screenshot skipped: nodeVisitFilePath is null or empty.");
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
            Debug.Log($"[FrontierEvaluationLogger] Screenshot saved: {pngPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FrontierEvaluationLogger] Screenshot capture failed: {ex}");
        }
    }

}
