using System.Collections.Generic;
using Animancer;
using GAS;
using UnityEngine;

namespace Enemy.AttackSystem
{
    /// <summary>
    /// 敵人單一攻擊招式的設定資料。
    /// 一份 .asset = 一招（橫斬、突刺、跳劈...），可被多個敵人共用。
    /// </summary>
    [CreateAssetMenu(
        fileName = "EAP_NewAttack",
        menuName = "YODEX/敵人/攻擊招式 (Attack Profile)",
        order = 0)]
    public class EnemyAttackProfile : ScriptableObject
    {
        // ─── 基本資料 ─────────────────────────────────────────────
        [Header("基本資料")]

        [SerializeField]
        [Tooltip("攻擊招式名稱（顯示與 Debug 用，例如「橫斬」、「突刺」）")]
        private string _attackName = "新攻擊";

        [SerializeField]
        [Tooltip("Animancer 播放的攻擊動畫片段。攻擊總時長 = 此動畫片段長度（動畫播完自動結束攻擊）")]
        private AnimationClip _animationClip;

        [SerializeField]
        [Tooltip("攻擊進入過渡時間（秒）— 從前一個動畫（走路/待機）淡入到攻擊動畫的時長。建議 0.1~0.2 秒。0 = 瞬切（會很突兀）")]
        private float _entryFadeDuration = 0.15f;

        [SerializeField]
        [Tooltip("攻擊動畫結束後的後搖時間（秒）— 期間敵人會停下播 Idle，不追擊、不出招\n用來平衡跑速太快的敵人，給玩家反應 / 拉開距離的空檔。建議 0~1.5")]
        private float _recoveryDuration = 0f;

        // ─── 預警 / 可招架窗口 ────────────────────────────────────
        [Space]
        [Header("預警 / 可招架窗口")]

        [SerializeField]
        [Tooltip("這招能不能被招架。\n勾選 = 黃光（可換武器彈反）\n取消 = 紅光（必須閃避，無法擋）")]
        private bool _isParryable = true;

        [SerializeField]
        [Tooltip("黃光特效持續時間（秒）。從攻擊開始 t=0 亮起，持續此秒數。建議 0.3~0.5 秒")]
        private float _parryFlashDuration = 0.35f;

        [SerializeField]
        [Tooltip("格擋緩衝時間（秒）。黃光熄滅後到攻擊判定生效前的緩衝（高手挑戰區間）。\n招架窗總時長 = ParryFlashDuration + ParryBufferDuration\n若動畫的 HitStart 早於此總時長，Executor 會自動減速 HitStart 前的動畫使其拉長到此總時長。建議 0.1~0.25 秒")]
        private float _parryBufferDuration = 0.15f;

        [SerializeField]
        [Tooltip("這招被招架時敵人會被「彈刀」（攻擊動畫被打斷、切換到下方 Stagger 動畫）：\n✗ 取消（不彈刀）：玩家被擊退、敵人動畫繼續播完。\n  → 適用：中攻擊、多段連擊的「非最後一段」\n✓ 勾選（會彈刀）：玩家不被擊退、頓幀結束後敵人切到 Stagger 動畫。\n  → 適用：單段輕攻擊、多段連擊的「最後一段」")]
        private bool _isParryStaggers = false;

        [SerializeField]
        [Tooltip("敵人被彈刀後播放的動畫。\n過渡時間由 ClipTransition 自帶的 FadeDuration 控制：\n• 0 = 瞬切無過渡\n• 0.05~0.15 = 一點點過渡（推薦）\n• > 0.2 = 明顯過渡\n僅當 IsParryStaggers 勾選時生效。建議：敵人被打飛/踉蹌的反應動作")]
        private ClipTransition _parryStaggerAnimation;

        // ─── 傷害判定窗口 ─────────────────────────────────────────
        [Space]
        [Header("傷害判定窗口")]

        [SerializeField]
        [Tooltip("攻擊開始後幾秒，武器開始有判定（動畫上的真實時間，不受招架減速影響）")]
        private float _hitStart = 0.8f;

