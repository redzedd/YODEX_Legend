using System.Collections;
using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 迴避支援能力 - 遠程武器使用
    /// 切換武器時觸發，進入子彈時間並獲得傷害加成
    /// </summary>
    [CreateAssetMenu(fileName = "GA_DodgeAssist", menuName = "GAS/Abilities/Dodge Assist")]
    public class GA_DodgeAssist : GameplayAbility
    {
        [Header("Animation")]
        [Tooltip("迴避動畫")]
        public ClipTransition DodgeAnimation;

        [Header("Bullet Time")]
        [Tooltip("子彈時間持續時間")]
        public float BulletTimeDuration = 2.0f;

        [Tooltip("子彈時間縮放比例（0.2 = 20% 速度）")]
        [Range(0.01f, 1f)]
        public float BulletTimeScale = 0.2f;

        [Tooltip("進入子彈時間的過渡時間")]
        public float BulletTimeEnterDuration = 0.1f;

        [Tooltip("退出子彈時間的過渡時間")]
        public float BulletTimeExitDuration = 0.2f;

        [Header("Combat Bonus")]
        [Tooltip("子彈時間內攻擊傷害加成倍率")]
        public float DamageBonus = 1.5f;

        [Tooltip("傷害加成效果")]
        public GameplayEffect DamageBonusEffect;

        [Header("Movement")]
        [Tooltip("迴避移動距離")]
        public float DodgeDistance = 3.0f;

        [Tooltip("迴避移動時間")]
        public float DodgeDuration = 0.3f;

        [Header("Cost")]
        [Tooltip("支援點數消耗")]
        public float AssistPointsCost = 1f;

        [Header("Cues")]
        [Tooltip("迴避支援開始 Cue")]
        public GameplayTag DodgeAssistStartCue;

        [Tooltip("子彈時間開始 Cue")]
        public GameplayTag BulletTimeStartCue;

        [Tooltip("子彈時間結束 Cue")]
        public GameplayTag BulletTimeEndCue;

        // 靜態引用，用於追蹤當前的子彈時間實例
        private static GA_DodgeAssist _activeBulletTimeInstance;
        private static GameplayAbilitySpec _activeBulletTimeSpec;

        protected override bool CanPayCost(GameplayAbilitySpec spec)
        {
            // 檢查支援點數
            CombatAttributeSet attrSet = spec.Owner.GetAttributeSet<CombatAttributeSet>();
            if (attrSet != null)
            {
                return attrSet.HasAssistPoints(AssistPointsCost);
            }
            return base.CanPayCost(spec);
        }

        protected override void PayCost(GameplayAbilitySpec spec)
        {
            // 消耗支援點數
            CombatAttributeSet attrSet = spec.Owner.GetAttributeSet<CombatAttributeSet>();
            if (attrSet != null)
            {
                attrSet.TryConsumeAssistPoints(AssistPointsCost);
            }
            base.PayCost(spec);
        }

        public override void ActivateAbility(GameplayAbilitySpec spec)
        {
            // 支付消耗
            PayCost(spec);

            // 啟動迴避支援協程
            Coroutine coroutine = StartCoroutine(spec, DodgeAssistRoutine(spec));
            spec.SetActiveCoroutine(coroutine);
        }

        public override void EndAbility(GameplayAbilitySpec spec, bool wasCancelled)
        {
            // 清理運行時數據
            if (spec.CustomData is DodgeAssistRuntimeData runtimeData)
            {
                runtimeData.Cleanup();
            }

            // 確保恢復時間縮放
            if (_activeBulletTimeSpec == spec)
            {
                TimeScaleUtility.RestoreTimeScale();
                _activeBulletTimeInstance = null;
                _activeBulletTimeSpec = null;
            }

            // 移除狀態標籤
            if (spec.Owner != null)
            {
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.BulletTime);
                spec.Owner.OwnedTags.RemoveTag(GameplayTags.State.Invincible);
            }

            // 觸發結束 Cue
            if (BulletTimeEndCue.IsValid && spec.Owner != null)
            {
                ExecuteGameplayCue(spec, BulletTimeEndCue, spec.Owner.transform.position);
            }

            base.EndAbility(spec, wasCancelled);
        }

        /// <summary>
        /// 迴避支援主協程
        /// </summary>
        private IEnumerator DodgeAssistRoutine(GameplayAbilitySpec spec)
        {
            AbilitySystemComponent owner = spec.Owner;
            
            // 從 NewGASPlayerController 獲取正確的 Animancer 引用
            NewGASPlayerController playerController = owner.GetComponent<NewGASPlayerController>();
            AnimancerComponent animancer = playerController?.Animancer;
            
            // 如果 PlayerController 沒有 Animancer，嘗試直接獲取
            if (animancer == null)
            {
                animancer = owner.GetComponentInChildren<AnimancerComponent>();
            }
            
            CharacterController cc = owner.GetComponent<CharacterController>();

            // 創建運行時數據
            DodgeAssistRuntimeData runtimeData = new DodgeAssistRuntimeData(owner, this);
            spec.CustomData = runtimeData;

            // 設置靜態引用
            _activeBulletTimeInstance = this;
            _activeBulletTimeSpec = spec;

            // 添加無敵標籤（迴避過程中無敵）
            owner.OwnedTags.AddTag(GameplayTags.State.Invincible);

            // 觸發開始 Cue
            if (DodgeAssistStartCue.IsValid)
            {
                ExecuteGameplayCue(spec, DodgeAssistStartCue, owner.transform.position);
            }

            // 播放迴避動畫
            if (animancer != null && DodgeAnimation != null)
            {
                AnimancerState animState = animancer.Play(DodgeAnimation);
                animState.Time = 0;
            }

            // 執行迴避移動
            if (cc != null && DodgeDistance > 0)
            {
                Vector3 dodgeDirection = CalculateDodgeDirection(owner);
                Vector3 targetPos = owner.transform.position + dodgeDirection * DodgeDistance;
                yield return runtimeData.StartDodgeMovement(cc, targetPos, DodgeDuration);
            }
            else
            {
                yield return new WaitForSeconds(DodgeDuration);
            }

            // 移除無敵（迴避完成）
            owner.OwnedTags.RemoveTag(GameplayTags.State.Invincible);

            // 進入子彈時間
            yield return StartBulletTime(spec, runtimeData);
        }

        /// <summary>
        /// 計算迴避方向
        /// </summary>
        private Vector3 CalculateDodgeDirection(AbilitySystemComponent owner)
        {
            // 獲取輸入方向
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 inputDir = new Vector3(h, 0, v);

            Vector3 dodgeDir;
            if (inputDir.magnitude > 0.1f)
            {
                // 根據相機方向轉換輸入
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    dodgeDir = mainCamera.transform.TransformDirection(inputDir);
                }
                else
                {
                    dodgeDir = inputDir;
                }
            }
            else
            {
                // 無輸入時向後迴避
                dodgeDir = -owner.transform.forward;
            }

            dodgeDir.y = 0;
            dodgeDir.Normalize();

            return dodgeDir;
        }

        /// <summary>
        /// 開始子彈時間
        /// </summary>
        private IEnumerator StartBulletTime(GameplayAbilitySpec spec, DodgeAssistRuntimeData runtimeData)
        {
            AbilitySystemComponent owner = spec.Owner;

            // 添加子彈時間標籤
            owner.OwnedTags.AddTag(GameplayTags.State.BulletTime);

            // 觸發子彈時間開始 Cue
            if (BulletTimeStartCue.IsValid)
            {
                ExecuteGameplayCue(spec, BulletTimeStartCue, owner.transform.position);
            }

            // 應用傷害加成效果
            if (DamageBonusEffect != null)
            {
                ApplyEffectToSelf(spec, DamageBonusEffect);
            }

            // 平滑進入子彈時間
            yield return TimeScaleUtility.SmoothTimeScale(Time.timeScale, BulletTimeScale, BulletTimeEnterDuration);

            // 記錄子彈時間開始的真實時間
            float bulletTimeStartRealTime = Time.realtimeSinceStartup;
            float bulletTimeRealDuration = BulletTimeDuration * BulletTimeScale; // 實際經過的真實時間

            // 子彈時間主循環
            while (spec.IsActive)
            {
                float elapsedRealTime = Time.realtimeSinceStartup - bulletTimeStartRealTime;

                // 檢查是否超時
                if (elapsedRealTime >= bulletTimeRealDuration)
                {
                    break;
                }

                // 檢查是否應該提前結束子彈時間
                if (ShouldEndBulletTime(owner, runtimeData))
                {
                    break;
                }

                yield return null;
            }

            // 平滑退出子彈時間
            yield return TimeScaleUtility.SmoothTimeScale(Time.timeScale, 1f, BulletTimeExitDuration);

            // 結束能力
            spec.EndAbility();
        }

        /// <summary>
        /// 檢查是否應該提前結束子彈時間
        /// </summary>
        private bool ShouldEndBulletTime(AbilitySystemComponent owner, DodgeAssistRuntimeData runtimeData)
        {
            // 如果玩家執行了攻擊，結束子彈時間
            if (owner.OwnedTags.HasTag(GameplayTags.State.Attacking))
            {
                return true;
            }

            // 如果玩家受到傷害（被其他敵人攻擊），結束子彈時間
            // 這個檢查需要在運行時數據中追蹤

            return false;
        }


        /// <summary>
        /// 靜態方法：強制結束當前的子彈時間
        /// </summary>
        public static void ForceEndBulletTime()
        {
            if (_activeBulletTimeSpec != null && _activeBulletTimeSpec.IsActive)
            {
                _activeBulletTimeSpec.CancelAbility();
            }
        }

        /// <summary>
        /// 靜態方法：檢查是否正在子彈時間中
        /// </summary>
        public static bool IsInBulletTime()
        {
            return _activeBulletTimeInstance != null && _activeBulletTimeSpec != null && _activeBulletTimeSpec.IsActive;
        }
    }

    /// <summary>
    /// 迴避支援運行時數據
    /// </summary>
    public class DodgeAssistRuntimeData
    {
        public AbilitySystemComponent Owner { get; private set; }
        public GA_DodgeAssist AbilityDef { get; private set; }
        public bool WasDamaged { get; set; }

        private Coroutine _moveCoroutine;

        public DodgeAssistRuntimeData(AbilitySystemComponent owner, GA_DodgeAssist abilityDef)
        {
            Owner = owner;
            AbilityDef = abilityDef;
            WasDamaged = false;
        }

        /// <summary>
        /// 開始迴避移動
        /// </summary>
        public IEnumerator StartDodgeMovement(CharacterController cc, Vector3 targetPos, float duration)
        {
            Vector3 startPos = Owner.transform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                
                // 使用平滑曲線
                float smoothT = Mathf.SmoothStep(0, 1, t);
                
                Vector3 nextPos = Vector3.Lerp(startPos, targetPos, smoothT);
                Vector3 delta = nextPos - Owner.transform.position;
                
                cc.Move(delta);
                
                yield return null;
            }
        }

        public void Cleanup()
        {
            if (_moveCoroutine != null && Owner != null)
            {
                Owner.StopCoroutine(_moveCoroutine);
                _moveCoroutine = null;
            }
        }
    }
}
