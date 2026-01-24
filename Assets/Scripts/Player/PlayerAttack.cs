using UnityEngine;

/// <summary>
/// Attacks nearest Enemy in range (distance-based, no collider needed).
/// </summary>
public class PlayerAttack : MonoBehaviour
{
    [Header("Target")]
    public bool enableAttack = true;
    public string enemyTag = "Enemy";

    [Header("Attack")]
    public float attackRange = 1.0f;
    public float attackCooldownSec = 1.0f;
    public int attackDamage = 1;

    [Header("Scan")]
    public float scanIntervalSec = 0.15f;

    [Header("Debug")]
    public bool debugLog = false;

    private float _nextAttackTime = 0f;
    private float _nextScanTime = 0f;
    private Transform _cachedTarget;

    private void Update()
    {
        if (!enableAttack) return;
        if (Time.time < _nextAttackTime) return;

        // ターゲット更新（毎フレームFindしない）
        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + Mathf.Max(0.01f, scanIntervalSec);
            _cachedTarget = FindNearestEnemy();
        }

        if (_cachedTarget == null) return;

        float dist = Vector3.Distance(transform.position, _cachedTarget.position);
        if (dist > attackRange) return;

        var eh = _cachedTarget.GetComponent<EnemyHealth>();
        if (eh != null)
        {
            eh.TakeDamage(attackDamage);
            if (debugLog)
                Debug.Log($"[PlayerAttack] Hit {_cachedTarget.name} for {attackDamage}. HP={eh.currentHP}/{eh.maxHP}");
        }
        else if (debugLog)
        {
            Debug.Log($"[PlayerAttack] EnemyHealth not found on {_cachedTarget.name}");
        }

        _nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldownSec);
    }

    private Transform FindNearestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTag);
        if (enemies == null || enemies.Length == 0) return null;

        Transform best = null;
        float bestD2 = float.PositiveInfinity;
        Vector3 me = transform.position;

        foreach (var e in enemies)
        {
            if (e == null) continue;
            float d2 = (e.transform.position - me).sqrMagnitude;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = e.transform;
            }
        }
        return best;
    }
}
