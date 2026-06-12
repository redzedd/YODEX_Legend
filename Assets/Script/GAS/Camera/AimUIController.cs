using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 瞄準 UI 控制器 - 管理準星、蓄力收縮環和 AoE 地面指示器
    /// </summary>
    public class AimUIController : MonoBehaviour
    {
        [Header("Crosshair")]
        [SerializeField]
        [Tooltip("準星 UI 物件")]
        private GameObject _crosshair;

        [Header("Charge Ring (Zelda-style)")]
        [SerializeField]
        [Tooltip("蓄力收縮圓環（瞄準正中心,從大縮到小,模仿薩爾達 BOTW 的弓蓄力指示）")]
        private RectTransform _chargeRing;

        [SerializeField]
        [Tooltip("圓環起始尺寸（蓄力 0% 時）")]
        private Vector2 _chargeRingStartSize = new(300f, 300f);

        [SerializeField]
        [Tooltip("圓環結束尺寸（蓄力 100% 時）")]
        private Vector2 _chargeRingEndSize = new(50f, 50f);

        [Header("AoE Indicator")]
        [SerializeField]
        [Tooltip("AoE 地面指示器（世界空間）")]
        private GameObject _aoeIndicator;

        /// <summary>是否正在顯示瞄準 UI</summary>
        public bool IsActive { get; private set; }

        private void Awake()
        {
            HideAll();
        }

        /// <summary>
        /// 顯示準星
        /// </summary>
        public void ShowCrosshair()
        {
            if (_crosshair != null)
            {
                _crosshair.SetActive(true);
            }
            IsActive = true;
        }

        /// <summary>
        /// 隱藏準星
        /// </summary>
        public void HideCrosshair()
        {
            if (_crosshair != null)
            {
                _crosshair.SetActive(false);
            }
        }

        /// <summary>
        /// 顯示蓄力收縮圓環（薩爾達式瞄準）
        /// </summary>
        public void ShowChargeRing()
        {
            if (_chargeRing != null)
            {
                _chargeRing.gameObject.SetActive(true);
                _chargeRing.sizeDelta = _chargeRingStartSize;
            }
        }

        /// <summary>
        /// 隱藏蓄力收縮圓環
        /// </summary>
        public void HideChargeRing()
        {
            if (_chargeRing != null)
            {
                _chargeRing.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 更新蓄力進度（0~1）— 驅動圓環收縮尺寸
        /// </summary>
        public void SetChargeProgress(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            if (_chargeRing != null)
            {
                _chargeRing.sizeDelta = Vector2.Lerp(_chargeRingStartSize, _chargeRingEndSize, clamped);
            }
        }

        /// <summary>
        /// 顯示 AoE 地面指示器
        /// </summary>
        public void ShowAoEIndicator(Vector3 worldPosition, float radius)
        {
            if (_aoeIndicator != null)
            {
                _aoeIndicator.SetActive(true);
                _aoeIndicator.transform.position = worldPosition + Vector3.up * 0.05f;
                float scale = radius * 2f;
                _aoeIndicator.transform.localScale = new Vector3(scale, 1f, scale);
            }
        }

        /// <summary>
        /// 更新 AoE 指示器位置
        /// </summary>
        public void UpdateAoEIndicatorPosition(Vector3 worldPosition)
        {
            if (_aoeIndicator != null && _aoeIndicator.activeSelf)
            {
                _aoeIndicator.transform.position = worldPosition + Vector3.up * 0.05f;
            }
        }

        /// <summary>
        /// 隱藏 AoE 地面指示器
        /// </summary>
        public void HideAoEIndicator()
        {
            if (_aoeIndicator != null)
            {
                _aoeIndicator.SetActive(false);
            }
        }

        /// <summary>
        /// 隱藏所有瞄準 UI
        /// </summary>
        public void HideAll()
        {
            HideCrosshair();
            HideChargeRing();
            HideAoEIndicator();
            IsActive = false;
        }
    }
}
