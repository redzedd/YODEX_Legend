using Enemy.AttackSystem;
using EnemyAI.Dragon;

namespace Boss.Dragon
{
    /// <summary>
    /// 飛龍追擊/就位狀態 — 追玩家位置,追到「目標招式的射程」就停下放招
    /// 目標招由 IdleState 決策後透過 SetApproachTarget 傳入:
    ///   • 有目標招 → 邊緣距離 <= 該招 MaxPickDistance 即放招 (遠程招追到射程就放,不必貼身)
    ///   • 無目標招 (預設) → 追到 GroundStopDistance 回 Idle 重新決策
    /// 移動由 BossGroundLocomotion 處理 (Root Motion 推進,MoveTo 只負責面向),動畫為 Run
    /// </summary>
    public class DragonChaseState : BossState
    {
        private readonly DragonBossController _controller;
        private EnemyAttackProfile _approachTarget;

        public DragonChaseState(DragonBossController controller)
        {
            _controller = controller;
        }

        /// <summary>設定這次追擊要就位的目標招式 — 追到該招射程內就停下放招。null = 追到 Config 停止距離回 Idle</summary>
        public void SetApproachTarget(EnemyAttackProfile target)
        {
            _approachTarget = target;
        }

        public override void OnEnter()
        {
            _controller.PlayAnimation(_controller.Animations != null ? _controller.Animations.Run : null, 0.2f);
        }

        public override void OnUpdate()
        {
            if (_controller.Player == null)
            {
                _controller.ChangeState(_controller.IdleState);
                return;
            }
            if (_controller.Boss == null || _controller.Boss.Config == null) return;

            BossConfig config = _controller.Boss.Config;
            float edgeDist = _controller.EdgeDistanceToPlayer;

            if (_approachTarget != null)
            {
                // 追到目標招射程 → 直接放 (此回合已在 Idle 等過 AttackInterval,不再等待)
                if (edgeDist <= _approachTarget.MaxPickDistance)
                {
                    _controller.AttackState.SetNextAttack(_approachTarget);
                    _controller.ChangeState(_controller.AttackState);
                    return;
                }
            }
            else if (edgeDist <= config.GroundStopDistance)
            {
                _controller.ChangeState(_controller.IdleState);
                return;
            }

            _controller.Locomotion.MoveTo(
                _controller.Player.position,
                config.RunSpeed,
                _controller.CenterStopDistance,
                config.GroundRotationSpeed);
        }

        public override void OnExit()
        {
            _controller.Locomotion.Stop();
        }
    }
}
