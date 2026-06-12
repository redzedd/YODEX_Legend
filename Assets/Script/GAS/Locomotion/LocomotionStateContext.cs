using UnityEngine;
using Player.Locomotion.States;

namespace Player.Locomotion
{
    /// <summary>
    /// 耐力預算介面 — 將 GAS.CombatAttributeSet 隔離在 Locomotion asmdef 之外。
    /// 由 NewGASPlayerController 層的 adapter 實作並注入 <see cref="LocomotionStateContext.StaminaBudget"/>;
    /// 目前供衝刺持續消耗 / 閃避一次性消耗共用,未來攻擊、跳躍若需扣耐力也可共用此介面。
    /// </summary>
    public interface IStaminaBudget
    {
        float Current { get; }
        bool TryConsume(float amount);
    }

    /// <summary>
    /// 所有狀態共享的執行期資料與服務。由 PlayerLocomotionController 建立並在每幀更新。
    /// </summary>
    public sealed class LocomotionStateContext
    {
        public LocomotionConfig Config { get; }
        public LocomotionAnimationSet AnimationSet { get; }
        public LocomotionAnimatorDriver AnimatorDriver { get; }
        public LocomotionInputReader Input { get; }
        public Transform ActorTransform { get; }
        public LocomotionStateMachine StateMachine { get; set; }

        public IdleState Idle { get; set; }
        public WalkState Walk { get; set; }
        public RunState Run { get; set; }
        public FastRunState FastRun { get; set; }
        public FastRunTurnState FastRunTurn { get; set; }
        public FastRunStopState FastRunStop { get; set; }
        public JumpState Jump { get; set; }
        public GliderState Glider { get; set; }
        public DodgeState Dodge { get; set; }
        public HitState Hit { get; set; }
        public KnockbackState Knockback { get; set; }
        public DeathState Death { get; set; }
        /// <summary>角色受擊資料 — Controller 於初始化時塞入,由 HitState / Walk / Run / FastRun 讀取</summary>
        public HitReactionData HitReactionData { get; set; }
        /// <summary>角色死亡資料 — Controller 於初始化時塞入,由 DeathState 讀取。null 時 DeathState 不播動畫,其他流程照常執行</summary>
        public PlayerDeathData DeathData { get; set; }
        /// <summary>Controller 於 OnHitReceived 時寫入,HitState.Enter 消費並解讀</summary>
        public HitOutcome PendingHitOutcome { get; set; }

        public Vector3 DesiredWorldDirection { get; set; }
        public float InputMagnitude { get; set; }
        public bool RunButtonHeld { get; set; }
        public bool UseRootMotionRotation { get; set; }
        public float CurrentRotationSpeed { get; set; }
        public float NoInputTime { get; set; }
        /// <summary>Turn 方向：1 = 右迴轉、-1 = 左迴轉、0 = 未設定</summary>
        public int TurnDirection { get; set; }

