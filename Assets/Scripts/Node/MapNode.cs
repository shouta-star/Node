using System.Collections.Generic;
using UnityEngine;

public class MapNode : MonoBehaviour
{
    // ============================================================
    // 静的共有データ
    // ============================================================
    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();

    [Header("リンク情報")]
    public List<MapNode> links = new List<MapNode>();

    [Header("Value設定")]
    public float value = 0f;
    public int visits = 0;
    public float alpha = 0.3f;  // 学習率
    public float gamma = 0.9f;  // 割引率
    public float rho = 0.02f;   // 蒸発率
    public float beta = 0.5f;   // 未探索ボーナス強度

    [Header("探索設定")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Header("動作設定")]
    public bool debugLog = false;

    // ============================================================
    // 起動時処理
    // ============================================================
    private void Start()
    {
        // 自身のセル座標を登録（重複防止）
        Vector2Int cell = WorldToCell(transform.position);
        allNodeCells.Add(cell);

        // 🔴 自動リンク・再探索系は削除（LinkWithNearbyNodes, FindNeighbors, PeriodicRecheck など）
    }

    // ============================================================
    // Player側から呼ばれるリンク追加専用関数
    // ============================================================
    public void AddLink(MapNode other)
    {
        if (other == null || other == this) return;

        if (!links.Contains(other))
            links.Add(other);

        if (!other.links.Contains(this))
            other.links.Add(this);

        if (debugLog)
            Debug.Log($"[MapNode] Linked manually: {name} ↔ {other.name}");
    }

    // ============================================================
    // Value初期化（Goal距離ベース）
    // ============================================================
    public void InitializeValue(Vector3 goalPos)
    {
        float dist = Vector3.Distance(transform.position, goalPos);
        value = 1f / (dist + 1f);
    }

    // ============================================================
    // Value更新（報酬伝播＋未知補正＋蒸発）
    // ============================================================
    public void UpdateValue(MapNode goal)
    {
        if (goal == null) return;

        float dist = Vector3.Distance(transform.position, goal.transform.position);
        float reward = 1f / (dist + 1f);

        float neighborMax = 0f;
        if (links.Count > 0)
            neighborMax = Mathf.Max(links.ConvertAll(n => n.value).ToArray());

        value = (1 - rho) * value
              + alpha * (reward + gamma * neighborMax - value)
              + beta / (visits + 1);

        visits++;
    }

    // ============================================================
    // 座標変換
    // ============================================================
    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 p = worldPos - gridOrigin;
        int cx = Mathf.RoundToInt(p.x / cellSize);
        int cz = Mathf.RoundToInt(p.z / cellSize);
        return new Vector2Int(cx, cz);
    }

    // ============================================================
    // Gizmos可視化（Valueとリンク線）
    // ============================================================
    private void OnDrawGizmos()
    {
        float intensity = Mathf.Clamp01(value);
        Gizmos.color = Color.Lerp(Color.blue, Color.red, intensity);
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

        Gizmos.color = Color.yellow;
        foreach (var node in links)
        {
            if (node != null)
                Gizmos.DrawLine(transform.position, node.transform.position);
        }
    }
}

//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;

//public class MapNode : MonoBehaviour
//{
//    // ============================================================
//    // 静的共有データ
//    // ============================================================
//    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();

//    [Header("リンク情報")]
//    public List<MapNode> links = new List<MapNode>();

//    [Header("Value設定")]
//    public float value = 0f;
//    public int visits = 0;
//    public float alpha = 0.3f;  // 学習率
//    public float gamma = 0.9f;  // 割引率
//    public float rho = 0.02f;   // 蒸発率
//    public float beta = 0.5f;   // 未探索ボーナス強度

//    [Header("探索設定")]
//    public float cellSize = 1f;      // 1マスの長さ
//    public int maxSteps = 20;        // 何マス先まで探索するか
//    public LayerMask wallLayer;      // 壁レイヤー
//    public LayerMask nodeLayer;      // Nodeレイヤー
//    public Vector3 gridOrigin = Vector3.zero;

