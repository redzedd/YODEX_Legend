using UnityEngine;

namespace GAS.Targeting.LockOnV2
{
    /// <summary>
    /// 鎖定目標評分器 — 純 C# 類別,不繼承 MonoBehaviour
    /// 從 LockOnRegistry 蒐集候選,經視線 / 螢幕邊界 / 距離過濾後評分,回傳最佳目標
    /// 零 GC:單次遍歷追蹤最佳分數,不開暫存 List
    /// </summary>
    public class LockOnSelector
    {
        private Camera _camera;
        private LockOnSelectorConfig _config;

        public LockOnSelector(Camera camera, LockOnSelectorConfig config)
        {
            _camera = camera;
            _config = config;
        }

        public Camera Camera => _camera;

        public LockOnSelectorConfig Config => _config;

        public void SetCamera(Camera camera) => _camera = camera;

        public void SetConfig(LockOnSelectorConfig config) => _config = config;

        /// <summary>
        /// 從 Registry 挑選最佳初始鎖定目標
        /// 評分 = 螢幕中央距離 * CenterWeight + (世界距離 / range) * DistanceWeight,分數越低越佳
        /// rangeOverride > 0 時以此為搜尋半徑 (距離正規化也以它為分母);≤0 則用 Config.SearchRange
        /// </summary>
        public LockOnTarget FindInitialTarget(Vector3 originPos, float rangeOverride = -1f)
        {
            if (_camera == null || _config == null) return null;
            float range = rangeOverride > 0f ? rangeOverride : _config.SearchRange;
            float rangeSq = range * range;
            LockOnTarget best = null;
            float bestScore = float.PositiveInfinity;
            foreach (LockOnTarget t in LockOnRegistry.All)
            {
                if (!IsValidCandidate(t, originPos, rangeSq, out _, out Vector3 viewport, out float worldDist)) continue;
                float centerDist = Vector2.Distance(new Vector2(0.5f, 0.5f), new Vector2(viewport.x, viewport.y));
                float distNorm = Mathf.Clamp01(worldDist / range);
                float score = centerDist * _config.CenterWeight + distNorm * _config.DistanceWeight;
                if (score >= bestScore) continue;
                bestScore = score;
                best = t;
            }
            return best;
        }

        /// <summary>
        /// 8 方向目標切換:在 viewport 空間中尋找與 stickDir 同方向的最佳候選
        /// stickDir 為螢幕空間 (右=+X、上=+Y);夾角過大或同位置會被過濾
        /// </summary>
        public LockOnTarget FindDirectionalTarget(LockOnTarget current, Vector2 stickDir, Vector3 originPos)
        {
            if (_camera == null || _config == null || current == null) return null;
            if (stickDir.sqrMagnitude < 0.0001f) return null;
            stickDir.Normalize();
            Transform curAnchor = current.AnchorTransform;
            if (curAnchor == null) return null;
            Vector3 curVp3 = _camera.WorldToViewportPoint(curAnchor.position);
            Vector2 curVp = new(curVp3.x, curVp3.y);
            float rangeSq = _config.SearchRange * _config.SearchRange;
            LockOnTarget best = null;
            float bestScore = float.PositiveInfinity;
            foreach (LockOnTarget t in LockOnRegistry.All)
            {
                if (t == current) continue;
                if (!IsValidCandidate(t, originPos, rangeSq, out _, out Vector3 vp3, out _)) continue;
                Vector2 delta = new Vector2(vp3.x, vp3.y) - curVp;
                float deltaMag = delta.magnitude;
                if (deltaMag < 0.005f) continue;
                Vector2 candidateDir = delta / deltaMag;
                float dot = Vector2.Dot(stickDir, candidateDir);
                if (dot < _config.DirectionDotMin) continue;
                float score = (1f - dot) * _config.DirectionScoreMul + deltaMag;
                if (score >= bestScore) continue;
                bestScore = score;
                best = t;
            }
            return best;
        }

        private bool IsValidCandidate(
            LockOnTarget t,
            Vector3 originPos,
            float rangeSq,
            out Vector3 anchorPos,
            out Vector3 viewport,
            out float worldDist)
        {
            anchorPos = default;
            viewport = default;
            worldDist = 0f;
            if (t == null || !t.isActiveAndEnabled || !t.IsLockable) return false;
            Transform anchorT = t.AnchorTransform;
            if (anchorT == null) return false;
            anchorPos = anchorT.position;
            Vector3 delta = anchorPos - originPos;
            float sqDist = delta.sqrMagnitude;
            if (sqDist > rangeSq) return false;
            worldDist = Mathf.Sqrt(sqDist);
            viewport = _camera.WorldToViewportPoint(anchorPos);
            if (viewport.z <= 0f) return false;
            float m = _config.ScreenMargin;
            if (viewport.x < -m || viewport.x > 1f + m || viewport.y < -m || viewport.y > 1f + m) return false;
            if (IsOccluded(originPos, anchorPos)) return false;
            return true;
        }

        private bool IsOccluded(Vector3 from, Vector3 to)
        {
            if (_config.OcclusionMask.value == 0) return false;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return false;
            dir /= dist;
            return Physics.SphereCast(from, _config.OcclusionRadius, dir, out _, dist,
                _config.OcclusionMask, QueryTriggerInteraction.Ignore);
        }
    }
}
