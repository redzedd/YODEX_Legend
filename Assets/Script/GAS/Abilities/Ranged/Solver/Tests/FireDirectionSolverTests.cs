using NUnit.Framework;
using UnityEngine;

namespace GAS.Tests
{
    /// <summary>
    /// FireDirectionSolver 純解算器 EditMode 單元測試
    /// 涵蓋: 優先順序、Direction Offset bug 修正驗證、Pitch Clamp、Spawn Position 變換
    /// </summary>
    public class FireDirectionSolverTests
    {
        private const float TOLERANCE = 0.0001f;

        // === 輔助 ===

        private static FireSolveContext MakeContext()
        {
            return new FireSolveContext
            {
                OwnerPosition = Vector3.zero,
                OwnerRotation = Quaternion.identity,
                SocketPosition = Vector3.zero,
                SocketRotation = Quaternion.identity,
                MarkedTargetMaxRange = 15f,
                ApplyPitchClamp = false,
                MaxPitchDown = 0.8f
            };
        }

        private static FireEventInput MakeEvent(Vector3 spawnOffset = default, Vector3 directionOffsetEuler = default)
        {
            return new FireEventInput
            {
                SpawnOffset = spawnOffset,
                DirectionOffsetEuler = directionOffsetEuler
            };
        }

        private static void AssertVectorApprox(Vector3 expected, Vector3 actual, string message = null)
        {
            float dist = Vector3.Distance(expected, actual);
            Assert.That(dist, Is.LessThan(TOLERANCE),
                message ?? "expected " + expected + " but got " + actual + " (dist=" + dist + ")");
        }

        // === 優先順序: Forward(預設) ===

        [Test]
        public void Solve_NoTargets_PointsAlongOwnerForward()
        {
            FireSolveContext ctx = MakeContext();
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.Forward, result.Source);
            AssertVectorApprox(Vector3.forward, result.FireDirection);
        }

        [Test]
        public void Solve_OwnerForward_RespectsOwnerRotation()
        {
            FireSolveContext ctx = MakeContext();
            ctx.OwnerRotation = Quaternion.Euler(0f, 90f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.Forward, result.Source);
            AssertVectorApprox(Vector3.right, result.FireDirection);
        }

        // === 優先順序: LockedTarget ===

