using System.Collections.Generic;
using UnityEngine;

namespace GAS.Targeting.Combat
{
    /// <summary>
    /// 戰鬥目標搜尋 — 純空間查詢元件,與鎖定系統完全解耦。
    /// 提供給攻擊/閃避能力使用:
    ///   FindBestTarget        — 前方錐形內最近的可見敵人
    ///   TryGetSnapTarget      — 攻擊吸附(preferredTarget 優先,來自 HitTargetMemory)
    ///   CalculateSnapPosition — 依 Collider 邊緣計算吸附落點
    ///   FindAllTargetsInRange — 範圍內所有敵人(AOE 用)
    /// 掛在玩家根物件上,取代舊 TargetingSystem 的搜尋職責。
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatTargetFinder : MonoBehaviour
    {
        [Header("圖層")]
        [SerializeField, Tooltip("敵人圖層 — Physics.OverlapSphere 的 LayerMask")]
        private LayerMask _enemyLayer;
        [SerializeField, Tooltip("障礙物圖層 — 視線遮擋檢查用(牆、地形等)")]
        private LayerMask _obstacleLayer;

        [Header("預設搜尋參數")]
        [SerializeField, Tooltip("預設搜尋半徑(公尺)— FindBestTarget 未指定 range 時使用")]
        private float _defaultSearchRange = 10f;
        [SerializeField, Tooltip("預設搜尋角度(度)— 取此值的一半作為前方錐形半開角")]
        private float _defaultSearchAngle = 120f;
        [SerializeField, Tooltip("視線起算高度 — 視線起點為 origin + Vector3.up * EyeHeight")]
        private float _eyeHeight = 1.5f;

        [Header("吸附參數")]
        [SerializeField, Tooltip("TryGetSnapTarget 中 preferredTarget 的允許前方夾角(度)")]
        private float _preferredTargetMaxAngle = 60f;

        [Header("除錯")]
        [SerializeField, Tooltip("於 Scene 視窗顯示 FindBestTarget 的視線檢查結果")]
        private bool _showDebugGizmos = false;

        public LayerMask EnemyLayer => _enemyLayer;
        public LayerMask ObstacleLayer => _obstacleLayer;
        public float DefaultSearchRange => _defaultSearchRange;
        public float DefaultSearchAngle => _defaultSearchAngle;
        public float EyeHeight => _eyeHeight;

        private readonly Collider[] _overlapBuffer = new Collider[32];
        private CharacterController _cachedCc;

        private struct DebugTargetInfo
        {
            public Vector3 EyePos;
            public Vector3 TargetPoint;
            public bool IsBlocked;
            public Vector3 HitPoint;
        }
        private readonly List<DebugTargetInfo> _debugTargets = new();

        private void Awake()
        {
            _cachedCc = GetComponent<CharacterController>();
        }

        /// <summary>
        /// 在 origin 前方半徑 range、角度 angle 的錐形範圍內,回傳距離最近且視線可達的敵人。
        /// range / angle 傳 -1 則套用 Inspector 預設值。
        /// </summary>
        public Transform FindBestTarget(Vector3 origin, Vector3 forward, float range = -1f, float angle = -1f)
        {
            if (range < 0f) range = _defaultSearchRange;
            if (angle < 0f) angle = _defaultSearchAngle;
            if (_showDebugGizmos) _debugTargets.Clear();
            int count = Physics.OverlapSphereNonAlloc(origin, range, _overlapBuffer, _enemyLayer);
            Transform bestTarget = null;
            float closestDist = float.MaxValue;
            Vector3 eyePos = origin + Vector3.up * _eyeHeight;
            float halfAngle = angle * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Collider hit = _overlapBuffer[i];
                if (hit == null) continue;
                if (hit.transform == transform) continue;
                Vector3 dirToTarget = (hit.transform.position - origin).normalized;
                float targetAngle = Vector3.Angle(forward, dirToTarget);
                if (targetAngle > halfAngle) continue;
                Vector3 targetPoint = hit.ClosestPoint(eyePos);
                DebugTargetInfo debugInfo = new()
                {
                    EyePos = eyePos,
                    TargetPoint = targetPoint,
                    IsBlocked = false,
                };
                if (Physics.Linecast(targetPoint, eyePos, out RaycastHit wallHit, _obstacleLayer))
                {
                    debugInfo.IsBlocked = true;
                    debugInfo.HitPoint = wallHit.point;
                    if (_showDebugGizmos) _debugTargets.Add(debugInfo);
                    continue;
                }
                if (_showDebugGizmos) _debugTargets.Add(debugInfo);
                float dist = Vector3.Distance(origin, hit.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    bestTarget = hit.transform;
                }
            }
            return bestTarget;
        }

