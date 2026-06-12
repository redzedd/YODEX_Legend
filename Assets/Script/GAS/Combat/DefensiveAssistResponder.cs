using System.Collections;
using Animancer;
using CameraSystem;
using DG.Tweening;
using Enemy.AttackSystem;
using Unity.Cinemachine;
using UnityEngine;

namespace GAS.Combat
{
    /// <summary>
    /// 招架支援響應器。
    /// 訂閱 WeaponManager.OnParryAssistTriggered，執行招架完整序列：
    /// 1. 玩家面向敵人 + 換武器
    /// 2. 瞬移到 HitStart 預測位置前方
    /// 3. Start 動畫 → 等待 HitWindowOpen 或 timeout
    /// 4. 接刀：頓幀 + 鏡頭切換 + 抖動 + 火花 + 擊退；空揮：正常播 End
    /// 5. 退出 Ability，Locomotion 接管
    /// 接刀鏡頭由 CameraDirector 中控（ID=Parry, Layer=Action）— 本元件透過 ticket 切換，不直接設 Priority。
    /// </summary>
    [RequireComponent(typeof(WeaponManager))]
    public class DefensiveAssistResponder : MonoBehaviour
    {
        // ────── Inspector 設定 ──────
        [Header("元件引用")]

        [SerializeField]
        [Tooltip("WeaponManager。留空會在 Awake 自動抓取")]
        private WeaponManager _weaponManager;

        [SerializeField]
        [Tooltip("NewGASPlayerController。留空會在 Awake 自動抓取。\n用來進出 Ability 狀態，禁用招架期間的移動 / 旋轉 / 攻擊輸入")]
        private NewGASPlayerController _playerController;

        [Header("反擊行為")]

        [SerializeField]
        [Tooltip("觸發招架時是否自動切到下一把武器")]
        private bool _switchWeaponOnParry = true;

        [SerializeField]
        [Tooltip("觸發招架時是否讓玩家瞬間面向敵人")]
        private bool _faceTargetOnParry = true;

        [SerializeField]
        [Tooltip("面向敵人的旋轉時間（秒）。建議 0.05~0.15 取得「瞬轉」感")]
        private float _faceTargetDuration = 0.08f;

        [Header("瞬移到敵人面前")]

        [SerializeField]
        [Tooltip("玩家停在敵人面前的「邊緣到邊緣」距離（公尺）— 從敵人 CharacterController 邊緣量到玩家 CharacterController 邊緣，已自動包含雙方縮放\n所以即使敵人或玩家放大縮小，這個距離都會維持一致，不會因為縮小而格擋不到。建議 0.2~0.6 公尺")]
        private float _dashOffset = 0.4f;

        [Header("等待接刀時長")]

        [SerializeField]
        [Tooltip("Start 動畫結束後，舉著刀等待接刀的最長秒數。\n超過此秒數仍未接到 → 自動進入 End 收勢（招架空揮）。建議 0.8~1.5 秒")]
        private float _maxWaitDuration = 1.2f;

        [Header("接刀凍結 / 擊退")]

        [SerializeField]
        [Tooltip("接刀後 End 第一幀的凍結時長（秒）。期間玩家保持 End 第一幀姿態 + 同步進行擊退位移。建議 0.3~0.6 秒")]
        private float _parryHoldDuration = 0.4f;

        [SerializeField]
        [Tooltip("接刀後玩家被擊退的距離（公尺，沿玩家面向反方向 = 遠離敵人）。\n0 = 不擊退。位移時長 = ParryHoldDuration。建議 1~2 公尺")]
        private float _knockbackDistance = 1.5f;

        [Header("End 動畫取消")]

        [SerializeField]
        [Tooltip("End 動畫播放期間,玩家移動輸入大於此值時立刻取消 End,把控制權交還 Locomotion。\n攻擊/迴避輸入則由各自的能力直接接管(End 期間不再阻擋)。\n0 = 任何輸入都取消;1 = 永遠不取消(End 必定播完)。建議 0.1~0.2")]
        private float _endCancelMoveThreshold = 0.1f;

        [Header("接刀頓幀")]