//    [Header("動作設定")]
//    public bool debugLog = false;
//    public float recheckInterval = 0.5f;   // 定期リンク確認間隔（秒）

//    // ============================================================
//    // 起動時処理
//    // ============================================================
//    private void Start()
//    {
//        // 自身のセル座標を登録（重複防止）
//        Vector2Int cell = WorldToCell(transform.position);
//        allNodeCells.Add(cell);

//        //// 近くのNodeと即リンク
//        //LinkWithNearbyNodes();

//        //// Rayで周囲探索
//        //FindNeighbors();

//        //// 定期的に欠損方向を再確認
//        //StartCoroutine(PeriodicRecheck());
//    }

//    public void AddLink(MapNode other)
//    {
//        if (other == null || other == this) return;
//        if (!links.Contains(other)) links.Add(other);
//        if (!other.links.Contains(this)) other.links.Add(this);
//    }


//    // ============================================================
//    // Value初期化（Goal距離ベース）
//    // ============================================================
//    public void InitializeValue(Vector3 goalPos)
//    {
//        float dist = Vector3.Distance(transform.position, goalPos);
//        value = 1f / (dist + 1f);
//    }

//    // ============================================================
//    // Value更新（報酬伝播＋未知補正＋蒸発）
//    // ============================================================
//    public void UpdateValue(MapNode goal)
//    {
//        if (goal == null) return;

//        float dist = Vector3.Distance(transform.position, goal.transform.position);
//        float reward = 1f / (dist + 1f);

//        float neighborMax = 0f;
//        if (links.Count > 0)
//            neighborMax = Mathf.Max(links.ConvertAll(n => n.value).ToArray());

//        value = (1 - rho) * value
//              + alpha * (reward + gamma * neighborMax - value)
//              + beta / (visits + 1);

//        visits++;
//    }

//    //// ============================================================
//    //// 段階的距離拡張レイキャスト（1,2,3...maxSteps）
//    //// ============================================================
//    //public void FindNeighbors()
//    //{
//    //    links.Clear();
//    //    Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
//    //    int mask = wallLayer | nodeLayer;

//    //    foreach (var dir in directions)
//    //    {
//    //        bool connected = false;

//    //        // 距離を1マスずつ伸ばして探索
//    //        for (int step = 1; step <= maxSteps; step++)
//    //        {
//    //            float distance = step * cellSize + 0.1f; // +0.1fで誤差吸収
//    //            Vector3 origin = transform.position + Vector3.up * 0.05f;

//    //            if (Physics.Raycast(origin, dir, out RaycastHit hit, distance, mask))
//    //            {
//    //                GameObject obj = hit.collider.gameObject;

//    //                // 壁に当たったら終了
//    //                if (((1 << obj.layer) & wallLayer) != 0)
//    //                    break;

//    //                // Nodeに当たったら接続
//    //                if (((1 << obj.layer) & nodeLayer) != 0)
//    //                {
//    //                    MapNode neighbor = obj.GetComponent<MapNode>();
//    //                    if (neighbor != null && neighbor != this)
//    //                    {
//    //                        if (!links.Contains(neighbor))
//    //                            links.Add(neighbor);
//    //                        if (!neighbor.links.Contains(this))
//    //                            neighbor.links.Add(this);

//    //                        if (debugLog)
//    //                            Debug.Log($"[MapNode] Link: {name} ↔ {neighbor.name}");
//    //                    }

//    //                    connected = true;
//    //                    break;
//    //                }
//    //            }
//    //        }

//    //        if (!connected)
//    //            continue;
//    //    }
//    //}

//    //// ============================================================
//    //// 新Node生成時に既存Nodeと接続＆再探索を促す
//    //// ============================================================
//    //public void LinkWithNearbyNodes()
//    //{
//    //    MapNode[] allNodes = FindObjectsOfType<MapNode>();
//    //    foreach (var node in allNodes)
//    //    {
//    //        if (node == this) continue;

