using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using DG.Tweening;
using GAS.Targeting.Combat;
using GAS.Targeting.LockOnV2;
using EnemyAI;

namespace GAS
{
    /// <summary>
    /// 近戰攻擊能力 - GAS 版本的攻擊系統
    /// </summary>
    [CreateAssetMenu(fileName = "GA_MeleeAttack", menuName = "GAS/Abilities/Melee Attack")]
    public class GA_MeleeAttack : GameplayAbility
    {
        [Header("Attack Data")]
        [Tooltip("初始攻擊數據 (第一擊)")]
        public MeleeAttackData FirstAttackData;

        [Header("References")]
        [Tooltip("敵人圖層")]
        public LayerMask EnemyLayer;

        [Tooltip("障礙物圖層")]
        public LayerMask ObstacleLayer;

        [Header("Settings")]
        [Tooltip("回退時使用的第一擊 (連招中斷後重新開始)")]
        public MeleeAttackData FallbackFirstAttack;

        [Header("Cross-Type Combo")]
        [Tooltip("遠程攻擊能力標籤（用於近戰→遠程跨類型連招）")]
        public GameplayTag CrossTypeAbilityTag;

        public override void ActivateAbility(GameplayAbilitySpec spec)
        {
            // 獲取自定義數據中的攻擊數據，或使用默認的第一擊
            var attackData = spec.CustomData as MeleeAttackData ?? FirstAttackData;
            
            if (attackData == null)
            {
                Debug.LogError("[GA_MeleeAttack] No attack data assigned!");
                spec.EndAbility();
                return;
            }

            // 啟動攻擊協程
            var coroutine = StartCoroutine(spec, AttackRoutine(spec, attackData));
            spec.SetActiveCoroutine(coroutine);
        }

        public override void EndAbility(GameplayAbilitySpec spec, bool wasCancelled)
        {
            // 清理攻擊狀態
            if (spec.CustomData is MeleeAttackRuntimeData runtimeData)
            {
                runtimeData.Cleanup(wasCancelled);
            }

            // 確保移除取消鎖定標籤
            spec.Owner?.OwnedTags.RemoveTag(GameplayTags.State.AttackNonCancellable);

            // [NEW] 如果被取消（如閃避）或被打斷，啟動延遲清除標記
            if (wasCancelled)
            {
                ScheduleClearMarkedTarget(spec);
                
                // 清空輸入緩衝避免殘留輸入觸發連招
                var inputHandler = spec.Owner?.GetComponent<AbilityInputHandler>();
                inputHandler?.ClearBuffer();
            }

            base.EndAbility(spec, wasCancelled);
        }

        /// <summary>
        /// 啟動延遲清除標記（用於所有清除情況）
        /// </summary>
        private void ScheduleClearMarkedTarget(GameplayAbilitySpec spec)
        {
            HitTargetMemory hitMemory = spec.Owner?.GetComponent<HitTargetMemory>();
            if (hitMemory != null)
            {
                hitMemory.ScheduleMarkClear();
                if (spec.Owner.DebugMode)
                {
                    Debug.Log("[GA_MeleeAttack] Scheduled delayed mark clear");
                }
            }
        }

        /// <summary>
        /// 攻擊主協程
        /// </summary>
        private IEnumerator AttackRoutine(GameplayAbilitySpec spec, MeleeAttackData attackData)
        {
            var owner = spec.Owner;
            
            // 從 NewGASPlayerController 獲取正確的 Animancer 引用
            var playerController = owner.GetComponent<NewGASPlayerController>();
            var animancer = playerController?.Animancer;
            
            // 如果 PlayerController 沒有 Animancer，嘗試直接獲取
            if (animancer == null)
            {
                animancer = owner.GetComponentInChildren<AnimancerComponent>();
            }
            
            CombatTargetFinder targetFinder = owner.GetComponent<CombatTargetFinder>();
            HitTargetMemory hitMemory = owner.GetComponent<HitTargetMemory>();
            LockOnController lockOn = owner.GetComponent<LockOnController>();
            var characterController = owner.GetComponent<CharacterController>();

            if (animancer == null)
            {
                Debug.LogError("[GA_MeleeAttack] AnimancerComponent not found!");
                spec.EndAbility();
                yield break;
            }

            // 創建運行時數據
            var runtimeData = new MeleeAttackRuntimeData(owner, attackData, animancer, characterController, targetFinder, hitMemory, lockOn);
            runtimeData.EnemyLayer = EnemyLayer;
            runtimeData.ObstacleLayer = ObstacleLayer;
            spec.CustomData = runtimeData;

            // 自動轉向目標 — 透過 RuntimeData 鎖定,本段 ability 期間不會換目標
            if (attackData.MovementConfig.AutoFaceTarget)
            {
                Transform faceTarget = runtimeData.ResolveLockedAutoFaceTarget();
                if (faceTarget != null)
                {
                    owner.transform.DOLookAt(faceTarget.position, attackData.MovementConfig.AutoFaceDuration, AxisConstraint.Y);
                }
            }

            // 播放動畫
            var animState = animancer.Play(attackData.Clip);
            animState.Time = 0;
            runtimeData.AnimState = animState;

            float animDuration = attackData.Clip.Clip.length;
            AnimationClip primaryClip = attackData.Clip.Clip;
            GameObject animTarget = animancer.gameObject;
            float stateTimer = 0f;
            float prevTimer = 0f;

            // 子幀命中檢測的最大步長（等效 120fps，確保低於 120fps 時啟動子幀取樣）
            const float SUB_STEP_THRESHOLD = 1f / 120f;

            // [NEW] 啟動延遲清除標記的計時器
            bool hasScheduledClear = false;

            // 攻擊開始時禁止取消，到達 AllowCancelTime 後解除
            bool isCancelLocked = attackData.AllowCancelTime > 0f;
            if (isCancelLocked)
            {
                owner.OwnedTags.AddTag(GameplayTags.State.AttackNonCancellable);
            }

            // 主循環
            while (stateTimer < animDuration && spec.IsActive)
            {
                prevTimer = stateTimer;
                stateTimer = animState.Time;

                float frameDelta = stateTimer - prevTimer;

                // 子幀命中檢測：當幀間隔過大時，細分時間步驟並取樣動畫骨骼位置
                if (frameDelta > SUB_STEP_THRESHOLD && runtimeData.HasPendingHitWindows(prevTimer, stateTimer))
                {
                    int steps = Mathf.CeilToInt(frameDelta / SUB_STEP_THRESHOLD);
                    float stepSize = frameDelta / steps;
                    for (int i = 1; i <= steps; i++)
                    {
                        float subPrev = prevTimer + stepSize * (i - 1);
                        float subCurrent = prevTimer + stepSize * i;
                        // 取樣動畫到子幀時間點，讓骨骼移動到正確位置
                        primaryClip.SampleAnimation(animTarget, subCurrent);
                        runtimeData.UpdateHitWindows(subPrev, subCurrent);
                    }
                    // 還原骨骼到實際當前時間（避免視覺跳動）
                    primaryClip.SampleAnimation(animTarget, stateTimer);
                    // SampleAnimation 會將 root bone 偏移套用到 transform（繞過 OnAnimatorMove），
                    // 重置 localPosition/localRotation 防止 VFX 和模型偏移
                    animTarget.transform.localPosition = Vector3.zero;
                    animTarget.transform.localRotation = Quaternion.identity;
                }
                else
                {
                    runtimeData.UpdateHitWindows(prevTimer, stateTimer);
                }

                // 更新時間軸事件
                runtimeData.UpdateTimelineEvents(stateTimer);

                // 到達 AllowCancelTime 後解除取消鎖定
                if (isCancelLocked && stateTimer >= attackData.AllowCancelTime)
                {
                    isCancelLocked = false;
                    owner.OwnedTags.RemoveTag(GameplayTags.State.AttackNonCancellable);
                }

                // [NEW] 檢查是否超過 ComboResetTime，如果是則啟動延遲清除
                if (!hasScheduledClear && stateTimer >= attackData.ComboResetTime)
                {
                    hasScheduledClear = true;
                    ScheduleClearMarkedTarget(spec);
                }

                // 檢查輸入（攻擊輸入優先）
                if (CheckComboInput(spec, attackData, stateTimer))
                {
                    yield break; // 連招已觸發，當前能力結束
                }

                // 收刀取消：超過收刀時間且有移動輸入時，取消攻擊進入移動
                if (CheckSheatheCancelByMovement(spec, attackData, stateTimer))
                {
                    yield break;
                }

                yield return null;
            }

            // 動畫播放完成
            spec.EndAbility();
        }

