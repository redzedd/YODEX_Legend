using Animancer;
using UnityEngine;

namespace Player.Locomotion
{
    /// <summary>
    /// 儲存所有移動狀態使用的 Animancer Transition。由狀態類別取用而不直接參考動畫 clip。
    /// </summary>
    [CreateAssetMenu(menuName = "Player/Locomotion/Locomotion Animation Set", fileName = "LocomotionAnimationSet")]
    public sealed class LocomotionAnimationSet : ScriptableObject
    {
        [Header("待機")]
        [SerializeField] private ClipTransition _idle;

        [Header("走路")]
        [SerializeField] private ClipTransition _walkStart;
        [SerializeField] private ClipTransition _walkLoop;
        [SerializeField] private ClipTransition _walkEnd;

        [Header("跑步")]
        [SerializeField] private ClipTransition _runStart;
        [SerializeField] private ClipTransition _runLoop;
        [SerializeField] private ClipTransition _runEnd;

        [Header("快跑")]
        [SerializeField] private ClipTransition _fastRunStart;
        [SerializeField] private ClipTransition _fastRunEnd;
        [SerializeField, Tooltip("Turn 後若無輸入的過渡停止動畫；可與 FastRunEnd 指派相同 clip")]
        private ClipTransition _fastRunStop;
        [SerializeField, Tooltip("向左迴轉動畫（輸入方向在角色左側時使用）")]
        private ClipTransition _fastRunTurnLeft;
        [SerializeField, Tooltip("向右迴轉動畫（輸入方向在角色右側時使用）")]
        private ClipTransition _fastRunTurnRight;
        [SerializeField, Tooltip("快跑 loop 混合：Threshold -1=LeanLeft、0=FastRunLoop、1=LeanRight")]
        private LinearMixerTransition _fastRunLoopMixer;

        [Header("跳躍")]
        [SerializeField, Tooltip("起跳前搖動畫")] private ClipTransition _jumpStart;
        [SerializeField, Tooltip("滯空 Loop，應設為 Looping")] private ClipTransition _jumpLoop;
        [SerializeField, Tooltip("落地收尾動畫")] private ClipTransition _jumpEnd;

        [Header("滑翔翼")]
        [SerializeField, Tooltip("滑翔翼動畫 — 全身單一動畫,展開時播放,收起時淡出。\n" +
                                   "建議放雙手抓握滑翔翼 + 雙腳下垂 + 軀幹微傾的全身循環動畫,請設為 Looping。")]
        private ClipTransition _glider;

        [Header("閃避（8 方向）")]
        [Tooltip("以角色當前面向（藍線）為基準,依搖桿輸入方向（紅線）的 Signed Angle 決定播放哪個方向的 RM clip。\n" +
                 "角度區間:\n" +
                 "  0°±22.5°           → DodgeForward（正前）\n" +
                 "  +22.5° ~ +67.5°    → DodgeForwardRight（右前）\n" +
                 "  +67.5° ~ +112.5°   → DodgeRight（正右）\n" +
                 "  +112.5° ~ +157.5°  → DodgeBackRight（右後）\n" +
                 "  ±157.5° ~ ±180°    → Backstep（正後,與無輸入共用）\n" +
                 "  -157.5° ~ -112.5°  → DodgeBackLeft（左後）\n" +
                 "  -112.5° ~ -67.5°   → DodgeLeft（正左）\n" +
                 "  -67.5° ~ -22.5°    → DodgeForwardLeft（左前）\n" +
                 "缺任何一個方向的 clip 皆會 fallback 至 DodgeForward。")]
        [SerializeField] private ClipTransition _dodgeForward;
        [SerializeField] private ClipTransition _dodgeForwardRight;
        [SerializeField] private ClipTransition _dodgeRight;
        [SerializeField] private ClipTransition _dodgeBackRight;
        [SerializeField] private ClipTransition _dodgeBackLeft;
        [SerializeField] private ClipTransition _dodgeLeft;
        [SerializeField] private ClipTransition _dodgeForwardLeft;
        [SerializeField, Tooltip("後撤 — 兩種情境共用:\n" +
                                   "  (a) 無移動輸入時的 Backstep\n" +
                                   "  (b) 有輸入且方向指向角色正後方（±157.5° ~ ±180°）的 DodgeBack")]
        private ClipTransition _backstep;

        public ClipTransition Idle => _idle;
        public ClipTransition WalkStart => _walkStart;
        public ClipTransition WalkLoop => _walkLoop;
        public ClipTransition WalkEnd => _walkEnd;
        public ClipTransition RunStart => _runStart;
        public ClipTransition RunLoop => _runLoop;
        public ClipTransition RunEnd => _runEnd;
        public ClipTransition FastRunStart => _fastRunStart;
        public ClipTransition FastRunEnd => _fastRunEnd;
        public ClipTransition FastRunStop => _fastRunStop;
        public ClipTransition FastRunTurnLeft => _fastRunTurnLeft;
        public ClipTransition FastRunTurnRight => _fastRunTurnRight;
        public LinearMixerTransition FastRunLoopMixer => _fastRunLoopMixer;
        public ClipTransition JumpStart => _jumpStart;
        public ClipTransition JumpLoop => _jumpLoop;
        public ClipTransition JumpEnd => _jumpEnd;
        public ClipTransition Glider => _glider;
        public ClipTransition DodgeForward => _dodgeForward;
        public ClipTransition DodgeForwardRight => _dodgeForwardRight;
        public ClipTransition DodgeRight => _dodgeRight;
        public ClipTransition DodgeBackRight => _dodgeBackRight;
        public ClipTransition DodgeBackLeft => _dodgeBackLeft;
        public ClipTransition DodgeLeft => _dodgeLeft;
        public ClipTransition DodgeForwardLeft => _dodgeForwardLeft;
        public ClipTransition Backstep => _backstep;
    }
}
