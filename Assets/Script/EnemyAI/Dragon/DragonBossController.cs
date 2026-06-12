using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using Boss;
using Boss.Dragon;
using Enemy.AttackSystem;

namespace EnemyAI.Dragon
{
    /// <summary>
    /// 飛龍 Boss 總指揮
    /// 第 3 步階段:加入 ScreamState (開場 + 隕石攻擊),Sleep 醒過來會走 Scream 觸發隕石第一招
    /// 完整 FSM:Sleep / Scream / Idle / Chase / Attack / Die
    /// 距離計算用「邊緣對邊緣」(扣 BossRadius + 玩家 CC.radius)
    /// </summary>
    [RequireComponent(typeof(BossController))]
    [RequireComponent(typeof(BossGroundLocomotion))]
    public class DragonBossController : MonoBehaviour, IAttackProfileHost
    {
        #region Serialized Fields

        [Header("資料")]
        [SerializeField] [Tooltip("飛龍動畫集 SO — 包含 16 個飛龍動畫剪輯")]
        private DragonAnimationSet _animations;

        [Header("玩家偵測")]
        [SerializeField] [Tooltip("玩家 GameObject 的 Tag — 預設 \"Player\"")]
        private string _playerTag = "Player";

        [Header("攻擊系統")]
        [SerializeField] [Tooltip("飛龍可用的攻擊招式清單 — 拖入 EnemyAttackProfile .asset。每個招式內各自設定 PickWeight (機率) 與 Pick Distance 範圍")]
        private List<EnemyAttackProfile> _attackProfiles = new List<EnemyAttackProfile>();

        [SerializeField] [Tooltip("攻擊間的最短等待時間 (秒) — 一招結束後到下一招的 Idle 期。建議 1~2")]
        private float _attackInterval = 1.5f;

        [SerializeField] [Tooltip("EnemyAttackExecutor 元件 — 通常在飛龍模型子物件 (Animancer 同物件)。留空 Awake 自動 GetComponentInChildren")]
        private EnemyAttackExecutor _attackExecutor;

        [SerializeField] [Tooltip("隕石攻擊控制器 — 由 ScreamState 觸發。留空 Awake 自動 GetComponent。沒掛此元件則 Scream 期間不會召喚隕石")]
        private MeteorAttackController _meteorAttack;

        [Header("元件引用 (留空 Awake 自動抓)")]
        [SerializeField] [Tooltip("Animancer 元件 — 通常掛在飛龍模型子物件")]
        private AnimancerComponent _animancer;

        [SerializeField] [Tooltip("BossController 元件 — 同物件")]
        private BossController _boss;

        [SerializeField] [Tooltip("BossGroundLocomotion 元件 — 同物件")]
        private BossGroundLocomotion _locomotion;

        [Header("動畫過場時間 (秒)")]
        [SerializeField] [Tooltip("受擊動畫淡入時間 — 建議 0.05~0.15")]
        private float _hitFadeDuration = 0.1f;

        [Header("受擊抖動 (Flinch) — 攻擊霸體中被打的視覺回饋")]
        [SerializeField] [Tooltip("抖動目標 Transform — 建議拖入飛龍「視覺模型子物件」。留空 Awake 自動用 Animancer 所在的模型物件。抖動只動這個物件的本地位移/旋轉,不影響攻擊動畫與位移")]
        private Transform _flinchShakeTarget;

        [SerializeField] [Tooltip("抖動時長 (秒) — 建議 0.1~0.2")]
        private float _flinchShakeDuration = 0.15f;

        [SerializeField] [Tooltip("抖動位移強度 (公尺) — 大型 Boss 體積大,要明顯一點。建議先試 0.2~0.5")]
        private float _flinchShakeStrength = 0.3f;

        [SerializeField] [Tooltip("方向性反推強度 (公尺) — 從攻擊來向把模型推一下,給「被打到」的硬感。建議 0.15~0.4")]
        private float _flinchKnockOffset = 0.25f;

        [SerializeField] [Tooltip("旋轉搖晃角度 (度) — Pitch/Roll 範圍,給「被震一下」的真實感。Yaw 不轉避免影響面向。建議 3~8")]
        private float _flinchRotationAngle = 5f;

