using System.IO;
using UnityEngine;

/// <summary>
/// 実行評価を CSV に追記する共通ロガー
/// ・スクリプトごとに CSV を 1 個作成
/// ・RunID はアプリ起動からの連番（static）
/// ・1 行に 1 回分の評価結果を追加する
/// ・ファイルは Application.persistentDataPath に保存
/// </summary>
public static class EvaluationLogger
{
    // 起動してからの連番（全スクリプト共通）
    private static int runCounter = 0;

    // ★ Player が通過した Node を記録する CSV 用（ヘッダ書き込み済みフラグ）
    private static bool nodeVisitHeaderWritten = false;

    // Node到達ログ用のファイルパス（1Runで1つ）
    private static string nodeVisitFilePath = null;

    private static int nodeVisitFrameBase = 0;

    // このRunのID（1,2,3,...）
    private static int currentRunId = 0;

    // 最後に記録されたモード（SUMMARY行に使いたい場合）
    private static string lastUnknownSelectMode = "";
    private static string lastTargetUpdateMode = "";

    /// <summary>
    /// 評価結果を CSV に追記する
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
        // RunID を加算
        runCounter++;

        // ▼ 保存先パス
        string fileName = $"Evaluation_{scriptName}.csv";
        string baseDir = @"D:\GitHub\NodeGitHub\CSV";
        //string baseDir = @"D:\GitHub\Node\CSV";
        string path = Path.Combine(baseDir, fileName);

        // ▼ CSV が無ければヘッダ行を書く
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

        // ▼ CSV 1 行分のデータを作成
        string line =
            runCounter + "," +
            nodesCreated + "," +
            shortestPathLen + "," +
            timeToGoal.ToString("F3") + "," +
            totalNodeVisits + "," +
            totalProcessMs + "," +            // ★ 修正済
            avgProcessMs.ToString("F3") + "," +  // ★ 修正済
            maxProcessMs.ToString("F3");         // ★ 修正済
                                             //heavyFrameCount + "," +
                                             //avgFrameTime.ToString("F3") + "," +
                                             //worstFrameTime.ToString("F3");

        // ▼ 追記
        File.AppendAllText(path, line + "\n");

