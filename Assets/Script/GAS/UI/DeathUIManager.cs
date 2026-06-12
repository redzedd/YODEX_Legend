using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using DG.Tweening;
using TMPro;
using Player.Input;

namespace GAS.UI
{
    /// <summary>
    /// 薩爾達曠野之息風格的死亡 UI 管理器。
    /// 所有動畫使用 DOTween Sequence + SetUpdate(true)，在 TimeScale=0 時正常運作。
    /// </summary>
    public class DeathUIManager : MonoBehaviour
    {
        public static DeathUIManager Instance { get; private set; }

        #region Serialized Fields

        [Header("UI 組件")]
        [SerializeField] private CanvasGroup _panelCanvasGroup;
        [SerializeField] private Image _blackScreenImage;
        [SerializeField] private CanvasGroup _gameOverTextGroup;
        [SerializeField] private CanvasGroup _buttonsGroup;

        [Header("按鈕")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _returnToTitleButton;

        [Header("音效")]
        [Tooltip("死亡音效播放器（Inspector 中勾選 ignoreListenerPause）")]
        [SerializeField] private AudioSource _deathJingleSource;
        [SerializeField] private AudioClip _deathJingleClip;

        [Header("BGM 淡出")]
        [Tooltip("場景 BGM 音源（拖入 FadeInManager 的 bgmSource）")]
        [SerializeField] private AudioSource _bgmSource;

        [Header("文字設定")]
        [SerializeField] private TextMeshProUGUI _gameOverLabel;
        [SerializeField] private string _gameOverText = "Game Over";

        [Header("時間設定")]
        [SerializeField] private float _delayBeforeFade = 1.5f;
        [SerializeField] private float _bgmFadeOutDuration = 1.5f;
        [SerializeField] private float _screenFadeDuration = 2.0f;
        [SerializeField] private float _textFadeInDuration = 0.8f;
        [SerializeField] private float _buttonsFadeInDelay = 0.5f;
        [SerializeField] private float _buttonsFadeInDuration = 0.5f;

        [Header("場景")]
        [SerializeField] private string _titleSceneName = "S_Menu";

        #endregion

        #region Private Fields

        private Sequence _deathSequence;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            // 初始化隱藏
            if (_panelCanvasGroup != null)
                _panelCanvasGroup.gameObject.SetActive(false);
            // 設定 Game Over 文字
            if (_gameOverLabel != null)
                _gameOverLabel.text = _gameOverText;
            // 綁定按鈕事件
            if (_continueButton != null)
                _continueButton.onClick.AddListener(OnContinue);
            if (_returnToTitleButton != null)
                _returnToTitleButton.onClick.AddListener(OnReturnToTitle);
        }