        #endregion

        #region Private Fields

        private Transform _player;
        private CharacterController _ownCC;
        private CharacterController _playerCC;
        private BossStateMachine _stateMachine;
        private bool _hasPlayedDeath;
        private bool _cinematicControl;
        private Coroutine _hitRecoveryRoutine;
        private float _cachedEdgeDistance = float.MaxValue;
        private float _cachedCenterStopDistance;

        private float _flinchShakeRemainingTime;
        private Vector3 _flinchShakeLastOffsetPos;
        private Quaternion _flinchShakeLastOffsetRot = Quaternion.identity;
        private float _flinchShakeNoiseSeed;
        private Vector3 _flinchKnockDirectionLocal;

        private DragonSleepState _sleepState;
        private DragonScreamState _screamState;
        private DragonIdleState _idleState;
        private DragonChaseState _chaseState;
        private DragonAttackState _attackState;
        private DragonDieState _dieState;

        #endregion

        #region Properties

        public BossController Boss => _boss;
        public BossGroundLocomotion Locomotion => _locomotion;
        public AnimancerComponent Animancer => _animancer;
        public DragonAnimationSet Animations => _animations;
        public Transform Player => _player;
        public EnemyAttackExecutor AttackExecutor => _attackExecutor;
        public MeteorAttackController MeteorAttack => _meteorAttack;
        public IReadOnlyList<EnemyAttackProfile> AttackProfiles => _attackProfiles;
        public float AttackInterval => _attackInterval;

        public float EdgeDistanceToPlayer => _cachedEdgeDistance;
        public float CenterStopDistance => _cachedCenterStopDistance;

        public DragonSleepState SleepState => _sleepState;
        public DragonScreamState ScreamState => _screamState;
        public DragonIdleState IdleState => _idleState;
        public DragonChaseState ChaseState => _chaseState;
        public DragonAttackState AttackState => _attackState;
        public DragonDieState DieState => _dieState;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_animancer == null)
                _animancer = GetComponentInChildren<AnimancerComponent>(true);
            if (_boss == null)
                _boss = GetComponent<BossController>();
            if (_locomotion == null)
                _locomotion = GetComponent<BossGroundLocomotion>();
            if (_attackExecutor == null)
                _attackExecutor = GetComponentInChildren<EnemyAttackExecutor>(true);
            if (_meteorAttack == null)
                _meteorAttack = GetComponent<MeteorAttackController>();
            _ownCC = GetComponent<CharacterController>();

            if (_flinchShakeTarget == null && _animancer != null)
                _flinchShakeTarget = _animancer.transform;

            if (_attackExecutor != null)
                _attackExecutor.SetOwnerRoot(transform);

            _sleepState = new DragonSleepState(this);
            _screamState = new DragonScreamState(this);
            _idleState = new DragonIdleState(this);
            _chaseState = new DragonChaseState(this);
            _attackState = new DragonAttackState(this);
            _dieState = new DragonDieState(this);

