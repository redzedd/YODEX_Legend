using UnityEngine;
using UnityEngine.Events;

namespace GAS
{
    /// <summary>
    /// 遠程武器動畫事件接收器
    /// 掛在帶有 Animator 的武器模型(如 Bow Prefab)上,負責接收動畫內嵌的 AnimationEvent,
    /// 避免「has no receiver! Are you missing a component?」警告。
    ///
    /// 預設僅作為靜默接收;如要實際反應(發射 VFX、SFX、觸發 GAS event 等),
    /// 可在 Inspector 把對應 UnityEvent 接到外部 Listener。
    ///
    /// 注意:GA_RangedAttack 的實際發射時機由 RangedAttackData.FireTime 驅動(資料驅動),
    /// 此元件只負責「動畫師留在 clip 內的事件」轉接,不取代 FireTime 邏輯。
    /// </summary>
    public class RangedAnimationEventReceiver : MonoBehaviour
    {
        [Tooltip("動畫播到 OnFireArrowEvent 的時候會 invoke 這個 UnityEvent。\n常見用途:箭離弦的 VFX/SFX 觸發。\n留空也沒關係 — 此元件主要目的是消滅 has no receiver 警告。")]
        public UnityEvent OnFireArrow;

        [Tooltip("動畫播到 OnDrawArrowEvent 的時候會 invoke 這個 UnityEvent。\n常見用途:拉弓音效。")]
        public UnityEvent OnDrawArrow;

        // === 以下是 AnimationEvent 用 SendMessage 風格呼叫的接收方法 ===
        // 方法名必須與 .anim clip 內 AnimationEvent 的 Function 欄位完全一致

        /// <summary>箭離弦的時刻(由 Aim_the_Target_End 等發射動畫嵌入)</summary>
        public void OnFireArrowEvent()
        {
            OnFireArrow?.Invoke();
        }

        /// <summary>拉弓的時刻(預留,可由蓄力動畫嵌入)</summary>
        public void OnDrawArrowEvent()
        {
            OnDrawArrow?.Invoke();
        }
    }
}
