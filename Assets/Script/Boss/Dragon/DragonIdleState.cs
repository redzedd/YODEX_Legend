using UnityEngine;
using Enemy.AttackSystem;
using EnemyAI.Dragon;

namespace Boss.Dragon
{
    /// <summary>
    /// 飛龍戰鬥待機狀態 — 決策中樞 (先選招再就位)
    /// 站定面向玩家,冷卻到 → 依權重抽一招「意圖招式」(不分距離):
    ///   • 已在該招射程內 → 直接放
    ///   • 太遠 → 帶著這招去 Chase,追到該招射程才放 (遠程招不必貼身)
    ///   • 太近 (抽到遠程但玩家貼身) → fallback 改放當下距離可用的招 (通常是近戰)
    /// </summary>
    public class DragonIdleState : BossState
    {
        // 沒有任何可用招式時 (清單空 / 全 0 權重),逼近到此緩衝外才追,避免邊界抖動
        private const float CHASE_TRIGGER_BUFFER = 1f;
        // 找不到可用招式時,延後重試的時間
        private const float NO_VALID_ATTACK_RETRY_DELAY = 0.5f;

        private readonly DragonBossController _controller;
        private float _attackCooldownTimer;

        public DragonIdleState(DragonBossController controller)
        {
            _controller = controller;
        }

        public override void OnEnter()
        {
            _controller.PlayAnimation(_controller.Animations != null ? _controller.Animations.Idle : null, 0.3f);
            _controller.Locomotion.Stop();
            if (_controller.Player != null && _controller.Boss != null && _controller.Boss.Config != null)
            {
                Vector3 toPlayer = _controller.Player.position - _controller.transform.position;
                _controller.Locomotion.SetFacing(toPlayer, _controller.Boss.Config.GroundRotationSpeed);
            }
            // 進 Idle 重置攻擊冷卻 (打完一招回到 Idle 要等 AttackInterval 才開新一招)
            _attackCooldownTimer = _controller.AttackInterval;
        }

        public override void OnUpdate()
        {
            if (_controller.Player == null) return;
            if (_controller.Boss == null || _controller.Boss.Config == null) return;

            Vector3 toPlayer = _controller.Player.position - _controller.transform.position;
            _controller.Locomotion.SetFacing(toPlayer, _controller.Boss.Config.GroundRotationSpeed);

            _attackCooldownTimer -= Time.deltaTime;
            if (_attackCooldownTimer > 0f) return;

            DecideAction();
        }

        private void DecideAction()
        {
            float edgeDist = _controller.EdgeDistanceToPlayer;

            // 依權重抽「意圖招式」(不分距離)
            EnemyAttackProfile intent = _controller.SelectAttackByWeight();
            if (intent == null)
            {
                // 沒有可用招式 → 退回預設行為:逼近到停止距離
                if (edgeDist > _controller.Boss.Config.GroundStopDistance + CHASE_TRIGGER_BUFFER)
                {
                    _controller.ChaseState.SetApproachTarget(null);
                    _controller.ChangeState(_controller.ChaseState);
                }
                else
                {
                    _attackCooldownTimer = NO_VALID_ATTACK_RETRY_DELAY;
                }
                return;
            }

            // 已在意圖招射程內 → 直接放
            if (DragonBossController.IsWithinAttackRange(intent, edgeDist))
            {
                _controller.AttackState.SetNextAttack(intent);
                _controller.ChangeState(_controller.AttackState);
                return;
            }

            // 太遠 → 帶著這招去追,追到射程才放
            if (edgeDist > intent.MaxPickDistance)
            {
                _controller.ChaseState.SetApproachTarget(intent);
                _controller.ChangeState(_controller.ChaseState);
                return;
            }

            // 太近,意圖招放不出 (抽到遠程但玩家貼身) → 改放當下距離可用的招
            EnemyAttackProfile eligible = _controller.SelectAttack();
            if (eligible != null)
            {
                _controller.AttackState.SetNextAttack(eligible);
                _controller.ChangeState(_controller.AttackState);
            }
            else
            {
                _attackCooldownTimer = NO_VALID_ATTACK_RETRY_DELAY;
            }
        }
    }
}