        [SerializeField]
        [Tooltip("接刀那刻 Time.timeScale 變慢的實時間長度。建議 0.08~0.15 秒，0 = 不頓幀")]
        private float _hitStopDuration = 0.1f;

        [SerializeField]
        [Tooltip("頓幀時 Time.timeScale 值。建議 0.03~0.1（越小越凍結感），1 = 不頓幀")]
        private float _hitStopTimeScale = 0.05f;

        [Header("接刀震動")]

        [SerializeField]
        [Tooltip("接刀時觸發震動的 CinemachineImpulseSource。\n建議：玩家身上加一個 ImpulseSource，在它身上設定 Impulse Definition 的振幅/時長。\n留空不震動")]
        private CinemachineImpulseSource _impulseSource;

        [Header("接刀火花")]

        [SerializeField]
        [Tooltip("接刀瞬間生成的火花特效 Prefab（建議 ParticleSystem）")]
        private GameObject _sparkVFXPrefab;

        [SerializeField]
        [Tooltip("火花生成位置的高度偏移（玩家-敵人連線中點向上 Y）。建議 1~1.5 公尺對應角色腰部")]
        private float _sparkSpawnHeight = 1.2f;

        [SerializeField]
        [Tooltip("火花自動銷毀延遲（秒，0 = 依 ParticleSystem 自己決定）。建議 1~2 秒")]
        private float _sparkAutoDestroyDelay = 1.5f;

        [SerializeField]
        [Tooltip("勾選後自動把火花內所有 ParticleSystem 設為 UnscaledTime，頓幀期間繼續飛")]
        private bool _sparkUseUnscaledTime = true;

        [Header("Debug")]

        [SerializeField]
        [Tooltip("勾選後印出招架事件 log")]
        private bool _debugMode = true;

        // ────── 私有狀態 ──────
        private Tween _faceTween;
        private CharacterController _characterController;
        private Coroutine _knockbackRoutine;
        private Coroutine _parryRoutine;
        private Coroutine _hitStopRoutine;
        private Coroutine _staggerRoutine;
        private CameraTicket _parryTicket;
        private AbilitySystemComponent _asc;
        // Invincible tag 是否已加 — 用 field 追蹤而非 coroutine local bool，
        // 避免 OnDisable / StopCoroutine 強制中止時 finally 不執行造成 tag 殘留
        private bool _hasInvincibleTag;
        // Parrying tag 是否已加 — 阻擋招架期間任何能力啟動（攻擊/迴避/能力等）
        // 沒這個 tag 玩家可在 ParryEnd 期間插攻擊，導致 finally 強制 ExitAbility 時還有活躍能力 → spec.IsActive 卡死
        private bool _hasParryingTag;

        // ────── Unity 生命週期 ──────
        private void Awake()
        {
            if (_weaponManager == null)
            {
                _weaponManager = GetComponent<WeaponManager>();
            }
            if (_playerController == null)
            {
                _playerController = GetComponent<NewGASPlayerController>();
            }
            // 抓 CharacterController 給擊退用 — 直接設 transform.position 會被 controller 內部狀態覆蓋
            _characterController = GetComponent<CharacterController>();
            // 招架期間需要 Invincible tag 讓 GASDamageReceiver 把 wasBlocked = true、跳過扣血與命中特效
            _asc = GetComponent<AbilitySystemComponent>();
            // CinemachineBrain.IgnoreTimeScale 與 ImpulseManager.IgnoreTimeScale 由 CameraDirector 統一管理
        }

        private void OnEnable()
        {
            if (_weaponManager != null)
            {
                _weaponManager.OnParryAssistTriggered += HandleParryAssistTriggered;
            }
        }

        private void OnDisable()
        {
            if (_weaponManager != null)
            {
                _weaponManager.OnParryAssistTriggered -= HandleParryAssistTriggered;
            }
            if (_parryRoutine != null)
            {
                StopCoroutine(_parryRoutine);
                _parryRoutine = null;
                RemoveInvincibleTagIfHeld();
                RemoveParryingTagIfHeld();
                ExitAbility();
            }
            if (_hitStopRoutine != null)
            {
                StopCoroutine(_hitStopRoutine);
                _hitStopRoutine = null;
                Time.timeScale = 1f;
            }
            RevertParryCamera();
            if (_knockbackRoutine != null)
            {
                StopCoroutine(_knockbackRoutine);
                _knockbackRoutine = null;
            }
            if (_staggerRoutine != null)
            {
                StopCoroutine(_staggerRoutine);
                _staggerRoutine = null;
            }
        }

