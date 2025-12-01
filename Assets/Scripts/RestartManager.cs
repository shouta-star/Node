using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.IO;
using System.Text;
using System.Globalization;

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

    public void WriteNodeCsv()
    {
        // ① 保存先フォルダ
        string baseDir = @"D:\GitHub\NodeGitHub\CSV";
        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        // ② いまの CellFromStart からモード名を拾って prefix 化
        var player = FindObjectOfType<CellFromStart>();
        string prefix = "CFS";
        if (player != null)
        {
            prefix = $"CFS_U{player.unknownSelectMode}_T{player.targetUpdateMode}";
        }

        string path = Path.Combine(baseDir, prefix + "_Node.csv");

        // ③ ヘッダ（ファイルが無いときだけ）
        if (!File.Exists(path))
        {
            File.AppendAllText(path,
                "RunIndex,NodeName,PosX,PosZ," +
                "DistanceFromStart,DistanceFromGoal," +
                "UnknownCount,WallCount,LinkCount,PassCount," +
                "IsDeadEnd,IsBranch\n");
        }

        // ④ RunIndex を決める
        //    既存ファイルの「最後の行」の RunIndex を見て +1 する
        int runIndex = 0;
        if (File.Exists(path))
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length > 1)
            {
                var last = lines[lines.Length - 1];
                var cols = last.Split(',');
                if (cols.Length > 0 && int.TryParse(cols[0], out var lastIndex))
                {
                    runIndex = lastIndex + 1;
                }
            }
        }

        // ⑤ すべての MapNode を列挙して1行ずつ書き出す
        var nodes = FindObjectsOfType<MapNode>();
        var sb = new StringBuilder();

        foreach (var n in nodes)
        {
            int linkCount = (n.links != null) ? n.links.Count : 0;
            bool isDeadEnd = (linkCount == 1);
            bool isBranch = (linkCount >= 3);

            sb.AppendFormat(
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}\n",
                runIndex,
                n.name,
                n.transform.position.x,
                n.transform.position.z,
                n.distanceFromStart,
                n.DistanceFromGoal,
                n.unknownCount,
                n.wallCount,
                linkCount,
                n.passCount,
                isDeadEnd ? 1 : 0,
                isBranch ? 1 : 0
            );
        }

        File.AppendAllText(path, sb.ToString());
        Debug.Log($"[RestartManager] Node.csv 追記 → {path}");
    }
    //private void WriteNodeCsv()
    //{
    //    // ★ 保存先フォルダ（Player.csv と同じ場所）
    //    string baseDir = @"D:\GitHub\NodeGitHub\CSV";
    //    if (!Directory.Exists(baseDir))
    //    {
    //        Directory.CreateDirectory(baseDir);
    //    }

    //    // ★ とりあえず固定ファイル名（必要になったら U/T も付けよう）
    //    string path = Path.Combine(baseDir, "CFS_Node.csv");

    //    // ★ ヘッダ行（ファイルが無いときだけ）
    //    if (!File.Exists(path))
    //    {
    //        File.AppendAllText(path,
    //            "RunIndex,NodeName,PosX,PosZ," +
    //            "DistanceFromStart,DistanceFromGoal," +
    //            "UnknownCount,WallCount,LinkCount,PassCount," +
    //            "IsDeadEnd,IsBranch\n");
    //    }

    //    // ★ RunIndex を決める（最後の行の RunIndex を見て +1）
    //    int runIndex = 0;
    //    var lines = File.ReadAllLines(path);
    //    if (lines.Length > 1)
    //    {
    //        int idx = lines.Length - 1;
    //        while (idx > 0 && string.IsNullOrWhiteSpace(lines[idx]))
    //            idx--;

    //        var last = lines[idx];
    //        var cols = last.Split(',');
    //        if (cols.Length > 0 && int.TryParse(cols[0], out var lastIndex))
    //        {
    //            runIndex = lastIndex + 1;
    //        }
    //    }

    //    // ★ すべての MapNode を列挙して、1行ずつ書き出す
    //    var nodes = FindObjectsOfType<MapNode>();
    //    var sb = new StringBuilder();

    //    foreach (var n in nodes)
    //    {
    //        int linkCount = (n.links != null) ? n.links.Count : 0;
    //        bool isDeadEnd = (linkCount == 1);
    //        bool isBranch = (linkCount >= 3);

    //        // ★ まだ MapNode に distanceFromGoal / passCount を実装していないので仮値
    //        int distanceFromGoal = 0; // TODO: MapNode に実装したら n.distanceFromGoal に差し替え
    //        int passCount = 0; // TODO: MapNode に実装したら n.passCount に差し替え

    //        sb.AppendFormat(
    //            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}\n",
    //            runIndex,
    //            n.name,
    //            n.transform.position.x,
    //            n.transform.position.z,
    //            n.distanceFromStart, // ★ ここは今も MapNode にある想定
    //            distanceFromGoal,
    //            n.unknownCount,
    //            n.wallCount,
    //            linkCount,
    //            passCount,
    //            isDeadEnd ? 1 : 0,
    //            isBranch ? 1 : 0
    //        );
    //    }

    //    File.AppendAllText(path, sb.ToString());
    //    Debug.Log($"[RestartManager] Node.csv 追記 → {path}");
    //}

    public void WriteRunSummaryCsv(CellFromStart player)
    {
        if (player == null)
        {
            Debug.LogWarning("[RestartManager] CellFromStart が null なので RunSummary.csv は出力しません。");
            return;
        }

        // ★ 保存先フォルダ（Player.csv と同じ場所）
        string baseDir = @"D:\GitHub\NodeGitHub\CSV";
        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        // ★ ファイル名（モード別に分ける）
        //   例: CFS_UFarthest_TEveryNode_RunSummary.csv
        string prefix = $"CFS_U{player.unknownSelectMode}_T{player.targetUpdateMode}";
        string path = Path.Combine(baseDir, prefix + "_RunSummary.csv");

        // ★ ヘッダ行（ファイルが無いときだけ書く）
        if (!File.Exists(path))
        {
            File.AppendAllText(path,
                "RunIndex,FrameToGoal,StepsWalked," +
                "TotalNodeCount,DeadEndNodeCount,BranchNodeCount," +
                "AvgPassCount,MaxPassCount,EndReason\n");
        }

        // ★ RunIndex を決める（既存行数から決定）
        int runIndex = 0;
        if (File.Exists(path))
        {
            int lineCount = File.ReadAllLines(path).Length; // ヘッダ込み
            runIndex = Mathf.Max(0, lineCount - 1);         // データ行数 = RunIndex
        }

        // ★ Node 情報を集計
        var nodes = FindObjectsOfType<MapNode>();
        int totalNodeCount = nodes.Length;
        int deadEndCount = 0;
        int branchCount = 0;
        int passSum = 0;
        int maxPass = 0;

        foreach (var n in nodes)
        {
            int linkCount = (n.links != null) ? n.links.Count : 0;
            if (linkCount == 1) deadEndCount++;
            if (linkCount >= 3) branchCount++;

            int pc = n.passCount;
            passSum += pc;
            if (pc > maxPass) maxPass = pc;
        }

        float avgPass = (totalNodeCount > 0) ? (float)passSum / totalNodeCount : 0f;

        // ★ 終了理由（今は Goal 到達でしか呼ばない想定）
        string endReason = "GoalReached";

        // ★ 1 行分を組み立てて追記
        string line = string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5},{6},{7},{8}\n",
            runIndex,
            player.frameToGoal,
            player.stepsWalked,
            totalNodeCount,
            deadEndCount,
            branchCount,
            avgPass,
            maxPass,
            endReason
        );

        File.AppendAllText(path, line);

        Debug.Log($"[RestartManager] RunSummary.csv 追記 → {path}");
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

        WriteNodeCsv();

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

        // ▼ 次のRun用に Node到達ログCSV を切り替える（ここを追加）
        EvaluationLogger.ResetNodeVisitLog();

        // ⑤ シーン再読み込み
        string sceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(sceneName);
    }

}