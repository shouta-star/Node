using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FrontierRestartManager : MonoBehaviour
{
    public static FrontierRestartManager Instance = null;

    [Header("CSV")]
    public string outputSubFolder = "CSV/Frontier"; // �v���W�F�N�g��������̑���
    public string filePrefix = "Frontier";

    [Header("Restart")]
    public bool destroyPlayersBeforeReload = true;

    private bool isRestarting = false;
    private bool hasRestarted = false;

    // ���u���݂�Run�ԍ��v(1�n�܂�)
    private int runIndex = 1;

    [Header("Periodic Snapshot (Color + Screenshot)")]
    [SerializeField] private bool enablePeriodicSnapshots = true;
    [SerializeField] private float snapshotIntervalSec = 1.0f;

    private float nextSnapshotTime = 0f;
    private int snapshotSeq = 0;
    private bool snapshotInProgress = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[FRM][AWAKE] Instance set");
    }

    private void Start()
    {
        FrontierEvaluationLogger.EnsureBaseDir(GetBaseDir());
        FrontierEvaluationLogger.ResetNodeVisitLog(filePrefix, runIndex);

        // ★ 追加：CellFromStart と同じ「一定間隔スナップショット」初期化
        snapshotSeq = 0;
        snapshotInProgress = false;
        nextSnapshotTime = Time.time + Mathf.Max(0.1f, snapshotIntervalSec);
    }

    private void Update()
    {
        if (!enablePeriodicSnapshots) return;
        if (isRestarting) return;              // リスタート中は撮らない
        if (snapshotInProgress) return;        // 多重起動防止

        if (Time.time >= nextSnapshotTime)
        {
            nextSnapshotTime = Time.time + Mathf.Max(0.1f, snapshotIntervalSec);
            StartCoroutine(CaptureSnapshotEndOfFrame());
        }
    }

    public int GetRunIndex() => runIndex;

    public void StartRestart(FrontierExplorer player)
    {
        Debug.Log($"[FRM][StartRestart] called isRestarting={isRestarting} hasRestarted={hasRestarted} runIndex={runIndex}");

        if (isRestarting || hasRestarted)
        {
            Debug.LogWarning("[FRM][StartRestart] rejected by flags");
            return;
        }

        // ★ ここでフラグを立てて二重起動を防ぐ
        isRestarting = true;
        hasRestarted = true;

        Debug.Log("[FrontierRestartManager] StartRestart called.");
        StartCoroutine(RestartFlow(player));
    }


    //private IEnumerator RestartFlow(FrontierExplorer player)
    //{
    //    yield return null; // Goal到達フレームの処理が落ち着くのを待つ

    //    int finishedRun = runIndex;

    //    try
    //    {
    //        // A) 色確定（スクショ用）
    //        FrontierNode.ApplyColorsOnceBeforeScreenshot();

    //        // B) CSV 出力（ここが例外で落ちると Reload されないので try/catch）
    //        try { WriteFrontierNodeCsv(finishedRun); }
    //        catch (Exception ex) { Debug.LogError($"[FrontierRestartManager] WriteFrontierNodeCsv failed: {ex}"); }

    //        try { WriteRunSummaryCsv(finishedRun, player); }
    //        catch (Exception ex) { Debug.LogError($"[FrontierRestartManager] WriteRunSummaryCsv failed: {ex}"); }

    //        // C) 次 Run へ（Visitログ切替）
    //        runIndex++;
    //        try { FrontierEvaluationLogger.ResetNodeVisitLog(filePrefix, runIndex); }
    //        catch (Exception ex) { Debug.LogError($"[FrontierRestartManager] ResetNodeVisitLog failed: {ex}"); }
    //    }
    //    finally
    //    {
    //        // D) Player 削除（任意）
    //        if (destroyPlayersBeforeReload)
    //        {
    //            var players = FindObjectsOfType<FrontierExplorer>();
    //            foreach (var p in players) Destroy(p.gameObject);
    //        }

    //        yield return null;

    //        // E) Node クリア
    //        FrontierNode.ClearAllNodes();

    //        // F) フラグ解除
    //        isRestarting = false;
    //        hasRestarted = false;

    //        // G) シーンリロード
    //        string sceneName = SceneManager.GetActiveScene().name;
    //        Debug.Log($"[FrontierRestartManager] Reload scene: {sceneName}");
    //        SceneManager.LoadScene(sceneName);
    //    }
    //}
    //private IEnumerator RestartFlow(FrontierExplorer player)
    //{
    //    // Goal到達フレームの処理が落ち着くのを待つ
    //    yield return null;

    //    int finishedRun = runIndex;

    //    // A) 色確定（スクショ用）
    //    try { FrontierNode.ApplyColorsOnceBeforeScreenshot(); }
    //    catch (Exception ex) { Debug.LogError($"[FrontierRestartManager] ApplyColors failed: {ex}"); }

    //    // B) CSV 出力（失敗してもリロードは止めない）
    //    try { WriteFrontierNodeCsv(finishedRun); }
    //    catch (Exception ex) { Debug.LogError($"[FrontierRestartManager] WriteFrontierNodeCsv failed: {ex}"); }

    //    try { WriteRunSummaryCsv(finishedRun, player); }
    //    catch (Exception ex) { Debug.LogError($"[FrontierRestartManager] WriteRunSummaryCsv failed: {ex}"); }

    //    // C) 次 Run へ（Visitログ切替）
    //    runIndex++;
    //    try { FrontierEvaluationLogger.ResetNodeVisitLog(filePrefix, runIndex); }
    //    catch (Exception ex) { Debug.LogError($"[FrontierRestartManager] ResetNodeVisitLog failed: {ex}"); }

    //    // D) Player 削除（任意）
    //    if (destroyPlayersBeforeReload)
    //    {
    //        var players = FindObjectsOfType<FrontierExplorer>();
    //        foreach (var p in players) Destroy(p.gameObject);
    //    }

    //    yield return null;

    //    // E) Node クリア
    //    try { FrontierNode.ClearAllNodes(); }
    //    catch (Exception ex) { Debug.LogError($"[FrontierRestartManager] ClearAllNodes failed: {ex}"); }

    //    // F) フラグ解除
    //    isRestarting = false;
    //    hasRestarted = false;

    //    // G) シーンリロード
    //    string sceneName = SceneManager.GetActiveScene().name;
    //    Debug.Log($"[FrontierRestartManager] Reload scene: {sceneName}");
    //    SceneManager.LoadScene(sceneName);
    //}
    private IEnumerator RestartFlow(FrontierExplorer player)
    {
        Debug.Log($"[FRM][Flow] BEGIN runIndex={runIndex} player={(player ? player.name : "null")}");

        // Goal到達フレームの処理が落ち着くのを待つ
        yield return null;
        Debug.Log($"[FRM][Flow] after first yield frame={Time.frameCount}");

        int finishedRun = runIndex;
        Debug.Log($"[FRM][Flow] finishedRun={finishedRun}");

        // A) 色確定（スクショ用）
        Debug.Log("[FRM][Flow][A] ApplyColors start");
        try
        {
            FrontierNode.ApplyColorsOnceBeforeScreenshot();
            Debug.Log("[FRM][Flow][A] ApplyColors ok");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FRM][Flow][A] ApplyColors failed: {ex}");
        }

        // ★ ここを追加：描画完了後にスクショ
        yield return new WaitForEndOfFrame();
        try
        {
            FrontierEvaluationLogger.CaptureRunScreenshot($"S{finishedRun:000}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FRM][Flow] CaptureRunScreenshot failed: {ex}");
        }

        // B) CSV 出力（失敗してもリロードは止めない）
        Debug.Log("[FRM][Flow][B1] WriteFrontierNodeCsv start");
        try
        {
            WriteFrontierNodeCsv(finishedRun);
            Debug.Log("[FRM][Flow][B1] WriteFrontierNodeCsv ok");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FRM][Flow][B1] WriteFrontierNodeCsv failed: {ex}");
        }

        Debug.Log("[FRM][Flow][B2] WriteRunSummaryCsv start");
        try
        {
            WriteRunSummaryCsv(finishedRun, player);
            Debug.Log("[FRM][Flow][B2] WriteRunSummaryCsv ok");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FRM][Flow][B2] WriteRunSummaryCsv failed: {ex}");
        }

        FrontierEvaluationLogger.LogSummary(
            finishedRun,
            unknownSelectMode: "Frontier",
            targetUpdateMode: "Frontier",
            unknownReferenceDepth: 0,
            nodesCreated: (FrontierNode.All != null ? FrontierNode.All.Count : 0),
            shortestPathLen: -1,
            timeToGoal: -1f,
            totalNodeVisits: FrontierNode.totalPassCount,
            totalProcessMs: 0,
            avgProcessMs: 0f,
            maxProcessMs: 0f
        );

        // C) 次 Run へ（Visitログ切替）
        Debug.Log($"[FRM][Flow][C] increment runIndex {runIndex} -> {runIndex + 1}");
        runIndex++;

        Debug.Log("[FRM][Flow][C] ResetNodeVisitLog start");
        try
        {
            FrontierEvaluationLogger.ResetNodeVisitLog(filePrefix, runIndex);
            Debug.Log("[FRM][Flow][C] ResetNodeVisitLog ok");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FRM][Flow][C] ResetNodeVisitLog failed: {ex}");
        }

        // ★★★ ここに追加：次Runのスナップショット連番をリセット ★★★
        snapshotSeq = 0;
        snapshotInProgress = false;
        nextSnapshotTime = Time.time + Mathf.Max(0.1f, snapshotIntervalSec);

        // D) Player 削除（任意）
        Debug.Log($"[FRM][Flow][D] destroyPlayersBeforeReload={destroyPlayersBeforeReload}");
        if (destroyPlayersBeforeReload)
        {
            var players = FindObjectsOfType<FrontierExplorer>();
            Debug.Log($"[FRM][Flow][D] found players={players.Length}");
            foreach (var p in players)
            {
                if (p == null) continue;
                Debug.Log($"[FRM][Flow][D] Destroy player={p.name}");
                Destroy(p.gameObject);
            }
        }

        yield return null;
        Debug.Log($"[FRM][Flow] after second yield frame={Time.frameCount}");

        // E) Node クリア
        Debug.Log("[FRM][Flow][E] ClearAllNodes start");
        try
        {
            FrontierNode.ClearAllNodes();
            Debug.Log("[FRM][Flow][E] ClearAllNodes ok");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FRM][Flow][E] ClearAllNodes failed: {ex}");
        }

        // F) フラグ解除
        Debug.Log($"[FRM][Flow][F] flags before isRestarting={isRestarting} hasRestarted={hasRestarted}");
        isRestarting = false;
        hasRestarted = false;
        Debug.Log($"[FRM][Flow][F] flags after isRestarting={isRestarting} hasRestarted={hasRestarted}");

        // G) シーンリロード
        string sceneName = SceneManager.GetActiveScene().name;
        Debug.Log($"[FRM][Flow][G] Reload scene: {sceneName} frame={Time.frameCount}");
        SceneManager.LoadScene(sceneName);
    }



    private string GetBaseDir()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string dir = Path.Combine(projectRoot, outputSubFolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void WriteFrontierNodeCsv(int runIdx)
    {
        string baseDir = GetBaseDir();
        string path = Path.Combine(baseDir, $"{filePrefix}_Node.csv");

        if (!File.Exists(path))
        {
            File.AppendAllText(path,
                "RunIndex,NodeName,CellX,CellY,PosX,PosZ,UnknownCount,WallCount,LinkCount,PassCount,IsDeadEnd,IsFrontier\n");
        }

        var sb = new StringBuilder(4096);

        foreach (var n in FrontierNode.All)
        {
            if (n == null) continue;

            bool isDeadEnd = (n.links != null && n.links.Count == 1);
            bool isFrontier = (n.unknownCount > 0);

            sb.Append(runIdx).Append(',')
              .Append(n.name).Append(',')
              .Append(n.cell.x).Append(',')
              .Append(n.cell.y).Append(',')
              .Append(n.transform.position.x.ToString("F3")).Append(',')
              .Append(n.transform.position.z.ToString("F3")).Append(',')
              .Append(n.unknownCount).Append(',')
              .Append(n.wallCount).Append(',')
              .Append(n.links != null ? n.links.Count : 0).Append(',')
              .Append(n.passCount).Append(',')
              .Append(isDeadEnd ? 1 : 0).Append(',')
              .Append(isFrontier ? 1 : 0).Append('\n');
        }

        File.AppendAllText(path, sb.ToString());
    }

    private void WriteRunSummaryCsv(int runIdx, FrontierExplorer player)
    {
        string baseDir = GetBaseDir();
        string path = Path.Combine(baseDir, $"{filePrefix}_RunSummary.csv");

        if (!File.Exists(path))
        {
            File.AppendAllText(path,
                "RunIndex,Timestamp,Scene,StepsToGoal,ElapsedSec,TotalNodes,TotalNodeVisits,NewNodesPlaced,VisitLogPath\n");
        }

        string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string sceneName = SceneManager.GetActiveScene().name;

        int steps = player != null ? player.StepIndex : -1;
        float elapsed = player != null ? player.ElapsedTime : -1f;
        int totalNodes = FrontierNode.All != null ? FrontierNode.All.Count : 0;
        int totalVisits = FrontierNode.totalPassCount;
        int newNodes = player != null ? player.NewNodesPlaced : -1;

        string visitPath = FrontierEvaluationLogger.GetCurrentVisitLogPath() ?? "";

        string line =
            $"{runIdx},{ts},{sceneName},{steps},{elapsed:F3},{totalNodes},{totalVisits},{newNodes},\"{visitPath}\"\n";

        File.AppendAllText(path, line);
    }

    private IEnumerator CaptureSnapshotEndOfFrame()
    {
        snapshotInProgress = true;

        try
        {
            // 1) 色更新
            try
            {
                FrontierNode.ApplyColorsOnceBeforeScreenshot();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FRM][SNAP] ApplyColors failed: {ex}");
            }

            // 2) 描画完了待ち
            yield return new WaitForEndOfFrame();

            // 3) スクショ（同一Run内で上書きされないよう連番）
            string tag = $"S{snapshotSeq:000}";
            snapshotSeq++;

            try
            {
                FrontierEvaluationLogger.CaptureRunScreenshot(tag);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FRM][SNAP] CaptureRunScreenshot failed: {ex}");
            }
        }
        finally
        {
            snapshotInProgress = false;
        }
    }

}
