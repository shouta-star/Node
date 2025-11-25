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

    /// <summary>
    /// 評価結果を CSV に追記する
    /// </summary>
    public static void Record(
        string scriptName,
        int nodesCreated,
        int shortestPathLen,
        float timeToGoal,
        int totalNodeVisits,
        int heavyFrameCount,
        float avgFrameTime,
        float worstFrameTime
    )
    {
        // RunID を加算
        runCounter++;

        // ▼ 保存先パス
        string fileName = $"Evaluation_{scriptName}.csv";
        //string baseDir = @"D:\GitHub\NodeGitHub\CSV";
        string baseDir = @"D:\GitHub\Node\CSV";
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
                "HeavyFrameCount," +
                "AvgFrame," +
                "WorstFrame";

            File.AppendAllText(path, header + "\n");
        }

        // ▼ CSV 1 行分のデータを作成
        string line =
            runCounter + "," +
            nodesCreated + "," +
            shortestPathLen + "," +
            timeToGoal.ToString("F3") + "," +
            totalNodeVisits + "," +
            heavyFrameCount + "," +
            avgFrameTime.ToString("F3") + "," +
            worstFrameTime.ToString("F3");

        // ▼ 追記
        File.AppendAllText(path, line + "\n");

        Debug.Log($"[EvaluationLogger] Log appended → {path}");
    }
}
