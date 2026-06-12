using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using Pathfinding;
using GAS;
using GAS.Targeting.LockOnV2;
using Enemy.AttackSystem;
using DG.Tweening;

namespace EnemyAI
{
    /// <summary>
    /// 受擊反應等級 — 對應動畫優先級的高低
    /// </summary>
    public enum HitReactionLevel
    {
        None = 0,       // 不反應（如：完全免疫）
        Flinch = 1,     // 純抖動 VFX，不切動畫
        Light = 2,      // 切到 HitLight 狀態
        Heavy = 3,      // 切到 HitHeavy 狀態
        Break = 4,      // Poise 歸零，切到 Stagger 狀態（最高優先）
    }

    /// <summary>
    /// Alert 觸發抑制等級 — 控制玩家被偵測時 Alert VFX 跟動畫的播放範圍
    /// </summary>
    public enum AlertSuppression
    {
        None = 0,           // 完整 Alert（VFX + 動畫）— 預設 / Idle/Patrol / Search Phase 3 LookAround 之後
        SkipAnimation = 1,  // 只播 VFX 跳過動畫 — Search Phase 2 期間
        SkipAll = 2,        // 完全不播（連 VFX 也不） — Search Phase 1 期間
    }

    /// <summary>
    /// 動作霸體等級 — 比受擊等級「低」時才會被打斷
    /// </summary>
    public enum ArmorLevel
    {
        None = 0,            // 任何 Light 以上受擊都能打斷（Idle/Patrol/Walk 預設）
        AttackingArmor = 1,  // 攻擊執行期間：Heavy 以上才能打斷
        SuperArmor = 2,      // 蓄力 / 特殊招式：只有 Break 能打斷
        Invulnerable = 3,    // 完全無敵（死亡）
    }
    /// <summary>
    /// 敵人核心控制器 — 最小骨架版
    /// 只負責持有元件引用、基本狀態（血量、死亡、硬直）、提供 API 給 NodeCanvas FSM/BT 呼叫
    /// 不包含任何 AI 決策邏輯（決策全部在 NodeCanvas 圖中）
    /// 血量管理委派給 GAS：CombatAttributeSet 為唯一真相來源
    /// </summary>
    [RequireComponent(typeof(AbilitySystemComponent))]
    public class EnemyController : MonoBehaviour, IHitReceiver, IAttackProfileHost
    {
        #region Serialized Fields

        [Header("Config — 數值設定")]
        [SerializeField] [Tooltip("敵人數值設定資產（移動、感知、戰鬥距離等）")]
        private EnemyConfig _config;

        [SerializeField] [Tooltip("敵人動畫剪輯集合（Idle/Walk/Run 等）")]
        private EnemyAnimationSet _animationSet;

        [Header("元件引用")]
        [SerializeField] [Tooltip("Animancer 元件 — 留空則 Awake 自動 GetComponent")]
        private AnimancerComponent _animancer;

        [Header("場景引用")]
        [SerializeField] [Tooltip("視線起點（通常綁在頭部骨骼）— 留空則使用自身 transform")]
        private Transform _eyePosition;

        [SerializeField] [Tooltip("巡邏路徑點（按順序，Patrol 會循環走訪）— 留空則無法巡邏")]
        private Transform[] _patrolPoints;

        [Header("音效")]
        [SerializeField] [Tooltip("發現玩家時播放（警覺音效）")]
        private AudioClip _alertSfx;

        [SerializeField] [Tooltip("受擊時播放")]
        private AudioClip _hitSfx;

        [SerializeField] [Tooltip("死亡時播放")]
        private AudioClip _deathSfx;

        [Header("受擊回饋（Flinch 抖動）")]
        [SerializeField] [Tooltip("抖動目標 Transform — 建議拖入「視覺容器子物件」（Mesh / Armature 的父）完全隔離。也可拖自身 root（增量模式安全，不會撤銷移動），但 shake offset 會稍微影響 CC 位置精度。留空則不顯示抖動")]
        private Transform _flinchShakeTarget;

        [SerializeField] [Tooltip("抖動時長（秒）— 建議 0.1~0.2")]
        private float _flinchShakeDuration = 0.15f;

        [SerializeField] [Tooltip("抖動位移強度（公尺）— 建議 0.1~0.3。太小會看不出來")]
        private float _flinchShakeStrength = 0.15f;

        [SerializeField] [Tooltip("方向性反推強度（公尺）— 從攻擊方向把模型推一下，給「被打到」的硬感。建議 0.08~0.2")]
        private float _flinchKnockOffset = 0.12f;

        [SerializeField] [Tooltip("旋轉搖晃角度（度）— Pitch/Roll 範圍，給「脖子被震」的真實感。建議 3~8")]
        private float _flinchRotationAngle = 5f;

        [Header("攻擊招式")]
        [SerializeField] [Tooltip("可使用的攻擊招式清單（EnemyAttackProfile）— CombatLoop 會從中隨機/順序挑選")]
        private List<EnemyAttackProfile> _attackProfiles = new List<EnemyAttackProfile>();

        [SerializeField] [Tooltip("攻擊招式選擇方式")]
        private AttackPickMode _attackPickMode = AttackPickMode.Random;

        [Header("VFX 共用設定 (Alert / Stagger / Search 三個 VFX 共用)")]
        [SerializeField] [Tooltip("三個 VFX 共用的彈入時長（秒）— OutBack easing 給「冒出來」的彈跳感。建議 0.2~0.4")]
        private float _vfxScaleInDuration = 0.3f;

        [SerializeField] [Tooltip("三個 VFX 共用的彈出時長（秒）— InBack easing 給「縮回去」的緊縮感。建議 0.15~0.25")]
        private float _vfxScaleOutDuration = 0.2f;

        [Header("Alert 提示器")]
        [SerializeField] [Tooltip("Alert 提示用 Transform — 通常是子物件下的 ParticleSystem 容器。設計師調好位置與 scale，runtime 觸發 Alert 時 scale 從 0 彈入 → 維持 → 彈出（一次性事件）。建議放在頭頂或胸口")]
        private Transform _alertVfx;

        [SerializeField] [Tooltip("Alert VFX 彈入完成後到開始彈出的維持時長（秒）— 越大顯示越久。建議 0.5~2")]
        private float _alertVfxHoldDuration = 1f;

        [Header("Stagger 暈眩特效")]
        [SerializeField] [Tooltip("Stagger 期間的暈眩特效 Transform — 通常是子物件下的 ParticleSystem 容器。先在 Inspector 調好位置與 scale（這個 scale 就是「彈入終點」），程式 runtime 進 Stagger 時 scale 從 0 → 設定值（OutBack 彈跳），離開時縮回 0。留空則不顯示")]
        private Transform _staggerVfx;

        [Header("Search 問號特效")]
        [SerializeField] [Tooltip("Search 階段 2（走到外推位置）時顯示的問號特效 Transform — 通常是子物件下的 ParticleSystem 容器。設計師調好位置與 scale 後，runtime 進入 WalkToExtrapolation 階段時彈入；進 LookAround 或被 Alert 蓋掉時消失")]
        private Transform _searchQuestionVfx;

        [Header("Search 記錄設定")]
        [SerializeField] [Tooltip("敵人丟失視線後繼續追蹤玩家真實位置的「較短時間」（秒）— 此時間結束時記錄 PointA，作為 Search 階段 1 跑步目的地。建議 0.5~1.5")]
        private float _sightLossShortRecordTime = 1f;

        [SerializeField] [Tooltip("敵人丟失視線後繼續追蹤玩家真實位置的「較長時間」（秒）— 此時間結束時記錄 PointB，並切換到 Search state。長於 ShortRecordTime。建議 2~5")]
        private float _sightLossLongRecordTime = 3f;

        #endregion

        #region Private Fields

