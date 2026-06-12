using UnityEngine;
using Enemy.AttackSystem;
using EnemyAI.Dragon;

namespace Boss.Dragon
{
    /// <summary>
    /// 飛龍攻擊狀態 — 執行一個 EnemyAttackProfile
    /// 由 IdleState 在冷卻倒數結束時呼叫 SetNextAttack 後切到此狀態
    /// Executor 跑完 (IsAttacking = false) 自動切回 Idle
    /// </summary>
    public class DragonAttackState : BossState
    {
        private readonly DragonBossController _controller;
        private EnemyAttackProfile _pendingAttack;

        public DragonAttackState(DragonBossController controller)
        {
            _controller = controller;
        }

        /// <summary>由 IdleState 呼叫,設定下次要執行的招式</summary>
        public void SetNextAttack(EnemyAttackProfile profile)
        {
            _pendingAttack = profile;
        }

        public override void OnEnter()
        {
            _controller.Locomotion.Stop();
            if (_controller.Player != null && _controller.Boss != null && _controller.Boss.Config != null)
            {
                Vector3 toPlayer = _controller.Player.position - _controller.transform.position;
                _controller.Locomotion.SetFacing(toPlayer, _controller.Boss.Config.GroundRotationSpeed);
            }

            if (_pendingAttack == null || _controller.AttackExecutor == null)
            {
                _controller.ChangeState(_controller.IdleState);
                return;
            }

            bool started = _controller.AttackExecutor.Execute(_pendingAttack);
            if (!started)
            {
                _controller.ChangeState(_controller.IdleState);
            }
        }

        public override void OnUpdate()
        {
            if (_controller.AttackExecutor == null || !_controller.AttackExecutor.IsAttacking)
            {
                _controller.ChangeState(_controller.IdleState);
            }
        }

        public override void OnExit()
        {
            _controller.Locomotion.ClearFacing();
            // 異常切換 (如死亡) 時 Cancel 進行中的攻擊;正常結束時 IsAttacking 已是 false,不會進這條
            if (_controller.AttackExecutor != null && _controller.AttackExecutor.IsAttacking)
            {
                _controller.AttackExecutor.Cancel();
            }
        }
    }
}