//    //        float dist = Vector3.Distance(node.transform.position, transform.position);
//    //        if (dist <= cellSize + 0.1f)
//    //        {
//    //            // 双方向リンク
//    //            if (!links.Contains(node))
//    //                links.Add(node);
//    //            if (!node.links.Contains(this))
//    //                node.links.Add(this);

//    //            // 既存Nodeにも再探索を依頼（GoalNode含む）
//    //            node.FindNeighbors();
//    //        }
//    //    }
//    //}

//    //// ============================================================
//    //// 定期的に欠損方向を再チェック（Raycast補修）
//    //// ============================================================
//    //private IEnumerator PeriodicRecheck()
//    //{
//    //    WaitForSeconds wait = new WaitForSeconds(recheckInterval);
//    //    while (true)
//    //    {
//    //        yield return wait;
//    //        CheckUnlinkedDirections();
//    //    }
//    //}

//    //private void CheckUnlinkedDirections()
//    //{
//    //    Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
//    //    int mask = wallLayer | nodeLayer;

//    //    foreach (var dir in dirs)
//    //    {
//    //        // すでに同方向にリンクがある場合はスキップ
//    //        if (links.Exists(n =>
//    //            Mathf.Abs(Vector3.Distance(n.transform.position,
//    //                transform.position + dir * cellSize)) < 0.15f))
//    //            continue;

//    //        Vector3 origin = transform.position + Vector3.up * 0.05f;
//    //        for (int step = 1; step <= maxSteps; step++)
//    //        {
//    //            float distance = step * cellSize + 0.1f;

//    //            if (Physics.Raycast(origin, dir, out RaycastHit hit, distance, mask))
//    //            {
//    //                GameObject obj = hit.collider.gameObject;

//    //                if (((1 << obj.layer) & wallLayer) != 0)
//    //                    break;

//    //                if (((1 << obj.layer) & nodeLayer) != 0)
//    //                {
//    //                    MapNode neighbor = hit.collider.GetComponent<MapNode>();
//    //                    if (neighbor != null && !links.Contains(neighbor))
//    //                    {
//    //                        links.Add(neighbor);
//    //                        if (!neighbor.links.Contains(this))
//    //                            neighbor.links.Add(this);

//    //                        if (debugLog)
//    //                            Debug.Log($"[MapNode] Repaired link: {name} ↔ {neighbor.name}");
//    //                    }
//    //                    break;
//    //                }
//    //            }
//    //        }
//    //    }
//    //}

//    // ============================================================
//    // 座標変換
//    // ============================================================
//    private Vector2Int WorldToCell(Vector3 worldPos)
//    {
//        Vector3 p = worldPos - gridOrigin;
//        int cx = Mathf.RoundToInt(p.x / cellSize);
//        int cz = Mathf.RoundToInt(p.z / cellSize);
//        return new Vector2Int(cx, cz);
//    }

//    // ============================================================
//    // Gizmos可視化：Valueとリンク表示
//    // ============================================================
//    private void OnDrawGizmos()
//    {
//        float intensity = Mathf.Clamp01(value);
//        Gizmos.color = Color.Lerp(Color.blue, Color.red, intensity);
//        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

//        Gizmos.color = Color.yellow;
//        foreach (var node in links)
//        {
//            if (node != null)
//                Gizmos.DrawLine(transform.position, node.transform.position);
//        }
//    }
//}

////using System.Collections;
////using System.Collections.Generic;
////using UnityEngine;

////public class MapNode : MonoBehaviour
////{
////    // ============================================================
////    // 共有データ
////    // ============================================================
////    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();

////    [Header("リンク情報")]
////    public List<MapNode> links = new List<MapNode>();

////    [Header("Value設定")]
////    public float value = 0f;
////    public int visits = 0;
////    public float alpha = 0.3f;  // 学習率
////    public float gamma = 0.9f;  // 割引率
////    public float rho = 0.02f;   // 蒸発率
////    public float beta = 0.5f;   // 未探索ボーナス強度