        // ────── 事件處理 ──────
        private void HandleParryAssistTriggered(EnemyAttackExecutor target)
        {
            if (target == null)
            {
                return;
            }
            if (_debugMode)
            {
                Debug.Log($"[招架響應器] 接收事件，目標：{target.name}", this);
            }
            if (_faceTargetOnParry)
            {
                FaceTarget(target);
            }
            if (_switchWeaponOnParry)
            {
                SwitchWeapon();
            }
            if (_parryRoutine != null)
            {
                StopCoroutine(_parryRoutine);
            }
            _parryRoutine = StartCoroutine(ParrySequence(target));
        }

        // ────── 動作執行 ──────
        // 玩家朝向 = 敵人 forward 的反方向，正對敵人攻擊面（不論玩家原本在哪都統一朝向）
        private void FaceTarget(EnemyAttackExecutor target)
        {
            Vector3 enemyForward = GetEnemyForward(target);
            Quaternion targetRot = Quaternion.LookRotation(-enemyForward);
            if (_faceTween != null && _faceTween.IsActive())
            {
                _faceTween.Kill();
            }
            _faceTween = transform
                .DORotateQuaternion(targetRot, _faceTargetDuration)
                .SetLink(gameObject);
        }

        // 取得敵人「當下」面向（即時 forward，已排除 Y 軸）— 用即時值而非 AttackStartForward
        // 玩家繞圈時敵人 AI 會持續轉身面向玩家，落點要跟著轉才不會打空
        private static Vector3 GetEnemyForward(EnemyAttackExecutor target)
        {
            Vector3 fwd = target.transform.forward;
            fwd.y = 0f;
            return fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
        }

        private void SwitchWeapon()
        {
            if (_weaponManager.WeaponCount <= 1)
            {
                return;
            }
            // 用 SwitchToIndex 而非 SwitchToNext，避開招架攔截造成的無限遞迴
            int nextIndex = (_weaponManager.CurrentIndex + 1) % _weaponManager.WeaponCount;
            _weaponManager.SwitchToIndex(nextIndex);
        }

