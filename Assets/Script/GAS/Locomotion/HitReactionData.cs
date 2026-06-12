using Animancer;
using UnityEngine;

namespace Player.Locomotion
{
    /// <summary>
    /// 角色受擊反應資料 — 每個角色指派一份 SO,集中設定 4 方向受擊動畫與時序參數。
    /// 由 NewGASPlayerController 透過 Inspector 指派後塞入 LocomotionStateContext.HitReactionData,
    /// HitState 於 Enter 時讀取對應方向 clip 播放。
    /// </summary>
    [CreateAssetMenu(menuName = "Player/Locomotion/Hit Reaction Data", fileName = "HitReactionData")]
    public sealed class HitReactionData : ScriptableObject
    {
        [Header("受擊動畫(4 方向)")]
        [SerializeField, Tooltip("前方受擊 — 攻擊者來自角色正前方")]
        private ClipTransition _staggerFront;
        [SerializeField, Tooltip("後方受擊 — 攻擊者來自角色正後方")]
        private ClipTransition _staggerBack;
        [SerializeField, Tooltip("左方受擊 — 攻擊者來自角色左側")]
        private ClipTransition _staggerLeft;
        [SerializeField, Tooltip("右方受擊 — 攻擊者來自角色右側")]
        private ClipTransition _staggerRight;

        [Header("Stagger 時序")]
        [SerializeField, Tooltip("受擊硬直時長(秒)— 期間玩家無法操作,結束後自動回 Locomotion")]
        private float _stunDuration = 0.5f;
        [SerializeField, Tooltip("進入受擊動畫的 fade 時間")]
        private float _hitEnterFadeDuration = 0.05f;
        [SerializeField, Tooltip("受擊結束轉回 Idle / Walk / Run 的 fade 時間")]
        private float _stunExitFadeDuration = 0.15f;

        // Knockback 動畫流程:
        //   Phase 1 StaggerIntro:播 Stagger 四方向 clip 前段(StaggerIntroDuration 秒),顯示受擊方向
        //   Phase 2 Main       :播單支 Front-view Knockback clip,同時在 KnockbackEnterFadeDuration 期間
        //                        平滑旋轉角色朝向,讓單一 clip 能產生對應 4 方向的飛出效果;
        //                        旋轉完成時角色 forward 會對齊到「與 clip 向後倒相反的世界方向」,
        //                        使角色起身後面向不會因單一 clip 被鎖死。
        //   Phase 3 StandUp    :播起身 clip 後自動回 Idle
        [Header("Knockback(重擊 — HitContext.knockbackForce > 0 時取代 Stagger)")]
        [SerializeField, Tooltip("單支 Front-view Knockback clip — 由 KnockbackState 在進入 Main 時平滑旋轉角色以對應 4 方向")]
        private ClipTransition _knockback;
        [SerializeField, Tooltip("從地面爬起的動畫(單支)— Knockback 主動畫播完後接續播放,完成後回 Idle")]
        private ClipTransition _standUp;
        [SerializeField, Tooltip("Phase 1 StaggerIntro 播多久後 cross-fade 進入 Phase 2 Main(秒)")]
        private float _staggerIntroDuration = 0.2f;
        [SerializeField, Tooltip("從 StaggerIntro cross-fade 進 Knockback 主動畫的 fade 時間")]
        private float _knockbackEnterFadeDuration = 0.15f;
        [SerializeField, Tooltip("Knockback 進入 Main 階段後,角色從原朝向平滑旋轉至目標方向的時間(秒)。\n" +
                                   "與 KnockbackEnterFadeDuration 解耦:可設成比 fade 短讓旋轉快速完成," +
                                   "或設成比 fade 長讓旋轉延展到整個 Main 階段。0 表示瞬間旋轉")]
        private float _knockbackRotateDuration = 0.15f;
        [SerializeField, Tooltip("Knockback 動畫從第幾秒(Time)開始播放。\n" +
                                   "若 Knockback clip 前段有「站立反應」等冗餘部分(與 StaggerIntro 重複)," +
                                   "可設此值跳過該段直接進入主要飛出動作。設為 0 表示從頭播。\n" +
                                   "與 Dodge 的 DodgeReentryStartTime 同模式。")]
        private float _knockbackStartTime = 0f;
        [SerializeField, Tooltip("從 Knockback 主動畫 cross-fade 進 StandUp 的 fade 時間")]
        private float _standUpEnterFadeDuration = 0.15f;

