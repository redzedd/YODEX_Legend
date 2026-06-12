using Animancer;
using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — 進入 Stagger 狀態時呼叫
    /// OnExecute：播放 Stagger 動畫
    /// OnUpdate：依 duration 欄位決定結束時機：
    ///   duration > 0 → 程式計時到秒數 EndAction（適合 Loop 動畫）
    ///   duration = 0 → 用動畫 NormalizedTime >= 1 判斷（適合 non-loop 動畫）
    /// OnStop：呼叫 EnemyController.EndStagger 解除硬直旗標
    /// 副作用（取消攻擊、停移動、重置 Poise）由 EnemyController.TriggerStagger 處理
    /// </summary>
    [Category("Enemy AI/Reaction")]
    [Name("Play Stagger")]
    [Description("播放 Stagger 動畫；依 duration 欄位用程式計時或動畫 NormalizedTime 結束 → FSM 用 On Finish 接到 Combat")]
    public class PlayStaggerAction : ActionTask<EnemyController>
    {
        [Tooltip("Stagger 持續秒數（程式計時，不依賴動畫長度）。\n0 = 用動畫 NormalizedTime 判斷結束（適合 non-loop 動畫）\n> 0 = 用此秒數計時（適合 Loop 動畫）\n建議 0.5~1.5")]
        public float duration = 1f;

        private AnimancerState _animState;
        private float _elapsedTime;

        protected override string info => duration > 0f
            ? $"Play Stagger ({duration:F1}s)"
            : "Play Stagger (anim length)";

        protected override void OnExecute()
        {
            _animState = agent.PlayAnimation(EnemyAnimationType.Stagger);
            _elapsedTime = 0f;
            // 沒設 Stagger clip 且沒用 duration → 立即結束避免卡 state
            if (_animState == null && duration <= 0f)
            {
                EndAction(true);
            }
        }

        protected override void OnUpdate()
        {
            _elapsedTime += Time.deltaTime;
            if (duration > 0f)
            {
                if (_elapsedTime >= duration) EndAction(true);
            }
            else if (_animState != null && _animState.NormalizedTime >= 1f)
            {
                EndAction(true);
            }
        }

        protected override void OnStop()
        {
            if (agent != null && agent.IsStaggered)
            {
                agent.EndStagger();
            }
            _animState = null;
        }
    }
}
