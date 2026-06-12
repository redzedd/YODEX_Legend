using Animancer;
using UnityEngine;
using Player.Locomotion.States;

namespace Player.Locomotion
{
    /// <summary>
    /// 向量驅動玩家移動主控制器。組合輸入、狀態機、動畫驅動與旋轉邏輯。
    /// 位移由 Animancer 播放的 RootMotion 動畫提供；轉向預設由程式向量對齊，僅在 FastRunTurn 交由 RootMotion。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerLocomotionController : MonoBehaviour
    {
        [Header("設定資產")]
        [SerializeField] private LocomotionConfig _config;
        [SerializeField] private LocomotionAnimationSet _animationSet;

        [Header("元件")]
        [SerializeField] private LocomotionInputReader _inputReader;
        [SerializeField] private AnimancerComponent _animancer;
        [SerializeField] private Animator _animator;
        [SerializeField] private Transform _cameraTransform;

        [Header("除錯")]
        [SerializeField, Tooltip("於 Scene 視圖顯示藍色 Actor Forward 與紅色 Desired Direction 箭頭")]
        private bool _drawDebugArrows = true;
        [SerializeField] private float _debugArrowLength = 2f;

        private CharacterController _characterController;
        private LocomotionStateMachine _stateMachine;
        private LocomotionStateContext _context;
        private LocomotionAnimatorDriver _animatorDriver;
        private float _verticalVelocity;
        private Vector3 _prevHorizontalDelta;
        private float _timeSinceGrounded;
        private float _jumpBufferTimer;

        /// <summary>
        /// 外部抑制 locomotion 處理 — 啟用時 Update 與 OnAnimatorMove 都早退,角色完全凍結。
        /// 由 GA_RangedAttack PlayerCursor 模式蓄力期間設為 true,結束時清回 false。
        /// 跨 asmdef 抑制(GAS asmdef 無法引用 Player.Locomotion tag),用 public 旗標代替 OwnedTags 檢查。
        /// </summary>
        public bool LocomotionSuppressed { get; set; }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }
            _animator.applyRootMotion = true;
            _animatorDriver = new LocomotionAnimatorDriver(_animancer);
            _context = new LocomotionStateContext(_config, _animationSet, _animatorDriver, _inputReader, transform)
            {
                Idle = new IdleState(),
                Walk = new WalkState(),
                Run = new RunState(),
                FastRun = new FastRunState(),
                FastRunTurn = new FastRunTurnState(),
                FastRunStop = new FastRunStopState(),
                Jump = new JumpState(),
            };
            _stateMachine = new LocomotionStateMachine(_context);
            _stateMachine.Start(_context.Idle);
        }

        private void Update()
        {
            if (LocomotionSuppressed) return;
            float deltaTime = Time.deltaTime;
            UpdateInputContext();
            UpdateJumpTimers(deltaTime);
            TryTriggerJump();
            _stateMachine.Tick(deltaTime);
            ApplyScriptedRotation(deltaTime);
        }

        private void UpdateJumpTimers(float deltaTime)
        {
            if (_characterController.isGrounded)
            {
                _timeSinceGrounded = 0f;
            }
            else
            {
                _timeSinceGrounded += deltaTime;
            }
            if (_inputReader.JumpPressedThisFrame)
            {
                _jumpBufferTimer = _config.JumpBufferTime;
            }
            else if (_jumpBufferTimer > 0f)
            {
                _jumpBufferTimer -= deltaTime;
            }
        }

        private void TryTriggerJump()
        {
            if (_context.IsAirborne)
            {
                return;
            }
            if (_stateMachine.Current == _context.FastRunTurn)
            {
                return;
            }
            if (_jumpBufferTimer <= 0f)
            {
                return;
            }
            if (_timeSinceGrounded > _config.CoyoteTime)
            {
                return;
            }
            _jumpBufferTimer = 0f;
            _stateMachine.ChangeState(_context.Jump);
        }

        private void OnAnimatorMove()
        {
            if (LocomotionSuppressed) return;
            if (_characterController == null || _animator == null)
            {
                return;
            }
            float deltaTime = Time.deltaTime;
            Vector3 rawDelta = _animator.deltaPosition;
            bool isAirborne = _context != null && _context.IsAirborne;
            Vector3 horizontalDelta;
            if (isAirborne)
            {
                horizontalDelta = _context.JumpHorizontalVelocity * deltaTime;
            }
            else
            {
                Vector3 rawHorizontal = new Vector3(rawDelta.x, 0f, rawDelta.z);
                horizontalDelta = ApplyHorizontalContinuation(rawHorizontal, deltaTime);
            }
            _prevHorizontalDelta = horizontalDelta;
            if (_context != null && _context.PendingJumpImpulse > 0f)
            {
                _verticalVelocity = _context.PendingJumpImpulse;
                _context.PendingJumpImpulse = 0f;
            }
            else if (_characterController.isGrounded && !isAirborne)
            {
                _verticalVelocity = -1f;
            }
            else
            {
                _verticalVelocity -= _config.Gravity * deltaTime;
            }
            float verticalAnimDelta = isAirborne ? 0f : rawDelta.y;
            Vector3 finalDelta = new Vector3(horizontalDelta.x, verticalAnimDelta + _verticalVelocity * deltaTime, horizontalDelta.z);
            _characterController.Move(finalDelta);
            if (_context != null)
            {
                _context.LastHorizontalVelocity = deltaTime > 0f ? horizontalDelta / deltaTime : Vector3.zero;
            }
            if (_context != null && _context.UseRootMotionRotation)
            {
                transform.rotation *= _animator.deltaRotation;
            }
        }

        private Vector3 ApplyHorizontalContinuation(Vector3 currentHorizontal, float deltaTime)
        {
            // Time.timeScale=0(背包/字卡/寶箱暫停)時 deltaTime=0,decay=1 不衰減會沿用 _prevHorizontalDelta 造成滑行
            if (deltaTime <= 0f)
            {
                return Vector3.zero;
            }
            float tau = _config.HorizontalVelocityContinuationTau;
            if (tau <= 0f)
            {
                return currentHorizontal;
            }
            float decay = Mathf.Exp(-deltaTime / tau);
            Vector3 decayedPrev = _prevHorizontalDelta * decay;
            if (currentHorizontal.sqrMagnitude >= decayedPrev.sqrMagnitude)
            {
                return currentHorizontal;
            }
            float currentMag = currentHorizontal.magnitude;
            float targetMag = decayedPrev.magnitude;
            Vector3 direction = currentMag > 0.0001f ? currentHorizontal / currentMag : decayedPrev.normalized;
            return direction * targetMag;
        }

        private void UpdateInputContext()
        {
            Vector2 raw = _inputReader.RawMove;
            Vector3 desired = BuildCameraRelativeDirection(raw);
            _context.InputMagnitude = Mathf.Clamp01(raw.magnitude);
            _context.DesiredWorldDirection = desired;
            _context.RunButtonHeld = _inputReader.RunHeld;
            _context.IsGrounded = _characterController.isGrounded;
            if (_context.HasMoveInput)
            {
                _context.NoInputTime = 0f;
            }
            else
            {
                _context.NoInputTime += Time.deltaTime;
            }
        }

        private Vector3 BuildCameraRelativeDirection(Vector2 raw)
        {
            float deadzone = _config.IdleDeadzone;
            if (raw.sqrMagnitude < deadzone * deadzone)
            {
                return Vector3.zero;
            }
            Vector3 camForward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
            Vector3 camRight = Vector3.ProjectOnPlane(_cameraTransform.right, Vector3.up).normalized;
            return camForward * raw.y + camRight * raw.x;
        }

        private void ApplyScriptedRotation(float deltaTime)
        {
            if (_context.UseRootMotionRotation)
            {
                return;
            }
            if (_context.DesiredWorldDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }
            if (_context.CurrentRotationSpeed <= 0f)
            {
                return;
            }
            transform.rotation = LocomotionRotator.Step(transform.rotation, _context.DesiredWorldDirection, _context.CurrentRotationSpeed, deltaTime);
        }

        private bool ValidateReferences()
        {
            if (_config == null)
            {
                Debug.LogError("[PlayerLocomotionController] LocomotionConfig 未指定。", this);
                return false;
            }
            if (_animationSet == null)
            {
                Debug.LogError("[PlayerLocomotionController] LocomotionAnimationSet 未指定。", this);
                return false;
            }
            if (_inputReader == null)
            {
                Debug.LogError("[PlayerLocomotionController] LocomotionInputReader 未指定。", this);
                return false;
            }
            if (_animancer == null || _animator == null)
            {
                Debug.LogError("[PlayerLocomotionController] Animancer / Animator 未指定。", this);
                return false;
            }
            if (_cameraTransform == null)
            {
                Debug.LogError("[PlayerLocomotionController] 攝影機 Transform 未指定。", this);
                return false;
            }
            return true;
        }

        private void OnDrawGizmos()
        {
            if (!_drawDebugArrows || !Application.isPlaying || _context == null)
            {
                return;
            }
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(origin, transform.forward * _debugArrowLength);
            Gizmos.color = Color.red;
            Gizmos.DrawRay(origin, _context.DesiredWorldDirection * _debugArrowLength);
        }
    }
}
