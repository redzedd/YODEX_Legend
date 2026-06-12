using NodeCanvas.Framework;
using ParadoxNotion.Design;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 敵人是否要直接進 Combat State（跳過 Alert 動畫）
    /// 條件：已偵測到玩家 + (Alert 冷卻中 OR 由攻擊觸發 OR Alert 動畫已播完)
    /// 用於：
    ///   1. Idle/Patrol → Combat（冷卻中或脫戰被打）
    ///   2. Alert → Combat（Alert 動畫播完後，配 Wait For Seconds 接到本條件）
    /// 與 WantsAlertEntry 互斥（同一時刻只會有一個為 true）
    /// </summary>
    [Category("Enemy AI/Combat")]
    [Name("Wants Combat Entry")]
    [Description("敵人是否要直接進 Combat State（跳過 Alert 動畫）— Alert 冷卻中或脫戰被攻擊時為 true")]
    public class WantsCombatEntry : ConditionTask<EnemyController>
    {
        protected override string info => "Wants Combat entry";

        protected override bool OnCheck()
        {
            return agent.WantsCombatEntry;
        }
    }
}
