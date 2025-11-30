using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.IO;

public class RestartManager : MonoBehaviour
{
    // ★ シングルトン追加
    public static RestartManager Instance = null;

    private bool hasRestarted = false;

    // 実行開始時間
    private float runStartTime = 0f;

    bool isRestarting = false;

    // ★ CellFromStart 実験用：何回目の実行か
    private static int runIndex = 0;

    // ★ Awake 追加：永続化 & シングルトン構築
    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);   // ★追加：再ロード後も破棄しない
    }

    void Start()
    {
        // 実験開始の時刻
        runStartTime = Time.time;
    }

    private void Update()
    {
        if (hasRestarted) return;

        //// 最短経路確定した？
        //if (ShortestPathJudge.Instance != null &&
        //    ShortestPathJudge.Instance.IsShortestConfirmed)
        //{
        //    hasRestarted = true;
        //    StartCoroutine(RestartFlow());
        //}

        //if (UnknownQuantity.shortestModeArrivalCount >= 10)
        //{
        //    hasRestarted = true;
        //    StartCoroutine(RestartFlow());
        //}

    }

    public void StartRestart()
    {
        if (isRestarting) return;
        isRestarting = true;
        StartCoroutine(RestartFlow());
    }

    // =============================================================
    // CellFromStart 用 Player.csv 出力
    // =============================================================
    //private void WriteCellFromStartPlayerCsv()
    //{
    //    // ★ シーン内の CellFromStart プレイヤーを全部取得
    //    var players = FindObjectsOfType<CellFromStart>();
    //    if (players == null || players.Length == 0)
    //    {
    //        Debug.LogWarning("[RestartManager] CellFromStart が見つからないので Player.csv は出力しません。");
    //        return;
    //    }

    //    // ★ モード情報は 1Run 中では全Player共通の前提
    //    var first = players[0];
    //    string u = first.unknownSelectMode.ToString();
    //    string t = first.targetUpdateMode.ToString();

    //    // ★ ファイル名の共通プレフィックス
    //    //    例: CFS_UNearest_TFarthest_Run01_Player.csv
    //    string prefix = $"CFS_U{u}_T{t}_Run{runIndex:00}";

    //    //string dir = Application.persistentDataPath;
    //    string dir = @"D:\GitHub\NodeGitHub\CSV";
    //    string path = Path.Combine(dir, prefix + "_Player.csv");

    //    // ★ ディレクトリ存在チェック
    //    if (!Directory.Exists(dir))
    //    {
    //        Directory.CreateDirectory(dir);
    //    }

    //    // ★ ヘッダー行（ファイルがまだ無ければ書く）
    //    if (!File.Exists(path))
    //    {
    //        string header =
    //            "RunIndex," +
    //            "UnknownSelectMode," +
    //            "TargetUpdateMode," +
    //            "PlayerSessionId," +
    //            "GoalReached," +
    //            "FrameToGoal," +
    //            "StepsWalked," +
    //            "UniqueNodesVisited," +
    //            "DeadEndEnterCount";

    //        File.WriteAllText(path, header + "\n");
    //    }

    //    // ★ 各 Player 1行ずつ追記
    //    for (int i = 0; i < players.Length; i++)
    //    {
    //        var p = players[i];

    //        int goal = p.goalReached ? 1 : 0;

    //        string line = string.Join(",",
    //            runIndex,
    //            u,
    //            t,
    //            i,                      // PlayerSessionId として一旦 index を使用
    //            goal,
    //            p.frameToGoal,
    //            p.stepsWalked,
    //            p.uniqueNodesVisited,
    //            p.deadEndEnterCount
    //        );

    //        File.AppendAllText(path, line + "\n");
    //    }

    //    Debug.Log($"[RestartManager] Player.csv 出力完了 → {path}");
    //}
    public void WriteCellFromStartPlayerCsv(CellFromStart player)
    {
        if (player == null)
        {
            Debug.LogWarning("[RestartManager] CellFromStart が null なので Player.csv は出力しません。");
            return;
        }

        // ★ 保存先フォルダ（今の Evaluation と同じ所）
        string baseDir = @"D:\GitHub\NodeGitHub\CSV";
        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        // ★ ファイル名（UnknownSelectMode / TargetUpdateMode 付き）
        //   例: CFS_UFarthest_TEveryNode_Player.csv
        string prefix = $"CFS_U{player.unknownSelectMode}_T{player.targetUpdateMode}";
        string path = Path.Combine(baseDir, prefix + "_Player.csv");

        // ★ ヘッダ行（ファイルが無いときだけ書く）
        if (!File.Exists(path))
        {
            File.AppendAllText(path,
                "RunIndex,StepsWalked,UniqueNodesVisited,DeadEndEnterCount,FrameToGoal\n");
        }

        // ★ 1行分のデータ（RunIndex は RestartManager 側で管理してるならそれを使う）
        int runIndex = 0; // もし RestartManager に runIndex フィールドがあるならそれを使う
        if (File.Exists(path))
        {
            int lineCount = File.ReadAllLines(path).Length; // ヘッダ含む行数
            runIndex = Mathf.Max(0, lineCount - 1);        // データ行数
        }

        string line = string.Format(
            "{0},{1},{2},{3},{4}\n",
            runIndex,
            player.stepsWalked,
            player.uniqueNodesVisited,
            player.deadEndEnterCount,
            player.frameToGoal
        );

        File.AppendAllText(path, line);

        Debug.Log($"[RestartManager] Player.csv 追記 → {path}");
    }

    // -------------------------------------------------------
    // ★ CSVに UnknownQuantity の実験データを書き出す
    // -------------------------------------------------------
    private void RecordEvaluation()
    {
        Debug.Log("[RestartManager] RecordEvaluation START");

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
        //int heavyFrameCount = 0;
        //float avgFrame = 0f;
        //float worstFrame = 0f;
        //int heavyFrameCount = Mathf.RoundToInt(UnknownQuantity.AlgTotalMs);
        //float avgFrame = UnknownQuantity.AlgFrameCount > 0
        //    ? UnknownQuantity.AlgTotalMs / UnknownQuantity.AlgFrameCount
        //    : 0f;
        //float worstFrame = UnknownQuantity.AlgMaxMs;
        int totalProcessMs = Mathf.RoundToInt(UnknownQuantity.AlgTotalMs);
        float avgProcessMs = UnknownQuantity.AlgFrameCount > 0
            ? UnknownQuantity.AlgTotalMs / UnknownQuantity.AlgFrameCount
            : 0f;
        float maxProcessMs = UnknownQuantity.AlgMaxMs;


        // ★ EvaluationLogger に記録
        EvaluationLogger.Record(
            scriptName,
            nodesCreated,
            shortestDist,
            timeToGoal,
            totalNodeVisits,
            totalProcessMs,
            avgProcessMs,
            maxProcessMs
        //heavyFrameCount,
        //avgFrame,
        //worstFrame
        );

        Debug.Log("[RestartManager] RecordEvaluation END");
        //Debug.Log("=== CSV 出力完了 ===");
    }

    private IEnumerator RestartFlow()
    {
        Debug.Log("=== ★ 最短経路確定：CSV記録 & 再実行準備中 ★ ===");

        // ① 古いプレイヤーをすべて削除（ここが最重要）
        var players = FindObjectsOfType<UnknownQuantity>();
        foreach (var p in players)
            Destroy(p.gameObject);
        // ★ CellFromStart プレイヤーも全部消す
        var cfsPlayers = FindObjectsOfType<CellFromStart>();
        foreach (var p in cfsPlayers)
            Destroy(p.gameObject);

        // ② 旧プレイヤーの Update が走らないよう 1フレーム待つ
        yield return null;

        // ★ 何回目のRunかをインクリメント
        runIndex++;

        // ★ CFS用 Player.csv を出力
        //WriteCellFromStartPlayerCsv();

        // ③ CSV出力
        //RecordEvaluation();
        yield return new WaitForSeconds(1f);

        // ④ Nodeデータクリア
        MapNode.ClearAllNodes();

        UnknownQuantity.ResetAlgorithmMetrics();

        UnknownQuantity.shortestModeArrivalCount = 0;

        // フラグリセット ←★ これが無いと2回目以降動かない
        isRestarting = false;
        hasRestarted = false;
        UnknownQuantity.hasLearnedGoal = false;

        // TImeToGoalを毎回リセット
        runStartTime = Time.time;

        // ⑤ シーン再読み込み
        string sceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(sceneName);
    }

}