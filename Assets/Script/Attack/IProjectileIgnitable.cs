using UnityEngine;

/// <summary>
/// 場景互動物件 — 被投射物(箭矢/魔法等)命中時會被「點燃」觸發特殊反應。
/// 由 GAS.ProjectileBehaviour.HandleHit 在 layer 篩選之前優先呼叫,
/// 因此實作此介面的物件不需要落在投射物的 HitLayers / ObstacleLayers 內也能被引爆。
/// 典型實作:爆炸桶、油桶、瓦斯閥、可破壞炸藥包。
/// </summary>
public interface IProjectileIgnitable
{
    /// <summary>投射物命中時呼叫,參數為命中世界座標(由投射物計算)</summary>
    void OnProjectileImpact(Vector3 hitPoint);
}
