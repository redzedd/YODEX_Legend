using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 完美閃避數據 - 定義完美閃避的所有可配置參數
    /// 包含子彈時間、反擊窗口、殘留影分身、Cue 等設定
    /// </summary>
    [CreateAssetMenu(fileName = "New PerfectDodge Data", menuName = "GAS/Abilities/Perfect Dodge Data")]
    public class PerfectDodgeData : ScriptableObject
    {
        [Header("Bullet Time（子彈時間）")]
        [Tooltip("子彈時間持續秒數（真實時間）")]
        [SerializeField] private float _bulletTimeDuration = 2.0f;
        public float BulletTimeDuration => _bulletTimeDuration;

        [Tooltip("時間縮放比例（0.1 = 十分之一速度）")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _bulletTimeScale = 0.1f;
        public float BulletTimeScale => _bulletTimeScale;

        [Tooltip("進入子彈時間的過渡時間（真實秒）")]
        [SerializeField] private float _bulletTimeEnterDuration = 0.05f;
        public float BulletTimeEnterDuration => _bulletTimeEnterDuration;

        [Tooltip("退出子彈時間的過渡時間（真實秒）")]
        [SerializeField] private float _bulletTimeExitDuration = 0.3f;
        public float BulletTimeExitDuration => _bulletTimeExitDuration;

        [Header("Counter Attack（反擊窗口）")]
        [Tooltip("反擊傷害加成效果（Duration 類型的 GameplayEffect）")]
        [SerializeField] private GameplayEffect _counterDamageBonusEffect;
        public GameplayEffect CounterDamageBonusEffect => _counterDamageBonusEffect;

        [Tooltip("自動鎖定最近敵人的範圍")]
        [SerializeField] private float _autoTargetRange = 8.0f;
        public float AutoTargetRange => _autoTargetRange;

        [Header("Cues（視覺/音效回饋）")]
        [Tooltip("完美閃避觸發時的 Cue（建議使用 CombinedCue：頓幀+閃光+音效）")]
        [SerializeField] private GameplayTag _perfectDodgeCue;
        public GameplayTag PerfectDodgeCue => _perfectDodgeCue;

        [Tooltip("子彈時間結束時的 Cue")]
        [SerializeField] private GameplayTag _bulletTimeEndCue;
        public GameplayTag BulletTimeEndCue => _bulletTimeEndCue;

        [Header("Ghost（殘留影分身）")]
        [Tooltip("影分身存在時間（秒）— 閃避瞬間在原位留下的虛擬受擊點")]
        [SerializeField] private float _ghostDuration = 0.3f;
        public float GhostDuration => _ghostDuration;

        [Tooltip("影分身偵測半徑")]
        [SerializeField] private float _ghostRadius = 0.8f;
        public float GhostRadius => _ghostRadius;

        [Header("Cooldown（冷卻）")]
        [Tooltip("連續完美閃避的最小間隔（真實秒）— 防止連續閃避時閃光疲勞")]
        [SerializeField] private float _minInterval = 0.5f;
        public float MinInterval => _minInterval;
    }
}
