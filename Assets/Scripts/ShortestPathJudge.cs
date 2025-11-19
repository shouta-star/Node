using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 最短経路確定を判定する統括クラス。
/// 条件 A: 直近の Goal 到達距離が3回連続同じ
/// 条件 B: 2分間 Node 数の増加がない
/// A&B が true になったら "最短経路確定"
/// </summary>
public class ShortestPathJudge : MonoBehaviour
{
    // Singleton
    public static ShortestPathJudge Instance { get; private set; }

    // ======== 設定値 ========
    private const int DistanceStableCount = 3;
    private const float NoNodeIncreaseTime = 120f; // 2分

    // ======== 距離管理 ========
    private List<int> lastDistances = new List<int>(); // 距離の履歴

    // ======== Node増加監視 ========
    private int lastNodeCount = 0;
    private float lastNodeIncreaseTime = 0f;

    // ======== 判定 ========
    public bool IsShortestConfirmed { get; private set; } = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);

        lastNodeCount = MapNode.allNodes.Count;
        lastNodeIncreaseTime = Time.time;
    }

    private void Update()
    {
        if (IsShortestConfirmed) return;

        // A. 距離が安定したか？
        bool distanceStable = CheckDistanceStable();

        // B. Node が2分間増えていないか？
        bool noNodeIncrease = CheckNodeIncreaseStopped();

        // 両方成立 → 最短確定
        if (distanceStable && noNodeIncrease)
        {
            IsShortestConfirmed = true;
            Debug.Log("=== ★ 最短経路確定 ★ ===");
        }
    }

    // ===========================================
    // public API（どのスクリプトからでも呼べる）
    // ===========================================

    /// <summary>
    /// Goal 到達時に距離を通知
    /// </summary>
    public void OnGoalReached(int distance)
    {
        if (IsShortestConfirmed) return;

        lastDistances.Add(distance);
        if (lastDistances.Count > DistanceStableCount)
            lastDistances.RemoveAt(0);
    }

    /// <summary>
    /// Node が新しく追加されたときに通知
    /// </summary>
    public void OnNodeAdded()
    {
        if (IsShortestConfirmed) return;

        int currentCount = MapNode.allNodes.Count;

        if (currentCount > lastNodeCount)
        {
            lastNodeCount = currentCount;
            lastNodeIncreaseTime = Time.time;
        }
    }

    // ===========================================
    // 内部判定処理
    // ===========================================

    /// <summary>
    /// 距離が3回連続同じか？
    /// </summary>
    private bool CheckDistanceStable()
    {
        if (lastDistances.Count < DistanceStableCount) return false;

        int d0 = lastDistances[0];
        for (int i = 1; i < lastDistances.Count; i++)
        {
            if (lastDistances[i] != d0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 2分間 Node が増えていないか？
    /// </summary>
    private bool CheckNodeIncreaseStopped()
    {
        return (Time.time - lastNodeIncreaseTime) >= NoNodeIncreaseTime;
    }
}
