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
    public static void LogNodeVisit(int playerId, MapNode node)
    {
        if (node == null)
        {
            Debug.LogWarning("[EvaluationLogger] LogNodeVisit called with null node.");
            return;
        }

        try
        {
            // ★ 出力先フォルダ（プロジェクトに合わせて変更OK）
            string baseDir = @"D:\GitHub\NodeGitHub\CSV";
            if (!System.IO.Directory.Exists(baseDir))
            {
                System.IO.Directory.CreateDirectory(baseDir);
            }

            string fileName = "NodeVisit_CellFromStart.csv";
            string path = System.IO.Path.Combine(baseDir, fileName);

            // ★ ファイルが無ければヘッダ行を書き込む
            if (!System.IO.File.Exists(path))
            {
                string header = "PlayerId,NodeName,NodePosX,NodePosY,NodePosZ";
                System.IO.File.AppendAllText(path, header + System.Environment.NewLine);
            }

            // ★ Node の座標を取得
            Vector3 nodePos = node.transform.position;

            // 小数点のフォーマットを安定させる（カンマ区切りと衝突しないように）
            var ci = System.Globalization.CultureInfo.InvariantCulture;

            string line = string.Format(
                ci,
                "{0},{1},{2},{3},{4}",
                playerId,
                node.name,
                nodePos.x,
                nodePos.y,
                nodePos.z
            );

            System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[EvaluationLogger] LogNodeVisit failed: {ex}");
        }
    }

}