        /// <summary>
        /// 檢查連招輸入
        /// </summary>
        private bool CheckComboInput(GameplayAbilitySpec spec, MeleeAttackData attackData, float currentTime)
        {
            // 檢查是否可以接受輸入
            if (currentTime < attackData.AllowInputTime) return false;

            var inputHandler = spec.Owner.GetComponent<AbilityInputHandler>();
            if (inputHandler == null || !inputHandler.HasInput()) return false;

            var nextInput = inputHandler.PeekInput();
            
            // 確保有有效輸入 (None 代表沒有輸入)
            if (nextInput == MeleeInputType.None) return false;

            if (nextInput == MeleeInputType.LightAttack || nextInput == MeleeInputType.HeavyAttack
                || nextInput == MeleeInputType.RangedAttack)
            {
                // 檢查是否超過連招時間
                if (currentTime > attackData.ComboResetTime)
                {
                    // 重置為第一擊（僅輕攻擊可重置）
                    if (nextInput == MeleeInputType.LightAttack)
                    {
                        inputHandler.ConsumeInput();
                        var resetAttack = FallbackFirstAttack ?? FirstAttackData;
                        TriggerComboAttack(spec, resetAttack);
                        return true;
                    }
                    // 超過連招時間但不是輕攻擊，消耗輸入但不觸發
                    inputHandler.ConsumeInput();
                    return false;
                }
                else
                {
                    // 搜索連招
                    foreach (var combo in attackData.NextCombos)
                    {
                        if (combo.InputType == nextInput && combo.NextAttack != null)
                        {
                            inputHandler.ConsumeInput();
                            TriggerComboAttack(spec, combo.NextAttack);
                            return true;
                        }
                    }
                    // 沒有找到匹配的連招，消耗輸入但不做任何事
                    inputHandler.ConsumeInput();
                }
            }
            else
            {
                // 其他輸入類型（如 Special），消耗掉
                inputHandler.ConsumeInput();
            }

            return false;
        }

        /// <summary>
        /// 檢查收刀取消：超過收刀時間且有移動輸入（無攻擊輸入）時取消攻擊
        /// </summary>
        private bool CheckSheatheCancelByMovement(GameplayAbilitySpec spec, MeleeAttackData attackData, float currentTime)
        {
            if (attackData.SheatheCancelTime < 0f) return false;
            if (currentTime < attackData.SheatheCancelTime) return false;
            // 攻擊輸入優先：有攻擊輸入時不取消
            var inputHandler = spec.Owner.GetComponent<AbilityInputHandler>();
            if (inputHandler != null && inputHandler.HasInput())
            {
                var peeked = inputHandler.PeekInput();
                if (peeked == MeleeInputType.LightAttack || peeked == MeleeInputType.HeavyAttack
                    || peeked == MeleeInputType.RangedAttack)
                {
                    return false;
                }
            }
            // 檢查是否有移動相關輸入（走路/跑步/跳躍）
            var locomotionReader = spec.Owner.GetComponent<Player.Locomotion.LocomotionInputReader>();
            if (locomotionReader == null) return false;
            bool hasMovement = locomotionReader.RawMove.magnitude > 0.1f;
            bool hasJump = locomotionReader.JumpPressedThisFrame;
            if (!hasMovement && !hasJump) return false;
            // 收刀取消 → 結束能力，combo 會因為提前結束而自動重置
            spec.EndAbility();
            return true;
        }

        /// <summary>
        /// 從 ASC 上的活躍近戰攻擊能力擷取攻擊快照。
        /// 由 WeaponManager 在攻擊途中切武器時呼叫,把當前攻擊交給殘影執行器接手。
        /// 找不到活躍的近戰攻擊時回傳 null。
        /// </summary>
        public static MeleeAttackSnapshot TryCaptureSnapshot(AbilitySystemComponent owner)
        {
            if (owner == null) return null;
            foreach (GameplayAbilitySpec spec in owner.GetAllAbilities())
            {
                if (!spec.IsActive) continue;
                if (spec.AbilityDef is GA_MeleeAttack && spec.CustomData is MeleeAttackRuntimeData rt)
                {
                    return rt.ToSnapshot();
                }
            }
            return null;
        }

        /// <summary>
        /// 觸發連招攻擊（支援跨類型：近戰→遠程）
        /// </summary>
        private void TriggerComboAttack(GameplayAbilitySpec spec, AttackDataBase nextAttack)
        {
            spec.EndAbility();

            // 根據攻擊數據類型決定使用哪個能力
            GameplayTag targetTag;
            if (nextAttack is MeleeAttackData)
            {
                targetTag = AbilityTag;
            }
            else if (CrossTypeAbilityTag.IsValid)
            {
                targetTag = CrossTypeAbilityTag;
            }
            else
            {
                Debug.LogWarning("[GA_MeleeAttack] 跨類型連招需要設定 CrossTypeAbilityTag！");
                return;
            }

            var newSpec = spec.Owner.FindAbilitySpec(targetTag);
            if (newSpec != null)
            {
                newSpec.CustomData = nextAttack;
                newSpec.TryActivate();
            }
        }
    }

