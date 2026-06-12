using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using DG.Tweening;
using GAS.Targeting.Combat;
using GAS.Targeting.LockOnV2;

namespace GAS
{
    /// <summary>
    /// 閃避能力 - GAS 版本
    /// 從 DodgeData ScriptableObject 讀取所有配置參數
    /// </summary>
    [CreateAssetMenu(fileName = "GA_Dodge", menuName = "GAS/Abilities/Dodge")]
    public class GA_Dodge : GameplayAbility
    {
        [Header("Data")]
        [Tooltip("閃避數據設定")]
        [SerializeField] private DodgeData _dodgeData;
        public DodgeData DodgeData => _dodgeData;

        // === 舊欄位（遷移用，遷移完成後可刪除） ===
        [HideInInspector] public ClipTransition DodgeAnimation;
        [HideInInspector] public ClipTransition BackstepAnimation;
        [HideInInspector] public float DodgeDistance = 5.0f;
        [HideInInspector] public float DodgeDuration = 0.4f;
        [HideInInspector] public AnimationCurve DodgeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [HideInInspector] public float BackstepDistance = 3.0f;
        [HideInInspector] public float BackstepDuration = 0.35f;
        [HideInInspector] public AnimationCurve BackstepCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [HideInInspector] public GameplayEffect InvincibilityEffect;
        [HideInInspector] public float InvincibilityStartTime = 0f;
        [HideInInspector] public float InvincibilityDuration = 0.3f;
        [HideInInspector] public GameplayTag DodgeStartCue;
        [HideInInspector] public GameplayTag DodgeEndCue;

        public override bool CanActivateAbility(GameplayAbilitySpec spec)
        {
            if (!base.CanActivateAbility(spec)) return false;
            // 攻擊尚未到達 AllowCancelTime 時，禁止閃避
            if (spec.Owner.OwnedTags.HasTag(GameplayTags.State.AttackNonCancellable))
            {
                return false;
            }
            // 閃避尚未到達 AllowCancelTime 時，禁止再次閃避
            if (spec.Owner.OwnedTags.HasTag(GameplayTags.State.DodgeNonCancellable))
            {
                return false;
            }
            return true;
        }

        public override void ActivateAbility(GameplayAbilitySpec spec)
        {
            // 取消所有正在執行的攻擊能力
            CancelAllAttackAbilities(spec);
            // 支付消耗
            PayCost(spec);
            // 啟動閃避協程
            var coroutine = StartCoroutine(spec, DodgeRoutine(spec));
            spec.SetActiveCoroutine(coroutine);
        }

        /// <summary>
        /// 取消所有攻擊能力
        /// </summary>
        private void CancelAllAttackAbilities(GameplayAbilitySpec spec)
        {
            if (spec.Owner == null) return;
            var activeAbilities = new List<GameplayAbilitySpec>();
            foreach (var otherSpec in spec.Owner.GetAllAbilities())
            {
                if (otherSpec != spec && otherSpec.IsActive)
                {
                    activeAbilities.Add(otherSpec);
                }
            }
            foreach (var otherSpec in activeAbilities)
            {
                if (otherSpec.AbilityDef is GA_MeleeAttack ||
                    otherSpec.AbilityDef.AbilityTag.MatchesTagHierarchy(GameplayTags.Ability.Attack.Root))
                {
                    if (spec.Owner.DebugMode)
                    {
                        Debug.Log($"[GA_Dodge] Cancelling ability: {otherSpec.AbilityDef.AbilityName}");
                    }
                    otherSpec.CancelAbility();
                }
            }
            TryCancelOtherAbilities(spec);
        }