        [Header("擊退位移")]
        [SerializeField, Tooltip("擊退 / 外力速度指數衰減時間常數(秒)。\n" +
                                   "衰減公式:v(t) = v₀ · e^(-t/τ);漸近總位移 ≈ v₀ · τ,95% 在 3τ 內完成。\n" +
                                   "值越小擊退越「緊」(短促急煞),越大越「黏」(拖尾)。0.08~0.12 為手感甜區。\n" +
                                   "擊退距離由各攻擊資料(AttackProfile / HitWindow)的 KnockbackForce 個別指定,不在此統一預設")]
        private float _externalVelocityDecayTau = 0.1f;

        [Header("Flinch(上半身疊加 — Poise 未擊破時播放)")]
        [SerializeField, Tooltip("前方輕擊 — 上半身後仰")] private ClipTransition _flinchFront;
        [SerializeField, Tooltip("後方輕擊 — 上半身前傾")] private ClipTransition _flinchBack;
        [SerializeField, Tooltip("左方輕擊 — 上半身向右晃")] private ClipTransition _flinchLeft;
        [SerializeField, Tooltip("右方輕擊 — 上半身向左晃")] private ClipTransition _flinchRight;
        [SerializeField, Tooltip("進入 Flinch 動畫的 fade 時間")]
        private float _flinchEnterFadeDuration = 0.08f;
        [SerializeField, Tooltip("Flinch clip 播完後,Flinch Layer 權重淡出至 0 的時間 — 讓上半身平順回到主 Layer")]
        private float _flinchLayerFadeOutDuration = 0.15f;

        public float StunDuration => _stunDuration;
        public float HitEnterFadeDuration => _hitEnterFadeDuration;
        public float StunExitFadeDuration => _stunExitFadeDuration;
        public ClipTransition Knockback => _knockback;
        public ClipTransition StandUp => _standUp;
        public float StaggerIntroDuration => _staggerIntroDuration;
        public float KnockbackEnterFadeDuration => _knockbackEnterFadeDuration;
        public float KnockbackRotateDuration => _knockbackRotateDuration;
        public float KnockbackStartTime => _knockbackStartTime;
        public float StandUpEnterFadeDuration => _standUpEnterFadeDuration;
        public float FlinchEnterFadeDuration => _flinchEnterFadeDuration;
        public float FlinchLayerFadeOutDuration => _flinchLayerFadeOutDuration;
        public float ExternalVelocityDecayTau => _externalVelocityDecayTau;

        /// <summary>
        /// 依受擊方向回傳對應 Stagger clip。缺漏方向會 fallback 到 Front,保證不會因 Inspector 漏填造成無動畫。
        /// </summary>
        public ClipTransition GetClip(HitDirection direction)
        {
            ClipTransition clip = direction switch
            {
                HitDirection.Front => _staggerFront,
                HitDirection.Back => _staggerBack,
                HitDirection.Left => _staggerLeft,
                HitDirection.Right => _staggerRight,
                _ => _staggerFront,
            };
            return clip != null ? clip : _staggerFront;
        }

        /// <summary>
        /// 依受擊方向回傳對應 Flinch clip。缺漏方向(包含 Front)會回傳 null — 呼叫端應視為「不播 Flinch」略過。
        /// 設計上允許部分方向不配置 Flinch(例如只配前後不配左右)。
        /// </summary>
        public ClipTransition GetFlinchClip(HitDirection direction)
        {
            ClipTransition clip = direction switch
            {
                HitDirection.Front => _flinchFront,
                HitDirection.Back => _flinchBack,
                HitDirection.Left => _flinchLeft,
                HitDirection.Right => _flinchRight,
                _ => _flinchFront,
            };
            return clip != null ? clip : _flinchFront;
        }
    }
}
