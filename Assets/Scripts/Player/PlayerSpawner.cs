using UnityEngine;
using System.Collections;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab; // 生成するプレイヤーのPrefab
    [SerializeField] private Transform spawnPoint;    // 生成位置（指定しなければ(0,0,0)）
    [SerializeField] private float spawnInterval = 5f; // 5秒ごと
    [SerializeField] private int spawnCount = 10;      // 一度に10体
    [SerializeField] private bool loop = true;         // 無限に繰り返すか

    private void Start()
    {
        StartCoroutine(SpawnPlayers());
    }

    private IEnumerator SpawnPlayers()
    {
        do
        {
            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 pos = new Vector3(-10f, 0f, -5f);

                Instantiate(playerPrefab, pos, Quaternion.identity);
            }

            yield return new WaitForSeconds(spawnInterval);

        } while (loop);
    }
}
