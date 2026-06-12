using System.Collections;
using System.Collections.Generic;
using Animancer;
using Unity.Cinemachine;
using UnityEngine;
using Player.Locomotion;
using Player.Locomotion.States;
using Player.Input;
using GAS.Targeting.Combat;
using GAS.Targeting.LockOnV2;
using GAS.UI;

namespace GAS
{
    /// <summary>
    /// 頂層狀態 — 決定 Controller 每幀走哪條流程。
    /// Locomotion 為預設,其餘狀態表示「某個系統暫時接管角色」。
    /// Jump 不列入此層(由 LocomotionStateMachine 內部管理)。
    /// </summary>
    public enum TopState
    {
        /// <summary>正常移動 — 讀輸入、Tick Locomotion 狀態機、套腳本旋轉</summary>
        Locomotion,
        /// <summary>能力接管 — 攻擊、閃避、格擋、蓄力等 GAS 能力執行期間</summary>
        Ability,
        /// <summary>受擊硬直 — 類似 Ability,但允許特定取消條件</summary>
        HitStun,
        /// <summary>死亡 — 全部凍結,單向狀態</summary>
        Dead,
    }
    /// <summary>
    /// 新版 GAS 玩家控制器 — 以 PlayerLocomotionController 為基礎,
    /// 採用向量驅動 + RootMotion 的移動架構,逐步整合 GAS、戰鬥、鎖定、受擊等功能。
    /// 目前階段:Locomotion + Jump + TopState 閘門 + ASC 橋接(Step 2)。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(AbilitySystemComponent))]
    [RequireComponent(typeof(LockOnController))]
    [RequireComponent(typeof(CombatTargetFinder))]
    [RequireComponent(typeof(HitTargetMemory))]
    public sealed class NewGASPlayerController : MonoBehaviour
    {
        [Header("設定資產")]
        [SerializeField] private LocomotionConfig _config;
        [SerializeField] private LocomotionAnimationSet _animationSet;
        [SerializeField, Tooltip("受擊反應資料 — 未指派時,OnHitReceived 會直接略過")]
        private HitReactionData _hitReactionData;
        [SerializeField, Tooltip("死亡資料 — 未指派時 Die 不播動畫,但仍執行 Tag / 輸入停用 / TimeScale 凍結 / UI 觸發")]
        private PlayerDeathData _deathData;
        [SerializeField, Tooltip("Flinch Layer 使用的 AvatarMask — 建議只勾選上半身骨架(脊椎、手臂、頭),\n" +
                                   "這樣 Flinch 播放時角色能繼續走路、跑步、攻擊,只有上半身會晃動。\n" +
                                   "未指派時 Flinch 會在全身播放(退化為全身微受擊)。")]
        private AvatarMask _flinchAvatarMask;
        [SerializeField, Tooltip("滑翔翼身上特效 — 持續射出的循環粒子(例如風流、光痕)。\n" +
                                   "建議掛在玩家根物件下的子 GameObject,避免武器/模型切換時被銷毀。\n" +
                                   "ParticleSystem 設定:Play On Awake = false、Loop = true。\n" +
                                   "展開滑翔翼時呼叫 Play(),收起時呼叫 Pause()。未指派時不影響滑翔功能。")]
        private ParticleSystem _gliderVFX;

        [Header("元件")]
        [SerializeField] private LocomotionInputReader _inputReader;
        [SerializeField] private AnimancerComponent _animancer;
        [SerializeField] private Transform _cameraTransform;
        [Tooltip("能力系統元件 — 用於 Tag 阻擋檢查(CanJump 等)。未指定時 Awake 自動 GetComponent。")]
        [SerializeField] private AbilitySystemComponent _asc;

        [Header("動畫 IK")]
        [SerializeField, Tooltip("為所有 Animancer 狀態強制啟用 Humanoid Foot IK Pass(需 Humanoid Avatar)。\n" +
                                   "每幀重新套用 — ClipTransition.Apply() 會在 Play 時把 state.ApplyFootIK 重設為 transition 本身的值,\n" +
                                   "因此一次性設定無法涵蓋 Locomotion / HitReaction / Death / Ability 等多條播放路徑。\n" +
                                   "關閉時不做任何處理,讓各 ClipTransition 的設定生效。")]
        private bool _applyFootIKToAllStates = true;

        [Header("鎖定 / 戰鬥目標")]
        [SerializeField, Tooltip("鎖定控制器(LockOnV2)— 未指定時 Awake 自動 GetComponent。搜尋範圍/優先權由 LockOnSelectorConfig 設定,不再於此層配置。")]
        private LockOnController _lockOn;
        [SerializeField, Tooltip("戰鬥目標搜尋器 — 供攻擊/閃避能力呼叫 FindBestTarget / TryGetSnapTarget 等。未指定時 Awake 自動 GetComponent。")]
        private CombatTargetFinder _targetFinder;
        [SerializeField, Tooltip("命中目標記憶 — 攻擊能力讀寫 LastHitTarget 供連擊/Homing 使用。未指定時 Awake 自動 GetComponent。")]
        private HitTargetMemory _hitMemory;

        [Header("Cinemachine 攝影機")]
        [Tooltip("第三人稱攝影機 — 供能力觸發垂直軸回中使用,可留空(留空時 RecenterThirdPersonVerticalOnce 會失效)")]
        [SerializeField] private CinemachineCamera _thirdPersonCam;
        [SerializeField, Tooltip("垂直軸回中所需時間(秒)")] private float _verticalRecenteringTime = 0.35f;
        [SerializeField, Tooltip("垂直軸回中前的等待時間(秒)")] private float _verticalRecenteringWait = 0f;

        [Header("除錯")]
        [SerializeField, Tooltip("於 Scene 視圖顯示藍色 Actor Forward 與紅色 Desired Direction 箭頭")]
        private bool _drawDebugArrows = true;
        [SerializeField] private float _debugArrowLength = 2f;
        [SerializeField, Tooltip("於 Scene 視圖繪製地面偵測 SphereCast 範圍:綠=CC 快速路徑接地、藍=SphereCast 補判接地、紅=離地;黃點為 SphereCast 命中點")]
        private bool _drawGroundSensorGizmos = true;
        [SerializeField, Tooltip("啟用後按下 _debugHitKey 會從 4 個方向輪流觸發受擊,用於沒有敵人 AI 時測試 HitState")]
        private bool _enableHitDebugKey = true;
        [SerializeField] private KeyCode _debugHitKey = KeyCode.H;
        [SerializeField, Tooltip("除錯受擊的 Poise Damage — 每次按下造成此量韌性傷害,累積歸零時觸發硬直。\n" +
                                   "預設 200 足以一擊擊破 Poise(預設上限 100),方便立刻驗證 Stagger/Knockback 位移")]
        private float _debugHitPoiseDamage = 200f;
        [SerializeField, Tooltip("除錯受擊的 HP Damage(目前僅顯示於 Log,真正扣血由 GE 流程處理)")]
        private float _debugHitDamage = 0f;
        [SerializeField, Tooltip("除錯按鍵是否視為重攻擊(HitContext.isHeavyAttack)— true 走 KnockbackState(3 階段倒地起身);false 走 HitState(單段 Stagger)。\n" +
                                   "擊退距離由 _debugKnockbackDistance 獨立控制,兩條路徑都吃同一個數值")]
        private bool _debugHitAsKnockback = false;
        [SerializeField, Tooltip("除錯擊退漸近距離(公尺)— 對應 HitContext.knockbackForce;Stagger 與 Knockback 都使用此值")]
        private float _debugKnockbackDistance = 5f;
        [SerializeField, Tooltip("於 Scene 視圖以橘色箭頭繪製目前 _externalVelocity(擊退 / 外力)方向與強度;起點為角色膠囊中段")]
        private bool _drawExternalVelocityGizmo = true;
        [SerializeField, Tooltip("在螢幕左上角顯示 Stamina、衝刺耗盡鎖定狀態(僅 Editor / Development Build),用於 UI 未接入前的快速驗證")]
        private bool _showStaminaDebugHud = false;

        private CharacterController _characterController;
        private WeaponManager _weaponManager;
        private LocomotionStateMachine _stateMachine;
        private LocomotionStateContext _context;
        private LocomotionAnimatorDriver _animatorDriver;
        private GroundSensor _groundSensor;
        private float _verticalVelocity;
        private Vector3 _prevHorizontalDelta;
        private Vector3 _externalVelocity;
        private float _timeSinceGrounded;
        private float _jumpBufferTimer;
        private float _dodgeBufferTimer;
        private bool _prevDodgeLocked;
        private float _dodgeIFrameElapsed;
        private bool _dodgeIFrameWindowRunning;
        private bool _dodgeIFrameTagAdded;
        private bool _dodgeIFrameSlowmoUsed;
        private Coroutine _dodgeSlowmoRoutine;
        private int _debugHitDirectionIndex;
        private float _maxAirborneY;
        private bool _trackingFall;
        private float _jumpStartY;
        private bool _airborneFromJump;
        private TopState _topState = TopState.Locomotion;
        private bool _locomotionInitialized;
        private System.Type _pendingRefreshStateType;
        private Player.Locomotion.LocomotionAnimSlot _pendingResumeSlot;
        private float _pendingResumeNormalizedTime;
        private bool _pendingIsAirborne;
        private bool _pendingIsDodgeLocked;
        private bool _pendingUseRootMotionRotation;
        private Vector3 _pendingJumpHorizontalVelocity;
        private int _pendingTurnDirection;