        public override void EndAbility(GameplayAbilitySpec spec, bool wasCancelled)
        {
            // 清理閃避狀態
            if (spec.CustomData is DodgeRuntimeData runtimeData)
            {
                runtimeData.Cleanup();
            }
            // 確保移除所有相關標籤
            if (spec.Owner != null)
            {
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.Invincible);
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.Dodging);
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.DodgeNonCancellable);
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.PerfectDodgeWindow);
                // 如果子彈時間仍在進行，兜底恢復
                if (spec.Owner.OwnedTags.HasTag(GameplayTags.State.PerfectDodgeBulletTime))
                {
                    spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.PerfectDodgeBulletTime);
                    spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.PerfectDodgeCounterWindow);
                    TimeScaleUtility.RestoreTimeScale();
                }
            }
            // 觸發結束 Cue
            var endCue = _dodgeData != null ? _dodgeData.DodgeEndCue : DodgeEndCue;
            if (endCue.IsValid && spec.Owner != null)
            {
                ExecuteGameplayCue(spec, endCue, spec.Owner.transform.position);
            }
            base.EndAbility(spec, wasCancelled);
        }

        /// <summary>
        /// 閃避主協程
        /// </summary>
        private IEnumerator DodgeRoutine(GameplayAbilitySpec spec)
        {
            var owner = spec.Owner;
            var playerController = owner.GetComponent<NewGASPlayerController>();
            var animancer = playerController != null ? playerController.Animancer : null;
            if (animancer == null)
            {
                animancer = owner.GetComponentInChildren<AnimancerComponent>();
            }
            var cc = owner.GetComponent<CharacterController>();
            // 判斷是否有輸入方向
            bool hasInput = HasMoveInput(owner);
            bool isBackstep = !hasInput;
            // 取得參數（優先從 DodgeData，若無則 fallback 舊欄位）
            ClipTransition animToPlay;
            float distance;
            float moveDuration;
            AnimationCurve curve;
            GameplayTag startCue;
            float invStartTime;
            float invDuration;
            GameplayEffect invEffect;
            List<TimelineEvent> timelineEvents;
            float allowInputTime;
            float allowCancelTime;
            float sheatheCancelTime;
            bool useRootMotion;
            if (_dodgeData != null)
            {
                animToPlay = _dodgeData.GetClipTransition(isBackstep);
                distance = _dodgeData.GetDistance(isBackstep);
                moveDuration = _dodgeData.GetDuration(isBackstep);
                curve = _dodgeData.GetCurve(isBackstep);
                startCue = _dodgeData.DodgeStartCue;
                invStartTime = _dodgeData.InvincibilityStartTime;
                invDuration = _dodgeData.InvincibilityDuration;
                invEffect = _dodgeData.InvincibilityEffect;
                timelineEvents = _dodgeData.GetTimelineEvents(isBackstep);
                allowInputTime = _dodgeData.AllowInputTime;
                allowCancelTime = _dodgeData.AllowCancelTime;
                sheatheCancelTime = _dodgeData.SheatheCancelTime;
                useRootMotion = _dodgeData.UseRootMotion;
            }
            else
            {
                animToPlay = (hasInput || BackstepAnimation == null) ? DodgeAnimation : BackstepAnimation;
                distance = hasInput ? DodgeDistance : BackstepDistance;
                moveDuration = hasInput ? DodgeDuration : BackstepDuration;
                curve = hasInput ? DodgeCurve : BackstepCurve;
                startCue = DodgeStartCue;
                invStartTime = InvincibilityStartTime;
                invDuration = InvincibilityDuration;
                invEffect = InvincibilityEffect;
                timelineEvents = null;
                allowInputTime = 0f;
                allowCancelTime = 0f;
                sheatheCancelTime = -1f;
                useRootMotion = false;
            }
            if (animancer == null || animToPlay == null)
            {
                Debug.LogError("[GA_Dodge] Missing AnimancerComponent or DodgeAnimation!");
                spec.EndAbility();
                yield break;
            }
            // 動畫長度決定能力總時長
            float animDuration = animToPlay.Clip != null ? animToPlay.Clip.length : moveDuration;
            // 計算角色縮放係數
            float scaleFactor = SpatialScaleUtility.GetScaleFactor(
                animancer != null ? animancer.transform : owner.transform);
            // 創建運行時數據
            var runtimeData = new DodgeRuntimeData(owner, cc);
            spec.CustomData = runtimeData;
            // 計算閃避方向
            Vector3 dodgeDirection = CalculateDodgeDirection(owner, hasInput);
            if (hasInput)
            {
                owner.transform.rotation = Quaternion.LookRotation(dodgeDirection, Vector3.up);
            }
            // 播放動畫
            var animState = animancer.Play(animToPlay);
            animState.Time = 0;
            // 觸發開始 Cue
            if (startCue.IsValid)
            {
                ExecuteGameplayCue(spec, startCue, owner.transform.position);
            }
            // 開始位移 — RM 模式:由 Clip 的 Root Motion 透過 NewGASPlayerController.OnRootMotionUpdate 驅動,無需 DOTween
            //           IP 模式:用 DOTween 依 Distance / Duration / Curve 推進 CharacterController
            if (!useRootMotion)
            {
                Vector3 targetPos = owner.transform.position + dodgeDirection * distance * scaleFactor;
                runtimeData.StartMovement(targetPos, moveDuration, curve);
            }
            // 處理無敵（含完美閃避偵測窗口）
            var perfectDodgeData = _dodgeData != null ? _dodgeData.PerfectDodgeData : null;
            if (invEffect != null || invDuration > 0f)
            {
                owner.StartCoroutine(HandleInvincibility(spec, invStartTime, invDuration, invEffect,
                    perfectDodgeData != null));
            }
            // 生成殘留影分身
            var damageReceiver = owner.GetComponent<GASDamageReceiver>();
            if (perfectDodgeData != null && perfectDodgeData.GhostDuration > 0f && damageReceiver != null)
            {
                PerfectDodgeGhost.Spawn(
                    owner.transform.position,
                    perfectDodgeData.GhostRadius * scaleFactor,
                    perfectDodgeData.GhostDuration,
                    owner,
                    damageReceiver);
            }
            // 訂閱完美閃避事件（本體 + Ghost 都會觸發此事件）
            if (perfectDodgeData != null && damageReceiver != null)
            {
                runtimeData.SubscribePerfectDodge(damageReceiver, (hitCtx) =>
                {
                    OnPerfectDodgeTriggered(spec, runtimeData, perfectDodgeData, hitCtx);
                });
            }
            // 取消鎖定：閃避開始時加入不可取消標籤
            bool isCancelLocked = allowCancelTime > 0f;
            if (isCancelLocked)
            {
                owner.OwnedTags.AddTag(GameplayTags.State.DodgeNonCancellable);
            }
            // 時間軸事件追蹤
            var triggeredEvents = new HashSet<TimelineEvent>();
            // 主迴圈：逐幀更新，直到動畫播完或被取消
            float stateTimer = 0f;
            while (stateTimer < animDuration && spec.IsActive)
            {
                stateTimer += Time.deltaTime;
                // 到達 AllowCancelTime 後解除取消鎖定
                if (isCancelLocked && stateTimer >= allowCancelTime)
                {
                    isCancelLocked = false;
                    owner.OwnedTags.RemoveTag(GameplayTags.State.DodgeNonCancellable);
                }
                // 到達 AllowInputTime 後允許輸入下一個動作（完美閃避時立即允許）
                if (stateTimer >= allowInputTime || runtimeData.HasTriggeredPerfectDodge)
                {
                    if (CheckDodgeInput(spec))
                    {
                        yield break;
                    }
                }
                // 收刀取消：超過收刀時間且有移動輸入時，結束閃避
                if (CheckSheatheCancelByMovement(spec, sheatheCancelTime, stateTimer))
                {
                    yield break;
                }
                // 處理時間軸事件
                if (timelineEvents != null)
                {
                    foreach (var evt in timelineEvents)
                    {
                        if (!triggeredEvents.Contains(evt) && stateTimer >= evt.TriggerTime)
                        {
                            TriggerTimelineEvent(spec, evt, scaleFactor);
                            triggeredEvents.Add(evt);
                        }
                    }
                }
                yield return null;
            }
            spec.EndAbility();
        }

        /// <summary>
        /// 檢查閃避輸入（攻擊、再次閃避等）
        /// </summary>
        private bool CheckDodgeInput(GameplayAbilitySpec spec)
        {
            var inputHandler = spec.Owner.GetComponent<AbilityInputHandler>();
            if (inputHandler == null || !inputHandler.HasInput()) return false;
            // 有攻擊輸入時，結束閃避並直接觸發攻擊
            var peeked = inputHandler.PeekInput();
            if (peeked == MeleeInputType.LightAttack || peeked == MeleeInputType.HeavyAttack)
            {
                inputHandler.ConsumeInput();
                var abilityTag = peeked == MeleeInputType.LightAttack
                    ? GameplayTags.Ability.Attack.Light
                    : GameplayTags.Ability.Attack.Heavy;
                spec.EndAbility();
                spec.Owner.TryActivateAbility(abilityTag);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 收刀取消：超過收刀時間且有移動輸入（無攻擊輸入）時取消閃避
        /// </summary>
        private bool CheckSheatheCancelByMovement(GameplayAbilitySpec spec, float sheatheCancelTime, float currentTime)
        {
            if (sheatheCancelTime < 0f) return false;
            if (currentTime < sheatheCancelTime) return false;
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
            var locomotionReader = spec.Owner.GetComponent<Player.Locomotion.LocomotionInputReader>();
            if (locomotionReader == null) return false;
            bool hasMovement = locomotionReader.RawMove.magnitude > 0.1f;
            bool hasJump = locomotionReader.JumpPressedThisFrame;
            if (!hasMovement && !hasJump) return false;
            spec.EndAbility();
            return true;
        }

        private void TriggerTimelineEvent(GameplayAbilitySpec spec, TimelineEvent evt, float scaleFactor)
        {
            if (!evt.CueTag.IsValid) return;
            Transform socket = ResolveSocket(spec.Owner, evt.SocketName);
            socket.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);
            pos += rot * (evt.PositionOffset * scaleFactor);
            rot *= Quaternion.Euler(evt.RotationOffset);
            var parameters = new GameplayCueParameters
            {
                Location = pos,
                Rotation = rot,
                Scale = evt.IsAttached ? evt.Scale : evt.Scale * scaleFactor,
                TargetObject = evt.IsAttached ? socket.gameObject : null,
                Instigator = spec.Owner
            };
            var cueManager = GameplayCueManager.Instance;
            if (cueManager != null)
            {
                cueManager.ExecuteCue(evt.CueTag, parameters);
            }
        }

        private Transform ResolveSocket(AbilitySystemComponent owner, string socketName)
        {
            AnimancerComponent animancer = owner.GetComponentInChildren<AnimancerComponent>();
            Transform searchRoot = animancer != null ? animancer.transform : owner.transform;
            if (string.IsNullOrEmpty(socketName)) return searchRoot;
            Transform found = FindChildRecursive(searchRoot, socketName);
            return found != null ? found : searchRoot;
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent.name == childName) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform result = FindChildRecursive(parent.GetChild(i), childName);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// 判斷玩家是否有移動輸入
        /// </summary>
        private bool HasMoveInput(AbilitySystemComponent owner)
        {
            var playerCtrl = owner.GetComponent<NewGASPlayerController>();
            Vector2 moveInput = playerCtrl != null ? playerCtrl.MoveInput : Vector2.zero;
            return moveInput.magnitude > 0.1f;
        }

        /// <summary>
        /// 計算閃避方向
        /// </summary>
        private Vector3 CalculateDodgeDirection(AbilitySystemComponent owner, bool hasInput)
        {
            var playerCtrl = owner.GetComponent<NewGASPlayerController>();
            Vector3 dodgeDir;
            if (hasInput)
            {
                Vector2 moveInput = playerCtrl != null ? playerCtrl.MoveInput : Vector2.zero;
                Vector3 inputDir = new(moveInput.x, 0, moveInput.y);
                Transform camTf = playerCtrl != null ? playerCtrl.CameraTransform : (Camera.main != null ? Camera.main.transform : null);
                dodgeDir = camTf != null
                    ? Quaternion.Euler(0, camTf.eulerAngles.y, 0) * inputDir
                    : inputDir;
            }
            else
            {
                dodgeDir = -owner.transform.forward;
            }
            dodgeDir.y = 0;
            dodgeDir.Normalize();
            return dodgeDir;
        }

        /// <summary>
        /// 處理無敵幀（含完美閃避窗口標籤）
        /// </summary>
        private IEnumerator HandleInvincibility(GameplayAbilitySpec spec, float startDelay, float duration,
            GameplayEffect invEffect, bool enablePerfectDodgeWindow)
        {
            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }
            if (!spec.IsActive) yield break;
            // 添加無敵標籤
            spec.Owner.OwnedTags.AddTag(GameplayTags.State.Invincible);
            // 如果啟用完美閃避，同時添加偵測窗口標籤
            if (enablePerfectDodgeWindow)
            {
                spec.Owner.OwnedTags.AddTag(GameplayTags.State.PerfectDodgeWindow);
            }
            // 如果有無敵效果，應用它
            if (invEffect != null)
            {
                ApplyEffectToSelf(spec, invEffect);
            }
            // 等待無敵持續時間
            yield return new WaitForSeconds(duration);
            // 移除標籤
            if (spec.Owner != null)
            {
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.Invincible);
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.PerfectDodgeWindow);
            }
        }

        /// <summary>
        /// 完美閃避觸發回應
        /// </summary>
        private void OnPerfectDodgeTriggered(GameplayAbilitySpec spec, DodgeRuntimeData runtimeData,
            PerfectDodgeData data, HitContext hitCtx)
        {
            // 防止同一次閃避中多次觸發
            if (runtimeData.HasTriggeredPerfectDodge) return;
            float now = Time.realtimeSinceStartup;
            if (now - DodgeRuntimeData.LastPerfectDodgeRealTime < data.MinInterval) return;
            runtimeData.HasTriggeredPerfectDodge = true;
            DodgeRuntimeData.LastPerfectDodgeRealTime = now;
            // 立即解除取消鎖定，讓玩家可以馬上攻擊
            if (spec.Owner != null)
            {
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.DodgeNonCancellable);
            }
            // 觸發完美閃避 Cue（VFX 閃光 + SFX + 頓幀）
            if (data.PerfectDodgeCue.IsValid && spec.Owner != null)
            {
                ExecuteGameplayCue(spec, data.PerfectDodgeCue, spec.Owner.transform.position);
            }
            // 啟動子彈時間協程
            if (spec.Owner != null)
            {
                spec.Owner.StartCoroutine(PerfectDodgeBulletTimeRoutine(spec, data));
            }
        }

        /// <summary>
        /// 完美閃避子彈時間協程
        /// </summary>
        private IEnumerator PerfectDodgeBulletTimeRoutine(GameplayAbilitySpec spec, PerfectDodgeData data)
        {
            var owner = spec.Owner;
            if (owner == null) yield break;
            // 添加子彈時間 + 反擊窗口標籤
            owner.OwnedTags.AddTag(GameplayTags.State.PerfectDodgeBulletTime);
            owner.OwnedTags.AddTag(GameplayTags.State.PerfectDodgeCounterWindow);
            // 應用反擊傷害加成效果
            if (data.CounterDamageBonusEffect != null)
            {
                ApplyEffectToSelf(spec, data.CounterDamageBonusEffect);
            }
            // 自動鎖定最近敵人
            AutoTargetNearestEnemy(owner, data.AutoTargetRange);
            // 平滑進入子彈時間
            yield return TimeScaleUtility.SmoothTimeScale(
                Time.timeScale, data.BulletTimeScale, data.BulletTimeEnterDuration);
            // 子彈時間持續（使用 realtime 計時）
            float startRealTime = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startRealTime < data.BulletTimeDuration)
            {
                // 玩家發動攻擊時提前結束子彈時間
                if (owner == null) yield break;
                if (owner.OwnedTags.HasTag(GameplayTags.State.Attacking))
                {
                    break;
                }
                yield return null;
            }
            if (owner == null) yield break;
            // 移除反擊窗口
            owner.OwnedTags.RemoveTag(GameplayTags.State.PerfectDodgeCounterWindow);
            // 移除傷害加成效果
            if (data.CounterDamageBonusEffect != null && data.CounterDamageBonusEffect.EffectTag.IsValid)
            {
                owner.RemoveEffectsWithTag(data.CounterDamageBonusEffect.EffectTag);
            }
            // 平滑退出子彈時間
            yield return TimeScaleUtility.SmoothTimeScale(
                Time.timeScale, 1f, data.BulletTimeExitDuration);
            // 移除子彈時間標籤
            if (owner != null)
            {
                owner.OwnedTags.RemoveTag(GameplayTags.State.PerfectDodgeBulletTime);
            }
            // 觸發結束 Cue
            if (data.BulletTimeEndCue.IsValid && owner != null)
            {
                owner.ExecuteGameplayCue(data.BulletTimeEndCue, owner.transform.position);
            }
        }

        /// <summary>
        /// 自動鎖定最近敵人
        /// </summary>
        private void AutoTargetNearestEnemy(AbilitySystemComponent owner, float range)
        {
            LockOnController lockOn = owner.GetComponent<LockOnController>();
            CombatTargetFinder finder = owner.GetComponent<CombatTargetFinder>();
            if (lockOn == null || finder == null) return;
            if (lockOn.IsLocked) return;
            Transform bestTarget = finder.FindBestTarget(
                owner.transform.position,
                owner.transform.forward,
                range,
                360f);
            if (bestTarget == null) return;
            LockOnTarget lockTarget = bestTarget.GetComponentInParent<LockOnTarget>();
            if (lockTarget != null)
            {
                lockOn.Lock(lockTarget);
            }
        }
    }

    /// <summary>
    /// 閃避運行時數據
    /// </summary>
    public class DodgeRuntimeData
    {
        public AbilitySystemComponent Owner { get; private set; }
        public CharacterController CharacterController { get; private set; }

        // 完美閃避狀態
        public bool HasTriggeredPerfectDodge;
        // 使用 static 確保跨閃避的冷卻間隔生效
        public static float LastPerfectDodgeRealTime;

        private Tween _moveTween;
        private Action<HitContext> _perfectDodgeHandler;
        private GASDamageReceiver _subscribedReceiver;

        public DodgeRuntimeData(AbilitySystemComponent owner, CharacterController cc)
        {
            Owner = owner;
            CharacterController = cc;
        }

        public void StartMovement(Vector3 targetPos, float duration, AnimationCurve curve)
        {
            if (CharacterController == null) return;
            Vector3 startPos = Owner.transform.position;
            _moveTween = DOTween.To(() => 0f, x =>
            {
                Vector3 nextPos = Vector3.Lerp(startPos, targetPos, x);
                Vector3 delta = nextPos - Owner.transform.position;
                CharacterController.Move(delta);
            }, 1f, duration)
            .SetEase(curve);
        }

        /// <summary>
        /// 訂閱完美閃避事件
        /// </summary>
        public void SubscribePerfectDodge(GASDamageReceiver receiver, Action<HitContext> handler)
        {
            _subscribedReceiver = receiver;
            _perfectDodgeHandler = handler;
            receiver.OnPerfectDodge += _perfectDodgeHandler;
        }

        /// <summary>
        /// 取消訂閱完美閃避事件
        /// </summary>
        private void UnsubscribePerfectDodge()
        {
            if (_perfectDodgeHandler != null && _subscribedReceiver != null)
            {
                _subscribedReceiver.OnPerfectDodge -= _perfectDodgeHandler;
                _perfectDodgeHandler = null;
                _subscribedReceiver = null;
            }
        }

        public void Cleanup()
        {
            UnsubscribePerfectDodge();
            if (_moveTween != null && _moveTween.IsActive())
            {
                _moveTween.Kill();
                _moveTween = null;
            }
        }
    }
}
