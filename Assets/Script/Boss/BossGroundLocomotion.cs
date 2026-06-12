using UnityEngine;

namespace Boss
{
    /// <summary>
    /// Boss 地面移動元件 — Root Motion 驅動
    /// 前進位移完全由動畫自帶的 Root Motion 提供(動畫請用「帶位移」版本,非原地版),
    /// 本元件只負責三件事:轉身面向、重力、把 Root Motion 餵給 CharacterController。
    /// FSM / Boss 控制器透過 MoveTo / Stop / SetFacing 控制「要面向哪裡」,
    /// 實際前進多快由當前播放的動畫(走 / 跑 / 攻擊)決定。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class BossGroundLocomotion : MonoBehaviour
    {
        #region Serialized Fields

        [Header("元件引用 (留空 Awake 自動抓)")]
        [SerializeField] [Tooltip("CharacterController — 同物件上")]
        private CharacterController _characterController;

        [Header("物理")]
        [SerializeField] [Tooltip("重力倍率 — 乘在 Physics.gravity.y 上。1 = 標準重力,大型 Boss 建議 1.5~3 有落地感")]
        private float _gravityMultiplier = 2f;

#if UNITY_EDITOR
        [Header("測試 (僅 Editor 啟用 — Release Build 自動移除)")]
        [SerializeField] [Tooltip("Play 模式按此鍵: 面向下方測試目的地 (Root Motion 模式下只會轉身,前進需有帶位移的動畫在播)")]
        private KeyCode _testFaceKey = KeyCode.M;

        [SerializeField] [Tooltip("Play 模式按此鍵: 停止 (清除目的地與轉身)")]
        private KeyCode _testStopKey = KeyCode.N;

        [SerializeField] [Tooltip("測試面向目的地 (世界座標)")]
        private Vector3 _testDestination = new Vector3(10f, 0f, 0f);

        [SerializeField] [Tooltip("測試轉身速度 (度/秒)")]
        private float _testRotationSpeed = 270f;
#endif

        #endregion

        #region Private Fields

        private Animator _animator;
        private Vector3 _destination;
        private bool _hasDestination;
        private float _stopDistance;
        private Vector3 _facingDirection;
        private float _rotationSpeed;
        private float _verticalVelocity;
        // 由外部系統(如攻擊執行器的 ManualLerp)累積的水平位移,下次 Root Motion 套用時一併加進去
        // 用累積模式避免一幀內 CC.Move 被呼叫多次(Unity 對連續呼叫的行為不可靠 → 等於白 Move)
        private Vector3 _pendingExternalHorizontalDelta;

        // 著地速度鎖 — 避免 isGrounded 在 0 速度時跳動誤判離地
        private const float GROUND_STICK_VELOCITY = -2f;

        #endregion

        #region Properties

        /// <summary>是否還沒抵達目的地 (有設定目的地且未到達)</summary>
        public bool IsMoving => _hasDestination;

        /// <summary>是否已抵達目的地 (或從未設定過目的地)</summary>
        public bool HasReachedDestination => !_hasDestination;

        /// <summary>上一幀實際套用的水平速度向量 (公尺/秒) — 來自 Root Motion</summary>
        public Vector3 CurrentVelocity { get; private set; }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_characterController == null)
                _characterController = GetComponent<CharacterController>();

