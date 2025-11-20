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

    bool alreadyTriggered = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        //DontDestroyOnLoad(this.gameObject);
    }

    private void Update()
    {
        if (alreadyTriggered) return;

        if (IsShortestConfirmed) return;

        // ★ UnknownQuantity 専用：unknownCount だけを見る
        if (CheckAllUnknownZero())
        {
            alreadyTriggered = true;
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
            {
                //Debug.Log($"[DEBUG-U] Node {node.name} U={node.unknownCount} → 最短確定ではない");
                return false;
            }
        }

        Debug.Log("[DEBUG-U] ★ 全ノード U=0 判定通過 ★");
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