    /// <summary>
    /// 近戰攻擊運行時數據
    /// </summary>
    public class MeleeAttackRuntimeData
    {
        public AbilitySystemComponent Owner { get; private set; }
        public MeleeAttackData AttackData { get; private set; }
        public AnimancerComponent Animancer { get; private set; }
        public CharacterController CharacterController { get; private set; }
        public CombatTargetFinder TargetFinder { get; private set; }
        public HitTargetMemory HitMemory { get; private set; }
        public AnimancerState AnimState { get; set; }
        /// <summary>
        /// 角色統一縮放係數（用於等比例調整 Hitbox、VFX、移動距離）
        /// </summary>
        public float ScaleFactor { get; private set; }

        public LayerMask EnemyLayer;
        public LayerMask ObstacleLayer;

        /// <summary>
        /// AutoFace 鎖定的目標 — 整段 ability 期間使用同一個目標,避免複數敵人時連擊中突然轉向
        /// 失效(null/inactive/超範圍/被障礙物擋)時 ResolveLockedAutoFaceTarget 會重新解析
        /// 下一個 ability(下一段連擊)RuntimeData 重建時自然重新鎖定
        /// </summary>
        public Transform AutoFaceLockedTarget { get; private set; }

        /// <summary>
        /// 取得 AutoFace 鎖定目標 — 已鎖定且有效就回傳,失效時 fallback 重新解析(讀 HitMemory.LastHitTarget)
        /// </summary>
        public Transform ResolveLockedAutoFaceTarget()
        {
            // 鎖定中 — 玩家明確選定的目標,只要還活著就面向它(不受視線遮擋 / AutoFace 範圍限制;脫鎖由鎖定系統自己管理)
            Transform locked = ResolveLockOnAnchor();
            if (locked != null)
            {
                AutoFaceLockedTarget = locked;
                return locked;
            }
            // 未鎖定 — 沿用本段攻擊鎖定的目標快取,失效才重新搜尋
            if (IsAutoFaceTargetValid(AutoFaceLockedTarget))
            {
                return AutoFaceLockedTarget;
            }
            AutoFaceLockedTarget = ResolveFreshAutoFaceTarget();
            return AutoFaceLockedTarget;
        }

        /// <summary>鎖定中且目標仍 active → 回傳鎖定點(AnchorTransform);未鎖定回傳 null</summary>
        private Transform ResolveLockOnAnchor()
        {
            if (LockOn == null || LockOn.CurrentTarget == null) return null;
            Transform anchor = LockOn.CurrentTarget.AnchorTransform;
            if (anchor == null || !anchor.gameObject.activeInHierarchy) return null;
            return anchor;
        }

        private bool IsAutoFaceTargetValid(Transform target)
        {
            if (target == null) return false;
            if (!target.gameObject.activeInHierarchy) return false;
            float dist = Vector3.Distance(Owner.transform.position, target.position);
            // 取兩個範圍的較大值 — 確保 360° proximity 搜尋找到的目標不會立刻失效
            float maxValidRange = Mathf.Max(
                AttackData.MovementConfig.AutoFaceRange,
                AttackData.MovementConfig.AutoFaceProximityRange);
            if (dist >= maxValidRange) return false;
            // 視線檢查 — 被障礙物擋住就不能 AutoFace
            Vector3 eyePos = Owner.transform.position + Vector3.up * 1.5f;
            Vector3 targetPoint = target.GetComponent<Collider>()?.ClosestPoint(eyePos) ?? target.position;
            return !Physics.Linecast(targetPoint, eyePos, ObstacleLayer);
        }

        private Transform ResolveFreshAutoFaceTarget()
        {
            // 此函式只在「未鎖定」時被呼叫(鎖定情況已在 ResolveLockedAutoFaceTarget 上層優先處理)
            // fallback 到上一次命中的敵人(維持既有未鎖定行為)
            if (HitMemory != null && HitMemory.LastHitTarget != null
                && IsAutoFaceTargetValid(HitMemory.LastHitTarget))
            {
                return HitMemory.LastHitTarget;
            }
            if (TargetFinder == null) return null;
            // 近距離 360° 全方位搜尋最近的敵人(背後也算),提供「軟鎖定」手感
            float proximityRange = AttackData.MovementConfig.AutoFaceProximityRange;
            if (proximityRange > 0f)
            {
                Transform nearest = TargetFinder.FindBestTarget(
                    Owner.transform.position, Owner.transform.forward,
                    proximityRange * ScaleFactor, 360f);
                if (nearest != null) return nearest;
            }
            // 近距離沒搜到 → 改用前方扇形,涵蓋正面但超出近距離的敵人(攻擊敵人時自動轉向的主力)
            float faceRange = AttackData.MovementConfig.AutoFaceRange;
            float faceAngle = AttackData.MovementConfig.AutoFaceAngle;
            if (faceRange > 0f && faceAngle > 0f)
            {
                Transform front = TargetFinder.FindBestTarget(
                    Owner.transform.position, Owner.transform.forward,
                    faceRange, faceAngle);
                if (front != null) return front;
            }
            return null;
        }

        // 命中視窗運行時狀態
        private readonly Dictionary<MeleeHitWindow, HitWindowRuntimeState> _hitWindowStates = new();

        // 時間軸事件觸發記錄
        private readonly HashSet<TimelineEvent> _triggeredEvents = new();

        // 時間軸事件追蹤(打斷 / 結束時的 VFX 銷毀 / detach 統一交由 TimelineEventSpawner.Cleanup 處理)
        private readonly Dictionary<TimelineEvent, TimelineEventInstance> _activeTimelineInstances = new();

        // 命中檢測緩衝
        private readonly RaycastHit[] _raycastBuffer = new RaycastHit[32];
        private readonly Collider[] _colliderBuffer = new Collider[32];

        // 骨骼映射
        private Dictionary<string, Transform> _socketMap;

        // 移動 Tween
        private Tween _moveTween;
        private NewGASPlayerController _playerController;

        public LockOnController LockOn { get; private set; }

