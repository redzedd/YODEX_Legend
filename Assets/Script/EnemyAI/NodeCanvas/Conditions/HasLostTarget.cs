using NodeCanvas.Framework;
using ParadoxNotion.Design;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 敵人是否已丟失目標
    /// 條件：戰鬥中 + 視線中斷已超過寬限期 + 有最後已知位置
    /// 用於 Combat → Search 的轉移
    /// </summary>
    [Category("Enemy AI/Perception")]
    [Name("Has Lost Target")]
    [Description("敵人是否已丟失目標（戰鬥中視野中斷且寬限期已過）— 用於 Combat → Search 轉移")]
    public class HasLostTarget : ConditionTask<EnemyController>
    {
        protected override string info => "Has lost target";

        protected override bool OnCheck()
        {
            return agent.HasLostTarget;
        }
    }
}
