using NodeCanvas.Framework;
using ParadoxNotion.Design;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 是否有待處理的重受擊事件
    /// 由 OnHit 判定重攻擊命中時設定，由 PlayHitHeavyAction 進入時消費
    /// 常用於 Any State → HitHeavy 的全域中斷轉移
    /// </summary>
    [Category("Enemy AI/Reaction")]
    [Name("Has Pending Hit Heavy")]
    [Description("是否有待處理的重受擊事件（EnemyController.HasPendingHitHeavy）")]
    public class HasPendingHitHeavy : ConditionTask<EnemyController>
    {
        protected override string info => "Has Pending Hit Heavy";

        protected override bool OnCheck()
        {
            return agent.HasPendingHitHeavy;
        }
    }
}
