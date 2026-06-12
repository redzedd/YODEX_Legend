using NodeCanvas.Framework;
using ParadoxNotion.Design;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 敵人當前是否能看到玩家
    /// 直接讀 EnemyController.CanSeePlayer（由 EnemyVisionSensor 每幀更新）
    /// </summary>
    [Category("Enemy AI/Perception")]
    [Name("Can See Player")]
    [Description("敵人當前是否能看到玩家（扇形視野 + 視線遮蔽）。常用於 Idle/Patrol → Alert 的轉移條件")]
    public class CanSeePlayer : ConditionTask<EnemyController>
    {
        protected override string info => "Can see player";

        protected override bool OnCheck()
        {
            return agent.CanSeePlayer;
        }
    }
}
