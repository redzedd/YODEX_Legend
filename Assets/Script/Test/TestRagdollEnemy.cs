using UnityEngine;

/// <summary>
/// 宣傳片演示用敵人:平時站立播動畫,被爆炸觸發時切換到 Ragdoll 並被推飛。
/// 設定流程:
/// 1. 放好 Humanoid 角色 Prefab
/// 2. Unity 選單 GameObject → 3D Object → Ragdoll...,把各部位骨骼拖進 wizard 生成 Rigidbody/Collider/CharacterJoint
/// 3. 在角色根物件掛上本元件,Ragdoll 骨骼欄位留空會自動掃描 child
/// </summary>
public class TestRagdollEnemy : MonoBehaviour
{
    [Header("平時驅動")]
    [SerializeField, Tooltip("平時動畫驅動,Ragdoll 時會停用 (留空自動於 Awake 抓 child)")]
    private Animator _animator;
    [SerializeField, Tooltip("Ragdoll 時一併停用的其他元件 (例如 CharacterController / NavMeshAgent / 自訂 AI)")]
    private Behaviour[] _disableOnRagdoll;

    [Header("Ragdoll 物理")]
    [SerializeField, Tooltip("Ragdoll 骨骼 Rigidbody (留空於 Awake 自動從 child 抓取)")]
    private Rigidbody[] _ragdollBodies;
    [SerializeField, Tooltip("從平常狀態轉為 Ragdoll 前要歸零的額外 Collider (主體 Collider),留空略過")]
    private Collider _mainCollider;

    [Header("Debug")]
    [SerializeField, Tooltip("啟動時印出找到的 Ragdoll 骨骼數量")]
    private bool _logSetup = true;

    private bool _ragdolled;

    public bool IsRagdolled => _ragdolled;

    private void Awake()
    {
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>();
        }
        if (_ragdollBodies == null || _ragdollBodies.Length == 0)
        {
            _ragdollBodies = GetComponentsInChildren<Rigidbody>();
        }
        if (_ragdollBodies.Length == 0)
        {
            Debug.LogWarning($"[TestRagdollEnemy] '{name}' 未找到任何 Rigidbody,Ragdoll 無法觸發。請先用 GameObject → 3D Object → Ragdoll... 產生骨骼物理。");
        }
        else if (_logSetup)
        {
            Debug.Log($"[TestRagdollEnemy] '{name}' 已註冊 {_ragdollBodies.Length} 根 Ragdoll 骨骼");
        }
        SetRagdollActive(false);
    }

    public void SetRagdollActive(bool active)
    {
        _ragdolled = active;
        if (_animator != null)
        {
            _animator.enabled = !active;
        }
        if (_mainCollider != null)
        {
            _mainCollider.enabled = !active;
        }
        if (_disableOnRagdoll != null)
        {
            for (int i = 0; i < _disableOnRagdoll.Length; i++)
            {
                if (_disableOnRagdoll[i] != null) _disableOnRagdoll[i].enabled = !active;
            }
        }
        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb == null) continue;
            rb.isKinematic = !active;
            rb.useGravity = active;
            if (!active)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    /// <summary>由爆炸等外部事件呼叫,觸發 Ragdoll 並將所有骨骼依爆炸位置做推飛。</summary>
    public void Explode(Vector3 position, float radius, float force, float upwardsModifier)
    {
        if (_ragdolled)
        {
            ApplyExplosionForceToBones(position, radius, force, upwardsModifier);
            return;
        }
        SetRagdollActive(true);
        ApplyExplosionForceToBones(position, radius, force, upwardsModifier);
    }

    private void ApplyExplosionForceToBones(Vector3 position, float radius, float force, float upwardsModifier)
    {
        for (int i = 0; i < _ragdollBodies.Length; i++)
        {
            Rigidbody rb = _ragdollBodies[i];
            if (rb != null && !rb.isKinematic)
            {
                rb.AddExplosionForce(force, position, radius, upwardsModifier, ForceMode.Impulse);
            }
        }
    }
}
