using UnityEngine;

public class EnemyAttackHitbox : MonoBehaviour
{
    public int damage = 10;
    public bool oneHitOnly = true; // 每次攻擊只打一次
    private bool hasHit = false;

    private void OnTriggerEnter(Collider other)
    {
        if (hasHit && oneHitOnly) return;

        if (other.CompareTag("Player"))
        {
            PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
                hasHit = true;
            }
        }
    }

    public void ResetHit()
    {
        hasHit = false;
    }
}