        private void OnDestroy()
        {
            KillSequence();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 觸發薩爾達風格的死亡 UI 序列。
        /// </summary>
        /// <param name="preFadeDelayOverride">覆寫 UI 淡入前的等待秒數(留給死亡動畫播放的空間)。傳負值(預設)時採 Inspector 的 _delayBeforeFade。</param>
        public void TriggerDeathSequence(float preFadeDelayOverride = -1f)
        {
            KillSequence();
            InitializeUI();
            float delay = preFadeDelayOverride >= 0f ? preFadeDelayOverride : _delayBeforeFade;
            BuildDeathSequence(delay);
        }

        #endregion

        #region Private Methods

        private void InitializeUI()
        {
            // 啟用面板、重置所有透明度
            _panelCanvasGroup.gameObject.SetActive(true);
            _panelCanvasGroup.alpha = 1f;
            // 黑幕從全透明開始
            if (_blackScreenImage != null)
            {
                Color c = _blackScreenImage.color;
                c.a = 0f;
                _blackScreenImage.color = c;
            }
            // Game Over 文字隱藏
            if (_gameOverTextGroup != null)
                _gameOverTextGroup.alpha = 0f;
            // 按鈕隱藏且不可互動
            if (_buttonsGroup != null)
            {
                _buttonsGroup.alpha = 0f;
                _buttonsGroup.interactable = false;
                _buttonsGroup.blocksRaycasts = false;
            }
        }

        private void BuildDeathSequence(float preFadeDelay)
        {
            _deathSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject);
            // ── 階段 1：等待死亡動畫播放 ──
            _deathSequence.AppendInterval(preFadeDelay);
            // ── 階段 2：播放死亡音樂 + 淡出 BGM ──
            _deathSequence.AppendCallback(() =>
            {
                PlayDeathJingle();
                FadeOutBGM();
            });
            // ── 階段 3：螢幕淡入黑色 ──
            if (_blackScreenImage != null)
            {
                _deathSequence.Append(
                    _blackScreenImage.DOFade(1f, _screenFadeDuration)
                        .SetEase(Ease.InQuad)
                        .SetUpdate(true)
                );
            }
            // ── 階段 4：Game Over 文字淡入 ──
            if (_gameOverTextGroup != null)
            {
                _deathSequence.Append(
                    _gameOverTextGroup.DOFade(1f, _textFadeInDuration)
                        .SetEase(Ease.OutQuad)
                        .SetUpdate(true)
                );
            }
            // ── 階段 5：等待一拍 ──
            _deathSequence.AppendInterval(_buttonsFadeInDelay);
            // ── 階段 6：按鈕淡入 + 顯示游標 ──
            _deathSequence.AppendCallback(() =>
            {
                if (_buttonsGroup != null)
                {
                    _buttonsGroup.interactable = true;
                    _buttonsGroup.blocksRaycasts = true;
                }
                // 啟用 UI 輸入
                if (SystemInputReader.Instance != null)
                    SystemInputReader.Instance.EnableUIMapInput();
                // 顯示滑鼠游標
                if (MouseVisibilityManager.Instance != null)
                    MouseVisibilityManager.Instance.ShowCursor();
                // 設定搖桿/鍵盤導覽起點 — 不選取則搖桿按方向無反應
                SelectFirstButton();
            });
            if (_buttonsGroup != null)
            {
                _deathSequence.Append(
                    _buttonsGroup.DOFade(1f, _buttonsFadeInDuration)
                        .SetEase(Ease.OutQuad)
                        .SetUpdate(true)
                );
            }
        }

        private void PlayDeathJingle()
        {
            if (_deathJingleSource == null || _deathJingleClip == null) return;
            _deathJingleSource.clip = _deathJingleClip;
            _deathJingleSource.loop = false;
            _deathJingleSource.Play();
        }

        private void FadeOutBGM()
        {
            if (_bgmSource == null) return;
            _bgmSource.DOFade(0f, _bgmFadeOutDuration)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private void OnContinue()
        {
            // 復活回遊戲 — 隱藏死亡時為了點按鈕而顯示的游標(回主選單則保留游標,故只在此處理)
            if (MouseVisibilityManager.Instance != null)
            {
                MouseVisibilityManager.Instance.enableDynamicMouse = false;
                MouseVisibilityManager.Instance.HideCursorImmediate();
            }
            CleanupAndLoadScene(SceneManager.GetActiveScene().name);
        }

        private void OnReturnToTitle()
        {
            CleanupAndLoadScene(_titleSceneName);
        }

        private void CleanupAndLoadScene(string sceneName)
        {
            KillSequence();
            Time.timeScale = 1f;
            // 死亡時關掉了 Player 輸入閘門,SystemInputReader 跨場景常駐不會自動還原。
            // 重載前主動恢復,否則復活後攻擊(受此閘門管控)會打不出來。
            if (SystemInputReader.Instance != null)
            {
                SystemInputReader.Instance.DisableUIMapInput();
                SystemInputReader.Instance.EnablePlayerInput();
                SystemInputReader.Instance.ResetTriggeredFlags();
            }
            SceneManager.LoadScene(sceneName);
        }

        private void SelectFirstButton()
        {
            if (EventSystem.current == null) return;
            Button target = _continueButton != null ? _continueButton : _returnToTitleButton;
            if (target == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private void KillSequence()
        {
            if (_deathSequence != null && _deathSequence.IsActive())
                _deathSequence.Kill();
            _deathSequence = null;
        }

        #endregion
    }
}
