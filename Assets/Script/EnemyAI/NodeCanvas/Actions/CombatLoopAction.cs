using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;
using Animancer;
using Enemy.AttackSystem;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — Combat 狀態主迴圈
    /// 兩階段循環：
    /// 1. Chasing — 追擊玩家直到有任一招式落入「Min/Max Pick Distance」射程內，cooldown 期間也持續追擊（不發呆）
    /// 2. Attacking — 執行 EnemyAttackExecutor.Execute；結束 / 取消都立即切回 Chasing
    ///
    /// cooldown 純粹當「下次攻擊的最早時機」閘門，不再讓敵人靜止。
    /// 被招架時的彈刀僵直透過獨立的 _parryStunRemainingTime 在 Chasing 內擋住移動，
    /// 跟 attackCooldown 解耦（cooldown 控制攻擊頻率，stun 控制視覺僵直時長）。
    /// </summary>
    [Category("Enemy AI/Combat")]
    [Name("Combat Loop")]
    [Description("Combat 狀態主迴圈：追擊 → 進攻擊距離 → 執行攻擊 → 立即繼續追擊")]
    public class CombatLoopAction : ActionTask<EnemyController>
    {
        [Tooltip("追擊時使用的動畫類型（建議 Walk 或 Run）")]
        public EnemyAnimationType chaseAnimation = EnemyAnimationType.Walk;

        [Tooltip("攻擊間隔（秒）— 下次攻擊的最早時機。cooldown 期間敵人會持續追擊，但不會發動新攻擊。建議 0.5~2.5")]
        public float attackCooldown = 1.5f;

        [Tooltip("追擊期間是否主動控制轉身。\ntrue（建議）：移動中朝 A* 路徑方向轉（自動繞牆），抵達停止距離後朝玩家轉。\nfalse：完全不主動轉身，由 Animator / Root Motion 控制")]
        public bool faceTargetWhileChasing = true;

        private enum Phase { Chasing, Attacking }

        private Phase _phase;
        private float _cooldownTimer;
        private float _parryStunRemainingTime;
        // 攻擊動畫剛結束時的後搖時間 — 期間敵人停在原地播 Idle、不追擊、不出招
        private float _recoveryRemainingTime;
        private EnemyAttackExecutor _executor;
        private EnemyAnimationType _currentAnim;
        private bool _hasCurrentAnim;
        private bool _attackFinishedFlag;

        protected override string info => $"Combat Loop (cd {attackCooldown}s)";

        protected override void OnExecute()
        {
            _executor = agent.AttackExecutor;
            _phase = Phase.Chasing;
            _cooldownTimer = 0f;
            _parryStunRemainingTime = 0f;
            _recoveryRemainingTime = 0f;
            _hasCurrentAnim = false;
            _attackFinishedFlag = false;
            agent.NotifyEnteredCombat();
            SubscribeExecutorEvents();
            if (_executor == null)
            {
                Debug.LogWarning($"[{agent.name}] CombatLoopAction：EnemyController 找不到 EnemyAttackExecutor，敵人無法攻擊", agent);
            }
            if (!agent.HasAttackProfiles)
            {
                Debug.LogWarning($"[{agent.name}] CombatLoopAction：EnemyController.AttackProfiles 為空，敵人會持續追擊但不會攻擊", agent);
            }
        }

        protected override void OnUpdate()
        {
            if (agent.PlayerTransform == null) return;
            if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
            if (_parryStunRemainingTime > 0f) _parryStunRemainingTime -= Time.deltaTime;
            if (_recoveryRemainingTime > 0f) _recoveryRemainingTime -= Time.deltaTime;

            switch (_phase)
            {
                case Phase.Chasing:
                    TickChasing();
                    break;
                case Phase.Attacking:
                    TickAttacking();
                    break;
            }
        }

        protected override void OnStop()
        {
            UnsubscribeExecutorEvents();
            if (agent != null)
            {
                agent.StopMovement();
                agent.ClearFacingDirection();
                agent.SetArmor(ArmorLevel.None);
                agent.NotifyExitedCombat();
            }
            if (_executor != null && _executor.IsAttacking)
            {
                _executor.Cancel();
            }
            _hasCurrentAnim = false;
        }

        private void SubscribeExecutorEvents()
        {
            if (_executor == null) return;
            _executor.OnAttackStart -= HandleAttackStarted;
            _executor.OnAttackStart += HandleAttackStarted;
            _executor.OnAttackEnd -= HandleAttackEnded;
            _executor.OnAttackEnd += HandleAttackEnded;
            _executor.OnAttackCanceled -= HandleAttackCanceled;
            _executor.OnAttackCanceled += HandleAttackCanceled;
        }

        private void UnsubscribeExecutorEvents()
        {
            if (_executor == null) return;
            _executor.OnAttackStart -= HandleAttackStarted;
            _executor.OnAttackEnd -= HandleAttackEnded;
            _executor.OnAttackCanceled -= HandleAttackCanceled;
        }

        private void HandleAttackStarted(EnemyAttackExecutor e, EnemyAttackProfile p)
        {
            agent.SetArmor(ArmorLevel.AttackingArmor);
        }

        private void HandleAttackEnded(EnemyAttackExecutor e, EnemyAttackProfile p)
        {
            if (agent != null) agent.SetArmor(ArmorLevel.None);
            _cooldownTimer = attackCooldown;
            _recoveryRemainingTime = p != null ? p.RecoveryDuration : 0f;
            _attackFinishedFlag = true;
        }

        private void HandleAttackCanceled(EnemyAttackExecutor e, EnemyAttackProfile p)
        {
            if (agent != null) agent.SetArmor(ArmorLevel.None);
            _cooldownTimer = attackCooldown;
            _parryStunRemainingTime = GetParryStaggerLength(p);
            // 被打斷不算正常攻擊結束，不套用後搖
            _recoveryRemainingTime = 0f;
            _attackFinishedFlag = true;
        }

        private static float GetParryStaggerLength(EnemyAttackProfile profile)
        {
            if (profile == null) return 0f;
            if (!profile.IsParryStaggers) return 0f;
            ClipTransition clip = profile.ParryStaggerAnimation;
            if (clip == null || !clip.IsValid) return 0f;
            AnimationClip animClip = clip.Clip;
            return animClip != null ? animClip.length : 0f;
        }

        private void TickChasing()
        {
            // 彈刀僵直期間靜止 — 不動、不轉、不切動畫（讓 ParryStaggerAnimation 自然播完）
            if (_parryStunRemainingTime > 0f)
            {
                agent.StopMovement();
                agent.ClearFacingDirection();
                _hasCurrentAnim = false;
                return;
            }
            // 後搖期間 — 停在原地播 Idle，不追擊、不出招（平衡跑速太快的敵人）
            if (_recoveryRemainingTime > 0f)
            {
                agent.StopMovement();
                agent.ClearFacingDirection();
                EnsureAnimation(EnemyAnimationType.Idle);
                return;
            }

            float dist = agent.GetDistanceToPlayer();
            bool canAttack = _cooldownTimer <= 0f
                && _executor != null
                && !_executor.IsAttacking
                && agent.HasAttackProfiles;

            if (canAttack && agent.HasAnyAttackInRange)
            {
                TryStartAttack();
                return;
            }

            bool isMoving;
            if (dist > agent.Config.StopDistance)
            {
                // Combat 期間直接追真實玩家位置（透視追擊）— EnemyController 內部會用 ShortRecordTime/LongRecordTime
                // 紀錄 PointA/PointB，LongRecordTime 結束時 HasLostTarget=true 觸發 Combat → Search 轉移
                agent.SetDestination(agent.PlayerTransform.position);
                EnsureAnimation(chaseAnimation);
                isMoving = true;
            }
            else
            {
                agent.StopMovement();
                EnsureAnimation(EnemyAnimationType.Idle);
                isMoving = false;
            }

            if (!faceTargetWhileChasing) return;

            if (isMoving) FaceAlongPath();
            else FaceTowardsPlayer();
        }

        /// <summary>
        /// 朝 A* 路徑方向轉身（避免繞牆時往玩家方向直走撞牆卡住）。
        /// A* 還沒算出有效速度時 fallback 到朝玩家轉
        /// </summary>
        private void FaceAlongPath()
        {
            Vector3 pathDir = agent.DesiredVelocity;
            pathDir.y = 0f;
            if (pathDir.sqrMagnitude > 0.01f)
            {
                agent.SetFacingDirection(pathDir);
            }
            else
            {
                FaceTowardsPlayer();
            }
        }

        private void TickAttacking()
        {
            if (_attackFinishedFlag)
            {
                _attackFinishedFlag = false;
                _phase = Phase.Chasing;
                _hasCurrentAnim = false;
                return;
            }

            EnemyAttackProfile profile = _executor != null ? _executor.CurrentProfile : null;
            // 黃光（ParryFlashDuration）期間還能轉身追玩家 — 之後鎖死方向直到攻擊結束
            // 給玩家「黃光亮起就可以開始繞背」的明確規則，避免敵人無限轉向跟住玩家
            bool inParryFlash = profile != null && _executor.ElapsedTime < profile.ParryFlashDuration;
            if (inParryFlash)
            {
                FaceTowardsPlayer();
            }
            else
            {
                agent.ClearFacingDirection();
            }
        }

        private void TryStartAttack()
        {
            EnemyAttackProfile profile = agent.SelectNextAttack();
            if (profile == null || _executor == null) return;
            agent.StopMovement();
            FaceTowardsPlayer();
            bool started = _executor.Execute(profile);
            if (!started)
            {
                _cooldownTimer = 0.25f;
                return;
            }
            _phase = Phase.Attacking;
            _hasCurrentAnim = false;
            _attackFinishedFlag = false;
        }

        private void FaceTowardsPlayer()
        {
            Vector3 dir = agent.GetDirectionToPlayer();
            if (dir.sqrMagnitude > 0.01f) agent.SetFacingDirection(dir);
        }

        private void EnsureAnimation(EnemyAnimationType type)
        {
            if (_hasCurrentAnim && _currentAnim == type) return;
            agent.PlayAnimation(type);
            _currentAnim = type;
            _hasCurrentAnim = true;
        }
    }
}
