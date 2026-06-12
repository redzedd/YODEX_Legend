/// <summary>
/// 受擊特效縮放提供者 — 受擊方(敵人/可破壞物)實作此介面可覆寫攻擊方的全身受擊 VFX 縮放係數。
/// 未實作時 AoE 等攻擊預設用 Transform.lossyScale.x 推算(SpatialScaleUtility 慣例);
/// 大型 Boss / 特殊比例模型實作此介面以精確指定特效大小,避免特效太小看不出 impact 或太大蓋住身體。
/// 等比例(uniform): 回傳的 float 會以 Vector3.one * value 套用到 VFX。
/// </summary>
public interface IHitVFXSizeProvider
{
    /// <summary>受擊 VFX 縮放係數(1.0 = 預設大小,大於 1 放大,小於 1 縮小)</summary>
    float HitVFXScale { get; }
}
