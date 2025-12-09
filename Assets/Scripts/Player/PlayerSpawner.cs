using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerSpawner : MonoBehaviour
{
    [System.Serializable]
    public class UnknownSelectModeOption
    {
        public CellFromStart.UnknownSelectMode mode; // Random / Nearest / ...
        public float weight = 1f;                    // 確率の重み
    }

    [System.Serializable]
    public class TargetUpdateModeOption
    {
        public CellFromStart.TargetUpdateMode mode;  // EveryNode / OnArrival
        public float weight = 1f;
    }

    [Header("探索方針①：UnknownSelectMode の候補")]
    public List<UnknownSelectModeOption> unknownSelectModeOptions;

    [Header("探索方針②：TargetUpdateMode の候補")]
    public List<TargetUpdateModeOption> targetUpdateModeOptions;

    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spawnInterval = 5f;
    [SerializeField] private int spawnCount = 1;
    [SerializeField] private bool loop = true;

    private void Start()
    {
        StartCoroutine(SpawnPlayersDelayed());
    }

    private IEnumerator SpawnPlayersDelayed()
    {
        // ★ Colliderがすべて登録されるまで10フレーム待つ
        for (int i = 0; i < 10; i++)
            yield return null;

        StartCoroutine(SpawnPlayers());
    }

    private IEnumerator SpawnPlayers()
    {
        do
        {
            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 pos = new Vector3(-10f, 0f, -5f);

                // ★ Instantiate の戻り値を受け取る
                GameObject obj = Instantiate(playerPrefab, pos, Quaternion.identity);

                // ★ CellFromStart を取得してモードを設定
                var cfs = obj.GetComponent<CellFromStart>();
                if (cfs != null)
                {
                    if (unknownSelectModeOptions != null && unknownSelectModeOptions.Count > 0)
                        cfs.unknownSelectMode = ChooseUnknownSelectMode();

                    if (targetUpdateModeOptions != null && targetUpdateModeOptions.Count > 0)
                        cfs.targetUpdateMode = ChooseTargetUpdateMode();
                }

                // ★ UnknownQuantity を取得
                UnknownQuantity uq = obj.GetComponent<UnknownQuantity>();
                Debug.Log($"[Spawner] Player spawned at {pos}, uq={uq}");

                if (uq != null)
                {
                    //Debug.Log($"[Spawner] uq.CurrentNode = {uq.CurrentNode}");
                    //Debug.Log($"[Spawner] MapNode.StartNode = {MapNode.StartNode}");
                }
            }

            yield return new WaitForSeconds(spawnInterval);

        } while (loop);
    }

    // ★ UnknownSelectMode を重み付きランダムで 1 つ選ぶ
    private CellFromStart.UnknownSelectMode ChooseUnknownSelectMode()
    {
        if (unknownSelectModeOptions == null || unknownSelectModeOptions.Count == 0)
        {
            // 候補が未設定なら CellFromStart 側のデフォルトに任せてもいいが、
            // ここでは一応 Farthest を返す
            return CellFromStart.UnknownSelectMode.Farthest;
        }

        float total = 0f;
        foreach (var opt in unknownSelectModeOptions)
            total += Mathf.Max(0f, opt.weight);

        if (total <= 0f)
            return unknownSelectModeOptions[0].mode;

        float r = Random.value * total;

        foreach (var opt in unknownSelectModeOptions)
        {
            float w = Mathf.Max(0f, opt.weight);
            r -= w;
            if (r <= 0f)
                return opt.mode;
        }

        return unknownSelectModeOptions[unknownSelectModeOptions.Count - 1].mode;
    }

    // ★ TargetUpdateMode を重み付きランダムで 1 つ選ぶ
    private CellFromStart.TargetUpdateMode ChooseTargetUpdateMode()
    {
        if (targetUpdateModeOptions == null || targetUpdateModeOptions.Count == 0)
        {
            return CellFromStart.TargetUpdateMode.EveryNode;
        }

        float total = 0f;
        foreach (var opt in targetUpdateModeOptions)
            total += Mathf.Max(0f, opt.weight);

        if (total <= 0f)
            return targetUpdateModeOptions[0].mode;

        float r = Random.value * total;

        foreach (var opt in targetUpdateModeOptions)
        {
            float w = Mathf.Max(0f, opt.weight);
            r -= w;
            if (r <= 0f)
                return opt.mode;
        }

        return targetUpdateModeOptions[targetUpdateModeOptions.Count - 1].mode;
    }

}