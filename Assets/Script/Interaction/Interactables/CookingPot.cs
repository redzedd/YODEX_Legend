using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// [已棄用] 烹飪鍋互動 — 請改用 GenericInteractable + CookingInteractionHandler
    /// </summary>
    [Obsolete("請改用 GenericInteractable + CookingInteractionHandler")]
    public class CookingPot : InteractableTriggerBase
    {
        [Header("烹飪 UI")]
        [SerializeField] private GameObject _cookingUIPanel;
        [SerializeField] private CookingInventoryDisplay _cookingInventoryDisplay;
        [SerializeField] private CookingManager _cookingManager;

        [Header("音效")]
        [SerializeField] private AudioClip _activationSFX;
        [SerializeField] private AudioSource _audioSource;

        public override int Priority => 2;
        public override string InteractionTypeName => InteractionType.Activate;
        public override string PromptText => "烹飪";

        public override void Interact()
        {
            if (_audioSource != null && _activationSFX != null)
                _audioSource.PlayOneShot(_activationSFX);
            if (_cookingUIPanel != null)
            {
                _cookingUIPanel.SetActive(true);
                _cookingInventoryDisplay.OpenUI();
            }
            if (_cookingManager != null)
                _cookingManager.ClearIngredients();
        }
    }
}
