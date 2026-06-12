using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 互動處理器抽象基底 — 定義互動執行邏輯的契約
    /// 掛在與 GenericInteractable 相同的 GameObject 或子物件上
    /// 由 GenericInteractable 委派呼叫，實現互動邏輯與觸發設定的分離
    /// </summary>
    public abstract class InteractionHandler : MonoBehaviour
    {
        /// <summary>執行互動（由 GenericInteractable.Interact() 委派呼叫）</summary>
        public abstract void Execute();

        /// <summary>是否可執行互動（由 GenericInteractable.CanInteract 委派查詢）</summary>
        public virtual bool CanExecute() => true;

        /// <summary>玩家進入 Trigger 範圍時觸發（可選覆寫）</summary>
        public virtual void OnPlayerEnterRange() { }

        /// <summary>玩家離開 Trigger 範圍時觸發（可選覆寫）</summary>
        public virtual void OnPlayerExitRange() { }
    }
}
