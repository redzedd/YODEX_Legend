using Animancer;
using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — 警覺反應
    /// 進入時：停下移動、播警覺動畫、播警覺音效、標記已偵測玩家、消費 Alert Entry 旗標
    /// 持續期間：每幀朝玩家方向轉身、檢查動畫 NormalizedTime
    /// 動畫播完（NormalizedTime >= 1）→ EndAction(true)，由 FSM 的 On Finish transition 自動接到 Combat
    /// 不再需要 WaitForSeconds — 動畫長度不同的攻擊招式不用手動同步
    /// </summary>
    [Category("Enemy AI/Combat")]
    [Name("Alert Reaction")]
    [Description("發現玩家時的警覺反應：停下、播警覺動畫與音效、持續朝玩家轉身；動畫播完自動 End → FSM 走 On Finish 接 Combat")]
    public class AlertReactionAction : ActionTask<EnemyController>
    {
        [Tooltip("動畫淡入時間（秒）")]
        public float fadeDuration = 0.15f;

        private AnimancerState _animState;

        protected override void OnExecute()
        {
            agent.MarkPlayerDetected();
            agent.ConsumeAlertEntryFlag();
            agent.StopMovement();
            _animState = agent.PlayAnimation(EnemyAnimationType.Alert, fadeDuration);
            agent.PlaySfx(agent.AlertSfx);
            // 若沒有 Alert clip（_animState == null）→ 立即 End，避免卡在這個 state
            if (_animState == null)
            {
                EndAction(true);
            }
        }

        protected override void OnUpdate()
        {
            Vector3 dir = agent.GetDirectionToPlayer();
            if (dir.sqrMagnitude > 0.01f)
            {
                agent.SetFacingDirection(dir);
            }
            // 動畫播完自動結束 — NormalizedTime >= 1 表示 clip 跑完一次
            if (_animState != null && _animState.NormalizedTime >= 1f)
            {
                EndAction(true);
            }
        }

        protected override void OnStop()
        {
            if (agent != null) agent.ClearFacingDirection();
            _animState = null;
        }
    }
}
