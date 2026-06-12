using System.Collections.Generic;

namespace Enemy.AttackSystem
{
    /// <summary>
    /// 持有 EnemyAttackProfile 清單的物件介面
    /// 給編輯器 (EnemyAttackProfileTimelineWindow) 找場景中「使用某招式」的物件用
    /// 實作者:EnemyController (雜兵) / DragonBossController (Boss) / 未來其他 Boss controller
    /// 這層抽象讓 Editor 不必 hard-code 具體型別,新 Boss 只要實作介面就能被預覽找到
    /// </summary>
    public interface IAttackProfileHost
    {
        /// <summary>當前可用的攻擊招式清單</summary>
        IReadOnlyList<EnemyAttackProfile> AttackProfiles { get; }
    }
}
