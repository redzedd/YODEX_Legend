using UnityEngine;

namespace Player.Locomotion
{
    /// <summary>
    /// 移動系統可調參數集合。透過 ScriptableObject 解耦資料與邏輯，方便設計師在 Inspector 調整手感。
    /// </summary>
    [CreateAssetMenu(menuName = "Player/Locomotion/Locomotion Config", fileName = "LocomotionConfig")]
    public sealed class LocomotionConfig : ScriptableObject
    {
        [Header("輸入閾值")]
        [SerializeField, Range(0.05f, 0.95f), Tooltip("搖桿 magnitude 超過此值由走路升檔為跑步")]
        private float _walkMagnitudeThreshold = 0.5f;
        [SerializeField, Range(0.05f, 0.95f), Tooltip("跑步降檔為走路的 magnitude 閾值，需小於升檔閾值以形成遲滯")]
        private float _runToWalkMagnitudeThreshold = 0.4f;
        [SerializeField, Range(0.01f, 0.5f), Tooltip("搖桿 magnitude 低於此值視為無輸入（死區）")]
        private float _idleDeadzone = 0.1f;
        [SerializeField, Tooltip("無輸入必須持續超過此秒數才觸發 End 動畫，避免搖桿瞬間掃過中心點")]
        private float _inputReleaseDebounce = 0.08f;
        [SerializeField, Tooltip("跑步中 magnitude 持續低於降檔閾值多久才真正降為走路")]
        private float _runDownshiftHoldTime = 0.12f;
        [SerializeField, Tooltip("Idle 偵測到輸入後等待此秒數讓搖桿位置穩定，用期間內的 peak magnitude 決定切 Walk/Run。Peak 若已超過升檔閾值會立刻決定，無須等滿此時間")]
        private float _idleInputSettleTime = 0.05f;

        [Header("旋轉速率（度/秒）")]
        [SerializeField, Tooltip("待機時靈敏轉向")]
        private float _idleRotationSpeed = 1080f;
        [SerializeField, Tooltip("走路狀態轉向速率")]
        private float _walkRotationSpeed = 720f;
        [SerializeField, Tooltip("跑步狀態轉向速率")]
        private float _runRotationSpeed = 480f;
        [SerializeField, Tooltip("快跑狀態轉向速率，最慢以模擬大迴轉半徑")]
        private float _fastRunRotationSpeed = 240f;

        [Header("快跑轉身")]
        [SerializeField, Range(60f, 180f), Tooltip("快跑中當輸入方向與角色朝向夾角超過此值時觸發 FastRunTurn")]
        private float _fastRunTurnAngleThreshold = 135f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Turn 觸發所需的最小搖桿 magnitude。高於此值才算有意圖的反向推桿，濾掉搖桿回彈")]
        private float _turnTriggerMinMagnitude = 0.7f;
        [SerializeField, Tooltip("Turn 觸發條件（夾角 + magnitude）必須持續此秒數才真正切入 FastRunTurn")]
        private float _turnTriggerHoldTime = 0.05f;
        [SerializeField, Tooltip("Turn 完成後轉場至 FastRun/Run 的短淡入時間；Turn 已完整旋轉完畢，此 fade 僅用於姿勢平滑")]
        private float _postTurnFadeDuration = 0.05f;
        [SerializeField, Tooltip("快跑 Lean 混合參數 SmoothDamp 阻尼時間")]
        private float _leanBlendSmoothTime = 0.15f;
        [SerializeField, Tooltip("快跑 Lean 對應角度上限，超過此角度視為 ±1")]
        private float _leanMaxAngle = 45f;