        // ────── 招架序列（事件驅動） ──────
        private IEnumerator ParrySequence(EnemyAttackExecutor target)
        {
            WeaponData weapon = _weaponManager.CurrentWeapon;
            AnimancerComponent animancer = GetCurrentAnimancer();
            if (weapon == null || animancer == null)
            {
                if (_debugMode)
                {
                    Debug.LogWarning($"[招架響應器] ParrySequence 提前結束 — weapon={(weapon != null ? weapon.WeaponName : "null")}, animancer={(animancer != null ? "OK" : "null")}, modelInstance={(_weaponManager.CurrentModelInstance != null ? _weaponManager.CurrentModelInstance.name : "null")}", this);
                }
                _parryRoutine = null;
                yield break;
            }
            bool hitTriggered = false;
            bool attackEnded = false;
            // OnHitConfirmed = 敵人 hitbox 真的打到某個 collider 才觸發（不是 HitStart 時間到就觸發）
            // 玩家有招架時才在這裡凍住敵人動畫（讓頓幀時武器停在命中位置）；無招架時敵人攻擊正常播完
            System.Action<EnemyAttackExecutor, EnemyAttackProfile, GameObject> onHit = (e, p, g) =>
            {
                hitTriggered = true;
                e.FreezeAnimation();
            };
            System.Action<EnemyAttackExecutor, EnemyAttackProfile> onEnded = (e, p) => attackEnded = true;
            EnterAbility();
            // 招架期間玩家無敵 — GASDamageReceiver 看到 Invincible tag 會把 wasBlocked = true，
            // 進而跳過扣血、跳過 EnemyController.SpawnHitVfx 的全身命中特效
            AddInvincibleTag();
            // 招架期間阻擋任何能力啟動 — GameplayAbility.CheckTagRequirements 看到 Parrying tag 直接 return false
            // 防止玩家在 ParryEnd 期間插攻擊造成 spec.IsActive 卡死
            AddParryingTag();
            try
            {
                target.OnHitConfirmed += onHit;
                target.OnAttackEnd += onEnded;
                target.OnAttackCanceled += onEnded;
                try
                {
                    TeleportToTarget(target);
                    if (IsValidClip(weapon.ParryStartAnimation))
                    {
                        AnimancerState startState = animancer.Play(weapon.ParryStartAnimation);
                        if (_debugMode)
                        {
                            Debug.Log($"[招架響應器] 播放 Start（{startState.Length:F2}s）", this);
                        }
                        float startTotal = startState.Length * Mathf.Max(0f, 1f - startState.NormalizedTime);
                        float startElapsed = 0f;
                        while (startElapsed < startTotal && !hitTriggered && !attackEnded)
                        {
                            startElapsed += Time.deltaTime;
                            yield return null;
                        }
                    }
                    if (!hitTriggered && !attackEnded)
                    {
                        if (_debugMode)
                        {
                            Debug.Log($"[招架響應器] 舉刀等待接刀（最多 {_maxWaitDuration:F2}s）", this);
                        }
                        float waitElapsed = 0f;
                        while (waitElapsed < _maxWaitDuration && !hitTriggered && !attackEnded)
                        {
                            waitElapsed += Time.deltaTime;
                            yield return null;
                        }
                    }
                }
                finally
                {
                    target.OnHitConfirmed -= onHit;
                    target.OnAttackEnd -= onEnded;
                    target.OnAttackCanceled -= onEnded;
                }
                if (hitTriggered)
                {
                    // 在 Cancel 前先紀錄 profile（之後 DelayedStagger 還會用到）
                    EnemyAttackProfile attackedProfile = target.CurrentProfile;
                    // 接刀瞬間立刻 Cancel：停止 coroutine + 廣播 OnAttackCanceled → 黃光跟著熄滅
                    // 兩種模式一致，避免彈刀模式延遲到頓幀結束才熄黃光
                    target.Cancel();
                    if (_debugMode)
                    {
                        Debug.Log("[招架響應器] ★ 接刀成功！", this);
                    }
                    yield return PlayParryHold(animancer, weapon.ParryEndAnimation, target, attackedProfile);
                }
                else
                {
                    if (_debugMode)
                    {
                        Debug.Log(attackEnded
                            ? "[招架響應器] 招架空揮（敵人攻擊已結束）— 播完 End 動畫或玩家移動時還回控制權"
                            : "[招架響應器] 招架空揮（等待 timeout）— 播完 End 動畫或玩家移動時還回控制權", this);
                    }
                    yield return PlayParryEndAndWait(animancer, weapon.ParryEndAnimation);
                }
            }
            finally
            {
                RemoveInvincibleTagIfHeld();
                RemoveParryingTagIfHeld();
                // End 期間若玩家啟動了新能力(攻擊/迴避),留 TopState=Ability 讓新能力的 OnAbilityEnded 自然交回 Locomotion
                // 直接 ExitAbility 會 ResumeLocomotionToIdle → ForceChangeState(Idle) 把新能力的動畫蓋掉
                if (!HasOtherActiveAbility())
                {
                    ExitAbility();
                }
                else if (_debugMode)
                {
                    Debug.Log("[招架響應器] 偵測到活躍能力 → 跳過 ExitAbility，由該能力結束時交回 Locomotion", this);
                }
                // 鏡頭 revert 由 HitStopRoutine 結束時處理，這裡只做保險
                RevertParryCamera();
            }
            _parryRoutine = null;
        }

        private void AddInvincibleTag()
        {
            if (_asc == null || _hasInvincibleTag) return;
            _asc.OwnedTags.AddTag(GameplayTags.State.Invincible);
            _hasInvincibleTag = true;
        }

