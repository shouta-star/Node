using UnityEngine;

/// <summary>
/// Attacks the current target selected by EnemyTargetSelector when in range.
/// </summary>
[RequireComponent(typeof(EnemyTargetSelector))]
public class EnemyAttack : MonoBehaviour
{
    [Header("Attack")]
    public bool enableAttack = true;
    public float attackRange = 1.0f;
    public float attackCooldownSec = 1.0f;
    public int attackDamage = 1;

    [Header("Debug")]
    public bool debugLog = false;

    private EnemyTargetSelector selector;
    private float nextAttackTime = 0f;

    private void Awake()
    {
        selector = GetComponent<EnemyTargetSelector>();
    }

    private void Update()
    {
        if (!enableAttack) return;
        if (selector.CurrentTarget == null) return;
        if (Time.time < nextAttackTime) return;

        float dist = Vector3.Distance(transform.position, selector.CurrentTarget.position);
        if (dist > attackRange) return;

        // Deal damage
        var health = selector.CurrentTarget.GetComponent<PlayerHealth>();
        if (health != null)
        {
            health.TakeDamage(attackDamage);

            if (debugLog)
                Debug.Log($"[EnemyAttack] Hit {selector.CurrentTarget.name} for {attackDamage}. HP={health.currentHP}/{health.maxHP}");
        }

        nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldownSec);
    }
}
