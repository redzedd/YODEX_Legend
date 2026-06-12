using EnemyAI.Dragon;

namespace Boss.Dragon
{
    /// <summary>
    /// 飛龍睡眠狀態 — 開場前的等待姿勢
    /// 不再靠距離自動醒來:開場改由 DragonBossIntroSequence 在玩家進入 Trigger
    /// (或玩家提前攻擊沉睡飛龍) 時觸發,跑完開場演出後直接 ChangeState 進 Idle 戰鬥。
    /// 本狀態只負責播 Sleep 動畫並等待,OnUpdate 不做任何事。
    /// </summary>
    public class DragonSleepState : BossState
    {
        private readonly DragonBossController _controller;

        public DragonSleepState(DragonBossController controller)
        {
            _controller = controller;
        }

        public override void OnEnter()
        {
            _controller.PlayAnimation(_controller.Animations != null ? _controller.Animations.Sleep : null, 0.3f);
        }
    }
}
