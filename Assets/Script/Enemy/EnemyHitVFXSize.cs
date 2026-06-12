using UnityEngine;

namespace GAS.EnemyAI
{
    /// <summary>
    /// 敵人受擊全身 VFX 縮放覆寫 — 掛到敵人根 GameObject(與 AbilitySystemComponent 同層或父層皆可)
    /// 適用情境:模型 lossyScale=1 但本身體型大/小,需要全身受擊特效對應放大或縮小。
    /// 未掛此元件的敵人會 fallback 到 Transform.lossyScale.x 推算(SpatialScaleUtility 慣例)。
    /// 等比例(uniform):同一個值套到 X/Y/Z。
    /// </summary>
    public class EnemyHitVFXSize : MonoBehaviour, IHitVFXSizeProvider
    {
        [Tooltip("受擊 VFX 縮放係數\n1.0 = 預設大小\n2.0 = 兩倍大(大型 Boss / 體型偏大模型)\n0.5 = 一半大(小型敵人)\n建議實機測試後再微調")]
        [SerializeField]
        [Range(0.1f, 5f)]
        private float _hitVFXScale = 1f;

        public float HitVFXScale => _hitVFXScale;
    }
}
