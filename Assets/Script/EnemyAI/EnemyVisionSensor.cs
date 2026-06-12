using UnityEngine;

namespace EnemyAI
{
    /// <summary>
    /// 敵人視覺感知 — 扇形視野 + 距離 + 視線遮蔽（Raycast）
    /// 純 C# 類別，由 EnemyController 持有並驅動
    /// </summary>
    public class EnemyVisionSensor
    {
        // 多點 LOS 檢測高度（從玩家 transform 算起的 Y 偏移）— 腳、胸、頭三點，任一可見即視為看得到
        // 避免「玩家蹲在矮牆後」「玩家站在略高/略低台階」「轉角邊緣半遮蔽」這類單點 Raycast 容易誤判的情境
        private static readonly float[] LOS_CHECK_HEIGHTS = { 0.3f, 1.0f, 1.7f };

        private readonly Transform _self;
        private readonly Transform _eye;
        private readonly EnemyConfig _config;

        private bool _canSeePlayer;
        private Vector3 _lastKnownPosition;
        private bool _hasLastKnownPosition;
        private float _lastSeenTime = -1f;

        #region Properties

        public bool CanSeePlayer => _canSeePlayer;
        public Vector3 LastKnownPosition => _lastKnownPosition;
        public bool HasLastKnownPosition => _hasLastKnownPosition;

        /// <summary>距離上次看到玩家經過的秒數。從未看過時回傳 float.MaxValue</summary>
        public float TimeSinceLastSeen => _lastSeenTime < 0f ? float.MaxValue : Time.time - _lastSeenTime;

        #endregion

        public EnemyVisionSensor(Transform self, Transform eye, EnemyConfig config)
        {
            _self = self;
            _eye = eye != null ? eye : self;
            _config = config;
        }

        /// <summary>
        /// 每幀更新視野檢測
        /// hasDetectedPlayer 為 true 時放寬條件（已知玩家位置就用較大半徑追蹤，且不檢角度）
        /// </summary>
        public void Tick(Transform player, bool hasDetectedPlayer)
        {
            _canSeePlayer = false;
            if (player == null) return;

            Vector3 selfPos = _self.position;
            Vector3 toPlayer = player.position - selfPos;
            float dist = toPlayer.magnitude;

            float maxDist = hasDetectedPlayer ? _config.LoseTargetDistance : _config.ViewRadius;
            if (dist > maxDist) return;

            if (!hasDetectedPlayer && !IsWithinViewAngle(toPlayer)) return;

            if (IsLineOfSightBlocked(player)) return;

            _canSeePlayer = true;
            _lastKnownPosition = player.position;
            _hasLastKnownPosition = true;
            _lastSeenTime = Time.time;
        }

        public void UpdateLastKnownPosition(Vector3 position)
        {
            _lastKnownPosition = position;
            _hasLastKnownPosition = true;
        }

        public void ClearLastKnownPosition()
        {
            _hasLastKnownPosition = false;
        }

        private bool IsWithinViewAngle(Vector3 toPlayer)
        {
            Vector3 flat = toPlayer; flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return true;
            Vector3 forward = _self.forward; forward.y = 0f;
            float angle = Vector3.Angle(forward.normalized, flat.normalized);
            return angle <= _config.ViewAngle * 0.5f;
        }

        /// <summary>
        /// 多點 LOS 檢測 — 從眼睛打 Ray 到玩家身上 3 個高度（腳/胸/頭），任一未被擋就視為看得到。
        /// 降低「轉角邊緣」「玩家蹲下/站在略高低台階」這類短遮蔽造成的誤丟失。
        /// </summary>
        private bool IsLineOfSightBlocked(Transform player)
        {
            Vector3 origin = _eye.position;
            Vector3 playerPos = player.position;
            for (int i = 0; i < LOS_CHECK_HEIGHTS.Length; i++)
            {
                Vector3 target = playerPos + Vector3.up * LOS_CHECK_HEIGHTS[i];
                Vector3 dir = target - origin;
                float rayDist = dir.magnitude;
                if (rayDist < 0.01f) return false;
                if (!Physics.Raycast(origin, dir.normalized, rayDist, _config.ObstacleLayer))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