        /// <summary>
        /// 攻擊吸附入口:
        ///   若 preferredTarget 在範圍/前方 _preferredTargetMaxAngle/視線內 → 優先吸附。
        ///   否則退回 FindBestTarget(固定 120 度)。
        /// preferredTarget 由呼叫者提供(一般為 HitTargetMemory.LastHitTarget);此元件保持無狀態。
        /// </summary>
        public bool TryGetSnapTarget(Vector3 origin, Vector3 forward, float range, float stopDist,
            Transform preferredTarget, out Vector3 targetPos, out Transform targetTransform)
        {
            targetPos = Vector3.zero;
            targetTransform = null;
            if (preferredTarget != null && preferredTarget.gameObject.activeInHierarchy)
            {
                float dist = Vector3.Distance(origin, preferredTarget.position);
                if (dist <= range)
                {
                    Vector3 dir = (preferredTarget.position - origin).normalized;
                    if (Vector3.Angle(forward, dir) < _preferredTargetMaxAngle)
                    {
                        Vector3 eyePos = origin + Vector3.up * _eyeHeight;
                        Collider col = preferredTarget.GetComponent<Collider>();
                        Vector3 targetPoint = col != null ? col.ClosestPoint(eyePos) : preferredTarget.position;
                        if (!Physics.Linecast(targetPoint, eyePos, _obstacleLayer))
                        {
                            targetTransform = preferredTarget;
                            return CalculateSnapPosition(origin, targetTransform, stopDist, out targetPos);
                        }
                    }
                }
            }
            targetTransform = FindBestTarget(origin, forward, range, 120f);
            if (targetTransform != null)
            {
                return CalculateSnapPosition(origin, targetTransform, stopDist, out targetPos);
            }
            return false;
        }

        /// <summary>
        /// 計算吸附落點:優先以 target 的 Collider.ClosestPoint 取表面點,再依角色半徑 + stopDist 後退。
        /// 落點 Y 會強制同步 origin.y,避免吸附跳高/跳低。
        /// </summary>
        public bool CalculateSnapPosition(Vector3 origin, Transform target, float stopDist, out Vector3 snapPos)
        {
            snapPos = origin;
            if (target == null) return false;
            float characterRadius = _cachedCc != null ? _cachedCc.radius : 0.5f;
            if (target.TryGetComponent(out Collider col))
            {
                Vector3 closestPoint = col.ClosestPoint(origin);
                Vector3 direction = (closestPoint - origin).normalized;
                direction.y = 0f;
                float offset = characterRadius + Mathf.Max(0f, stopDist);
                snapPos = closestPoint - direction * offset;
                snapPos.y = origin.y;
            }
            else
            {
                Vector3 direction = (target.position - origin).normalized;
                snapPos = target.position - direction * (1f + stopDist);
                snapPos.y = origin.y;
            }
            return true;
        }

        /// <summary>
        /// 回傳 origin 半徑 range 內所有敵人(不做視線/角度篩選)。
        /// 提供 AOE 攻擊或範圍標記使用;結果為新 List,呼叫者自行處理生命週期。
        /// </summary>
        public List<Transform> FindAllTargetsInRange(Vector3 origin, float range)
        {
            List<Transform> results = new();
            int count = Physics.OverlapSphereNonAlloc(origin, range, _overlapBuffer, _enemyLayer);
            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null) continue;
                if (col.transform == transform) continue;
                results.Add(col.transform);
            }
            return results;
        }

        private void OnDrawGizmos()
        {
            if (!_showDebugGizmos) return;
            foreach (DebugTargetInfo info in _debugTargets)
            {
                if (info.IsBlocked)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(info.EyePos, info.HitPoint);
                    Gizmos.DrawSphere(info.HitPoint, 0.1f);
                    Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
                    Gizmos.DrawLine(info.HitPoint, info.TargetPoint);
                }
                else
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(info.EyePos, info.TargetPoint);
                }
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(info.TargetPoint, 0.05f);
            }
        }
    }
}