        [Header("動畫交接時間")]
        [SerializeField, Tooltip("Idle 進入 Start 動畫的淡入時間；過長會造成 crossfade 稀釋 root motion 形成頓挫")]
        private float _startAnimFadeDuration = 0.05f;
        [SerializeField, Tooltip("Start 動畫銜接到 Loop 動畫的 crossfade 時間（Walk/Run/FastRun 的 Start→Loop 共用）")]
        private float _startToLoopFadeDuration = 0.15f;
        [SerializeField, Tooltip("Walk Loop 與 Run Loop 之間交接時間（僅用於相同層級的 Loop 切換，不影響 Start→Loop）")]
        private float _walkToRunFadeDuration = 0.15f;
        [SerializeField, Tooltip("由 Walk/Run/FastRunStop 進入 FastRun Loop 的交接時間")]
        private float _fastRunFadeDuration = 0.12f;
        [SerializeField, Tooltip("End 動畫淡入時間")]
        private float _endAnimFadeDuration = 0.1f;
        [SerializeField, Tooltip("FastRunTurn 淡入時間")]
        private float _fastRunTurnFadeDuration = 0.05f;
        [SerializeField, Tooltip("離開 Ability / HitStun 回到 Locomotion 時，Idle 動畫的淡入時間。值較大可避免角色從能力最後一幀瞬切回 Idle 的突兀感")]
        private float _abilityExitFadeDuration = 0.15f;

        [Header("位移平滑")]
        [SerializeField, Tooltip("水平位移延續時間常數（秒）；動畫交接時若 root motion 被稀釋，會以此時間常數讓上一幀位移指數衰減過渡。設為 0 可關閉")]
        private float _horizontalVelocityContinuationTau = 0.1f;

        [Header("重力")]
        [SerializeField, Tooltip("簡易重力加速度，僅讓 CharacterController 貼地")]
        private float _gravity = 20f;

        [Header("地面偵測")]
        [SerializeField, Tooltip("地面偵測用 LayerMask — 僅勾選地形/可站立物件,避免把敵人、玩家自身或 Trigger 誤判為地面。預設為 Default(Layer 0)。")]
        private LayerMask _groundMask = 1;
        [SerializeField, Tooltip("CharacterController.isGrounded == false 時,從膠囊底往下 SphereCast 的最大距離(公尺)。" +
                                   "作為 isGrounded 的補強,避免膠囊邊緣懸空、或垂直速度略為正值時誤判離地造成 Coyote Time 提前啟動。" +
                                   "起算點已經在膠囊底,不需太長,0.15~0.25 通常足夠。")]
        private float _groundProbeDistance = 0.2f;
        [SerializeField, Range(0.5f, 1f), Tooltip("SphereCast 半徑相對於 CharacterController.radius 的比例。" +
                                                    "略小於 1 可避免側向斜牆被誤判為地面;常用 0.9~0.95。")]
        private float _groundSphereRadiusScale = 0.95f;

        [Header("跳躍")]
        [SerializeField, Tooltip("起跳初速度（公尺/秒）。與 Gravity 共同決定跳躍高度：peak ≈ v²/2g")]
        private float _jumpInitialUpVelocity = 7f;
        [SerializeField, Tooltip("滯空時的轉向速率（度/秒）。中等值保留輕量空中轉向手感")]
        private float _jumpRotationSpeed = 360f;
        [SerializeField, Range(0f, 1f), Tooltip("空中控制權重：0=無空中控制、1=完全控制、推薦 0.4~0.6 接近尼爾/絕區零")]
        private float _airControlWeight = 0.6f;
        [SerializeField, Tooltip("空中控制響應速率（指數平滑常數，數值越大收斂越快，推薦 6~10）")]
        private float _airControlResponsiveness = 8f;
        [SerializeField, Tooltip("從靜止起跳時可達到的空中基礎水平速度（公尺/秒）。若起跳時已有高於此值的速度則保留原速度")]
        private float _airMoveBaseSpeed = 3f;
        [SerializeField, Tooltip("由地面狀態進入 JumpStart 的淡入時間")]
        private float _jumpStartFadeDuration = 0.05f;
        [SerializeField, Tooltip("JumpStart 淡入 JumpLoop 的交接時間")]
        private float _jumpLoopFadeDuration = 0.1f;
        [SerializeField, Tooltip("落地切入 JumpEnd 的淡入時間")]
        private float _jumpEndFadeDuration = 0.08f;
        [SerializeField, Tooltip("落地後鎖定操作的時間（秒）。期間無法響應移動輸入與轉向，JumpEnd 動畫優先播放。設為 0 表示落地即可操作")]
        private float _jumpLandingLockDuration = 0.25f;
        [SerializeField, Tooltip("落地解鎖後切 Walk/Run/FastRun 的淡入時間")]
        private float _jumpLandingToMoveFadeDuration = 0.12f;
        [SerializeField, Tooltip("起跳後至少滯空此秒數才允許偵測落地，避免起跳當幀誤判")]
        private float _minAirborneTimeBeforeLand = 0.08f;

