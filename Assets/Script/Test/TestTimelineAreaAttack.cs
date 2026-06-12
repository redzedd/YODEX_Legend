using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 宣傳片演示用:供 Timeline Signal Receiver 或 AnimationEvent 呼叫 TriggerAttack(),
/// 於設定位置做一次球形範圍偵測,將 TestRagdollEnemy 切換成 Ragdoll 並依爆炸推力擊飛。
/// 與 TestExplosiveBarrel 共用 TestRagdollEnemy.Explode() 流程,行為一致。
/// </summary>
public class TestTimelineAreaAttack : MonoBehaviour
{
    [Header("攻擊範圍")]
    [SerializeField, Tooltip("攻擊原點 (留空 = 本物件;可拖子物件例如拳頭/武器 Anchor)")]
    private Transform _origin;
    [SerializeField, Tooltip("攻擊半徑 (公尺)"), Min(0f)]
    private float _radius = 4f;
    [SerializeField, Tooltip("沿 origin.forward 向前偏移 (讓判定落在角色前方)")]
    private float _forwardOffset = 2f;
    [SerializeField, Tooltip("向上偏移 (設在胸口/肩膀高度比較自然)")]
    private float _upOffset = 1f;

    [Header("擊飛力道")]
    [SerializeField, Tooltip("爆炸推力"), Min(0f)]
    private float _force = 1200f;
    [SerializeField, Tooltip("向上推力修正 (愈大愈有拋飛感)"), Min(0f)]
    private float _upwardsModifier = 1.5f;
    [SerializeField, Tooltip("可被影響的圖層 (預設全部)")]
    private LayerMask _damageLayers = ~0;

    [Header("特效與音效")]
    [SerializeField, Tooltip("命中 VFX Prefab,於攻擊中心生成 (選填)")]
    private GameObject _hitVfxPrefab;
    [SerializeField, Tooltip("VFX 存活秒數")]
    private float _vfxLifetime = 3f;
    [SerializeField, Tooltip("命中音效 (選填)")]
    private AudioClip _hitSound;
    [SerializeField, Range(0f, 1f), Tooltip("音效音量")]
    private float _hitVolume = 1f;

    [Header("Debug")]
    [SerializeField, Tooltip("Scene 視窗持續顯示攻擊範圍 Gizmo")]
    private bool _drawRadius = true;

    /// <summary>Timeline Signal Receiver / AnimationEvent 呼叫:執行一次範圍攻擊。</summary>
    public void TriggerAttack()
    {
        Vector3 center = GetAttackCenter();
        SpawnVfx(center);
        PlaySound(center);
        ApplyBlast(center);
    }

    private Vector3 GetAttackCenter()
    {
        Transform origin = _origin != null ? _origin : transform;
        return origin.position + origin.forward * _forwardOffset + Vector3.up * _upOffset;
    }

    private void SpawnVfx(Vector3 position)
    {
        if (_hitVfxPrefab == null) return;
        GameObject vfx = Instantiate(_hitVfxPrefab, position, Quaternion.identity);
        Destroy(vfx, _vfxLifetime > 0f ? _vfxLifetime : 5f);
    }

    private void PlaySound(Vector3 position)
    {
        if (_hitSound == null) return;
        AudioSource.PlayClipAtPoint(_hitSound, position, _hitVolume);
    }

    private void ApplyBlast(Vector3 position)
    {
        Collider[] hits = Physics.OverlapSphere(position, _radius, _damageLayers, QueryTriggerInteraction.Ignore);
        HashSet<TestRagdollEnemy> enemies = new HashSet<TestRagdollEnemy>();
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null) continue;
            TestRagdollEnemy enemy = c.GetComponentInParent<TestRagdollEnemy>();
            if (enemy != null)
            {
                enemies.Add(enemy);
                continue;
            }
            Rigidbody rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
            {
                rb.AddExplosionForce(_force, position, _radius, _upwardsModifier, ForceMode.Impulse);
            }
        }
        foreach (TestRagdollEnemy enemy in enemies)
        {
            enemy.Explode(position, _radius, _force, _upwardsModifier);
        }
    }

    private void OnDrawGizmos()
    {
        if (!_drawRadius) return;
        Vector3 center = GetAttackCenter();
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.2f);
        Gizmos.DrawSphere(center, _radius);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(center, _radius);
    }
}