        private Transform _playerTransform;
        private AbilitySystemComponent _asc;
        private CombatAttributeSet _combatAttributes;
        private bool _isDead;
        private bool _isStaggered;
        private IAstarAI _astarAI;
        private Vector3 _facingDirection;
        private EnemyVisionSensor _vision;
        private AudioSource _audioSource;
        private EnemyAttackExecutor _attackExecutor;
        private CharacterController _characterController;
        private LockOnTarget _lockOnTarget;
        private ArmorLevel _currentArmor = ArmorLevel.None;
        private bool _pendingHitLight;
        private bool _pendingHitHeavy;
        private bool _hasDetectedPlayer;
        private int _attackSequenceIndex;
        // 每招上次被選用的 Time.time — 給 RangeAndWeight 模式查冷卻用
        private Dictionary<EnemyAttackProfile, float> _attackLastUseTime;
        // 玩家 CharacterController 快取 — GetDistanceToPlayer 算邊緣距離用，避免每幀 GetComponentInParent
        private CharacterController _cachedPlayerCC;
        private Transform _cachedPlayerCCSource;
        private float _flinchShakeRemainingTime;
        // 增量模式：保存「上一幀套用的 shake offset」，每幀計算新 offset 跟上幀的 delta，
        // 用 += 疊加到 target.localPosition / localRotation。不依賴 base position，所以 target
        // 即使是 EnemyController 自身的 transform（root）也安全，不會撤銷 CC.Move 推進的位移
        private Vector3 _flinchShakeLastOffsetPos;
        private Quaternion _flinchShakeLastOffsetRot = Quaternion.identity;
        private float _flinchShakeNoiseSeed;
        private Vector3 _flinchKnockDirectionLocal;
        private bool _isInCombat;
        private bool _isInSearch;
        private bool _wantsCombatEntry;
        private bool _shouldPlayAlertFirst;
        // Alert 抑制三段式：None=完整 Alert、SkipAnimation=只播 VFX、SkipAll=完全不播。
        // 由 Combat / Search Phase 切換時透過 Notify* API 設定
        private AlertSuppression _alertSuppression;
        private bool _wasVisiblePrev;
        // 視線中斷後計時，到達 ShortRecordTime / LongRecordTime 時分別記錄 PointA / PointB
        private float _sightLossTimer;
        private Vector3 _searchPointA;
        private bool _hasSearchPointA;
        private Vector3 _searchPointB;
        private bool _hasSearchPointB;
        private Vector3 _playerPrevPosition;
        private bool _hasPlayerPrevPosition;
        private bool _wasHearingPlayerPrev;
        private float _verticalVelocity;
        // 由外部系統（如攻擊執行器的 ManualLerp）累積的水平位移，OnAnimatorMove 在 CC.Move 前一併加進去
        // 用累積模式避免一幀內 CC.Move 被呼叫多次（Unity 對連續呼叫的行為不可靠 → 等於白 Move）
        private Vector3 _pendingExternalHorizontalDelta;
        private Vector3 _staggerVfxOriginalScale;
        private bool _staggerVfxScaleRecorded;
        private Tween _staggerVfxTween;
        private ParticleSystem[] _staggerVfxParticles;
        private Vector3 _searchQuestionVfxOriginalScale;
        private bool _searchQuestionVfxScaleRecorded;
        private Tween _searchQuestionVfxTween;
        private ParticleSystem[] _searchQuestionVfxParticles;
        private Vector3 _alertVfxOriginalScale;
        private bool _alertVfxScaleRecorded;
        private Tween _alertVfxTween;
        private ParticleSystem[] _alertVfxParticles;

        // 著地時鎖定的下壓速度（避免 CharacterController.isGrounded 在 0 速度時跳動誤判離地）
        private const float GROUND_STICK_VELOCITY = -2f;

        #endregion

        #region Properties

        public EnemyConfig Config => _config;
        public EnemyAnimationSet AnimationSet => _animationSet;
        public AnimancerComponent Animancer => _animancer;
        public Transform PlayerTransform => _playerTransform;
        public AbilitySystemComponent ASC => _asc;
        public CombatAttributeSet CombatAttributes => _combatAttributes;
        public float MaxHealth => _combatAttributes != null ? _combatAttributes.MaxHealth.CurrentValue : 0f;
        public float CurrentHealth => _combatAttributes != null ? _combatAttributes.Health.CurrentValue : 0f;
        public bool IsDead => _isDead;
        public bool IsStaggered => _isStaggered;
        public float HealthPercent => _combatAttributes?.HealthPercent ?? 0f;

        /// <summary>玩家是否存在於場景</summary>
        public bool HasPlayerReference => _playerTransform != null;

        /// <summary>巡邏路徑點（Patrol Action 讀取使用）</summary>
        public Transform[] PatrolPoints => _patrolPoints;

        /// <summary>A* 計算出當前該前進的速度向量（用於轉身對齊）</summary>
        public Vector3 DesiredVelocity => _astarAI != null ? _astarAI.desiredVelocity : Vector3.zero;

        /// <summary>是否已抵達 A* 的目的地</summary>
        public bool HasReachedDestination => _astarAI != null && _astarAI.reachedDestination;

        /// <summary>視線起點（未設定則回傳自身 transform）</summary>
        public Transform EyePosition => _eyePosition != null ? _eyePosition : transform;

        /// <summary>當前是否能看到玩家</summary>
        public bool CanSeePlayer => _vision != null && _vision.CanSeePlayer;

        /// <summary>是否曾經偵測到玩家（首次警覺判斷用）</summary>
        public bool HasDetectedPlayer => _hasDetectedPlayer;

        /// <summary>是否有玩家最後已知位置</summary>
        public bool HasLastKnownPosition => _vision != null && _vision.HasLastKnownPosition;

        /// <summary>玩家最後已知位置（脫離視線後仍可追蹤）</summary>
        public Vector3 LastKnownPosition => _vision != null ? _vision.LastKnownPosition : Vector3.zero;

        /// <summary>警覺音效（給 NodeCanvas Action 直接讀）</summary>
        public AudioClip AlertSfx => _alertSfx;

        /// <summary>死亡音效（給 NodeCanvas Action 直接讀）</summary>
        public AudioClip DeathSfx => _deathSfx;

        /// <summary>攻擊執行器（由 NodeCanvas Action 操作）— 同物件上需有 EnemyAttackExecutor</summary>
        public EnemyAttackExecutor AttackExecutor => _attackExecutor;

        /// <summary>攻擊招式清單（給 BT/FSM 讀）</summary>
        public IReadOnlyList<EnemyAttackProfile> AttackProfiles => _attackProfiles;

        /// <summary>是否有任何可用的攻擊招式</summary>
        public bool HasAttackProfiles => _attackProfiles != null && _attackProfiles.Count > 0;

        /// <summary>當前玩家距離下是否有任何招式可釋放（Min/Max Pick Distance 過濾後仍至少一招符合）— 給 CombatLoop 當「進入攻擊」閘門用</summary>
        public bool HasAnyAttackInRange
        {
            get
            {
                if (!HasAttackProfiles) return false;
                float dist = GetDistanceToPlayer();
                for (int i = 0; i < _attackProfiles.Count; i++)
                {
                    if (IsProfileInRange(_attackProfiles[i], dist)) return true;
                }
                return false;
            }
        }

        /// <summary>當前動作的霸體等級（由 NodeCanvas Action 進入時設定）</summary>
        public ArmorLevel CurrentArmor => _currentArmor;

        /// <summary>是否有待處理的輕受擊（給 FSM Condition 用）</summary>
        public bool HasPendingHitLight => _pendingHitLight;

        /// <summary>是否有待處理的重受擊（給 FSM Condition 用）</summary>
        public bool HasPendingHitHeavy => _pendingHitHeavy;

        /// <summary>是否需要先進 Alert State（給 Idle/Patrol → Alert 的 Condition 用）— Alert 動畫播完前都為 true</summary>
        public bool WantsAlertEntry => _wantsCombatEntry && _shouldPlayAlertFirst;

        /// <summary>是否需要進 Combat State（給 Idle/Patrol → Combat、Alert → Combat 的 Condition 用）— 與 WantsAlertEntry 互斥</summary>
        public bool WantsCombatEntry => _wantsCombatEntry && !_shouldPlayAlertFirst;

        /// <summary>Alert 是否被抑制（不是 None）— debug / 對外查詢用</summary>
        public bool IsAlertOnCooldown => _alertSuppression != AlertSuppression.None;

        /// <summary>當前 Alert 抑制等級</summary>
        public AlertSuppression CurrentAlertSuppression => _alertSuppression;

        /// <summary>當前是否處於 Combat 狀態（由 CombatLoopAction 進出時通知）</summary>
        public bool IsInCombat => _isInCombat;

        /// <summary>是否已丟失目標（戰鬥中且長時間記錄已完成）— Combat → Search 的轉移條件</summary>
        public bool HasLostTarget => _isInCombat && _hasSearchPointB;

        /// <summary>當前是否處於 Search 狀態（由 SearchAction 進出時通知）</summary>
        public bool IsInSearch => _isInSearch;

        /// <summary>Search 階段 1 跑步目的地（丟失視線後 ShortRecordTime 秒記錄的玩家位置）</summary>
        public Vector3 SearchPointA => _searchPointA;

        /// <summary>是否已記錄 PointA</summary>
        public bool HasSearchPointA => _hasSearchPointA;

        /// <summary>Search 階段 2 走路目的地（丟失視線後 LongRecordTime 秒記錄的玩家位置）</summary>
        public Vector3 SearchPointB => _searchPointB;

        /// <summary>是否已記錄 PointB</summary>
        public bool HasSearchPointB => _hasSearchPointB;

        #endregion

        #region Events

        /// <summary>受到傷害時觸發，傳入扣除的血量</summary>
        public event Action<float> OnDamaged;

        /// <summary>硬直觸發時</summary>
        public event Action OnStaggered;

