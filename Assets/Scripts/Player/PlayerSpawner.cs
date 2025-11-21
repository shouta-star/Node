using UnityEngine;
using System.Collections;

public class PlayerSpawner : MonoBehaviour
{
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
        // š Collider‚ª‚·‚×‚Ä“o˜^‚³‚ê‚é‚Ü‚Å10ƒtƒŒ[ƒ€‘Ò‚Â
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

                // š Instantiate ‚Ì–ß‚è’l‚ğó‚¯æ‚é
                GameObject obj = Instantiate(playerPrefab, pos, Quaternion.identity);

                // š UnknownQuantity ‚ğæ“¾
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
}