        [Header("跳躍手感輔助")]
        [SerializeField, Tooltip("Coyote Time：離開地面後仍允許跳躍的寬限時間（秒）")]
        private float _coyoteTime = 0.1f;
        [SerializeField, Tooltip("Jump Buffer：落地前預先按跳躍鍵的輸入緩衝時間（秒）")]
        private float _jumpBufferTime = 0.12f;

        [Header("滑翔翼")]
        [SerializeField, Tooltip("一般墜落(非跳躍)展開滑翔翼所需的最短滯空時間(秒)。\n" +
                                   "適用情境:走下懸崖、被推下平台等「無跳躍動作」的浮空。\n" +
                                   "若浮空是「玩家主動跳躍」造成,改採『必須掉到比起跳點低』的判定,不吃此秒數。\n" +
                                   "建議 0.25~0.5。配合 GliderMinHeightAboveGround 雙重門檻。")]
        private float _gliderMinAirborneTime = 0.3f;
        [SerializeField, Tooltip("展開滑翔翼所需的最低離地高度(公尺)。\n" +
                                   "防呆 2:站平地小跳不會展開,避免奇怪的「平地展開又馬上落地」觀感。\n" +
                                   "建議 1.0~2.0。從角色腳底往下 Raycast,距離不足直接拒絕。")]
        private float _gliderMinHeightAboveGround = 1.5f;
        [SerializeField, Tooltip("滑翔翼最大下降速度(公尺/秒,正值)。垂直速度會被 clamp 到 ≥ -此值,\n" +
                                   "上升動能會被重力自然衰減,落到指定速度後維持等速下降。\n" +
                                   "建議 1.5~3。設太低=飄太久;設太高=幾乎沒效果。")]
        private float _gliderDescentSpeed = 2f;
        [SerializeField, Tooltip("滑翔翼水平移動最大速度(公尺/秒)。\n" +
                                   "由攝影機相對輸入 × 此值決定,比一般跳躍空中控制更敏銳。建議 3~6。")]
        private float _gliderHorizontalSpeed = 4f;
        [SerializeField, Tooltip("滑翔翼水平輸入響應速率(指數平滑常數)。\n" +
                                   "數值越大轉向越靈活但容易抖,推薦 6~10。")]
        private float _gliderHorizontalResponsiveness = 8f;
        [SerializeField, Tooltip("滑翔翼朝向旋轉速率(度/秒)。\n" +
                                   "角色面朝會逐步轉向移動方向。建議 240~480。")]
        private float _gliderRotationSpeed = 360f;
        [SerializeField, Tooltip("展開滑翔翼動畫的淡入時間(秒)。建議 0.1~0.2。")]
        private float _gliderEnterFadeDuration = 0.15f;
        [SerializeField, Tooltip("收起滑翔翼動畫的淡出時間(秒)。建議 0.08~0.15。")]
        private float _gliderExitFadeDuration = 0.1f;
        [SerializeField, Tooltip("滑翔翼每秒耐力消耗量。\n" +
                                   "耐力扣到 0 時自動收起滑翔翼,玩家直接進入 JumpLoop 自由落體。\n" +
                                   "設為 0 表示不耗耐力(任意時間滑翔)。建議 5~15。")]
        private float _gliderStaminaCostPerSec = 10f;

