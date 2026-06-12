using UnityEngine;
using Animancer;

namespace EnemyAI.Dragon
{
    /// <summary>
    /// 飛龍 Boss 專屬動畫剪輯集合 (ScriptableObject)
    /// 所有飛龍相關系統 (Boss Controller / 攻擊招式 / 飛行控制) 都從這裡取動畫,
    /// 維持單一資料來源。動畫請全部使用 InPlace 版本 (位移由程式控制)
    /// </summary>
    [CreateAssetMenu(menuName = "YODEX/敵人/飛龍動畫集", fileName = "DragonAnimationSet")]
    public class DragonAnimationSet : ScriptableObject
    {
        #region Serialized Fields

        [Header("待機與覺醒")]
        [SerializeField] [Tooltip("睡眠待機 — Boss 戰開始前的預設姿勢 (玩家進入觸發區之前一直播)。建議 Loop")]
        private ClipTransition _sleep;

        [SerializeField] [Tooltip("咆哮 — 雙用:(1) 玩家觸發 Boss 戰時起身咆哮 (2) 隕石攻擊期間播放。不應為 Loop")]
        private ClipTransition _scream;

        [SerializeField] [Tooltip("站立待機 — 戰鬥中的閒置姿勢。建議 Loop")]
        private ClipTransition _idle;

        [Header("地面移動")]
        [SerializeField] [Tooltip("行走 (Loop) — 巡邏或慢速接近時使用")]
        private ClipTransition _walk;

        [SerializeField] [Tooltip("奔跑 (Loop) — 戰鬥追擊時使用")]
        private ClipTransition _run;

        [Header("受擊與死亡")]
        [SerializeField] [Tooltip("受擊反應 — 被打到時的短暫抖動 (建議 0.4~0.8 秒,不應為 Loop)")]
        private ClipTransition _getHit;

        [SerializeField] [Tooltip("死亡 — 不應為 Loop。動畫播完飛龍會停在最後一幀")]
        private ClipTransition _die;

        [Header("飛行")]
        [SerializeField] [Tooltip("起飛 — 一次性,從地面拔起到空中固定高度。不應為 Loop")]
        private ClipTransition _takeOff;

        [SerializeField] [Tooltip("空中懸停 (Loop) — 預設空中閒置姿勢,火球攻擊期間也可用此")]
        private ClipTransition _flyIdle;

        [SerializeField] [Tooltip("空中前進 (Loop) — 一般空中巡航")]
        private ClipTransition _flyForward;

        [SerializeField] [Tooltip("空中滑翔/快速移動 (Loop) — 用於俯衝噴火等高速空中位移")]
        private ClipTransition _flyGlide;

        [SerializeField] [Tooltip("降落 — 一次性,從空中回到地面。不應為 Loop")]
        private ClipTransition _landing;

        [Header("地面攻擊")]
        [SerializeField] [Tooltip("基本咬擊 — 單下咬,第一階段高機率使用")]
        private ClipTransition _basicAttack;

        [SerializeField] [Tooltip("爪擊二連擊 — 兩段判定,用 EnemyAttackProfile.ExtraHitboxes 設定第二段。第二階段高機率使用")]
        private ClipTransition _clawAttack;

        [SerializeField] [Tooltip("地面噴火攻擊 — 快速橫掃,第二階段地面期才使用")]
        private ClipTransition _flameAttack;

        [Header("空中攻擊")]
        [SerializeField] [Tooltip("空中橫掃噴火 — 向地面大範圍掃射的噴火攻擊")]
        private ClipTransition _flyFlameAttack;

        #endregion

        #region Public API

        public ClipTransition Sleep => _sleep;
        public ClipTransition Scream => _scream;
        public ClipTransition Idle => _idle;
        public ClipTransition Walk => _walk;
        public ClipTransition Run => _run;
        public ClipTransition GetHit => _getHit;
        public ClipTransition Die => _die;
        public ClipTransition TakeOff => _takeOff;
        public ClipTransition FlyIdle => _flyIdle;
        public ClipTransition FlyForward => _flyForward;
        public ClipTransition FlyGlide => _flyGlide;
        public ClipTransition Landing => _landing;
        public ClipTransition BasicAttack => _basicAttack;
        public ClipTransition ClawAttack => _clawAttack;
        public ClipTransition FlameAttack => _flameAttack;
        public ClipTransition FlyFlameAttack => _flyFlameAttack;

        #endregion
    }
}