        public MeleeAttackRuntimeData(AbilitySystemComponent owner, MeleeAttackData data,
            AnimancerComponent animancer, CharacterController cc,
            CombatTargetFinder targetFinder, HitTargetMemory hitMemory, LockOnController lockOn)
        {
            Owner = owner;
            AttackData = data;
            Animancer = animancer;
            CharacterController = cc;
            _playerController = owner.GetComponent<NewGASPlayerController>();
            TargetFinder = targetFinder;
            HitMemory = hitMemory;
            LockOn = lockOn;
            ScaleFactor = SpatialScaleUtility.GetScaleFactor(
                animancer != null ? animancer.transform : owner.transform);
            // 建立骨骼映射
            InitializeSocketMap();
        }

        /// <summary>
        /// 把當前攻擊狀態打包成快照供殘影執行器接手。
        /// 含:當前動畫時間、玩家 ASC、Layer 設定、活動中 HitWindow 的已命中清單、已觸發 TimelineEvent。
        /// 殘影執行器讀此快照後從接手時間點繼續攻擊,不會對同一敵人重複造成傷害,也不會重觸已 fired 的特效。
        /// </summary>
        public MeleeAttackSnapshot ToSnapshot()
        {
            MeleeAttackSnapshot snapshot = new MeleeAttackSnapshot
            {
                AttackData = AttackData,
                ResumeTime = AnimState != null ? AnimState.Time : 0f,
                InstigatorOwner = Owner,
                EnemyLayer = EnemyLayer,
                ObstacleLayer = ObstacleLayer,
                InheritedActiveHits = new Dictionary<MeleeHitWindow, HashSet<Collider>>(_hitWindowStates.Count),
                InheritedTriggeredEvents = new HashSet<TimelineEvent>(_triggeredEvents),
            };
            foreach (var kvp in _hitWindowStates)
            {
                snapshot.InheritedActiveHits[kvp.Key] = new HashSet<Collider>(kvp.Value.HitEnemies);
            }
            return snapshot;
        }

        /// <summary>
        /// 檢查在 [prevTime, currentTime] 區間內是否有任何 HitWindow 需要處理
        /// 用於決定是否需要啟動子幀命中檢測
        /// </summary>
        public bool HasPendingHitWindows(float prevTime, float currentTime)
        {
            foreach (var hitWindow in AttackData.HitWindows)
            {
                if (currentTime >= hitWindow.StartTime && prevTime <= hitWindow.EndTime)
                {
                    return true;
                }
            }
            return false;
        }

        private void InitializeSocketMap()
        {
            _socketMap = new Dictionary<string, Transform>();
            
            // [FIX] 不再從 FluxCombat.CombatController 獲取 socket 引用
            // 因為這些引用可能指向舊的 Prefab Transform，而不是動態創建的模型
            // 現在依靠 ResolveSocket() 方法使用 FindChildRecursive 動態查找骨骼
            
            // 如果需要預先緩存常用的 socket，可以在這裡添加邏輯
            // 例如：從 Animancer.gameObject（角色模型）開始搜尋預定義的骨骼名稱
            if (Animancer != null && Animancer.gameObject != null)
            {
                // 預先緩存模型的根節點，作為搜尋起點
                Transform modelRoot = Animancer.transform;
                
                if (Owner.DebugMode)
                {
                    Debug.Log($"[GA_MeleeAttack] Initialized socket map. Model root: {modelRoot.name}");
                }
            }
        }

        /// <summary>
        /// 更新命中視窗
        /// </summary>
        public void UpdateHitWindows(float prevTime, float currentTime)
        {
            // 重置上一幀的 JustActivated 標記
            foreach (var kvp in _hitWindowStates)
            {
                kvp.Value.JustActivated = false;
            }
            foreach (var hitWindow in AttackData.HitWindows)
            {
                bool isInWindow = currentTime >= hitWindow.StartTime && prevTime <= hitWindow.EndTime;
                if (isInWindow)
                {
                    if (!_hitWindowStates.ContainsKey(hitWindow))
                    {
                        // [FIX] 在觸發移動之前先完成轉向，確保移動方向正確
                        // 走 ability 鎖定目標,避免連擊中 LastHitTarget 被覆寫導致 HitWindow 轉向錯人
                        if (hitWindow.AutoFaceMarkedTarget)
                        {
                            Transform markedTarget = ResolveLockedAutoFaceTarget();
                            if (markedTarget != null)
                            {
                                Vector3 directionToTarget = markedTarget.position - Owner.transform.position;
                                directionToTarget.y = 0;
                                if (directionToTarget.sqrMagnitude > 0.001f)
                                {
                                    Owner.transform.rotation = Quaternion.LookRotation(directionToTarget.normalized);
                                }
                                if (Owner.DebugMode)
                                {
                                    Debug.Log($"[GA_MeleeAttack] HitWindow instantly faced locked target: {markedTarget.name}");
                                }
                            }
                        }
                        // 開始新的命中視窗
                        Transform origin = ResolveSocket(hitWindow.SocketName);
                        var state = new HitWindowRuntimeState
                        {
                            Origin = origin,
                            IsAttached = hitWindow.AttachToBody,
                            JustActivated = true,
                            WasFrameSkipped = prevTime < hitWindow.StartTime && currentTime > hitWindow.EndTime
                        };
                        Vector3 startPos = origin.TransformPoint(hitWindow.Offset);
                        state.PreviousPosition = startPos;
                        // 初始化射線軌跡的各段起始位置
                        if (hitWindow.UseRaycastTrail)
                        {
                            int segments = hitWindow.TrailSegments;
                            state.PreviousTrailPositions = new Vector3[segments];
                            for (int s = 0; s < segments; s++)
                            {
                                float t = segments > 1 ? (float)s / (segments - 1) : 0f;
                                Vector3 localPos = Vector3.Lerp(hitWindow.TrailStartOffset, hitWindow.TrailEndOffset, t);
                                state.PreviousTrailPositions[s] = origin.TransformPoint(localPos);
                            }
                        }
                        if (!hitWindow.AttachToBody)
                        {
                            state.WorldLockPosition = startPos;
                            state.WorldLockRotation = origin.rotation;
                        }
                        _hitWindowStates[hitWindow] = state;
                        // 觸發命中移動（此時朝向已經正確）
                        if (hitWindow.TriggerMovement)
                        {
                            ApplyHitMovement(hitWindow);
                        }
                    }
                    // 處理命中檢測
                    ProcessHitDetection(hitWindow, _hitWindowStates[hitWindow]);
                }
            }
            // 清理過期的命中視窗（剛啟動的視窗保留到下一幀，防止單幀跳過時只得到一次檢測）
            var toRemove = new List<MeleeHitWindow>();
            foreach (var kvp in _hitWindowStates)
            {
                if (currentTime > kvp.Key.EndTime && !kvp.Value.JustActivated)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var key in toRemove)
            {
                _hitWindowStates.Remove(key);
            }
        }

