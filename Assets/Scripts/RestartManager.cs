using UnityEngine;
using UnityEngine.SceneManagement;

public class RestartManager : MonoBehaviour
{
    private bool hasRestarted = false;

    private void Update()
    {
        // すでに再実行していたら何もしない
        if (hasRestarted) return;

        // 最短経路確定した？
        if (ShortestPathJudge.Instance != null &&
            ShortestPathJudge.Instance.IsShortestConfirmed)
        {
            hasRestarted = true;

            Debug.Log("=== 最短経路確定：再実行準備へ ===");

            // CSV出力して → 少し待って → シーン再読み込み
            StartCoroutine(RestartFlow());
        }
    }

    private System.Collections.IEnumerator RestartFlow()
    {
        // ★ CSV出力（必要ならここに入れる）
        // EvaluationLogger.Record(...)

        yield return new WaitForSeconds(2f);

        string sceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(sceneName);
    }
}
