using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 場景互動爆炸桶 — 被箭矢命中後點燃引爆,對範圍內玩家/敵人套用 HitContext 擊飛,
/// 對其他 Rigidbody 施加爆炸推力,並連鎖引爆範圍內其他爆炸桶。
///
/// 點燃機制:實作 <see cref="IProjectileIgnitable"/>,由 GAS.ProjectileBehaviour 命中時直接呼叫
/// <see cref="OnProjectileImpact"/>。完全不依賴 Trigger 事件或 Layer 設定,
/// 也不需要桶子的 Collider 在 ProjectileData.HitLayers 內(避免被當成傷害目標)。
///
/// 同時保留 OnCollisionEnter / OnTriggerEnter 路徑,讓 TestArrowProjectile(非走 GAS 流程的測試箭矢)
/// 也能引爆。
/// </summary>
public class ExplosiveBarrel : MonoBehaviour, IProjectileIgnitable
{
    [Header("爆炸特效")]
    [SerializeField, Tooltip("爆炸 VFX Prefab(於爆炸點生成,自動偵測粒子壽命銷毀)")]
    private GameObject _explosionVfxPrefab;

    [SerializeField, Tooltip("VFX 存活秒數(0 = 自動偵測粒子時長,偵測失敗 fallback 5 秒,建議填 0)")]
    private float _vfxLifetime = 0f;

    [SerializeField, Tooltip("爆炸音效(可留空)")]
    private AudioClip _explosionSound;

    [SerializeField, Tooltip("音效音量(建議 0.5 ~ 1)")]
    private float _explosionVolume = 1f;

    [Header("爆炸範圍")]
    [SerializeField, Tooltip("爆炸半徑(公尺,建議 4 ~ 6)")]
    private float _explosionRadius = 5f;

    [SerializeField, Tooltip("可被爆炸影響的圖層(預設全部)")]
    private LayerMask _affectedLayers = ~0;

    [Header("爆炸傷害(對 IHitReceiver:玩家/敵人)")]
    [SerializeField, Tooltip("爆炸中心傷害值;會依距離由中心到邊緣線性衰減")]
    private float _explosionDamage = 40f;

    [SerializeField, Tooltip("Poise 傷害 — 用於擊破敵人霸體(建議 50 ~ 80)")]
    private float _explosionPoiseDamage = 60f;

    [SerializeField, Tooltip("擊飛距離(公尺,玩家與敵人共用,建議 4 ~ 8)")]
    private float _knockbackDistance = 6f;

    [SerializeField, Tooltip("命中是否視為重攻擊(AttackTier.Heavy)— 勾選代表能打破攻擊霸體,玩家走 Knockback 而非 Stagger")]
    private bool _isHeavyHit = true;

    [Header("爆炸推力(對其他 Rigidbody — 道具、雜物)")]
    [SerializeField, Tooltip("AddExplosionForce 推力大小(建議 500 ~ 1500)")]
    private float _physicsForce = 800f;

    [SerializeField, Tooltip("向上推力修正(>0 可讓爆炸更有抛飛感,建議 0.5 ~ 1.5)")]
    private float _upwardsModifier = 1f;

    [Header("命中回饋")]
    [SerializeField, Tooltip("鏡頭震動強度(0 = 不震動,建議 0.4 ~ 0.8)")]
    private float _cameraShakeIntensity = 0.6f;

    [SerializeField, Tooltip("頓幀秒數(0 = 不頓幀,建議 0.04 ~ 0.08)")]
    private float _hitStopDuration = 0.05f;

    [SerializeField, Tooltip("頓幀時的 timeScale(0 = 完全停止,建議 0 ~ 0.1)")]
    private float _hitStopTimeScale = 0.05f;

    [Header("觸發條件")]
    [SerializeField, Tooltip("被點燃後延遲爆炸秒數(0 = 立即爆炸,建議 0.05 ~ 0.2)")]
    private float _explodeDelay = 0.05f;

    [Header("Debug")]
    [SerializeField, Tooltip("Scene 視窗顯示爆炸半徑 Gizmo")]
    private bool _drawGizmos = true;

    [SerializeField, Tooltip("Console 印出點燃 / 爆炸日誌(除錯用)")]
    private bool _debugLog = false;

    private bool _hasExploded;

