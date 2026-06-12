using Animancer;
using UnityEngine;

namespace Player.Locomotion
{
    /// <summary>
    /// 玩家死亡資料 — 每個角色指派一份 SO,集中設定死亡動畫與時序。
    /// 由 NewGASPlayerController 透過 Inspector 指派後塞入 LocomotionStateContext.DeathData,
    /// DeathState 於 Enter 時讀取 DeathClip 播放。
    /// 未指派時 Die() 仍會執行其餘流程(Tag、UI、TimeScale),只是不播動畫。
    /// </summary>
    [CreateAssetMenu(menuName = "Player/Locomotion/Player Death Data", fileName = "PlayerDeathData")]
    public sealed class PlayerDeathData : ScriptableObject
    {
        [SerializeField, Tooltip("死亡動畫 — 進入 Dead 狀態時播放,播完凍結在最後一幀")]
        private ClipTransition _deathClip;
        [SerializeField, Tooltip("淡入死亡動畫的 fade 時間")]
        private float _deathEnterFadeDuration = 0.2f;
        [SerializeField, Tooltip("死亡畫面 UI 淡入前的等待時間(秒)— 留給死亡動畫播放的空間。\n" +
                                   "此值會覆蓋 DeathUIManager 的 _delayBeforeFade;採 UnscaledTime 計時,不受 Time.timeScale=0 影響")]
        private float _preUiDelay = 2f;

        public ClipTransition DeathClip => _deathClip;
        public float DeathEnterFadeDuration => _deathEnterFadeDuration;
        public float PreUiDelay => _preUiDelay;
    }
}
