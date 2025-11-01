using System.Linq;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("現在の位置")]
    public MapNode currentNode;

    [Header("移動設定")]
    public float moveSpeed = 3f;
    public float waitTime = 0.3f;      // Node間の移動間隔
    public float explorationRate = 0.2f; // ランダム探索確率(20%)

    private bool isMoving = false;

    void Update()
    {
        if (!isMoving && currentNode != null)
        {
            StartCoroutine(MoveNextNode());
        }
    }

    private System.Collections.IEnumerator MoveNextNode()
    {
        isMoving = true;

        if (currentNode.links.Count > 0)
        {
            // 1 候補Nodeの中で最もValueが高いものを取得
            MapNode nextNode = currentNode.links
                .OrderByDescending(n => n.value)
                .First();

            // 2 確率的にランダム移動(索性)
            if (Random.value < explorationRate)
            {
                nextNode = currentNode.links[Random.Range(0, currentNode.links.Count)];
            }

            // 3 移動
            yield return StartCoroutine(MoveTo(nextNode.transform.position));

            // 4 現在位置更新
            currentNode = nextNode;
        }

        // 5 少し待機して次の移動へ
        yield return new WaitForSeconds(waitTime);
        isMoving = false;
    }

    private System.Collections.IEnumerator MoveTo(Vector3 target)
    {
        while (Vector3.Distance(transform.position, target) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = target;
    }
}