        [SerializeField]
        [Tooltip("攻擊開始後幾秒，武器結束判定（動畫上的真實時間）")]
        private float _hitEnd = 1.0f;

        [SerializeField]
        [Tooltip("命中時造成的基礎傷害。建議 10~50 對應普攻、80~150 對應重攻擊")]
        private float _damage = 10f;

        [SerializeField]
        [Tooltip("Hitbox 綁定的骨骼名稱（例如武器骨「RightHandWeapon」）。\n留空則綁角色根節點")]
        private string _hitboxBone = "";

        [SerializeField]
        [Tooltip("相對 HitboxBone 的中心偏移（local space）。武器骨通常 (0, 0, +Z) 偏向劍尖")]
        private Vector3 _hitboxOffset = Vector3.zero;

        [SerializeField]
        [Tooltip("相對 HitboxBone 的旋轉（Euler 角度，local space）— 預設 0 跟著骨骼方向；想斜切就轉 X/Y/Z")]
        private Vector3 _hitboxRotation = Vector3.zero;

        [SerializeField]
        [Tooltip("Hitbox 大小（local space 的 X/Y/Z 全長，不是半徑）。\n建議：劍 (0.2, 0.2, 1.2)、拳 (0.4, 0.4, 0.4)、大斧 (0.3, 0.3, 1.5)")]
        private Vector3 _hitboxSize = new Vector3(0.3f, 0.3f, 1f);

        [SerializeField]
        [Tooltip("Hitbox 要打到的層級（通常勾選玩家所在的 Layer）")]
        private LayerMask _hitboxLayerMask = ~0;

        [SerializeField]
        [Tooltip("額外 Hitbox 清單 — 多段攻擊（左劈→右斬→踢）或多判定區（同時前後）用。每筆都有獨立的時間範圍、骨骼、形狀\n命中規則：所有 hitbox（主 + 額外）按時間檢查，第一個命中即整招判定完成（不重複扣血）")]
        private List<EnemyAttackHitboxData> _extraHitboxes = new List<EnemyAttackHitboxData>();

        // ─── 遠程攻擊（可選）─────────────────────────────────────
        [Space]
        [Header("遠程攻擊 (設了 Aim Mode != None 就會在 HitStart 發射投射物，跳過近戰 Hitbox 偵測)")]

        [SerializeField]
        [Tooltip("瞄準模式 — 決定這招是近戰還是遠程\n• None = 近戰攻擊（用上方的 Hitbox 做命中判定）\n• Forward = 朝發射骨骼的 forward 方向直線射\n• TowardPlayer = 發射瞬間鎖定玩家位置直線射（一射出後固定方向）\n• TowardPlayerHoming = 朝玩家發射 + 邊飛邊追蹤（同時 ProjectileData 的 HomingEnabled 也要勾選）")]
        private RangedAimMode _rangedAimMode = RangedAimMode.None;

        [SerializeField]
        [Tooltip("投射物資料 SO — 套用 GAS 的 ProjectileData，定義速度、外型、命中特效、穿透爆炸等\n可直接重用玩家現有的箭矢 / 法球 .asset")]
        private ProjectileData _rangedProjectile;

        [SerializeField]
        [Tooltip("命中傷害 GameplayEffect — 投射物命中玩家時套用（同樣是 GAS 流程，自動扣血）\n可重用玩家既有的傷害 GE 資產")]
        private GameplayEffect _rangedDamageEffect;

        [SerializeField]
        [Tooltip("發射骨骼名稱 — 通常是武器、手、頭頂、嘴巴\n留空 = 從敵人根節點發射")]
        private string _projectileSpawnBone = "";

        [SerializeField]
        [Tooltip("相對發射骨骼的位置偏移（local space）— 例：(0, 0, 0.5) 從劍尖前方 50cm 發射")]
        private Vector3 _projectileSpawnOffset = Vector3.zero;

