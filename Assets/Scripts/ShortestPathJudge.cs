using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UnknownQuantity 専用：
/// 最短経路確定条件は「全 Node の unknownCount が 0 であること」のみ。
/// </summary>
public class ShortestPathJudge : MonoBehaviour
{
    public static ShortestPathJudge Instance { get; private set; }

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
    }

    private void Update()
    {
        if (IsShortestConfirmed) return;

        // ★ UnknownQuantity 専用：unknownCount だけを見る
        if (CheckAllUnknownZero())
        {
            IsShortestConfirmed = true;
            Debug.Log("=== ★ UnknownQuantity 最短経路確定（unknownCount 0）★ ===");
        }
    }

    // ---------------------------------------------------------
    // すべての Node の unknownCount == 0 ?
    // ---------------------------------------------------------
    private bool CheckAllUnknownZero()
    {
        foreach (var node in MapNode.allNodes)
        {
            if (node.unknownCount > 0)
                return false;
        }
        return true;
    }

    // ---------------------------------------------------------
    // Player からの呼び出し（UnknownQuantity では不要だが互換性維持）
    // ---------------------------------------------------------
    public void OnNodeAdded() { }
    public void OnGoalReached(int distance) { }

    // UnknownQuantity では距離は不要なので常に -1
    public int GetLastStableDistance() => -1;
}


//using System.Collections.Generic;
//using UnityEngine;

///// <summary>
///// 最短経路確定を判定する統括クラス。
///// 条件 A: 直近の Goal 到達距離が3回連続同じ
///// 条件 B: 2分間 Node 数の増加がない
///// A&B が true になったら "最短経路確定"
///// </summary>
//public class ShortestPathJudge : MonoBehaviour
//{
//    // Singleton
//    public static ShortestPathJudge Instance { get; private set; }

//    // ======== 設定値 ========
//    private const int DistanceStableCount = 3;
//    private const float NoNodeIncreaseTime = 3f; // 2分

//    // ======== 距離管理 ========
//    private List<int> lastDistances = new List<int>(); // 距離の履歴

//    // ======== Node増加監視 ========
//    private int lastNodeCount = 0;
//    private float lastNodeIncreaseTime = 0f;

//    // ======== 判定 ========
//    public bool IsShortestConfirmed { get; private set; } = false;

//    private void Awake()
//    {
//        if (Instance != null)
//        {
//            Destroy(this.gameObject);
//            return;
//        }

//        Instance = this;
//        DontDestroyOnLoad(this.gameObject);

//        lastNodeCount = MapNode.allNodes.Count;
//        lastNodeIncreaseTime = Time.time;
//    }

//    private void Update()
//    {
//        if (IsShortestConfirmed) return;

//        // A. 距離が安定したか？
//        bool distanceStable = CheckDistanceStable();

//        // B. Node が2分間増えていないか？
//        bool noNodeIncrease = CheckNodeIncreaseStopped();

//        // 両方成立 → 最短確定
//        if (distanceStable && noNodeIncrease)
//        {
//            IsShortestConfirmed = true;
//            Debug.Log("=== ★ 最短経路確定 ★ ===");
//        }
//    }

//    // ===========================================
//    // public API（どのスクリプトからでも呼べる）
//    // ===========================================

//    /// <summary>
//    /// Goal 到達時に距離を通知
//    /// </summary>
//    public void OnGoalReached(int distance)
//    {
//        if (IsShortestConfirmed) return;

//        lastDistances.Add(distance);
//        if (lastDistances.Count > DistanceStableCount)
//            lastDistances.RemoveAt(0);
//    }

//    /// <summary>
//    /// Node が新しく追加されたときに通知
//    /// </summary>
//    public void OnNodeAdded()
//    {
//        if (IsShortestConfirmed) return;

//        int currentCount = MapNode.allNodes.Count;

//        if (currentCount > lastNodeCount)
//        {
//            lastNodeCount = currentCount;
//            lastNodeIncreaseTime = Time.time;
//        }
//    }

//    // ===========================================
//    // 内部判定処理
//    // ===========================================

//    /// <summary>
//    /// 距離が3回連続同じか？
//    /// </summary>
//    private bool CheckDistanceStable()
//    {
//        if (lastDistances.Count < DistanceStableCount) return false;

//        int d0 = lastDistances[0];
//        for (int i = 1; i < lastDistances.Count; i++)
//        {
//            if (lastDistances[i] != d0)
//                return false;
//        }

//        return true;
//    }

//    /// <summary>
//    /// 2分間 Node が増えていないか？
//    /// </summary>
//    private bool CheckNodeIncreaseStopped()
//    {
//        return (Time.time - lastNodeIncreaseTime) >= NoNodeIncreaseTime;
//    }

//    /// <summary>
//    /// 直近の安定した距離（lastDistances の最後の値）を返す
//    /// 安定していない場合は -1 を返す
//    /// </summary>
//    public int GetLastStableDistance()
//    {
//        if (lastDistances.Count < DistanceStableCount)
//            return -1;

//        // 3回連続同じだった時だけ安定と認める
//        int d0 = lastDistances[0];
//        for (int i = 1; i < lastDistances.Count; i++)
//        {
//            if (lastDistances[i] != d0)
//                return -1;
//        }

//        return d0;  // 安定した距離を返す
//    }

//}
