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

    //private IEnumerator SpawnPlayers()
    //{
    //    do
    //    {
    //        // 基準スポーン位置（spawnPointがあればそれ、無ければ従来の固定座標）
    //        Vector3 basePos = (spawnPoint != null)
    //            ? spawnPoint.position
    //            : new Vector3(19f, 0f, 10f);

    //        // 上/下/左/右
    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

    //        // 4体に固定（spawnCountはInspectorで4にしておくのが基本）
    //        int count = Mathf.Min(spawnCount, 4);

    //        for (int i = 0; i < count; i++)
    //        {
    //            Vector3 dir = dirs[i];

    //            // いったん基準位置で生成（Startが走る前に位置/方向を設定する）
    //            GameObject obj = Instantiate(playerPrefab, basePos, Quaternion.LookRotation(dir, Vector3.up));

    //            var cfs = obj.GetComponent<CellFromStart>();
    //            float cell = (cfs != null) ? cfs.cellSize : 1f;

    //            // 重なり防止：基準位置から1マスずらす（上/下/左/右に配置）
    //            obj.transform.position = basePos + dir * cell;
    //            obj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

    //            if (cfs != null)
    //            {
    //                // ★ここが本命：初期進行方向を個体ごとに設定
    //                cfs.startDirection = dir;

    //                // 既存のモード設定はそのまま
    //                if (unknownSelectModeOptions != null && unknownSelectModeOptions.Count > 0)
    //                    cfs.unknownSelectMode = ChooseUnknownSelectMode();

    //                if (targetUpdateModeOptions != null && targetUpdateModeOptions.Count > 0)
    //                    cfs.targetUpdateMode = ChooseTargetUpdateMode();
    //            }
    //        }

    //        yield return new WaitForSeconds(spawnInterval);

    //    } while (loop);
    //}
    //private IEnumerator SpawnPlayers()
    //{
    //    do
    //    {
    //        // 基準スポーン位置（spawnPoint があればそれ、無ければ従来の固定座標）
    //        Vector3 basePos = (spawnPoint != null)
    //            ? spawnPoint.position
    //            : new Vector3(19f, 0f, 10f);

    //        // 上/下/左/右（4方向を使い回す）
    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

    //        // 4体制限を撤廃：spawnCount の数だけ出す
    //        int count = Mathf.Max(0, spawnCount);

    //        for (int i = 0; i < count; i++)
    //        {
    //            // 方向は4方向を循環
    //            Vector3 dir = dirs[i % dirs.Length];

    //            // いったん基準位置で生成（Start が走る前に位置/方向を設定する）
    //            GameObject obj = Instantiate(playerPrefab, basePos, Quaternion.LookRotation(dir, Vector3.up));

    //            var cfs = obj.GetComponent<CellFromStart>();
    //            float cell = (cfs != null) ? cfs.cellSize : 1f;

    //            // 5体目以降は外側へ（1マス/2マス/3マス…）
    //            // 0-3 => ring=1, 4-7 => ring=2, 8-11 => ring=3...
    //            int ring = (i / dirs.Length) + 1;

    //            // 配置
    //            obj.transform.position = basePos + dir * cell * ring;
    //            obj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

    //            if (cfs != null)
    //            {
    //                // 初期進行方向を個体ごとに設定
    //                cfs.startDirection = dir;

    //                // 既存のモード設定はそのまま
    //                if (unknownSelectModeOptions != null && unknownSelectModeOptions.Count > 0)
    //                    cfs.unknownSelectMode = ChooseUnknownSelectMode();

    //                if (targetUpdateModeOptions != null && targetUpdateModeOptions.Count > 0)
    //                    cfs.targetUpdateMode = ChooseTargetUpdateMode();
    //            }
    //        }

    //        yield return new WaitForSeconds(spawnInterval);

    //    } while (loop);
    //}
    //private IEnumerator SpawnPlayers()
    //{
    //    do
    //    {
    //        // 基準スポーン位置（spawnPoint があればそれ、無ければ固定座標）
    //        Vector3 basePos = (spawnPoint != null)
    //            ? spawnPoint.position
    //            : new Vector3(19f, 0f, 10f);

    //        // 上/下/左/右（方向は使い回す）
    //        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

    //        // spawnCount の数だけ出す（5体以上OK）
    //        int count = Mathf.Max(0, spawnCount);

    //        for (int i = 0; i < count; i++)
    //        {
    //            Vector3 dir = dirs[i % dirs.Length];

    //            // 生成（位置は全員 basePos 固定）
    //            GameObject obj = Instantiate(playerPrefab, basePos, Quaternion.LookRotation(dir, Vector3.up));

    //            // 念のため明示的に固定（Prefab側でズレてても潰す）
    //            obj.transform.position = basePos;
    //            obj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

    //            var cfs = obj.GetComponent<CellFromStart>();
    //            if (cfs != null)
    //            {
    //                // 初期進行方向だけ個体ごとに設定
    //                cfs.startDirection = dir;

    //                // 既存のモード設定はそのまま
    //                if (unknownSelectModeOptions != null && unknownSelectModeOptions.Count > 0)
    //                    cfs.unknownSelectMode = ChooseUnknownSelectMode();

    //                if (targetUpdateModeOptions != null && targetUpdateModeOptions.Count > 0)
    //                    cfs.targetUpdateMode = ChooseTargetUpdateMode();
    //            }
    //        }

    //        yield return new WaitForSeconds(spawnInterval);

    //    } while (loop);
    //}
    private IEnumerator SpawnPlayers()
    {
        do
        {
            // 基準スポーン位置（spawnPoint があればそれ、無ければ固定座標）
            Vector3 basePos = (spawnPoint != null)
                ? spawnPoint.position
                : new Vector3(19f, 0f, 10f);

            // 上/下/左/右（方向は使い回す）
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

            // spawnCount の数だけ出す（5体以上OK）
            int count = Mathf.Max(0, spawnCount);

            for (int i = 0; i < count; i++)
            {
                Vector3 dir = dirs[i % dirs.Length];

                // ★ 1周(4体)ごとに距離を1マス増やす：1,1,1,1, 2,2,2,2, 3,3,3,3...
                int ring = (i / dirs.Length) + 1;

                // いったん基準位置で生成（Startが走る前に位置/方向を設定する）
                GameObject obj = Instantiate(playerPrefab, basePos, Quaternion.LookRotation(dir, Vector3.up));

                var cfs = obj.GetComponent<CellFromStart>();
                float cell = (cfs != null) ? cfs.cellSize : 1f;

                // ★ 上の関数に合わせる：基準位置から ring マスずらす
                obj.transform.position = basePos + dir * (cell * ring);
                obj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

                if (cfs != null)
                {
                    // 初期進行方向を個体ごとに設定
                    cfs.startDirection = dir;

                    // 既存のモード設定はそのまま
                    if (unknownSelectModeOptions != null && unknownSelectModeOptions.Count > 0)
                        cfs.unknownSelectMode = ChooseUnknownSelectMode();

                    if (targetUpdateModeOptions != null && targetUpdateModeOptions.Count > 0)
                        cfs.targetUpdateMode = ChooseTargetUpdateMode();
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