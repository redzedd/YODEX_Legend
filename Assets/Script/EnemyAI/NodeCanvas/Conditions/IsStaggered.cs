using NodeCanvas.Framework;
using ParadoxNotion.Design;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 敵人是否處於硬直狀態
    /// 常用於 AnyState → Stagger 的全域中斷轉移
    /// </summary>
    [Category("Enemy AI/State")]
    [Name("Is Staggered")]
    [Description("敵人是否處於硬直狀態（EnemyController.IsStaggered）")]
    public class IsStaggered : ConditionTask<EnemyController>
    {
        protected override string info => "Is Staggered";

        protected override bool OnCheck()
        {
            return agent.IsStaggered;
        }
    }
}
