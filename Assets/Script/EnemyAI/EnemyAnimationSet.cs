using UnityEngine;
using Animancer;

namespace EnemyAI
{
    /// <summary>
    /// 敵人動畫類型 — FSM/BT 透過此 enum 選擇要播放的動畫
    /// 之後加新動畫請同步擴充本 enum 與 EnemyAnimationSet.GetClip
    /// </summary>
    public enum EnemyAnimationType
    {
        Idle = 0,
        Walk = 1,
        Alert = 2,
        Stagger = 3,
        Death = 4,
        HitLight = 5,
        HitHeavy = 6,
        Run = 7,
        LookAround = 8,
        PatrolWait = 9,
    }

    /// <summary>
    /// 敵人動畫剪輯集合（ScriptableObject）
    /// 集中管理所有 FSM 狀態會用到的動畫，設計師可複製變體做不同怪物
    /// </summary>
    [CreateAssetMenu(menuName = "EnemyAI/Enemy Animation Set", fileName = "EnemyAnimationSet")]
    public class EnemyAnimationSet : ScriptableObject
    {
        #region Serialized Fields

        [Header("基本動畫")]
        [SerializeField] [Tooltip("待機動畫 — Idle 狀態使用，建議為 Looping 動畫")]
        private ClipTransition _idle;

        [SerializeField] [Tooltip("行走動畫 — Patrol 巡邏時使用。動畫本身的 Root Motion 速度建議接近 EnemyConfig.WalkSpeed")]
        private ClipTransition _walk;

        [SerializeField] [Tooltip("奔跑動畫 — Combat 追擊、Search 跑去最後位置時使用。沒設定的話 fallback 到 Walk。動畫 Root Motion 建議接近 EnemyConfig.RunSpeed")]
        private ClipTransition _run;

        [SerializeField] [Tooltip("警覺動畫 — Alert 狀態使用（發現玩家時的驚訝/咆哮動作），通常為一次性動畫")]
        private ClipTransition _alert;

        [SerializeField] [Tooltip("環顧動畫 — Search 狀態抵達失蹤點後的環顧動作（建議為 Loop 動畫，內含左右擺頭骨骼動畫）。沒設定的話 fallback 到 Idle")]
        private ClipTransition _lookAround;

        [SerializeField] [Tooltip("巡邏停留動畫 — Patrol 抵達路徑點等待時播放的動作（嗅探 / 環視 / 整理姿勢 之類）。沒設定的話 fallback 到 Idle")]
        private ClipTransition _patrolWait;

        [SerializeField] [Tooltip("硬直動畫 — 受擊韌性歸零時播放，通常為一次性動畫")]
        private ClipTransition _stagger;

        [SerializeField] [Tooltip("死亡動畫 — 生命值歸零時播放，不應為 Looping")]
        private ClipTransition _death;

        [Header("受擊反應")]
        [SerializeField] [Tooltip("輕受擊動畫 — 一般攻擊命中時瞬切播放（建議 0.3~0.5 秒短動畫）")]
        private ClipTransition _hitLight;

        [SerializeField] [Tooltip("重受擊動畫 — 重攻擊命中時瞬切播放，能打斷攻擊動作（建議 0.5~0.9 秒）")]
        private ClipTransition _hitHeavy;

        #endregion

        #region Public API

        /// <summary>
        /// 依類型取得對應 ClipTransition；Run/LookAround/PatrolWait 未設定 fallback 到對應動畫，
        /// 其他欄位未設定時回傳 null
        /// </summary>
        public ClipTransition GetClip(EnemyAnimationType type)
        {
            return type switch
            {
                EnemyAnimationType.Idle => _idle,
                EnemyAnimationType.Walk => _walk,
                EnemyAnimationType.Run => (_run != null && _run.Clip != null) ? _run : _walk,
                EnemyAnimationType.Alert => _alert,
                EnemyAnimationType.LookAround => (_lookAround != null && _lookAround.Clip != null) ? _lookAround : _idle,
                EnemyAnimationType.PatrolWait => (_patrolWait != null && _patrolWait.Clip != null) ? _patrolWait : _idle,
                EnemyAnimationType.Stagger => _stagger,
                EnemyAnimationType.Death => _death,
                EnemyAnimationType.HitLight => _hitLight,
                EnemyAnimationType.HitHeavy => _hitHeavy,
                _ => null
            };
        }

        #endregion
    }
}
