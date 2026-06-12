using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using DG.Tweening;
using GAS.Targeting;
using GAS.Targeting.Combat;
using GAS.Targeting.LockOnV2;
using Player.Locomotion;

namespace GAS
{
    /// <summary>
    /// 遠程攻擊能力 - 統一處理投射物、AoE、蓄力等遠程攻擊模式
    /// 模仿 GA_MeleeAttack 的 Coroutine 架構
    /// </summary>
    [CreateAssetMenu(fileName = "GA_RangedAttack", menuName = "GAS/Abilities/Ranged Attack")]
    public class GA_RangedAttack : GameplayAbility
    {
        [Header("Attack Data")]
        [Tooltip("初始攻擊數據（第一擊）")]
        public RangedAttackData FirstAttackData;

        [Header("References")]
        [Tooltip("敵人圖層")]
        public LayerMask EnemyLayer;

        [Tooltip("障礙物圖層")]
        public LayerMask ObstacleLayer;

        [Header("Settings")]
        [Tooltip("回退時使用的第一擊（連招中斷後重新開始）")]
        public RangedAttackData FallbackFirstAttack;

        [Header("Cross-Type Combo")]
        [Tooltip("近戰攻擊能力標籤（用於遠程→近戰跨類型連招）")]
        public GameplayTag CrossTypeAbilityTag;

        /// <summary>
        /// 從 ASC 上的活躍遠程攻擊能力擷取攻擊快照(僅 QuickFire = ChargeMode.None)。
        /// 蓄力 / 瞄準模式(HoldToCharge / HoldToAim)目前不支援接管,回傳 null 讓殘影退回純視覺。
        /// </summary>
        public static RangedAttackSnapshot TryCaptureSnapshot(AbilitySystemComponent owner)
        {
            if (owner == null) return null;
            foreach (GameplayAbilitySpec spec in owner.GetAllAbilities())
            {
                if (!spec.IsActive) continue;
                if (spec.AbilityDef is GA_RangedAttack rangedAbility
                    && spec.CustomData is RangedAttackRuntimeData rt
                    && rt.AttackData != null && rt.AttackData.Charge == ChargeMode.None)
                {
                    RangedAttackSnapshot snapshot = rt.ToSnapshot();
                    // EnemyLayer / ObstacleLayer 在 ability 物件上,不在 RuntimeData,從 ability 直接注入
                    snapshot.EnemyLayer = rangedAbility.EnemyLayer;
                    snapshot.ObstacleLayer = rangedAbility.ObstacleLayer;
                    return snapshot;
                }
            }
            return null;
        }

        public override void ActivateAbility(GameplayAbilitySpec spec)
        {
            var attackData = spec.CustomData as RangedAttackData ?? FirstAttackData;
            if (attackData == null)
            {
                Debug.LogError("[GA_RangedAttack] 沒有設定攻擊數據！");
                spec.EndAbility();
                return;
            }
            var coroutine = StartCoroutine(spec, RangedAttackRoutine(spec, attackData));
            spec.SetActiveCoroutine(coroutine);
        }

        public override void EndAbility(GameplayAbilitySpec spec, bool wasCancelled)
        {
            if (spec.CustomData is RangedAttackRuntimeData runtimeData)
            {
                runtimeData.Cleanup(wasCancelled);
            }
            // 確保移除取消鎖定標籤 + State.Aiming(HoldToAim 進行中的標示)
            spec.Owner?.OwnedTags.RemoveTag(GameplayTags.State.AttackNonCancellable);
            spec.Owner?.OwnedTags.RemoveTag(GameplayTags.State.Aiming);
            if (wasCancelled)
            {
                ScheduleClearMarkedTarget(spec);
                var inputHandler = spec.Owner?.GetComponent<AbilityInputHandler>();
                inputHandler?.ClearBuffer();
            }
            base.EndAbility(spec, wasCancelled);
        }

        /// <summary>
        /// 啟動延遲清除標記
        /// </summary>
        private void ScheduleClearMarkedTarget(GameplayAbilitySpec spec)
        {
            HitTargetMemory hitMemory = spec.Owner?.GetComponent<HitTargetMemory>();
            if (hitMemory != null)
            {
                hitMemory.ScheduleMarkClear();
            }
        }

        /// <summary>
        /// 遠程攻擊主協程
        /// </summary>
        private IEnumerator RangedAttackRoutine(GameplayAbilitySpec spec, RangedAttackData attackData)
        {
            var owner = spec.Owner;
            var playerController = owner.GetComponent<NewGASPlayerController>();
            var animancer = playerController?.Animancer;
            if (animancer == null)
            {
                animancer = owner.GetComponentInChildren<AnimancerComponent>();
            }
            CombatTargetFinder targetFinder = owner.GetComponent<CombatTargetFinder>();
            HitTargetMemory hitMemory = owner.GetComponent<HitTargetMemory>();
            LockOnController lockOn = owner.GetComponent<LockOnController>();
            var characterController = owner.GetComponent<CharacterController>();
            var aimCamera = owner.GetComponent<AimCameraController>();
            var aimUI = FindAimUI(owner);

            if (animancer == null)
            {
                Debug.LogError("[GA_RangedAttack] 找不到 AnimancerComponent！");
                spec.EndAbility();
                yield break;
            }

            // 建立運行時數據
            var runtimeData = new RangedAttackRuntimeData(owner, attackData, animancer, targetFinder, hitMemory, lockOn, aimCamera, aimUI);
            spec.CustomData = runtimeData;

            // 根據蓄力模式分流
            switch (attackData.Charge)
            {
                case ChargeMode.None:
                    yield return QuickFireRoutine(spec, runtimeData);
                    break;
                case ChargeMode.HoldToCharge:
                    yield return HoldToChargeRoutine(spec, runtimeData);
                    break;
                case ChargeMode.HoldToAim:
                    yield return HoldToAimRoutine(spec, runtimeData);
                    break;
            }
        }

        #region Quick Fire (ChargeMode.None)

        /// <summary>
        /// 快速射擊流程（輕攻擊）
        /// </summary>
        private IEnumerator QuickFireRoutine(GameplayAbilitySpec spec, RangedAttackRuntimeData runtime)
        {
            var attackData = runtime.AttackData;
            var animancer = runtime.Animancer;
            var owner = spec.Owner;

            // 此 ability 不使用瞄準鏡頭,但場上仍有 aim 狀態(殘留自前一個 HoldToAim 且 KeepAim=true) → 主動退出
            // 否則 QuickFire 動畫會在肩射視角下播放,身體又沒被同步轉到相機方向 → 視覺錯亂
            if (!attackData.EnableAimCamera && runtime.AimCamera != null && runtime.AimCamera.IsAiming)
            {
                runtime.AimCamera.ExitAim();
                runtime.AimUI?.HideAll();
                runtime.AimIK?.ClearAimTarget();
            }

            // 自動面向目標
            AutoFaceTarget(runtime);

            // 播放發射動畫
            if (attackData.FireAnimation.Clip == null)
            {
                Debug.LogError("[GA_RangedAttack] 沒有設定發射動畫！");
                spec.EndAbility();
                yield break;
            }

            var animState = animancer.Play(attackData.FireAnimation);
            animState.Time = 0;
            runtime.AnimState = animState;

            float animDuration = attackData.FireAnimation.Clip.length;
            float stateTimer = 0f;
            bool hasScheduledClear = false;

            // 攻擊開始時禁止取消，到達 AllowCancelTime 後解除
            bool isCancelLocked = attackData.AllowCancelTime > 0f;
            if (isCancelLocked)
            {
                owner.OwnedTags.AddTag(GameplayTags.State.AttackNonCancellable);
            }

            // 多段位移追蹤
            var startedMovements = new HashSet<int>();

            while (stateTimer < animDuration && spec.IsActive)
            {
                stateTimer = animState.Time;
                runtime.UpdateAimIK();
                UpdateContinuousFacing(runtime, Time.deltaTime);

                // 到達 AllowCancelTime 後解除取消鎖定
                if (isCancelLocked && stateTimer >= attackData.AllowCancelTime)
                {
                    isCancelLocked = false;
                    owner.OwnedTags.RemoveTag(GameplayTags.State.AttackNonCancellable);
                }

                // 多發射擊：根據每發的 FireTime 依序發射
                FireByTime(runtime, stateTimer, 1f);

                // 更新時間軸事件（VFX/SFX）
                runtime.UpdateTimelineEvents(stateTimer);

                // 多段攻擊位移
                for (int i = 0; i < attackData.AttackMovements.Count; i++)
                {
                    var moveCfg = attackData.AttackMovements[i];
                    if (moveCfg.Enabled && !startedMovements.Contains(i)
                        && stateTimer >= moveCfg.StartTime)
                    {
                        startedMovements.Add(i);
                        ApplyAttackMovement(runtime, moveCfg);
                    }
                }

                // 超過 ComboResetTime 啟動延遲清除
                if (!hasScheduledClear && stateTimer >= attackData.ComboResetTime)
                {
                    hasScheduledClear = true;
                    ScheduleClearMarkedTarget(spec);
                }

                // 檢查連招輸入（攻擊輸入優先）
                if (CheckComboInput(spec, attackData, stateTimer))
                {
                    yield break;
                }

                // 收刀取消：超過收刀時間且有移動輸入時，取消攻擊進入移動
                if (CheckSheatheCancelByMovement(spec, attackData, stateTimer))
                {
                    yield break;
                }

                yield return null;
            }

            spec.EndAbility();
        }

        #endregion

        #region Hold To Charge (ChargeMode.HoldToCharge)

