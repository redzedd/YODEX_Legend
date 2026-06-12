using UnityEngine;

namespace Player.Locomotion
{
    /// <summary>
    /// 地面偵測 — CharacterController.isGrounded 作為快速路徑,僅在離地時才往下 SphereCast 補強。
    /// 修正 CharacterController.isGrounded 的兩個典型缺陷:
    ///   1. 垂直速度為正(哪怕 +0.01f)即回傳 false → 造成 Coyote Time 提早啟動
    ///   2. 踩在邊緣(膠囊半徑一半懸空)誤判離地
    /// 每幀由 Controller 呼叫 <see cref="Probe"/> 一次,結果寫入 <see cref="LocomotionStateContext.IsGrounded"/>。
    /// 不依賴 MonoBehaviour,純 C# 類別,未來可被其他 Controller 重用。
    /// </summary>
    public sealed class GroundSensor
    {
        /// <summary>單次探測結果 — 供 Gizmos 與除錯面板顯示使用。</summary>
        public struct Result
        {
            public bool IsGrounded;
            public Vector3 SphereCenter;
            public Vector3 HitPoint;
            public float ProbedDistance;
            /// <summary>true 代表靠 CharacterController.isGrounded 即判定為地面,未實際發出 SphereCast</summary>
            public bool FastPathUsed;
            public bool HasSphereCastHit;
        }

        private readonly LocomotionConfig _config;
        private readonly CharacterController _controller;
        private readonly Transform _transform;
        private Result _last;

        public Result LastResult => _last;

        public GroundSensor(LocomotionConfig config, CharacterController controller, Transform actorTransform)
        {
            _config = config;
            _controller = controller;
            _transform = actorTransform;
        }

        /// <summary>
        /// 執行一次地面偵測。先讀 CharacterController.isGrounded(快速路徑),離地時才實際 SphereCast。
        /// 回傳 true 表示角色接地或極近地面(已在 GroundProbeDistance 範圍內)。
        /// </summary>
        public bool Probe()
        {
            if (_controller == null || _config == null)
            {
                _last = default;
                return false;
            }
            if (_controller.isGrounded)
            {
                _last = new Result
                {
                    IsGrounded = true,
                    SphereCenter = GetCapsuleBottomSphereCenter(),
                    HitPoint = _transform.position,
                    ProbedDistance = 0f,
                    FastPathUsed = true,
                    HasSphereCastHit = false,
                };
                return true;
            }
            float radius = Mathf.Max(0.01f, _controller.radius * _config.GroundSphereRadiusScale);
            Vector3 sphereCenter = GetCapsuleBottomSphereCenter();
            float maxDistance = Mathf.Max(0f, _config.GroundProbeDistance);
            bool hit = Physics.SphereCast(
                sphereCenter,
                radius,
                Vector3.down,
                out RaycastHit hitInfo,
                maxDistance,
                _config.GroundMask,
                QueryTriggerInteraction.Ignore);
            _last = new Result
            {
                IsGrounded = hit,
                SphereCenter = sphereCenter,
                HitPoint = hit ? hitInfo.point : sphereCenter + Vector3.down * maxDistance,
                ProbedDistance = maxDistance,
                FastPathUsed = false,
                HasSphereCastHit = hit,
            };
            return hit;
        }

        /// <summary>
        /// 計算膠囊底部半球的中心 — CharacterController 的 center 為膠囊中心,
        /// 膠囊底的半球中心 = center - up * max(0, height*0.5 - radius)。
        /// SphereCast 以此為起點向下發,確保偵測範圍剛好涵蓋角色腳底。
        /// </summary>
        private Vector3 GetCapsuleBottomSphereCenter()
        {
            float radius = Mathf.Max(0.01f, _controller.radius * _config.GroundSphereRadiusScale);
            float halfHeight = _controller.height * 0.5f;
            float offset = Mathf.Max(0f, halfHeight - radius);
            return _transform.position + _controller.center - Vector3.up * offset;
        }

        /// <summary>在 Scene 視圖畫出 SphereCast 軌跡與命中點,供除錯使用。</summary>
        public void DrawGizmos()
        {
            if (_controller == null || _config == null || _transform == null)
            {
                return;
            }
            float radius = Mathf.Max(0.01f, _controller.radius * _config.GroundSphereRadiusScale);
            Vector3 sphereCenter = GetCapsuleBottomSphereCenter();
            Vector3 endCenter = sphereCenter + Vector3.down * _config.GroundProbeDistance;
            Color color;
            if (_last.IsGrounded && _last.FastPathUsed)
            {
                color = Color.green;
            }
            else if (_last.IsGrounded)
            {
                color = new Color(0.2f, 0.8f, 1f);
            }
            else
            {
                color = Color.red;
            }
            Gizmos.color = color;
            Gizmos.DrawWireSphere(sphereCenter, radius);
            Gizmos.DrawLine(sphereCenter, endCenter);
            Gizmos.DrawWireSphere(endCenter, radius);
            if (_last.HasSphereCastHit)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(_last.HitPoint, 0.05f);
            }
        }
    }
}
