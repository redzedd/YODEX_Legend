using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — 在 FSM 狀態進入時，呼叫 EnemyController 播放指定動畫
    /// FSM 狀態裡放這個 Action 即可，無需另寫程式
    /// </summary>
    [Category("Enemy AI/Animation")]
    [Name("Play Enemy Animation")]
    [Description("由 EnemyController 透過 Animancer 播放指定的動畫類型。Action 在進入狀態時執行一次後即視為完成。")]
    public class PlayEnemyAnimation : ActionTask<EnemyController>
    {
        [Tooltip("要播放的動畫類型 — 對應 EnemyAnimationSet 中的剪輯")]
        public EnemyAnimationType animationType = EnemyAnimationType.Idle;

        [Tooltip("淡入時間（秒）— 建議 0.1~0.3，循環待機可設較長")]
        public float fadeDuration = 0.25f;

        protected override void OnExecute()
        {
            agent.PlayAnimation(animationType, fadeDuration);
            EndAction(true);
        }
    }
}