        /// <summary>
        /// 長按蓄力流程（重攻擊）
        /// </summary>
        private IEnumerator HoldToChargeRoutine(GameplayAbilitySpec spec, RangedAttackRuntimeData runtime)
        {
            var attackData = runtime.AttackData;
            var animancer = runtime.Animancer;
            AbilityInputHandler abilityInput = spec.Owner.GetComponent<AbilityInputHandler>();

            // 自動面向目標
            AutoFaceTarget(runtime);

            // PlayerCursor 模式 — 凍結 locomotion 並初始化光標位置(WASD 改為操作 cursor)
            bool isCursorMode = attackData.AoEOriginMode == AoEOriginMode.PlayerCursor;
            if (isCursorMode)
            {
                EnterCursorMode(runtime);
            }

            // 顯示蓄力收縮環
            runtime.AimUI?.ShowChargeRing();

            // 若為 AoE 攻擊且有設 AoE Prefab,建立預覽(蓄力中即時顯示落點圈)
            TryBeginAoEPreview(runtime);

            // 蓄力時間從 ChargeStart 動畫開始就累計(玩家視覺上看到拉弓動作就在蓄力)
            float chargeTime = 0f;
            bool autoFired = false;
            bool earlyFire = false;

            // 播放蓄力開始動畫(同時累計 chargeTime / 偵測釋放 / 偵測滿蓄)
            if (attackData.ChargeStartAnimation.Clip != null)
            {
                var startAnim = animancer.Play(attackData.ChargeStartAnimation);
                startAnim.Time = 0;
                runtime.AnimState = startAnim;

                float startDuration = attackData.ChargeStartAnimation.Clip.length;
                float startTimer = 0f;
                while (startTimer < startDuration && spec.IsActive)
                {
                    startTimer = startAnim.Time;
                    chargeTime += Time.deltaTime;
                    // 視覺/範圍比例:分段曲線,MinChargeTime 時剛好 ratio=0(對應 100% 半徑)
                    float visualRatio = attackData.ComputeVisualChargeRatio(chargeTime);
                    // AimUI ChargeRing 進度條:單純 0→1 線性,給玩家「蓄力進度」感受
                    float uiProgress = Mathf.Clamp01(chargeTime / attackData.MaxChargeTime);
                    runtime.AimUI?.SetChargeProgress(uiProgress);
                    runtime.CurrentChargeRatio = visualRatio;
                    runtime.UpdateAimIK();
                    runtime.UpdateTimelineEvents(startTimer, TimelineEventPhase.ChargeStart);
                    if (isCursorMode) UpdateCursor(runtime, Time.deltaTime);
                    UpdateAoEPreview(runtime);
                    UpdateAoEPrefabPreview(runtime, visualRatio);
                    UpdateContinuousFacing(runtime, Time.deltaTime);

                    if (chargeTime >= attackData.MaxChargeTime)
                    {
                        autoFired = true;
                        break;
                    }
                    if (chargeTime >= attackData.MinChargeTime
                        && !IsChargeInputHeld(abilityInput, attackData))
                    {
                        earlyFire = true;
                        break;
                    }
                    yield return null;
                }
            }

            if (!spec.IsActive)
            {
                runtime.AimUI?.HideChargeRing();
                runtime.AimUI?.HideAoEIndicator();
                // 蓄力中斷 → 銷毀預覽 AoE
                if (runtime.PendingAoEPreview != null)
                {
                    runtime.PendingAoEPreview.CancelPreview();
                    runtime.PendingAoEPreview = null;
                }
                yield break;
            }

            // 若 ChargeStart 階段已自動發射或釋放,跳過 ChargeLoop 直接進發射
            if (!autoFired && !earlyFire)
            {
                if (attackData.ChargeLoopAnimation.Clip != null)
                {
                    var loopAnim = animancer.Play(attackData.ChargeLoopAnimation);
                    runtime.AnimState = loopAnim;
                }

                float loopTimer = 0f;
                while (spec.IsActive)
                {
                    chargeTime += Time.deltaTime;
                    loopTimer += Time.deltaTime;
                    runtime.UpdateAimIK();
                    runtime.UpdateTimelineEvents(loopTimer, TimelineEventPhase.ChargeLoop);
                    // 視覺/範圍比例(分段曲線):MinChargeTime 接點為 ratio=0(對應 100% 半徑)
                    float visualRatio = attackData.ComputeVisualChargeRatio(chargeTime);
                    float uiProgress = Mathf.Clamp01(chargeTime / attackData.MaxChargeTime);

                    runtime.AimUI?.SetChargeProgress(uiProgress);
                    runtime.CurrentChargeRatio = visualRatio;
                    if (isCursorMode) UpdateCursor(runtime, Time.deltaTime);
                    UpdateAoEPreview(runtime);
                    UpdateAoEPrefabPreview(runtime, visualRatio);
                    UpdateContinuousFacing(runtime, Time.deltaTime);

                    if (chargeTime >= attackData.MaxChargeTime)
                    {
                        autoFired = true;
                        break;
                    }
                    if (chargeTime >= attackData.MinChargeTime
                        && !IsChargeInputHeld(abilityInput, attackData))
                    {
                        break;
                    }
                    yield return null;
                }
            }

            if (!spec.IsActive)
            {
                runtime.AimUI?.HideChargeRing();
                runtime.AimUI?.HideAoEIndicator();
                yield break;
            }

            // 傷害倍率:仍以 MinChargeTime 為門檻(BOTW 風格,鬆鍵越晚傷害越高)
            float damageRatio = Mathf.Clamp01(
                (chargeTime - attackData.MinChargeTime) /
                (attackData.MaxChargeTime - attackData.MinChargeTime));
            float damageMultiplier = Mathf.Lerp(1f, attackData.ChargeMultiplier, damageRatio);
            // 視覺/範圍比例(分段曲線):釋放當下的視覺即為實際命中半徑
            runtime.CurrentChargeRatio = attackData.ComputeVisualChargeRatio(chargeTime);

            // 隱藏蓄力收縮環 + AoE 預覽圈(即將實際發射,真正的 AoE VFX 由 AoEBehaviour 接手)
            runtime.AimUI?.HideChargeRing();
            runtime.AimUI?.HideAoEIndicator();

            // PlayerCursor 模式 — 玩家已 commit,恢復 locomotion(發射動畫期間正常受 AttackNonCancellable 保護)
            if (isCursorMode)
            {
                ExitCursorMode(runtime);
            }

            // 多段攻擊位移
            foreach (var moveCfg in attackData.AttackMovements)
            {
                if (moveCfg.Enabled)
                {
                    ApplyAttackMovement(runtime, moveCfg);
                }
            }

            // 播放蓄力發射動畫
            if (attackData.ChargeFireAnimation.Clip != null)
            {
                var fireAnim = animancer.Play(attackData.ChargeFireAnimation);
                fireAnim.Time = 0;
                runtime.AnimState = fireAnim;

                float fireDuration = attackData.ChargeFireAnimation.Clip.length;
                float fireTimer = 0f;
                bool hasScheduledClear = false;
                bool isCancelLocked = attackData.AllowCancelTime > 0f;
                if (isCancelLocked)
                {
                    spec.Owner.OwnedTags.AddTag(GameplayTags.State.AttackNonCancellable);
                }

                while (fireTimer < fireDuration && spec.IsActive)
                {
                    fireTimer = fireAnim.Time;
                    runtime.UpdateAimIK();
                    UpdateContinuousFacing(runtime, Time.deltaTime);
                    FireByTime(runtime, fireTimer, damageMultiplier);
                    runtime.UpdateTimelineEvents(fireTimer);

                    if (isCancelLocked && fireTimer >= attackData.AllowCancelTime)
                    {
                        isCancelLocked = false;
                        spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.AttackNonCancellable);
                    }

                    if (!hasScheduledClear && fireTimer >= attackData.ComboResetTime)
                    {
                        hasScheduledClear = true;
                        ScheduleClearMarkedTarget(spec);
                    }

                    if (CheckComboInput(spec, attackData, fireTimer))
                    {
                        yield break;
                    }

                    if (CheckSheatheCancelByMovement(spec, attackData, fireTimer))
                    {
                        yield break;
                    }

                    yield return null;
                }
            }
            else
            {
                // 沒有蓄力發射動畫，直接發射
                FireAll(runtime, damageMultiplier);
            }

            spec.EndAbility();
        }

        #endregion

        #region Hold To Aim (ChargeMode.HoldToAim)