        /// <summary>死亡觸發時</summary>
        public event Action OnDied;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_config == null)
            {
                Debug.LogError($"[{name}] 缺少 EnemyConfig — 請在 Inspector 拖入數值設定資產，否則 AI 將無法運作", this);
                enabled = false;
                return;
            }
            // 動畫 / 攻擊執行器在「子物件視覺 prefab」上，邏輯腳本在父物件 — 用 InChildren 抓
            if (_animancer == null) _animancer = GetComponentInChildren<AnimancerComponent>(true);
            _astarAI = GetComponent<IAstarAI>();
            _asc = GetComponent<AbilitySystemComponent>();
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
            _attackExecutor = GetComponentInChildren<EnemyAttackExecutor>(true);
            _characterController = GetComponent<CharacterController>();
            if (_characterController == null)
            {
                Debug.LogWarning($"[{name}] 缺少 CharacterController — Root Motion 將直接寫 transform.position（不檢查碰撞），玩家可能會穿過敵人。建議在根物件加 CharacterController", this);
            }
            // LockOnTarget 為選用 — 沒掛就不能被玩家鎖定;掛了死亡時自動關閉 IsLockable
            TryGetComponent(out _lockOnTarget);
            ConfigureAstarForRootMotion();
            EnableRootMotion();
            _vision = new EnemyVisionSensor(transform, _eyePosition, _config);
            if (_attackExecutor != null)
            {
                // 告訴 Executor 自身樹的根 — 命中判定排除自己用，必須涵蓋父+子整棵樹
                _attackExecutor.SetOwnerRoot(transform);
                _attackExecutor.OnHitConfirmed -= HandleAttackHitConfirmed;
                _attackExecutor.OnHitConfirmed += HandleAttackHitConfirmed;
            }
            InitializeAlertVfx();
            InitializeStaggerVfx();
            InitializeSearchQuestionVfx();
        }

        private void Start()
        {
            FindPlayer();
            InitializeAttributesFromASC();
        }

        private void OnDestroy()
        {
            UnsubscribeAttributes();
            if (_attackExecutor != null)
            {
                _attackExecutor.OnHitConfirmed -= HandleAttackHitConfirmed;
            }
        }

        private void Update()
        {
            if (_isDead) return;
            if (_playerTransform == null) FindPlayer();
            EnsureRootMotionActive();
            _vision?.Tick(_playerTransform, _hasDetectedPlayer);
            DetectVisionRisingEdge();
            TickLostSightTracking();
            TickHearing();
            UpdateRotation();
        }

        /// <summary>
        /// 聽覺偵測 — 玩家在 HearingRadius 內 + 水平速度 ≥ HearingSpeedThreshold 視為「被聽到」
        /// 戰鬥 / 搜索中略過（已經在追擊沒必要再偵測）；其他狀態（Idle/Patrol/Reaction 後）都可觸發
        /// RequestCombatEntry 內部已 dedup（_wantsCombatEntry / _isInCombat 雙閘門），每幀呼叫安全
        /// </summary>
        private void TickHearing()
        {
            // Combat / Search 中已經在追擊玩家，聽覺檢測沒意義
            // 其他狀態（含 HitLight/Stagger 結束後卡在 Idle 的情況）都允許聽覺重新觸發進戰鬥
            if (_isInCombat || _isInSearch || _config == null || _playerTransform == null)
            {
                _wasHearingPlayerPrev = false;
                _hasPlayerPrevPosition = false;
                return;
            }

            Vector3 playerPos = _playerTransform.position;
            bool isHearingPlayer = false;

            if (_hasPlayerPrevPosition)
            {
                Vector3 delta = playerPos - _playerPrevPosition;
                delta.y = 0f;
                float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                float playerSpeed = delta.magnitude / dt;
                if (playerSpeed >= _config.HearingSpeedThreshold)
                {
                    float distSqr = (playerPos - transform.position).sqrMagnitude;
                    float radius = _config.HearingRadius;
                    if (distSqr <= radius * radius)
                    {
                        isHearingPlayer = true;
                    }
                }
            }

            _playerPrevPosition = playerPos;
            _hasPlayerPrevPosition = true;

            if (isHearingPlayer)
            {
                RequestCombatEntry();
            }
            _wasHearingPlayerPrev = isHearingPlayer;
        }

        /// <summary>
        /// 視線中斷後追蹤計時 — Combat 期間每幀 tick
        /// 看得到玩家時 timer 跟記錄狀態都歸零（持續追擊）
        /// 看不到玩家時 timer 倒數，到達 ShortRecordTime 記錄 PointA，到達 LongRecordTime 記錄 PointB（同時 HasLostTarget = true 觸發 Combat → Search 轉移）
        /// </summary>
        private void TickLostSightTracking()
        {
            if (!_isInCombat || _vision == null || _playerTransform == null) return;

            if (_vision.CanSeePlayer)
            {
                _sightLossTimer = 0f;
                _hasSearchPointA = false;
                _hasSearchPointB = false;
                return;
            }

            _sightLossTimer += Time.deltaTime;
            if (!_hasSearchPointA && _sightLossTimer >= _sightLossShortRecordTime)
            {
                _searchPointA = _playerTransform.position;
                _hasSearchPointA = true;
            }
            if (!_hasSearchPointB && _sightLossTimer >= _sightLossLongRecordTime)
            {
                _searchPointB = _playerTransform.position;
                _hasSearchPointB = true;
            }
        }

        /// <summary>
        /// 視覺偵測 — 戰鬥 / 搜索中略過（已經在追擊）；其他狀態下看到玩家就要求進戰鬥
        /// RequestCombatEntry 內部已 dedup（_wantsCombatEntry / _isInCombat 雙閘門），每幀呼叫安全
        /// _alertSuppression 控制是否要播 Alert VFX/動畫（首次偵測=完整 Alert，戰鬥剛結束=SkipAll 直接進）
        /// </summary>
        private void DetectVisionRisingEdge()
        {
            bool isVisible = _vision != null && _vision.CanSeePlayer;
            // Combat / Search 中已經在追擊玩家，再呼叫也只是 no-op；其他狀態（含 Reaction 後卡在 Idle）允許重新進戰鬥
            if (isVisible && !_isInCombat && !_isInSearch)
            {
                RequestCombatEntry();
            }
            _wasVisiblePrev = isVisible;
        }


        /// <summary>
        /// 每幀確保 Animator.applyRootMotion = true
        /// 招架彈刀模式下 PlayParryStagger 切換動畫可能讓 applyRootMotion 失效，
        /// 導致 Stagger 動畫播完後 Walk 動畫無法推動 transform（root motion 為 0）
        /// </summary>
        private void EnsureRootMotionActive()
        {
            if (_animancer == null || _animancer.Animator == null) return;
            if (!_animancer.Animator.applyRootMotion)
            {
                _animancer.Animator.applyRootMotion = true;
            }
        }

        /// <summary>
        /// LateUpdate — 在 Animator 寫骨骼之後套用 Flinch 抖動 offset，避免被骨骼動畫覆寫
        /// 另兜底：若 OnAnimatorMove 這幀沒觸發（Animator 被剔除等），把 _pendingExternalHorizontalDelta 也送出
        /// </summary>
        private void LateUpdate()
        {
            ApplyFlinchShake();
            if (_pendingExternalHorizontalDelta.sqrMagnitude > 0.0000001f)
            {
                ApplyAnimatorRootMotion(Vector3.zero);
            }
        }

        /// <summary>
        /// 受擊抖動 — 增量模式
        /// 每幀計算「當前應有的 shake offset」（衰減 + perlin noise + 方向反推），
        /// 跟上一幀的 offset 算 delta，用 += 疊加到 target.localPosition / localRotation。
        /// 不依賴 base position，所以 target 拉 root 也安全（不會撤銷 CC.Move 推進的位移）。
        /// shake 結束時 current = 0，自動扣掉上幀殘留 offset 還原。
        /// </summary>
        private void ApplyFlinchShake()
        {
            if (_flinchShakeTarget == null) return;

            Vector3 currentOffsetPos = Vector3.zero;
            Quaternion currentOffsetRot = Quaternion.identity;

            if (_flinchShakeRemainingTime > 0f)
            {
                _flinchShakeRemainingTime -= Time.deltaTime;
                float t = Mathf.Clamp01(_flinchShakeRemainingTime / Mathf.Max(0.0001f, _flinchShakeDuration));
                // t² 衰減：第一幀重，快速 fade — 給「snap」打擊感
                float decay = t * t;
                // 方向性反推：第一幀最大，沿攻擊方向把模型推開
                Vector3 knockOffset = _flinchKnockDirectionLocal * (_flinchKnockOffset * decay);
                // 高頻隨機抖動（~65Hz）疊在反推之上
                float noiseTime = (Time.time + _flinchShakeNoiseSeed) * 65f;
                Vector3 randomShake = new Vector3(
                    (Mathf.PerlinNoise(noiseTime, 0.13f) * 2f - 1f),
                    (Mathf.PerlinNoise(0.41f, noiseTime) * 2f - 1f) * 0.3f,
                    (Mathf.PerlinNoise(noiseTime, noiseTime * 0.7f) * 2f - 1f)
                ) * (_flinchShakeStrength * decay);
                currentOffsetPos = knockOffset + randomShake;

                // 旋轉搖晃（Pitch/Roll）— 給「脖子被震」的真實感，Yaw 不轉避免影響面向邏輯
                float rotAngle = _flinchRotationAngle * decay;
                float pitchNoise = Mathf.PerlinNoise(noiseTime * 0.5f, 0.7f) * 2f - 1f;
                float rollNoise = Mathf.PerlinNoise(0.2f, noiseTime * 0.5f) * 2f - 1f;
                currentOffsetRot = Quaternion.Euler(pitchNoise * rotAngle, 0f, rollNoise * rotAngle);
            }

            // 增量套用：扣上幀 offset，加當幀 offset。shake 結束時 current = 0 自動把殘留還原
            Vector3 deltaPos = currentOffsetPos - _flinchShakeLastOffsetPos;
            Quaternion deltaRot = Quaternion.Inverse(_flinchShakeLastOffsetRot) * currentOffsetRot;

            _flinchShakeTarget.localPosition += deltaPos;
            _flinchShakeTarget.localRotation = _flinchShakeTarget.localRotation * deltaRot;

            _flinchShakeLastOffsetPos = currentOffsetPos;
            _flinchShakeLastOffsetRot = currentOffsetRot;
        }

        /// <summary>
        /// 單體架構 fallback — Animator 跟 EnemyController 在同一物件時 Unity 會呼叫這個
        /// 父子分離架構：本 method 不會被呼叫（Animator 不在父物件），由子物件上的 EnemyAnimatorRelay 接手轉發
        /// </summary>
        private void OnAnimatorMove()
        {
            if (_animancer == null || _animancer.Animator == null) return;
            ApplyAnimatorRootMotion(_animancer.Animator.deltaPosition);
        }

        /// <summary>
        /// Root Motion 接收 — 兩個進入點：
        /// 1. 單體架構：EnemyController.OnAnimatorMove → ApplyAnimatorRootMotion
        /// 2. 父子分離：子物件 EnemyAnimatorRelay.OnAnimatorMove → ApplyAnimatorRootMotion
        ///
        /// 處理流程：
        /// 1. 截掉 Y 軸 deltaPosition（避免攻擊動畫腳抬起 / 蹲下的 Y 軸 Root Motion 把敵人推上空中）
        /// 2. 重力累積：CC 著地 → 鎖 GROUND_STICK_VELOCITY；離地 → 累積 Physics.gravity.y
        /// 3. CC.Move(水平 Root Motion + 重力 Y)；沒 CC 時 fallback 到 transform += delta（無重力）
        /// 4. 同步給 A*，避免內部位置與 transform 脫節
        /// </summary>
        public void ApplyAnimatorRootMotion(Vector3 deltaPosition)
        {
            Vector3 delta = deltaPosition;
            delta.y = 0f;
            // 把外部累積的水平位移（如 ManualLerp）一起算進來，CC.Move 才一幀只呼叫一次
            delta += _pendingExternalHorizontalDelta;
            _pendingExternalHorizontalDelta = Vector3.zero;
            if (_characterController != null && _characterController.enabled)
            {
                if (_characterController.isGrounded)
                {
                    if (_verticalVelocity < 0f) _verticalVelocity = GROUND_STICK_VELOCITY;
                }
                else
                {
                    float gravityScale = _config != null ? _config.GravityMultiplier : 1f;
                    _verticalVelocity += Physics.gravity.y * gravityScale * Time.deltaTime;
                }
                delta.y = _verticalVelocity * Time.deltaTime;
                _characterController.Move(delta);
                SyncAstarPosition();
            }
            else
            {
                if (delta.sqrMagnitude < 0.0000001f) return;
                transform.position += delta;
                SyncAstarPosition();
            }
        }

        /// <summary>
        /// 累積一筆外部水平位移（公尺）— 將在下次 OnAnimatorMove 的 CC.Move 一併套用
        /// 用途：攻擊執行器的 ManualLerp、AI 推進需求等。Y 軸會被自動歸零（重力由 CC 流程處理）
        /// </summary>
        public void AddExternalHorizontalMovement(Vector3 worldDelta)
        {
            worldDelta.y = 0f;
            _pendingExternalHorizontalDelta += worldDelta;
        }

        #endregion

        #region Public API

        /// <summary>
        /// 依動畫類型播放對應 ClipTransition（Animancer / AnimationSet 任一缺失或剪輯未指定時靜默回傳 null）
        /// restartIfSame = true 時，即使切到同一個 clip 也會強制從 Time = 0 重播（受擊 retrigger 用）
        /// </summary>
        public AnimancerState PlayAnimation(EnemyAnimationType type, float fadeDuration = 0.25f, bool restartIfSame = false)
        {
            if (_animancer == null || _animationSet == null) return null;
            ClipTransition clip = _animationSet.GetClip(type);
            if (clip == null || clip.Clip == null) return null;
            AnimancerState state = _animancer.Play(clip, fadeDuration);
            if (restartIfSame && state != null) state.Time = 0f;
            return state;
        }

        /// <summary>
        /// 設定 A* 巡航目的地（純路徑規劃，實際位移仍由 Root Motion 提供）
        /// </summary>
        public void SetDestination(Vector3 worldPosition)
        {
            if (_astarAI == null) return;
            _astarAI.destination = worldPosition;
            _astarAI.isStopped = false;
        }

        /// <summary>
        /// 停止 A* 路徑跟隨；不會自動停下動畫，呼叫者需自行切回 Idle 動畫
        /// </summary>
        public void StopMovement()
        {
            ClearFacingDirection();
            if (_astarAI == null) return;
            _astarAI.isStopped = true;
        }

        /// <summary>
        /// 設定持續轉身方向（世界座標方向向量，會被自動水平化）
        /// 持續性語意：設定後每幀朝此方向轉，直到 ClearFacingDirection 或設定新方向
        /// 傳入 Vector3.zero 等同 ClearFacingDirection
        /// </summary>
        public void SetFacingDirection(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            _facingDirection = worldDirection;
        }

        /// <summary>
        /// 清除主動轉身（停止旋轉）
        /// </summary>
        public void ClearFacingDirection()
        {
            _facingDirection = Vector3.zero;
        }

        /// <summary>
        /// 取得到玩家的「邊緣對邊緣」距離（公尺）— 已自動扣掉雙方 CharacterController 半徑 × lossyScale
        /// 玩家不存在時回傳 float.MaxValue；任一方沒 CharacterController 時退化為純中心距離
        /// 縮放敵人/玩家不會影響距離判定（StopDistance / AttackRange / RangeAndWeight 都用這個）
        /// </summary>
        public float GetDistanceToPlayer()
        {
            if (_playerTransform == null) return float.MaxValue;
            float centerDist = Vector3.Distance(transform.position, _playerTransform.position);
            float enemyRadius = GetScaledCapsuleRadius(_characterController);
            float playerRadius = GetScaledCapsuleRadius(GetPlayerCharacterController());
            return Mathf.Max(0f, centerDist - enemyRadius - playerRadius);
        }

        private CharacterController GetPlayerCharacterController()
        {
            if (_playerTransform == null) return null;
            if (_cachedPlayerCCSource != _playerTransform)
            {
                _cachedPlayerCC = _playerTransform.GetComponentInParent<CharacterController>();
                _cachedPlayerCCSource = _playerTransform;
            }
            return _cachedPlayerCC;
        }

        private static float GetScaledCapsuleRadius(CharacterController cc)
        {
            if (cc == null) return 0f;
            Vector3 scale = cc.transform.lossyScale;
            float scaleXZ = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            return cc.radius * scaleXZ;
        }

        /// <summary>
        /// 取得到玩家的方向向量（水平面，已正規化；玩家不存在時回傳 Vector3.zero）
        /// </summary>
        public Vector3 GetDirectionToPlayer()
        {
            if (_playerTransform == null) return Vector3.zero;
            Vector3 dir = _playerTransform.position - transform.position;
            dir.y = 0f;
            return dir.normalized;
        }

        /// <summary>
        /// 直接造成傷害（繞過 GameplayEffect）— 委派給 CombatAttributeSet.ApplyDamage
        /// 一般攻擊應該由攻擊方 ASC.ApplyEffectToTarget 走標準 GAS 流程，這裡只供 AI 內部測試 / 簡單情境使用
        /// </summary>
        public void TakeDamage(float damage)
        {
            if (_isDead || damage <= 0f) return;
            _combatAttributes?.ApplyDamage(damage);
        }

        /// <summary>
        /// IHitReceiver 入口 — 分級受擊判定（對應 ZZZ 風格）
        /// 流程：
        /// 1. 死亡 / Invulnerable → wasBlocked，不處理
        /// 2. 扣血（除非 ctx.gasDamageApplied 已走 GAS 流程）
        /// 3. 失衡中（_isStaggered）：扣血但只 Flinch 抖動，不切 State（保持失衡霸體）
        /// 4. 扣 Poise — 歸零會經 OnPoiseBroken 觸發 TriggerStagger
        /// 5. 沒 Break 的話判定 reaction (Light/Heavy)，比對 _currentArmor 決定切 State 或只 Flinch
        /// </summary>
        public void OnHit(ref HitContext ctx)
        {
            if (_isDead || _currentArmor == ArmorLevel.Invulnerable)
            {
                ctx.wasBlocked = true;
                return;
            }

            // 被攻擊一律請求進戰鬥（RequestCombatEntry 內部已 dedup，_isInCombat 時 early-exit）
            // 涵蓋三種情境：
            //   1. 脫戰下被偷襲 / 衝刺攻擊 → 首次警覺進 Combat（依當前 AlertSuppression 決定 VFX/動畫）
            //   2. 戰鬥中被打 → no-op（RequestCombatEntry 內部 return）
            //   3. 戰鬥剛結束（HitLight/Stagger 後 FSM 暫退到非戰鬥狀態）又被打 → 重新進戰鬥
            MarkPlayerDetected();
            RequestCombatEntry();

            if (!ctx.gasDamageApplied && _combatAttributes != null)
            {
                _combatAttributes.ApplyDamage(ctx.damage);
            }

            if (!ctx.skipHitEffects && _hitSfx != null)
            {
                PlaySfx(_hitSfx);
            }

            // 失衡中：保持霸體，僅顯示抖動，不再切換 reaction state
            if (_isStaggered)
            {
                PlayFlinchShake(ctx.attackDirection);
                return;
            }

            bool brokeThisHit = false;
            if (ctx.poiseDamage > 0f && _combatAttributes != null)
            {
                brokeThisHit = _combatAttributes.ApplyPoiseDamage(ctx.poiseDamage);
            }

            // Break 由 OnPoiseBroken event 自動觸發 TriggerStagger，這裡只負責 Flinch 視覺
            if (brokeThisHit)
            {
                PlayFlinchShake(ctx.attackDirection);
                return;
            }

            HitReactionLevel reaction = ClassifyHitReaction(ctx);
            bool canInterrupt = CanReactionInterruptArmor(reaction, _currentArmor);

            // 不管能否打斷，都有抖動
            PlayFlinchShake(ctx.attackDirection);

            if (!canInterrupt) return;

            if (reaction == HitReactionLevel.Heavy)
            {
                _pendingHitHeavy = true;
            }
            else if (reaction == HitReactionLevel.Light)
            {
                _pendingHitLight = true;
            }
        }

        /// <summary>
        /// 設定當前動作的霸體等級 — 由 NodeCanvas Action 進入時呼叫
        /// 例如：CombatLoop 開始攻擊時設 AttackingArmor，攻擊結束時設回 None
        /// </summary>
        public void SetArmor(ArmorLevel armor)
        {
            _currentArmor = armor;
        }

        /// <summary>
        /// 通知 Controller 已進入 Combat 狀態 — 由 CombatLoopAction.OnExecute 呼叫
        /// 副作用：清除所有 pending 進戰旗標、Alert 抑制設回 None（戰鬥中發現玩家用完整 Alert）、重置視線中斷計時
        /// </summary>
        public void NotifyEnteredCombat()
        {
            _isInCombat = true;
            _wantsCombatEntry = false;
            _shouldPlayAlertFirst = false;
            _alertSuppression = AlertSuppression.None;
            _sightLossTimer = 0f;
            _hasSearchPointA = false;
            _hasSearchPointB = false;
        }

        /// <summary>
        /// 通知 Controller 已退出 Combat 狀態 — 由 CombatLoopAction.OnStop 呼叫
        /// 副作用：Alert 抑制設 SkipAll（進 Search Phase 1 期間發現玩家完全不播 VFX 不播動畫）；死亡時不設
        /// </summary>
        public void NotifyExitedCombat()
        {
            _isInCombat = false;
            if (_isDead) return;
            _alertSuppression = AlertSuppression.SkipAll;
        }

        /// <summary>
        /// 進入 Alert State 時呼叫（由 AlertReactionAction.OnExecute）— 清掉「Alert 那條」轉移旗標，
        /// 但保留 _wantsCombatEntry 為 true，讓 Alert 動畫播完後可以直接接 Combat
        /// </summary>
        public void ConsumeAlertEntryFlag()
        {
            _shouldPlayAlertFirst = false;
        }

        /// <summary>
        /// 標記回「未警覺」狀態 — 視野檢查回到 ViewRadius + 角度限制，最後已知位置 / 外推位置清除
        /// 由 SearchAction.OnExecute 呼叫，讓 Search 期間玩家可以從背後 / 視野外潛行
        /// 注意：不重置 Alert 冷卻（保留「短期內再次偵測仍跳過 Alert 動畫」的行為）
        /// </summary>
        public void MarkUnaware()
        {
            _hasDetectedPlayer = false;
            _vision?.ClearLastKnownPosition();
            _wasVisiblePrev = false;
            _wasHearingPlayerPrev = false;
            // 清掉視線中斷追蹤狀態（避免 Search 期間 _hasSearchPointB 還 true 讓 HasLostTarget 一直為 true）
            _sightLossTimer = 0f;
            _hasSearchPointA = false;
            _hasSearchPointB = false;
        }

        /// <summary>
        /// 通知 Controller 已進入 Search 狀態 — 由 SearchAction.OnExecute 呼叫
        /// 副作用：暫停 Alert 冷卻倒數（避免「搜索 10 秒結果冷卻被吃光」）
        /// </summary>
        public void NotifyEnteredSearch()
        {
            _isInSearch = true;
        }

        /// <summary>
        /// 通知 Controller 已退出 Search 狀態 — 由 SearchAction.OnStop 呼叫
        /// 副作用：若不是轉去 Combat，會徹底重置偵測狀態（_hasDetectedPlayer、視野/聽覺前一幀旗標、Alert 抑制等）
        /// 避免 Search 期間 OnHit/視野/聽覺把旗標寫進去後沒清掉，導致回到 Idle/Patrol 時新事件無法觸發 Combat
        /// </summary>
        public void NotifyExitedSearch()
        {
            _isInSearch = false;
            if (_isInCombat) return;
            _hasDetectedPlayer = false;
            _vision?.ClearLastKnownPosition();
            _wasVisiblePrev = false;
            _wasHearingPlayerPrev = false;
            _hasPlayerPrevPosition = false;
            _wantsCombatEntry = false;
            _shouldPlayAlertFirst = false;
            _alertSuppression = AlertSuppression.None;
            _sightLossTimer = 0f;
            _hasSearchPointA = false;
            _hasSearchPointB = false;
        }

        /// <summary>
        /// 通知 Controller 已進到 Search 階段 2（WalkToPointB） — 由 SearchAction 切到 Phase 2 時呼叫
        /// 副作用：Alert 抑制升級為 SkipAnimation（只播 VFX、跳過動畫）
        /// </summary>
        public void NotifySearchPhase2Started()
        {
            _alertSuppression = AlertSuppression.SkipAnimation;
        }

        /// <summary>
        /// 通知 Controller 已進到 Search 階段 3（LookAround） — 由 SearchAction.EnterLookAround 呼叫
        /// 副作用：Alert 抑制解除（None），敵人再次發現玩家會走完整 Alert 流程（含動畫）
        /// </summary>
        public void NotifyLookAroundReached()
        {
            _alertSuppression = AlertSuppression.None;
        }

        /// <summary>
        /// 清除 pending 受擊旗標 — 由 PlayHitLight / PlayHitHeavy Action 進入時呼叫
        /// 避免 FSM 從 HitLight 退出後又被 AnyState 立刻拉回
        /// </summary>
        public void ConsumePendingHitReactions()
        {
            _pendingHitLight = false;
            _pendingHitHeavy = false;
        }

        /// <summary>
        /// 啟動受擊抖動 — 實際位移在 LateUpdate 內套用（在 Animator 寫骨骼之後加 offset，避免被覆寫）
        /// 重複呼叫會重置剩餘時間達到瞬切重啟效果
        /// attackWorldDirection：攻擊方向（世界座標），用於方向性反推。Vector3.zero 則純隨機抖動
        /// </summary>
        public void PlayFlinchShake(Vector3 attackWorldDirection = default)
        {
            if (_flinchShakeTarget == null) return;
            _flinchShakeRemainingTime = _flinchShakeDuration;
            _flinchShakeNoiseSeed = UnityEngine.Random.value * 1000f;
            if (attackWorldDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 world = attackWorldDirection;
                world.y = 0f;
                world.Normalize();
                _flinchKnockDirectionLocal = transform.InverseTransformDirection(world);
            }
            else
            {
                _flinchKnockDirectionLocal = Vector3.zero;
            }
        }

        /// <summary>
        /// 依 AttackPickMode 選下一招（Random / Sequential / RangeAndWeight）；清單為空時回傳 null
        /// 所有模式都會先用 Profile.MinPickDistance/MaxPickDistance 過濾「當前距離可用」的招式
        /// RangeAndWeight 額外用 PickWeight 加權 + PickCooldown 過濾。全部過濾完仍沒符合 → fallback 任選一招
        /// </summary>
        public EnemyAttackProfile SelectNextAttack()
        {
            if (!HasAttackProfiles) return null;
            float dist = GetDistanceToPlayer();
            float now = Time.time;
            EnemyAttackProfile chosen;
            switch (_attackPickMode)
            {
                case AttackPickMode.RangeAndWeight:
                    chosen = SelectByRangeAndWeight(dist, now);
                    break;
                case AttackPickMode.Sequential:
                    chosen = SelectSequentialInRange(dist);
                    break;
                case AttackPickMode.Random:
                default:
                    chosen = SelectRandomInRange(dist);
                    break;
            }
            RecordAttackUse(chosen);
            return chosen;
        }

        private EnemyAttackProfile SelectRandomInRange(float dist)
        {
            int eligibleCount = 0;
            for (int i = 0; i < _attackProfiles.Count; i++)
            {
                if (IsProfileInRange(_attackProfiles[i], dist)) eligibleCount++;
            }
            if (eligibleCount == 0) return PickAnyAttackFallback();
            int target = UnityEngine.Random.Range(0, eligibleCount);
            int seen = 0;
            for (int i = 0; i < _attackProfiles.Count; i++)
            {
                if (!IsProfileInRange(_attackProfiles[i], dist)) continue;
                if (seen == target) return _attackProfiles[i];
                seen++;
            }
            return PickAnyAttackFallback();
        }

        private EnemyAttackProfile SelectSequentialInRange(float dist)
        {
            int count = _attackProfiles.Count;
            for (int i = 0; i < count; i++)
            {
                int idx = (_attackSequenceIndex + i) % count;
                EnemyAttackProfile p = _attackProfiles[idx];
                if (IsProfileInRange(p, dist))
                {
                    _attackSequenceIndex = (idx + 1) % count;
                    return p;
                }
            }
            return PickAnyAttackFallback();
        }

        private static bool IsProfileInRange(EnemyAttackProfile profile, float distance)
        {
            if (profile == null) return false;
            if (profile.PickWeight <= 0f) return false;
            if (distance < profile.MinPickDistance) return false;
            if (distance > profile.MaxPickDistance) return false;
            return true;
        }

        /// <summary>
        /// RangeAndWeight 模式選招：兩次掃描清單 — 先算總權重，再 weighted random
        /// 全部不符合（全在冷卻或全超出範圍）→ 退化成「忽略限制隨機選一招」避免敵人卡住不出招
        /// </summary>
        private EnemyAttackProfile SelectByRangeAndWeight(float dist, float now)
        {
            float totalWeight = 0f;
            for (int i = 0; i < _attackProfiles.Count; i++)
            {
                EnemyAttackProfile p = _attackProfiles[i];
                if (!IsProfileEligible(p, dist, now)) continue;
                totalWeight += p.PickWeight;
            }

            if (totalWeight > 0f)
            {
                float roll = UnityEngine.Random.value * totalWeight;
                float accum = 0f;
                for (int i = 0; i < _attackProfiles.Count; i++)
                {
                    EnemyAttackProfile p = _attackProfiles[i];
                    if (!IsProfileEligible(p, dist, now)) continue;
                    accum += p.PickWeight;
                    if (roll <= accum) return p;
                }
            }

            return PickAnyAttackFallback();
        }

        private bool IsProfileEligible(EnemyAttackProfile profile, float distance, float now)
        {
            if (!IsProfileInRange(profile, distance)) return false;
            if (profile.PickCooldown <= 0f) return true;
            if (_attackLastUseTime != null && _attackLastUseTime.TryGetValue(profile, out float lastUse))
            {
                if (now - lastUse < profile.PickCooldown) return false;
            }
            return true;
        }

        private void RecordAttackUse(EnemyAttackProfile profile)
        {
            if (profile == null) return;
            if (_attackLastUseTime == null)
            {
                _attackLastUseTime = new Dictionary<EnemyAttackProfile, float>();
            }
            _attackLastUseTime[profile] = Time.time;
        }

        private EnemyAttackProfile PickAnyAttackFallback()
        {
            int count = _attackProfiles.Count;
            int startIdx = UnityEngine.Random.Range(0, count);
            for (int i = 0; i < count; i++)
            {
                EnemyAttackProfile p = _attackProfiles[(startIdx + i) % count];
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>
        /// 觸發硬直 — 由 CombatAttributeSet.OnPoiseBroken 訂閱呼叫，或外部強制觸發
        /// 副作用：取消當前攻擊、停止移動、清除轉身、重置 Poise 防止連續擊破鎖死
        /// </summary>
        public void TriggerStagger()
        {
            if (_isDead || _isStaggered) return;
            _isStaggered = true;
            _pendingHitLight = false;
            _pendingHitHeavy = false;
            _combatAttributes?.ResetPoise();
            if (_attackExecutor != null && _attackExecutor.IsAttacking)
            {
                _attackExecutor.Cancel();
            }
            StopMovement();
            PlayStaggerVfx();
            OnStaggered?.Invoke();
        }

        /// <summary>
        /// 結束硬直 — 由 FSM Stagger 狀態的退出時機呼叫
        /// </summary>
        public void EndStagger()
        {
            _isStaggered = false;
            StopStaggerVfx();
        }

        /// <summary>
        /// 標記已偵測到玩家（首次警覺時呼叫）— 影響視野判定是否略過角度限制
        /// </summary>
        public void MarkPlayerDetected()
        {
            _hasDetectedPlayer = true;
        }

        /// <summary>
        /// 重置「已偵測」狀態（玩家逃出 LoseTargetDistance 後可呼叫）
        /// </summary>
        public void ClearDetectedPlayer()
        {
            _hasDetectedPlayer = false;
            _vision?.ClearLastKnownPosition();
        }

        /// <summary>
        /// 播放單次音效
        /// </summary>
        public void PlaySfx(AudioClip clip)
        {
            if (clip == null || _audioSource == null) return;
            _audioSource.PlayOneShot(clip);
        }

        /// <summary>
        /// 觸發死亡 — 設標記、停 AI、取消攻擊、禁碰撞，動畫由 FSM Dead 狀態接管
        /// </summary>
        public void TriggerDeath()
        {
            if (_isDead) return;
            _isDead = true;
            _isStaggered = false;
            _currentArmor = ArmorLevel.Invulnerable;
            _pendingHitLight = false;
            _pendingHitHeavy = false;
            StopMovement();
            if (_astarAI != null) _astarAI.canMove = false;
            if (_attackExecutor != null && _attackExecutor.IsAttacking)
            {
                _attackExecutor.Cancel();
            }
            StopStaggerVfx();
            DisableHurtboxColliders();
            // 通知 LockOn 系統:此目標不可再被鎖定(玩家若已鎖定會自動釋放)
            if (_lockOnTarget != null) _lockOnTarget.IsLockable = false;
            OnDied?.Invoke();
        }

        #endregion

        #region Private Methods

        private void FindPlayer()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                _playerTransform = player.transform;
            }
        }

        /// <summary>
        /// 統一的 Alert 觸發入口（內部使用）— 視野 / 聽覺 / OnHit 三條路徑共用
        /// 依當前 AlertSuppression 決定要不要播 VFX 跟動畫：
        ///   None          → 播 VFX + 動畫（完整 Alert）— Patrol/Idle/LookAround 之後
        ///   SkipAnimation → 播 VFX、不播動畫 — Search Phase 2
        ///   SkipAll       → 兩個都不播，直接進 Combat — Search Phase 1
        /// </summary>
        private void RequestCombatEntry()
        {
            if (_isDead || _isInCombat) return;
            if (_wantsCombatEntry) return;

            bool playVfx;
            bool playAnim;
            switch (_alertSuppression)
            {
                case AlertSuppression.None:
                    playVfx = true;
                    playAnim = true;
                    break;
                case AlertSuppression.SkipAnimation:
                    playVfx = true;
                    playAnim = false;
                    break;
                case AlertSuppression.SkipAll:
                default:
                    playVfx = false;
                    playAnim = false;
                    break;
            }

            if (playVfx) PlayAlertVfx();
            _wantsCombatEntry = true;
            _shouldPlayAlertFirst = playAnim;
        }

        private void InitializeAlertVfx()
        {
            if (_alertVfx == null) return;
            _alertVfxOriginalScale = _alertVfx.localScale;
            _alertVfxScaleRecorded = true;
            _alertVfxParticles = _alertVfx.GetComponentsInChildren<ParticleSystem>(true);
            _alertVfx.localScale = Vector3.zero;
            _alertVfx.gameObject.SetActive(false);
        }

        /// <summary>
        /// 一次性事件：ScaleIn (OutBack) → 維持 holdDuration → ScaleOut (InBack) → disable
        /// Alert 跟 Search 問號互斥（Alert 觸發時瞬切隱藏問號）
        /// </summary>
        private void PlayAlertVfx()
        {
            if (_alertVfx == null || !_alertVfxScaleRecorded) return;
            HideSearchQuestionVfxImmediate();

            _alertVfxTween?.Kill();
            _alertVfx.gameObject.SetActive(true);
            _alertVfx.localScale = Vector3.zero;

            if (_alertVfxParticles != null)
            {
                for (int i = 0; i < _alertVfxParticles.Length; i++)
                {
                    ParticleSystem ps = _alertVfxParticles[i];
                    if (ps == null) continue;
                    ps.Clear();
                    ps.Play(false);
                }
            }

            Transform target = _alertVfx;
            _alertVfxTween = DOTween.Sequence()
                .Append(target.DOScale(_alertVfxOriginalScale, _vfxScaleInDuration).SetEase(Ease.OutBack))
                .AppendInterval(_alertVfxHoldDuration)
                .AppendCallback(StopAlertVfxParticleEmission)
                .Append(target.DOScale(Vector3.zero, _vfxScaleOutDuration).SetEase(Ease.InBack))
                .OnComplete(() =>
                {
                    if (target != null) target.gameObject.SetActive(false);
                })
                .SetLink(target.gameObject);
        }

        private void StopAlertVfxParticleEmission()
        {
            if (_alertVfxParticles == null) return;
            for (int i = 0; i < _alertVfxParticles.Length; i++)
            {
                ParticleSystem ps = _alertVfxParticles[i];
                if (ps == null) continue;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>立即隱藏 Alert VFX — 給其他互斥邏輯用（目前沒人呼叫，預留 API）</summary>
        private void HideAlertVfxImmediate()
        {
            if (_alertVfx == null) return;
            _alertVfxTween?.Kill();
            if (_alertVfxParticles != null)
            {
                for (int i = 0; i < _alertVfxParticles.Length; i++)
                {
                    ParticleSystem ps = _alertVfxParticles[i];
                    if (ps == null) continue;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            _alertVfx.localScale = Vector3.zero;
            _alertVfx.gameObject.SetActive(false);
        }

        private void InitializeSearchQuestionVfx()
        {
            if (_searchQuestionVfx == null) return;
            _searchQuestionVfxOriginalScale = _searchQuestionVfx.localScale;
            _searchQuestionVfxScaleRecorded = true;
            _searchQuestionVfxParticles = _searchQuestionVfx.GetComponentsInChildren<ParticleSystem>(true);
            ForceParticleLoop(_searchQuestionVfxParticles);
            _searchQuestionVfx.localScale = Vector3.zero;
            _searchQuestionVfx.gameObject.SetActive(false);
        }

        /// <summary>
        /// 強制所有 ParticleSystem 設成 Looping — 給「狀態期間持續顯示」型 VFX 用（Stagger / Search 問號）
        /// 避免設計師忘了在 Inspector 勾 Looping 導致「亮一下就消失」
        /// </summary>
        private static void ForceParticleLoop(ParticleSystem[] particles)
        {
            if (particles == null) return;
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;
                main.loop = true;
            }
        }

        /// <summary>
        /// 進 Search 階段 2（WalkToExtrapolation）時呼叫 — OutBack 彈入，Particle 開始播
        /// </summary>
        public void PlaySearchQuestionVfx()
        {
            if (_searchQuestionVfx == null || !_searchQuestionVfxScaleRecorded) return;
            _searchQuestionVfxTween?.Kill();
            _searchQuestionVfx.gameObject.SetActive(true);
            _searchQuestionVfx.localScale = Vector3.zero;
            _searchQuestionVfxTween = _searchQuestionVfx.DOScale(_searchQuestionVfxOriginalScale, _vfxScaleInDuration)
                .SetEase(Ease.OutBack)
                .SetLink(_searchQuestionVfx.gameObject);
            if (_searchQuestionVfxParticles != null)
            {
                for (int i = 0; i < _searchQuestionVfxParticles.Length; i++)
                {
                    ParticleSystem ps = _searchQuestionVfxParticles[i];
                    if (ps == null) continue;
                    ps.Clear();
                    ps.Play(false);
                }
            }
        }

        /// <summary>
        /// Search 階段 2 結束（進 LookAround 或 OnStop）時呼叫 — InBack 彈出，動畫結束後 disable
        /// </summary>
        public void StopSearchQuestionVfx()
        {
            if (_searchQuestionVfx == null || !_searchQuestionVfxScaleRecorded) return;
            _searchQuestionVfxTween?.Kill();
            if (_searchQuestionVfxParticles != null)
            {
                for (int i = 0; i < _searchQuestionVfxParticles.Length; i++)
                {
                    ParticleSystem ps = _searchQuestionVfxParticles[i];
                    if (ps == null) continue;
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            if (!_searchQuestionVfx.gameObject.activeSelf) return;
            Transform target = _searchQuestionVfx;
            _searchQuestionVfxTween = target.DOScale(Vector3.zero, _vfxScaleOutDuration)
                .SetEase(Ease.InBack)
                .SetLink(target.gameObject)
                .OnComplete(() =>
                {
                    if (target != null) target.gameObject.SetActive(false);
                });
        }

        /// <summary>
        /// 立即隱藏問號 — Alert 觸發時呼叫，瞬切互斥避免兩個提示同時出現
        /// </summary>
        private void HideSearchQuestionVfxImmediate()
        {
            if (_searchQuestionVfx == null) return;
            _searchQuestionVfxTween?.Kill();
            if (_searchQuestionVfxParticles != null)
            {
                for (int i = 0; i < _searchQuestionVfxParticles.Length; i++)
                {
                    ParticleSystem ps = _searchQuestionVfxParticles[i];
                    if (ps == null) continue;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            _searchQuestionVfx.localScale = Vector3.zero;
            _searchQuestionVfx.gameObject.SetActive(false);
        }

        /// <summary>
        /// Awake 階段呼叫 — 記下設計師在 Inspector 設定的 scale 作為「彈入終點」，
        /// 然後把 VFX 初始化為 scale=0 + disable，避免一開始就跑出來
        /// </summary>
        private void InitializeStaggerVfx()
        {
            if (_staggerVfx == null) return;
            _staggerVfxOriginalScale = _staggerVfx.localScale;
            _staggerVfxScaleRecorded = true;
            _staggerVfxParticles = _staggerVfx.GetComponentsInChildren<ParticleSystem>(true);
            ForceParticleLoop(_staggerVfxParticles);
            _staggerVfx.localScale = Vector3.zero;
            _staggerVfx.gameObject.SetActive(false);
        }

        /// <summary>
        /// 進 Stagger 時呼叫 — DOTween OutBack 把 scale 從 0 彈到原尺寸，同時播放容器內所有 ParticleSystem
        /// </summary>
        private void PlayStaggerVfx()
        {
            if (_staggerVfx == null || !_staggerVfxScaleRecorded) return;
            _staggerVfxTween?.Kill();
            _staggerVfx.gameObject.SetActive(true);
            _staggerVfx.localScale = Vector3.zero;
            _staggerVfxTween = _staggerVfx.DOScale(_staggerVfxOriginalScale, _vfxScaleInDuration)
                .SetEase(Ease.OutBack)
                .SetLink(_staggerVfx.gameObject);
            if (_staggerVfxParticles != null)
            {
                for (int i = 0; i < _staggerVfxParticles.Length; i++)
                {
                    ParticleSystem ps = _staggerVfxParticles[i];
                    if (ps == null) continue;
                    ps.Clear();
                    ps.Play(false);
                }
            }
        }

        /// <summary>
        /// 離 Stagger / 死亡時呼叫 — Particle Stop（已發射的粒子飛完），DOTween InBack 把 scale 縮回 0 後 disable GameObject
        /// </summary>
        private void StopStaggerVfx()
        {
            if (_staggerVfx == null || !_staggerVfxScaleRecorded) return;
            _staggerVfxTween?.Kill();
            if (_staggerVfxParticles != null)
            {
                for (int i = 0; i < _staggerVfxParticles.Length; i++)
                {
                    ParticleSystem ps = _staggerVfxParticles[i];
                    if (ps == null) continue;
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            // 已經 disable 就不再 tween（避免從 inactive 物件做動畫無效）
            if (!_staggerVfx.gameObject.activeSelf) return;
            Transform target = _staggerVfx;
            _staggerVfxTween = target.DOScale(Vector3.zero, _vfxScaleOutDuration)
                .SetEase(Ease.InBack)
                .SetLink(target.gameObject)
                .OnComplete(() =>
                {
                    if (target != null) target.gameObject.SetActive(false);
                });
        }

        /// <summary>
        /// 朝 _facingDirection 持續轉身（依 EnemyConfig.RotationSpeed）
        /// </summary>
        private void UpdateRotation()
        {
            Vector3 dir = _facingDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion targetRotation = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                _config.RotationSpeed * Time.deltaTime
            );
        }

        /// <summary>
        /// Root Motion 推進 transform 後，將位置同步給 A* 內部狀態，避免路徑漂移
        /// </summary>
        private void SyncAstarPosition()
        {
            if (_astarAI == null) return;
            _astarAI.Teleport(transform.position, false);
        }

        /// <summary>
        /// 強制 A* 降級為純路徑規劃：位置與旋轉由 Root Motion + UpdateRotation 接管
        /// 設計師若手動勾選 Inspector 也會被覆蓋，避免「Inspector 看到的」與「runtime 行為」不一致
        /// </summary>
        private void ConfigureAstarForRootMotion()
        {
            if (_astarAI == null) return;
            _astarAI.canMove = true;
            _astarAI.updatePosition = false;
            _astarAI.updateRotation = false;
            RichAI richAI = _astarAI as RichAI;
            if (richAI != null)
            {
                richAI.enableRotation = false;
                richAI.slowdownTime = 0f;
            }
        }

        private void EnableRootMotion()
        {
            if (_animancer == null || _animancer.Animator == null) return;
            _animancer.Animator.applyRootMotion = true;
        }

        /// <summary>
        /// 判定攻擊應產生的受擊等級
        /// Light  → Flinch（只抖動，不切 State）
        /// Normal → 切 HitLight State
        /// Heavy  → 切 HitHeavy State
        /// </summary>
        private static HitReactionLevel ClassifyHitReaction(HitContext ctx)
        {
            return ctx.attackTier switch
            {
                AttackTier.Light => HitReactionLevel.Flinch,
                AttackTier.Normal => HitReactionLevel.Light,
                AttackTier.Heavy => HitReactionLevel.Heavy,
                _ => HitReactionLevel.Light,
            };
        }

        /// <summary>
        /// 判定該等級的受擊能否打斷目前的霸體狀態
        /// 規則：
        /// None           → Light / Heavy / Break 全部能打斷
        /// AttackingArmor → 只有 Heavy / Break 能打斷
        /// SuperArmor     → 只有 Break 能打斷（這裡看不到，Break 在 OnHit 前段已處理）
        /// Invulnerable   → 全部擋下（OnHit 前段已 wasBlocked return）
        /// </summary>
        private static bool CanReactionInterruptArmor(HitReactionLevel reaction, ArmorLevel armor)
        {
            return armor switch
            {
                ArmorLevel.None => reaction >= HitReactionLevel.Light,
                ArmorLevel.AttackingArmor => reaction >= HitReactionLevel.Heavy,
                ArmorLevel.SuperArmor => false,
                ArmorLevel.Invulnerable => false,
                _ => false
            };
        }

        /// <summary>
        /// 死亡時禁用所有 hurtbox Collider，避免死後仍可受擊或被招架
        /// 排除 CharacterController（父物件上的物理 collider），不然敵人會穿地板掉下去
        /// </summary>
        private void DisableHurtboxColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] is CharacterController) continue;
                colliders[i].enabled = false;
            }
        }

        private void InitializeAttributesFromASC()
        {
            if (_asc == null) return;
            _combatAttributes = _asc.GetAttributeSet<CombatAttributeSet>();
            if (_combatAttributes == null) return;
            // 血量與韌性數值由 EnemyConfig 提供（集中管理，可複製 .asset 變體調出不同敵人）
            if (_config != null)
            {
                if (_config.MaxHealth > 0f)
                {
                    _combatAttributes.MaxHealth.BaseValue = _config.MaxHealth;
                    _combatAttributes.Health.BaseValue = _config.MaxHealth;
                }
                if (_config.MaxPoise > 0f)
                {
                    _combatAttributes.MaxPoise.BaseValue = _config.MaxPoise;
                    _combatAttributes.Poise.BaseValue = _config.MaxPoise;
                }
                _combatAttributes.PoiseRegen.BaseValue = _config.PoiseRegen;
            }
            _combatAttributes.OnDeath -= HandleAttributeDeath;
            _combatAttributes.OnDeath += HandleAttributeDeath;
            _combatAttributes.OnDamageTaken -= HandleDamageTaken;
            _combatAttributes.OnDamageTaken += HandleDamageTaken;
            _combatAttributes.OnPoiseBroken -= HandlePoiseBroken;
            _combatAttributes.OnPoiseBroken += HandlePoiseBroken;
        }

        private void UnsubscribeAttributes()
        {
            if (_combatAttributes == null) return;
            _combatAttributes.OnDeath -= HandleAttributeDeath;
            _combatAttributes.OnDamageTaken -= HandleDamageTaken;
            _combatAttributes.OnPoiseBroken -= HandlePoiseBroken;
        }

        private void HandleAttributeDeath()
        {
            TriggerDeath();
        }

        private void HandlePoiseBroken()
        {
            TriggerStagger();
        }

        private void HandleDamageTaken(AbilitySystemComponent source, float damage)
        {
            if (_isDead) return;
            OnDamaged?.Invoke(damage);
            // 不走 IHitReceiver.OnHit 的傷害來源（玩家投射物、AoE、DOT）會直接走 GAS effect 觸發此事件
            // 一樣要讓敵人進入戰鬥 — 排除自身傷害（buff/debuff）避免敵人對自己 aggro
            if (source != null && source != _asc)
            {
                MarkPlayerDetected();
                RequestCombatEntry();
            }
        }

        /// <summary>
        /// EnemyAttackExecutor 命中玩家時呼叫：包成 HitContext 走遊戲統一的 IHitReceiver 管線
        /// 目標的 IHitReceiver 實作（玩家身上）會自行決定如何套用 GAS 傷害
        /// 套用後若沒被擋下（wasBlocked = false），生成 Profile 設定的「全身命中特效」於玩家中心
        /// </summary>
        private void HandleAttackHitConfirmed(EnemyAttackExecutor executor, EnemyAttackProfile profile, GameObject hitObject)
        {
            if (_isDead || profile == null || hitObject == null) return;
            IHitReceiver receiver = hitObject.GetComponentInParent<IHitReceiver>();
            if (receiver == null)
            {
                Debug.LogWarning($"[{name}] 命中 {hitObject.name} 但找不到 IHitReceiver — 無法套用傷害。請確認玩家身上有 GASDamageReceiver", hitObject);
                return;
            }
            Vector3 attackDir = (hitObject.transform.position - transform.position);
            attackDir.y = 0f;
            if (attackDir.sqrMagnitude > 0.0001f) attackDir.Normalize();
            HitContext ctx = new HitContext
            {
                damage = profile.Damage,
                poiseDamage = profile.DazeBuildup,
                knockbackForce = profile.KnockbackDistance,
                attackTier = profile.AttackTier,
                isHeavyAttack = profile.AttackTier == AttackTier.Heavy,
                hitPoint = hitObject.transform.position,
                hitNormal = -attackDir,
                attackDirection = attackDir,
                sourceProfile = null,
                skipHitEffects = false,
                gasDamageApplied = false,
                hitStopDuration = 0f,
                hitStopTimeScale = 1f,
                cameraShakeIntensity = 0f
            };
            receiver.OnHit(ref ctx);
            // 玩家成功格擋 / 招架 / 無敵時 wasBlocked = true，不播全身命中特效
            if (!ctx.wasBlocked && profile.HitVfxPrefab != null)
            {
                SpawnHitVfx(profile, hitObject.transform);
            }
        }

        /// <summary>
        /// 在玩家中心生成全身命中特效（不 parent，留在世界座標）
        /// 跟隨 Profile 的 HitVfxOffset / HitVfxScaleMultiplier / HitVfxLifetime
        /// </summary>
        private void SpawnHitVfx(EnemyAttackProfile profile, Transform target)
        {
            Vector3 worldPos = target.TransformPoint(profile.HitVfxOffset);
            GameObject vfx = Instantiate(profile.HitVfxPrefab, worldPos, target.rotation);
            // Prefab 自帶 scale × 倍率 × 玩家 lossyScale —— 玩家被放大時特效一起放大
            Vector3 baseScale = Vector3.Scale(vfx.transform.localScale, profile.HitVfxScaleMultiplier);
            vfx.transform.localScale = Vector3.Scale(baseScale, target.lossyScale);
            if (profile.HitVfxLifetime > 0f)
            {
                Destroy(vfx, profile.HitVfxLifetime);
            }
        }

        #endregion

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_config == null) return;
            Vector3 origin = _eyePosition != null ? _eyePosition.position : transform.position;
            Vector3 forward = transform.forward;

            // 視野半徑（黃色圓）
            Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
            Gizmos.DrawWireSphere(origin, _config.ViewRadius);

            // 放棄追擊半徑（紅色虛線概念用點）
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.2f);
            Gizmos.DrawWireSphere(origin, _config.LoseTargetDistance);

            // 聽覺半徑（青色圓，以腳底為中心）— 玩家在此範圍內快速移動會被聽到
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.45f);
            Gizmos.DrawWireSphere(transform.position, _config.HearingRadius);

            // 扇形視野邊界
            float halfAngle = _config.ViewAngle * 0.5f;
            Quaternion leftRot = Quaternion.AngleAxis(-halfAngle, Vector3.up);
            Quaternion rightRot = Quaternion.AngleAxis(halfAngle, Vector3.up);
            Vector3 leftDir = leftRot * forward;
            Vector3 rightDir = rightRot * forward;
            Color viewColor = (Application.isPlaying && CanSeePlayer) ? Color.green : Color.yellow;
            Gizmos.color = viewColor;
            Gizmos.DrawLine(origin, origin + leftDir * _config.ViewRadius);
            Gizmos.DrawLine(origin, origin + rightDir * _config.ViewRadius);

            // 視線到玩家（執行時）
            if (Application.isPlaying && _playerTransform != null)
            {
                Gizmos.color = CanSeePlayer ? Color.green : Color.red;
                Gizmos.DrawLine(origin, _playerTransform.position + Vector3.up * 1f);
            }
        }
#endif
    }

    /// <summary>
    /// 敵人選擇下一招的方式
    /// </summary>
    public enum AttackPickMode
    {
        // 從「距離合適」的招式中隨機選一招（看 Min/Max；不看 Weight、不看 Cooldown）
        Random = 0,
        // 依清單順序循環使用，超出 Min/Max 的招會被跳過（看 Min/Max；不看 Weight、不看 Cooldown）
        Sequential = 1,
        // 依各招的 MinPickDistance/MaxPickDistance/PickWeight/PickCooldown 自動選最合適的招式
        // 玩家距離 + 權重 + 冷卻三因子綜合考量；沒招符合會 fallback 到隨機
        RangeAndWeight = 2,
    }
}