        [Header("下落傷害")]
        [SerializeField, Tooltip("開始造成下落傷害的最低落差(公尺)。\n" +
                                   "落差 = 起跳/離地以來的最高點 Y - 落地 Y。低於此值不扣血。\n" +
                                   "展開滑翔翼會把基準點重置為當下高度,等於清除目前累積的落差。\n" +
                                   "建議 3~6。設為極大值可關閉下落傷害。")]
        private float _fallDamageThreshold = 5f;
        [SerializeField, Tooltip("達到最高下落傷害所需的落差(公尺)。\n" +
                                   "落差超過此值仍以此值的傷害量計算(傷害飽和)。建議 15~30。")]
        private float _fallDamageMaxDistance = 20f;
        [SerializeField, Tooltip("落差達到 FallDamageMaxDistance 時造成的傷害值(HP)。\n" +
                                   "落差介於 Threshold 與 MaxDistance 間的傷害用線性內插計算。\n" +
                                   "建議設為玩家最大 HP 的 80~100%,讓極端高度墜落致死。")]
        private float _fallDamageAtMaxDistance = 100f;

        [Header("下落動畫觸發")]
        [SerializeField, Tooltip("離地必須持續超過此秒數才會切入 JumpLoop 下落動畫。\n" +
                                   "建議 0.12~0.2 秒。設太小:下樓梯、邊緣抖動可能誤觸；\n" +
                                   "設太大:從矮台跳下時不會播下落動畫,直接落地。\n" +
                                   "與 GroundSensor 的 SphereCast 0.2m 防呆共同生效。")]
        private float _fallTriggerDelay = 0.15f;
        [SerializeField, Tooltip("垂直速度必須低於此值(負數,公尺/秒)才會切入 JumpLoop 下落動畫。\n" +
                                   "建議 -2 ~ -3。配合 _fallTriggerDelay 雙重門檻,確保是真的在向下加速,\n" +
                                   "而非貼地時的抖動 (-1f 重力鎖定值)。")]
        private float _fallTriggerVerticalVelocity = -3f;