        private void RemoveInvincibleTagIfHeld()
        {
            if (!_hasInvincibleTag || _asc == null) return;
            _asc.OwnedTags.RemoveTag(GameplayTags.State.Invincible);
            _hasInvincibleTag = false;
        }

        private void AddParryingTag()
        {
            if (_asc == null || _hasParryingTag) return;
            _asc.OwnedTags.AddTag(GameplayTags.State.Parrying);
            _hasParryingTag = true;
        }

        private void RemoveParryingTagIfHeld()
        {
            if (!_hasParryingTag || _asc == null) return;
            _asc.OwnedTags.RemoveTag(GameplayTags.State.Parrying);
            _hasParryingTag = false;
        }

        /// <summary>
        /// 接刀的 End 動畫播放 + 依攻擊 Profile 分支：
        /// • IsParryStaggers = false（不彈刀）：立刻 Cancel + 擊退、敵人動畫繼續播完
        /// • IsParryStaggers = true（會彈刀）：不擊退、用實時間等頓幀結束、切換敵人 Stagger 動畫
        /// </summary>
        private IEnumerator PlayParryHold(AnimancerComponent animancer, ClipTransition clip, EnemyAttackExecutor target, EnemyAttackProfile attackProfile)
        {
            if (!IsValidClip(clip))
            {
                if (_debugMode)
                {
                    Debug.Log("[招架響應器] End 動畫未設定，跳過接刀動作", this);
                }
                yield break;
            }
            TriggerParryEffects(target);
            AnimancerState state = animancer.Play(clip.Clip, 0f);
            state.Time = 0f;
            state.Speed = 0f;
            bool willStagger = attackProfile != null
                && attackProfile.IsParryStaggers
                && IsValidClip(attackProfile.ParryStaggerAnimation);
            if (willStagger)
            {
                if (_debugMode)
                {
                    Debug.Log($"[招架響應器] 彈刀模式 — 玩家不擊退、頓幀後敵人切 Stagger 動畫", this);
                }
                if (_staggerRoutine != null)
                {
                    StopCoroutine(_staggerRoutine);
                }
                _staggerRoutine = StartCoroutine(DelayedStagger(target, attackProfile.ParryStaggerAnimation));
            }
            else
            {
                if (_debugMode)
                {
                    Debug.Log($"[招架響應器] 不彈刀模式 — 玩家擊退 {_knockbackDistance:F2}m、敵人動畫繼續", this);
                }
                StartKnockback();
                // 命中時敵人動畫已被凍住 → 頓幀結束後恢復 1x 讓動畫播完
                if (_staggerRoutine != null)
                {
                    StopCoroutine(_staggerRoutine);
                }
                _staggerRoutine = StartCoroutine(DelayedResumeAnimation(target));
            }
            yield return new WaitForSeconds(_parryHoldDuration);
            state.Speed = 1f;
            // Hold 結束後等 End 動畫剩餘段播完 — 期間玩家若有移動輸入則立刻取消,讓 Locomotion 淡入 Walk/Run
            // 沒輸入時 End 自然播完,finally 退出 Ability,Locomotion 進 Idle
            yield return WaitForEndOrMove(state);
        }

        // 彈刀模式專用：用實時間等頓幀結束後，通知敵人切換到 Stagger 動畫
        private IEnumerator DelayedStagger(EnemyAttackExecutor target, ClipTransition staggerClip)
        {
            if (_hitStopDuration > 0f && _hitStopTimeScale < 1f)
            {
                yield return new WaitForSecondsRealtime(_hitStopDuration);
            }
            if (target != null)
            {
                target.PlayParryStagger(staggerClip);
            }
            _staggerRoutine = null;
        }

        // 不彈刀模式專用：用實時間等頓幀結束後，恢復敵人動畫 1x（讓被凍住的攻擊動畫繼續播完）
        private IEnumerator DelayedResumeAnimation(EnemyAttackExecutor target)
        {
            if (_hitStopDuration > 0f && _hitStopTimeScale < 1f)
            {
                yield return new WaitForSecondsRealtime(_hitStopDuration);
            }
            if (target != null)
            {
                target.ResumeAnimation();
            }
            _staggerRoutine = null;
        }