        /// <summary>當前頂層狀態(只讀)。</summary>
        public TopState CurrentTopState => _topState;
        /// <summary>是否處於 Locomotion 階段 — 可接收輸入與 Locomotion 狀態機轉換。</summary>
        public bool IsInLocomotion => _topState == TopState.Locomotion;
        /// <summary>是否已死亡(單向狀態,第一步尚未接復活機制)。</summary>
        public bool IsDead => _topState == TopState.Dead;
        /// <summary>角色是否接地 — 以 GroundSensor 為單一真實來源(CC 快速路徑 + SphereCast 補強)。初始化前退回 CharacterController.isGrounded。</summary>
        public bool IsGrounded => _context != null ? _context.IsGrounded : (_characterController != null && _characterController.isGrounded);
        /// <summary>動畫元件引用 — 供能力系統取得 AnimancerComponent。</summary>
        public AnimancerComponent Animancer => _animancer;
        /// <summary>移動輸入 — 供能力系統讀取玩家方向輸入。</summary>
        public Vector2 MoveInput => _inputReader != null ? _inputReader.RawMove : Vector2.zero;
        /// <summary>攝影機 Transform — 供能力系統計算攝影機相對方向。</summary>
        public Transform CameraTransform => _cameraTransform;
        /// <summary>鎖定控制器(LockOnV2)— 供能力、UI、Cinemachine 整合層取得。</summary>
        public LockOnController LockOn => _lockOn;
        /// <summary>戰鬥目標搜尋器 — 供攻擊/閃避能力呼叫 FindBestTarget / TryGetSnapTarget 等。</summary>
        public CombatTargetFinder TargetFinder => _targetFinder;
        /// <summary>命中目標記憶 — 攻擊能力讀寫 LastHitTarget / 排定清除。</summary>
        public HitTargetMemory HitMemory => _hitMemory;
        /// <summary>當前鎖定目標的 AnchorTransform — 未鎖定時回傳 null;供能力做朝向目標旋轉、攻擊瞄準等(相容舊 API)。</summary>
        public Transform LockOnTarget
        {
            get
            {
                if (_lockOn == null)
                {
                    return null;
                }
                if (_lockOn.CurrentTarget == null)
                {
                    return null;
                }
                return _lockOn.CurrentTarget.AnchorTransform;
            }
        }
        /// <summary>
        /// 是否允許起跳 — TopState 閘門 + ASC Tag 阻擋檢查。
        /// 目前阻擋 Tag:State.CannotMove(通用移動禁止)。
        /// </summary>
        public bool CanJump
        {
            get
            {
                if (_topState != TopState.Locomotion)
                {
                    return false;
                }
                if (_asc != null && _asc.OwnedTags.HasTag(GameplayTags.State.CannotMove))
                {
                    return false;
                }
                return true;
            }
        }
        /// <summary>
        /// 是否允許閃避 — TopState + ASC Tag + 地面 + 非 FastRunTurn + 非 Dodge 中 + 耐力足夠。
        /// 目前只支援地面閃避,滯空禁用;Dodge 中不可再 Dodge。
        /// 耐力不足時回 false,UI 可據此灰化閃避按鍵。
        /// </summary>
        public bool CanDodge
        {
            get
            {
                if (_topState != TopState.Locomotion)
                {
                    return false;
                }
                if (_asc != null && _asc.OwnedTags.HasTag(GameplayTags.State.CannotMove))
                {
                    return false;
                }
                if (_context == null || _stateMachine == null)
                {
                    return false;
                }
                if (_context.IsAirborne)
                {
                    return false;
                }
                if (_context.IsDodgeLocked)
                {
                    return false;
                }
                if (_stateMachine.Current == _context.FastRunTurn)
                {
                    return false;
                }
                if (!_context.CanAffordDodge)
                {
                    return false;
                }
                return true;
            }
        }
        /// <summary>
        /// 暫時抑制 RM 水平位移 — MoveToTarget(Snap/Pierce)啟動時設 true,結束時設 false。
        /// 讓 DOTween 全權控制位移,避免與 RM 打架。
        /// </summary>
        public bool SuppressRootMotionPosition { get; set; }
        /// <summary>TopState 轉換事件:(previous, next)。UI / 音效 / VFX / 除錯 log 可訂閱。</summary>
        public event System.Action<TopState, TopState> TopStateChanged;
        /// <summary>下落傷害觸發事件:(fallDistance 公尺, damage HP 量)。UI 紅屏 / 鏡頭晃動 / 音效可訂閱。發生在 ApplyDamage 之後。</summary>
        public event System.Action<float, float> FallDamageApplied;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _weaponManager = GetComponent<WeaponManager>();
            if (_asc == null)
            {
                _asc = GetComponent<AbilitySystemComponent>();
            }
            if (_lockOn == null)
            {
                _lockOn = GetComponent<LockOnController>();
            }
            if (_targetFinder == null)
            {
                _targetFinder = GetComponent<CombatTargetFinder>();
            }
            if (_hitMemory == null)
            {
                _hitMemory = GetComponent<HitTargetMemory>();
            }
            if (!ValidateCoreReferences())
            {
                enabled = false;
                return;
            }
            _groundSensor = new GroundSensor(_config, _characterController, transform);
            // 訂閱能力事件 — 自動切換 TopState
            _asc.OnAbilityActivated += OnAbilityActivated;
            _asc.OnAbilityEnded += OnAbilityEnded;
            // _animancer 已在 Inspector 指定(扁平層級)→ 立即初始化
            // _animancer 為空(父子層級,模型尚未生成)→ 延遲到 Start / Update 自動偵測
            if (_animancer != null)
            {
                InitializeLocomotion();
            }
        }

        private void Start()
        {
            // WeaponManager.Start() 會在此幀生成模型 — 嘗試自動偵測
            if (!_locomotionInitialized)
            {
                TryAutoDetectModel();
            }
            // 訂閱死亡事件 — Start 才訂閱避免 Awake 階段 ASC.AttributeSet 尚未 ready 的時序問題
            if (_asc != null)
            {
                CombatAttributeSet combatSet = _asc.GetAttributeSet<CombatAttributeSet>();
                if (combatSet != null)
                {
                    combatSet.OnDeath += Die;
                }
            }
        }