        /// <summary>跳躍活動中旗標：為 true 時 Controller 不再於 isGrounded 強制貼地重置垂直速度</summary>
        public bool IsAirborne { get; set; }
        /// <summary>下落觸發旗標 — Controller 偵測到「非跳躍離地」時設為 true,JumpState.Enter 讀到後跳過 JumpStart 直接進 JumpLoop,不寫 PendingJumpImpulse(保留既有垂直速度由重力驅動)。JumpState.Enter 消費後自動清為 false。</summary>
        public bool EnterFallLoop { get; set; }
        /// <summary>落地分支旗標 — GliderState 偵測到落地時設為 true,JumpState.Enter 讀到後直接進 Phase.End 播 JumpEnd 動畫,跳過 JumpStart / JumpLoop。JumpState.Enter 消費後自動清為 false。</summary>
        public bool EnterJumpEnd { get; set; }
        /// <summary>滑翔翼展開中旗標 — GliderState.Enter/Exit 維護;Controller 於 OnRootMotionUpdate 讀取此旗標,將垂直速度 clamp 至 ≥ -GliderDescentSpeed,模擬空氣阻力下的等速下降。</summary>
        public bool IsGliding { get; set; }
        /// <summary>滑翔翼身上特效 — Controller 於初始化時塞入;GliderState.Enter 呼叫 Play(),Exit 呼叫 Pause()。為 null 時不做事(無 VFX 也能正常滑翔)。</summary>
        public ParticleSystem GliderVFX { get; set; }
        /// <summary>閃避鎖定中旗標：DodgeState.Enter 設為 true，鎖定期結束或 Exit 清為 false。為 true 時禁止再次觸發閃避與跳躍</summary>
        public bool IsDodgeLocked { get; set; }
        /// <summary>JumpState.Enter 寫入的起跳衝量，Controller 於下一次 OnAnimatorMove 消費後歸 0</summary>
        public float PendingJumpImpulse { get; set; }
        /// <summary>跳躍期間以此向量取代動畫 root motion 的水平分量</summary>
        public Vector3 JumpHorizontalVelocity { get; set; }
        /// <summary>由 Controller 每幀寫入，讓狀態讀取最新 CharacterController.isGrounded</summary>
        public bool IsGrounded { get; set; }
        /// <summary>由 Controller 在 OnAnimatorMove 尾端寫入（horizontalDelta / deltaTime），供 JumpState 鎖定起跳水平速度使用</summary>
        public Vector3 LastHorizontalVelocity { get; set; }
        /// <summary>一次性 fade 覆寫 — IdleState.Enter 若偵測到此值會優先採用，使用後自動清空。離開 Ability / HitStun 由 Controller 寫入 Config.AbilityExitFadeDuration。</summary>
        public float? IdleEnterFadeOverride { get; set; }
        /// <summary>武器切換刷新中旗標 — 為 true 時,各 Resumable State 於 Enter 會優先嘗試從 <see cref="ResumeSlot"/> 與 <see cref="ResumeNormalizedTime"/> 接回;失敗時 Walk / Run / FastRun 退回「直接進入 Loop phase」,Idle 退回正常播放。Controller 於 InitializeLocomotion 設定,Start 之後即刻清除。</summary>
        public bool IsRefreshingFromModelSwitch { get; set; }
        /// <summary>武器切換前舊 AnimancerComponent 所播的 slot(由 PrepareForModelSwitch 透過 <see cref="States.IResumableLocomotionState"/> 擷取);InitializeLocomotion 灌入 context,State.Enter 讀取以決定要播新 AnimationSet 的哪個 slot。</summary>
        public LocomotionAnimSlot ResumeSlot { get; set; }
        /// <summary>武器切換前舊 AnimancerState 的 NormalizedTime;與 <see cref="ResumeSlot"/> 搭配使用,讓新模型接播同進度。</summary>
        public float ResumeNormalizedTime { get; set; }
        /// <summary>耐力預算 — Controller 於初始化時塞入 CombatAttributeSet 的 adapter。為 null 時視為無限耐力(不扣、不鎖)。供衝刺 / 閃避共用。</summary>
        public IStaminaBudget StaminaBudget { get; set; }
        /// <summary>衝刺耐力耗盡鎖定旗標 — FastRun 扣光耐力時設為 true,必須等 Stamina 恢復至 <see cref="LocomotionConfig.SprintStaminaThreshold"/> 才會由 Controller 解除。鎖定期間所有升檔 FastRun 的路徑皆被阻擋。</summary>
        public bool IsSprintStaminaDepleted { get; set; }

        public LocomotionStateContext(
            LocomotionConfig config,
            LocomotionAnimationSet animationSet,
            LocomotionAnimatorDriver animatorDriver,
            LocomotionInputReader input,
            Transform actorTransform)
        {
            Config = config;
            AnimationSet = animationSet;
            AnimatorDriver = animatorDriver;
            Input = input;
            ActorTransform = actorTransform;
        }

