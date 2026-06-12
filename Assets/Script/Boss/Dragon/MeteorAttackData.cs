using UnityEngine;

namespace Boss.Dragon
{
    /// <summary>
    /// 隕石攻擊數值設定 (ScriptableObject)
    /// 飛龍 Scream 期間定時生成隕石 Prefab — Prefab 自己處理「警告/下降/爆炸」完整視覺
    /// 傷害機制:Prefab 內 PS Collision 觸發 (粒子撞地面) → Handler 在碰撞點做 OverlapSphere splash 找玩家
    /// 跟玩家 AoEBehaviour (MeteorRain) 概念一致
    /// </summary>
    [CreateAssetMenu(menuName = "YODEX/Boss/Meteor Attack Data", fileName = "MeteorAttackData")]
    public class MeteorAttackData : ScriptableObject
    {
        #region Serialized Fields

        [Header("生成節奏")]
        [SerializeField] [Tooltip("Scream 觸發後等待多久 spawn 第一顆隕石 (秒) — 等飛龍動畫進入咆哮姿勢。建議 0.5~1.5")]
        private float _initialDelay = 1f;

        [SerializeField] [Tooltip("總共生成幾顆隕石。建議 3~6")]
        private int _meteorCount = 5;

        [SerializeField] [Tooltip("每顆隕石之間的 spawn 間隔 (秒)。建議 0.3~0.8")]
        private float _spawnInterval = 0.5f;

        [Header("隕石 Prefab")]
        [SerializeField] [Tooltip("隕石 Prefab — 一體式設計,內含警告/下降/爆炸完整視覺。\n必須條件:\n  (1) Prefab Root 掛 MeteorPSCollisionHandler 元件\n  (2) 內含 ParticleSystem 開啟 Collision module + Send Collision Messages\n  (3) PS Collides With 勾「地面 Layer」 (撞地面觸發落地事件)")]
        private GameObject _meteorPrefab;

        [SerializeField] [Tooltip("Spawn 後自動銷毀時間 (秒) — 給足夠時間讓 Prefab 內所有粒子播完。建議 3~6,0 = 不主動銷毀")]
        private float _meteorLifetime = 4f;

        [Header("瞄準散佈")]
        [SerializeField] [Tooltip("Spawn 位置漂移半徑 (公尺) — 隕石生成位置 = 玩家當下位置 ±此距離隨機,避免多顆疊一點。建議 1~4")]
        private float _spawnSpreadRadius = 2f;

        [Header("濺射傷害")]
        [SerializeField] [Tooltip("濺射半徑 (公尺) — 隕石落地點 (PS 碰撞點) 周圍此半徑內的玩家會中招。\n比 PS Collider 直接打更穩定 (粒子撞到地面就算落地,範圍判定獨立)。\n建議 2~5")]
        private float _splashRadius = 3f;

        [SerializeField] [Tooltip("✅ 勾選後在 Scene View / Game View (開 Gizmos toggle) 顯示每次濺射的位置與範圍 wire sphere,持續 2.5 秒後淡出。\nConsole 也會印每次 splash 的座標與命中數。Debug 用,正式關閉")]
        private bool _debugDrawSplash = true;

        [Header("傷害數值 (由 MeteorPSCollisionHandler 套用)")]
        [SerializeField] [Tooltip("基礎傷害。建議 80~150 (重攻擊級)")]
        private float _damage = 100f;

        [SerializeField] [Tooltip("攻擊類型 (給 IHitReceiver.OnHit 用)")]
        private AttackTier _attackTier = AttackTier.Heavy;

        [SerializeField] [Tooltip("韌性傷害 — 命中時消耗玩家韌性。建議 30~60")]
        private float _dazeBuildup = 40f;

        [SerializeField] [Tooltip("擊退距離 (公尺)。建議 2~4")]
        private float _knockbackDistance = 3f;

        [SerializeField] [Tooltip("濺射命中 Layer Mask — 只勾玩家所在 Layer。\n⚠️ 跟 ParticleSystem Collision module 的 Collides With **不同**:\n  • PS Collides With = 勾「地面 Layer」(粒子撞地觸發落地)\n  • Hit Layer Mask = 勾「玩家 Layer」(splash 範圍內找誰扣血)")]
        private LayerMask _hitLayerMask = ~0;

        #endregion

        #region Properties

        public float InitialDelay => _initialDelay;
        public int MeteorCount => _meteorCount;
        public float SpawnInterval => _spawnInterval;
        public GameObject MeteorPrefab => _meteorPrefab;
        public float MeteorLifetime => _meteorLifetime;
        public float SpawnSpreadRadius => _spawnSpreadRadius;
        public float SplashRadius => _splashRadius;
        public bool DebugDrawSplash => _debugDrawSplash;
        public float Damage => _damage;
        public AttackTier AttackTier => _attackTier;
        public float DazeBuildup => _dazeBuildup;
        public float KnockbackDistance => _knockbackDistance;
        public LayerMask HitLayerMask => _hitLayerMask;

        #endregion
    }
}