        [SerializeField]
        [Tooltip("Forward 模式的射出方向角度偏移（相對發射骨骼）— 只在 Aim Mode = Forward 時生效。\n(0,0,0) = 沿發射骨骼的 forward 方向。\nX = 上下俯仰（正值朝上），Y = 左右偏轉，Z = 翻滾（直線彈道無影響）。\n可在「攻擊招式時間軸編輯器」開預覽後，於場景用旋轉 gizmo 直接拖出方向。\n提示：發射骨骼留空 = 從敵人根節點發射，此時方向就相對敵人正面，最直覺。")]
        private Vector3 _projectileForwardAngles = Vector3.zero;

        // ─── 位移 ────────────────────────────────────────────────
        [Space]
        [Header("位移")]

        [SerializeField]
        [Tooltip("攻擊期間敵人的位移方式")]
        private AttackMoveType _moveType = AttackMoveType.None;

        [SerializeField]
        [Tooltip("ManualLerp 模式下，整段攻擊期間朝面向方向移動的距離（公尺）。建議 0~5 公尺")]
        private float _moveDistance = 0f;

        [SerializeField]
        [Tooltip("HitStart 那一刻，敵人從攻擊起點朝 forward 方向已位移的距離（公尺）。\n用於玩家招架時精準預測敵人位置：玩家會瞬移到「攻擊起點 + forward × 此距離」前方。\n• Root Motion 動畫:請手填從動畫量到的距離(例如「敵人衝 3m 後揮刀」就填 3)\n• ManualLerp 線性位移：留 0 會自動用 MoveDistance × HitStart ÷ 動畫片段長度 推算")]
        private float _distanceAtHit = 0f;

        // ─── 選招條件（給 EnemyController.AttackPickMode = RangeAndWeight 時使用）──
        [Space]
        [Header("選招條件（給 RangeAndWeight 模式用）")]

        [SerializeField]
        [Tooltip("選招最小「邊緣對邊緣」距離（公尺）— 玩家離敵人邊緣小於此距離時這招不會被選\n已自動扣掉雙方 CharacterController 半徑，縮放敵人/玩家不影響判定\n例：「衝刺攻擊」設 4 → 玩家在 4m 內不會用這招\n建議：近戰 0、衝刺 3~5、遠程 8+")]
        private float _minPickDistance = 0f;

        [SerializeField]
        [Tooltip("選招最大「邊緣對邊緣」距離（公尺）— 玩家離敵人邊緣超過此距離時這招不會被選\n已自動扣掉雙方 CharacterController 半徑\n例：「橫斬」設 2 → 玩家超過 2m 不會用這招\n建議：近戰 2~4、衝刺 12~20、遠程 25+")]
        private float _maxPickDistance = 100f;

        [SerializeField]
        [Tooltip("選招權重（相對機率）— 同距離有多招符合時，權重越高越容易被選中\n例：衝刺權重 3、跳劈權重 1 → 兩招都符合範圍時衝刺機率 3/4\n0 = 永不被選")]
        private float _pickWeight = 1f;

        [SerializeField]
        [Tooltip("使用後冷卻秒數 — 用過這招後幾秒內不會再被選\n0 = 不冷卻可連續使用\n建議：王牌大招 5~10、特殊招 2~4、普通招 0~1")]
        private float _pickCooldown = 0f;

        // ─── 失衡值（之後使用）────────────────────────────────────
        [Space]
        [Header("失衡值（之後使用）")]

        [SerializeField]
        [Tooltip("命中玩家時累積的失衡值（負面 Daze 效果，之後才啟用）。建議 5~30")]
        private float _dazeBuildup = 10f;

        // ─── 攻擊型態與命中特效 ──────────────────────────────────
        [Space]
        [Header("攻擊型態與命中特效")]

        [SerializeField]
        [Tooltip("攻擊型態 — 決定玩家受擊的反應級別\n• Normal = 一般攻擊（打斷玩家基本動作，會被攻擊霸體擋下）\n• Light = 輕攻擊（只觸發抖動 VFX，不打斷任何動作；對應 DOT / 短劍輕擊）\n• Heavy = 重攻擊（能打斷玩家的攻擊霸體；Poise 擊破時走 Knockback 而非 Stagger）")]
        private AttackTier _attackTier = AttackTier.Normal;