////    [Header("探索設定")]
////    public float cellSize = 1f;
////    public int maxSteps = 20;   // ← 距離拡張ステップ数
////    public LayerMask wallLayer;
////    public LayerMask nodeLayer;
////    public Vector3 gridOrigin = Vector3.zero;

////    [Header("動作設定")]
////    public bool debugLog = false;
////    public float recheckInterval = 0.5f;   // 定期リンク確認間隔（秒）

////    // ============================================================
////    // 起動時処理
////    // ============================================================
////    private void Start()
////    {
////        Vector2Int cell = WorldToCell(transform.position);
////        allNodeCells.Add(cell);

////        // 近傍ノードと初期リンク
////        LinkWithNearbyNodes();

////        // Ray探索で壁越し接続
////        FindNeighbors();

////        // 定期的に未リンク方向を再チェック
////        StartCoroutine(PeriodicRecheck());
////    }

////    // ============================================================
////    // Value初期化（Goal距離ベース）
////    // ============================================================
////    public void InitializeValue(Vector3 goalPos)
////    {
////        float dist = Vector3.Distance(transform.position, goalPos);
////        value = 1f / (dist + 1f);
////    }

////    // ============================================================
////    // Value更新（報酬伝播 + 未知補正 + 蒸発）
////    // ============================================================
////    public void UpdateValue(MapNode goal)
////    {
////        if (goal == null) return;

////        float dist = Vector3.Distance(transform.position, goal.transform.position);
////        float reward = 1f / (dist + 1f);
////        float neighborMax = 0f;

////        if (links.Count > 0)
////            neighborMax = Mathf.Max(links.ConvertAll(n => n.value).ToArray());

////        value = (1 - rho) * value
////              + alpha * (reward + gamma * neighborMax - value)
////              + beta / (visits + 1);

////        visits++;
////    }

////    // ============================================================
////    // 距離拡張型レイキャストで近傍ノードを探索・接続
////    // ============================================================
////    public void FindNeighbors()
////    {
////        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
////        int mask = wallLayer | nodeLayer;

////        foreach (var dir in directions)
////        {
////            for (int step = 1; step <= maxSteps; step++)
////            {
////                float distance = step * cellSize + 0.1f; // ← 余裕を持たせて誤差防止
////                Vector3 origin = transform.position + Vector3.up * 0.05f;

////                if (Physics.Raycast(origin, dir, out RaycastHit hit, distance, mask))
////                {
////                    GameObject obj = hit.collider.gameObject;

////                    // 壁なら打ち切り
////                    if (((1 << obj.layer) & wallLayer) != 0)
////                        break;

////                    // Nodeならリンク
////                    if (((1 << obj.layer) & nodeLayer) != 0)
////                    {
////                        MapNode neighbor = obj.GetComponent<MapNode>();
////                        if (neighbor != null && neighbor != this)
////                        {
////                            if (!links.Contains(neighbor))
////                                links.Add(neighbor);
////                            if (!neighbor.links.Contains(this))
////                                neighbor.links.Add(this);

////                            if (debugLog)
////                                Debug.Log($"[MapNode] Link: {name} ↔ {neighbor.name}");
////                        }
////                        break;
////                    }
////                }
////            }
////        }
////    }

////    // ============================================================
////    // 新Node生成時に呼ばれる：既存Nodeとの接続＆再探索依頼
////    // ============================================================
////    public void LinkWithNearbyNodes()
////    {
////        MapNode[] allNodes = FindObjectsOfType<MapNode>();
////        foreach (var node in allNodes)
////        {
////            if (node == this) continue;

////            float dist = Vector3.Distance(node.transform.position, transform.position);
////            if (dist <= cellSize + 0.1f)
////            {
////                // 双方向リンク
////                if (!links.Contains(node))
////                    links.Add(node);
////                if (!node.links.Contains(this))
////                    node.links.Add(this);

////                // 既存Nodeにも再探索を依頼（GoalNodeも含む）
////                node.FindNeighbors();
////            }
////        }
////    }

