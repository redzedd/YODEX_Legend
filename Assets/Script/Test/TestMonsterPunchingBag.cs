using UnityEngine;
using Animancer;
using DG.Tweening;

/// <summary>
/// 宣傳片演示用沙包怪物:實作 IHitReceiver 接收玩家 GAS 攻擊 (MeleeHitScanner)。
/// 前 (MaxHits - 1) 下播受擊動畫 + VFX/SFX + 身體震動;
/// 達到 MaxHits 時切換為 Ragdoll 沿攻擊方向被擊飛。
/// 不修改任何既有程式碼,需同一 GameObject 已掛好 TestRagdollEnemy。
/// </summary>
public class TestMonsterPunchingBag : MonoBehaviour, IHitReceiver
{
    [Header("受擊規則")]
    [SerializeField, Tooltip("觸發 Ragdoll 擊飛所需的命中次數"), Min(1)]
    private int _maxHits = 3;
    [SerializeField, Tooltip("Ragdoll 後是否阻擋後續攻擊 (wasBlocked = true)")]
    private bool _ignoreHitsWhenDown = true;

    [Header("擊飛力道")]
    [SerializeField, Tooltip("沿攻擊方向擊飛 (關閉則沿本物件 forward)")]
    private bool _launchAlongAttackDir = true;
    [SerializeField, Tooltip("HitContext.knockbackForce 乘數"), Min(0f)]
    private float _forceMultiplier = 2f;
    [SerializeField, Tooltip("當 HitContext.knockbackForce = 0 時改用此力道"), Min(0f)]
    private float _fallbackForce = 1200f;
    [SerializeField, Tooltip("向上推力修正"), Min(0f)]
    private float _upwardsModifier = 0.8f;
    [SerializeField, Tooltip("模擬爆炸半徑 (計算 AddExplosionForce 時用,不是實際範圍)")]
    private float _virtualBlastRadius = 3f;

    [Header("受擊動畫 (選填)")]
    [SerializeField, Tooltip("用於播受擊動畫的 AnimancerComponent (留空略過)")]
    private AnimancerComponent _animancer;
    [SerializeField, Tooltip("受擊動畫 (One-shot)")]
    private ClipTransition _hitReactAnim;
    [SerializeField, Tooltip("受擊結束後回到的待機動畫 (選填)")]
    private ClipTransition _idleAnim;

    [Header("受擊 VFX / SFX")]
    [SerializeField, Tooltip("命中 VFX Prefab,於 hitPoint 生成,朝 hitNormal")]
    private GameObject _hitVfxPrefab;
    [SerializeField, Tooltip("VFX 存活秒數")]
    private float _hitVfxLifetime = 2f;
    [SerializeField, Tooltip("命中音效")]
    private AudioClip _hitSound;
    [SerializeField, Range(0f, 1f), Tooltip("命中音效音量")]
    private float _hitVolume = 1f;

    [Header("受擊震動 (選填)")]
    [SerializeField, Tooltip("要做受擊震動的視覺子物件 (留空則不震動;通常是 Mesh 根物件)")]
    private Transform _visualRoot;
    [SerializeField, Tooltip("震動幅度 (公尺)"), Min(0f)]
    private float _punchShakeStrength = 0.08f;
    [SerializeField, Tooltip("震動時長"), Min(0f)]
    private float _punchShakeDuration = 0.25f;

    [Header("Debug")]
    [SerializeField, Tooltip("顯示當前命中計數 log")]
    private bool _logHits = true;

    private int _hitCount;
    private bool _down;
    private TestRagdollEnemy _ragdoll;
    private Vector3 _visualInitialLocalPos;

    public int HitCount => _hitCount;
    public bool IsDown => _down;

    private void Awake()
    {
        _ragdoll = GetComponent<TestRagdollEnemy>();
        if (_ragdoll == null)
        {
            Debug.LogWarning("[TestMonsterPunchingBag] 缺少 TestRagdollEnemy 元件,擊飛不會生效", this);
        }
        if (_animancer == null)
        {
            _animancer = GetComponentInChildren<AnimancerComponent>();
        }
        if (_visualRoot != null)
        {
            _visualInitialLocalPos = _visualRoot.localPosition;
        }
    }

