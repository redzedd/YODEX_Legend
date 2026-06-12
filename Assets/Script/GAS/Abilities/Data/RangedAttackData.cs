using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// AoE 中心位置的解析來源(由攻擊層決定,與 AoE 特效 prefab 解耦)
    /// </summary>
    public enum AoEOriginMode
    {
        /// <summary>瞄準相機螢幕中心射線命中地面點（火雨類）</summary>
        ScreenAim,
        /// <summary>鎖定目標位置（閃電類）</summary>
        LockedTarget,
        /// <summary>玩家前方固定距離 — 有鎖定/AutoFace 目標時自動改落在目標位置(震波/旋風斬類)</summary>
        PlayerForward,
        /// <summary>玩家光標 — 蓄力期間用移動鍵控制 AoE 位置(重攻擊地面標記類),鎖定優先當起點,相機相對 WASD 操作</summary>
        PlayerCursor
    }

    /// <summary>
    /// 遠程攻擊類型
    /// </summary>
    public enum RangedAttackType
    {
        /// <summary>投射物（箭矢、彈丸）</summary>
        Projectile,
        /// <summary>目標地面 AoE（火雨，需要瞄準位置）</summary>
        AoETargeted,
        /// <summary>指向鎖定目標的 AoE（閃電，自動落在目標位置）</summary>
        AoEAtTarget,
        /// <summary>即時命中（射線判定）</summary>
        Hitscan
    }

    /// <summary>
    /// 蓄力模式
    /// </summary>
    public enum ChargeMode
    {
        /// <summary>無蓄力（按下即發射）</summary>
        None,
        /// <summary>長按蓄力，鬆開發射（傷害隨蓄力時間增加）</summary>
        HoldToCharge,
        /// <summary>長按進入瞄準模式，按攻擊鍵發射</summary>
        HoldToAim
    }

    /// <summary>
    /// 蓄力期間要監聽的「按住中」按鍵 — 對應實際 InputAction 的 binding
    /// 系統在 HoldToCharge / HoldToAim 期間用這個來偵測「玩家是否還按住」
    /// </summary>
    public enum ChargeInputBinding
    {
        /// <summary>RangeAttackAction（預設右鍵綁定的話用這個）</summary>
        RangeAttack,
        /// <summary>LightAttackAction（左鍵綁定的話用這個）</summary>
        LightAttack,
        /// <summary>HeavyAttackAction（如果你把蓄力綁在重攻擊鍵）</summary>
        HeavyAttack
    }

    /// <summary>
    /// 遠程攻擊數據 - 定義一次遠程攻擊的所有參數
    /// 繼承 AttackDataBase 共用 Timing、Combo、TimelineEvents
    /// </summary>
    [CreateAssetMenu(fileName = "New Ranged Attack", menuName = "GAS/Abilities/Ranged Attack Data")]
    public class RangedAttackData : AttackDataBase
    {
        [Header("Attack Type")]
        [Tooltip("遠程攻擊類型")]
        public RangedAttackType AttackType = RangedAttackType.Projectile;

        [Tooltip("蓄力模式")]
        public ChargeMode Charge = ChargeMode.None;

        [Tooltip("蓄力期間偵測哪個 InputAction 仍按住中。\n• RangeAttack: 預設,綁右鍵時用\n• LightAttack: 左鍵綁定\n• HeavyAttack: 重攻擊鍵綁定（你目前的設定可能屬此項）")]
        public ChargeInputBinding ChargeInput = ChargeInputBinding.RangeAttack;

        [Header("Animation - Fire")]
        [Tooltip("發射動畫（無蓄力時使用）")]
        public ClipTransition FireAnimation;

        [Header("Animation - Charge")]
        [Tooltip("蓄力開始動畫")]
        public ClipTransition ChargeStartAnimation;

        [Tooltip("蓄力循環動畫")]
        public ClipTransition ChargeLoopAnimation;

        [Tooltip("蓄力完成發射動畫")]
        public ClipTransition ChargeFireAnimation;

        [Header("Fire Timing")]
        [Tooltip("動畫中實際發射的時間點（秒）")]
        public float FireTime = 0.3f;

        [Tooltip("最短蓄力時間")]
        public float MinChargeTime = 0.3f;

        [Tooltip("最長蓄力時間（超過自動發射）")]
        public float MaxChargeTime = 2.0f;

        [Header("Projectile")]
        [Tooltip("投射物配置（AttackType 為 Projectile 時使用）")]
        public ProjectileData ProjectileConfig;

        [Header("AoE")]
        [Tooltip("AoE 特效 Prefab（必須帶 AoEBehaviour 組件）— 直接掛在特效上的設計,Inspector 設範圍/Tick,Scene 中可視化")]
        public GameObject AoEPrefab;

        [Tooltip("AoE 中心位置的解析來源(Prefab 會被生在解析出來的位置)")]
        public AoEOriginMode AoEOriginMode = AoEOriginMode.PlayerForward;

        [Tooltip("PlayerForward 模式專用 — 玩家前方偏移距離(公尺)\n注意:有鎖定目標或 AutoFaceTarget 搜得到目標時會改落在目標位置,此距離只在「沒有任何目標」時生效")]
        public float AoEForwardDistance = 3f;

        [Header("AoE Cursor (PlayerCursor 模式專用)")]
        [Tooltip("Cursor 起始距離(公尺) — 未鎖定時 cursor 起點 = 玩家位置 + 相機水平 forward × 此距離;有鎖定目標時優先用鎖定點當起點")]
        public float AoECursorInitialDistance = 5f;

        [Tooltip("Cursor 移動速度(公尺/秒) — Camera-relative WASD 輸入 × 此速度")]
        public float AoECursorMoveSpeed = 8f;

        [Tooltip("Cursor 距離玩家的最大半徑(公尺) — 超過時 clamp 到邊界,玩家仍能繼續推但不會飛太遠")]
        public float AoECursorMaxRange = 12f;

        [Tooltip("每幀對 cursor 位置打 raycast 貼地(地形高低差時 AoE 不會懸空) — 平坦場景關閉可省 raycast")]
        public bool AoECursorClampToGround = true;

        [Tooltip("Cursor 貼地用的 Ground LayerMask(通常設地面/地形 layer,排除敵人與小物件)")]
        public LayerMask AoECursorGroundMask = ~0;

        [Header("Damage")]
        [Tooltip("基礎傷害")]
        public float BaseDamage = 15f;

        [Tooltip("滿蓄力傷害倍率")]
        public float ChargeMultiplier = 2.0f;

        [Tooltip("命中時應用的效果")]
        public GameplayEffect HitEffect;

        [Header("VFX/SFX")]
        [Tooltip("發射時觸發的 Cue 標籤")]
        public GameplayTag FireCueTag;

        [Tooltip("蓄力起手時觸發的 Cue 標籤（HoldToCharge/HoldToAim 進入瞄準時播放一次）")]
        public GameplayTag ChargeCueTag;

        [Tooltip("達到最低蓄力門檻時觸發的 Cue 標籤（一次性，給玩家「可發射」的回饋）")]
        public GameplayTag ChargeReadyCueTag;

        [Tooltip("命中時觸發的 Cue 標籤")]
        public GameplayTag HitCueTag;

        [Tooltip("命中特效預製體 (直接設置,不需要 Cue 系統) — 與 HitCueTag 同時設定時兩者都會生效")]
        public GameObject HitVFXPrefab;

        [Tooltip("命中音效 (直接設置,不需要 Cue 系統)")]
        public AudioClip HitSFX;

        [Tooltip("命中特效自動銷毀時間(秒)")]
        public float HitVFXLifetime = 2f;

        [Tooltip("命中特效是否附著到被命中物體表面(例如插箭)")]
        public bool AttachHitVFXToSurface;

        [Tooltip("命中特效縮放倍率(乘在 Prefab 原始 scale 上)。\n(1,1,1) = 維持原大小;(2,2,2) = 雙倍;(0.5,0.5,0.5) = 半倍。\n最終會再乘上角色當下的整體縮放(巨大化/縮小狀態自動跟著放大)")]
        public Vector3 HitVFXScale = Vector3.one;

        [Tooltip("勾選 = 縮放套用到所有子物件的 ParticleSystem(粒子/發射形狀都跟著放大)\n取消 = 只縮 GameObject Transform,粒子維持原始尺寸(複雜特效易視覺斷層)\n建議:保持勾選")]
        public bool HitVFXScaleAllChildren = true;

        [Header("Aiming")]
        [Tooltip("是否啟用肩射瞄準相機")]
        public bool EnableAimCamera;

        [Tooltip("瞄準相機偏移")]
        public Vector3 AimCameraOffset = new(0.5f, 0.3f, -2f);

        [Tooltip("瞄準 IK 角度偏移（單位: 度,角色 body local Euler）— 補償視覺視差。\n" +
                 "與距離無關,所有距離下視覺效果一致。\n" +
                 "X: 俯仰 +向下 / -向上\n" +
                 "Y: 偏航 +向右 / -向左\n" +
                 "Z: 翻滾(通常不用)\n" +
                 "例:左手持弓導致上半身視覺偏左 → 設 Y 為正值(例 3~10 度)往右補")]
        public Vector3 AimIKAngularOffset;

        [Tooltip("發射後保持瞄準鏡頭(BOTW 式連續射擊)。\n" +
                 "✓ 啟用: 射出後鏡頭仍在瞄準狀態,玩家可立即再次蓄力射擊;由 AimCameraController 監測移動/受擊/死亡才退出\n" +
                 "✗ 停用: 射出後立刻退出瞄準鏡頭(舊行為)")]
        public bool KeepAimAfterFire = true;

        [Header("Direction Solver")]
        [Tooltip("是否套用俯仰夾角(防止過度下射穿透地面)。鎖定/瞄準/標記/前方四種來源都會被夾。")]
        public bool ApplyPitchClamp = true;

        [Tooltip("俯仰下限,direction.y 不會低於 -MaxPitchDown(例 0.8 對應約 53° 下射上限)")]
        [Range(0f, 1f)]
        public float MaxPitchDown = 0.8f;

        [Header("Movement")]
        [Tooltip("發射時鎖定移動")]
        public bool LockMovementDuringFire;

        [Tooltip("自動面向目標")]
        public bool AutoFaceTarget = true;

        [Tooltip("自動面向的搜索範圍")]
        public float AutoFaceRange = 15f;

        [Tooltip("自動面向的搜索扇形角度（無鎖定/標記目標時使用）")]
        [Range(10f, 360f)]
        public float AutoFaceAngle = 120f;

        [Tooltip("近距離 360° 全方位搜尋範圍 (公尺) — 沒有鎖定也沒有上次命中目標時,在這個半徑內(含背後/側邊)找最近的敵人優先轉向,提供近身自衛手感。比前方扇形優先。建議 3~6 公尺,設 0 = 停用(只用前方扇形)")]
        public float AutoFaceProximityRange = 5f;

        [Tooltip("自動面向的持續時間")]
        public float AutoFaceDuration = 0.05f;

        [Tooltip("攻擊執行中是否持續面對目標(預設關閉)。\n" +
                 "✗ 關閉(建議揮砍/快射): 攻擊開始時對齊一次,動畫期間保持原方向 — 敵人繞背不會亂轉。\n" +
                 "✓ 開啟(建議空中蓄力 / 鎖定射擊): 蓄力與發射動畫期間每幀以 ContinuousFaceTurnSpeed 平滑追蹤初始鎖定目標。\n" +
                 "目標死亡或失效時不會切換到新敵人,避免突然轉身。")]
        public bool ContinuousFaceTarget;

        [Tooltip("ContinuousFaceTarget 開啟時的旋轉速度(度/秒) — 數值越大反應越靈敏。180 約等於 0.5 秒轉半圈,適合一般空中蓄力。")]
        public float ContinuousFaceTurnSpeed = 180f;

        [Header("Attack Movement")]
        [Tooltip("攻擊時位移設定列表（可設定多段位移，如後跳射擊）")]
        public List<RangedMovementConfig> AttackMovements = new();

        [Header("Spawn Point (Default)")]
        [Tooltip("投射物生成的骨骼名稱（留空使用根節點前方）")]
        public string SpawnSocketName;

        [Tooltip("生成位置偏移")]
        public Vector3 SpawnOffset = new(0f, 1.2f, 0.8f);

        [Header("Multi-Shot")]
        [Tooltip("多發射擊設定（留空則使用上方的單發設定）")]
        public List<RangedFireEvent> FireEvents = new();

        /// <summary>
        /// 快取的預設發射事件（避免每幀建立新物件導致 HashSet 比對失敗）
        /// </summary>
        [NonSerialized]
        private List<RangedFireEvent> _resolvedFireEventsCache;

        /// <summary>
        /// 取得所有發射事件（若 FireEvents 為空，自動從 FireTime/SpawnOffset 建立單發）
        /// 使用快取確保回傳相同引用，防止重複發射
        /// </summary>
        public List<RangedFireEvent> GetResolvedFireEvents()
        {
            if (FireEvents != null && FireEvents.Count > 0) return FireEvents;
            if (_resolvedFireEventsCache == null)
            {
                _resolvedFireEventsCache = new List<RangedFireEvent>
                {
                    new()
                    {
                        FireTime = FireTime,
                        SpawnOffset = SpawnOffset,
                        DirectionOffset = Vector3.zero
                    }
                };
            }
            else
            {
                // 同步 Inspector 中可能修改的數值
                _resolvedFireEventsCache[0].FireTime = FireTime;
                _resolvedFireEventsCache[0].SpawnOffset = SpawnOffset;
                _resolvedFireEventsCache[0].DirectionOffset = Vector3.zero;
            }
            return _resolvedFireEventsCache;
        }

        public override AnimationClip GetPrimaryAnimationClip()
        {
            return FireAnimation?.Clip;
        }

        /// <summary>
        /// 計算視覺/範圍蓄力 ratio(分段曲線):
        /// • chargeTime ∈ [0, MinChargeTime] → ratio ∈ [-1, 0],對應 AoEBehaviour 的 _minScaleMultiplier → 1.0 區段
        /// • chargeTime ∈ [MinChargeTime, MaxChargeTime] → ratio ∈ [0, 1],對應 1.0 → _radiusChargeMultiplier 區段
        /// 接點 chargeTime = MinChargeTime 時 ratio = 0,AoE 範圍剛好為 base 半徑(100%)
        /// MinChargeTime = 0 時退化為單純的 chargeTime / MaxChargeTime
        /// </summary>
        public float ComputeVisualChargeRatio(float chargeTime)
        {
            if (MinChargeTime <= 0f)
            {
                return Mathf.Clamp01(chargeTime / Mathf.Max(MaxChargeTime, 0.001f));
            }
            if (chargeTime < MinChargeTime)
            {
                return (chargeTime / MinChargeTime) - 1f;
            }
            return Mathf.Clamp01((chargeTime - MinChargeTime) / (MaxChargeTime - MinChargeTime));
        }
    }

    /// <summary>
    /// 單發射擊事件 - 定義一發投射物的發射時間、位置、傷害與效果
    /// 覆寫欄位為空/預設時會回退到 RangedAttackData 的共用設定
    /// </summary>
    [Serializable]
    public class RangedFireEvent
    {
        [Tooltip("發射時間點（秒）")]
        public float FireTime = 0.3f;

        [Tooltip("生成位置偏移（相對於 Socket）")]
        public Vector3 SpawnOffset = new(0f, 1.2f, 0.8f);

        [Tooltip("發射方向偏移（角度，相對於角色正面）")]
        public Vector3 DirectionOffset;

        [Header("Spawn Override")]
        [Tooltip("此發的生成骨骼名稱覆寫（留空時使用 RangedAttackData.SpawnSocketName）")]
        public string SpawnSocketNameOverride;

        [Header("Damage Override")]
        [Tooltip("此發的傷害覆寫（<= 0 時使用 RangedAttackData 的 BaseDamage）")]
        public float BaseDamageOverride = -1f;

        [Tooltip("此發的傷害倍率")]
        public float DamageMultiplier = 1f;

        [Header("Effect Override")]
        [Tooltip("此發的命中效果覆寫（null 時使用 RangedAttackData 的 HitEffect）")]
        public GameplayEffect HitEffectOverride;

        [Tooltip("此發的命中 Cue 標籤覆寫（無效時使用 RangedAttackData 的 HitCueTag）")]
        public GameplayTag HitCueTagOverride;

        [Tooltip("此發的命中特效 Prefab 覆寫(null 時使用 RangedAttackData 的 HitVFXPrefab) — 直接設置,不需要 Cue 系統")]
        public GameObject HitVFXPrefabOverride;

        [Tooltip("此發的命中音效覆寫(null 時使用 RangedAttackData 的 HitSFX) — 直接設置,不需要 Cue 系統")]
        public AudioClip HitSFXOverride;

        /// <summary>取得此發的有效基礎傷害</summary>
        public float GetEffectiveBaseDamage(RangedAttackData parent)
        {
            return BaseDamageOverride > 0f ? BaseDamageOverride : parent.BaseDamage;
        }

        /// <summary>取得此發的有效命中效果</summary>
        public GameplayEffect GetEffectiveHitEffect(RangedAttackData parent)
        {
            return HitEffectOverride != null ? HitEffectOverride : parent.HitEffect;
        }

        /// <summary>取得此發的有效命中 Cue 標籤</summary>
        public GameplayTag GetEffectiveHitCueTag(RangedAttackData parent)
        {
            return HitCueTagOverride.IsValid ? HitCueTagOverride : parent.HitCueTag;
        }

        /// <summary>取得此發的有效命中特效 Prefab</summary>
        public GameObject GetEffectiveHitVFXPrefab(RangedAttackData parent)
        {
            return HitVFXPrefabOverride != null ? HitVFXPrefabOverride : parent.HitVFXPrefab;
        }

        /// <summary>取得此發的有效命中音效</summary>
        public AudioClip GetEffectiveHitSFX(RangedAttackData parent)
        {
            return HitSFXOverride != null ? HitSFXOverride : parent.HitSFX;
        }

        /// <summary>取得此發的有效生成骨骼名稱</summary>
        public string GetEffectiveSpawnSocketName(RangedAttackData parent)
        {
            return string.IsNullOrEmpty(SpawnSocketNameOverride) ? parent.SpawnSocketName : SpawnSocketNameOverride;
        }
    }

    /// <summary>
    /// 遠程攻擊位移設定
    /// </summary>
    [Serializable]
    public class RangedMovementConfig
    {
        [Tooltip("啟用攻擊位移")]
        public bool Enabled;

        [Tooltip("位移開始時間（秒）")]
        public float StartTime;

        [Tooltip("位移持續時間")]
        public float Duration = 0.2f;

        [Tooltip("位移距離（正值=前進，負值=後退）")]
        public float Distance = -2f;

        [Tooltip("位移曲線")]
        public AnimationCurve Curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }
}
