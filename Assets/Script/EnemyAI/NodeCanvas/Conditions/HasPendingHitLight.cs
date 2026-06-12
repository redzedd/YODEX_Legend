using NodeCanvas.Framework;
using ParadoxNotion.Design;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 是否有待處理的輕受擊事件
    /// 由 OnHit 在攻擊能打斷當前霸體時設定，由 PlayHitLightAction 進入時消費
    /// 常用於 Any State → HitLight 的全域中斷轉移
    /// </summary>
    [Category("Enemy AI/Reaction")]
    [Name("Has Pending Hit Light")]
    [Description("是否有待處理的輕受擊事件（EnemyController.HasPendingHitLight）")]
    public class HasPendingHitLight : ConditionTask<EnemyController>
    {
        protected override string info => "Has Pending Hit Light";

        protected override bool OnCheck()
        {
            return agent.HasPendingHitLight;
        }
    }
}
