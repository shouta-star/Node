using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public int maxHP = 3;
    public int currentHP = 3;

    [Header("Death")]
    public bool destroyOnDeath = true;
    public bool debugLog = false;

    private bool _dead = false;

    private void Awake()
    {
        if (currentHP <= 0) currentHP = maxHP;
    }

    public void TakeDamage(int amount)
    {
        if (_dead) return;
        amount = Mathf.Max(0, amount);
        if (amount == 0) return;

        currentHP -= amount;
        if (debugLog) Debug.Log($"[EnemyHealth] {name} took {amount} dmg. HP={currentHP}/{maxHP}");

        if (currentHP <= 0)
        {
            _dead = true;
            if (debugLog) Debug.Log($"[EnemyHealth] {name} died.");

            if (destroyOnDeath)
                Destroy(gameObject);
            else
                gameObject.SetActive(false);
        }
    }
}