        private void Update()
        {
            EnforceFootIK();
            if (_topState == TopState.Dead)
            {
                return;
            }
            // 延遲初始化:模型尚未生成,或武器切換後模型被重建 → 重新偵測
            // 模型遺失前先記錄當前 Locomotion 狀態類型,讓重建後可恢復到同一狀態(Walk/Run/FastRun 跳過 Start clip 直接進 Loop)
            if (_locomotionInitialized && _animancer == null)
            {
                _pendingRefreshStateType = _stateMachine?.Current?.GetType();
                _locomotionInitialized = false;
            }
            if (!_locomotionInitialized)
            {
                TryAutoDetectModel();
                if (!_locomotionInitialized)
                {
                    return;
                }
            }
            float deltaTime = Time.deltaTime;
            // 任何非 Dead 狀態都先更新輸入 context — DodgeState.Enter 取消接 Dodge 時需要最新的
            // DesiredWorldDirection 才能正確選擇 8 方向 clip;同時也順便維護 IsGrounded
            UpdateInputContext();
            UpdateDodgeIFrames(deltaTime);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_enableHitDebugKey && Input.GetKeyDown(_debugHitKey))
            {
                DebugTriggerHit();
            }
#endif
            // 屬性回復 — Poise / Stamina / Mana 每幀 Tick,與 TopState 無關(Dead 除外,上方已 return)
            if (_asc != null)
            {
                CombatAttributeSet combatSet = _asc.GetAttributeSet<CombatAttributeSet>();
                combatSet?.TickRegeneration(deltaTime);
            }
            // Flinch Layer 維護 — 每幀讓 clip 播完後自動淡出 Layer Weight
            if (_hitReactionData != null)
            {
                _animatorDriver?.TickFlinch(deltaTime, _hitReactionData.FlinchLayerFadeOutDuration);
            }
            if (_topState == TopState.Locomotion)
            {
                UpdateJumpTimers(deltaTime);
                UpdateDodgeTimer(deltaTime);
                TryTriggerJump();
                TryTriggerDodge();
                TryTriggerFall();
                TryTriggerGlider();
                _stateMachine.Tick(deltaTime);
                ApplyScriptedRotation(deltaTime);
                SyncDodgeLockTag();
                SyncSprintDepleted();
                TickFallTracking();
                return;
            }
            // Ability 期間仍接受 Dodge 輸入以實現「攻擊取消接 Dodge」(AllowCancelTime 機制)
            if (_topState == TopState.Ability)
            {
                UpdateDodgeTimer(deltaTime);
                TryTriggerDodge();
                SyncDodgeLockTag();
                return;
            }
            // HitStun 期間:State Machine 繼續 Tick(HitState 自行計時),Hit 結束後同步 TopState 回 Locomotion
            if (_topState == TopState.HitStun)
            {
                _stateMachine.Tick(deltaTime);
                SyncHitStunTopState();
                SyncDodgeLockTag();
                return;
            }
            SyncDodgeLockTag();
        }

        /// <summary>
        /// 受擊事件入口 — 攻擊端或除錯按鍵透過此 API 觸發。
        /// 流程:
        ///   1. Invincible Tag → 完全免疫,直接 return(不扣血由 GE 路徑另行處理;不扣 Poise,不播任何反應)。
        ///   2. 扣 Poise(ctx.poiseDamage)— SuperArmor 與無 Tag 角色都會扣。
        ///   3. Poise 未擊破:
        ///        - SuperArmor → 不播 Flinch,角色繼續當前動作(Poise 已扣、HP 由 GE 扣)
        ///        - 無 Tag   → 播 Flinch(上半身疊加)
        ///   4. Poise 擊破:無論有無 SuperArmor 一律走 Stagger / Knockback 流程,
        ///      切 TopState、加 HitStunned Tag、取消 Attack 能力、ForceChangeState、重置 Poise 至滿。
        /// </summary>
        public void OnHitReceived(HitContext ctx)
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            if (_context == null || _stateMachine == null)
            {
                return;
            }
            // Invincible — 完全免疫,攔截所有後續邏輯(不扣血、不扣 Poise、不 Flinch、不 Stagger)
            if (_asc != null && _asc.OwnedTags.HasTag(GameplayTags.State.Invincible))
            {
                // 閃避無敵期間被攻擊到 → 觸發短暫慢動作回饋
                NotifyDodgeIFrameHit();
                return;
            }
            HitDirection dir = ComputeHitDirection(ctx);
            bool hasSuperArmor = _asc != null && _asc.OwnedTags.HasTag(GameplayTags.State.SuperArmor);
            // Poise 判定 — SuperArmor 與無 Tag 角色都會累計 Poise 扣值;差別只在未擊破時是否播 Flinch
            CombatAttributeSet combatSet = _asc != null ? _asc.GetAttributeSet<CombatAttributeSet>() : null;
            bool poiseBroken;
            if (combatSet != null && ctx.poiseDamage > 0f)
            {
                poiseBroken = combatSet.ApplyPoiseDamage(ctx.poiseDamage);
                if (!poiseBroken)
                {
                    // SuperArmor → 吃下此擊,不播 Flinch、不中斷動作(Poise 已記錄、HP 由 GE 另行扣除)
                    if (hasSuperArmor)
                    {
                        return;
                    }
                    // 無 Tag 且不在 HitStun → 播 Flinch(上半身疊加)
                    if (_topState != TopState.HitStun && _hitReactionData != null)
                    {
                        ClipTransition flinchClip = _hitReactionData.GetFlinchClip(dir);
                        if (flinchClip != null)
                        {
                            _animatorDriver?.PlayFlinch(flinchClip, _hitReactionData.FlinchEnterFadeDuration);
                        }
                    }
                    return;
                }
                // poiseBroken == true:繼續往下走 Stagger / Knockback 流程(SuperArmor 不再豁免)
            }
            // 無 AttributeSet 或 poiseDamage <= 0 的邊界情境 → 退回每擊硬直(相容未配置新欄位的舊攻擊)
            if (_hitReactionData == null)
            {
                Debug.LogWarning("[NewGASPlayerController] HitReactionData 未指派,受擊事件被略過。", this);
                return;
            }
            // 分支判定:isHeavyAttack 決定動畫流程(Stagger 單段 vs Knockback 3 階段);
            // 擊退距離獨立由 knockbackForce 控制,允許兩條路徑都帶任意距離(或都為 0 的純動作)
            bool isKnockback = ctx.isHeavyAttack;
            ClipTransition introClip = _hitReactionData.GetClip(dir);
            if (introClip == null)
            {
                Debug.LogWarning($"[NewGASPlayerController] HitReactionData 缺 {dir} 方向 Stagger clip,受擊事件被略過。", this);
                return;
            }
            SetTopState(TopState.HitStun);
            if (_asc != null)
            {
                _asc.OwnedTags.AddTag(GameplayTags.State.HitStunned);
                CancelActiveAttackAbilities();
            }
            // Stagger 時強制停掉進行中的 Flinch,避免上半身晃動疊加到全身受擊動畫上
            _animatorDriver?.StopFlinch();
            // 擊破後重置 Poise 至滿 — 避免 Poise 持續為 0 造成 HitState 中再被輕擊即再次 stagger 的鎖死
            combatSet?.ResetPoise();
            // 擊退位移 — 一律取 ctx.knockbackForce 當距離;為 0 則純動畫無位移
            if (ctx.knockbackForce > 0f)
            {
                AddKnockback(ComputeKnockbackDirection(ctx), ctx.knockbackForce);
            }
            _context.PendingHitOutcome = new HitOutcome
            {
                Clip = introClip,
                StunDuration = _hitReactionData.StunDuration,
                EnterFadeDuration = _hitReactionData.HitEnterFadeDuration,
                Direction = dir,
            };
            _stateMachine.ForceChangeState(isKnockback ? (ILocomotionState)_context.Knockback : _context.Hit);
        }

        /// <summary>
        /// 從 HitContext 解出水平擊退方向(Y 一律歸零)。
        /// 優先順序:attackDirection(攻擊方明確給定)→ 從 hitPoint 指向角色的反推向量(遠離命中點)→ 角色後方。
        /// </summary>
        private Vector3 ComputeKnockbackDirection(HitContext ctx)
        {
            Vector3 dir = ctx.attackDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                return dir;
            }
            if (ctx.hitPoint.sqrMagnitude > 0.0001f)
            {
                Vector3 away = transform.position - ctx.hitPoint;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                {
                    return away;
                }
            }
            Vector3 fallback = -transform.forward;
            fallback.y = 0f;
            return fallback;
        }

        /// <summary>
        /// 以「命中點 → 角色位置」向量點積角色 forward / right 決定 4 方向。
        /// 命中點為零向量時退而求其次使用 HitDirection(取反,因為 HitDirection 是攻擊力方向)。
        /// </summary>
        private HitDirection ComputeHitDirection(HitContext ctx)
        {
            Vector3 toAttacker;
            if (ctx.hitPoint.sqrMagnitude > 0.0001f)
            {
                toAttacker = ctx.hitPoint - transform.position;
            }
            else if (ctx.attackDirection.sqrMagnitude > 0.0001f)
            {
                toAttacker = -ctx.attackDirection;
            }
            else
            {
                return HitDirection.Front;
            }
            toAttacker.y = 0f;
            if (toAttacker.sqrMagnitude < 0.0001f)
            {
                return HitDirection.Front;
            }
            toAttacker.Normalize();
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();
            float fDot = Vector3.Dot(forward, toAttacker);
            float rDot = Vector3.Dot(right, toAttacker);
            if (Mathf.Abs(fDot) > Mathf.Abs(rDot))
            {
                return fDot > 0f ? HitDirection.Front : HitDirection.Back;
            }
            return rDot > 0f ? HitDirection.Right : HitDirection.Left;
        }

        private void CancelActiveAttackAbilities()
        {
            List<GameplayAbilitySpec> attacks = null;
            foreach (GameplayAbilitySpec spec in _asc.GetAllAbilities())
            {
                if (!spec.IsActive || spec.AbilityDef == null)
                {
                    continue;
                }
                if (spec.AbilityDef.AbilityTag.MatchesTagHierarchy(GameplayTags.Ability.Attack.Root))
                {
                    attacks ??= new List<GameplayAbilitySpec>();
                    attacks.Add(spec);
                }
            }
            if (attacks == null)
            {
                return;
            }
            foreach (GameplayAbilitySpec spec in attacks)
            {
                spec.CancelAbility();
            }
        }

        /// <summary>
        /// HitStun 結束後同步 TopState — HitState / KnockbackState 轉場回 Idle 後,
        /// 這裡偵測到當前 State 已非任何受擊狀態,負責把 TopState 切回 Locomotion 並移除 HitStunned Tag。
        /// </summary>
        private void SyncHitStunTopState()
        {
            if (_topState != TopState.HitStun)
            {
                return;
            }
            if (_stateMachine.Current == _context.Hit || _stateMachine.Current == _context.Knockback)
            {
                return;
            }
            SetTopState(TopState.Locomotion);
            if (_asc != null)
            {
                _asc.OwnedTags.RemoveTag(GameplayTags.State.HitStunned);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 除錯按鍵:從 4 個方向依序(Front → Back → Left → Right)輪流觸發受擊,
        /// 方便在沒有敵人 AI 時驗證 HitState 流程。
        /// </summary>
        private void DebugTriggerHit()
        {
            HitDirection[] cycle = { HitDirection.Front, HitDirection.Back, HitDirection.Left, HitDirection.Right };
            HitDirection dir = cycle[_debugHitDirectionIndex % cycle.Length];
            _debugHitDirectionIndex++;
            Vector3 offset = dir switch
            {
                HitDirection.Front => transform.forward,
                HitDirection.Back => -transform.forward,
                HitDirection.Left => -transform.right,
                HitDirection.Right => transform.right,
                _ => transform.forward,
            };
            HitContext ctx = new HitContext
            {
                hitPoint = transform.position + offset * 2f,
                attackDirection = -offset,
                damage = _debugHitDamage,
                poiseDamage = _debugHitPoiseDamage,
                knockbackForce = _debugKnockbackDistance,
                attackTier = _debugHitAsKnockback ? AttackTier.Heavy : AttackTier.Normal,
                isHeavyAttack = _debugHitAsKnockback,
            };
            float poiseBefore = 0f;
            CombatAttributeSet combatSet = _asc != null ? _asc.GetAttributeSet<CombatAttributeSet>() : null;
            if (combatSet != null)
            {
                poiseBefore = combatSet.Poise.CurrentValue;
            }
            Vector3 externalBefore = _externalVelocity;
            OnHitReceived(ctx);
            Vector3 externalAfter = _externalVelocity;
            float tauLog = _hitReactionData != null ? _hitReactionData.ExternalVelocityDecayTau : 0f;
            string mode = _debugHitAsKnockback ? "Knockback" : "Stagger";
            string poiseInfo = combatSet != null
                ? $"Poise {poiseBefore:F0}→{combatSet.Poise.CurrentValue:F0}"
                : "(無 AttributeSet)";
            Debug.Log($"[NewGASPlayerController] DebugHit {dir} | {mode} {_debugKnockbackDistance:F1}m | {poiseInfo} | " +
                      $"ExternalVelocity {externalBefore.magnitude:F2}→{externalAfter.magnitude:F2} m/s " +
                      $"(τ={tauLog:F2}s)", this);
        }
#endif

        /// <summary>
        /// 每幀檢查耗盡鎖定是否可解除 — 僅在 <see cref="LocomotionStateContext.IsSprintStaminaDepleted"/>
        /// 為 true 時比對當前 Stamina 是否已恢復至 <see cref="LocomotionConfig.SprintStaminaThreshold"/>,
        /// 達標即清除旗標允許再次衝刺。鎖定 false 時不做事,避免對正常衝刺路徑造成干擾。
        /// </summary>
        private void SyncSprintDepleted()
        {
            if (_context == null)
            {
                return;
            }
            if (!_context.IsSprintStaminaDepleted)
            {
                return;
            }
            IStaminaBudget budget = _context.StaminaBudget;
            if (budget == null)
            {
                _context.IsSprintStaminaDepleted = false;
                return;
            }
            if (budget.Current >= _config.SprintStaminaThreshold)
            {
                _context.IsSprintStaminaDepleted = false;
            }
        }

        /// <summary>
        /// 將 IsDodgeLocked 旗標同步到 ASC 上的 State.DodgeNonCancellable Tag,
        /// 讓 GameplayAbility.CheckTagRequirements 可阻擋攻擊類能力於 Dodge 鎖定期啟動。
        /// 只在狀態變化時才動 Tag,避免每幀 Add/Remove 觸發其他訂閱者。
        /// </summary>
        private void SyncDodgeLockTag()
        {
            if (_context == null || _asc == null)
            {
                return;
            }
            if (_context.IsDodgeLocked == _prevDodgeLocked)
            {
                return;
            }
            if (_context.IsDodgeLocked)
            {
                _asc.OwnedTags.AddTag(GameplayTags.State.DodgeNonCancellable);
            }
            else
            {
                _asc.OwnedTags.RemoveTag(GameplayTags.State.DodgeNonCancellable);
            }
            _prevDodgeLocked = _context.IsDodgeLocked;
        }

        private void UpdateJumpTimers(float deltaTime)
        {
            // 使用 Context.IsGrounded(本幀 UpdateInputContext 已透過 GroundSensor 更新),
            // 避免 CharacterController.isGrounded 於垂直速度略為正值時誤判離地造成 Coyote Time 提早啟動
            if (_context.IsGrounded)
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
            // 頂層閘門 — 第一步只檢查 TopState,第二步會整合 ASC.OwnedTags 阻擋
            if (!CanJump)
            {
                return;
            }
            if (_context.IsAirborne)
            {
                return;
            }
            if (_stateMachine.Current == _context.FastRunTurn)
            {
                return;
            }
            // 閃避鎖定期內不可跳躍
            if (_context.IsDodgeLocked)
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
            // 記錄起跳資訊 — 供 CanDeployGlider 走「跳躍路徑」判定:必須掉到比起跳點低才能展開
            _jumpStartY = transform.position.y;
            _airborneFromJump = true;
            _stateMachine.ChangeState(_context.Jump);
        }

        private void UpdateDodgeTimer(float deltaTime)
        {
            if (_inputReader.DodgePressedThisFrame)
            {
                _dodgeBufferTimer = _config.DodgeBufferTime;
            }
            else if (_dodgeBufferTimer > 0f)
            {
                _dodgeBufferTimer -= deltaTime;
            }
        }

        private void TryTriggerDodge()
        {
            if (_dodgeBufferTimer <= 0f)
            {
                return;
            }
            // Ability 期間嘗試攻擊取消接 Dodge:
            //   有 State.AttackNonCancellable Tag(攻擊鎖定期內)→ 放棄,buffer 繼續扣減,
            //     鎖定期結束前若按鍵還在 buffer 內仍可生效。
            //   無 Tag → 取消活躍攻擊,OnAbilityEnded → ExitAbilityState 會把 TopState 切回 Locomotion,
            //     接著走下方一般流程觸發 Dodge。
            if (_topState == TopState.Ability)
            {
                if (!TryCancelAttacksForDodge())
                {
                    return;
                }
            }
            if (!CanDodge)
            {
                // 耐力不足或其他阻擋條件 — 不歸零 buffer,讓預輸入期間(DodgeBufferTime)內
                // 若耐力回升或阻擋條件解除仍能自動接上,符合預輸入緩衝的手感。
                return;
            }
            // CanDodge 已含 CanAffordDodge 檢查,此處 TryConsume 預期必成功;
            // 保險起見仍檢查回傳值,失敗時不觸發 Dodge 且保留 buffer(同上邏輯)。
            if (!_context.TryConsumeDodgeStamina())
            {
                return;
            }
            _dodgeBufferTimer = 0f;
            // 若已在 Dodge 狀態（鎖定期結束後的連續閃避），需 ForceChangeState 才能重新觸發 Enter
            _stateMachine.ForceChangeState(_context.Dodge);
            BeginDodgeIFrames();
        }

        /// <summary>
        /// 啟動一輪閃避無敵窗口計時 — 連續閃避會重新計時。實際加/移除 Invincible Tag 由 UpdateDodgeIFrames 依時間窗處理。
        /// </summary>
        private void BeginDodgeIFrames()
        {
            _dodgeIFrameElapsed = 0f;
            _dodgeIFrameWindowRunning = _config != null && _config.DodgeInvincibilityDuration > 0f;
            _dodgeIFrameSlowmoUsed = false;
        }

        /// <summary>
        /// 通知「閃避無敵期間被攻擊到」— 由近戰受擊(OnHitReceived)與投射物穿透時呼叫。
        /// 僅在無敵來自閃避 i-frame、且本輪尚未觸發過時生效(自帶單次防抖,避免同一輪多次命中疊加慢動作)。
        /// </summary>
        public void NotifyDodgeIFrameHit()
        {
            if (!_dodgeIFrameTagAdded || _dodgeIFrameSlowmoUsed)
            {
                return;
            }
            if (_config == null || _config.DodgeIFrameHitSlowHoldDuration <= 0f)
            {
                return;
            }
            _dodgeIFrameSlowmoUsed = true;
            // 觸發瞬間在原地產生定格殘影(沿用換武器的凍結姿態殘影)
            if (_weaponManager != null)
            {
                _weaponManager.SpawnFreezePoseAfterImage();
            }
            if (_dodgeSlowmoRoutine != null)
            {
                StopCoroutine(_dodgeSlowmoRoutine);
            }
            _dodgeSlowmoRoutine = StartCoroutine(DodgeIFrameSlowmoRoutine());
        }

        private IEnumerator DodgeIFrameSlowmoRoutine()
        {
            float scale = Mathf.Clamp(_config.DodgeIFrameHitSlowScale, 0.01f, 1f);
            Time.timeScale = scale;
            Time.fixedDeltaTime = 0.02f * scale;
            float hold = _config.DodgeIFrameHitSlowHoldDuration;
            float t = 0f;
            while (t < hold)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return TimeScaleUtility.SmoothTimeScale(Time.timeScale, 1f, _config.DodgeIFrameHitSlowRecoverDuration);
            _dodgeSlowmoRoutine = null;
        }

        /// <summary>
        /// 依 LocomotionConfig 的無敵窗口逐幀維護 State.Invincible Tag。
        /// 時間窗 = [StartTime, StartTime + Duration]。無敵期間免疫近戰傷害,投射物亦會穿透(由 ProjectileBehaviour 檢查同一 Tag)。
        /// 採時間驅動而非綁定 Dodge 狀態,讓「閃避取消接其他動作」後剩餘的無敵幀仍能正常結束。
        /// </summary>
        private void UpdateDodgeIFrames(float deltaTime)
        {
            if (!_dodgeIFrameWindowRunning || _asc == null)
            {
                return;
            }
            _dodgeIFrameElapsed += deltaTime;
            float start = _config.DodgeInvincibilityStartTime;
            float end = start + _config.DodgeInvincibilityDuration;
            bool shouldBeInvincible = _dodgeIFrameElapsed >= start && _dodgeIFrameElapsed < end;
            if (shouldBeInvincible && !_dodgeIFrameTagAdded)
            {
                _asc.OwnedTags.AddTag(GameplayTags.State.Invincible);
                _dodgeIFrameTagAdded = true;
            }
            else if (!shouldBeInvincible && _dodgeIFrameTagAdded)
            {
                _asc.OwnedTags.RemoveTag(GameplayTags.State.Invincible);
                _dodgeIFrameTagAdded = false;
            }
            if (_dodgeIFrameElapsed >= end)
            {
                _dodgeIFrameWindowRunning = false;
            }
        }

        /// <summary>
        /// 強制結束閃避無敵 — 死亡等情境呼叫,確保 Invincible Tag 不殘留。
        /// </summary>
        private void ClearDodgeIFrames()
        {
            _dodgeIFrameWindowRunning = false;
            if (_dodgeIFrameTagAdded && _asc != null)
            {
                _asc.OwnedTags.RemoveTag(GameplayTags.State.Invincible);
            }
            _dodgeIFrameTagAdded = false;
            if (_dodgeSlowmoRoutine != null)
            {
                StopCoroutine(_dodgeSlowmoRoutine);
                _dodgeSlowmoRoutine = null;
            }
            _dodgeIFrameSlowmoUsed = false;
        }

        /// <summary>
        /// 是否允許在空中展開滑翔翼 — 共同條件 + 雙路徑判定:
        ///   共同:TopState Locomotion + Airborne + 非 Gliding + 距離地面足夠
        ///   路徑 A(跳躍浮空):currentY < _jumpStartY — 玩家必須掉到比起跳點低才能展開
        ///   路徑 B(墜落浮空):_timeSinceGrounded > GliderMinAirborneTime — 沒跳的純墜落要等指定秒數
        /// 設計用途:UI 灰化、能力 PreCondition、Debug HUD。
        /// </summary>
        public bool CanDeployGlider
        {
            get
            {
                if (_topState != TopState.Locomotion)
                {
                    return false;
                }
                if (_context == null || _stateMachine == null)
                {
                    return false;
                }
                if (_context.IsGliding)
                {
                    return false;
                }
                if (!_context.IsAirborne)
                {
                    return false;
                }
                if (!HasMinAltitudeForGlider())
                {
                    return false;
                }
                if (_airborneFromJump)
                {
                    // 跳躍路徑 — 必須掉到低於起跳點才允許,防止「平地起跳當下就開傘」
                    return transform.position.y < _jumpStartY;
                }
                // 墜落路徑 — 走時間門檻,避免下樓梯瞬間抖動誤觸
                return _timeSinceGrounded > _config.GliderMinAirborneTime;
            }
        }

        /// <summary>
        /// 空中再按跳躍鍵 — 嘗試展開或收起滑翔翼。
        /// 展開:CanDeployGlider 為 true 時切入 GliderState。
        /// 收起:已在滑翔中按下跳躍 → 切回 JumpLoop 自由落體(用 EnterFallLoop 旗標跳過 JumpStart)。
        /// 使用 JumpPressedThisFrame 直接讀取,不走 JumpBuffer(滑翔展開是即時動作,不該緩衝)。
        /// </summary>
        private void TryTriggerGlider()
        {
            if (_inputReader == null || !_inputReader.JumpPressedThisFrame)
            {
                return;
            }
            if (_context == null || _stateMachine == null)
            {
                return;
            }
            // 在滑翔中再按跳躍 → 收起,回到自由落體
            if (_context.IsGliding)
            {
                _jumpBufferTimer = 0f;
                _context.EnterFallLoop = true;
                _stateMachine.ChangeState(_context.Jump);
                return;
            }
            if (!CanDeployGlider)
            {
                return;
            }
            _jumpBufferTimer = 0f;
            // 展開滑翔翼即重置下落基準點 — 之前累積的高度不算數,後續落差從這裡開始
            _maxAirborneY = transform.position.y;
            _stateMachine.ChangeState(_context.Glider);
        }

        /// <summary>
        /// 從角色腳底往下 Raycast,確認距離地面是否超過 GliderMinHeightAboveGround。
        /// 起點為膠囊底,使用 LocomotionConfig.GroundMask;Raycast 距離為「需要的最低高度」,
        /// 命中代表離地不夠高 → 回 false。
        /// </summary>
        private bool HasMinAltitudeForGlider()
        {
            float minHeight = _config.GliderMinHeightAboveGround;
            if (minHeight <= 0f)
            {
                return true;
            }
            if (_characterController == null)
            {
                return true;
            }
            float halfHeight = _characterController.height * 0.5f;
            Vector3 origin = transform.position + _characterController.center - Vector3.up * Mathf.Max(0f, halfHeight - 0.05f);
            return !Physics.Raycast(
                origin,
                Vector3.down,
                minHeight,
                _config.GroundMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 非跳躍離地的下落偵測 — 走下懸崖、被撞下平台等情境,讓角色播放 JumpLoop。
        /// 觸發條件(全部為 true):
        ///   1. 當前 Locomotion 狀態不是 Jump / Dodge / FastRunTurn(避免插入這些已自管空中/位移的狀態)
        ///   2. Context.IsAirborne == false(還沒在空中流程)
        ///   3. Context.IsGrounded == false(GroundSensor 沒偵測到 0.2m 內地面 — 樓梯與小台階自動被擋住)
        ///   4. _timeSinceGrounded > Config.FallTriggerDelay(離地時間足夠 — 過濾 isGrounded 瞬間抖動)
        ///   5. _verticalVelocity < Config.FallTriggerVerticalVelocity(向下加速到指定速度 — 過濾貼地時的 -1f 鎖定值)
        /// 透過 EnterFallLoop 旗標讓 JumpState 直接進 Loop phase,跳過 JumpStart 與起跳衝量。
        /// </summary>
        private void TryTriggerFall()
        {
            if (_context == null || _stateMachine == null)
            {
                return;
            }
            if (_context.IsAirborne)
            {
                return;
            }
            ILocomotionState current = _stateMachine.Current;
            if (current == _context.Jump || current == _context.Dodge || current == _context.FastRunTurn)
            {
                return;
            }
            if (_context.IsGrounded)
            {
                return;
            }
            if (_timeSinceGrounded <= _config.FallTriggerDelay)
            {
                return;
            }
            if (_verticalVelocity >= _config.FallTriggerVerticalVelocity)
            {
                return;
            }
            _context.EnterFallLoop = true;
            _airborneFromJump = false;
            _stateMachine.ChangeState(_context.Jump);
        }

        /// <summary>
        /// 重置下落傷害追蹤 — 把累積的最高點 Y 拉到玩家當前 Y,等於清掉之前的下落距離。
        /// 適用情境:彈跳板、上拋類能力、救命光柱等「強制中斷自由落體」的外部觸發。
        /// 不影響 _trackingFall 旗標(玩家可能仍在空中),只重置基準點;若想完整清除追蹤,呼叫者可在 Launch 之後手動操作。
        /// </summary>
        public void ResetFallDamageTracking()
        {
            _maxAirborneY = transform.position.y;
        }

        /// <summary>
        /// 每幀追蹤下落距離 — 計算離地最高點 Y 與當前 Y 的落差,落地當下若落差 > Threshold 則施加傷害。
        /// 規則:
        ///   - 貼地:落差判定(若有 tracking)→ 套傷害 → 重置基準為當下 Y
        ///   - 滯空且未追蹤:啟動追蹤,基準為當下 Y
        ///   - 滯空 + 滑翔中:基準持續貼齊當下 Y(滑翔等於免疫,落差歸零)
        ///   - 滯空 + 自由落體:基準取「歷史最高 Y」(只往上,不往下)
        /// 只在 TopState.Locomotion 內 Tick(由 Update Locomotion 分支呼叫),
        /// 受擊/能力/死亡期間不累計;TopState 離開 Locomotion 時 SetTopState 會清旗標。
        /// </summary>
        private void TickFallTracking()
        {
            if (_context == null)
            {
                return;
            }
            float currentY = transform.position.y;
            if (_context.IsGrounded)
            {
                if (_trackingFall)
                {
                    // 只有「真正從空中落地的那一幀」才清旗標 — 避免起跳當幀(物理還沒套用、IsGrounded 仍為 true)
                    // 把剛被 TryTriggerJump 設好的 _airborneFromJump 又清掉,造成平地起跳能立刻開傘的 Bug
                    float fallDistance = _maxAirborneY - currentY;
                    if (fallDistance > _config.FallDamageThreshold)
                    {
                        ApplyFallDamage(fallDistance);
                    }
                    _trackingFall = false;
                    _airborneFromJump = false;
                }
                _maxAirborneY = currentY;
                return;
            }
            if (!_trackingFall)
            {
                _trackingFall = true;
                _maxAirborneY = currentY;
                return;
            }
            if (_context.IsGliding)
            {
                _maxAirborneY = currentY;
                return;
            }
            if (currentY > _maxAirborneY)
            {
                _maxAirborneY = currentY;
            }
        }

        /// <summary>
        /// 計算並施加下落傷害 — 走 CombatAttributeSet.ApplyDamage 路徑,套用既有減傷流程。
        /// Invincible Tag 或 ASC/AttributeSet 缺失時跳過;傷害計算交給 ComputeFallDamage。
        /// 傷害施加完觸發 FallDamageApplied 事件供 UI/SFX 訂閱。
        /// </summary>
        private void ApplyFallDamage(float fallDistance)
        {
            if (_asc == null)
            {
                return;
            }
            if (_asc.OwnedTags.HasTag(GameplayTags.State.Invincible))
            {
                return;
            }
            CombatAttributeSet combatSet = _asc.GetAttributeSet<CombatAttributeSet>();
            if (combatSet == null)
            {
                return;
            }
            float damage = ComputeFallDamage(fallDistance);
            if (damage <= 0f)
            {
                return;
            }
            combatSet.ApplyDamage(damage, null);
            FallDamageApplied?.Invoke(fallDistance, damage);
        }

        /// <summary>
        /// 下落落差 → 傷害值的線性內插:
        ///   落差 <= Threshold        → 0(不扣)
        ///   落差 >= MaxDistance      → AtMaxDistance(飽和)
        ///   中間區段                 → Lerp(0, AtMaxDistance, t)
        /// 邊界:MaxDistance <= Threshold 時直接回 AtMaxDistance,避免除以 0。
        /// </summary>
        private float ComputeFallDamage(float fallDistance)
        {
            float threshold = _config.FallDamageThreshold;
            if (fallDistance <= threshold)
            {
                return 0f;
            }
            float maxDist = _config.FallDamageMaxDistance;
            float maxDmg = _config.FallDamageAtMaxDistance;
            if (maxDist <= threshold)
            {
                return maxDmg;
            }
            float t = Mathf.Clamp01((fallDistance - threshold) / (maxDist - threshold));
            return t * maxDmg;
        }

        /// <summary>
        /// 取消所有匹配 Ability.Attack 的活躍能力以讓位給 Dodge。
        /// 回傳:
        ///   true  — 沒有 AttackNonCancellable 阻擋,(若有攻擊)已全部取消,可繼續觸發 Dodge
        ///   false — 處於 AttackNonCancellable 鎖定期,本次 Dodge 必須放棄
        /// 取消後 OnAbilityEnded 會把 TopState 自動切回 Locomotion(由 ExitAbilityState 接手)。
        /// </summary>
        private bool TryCancelAttacksForDodge()
        {
            if (_asc == null)
            {
                return true;
            }
            if (_asc.OwnedTags.HasTag(GameplayTags.State.AttackNonCancellable))
            {
                return false;
            }
            // 收集後再取消,避免在迭代中修改集合
            List<GameplayAbilitySpec> attacks = null;
            foreach (GameplayAbilitySpec spec in _asc.GetAllAbilities())
            {
                if (!spec.IsActive || spec.AbilityDef == null)
                {
                    continue;
                }
                if (spec.AbilityDef.AbilityTag.MatchesTagHierarchy(GameplayTags.Ability.Attack.Root))
                {
                    attacks ??= new List<GameplayAbilitySpec>();
                    attacks.Add(spec);
                }
            }
            if (attacks != null)
            {
                foreach (GameplayAbilitySpec spec in attacks)
                {
                    spec.CancelAbility();
                }
            }
            return true;
        }

        /// <summary>
        /// Root Motion 核心處理 — 接收 Animator 的位移與旋轉差值,套用重力、水平補間後驅動 CharacterController。
        /// 呼叫來源:
        ///   (a) 扁平層級 — 由上方 OnAnimatorMove() 直接呼叫。
        ///   (b) 父子層級 — 由子物件的 RootMotionRelay.OnAnimatorMove() 呼叫。
        /// </summary>
        public void OnRootMotionUpdate(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (_characterController == null)
            {
                return;
            }
            // 死亡 — RM 驅動位移與旋轉(無重力、無水平補間、無能力覆蓋)。
            // Animancer 已設 UnscaledTime,Time.timeScale=0 下仍持續產出 frame delta。
            if (_topState == TopState.Dead)
            {
                _characterController.Move(deltaPosition);
                transform.rotation *= deltaRotation;
                return;
            }
            float deltaTime = Time.deltaTime;
            bool isAirborne = _context != null && _context.IsAirborne;
            // 水平位移來源:Ability/HitStun 走純 RootMotion;Locomotion 走空中 velocity 或地面 continuation 補間
            // SuppressRootMotionPosition 為 true 時跳過 RM 水平位移,讓 MoveToTarget(Snap/Pierce)全權控制
            bool suppressed = _topState != TopState.Locomotion && SuppressRootMotionPosition;
            Vector3 horizontalDelta;
            if (suppressed)
            {
                horizontalDelta = Vector3.zero;
            }
            else if (_topState != TopState.Locomotion)
            {
                horizontalDelta = new Vector3(deltaPosition.x, 0f, deltaPosition.z);
            }
            else if (isAirborne)
            {
                horizontalDelta = _context.JumpHorizontalVelocity * deltaTime;
            }
            else
            {
                Vector3 rawHorizontal = new Vector3(deltaPosition.x, 0f, deltaPosition.z);
                horizontalDelta = ApplyHorizontalContinuation(rawHorizontal, deltaTime);
            }
            // 外部速度(擊退 / 爆炸 / 氣流)— 除 Snap/Pierce 壓制期間外,全狀態疊加後指數衰減
            if (!suppressed)
            {
                horizontalDelta += TickExternalVelocity(deltaTime);
            }
            _prevHorizontalDelta = horizontalDelta;
            // 垂直速度(重力):所有非 Dead 狀態一致套用
            bool groundedForGravity = _context != null ? _context.IsGrounded : _characterController.isGrounded;
            if (_context != null && _context.PendingJumpImpulse > 0f)
            {
                _verticalVelocity = _context.PendingJumpImpulse;
                _context.PendingJumpImpulse = 0f;
            }
            else if (groundedForGravity && !isAirborne)
            {
                _verticalVelocity = -1f;
            }
            else
            {
                _verticalVelocity -= _config.Gravity * deltaTime;
            }
            // 滑翔翼:重力照常作用讓上升動能衰減,但下降速度被 clamp 在 -GliderDescentSpeed,
            // 達到「上升期照常減速、下降期等速飄落」的滑翔感
            if (_context != null && _context.IsGliding && _verticalVelocity < -_config.GliderDescentSpeed)
            {
                _verticalVelocity = -_config.GliderDescentSpeed;
            }
            float verticalAnimDelta = isAirborne ? 0f : deltaPosition.y;
            Vector3 finalDelta = new Vector3(horizontalDelta.x, verticalAnimDelta + _verticalVelocity * deltaTime, horizontalDelta.z);
            _characterController.Move(finalDelta);
            if (_context != null)
            {
                _context.LastHorizontalVelocity = deltaTime > 0f ? horizontalDelta / deltaTime : Vector3.zero;
            }
            // 旋轉:僅在 LocomotionStateMachine 明確要求時才套用 RM 旋轉（如 FastRunTurn）。
            // Ability/HitStun 由能力自己控制旋轉（DOLookAt、即時轉向等），不自動套 deltaRotation。
            bool applyRootRotation = _context != null && _context.UseRootMotionRotation;
            if (applyRootRotation)
            {
                transform.rotation *= deltaRotation;
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

        /// <summary>
        /// 推進外部速度一幀:回傳本幀位移貢獻,同時對 _externalVelocity 做指數衰減。
        /// 衰減半衰期由 LocomotionConfig.ExternalVelocityDecayTau 決定;近零時直接歸零避免尾部漂移。
        /// </summary>
        private Vector3 TickExternalVelocity(float deltaTime)
        {
            if (_externalVelocity.sqrMagnitude < 0.0001f)
            {
                _externalVelocity = Vector3.zero;
                return Vector3.zero;
            }
            Vector3 contribution = _externalVelocity * deltaTime;
            float tau = _hitReactionData != null ? _hitReactionData.ExternalVelocityDecayTau : 0f;
            if (tau > 0.0001f)
            {
                _externalVelocity *= Mathf.Exp(-deltaTime / tau);
            }
            else
            {
                _externalVelocity = Vector3.zero;
            }
            return contribution;
        }

        /// <summary>
        /// 施加一次性速度脈衝(世界座標,單位:公尺/秒)。
        /// 僅疊加 X/Z 分量至 _externalVelocity(走水平衰減管道);Y 分量目前直接忽略。
        /// 上挑 / 下砸類垂直脈衝未來如需支援,可再加 AddLaunch / AddSlam API 分流,不要複用此 API 的 Y 通道。
        /// Dead 狀態下呼叫無效。非擊退用途(爆炸、氣流、拉鉤)也走此 API。
        /// </summary>
        public void AddForce(Vector3 velocity)
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            _externalVelocity.x += velocity.x;
            _externalVelocity.z += velocity.z;
        }

        /// <summary>
        /// 以「擊退漸近距離(公尺)」為單位施加水平外力。
        /// 換算:v₀ = distance / τ;指數衰減下漸近總位移約等於 distance,95% 於 3τ 秒內完成。
        /// direction 的 X/Z 定義水平飛行方向(會被 normalize 到水平面),Y 分量一律忽略。
        /// </summary>
        public void AddKnockback(Vector3 direction, float distance)
        {
            if (distance <= 0f)
            {
                return;
            }
            float tau = _hitReactionData != null ? _hitReactionData.ExternalVelocityDecayTau : 0f;
            if (tau <= 0.0001f)
            {
                return;
            }
            Vector3 horizontal = new Vector3(direction.x, 0f, direction.z);
            float horizontalMag = horizontal.magnitude;
            if (horizontalMag < 0.0001f)
            {
                return;
            }
            AddForce(horizontal / horizontalMag * (distance / tau));
        }

        private void UpdateInputContext()
        {
            Vector2 raw = _inputReader.RawMove;
            Vector3 desired = BuildCameraRelativeDirection(raw);
            _context.InputMagnitude = Mathf.Clamp01(raw.magnitude);
            _context.DesiredWorldDirection = desired;
            _context.RunButtonHeld = _inputReader.RunHeld;
            // 每幀呼叫一次 GroundSensor.Probe,結果作為 Context 內所有地面判斷的單一真實來源。
            // CharacterController.isGrounded 作為快速路徑,只在離地時才實際發出 SphereCast。
            _context.IsGrounded = _groundSensor != null ? _groundSensor.Probe() : _characterController.isGrounded;
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

        /// <summary>
        /// 驗證 Inspector 必須預先指定的引用（不含 _animancer,因為模型可能在執行時才生成）。
        /// </summary>
        private bool ValidateCoreReferences()
        {
            if (_config == null)
            {
                Debug.LogError("[NewGASPlayerController] LocomotionConfig 未指定。", this);
                return false;
            }
            if (_animationSet == null)
            {
                Debug.LogError("[NewGASPlayerController] LocomotionAnimationSet 未指定。", this);
                return false;
            }
            if (_inputReader == null)
            {
                Debug.LogError("[NewGASPlayerController] LocomotionInputReader 未指定。", this);
                return false;
            }
            if (_cameraTransform == null)
            {
                Debug.LogError("[NewGASPlayerController] 攝影機 Transform 未指定。", this);
                return false;
            }
            return true;
        }

        #region Model / Locomotion Initialization

        /// <summary>
        /// 建立 LocomotionAnimatorDriver、StateContext、StateMachine 並啟動初始狀態。
        /// 需要 _animancer 已非 null 才能呼叫。
        /// 若 _pendingRefreshStateType 有值(武器切換後模型重建),會恢復到對應狀態並設 IsRefreshingFromModelSwitch 旗標。
        /// 若 _pendingResumeSlot 也有值(PrepareForModelSwitch 有在銷毀前抓到 slot + NormalizedTime),
        /// 各 Resumable State 會嘗試從新 AnimationSet 的同名 slot 同進度接播;失敗時退回「直接進 Loop」的 fallback。
        /// </summary>
        private void InitializeLocomotion()
        {
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
                Glider = new GliderState(),
                Dodge = new DodgeState(),
                Hit = new HitState(),
                Knockback = new KnockbackState(),
                Death = new DeathState(),
            };
            _context.HitReactionData = _hitReactionData;
            _context.DeathData = _deathData;
            _context.StaminaBudget = BuildStaminaBudget();
            _context.ResumeSlot = _pendingResumeSlot;
            _context.ResumeNormalizedTime = _pendingResumeNormalizedTime;
            // 恢復武器切換前的 context 旗標,讓 Jump/Dodge/FastRunTurn 持續(rebuild 後本來會是 default)
            _context.IsAirborne = _pendingIsAirborne;
            _context.IsDodgeLocked = _pendingIsDodgeLocked;
            _context.UseRootMotionRotation = _pendingUseRootMotionRotation;
            _context.JumpHorizontalVelocity = _pendingJumpHorizontalVelocity;
            _context.TurnDirection = _pendingTurnDirection;
            _stateMachine = new LocomotionStateMachine(_context);
            ILocomotionState initialState = ResolveResumeState(_pendingRefreshStateType);
            _context.IsRefreshingFromModelSwitch = _pendingRefreshStateType != null;
            _stateMachine.Start(initialState);
            _context.IsRefreshingFromModelSwitch = false;
            _context.ResumeSlot = Player.Locomotion.LocomotionAnimSlot.None;
            _context.ResumeNormalizedTime = 0f;
            _pendingRefreshStateType = null;
            _pendingResumeSlot = Player.Locomotion.LocomotionAnimSlot.None;
            _pendingResumeNormalizedTime = 0f;
            _pendingIsAirborne = false;
            _pendingIsDodgeLocked = false;
            _pendingUseRootMotionRotation = false;
            _pendingJumpHorizontalVelocity = Vector3.zero;
            _pendingTurnDirection = 0;
            _animatorDriver.ConfigureFlinchLayer(_flinchAvatarMask);
            _context.GliderVFX = _gliderVFX;
            _locomotionInitialized = true;
        }

        /// <summary>
        /// 建立耐力 adapter — 從 ASC 取 CombatAttributeSet 並包裝成 Locomotion asmdef 看得到的介面。
        /// 缺 ASC 或 CombatAttributeSet 時回 null,Context 視為無限耐力(不扣、不鎖)。
        /// 供衝刺持續消耗、閃避一次性消耗共用。
        /// </summary>
        private IStaminaBudget BuildStaminaBudget()
        {
            if (_asc == null)
            {
                return null;
            }
            CombatAttributeSet set = _asc.GetAttributeSet<CombatAttributeSet>();
            if (set == null)
            {
                return null;
            }
            return new CombatAttributeStaminaBudget(set);
        }

        /// <summary>
        /// 耐力 adapter — 將 CombatAttributeSet.Stamina 介接到 Locomotion 層,
        /// 避免 Player.Locomotion asmdef 直接依賴 GAS 類別(因 GAS 目前位於預設 Assembly-CSharp,
        /// 無法被獨立 asmdef 引用)。
        /// </summary>
        private sealed class CombatAttributeStaminaBudget : IStaminaBudget
        {
            private readonly CombatAttributeSet _set;
            public CombatAttributeStaminaBudget(CombatAttributeSet set) { _set = set; }
            public float Current => _set.Stamina.CurrentValue;
            public bool TryConsume(float amount) => _set.TryConsumeStamina(amount);
        }

        /// <summary>
        /// 依舊狀態類型映射到新 context 的對應狀態實例。
        /// 涵蓋所有 Locomotion 可見動作:Idle / Walk / Run / FastRun / FastRunTurn / FastRunStop / Jump / Dodge。
        /// Hit / Knockback 不接 — 受擊時切武器由 Q3 守門(WeaponManager.CanSwitch)於 TopState 層阻擋,此處保險 fallback 到 Idle。
        /// </summary>
        private ILocomotionState ResolveResumeState(System.Type previousStateType)
        {
            if (previousStateType == null) return _context.Idle;
            if (previousStateType == typeof(WalkState)) return _context.Walk;
            if (previousStateType == typeof(RunState)) return _context.Run;
            if (previousStateType == typeof(FastRunState)) return _context.FastRun;
            if (previousStateType == typeof(FastRunTurnState)) return _context.FastRunTurn;
            if (previousStateType == typeof(FastRunStopState)) return _context.FastRunStop;
            if (previousStateType == typeof(JumpState)) return _context.Jump;
            if (previousStateType == typeof(GliderState)) return _context.Glider;
            if (previousStateType == typeof(DodgeState)) return _context.Dodge;
            return _context.Idle;
        }

        /// <summary>
        /// 強制所有 Animancer 狀態的 ApplyFootIK = true,涵蓋 Locomotion / HitReaction / Death / Ability 所有播放路徑。
        /// 每幀執行 — ClipTransition.Apply() 會在 Play 時把 state.ApplyFootIK 重設為 transition 自己的值,
        /// 故無法靠一次性初始化解決。已正確的 state 用 if 守衛跳過,避免重複寫入 Playable。
        /// </summary>
        private void EnforceFootIK()
        {
            if (!_applyFootIKToAllStates || _animancer == null)
            {
                return;
            }
            foreach (AnimancerState state in _animancer.States)
            {
                if (!state.ApplyFootIK)
                {
                    state.ApplyFootIK = true;
                }
            }
        }

        /// <summary>
        /// 自動偵測子物件的 AnimancerComponent 並初始化 Locomotion。
        /// 父子層級時由 Start / Update 呼叫,處理 WeaponManager 動態生成模型的情境。
        /// </summary>
        private void TryAutoDetectModel()
        {
            if (_animancer == null)
            {
                // 跳過自身 GameObject,只搜尋子物件的 AnimancerComponent
                // (父物件上可能殘留舊的 AnimancerComponent,不是模型用的)
                foreach (AnimancerComponent candidate in GetComponentsInChildren<AnimancerComponent>())
                {
                    if (candidate.gameObject != gameObject)
                    {
                        _animancer = candidate;
                        break;
                    }
                }
            }
            if (_animancer == null)
            {
                return;
            }
            EnsureRootMotionRelay();
            InitializeLocomotion();
        }

        /// <summary>
        /// 確保持有 Animator 的子物件上有 RootMotionRelay。
        /// 模型動態生成時自動掛載,不需要手動修改 Prefab。
        /// </summary>
        private void EnsureRootMotionRelay()
        {
            if (_animancer == null || _animancer.gameObject == gameObject)
            {
                return;
            }
            Animator childAnimator = _animancer.GetComponent<Animator>();
            if (childAnimator != null && _animancer.GetComponent<RootMotionRelay>() == null)
            {
                _animancer.gameObject.AddComponent<RootMotionRelay>();
            }
        }

        /// <summary>
        /// 武器切換前必須呼叫 — 擷取當前 Animancer 正在播的 slot 與 NormalizedTime,
        /// 供後續 SetupModel → InitializeLocomotion 把新 AnimationSet 接播到相同進度。
        /// 必須在舊模型被 Destroy **之前** 呼叫,因為 Unity 的 fake-null 行為會讓 Destroy 後立刻無法讀取 AnimancerState。
        /// 未初始化、_animancer 為 null、或當前狀態未實作 IResumableLocomotionState 時,
        /// 只記錄狀態類型供 ResolveResumeState 使用,slot/time 留為 None/0 讓新 State 走 fallback。
        /// </summary>
        public void PrepareForModelSwitch()
        {
            _pendingResumeSlot = Player.Locomotion.LocomotionAnimSlot.None;
            _pendingResumeNormalizedTime = 0f;
            _pendingIsAirborne = false;
            _pendingIsDodgeLocked = false;
            _pendingUseRootMotionRotation = false;
            _pendingJumpHorizontalVelocity = Vector3.zero;
            _pendingTurnDirection = 0;
            if (!_locomotionInitialized)
            {
                return;
            }
            _pendingRefreshStateType = _stateMachine?.Current?.GetType();
            if (_context != null)
            {
                // 必要的 context 旗標 — 這些在 rebuild context 時會被重置為 default,要顯式保留才能讓 Jump/Dodge/Turn 持續
                _pendingIsAirborne = _context.IsAirborne;
                _pendingIsDodgeLocked = _context.IsDodgeLocked;
                _pendingUseRootMotionRotation = _context.UseRootMotionRotation;
                _pendingJumpHorizontalVelocity = _context.JumpHorizontalVelocity;
                _pendingTurnDirection = _context.TurnDirection;
            }
            if (_stateMachine?.Current is Player.Locomotion.States.IResumableLocomotionState resumable)
            {
                _pendingResumeSlot = resumable.CurrentSlot;
                _pendingResumeNormalizedTime = resumable.CurrentNormalizedTime;
            }
        }

        /// <summary>
        /// 外部系統(WeaponManager 等)主動設定 AnimancerComponent 與 per-weapon 的 Locomotion / HitReaction / Death 資料。
        /// 會重建整個 Locomotion 狀態機,適用於武器切換後模型更換的情境。
        /// 舊狀態類型會被記錄以供 InitializeLocomotion 恢復(Idle/Walk/Run/FastRun 保留;其餘 fallback 到 Idle)。
        /// 四個 SO 參數為 null 時沿用 Inspector 預設或切換前的值;非 null 時覆蓋,讓每把武器可帶自己的移動參數 / 動畫集 / 受擊 / 死亡資料。
        /// </summary>
        public void SetupModel(
            AnimancerComponent animancer,
            LocomotionConfig config = null,
            LocomotionAnimationSet animationSet = null,
            HitReactionData hitReactionData = null,
            PlayerDeathData deathData = null)
        {
            // 若 PrepareForModelSwitch 未被呼叫(例:舊 Controller 路徑或 late model detect),仍做 fallback 擷取(僅狀態類型,無 NormalizedTime)。
            if (_locomotionInitialized && _pendingRefreshStateType == null)
            {
                _pendingRefreshStateType = _stateMachine?.Current?.GetType();
            }
            if (animancer != null)
            {
                _animancer = animancer;
            }
            if (config != null)
            {
                _config = config;
            }
            if (animationSet != null)
            {
                _animationSet = animationSet;
            }
            if (hitReactionData != null)
            {
                _hitReactionData = hitReactionData;
            }
            if (deathData != null)
            {
                _deathData = deathData;
            }
            _locomotionInitialized = false;
            if (_animancer != null)
            {
                EnsureRootMotionRelay();
                InitializeLocomotion();
            }
        }

        #endregion

        #region TopState Control

        /// <summary>
        /// 進入能力接管狀態 — Locomotion 暫停,由外部腳本控制角色。
        /// 預期呼叫者:PlayerAbilityBridge(第二步加入)、劇情控制器、Cutscene、測試腳本。
        /// Dead 狀態下呼叫會被忽略;重複呼叫會被忽略。
        /// </summary>
        public void EnterAbilityState()
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            if (_topState == TopState.Ability)
            {
                return;
            }
            SetTopState(TopState.Ability);
            ResetLocomotionToIdle();
        }

        /// <summary>
        /// 離開能力接管狀態 — 還給 Locomotion。
        /// 下一幀 Update 會依當前輸入自動轉入 Idle / Walk / Run。
        /// 預期呼叫者:PlayerAbilityBridge、劇情控制器、Cutscene、測試腳本。
        /// </summary>
        public void ExitAbilityState()
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            if (_topState != TopState.Ability)
            {
                return;
            }
            SetTopState(TopState.Locomotion);
            ResumeLocomotionToIdle();
        }

        /// <summary>
        /// 進入受擊硬直狀態 — 第一步僅切換 TopState,實際硬直計時 / 取消邏輯留待第三步實作。
        /// 預期呼叫者:受擊系統 / GASDamageReceiver(第三步接線)。
        /// </summary>
        public void EnterHitStun()
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            if (_topState == TopState.HitStun)
            {
                return;
            }
            SetTopState(TopState.HitStun);
            ResetLocomotionToIdle();
        }

        /// <summary>
        /// 離開受擊硬直狀態 — 還給 Locomotion。
        /// 預期呼叫者:受擊系統(第三步接線)。
        /// </summary>
        public void ExitHitStun()
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            if (_topState != TopState.HitStun)
            {
                return;
            }
            SetTopState(TopState.Locomotion);
            ResumeLocomotionToIdle();
        }

        /// <summary>
        /// 死亡 — 單向狀態,Controller 全面凍結(無輸入、無 Tick、無 Move、無旋轉),凍結遊戲世界並觸發死亡 UI 序列。
        /// 呼叫源:CombatAttributeSet.OnDeath(Start 訂閱)、劇情殺、ContextMenu Debug/Die。
        /// 執行順序經過依賴分析,調整順序前請先確認:
        ///   1. CancelAllActiveAbilities 必須在 AddTag(Dead) 之前,避免 Cancel 路徑反被 Dead Tag 阻擋。
        ///   2. HitStop.StopAllCoroutines 必須在 Time.timeScale=0 之前,否則 HitStop 協程會把 timescale 還原 1。
        ///   3. Animancer.UpdateMode = UnscaledTime 必須在 ForceChangeState(Death) 之前,否則 Death clip 會被 timescale=0 凍在第一幀。
        /// </summary>
        public void Die()
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            CancelAllActiveAbilities();
            ClearDodgeIFrames();
            SetTopState(TopState.Dead);
            if (_asc != null)
            {
                _asc.OwnedTags.AddTag(GameplayTags.State.Dead);
            }
            if (HitStop.Instance != null)
            {
                HitStop.Instance.StopAllCoroutines();
            }
            TimeScaleUtility.RestoreTimeScale();
            if (_animancer != null)
            {
                _animancer.UpdateMode = AnimatorUpdateMode.UnscaledTime;
            }
            if (_context != null && _stateMachine != null)
            {
                _context.IsAirborne = false;
                _context.PendingJumpImpulse = 0f;
                _context.JumpHorizontalVelocity = Vector3.zero;
                _context.UseRootMotionRotation = false;
                _stateMachine.ForceChangeState(_context.Death);
            }
            _verticalVelocity = 0f;
            _jumpBufferTimer = 0f;
            _dodgeBufferTimer = 0f;
            _timeSinceGrounded = 0f;
            if (SystemInputReader.Instance != null)
            {
                SystemInputReader.Instance.DisablePlayerInput();
                SystemInputReader.Instance.ResetTriggeredFlags();
            }
            Time.timeScale = 0f;
            if (DeathUIManager.Instance != null)
            {
                float uiDelay = _deathData != null ? _deathData.PreUiDelay : -1f;
                DeathUIManager.Instance.TriggerDeathSequence(uiDelay);
            }
        }

        /// <summary>
        /// 取消 ASC 上所有活躍能力 — Die 專用。先快照再逐一 Cancel,避免在 GetActiveAbilities 迭代中修改集合。
        /// </summary>
        private void CancelAllActiveAbilities()
        {
            if (_asc == null)
            {
                return;
            }
            List<GameplayAbilitySpec> snapshot = null;
            foreach (GameplayAbilitySpec spec in _asc.GetAllAbilities())
            {
                if (!spec.IsActive)
                {
                    continue;
                }
                snapshot ??= new List<GameplayAbilitySpec>();
                snapshot.Add(spec);
            }
            if (snapshot == null)
            {
                return;
            }
            foreach (GameplayAbilitySpec spec in snapshot)
            {
                spec.CancelAbility();
            }
        }

        /// <summary>
        /// TopState 集中切換入口 — 所有狀態變動都要經過此函式,
        /// 方便未來在一個地方加 log / breakpoint 追蹤呼叫源頭。
        /// </summary>
        private void SetTopState(TopState next)
        {
            if (_topState == next)
            {
                return;
            }
            TopState prev = _topState;
            _topState = next;
            // 離開 Locomotion 時清掉下落追蹤 + 跳躍標記 — 避免被擊飛/能力推升的高度被算到落地傷害,
            // 也避免回到 Locomotion 時錯誤套用「跳躍路徑」的展開條件
            if (next != TopState.Locomotion)
            {
                _trackingFall = false;
                _airborneFromJump = false;
            }
            TopStateChanged?.Invoke(prev, next);
        }

        /// <summary>
        /// 強制把 Locomotion 狀態機重置到 Idle,並清掉跳躍相關 context 旗標。
        /// 進入 Ability / HitStun / 死亡時使用,避免卡在 FastRunTurn 等中間態。
        /// </summary>
        private void ResetLocomotionToIdle()
        {
            if (_context == null || _stateMachine == null)
            {
                return;
            }
            _context.IsAirborne = false;
            _context.PendingJumpImpulse = 0f;
            _context.JumpHorizontalVelocity = Vector3.zero;
            _context.UseRootMotionRotation = false;
            _stateMachine.ChangeState(_context.Idle);
        }

        /// <summary>
        /// 離開 Ability / HitStun 後呼叫:用 AbilityExitFadeDuration 重新淡入 Idle 動畫。
        /// 採用 ForceChangeState 以強制觸發 Idle.Enter,即使狀態機本身未離開過 Idle
        /// (能力動畫已透過 Animancer 覆蓋 Idle 動畫,需重新 Play 才能播回 Idle 而非凍結在能力最後一幀)。
        /// </summary>
        private void ResumeLocomotionToIdle()
        {
            if (_context == null || _stateMachine == null)
            {
                return;
            }
            _context.IsAirborne = false;
            _context.PendingJumpImpulse = 0f;
            _context.JumpHorizontalVelocity = Vector3.zero;
            _context.UseRootMotionRotation = false;
            _context.IdleEnterFadeOverride = _config.AbilityExitFadeDuration;
            _stateMachine.ForceChangeState(_context.Idle);
        }

