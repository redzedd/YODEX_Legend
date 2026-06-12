using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 投射物數據 - 定義投射物的行為和屬性
    /// </summary>
    [CreateAssetMenu(fileName = "New Projectile", menuName = "GAS/Abilities/Projectile Data")]
    public class ProjectileData : ScriptableObject
    {
        [Header("Prefab")]
        [Tooltip("投射物預製體")]
        public GameObject Prefab;

        [Header("Movement")]
        [Tooltip("飛行速度")]
        public float Speed = 20f;

        [Tooltip("存活時間（秒）")]
        public float Lifetime = 5f;

        [Tooltip("重力影響（0 = 直線飛行，> 0 = 拋物線）")]
        public float Gravity;

        [Header("Homing")]
        [Tooltip("是否追蹤目標")]
        public bool HomingEnabled;

        [Tooltip("追蹤強度（每秒轉向角度）")]
        public float HomingStrength = 5f;

        [Header("Piercing & Explosion")]
        [Tooltip("穿透次數（0 = 碰到即銷毀）")]
        public int PierceCount;

        [Tooltip("爆炸半徑（0 = 無爆炸，僅單體命中）")]
        public float ImpactRadius;

        [Header("Collision")]
        [Tooltip("命中圖層")]
        public LayerMask HitLayers;

        [Tooltip("障礙物圖層（碰到障礙物即銷毀）")]
        public LayerMask ObstacleLayers;

        [Tooltip("掃描碰撞半徑（公尺）— 用 SphereCast 在每幀位移之間做連續偵測,防止高速子彈穿透敵人。建議 0.1~0.3 公尺,設 0 則用射線（最便宜但容易擦邊不中）")]
        public float SweepRadius = 0.15f;

        [Header("Impact VFX/SFX")]
        [Tooltip("命中特效預製體")]
        public GameObject ImpactVFXPrefab;

        [Tooltip("命中音效")]
        public AudioClip ImpactSFX;

        [Tooltip("命中特效存活時間")]
        public float ImpactVFXLifetime = 2f;

        [Tooltip("命中特效是否附著在被命中物體表面（例如箭矢插在表面）")]
        public bool AttachImpactToSurface;

        [Tooltip("用於偵測表面法線的射線距離")]
        public float SurfaceDetectionDistance = 2f;
    }
}
