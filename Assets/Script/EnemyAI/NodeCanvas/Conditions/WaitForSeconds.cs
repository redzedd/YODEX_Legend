using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Condition — 進入當前狀態經過指定秒數後成立
    /// 用於 FSM Transition，例如「在 Idle 站 3 秒後切到 Patrol」
    /// </summary>
    [Category("Enemy AI/Timing")]
    [Name("Wait For Seconds")]
    [Description("FSM 進入此狀態後經過指定秒數後此條件成立，常用於計時轉移")]
    public class WaitForSeconds : ConditionTask
    {
        [Tooltip("需等待的秒數")]
        public float duration = 3f;

        private float _enterTime;

        protected override string info => $"Waited {duration}s";

        protected override void OnEnable()
        {
            _enterTime = Time.time;
        }

        protected override bool OnCheck()
        {
            return (Time.time - _enterTime) >= duration;
        }
    }
}
