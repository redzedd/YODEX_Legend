using NodeCanvas.Framework;
using ParadoxNotion.Design;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 敵人是否要先進 Alert State 播警覺動畫
    /// 條件：已偵測到玩家 + Alert 不在冷卻中 + 尚未進戰鬥
    /// 用於 Idle/Patrol → Alert 的轉移
    /// 與 WantsCombatEntry 互斥（同一時刻只會有一個為 true）
    /// </summary>
    [Category("Enemy AI/Combat")]
    [Name("Wants Alert Entry")]
    [Description("敵人是否要先進 Alert State 播警覺動畫（Alert 不在冷卻時才為 true）")]
    public class WantsAlertEntry : ConditionTask<EnemyController>
    {
        protected override string info => "Wants Alert entry";

        protected override bool OnCheck()
        {
            return agent.WantsAlertEntry;
        }
    }
}
