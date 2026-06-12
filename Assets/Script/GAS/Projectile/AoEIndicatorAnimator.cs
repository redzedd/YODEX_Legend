using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GAS
{
    /// <summary>
    /// AoE 指示器動畫控制器 — 掛在 AoEBehaviour._indicatorRoot 子物件上
    /// 由 AoEBehaviour 在生命週期關鍵點呼叫:
    ///   • PlayRise   — 蓄力開始,光壁從 scale.y=0 升起 + DecalProjector.fadeFactor 0→1 淡入
    ///   • PlayRelease — 釋放,先 flash 縮放放大,再 scale→0 + decal fadeFactor→0 淡出,結束自動 SetActive(false)
    ///   • Cancel     — 蓄力取消,立即還原並關閉
    /// </summary>
    [DisallowMultipleComponent]
    public class AoEIndicatorAnimator : MonoBehaviour
    {
        [Header("Rising (蓄力指示器升起)")]
        [Tooltip("光壁升起持續時間(秒)")]
        [SerializeField] private float _riseDuration = 0.35f;

        [Tooltip("升起緩動曲線 — OutBack 會有輕微反彈感")]
        [SerializeField] private Ease _riseEase = Ease.OutCubic;

        [Header("Release (釋放時亮閃 + 淡出)")]
        [Tooltip("亮閃階段持續時間 — 短促的縮放放大")]
        [SerializeField] private float _flashDuration = 0.08f;

        [Tooltip("淡出階段持續時間 — 從亮閃峰值縮回 0 + 透明度淡出")]
        [SerializeField] private float _fadeDuration = 0.45f;

        [Tooltip("亮閃峰值的縮放倍率(以原始 scale 為基準)")]
        [SerializeField] private float _flashScaleMultiplier = 1.25f;

        [Tooltip("亮閃峰值的材質強度倍率(以材質原本 _Intensity 為基準)— 透過 MaterialPropertyBlock 動態調整,不影響共享材質")]
        [SerializeField] private float _flashIntensityMultiplier = 2.5f;

        [Tooltip("Decal 圈是否跟 FlashScale 同步放大(release flash 階段 decal.size 同步乘 _flashScaleMultiplier)")]
        [SerializeField] private bool _decalFlashWithWall = true;

        [Header("Visual Targets")]
        [Tooltip("負責 scale 動畫的 transform — 通常是光壁/光柱 mesh;留空 = 自己 transform")]
        [SerializeField] private Transform _wallTransform;

        [Tooltip("DecalProjector — 同步動畫 fadeFactor 達成淡入/淡出;留空 = GetComponent<DecalProjector>")]
        [SerializeField] private DecalProjector _decal;

        [Tooltip("光壁 Renderer — 釋放時動態調 _Intensity 達成「亮閃」;留空 = 從 _wallTransform 自動抓")]
        [SerializeField] private Renderer _wallRenderer;

        // _FadeMultiplier 是 master 亮度控制 — 動畫一個 property 就能讓 pattern + bottomGlow + alpha 全部淡出
        private static readonly int FadeMultiplierPropID = Shader.PropertyToID("_FadeMultiplier");

        private Vector3 _baseScale = Vector3.one;
        private float _baseDecalFade = 1f;
        private float _baseWallFade = 1f;
        private float _currentWallFade = 1f;
        // 用 .material 取 per-renderer instance — 跟 GameObject 一起銷毀。
        // 不用 MaterialPropertyBlock 是因為 URP SRP Batcher 對 CBUFFER 屬性的 PropertyBlock 覆寫不可靠。
        private Material _wallMaterialInstance;
        private Sequence _currentSeq;

        private void Awake()
        {
            if (_wallTransform == null) _wallTransform = transform;
            _baseScale = _wallTransform.localScale;
            if (_decal == null) _decal = GetComponent<DecalProjector>();
            if (_decal != null) _baseDecalFade = _decal.fadeFactor;
            // 取光壁 renderer 與基礎強度 — 用 .material 取 per-renderer instance
            // 注意:.material 在第一次存取時自動 Instantiate,跟 GameObject 一起銷毀,不需手動清理
            if (_wallRenderer == null && _wallTransform != null)
            {
                _wallRenderer = _wallTransform.GetComponent<Renderer>();
            }
            if (_wallRenderer != null)
            {
                _wallMaterialInstance = _wallRenderer.material;
                if (_wallMaterialInstance != null && _wallMaterialInstance.HasProperty(FadeMultiplierPropID))
                {
                    _baseWallFade = _wallMaterialInstance.GetFloat(FadeMultiplierPropID);
                }
                _currentWallFade = _baseWallFade;
            }
        }

        /// <summary>
        /// 蓄力開始時呼叫 — 光壁從 0 升起,decal 淡入,材質強度回到基礎值
        /// </summary>
        public void PlayRise()
        {
            gameObject.SetActive(true);
            KillCurrent();
            // 起始狀態
            _wallTransform.localScale = new Vector3(_baseScale.x, 0f, _baseScale.z);
            if (_decal != null) _decal.fadeFactor = 0f;
            SetWallFade(_baseWallFade);
            // 動畫
            _currentSeq = DOTween.Sequence().SetLink(gameObject);
            _currentSeq.Append(_wallTransform.DOScaleY(_baseScale.y, _riseDuration).SetEase(_riseEase));
            if (_decal != null)
            {
                _currentSeq.Join(DOTween.To(
                    () => _decal.fadeFactor,
                    v => _decal.fadeFactor = v,
                    _baseDecalFade,
                    _riseDuration));
            }
        }

        /// <summary>
        /// 釋放時呼叫 — flash 階段放大 scale + 衝高材質強度,fade 階段保持大小,只靠 _Intensity → 0 + decal fadeFactor → 0 淡出
        /// </summary>
        public void PlayRelease()
        {
            KillCurrent();
            Vector3 flashScale = _baseScale * _flashScaleMultiplier;
            float flashPeak = _baseWallFade * _flashIntensityMultiplier;
            // 讀當下 decal.size 當 base(AoEBehaviour.SyncDecalSize 已經根據 Radius 設定好)
            Vector3 baseDecalSize = _decal != null ? _decal.size : Vector3.zero;
            Vector3 flashDecalSize = baseDecalSize * _flashScaleMultiplier;
            if (_decal != null) flashDecalSize.z = baseDecalSize.z; // 投影深度不動,只放大 x/y
            _currentSeq = DOTween.Sequence().SetLink(gameObject);

            // Flash:scale 衝高 + 整體亮度衝高(_FadeMultiplier)+ decal.size 同步放大
            _currentSeq.Append(_wallTransform.DOScale(flashScale, _flashDuration).SetEase(Ease.OutCubic));
            if (_wallRenderer != null)
            {
                _currentSeq.Join(DOTween.To(
                    () => _currentWallFade,
                    SetWallFade,
                    flashPeak,
                    _flashDuration).SetEase(Ease.OutQuad));
            }
            if (_decal != null && _decalFlashWithWall)
            {
                _currentSeq.Join(DOTween.To(
                    () => _decal.size,
                    v => _decal.size = v,
                    flashDecalSize,
                    _flashDuration).SetEase(Ease.OutCubic));
            }

            // Fade:維持 flash 大小,光壁 _FadeMultiplier → 0(連底環一起淡)+ decal fadeFactor → 0
            if (_wallRenderer != null)
            {
                _currentSeq.Append(DOTween.To(
                    () => _currentWallFade,
                    SetWallFade,
                    0f,
                    _fadeDuration).SetEase(Ease.InQuad));
            }
            else
            {
                _currentSeq.AppendInterval(_fadeDuration);
            }
            if (_decal != null)
            {
                _currentSeq.Join(DOTween.To(
                    () => _decal.fadeFactor,
                    v => _decal.fadeFactor = v,
                    0f,
                    _fadeDuration));
            }

            _currentSeq.OnComplete(() =>
            {
                if (this != null) gameObject.SetActive(false);
            });
        }

        /// <summary>
        /// 蓄力取消時呼叫 — 立即還原狀態並關閉
        /// </summary>
        public void Cancel()
        {
            KillCurrent();
            _wallTransform.localScale = _baseScale;
            if (_decal != null) _decal.fadeFactor = _baseDecalFade;
            SetWallFade(_baseWallFade);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 直接寫入 material instance 的 _FadeMultiplier — instance 跟此 GameObject 一起銷毀,不污染原 asset
        /// </summary>
        private void SetWallFade(float value)
        {
            _currentWallFade = value;
            if (_wallMaterialInstance == null) return;
            _wallMaterialInstance.SetFloat(FadeMultiplierPropID, value);
        }

        private void KillCurrent()
        {
            if (_currentSeq != null && _currentSeq.IsActive())
            {
                _currentSeq.Kill();
            }
            _currentSeq = null;
        }

        private void OnDestroy()
        {
            KillCurrent();
        }
    }
}