        /// <summary>
        /// 瞄準模式流程（弓重攻擊）
        /// </summary>
        private IEnumerator HoldToAimRoutine(GameplayAbilitySpec spec, RangedAttackRuntimeData runtime)
        {
            var attackData = runtime.AttackData;
            var animancer = runtime.Animancer;
            AbilityInputHandler abilityInput = spec.Owner.GetComponent<AbilityInputHandler>();

            // 標示「正在執行 aim ability」 — AimCameraController 靠這個 tag 區分
            // 「ability 進行中(允許移動,不退出 aim)」與「post-fire 持久 aim(任何移動就退出)」
            spec.Owner.OwnedTags.AddTag(GameplayTags.State.Aiming);

            // 啟用瞄準相機
            if (attackData.EnableAimCamera && runtime.AimCamera != null)
            {
                runtime.AimCamera.SetShoulderOffset(attackData.AimCameraOffset);
                runtime.AimCamera.EnterAim();
            }

            // 顯示準星 UI（收縮環僅當 MaxChargeTime > MinChargeTime 才顯示）
            runtime.AimUI?.ShowCrosshair();
            bool hasChargeRange = attackData.MaxChargeTime > attackData.MinChargeTime;
            if (hasChargeRange)
            {
                runtime.AimUI?.ShowChargeRing();
                runtime.AimUI?.SetChargeProgress(0f);
            }

            // 觸發蓄力起手 Cue
            if (attackData.ChargeCueTag.IsValid && runtime.Owner != null)
            {
                Vector3 chargeCuePos = runtime.GetSpawnPositionForEvent(null);
                runtime.Owner.ExecuteGameplayCue(attackData.ChargeCueTag, chargeCuePos, null);
            }

            // 蓄力時間從 ChargeStart 動畫播放時就開始累計（玩家視覺上看到開始拉弓動作就在蓄力）
            float aimTime = 0f;
            bool chargeReadyTriggered = false;
            bool earlyFire = false;

            // 播放蓄力開始動畫（同時計時、偵測釋放、偵測外部退出）
            if (attackData.ChargeStartAnimation.Clip != null)
            {
                var startAnim = animancer.Play(attackData.ChargeStartAnimation);
                startAnim.Time = 0;
                runtime.AnimState = startAnim;

                float startDuration = attackData.ChargeStartAnimation.Clip.length;
                float startTimer = 0f;
                while (startTimer < startDuration && spec.IsActive)
                {
                    startTimer = startAnim.Time;
                    aimTime += Time.deltaTime;
                    runtime.UpdateAimIK();
                    runtime.UpdateTimelineEvents(startTimer, TimelineEventPhase.ChargeStart);
                    UpdateBodyFaceCamera(runtime);
                    UpdateAoEPreview(runtime);
                    UpdateChargeUI(runtime, attackData, aimTime, hasChargeRange);
                    TriggerChargeReadyCueIfNeeded(runtime, attackData, aimTime, ref chargeReadyTriggered);

                    // 外部退出瞄準（移動/受擊/死亡 → AimCameraController 已 ExitAim） → 中斷蓄力
                    if (DetectExternalAimExit(runtime, attackData))
                    {
                        ExitAimMode(runtime);
                        spec.EndAbility();
                        yield break;
                    }

                    // 按下後即承諾發射 — 釋放只有在達 MinChargeTime 後才能提前觸發
                    // 早於 MinChargeTime 的釋放會被忽略,蓄力繼續到門檻才能射出
                    if (aimTime >= attackData.MinChargeTime
                        && !IsChargeInputHeld(abilityInput, attackData))
                    {
                        earlyFire = true;
                        break;
                    }

                    yield return null;
                }
            }

            if (!spec.IsActive)
            {
                ExitAimMode(runtime);
                yield break;
            }

            // 若 ChargeStart 階段未提前釋放,進入 ChargeLoop 等待迴圈
            if (!earlyFire)
            {
                if (attackData.ChargeLoopAnimation.Clip != null)
                {
                    var loopAnim = animancer.Play(attackData.ChargeLoopAnimation);
                    runtime.AnimState = loopAnim;
                }

                // ChargeLoop 階段的本地計時器(從 0 起算),供 phase=ChargeLoop 的 TimelineEvent 使用
                float chargeLoopTimer = 0f;

                while (spec.IsActive)
                {
                    aimTime += Time.deltaTime;
                    chargeLoopTimer += Time.deltaTime;
                    runtime.UpdateAimIK();
                    runtime.UpdateTimelineEvents(chargeLoopTimer, TimelineEventPhase.ChargeLoop);
                    UpdateBodyFaceCamera(runtime);
                    UpdateAoEPreview(runtime);
                    UpdateChargeUI(runtime, attackData, aimTime, hasChargeRange);
                    TriggerChargeReadyCueIfNeeded(runtime, attackData, aimTime, ref chargeReadyTriggered);

                    // 外部退出瞄準 → 中斷蓄力
                    if (DetectExternalAimExit(runtime, attackData))
                    {
                        ExitAimMode(runtime);
                        spec.EndAbility();
                        yield break;
                    }

                    // 達 MinChargeTime 後才接受釋放發射;早於門檻的釋放被忽略,蓄力繼續
                    if (aimTime >= attackData.MinChargeTime
                        && !IsChargeInputHeld(abilityInput, attackData))
                    {
                        break;
                    }

                    if (abilityInput != null && abilityInput.LightAttackTriggered) break;

                    yield return null;
                }
            }

            if (!spec.IsActive)
            {
                ExitAimMode(runtime);
                yield break;
            }

            // 發射 — 傷害用 MinChargeTime 門檻版本,視覺/範圍走分段曲線(MinChargeTime 時剛好 100%)
            float damageRatio = Mathf.Clamp01(
                (aimTime - attackData.MinChargeTime) /
                (attackData.MaxChargeTime - attackData.MinChargeTime));
            float damageMultiplier = Mathf.Lerp(1f, attackData.ChargeMultiplier, damageRatio);
            runtime.CurrentChargeRatio = attackData.ComputeVisualChargeRatio(aimTime);

            // 進入發射前先收掉收縮環/AoE 預覽（準星保留到 ExitAimMode）
            runtime.AimUI?.HideChargeRing();
            runtime.AimUI?.HideAoEIndicator();

            // 多段攻擊位移
            foreach (var moveCfg in attackData.AttackMovements)
            {
                if (moveCfg.Enabled)
                {
                    ApplyAttackMovement(runtime, moveCfg);
                }
            }

            // 播放發射動畫
            ClipTransition fireClip = attackData.ChargeFireAnimation.Clip != null
                ? attackData.ChargeFireAnimation
                : attackData.FireAnimation;

            if (fireClip.Clip != null)
            {
                var fireAnim = animancer.Play(fireClip);
                fireAnim.Time = 0;
                runtime.AnimState = fireAnim;

                float fireDuration = fireClip.Clip.length;
                // ability TTL = max(動畫時長, 所有 timings 的最大值 + 餘量)
                // 確保 ComboReset/SheatheCancel 即使長於動畫也能完整作用
                float maxTiming = Mathf.Max(attackData.ComboResetTime, attackData.SheatheCancelTime);
                float ttl = Mathf.Max(fireDuration, maxTiming + 0.05f);
                float fireTimer = 0f;
                bool hasScheduledClear = false;
                bool isCancelLocked = attackData.AllowCancelTime > 0f;
                if (isCancelLocked)
                {
                    spec.Owner.OwnedTags.AddTag(GameplayTags.State.AttackNonCancellable);
                }

                while (fireTimer < ttl && spec.IsActive)
                {
                    fireTimer += Time.deltaTime;

                    // 只在動畫播放期間更新 IK / 投射物 / 時間軸事件
                    if (fireTimer <= fireDuration)
                    {
                        runtime.UpdateAimIK();
                        FireByTime(runtime, fireTimer, damageMultiplier);
                        runtime.UpdateTimelineEvents(fireTimer);
                    }

                    if (isCancelLocked && fireTimer >= attackData.AllowCancelTime)
                    {
                        isCancelLocked = false;
                        spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.AttackNonCancellable);
                    }

                    if (!hasScheduledClear && fireTimer >= attackData.ComboResetTime)
                    {
                        hasScheduledClear = true;
                        ScheduleClearMarkedTarget(spec);
                    }

                    // 連發手感:fire 動畫過 AllowInputTime 後若玩家仍按住蓄力鍵 → 自動接下一發 HoldToAim
                    // 用 held state 偵測,即使 BufferTime 過期、玩家壓著沒重按也能觸發,
                    // 比 CheckComboInput 的離散按鍵 buffer 更穩定
                    if (fireTimer >= attackData.AllowInputTime
                        && IsChargeInputHeld(abilityInput, attackData))
                    {
                        TriggerComboAttack(spec, attackData);
                        yield break;
                    }

                    if (CheckComboInput(spec, attackData, fireTimer))
                    {
                        yield break;
                    }

                    if (CheckSheatheCancelByMovement(spec, attackData, fireTimer))
                    {
                        yield break;
                    }

                    yield return null;
                }
            }
            else
            {
                FireAll(runtime, damageMultiplier);
            }