        // 沿玩家面向反方向（遠離敵人）推 _knockbackDistance 公尺，時長 = _parryHoldDuration
        // 用 CharacterController.Move 驅動才能跟玩家 controller 內部狀態同步（直接設 transform 會被覆蓋）
        private void StartKnockback()
        {
            if (_knockbackDistance <= 0f || _parryHoldDuration <= 0f)
            {
                return;
            }
            Vector3 backDir = -transform.forward;
            backDir.y = 0f;
            if (backDir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            if (_knockbackRoutine != null)
            {
                StopCoroutine(_knockbackRoutine);
            }
            _knockbackRoutine = StartCoroutine(KnockbackRoutine(backDir.normalized));
        }

        private IEnumerator KnockbackRoutine(Vector3 backDir)
        {
            // 先用實時間完全等過頓幀，期間不做任何擊退位移（Time.deltaTime 雖受 timeScale 縮放但非 0，仍會累積出可見位移）
            if (_hitStopDuration > 0f && _hitStopTimeScale < 1f)
            {
                yield return new WaitForSecondsRealtime(_hitStopDuration);
            }
            float elapsed = 0f;
            float lastEased = 0f;
            while (elapsed < _parryHoldDuration)
            {
                elapsed += Time.deltaTime;
                float linearT = Mathf.Clamp01(elapsed / _parryHoldDuration);
                // OutQuad easing：開頭快、尾端慢
                float eased = 1f - (1f - linearT) * (1f - linearT);
                float deltaDistance = (eased - lastEased) * _knockbackDistance;
                lastEased = eased;
                Vector3 delta = backDir * deltaDistance;
                if (_characterController != null && _characterController.enabled)
                {
                    _characterController.Move(delta);
                }
                else
                {
                    transform.position += delta;
                }
                yield return null;
            }
            _knockbackRoutine = null;
        }

        // 空揮收勢:播 End 動畫並等播完或玩家移動輸入打斷
        // 沒輸入 → End 自然播完才退 Ability;有輸入 → 立刻退,Locomotion 淡入 Walk/Run 蓋掉 End
        private IEnumerator PlayParryEndAndWait(AnimancerComponent animancer, ClipTransition clip)
        {
            if (!IsValidClip(clip))
            {
                if (_debugMode)
                {
                    Debug.Log("[招架響應器] End 動畫未設定，跳過", this);
                }
                yield break;
            }
            AnimancerState state = animancer.Play(clip);
            if (_debugMode)
            {
                Debug.Log($"[招架響應器] 播放 End（{state.Length:F2}s，等播完或玩家輸入）", this);
            }
            yield return WaitForEndOrMove(state);
        }

        // 等 End 動畫播完或玩家輸入打斷:
        // • 移動輸入 → 跳出,finally 退 Ability,Locomotion 接 Walk/Run
        // • 攻擊/迴避輸入 → 對應能力啟動,蓋掉 End 動畫,weight 降低 → 跳出,finally 偵測有活躍能力會跳過 ExitAbility
        // • End 播完 → 跳出,finally 退 Ability,Locomotion 接 Idle
        // 進入前先拿掉 Parrying tag,讓其他能力能在 End 期間通過 CheckTagRequirements 啟動
        // 用 NormalizedTime 而非 WaitForSeconds — Animancer Speed 變動時仍能正確判斷播放進度
        private IEnumerator WaitForEndOrMove(AnimancerState state)
        {
            if (state == null)
            {
                yield break;
            }
            RemoveParryingTagIfHeld();
            while (state.NormalizedTime < 1f)
            {
                if (HasMoveInputToCancelEnd())
                {
                    if (_debugMode)
                    {
                        Debug.Log("[招架響應器] 玩家移動輸入 → 取消 End 動畫，交還 Locomotion", this);
                    }
                    yield break;
                }
                // End 被其他能力的 Play 蓋掉時權重會淡出 — 不再屬於當前主要動畫,跳出等待避免卡住
                if (state.Weight < 0.05f)
                {
                    if (_debugMode)
                    {
                        Debug.Log("[招架響應器] End 動畫被新能力接走 → 結束等待，由新能力接管控制權", this);
                    }
                    yield break;
                }
                yield return null;
            }
        }

        private bool HasMoveInputToCancelEnd()
        {
            if (_playerController == null) return false;
            return _playerController.MoveInput.magnitude > _endCancelMoveThreshold;
        }

        // 檢查 ASC 上是否有任何活躍能力 — 用來判斷 End 期間是否被新能力(攻擊/迴避)接走
        private bool HasOtherActiveAbility()
        {
            if (_asc == null) return false;
            foreach (GameplayAbilitySpec spec in _asc.GetAllAbilities())
            {
                if (spec.IsActive) return true;
            }
            return false;
        }

        // 玩家瞬移目標 = 敵人 HitStart 預測位置 + 敵人 forward × (dashOffset + 敵人半徑 + 玩家半徑)
        // 雙方 CharacterController 半徑會跟著縮放，所以 dashOffset 是「邊緣到邊緣」的固定距離 — 縮小不會讓格擋失效
        // 不論玩家原本在敵人正面、背後、側邊都一致 — 永遠出現在敵人攻擊面的正前方
        private void TeleportToTarget(EnemyAttackExecutor target)
        {
            Vector3 enemyPosAtHit = PredictEnemyPositionAtHit(target);
            Vector3 enemyForward = GetEnemyForward(target);
            CharacterController enemyCC = target.GetComponentInParent<CharacterController>();
            float enemyRadius = GetScaledCapsuleRadius(enemyCC);
            float playerRadius = GetScaledCapsuleRadius(_characterController);
            float centerDistance = _dashOffset + enemyRadius + playerRadius;
            Vector3 destination = enemyPosAtHit + enemyForward * centerDistance;
            destination.y = transform.position.y;
            // CharacterController 必須先停用才能正確設位置 — 否則 PhysX 內部 controller position 沒同步，
            // 同一幀稍後的 CC.Move(delta) 會從舊位置加 delta 寫回 transform.position，導致瞬移被「拉回」
            bool ccWasEnabled = _characterController != null && _characterController.enabled;
            if (ccWasEnabled)
            {
                _characterController.enabled = false;
            }
            transform.position = destination;
            if (ccWasEnabled)
            {
                _characterController.enabled = true;
            }
        }

        // 取得 CharacterController 在世界座標下的實際半徑（套用 lossyScale 的最大水平分量）
        // 沒掛 CC 時回傳 0 — 讓 dashOffset 退化為純中心距離
        private static float GetScaledCapsuleRadius(CharacterController cc)
        {
            if (cc == null) return 0f;
            Vector3 scale = cc.transform.lossyScale;
            float scaleXZ = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            return cc.radius * scaleXZ;
        }

        // 預測敵人在 HitStart 那一刻的位置 = 當下位置 + 當下 forward × 剩餘待移動距離
        // 剩餘距離 = DistanceAtHit − 已從攻擊起點往前移動的距離（投影到攻擊起點 forward）
        // 用「當下位置/forward」處理繞圈問題：敵人轉身 → 落點跟著轉，不會卡在攻擊起點時的方向
        // RootMotion：用設計師手填的 DistanceAtHit
        // ManualLerp：DistanceAtHit 留 0 會自動用 MoveDistance × HitStart ÷ 動畫片段長度 推算
        // None / 沒設值：回傳當下位置
        private Vector3 PredictEnemyPositionAtHit(EnemyAttackExecutor target)
        {
            EnemyAttackProfile profile = target.CurrentProfile;
            if (profile == null || !target.IsAttacking)
            {
                return target.transform.position;
            }
            float distanceAtHit = profile.DistanceAtHit;
            if (distanceAtHit <= 0f
                && profile.MoveType == AttackMoveType.ManualLerp
                && profile.MoveDistance > 0f
                && profile.Duration > 0f)
            {
                distanceAtHit = profile.MoveDistance * profile.HitStart / profile.Duration;
            }
            if (distanceAtHit <= 0f)
            {
                return target.transform.position;
            }
            Vector3 startForward = target.AttackStartForward;
            startForward.y = 0f;
            float alreadyMoved = 0f;
            if (startForward.sqrMagnitude > 0.0001f)
            {
                startForward.Normalize();
                Vector3 traveled = target.transform.position - target.AttackStartPosition;
                traveled.y = 0f;
                alreadyMoved = Mathf.Max(0f, Vector3.Dot(traveled, startForward));
            }
            float remaining = Mathf.Max(0f, distanceAtHit - alreadyMoved);
            return target.transform.position + GetEnemyForward(target) * remaining;
        }

        private void EnterAbility()
        {
            if (_playerController != null)
            {
                _playerController.EnterAbilityState();
            }
        }

        private void ExitAbility()
        {
            if (_playerController != null)
            {
                _playerController.ExitAbilityState();
            }
        }

        // 接刀觸發時呼叫：鏡頭震動 + 切到格擋鏡頭 + 火花 + 頓幀
        private void TriggerParryEffects(EnemyAttackExecutor target)
        {
            if (_impulseSource != null)
            {
                _impulseSource.GenerateImpulse();
            }
            BoostParryCamera();
            SpawnSparkVFX(target);
            StartHitStop();
        }

        private void SpawnSparkVFX(EnemyAttackExecutor target)
        {
            if (_sparkVFXPrefab == null || target == null)
            {
                return;
            }
            Vector3 spawnPos = (transform.position + target.transform.position) * 0.5f;
            spawnPos.y += _sparkSpawnHeight;
            GameObject vfx = Instantiate(_sparkVFXPrefab, spawnPos, Quaternion.identity);
            if (_sparkUseUnscaledTime)
            {
                ParticleSystem[] systems = vfx.GetComponentsInChildren<ParticleSystem>(true);
                foreach (ParticleSystem ps in systems)
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.useUnscaledTime = true;
                }
            }
            if (_sparkAutoDestroyDelay > 0f)
            {
                Destroy(vfx, _sparkAutoDestroyDelay);
            }
        }