#if UNITY_EDITOR
        [ContextMenu("Debug/Toggle SuperArmor")]
        private void Debug_ToggleSuperArmor()
        {
            if (_asc == null)
            {
                Debug.LogWarning("[NewGASPlayerController] ASC 尚未初始化,無法切換 SuperArmor。", this);
                return;
            }
            bool has = _asc.OwnedTags.HasTag(GameplayTags.State.SuperArmor);
            if (has)
            {
                _asc.OwnedTags.RemoveTag(GameplayTags.State.SuperArmor);
                Debug.Log("[NewGASPlayerController] SuperArmor Tag → 已移除", this);
            }
            else
            {
                _asc.OwnedTags.AddTag(GameplayTags.State.SuperArmor);
                Debug.Log("[NewGASPlayerController] SuperArmor Tag → 已添加(Flinch 免疫,仍會扣血、累計 Poise、Poise 擊破時仍 Stagger)", this);
            }
        }

        [ContextMenu("Debug/Enter Ability State")]
        private void Debug_EnterAbilityState() => EnterAbilityState();
        [ContextMenu("Debug/Exit Ability State")]
        private void Debug_ExitAbilityState() => ExitAbilityState();
        [ContextMenu("Debug/Enter HitStun")]
        private void Debug_EnterHitStun() => EnterHitStun();
        [ContextMenu("Debug/Exit HitStun")]
        private void Debug_ExitHitStun() => ExitHitStun();
        [ContextMenu("Debug/Die")]
        private void Debug_Die() => Die();

        [ContextMenu("Debug/Knockback/Toggle As Knockback")]
        private void Debug_ToggleHitAsKnockback()
        {
            _debugHitAsKnockback = !_debugHitAsKnockback;
            Debug.Log($"[NewGASPlayerController] DebugHit 模式 → {(_debugHitAsKnockback ? $"Knockback {_debugKnockbackDistance:F1}m" : "Stagger(小推)")}", this);
        }

        [ContextMenu("Debug/Knockback/Apply AddForce Forward 8 m·s")]
        private void Debug_AddForceForward()
        {
            Vector3 impulse = transform.forward * 8f;
            AddForce(impulse);
            Debug.Log($"[NewGASPlayerController] AddForce(forward * 8) → _externalVelocity = {_externalVelocity}", this);
        }

        [ContextMenu("Debug/Knockback/Apply AddKnockback Forward 5m")]
        private void Debug_AddKnockbackForward()
        {
            AddKnockback(transform.forward, 5f);
            Debug.Log($"[NewGASPlayerController] AddKnockback(forward, 5m) → _externalVelocity = {_externalVelocity}", this);
        }

        [ContextMenu("Debug/Glider/Force Deploy")]
        private void Debug_ForceDeployGlider()
        {
            if (_context == null || _stateMachine == null)
            {
                Debug.LogWarning("[NewGASPlayerController] Locomotion 尚未初始化,無法展開滑翔翼。", this);
                return;
            }
            _stateMachine.ChangeState(_context.Glider);
            Debug.Log("[NewGASPlayerController] 強制展開滑翔翼(略過防呆檢查)", this);
        }