        [Header("閃避")]
        [SerializeField, Tooltip("Dodge Buffer：未就緒時預先按閃避鍵的輸入緩衝時間（秒）")]
        private float _dodgeBufferTime = 0.1f;
        [SerializeField, Tooltip("進入閃避動畫的 fade 時間")]
        private float _dodgeEnterFadeDuration = 0.05f;
        [SerializeField, Tooltip("閃避鎖定操作的時間（秒）— 閃避開始後,期間必須播完此段時間才允許輸入轉場。設為 0 表示按下輸入即可中斷")]
        private float _dodgeLockDuration = 0.3f;
        [SerializeField, Tooltip("閃避後切其他 Locomotion 動作（Idle / Walk / Run / FastRun）的 fade 時間")]
        private float _dodgeToMoveFadeDuration = 0.12f;
        [SerializeField, Tooltip("連續閃避時新一輪動畫從第幾秒開始播放（Time）。\n" +
                                   "Dodge clip 結構為 [Idle > Dodge > Idle] 時,設為約 Idle 預備段的結束時間," +
                                   "可讓連擊跳過前段 Idle、直接進入 Dodge 主動作,避免抽動感。\n" +
                                   "設為 0 表示從頭播")]
        private float _dodgeReentryStartTime = 0.1f;
        [SerializeField, Tooltip("連續閃避時進入新一輪 Dodge 動畫的 fade 時間。\n" +
                                   "首次 Dodge 使用 DodgeEnterFadeDuration;連擊時可改用此值," +
                                   "通常設比首次短一些以讓連擊更俐落")]
        private float _dodgeReentryFadeDuration = 0.08f;
        [SerializeField, Tooltip("閃避單次消耗的耐力量 — 瞬發一次性扣除。\n" +
                                   "耐力不足時 CanDodge 會回 false、按鍵 buffer 會保留直到耐力回升或自然歸零。\n" +
                                   "設為 0 表示不消耗耐力。")]
        private float _dodgeStaminaCost = 15f;
        [SerializeField, Tooltip("閃避無敵開始時間（相對於閃避開始,秒）。\n" +
                                   "設為 0 表示一按下閃避就無敵;想做「起步有破綻」可填 0.05~0.1。")]
        private float _dodgeInvincibilityStartTime = 0f;
        [SerializeField, Tooltip("閃避無敵持續時間（秒）。\n" +
                                   "無敵期間免疫近戰傷害,且敵人投射物會直接穿透。\n" +
                                   "想讓整段閃避都無敵就填接近閃避動畫長度的值（建議 0.3~0.5）;設為 0 表示不啟用無敵。")]
        private float _dodgeInvincibilityDuration = 0.4f;
        [SerializeField, Tooltip("閃避無敵期間被敵人攻擊到時,觸發的慢動作時間縮放值。\n" +
                                   "0.3 = 慢到三成速;1 = 不變慢。建議 0.2~0.4。")]
        private float _dodgeIFrameHitSlowScale = 0.3f;
        [SerializeField, Tooltip("慢動作維持時間（秒,以真實時間計,不受縮放影響）。設為 0 表示關閉此回饋。建議 0.1~0.25。")]
        private float _dodgeIFrameHitSlowHoldDuration = 0.15f;
        [SerializeField, Tooltip("慢動作平滑回復到正常速度所需時間（秒,真實時間）。建議 0.15~0.3。")]
        private float _dodgeIFrameHitSlowRecoverDuration = 0.25f;

        [Header("衝刺耐力")]
        [SerializeField, Tooltip("衝刺(FastRun Loop)每秒耐力消耗量。僅 Loop 階段扣除,Start / End / Turn 不扣。")]
        private float _sprintStaminaCostPerSec = 10f;
        [SerializeField, Tooltip("可起動衝刺的最低耐力值。\n" +
                                   "衝刺中將耐力扣到 0 時進入耗盡鎖定,必須恢復到此值才能再次衝刺。\n" +
                                   "設為 0 表示任何耐力都能衝,且不走耗盡鎖定機制(耗光當幀即可再起動)。")]
        private float _sprintStaminaThreshold = 20f;