        // 接刀鏡頭啟用 — 向 CameraDirector 請求 ticket（自動壓過較低層鏡頭如 ThirdPerson/Aim/LockOn）
        private void BoostParryCamera()
        {
            if (_parryTicket != null) return;
            CameraDirector director = CameraDirector.Instance;
            if (director == null) return;
            _parryTicket = director.Request(CameraId.Parry);
        }

        // 接刀鏡頭退場 — 釋放 ticket（先 null 後 Release，避免事件 re-entrancy）
        // 釋放後觸發 ThirdPerson 水平 + 垂直軸回中，讓鏡頭拉回玩家身後預設角度
        private void RevertParryCamera()
        {
            if (_parryTicket == null) return;
            CameraTicket ticket = _parryTicket;
            _parryTicket = null;
            ticket.Release();
            if (_playerController != null)
            {
                _playerController.RecenterThirdPersonOnce();
            }
        }

        private void StartHitStop()
        {
            if (_hitStopDuration <= 0f || _hitStopTimeScale >= 1f)
            {
                return;
            }
            if (_hitStopRoutine != null)
            {
                StopCoroutine(_hitStopRoutine);
            }
            _hitStopRoutine = StartCoroutine(HitStopRoutine());
        }

        private IEnumerator HitStopRoutine()
        {
            Time.timeScale = _hitStopTimeScale;
            yield return new WaitForSecondsRealtime(_hitStopDuration);
            // 期間若被 UI 暫停成 0,不可蓋回,否則背包/寶箱開著遊戲仍在跑
            if (Mathf.Approximately(Time.timeScale, _hitStopTimeScale))
                Time.timeScale = 1f;
            // 頓幀結束 = 鏡頭立刻切回主視角（不是等整段 End 動畫播完）
            RevertParryCamera();
            _hitStopRoutine = null;
        }

        private AnimancerComponent GetCurrentAnimancer()
        {
            GameObject model = _weaponManager.CurrentModelInstance;
            if (model == null)
            {
                return null;
            }
            return model.GetComponent<AnimancerComponent>();
        }

        private static bool IsValidClip(ClipTransition clip)
        {
            return clip != null && clip.IsValid;
        }
    }
}