#endif

        #endregion

        #region External Launch / Vertical Control

        /// <summary>當前垂直速度(公尺/秒,正為上、負為下)— 供外部系統查詢(氣流區域、UI、除錯)。</summary>
        public float VerticalVelocity => _verticalVelocity;

        /// <summary>是否在滯空中 — 由 JumpState 生命週期管理,外部 LaunchUpward / AddVerticalVelocity 也會設為 true。</summary>
        public bool IsAirborne => _context != null && _context.IsAirborne;

        /// <summary>
        /// 向上彈射 — 覆寫當前垂直速度並強制進入 Airborne 狀態。
        /// 適用:爆炸浮空、能力升空、彈簧板、一次性風柱。
        /// Locomotion 期間會強制切入 JumpState 播完整 JumpStart → Loop → End 動畫序列;
        /// Ability 期間會先取消所有活躍能力(由 OnAbilityEnded → ExitAbilityState 切回 Locomotion),再同步完成彈射;
        /// HitStun 期間不切 Locomotion 狀態機,僅覆寫速度與 Airborne 旗標,由 HitState / KnockbackState 自行管動畫;
        /// Dead 狀態或 upwardVelocity <= 0 會被忽略。
        /// 註:若希望 HitStun 中被彈射打斷,呼叫方應先自行 ExitHitStun。
        /// </summary>
        public void LaunchUpward(float upwardVelocity)
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            if (upwardVelocity <= 0f)
            {
                return;
            }
            if (_context == null || _stateMachine == null)
            {
                return;
            }
            if (_topState == TopState.Ability)
            {
                CancelActiveAbilitiesForLaunch();
            }
            _context.IsAirborne = true;
            if (_topState == TopState.Locomotion)
            {
                _stateMachine.ForceChangeState(_context.Jump);
            }
            // 覆蓋 JumpState.Enter 寫入的 Config.JumpInitialUpVelocity,改用呼叫方指定值。
            // OnRootMotionUpdate 下一幀消費 PendingJumpImpulse;_verticalVelocity 亦先行同步避免 HitStun 下貼地邏輯搶寫。
            _context.PendingJumpImpulse = upwardVelocity;
            _verticalVelocity = upwardVelocity;
            // 外力彈射視為「全新的空中狀態」 — 清掉跳躍路徑旗標、累積落差、滯空計時器,
            // 讓彈跳板/氣流/能力浮空之後的滑翔翼展開走「墜落路徑」並重新等指定秒數
            _airborneFromJump = false;
            _maxAirborneY = transform.position.y;
            _timeSinceGrounded = 0f;
        }

        /// <summary>
        /// 施加垂直速度增量(公尺/秒),正向上、負向下。
        /// 適用:氣流、磁場等持續區域,呼叫方每幀以 `acceleration × Time.deltaTime` 補推。
        /// 累加後淨速度轉正時,Locomotion 期間會切入 JumpState 播動畫(ChangeState,非 Force,避免每幀重啟 Phase.Start);
        /// Ability / HitStun 期間僅累加速度與設 Airborne 旗標,不動狀態機。
        /// 呼叫方停止呼叫後,_verticalVelocity 自然由重力衰減;Dead 狀態呼叫會被忽略。
        /// </summary>
        public void AddVerticalVelocity(float delta)
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            if (_context == null)
            {
                return;
            }
            _verticalVelocity += delta;
            if (_verticalVelocity <= 0f)
            {
                return;
            }
            _context.IsAirborne = true;
            if (_topState != TopState.Locomotion || _stateMachine == null)
            {
                return;
            }
            if (_stateMachine.Current == _context.Jump)
            {
                return;
            }
            _stateMachine.ChangeState(_context.Jump);
            // 蓋過 JumpState.Enter 寫入的 Config.JumpInitialUpVelocity,改用外部累積值,避免氣流剛推起時被跳躍初速度覆蓋。
            _context.PendingJumpImpulse = _verticalVelocity;
        }

        /// <summary>
        /// 以目標高度回推起跳初速度:v = √(2g·h)。
        /// 外部能力或道具以「想要跳多高(公尺)」為設計單位時,換算後傳入 LaunchUpward。
        /// </summary>
        public float CalculateJumpVelocityForHeight(float targetHeight)
        {
            if (targetHeight <= 0f)
            {
                return 0f;
            }
            float gravity = _config != null ? _config.Gravity : 20f;
            return Mathf.Sqrt(2f * gravity * targetHeight);
        }

        /// <summary>
        /// 取消所有活躍能力以讓位給外部彈射 — LaunchUpward 於 Ability 期間呼叫。
        /// 相較於 CancelActiveAttackAbilities(受擊時只取消 Attack 類),此處不限 Tag,任何能力都會被彈射中斷。
        /// OnAbilityEnded → ExitAbilityState 會把 TopState 切回 Locomotion。
        /// </summary>
        private void CancelActiveAbilitiesForLaunch()
        {
            if (_asc == null)
            {
                return;
            }
            List<GameplayAbilitySpec> active = null;
            foreach (GameplayAbilitySpec spec in _asc.GetAllAbilities())
            {
                if (!spec.IsActive)
                {
                    continue;
                }
                active ??= new List<GameplayAbilitySpec>();
                active.Add(spec);
            }
            if (active == null)
            {
                return;
            }
            foreach (GameplayAbilitySpec spec in active)
            {
                spec.CancelAbility();
            }
        }

        #endregion

        #region Lock-On

        /// <summary>
        /// 切換鎖定狀態 — 轉發給 LockOnController.ToggleBestLock();保留此 API 供 UI / 除錯 / 外部腳本呼叫。
        /// 鎖定輸入(按鍵/搖桿方向切換)已完全由 LockOnInputHandler 獨佔處理,Controller 不再監聽輸入。
        /// </summary>
        public void ToggleLockOn()
        {
            if (_lockOn == null)
            {
                return;
            }
            _lockOn.ToggleBestLock();
        }

        /// <summary>
        /// 觸發第三人稱攝影機垂直軸回中 — 供能力(翻滾、大招、閃避結束等)呼叫。
        /// 未指派 _thirdPersonCam 或元件缺少 CinemachineOrbitalFollow 時直接 return。
        /// </summary>
        public void RecenterThirdPersonVerticalOnce()
        {
            if (_thirdPersonCam == null)
            {
                return;
            }
            CinemachineOrbitalFollow orbital = _thirdPersonCam.GetComponent<CinemachineOrbitalFollow>();
            if (orbital == null)
            {
                return;
            }
            var v = orbital.VerticalAxis;
            v.Recentering.Enabled = true;
            v.Recentering.Wait = _verticalRecenteringWait;
            v.Recentering.Time = _verticalRecenteringTime;
            orbital.VerticalAxis = v;
            StopCoroutine(nameof(CoDisableVertRecentering));
            StartCoroutine(CoDisableVertRecentering(orbital,
                _verticalRecenteringWait + _verticalRecenteringTime + 0.05f));
        }

        private IEnumerator CoDisableVertRecentering(CinemachineOrbitalFollow orbital, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            var v = orbital.VerticalAxis;
            v.Recentering.Enabled = false;
            orbital.VerticalAxis = v;
        }

        /// <summary>
        /// 觸發第三人稱攝影機水平 + 垂直軸同時回中 — 供 Parry 等需要鏡頭整個拉回玩家身後預設角度的場合呼叫。
        /// 共用 _verticalRecenteringWait/Time 的設定值。
        /// </summary>
        public void RecenterThirdPersonOnce()
        {
            if (_thirdPersonCam == null)
            {
                return;
            }
            CinemachineOrbitalFollow orbital = _thirdPersonCam.GetComponent<CinemachineOrbitalFollow>();
            if (orbital == null)
            {
                return;
            }
            float delay = _verticalRecenteringWait + _verticalRecenteringTime + 0.05f;
            var v = orbital.VerticalAxis;
            v.Recentering.Enabled = true;
            v.Recentering.Wait = _verticalRecenteringWait;
            v.Recentering.Time = _verticalRecenteringTime;
            orbital.VerticalAxis = v;
            var h = orbital.HorizontalAxis;
            h.Recentering.Enabled = true;
            h.Recentering.Wait = _verticalRecenteringWait;
            h.Recentering.Time = _verticalRecenteringTime;
            orbital.HorizontalAxis = h;
            StopCoroutine(nameof(CoDisableVertRecentering));
            StopCoroutine(nameof(CoDisableHorzRecentering));
            StartCoroutine(CoDisableVertRecentering(orbital, delay));
            StartCoroutine(CoDisableHorzRecentering(orbital, delay));
        }

        private IEnumerator CoDisableHorzRecentering(CinemachineOrbitalFollow orbital, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            var h = orbital.HorizontalAxis;
            h.Recentering.Enabled = false;
            orbital.HorizontalAxis = h;
        }

