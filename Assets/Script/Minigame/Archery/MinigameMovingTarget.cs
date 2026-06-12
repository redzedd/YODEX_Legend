using System;
using DG.Tweening;
using UnityEngine;
using GAS;

namespace Minigame.Archery
{
    /// <summary>
    /// 射箭小遊戲 — 移動靶心（只接弓箭，不接 AOE）
    /// 偵測方式：同時支援 OnTriggerEnter 與 OnCollisionEnter — 兩種 Collider 設定都能用
    ///   - 推薦：實體 Collider（不勾 Is Trigger）→ 箭會物理碰撞並釘在靶上，命中手感最好
    ///   - 備案：Trigger Collider → 箭直接穿透但仍會擊落靶心
    /// 不掛 AbilitySystemComponent，因此 AOE 的 OverlapSphere 會跳過此物件（filter 條件：ASC != null）
    /// 移動模式四選一：
    ///   Stationary       — 完全靜止
    ///   LinearOscillate  — 沿指定軸從生成點向兩側來回移動（左右擺、上下擺、前後擺）
    ///   CircularOrbit    — 以生成點為圓心做水平/垂直繞圈
    ///   WaypointYoyo     — 在 _startPoint / _endPoint 之間 Yoyo 來回
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MinigameMovingTarget : MonoBehaviour
    {
        public enum MovementMode
        {
            Stationary,
            LinearOscillate,
            CircularOrbit,
            WaypointYoyo
        }

        public enum LinearAxis
        {
            X_LeftRight,
            Y_UpDown,
            Z_ForwardBack
        }

        public enum OrbitPlane
        {
            Horizontal_XZ,
            Vertical_XY,
            Vertical_YZ
        }

        [Header("移動模式")]
        [Tooltip("選擇靶心的移動方式")]
        [SerializeField] private MovementMode _mode = MovementMode.LinearOscillate;

        [Header("LinearOscillate — 沿軸線左右/上下/前後擺動")]
        [Tooltip("擺動軸線（X = 左右、Y = 上下、Z = 前後）")]
        [SerializeField] private LinearAxis _linearAxis = LinearAxis.X_LeftRight;

        [Tooltip("從生成點向單側延伸的距離（總擺幅為此值的 2 倍）。建議 1~4 公尺")]
        [SerializeField] private float _linearAmplitude = 2f;

        [Tooltip("一次來回（左 → 右 → 左）所需秒數。數值越小越快。建議 1.5~4")]
        [SerializeField] private float _linearCycleDuration = 2.5f;

        [Tooltip("擺動 ease 曲線（建議 InOutSine 自然來回）")]
        [SerializeField] private Ease _linearEase = Ease.InOutSine;

        [Header("CircularOrbit — 以生成點為圓心繞圈")]
        [Tooltip("繞圈所在平面（Horizontal_XZ = 水平面、Vertical_XY = 正面、Vertical_YZ = 側面）")]
        [SerializeField] private OrbitPlane _orbitPlane = OrbitPlane.Horizontal_XZ;

        [Tooltip("繞圈半徑。建議 1.5~4 公尺")]
        [SerializeField] private float _orbitRadius = 2f;

        [Tooltip("轉一圈所需秒數。數值越小越快。建議 2~5")]
        [SerializeField] private float _orbitDuration = 3f;

        [Tooltip("勾選 = 反向轉")]
        [SerializeField] private bool _orbitClockwise = false;

        [Header("WaypointYoyo — 兩點來回")]
        [Tooltip("移動起點（拖場景中空 GameObject）")]
        [SerializeField] private Transform _startPoint;

        [Tooltip("移動終點（拖場景中空 GameObject）")]
        [SerializeField] private Transform _endPoint;

        [Tooltip("起點 → 終點所需秒數（速度由此決定，數值越小越快）")]
        [SerializeField] private float _waypointTravelDuration = 2.5f;

        [Tooltip("Waypoint 移動 ease 曲線")]
        [SerializeField] private Ease _waypointEase = Ease.InOutSine;

        [Header("命中後")]
        [Tooltip("被擊中時播放的特效 prefab（可留空）")]
        [SerializeField] private GameObject _hitVFX;

        [Tooltip("被擊中時播放的音效（可留空）")]
        [SerializeField] private AudioClip _hitSFX;

        [Tooltip("命中後延遲秒數才銷毀（讓特效播完）")]
        [SerializeField] private float _destroyDelay = 0.3f;

        /// <summary>靶心被擊落事件 — Controller 訂閱用於記分</summary>
        public event Action<MinigameMovingTarget> OnKilled;

        private Tween _moveTween;
        private Vector3 _spawnPosition;
        private float _orbitAngle;
        private bool _isDead;

        private void Start()
        {
            _spawnPosition = transform.position;
            BeginMovement();
        }

        private void Update()
        {
            // CircularOrbit 用 Update 算位置（DOTween 不擅長真正的圓周運動）
            if (_isDead || _mode != MovementMode.CircularOrbit) return;
            float dir = _orbitClockwise ? -1f : 1f;
            _orbitAngle += (360f / Mathf.Max(0.01f, _orbitDuration)) * Time.deltaTime * dir;
            Vector3 offset = ComputeOrbitOffset(_orbitAngle);
            transform.position = _spawnPosition + offset;
        }

        private void OnDestroy()
        {
            _moveTween?.Kill();
        }

        private void BeginMovement()
        {
            switch (_mode)
            {
                case MovementMode.Stationary: break;
                case MovementMode.LinearOscillate: BeginLinearOscillate(); break;
                case MovementMode.CircularOrbit: _orbitAngle = 0f; break;
                case MovementMode.WaypointYoyo: BeginWaypointYoyo(); break;
            }
        }

        private void BeginLinearOscillate()
        {
            Vector3 axis = LinearAxisToVector(_linearAxis);
            Vector3 from = _spawnPosition - axis * _linearAmplitude;
            Vector3 to = _spawnPosition + axis * _linearAmplitude;
            transform.position = from;
            _moveTween = transform
                .DOMove(to, _linearCycleDuration * 0.5f)
                .SetEase(_linearEase)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject);
        }

