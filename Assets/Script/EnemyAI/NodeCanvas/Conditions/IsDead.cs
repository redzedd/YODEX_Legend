using NodeCanvas.Framework;
using ParadoxNotion.Design;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 敵人是否已死亡
    /// 常用於 AnyState → Dead 的全域中斷轉移
    /// </summary>
    [Category("Enemy AI/State")]
    [Name("Is Dead")]
    [Description("敵人是否已死亡（EnemyController.IsDead）")]
    public class IsDead : ConditionTask<EnemyController>
    {
        protected override string info => "Is Dead";

        protected override bool OnCheck()
        {
            return agent.IsDead;
        }
    }
}