#if UNITY_EDITOR
        [ContextMenu("Debug/Toggle Lock-On")]
        private void Debug_ToggleLockOn() => ToggleLockOn();
#endif

        #endregion

        #region Ability Event Bridge

        /// <summary>
        /// 能力啟動時：切換 TopState 為 Ability。
        /// Jump 由 LocomotionStateMachine 內部管理,不經此路徑。
        /// </summary>
        private void OnAbilityActivated(GameplayAbilitySpec spec)
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            // Jump 由 LocomotionStateMachine 內部管理 — 不切換 TopState
            if (spec.AbilityDef != null &&
                spec.AbilityDef.AbilityTag.MatchesTagHierarchy(GameplayTags.Ability.Movement.Jump))
            {
                return;
            }
            EnterAbilityState();
        }

        /// <summary>
        /// 能力結束時：若沒有其他活躍能力,回到 Locomotion。
        /// </summary>
        private void OnAbilityEnded(GameplayAbilitySpec spec, bool wasCancelled)
        {
            if (_topState == TopState.Dead)
            {
                return;
            }
            if (_topState != TopState.Ability)
            {
                return;
            }
            // 檢查是否還有其他活躍能力
            foreach (var otherSpec in _asc.GetAllAbilities())
            {
                if (otherSpec != spec && otherSpec.IsActive)
                {
                    return;
                }
            }
            ExitAbilityState();
        }

        private void OnDestroy()
        {
            if (_asc != null)
            {
                _asc.OnAbilityActivated -= OnAbilityActivated;
                _asc.OnAbilityEnded -= OnAbilityEnded;
                CombatAttributeSet combatSet = _asc.GetAttributeSet<CombatAttributeSet>();
                if (combatSet != null)
                {
                    combatSet.OnDeath -= Die;
                }
            }
        }

        #endregion

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            if (_drawDebugArrows && _context != null)
            {
                Vector3 origin = transform.position + Vector3.up * 0.1f;
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(origin, transform.forward * _debugArrowLength);
                Gizmos.color = Color.red;
                Gizmos.DrawRay(origin, _context.DesiredWorldDirection * _debugArrowLength);
            }
            if (_drawExternalVelocityGizmo && _externalVelocity.sqrMagnitude > 0.01f)
            {
                // 長度 = 速度大小(m/s)× 0.2,約略呈現 1 個 τ 時間內的漸近距離 — 直覺可讀
                Vector3 origin = transform.position + Vector3.up * 1.0f;
                Gizmos.color = new Color(1f, 0.55f, 0f);
                Gizmos.DrawRay(origin, _externalVelocity * 0.2f);
                Gizmos.DrawSphere(origin + _externalVelocity * 0.2f, 0.05f);
            }
            if (_drawGroundSensorGizmos && _groundSensor != null)
            {
                _groundSensor.DrawGizmos();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private GUIStyle _debugHudStyle;

        private void OnGUI()
        {
            if (!_showStaminaDebugHud)
            {
                return;
            }
            if (_asc == null)
            {
                return;
            }
            CombatAttributeSet set = _asc.GetAttributeSet<CombatAttributeSet>();
            if (set == null)
            {
                return;
            }
            if (_debugHudStyle == null)
            {
                _debugHudStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                };
            }
            bool depleted = _context != null && _context.IsSprintStaminaDepleted;
            _debugHudStyle.normal.textColor = depleted ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.4f);
            float currentStamina = set.Stamina.CurrentValue;
            float maxStamina = set.MaxStamina.CurrentValue;
            float sprintThreshold = _config != null ? _config.SprintStaminaThreshold : 0f;
            float dodgeCost = _config != null ? _config.DodgeStaminaCost : 0f;
            bool canSprint = _context != null && _context.CanStartSprint;
            bool canDodgeNow = CanDodge;
            ILocomotionState currentState = _stateMachine != null ? _stateMachine.Current : null;
            string stateName = currentState != null ? currentState.GetType().Name : "<null>";
            GUI.Label(new Rect(10, 10, 540, 24),
                $"Stamina: {currentStamina:F1} / {maxStamina:F0}   (Sprint≥{sprintThreshold:F0}, Dodge={dodgeCost:F0})", _debugHudStyle);
            GUI.Label(new Rect(10, 34, 540, 24),
                $"Depleted: {depleted}   CanStartSprint: {canSprint}   CanDodge: {canDodgeNow}", _debugHudStyle);
            GUI.Label(new Rect(10, 58, 540, 24),
                $"TopState: {_topState}   Locomotion: {stateName}", _debugHudStyle);
        }
#endif
    }
}