        public float WalkMagnitudeThreshold => _walkMagnitudeThreshold;
        public float RunToWalkMagnitudeThreshold => _runToWalkMagnitudeThreshold;
        public float IdleDeadzone => _idleDeadzone;
        public float InputReleaseDebounce => _inputReleaseDebounce;
        public float RunDownshiftHoldTime => _runDownshiftHoldTime;
        public float IdleInputSettleTime => _idleInputSettleTime;
        public float IdleRotationSpeed => _idleRotationSpeed;
        public float WalkRotationSpeed => _walkRotationSpeed;
        public float RunRotationSpeed => _runRotationSpeed;
        public float FastRunRotationSpeed => _fastRunRotationSpeed;
        public float FastRunTurnAngleThreshold => _fastRunTurnAngleThreshold;
        public float TurnTriggerMinMagnitude => _turnTriggerMinMagnitude;
        public float TurnTriggerHoldTime => _turnTriggerHoldTime;
        public float PostTurnFadeDuration => _postTurnFadeDuration;
        public float LeanBlendSmoothTime => _leanBlendSmoothTime;
        public float LeanMaxAngle => _leanMaxAngle;
        public float StartAnimFadeDuration => _startAnimFadeDuration;
        public float StartToLoopFadeDuration => _startToLoopFadeDuration;
        public float WalkToRunFadeDuration => _walkToRunFadeDuration;
        public float FastRunFadeDuration => _fastRunFadeDuration;
        public float EndAnimFadeDuration => _endAnimFadeDuration;
        public float FastRunTurnFadeDuration => _fastRunTurnFadeDuration;
        public float AbilityExitFadeDuration => _abilityExitFadeDuration;
        public float HorizontalVelocityContinuationTau => _horizontalVelocityContinuationTau;
        public float Gravity => _gravity;
        public LayerMask GroundMask => _groundMask;
        public float GroundProbeDistance => _groundProbeDistance;
        public float GroundSphereRadiusScale => _groundSphereRadiusScale;
        public float JumpInitialUpVelocity => _jumpInitialUpVelocity;
        public float JumpRotationSpeed => _jumpRotationSpeed;
        public float AirControlWeight => _airControlWeight;
        public float AirControlResponsiveness => _airControlResponsiveness;
        public float AirMoveBaseSpeed => _airMoveBaseSpeed;
        public float JumpStartFadeDuration => _jumpStartFadeDuration;
        public float JumpLoopFadeDuration => _jumpLoopFadeDuration;
        public float JumpEndFadeDuration => _jumpEndFadeDuration;
        public float JumpLandingLockDuration => _jumpLandingLockDuration;
        public float JumpLandingToMoveFadeDuration => _jumpLandingToMoveFadeDuration;
        public float MinAirborneTimeBeforeLand => _minAirborneTimeBeforeLand;
        public float CoyoteTime => _coyoteTime;
        public float JumpBufferTime => _jumpBufferTime;
        public float FallTriggerDelay => _fallTriggerDelay;
        public float FallTriggerVerticalVelocity => _fallTriggerVerticalVelocity;
        public float FallDamageThreshold => _fallDamageThreshold;
        public float FallDamageMaxDistance => _fallDamageMaxDistance;
        public float FallDamageAtMaxDistance => _fallDamageAtMaxDistance;
        public float GliderMinAirborneTime => _gliderMinAirborneTime;
        public float GliderMinHeightAboveGround => _gliderMinHeightAboveGround;
        public float GliderDescentSpeed => _gliderDescentSpeed;
        public float GliderHorizontalSpeed => _gliderHorizontalSpeed;
        public float GliderHorizontalResponsiveness => _gliderHorizontalResponsiveness;
        public float GliderRotationSpeed => _gliderRotationSpeed;
        public float GliderEnterFadeDuration => _gliderEnterFadeDuration;
        public float GliderExitFadeDuration => _gliderExitFadeDuration;
        public float GliderStaminaCostPerSec => _gliderStaminaCostPerSec;
        public float DodgeBufferTime => _dodgeBufferTime;
        public float DodgeEnterFadeDuration => _dodgeEnterFadeDuration;
        public float DodgeLockDuration => _dodgeLockDuration;
        public float DodgeToMoveFadeDuration => _dodgeToMoveFadeDuration;
        public float DodgeReentryStartTime => _dodgeReentryStartTime;
        public float DodgeReentryFadeDuration => _dodgeReentryFadeDuration;
        public float DodgeStaminaCost => _dodgeStaminaCost;
        public float DodgeInvincibilityStartTime => _dodgeInvincibilityStartTime;
        public float DodgeInvincibilityDuration => _dodgeInvincibilityDuration;
        public float DodgeIFrameHitSlowScale => _dodgeIFrameHitSlowScale;
        public float DodgeIFrameHitSlowHoldDuration => _dodgeIFrameHitSlowHoldDuration;
        public float DodgeIFrameHitSlowRecoverDuration => _dodgeIFrameHitSlowRecoverDuration;
        public float SprintStaminaCostPerSec => _sprintStaminaCostPerSec;
        public float SprintStaminaThreshold => _sprintStaminaThreshold;
    }
}