            _stateMachine = new BossStateMachine();
        }

        private void OnEnable()
        {
            if (_boss != null)
            {
                _boss.OnDamaged += HandleDamaged;
                _boss.OnDied += HandleDied;
            }
            if (_attackExecutor != null)
            {
                _attackExecutor.OnHitConfirmed -= HandleAttackHitConfirmed;
                _attackExecutor.OnHitConfirmed += HandleAttackHitConfirmed;
            }
        }

        private void OnDisable()
        {
            if (_boss != null)
            {
                _boss.OnDamaged -= HandleDamaged;
                _boss.OnDied -= HandleDied;
            }
            if (_attackExecutor != null)
            {
                _attackExecutor.OnHitConfirmed -= HandleAttackHitConfirmed;
            }
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(_playerTag))
            {
                GameObject playerGO = GameObject.FindWithTag(_playerTag);
                if (playerGO != null)
                {
                    _player = playerGO.transform;
                    _playerCC = playerGO.GetComponentInParent<CharacterController>();
                }
            }
            if (_player == null)
                Debug.LogWarning($"[{name}] DragonBossController 找不到 Tag = \"{_playerTag}\" 的 GameObject — 玩家偵測會失效", this);

            _stateMachine.ChangeState(_sleepState);
        }

        private void Update()
        {
            UpdateDistanceCache();
            // 開場/過場接管中暫停 FSM 自動決策,動畫由 DragonBossIntroSequence 直接驅動
            if (!_cinematicControl)
                _stateMachine.Tick();
        }

        // 在 Animator 寫完骨骼之後套用受擊抖動 offset,避免被骨骼動畫覆寫
        private void LateUpdate()
        {
            ApplyFlinchShake();
        }

        #endregion

        #region Public API

        public void ChangeState(BossState newState)
        {
            _stateMachine.ChangeState(newState);
        }

        /// <summary>
        /// 開場/過場接管開關 — true 時暫停 FSM 自動決策(Update 不 Tick),
        /// 由 DragonBossIntroSequence 直接驅動動畫;過場結束設回 false 並 ChangeState 進戰鬥。
        /// </summary>
        public bool IsCinematicControlled => _cinematicControl;

        public void SetCinematicControl(bool active)
        {
            _cinematicControl = active;
        }

        public AnimancerState PlayAnimation(ClipTransition clip, float fadeDuration = 0f)
        {
            if (_animancer == null || clip == null || clip.Clip == null) return null;
            return _animancer.Play(clip, fadeDuration);
        }

        /// <summary>
        /// 從攻擊招式清單選一招 (加權隨機,RangeAndWeight 模式)
        /// 過濾:邊緣距離在 [MinPickDistance, MaxPickDistance] 內 + PickWeight > 0
        /// 沒符合條件回 null (IdleState 會稍後重試)
        /// </summary>
        public EnemyAttackProfile SelectAttack()
        {
            if (_attackProfiles == null || _attackProfiles.Count == 0) return null;
            float edgeDist = _cachedEdgeDistance;

            float totalWeight = 0f;
            for (int i = 0; i < _attackProfiles.Count; i++)
            {
                EnemyAttackProfile p = _attackProfiles[i];
                if (!IsAttackEligible(p, edgeDist)) continue;
                totalWeight += p.PickWeight;
            }
            if (totalWeight <= 0f) return null;

            float r = Random.Range(0f, totalWeight);
            float acc = 0f;
            for (int i = 0; i < _attackProfiles.Count; i++)
            {
                EnemyAttackProfile p = _attackProfiles[i];
                if (!IsAttackEligible(p, edgeDist)) continue;
                acc += p.PickWeight;
                if (r <= acc) return p;
            }
            return null;
        }

        /// <summary>
        /// 依 PickWeight 加權抽一招「意圖招式」— 不分距離 (只排除 PickWeight ≤ 0)。
        /// FSM 拿到後依該招的 Min/MaxPickDistance 決定要不要先移動到射程再放,
        /// 達成「先選招再就位」: 抽到遠程就從遠處放、抽到近戰就逼近。清單空 / 全 0 權重回 null。
        /// </summary>
        public EnemyAttackProfile SelectAttackByWeight()
        {
            if (_attackProfiles == null || _attackProfiles.Count == 0) return null;

            float totalWeight = 0f;
            for (int i = 0; i < _attackProfiles.Count; i++)
            {
                EnemyAttackProfile p = _attackProfiles[i];
                if (p == null || p.PickWeight <= 0f) continue;
                totalWeight += p.PickWeight;
            }
            if (totalWeight <= 0f) return null;

            float r = Random.Range(0f, totalWeight);
            float acc = 0f;
            for (int i = 0; i < _attackProfiles.Count; i++)
            {
                EnemyAttackProfile p = _attackProfiles[i];
                if (p == null || p.PickWeight <= 0f) continue;
                acc += p.PickWeight;
                if (r <= acc) return p;
            }
            return null;
        }

        /// <summary>該招在指定邊緣距離是否可放 (邊緣距離落在 [MinPickDistance, MaxPickDistance] 內)</summary>
        public static bool IsWithinAttackRange(EnemyAttackProfile p, float edgeDist)
        {
            if (p == null) return false;
            return edgeDist >= p.MinPickDistance && edgeDist <= p.MaxPickDistance;
        }

        #endregion

        #region Private Methods

        private static bool IsAttackEligible(EnemyAttackProfile p, float edgeDist)
        {
            if (p == null) return false;
            if (p.PickWeight <= 0f) return false;
            if (edgeDist < p.MinPickDistance) return false;
            if (edgeDist > p.MaxPickDistance) return false;
            return true;
        }

        private void UpdateDistanceCache()
        {
            if (_player == null || _boss == null || _boss.Config == null)
            {
                _cachedEdgeDistance = float.MaxValue;
                _cachedCenterStopDistance = 0f;
                return;
            }
            float centerDist = Vector3.Distance(transform.position, _player.position);
            float bossRadius = _boss.Config.BossRadius;
            float playerRadius = GetScaledRadius(_playerCC);
            _cachedEdgeDistance = Mathf.Max(0f, centerDist - bossRadius - playerRadius);
            _cachedCenterStopDistance = _boss.Config.GroundStopDistance + bossRadius + playerRadius;
        }

        private static float GetScaledRadius(CharacterController cc)
        {
            if (cc == null) return 0f;
            Vector3 scale = cc.transform.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        /// <summary>
        /// EnemyAttackExecutor 命中玩家時呼叫 — 包成 HitContext 走遊戲統一的 IHitReceiver 管線
        /// 玩家身上的 IHitReceiver 實作 (GASDamageReceiver) 會處理 GAS 扣血
        /// 套用後沒被擋下 (wasBlocked = false) 時生成 Profile 設定的命中特效
        /// </summary>
        private void HandleAttackHitConfirmed(EnemyAttackExecutor executor, EnemyAttackProfile profile, GameObject hitObject)
        {
            if (_hasPlayedDeath || profile == null || hitObject == null) return;
            IHitReceiver receiver = hitObject.GetComponentInParent<IHitReceiver>();
            if (receiver == null)
            {
                Debug.LogWarning($"[{name}] 命中 {hitObject.name} 但找不到 IHitReceiver — 無法套用傷害。請確認玩家身上有 GASDamageReceiver 或類似 IHitReceiver 實作", hitObject);
                return;
            }

            Vector3 attackDir = hitObject.transform.position - transform.position;
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
                cameraShakeIntensity = 0f,
            };
            receiver.OnHit(ref ctx);

            if (!ctx.wasBlocked && profile.HitVfxPrefab != null)
            {
                SpawnHitVfx(profile, hitObject.transform);
            }
        }

        /// <summary>
        /// 在玩家身上 (HitVfxOffset local space) 生成命中特效
        /// Prefab 自帶 scale × Profile 的倍率 × 玩家 lossyScale (玩家放大時特效一起放大)
        /// </summary>
        private static void SpawnHitVfx(EnemyAttackProfile profile, Transform target)
        {
            Vector3 worldPos = target.TransformPoint(profile.HitVfxOffset);
            GameObject vfx = Instantiate(profile.HitVfxPrefab, worldPos, target.rotation);
            Vector3 baseScale = Vector3.Scale(vfx.transform.localScale, profile.HitVfxScaleMultiplier);
            vfx.transform.localScale = Vector3.Scale(baseScale, target.lossyScale);
            if (profile.HitVfxLifetime > 0f)
            {
                Destroy(vfx, profile.HitVfxLifetime);
            }
        }

        private void HandleDamaged(float damage)
        {
            if (_hasPlayedDeath) return;

            // 開場/過場接管中:不切狀態(開場演出由 DragonBossIntroSequence 主導),只給抖動回饋
            if (_cinematicControl)
            {
                PlayFlinchShake(ComputeIncomingHitDirection());
                return;
            }

            // 沉睡中被打:開場由 DragonBossIntroSequence 訂閱 OnDamaged 觸發 (不在此自動切狀態)。
            // 攻擊 / 咆哮中:霸體不被打斷。三者皆只給受擊抖動,不播完整 GetHit (避免蓋掉當前動畫計時)
            if (_stateMachine.Current == _sleepState
                || _stateMachine.Current == _attackState
                || _stateMachine.Current == _screamState)
            {
                PlayFlinchShake(ComputeIncomingHitDirection());
                return;
            }

            if (_animations == null || _animations.GetHit == null || _animations.GetHit.Clip == null) return;
            AnimancerState state = _animancer.Play(_animations.GetHit, _hitFadeDuration);
            state.Time = 0f;

            if (_hitRecoveryRoutine != null) StopCoroutine(_hitRecoveryRoutine);
            _hitRecoveryRoutine = StartCoroutine(HitRecoveryCoroutine(state.Length));
        }

        private IEnumerator HitRecoveryCoroutine(float length)
        {
            yield return new WaitForSeconds(length);
            _hitRecoveryRoutine = null;
            if (_hasPlayedDeath) yield break;
            _stateMachine.Current?.OnEnter();
        }

        private void HandleDied()
        {
            if (_hasPlayedDeath) return;
            _hasPlayedDeath = true;
            if (_hitRecoveryRoutine != null)
            {
                StopCoroutine(_hitRecoveryRoutine);
                _hitRecoveryRoutine = null;
            }
            if (_attackExecutor != null && _attackExecutor.IsAttacking)
            {
                _attackExecutor.Cancel();
            }
            if (_meteorAttack != null && _meteorAttack.IsExecuting)
            {
                _meteorAttack.Cancel();
            }
            ChangeState(_dieState);
        }

        /// <summary>
        /// 觸發受擊抖動 — 重置倒數與隨機種子,計算攻擊來向 (本地) 做方向反推。
        /// 純視覺,不扣血、不改狀態 (扣血由 BossController 處理,攻擊霸體不打斷)。
        /// </summary>
        private void PlayFlinchShake(Vector3 attackWorldDirection)
        {
            if (_flinchShakeTarget == null) return;
            _flinchShakeRemainingTime = _flinchShakeDuration;
            _flinchShakeNoiseSeed = Random.value * 1000f;
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
        /// 受擊抖動 — 增量模式:每幀算出當前 offset (衰減 + Perlin 雜訊 + 方向反推),
        /// 跟上幀 offset 算 delta 用 += 疊加到 target 的本地位移/旋轉,所以拉 root 也安全。
        /// 抖動結束時 current = 0,自動扣掉上幀殘留 offset 還原。
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
                float decay = t * t;
                Vector3 knockOffset = _flinchKnockDirectionLocal * (_flinchKnockOffset * decay);
                float noiseTime = (Time.time + _flinchShakeNoiseSeed) * 65f;
                Vector3 randomShake = new Vector3(
                    Mathf.PerlinNoise(noiseTime, 0.13f) * 2f - 1f,
                    (Mathf.PerlinNoise(0.41f, noiseTime) * 2f - 1f) * 0.3f,
                    Mathf.PerlinNoise(noiseTime, noiseTime * 0.7f) * 2f - 1f
                ) * (_flinchShakeStrength * decay);
                currentOffsetPos = knockOffset + randomShake;

                float rotAngle = _flinchRotationAngle * decay;
                float pitchNoise = Mathf.PerlinNoise(noiseTime * 0.5f, 0.7f) * 2f - 1f;
                float rollNoise = Mathf.PerlinNoise(0.2f, noiseTime * 0.5f) * 2f - 1f;
                currentOffsetRot = Quaternion.Euler(pitchNoise * rotAngle, 0f, rollNoise * rotAngle);
            }

            Vector3 deltaPos = currentOffsetPos - _flinchShakeLastOffsetPos;
            Quaternion deltaRot = Quaternion.Inverse(_flinchShakeLastOffsetRot) * currentOffsetRot;

            _flinchShakeTarget.localPosition += deltaPos;
            _flinchShakeTarget.localRotation = _flinchShakeTarget.localRotation * deltaRot;

            _flinchShakeLastOffsetPos = currentOffsetPos;
            _flinchShakeLastOffsetRot = currentOffsetRot;
        }

        /// <summary>
        /// 推算攻擊來向 — OnDamaged 事件沒帶 HitContext,改用「玩家 → 飛龍」方向當代理,
        /// 把飛龍從玩家所在方向往外推。玩家不存在時回 default (只抖動、無方向反推)。
        /// </summary>
        private Vector3 ComputeIncomingHitDirection()
        {
            if (_player == null) return default;
            Vector3 dir = transform.position - _player.position;
            dir.y = 0f;
            return dir;
        }

        #endregion
    }
}
