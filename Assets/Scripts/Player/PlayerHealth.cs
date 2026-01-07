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

    private void Awake()
    {
        if (currentHP <= 0) currentHP = maxHP;
    }

    public void TakeDamage(int dmg)
    {
        if (dmg <= 0) return;

        currentHP = Mathf.Max(0, currentHP - dmg);
        totalDamageTaken += dmg;
        lastDamagedTime = Time.time;

        // š ”í’e‚µ‚½uŠÔ‚É‹‚½Node‚ð MustPass ‚É‚·‚é
        if (cfs != null)
            cfs.SetMustPassAtCurrentNode_Damaged();

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
