using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — 進入 HitHeavy 狀態
    /// 與 PlayHitLight 邏輯相同，僅動畫類型不同（HitHeavy 通常動畫較長且後仰更明顯）
    /// 連續重擊也會無過渡 retrigger 動畫
    /// </summary>
    [Category("Enemy AI/Reaction")]
    [Name("Play Hit Heavy")]
    [Description("瞬切播放 HitHeavy 動畫。連續重擊到敵人時會無過渡 retrigger 動畫達成壓制感")]
    public class PlayHitHeavyAction : ActionTask<EnemyController>
    {
        [Tooltip("HitHeavy 動畫播完判定的秒數 — 通常對齊動畫長度。期間若被連續重擊會重置計時")]
        public float duration = 0.7f;

        private float _enterTime;

        protected override string info => $"Hit Heavy ({duration}s)";

        protected override void OnExecute()
        {
            StartReaction();
        }

        protected override void OnUpdate()
        {
            if (agent.HasPendingHitHeavy)
            {
                StartReaction();
            }
            if (Time.time - _enterTime >= duration)
            {
                EndAction(true);
            }
        }

        protected override void OnStop()
        {
            if (agent != null) agent.ConsumePendingHitReactions();
        }

        private void StartReaction()
        {
            agent.ConsumePendingHitReactions();
            if (agent.AttackExecutor != null && agent.AttackExecutor.IsAttacking)
            {
                agent.AttackExecutor.Cancel();
            }
            agent.StopMovement();
            agent.ClearFacingDirection();
            agent.PlayAnimation(EnemyAnimationType.HitHeavy, 0f, restartIfSame: true);
            _enterTime = Time.time;
        }
    }
}
