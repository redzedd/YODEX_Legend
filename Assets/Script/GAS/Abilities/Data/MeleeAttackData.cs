using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 近戰攻擊數據 - 定義一次攻擊的所有參數
    /// 繼承 AttackDataBase 共用 Timing、Combo、TimelineEvents
    /// </summary>
    [CreateAssetMenu(fileName = "New Melee Attack", menuName = "GAS/Abilities/Melee Attack Data")]
    public class MeleeAttackData : AttackDataBase
    {
        [Header("Animation")]
        [Tooltip("攻擊動畫")]
        public ClipTransition Clip;

        [Header("Hit Windows")]
        [Tooltip("命中視窗定義")]
        public List<MeleeHitWindow> HitWindows = new();

        [Header("Movement")]
        [Tooltip("攻擊時的移動設定")]
        public MeleeMovementConfig MovementConfig = new();

        public override AnimationClip GetPrimaryAnimationClip()
        {
            return Clip?.Clip;
        }
    }

    /// <summary>
    /// 近戰輸入類型
    /// </summary>
    public enum MeleeInputType
    {
        None = 0,       // 重要：None 必須是 0，作為默認值
        LightAttack,
        HeavyAttack,
        Special,
        RangedAttack    // 用於近戰→遠程跨類型連招
    }

    /// <summary>
    /// 命中視窗定義
    /// </summary>
    [Serializable]
    public class MeleeHitWindow
    {
        [Header("Timing")]
        [Tooltip("開始時間 (秒)")]
        public float StartTime = 0.1f;

        [Tooltip("結束時間 (秒)")]
        public float EndTime = 0.3f;

        [Header("Hitbox")]
        [Tooltip("判定形狀")]
        public HitboxShape Shape = HitboxShape.Box;

        [Tooltip("位置偏移")]
        public Vector3 Offset = new(0, 1, 1);

        [Tooltip("尺寸")]
        public Vector3 Size = Vector3.one;

        [Header("Binding")]
        [Tooltip("綁定的骨骼名稱 (留空使用根節點)")]
        public string SocketName;

        [Tooltip("是否跟隨骨骼移動")]
        public bool AttachToBody = true;

        [Tooltip("被中斷時是否關閉判定")]
        public bool StopOnInterrupt = true;

        [Header("Raycast Trail（射線插值法）")]
        [Tooltip("啟用射線軌跡檢測（適合快速揮擊，沿武器佈置多個射線點）")]
        public bool UseRaycastTrail;

        [Tooltip("武器根部偏移（相對於 Socket，定義軌跡起點）")]
        public Vector3 TrailStartOffset = Vector3.zero;

        [Tooltip("武器末端偏移（相對於 Socket，定義軌跡終點）")]
        public Vector3 TrailEndOffset = new(0, 0, 1f);

        [Tooltip("武器軌跡上的射線段數（越多越精確，2 = 只有起點和終點）")]
        [Range(2, 8)]
        public int TrailSegments = 3;

        [Tooltip("射線半徑（SphereCast 的粗細，0 = 精確線段 Raycast）")]
        public float TrailRayRadius = 0.05f;

        [Header("Damage")]
        [Tooltip("基礎傷害")]
        public float BaseDamage = 10f;

        [Tooltip("傷害倍率")]
        public float DamageMultiplier = 1f;

        [Tooltip("硬直傷害")]
        public float PoiseDamage = 25f;

        [Tooltip("擊退漸近距離(公尺)— Poise 擊破後的水平位移;衰減採 HitReactionData.ExternalVelocityDecayTau")]
        public float KnockbackForce = 1f;

        [Tooltip("攻擊類型:\nNormal — 一般攻擊（打斷 Idle/Walk，被攻擊霸體擋）\nLight — 輕攻擊（只抖動 VFX，不打斷任何狀態）\nHeavy — 重攻擊（打斷攻擊霸體；Poise 擊破走 Knockback 倒地）")]
        public AttackTier AttackTier = AttackTier.Normal;

        [Tooltip("【已棄用，但保留兼容】— 寫入端會自動依 AttackTier == Heavy 同步此欄位。新攻擊請改設 AttackTier。")]
        public bool IsHeavyAttack = false;

        [Header("Effects")]
        [Tooltip("命中時應用的效果")]
        public GameplayEffect HitEffect;

        [Tooltip("命中時觸發的 Cue 標籤 (如果配置了 Cue 系統) — 與下方 HitVFXPrefab/HitSFX 兩者皆設定時都會生效")]
        public GameplayTag HitCueTag;

        [Tooltip("命中特效預製體 (直接設置，不需要 Cue 系統)")]
        public GameObject HitVFXPrefab;

        [Tooltip("命中音效 (直接設置，不需要 Cue 系統)")]
        public AudioClip HitSFX;

        [Tooltip("命中特效自動銷毀時間(秒)")]
        public float HitVFXLifetime = 2f;

        [Tooltip("命中特效是否附著到被命中物體表面(例如插箭/血濺跟著敵人移動)")]
        public bool AttachHitVFXToSurface;

        [Tooltip("命中特效縮放倍率(乘在 Prefab 原始 scale 上)。\n(1,1,1) = 維持原大小;(2,2,2) = 雙倍;(0.5,0.5,0.5) = 半倍。\n最終會再乘上角色當下的整體縮放(巨大化/縮小狀態自動跟著放大)")]
        public Vector3 HitVFXScale = Vector3.one;

        [Tooltip("勾選 = 縮放套用到所有子物件的 ParticleSystem(粒子/發射形狀都跟著放大)\n取消 = 只縮 GameObject Transform,粒子維持原始尺寸(複雜特效易視覺斷層)\n建議:保持勾選")]
        public bool HitVFXScaleAllChildren = true;

        [Header("Feedback")]
        [Tooltip("頓幀持續時間")]
        public float HitStopDuration = 0.1f;

        [Range(0f, 1f)]
        [Tooltip("頓幀時間縮放")]
        public float HitStopSpeed = 0f;

        [Tooltip("屏幕震動強度")]
        public float ScreenShakeForce = 1f;

        [Header("Target Tracking")]
        [Tooltip("命中後是否標記該敵人（用於下次攻擊自動面向）")]
        public bool MarkTargetOnHit = true;

        [Tooltip("若敵人身上有標記，此段攻擊是否要面向該敵人")]
        public bool AutoFaceMarkedTarget = false;

        [Header("Hit Movement")]
        [Tooltip("命中時觸發移動")]
        public bool TriggerMovement = false;

        [Tooltip("移動類型")]
        public MeleeMovementType MovementType = MeleeMovementType.StandardSnap;

        [Tooltip("吸附範圍")]
        public float SnapRange = 5f;

        [Tooltip("停止距離")]
        public float SnapStopDistance = 1f;

        [Tooltip("移動距離 (無目標時)")]
        public float MoveDistance = 0.5f;

        [Tooltip("移動持續時間")]
        public float MoveDuration = 0.1f;

        [Tooltip("移動曲線")]
        public AnimationCurve MoveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    /// <summary>
    /// 判定形狀
    /// </summary>
    public enum HitboxShape
    {
        Box,
        Sphere
    }

    /// <summary>
    /// 移動類型
    /// </summary>
    public enum MeleeMovementType
    {
        StandardSnap,   // 標準吸附
        PierceThrough,  // 穿刺
    }

    /// <summary>
    /// 時間軸事件所屬階段（用於遠程攻擊 HoldToCharge / HoldToAim 區分蓄力起手 / 蓄力循環 / 發射三個階段;
    /// 近戰與 QuickFire 永遠用 Fire 階段即可,沿用舊行為）
    /// </summary>
    public enum TimelineEventPhase
    {
        /// <summary>發射動畫期間觸發（近戰預設,遠程 QuickFire/Charge/Aim 的 fire animation）</summary>
        Fire,
        /// <summary>蓄力起手動畫期間觸發（HoldToCharge/HoldToAim 的 ChargeStartAnimation）</summary>
        ChargeStart,
        /// <summary>蓄力循環動畫期間觸發（HoldToCharge/HoldToAim 的 ChargeLoopAnimation）</summary>
        ChargeLoop
    }

    /// <summary>
    /// 時間軸事件
    /// </summary>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceClassName: "MeleeTimelineEvent")]
    public class TimelineEvent : ISerializationCallbackReceiver
    {
        [Tooltip("事件名稱")]
        public string Name;

        [Header("Phase")]
        [Tooltip("事件所屬階段（遠程攻擊用於區分蓄力起手 / 蓄力循環 / 發射三個 timeline;近戰恆為 Fire）")]
        public TimelineEventPhase Phase = TimelineEventPhase.Fire;

        [Header("Timing")]
        [Tooltip("觸發時間 (秒,從該階段動畫起始算起)")]
        public float TriggerTime;

        [Header("Effect")]
        [Tooltip("特效預製體 — 直接拉 Prefab 上去就好,觸發時自動生成。\n設定此欄位 (或 SFX) 後,下方 Cue Tag 會被忽略。")]
        public GameObject VFXPrefab;

        [Tooltip("音效 — 觸發時直接播放。\n設定此欄位 (或 VFXPrefab) 後,下方 Cue Tag 會被忽略。")]
        public AudioClip SFX;

        [Header("Cue (進階 — VFX/SFX 未設定時的 fallback)")]
        [Tooltip("Cue 標籤 — 僅當 VFXPrefab 與 SFX 兩欄都空白時才會走這條。\n用於套用預先註冊在 GameplayCueManager 上的複合 Cue (共用音效 / 頓幀 / 鏡頭抖動組合)。")]
        public GameplayTag CueTag;

        [Header("Transform")]
        [Tooltip("綁定的骨骼名稱")]
        public string SocketName;

        [Tooltip("VFX 跟隨骨骼的軸 — 每個軸獨立勾選,讓你做「位置跟人但旋轉不跟」或「只跟 Y 軸高度」等效果。\nAll = 完整黏在骨骼上(等同舊『跟隨骨骼』勾選);\nNone = spawn 後固定世界座標,完全不跟。\n縮放永遠等比跟隨 socket — 角色放大 VFX 也跟著放大。")]
        public AttachAxes Axes = AttachAxes.All;

        // 舊資料的 AttachToBody bool — 透過 FormerlySerializedAs 撿回 .asset 既有值,
        // OnAfterDeserialize 自動 migrate 到新的 Axes(true→All / false→None)。
        [SerializeField, HideInInspector, FormerlySerializedAs("AttachToBody")]
        private bool _legacyAttachToBody = true;

        [SerializeField, HideInInspector]
        private bool _axesMigrated;

        [Tooltip("位置偏移")]
        public Vector3 PositionOffset;

        [Tooltip("旋轉偏移")]
        public Vector3 RotationOffset;

        [Tooltip("縮放")]
        public Vector3 Scale = Vector3.one;

        [Header("Behavior")]
        [Tooltip("被中斷時是否停止")]
        public bool StopOnInterrupt = true;

        [Tooltip("被中斷時的行為（當 StopOnInterrupt = true 時）")]
        public VFXInterruptBehavior InterruptBehavior = VFXInterruptBehavior.StopAndDestroy;

        /// <summary>任一軸有勾即視為「正在跟隨 socket」— 給舊讀取點(走 GameplayCueParameters.TargetObject 等)的 helper。</summary>
        public bool IsAttached => Axes != AttachAxes.None;

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (!_axesMigrated)
            {
                Axes = _legacyAttachToBody ? AttachAxes.All : AttachAxes.None;
                _axesMigrated = true;
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
    }

    /// <summary>
    /// VFX 跟隨骨骼的軸選擇 — 每個軸獨立勾選,讓設計師能做「位置跟人但旋轉不跟」或「只跟 Y 軸位置」等效果。
    /// All = 整個 VFX 完全綁在骨骼上(等同舊 AttachToBody=true);
    /// None = VFX spawn 後固定世界座標,完全不跟隨(等同舊 AttachToBody=false)。
    /// 不論勾選何種組合,VFX 的縮放永遠跟著 socket 的 lossyScale 等比放大。
    /// </summary>
    [System.Flags]
    public enum AttachAxes
    {
        None = 0,
        PositionX = 1 << 0,
        PositionY = 1 << 1,
        PositionZ = 1 << 2,
        RotationX = 1 << 3,
        RotationY = 1 << 4,
        RotationZ = 1 << 5,
        All = PositionX | PositionY | PositionZ | RotationX | RotationY | RotationZ,
    }

    /// <summary>
    /// VFX 被中斷時的行為
    /// </summary>
    public enum VFXInterruptBehavior
    {
        [Tooltip("停止並立即銷毀特效")]
        StopAndDestroy,

        [Tooltip("分離特效但讓它繼續播放（不再跟隨骨骼）")]
        DetachAndContinue
    }

    /// <summary>
    /// 移動配置
    /// </summary>
    [Serializable]
    public class MeleeMovementConfig
    {
        [Tooltip("啟用自動轉向到目標")]
        public bool AutoFaceTarget = true;

        [Tooltip("自動轉向的範圍 — 鎖定目標 / 上次命中目標的有效距離。建議 8~15 公尺")]
        public float AutoFaceRange = 15f;

        [Tooltip("自動轉向的持續時間 — 從當前朝向轉到目標的 DOLookAt 動畫時長 (秒)。建議 0.05~0.15")]
        public float AutoFaceDuration = 0.05f;

        [Tooltip("近距離 360° 全方位搜尋範圍 (公尺) — 沒有鎖定也沒有上次命中目標時,在這個半徑內找最近的敵人轉向。建議 3~6 公尺,設 0 = 停用。注意:此值若大於 AutoFaceRange,搜到的目標可能在下一幀失效")]
        public float AutoFaceProximityRange = 5f;

        [Tooltip("前方扇形搜尋角度 (度) — 近距離 360° 沒搜到時,改在前方此扇形(以此值的一半為半開角)、AutoFaceRange 範圍內找最近的敵人轉向。建議 90~150 度,設 0 = 停用前方扇形搜尋")]
        public float AutoFaceAngle = 120f;
    }
}