        /// <summary>
        /// 處理命中檢測（根據設定選擇原始 Sweep 或射線軌跡法）
        /// </summary>
        private void ProcessHitDetection(MeleeHitWindow hitWindow, HitWindowRuntimeState state)
        {
            if (hitWindow.UseRaycastTrail && state.PreviousTrailPositions != null)
            {
                ProcessHitDetectionRaycastTrail(hitWindow, state);
            }
            else
            {
                ProcessHitDetectionSweep(hitWindow, state);
            }
        }

        /// <summary>
        /// 原始 Sweep/Overlap 命中檢測
        /// </summary>
        private void ProcessHitDetectionSweep(MeleeHitWindow hitWindow, HitWindowRuntimeState state)
        {
            Vector3 currentPos;
            Quaternion currentRot;
            if (state.IsAttached)
            {
                currentPos = state.Origin.TransformPoint(hitWindow.Offset);
                currentRot = state.Origin.rotation;
            }
            else
            {
                currentPos = state.WorldLockPosition;
                currentRot = state.WorldLockRotation;
            }
            Vector3 direction = currentPos - state.PreviousPosition;
            float distance = direction.magnitude;
            Vector3 expandedSize = hitWindow.Size * ScaleFactor;
            if (state.WasFrameSkipped)
            {
                expandedSize *= 1.5f;
                state.WasFrameSkipped = false;
            }
            var currentFrameHits = new HashSet<Collider>();
            // Sweep 檢測
            Vector3 sweepDir = distance > 0.001f ? direction.normalized : -Owner.transform.forward;
            float sweepDist = distance + 0.5f;
            Vector3 sweepStart = state.PreviousPosition - (sweepDir * 0.5f);
            int hitCount;
            if (hitWindow.Shape == HitboxShape.Box)
            {
                hitCount = Physics.BoxCastNonAlloc(sweepStart, expandedSize / 2, sweepDir,
                    _raycastBuffer, currentRot, sweepDist, EnemyLayer);
            }
            else
            {
                hitCount = Physics.SphereCastNonAlloc(sweepStart, expandedSize.x,
                    sweepDir, _raycastBuffer, sweepDist, EnemyLayer);
            }
            for (int i = 0; i < hitCount; i++)
            {
                currentFrameHits.Add(_raycastBuffer[i].collider);
            }
            // Overlap 檢測
            int overlapCount;
            if (hitWindow.Shape == HitboxShape.Box)
            {
                overlapCount = Physics.OverlapBoxNonAlloc(currentPos, expandedSize / 2,
                    _colliderBuffer, currentRot, EnemyLayer);
            }
            else
            {
                overlapCount = Physics.OverlapSphereNonAlloc(currentPos, expandedSize.x,
                    _colliderBuffer, EnemyLayer);
            }
            for (int i = 0; i < overlapCount; i++)
            {
                currentFrameHits.Add(_colliderBuffer[i]);
            }
            // 處理命中
            foreach (var hitCollider in currentFrameHits)
            {
                if (state.HitEnemies.Contains(hitCollider)) continue;
                state.HitEnemies.Add(hitCollider);
                OnHit(hitWindow, hitCollider, currentPos);
            }
            state.PreviousPosition = currentPos;
        }

        /// <summary>
        /// 射線軌跡命中檢測（Raycast Socket Trail）
        /// 沿武器佈置多個取樣點，每幀從上一幀位置向當前位置發射射線
        /// 形成網狀掃掠面，精確捕捉高速揮擊的碰撞
        /// </summary>
        private void ProcessHitDetectionRaycastTrail(MeleeHitWindow hitWindow, HitWindowRuntimeState state)
        {
            int segments = hitWindow.TrailSegments;
            float rayRadius = hitWindow.TrailRayRadius * ScaleFactor;
            Transform origin = state.Origin;
            var currentFrameHits = new HashSet<Collider>();
            // 計算當前幀各段的世界座標
            var currentPositions = new Vector3[segments];
            for (int s = 0; s < segments; s++)
            {
                float t = segments > 1 ? (float)s / (segments - 1) : 0f;
                Vector3 localPos = Vector3.Lerp(hitWindow.TrailStartOffset, hitWindow.TrailEndOffset, t);
                currentPositions[s] = origin.TransformPoint(localPos);
            }
            // 沿武器的每個取樣點：從上一幀位置射向當前位置（縱向射線）
            for (int s = 0; s < segments; s++)
            {
                Vector3 from = state.PreviousTrailPositions[s];
                Vector3 to = currentPositions[s];
                Vector3 delta = to - from;
                float dist = delta.magnitude;
                if (dist < 0.001f) continue;
                Vector3 dir = delta / dist;
                int hitCount;
                if (rayRadius > 0f)
                {
                    hitCount = Physics.SphereCastNonAlloc(from, rayRadius, dir, _raycastBuffer, dist, EnemyLayer);
                }
                else
                {
                    hitCount = Physics.RaycastNonAlloc(from, dir, _raycastBuffer, dist, EnemyLayer);
                }
                for (int i = 0; i < hitCount; i++)
                {
                    currentFrameHits.Add(_raycastBuffer[i].collider);
                }
            }
            // 相鄰取樣點之間的橫向射線（當前幀，補捉武器寬度方向的碰撞）
            for (int s = 0; s < segments - 1; s++)
            {
                Vector3 from = currentPositions[s];
                Vector3 to = currentPositions[s + 1];
                Vector3 delta = to - from;
                float dist = delta.magnitude;
                if (dist < 0.001f) continue;
                Vector3 dir = delta / dist;
                int hitCount;
                if (rayRadius > 0f)
                {
                    hitCount = Physics.SphereCastNonAlloc(from, rayRadius, dir, _raycastBuffer, dist, EnemyLayer);
                }
                else
                {
                    hitCount = Physics.RaycastNonAlloc(from, dir, _raycastBuffer, dist, EnemyLayer);
                }
                for (int i = 0; i < hitCount; i++)
                {
                    currentFrameHits.Add(_raycastBuffer[i].collider);
                }
            }
            // 對角線射線（上一幀的第 s 點 → 當前幀的第 s+1 點，形成交叉網面）
            for (int s = 0; s < segments - 1; s++)
            {
                Vector3 from = state.PreviousTrailPositions[s];
                Vector3 to = currentPositions[s + 1];
                Vector3 delta = to - from;
                float dist = delta.magnitude;
                if (dist < 0.001f) continue;
                Vector3 dir = delta / dist;
                int hitCount;
                if (rayRadius > 0f)
                {
                    hitCount = Physics.SphereCastNonAlloc(from, rayRadius, dir, _raycastBuffer, dist, EnemyLayer);
                }
                else
                {
                    hitCount = Physics.RaycastNonAlloc(from, dir, _raycastBuffer, dist, EnemyLayer);
                }
                for (int i = 0; i < hitCount; i++)
                {
                    currentFrameHits.Add(_raycastBuffer[i].collider);
                }
            }
            // 安全網：在武器當前位置執行 OverlapCapsule，防止射線因弧形軌跡遺漏
            if (segments >= 2)
            {
                Vector3 capsuleP1 = currentPositions[0];
                Vector3 capsuleP2 = currentPositions[segments - 1];
                float maxSegmentMove = 0f;
                for (int s = 0; s < segments; s++)
                {
                    float segMove = (currentPositions[s] - state.PreviousTrailPositions[s]).sqrMagnitude;
                    if (segMove > maxSegmentMove) maxSegmentMove = segMove;
                }
                // 動態半徑：基於最大移動距離，覆蓋射線之間的間隙
                float safetyRadius = Mathf.Max(rayRadius, Mathf.Sqrt(maxSegmentMove) * 0.25f);
                int overlapCount = Physics.OverlapCapsuleNonAlloc(
                    capsuleP1, capsuleP2, safetyRadius, _colliderBuffer, EnemyLayer);
                for (int i = 0; i < overlapCount; i++)
                {
                    currentFrameHits.Add(_colliderBuffer[i]);
                }
            }
            // 計算命中點（用武器中點作為參考）
            Vector3 hitCenter = currentPositions[segments / 2];
            // 處理命中
            foreach (var hitCollider in currentFrameHits)
            {
                if (state.HitEnemies.Contains(hitCollider)) continue;
                state.HitEnemies.Add(hitCollider);
                OnHit(hitWindow, hitCollider, hitCenter);
            }
            // 更新上一幀位置
            for (int s = 0; s < segments; s++)
            {
                state.PreviousTrailPositions[s] = currentPositions[s];
            }
            state.PreviousPosition = hitCenter;
        }