    /// <summary>由 GAS.ProjectileBehaviour 命中時呼叫(實作 IProjectileIgnitable)</summary>
    public void OnProjectileImpact(Vector3 hitPoint)
    {
        if (_debugLog) Debug.Log($"<color=orange>[爆炸桶]</color> {name} 被投射物點燃 @ {hitPoint}");
        TriggerExplode();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        TryIgniteByTestArrow(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null) return;
        TryIgniteByTestArrow(collision.gameObject);
    }

    private void TryIgniteByTestArrow(GameObject incoming)
    {
        if (_hasExploded || incoming == null) return;
        if (incoming.GetComponentInParent<TestArrowProjectile>() == null) return;
        if (_debugLog) Debug.Log($"<color=orange>[爆炸桶]</color> {name} 被測試箭矢點燃");
        TriggerExplode();
    }

    /// <summary>Inspector 右鍵測試用 — 在 Play 模式中對 ExplosiveBarrel 元件右鍵選「測試引爆」可立即觸發,用來驗證爆炸邏輯本身有沒有問題</summary>
    [ContextMenu("測試引爆")]
    private void DebugForceExplode()
    {
        Debug.Log($"<color=magenta>[爆炸桶]</color> {name} 手動強制引爆");
        TriggerExplode();
    }

    /// <summary>外部可呼叫(例:連鎖引爆、設計師腳本手動觸發)</summary>
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
        Vector3 center = transform.position;
        if (_debugLog) Debug.Log($"<color=red>[爆炸桶]</color> {name} 引爆 @ {center}");
        SpawnVfx(center);
        PlaySound(center);
        ApplyBlast(center);
        Destroy(gameObject);
    }

    private void ApplyBlast(Vector3 center)
    {
        Collider[] hits = Physics.OverlapSphere(center, _explosionRadius, _affectedLayers, QueryTriggerInteraction.Ignore);
        HashSet<IHitReceiver> hitReceivers = new HashSet<IHitReceiver>();
        HashSet<ExplosiveBarrel> chainBarrels = new HashSet<ExplosiveBarrel>();
        HashSet<Rigidbody> rigidbodies = new HashSet<Rigidbody>();
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null || c.gameObject == gameObject) continue;
            ExplosiveBarrel barrel = c.GetComponentInParent<ExplosiveBarrel>();
            if (barrel != null && barrel != this && !barrel._hasExploded)
            {
                chainBarrels.Add(barrel);
                continue;
            }
            IHitReceiver receiver = c.GetComponentInParent<IHitReceiver>();
            if (receiver != null)
            {
                hitReceivers.Add(receiver);
                continue;
            }
            Rigidbody rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
            {
                rigidbodies.Add(rb);
            }
        }
        foreach (IHitReceiver receiver in hitReceivers)
        {
            ApplyHit(receiver, center);
        }
        foreach (Rigidbody rb in rigidbodies)
        {
            rb.AddExplosionForce(_physicsForce, center, _explosionRadius, _upwardsModifier, ForceMode.Impulse);
        }
        foreach (ExplosiveBarrel barrel in chainBarrels)
        {
            barrel.TriggerExplode();
        }
    }

    private void ApplyHit(IHitReceiver receiver, Vector3 center)
    {
        MonoBehaviour mb = receiver as MonoBehaviour;
        if (mb == null) return;
        Vector3 targetPos = mb.transform.position;
        Vector3 dir = targetPos - center;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = mb.transform.forward;
        }
        dir.Normalize();
        float distance = Vector3.Distance(targetPos, center);
        float falloff = Mathf.Clamp01(1f - distance / Mathf.Max(0.001f, _explosionRadius));
        HitContext ctx = new HitContext
        {
            damage = _explosionDamage * falloff,
            poiseDamage = _explosionPoiseDamage,
            knockbackForce = _knockbackDistance,
            attackTier = _isHeavyHit ? AttackTier.Heavy : AttackTier.Normal,
            isHeavyAttack = _isHeavyHit,
            hitPoint = targetPos,
            hitNormal = -dir,
            attackDirection = dir,
            sourceProfile = null,
            skipHitEffects = false,
            gasDamageApplied = false,
            hitStopDuration = _hitStopDuration,
            hitStopTimeScale = _hitStopTimeScale,
            cameraShakeIntensity = _cameraShakeIntensity
        };
        receiver.OnHit(ref ctx);
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

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos) return;
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.25f);
        Gizmos.DrawSphere(transform.position, _explosionRadius);
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, _explosionRadius);
    }
}
