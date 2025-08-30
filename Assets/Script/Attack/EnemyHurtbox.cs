using UnityEngine;

public class EnemyHurtbox : MonoBehaviour
{
    public int health = 5;

    public void TakeDamage(int amount)
    {
        health -= amount;
        Debug.Log($"☠️ Enemy hit! HP: {health}");

        if (health <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log("💀 Enemy died!");
        // TODO: 播動畫 / 效果 / 消失
        Destroy(gameObject);
    }
}