    public void OnHit(ref HitContext ctx)
    {
        if (_down)
        {
            ctx.wasBlocked = _ignoreHitsWhenDown;
            return;
        }
        _hitCount++;
        if (_logHits)
        {
            Debug.Log($"[TestMonsterPunchingBag] '{name}' 受擊 {_hitCount}/{_maxHits}");
        }
        SpawnHitVfx(ctx.hitPoint, ctx.hitNormal);
        PlayHitSound(ctx.hitPoint);
        if (_hitCount >= _maxHits)
        {
            LaunchAsRagdoll(ctx);
            return;
        }
        PlayHitReactAnim();
        ShakeVisual();
    }

    private void PlayHitReactAnim()
    {
        if (_animancer == null || _hitReactAnim == null) return;
        AnimancerState state = _animancer.Play(_hitReactAnim);
        if (_idleAnim != null)
        {
            state.Events(this).OnEnd = ReturnToIdle;
        }
    }

    private void ReturnToIdle()
    {
        if (_down || _animancer == null || _idleAnim == null) return;
        _animancer.Play(_idleAnim);
    }

    private void SpawnHitVfx(Vector3 pos, Vector3 normal)
    {
        if (_hitVfxPrefab == null) return;
        Quaternion rot = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        GameObject vfx = Instantiate(_hitVfxPrefab, pos, rot);
        Destroy(vfx, _hitVfxLifetime > 0f ? _hitVfxLifetime : 2f);
    }

    private void PlayHitSound(Vector3 pos)
    {
        if (_hitSound == null) return;
        AudioSource.PlayClipAtPoint(_hitSound, pos, _hitVolume);
    }

    private void ShakeVisual()
    {
        if (_visualRoot == null || _punchShakeStrength <= 0f || _punchShakeDuration <= 0f) return;
        _visualRoot.DOKill(true);
        _visualRoot.localPosition = _visualInitialLocalPos;
        _visualRoot.DOShakePosition(_punchShakeDuration, _punchShakeStrength, 18, 90f, false, true)
            .SetLink(_visualRoot.gameObject)
            .OnComplete(OnShakeComplete);
    }

    private void OnShakeComplete()
    {
        if (_visualRoot != null)
        {
            _visualRoot.localPosition = _visualInitialLocalPos;
        }
    }

    private void LaunchAsRagdoll(HitContext ctx)
    {
        _down = true;
        if (_visualRoot != null)
        {
            _visualRoot.DOKill(true);
            _visualRoot.localPosition = _visualInitialLocalPos;
        }
        if (_ragdoll == null) return;
        float baseForce = ctx.knockbackForce > 0f ? ctx.knockbackForce : _fallbackForce;
        float finalForce = baseForce * Mathf.Max(0.01f, _forceMultiplier);
        Vector3 dir = ResolveLaunchDirection(ctx);
        Vector3 targetPos = transform.position + Vector3.up;
        Vector3 explosionPoint = targetPos - dir * (_virtualBlastRadius * 0.85f);
        _ragdoll.Explode(explosionPoint, _virtualBlastRadius, finalForce, _upwardsModifier);
    }

    private Vector3 ResolveLaunchDirection(HitContext ctx)
    {
        if (_launchAlongAttackDir && ctx.attackDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = ctx.attackDirection;
            dir.y = Mathf.Max(dir.y, 0f);
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            return dir.normalized;
        }
        return -transform.forward;
    }

    /// <summary>把沙包重置回站立狀態,供重複演示</summary>
    public void ResetBag()
    {
        _hitCount = 0;
        _down = false;
        if (_ragdoll != null)
        {
            _ragdoll.SetRagdollActive(false);
        }
        if (_visualRoot != null)
        {
            _visualRoot.DOKill(true);
            _visualRoot.localPosition = _visualInitialLocalPos;
        }
        if (_animancer != null && _idleAnim != null)
        {
            _animancer.Play(_idleAnim);
        }
    }
}
