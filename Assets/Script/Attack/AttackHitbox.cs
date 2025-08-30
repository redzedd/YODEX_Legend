using UnityEngine;

public class AttackHitbox : MonoBehaviour
{
    public int damage = 1; // 傷害數值（可調整）
    public string targetTag = "Enemy"; // 只對敵人有效

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            var hurtBox = other.GetComponent<EnemyHurtbox>();
            if (hurtBox != null)
            {
                hurtBox.TakeDamage(damage);
            }
        }
    }
}