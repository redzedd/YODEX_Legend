using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using Item;
using GAS;
using GAS.UI.Inventory;
using Player.Input;

namespace Interaction
{
    /// <summary>寶箱獎勵項目 — 在 Inspector 設定道具與數量</summary>
    [System.Serializable]
    public struct ChestReward
    {
        [Tooltip("獎勵物品")]
        public ItemData itemData;
        [Tooltip("數量")]
        [Min(1)] public int quantity;
    }

    /// <summary>
    /// 通用寶箱處理器 — 參考薩爾達曠野之息開箱邏輯
    /// 凍結遊戲時間 → Animancer 播放開箱動畫（Unscaled Time）→ 逐一顯示新道具字卡
    /// 可單獨使用（一般寶箱）或由 LockedChestHandler 委派呼叫（上鎖寶箱）
    /// </summary>
    public class ChestHandler : InteractionHandler
    {
        [Header("動畫（Animancer）")]
        [Tooltip("寶箱的 AnimancerComponent")]
        [SerializeField] private AnimancerComponent _animancer;
        [Tooltip("開箱動畫")]
        [SerializeField] private ClipTransition _openClip;

        [Header("獎勵設定")]
        [Tooltip("寶箱內的道具與數量")]
        [SerializeField] private List<ChestReward> _rewards = new();

        [Header("音效")]
        [Tooltip("開啟寶箱音效")]
        [SerializeField] private AudioClip _openSFX;
        [SerializeField] private AudioSource _audioSource;

        private bool _isOpened;
        private bool _isPlaying;
        private GenericInteractable _interactable;
        // 偵測同物件上的上鎖元件 — 防呆: 若 GenericInteractable 誤接到 ChestHandler,
        // 互動仍需委派回 LockedChestHandler, 避免繞過上鎖判定直接 Open()。
        private LockedChestHandler _lockedSibling;
        private bool _lockedSiblingChecked;

        /// <summary>寶箱是否已開啟</summary>
        public bool IsOpened => _isOpened;

        private LockedChestHandler LockedSibling
        {
            get
            {
                if (!_lockedSiblingChecked)
                {
                    // 同物件或父物件; LockedChestHandler 的設計允許 ChestHandler 掛在子物件上
                    _lockedSibling = GetComponentInParent<LockedChestHandler>(true);
                    _lockedSiblingChecked = true;
                }
                return _lockedSibling;
            }
        }

        private void Awake()
        {
            _interactable = GetComponentInParent<GenericInteractable>();
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();
        }

        /// <summary>開箱中或已開啟時不可互動;若有上鎖兄弟元件,可互動性以其為準(讓上鎖提示仍可顯示)</summary>
        public override bool CanExecute()
        {
            if (_isOpened || _isPlaying) return false;
            LockedChestHandler locked = LockedSibling;
            if (locked != null) return locked.CanExecute();
            return true;
        }

        /// <summary>由 GenericInteractable 委派呼叫(一般寶箱直接開啟,有上鎖兄弟元件時委派回它)</summary>
        public override void Execute()
        {
            if (_isOpened || _isPlaying) return;
            LockedChestHandler locked = LockedSibling;
            if (locked != null)
            {
                // 有 LockedChestHandler: 一律由它判斷上鎖狀態(上鎖→顯示提示,解鎖→呼叫 Open())
                locked.Execute();
                return;
            }
            Open();
        }

        /// <summary>
        /// 開啟寶箱 — 凍結時間 → 播放動畫 → 發放道具 → 顯示字卡
        /// 可由外部（如 LockedChestHandler）直接呼叫
        /// </summary>
        public void Open()
        {
            if (_isOpened || _isPlaying) return;
            StartCoroutine(OpenSequence());
        }

        private IEnumerator OpenSequence()
        {
            _isPlaying = true;
            _isOpened = true;
            // 1. 停用玩家輸入
            SystemInputReader inputHandler = SystemInputReader.Instance;
            if (inputHandler != null)
                inputHandler.DisablePlayerInput();
            // 2. 切換 Animancer 為 UnscaledTime，讓動畫在時間凍結下仍能播放
            AnimatorUpdateMode originalMode = AnimatorUpdateMode.Normal;
            if (_animancer != null && _animancer.Animator != null)
            {
                originalMode = _animancer.Animator.updateMode;
                _animancer.Animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            }
            Time.timeScale = 0f;
            // 3. 播放開箱音效與動畫
            if (_audioSource != null && _openSFX != null)
                _audioSource.PlayOneShot(_openSFX);
            if (_animancer != null && _openClip != null && _openClip.Clip != null)
            {
                AnimancerState state = _animancer.Play(_openClip);
                state.Time = 0;
                // 等待動畫播放完畢（非循環動畫）
                while (state.NormalizedTime < 1f)
                    yield return null;
            }
            // 4. 逐一發放獎勵並強制顯示新道具字卡
            InventoryManager inventory = InventoryManager.Instance;
            if (inventory != null)
            {
                for (int i = 0; i < _rewards.Count; i++)
                {
                    ChestReward reward = _rewards[i];
                    if (reward.itemData == null) continue;
                    int qty = Mathf.Max(1, reward.quantity);
                    // 靜默加入背包（不觸發字卡）
                    inventory.AddItemSilently(reward.itemData, qty);
                    // 強制顯示字卡（無論是否曾獲得過）
                    GameObject card = inventory.SpawnNewItemCard(reward.itemData);
                    if (card != null)
                    {
                        // 等待字卡被玩家關閉（字卡會自行恢復 timeScale 並銷毀）
                        while (card != null)
                            yield return null;
                    }
                }
            }
            // 5. 確保時間恢復（無獎勵時的安全措施，有獎勵時由最後一張字卡恢復）
            Time.timeScale = 1f;
            // 6. 恢復 Animancer 更新模式
            if (_animancer != null && _animancer.Animator != null)
                _animancer.Animator.updateMode = originalMode;
            // 7. 恢復玩家輸入
            if (inputHandler != null)
            {
                inputHandler.EnablePlayerInput();
                inputHandler.ResetTriggeredFlags();
            }
            // 8. 取消互動註冊
            UnregisterInteractable();
            _isPlaying = false;
        }

        private void UnregisterInteractable()
        {
            if (_interactable != null && InteractionManager.Instance != null)
                InteractionManager.Instance.UnregisterInteractable(_interactable);
        }
    }
}
