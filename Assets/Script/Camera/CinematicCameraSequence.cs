using System;
using System.Collections;
using DG.Tweening;
using Player.Input;
using UnityEngine;

namespace CameraSystem
{
    /// <summary>
    /// 通用劇情演出序列 — 可重用的鏡頭演出流程，包含 Camera 切換、Time Scale 凍結、UI 淡入淡出、輸入鎖定。
    /// 用法：Handler 內 [SerializeField] CinematicCameraSequence _sequence;
    ///       Execute 時 StartCoroutine(_sequence.Play(onAction));
    /// 流程：DisableInput → FadeUIOut → Camera Request → 等 Focus → (Freeze Time) → 等 Delay
    ///       → onAction → 等 Hold → (Unfreeze Time) → Camera Release → 等 Return → FadeUIIn → EnableInput
    /// </summary>
    [Serializable]
    public class CinematicCameraSequence
    {
        [Header("Camera")]

        [SerializeField]
        [Tooltip("演出用聚焦相機的 CameraEntry — 拖該 Cinemachine 相機 GameObject（上面要先掛 CameraEntry, ID=Cinematic, Layer=Cinematic）")]
        private CameraEntry _cameraEntry;

        [Header("Timing")]

        [SerializeField]
        [Tooltip("Request 相機後等待 blend 完成的秒數（受 timeScale 影響）。建議 1.5~2.5 秒")]
        private float _cameraFocusDuration = 2f;

        [SerializeField]
        [Tooltip("動作執行前的額外延遲秒數（用 Unscaled Time）。建議 0~0.5 秒")]
        private float _actionDelay = 0.5f;

        [SerializeField]
        [Tooltip("動作執行後的停滯秒數（用 Unscaled Time，凍結時間時仍會走完）。建議 1~2 秒")]
        private float _holdAfterAction = 1.5f;

        [SerializeField]
        [Tooltip("Release 相機後等待 blend 回主視角的秒數。建議 0.5~1 秒")]
        private float _cameraReturnDuration = 0.5f;

        [Header("Time Scale")]

        [SerializeField]
        [Tooltip("勾選後在動作執行前後凍結 Time.timeScale，做戲劇性停格演出")]
        private bool _freezeTimeScale = false;

        [SerializeField]
        [Tooltip("凍結時的 Time.timeScale 值。建議 0 完全停格")]
        private float _frozenTimeScale = 0f;

        [Header("UI Fade")]

        [SerializeField]
        [Tooltip("演出時要淡出/淡入的 UI 群組（HUD 等 CanvasGroup）。留空則不執行淡入淡出")]
        private CanvasGroup _gameplayUICanvasGroup;

        [SerializeField]
        [Tooltip("UI 淡出秒數。建議 0.3~0.5")]
        private float _uiFadeOutDuration = 0.4f;

        [SerializeField]
        [Tooltip("UI 淡入秒數。建議 0.2~0.4")]
        private float _uiFadeInDuration = 0.3f;

        [Header("Input")]

        [SerializeField]
        [Tooltip("勾選後演出期間自動關閉玩家輸入（透過 SystemInputReader.Instance.DisablePlayerInput）")]
        private bool _disablePlayerInput = true;

        /// <summary>演出進行中（true 時 Handler 應拒絕再次觸發）</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>當前綁定的相機 Entry（給 Handler 查詢用）</summary>
        public CameraEntry CameraEntry => _cameraEntry;

        private CameraTicket _ticket;
        private Tween _uiFadeTween;

        /// <summary>
        /// 啟動演出 — 回傳 IEnumerator，呼叫者用 StartCoroutine 啟動，或 yield 等候完成後做後續動作
        /// </summary>
        public IEnumerator Play(Action onAction)
        {
            if (IsPlaying) yield break;
            IsPlaying = true;
            SystemInputReader input = SystemInputReader.Instance;
            try
            {
                if (_disablePlayerInput && input != null)
                {
                    input.DisablePlayerInput();
                }
                yield return FadeUI(0f, _uiFadeOutDuration);
                RequestCamera();
                if (_cameraFocusDuration > 0f)
                {
                    yield return new WaitForSeconds(_cameraFocusDuration);
                }
                if (_freezeTimeScale)
                {
                    Time.timeScale = _frozenTimeScale;
                    Time.fixedDeltaTime = 0f;
                }
                if (_actionDelay > 0f)
                {
                    yield return new WaitForSecondsRealtime(_actionDelay);
                }
                onAction?.Invoke();
                if (_holdAfterAction > 0f)
                {
                    yield return new WaitForSecondsRealtime(_holdAfterAction);
                }
                if (_freezeTimeScale)
                {
                    Time.timeScale = 1f;
                    Time.fixedDeltaTime = 0.02f;
                }
                ReleaseCamera();
                if (_cameraReturnDuration > 0f)
                {
                    yield return new WaitForSeconds(_cameraReturnDuration);
                }
                yield return FadeUI(1f, _uiFadeInDuration);
                if (_disablePlayerInput && input != null)
                {
                    input.EnablePlayerInput();
                    input.ResetTriggeredFlags();
                }
            }
            finally
            {
                // 安全清理：即使中途被 StopCoroutine 也確保狀態還原
                IsPlaying = false;
                ReleaseCamera();
                if (_freezeTimeScale && Mathf.Approximately(Time.timeScale, _frozenTimeScale))
                {
                    Time.timeScale = 1f;
                    Time.fixedDeltaTime = 0.02f;
                }
            }
        }

        /// <summary>在持有 Handler 的 OnDestroy 呼叫，避免 DOTween 殘留 / ticket 殘留</summary>
        public void Cleanup()
        {
            KillFadeTween();
            ReleaseCamera();
        }

        private void RequestCamera()
        {
            if (_cameraEntry == null) return;
            CameraDirector director = CameraDirector.Instance;
            if (director == null) return;
            _ticket = director.Request(_cameraEntry);
        }

        // 先 null 後 Release，避免 Director.OnStackChanged 觸發時欄位殘留導致誤判
        private void ReleaseCamera()
        {
            if (_ticket == null) return;
            CameraTicket t = _ticket;
            _ticket = null;
            t.Release();
        }

        private IEnumerator FadeUI(float targetAlpha, float duration)
        {
            if (_gameplayUICanvasGroup == null) yield break;
            if (duration <= 0f)
            {
                _gameplayUICanvasGroup.alpha = targetAlpha;
                yield break;
            }
            KillFadeTween();
            _uiFadeTween = _gameplayUICanvasGroup
                .DOFade(targetAlpha, duration)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true);
            yield return _uiFadeTween.WaitForCompletion();
        }

        private void KillFadeTween()
        {
            if (_uiFadeTween == null) return;
            if (_uiFadeTween.IsActive()) _uiFadeTween.Kill();
            _uiFadeTween = null;
        }
    }
}
