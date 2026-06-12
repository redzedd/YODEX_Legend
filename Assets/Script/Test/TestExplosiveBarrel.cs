using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 宣傳片演示用炸彈桶:
/// 被 TestArrowProjectile 射中時觸發爆炸,生成 VFX,推飛周圍 Rigidbody,連鎖引爆範圍內其他炸彈桶,最後銷毀自身。
/// </summary>
[RequireComponent(typeof(Collider))]
public class TestExplosiveBarrel : MonoBehaviour
{
    [Header("爆炸特效")]
    [SerializeField, Tooltip("爆炸 VFX Prefab (ParticleSystem / VFX Graph,於爆炸點生成)")]
    private GameObject _explosionVfxPrefab;
    [SerializeField, Tooltip("VFX 存活秒數 (0 或以下則自動偵測粒子時長,偵測失敗 fallback 5 秒)")]
    private float _vfxLifetime = 0f;
    [SerializeField, Tooltip("爆炸音效 (可選)")]
    private AudioClip _explosionSound;
    [SerializeField, Range(0f, 1f), Tooltip("音效音量")]
    private float _explosionVolume = 1f;

    [Header("爆炸物理")]
    [SerializeField, Tooltip("爆炸半徑 (公尺)")]
    private float _explosionRadius = 5f;
    [SerializeField, Tooltip("爆炸推力 (ForceMode.Impulse)")]
    private float _explosionForce = 800f;
    [SerializeField, Tooltip("向上推力修正 (大於 0 可讓爆炸更有抛飛感)")]
    private float _upwardsModifier = 1f;
    [SerializeField, Tooltip("可被爆炸影響的圖層 (預設全部)")]
    private LayerMask _damageLayers = ~0;

    [Header("觸發條件")]
    [SerializeField, Tooltip("被射中後延遲爆炸秒數 (0 = 立刻)")]
    private float _explodeDelay = 0.05f;
    [SerializeField, Tooltip("任何碰撞都引爆 (取消勾選則僅箭矢能引爆)")]
    private bool _explodeOnAnyImpact;

    [Header("Debug")]
    [SerializeField, Tooltip("Scene 視窗顯示爆炸半徑 Gizmo")]
    private bool _drawRadius = true;

    private bool _hasExploded;

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasExploded) return;
        if (!_explodeOnAnyImpact && !IsArrowCollision(collision)) return;
        TriggerExplode();
    }

    private static bool IsArrowCollision(Collision collision)
    {
        if (collision == null || collision.gameObject == null) return false;
        return collision.gameObject.GetComponentInParent<TestArrowProjectile>() != null;
    }

    /// <summary>外部可呼叫 (例如其他爆炸連鎖引爆,或玩家手動觸發)。</summary>
    public void TriggerExplode()
    {
        if (_hasExploded) return;
        _hasExploded = true;
        if (_explodeDelay <= 0f)
        {
            Explode();
        }
        else
        {
            Invoke(nameof(Explode), _explodeDelay);
        }
    }

    private void Explode()
    {
        Vector3 position = transform.position;
        SpawnVfx(position);
        PlaySound(position);
        ApplyBlast(position);
        Destroy(gameObject);
    }

    private void SpawnVfx(Vector3 position)
    {
        if (_explosionVfxPrefab == null) return;
        GameObject vfx = Instantiate(_explosionVfxPrefab, position, Quaternion.identity);
        float life = _vfxLifetime > 0f ? _vfxLifetime : CalculateVfxLifetime(vfx);
        Destroy(vfx, life);
    }

    private static float CalculateVfxLifetime(GameObject vfx)
    {
        ParticleSystem[] systems = vfx.GetComponentsInChildren<ParticleSystem>();
        if (systems.Length == 0) return 5f;
        float maxLife = 0f;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            float life = main.duration + main.startLifetime.constantMax;
            if (life > maxLife) maxLife = life;
        }
        return maxLife > 0f ? maxLife + 0.5f : 5f;
    }

    private void PlaySound(Vector3 position)
    {
        if (_explosionSound == null) return;
        AudioSource.PlayClipAtPoint(_explosionSound, position, _explosionVolume);
    }

    private void ApplyBlast(Vector3 position)
    {
        Collider[] hits = Physics.OverlapSphere(position, _explosionRadius, _damageLayers, QueryTriggerInteraction.Ignore);
        HashSet<TestRagdollEnemy> enemies = new HashSet<TestRagdollEnemy>();
        HashSet<TestExplosiveBarrel> chainBarrels = new HashSet<TestExplosiveBarrel>();
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null || c.gameObject == gameObject) continue;
            TestRagdollEnemy enemy = c.GetComponentInParent<TestRagdollEnemy>();
            if (enemy != null)
            {
                enemies.Add(enemy);
                continue;
            }
            TestExplosiveBarrel other = c.GetComponentInParent<TestExplosiveBarrel>();
            if (other != null && other != this && !other._hasExploded)
            {
                chainBarrels.Add(other);
                continue;
            }
            Rigidbody rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
            {
                rb.AddExplosionForce(_explosionForce, position, _explosionRadius, _upwardsModifier, ForceMode.Impulse);
            }
        }
        foreach (TestRagdollEnemy enemy in enemies)
        {
            enemy.Explode(position, _explosionRadius, _explosionForce, _upwardsModifier);
        }
        foreach (TestExplosiveBarrel barrel in chainBarrels)
        {
            barrel.TriggerExplode();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawRadius) return;
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.25f);
        Gizmos.DrawSphere(transform.position, _explosionRadius);
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, _explosionRadius);
    }
}