        /// <summary>
        /// 命中處理
        /// </summary>
        private void OnHit(MeleeHitWindow hitWindow, Collider hitCollider, Vector3 hitboxCenter)
        {
            // [NEW] 根據 HitWindow 的設置決定是否標記目標
            if (hitWindow.MarkTargetOnHit && HitMemory != null)
            {
                // 設置新目標會自動重置延遲清除計時器
                HitMemory.LastHitTarget = hitCollider.transform;
                
                if (Owner.DebugMode)
                {
                    Debug.Log($"[GA_MeleeAttack] Marked target: {hitCollider.name} (reset clear timer)");
                }
            }

            // [FIX] 計算實際的命中點 - 取得敵人碰撞體最接近判定框中心的表面點
            Vector3 hitPoint = hitCollider.ClosestPoint(hitboxCenter);

            // 計算傷害並注入到 Effect
            float damage = hitWindow.BaseDamage * hitWindow.DamageMultiplier;

            // 嘗試應用傷害效果（透過 SetByCaller 注入計算後的傷害數值）
            var targetASC = hitCollider.GetComponent<AbilitySystemComponent>();
            bool gasApplied = false;
            if (targetASC != null && hitWindow.HitEffect != null)
            {
                Owner.ApplyEffectToTarget(targetASC, hitWindow.HitEffect, SetByCallerTags.DAMAGE, damage);
                gasApplied = true;
            }

            // 通知 IHitReceiver（處理硬直、擊退、死亡檢查、OnHurt 事件）
            var hitReceiver = hitCollider.GetComponent<IHitReceiver>();
            if (hitReceiver != null)
            {
                Vector3 attackDir = (hitCollider.transform.position - Owner.transform.position).normalized;
                attackDir.y = 0f;
                HitContext hitCtx = new HitContext
                {
                    damage = gasApplied ? 0f : damage,
                    poiseDamage = hitWindow.PoiseDamage,
                    knockbackForce = hitWindow.KnockbackForce,
                    attackTier = hitWindow.AttackTier,
                    isHeavyAttack = hitWindow.AttackTier == AttackTier.Heavy,
                    hitPoint = hitPoint,
                    hitNormal = (Owner.transform.position - hitPoint).normalized,
                    attackDirection = attackDir,
                    gasDamageApplied = gasApplied,
                    hitStopDuration = hitWindow.HitStopDuration,
                    hitStopTimeScale = hitWindow.HitStopSpeed,
                    cameraShakeIntensity = hitWindow.ScreenShakeForce,
                };
                hitReceiver.OnHit(ref hitCtx);
            }

            // === 處理命中特效(Cue + Prefab 兩條路並列生效;Prefab 用表面法線旋轉) ===
            Vector3 surfaceNormal = (Owner.transform.position - hitPoint).normalized;
            Quaternion surfaceRot = surfaceNormal.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(surfaceNormal)
                : Quaternion.identity;

            if (hitWindow.HitCueTag.IsValid)
            {
                Owner.ExecuteGameplayCue(hitWindow.HitCueTag, hitPoint, hitCollider.gameObject);
            }
            if (hitWindow.HitVFXPrefab != null)
            {
                HitVFXSpawner.Spawn(
                    hitWindow.HitVFXPrefab, hitPoint, surfaceRot,
                    hitWindow.HitVFXScale, ScaleFactor, hitWindow.HitVFXScaleAllChildren,
                    hitWindow.HitVFXLifetime,
                    hitWindow.AttachHitVFXToSurface ? hitCollider.transform : null);
            }
            if (hitWindow.HitSFX != null)
            {
                AudioSource.PlayClipAtPoint(hitWindow.HitSFX, hitPoint);
            }

            if (Owner.DebugMode)
            {
                Debug.Log($"[GA_MeleeAttack] Hit: {hitCollider.name}, Damage: {damage}, HitPoint: {hitPoint}");
            }
        }

        /// <summary>
        /// 應用命中移動
        /// </summary>
        private void ApplyHitMovement(MeleeHitWindow hitWindow)
        {
            if (CharacterController == null) return;

            switch (hitWindow.MovementType)
            {
                case MeleeMovementType.StandardSnap:
                    HandleSnapping(hitWindow);
                    break;
                case MeleeMovementType.PierceThrough:
                    HandlePierce(hitWindow);
                    break;
            }
        }

