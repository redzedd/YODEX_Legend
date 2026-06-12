using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 敵人鎖定配置 - 定義敵人的鎖定錨點、指示器位置和相機參數
    /// 放置於敵人根物件上，供 TargetingSystem 讀取
    /// </summary>
    public class EnemyLockOnConfig : MonoBehaviour
    {
        [Header("Lock-On Anchors")]
        [SerializeField]
        [Tooltip("鎖定瞄準錨點（相機注視點，建議設在胸口）")]
        private Transform _lockAnchor;

        [SerializeField]
        [Tooltip("鎖定指示器錨點（UI 圖示位置，建議設在頭頂上方）")]
        private Transform _indicatorAnchor;

        [Header("Camera Group Settings")]
        [SerializeField]
        [Tooltip("在 CinemachineTargetGroup 中的權重")]
        private float _weight = 1.0f;

        [SerializeField]
        [Tooltip("在 CinemachineTargetGroup 中的半徑")]
        private float _radius = 1.0f;

        [Header("Screen Position")]
        [SerializeField]
        [Tooltip("鎖定相機構圖螢幕位置偏移")]
        private Vector2 _screenPosition = Vector2.zero;

        /// <summary>有效的鎖定錨點（無設定則回傳自身 Transform）</summary>
        public Transform EffectiveLockAnchor => _lockAnchor != null ? _lockAnchor : transform;

        /// <summary>有效的指示器錨點（無設定則回傳自身 Transform）</summary>
        public Transform EffectiveIndicatorAnchor => _indicatorAnchor != null ? _indicatorAnchor : transform;

        /// <summary>TargetGroup 權重</summary>
        public float EffectiveWeight => _weight;

        /// <summary>TargetGroup 半徑</summary>
        public float EffectiveRadius => _radius;

        /// <summary>構圖螢幕位置</summary>
        public Vector2 EffectiveScreenPosition => _screenPosition;
    }
}