            // 自然 fire 結束 → 沒設 IsChainingNext,Cleanup 會自動 ExitAim
            // (auto-chain 的話會在迴圈內 yield break,不會走到這)
            spec.EndAbility();
        }

        /// <summary>
        /// 退出瞄準模式（清理相機和 UI）
        /// </summary>
        private void ExitAimMode(RangedAttackRuntimeData runtime)
        {
            runtime.AimCamera?.ExitAim();
            runtime.AimUI?.HideAll();
        }

        /// <summary>
        /// 蓄力進度更新（驅動 AimUI 的收縮環）
        /// </summary>
        private void UpdateChargeUI(RangedAttackRuntimeData runtime, RangedAttackData data, float aimTime, bool hasChargeRange)
        {
            if (!hasChargeRange) return;
            float ratio = Mathf.Clamp01(
                (aimTime - data.MinChargeTime) /
                (data.MaxChargeTime - data.MinChargeTime));
            runtime.AimUI?.SetChargeProgress(ratio);
        }

        /// <summary>
        /// 跨越 MinChargeTime 時觸發一次性 ChargeReady Cue
        /// </summary>
        private void TriggerChargeReadyCueIfNeeded(RangedAttackRuntimeData runtime, RangedAttackData data, float aimTime, ref bool triggered)
        {
            if (triggered) return;
            if (aimTime < data.MinChargeTime) return;
            triggered = true;
            if (data.ChargeReadyCueTag.IsValid && runtime.Owner != null)
            {
                Vector3 readyPos = runtime.GetSpawnPositionForEvent(null);
                runtime.Owner.ExecuteGameplayCue(data.ChargeReadyCueTag, readyPos, null);
            }
        }

        /// <summary>
        /// 偵測外部 AimCamera 是否被別處(AimCameraController auto-exit)關閉
        /// 用於 HoldToAim 蓄力中如果玩家移動/受擊導致鏡頭退出,中斷整個 ability,
        /// 避免 zombie 狀態(ability 還在跑但相機已退出)阻止下一次按鍵重新進入瞄準
        /// </summary>
        private bool DetectExternalAimExit(RangedAttackRuntimeData runtime, RangedAttackData data)
        {
            if (!data.EnableAimCamera) return false;
            if (runtime.AimCamera == null) return false;
            return !runtime.AimCamera.IsAiming;
        }

        /// <summary>
        /// 依 RangedAttackData.ChargeInput 偵測對應的「按住中」狀態
        /// </summary>
        private bool IsChargeInputHeld(AbilityInputHandler handler, RangedAttackData data)
        {
            if (handler == null) return false;
            return data.ChargeInput switch
            {
                ChargeInputBinding.RangeAttack => handler.IsRangeAttackHeld,
                ChargeInputBinding.LightAttack => handler.IsInputHeld(MeleeInputType.LightAttack),
                ChargeInputBinding.HeavyAttack => handler.IsInputHeld(MeleeInputType.HeavyAttack),
                _ => handler.IsRangeAttackHeld
            };
        }

        /// <summary>
        /// HoldToAim 期間每幀把角色 yaw 轉到相機 forward 的水平投影
        /// 讓上半身整體朝向 = 發射方向（避免身體面前、子彈卻往螢幕中央飛的錯位）
        /// </summary>
        private void UpdateBodyFaceCamera(RangedAttackRuntimeData runtime)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            Vector3 forward = mainCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();
            Transform tf = runtime.Owner.transform;
            Quaternion target = Quaternion.LookRotation(forward);
            float maxDelta = 720f * Time.deltaTime;
            tf.rotation = Quaternion.RotateTowards(tf.rotation, target, maxDelta);
        }

        /// <summary>
        /// 瞄準階段每幀更新 AoE 落點預覽指示器
        /// AoETargeted: 跟隨相機螢幕中心射線命中地面點
        /// AoEAtTarget: 鎖定目標位置；無鎖定 fallback 到 LastHitTarget
        /// 其他攻擊類型或無 AoEPrefab 時隱藏指示器
        /// </summary>
        private void UpdateAoEPreview(RangedAttackRuntimeData runtime)
        {
            var aimUI = runtime.AimUI;
            if (aimUI == null) return;

            var attackData = runtime.AttackData;
            if (attackData.AoEPrefab == null
                || (attackData.AttackType != RangedAttackType.AoETargeted
                    && attackData.AttackType != RangedAttackType.AoEAtTarget))
            {
                aimUI.HideAoEIndicator();
                return;
            }

            // 從 prefab 上的 AoEBehaviour 讀範圍設定
            AoEBehaviour prefabBehaviour = attackData.AoEPrefab.GetComponent<AoEBehaviour>();
            if (prefabBehaviour == null)
            {
                aimUI.HideAoEIndicator();
                return;
            }

            Vector3 center = ResolveAoECenter(runtime);
            // 走分段曲線(與 AoEPrefab 縮放、編輯器 gizmo 全部一致)
            float multiplier = prefabBehaviour.GetEffectiveScaleMultiplier(runtime.CurrentChargeRatio);
            aimUI.ShowAoEIndicator(center, prefabBehaviour.Radius * multiplier);
        }

        #endregion

        #region Fire Logic

        /// <summary>
        /// 發射所有 FireEvents 中時間到的投射物
        /// </summary>
        private void FireAll(RangedAttackRuntimeData runtime, float damageMultiplier)
        {
            List<RangedFireEvent> fireEvents = runtime.ResolvedFireEvents;
            foreach (var evt in fireEvents)
            {
                if (!runtime.FiredEvents.Contains(evt))
                {
                    runtime.FiredEvents.Add(evt);
                    FireSingle(runtime, damageMultiplier, evt);
                }
            }
        }

        /// <summary>
        /// 檢查並發射已到達 FireTime 的事件
        /// </summary>
        private void FireByTime(RangedAttackRuntimeData runtime, float currentTime, float damageMultiplier)
        {
            List<RangedFireEvent> fireEvents = runtime.ResolvedFireEvents;
            foreach (var evt in fireEvents)
            {
                if (!runtime.FiredEvents.Contains(evt) && currentTime >= evt.FireTime)
                {
                    runtime.FiredEvents.Add(evt);
                    FireSingle(runtime, damageMultiplier, evt);
                }
            }
        }

        /// <summary>
        /// 執行單發發射（根據 AttackType 選擇發射方式）
        /// 一發只 Solve 一次，結果傳給 Projectile/Hitscan handlers 共用
        /// </summary>
        private void FireSingle(RangedAttackRuntimeData runtime, float damageMultiplier, RangedFireEvent fireEvent)
        {
            var attackData = runtime.AttackData;
            // 使用每發有效傷害（覆寫或回退到共用值）
            float baseDamage = fireEvent.GetEffectiveBaseDamage(attackData);
            float finalDamage = baseDamage * damageMultiplier * fireEvent.DamageMultiplier;
            runtime.Solve(fireEvent, out FireSolveResult solve);

            // 觸發發射 Cue
            if (attackData.FireCueTag.IsValid && runtime.Owner != null)
            {
                runtime.Owner.ExecuteGameplayCue(attackData.FireCueTag, solve.SpawnPosition, null);
            }

            switch (attackData.AttackType)
            {
                case RangedAttackType.Projectile:
                    SpawnProjectile(runtime, finalDamage, fireEvent, in solve);
                    break;
                case RangedAttackType.AoETargeted:
                case RangedAttackType.AoEAtTarget:
                    SpawnAoE(runtime, finalDamage, fireEvent);
                    break;
                case RangedAttackType.Hitscan:
                    PerformHitscan(runtime, finalDamage, fireEvent, in solve);
                    break;
            }
        }

        /// <summary>
        /// 向後相容的單發介面
        /// </summary>
        private void Fire(RangedAttackRuntimeData runtime, float damageMultiplier)
        {
            FireAll(runtime, damageMultiplier);
        }

        /// <summary>
        /// 生成投射物
        /// </summary>
        private void SpawnProjectile(RangedAttackRuntimeData runtime, float damage, RangedFireEvent fireEvent, in FireSolveResult solve)
        {
            var projData = runtime.AttackData.ProjectileConfig;
            if (projData == null || projData.Prefab == null)
            {
                Debug.LogWarning("[GA_RangedAttack] 沒有設定投射物配置！");
                return;
            }

            Vector3 spawnPos = solve.SpawnPosition;
            Vector3 direction = solve.FireDirection;
            Quaternion rotation = Quaternion.LookRotation(direction);

            // 追蹤目標（如果啟用追蹤且有鎖定目標） — 傳 root,ProjectileBehaviour 內部解析 AimAnchor
            Transform homingTarget = null;
            if (projData.HomingEnabled && runtime.LockOn != null && runtime.LockOn.CurrentTarget != null)
            {
                homingTarget = runtime.LockOn.CurrentTarget.transform;
            }

            // 從池中取出投射物
            ProjectileBehaviour projectile;
            if (ProjectilePoolManager.Instance != null)
            {
                projectile = ProjectilePoolManager.Instance.Get(projData.Prefab, spawnPos, rotation);
            }
            else
            {
                GameObject instance = Instantiate(projData.Prefab, spawnPos, rotation);
                projectile = instance.GetComponent<ProjectileBehaviour>();
                if (projectile == null)
                {
                    projectile = instance.AddComponent<ProjectileBehaviour>();
                }
            }

            if (projectile != null)
            {
                GameplayEffect effectiveHitEffect = fireEvent.GetEffectiveHitEffect(runtime.AttackData);
                GameplayTag effectiveHitCueTag = fireEvent.GetEffectiveHitCueTag(runtime.AttackData);
                GameObject effectiveHitVFX = fireEvent.GetEffectiveHitVFXPrefab(runtime.AttackData);
                AudioClip effectiveHitSFX = fireEvent.GetEffectiveHitSFX(runtime.AttackData);
                projectile.Initialize(
                    projData,
                    runtime.Owner,
                    direction,
                    damage,
                    effectiveHitEffect,
                    effectiveHitCueTag,
                    effectiveHitVFX,
                    effectiveHitSFX,
                    runtime.AttackData.HitVFXLifetime,
                    runtime.AttackData.AttachHitVFXToSurface,
                    runtime.AttackData.HitVFXScale,
                    runtime.AttackData.HitVFXScaleAllChildren,
                    runtime.ScaleFactor,
                    homingTarget);
            }
        }

        /// <summary>
        /// 統一 AoE 生成入口 — 優先 promote 蓄力期間的 PendingAoEPreview,沒有則 Instantiate 新的
        /// 中心位置由 RangedAttackData.AoEOriginMode 解析(攻擊層控制位置)
        /// </summary>
        private void SpawnAoE(RangedAttackRuntimeData runtime, float damage, RangedFireEvent fireEvent)
        {
            var attackData = runtime.AttackData;
            AoEBehaviour aoeBehaviour;

            if (runtime.PendingAoEPreview != null)
            {
                // 預覽中的 AoE 直接 promote 成攻擊 — 無視覺斷層
                aoeBehaviour = runtime.PendingAoEPreview;
                runtime.PendingAoEPreview = null;
                Vector3 firePos = ResolveAoECenter(runtime);
                Quaternion fireRot = Quaternion.LookRotation(runtime.Owner.transform.forward, Vector3.up);
                aoeBehaviour.transform.SetPositionAndRotation(firePos, fireRot);
            }
            else
            {
                GameObject aoePrefab = attackData.AoEPrefab;
                if (aoePrefab == null)
                {
                    Debug.LogWarning("[GA_RangedAttack] 沒有設定 AoE Prefab！");
                    return;
                }
                Vector3 center = ResolveAoECenter(runtime);
                Quaternion rotation = Quaternion.LookRotation(runtime.Owner.transform.forward, Vector3.up);
                GameObject aoeInstance = Instantiate(aoePrefab, center, rotation);
                aoeBehaviour = aoeInstance.GetComponent<AoEBehaviour>();
                if (aoeBehaviour == null)
                {
                    Debug.LogWarning($"[GA_RangedAttack] AoE Prefab '{aoePrefab.name}' 沒有 AoEBehaviour 組件！");
                    Destroy(aoeInstance);
                    return;
                }
            }

            GameplayEffect effectiveHitEffect = fireEvent.GetEffectiveHitEffect(attackData);
            GameplayTag effectiveHitCueTag = fireEvent.GetEffectiveHitCueTag(attackData);
            AoEBehaviour.HitVFXInfo hitVFX = new AoEBehaviour.HitVFXInfo
            {
                Prefab = fireEvent.GetEffectiveHitVFXPrefab(attackData),
                SFX = fireEvent.GetEffectiveHitSFX(attackData),
                Lifetime = attackData.HitVFXLifetime,
                AttachToSurface = attackData.AttachHitVFXToSurface,
                Scale = attackData.HitVFXScale,
                ScaleAllChildren = attackData.HitVFXScaleAllChildren,
                AttackerScale = runtime.ScaleFactor,
            };
            aoeBehaviour.Activate(
                runtime.Owner,
                damage,
                effectiveHitEffect,
                effectiveHitCueTag,
                runtime.CurrentChargeRatio,
                hitVFX);
        }

        /// <summary>
        /// 蓄力期間建立 AoE 預覽實例(若 attack 為 AoE 類型且設了 AoEPrefab)
        /// 重複呼叫無效(已有 preview 就直接回傳)
        /// </summary>
        private static AoEBehaviour TryBeginAoEPreview(RangedAttackRuntimeData runtime)
        {
            if (runtime.PendingAoEPreview != null) return runtime.PendingAoEPreview;
            var attackData = runtime.AttackData;
            if (attackData.AttackType != RangedAttackType.AoETargeted
                && attackData.AttackType != RangedAttackType.AoEAtTarget) return null;
            if (attackData.AoEPrefab == null) return null;

            Vector3 center = ResolveAoECenter(runtime);
            Quaternion rotation = Quaternion.LookRotation(runtime.Owner.transform.forward, Vector3.up);
            GameObject instance = Instantiate(attackData.AoEPrefab, center, rotation);
            AoEBehaviour beh = instance.GetComponent<AoEBehaviour>();
            if (beh == null)
            {
                Object.Destroy(instance);
                return null;
            }
            beh.BeginPreview();
            beh.UpdatePreview(center, rotation, 0f);
            runtime.PendingAoEPreview = beh;
            return beh;
        }

        /// <summary>
        /// 蓄力期間每幀更新預覽位置/朝向/蓄力比例
        /// </summary>
        private static void UpdateAoEPrefabPreview(RangedAttackRuntimeData runtime, float chargeRatio)
        {
            if (runtime.PendingAoEPreview == null) return;
            Vector3 center = ResolveAoECenter(runtime);
            Quaternion rotation = Quaternion.LookRotation(runtime.Owner.transform.forward, Vector3.up);
            runtime.PendingAoEPreview.UpdatePreview(center, rotation, chargeRatio);
        }

        /// <summary>
        /// 依 RangedAttackData.AoEOriginMode 解析 AoE 中心世界位置。
        /// 凍結語意:任一實時 anchor(相機/LockOn/AutoFace) 解析成功時快取位置;
        /// 後續 anchor 全部失效(目標死亡等) → 回傳上次快取的位置,避免跳到新目標 / forward 預設造成 AoE 圈閃現。
        /// 從未有過 anchor → 走 forward 預設(玩家空曠處蓄力的合理 fallback)。
        /// </summary>
        private static Vector3 ResolveAoECenter(RangedAttackRuntimeData runtime)
        {
            var attackData = runtime.AttackData;
            Transform owner = runtime.Owner.transform;
            switch (attackData.AoEOriginMode)
            {
                case AoEOriginMode.PlayerCursor:
                    // Cursor 模式 — 直接用蓄力期間玩家推動的世界座標;未初始化前 fallback 到玩家前方
                    if (runtime.CursorInitialized)
                    {
                        return runtime.CacheAndReturnAoECenter(runtime.CursorPosition);
                    }
                    if (runtime.HasLastAoECenter) return runtime.LastAoECenter;
                    return owner.position + owner.forward * attackData.AoECursorInitialDistance;
                case AoEOriginMode.ScreenAim:
                    if (runtime.AimCamera != null
                        && runtime.AimCamera.TryGetGroundAimPoint(out Vector3 ground, ~0))
                    {
                        return runtime.CacheAndReturnAoECenter(ground);
                    }
                    if (TryGetLockOnAnchor(runtime, out Vector3 screenLockPos))
                    {
                        return runtime.CacheAndReturnAoECenter(screenLockPos);
                    }
                    if (runtime.HasLastAoECenter) return runtime.LastAoECenter;
                    return owner.position + owner.forward * 8f;
                case AoEOriginMode.LockedTarget:
                    if (TryGetLockOnAnchor(runtime, out Vector3 lockPos))
                    {
                        return runtime.CacheAndReturnAoECenter(lockPos);
                    }
                    if (TryGetHitMemoryAnchor(runtime, out Vector3 markPos))
                    {
                        return runtime.CacheAndReturnAoECenter(markPos);
                    }
                    if (runtime.HasLastAoECenter) return runtime.LastAoECenter;
                    return owner.position + owner.forward * 6f;
                case AoEOriginMode.PlayerForward:
                default:
                    if (TryGetLockOnAnchor(runtime, out Vector3 fwdLockPos))
                    {
                        return runtime.CacheAndReturnAoECenter(fwdLockPos);
                    }
                    // AutoFace 只讀已鎖定的目標(不呼叫 ResolveLockedAutoFaceTarget 重搜)— 目標死亡時走凍結,而非跳到新敵人
                    if (attackData.AutoFaceTarget
                        && TryGetAutoFaceLockedAnchor(runtime, out Vector3 afPos))
                    {
                        return runtime.CacheAndReturnAoECenter(afPos);
                    }
                    if (runtime.HasLastAoECenter) return runtime.LastAoECenter;
                    return owner.position + owner.forward * attackData.AoEForwardDistance;
            }
        }

        /// <summary>LockOn 鎖定中且目標 GameObject 仍 active → 回傳 anchor 位置</summary>
        private static bool TryGetLockOnAnchor(RangedAttackRuntimeData runtime, out Vector3 pos)
        {
            if (runtime.LockOn != null && runtime.LockOn.IsLocked && runtime.LockOn.CurrentTarget != null)
            {
                Transform anchor = runtime.LockOn.CurrentTarget.AnchorTransform;
                if (anchor != null && anchor.gameObject.activeInHierarchy)
                {
                    pos = anchor.position;
                    return true;
                }
            }
            pos = default;
            return false;
        }

        /// <summary>HitMemory.LastHitTarget 仍 active → 回傳其位置</summary>
        private static bool TryGetHitMemoryAnchor(RangedAttackRuntimeData runtime, out Vector3 pos)
        {
            if (runtime.HitMemory != null && runtime.HitMemory.LastHitTarget != null
                && runtime.HitMemory.LastHitTarget.gameObject.activeInHierarchy)
            {
                pos = runtime.HitMemory.LastHitTarget.position;
                return true;
            }
            pos = default;
            return false;
        }

        /// <summary>AutoFaceLockedTarget(本次 ability 初始鎖定的目標) 仍 active → 回傳其位置;不主動重搜</summary>
        private static bool TryGetAutoFaceLockedAnchor(RangedAttackRuntimeData runtime, out Vector3 pos)
        {
            Transform t = runtime.AutoFaceLockedTarget;
            if (t != null && t.gameObject.activeInHierarchy)
            {
                pos = t.position;
                return true;
            }
            pos = default;
            return false;
        }

        /// <summary>
        /// 即時射線命中（使用每發有效的 HitEffect/HitCueTag/HitVFX/HitSFX）
        /// </summary>
        private void PerformHitscan(RangedAttackRuntimeData runtime, float damage, RangedFireEvent fireEvent, in FireSolveResult solve)
        {
            Vector3 origin = solve.SpawnPosition;
            Vector3 direction = solve.FireDirection;
            GameplayEffect effectiveHitEffect = fireEvent.GetEffectiveHitEffect(runtime.AttackData);
            GameplayTag effectiveHitCueTag = fireEvent.GetEffectiveHitCueTag(runtime.AttackData);
            GameObject effectiveHitVFX = fireEvent.GetEffectiveHitVFXPrefab(runtime.AttackData);
            AudioClip effectiveHitSFX = fireEvent.GetEffectiveHitSFX(runtime.AttackData);
            if (Physics.Raycast(origin, direction, out RaycastHit hit, 100f, EnemyLayer))
            {
                var targetASC = hit.collider.GetComponentInParent<AbilitySystemComponent>();
                if (targetASC != null && targetASC != runtime.Owner)
                {
                    if (effectiveHitEffect != null)
                    {
                        runtime.Owner.ApplyEffectToTarget(targetASC, effectiveHitEffect, SetByCallerTags.DAMAGE, damage);
                    }
                    Quaternion surfaceRot = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    if (effectiveHitCueTag.IsValid)
                    {
                        runtime.Owner.ExecuteGameplayCue(effectiveHitCueTag, hit.point, surfaceRot, hit.collider.gameObject);
                    }
                    SpawnDirectHitFX(effectiveHitVFX, effectiveHitSFX, runtime, hit.point, surfaceRot, hit.collider.transform);
                }
            }
        }

        /// <summary>
        /// 直接生成 Hit Prefab + 播放音效(不經過 Cue) — Hitscan 路徑共用,沿用 HitVFXSpawner 的縮放邏輯
        /// </summary>
        private static void SpawnDirectHitFX(GameObject vfxPrefab, AudioClip sfx, RangedAttackRuntimeData runtime, Vector3 hitPoint, Quaternion surfaceRot, Transform hitTransform)
        {
            RangedAttackData attackData = runtime.AttackData;
            if (vfxPrefab != null)
            {
                HitVFXSpawner.Spawn(
                    vfxPrefab, hitPoint, surfaceRot,
                    attackData.HitVFXScale, runtime.ScaleFactor, attackData.HitVFXScaleAllChildren,
                    attackData.HitVFXLifetime,
                    attackData.AttachHitVFXToSurface ? hitTransform : null);
            }
            if (sfx != null)
            {
                AudioSource.PlayClipAtPoint(sfx, hitPoint);
            }
        }

        #endregion

        #region Combo

        /// <summary>
        /// 檢查連招輸入。
        /// 順序:
        /// 1. NextCombos 優先 — 不論時序,有對應 entry 就接過去(玩家配的 chain 永遠生效)
        /// 2. 過 ComboResetTime 且 NextCombos 沒對應的 LightAttack → reset 到 FallbackFirstAttack
        /// 3. 其他情況 → 消耗輸入但不觸發
        /// </summary>
        private bool CheckComboInput(GameplayAbilitySpec spec, RangedAttackData attackData, float currentTime)
        {
            if (currentTime < attackData.AllowInputTime) return false;

            var inputHandler = spec.Owner.GetComponent<AbilityInputHandler>();
            if (inputHandler == null || !inputHandler.HasInput()) return false;

            MeleeInputType nextInput = inputHandler.PeekInput();
            if (nextInput == MeleeInputType.None) return false;

            // 非攻擊類輸入直接消耗
            if (nextInput != MeleeInputType.LightAttack
                && nextInput != MeleeInputType.HeavyAttack
                && nextInput != MeleeInputType.RangedAttack)
            {
                inputHandler.ConsumeInput();
                return false;
            }

            // (1) NextCombos 優先 — 玩家明確配置的 chain,任何時序都應命中
            if (attackData.NextCombos != null)
            {
                foreach (var combo in attackData.NextCombos)
                {
                    if (combo.InputType == nextInput && combo.NextAttack != null)
                    {
                        inputHandler.ConsumeInput();
                        TriggerComboAttack(spec, combo.NextAttack);
                        return true;
                    }
                }
            }

            // (2) 過 ComboResetTime 的 LightAttack 但 NextCombos 沒對應 → reset 到第一招
            if (currentTime > attackData.ComboResetTime
                && nextInput == MeleeInputType.LightAttack)
            {
                inputHandler.ConsumeInput();
                var resetAttack = FallbackFirstAttack ?? FirstAttackData;
                TriggerComboAttack(spec, resetAttack);
                return true;
            }

            // (3) 其他情況消耗輸入但不觸發 — 避免延遲到下一個 ability 觸發
            inputHandler.ConsumeInput();
            return false;
        }

        /// <summary>
        /// 檢查收刀取消：超過收刀時間且有移動輸入（無攻擊輸入）時取消攻擊
        /// </summary>
        private bool CheckSheatheCancelByMovement(GameplayAbilitySpec spec, RangedAttackData attackData, float currentTime)
        {
            if (attackData.SheatheCancelTime < 0f) return false;
            if (currentTime < attackData.SheatheCancelTime) return false;
            // PlayerCursor 模式 — 移動輸入是操作 cursor,不該觸發收刀取消
            if (attackData.AoEOriginMode == AoEOriginMode.PlayerCursor) return false;
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
            // 檢查是否有移動相關輸入（走路/跑步/跳躍）— 用 RuntimeData 快取的 reader 避免每幀 GetComponent
            RangedAttackRuntimeData rt = spec.CustomData as RangedAttackRuntimeData;
            LocomotionInputReader locomotionReader = rt != null ? rt.LocomotionInput : null;
            if (locomotionReader == null) return false;
            bool hasMovement = locomotionReader.RawMove.magnitude > 0.1f;
            bool hasJump = locomotionReader.JumpPressedThisFrame;
            if (!hasMovement && !hasJump) return false;
            // 收刀取消 → 主動退出 aim 鏡頭(玩家要回 locomotion,不需保留瞄準視角)
            if (rt != null)
            {
                rt.AimCamera?.ExitAim();
                rt.AimUI?.HideAll();
                rt.AimIK?.ClearAimTarget();
            }
            spec.EndAbility();
            return true;
        }

        /// <summary>
        /// 觸發連招攻擊（支援跨類型：遠程→近戰）
        /// </summary>
        private void TriggerComboAttack(GameplayAbilitySpec spec, AttackDataBase nextAttack)
        {
            // 下一段不使用 aim 鏡頭(近戰 / Charge=None 一般攻擊) → 主動退出 aim,讓玩家視野切回正常
            // 下一段也用 aim → 保留(下一個 ability 的 EnterAim 會無痛接手)
            bool nextUsesAim = nextAttack is RangedAttackData rad && rad.EnableAimCamera;
            if (spec.CustomData is RangedAttackRuntimeData runtime)
            {
                if (!nextUsesAim)
                {
                    runtime.AimCamera?.ExitAim();
                    runtime.AimUI?.HideAll();
                    runtime.AimIK?.ClearAimTarget();
                }
                // 標記給 Cleanup:跳過自身的 aim 處理,因為這裡已經做了決定
                runtime.IsChainingNext = true;
            }

            spec.EndAbility();

            // 根據攻擊數據類型決定使用哪個能力
            GameplayTag targetTag;
            if (nextAttack is RangedAttackData)
            {
                targetTag = AbilityTag;
            }
            else if (CrossTypeAbilityTag.IsValid)
            {
                targetTag = CrossTypeAbilityTag;
            }
            else
            {
                Debug.LogWarning("[GA_RangedAttack] 跨類型連招需要設定 CrossTypeAbilityTag！");
                return;
            }

            var newSpec = spec.Owner.FindAbilitySpec(targetTag);
            if (newSpec != null)
            {
                newSpec.CustomData = nextAttack;
                newSpec.TryActivate();
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 自動面向目標 — 整段 ability 期間鎖定同一個目標,避免複數敵人時擺盪
        /// 目標失效(死亡/出範圍)才 fallback 重搜
        /// </summary>
        private void AutoFaceTarget(RangedAttackRuntimeData runtime)
        {
            if (!runtime.AttackData.AutoFaceTarget) return;
            Transform faceTarget = runtime.ResolveLockedAutoFaceTarget();
            if (faceTarget != null)
            {
                runtime.Owner.transform.DOLookAt(faceTarget.position, runtime.AttackData.AutoFaceDuration, AxisConstraint.Y);
            }
        }

        /// <summary>
        /// PlayerCursor 模式 — 進入 cursor 控制(蓄力起手呼叫):凍結 locomotion + 初始化 cursor 位置
        /// </summary>
        private static void EnterCursorMode(RangedAttackRuntimeData runtime)
        {
            if (runtime.LocomotionController != null)
            {
                runtime.LocomotionController.LocomotionSuppressed = true;
            }
            InitializeCursor(runtime);
        }

        /// <summary>
        /// PlayerCursor 模式 — 離開 cursor 控制(蓄力結束/取消呼叫):恢復 locomotion
        /// 重複呼叫安全(冪等),Cleanup catch-all 也呼叫此函式保險
        /// </summary>
        public static void ExitCursorMode(RangedAttackRuntimeData runtime)
        {
            if (runtime != null && runtime.LocomotionController != null)
            {
                runtime.LocomotionController.LocomotionSuppressed = false;
            }
        }

        /// <summary>
        /// 初始化 Cursor 起始位置 — 鎖定優先 → 相機水平 forward × InitialDistance
        /// </summary>
        private static void InitializeCursor(RangedAttackRuntimeData runtime)
        {
            var data = runtime.AttackData;
            Transform owner = runtime.Owner.transform;
            Vector3 cursorPos;
            // 1. LockOn 鎖定優先(起點 = 鎖定點)
            if (runtime.LockOn != null && runtime.LockOn.IsLocked
                && runtime.LockOn.CurrentTarget != null
                && runtime.LockOn.CurrentTarget.AnchorTransform != null)
            {
                cursorPos = runtime.LockOn.CurrentTarget.AnchorTransform.position;
            }
            else
            {
                // 2. 純相機水平 forward
                Camera cam = Camera.main;
                Vector3 forward;
                if (cam != null)
                {
                    forward = cam.transform.forward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 0.0001f) forward = owner.forward;
                    else forward.Normalize();
                }
                else
                {
                    forward = owner.forward;
                }
                cursorPos = owner.position + forward * data.AoECursorInitialDistance;
            }
            // 起始也貼地一次,避免懸空
            if (data.AoECursorClampToGround)
            {
                cursorPos = ClampCursorToGround(cursorPos, data.AoECursorGroundMask);
            }
            runtime.SetCursorPosition(cursorPos);
        }

        /// <summary>
        /// 每幀更新 Cursor — Camera-relative WASD 推動 + 半徑 clamp + 地形貼合
        /// </summary>
        private static void UpdateCursor(RangedAttackRuntimeData runtime, float deltaTime)
        {
            if (!runtime.CursorInitialized) return;
            var data = runtime.AttackData;
            LocomotionInputReader input = runtime.LocomotionInput;
            if (input == null) return;
            Vector3 cursorPos = runtime.CursorPosition;
            Vector2 raw = input.RawMove;
            // 套用移動輸入(Camera-relative)
            if (raw.sqrMagnitude > 0.0001f)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 camForward = cam.transform.forward;
                    camForward.y = 0f;
                    if (camForward.sqrMagnitude > 0.0001f)
                    {
                        camForward.Normalize();
                        // 從相機 forward 推導右向量(俯視 90° 順時針)
                        Vector3 camRight = new Vector3(camForward.z, 0f, -camForward.x);
                        Vector3 moveDir = camForward * raw.y + camRight * raw.x;
                        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
                        cursorPos += moveDir * (data.AoECursorMoveSpeed * deltaTime);
                    }
                }
            }
            // Clamp 半徑(軟邊界 — 推到牆就停,反方向可再推回)
            Transform owner = runtime.Owner.transform;
            Vector3 offset = cursorPos - owner.position;
            offset.y = 0f;
            float maxRange = data.AoECursorMaxRange;
            if (offset.sqrMagnitude > maxRange * maxRange)
            {
                offset = offset.normalized * maxRange;
                cursorPos = new Vector3(owner.position.x + offset.x, cursorPos.y, owner.position.z + offset.z);
            }
            // 每幀貼地 — 地形高低差時 cursor 跟著起伏
            if (data.AoECursorClampToGround)
            {
                cursorPos = ClampCursorToGround(cursorPos, data.AoECursorGroundMask);
            }
            runtime.SetCursorPosition(cursorPos);
        }

        /// <summary>
        /// 從 cursor 上方往下 raycast 找地面;命中 → cursor.y 對齊命中點,未命中 → 保持原 y
        /// </summary>
        private static Vector3 ClampCursorToGround(Vector3 pos, LayerMask groundMask)
        {
            Vector3 rayOrigin = new Vector3(pos.x, pos.y + 5f, pos.z);
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 50f, groundMask))
            {
                return new Vector3(pos.x, hit.point.y, pos.z);
            }
            return pos;
        }

        /// <summary>
        /// 攻擊執行中(蓄力/動畫)的持續面向追蹤 — 平滑旋轉到初始鎖定的目標。
        /// 與 AutoFaceTarget 的差異:
        /// • 不重搜:用 runtime.AutoFaceLockedTarget(攻擊開始時鎖定的目標),目標死亡/失效時直接停止追蹤而非切換到新敵人 — 避免「揮砍途中目標死亡又轉向另一個敵人」的突兀感
        /// • 平滑:Quaternion.RotateTowards + 可調 turn speed,不靠 DOLookAt 每幀建 tween
        /// • 受 ContinuousFaceTarget 旗標控制(預設關閉)
        /// </summary>
        private static void UpdateContinuousFacing(RangedAttackRuntimeData runtime, float deltaTime)
        {
            RangedAttackData data = runtime.AttackData;
            if (!data.AutoFaceTarget || !data.ContinuousFaceTarget) return;
            Transform target = runtime.AutoFaceLockedTarget;
            if (target == null || !target.gameObject.activeInHierarchy) return;
            Transform owner = runtime.Owner.transform;
            Vector3 toTarget = target.position - owner.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;
            Quaternion targetRot = Quaternion.LookRotation(toTarget);
            float maxDelta = data.ContinuousFaceTurnSpeed * deltaTime;
            owner.rotation = Quaternion.RotateTowards(owner.rotation, targetRot, maxDelta);
        }

        /// <summary>
        /// 執行攻擊位移（後跳、前衝等）
        /// </summary>
        private void ApplyAttackMovement(RangedAttackRuntimeData runtime, RangedMovementConfig moveCfg)
        {
            if (!moveCfg.Enabled) return;
            var owner = runtime.Owner;
            float dist = moveCfg.Distance * runtime.ScaleFactor;
            // 正值=前進，負值=後退
            Vector3 direction = dist >= 0 ? owner.transform.forward : -owner.transform.forward;
            Vector3 targetPos = owner.transform.position + direction * Mathf.Abs(dist);
            var characterController = owner.GetComponent<CharacterController>();
            Vector3 startPos = owner.transform.position;
            DOTween.To(() => 0f, x =>
            {
                Vector3 nextPos = Vector3.Lerp(startPos, targetPos, x);
                Vector3 delta = nextPos - owner.transform.position;
                if (characterController != null)
                {
                    characterController.Move(delta);
                }
                else
                {
                    owner.transform.position = nextPos;
                }
            }, 1f, moveCfg.Duration)
            .SetEase(moveCfg.Curve)
            .SetLink(owner.gameObject);
        }

        /// <summary>
        /// 查找 AimUIController
        /// </summary>
        private AimUIController FindAimUI(AbilitySystemComponent owner)
        {
            var ui = owner.GetComponentInChildren<AimUIController>();
            if (ui != null) return ui;

            // 嘗試從場景中查找
            return Object.FindAnyObjectByType<AimUIController>();
        }

        #endregion
    }

    /// <summary>
    /// 遠程攻擊運行時數據
    /// </summary>
    public class RangedAttackRuntimeData
    {
        public AbilitySystemComponent Owner { get; private set; }
        public RangedAttackData AttackData { get; private set; }
        public AnimancerComponent Animancer { get; private set; }
        public CombatTargetFinder TargetFinder { get; private set; }
        public HitTargetMemory HitMemory { get; private set; }
        public LockOnController LockOn { get; private set; }
        public AimCameraController AimCamera { get; private set; }
        public AimUIController AimUI { get; private set; }
        public RangedAimIK AimIK { get; private set; }
        public AnimancerState AnimState { get; set; }
        /// <summary>
        /// 角色統一縮放係數（用於等比例調整 VFX、移動距離）
        /// </summary>
        public float ScaleFactor { get; private set; }

        /// <summary>
        /// 已發射的 FireEvent 追蹤（防止重複發射）
        /// </summary>
        public HashSet<RangedFireEvent> FiredEvents { get; } = new();

        /// <summary>
        /// 由 TriggerComboAttack 在執行 chain 前設為 true,告知 Cleanup「不要關閉 aim 鏡頭,
        /// 下一發/下一招會接手」。chain 到非 aim 攻擊或自然結束時保持 false → Cleanup 退出 aim
        /// </summary>
        public bool IsChainingNext { get; set; }

        /// <summary>
        /// 當前蓄力比例 0~1(HoldToCharge/HoldToAim 釋放發射前由 routine 寫入,QuickFire 保持 0)
        /// 供 AoE 縮放與預覽指示器讀取
        /// </summary>
        public float CurrentChargeRatio { get; set; }

        /// <summary>
        /// 蓄力期間建立的 AoE 預覽實例 — 釋放發射時被 promote 成實際攻擊(避免 destroy+respawn 的視覺斷層)
        /// SpawnAoE 會優先使用此 instance,使用完清空為 null;Cleanup 路徑遇到非 null 會 CancelPreview
        /// </summary>
        public AoEBehaviour PendingAoEPreview { get; set; }

        /// <summary>
        /// 上次成功解析的 AoE 中心位置 — 鎖定/AutoFace 目標蓄力中死亡時,後續 ResolveAoECenter 回傳此位置,
        /// 避免「目標死亡 → 跳到下一個目標 / 回到前方預設」的 AoE 範圍閃現
        /// </summary>
        public Vector3 LastAoECenter { get; private set; }

        /// <summary>從未有過實時 anchor 時為 false,此時 ResolveAoECenter 走 forward 預設(避免回傳 Vector3.zero)</summary>
        public bool HasLastAoECenter { get; private set; }

        /// <summary>
        /// 同時快取與回傳 AoE 中心位置 — 在 ResolveAoECenter 找到實時 anchor 時鏈式呼叫,
        /// 之後 anchor 失效仍可走 LastAoECenter 凍結
        /// </summary>
        public Vector3 CacheAndReturnAoECenter(Vector3 pos)
        {
            LastAoECenter = pos;
            HasLastAoECenter = true;
            return pos;
        }

        /// <summary>PlayerCursor 模式 — 當前光標世界座標(蓄力期間玩家用 WASD 推動)</summary>
        public Vector3 CursorPosition { get; private set; }

        /// <summary>Cursor 是否已初始化(避免 ResolveAoECenter 在 InitializeCursor 之前回傳 Vector3.zero)</summary>
        public bool CursorInitialized { get; private set; }

        /// <summary>由 GA_RangedAttack 在 InitializeCursor / UpdateCursor 時呼叫,更新 cursor 位置</summary>
        public void SetCursorPosition(Vector3 pos)
        {
            CursorPosition = pos;
            CursorInitialized = true;
        }

        /// <summary>Locomotion 輸入讀取器(快取於 ctor) — 用於讀 RawMove 驅動 cursor + 收刀偵測</summary>
        public LocomotionInputReader LocomotionInput { get; private set; }

        /// <summary>Locomotion 主控制器(快取於 ctor) — PlayerCursor 模式時 LocomotionSuppressed=true 凍結角色</summary>
        public PlayerLocomotionController LocomotionController { get; private set; }

        /// <summary>
        /// AutoFace 鎖定的目標 — 整段 ability 期間使用同一個目標,避免複數敵人時鏡頭擺盪
        /// 失效(null/inactive/超出範圍)時 ResolveLockedAutoFaceTarget 會重新解析
        /// 連擊到下一個 ability 時 RuntimeData 重建,自然重新鎖定
        /// </summary>
        public Transform AutoFaceLockedTarget { get; private set; }

        /// <summary>
        /// 取得本次 ability 的 AutoFace 鎖定目標
        /// 已鎖定且有效 → 回傳同一個(不重搜尋)
        /// 失效 → fallback 重新解析(LockOn > HitMemory > FindBestTarget)並寫入鎖定
        /// </summary>
        public Transform ResolveLockedAutoFaceTarget()
        {
            // 鎖定中 — 玩家明確選定的目標,只要還活著就面向它(不受 AutoFace 範圍限制;切換鎖定即時跟上;脫鎖由鎖定系統管理)
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
            if (LockOn == null || !LockOn.IsLocked || LockOn.CurrentTarget == null) return null;
            Transform anchor = LockOn.CurrentTarget.AnchorTransform;
            if (anchor == null || !anchor.gameObject.activeInHierarchy) return null;
            return anchor;
        }

        /// <summary>
        /// 檢查鎖定目標是否仍有效:存在、active、在 AutoFaceRange 內
        /// </summary>
        private bool IsAutoFaceTargetValid(Transform target)
        {
            if (target == null) return false;
            if (!target.gameObject.activeInHierarchy) return false;
            float dist = Vector3.Distance(Owner.transform.position, target.position);
            return dist <= AttackData.AutoFaceRange;
        }

        /// <summary>
        /// 未鎖定時重新解析 AutoFace 目標(優先序:HitMemory(範圍內) → 近距離 360° → 前方扇形)
        /// </summary>
        private Transform ResolveFreshAutoFaceTarget()
        {
            // 此函式只在「未鎖定」時被呼叫(鎖定情況已在 ResolveLockedAutoFaceTarget 上層優先處理)
            if (HitMemory != null && HitMemory.LastHitTarget != null
                && HitMemory.LastHitTarget.gameObject.activeInHierarchy)
            {
                float dist = Vector3.Distance(Owner.transform.position, HitMemory.LastHitTarget.position);
                if (dist < AttackData.AutoFaceRange)
                {
                    return HitMemory.LastHitTarget;
                }
            }
            if (TargetFinder == null) return null;
            // 近距離 360° 全方位(含背後/側邊)優先 — 近敵逼近時遠程攻擊也會轉向自衛,與近戰一致
            float proximityRange = AttackData.AutoFaceProximityRange;
            if (proximityRange > 0f)
            {
                Transform nearest = TargetFinder.FindBestTarget(
                    Owner.transform.position, Owner.transform.forward,
                    proximityRange * ScaleFactor, 360f);
                if (nearest != null) return nearest;
            }
            // 近距離沒搜到 → 前方扇形(一般遠程瞄準)
            return TargetFinder.FindBestTarget(
                Owner.transform.position,
                Owner.transform.forward,
                AttackData.AutoFaceRange,
                AttackData.AutoFaceAngle);
        }

        /// <summary>
        /// 構造時解析出的發射事件列表（取代 RangedAttackData 的 SO 內快取，per-spec 隔離）
        /// </summary>
        public List<RangedFireEvent> ResolvedFireEvents { get; private set; }

        // 時間軸事件觸發記錄
        private readonly HashSet<TimelineEvent> _triggeredEvents = new();

        // 時間軸事件追蹤(打斷 / 結束時的 VFX 銷毀 / detach 統一交由 TimelineEventSpawner.Cleanup 處理)
        private readonly Dictionary<TimelineEvent, TimelineEventInstance> _activeTimelineInstances = new();

        private Dictionary<string, Transform> _socketMap;

        public RangedAttackRuntimeData(
            AbilitySystemComponent owner,
            RangedAttackData attackData,
            AnimancerComponent animancer,
            CombatTargetFinder targetFinder,
            HitTargetMemory hitMemory,
            LockOnController lockOn,
            AimCameraController aimCamera,
            AimUIController aimUI)
        {
            Owner = owner;
            AttackData = attackData;
            Animancer = animancer;
            TargetFinder = targetFinder;
            HitMemory = hitMemory;
            LockOn = lockOn;
            AimCamera = aimCamera;
            AimUI = aimUI;
            ScaleFactor = SpatialScaleUtility.GetScaleFactor(
                animancer != null ? animancer.transform : owner.transform);
            BuildSocketMap();
            ResolvedFireEvents = ResolveFireEvents(attackData);
            AimIK = animancer != null ? animancer.GetComponentInChildren<RangedAimIK>() : null;
            if (AimIK == null && owner != null)
            {
                AimIK = owner.GetComponentInChildren<RangedAimIK>();
            }
            // PlayerCursor 模式 + 收刀偵測共用 — 快取一次,避免每幀 GetComponent
            if (owner != null)
            {
                LocomotionInput = owner.GetComponent<LocomotionInputReader>();
                LocomotionController = owner.GetComponent<PlayerLocomotionController>();
            }
        }

        /// <summary>
        /// 預設投射物生成位置（使用 AttackData 的 SpawnOffset）
        /// </summary>
        public Vector3 SpawnPosition => GetSpawnPositionForEvent(null);

        /// <summary>
        /// 取得指定 FireEvent 的生成位置（內部委派給 FireDirectionSolver）
        /// </summary>
        public Vector3 GetSpawnPositionForEvent(RangedFireEvent fireEvent) => Solve(fireEvent).SpawnPosition;

        /// <summary>
        /// 預設發射方向
        /// </summary>
        public Vector3 GetFireDirection() => GetFireDirectionForEvent(null);

        /// <summary>
        /// 取得指定 FireEvent 的發射方向（內部委派給 FireDirectionSolver）
        /// </summary>
        public Vector3 GetFireDirectionForEvent(RangedFireEvent fireEvent) => Solve(fireEvent).FireDirection;

        /// <summary>
        /// 解算單發射擊事件，回傳完整 FireSolveResult。
        /// 熱路徑請改用 Solve(fireEvent, out result) 避免結構複製
        /// </summary>
        public FireSolveResult Solve(RangedFireEvent fireEvent)
        {
            Solve(fireEvent, out FireSolveResult result);
            return result;
        }

        /// <summary>
        /// 解算單發射擊事件，結果寫入 out 參數（熱路徑用）
        /// </summary>
        public void Solve(RangedFireEvent fireEvent, out FireSolveResult result)
        {
            BuildSolveContext(fireEvent, out FireSolveContext ctx, out FireEventInput input);
            FireDirectionSolver.Solve(in ctx, in input, out result);
        }

        /// <summary>
        /// 從當下狀態快照組裝 FireSolveContext + FireEventInput
        /// </summary>
        private void BuildSolveContext(RangedFireEvent fireEvent, out FireSolveContext ctx, out FireEventInput input)
        {
            string socketName = fireEvent != null
                ? fireEvent.GetEffectiveSpawnSocketName(AttackData)
                : AttackData.SpawnSocketName;
            Transform socket = ResolveSocket(socketName);
            socket.GetPositionAndRotation(out Vector3 socketPos, out Quaternion socketRot);
            ctx = new FireSolveContext
            {
                OwnerPosition = Owner.transform.position,
                OwnerRotation = Owner.transform.rotation,
                SocketPosition = socketPos,
                SocketRotation = socketRot,
                MarkedTargetMaxRange = AttackData.AutoFaceRange,
                ApplyPitchClamp = AttackData.ApplyPitchClamp,
                MaxPitchDown = AttackData.MaxPitchDown
            };
            PopulateLockedTarget(ref ctx);
            PopulateAimCamera(ref ctx);
            PopulateMarkedTarget(ref ctx);
            PopulateAutoFaceTarget(ref ctx);
            input = new FireEventInput
            {
                SpawnOffset = fireEvent != null ? fireEvent.SpawnOffset : AttackData.SpawnOffset,
                DirectionOffsetEuler = fireEvent != null ? fireEvent.DirectionOffset : Vector3.zero
            };
        }

        private void PopulateLockedTarget(ref FireSolveContext ctx)
        {
            if (LockOn == null || !LockOn.IsLocked || LockOn.CurrentTarget == null) return;
            ctx.HasLockedTarget = true;
            // 用模型中心(AimAnchor)而非 LockOnAnchor — 避免射向鎖定 UI 點而非身體中心
            ctx.LockedTargetPosition = AimAnchorResolver.ResolveAimPosition(LockOn.CurrentTarget.transform);
        }

        private void PopulateAimCamera(ref FireSolveContext ctx)
        {
            if (AimCamera == null || !AimCamera.IsAiming) return;
            ctx.HasAimCamera = true;
            ctx.AimHitPoint = AimCamera.GetAimHitPoint();
        }

        private void PopulateMarkedTarget(ref FireSolveContext ctx)
        {
            if (HitMemory == null || HitMemory.LastHitTarget == null) return;
            if (!HitMemory.LastHitTarget.gameObject.activeInHierarchy) return;
            ctx.HasMarkedTarget = true;
            // 標記目標的 Transform 通常在腳底 → 解析為模型中心
            ctx.MarkedTargetPosition = AimAnchorResolver.ResolveAimPosition(HitMemory.LastHitTarget);
        }

        private void PopulateAutoFaceTarget(ref FireSolveContext ctx)
        {
            if (!AttackData.AutoFaceTarget) return;
            // 走鎖定目標(與 AutoFaceTarget DOLookAt 一致),避免身體面向 A 卻射向 B 的視覺錯位
            Transform target = ResolveLockedAutoFaceTarget();
            if (target == null) return;
            ctx.HasAutoFaceTarget = true;
            // 自動瞄準目標的 Transform 通常在腳底 → 解析為模型中心
            ctx.AutoFaceTargetPosition = AimAnchorResolver.ResolveAimPosition(target);
        }

        /// <summary>
        /// IK look-at 沿發射線的最低投影距離 — 目標若太近,改用「spawn + fireDir * 此距離」當虛擬 look-at 點。
        /// 用意:避免 head 與 spawn 位置不同造成的近距離視差(上半身看起來偏向某側)。
        /// 距離取 8m 是近戰範圍上限的常見經驗值,可視玩家手感再調。
        /// </summary>
        private const float MIN_IK_LOOK_DISTANCE = 8f;

        /// <summary>
        /// 每幀呼叫,把 Solver 解析的目標位置推給 RangedAimIK 驅動上半身瞄準
        /// Forward 來源時清除 IK(避免角色看著虛擬點)
        /// 流程:
        /// 1. 取得發射方向 + AttackData.AimIKAngularOffset 在 body-local 套用角度旋轉
        ///    (角度量與距離無關,任何距離下視覺效果一致)
        /// 2. 從 spawn 沿調整後方向投影到 max(實際距離, MIN_IK_LOOK_DISTANCE)
        ///    讓上半身對準「發射線」而非「實際目標位置」,消除近距離視差
        /// </summary>
        public void UpdateAimIK()
        {
            if (AimIK == null) return;
            Solve(null, out FireSolveResult result);
            if (result.Source == FireDirectionSource.Forward)
            {
                AimIK.ClearAimTarget();
                return;
            }
            Vector3 baseDir = result.FireDirection;
            // 套用 body-local 角度偏移 — 所有距離下視覺角度量一致
            Vector3 angularOffset = AttackData.AimIKAngularOffset;
            if (angularOffset != Vector3.zero)
            {
                Quaternion bodyRot = Owner.transform.rotation;
                Vector3 localDir = Quaternion.Inverse(bodyRot) * baseDir;
                Vector3 adjustedLocalDir = Quaternion.Euler(angularOffset) * localDir;
                baseDir = bodyRot * adjustedLocalDir;
            }
            float distToTarget = (result.ResolvedTargetPosition - result.SpawnPosition).magnitude;
            float effectiveDist = Mathf.Max(distToTarget, MIN_IK_LOOK_DISTANCE);
            Vector3 ikTarget = result.SpawnPosition + baseDir * effectiveDist;
            AimIK.SetAimTarget(ikTarget);
        }

        /// <summary>停止 AimIK 跟隨(權重會自動淡出)</summary>
        public void StopAimIK()
        {
            AimIK?.ClearAimTarget();
        }

        /// <summary>
        /// 解析發射事件列表（構造時呼叫一次）
        /// FireEvents 有設定時直接回傳引用；否則 fallback 為單發 list
        /// </summary>
        private static List<RangedFireEvent> ResolveFireEvents(RangedAttackData attackData)
        {
            if (attackData.FireEvents != null && attackData.FireEvents.Count > 0)
            {
                return attackData.FireEvents;
            }
            return new List<RangedFireEvent>
            {
                new()
                {
                    FireTime = attackData.FireTime,
                    SpawnOffset = attackData.SpawnOffset,
                    DirectionOffset = Vector3.zero
                }
            };
        }

        /// <summary>
        /// 更新時間軸事件（VFX/SFX）
        /// </summary>
        public void UpdateTimelineEvents(float currentTime)
        {
            UpdateTimelineEvents(currentTime, TimelineEventPhase.Fire);
        }

        /// <summary>
        /// 帶 phase 篩選的版本 — 只觸發匹配 phase 的事件
        /// HoldToCharge/HoldToAim 用此版本在不同階段(ChargeStart/ChargeLoop/Fire)各自跑各自的事件
        /// </summary>
        public void UpdateTimelineEvents(float currentTime, TimelineEventPhase phase)
        {
            foreach (var evt in AttackData.TimelineEvents)
            {
                if (evt.Phase != phase) continue;
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

        /// <summary>
        /// 打包當前攻擊狀態供殘影執行器接手。
        /// 含:動畫時間、玩家 ASC、Layer 設定、已發射 FireEvents、已觸發 TimelineEvents。
        /// 殘影執行器讀此快照後從接手時間點繼續,不會重發同一發子彈,不會重觸已 fired 的特效。
        /// 僅 QuickFire 模式有意義(蓄力 / 瞄準的 charge state 無法封存,由 GA_RangedAttack.TryCaptureSnapshot 把關)
        /// </summary>
        public RangedAttackSnapshot ToSnapshot()
        {
            RangedAttackSnapshot snap = new RangedAttackSnapshot
            {
                AttackData = AttackData,
                ResumeTime = AnimState != null ? AnimState.Time : 0f,
                InstigatorOwner = Owner,
                AlreadyFiredEvents = new HashSet<RangedFireEvent>(FiredEvents),
                AlreadyTriggeredEvents = new HashSet<TimelineEvent>(_triggeredEvents),
            };

            // 抓「Transform 引用」而非當下位置 — 殘影發射瞬間會讀最新位置,敵人移動仍能命中。
            // 條件與玩家端 Populate*Target 一致,確保殘影與玩家瞄同樣目標。
            // 存 root transform(殘影執行端會用 AimAnchorResolver 解析成模型中心),而非 LockOnAnchor 鎖定點
            if (LockOn != null && LockOn.IsLocked && LockOn.CurrentTarget != null)
            {
                snap.LockedTarget = LockOn.CurrentTarget.transform;
            }
            if (HitMemory != null && HitMemory.LastHitTarget != null
                && HitMemory.LastHitTarget.gameObject.activeInHierarchy)
            {
                snap.MarkedTarget = HitMemory.LastHitTarget;
            }
            if (AttackData != null && AttackData.AutoFaceTarget)
            {
                snap.AutoFaceTarget = ResolveLockedAutoFaceTarget();
            }
            // AimCamera 跟著玩家相機跑,殘影無法存活查 → 用 snapshot 當下值(stale 但接近射出時的瞄點)
            if (AimCamera != null && AimCamera.IsAiming)
            {
                snap.AimHitPoint = AimCamera.GetAimHitPoint();
                snap.HasAimHitPoint = true;
            }
            return snap;
        }

        /// <summary>
        /// 清理運行時狀態
        /// </summary>
        public void Cleanup(bool wasCancelled)
        {
            // PlayerCursor 模式 catch-all — 任何結束路徑(正常/中斷/chain)都恢復 locomotion,避免角色卡在凍結狀態
            GA_RangedAttack.ExitCursorMode(this);
            // 時間軸事件的特效清理 — 統一交給 TimelineEventSpawner.Cleanup
            foreach (var kvp in _activeTimelineInstances)
            {
                TimelineEventSpawner.Cleanup(kvp.Value, wasCancelled);
            }
            _triggeredEvents.Clear();
            _activeTimelineInstances.Clear();
            // 預設保留 aim 鏡頭(BOTW 連射手感) — 玩家發射完仍維持瞄準狀態,
            // 想預瞄下一發就不會中間切視角。
            // 主動退出時機由各觸發點處理:
            //   - TriggerComboAttack chain 到非 aim 攻擊 → 那邊自己呼叫 ExitAim
            //   - CheckSheatheCancelByMovement → 那邊自己呼叫 ExitAim
            //   - DetectExternalAimExit (蓄力中受擊/閃避) → 那邊自己呼叫 ExitAimMode
            //   - AimCameraController auto-exit (HitStunned/Dodging/Dead tag) → 自動偵測退出
            //   - QuickFireRoutine 啟動時若殘留 aim → 自己處理退出
            // 此 Cleanup 只負責 wasCancelled 的 catch-all
            AimUI?.HideChargeRing();
            AimUI?.HideAoEIndicator();
            AimIK?.ClearAimTarget();
            // 任何收尾路徑都銷毀殘留的 AoE 預覽(避免 ability 結束後場上留下 ghost prefab)
            if (PendingAoEPreview != null)
            {
                PendingAoEPreview.CancelPreview();
                PendingAoEPreview = null;
            }
            if (wasCancelled)
            {
                AimCamera?.ExitAim();
                AimUI?.HideAll();
            }
        }

        /// <summary>
        /// 建立骨骼映射表
        /// </summary>
        private void BuildSocketMap()
        {
            _socketMap = new Dictionary<string, Transform>();
            if (Animancer == null) return;

            var animator = Animancer.GetComponent<Animator>();
            if (animator == null) return;

            // 快取所有子物件
            Transform[] allChildren = Animancer.GetComponentsInChildren<Transform>();
            foreach (Transform child in allChildren)
            {
                if (!_socketMap.ContainsKey(child.name))
                {
                    _socketMap[child.name] = child;
                }
            }
        }

        /// <summary>
        /// 解析骨骼位置
        /// </summary>
        private Transform ResolveSocket(string socketName)
        {
            if (string.IsNullOrEmpty(socketName))
            {
                return Animancer != null ? Animancer.transform : Owner.transform;
            }

            if (_socketMap != null && _socketMap.TryGetValue(socketName, out Transform socket))
            {
                if (socket != null) return socket;
            }

            // 回退：遞迴搜尋
            Transform found = Animancer != null
                ? Animancer.transform.Find(socketName)
                : Owner.transform.Find(socketName);

            return found != null ? found : (Animancer != null ? Animancer.transform : Owner.transform);
        }
    }
}
