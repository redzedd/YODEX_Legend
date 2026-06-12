using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 烹飪互動處理器 — 開啟烹飪 UI 面板並清空材料槽
    /// 掛在烹飪鍋的 GameObject 上，由 GenericInteractable 委派呼叫
    /// </summary>
    public class CookingInteractionHandler : InteractionHandler
    {
        [Header("烹飪 UI")]
        [Tooltip("烹飪面板根物件")]
        [SerializeField] private GameObject _cookingUIPanel;
        [Tooltip("烹飪背包顯示")]
        [SerializeField] private CookingInventoryDisplay _cookingInventoryDisplay;
        [Tooltip("烹飪管理器")]
        [SerializeField] private CookingManager _cookingManager;

        [Header("音效")]
        [SerializeField] private AudioClip _activationSFX;
        [SerializeField] private AudioSource _audioSource;

        public override void Execute()
        {
            if (_audioSource != null && _activationSFX != null)
                _audioSource.PlayOneShot(_activationSFX);
            if (_cookingUIPanel != null && _cookingInventoryDisplay != null)
            {
                _cookingUIPanel.SetActive(true);
                _cookingInventoryDisplay.OpenUI();
            }
            if (_cookingManager != null)
                _cookingManager.ClearIngredients();
        }
    }
}
