using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 遠程攻擊發射方向/位置的純 C# 解算器
    /// 不持有任何 Unity Component 引用,可在 EditMode 測試中直接呼叫
    /// 優先順序: LockedTarget > AimCamera > MarkedTarget(in range) > AutoFaceTarget > Forward
    /// </summary>
    public static class FireDirectionSolver
    {
        /// <summary>方向向量視為「零」的平方長度閾值(對應 0.01 公尺,防止 spawn 與 target 重疊時的退化方向)</summary>
        private const float MIN_SQR_MAGNITUDE = 0.0001f;

        /// <summary>Forward 來源時 ResolvedTargetPosition 的虛擬距離(供 IK LookAt 預設指向前方)</summary>
        private const float FORWARD_VIRTUAL_DISTANCE = 10f;

        /// <summary>
        /// 解算單發射擊事件,回傳結果(便於測試與直接讀寫變數)
        /// </summary>
        public static FireSolveResult Solve(in FireSolveContext ctx, in FireEventInput evt)
        {
            Solve(in ctx, in evt, out FireSolveResult result);
            return result;
        }

        /// <summary>
        /// 解算單發射擊事件,結果寫入 out 參數(熱路徑用,避免結構複製)
        /// </summary>
        public static void Solve(in FireSolveContext ctx, in FireEventInput evt, out FireSolveResult result)
        {
            result.SpawnPosition = ResolveSpawnPosition(in ctx, evt.SpawnOffset);
            Vector3 baseDir = ResolveBaseDirection(in ctx, result.SpawnPosition, out result.Source);
            if (ctx.ApplyPitchClamp)
            {
                baseDir = ApplyPitchClampInternal(baseDir, ctx.MaxPitchDown);
            }
            result.FireDirection = ApplyDirectionOffset(baseDir, evt.DirectionOffsetEuler);
            result.ResolvedTargetPosition = ResolveTargetPosition(in ctx, result.Source, result.SpawnPosition, baseDir);
        }

        private static Vector3 ResolveSpawnPosition(in FireSolveContext ctx, Vector3 spawnOffset)
        {
            return ctx.SocketPosition + ctx.SocketRotation * spawnOffset;
        }

        private static Vector3 ResolveBaseDirection(in FireSolveContext ctx, Vector3 spawnPos, out FireDirectionSource source)
        {
            if (ctx.HasLockedTarget && TryGetDirectionTo(ctx.LockedTargetPosition, spawnPos, out Vector3 lockDir))
            {
                source = FireDirectionSource.LockedTarget;
                return lockDir;
            }
            if (ctx.HasAimCamera && TryGetDirectionTo(ctx.AimHitPoint, spawnPos, out Vector3 aimDir))
            {
                source = FireDirectionSource.AimCamera;
                return aimDir;
            }
            if (ctx.HasMarkedTarget && IsMarkedTargetInRange(in ctx)
                && TryGetDirectionTo(ctx.MarkedTargetPosition, spawnPos, out Vector3 markDir))
            {
                source = FireDirectionSource.MarkedTarget;
                return markDir;
            }
            if (ctx.HasAutoFaceTarget && TryGetDirectionTo(ctx.AutoFaceTargetPosition, spawnPos, out Vector3 autoFaceDir))
            {
                source = FireDirectionSource.AutoFaceTarget;
                return autoFaceDir;
            }
            source = FireDirectionSource.Forward;
            return ctx.OwnerRotation * Vector3.forward;
        }

        /// <summary>
        /// 計算解析後的目標位置(供 IK LookAt 使用)。
        /// Forward 來源時無實體目標,投影虛擬點到前方 FORWARD_VIRTUAL_DISTANCE。
        /// </summary>
        private static Vector3 ResolveTargetPosition(in FireSolveContext ctx, FireDirectionSource source, Vector3 spawnPos, Vector3 baseDir)
        {
            return source switch
            {
                FireDirectionSource.LockedTarget => ctx.LockedTargetPosition,
                FireDirectionSource.AimCamera => ctx.AimHitPoint,
                FireDirectionSource.MarkedTarget => ctx.MarkedTargetPosition,
                FireDirectionSource.AutoFaceTarget => ctx.AutoFaceTargetPosition,
                _ => spawnPos + baseDir * FORWARD_VIRTUAL_DISTANCE
            };
        }

        private static Vector3 ApplyPitchClampInternal(Vector3 direction, float maxPitchDown)
        {
            if (direction.y >= -maxPitchDown) return direction;
            direction.y = -maxPitchDown;
            return direction.normalized;
        }

        private static Vector3 ApplyDirectionOffset(Vector3 baseDir, Vector3 offsetEuler)
        {
            if (offsetEuler == Vector3.zero) return baseDir;
            Quaternion baseRot = Quaternion.LookRotation(baseDir);
            return baseRot * Quaternion.Euler(offsetEuler) * Vector3.forward;
        }

        private static bool TryGetDirectionTo(Vector3 target, Vector3 origin, out Vector3 direction)
        {
            Vector3 delta = target - origin;
            float sqrMag = delta.sqrMagnitude;
            if (sqrMag < MIN_SQR_MAGNITUDE)
            {
                direction = Vector3.zero;
                return false;
            }
            direction = delta / Mathf.Sqrt(sqrMag);
            return true;
        }

        private static bool IsMarkedTargetInRange(in FireSolveContext ctx)
        {
            Vector3 delta = ctx.MarkedTargetPosition - ctx.OwnerPosition;
            float sqrRange = ctx.MarkedTargetMaxRange * ctx.MarkedTargetMaxRange;
            return delta.sqrMagnitude < sqrRange;
        }
    }
}