////    // ============================================================
////    // 定期的に欠損方向を再チェック
////    // ============================================================
////    private IEnumerator PeriodicRecheck()
////    {
////        WaitForSeconds wait = new WaitForSeconds(recheckInterval);
////        while (true)
////        {
////            yield return wait;
////            CheckUnlinkedDirections();
////        }
////    }

////    private void CheckUnlinkedDirections()
////    {
////        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
////        int mask = wallLayer | nodeLayer;

////        foreach (var dir in dirs)
////        {
////            // 既にリンク済みならスキップ
////            if (links.Exists(n =>
////                Mathf.Abs(Vector3.Distance(n.transform.position,
////                    transform.position + dir * cellSize)) < 0.1f))
////                continue;

////            Vector3 origin = transform.position + Vector3.up * 0.05f;
////            if (Physics.Raycast(origin, dir, out RaycastHit hit, cellSize * maxSteps, mask))
////            {
////                GameObject obj = hit.collider.gameObject;

////                // 壁なら打ち切り
////                if (((1 << obj.layer) & wallLayer) != 0)
////                    continue;

////                if (((1 << obj.layer) & nodeLayer) != 0)
////                {
////                    MapNode neighbor = hit.collider.GetComponent<MapNode>();
////                    if (neighbor != null && !links.Contains(neighbor))
////                    {
////                        links.Add(neighbor);
////                        if (!neighbor.links.Contains(this))
////                            neighbor.links.Add(this);

////                        if (debugLog)
////                            Debug.Log($"[MapNode] Repaired link: {name} ↔ {neighbor.name}");
////                    }
////                }
////            }
////        }
////    }

////    // ============================================================
////    // 座標変換
////    // ============================================================
////    private Vector2Int WorldToCell(Vector3 worldPos)
////    {
////        Vector3 p = worldPos - gridOrigin;
////        int cx = Mathf.RoundToInt(p.x / cellSize);
////        int cz = Mathf.RoundToInt(p.z / cellSize);
////        return new Vector2Int(cx, cz);
////    }

////    // ============================================================
////    // Gizmos：Value可視化
////    // ============================================================
////    private void OnDrawGizmos()
////    {
////        float intensity = Mathf.Clamp01(value);
////        Gizmos.color = Color.Lerp(Color.blue, Color.red, intensity);
////        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.12f);

////        Gizmos.color = Color.yellow;
////        foreach (var node in links)
////        {
////            if (node != null)
////                Gizmos.DrawLine(transform.position, node.transform.position);
////        }
////    }
////}

//////using System.Collections.Generic;
//////using UnityEngine;

////////[ExecuteAlways]
//////public class MapNode : MonoBehaviour
//////{
//////    // =========================================
//////    // 全プレイヤー共有のNode座標リスト
//////    // =========================================
//////    public static HashSet<Vector2Int> allNodeCells = new HashSet<Vector2Int>();

//////    [Header("リンク情報")]
//////    public List<MapNode> links = new List<MapNode>();
//////    public float value = 0f;

//////    [Header("探索設定")]
//////    public float cellSize = 1f;            // グリッド1マスの長さ
//////    public int maxSteps = 20;              // 何マス先まで探索するか
//////    public LayerMask wallLayer;            // 壁レイヤー
//////    public LayerMask nodeLayer;            // Nodeレイヤー
//////    public Vector3 gridOrigin = Vector3.zero; // グリッド原点（オフセット補正用）

//////    private void Start()
//////    {
//////        // 起動時に自身の位置を登録（重複は自動スキップ）
//////        Vector2Int cell = WorldToCell(transform.position);
//////        allNodeCells.Add(cell);

//////        //if (Application.isPlaying)
//////        //    FindNeighbors();

//////        // 既存Nodeとのリンク同期
//////        LinkWithNearbyNodes();

//////        // 周囲をRayで再確認
//////        FindNeighbors();
//////    }

