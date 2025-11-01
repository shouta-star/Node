using System.Linq;
using UnityEngine;
using System.Collections;

/// <summary>
/// - 全プレイヤーが同位置にスポーンし、それぞれNodeを生成する。
/// - 通常は既存ルートの中でvalueが高い方向に進む。
/// - 一定確率で新しい方向（未探索方向）にNodeを生成して進む。
/// - 来た道には基本的に戻らない。
/// - 新しいルートを開拓した場合は、以後のプレイヤーにも共有される。
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("現在の位置")]
    public MapNode currentNode;
    public MapNode goalNode;

    [Header("移動設定")]
    public float moveSpeed = 3f;
    public float waitTime = 0.3f;
    public float explorationRate = 0.2f;     // 既存ルート内でのランダム探索確率
    public float newRouteChance = 0.1f;      // 新しい方向へ進む確率（0〜1）

    [Header("学習設定")]
    public float baseReward = 1f;
    public float decayRate = 0.9f;
    public float updateRate = 0.5f;

    [Header("Node設定")]
    public GameObject nodePrefab;
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;
    public LayerMask wallLayer;
    public LayerMask nodeLayer;

    private bool isMoving = false;
    private MapNode previousNode = null;

    private IEnumerator Start()
    {
        // 1フレーム遅らせて他プレイヤーのNode生成を反映
        yield return null;

        Vector3 snappedPos = SnapToGrid(transform.position);

        // 自分専用のNodeを生成
        GameObject nodeObj = Instantiate(nodePrefab, snappedPos, Quaternion.identity);
        currentNode = nodeObj.GetComponent<MapNode>();

        // 同座標Nodeとリンク共有
        LinkWithExistingNodes(currentNode);

        // 隣接関係を再構築
        currentNode.FindNeighbors();
    }

    private void Update()
    {
        if (!isMoving && currentNode != null)
            StartCoroutine(MoveNextNode());
    }

    private IEnumerator MoveNextNode()
    {
        isMoving = true;

        // 新ルートを開拓するか確率判定
        bool createNewRoute = Random.value < newRouteChance;

        if (createNewRoute)
        {
            // 新しい方向へ Node を生成して進む
            TryCreateNewRoute();
        }
        else if (currentNode.links.Count > 0)
        {
            // 通常探索（既知ルート）
            var candidates = currentNode.links.Where(n => n != previousNode).ToList();
            if (candidates.Count == 0)
                candidates = currentNode.links.ToList();

            MapNode nextNode = candidates.OrderByDescending(n => n.value).First();

            if (Random.value < explorationRate)
                nextNode = candidates[Random.Range(0, candidates.Count)];

            yield return StartCoroutine(MoveTo(nextNode.transform.position));

            previousNode = currentNode;
            currentNode = nextNode;

            UpdateNodeValue(currentNode);
        }

        yield return new WaitForSeconds(waitTime);
        isMoving = false;
    }

    /// <summary>
    /// 新しい方向にNodeを生成してルートを拡張する
    /// </summary>
    private void TryCreateNewRoute()
    {
        // 上下左右の方向ベクトル
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        foreach (var dir in directions.OrderBy(_ => Random.value))
        {
            Vector3 origin = currentNode.transform.position + Vector3.up * 0.05f;
            float distance = cellSize;

            // 前方に壁や既存Nodeがないか確認
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, distance, wallLayer | nodeLayer))
            {
                // 新しいNodeを生成
                Vector3 newPos = currentNode.transform.position + dir * cellSize;
                GameObject newNodeObj = Instantiate(nodePrefab, newPos, Quaternion.identity);
                MapNode newNode = newNodeObj.GetComponent<MapNode>();

                // 双方向リンク
                currentNode.links.Add(newNode);
                newNode.links.Add(currentNode);

                // 進行
                StartCoroutine(MoveTo(newNode.transform.position));

                previousNode = currentNode;
                currentNode = newNode;

                UpdateNodeValue(currentNode);
                Debug.Log($"{gameObject.name} created new route: {dir}");
                return;
            }
        }

        // すべて塞がっていた場合 → 通常探索に戻る
        Debug.Log($"{gameObject.name} could not create new route (blocked)");
    }

    private IEnumerator MoveTo(Vector3 target)
    {
        while (Vector3.Distance(transform.position, target) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = target;
    }

    private void UpdateNodeValue(MapNode node)
    {
        if (goalNode == null) return;

        float dist = Vector3.Distance(node.transform.position, goalNode.transform.position);
        float reward = baseReward / (dist + 1f);
        float newValue = Mathf.Lerp(node.value, node.value + reward, updateRate);
        node.value = newValue * decayRate;
    }

    private void LinkWithExistingNodes(MapNode selfNode)
    {
        MapNode[] allNodes = FindObjectsOfType<MapNode>();
        foreach (var node in allNodes)
        {
            if (node == selfNode) continue;
            if (Vector3.Distance(node.transform.position, selfNode.transform.position) < 0.05f)
            {
                if (!selfNode.links.Contains(node))
                    selfNode.links.Add(node);
                if (!node.links.Contains(selfNode))
                    node.links.Add(selfNode);
            }
        }
    }

    private Vector3 SnapToGrid(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / cellSize);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / cellSize);
        return new Vector3(x * cellSize, 0f, z * cellSize) + gridOrigin;
    }
}