            // Animator 通常在視覺模型子物件上(跟 AnimancerComponent 同物件)
            _animator = GetComponentInChildren<Animator>(true);
            if (_animator != null)
            {
                _animator.applyRootMotion = true;
                BossAnimatorRelay relay = _animator.GetComponent<BossAnimatorRelay>();
                if (relay == null) relay = _animator.gameObject.AddComponent<BossAnimatorRelay>();
                relay.Initialize(this);
            }
            else
            {
                Debug.LogWarning($"[{name}] BossGroundLocomotion 找不到 Animator — Root Motion 無法驅動,Boss 不會移動。請確認模型子物件上有 Animator / AnimancerComponent", this);
            }
        }

        private void Update()
        {
            EnsureRootMotionActive();
            UpdateDestinationReached();
            ApplyRotation();
#if UNITY_EDITOR
            HandleTestInput();
#endif
        }

        private void LateUpdate()
        {
            // 沒有 Animator(Root Motion 無法驅動)時,仍在這裡套用重力避免 Boss 浮空
            if (_animator == null)
            {
                ApplyRootMotion(Vector3.zero);
                return;
            }
            // 兜底:若這幀 OnAnimatorMove 沒觸發(Animator 被剔除等),仍把累積的外部位移送出
            if (_pendingExternalHorizontalDelta.sqrMagnitude > 0.0000001f)
            {
                ApplyRootMotion(Vector3.zero);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 設定要朝向移動的目的地與轉身速度。
        /// Root Motion 模式下:本元件只負責轉身面向目的地,前進位移由當前播放的動畫提供。
        /// moveSpeed 在 Root Motion 模式下被忽略(前進速度由動畫決定),保留參數維持 API 一致。
        /// stopDistance 用來判定 HasReachedDestination(中心到中心),不影響實際位移。
        /// </summary>
        public void MoveTo(Vector3 worldDestination, float moveSpeed, float stopDistance, float rotationSpeed)
        {
            _destination = worldDestination;
            _hasDestination = true;
            _stopDistance = Mathf.Max(0f, stopDistance);
            _rotationSpeed = Mathf.Max(0f, rotationSpeed);

            Vector3 toTarget = worldDestination - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
                _facingDirection = toTarget.normalized;
        }

        /// <summary>清除目的地(視為已抵達)。保留當前面向繼續轉身,如需停止旋轉請另呼叫 ClearFacing</summary>
        public void Stop()
        {
            _hasDestination = false;
            CurrentVelocity = Vector3.zero;
        }

        /// <summary>設定持續面向 — Stop 後仍會持續轉身直到 ClearFacing</summary>
        public void SetFacing(Vector3 worldDirection, float rotationSpeed)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                _facingDirection = Vector3.zero;
                return;
            }
            _facingDirection = worldDirection.normalized;
            _rotationSpeed = Mathf.Max(0f, rotationSpeed);
        }

        /// <summary>清除主動轉身 (停止旋轉)</summary>
        public void ClearFacing()
        {
            _facingDirection = Vector3.zero;
        }

        /// <summary>
        /// 累積一筆外部水平位移(公尺)— 下次 Root Motion 套用時一併 CC.Move。
        /// 用途:攻擊執行器的 ManualLerp、特殊推進需求。Y 軸自動歸零(重力由本元件流程處理)。
        /// </summary>
        public void AddExternalHorizontalMovement(Vector3 worldDelta)
        {
            worldDelta.y = 0f;
            _pendingExternalHorizontalDelta += worldDelta;
        }

        /// <summary>
        /// Root Motion 接收 — 由子物件 BossAnimatorRelay.OnAnimatorMove 轉發。
        /// 1. 截掉 Y 軸位移(避免攻擊動畫抬腳 / 蹲下的 Y Root Motion 把 Boss 推上空中)
        /// 2. 併入外部累積位移,CC.Move 一幀只呼叫一次
        /// 3. 重力累積:著地 → 鎖 GROUND_STICK_VELOCITY;離地 → 累積 Physics.gravity.y
        /// 4. CC.Move(水平 Root Motion + 重力 Y);沒 CC 時 fallback 直接寫 transform(無重力)
        /// </summary>
        public void ApplyRootMotion(Vector3 deltaPosition)
        {
            Vector3 delta = deltaPosition;
            delta.y = 0f;
            delta += _pendingExternalHorizontalDelta;
            _pendingExternalHorizontalDelta = Vector3.zero;

            float dt = Time.deltaTime;
            CurrentVelocity = dt > 0f ? delta / dt : Vector3.zero;

            if (_characterController != null && _characterController.enabled)
            {
                if (_characterController.isGrounded)
                {
                    if (_verticalVelocity < 0f) _verticalVelocity = GROUND_STICK_VELOCITY;
                }
                else
                {
                    _verticalVelocity += Physics.gravity.y * _gravityMultiplier * dt;
                }
                delta.y = _verticalVelocity * dt;
                _characterController.Move(delta);
            }
            else
            {
                if (delta.sqrMagnitude < 0.0000001f) return;
                transform.position += delta;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 每幀確保 Animator.applyRootMotion = true。
        /// 招架彈刀 / 動畫切換可能讓 applyRootMotion 失效,導致之後動畫推不動 Boss(root motion = 0)。
        /// </summary>
        private void EnsureRootMotionActive()
        {
            if (_animator == null) return;
            if (!_animator.applyRootMotion) _animator.applyRootMotion = true;
        }

        /// <summary>判定是否抵達目的地 — 只更新 IsMoving / HasReachedDestination 查詢狀態,不產生位移</summary>
        private void UpdateDestinationReached()
        {
            if (!_hasDestination) return;
            Vector3 toTarget = _destination - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude <= _stopDistance)
            {
                _hasDestination = false;
                CurrentVelocity = Vector3.zero;
            }
        }

        private void ApplyRotation()
        {
            if (_facingDirection.sqrMagnitude < 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(_facingDirection);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, _rotationSpeed * Time.deltaTime);
        }

#if UNITY_EDITOR
        private void HandleTestInput()
        {
            if (Input.GetKeyDown(_testFaceKey))
                MoveTo(_testDestination, 0f, 0f, _testRotationSpeed);
            if (Input.GetKeyDown(_testStopKey))
            {
                Stop();
                ClearFacing();
            }
        }
#endif

        #endregion
    }
}