        private void BeginWaypointYoyo()
        {
            if (_startPoint == null || _endPoint == null) return;
            transform.position = _startPoint.position;
            _moveTween = transform
                .DOMove(_endPoint.position, _waypointTravelDuration)
                .SetEase(_waypointEase)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject);
        }

        private Vector3 LinearAxisToVector(LinearAxis axis)
        {
            return axis switch
            {
                LinearAxis.X_LeftRight => Vector3.right,
                LinearAxis.Y_UpDown => Vector3.up,
                LinearAxis.Z_ForwardBack => Vector3.forward,
                _ => Vector3.right
            };
        }

        private Vector3 ComputeOrbitOffset(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad) * _orbitRadius;
            float s = Mathf.Sin(rad) * _orbitRadius;
            return _orbitPlane switch
            {
                OrbitPlane.Horizontal_XZ => new Vector3(c, 0f, s),
                OrbitPlane.Vertical_XY => new Vector3(c, s, 0f),
                OrbitPlane.Vertical_YZ => new Vector3(0f, s, c),
                _ => new Vector3(c, 0f, s)
            };
        }

        private void OnTriggerEnter(Collider other)
        {
            TryRegisterArrowHit(other, other.ClosestPoint(transform.position));
        }

        private void OnCollisionEnter(Collision collision)
        {
            Vector3 contactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.collider.ClosestPoint(transform.position);
            TryRegisterArrowHit(collision.collider, contactPoint);
        }

        private void TryRegisterArrowHit(Collider other, Vector3 hitPoint)
        {
            if (_isDead) return;
            // 弓箭判定：箭矢根節點掛有 ProjectileBehaviour
            ProjectileBehaviour projectile = other.GetComponentInParent<ProjectileBehaviour>();
            if (projectile == null) return;
            Die(hitPoint);
        }

        private void Die(Vector3 hitPoint)
        {
            _isDead = true;
            _moveTween?.Kill();
            if (_hitVFX != null)
                Instantiate(_hitVFX, hitPoint, Quaternion.identity);
            if (_hitSFX != null)
                AudioSource.PlayClipAtPoint(_hitSFX, hitPoint);
            OnKilled?.Invoke(this);
            Destroy(gameObject, _destroyDelay);
        }
    }
}
