using UnityEngine;

/// <summary>
/// 宣傳片演示用的簡易箭矢 Projectile,
/// 將此腳本掛在 TestPlayerDemo._arrowPrefab 引用的 Prefab 上即可。
/// 飛行時自動對齊速度方向,逾時或命中後銷毀。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class TestArrowProjectile : MonoBehaviour
{
    [SerializeField, Tooltip("逾時自動銷毀秒數")]
    private float _lifetime = 5f;
    [SerializeField, Tooltip("飛行速度 (公尺/秒,會被 TestPlayerDemo 覆寫,預設 60 接近薩爾達感)")]
    private float _launchSpeed = 60f;
    [SerializeField, Range(0f, 1f), Tooltip("重力倍率 (會被 TestPlayerDemo 覆寫,預設 0.3 接近薩爾達感)")]
    private float _gravityScale = 0.3f;
    [SerializeField, Tooltip("是否在飛行中對齊速度方向")]
    private bool _alignToVelocity = true;
    [SerializeField, Tooltip("命中物件後是否銷毀")]
    private bool _destroyOnHit = true;
    [SerializeField, Tooltip("命中後將箭矢父子化到被命中物 (插箭效果)")]
    private bool _attachOnHit = true;

    private Rigidbody _rigidbody;
    private bool _hasHit;

    public float LaunchSpeed
    {
        get => _launchSpeed;
        set => _launchSpeed = value;
    }

    public float GravityScale
    {
        get => _gravityScale;
        set => _gravityScale = value;
    }

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.useGravity = false;
    }

    private void Start()
    {
        Destroy(gameObject, _lifetime);
    }

    private void FixedUpdate()
    {
        if (_hasHit) return;
        _rigidbody.AddForce(Physics.gravity * _gravityScale, ForceMode.Acceleration);
        if (!_alignToVelocity) return;
        Vector3 velocity = _rigidbody.linearVelocity;
        if (velocity.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.LookRotation(velocity);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasHit) return;
        _hasHit = true;
        if (_attachOnHit)
        {
            _rigidbody.isKinematic = true;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            transform.SetParent(collision.transform, worldPositionStays: true);
        }
        if (_destroyOnHit)
        {
            Destroy(gameObject, _attachOnHit ? 2f : 0f);
        }
    }
}
