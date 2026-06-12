using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 遠程攻擊上半身瞄準 IK
    /// 掛在 Animator(Humanoid)物件上,接收 Solver 解析的瞄準位置,
    /// 透過 OnAnimatorIK 驅動 Body/Head/Eyes 看向目標(支援權重淡入淡出)
    /// 需求: Animator 為 Humanoid Avatar
    /// 自動啟用 Animancer Layer[0] 的 ApplyAnimatorIK(專案無 Animator Controller),
    /// 若有 AnimatorController 則需自行勾選 IK Pass
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class RangedAimIK : MonoBehaviour
    {
        [Header("IK Weights")]
        [Tooltip("整體 LookAt 強度(總開關,0=不啟用 IK)")]
        [Range(0f, 1f)]
        [SerializeField] private float _overallWeight = 1f;

        [Tooltip("身體跟隨強度(脊椎)")]
        [Range(0f, 1f)]
        [SerializeField] private float _bodyWeight = 0.4f;

        [Tooltip("頭部跟隨強度")]
        [Range(0f, 1f)]
        [SerializeField] private float _headWeight = 1f;

        [Tooltip("眼睛跟隨強度(僅 Avatar 有設定 eyes 才有效)")]
        [Range(0f, 1f)]
        [SerializeField] private float _eyesWeight = 0f;

        [Tooltip("夾角限制(0=完全跟隨頭部旋轉,1=不轉動)")]
        [Range(0f, 1f)]
        [SerializeField] private float _clampWeight = 0.3f;

        [Header("Smoothing")]
        [Tooltip("權重淡入淡出時間(秒),避免突然轉頭")]
        [SerializeField] private float _smoothTime = 0.15f;

        [Tooltip("瞄準位置 SmoothDamp 時間(秒) — 給上半身跟隨相機加一點點延遲感(weighty 感)。\n" +
                 "0 = 完全跟隨即時(無延遲)\n" +
                 "0.05 = 預設,輕微延遲\n" +
                 "0.1+ = 明顯滯後感(像 BOTW 弓)")]
        [SerializeField] private float _positionSmoothTime = 0.05f;

        [Header("Debug")]
        [Tooltip("輸出診斷訊息到 Console,定位 IK 為何不動的問題")]
        [SerializeField] private bool _debugLog;

        private Animator _animator;
        private AnimancerComponent _animancer;
        private Vector3 _aimTargetPosition;
        private Vector3 _smoothedAimPosition;
        private Vector3 _aimVelocity;
        private bool _isAiming;
        private float _currentWeight;
        private float _weightVelocity;
        private float _lastDebugLogTime;

        /// <summary>當前是否正在跟隨目標(權重大於零)</summary>
        public bool IsAiming => _isAiming;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (!_animator.isHuman)
            {
                Debug.LogWarning($"[RangedAimIK] {gameObject.name} 的 Animator 不是 Humanoid Avatar,IK 無法啟用,元件停用。");
                enabled = false;
                return;
            }
            _animancer = GetComponent<AnimancerComponent>();
            if (_animancer == null) _animancer = GetComponentInParent<AnimancerComponent>();
            if (_animancer == null) _animancer = GetComponentInChildren<AnimancerComponent>();
            if (_debugLog)
            {
                string animancerInfo = _animancer != null ? _animancer.gameObject.name : "NULL";
                Debug.Log($"[RangedAimIK] Awake on {gameObject.name}: isHuman=True, animancer={animancerInfo}");
            }
        }

        private void OnEnable()
        {
            // Animancer 模式: 啟用 Layer 0 的 IK Pass(專案沒用 AnimatorController,所以無法在 Animator 上勾)
            // 注意: Layers[0] 會自動建立,不可用 Layers.Count > 0 檢查(那會是首次存取前 = 0)
            if (_animancer != null)
            {
                _animancer.Layers[0].ApplyAnimatorIK = true;
                if (_debugLog) Debug.Log($"[RangedAimIK] OnEnable: ApplyAnimatorIK=true on Layer[0]");
            }
            else if (_debugLog)
            {
                Debug.LogWarning("[RangedAimIK] OnEnable: 沒找到 AnimancerComponent,IK Pass 沒有啟用");
            }
        }

        /// <summary>
        /// 設定瞄準目標(世界座標),啟動 IK 跟隨。
        /// 首次啟動時 snap smoothed 位置到目標,避免從原點 (0,0,0) 平滑飛入造成的詭異首幀
        /// </summary>
        public void SetAimTarget(Vector3 worldPosition)
        {
            if (_debugLog && !_isAiming) Debug.Log($"[RangedAimIK] SetAimTarget: {worldPosition}");
            if (!_isAiming)
            {
                _smoothedAimPosition = worldPosition;
                _aimVelocity = Vector3.zero;
            }
            _aimTargetPosition = worldPosition;
            _isAiming = true;
        }

        /// <summary>
        /// 停止瞄準,IK 權重會在 _smoothTime 內淡出
        /// </summary>
        public void ClearAimTarget()
        {
            if (_debugLog && _isAiming) Debug.Log("[RangedAimIK] ClearAimTarget");
            _isAiming = false;
        }

        private void Update()
        {
            float target = _isAiming ? _overallWeight : 0f;
            _currentWeight = Mathf.SmoothDamp(_currentWeight, target, ref _weightVelocity, _smoothTime);

            // 位置 SmoothDamp — 給上半身跟隨相機加一點點延遲感
            if (_isAiming && _positionSmoothTime > 0f)
            {
                _smoothedAimPosition = Vector3.SmoothDamp(_smoothedAimPosition, _aimTargetPosition, ref _aimVelocity, _positionSmoothTime);
            }
            else
            {
                _smoothedAimPosition = _aimTargetPosition;
                _aimVelocity = Vector3.zero;
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_debugLog && Time.unscaledTime - _lastDebugLogTime > 1f)
            {
                _lastDebugLogTime = Time.unscaledTime;
                Debug.Log($"[RangedAimIK] OnAnimatorIK[layer={layerIndex}] weight={_currentWeight:F2} isAiming={_isAiming}");
            }
            if (_currentWeight < 0.001f) return;
            _animator.SetLookAtPosition(_smoothedAimPosition);
            _animator.SetLookAtWeight(_currentWeight, _bodyWeight, _headWeight, _eyesWeight, _clampWeight);
        }
    }
}