        private void HandleSnapping(MeleeHitWindow config)
        {
            if (TargetFinder == null) return;
            Transform preferred = HitMemory != null ? HitMemory.LastHitTarget : null;
            if (TargetFinder.TryGetSnapTarget(Owner.transform.position, Owner.transform.forward,
                config.SnapRange * ScaleFactor, config.SnapStopDistance * ScaleFactor,
                preferred, out Vector3 targetPos, out Transform target))
            {
                if (target != null)
                {
                    Owner.transform.DOLookAt(target.position, 0.1f, AxisConstraint.Y);
                }
                MoveToTarget(targetPos, config.MoveDuration, config.MoveCurve);
            }
            // 無目標時不再用程式碼位移 — RM 動畫自帶基礎位移
        }

        private void HandlePierce(MeleeHitWindow config)
        {
            // [FIX] 完整的穿刺移動邏輯 - 參考 PlayerTest 的實作
            Transform targetT = null;

            // 1. 嘗試找到目標敵人
            if (TargetFinder != null)
            {
                // 優先使用最後命中的目標
                Transform lastHit = HitMemory != null ? HitMemory.LastHitTarget : null;
                if (lastHit != null && lastHit.gameObject.activeInHierarchy)
                {
                    float dist = Vector3.Distance(Owner.transform.position, lastHit.position);
                    if (dist <= config.SnapRange * ScaleFactor)
                    {
                        Vector3 dir = (lastHit.position - Owner.transform.position).normalized;
                        if (Vector3.Angle(Owner.transform.forward, dir) < 60f)
                        {
                            // 檢查是否有障礙物擋住
                            Vector3 eyePos = Owner.transform.position + Vector3.up * 1.5f;
                            Vector3 targetPoint = lastHit.GetComponent<Collider>()?.ClosestPoint(eyePos)
                                ?? lastHit.position;
                            if (!Physics.Linecast(targetPoint, eyePos, ObstacleLayer))
                            {
                                targetT = lastHit;
                            }
                        }
                    }
                }
                // 如果沒有最後目標，搜索新目標
                if (targetT == null)
                {
                    targetT = TargetFinder.FindBestTarget(Owner.transform.position, Owner.transform.forward,
                        config.SnapRange * ScaleFactor, 120f);
                }
            }

            // 排除已死亡的目標（碰撞器已禁用，ClosestPoint 會回傳異常值）
            if (targetT != null)
            {
                EnemyController ec = targetT.GetComponent<EnemyController>();
                if (ec != null && ec.IsDead) targetT = null;
            }
            // 2. 如果找到目標，執行穿刺
            if (targetT != null)
            {
                // 計算穿刺方向 (確保正規化)
                Vector3 rawDir = targetT.position - Owner.transform.position;
                rawDir.y = 0;
                Vector3 direction = rawDir.sqrMagnitude > 0.001f ? rawDir.normalized : Owner.transform.forward;

                // 計算理想的穿刺終點 (敵人後方)
                Vector3 farPointBehindEnemy = targetT.position + (direction * 50.0f);
                Vector3 exitPoint = targetT.position;
                Collider targetCol = targetT.GetComponent<Collider>();
                if (targetCol != null)
                {
                    exitPoint = targetCol.ClosestPoint(farPointBehindEnemy);
                }

                float safetyMargin = CharacterController != null ? CharacterController.radius + 0.1f : 0.6f;
                float totalMoveDist = config.SnapStopDistance * ScaleFactor + safetyMargin;
                Vector3 pierceTargetPos = exitPoint + (direction * totalMoveDist);
                pierceTargetPos.y = Owner.transform.position.y;

                // 障礙物檢測 (使用 CapsuleCast)
                bool hitObstacle = false;
                Vector3 finalTargetPos = pierceTargetPos;

                if (CharacterController != null)
                {
                    float castRadius = CharacterController.radius * 0.9f;
                    float castHeight = CharacterController.height;
                    float capsuleOffset = Mathf.Max(0, castHeight * 0.5f - castRadius);
                    Vector3 p1 = Owner.transform.position + CharacterController.center + Vector3.up * capsuleOffset;
                    Vector3 p2 = Owner.transform.position + CharacterController.center - Vector3.up * capsuleOffset;

                    float checkDist = Vector3.Distance(Owner.transform.position, pierceTargetPos);

                    if (Physics.CapsuleCast(p1, p2, castRadius, direction, out RaycastHit wallHit, checkDist, ObstacleLayer))
                    {
                        hitObstacle = true;
                        float distToEnemy = Vector3.Distance(Owner.transform.position, targetT.position);

                        // 判斷障礙物位置
                        if (wallHit.distance < distToEnemy - CharacterController.radius)
                        {
                            // 牆在敵人與玩家之間：停在牆前
                            float safeDist = Mathf.Max(0, wallHit.distance - 0.05f);
                            finalTargetPos = Owner.transform.position + (direction * safeDist);
                        }
                        else
                        {
                            // 牆在敵人後方：切換為貼附行為
                            if (TargetFinder != null && TargetFinder.CalculateSnapPosition(
                                Owner.transform.position, targetT, config.SnapStopDistance * ScaleFactor, out Vector3 snapPos))
                            {
                                finalTargetPos = snapPos;
                            }
                            else
                            {
                                finalTargetPos = targetT.position - (direction * (config.SnapStopDistance * ScaleFactor + safetyMargin));
                                finalTargetPos.y = Owner.transform.position.y;
                            }
                        }
                    }
                    else
                    {
                        // 雙重檢查：確保終點本身不在牆內
                        Vector3 destP1 = finalTargetPos + CharacterController.center + Vector3.up * capsuleOffset;
                        Vector3 destP2 = finalTargetPos + CharacterController.center - Vector3.up * capsuleOffset;

                        if (Physics.CheckCapsule(destP1, destP2, castRadius, ObstacleLayer))
                        {
                            // 終點在牆內，回退到貼附
                            if (TargetFinder != null && TargetFinder.CalculateSnapPosition(
                                Owner.transform.position, targetT, config.SnapStopDistance * ScaleFactor, out Vector3 snapPos))
                            {
                                finalTargetPos = snapPos;
                            }
                        }
                    }
                }

                // 暫時忽略目標的碰撞，以完成穿刺
                if (!hitObstacle && CharacterController != null)
                {
                    Collider[] targetColliders = targetT.GetComponentsInChildren<Collider>();
                    System.Collections.Generic.List<Collider> ignoredColliders = new();

                    foreach (var col in targetColliders)
                    {
                        if (col != null && col.enabled)
                        {
                            Physics.IgnoreCollision(CharacterController, col, true);
                            ignoredColliders.Add(col);
                        }
                    }

                    Physics.SyncTransforms();

                    // 移動完成後恢復碰撞
                    System.Action cleanup = () => {
                        if (CharacterController != null)
                        {
                            foreach (var col in ignoredColliders)
                            {
                                if (col != null) Physics.IgnoreCollision(CharacterController, col, false);
                            }
                        }
                    };

                    MoveToTargetWithCleanup(finalTargetPos, config.MoveDuration, config.MoveCurve, cleanup);
                }
                else
                {
                    MoveToTarget(finalTargetPos, config.MoveDuration, config.MoveCurve);
                }
            }
            // 無目標時不再用程式碼位移 — RM 動畫自帶基礎位移
        }