        [SerializeField]
        [Tooltip("擊退漸近距離（公尺）— 命中時玩家朝攻擊方向被推開的距離。\n0 = 不擊退（純動畫反應，無位移）\n1~2 = 中等擊退\n3+ = 強擊退")]
        private float _knockbackDistance = 0f;

        [SerializeField]
        [Tooltip("命中玩家時生成的「全身受擊特效」Prefab。\n• 在玩家模型中心位置生成\n• 玩家成功格擋 / 招架時不會生成（wasBlocked = true 時跳過）\n• 留空 = 不生成命中特效")]
        private GameObject _hitVfxPrefab;

        [SerializeField]
        [Tooltip("相對玩家中心的位置偏移（公尺）。例：(0, 1, 0) = 胸口高度。\n讓全身特效對準身體中段而不是腳下")]
        private Vector3 _hitVfxOffset = new Vector3(0f, 1f, 0f);

        [SerializeField]
        [Tooltip("命中特效縮放倍率（在 Prefab 自帶 scale 上乘上此倍率）。\n(1, 1, 1) = 維持 Prefab 原始大小，(2, 2, 2) = 雙倍，(0.5, 0.5, 0.5) = 半倍")]
        private Vector3 _hitVfxScaleMultiplier = Vector3.one;

        [SerializeField]
        [Tooltip("命中特效自動銷毀秒數。\n> 0：經過此秒數後 Destroy GameObject\n= 0：不主動銷毀，讓 ParticleSystem 自然消逝（記得在 Prefab 的 ParticleSystem Main 設 Stop Action = Destroy）")]
        private float _hitVfxLifetime = 2f;

        // ─── 特效事件 (透過 Timeline 視窗編輯) ────────────────────
        // 不用 [Header] 露在標準 Inspector — 透過獨立的攻擊招式時間軸編輯器視窗編輯
        [SerializeField] [HideInInspector]
        private List<EnemyAttackVfxEvent> _vfxEvents = new List<EnemyAttackVfxEvent>();

        // ─── 公開存取（程式讀取用，設計師不會看到）───────────────
        public string AttackName => _attackName;
        public AnimationClip AnimationClip => _animationClip;
        // 動畫片段長度（秒）— 攻擊總時長以動畫為準。未指定動畫時回傳 0
        public float Duration => _animationClip != null ? _animationClip.length : 0f;
        public float EntryFadeDuration => _entryFadeDuration;
        public float RecoveryDuration => _recoveryDuration;

        public bool IsParryable => _isParryable;
        public float ParryFlashDuration => _parryFlashDuration;
        public float ParryBufferDuration => _parryBufferDuration;
        public float ParryWindowDuration => _parryFlashDuration + _parryBufferDuration;
        public bool IsParryStaggers => _isParryStaggers;
        public ClipTransition ParryStaggerAnimation => _parryStaggerAnimation;

        public float HitStart => _hitStart;
        public float HitEnd => _hitEnd;
        public float Damage => _damage;
        public string HitboxBone => _hitboxBone;
        public Vector3 HitboxOffset => _hitboxOffset;
        public Vector3 HitboxRotation => _hitboxRotation;
        public Vector3 HitboxSize => _hitboxSize;
        public LayerMask HitboxLayerMask => _hitboxLayerMask;
        public IReadOnlyList<EnemyAttackHitboxData> ExtraHitboxes => _extraHitboxes;
        public int ExtraHitboxCount => _extraHitboxes != null ? _extraHitboxes.Count : 0;

        public AttackMoveType MoveType => _moveType;
        public float MoveDistance => _moveDistance;
        public float DistanceAtHit => _distanceAtHit;

        public RangedAimMode RangedAimMode => _rangedAimMode;
        public ProjectileData RangedProjectile => _rangedProjectile;
        public GameplayEffect RangedDamageEffect => _rangedDamageEffect;
        public string ProjectileSpawnBone => _projectileSpawnBone;
        public Vector3 ProjectileSpawnOffset => _projectileSpawnOffset;
        public Vector3 ProjectileForwardAngles => _projectileForwardAngles;
        // 是否為遠程攻擊 — 看 AimMode（非 None 即遠程）。簡化 Executor 判斷
        public bool IsRanged => _rangedAimMode != RangedAimMode.None;

