using Animancer;
using UnityEngine;
using EnemyAI.Dragon;

namespace Boss.Dragon
{
    /// <summary>
    /// 飛龍 Scream 狀態 — 雙用:
    /// (1) Boss 戰開場 (Sleep 醒過來) 第一招強制走這個 → 播 Scream + 觸發隕石
    /// (2) 戰鬥中低機率插招 (第 3c 步加進 Idle 內的機率判斷)
    /// 結束條件:Scream 動畫播完。MeteorAttack 是並行的副作用,結束時仍在背景跑
    /// </summary>
    public class DragonScreamState : BossState
    {
        // 沒抓到動畫長度時 fallback 用的等待時間 (秒)
        private const float FALLBACK_DURATION = 2f;

        private readonly DragonBossController _controller;
        private float _remainingTime;

        public DragonScreamState(DragonBossController controller)
        {
            _controller = controller;
        }

        public override void OnEnter()
        {
            _controller.Locomotion.Stop();
            if (_controller.Player != null && _controller.Boss != null && _controller.Boss.Config != null)
            {
                Vector3 toPlayer = _controller.Player.position - _controller.transform.position;
                _controller.Locomotion.SetFacing(toPlayer, _controller.Boss.Config.GroundRotationSpeed);
            }

            // 播 Scream 動畫,用實際長度當倒數
            ClipTransition screamClip = _controller.Animations != null ? _controller.Animations.Scream : null;
            AnimancerState animState = _controller.PlayAnimation(screamClip, 0.2f);
            _remainingTime = animState != null ? animState.Length : FALLBACK_DURATION;

            // 觸發隕石攻擊 (並行進行,不會卡 Scream 狀態結束)
            if (_controller.MeteorAttack != null)
            {
                _controller.MeteorAttack.Execute();
            }
        }

        public override void OnUpdate()
        {
            _remainingTime -= Time.deltaTime;
            if (_remainingTime <= 0f)
            {
                _controller.ChangeState(_controller.IdleState);
            }
        }

        public override void OnExit()
        {
            _controller.Locomotion.ClearFacing();
        }
    }
}