        [Test]
        public void Solve_LockedTarget_DirectionPointsAtTarget()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(10f, 0f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.LockedTarget, result.Source);
            AssertVectorApprox(Vector3.right, result.FireDirection);
        }

        [Test]
        public void Solve_LockedOverridesAim()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(0f, 0f, 5f);
            ctx.HasAimCamera = true;
            ctx.AimHitPoint = new Vector3(10f, 0f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.LockedTarget, result.Source);
        }

        [Test]
        public void Solve_LockedOverridesMarked()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(0f, 0f, 5f);
            ctx.HasMarkedTarget = true;
            ctx.MarkedTargetPosition = new Vector3(10f, 0f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.LockedTarget, result.Source);
        }

        // === 優先順序: AimCamera ===

        [Test]
        public void Solve_AimUsedWhenNoLock()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasAimCamera = true;
            ctx.AimHitPoint = new Vector3(0f, 0f, 10f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.AimCamera, result.Source);
            AssertVectorApprox(Vector3.forward, result.FireDirection);
        }

        // === 優先順序: MarkedTarget ===

        [Test]
        public void Solve_MarkedInRange_Used()
        {
            FireSolveContext ctx = MakeContext();
            ctx.MarkedTargetMaxRange = 15f;
            ctx.HasMarkedTarget = true;
            ctx.MarkedTargetPosition = new Vector3(10f, 0f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.MarkedTarget, result.Source);
            AssertVectorApprox(Vector3.right, result.FireDirection);
        }

        [Test]
        public void Solve_MarkedOutOfRange_FallsThroughToForward()
        {
            FireSolveContext ctx = MakeContext();
            ctx.MarkedTargetMaxRange = 5f;
            ctx.HasMarkedTarget = true;
            ctx.MarkedTargetPosition = new Vector3(10f, 0f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.Forward, result.Source);
            AssertVectorApprox(Vector3.forward, result.FireDirection);
        }

        [Test]
        public void Solve_SpawnEqualsTarget_FallsThroughToNextPriority()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = Vector3.zero;
            ctx.HasAimCamera = true;
            ctx.AimHitPoint = new Vector3(0f, 0f, 10f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.AimCamera, result.Source);
        }

        // === 優先順序: AutoFaceTarget(扇形搜尋,新增層) ===

        [Test]
        public void Solve_AutoFaceTarget_UsedWhenNoLockNoAimNoMark()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasAutoFaceTarget = true;
            ctx.AutoFaceTargetPosition = new Vector3(0f, 0f, 8f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.AutoFaceTarget, result.Source);
            AssertVectorApprox(Vector3.forward, result.FireDirection);
        }

        [Test]
        public void Solve_AutoFaceTarget_OverriddenByLocked()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(10f, 0f, 0f);
            ctx.HasAutoFaceTarget = true;
            ctx.AutoFaceTargetPosition = new Vector3(0f, 0f, 8f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.LockedTarget, result.Source);
        }

        [Test]
        public void Solve_AutoFaceTarget_OverriddenByMarkedInRange()
        {
            FireSolveContext ctx = MakeContext();
            ctx.MarkedTargetMaxRange = 15f;
            ctx.HasMarkedTarget = true;
            ctx.MarkedTargetPosition = new Vector3(10f, 0f, 0f);
            ctx.HasAutoFaceTarget = true;
            ctx.AutoFaceTargetPosition = new Vector3(0f, 0f, 8f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.MarkedTarget, result.Source);
        }

        [Test]
        public void Solve_AutoFaceTarget_UsedWhenMarkedOutOfRange()
        {
            FireSolveContext ctx = MakeContext();
            ctx.MarkedTargetMaxRange = 5f;
            ctx.HasMarkedTarget = true;
            ctx.MarkedTargetPosition = new Vector3(10f, 0f, 0f);
            ctx.HasAutoFaceTarget = true;
            ctx.AutoFaceTargetPosition = new Vector3(0f, 0f, 8f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.AreEqual(FireDirectionSource.AutoFaceTarget, result.Source);
        }

        // === ResolvedTargetPosition(供 IK LookAt) ===

        [Test]
        public void Solve_ResolvedTargetPosition_LockedSource_EqualsLockedPosition()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(7f, 3f, 5f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            AssertVectorApprox(new Vector3(7f, 3f, 5f), result.ResolvedTargetPosition);
        }

        [Test]
        public void Solve_ResolvedTargetPosition_ForwardSource_PointsAhead()
        {
            FireSolveContext ctx = MakeContext();
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            // Forward 來源時投影到前方虛擬點(spawn + forward * 10)
            Assert.That(result.ResolvedTargetPosition.z, Is.GreaterThan(0f));
            Assert.That(result.ResolvedTargetPosition.z, Is.LessThanOrEqualTo(10f + TOLERANCE));
        }

        [Test]
        public void Solve_ResolvedTargetPosition_AutoFaceSource_EqualsAutoFacePosition()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasAutoFaceTarget = true;
            ctx.AutoFaceTargetPosition = new Vector3(2f, 1f, 5f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            AssertVectorApprox(new Vector3(2f, 1f, 5f), result.ResolvedTargetPosition);
        }

        // === Direction Offset (本次 bug fix 重點驗證) ===

        [Test]
        public void Solve_DirectionOffsetZero_PreservesBaseDir()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(7f, 3f, 5f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Vector3 expected = new Vector3(7f, 3f, 5f).normalized;
            AssertVectorApprox(expected, result.FireDirection);
        }

        [Test]
        public void Solve_DirectionOffsetYaw30_FromForward_RotatesAroundY()
        {
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(0f, 0f, 10f);
            FireEventInput evt = MakeEvent(Vector3.zero, new Vector3(0f, 30f, 0f));
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Vector3 expected = Quaternion.Euler(0f, 30f, 0f) * Vector3.forward;
            AssertVectorApprox(expected, result.FireDirection);
        }

        [Test]
        public void Solve_DirectionOffsetWithLockedTarget_RotatesFromTargetDir()
        {
            // 驗證: offset 應「以 baseDir 為軸」旋轉,而非「以 owner forward 為軸」(舊 bug)
            FireSolveContext ctx = MakeContext();
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(10f, 0f, 0f);
            FireEventInput evt = MakeEvent(Vector3.zero, new Vector3(0f, 30f, 0f));
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Quaternion baseRot = Quaternion.LookRotation(Vector3.right);
            Vector3 expected = baseRot * Quaternion.Euler(0f, 30f, 0f) * Vector3.forward;
            AssertVectorApprox(expected, result.FireDirection);
        }

        [Test]
        public void Solve_DirectionOffsetWithAimCamera_RotatesFromAimDir()
        {
            // 驗證: offset 不應蓋掉 AimCamera 解算結果(舊 bug 是 offset 把 baseDir 從 owner.rotation 重算)
            FireSolveContext ctx = MakeContext();
            ctx.HasAimCamera = true;
            ctx.AimHitPoint = new Vector3(5f, 0f, 10f);
            FireEventInput evt = MakeEvent(Vector3.zero, new Vector3(0f, 15f, 0f));
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Vector3 baseDir = new Vector3(5f, 0f, 10f).normalized;
            Quaternion baseRot = Quaternion.LookRotation(baseDir);
            Vector3 expected = baseRot * Quaternion.Euler(0f, 15f, 0f) * Vector3.forward;
            AssertVectorApprox(expected, result.FireDirection);
        }

        // === Pitch Clamp ===

        [Test]
        public void Solve_PitchClampOff_AllowsSteepDown()
        {
            FireSolveContext ctx = MakeContext();
            ctx.ApplyPitchClamp = false;
            ctx.SocketPosition = new Vector3(0f, 5f, 0f);
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(3f, 0f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            Assert.That(result.FireDirection.y, Is.LessThan(-0.5f));
        }

        [Test]
        public void Solve_PitchClampOn_ReducesDownwardYComparedToOff()
        {
            FireSolveContext baseCtx = MakeContext();
            baseCtx.SocketPosition = new Vector3(0f, 5f, 0f);
            baseCtx.HasLockedTarget = true;
            baseCtx.LockedTargetPosition = new Vector3(3f, 0f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveContext ctxOff = baseCtx;
            ctxOff.ApplyPitchClamp = false;
            FireSolveContext ctxOn = baseCtx;
            ctxOn.ApplyPitchClamp = true;
            ctxOn.MaxPitchDown = 0.5f;
            FireSolveResult resOff = FireDirectionSolver.Solve(in ctxOff, in evt);
            FireSolveResult resOn = FireDirectionSolver.Solve(in ctxOn, in evt);
            Assert.That(resOn.FireDirection.y, Is.GreaterThan(resOff.FireDirection.y),
                "啟用 clamp 後 y 應較不負值");
        }

        [Test]
        public void Solve_PitchClampOn_HorizontalDirection_NoChange()
        {
            FireSolveContext ctx = MakeContext();
            ctx.ApplyPitchClamp = true;
            ctx.MaxPitchDown = 0.5f;
            ctx.HasLockedTarget = true;
            ctx.LockedTargetPosition = new Vector3(10f, 0f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            AssertVectorApprox(Vector3.right, result.FireDirection);
        }

        // === Spawn Position ===

        [Test]
        public void Solve_SpawnPosition_AppliesSocketRotationAndOffset()
        {
            FireSolveContext ctx = MakeContext();
            ctx.SocketPosition = new Vector3(1f, 2f, 3f);
            ctx.SocketRotation = Quaternion.Euler(0f, 90f, 0f);
            FireEventInput evt = MakeEvent(new Vector3(0f, 0f, 1f), Vector3.zero);
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            AssertVectorApprox(new Vector3(2f, 2f, 3f), result.SpawnPosition);
        }

        [Test]
        public void Solve_SpawnPosition_NoOffset_EqualsSocketPosition()
        {
            FireSolveContext ctx = MakeContext();
            ctx.SocketPosition = new Vector3(5f, 1f, -2f);
            ctx.SocketRotation = Quaternion.Euler(0f, 45f, 0f);
            FireEventInput evt = MakeEvent();
            FireSolveResult result = FireDirectionSolver.Solve(in ctx, in evt);
            AssertVectorApprox(new Vector3(5f, 1f, -2f), result.SpawnPosition);
        }
    }
}
