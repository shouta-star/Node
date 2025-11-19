using UnityEngine;
using UnityEngine.SceneManagement;

public class RestartManager : MonoBehaviour
{
    private bool hasRestarted = false;

    // 実行開始時間
    private float runStartTime = 0f;

    void Start()
    {
        // 実験開始の時刻
        runStartTime = Time.time;
    }

    private void Update()
    {
        if (hasRestarted) return;

        // 最短経路確定した？
        if (ShortestPathJudge.Instance != null &&
            ShortestPathJudge.Instance.IsShortestConfirmed)
        {
            hasRestarted = true;
            StartCoroutine(RestartFlow());
        }
    }

    // -------------------------------------------------------
    // ★ CSVに UnknownQuantity の実験データを書き出す
    // -------------------------------------------------------
    private void RecordEvaluation()
    {
        // ① ScriptName
        string scriptName = "UnknownQuantity";

        // ② NodesCreated（MapNode総数）
        int nodesCreated = MapNode.allNodeCells.Count;

        // ③ 最短距離
        int shortestDist = ShortestPathJudge.Instance.GetLastStableDistance();
        if (shortestDist < 0) shortestDist = -1;

        // ④ TimeToGoal（＝最短経路確定までの時間）
        float timeToGoal = Time.time - runStartTime;

        // ⑤ TotalNodeVisits（現状は未実装なので0）
        int totalNodeVisits = 0;

        // ⑥ HeavyFrameCount / AvgFrame / WorstFrame（現状0）
        int heavyFrameCount = 0;
        float avgFrame = 0f;
        float worstFrame = 0f;

        // ★ EvaluationLogger に記録
        EvaluationLogger.Record(
            scriptName,
            nodesCreated,
            shortestDist,
            timeToGoal,
            totalNodeVisits,
            heavyFrameCount,
            avgFrame,
            worstFrame
        );

        Debug.Log("=== CSV 出力完了 ===");
    }

    // -------------------------------------------------------
    // ★ シーン再読み込みフロー
    // -------------------------------------------------------
    private System.Collections.IEnumerator RestartFlow()
    {
        Debug.Log("=== ★ 最短経路確定：CSV記録 & 再実行準備中 ★ ===");

        yield return new WaitForSeconds(1f);

        // CSV出力
        RecordEvaluation();

        yield return new WaitForSeconds(1f);

        // シーン再読み込み
        string sceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(sceneName);
    }
}