        public bool HasMoveInput => InputMagnitude > Config.IdleDeadzone;
        public bool IsWalkInput => InputMagnitude > Config.IdleDeadzone && InputMagnitude <= Config.WalkMagnitudeThreshold;
        public bool IsRunInput => InputMagnitude > Config.WalkMagnitudeThreshold;

        /// <summary>
        /// 是否允許由其他 Locomotion 狀態升檔至 FastRun。
        /// 阻擋條件:耗盡鎖定中、或當前 Stamina 未達 <see cref="LocomotionConfig.SprintStaminaThreshold"/>。
        /// 未注入 <see cref="StaminaBudget"/> 時視為無限耐力,永遠允許。
        /// </summary>
        public bool CanStartSprint
        {
            get
            {
                if (IsSprintStaminaDepleted) return false;
                if (StaminaBudget == null) return true;
                return StaminaBudget.Current >= Config.SprintStaminaThreshold;
            }
        }

        /// <summary>
        /// FastRun Loop 每幀呼叫:扣除 <see cref="LocomotionConfig.SprintStaminaCostPerSec"/> × deltaTime 的耐力。
        /// 扣除失敗(耐力不足)時自動設 <see cref="IsSprintStaminaDepleted"/> 為 true 並回傳 false,呼叫端應隨即降檔。
        /// 未注入 <see cref="StaminaBudget"/> 時視為無限耐力,永遠回 true。
        /// </summary>
        public bool TryConsumeSprintStamina(float deltaTime)
        {
            if (StaminaBudget == null) return true;
            float cost = Config.SprintStaminaCostPerSec * deltaTime;
            if (StaminaBudget.TryConsume(cost)) return true;
            IsSprintStaminaDepleted = true;
            return false;
        }

        /// <summary>
        /// 是否有足夠耐力啟動一次閃避 — 當前 Stamina ≥ <see cref="LocomotionConfig.DodgeStaminaCost"/>。
        /// 未注入 <see cref="StaminaBudget"/> 時視為無限耐力,永遠允許;DodgeStaminaCost = 0 也永遠允許。
        /// 供 <c>NewGASPlayerController.CanDodge</c> 組合判定,UI 可透過 CanDodge 反映按鍵可用狀態。
        /// </summary>
        public bool CanAffordDodge
        {
            get
            {
                if (StaminaBudget == null) return true;
                float cost = Config.DodgeStaminaCost;
                if (cost <= 0f) return true;
                return StaminaBudget.Current >= cost;
            }
        }

        /// <summary>
        /// 扣除一次閃避耐力 — 由 Controller 於 Dodge 觸發當幀呼叫一次,失敗時 Dodge 不應觸發。
        /// 未注入 <see cref="StaminaBudget"/> 或 DodgeStaminaCost = 0 時視為免費,永遠回 true。
        /// 注意:不在 <c>DodgeState.Enter</c> 扣除,因武器切換 Resume 會重新觸發 Enter 造成重扣。
        /// </summary>
        public bool TryConsumeDodgeStamina()
        {
            if (StaminaBudget == null) return true;
            float cost = Config.DodgeStaminaCost;
            if (cost <= 0f) return true;
            return StaminaBudget.TryConsume(cost);
        }

        /// <summary>
        /// 滑翔翼每幀呼叫:扣除 <see cref="LocomotionConfig.GliderStaminaCostPerSec"/> × deltaTime 的耐力。
        /// 扣除失敗(耐力不足)時回 false,呼叫端(GliderState)應立即切回 JumpLoop 自由落體。
        /// 未注入 <see cref="StaminaBudget"/> 或 GliderStaminaCostPerSec = 0 時視為免費,永遠回 true。
        /// </summary>
        public bool TryConsumeGliderStamina(float deltaTime)
        {
            if (StaminaBudget == null) return true;
            float cost = Config.GliderStaminaCostPerSec * deltaTime;
            if (cost <= 0f) return true;
            return StaminaBudget.TryConsume(cost);
        }
    }
}
