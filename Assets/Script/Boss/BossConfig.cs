using UnityEngine;

namespace Boss
{
    /// <summary>
    /// Boss 戰專用數值設定 (ScriptableObject)
    /// 完全獨立於雜兵系統 — Boss 戰沒有巡邏/感知/聽覺等雜兵邏輯,
    /// 只關注「戰鬥本身」的數值 (體型、HP、韌性、移動、階段切換)
    /// </summary>
    [CreateAssetMenu(menuName = "YODEX/Boss/Boss Config", fileName = "BossConfig")]
    public class BossConfig : ScriptableObject
    {
        #region Serialized Fields

        [Header("體型 (距離計算用)")]
        [SerializeField] [Tooltip("Boss 視覺半徑 (公尺) — 用於 FSM 追擊/停止/醒來等距離判斷的「Boss 自身半徑」。\nCharacterController.radius 通常太小 (給物理碰撞用,設大會擋路),不足以代表大型 Boss 的視覺尺寸,所以這裡另設一個欄位給 AI 邏輯用。\n建議: 大型 Boss 3~6, 中型 1.5~3, 小型 0.5~1")]
        private float _bossRadius = 3f;

        [Header("血量")]
        [SerializeField] [Tooltip("最大血量 — Start 時寫入 CombatAttributeSet 的 MaxHealth 與 Health。大型 Boss 建議 1000~2500")]
        private float _maxHealth = 1500f;

        [Header("韌性 Poise (第 4 步啟用受擊系統時才生效)")]
        [SerializeField] [Tooltip("最大韌性 — 累積受到的韌性傷害達此值會被擊破進硬直。大型 Boss 建議 150~300")]
        private float _maxPoise = 200f;

        [SerializeField] [Tooltip("韌性每秒回復量 — 未受擊一段時間後韌性自動回復。建議 10~25")]
        private float _poiseRegen = 15f;

        [Header("地面移動 (第 2 步啟用地面 AI 時才生效)")]
        [SerializeField] [Tooltip("地面行走速度 (公尺/秒) — InPlace 動畫,位移由程式套用。建議 2~3.5")]
        private float _walkSpeed = 2.5f;

        [SerializeField] [Tooltip("地面追擊速度 (公尺/秒) — InPlace 動畫,位移由程式套用。建議 4.5~6.5")]
        private float _runSpeed = 5.5f;

        [SerializeField] [Tooltip("地面轉身速度 (度/秒) — 大型 Boss 轉身應該較慢、有重量感,建議 180~360")]
        private float _groundRotationSpeed = 270f;

        [SerializeField] [Tooltip("地面停止距離 (邊緣對邊緣,公尺) — 玩家邊緣到 Boss 邊緣 (Boss Radius 之外) 的距離,Boss 追到此距離停下。\nFSM 會自動換算成「中心到中心」距離傳給 Locomotion (centerStop = stopDistance + BossRadius + 玩家半徑)。\n建議 1~3 (玩家剛好能站在 Boss 邊緣揮砍)")]
        private float _groundStopDistance = 1.5f;

        [Header("階段切換 (設 0 表示單階段)")]
        [SerializeField] [Tooltip("二階段血量門檻 (0~1) — 血量降到此比例會觸發二階段 (如:強制起飛)。0 = 不分階段。0.5 = 半血質變")]
        private float _phase2HealthThreshold = 0.5f;

        #endregion

        #region Properties

        public float BossRadius => _bossRadius;
        public float MaxHealth => _maxHealth;
        public float MaxPoise => _maxPoise;
        public float PoiseRegen => _poiseRegen;
        public float WalkSpeed => _walkSpeed;
        public float RunSpeed => _runSpeed;
        public float GroundRotationSpeed => _groundRotationSpeed;
        public float GroundStopDistance => _groundStopDistance;
        public float Phase2HealthThreshold => _phase2HealthThreshold;

        #endregion
    }
}
