using UnityEngine;

/// <summary>
/// Player health component.
/// - Manages HP
/// - Receives damage via TakeDamage
/// - Records last time damaged (for "no-damage time window" logic later)
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("HP")]
    public int maxHP = 10;
    public int currentHP = 10;

    [Header("Damage history")]
    [Tooltip("Last time this player took damage (Time.time). -1 means never.")]
    public float lastDamagedTime = -1f;

    [Tooltip("Total damage taken (optional debug/analytics).")]
    public int totalDamageTaken = 0;

    private CellFromStart cfs;

    [Header("Stop on damage")]
    [Tooltip("If true, when this player takes damage once, its CellFromStart movement will be permanently locked (until death).")]
    public bool stopMovementForeverOnFirstDamage = true;

    // Åö í«â¡ÅFéÄñSèàóù
    [Header("Death")]
    public bool destroyOnDeath = true;
    public float destroyDelay = 0f;
    private bool isDead = false;

    private void Awake()
    {
        if (currentHP <= 0) currentHP = maxHP;

        // Åö í«â¡ÅFìØÇ∂GameObjectè„Ç…Ç†ÇÈÇ»ÇÁèEÇ§Åiñ≥ÇØÇÍÇŒnullÇÃÇ‹Ç‹Åj
        cfs = GetComponent<CellFromStart>();
    }

    public void TakeDamage(int dmg)
    {
        if (dmg <= 0) return;

        if (isDead) return; // Åö í«â¡ÅFéÄñSå„ÇÕñ≥éã

        currentHP = Mathf.Max(0, currentHP - dmg);
        totalDamageTaken += dmg;
        lastDamagedTime = Time.time;

        // Åö îÌíeÇµÇΩèuä‘Ç…ãèÇΩNodeÇ MustPass Ç…Ç∑ÇÈ
        if (cfs != null)
            cfs.SetMustPassAtCurrentNode_Damaged();

        if (cfs != null && stopMovementForeverOnFirstDamage)
            cfs.LockMovementForever_OnDamaged();

        // Åö í«â¡ÅFHP0Ç»ÇÁDestroy
        if (currentHP == 0)
        {
            isDead = true;

            if (destroyOnDeath)
            {
                if (destroyDelay <= 0f) Destroy(gameObject);
                else Destroy(gameObject, destroyDelay);
            }
        }

        // Optional: add death handling later
        // if (currentHP == 0) { ... }
    }

    /// <summary>Seconds since last hit. Returns +infinity if never damaged.</summary>
    public float TimeSinceLastDamaged()
    {
        if (lastDamagedTime < 0f) return float.PositiveInfinity;
        return Time.time - lastDamagedTime;
    }

    public bool IsDamagedWithin(float seconds)
    {
        return TimeSinceLastDamaged() <= Mathf.Max(0f, seconds);
    }
}