        public float MinPickDistance => _minPickDistance;
        public float MaxPickDistance => _maxPickDistance;
        public float PickWeight => _pickWeight;
        public float PickCooldown => _pickCooldown;

        public float DazeBuildup => _dazeBuildup;

        public AttackTier AttackTier => _attackTier;
        public float KnockbackDistance => _knockbackDistance;
        public GameObject HitVfxPrefab => _hitVfxPrefab;
        public Vector3 HitVfxOffset => _hitVfxOffset;
        public Vector3 HitVfxScaleMultiplier => _hitVfxScaleMultiplier;
        public float HitVfxLifetime => _hitVfxLifetime;

        // VFX 事件清單（唯讀對外公開；Editor 視窗透過 SerializedProperty 直接編輯 _vfxEvents）
        public IReadOnlyList<EnemyAttackVfxEvent> VfxEvents => _vfxEvents;

        // 在指定播放時間點是否處於可招架狀態（招架窗：playbackTime ∈ [0, ParryWindowDuration]）
        public bool IsInParryWindow(float playbackTime)
        {
            if (!_isParryable)
            {
                return false;
            }
            return playbackTime >= 0f && playbackTime <= ParryWindowDuration;
        }

        // Inspector 改動時自動修正不合理時序
        // 注意：HitStart 不再受 ParryWindow 限制 — 動畫的 HitStart 可以早於招架窗，Executor 會自動減速動畫前段
        private void OnValidate()
        {
            if (_hitEnd < _hitStart)
            {
                _hitEnd = _hitStart;
            }
        }
    }

    /// <summary>
    /// 額外 Hitbox 資料（給多段攻擊或多判定區用）
    /// </summary>
    [System.Serializable]
    public class EnemyAttackHitboxData
    {
        [Tooltip("名稱（顯示用，例如「左劈」「踢擊」）")]
        public string Label = "Extra Hitbox";

        [Tooltip("命中判定開始時間（秒）— 動畫上的真實時間")]
        public float HitStart = 0.8f;

        [Tooltip("命中判定結束時間（秒）")]
        public float HitEnd = 1.0f;

        [Tooltip("Hitbox 綁定的骨骼名稱（例 RightHand）。留空 = 角色根節點")]
        public string Bone = "";

        [Tooltip("相對骨骼的中心偏移（local space）")]
        public Vector3 Offset = Vector3.zero;

        [Tooltip("相對骨骼的旋轉（Euler，local）")]
        public Vector3 Rotation = Vector3.zero;

        [Tooltip("大小（X/Y/Z 全長，不是半徑）")]
        public Vector3 Size = new Vector3(0.3f, 0.3f, 1f);

        [Tooltip("命中的 Layer")]
        public LayerMask LayerMask = ~0;
    }

    /// <summary>
    /// 遠程攻擊瞄準方式
    /// </summary>
    public enum RangedAimMode
    {
        // 近戰攻擊（用 Hitbox 做命中判定）— 不發射投射物
        None = 0,
        // 朝發射骨骼的 forward 方向直線射
        Forward = 1,
        // 發射瞬間鎖定玩家位置直線射出（不追蹤）
        TowardPlayer = 2,
        // 朝玩家方向射 + 邊飛邊追蹤（須同時開啟 ProjectileData.HomingEnabled）
        TowardPlayerHoming = 3,
    }

    /// <summary>
    /// 攻擊期間的位移方式。
    /// </summary>
    public enum AttackMoveType
    {
        // 不位移，敵人在原地揮砍
        None = 0,

        // 程式碼用 Lerp 推動，簡單但動畫腳底可能會滑
        ManualLerp = 1,

        // 用 Animancer 動畫自帶的 Root Motion 位移，視覺最自然
        RootMotion = 2,
    }
}
