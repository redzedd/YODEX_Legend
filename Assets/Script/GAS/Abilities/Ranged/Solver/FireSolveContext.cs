using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 發射方向的解析來源(用於除錯與測試斷言)
    /// </summary>
    public enum FireDirectionSource
    {
        /// <summary>角色面朝方向(無任何有效目標)</summary>
        Forward,
        /// <summary>鎖定中的目標</summary>
        LockedTarget,
        /// <summary>瞄準相機螢幕中心射線命中點</summary>
        AimCamera,
        /// <summary>HitTargetMemory 標記的最近一次命中目標(限制在 MarkedTargetMaxRange 內)</summary>
        MarkedTarget,
        /// <summary>CombatTargetFinder 在前方扇形範圍搜尋到的最佳敵人</summary>
        AutoFaceTarget
    }

    /// <summary>
    /// FireDirectionSolver 的輸入上下文
    /// 由呼叫方一次性快照所有狀態,純資料,不持有 Unity Component 引用,可在 EditMode 測試中直接建構
    /// </summary>
    public struct FireSolveContext
    {
        // === Owner 與 Socket 世界變換 ===
        /// <summary>角色根節點位置</summary>
        public Vector3 OwnerPosition;

        /// <summary>角色根節點旋轉(用於 forward 預設方向)</summary>
        public Quaternion OwnerRotation;

        /// <summary>投射物生成骨骼的世界位置</summary>
        public Vector3 SocketPosition;

        /// <summary>投射物生成骨骼的世界旋轉(用於 SpawnOffset 的 local→world 變換)</summary>
        public Quaternion SocketRotation;

        // === 目標來源(優先順序: Locked > AimCamera > Marked > Forward) ===
        /// <summary>是否有鎖定目標</summary>
        public bool HasLockedTarget;

        /// <summary>鎖定目標的世界位置(僅 HasLockedTarget=true 時有效)</summary>
        public Vector3 LockedTargetPosition;

        /// <summary>是否啟用瞄準相機(肩射模式)</summary>
        public bool HasAimCamera;

        /// <summary>瞄準相機螢幕中心射線的世界命中點(僅 HasAimCamera=true 時有效)</summary>
        public Vector3 AimHitPoint;

        /// <summary>是否有標記目標</summary>
        public bool HasMarkedTarget;

        /// <summary>標記目標的世界位置(僅 HasMarkedTarget=true 且距離 ≤ MarkedTargetMaxRange 時採用)</summary>
        public Vector3 MarkedTargetPosition;

        /// <summary>是否有 AutoFace 搜尋到的扇形範圍內目標</summary>
        public bool HasAutoFaceTarget;

        /// <summary>AutoFace 搜尋到的目標世界位置(僅 HasAutoFaceTarget=true 時採用,優先順序低於 Marked,高於 Forward)</summary>
        public Vector3 AutoFaceTargetPosition;

        // === 解算設定(來自 RangedAttackData) ===
        /// <summary>標記目標的有效採用距離(超過此距離 Marked 退化為 Forward)</summary>
        public float MarkedTargetMaxRange;

        /// <summary>是否啟用俯仰夾角(防止過度下射)</summary>
        public bool ApplyPitchClamp;

        /// <summary>俯仰下限(以 direction.y 為基準,例 0.8 代表 direction.y >= -0.8)</summary>
        public float MaxPitchDown;
    }

    /// <summary>
    /// 單發射擊事件的解算輸入(從 RangedFireEvent 萃取出 solver 真正需要的兩個欄位)
    /// </summary>
    public struct FireEventInput
    {
        /// <summary>生成位置偏移(Socket local 座標)</summary>
        public Vector3 SpawnOffset;

        /// <summary>方向偏移(度數,以 baseDir 為 +Z 的 local Euler,套用順序: LookRotation(baseDir) * Euler(offset) * forward)</summary>
        public Vector3 DirectionOffsetEuler;
    }

    /// <summary>
    /// FireDirectionSolver 的解算結果
    /// </summary>
    public struct FireSolveResult
    {
        /// <summary>世界座標的投射物生成位置</summary>
        public Vector3 SpawnPosition;

        /// <summary>世界座標的發射方向(已正規化)</summary>
        public Vector3 FireDirection;

        /// <summary>解析後的目標世界座標位置(供 IK LookAt 使用)。Forward 來源時為 spawn 前方虛擬點。</summary>
        public Vector3 ResolvedTargetPosition;

        /// <summary>方向是從哪一級優先順序解出來的(供測試斷言/除錯)</summary>
        public FireDirectionSource Source;
    }
}
