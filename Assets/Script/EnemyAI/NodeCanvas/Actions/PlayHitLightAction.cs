using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — 進入 HitLight 狀態
    /// 流程：瞬切 HitLight 動畫 → 計時播完 → EndAction（由 FSM OnFinish 連線退出）
    /// 連續受擊：OnUpdate 偵測 HasPendingHitLight，重新 retrigger 動畫（無過渡），時間累加
    /// 動畫切換用 fadeDuration = 0 對應 ZZZ 的「無過渡瞬切」打擊感
    /// </summary>
    [Category("Enemy AI/Reaction")]
    [Name("Play Hit Light")]
    [Description("瞬切播放 HitLight 動畫。連續輕擊到敵人時會無過渡 retrigger 動畫達成壓制感")]
    public class PlayHitLightAction : ActionTask<EnemyController>
    {
        [Tooltip("HitLight 動畫播完判定的秒數 — 通常對齊動畫長度。期間若被連續輕擊會重置計時")]
        public float duration = 0.4f;

        private float _enterTime;

        protected override string info => $"Hit Light ({duration}s)";

        protected override void OnExecute()
        {
            StartReaction();
        }

        protected override void OnUpdate()
        {
            if (agent.HasPendingHitLight)
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
            agent.PlayAnimation(EnemyAnimationType.HitLight, 0f, restartIfSame: true);
            _enterTime = Time.time;
        }
    }
}
