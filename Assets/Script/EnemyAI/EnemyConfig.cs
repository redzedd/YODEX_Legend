using UnityEngine;

namespace EnemyAI
{
    /// <summary>
    /// 敵人純數值設定（ScriptableObject）— 移動、感知、戰鬥距離、圖層
    /// 設計師可複製 .asset 變體調出不同敵人類型，不需動程式碼
    /// </summary>
    [CreateAssetMenu(menuName = "EnemyAI/Enemy Config", fileName = "EnemyConfig")]
    public class EnemyConfig : ScriptableObject
    {
        #region Serialized Fields

        [Header("戰鬥數值（血量 / 韌性）")]
        [SerializeField] [Tooltip("最大生命值 — Start 時寫入 CombatAttributeSet.MaxHealth & Health。建議雜兵 30~80、精英 150~300")]
        private float _maxHealth = 100f;

        [SerializeField] [Tooltip("最大韌性值（擊破防值）— 累積受到的「硬直傷害」達到此值會被擊破進入 Stagger。\n越高越耐打、越難被打出硬直。建議雜兵 30~60、重甲/精英 100~200")]
        private float _maxPoise = 100f;

        [SerializeField] [Tooltip("韌性每秒回復量 — 未受擊一段時間後韌性自動回復的速度。\n設 0 = 韌性不自動回復(被打多了遲早被擊破);越大越快回滿。建議 10~30")]
        private float _poiseRegen = 20f;

        [Header("移動")]
        [SerializeField] [Tooltip("巡邏速度（公尺/秒）— 建議 1.5~2.5")]
        private float _walkSpeed = 2f;

        [SerializeField] [Tooltip("追擊速度（公尺/秒）— 建議 3.5~6")]
        private float _runSpeed = 4.5f;

        [SerializeField] [Tooltip("轉身速度（度/秒）— 越大轉越快，建議 360~900")]
        private float _rotationSpeed = 720f;

        [SerializeField] [Tooltip("重力倍率 — 乘在 Physics.gravity.y 上的係數。\n1 = 標準重力，覺得敵人飄就調大。建議 1.5~3（落地較有重量感）；4 以上會明顯像 BOTW 風格的快速下墜")]
        private float _gravityMultiplier = 2f;

        [Header("感知")]
        [SerializeField] [Tooltip("視野半徑（公尺）— 敵人能看多遠")]
        private float _viewRadius = 12f;

        [SerializeField] [Tooltip("視野角度（度，總角度）— 例如 120 代表前方左右各 60 度")]
        private float _viewAngle = 120f;

        [SerializeField] [Tooltip("聽覺半徑（公尺）— 玩家在此範圍內以足夠速度移動會被聽到（自動觸發 Alert）。Scene View 內以青色圓圈標示。建議 5~10")]
        private float _hearingRadius = 6f;

        [SerializeField] [Tooltip("玩家觸發聽覺的最小水平移動速度（公尺/秒）— 慢於此值視為「潛行」不被聽到。建議 3.5~4.5（走路約 2-3、跑步約 5-7）")]
        private float _hearingSpeedThreshold = 4f;

        [SerializeField] [Tooltip("放棄追擊距離（公尺）— 玩家拉開超過此距離後敵人會放棄")]
        private float _loseTargetDistance = 18f;

        [Header("戰鬥距離（邊緣對邊緣）")]
        [SerializeField] [Tooltip("接近玩家後停止的「邊緣對邊緣」距離（公尺）— 已自動扣掉雙方 CharacterController 半徑，縮放敵人/玩家不影響判定。建議 0.5~2")]
        private float _stopDistance = 1f;

        [Header("圖層")]
        [SerializeField] [Tooltip("玩家所在的 Layer")]
        private LayerMask _playerLayer;

        [SerializeField] [Tooltip("視線會被擋住的 Layer — 通常是牆壁、地形")]
        private LayerMask _obstacleLayer;

        #endregion

        #region Properties

        public float MaxHealth => _maxHealth;
        public float MaxPoise => _maxPoise;
        public float PoiseRegen => _poiseRegen;
        public float WalkSpeed => _walkSpeed;
        public float RunSpeed => _runSpeed;
        public float RotationSpeed => _rotationSpeed;
        public float GravityMultiplier => _gravityMultiplier;
        public float ViewRadius => _viewRadius;
        public float ViewAngle => _viewAngle;
        public float HearingRadius => _hearingRadius;
        public float HearingSpeedThreshold => _hearingSpeedThreshold;
        public float LoseTargetDistance => _loseTargetDistance;
        public float StopDistance => _stopDistance;
        public LayerMask PlayerLayer => _playerLayer;
        public LayerMask ObstacleLayer => _obstacleLayer;

        #endregion
    }
}
