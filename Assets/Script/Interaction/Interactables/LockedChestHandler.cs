using System.Collections.Generic;
using UnityEngine;
using CameraSystem;
using EnemyAI;

namespace Interaction
{
    /// <summary>
    /// 上鎖寶箱處理器 — 需擊敗場上指定敵人才能解鎖
    /// 上鎖時互動：透過 InteractionHintUI 顯示提示文字
    /// 全部敵人擊敗後：透過 CinematicCameraSequence 演出（含時間凍結、UI 淡入淡出、輸入鎖定）→ 播解鎖特效
    /// 解鎖後委派 ChestHandler 執行開啟邏輯
    /// 由 GenericInteractable 委派呼叫
    /// </summary>
    public class LockedChestHandler : InteractionHandler
    {
        [Header("寶箱處理器")]
        [Tooltip("開啟寶箱的通用處理器（掛在同物件或子物件上）")]
        [SerializeField] private ChestHandler _chestHandler;

        [Header("上鎖提示")]
        [Tooltip("寶箱上鎖時的提示文字")]
        [SerializeField] private string _lockedHintMessage = "寶箱被鎖住了";
        [Tooltip("上鎖提示音效（傳給 InteractionHintUI）")]
        [SerializeField] private AudioClip _lockedHintSFX;

        [Header("敵人監視")]
        [Tooltip("需要擊敗的敵人清單（可設單一或多個）")]
        [SerializeField] private List<EnemyController> _requiredEnemies = new();

        [Header("解鎖特效")]
        [Tooltip("解鎖粒子特效（會自動設為 useUnscaledTime，凍結時間時仍會播完）")]
        [SerializeField] private ParticleSystem _unlockVFX;
        [Tooltip("解鎖音效")]
        [SerializeField] private AudioClip _unlockSFX;
        [SerializeField] private AudioSource _audioSource;

        [Header("演出序列")]
        [Tooltip("Camera 切換 / 時間凍結 / UI 淡入淡出 / 輸入鎖定都在這裡設定（建議開啟 Freeze Time Scale）")]
        [SerializeField] private CinematicCameraSequence _sequence = new();

        private bool _isUnlocked;
        private int _defeatedCount;
        private int _requiredCount;

        /// <summary>是否已解鎖 — 供 ChestHandler 防呆檢查使用</summary>
        public bool IsUnlocked => _isUnlocked;

        private void Awake()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();
        }

        private void Start()
        {
            _requiredCount = 0;
            _defeatedCount = 0;
            for (int i = 0; i < _requiredEnemies.Count; i++)
            {
                if (_requiredEnemies[i] == null) continue;
                _requiredCount++;
                if (_requiredEnemies[i].IsDead)
                {
                    _defeatedCount++;
                    continue;
                }
                _requiredEnemies[i].OnDied += OnEnemyDefeated;
            }
            // 無敵人需求或已全部擊敗 → 直接解鎖（不播演出）
            if (_requiredCount == 0 || _defeatedCount >= _requiredCount)
                _isUnlocked = true;
        }

        private void OnDestroy()
        {
            // 取消訂閱，避免已銷毀物件殘留委派
            for (int i = 0; i < _requiredEnemies.Count; i++)
            {
                if (_requiredEnemies[i] != null)
                    _requiredEnemies[i].OnDied -= OnEnemyDefeated;
            }
            _sequence?.Cleanup();
        }

        /// <summary>演出中或寶箱已開啟時不可互動</summary>
        public override bool CanExecute() => !_sequence.IsPlaying && !_chestHandler.IsOpened;

        public override void Execute()
        {
            if (_sequence.IsPlaying || _chestHandler.IsOpened) return;
            if (!_isUnlocked)
            {
                // 上鎖 → 顯示提示
                if (InteractionHintUI.Instance != null)
                    InteractionHintUI.Instance.Show(_lockedHintMessage, _lockedHintSFX);
                return;
            }
            // 已解鎖 → 委派 ChestHandler 開啟寶箱
            _chestHandler.Open();
        }

        private void OnEnemyDefeated()
        {
            _defeatedCount++;
            if (_defeatedCount >= _requiredCount && !_isUnlocked)
                StartCoroutine(_sequence.Play(PlayUnlockEffects));
        }

        // 演出動作回呼：標記解鎖 + 播 VFX + 播音效
        private void PlayUnlockEffects()
        {
            _isUnlocked = true;
            if (_unlockVFX != null)
            {
                // 對根粒子與所有子粒子統一設定 useUnscaledTime（演出可能凍結時間）
                ParticleSystem[] allParticles = _unlockVFX.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < allParticles.Length; i++)
                {
                    ParticleSystem.MainModule main = allParticles[i].main;
                    main.useUnscaledTime = true;
                }
                _unlockVFX.gameObject.SetActive(true);
                _unlockVFX.Play(true);
            }
            if (_audioSource != null && _unlockSFX != null)
                _audioSource.PlayOneShot(_unlockSFX);
        }
    }
}