        private void MoveToTarget(Vector3 targetPos, float duration, AnimationCurve curve)
        {
            MoveToTargetWithCleanup(targetPos, duration, curve, null);
        }

        private void MoveToTargetWithCleanup(Vector3 targetPos, float duration, AnimationCurve curve, System.Action onComplete)
        {
            // 完成當前的移動,確保位置同步
            if (_moveTween != null && _moveTween.IsActive())
            {
                _moveTween.Complete();
                _moveTween = null;
            }

            // 抑制 RM 水平位移,讓 DOTween 全權控制
            if (_playerController != null)
            {
                _playerController.SuppressRootMotionPosition = true;
            }

            Vector3 startPos = Owner.transform.position;

            _moveTween = DOTween.To(() => 0f, x =>
            {
                Vector3 nextPos = Vector3.Lerp(startPos, targetPos, x);
                Vector3 delta = nextPos - Owner.transform.position;
                if (CharacterController != null)
                {
                    CharacterController.Move(delta);
                }
            }, 1f, duration)
            .SetEase(curve)
            .OnKill(() =>
            {
                if (_playerController != null) _playerController.SuppressRootMotionPosition = false;
                onComplete?.Invoke();
            })
            .OnComplete(() =>
            {
                if (_playerController != null) _playerController.SuppressRootMotionPosition = false;
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// 更新時間軸事件
        /// </summary>
        public void UpdateTimelineEvents(float currentTime)
        {
            foreach (var evt in AttackData.TimelineEvents)
            {
                if (!_triggeredEvents.Contains(evt) && currentTime >= evt.TriggerTime)
                {
                    TriggerTimelineEvent(evt);
                    _triggeredEvents.Add(evt);
                }
            }
        }

        private void TriggerTimelineEvent(TimelineEvent evt)
        {
            Transform socket = ResolveSocket(evt.SocketName);
            TimelineEventInstance inst = TimelineEventSpawner.Trigger(evt, socket, ScaleFactor, Owner);
            if (inst != null && (inst.SpawnedVFX != null || inst.CueHandler != null))
            {
                _activeTimelineInstances[evt] = inst;
            }
        }

        private Transform ResolveSocket(string socketName)
        {
            if (string.IsNullOrEmpty(socketName))
            {
                // 如果沒有指定 socket 名稱，使用角色模型的根節點，而不是玩家根節點
                return Animancer != null ? Animancer.transform : Owner.transform;
            }

            // 優先使用 socket 映射表
            if (_socketMap != null && _socketMap.TryGetValue(socketName, out var socket))
            {
                // 驗證 socket 是否仍然有效（可能因模型切換而失效）
                if (socket != null)
                {
                    return socket;
                }
                else
                {
                    // 移除無效的緩存
                    _socketMap.Remove(socketName);
                }
            }

            // 優先從角色模型開始搜尋，而不是從玩家根節點
            Transform searchRoot = Animancer != null ? Animancer.transform : Owner.transform;
            Transform found = FindChildRecursive(searchRoot, socketName);
            
            if (found != null)
            {
                // 找到後加入映射表，避免重複搜尋
                if (_socketMap != null)
                {
                    _socketMap[socketName] = found;
                }
                
                if (Owner.DebugMode)
                {
                    Debug.Log($"[GA_MeleeAttack] Found socket '{socketName}' at: {GetTransformPath(found)}");
                }
                
                return found;
            }

            // 找不到時警告並返回角色模型根節點
            if (Owner.DebugMode)
            {
                Debug.LogWarning($"[GA_MeleeAttack] Cannot find socket: '{socketName}' in model '{searchRoot.name}'. Using model root transform.");
            }
            return searchRoot;
        }
        
        /// <summary>
        /// 獲取 Transform 的完整路徑（用於調試）
        /// </summary>
        private string GetTransformPath(Transform t)
        {
            if (t == null) return "null";
            
            string path = t.name;
            Transform parent = t.parent;
            
            while (parent != null && parent != Owner.transform)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }

        /// <summary>
        /// 遞迴搜尋子物件（參考 PlayerTest 的 CombatHitDetection 實作）
        /// </summary>
        private Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            
            foreach (Transform child in parent)
            {
                var result = FindChildRecursive(child, name);
                if (result != null) return result;
            }
            
            return null;
        }

        /// <summary>
        /// 清理
        /// </summary>
        public void Cleanup(bool wasInterrupted)
        {
            if (_moveTween != null && _moveTween.IsActive())
            {
                _moveTween.Kill();
            }
            // 確保清除旗標(被取消/打斷時 Tween 的 OnComplete 可能沒跑到)
            if (_playerController != null)
            {
                _playerController.SuppressRootMotionPosition = false;
            }

            // 清理需要在中斷時停止的命中視窗
            if (wasInterrupted)
            {
                foreach (var kvp in _hitWindowStates)
                {
                    if (kvp.Key.StopOnInterrupt)
                    {
                        // 可以在這裡添加清理邏輯
                    }
                }
            }
            
            // TimelineEvent 收尾統一交給 Spawner.Cleanup 處理 — 包含直接 Prefab 與 CueTag fallback 兩條路徑
            foreach (var kvp in _activeTimelineInstances)
            {
                TimelineEventSpawner.Cleanup(kvp.Value, wasInterrupted);
            }

            _hitWindowStates.Clear();
            _triggeredEvents.Clear();
            _activeTimelineInstances.Clear();
        }
    }

    /// <summary>
    /// 命中視窗運行時狀態
    /// </summary>
    public class HitWindowRuntimeState
    {
        public HashSet<Collider> HitEnemies = new();
        public Vector3 PreviousPosition;
        public Transform Origin;
        public bool IsAttached;
        public Vector3 WorldLockPosition;
        public Quaternion WorldLockRotation;
        /// <summary>是否在這一幀剛初始化（防止被立即清理）</summary>
        public bool JustActivated;
        /// <summary>視窗是否被整個跳過（需要擴大檢測範圍）</summary>
        public bool WasFrameSkipped;
        /// <summary>射線軌跡上一幀各段的世界座標（索引 0 = 武器根部，末尾 = 武器末端）</summary>
        public Vector3[] PreviousTrailPositions;
    }
}
