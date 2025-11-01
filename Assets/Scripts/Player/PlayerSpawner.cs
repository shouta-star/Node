using UnityEngine;
using System.Collections;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab; // ��������v���C���[��Prefab
    [SerializeField] private Transform spawnPoint;    // �����ʒu�i�w�肵�Ȃ����(0,0,0)�j
    [SerializeField] private float spawnInterval = 5f; // 5�b����
    [SerializeField] private int spawnCount = 10;      // ��x��10��
    [SerializeField] private bool loop = true;         // �����ɌJ��Ԃ���

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
