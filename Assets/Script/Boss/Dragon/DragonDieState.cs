using EnemyAI.Dragon;

namespace Boss.Dragon
{
    /// <summary>
    /// 飛龍死亡狀態 — 永久 (不會切到其他狀態)
    /// 播 Die 動畫,停止移動與轉身
    /// </summary>
    public class DragonDieState : BossState
    {
        private readonly DragonBossController _controller;

        public DragonDieState(DragonBossController controller)
        {
            _controller = controller;
        }

        public override void OnEnter()
        {
            _controller.PlayAnimation(_controller.Animations != null ? _controller.Animations.Die : null, 0.3f);
            _controller.Locomotion.Stop();
            _controller.Locomotion.ClearFacing();
        }
    }
}