        Debug.Log($"[EvaluationLogger] Log appended → {path}");
    }

    // EvaluationLogger.cs に追加するメソッド例
    //public static void LogNodeVisit(int playerId, int frame, MapNode node)
    //{
    //    if (node == null)
    //    {
    //        Debug.LogWarning("[EvaluationLogger] LogNodeVisit called with null node.");
    //        return;
    //    }

    //    try
    //    {
    //        // ★ 出力先フォルダ（プロジェクトに合わせて変更OK）
    //        string baseDir = @"D:\GitHub\NodeGitHub\CSV";
    //        if (!System.IO.Directory.Exists(baseDir))
    //        {
    //            System.IO.Directory.CreateDirectory(baseDir);
    //        }

    //        string fileName = "NodeVisit_CellFromStart.csv";
    //        string path = System.IO.Path.Combine(baseDir, fileName);

    //        // ★ ファイルが無ければヘッダ行を書き込む
    //        if (!System.IO.File.Exists(path))
    //        {
    //            string header = "PlayerId,NodeName,NodePosX,NodePosY,NodePosZ";
    //            System.IO.File.AppendAllText(path, header + System.Environment.NewLine);
    //        }

    //        // ★ Node の座標を取得
    //        Vector3 nodePos = node.transform.position;

    //        // 小数点のフォーマットを安定させる（カンマ区切りと衝突しないように）
    //        var ci = System.Globalization.CultureInfo.InvariantCulture;

    //        string line = string.Format(
    //            ci,
    //            "{0},{1},{2},{3},{4}",
    //            playerId,
    //            node.name,
    //            nodePos.x,
    //            nodePos.y,
    //            nodePos.z
    //        );

    //        System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Debug.LogError($"[EvaluationLogger] LogNodeVisit failed: {ex}");
    //    }
    //}
    //public static void LogNodeVisit(int playerId, int frame, MapNode node)
    //{
    //    if (node == null)
    //    {
    //        Debug.LogWarning("[EvaluationLogger] LogNodeVisit called with null node.");
    //        return;
    //    }

    //    try
    //    {
    //        // ★ 出力先フォルダ（他のCSVと揃える）
    //        string baseDir = @"D:\GitHub\NodeGitHub\CSV";
    //        if (!Directory.Exists(baseDir))
    //        {
    //            Directory.CreateDirectory(baseDir);
    //        }

    //        string fileName = "NodeVisit_CellFromStart.csv";
    //        string path = Path.Combine(baseDir, fileName);

    //        // ★ ファイルが無ければヘッダ行を書き込む
    //        if (!File.Exists(path))
    //        {
    //            string header = "PlayerId,Frame,NodeName,NodePosX,NodePosY,NodePosZ";
    //            File.AppendAllText(path, header + System.Environment.NewLine);
    //        }

    //        // ★ Node の座標を取得
    //        Vector3 nodePos = node.transform.position;

    //        // 小数点のフォーマットを安定させる（カンマと衝突しないように）
    //        var ci = System.Globalization.CultureInfo.InvariantCulture;

    //        // ★ 1行ぶんを組み立て
    //        string line = string.Format(
    //            ci,
    //            "{0},{1},{2},{3},{4},{5}",
    //            playerId,
    //            frame,
    //            node.name,
    //            nodePos.x,
    //            nodePos.y,
    //            nodePos.z
    //        );

    //        // ★ 追記
    //        File.AppendAllText(path, line + System.Environment.NewLine);
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Debug.LogError($"[EvaluationLogger] LogNodeVisit failed: {ex}");
    //    }
    //}
    public static void LogNodeVisit(
    int playerId,
    int frame,
    MapNode node,
    string unknownSelectMode,
    string targetUpdateMode,
    int stepIndex)
    {
        if (node == null)
        {
            Debug.LogWarning("[EvaluationLogger] LogNodeVisit called with null node.");
            return;
        }

        try
        {
            // ★ 出力先フォルダ（他のCSVと揃える）
            //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
            string baseDir = @"D:\GitHub\Node\CSV\Random_OnArrival";
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            // ★ ファイルパスがまだ決まっていなければ、ここで決める
            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                // 日付時間: 20251130_235959 のような形式
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // モード名に変な文字（空白やカンマ）が入っても困らないように軽く整形
                string safeUnknown = (unknownSelectMode ?? "Unknown").Replace(",", "_").Replace(" ", "");
                string safeTarget = (targetUpdateMode ?? "None").Replace(",", "_").Replace(" ", "");

                string fileName = $"{timestamp}_{safeUnknown}_{safeTarget}.csv";
                nodeVisitFilePath = Path.Combine(baseDir, fileName);
            }

            // ★ ファイルがまだ無い or ヘッダ未書き込みならヘッダ行を書く
            if (!nodeVisitHeaderWritten || !File.Exists(nodeVisitFilePath))
            {
                //string header = "PlayerId,Frame,NodeName,NodePosX,NodePosY,NodePosZ";
                string header =
                    "RunID," +
                    "RowType," +
                    "PlayerID," +
                    "UnknownSelectMode," +
                    "TargetUpdateMode," +
                    "StepIndex," +
                    "Frame," +
                    "NodeName," +
                    "NodePosX," +
                    "NodePosY," +
                    "NodePosZ," +
                    "CellX," +
                    "CellZ," +
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

            // ★ Node の座標を取得
            Vector3 nodePos = node.transform.position;

            // グリッド座標（MapNode 側で持っている cell）
            int cellX = node.cell.x;
            int cellZ = node.cell.y;

            // 小数点のフォーマット（カンマと衝突しないように）
            var ci = System.Globalization.CultureInfo.InvariantCulture;

            // Run内フレーム
            int localFrame = frame - nodeVisitFrameBase;

            // RowType は VISIT 固定
            string rowType = "VISIT";

            // モード名は後でSUMMARY行にも使いたいので保存
            lastUnknownSelectMode = unknownSelectMode;
            lastTargetUpdateMode = targetUpdateMode;

            // ★ 1行ぶんを組み立て
            //string line = string.Format(
            //    ci,
            //    "{0},{1},{2},{3},{4},{5}",
            //    playerId,
            //    //frame,
            //    localFrame,
            //    node.name,
            //    nodePos.x,
            //    nodePos.y,
            //    nodePos.z
            //);
            string line = string.Format(
                ci,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19}",
                currentRunId,          // RunID
                rowType,               // RowType = VISIT
                playerId,              // PlayerID
                unknownSelectMode,     // UnknownSelectMode
                targetUpdateMode,      // TargetUpdateMode
                stepIndex,             // StepIndex
                localFrame,            // Frame
                node.name,             // NodeName
                nodePos.x,             // NodePosX
                nodePos.y,             // NodePosY
                nodePos.z,             // NodePosZ
                cellX,                 // CellX
                cellZ,                 // CellZ
                "",                    // NodesCreated (VISIT行なので空)
                "",                    // ShortestPathLen
                "",                    // TimeToGoal
                "",                    // TotalNodeVisits
                "",                    // TotalProcessMs
                "",                    // AvgProcessMs
                ""                     // MaxProcessMs
            );

            // ★ 追記
            File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EvaluationLogger] LogNodeVisit failed: {ex}");
        }
    }

    /// <summary>
    /// この Run のサマリ情報を「RowType=SUMMARY」として
    /// NodeVisit 用 CSV に 1 行だけ追記する
    /// </summary>
    public static void LogSummaryRowForCurrentRun(
        string unknownSelectMode,
        string targetUpdateMode,
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
            // ★ 出力先フォルダ（他のCSVと揃える）
            string baseDir = @"D:\GitHub\NodeGitHub\CSV";
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            // ★ ファイルパスがまだ決まっていなければ、ここで決める
            // （このRunで VISIT 行が 1 回も出ていないケースもカバー）
            if (string.IsNullOrEmpty(nodeVisitFilePath))
            {
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeUnknown = (unknownSelectMode ?? "Unknown").Replace(",", "_").Replace(" ", "");
                string safeTarget = (targetUpdateMode ?? "None").Replace(",", "_").Replace(" ", "");
                string fileName = $"{timestamp}_{safeUnknown}_{safeTarget}.csv";
                nodeVisitFilePath = Path.Combine(baseDir, fileName);
            }

            // ★ ヘッダがまだ書かれていないなら、ここで書く
            if (!nodeVisitHeaderWritten || !File.Exists(nodeVisitFilePath))
            {
                string header =
                    "RunID," +
                    "RowType," +
                    "PlayerID," +
                    "UnknownSelectMode," +
                    "TargetUpdateMode," +
                    "StepIndex," +
                    "Frame," +
                    "NodeName," +
                    "NodePosX," +
                    "NodePosY," +
                    "NodePosZ," +
                    "CellX," +
                    "CellZ," +
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

            // ★ RowType = SUMMARY の 1 行を組み立てる
            // 先頭 13 列（PlayerID, StepIndex, Frame, NodeName, ... CellZ）は空欄にしておく
            string line = string.Format(
                ci,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19}",
                currentRunId,          // RunID
                "SUMMARY",             // RowType
                "",                    // PlayerID（Run全体なので空欄）
                unknownSelectMode,     // UnknownSelectMode
                targetUpdateMode,      // TargetUpdateMode
                "",                    // StepIndex
                "",                    // Frame
                "",                    // NodeName
                "",                    // NodePosX
                "",                    // NodePosY
                "",                    // NodePosZ
                "",                    // CellX
                "",                    // CellZ
                nodesCreated,          // NodesCreated
                shortestPathLen,       // ShortestPathLen
                timeToGoal.ToString("F3", ci),   // TimeToGoal
                totalNodeVisits,       // TotalNodeVisits
                totalProcessMs,        // TotalProcessMs
                avgProcessMs.ToString("F3", ci), // AvgProcessMs
                maxProcessMs.ToString("F3", ci)  // MaxProcessMs
            );

            File.AppendAllText(nodeVisitFilePath, line + System.Environment.NewLine);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EvaluationLogger] LogSummaryRowForCurrentRun failed: {ex}");
        }
    }


    public static void ResetNodeVisitLog()
    {
        nodeVisitFilePath = null;
        nodeVisitHeaderWritten = false;

        // ★ このタイミングを Run の開始とみなす
        nodeVisitFrameBase = Time.frameCount;

        // ★ RunID をインクリメント
        currentRunId++;
    }
}