//////#if UNITY_EDITOR
//////    private void OnValidate()
//////    {
//////        if (!Application.isPlaying)
//////            FindNeighbors();
//////    }
//////#endif

//////    // =====================================================
//////    // 静的関数：指定位置にNodeが存在するかをチェック
//////    // =====================================================
//////    public static bool NodeExistsAt(Vector3 worldPos, float cellSize, Vector3 origin)
//////    {
//////        Vector3 p = worldPos - origin;
//////        Vector2Int cell = new Vector2Int(
//////            Mathf.RoundToInt(p.x / cellSize),
//////            Mathf.RoundToInt(p.z / cellSize)
//////        );
//////        return allNodeCells.Contains(cell);
//////    }

//////    // =====================================================
//////    // ワールド座標 → グリッド座標変換
//////    // =====================================================
//////    private Vector2Int WorldToCell(Vector3 worldPos)
//////    {
//////        Vector3 p = worldPos - gridOrigin;
//////        int cx = Mathf.RoundToInt(p.x / cellSize);
//////        int cz = Mathf.RoundToInt(p.z / cellSize);
//////        return new Vector2Int(cx, cz);
//////    }

//////    // =====================================================
//////    // 近隣ノードとの自動リンク（生成時に呼ぶ）
//////    // =====================================================
//////    private void LinkWithNearbyNodes()
//////    {
//////        MapNode[] allNodes = FindObjectsOfType<MapNode>();
//////        foreach (var node in allNodes)
//////        {
//////            if (node == this) continue;

//////            float dist = Vector3.Distance(node.transform.position, transform.position);
//////            // 1マス以内（上下左右）のNodeと接続
//////            if (Mathf.Abs(dist - cellSize) < 0.05f)
//////            {
//////                if (!links.Contains(node))
//////                    links.Add(node);
//////                if (!node.links.Contains(this))
//////                    node.links.Add(this);

//////                node.FindNeighbors();
//////            }
//////        }
//////    }

//////    // =====================================================
//////    // グリッドベースでRayを段階的に伸ばし、壁 or Node に当たるまで探索
//////    // =====================================================
//////    public void FindNeighbors()
//////    {
//////        links.Clear();
//////        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

//////        foreach (var dir in directions)
//////        {
//////            bool hitSomething = false;

//////            // 1マスずつ伸ばして調べる
//////            for (int step = 1; step <= maxSteps; step++)
//////            {
//////                float distance = step * cellSize;
//////                Vector3 origin = transform.position;

//////                // Rayを飛ばす
//////                if (Physics.Raycast(origin, dir, out RaycastHit hit, distance))
//////                {
//////                    GameObject obj = hit.collider.gameObject;
//////                    int layerMask = 1 << obj.layer;

//////                    // 壁なら探索終了（リンクしない）
//////                    if ((wallLayer.value & layerMask) != 0)
//////                    {
//////                        hitSomething = true;
//////                        break;
//////                    }

//////                    // Nodeならリンク確定
//////                    if ((nodeLayer.value & layerMask) != 0)
//////                    {
//////                        MapNode neighbor = obj.GetComponent<MapNode>();
//////                        if (neighbor != null && neighbor != this)
//////                        {
//////                            if (!links.Contains(neighbor))
//////                            {
//////                                links.Add(neighbor);
//////                                if (!neighbor.links.Contains(this))
//////                                    neighbor.links.Add(this);
//////                            }
//////                        }
//////                        hitSomething = true;
//////                        break;
//////                    }
//////                }
//////            }

//////            // 何にも当たらなかった方向は無視
//////            if (!hitSomething)
//////                continue;
//////        }
//////    }

//////    // =====================================================
//////    // Gizmos：リンク線を赤で表示
//////    // =====================================================
//////    private void OnDrawGizmos()
//////    {
//////        Gizmos.color = Color.red;
//////        foreach (var node in links)
//////        {
//////            if (node != null)
//////            {
//////                Gizmos.DrawLine(transform.position, node.transform.position);
//////            }
//////        }
//////    }
//////}
