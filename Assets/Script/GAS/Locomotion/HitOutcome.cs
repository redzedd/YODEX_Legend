using Animancer;

namespace Player.Locomotion
{
    /// <summary>
    /// 受擊反應決策結果 — Controller 依 HitContext 計算出的最終動畫 / 時長 / 方向,
    /// 透過 LocomotionStateContext.PendingHitOutcome 傳遞給 HitState.Enter 消費。
    /// </summary>
    public struct HitOutcome
    {
        /// <summary>要播放的受擊動畫</summary>
        public ClipTransition Clip;
        /// <summary>硬直時長(秒)— HitState 計時器結束後才允許轉場</summary>
        public float StunDuration;
        /// <summary>進入受擊動畫的 fade 時間</summary>
        public float EnterFadeDuration;
        /// <summary>計算出的受擊方向(供除錯 / 未來特效使用)</summary>
        public HitDirection Direction;
    }
}
