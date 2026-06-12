using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

namespace GAS
{
    /// <summary>
    /// 目標系統 - 整合目標搜尋、選擇、追蹤和完整的鎖定功能
    /// 合併了原 TargetingSystem 和 LockOnManager 的功能
    /// </summary>
    public class TargetingSystem : MonoBehaviour
    {
        #region Enemy Registry (Static)

        private static readonly HashSet<GameObject> _registeredEnemies = new();

        /// <summary>註冊敵人（由敵人的 OnEnable 呼叫）</summary>
        public static void RegisterEnemy(GameObject enemy)
        {
            if (enemy != null) _registeredEnemies.Add(enemy);
        }

        /// <summary>取消註冊敵人（由敵人的 OnDisable 呼叫）</summary>
        public static void UnregisterEnemy(GameObject enemy)
        {
            _registeredEnemies.Remove(enemy);
        }

        /// <summary>取得所有已註冊的敵人（唯讀）</summary>
        public static IReadOnlyCollection<GameObject> RegisteredEnemies => _registeredEnemies;

        #endregion

        #region Configuration

        [Header("Target Detection")]
        [Tooltip("敵人圖層")]
        public LayerMask EnemyLayer;

        [Tooltip("障礙物圖層")]
        public LayerMask ObstacleLayer;

        [Header("Search Settings")]
        [Tooltip("默認搜索範圍")]
        public float DefaultSearchRange = 10f;

        [Tooltip("默認搜索角度")]
        public float DefaultSearchAngle = 120f;

        [Tooltip("視線檢測高度")]
        public float EyeHeight = 1.5f;

        #endregion

        #region Lock-On Configuration

        [Header("Lock-On - UI / Indicator")]
        public GameObject IndicatorPrefab;
        public Camera MainCamera;

        [Header("Lock-On - Cameras")]
        public CinemachineCamera ThirdPersonCam;
        public CinemachineCamera LockOnCamA;
        public CinemachineCamera LockOnCamB;

        [Header("Lock-On - Target Groups")]
        public CinemachineTargetGroup TargetGroupA;
        public CinemachineTargetGroup TargetGroupB;

        [Header("Lock-On - Player Anchor")]
        [Tooltip("玩家相機定位點 (胸口/頭上)")]
        public Transform PlayerCenterTransform;

        [Header("Lock-On - Settings")]
        public float ScreenMargin = 0.2f;
        public float NearPriorityRange = 4f;
        public float HardUnlockRangeMul = 1.5f;

        [Header("Lock-On - Occlusion")]
        public LayerMask OcclusionMask;
        public float OccludedGraceTime = 0.35f;
        public float IndicatorHeight = 4.5f;

        [Header("Lock-On - Camera Taste")]
        public float LockFOV = 55f;
        public float ThirdFOV = 60f;
        public float GroupPlayerWeight = 1f;
        public float GroupPlayerRadius = 1.2f;
        public float GroupTargetWeight = 1.0f;
        public float GroupTargetRadius = 1.0f;

        [Header("Lock-On - Vertical Limits")]
        [Tooltip("鎖定相機最大仰角（防止從頭頂穿過）")]
        public float MaxVerticalAngle = 50f;

        [Tooltip("鎖定相機最大俯角（防止從腳底穿過）")]
        public float MaxDownwardAngle = 35f;

        [Header("Lock-On - Proximity Fix（近距離防俯視）")]
        [Tooltip("近距離起始距離(公尺)— 低於此距離開始降低玩家權重、放大敵人權重,避免 TargetGroup 中心塌到玩家身上造成鏡頭俯視")]
        public float ProximityCloseRange = 2.5f;
        [Tooltip("恢復預設權重的距離(公尺)— 超過此距離後權重回復為 GroupPlayerWeight / 敵人 EffectiveWeight")]
        public float ProximityFarRange = 6f;
        [Tooltip("近距離時玩家在 TargetGroup 內的權重下限 — 越低鏡頭越偏向敵人")]
        public float ProximityCloseRangePlayerWeight = 0.1f;
        [Tooltip("近距離時敵人權重的倍率 — 放大敵人在 TargetGroup 的影響")]
        public float ProximityCloseRangeEnemyMul = 3f;

        #endregion

        #region Target Marking

        [Header("Target Marking")]
        [Tooltip("標記清除延遲時間（秒）")]
        public float MarkClearDelay = 3f;

        #endregion

        #region Debug

        [Header("Debug")]
        public bool ShowDebugGizmos = true;

        #endregion

        #region Public Properties

        /// <summary>是否鎖定中</summary>
        public bool IsLocked => _isLocked;

        /// <summary>目前鎖定目標的根物件</summary>
        public Transform CurrentTarget
        {
            get
            {
                if (_currentEnemyRoot != null && _currentEnemyRoot.gameObject.activeInHierarchy)
                    return _currentEnemyRoot;
                return null;
            }
        }

        /// <summary>目前的鎖定錨點 (若無就回 Root)</summary>
        public Transform CurrentLockAnchor =>
            _currentEnemyAnchor != null ? _currentEnemyAnchor : _currentEnemyRoot;

        /// <summary>最後命中的目標 (用於智能追蹤)</summary>
        public Transform LastHitTarget 
        { 
            get => _lastHitTarget;
            set
            {
                _lastHitTarget = value;
                
                // [NEW] 每次設置新目標時，重置延遲清除計時器
                if (value != null)
                {
                    _clearMarkTimer = 0f;
                    _isMarkClearScheduled = false;
                }
            }
        }
        private Transform _lastHitTarget;

        #endregion

        #region Private Fields

        // 緩衝區
        private readonly Collider[] _overlapBuffer = new Collider[32];
        // 鎖定候選蒐集 buffer — GatherLockCandidates 每次呼叫前清空;不可跨幀保存回傳結果
        private readonly HashSet<Transform> _candidateDedupe = new();
        private readonly List<Transform> _candidateBuffer = new();

        // 鎖定狀態
        private bool _isLocked = false;
        private Transform _currentEnemyRoot;
        private Transform _currentEnemyAnchor;
        private Transform _currentIndicatorAnchor;
        private EnemyLockOnConfig _currentConfig;
        private GameObject _currentIndicator;
        private float _occludedTimer = 0f;

        // 雙攝影機索引: 0=A, 1=B, -1=未鎖定
        private int _activeLockIdx = -1;

        // 標記清除計時器
        private float _clearMarkTimer = 0f;
        private bool _isMarkClearScheduled = false;

        private CinemachineCamera ActiveLockCam => _activeLockIdx == 0 ? LockOnCamA : LockOnCamB;
        private CinemachineCamera InactiveLockCam => _activeLockIdx == 0 ? LockOnCamB : LockOnCamA;
        private CinemachineTargetGroup ActiveGroup => _activeLockIdx == 0 ? TargetGroupA : TargetGroupB;
        private CinemachineTargetGroup InactiveGroup => _activeLockIdx == 0 ? TargetGroupB : TargetGroupA;

        private CinemachineRotationComposer _rotA;
        private CinemachineRotationComposer _rotB;

        // Debug
        private struct DebugTargetInfo
        {
            public Transform Target;
            public Vector3 EyePos;
            public Vector3 TargetPoint;
            public bool IsBlocked;
            public Vector3 HitPoint;
        }
        private readonly List<DebugTargetInfo> _debugTargets = new();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (MainCamera == null) MainCamera = Camera.main;

            // 初始化鎖定攝影機
            if (LockOnCamA != null)
            {
                if (PlayerCenterTransform != null) LockOnCamA.Follow = PlayerCenterTransform;
                if (TargetGroupA != null) LockOnCamA.LookAt = TargetGroupA.transform;
                _rotA = LockOnCamA.GetComponent<CinemachineRotationComposer>();
            }
            if (LockOnCamB != null)
            {
                if (PlayerCenterTransform != null) LockOnCamB.Follow = PlayerCenterTransform;
                if (TargetGroupB != null) LockOnCamB.LookAt = TargetGroupB.transform;
                _rotB = LockOnCamB.GetComponent<CinemachineRotationComposer>();
            }
            if (ThirdPersonCam != null && PlayerCenterTransform != null)
            {
                ThirdPersonCam.Follow = PlayerCenterTransform;
                ThirdPersonCam.LookAt = PlayerCenterTransform;
            }

            SetLockOnPriorities(-1);
            if (ThirdFOV > 0f && ThirdPersonCam != null)
                ThirdPersonCam.Lens.FieldOfView = ThirdFOV;

            ResetGroup(TargetGroupA, null, 0, 0, clearAll: true); EnsurePlayerMember(TargetGroupA);
            ResetGroup(TargetGroupB, null, 0, 0, clearAll: true); EnsurePlayerMember(TargetGroupB);

            SetScreenPos(_rotA, Vector2.zero);
            SetScreenPos(_rotB, Vector2.zero);
        }

        private void Update()
        {
            // [NEW] 更新標記清除計時器
            UpdateMarkClearTimer();
        }

        #endregion

        #region Target Marking System

        /// <summary>
        /// 啟動延遲清除標記計時器
        /// </summary>
        public void ScheduleMarkClear()
        {
            if (_lastHitTarget != null)
            {
                _isMarkClearScheduled = true;
                _clearMarkTimer = 0f;
                
                if (ShowDebugGizmos)
                {
                    Debug.Log($"[TargetingSystem] Scheduled mark clear in {MarkClearDelay}s for target: {_lastHitTarget.name}");
                }
            }
        }

        /// <summary>
        /// 取消延遲清除標記
        /// </summary>
        public void CancelMarkClear()
        {
            if (_isMarkClearScheduled)
            {
                _isMarkClearScheduled = false;
                _clearMarkTimer = 0f;
                
                if (ShowDebugGizmos)
                {
                    Debug.Log("[TargetingSystem] Cancelled scheduled mark clear");
                }
            }
        }

        /// <summary>
        /// 立即清除標記（用於取消動作、被打斷等情況）
        /// </summary>
        public void ClearMarkImmediate()
        {
            if (_lastHitTarget != null)
            {
                if (ShowDebugGizmos)
                {
                    Debug.Log($"[TargetingSystem] Immediately cleared mark for target: {_lastHitTarget.name}");
                }
                
                _lastHitTarget = null;
            }
            
            _isMarkClearScheduled = false;
            _clearMarkTimer = 0f;
        }

        /// <summary>
        /// 更新標記清除計時器
        /// </summary>
        private void UpdateMarkClearTimer()
        {
            if (!_isMarkClearScheduled || _lastHitTarget == null) return;
            if (_isLocked) return; // 鎖定中不自動清除標記

            _clearMarkTimer += Time.deltaTime;

            if (_clearMarkTimer >= MarkClearDelay)
            {
                if (ShowDebugGizmos)
                {
                    Debug.Log($"[TargetingSystem] Auto-cleared mark after {MarkClearDelay}s delay");
                }
                
                _lastHitTarget = null;
                _isMarkClearScheduled = false;
                _clearMarkTimer = 0f;
            }
        }

        #endregion

        #region Target Finding (from original TargetingSystem)

        /// <summary>
        /// 搜索最佳目標 (基於角度和距離，不依賴攝影機)
        /// </summary>
        public Transform FindBestTarget(Vector3 origin, Vector3 forward, float range = -1f, float angle = -1f)
        {
            if (range < 0f) range = DefaultSearchRange;
            if (angle < 0f) angle = DefaultSearchAngle;

            if (ShowDebugGizmos) _debugTargets.Clear();

            int count = Physics.OverlapSphereNonAlloc(origin, range, _overlapBuffer, EnemyLayer);

            Transform bestTarget = null;
            float closestDist = float.MaxValue;
            Vector3 eyePos = origin + Vector3.up * EyeHeight;

            for (int i = 0; i < count; i++)
            {
                var hit = _overlapBuffer[i];
                if (hit.transform == transform) continue;

                DebugTargetInfo debugInfo = new()
                {
                    Target = hit.transform,
                    EyePos = eyePos,
                    IsBlocked = false
                };

                Vector3 dirToTarget = (hit.transform.position - origin).normalized;
                float targetAngle = Vector3.Angle(forward, dirToTarget);

                if (targetAngle > angle * 0.5f) continue;

                Vector3 targetPoint = hit.ClosestPoint(eyePos);
                debugInfo.TargetPoint = targetPoint;

                if (Physics.Linecast(targetPoint, eyePos, out RaycastHit wallHit, ObstacleLayer))
                {
                    debugInfo.IsBlocked = true;
                    debugInfo.HitPoint = wallHit.point;
                    if (ShowDebugGizmos) _debugTargets.Add(debugInfo);
                    continue;
                }

                if (ShowDebugGizmos) _debugTargets.Add(debugInfo);

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
        /// 嘗試獲取智能吸附目標
        /// </summary>
        public bool TryGetSnapTarget(Vector3 origin, Vector3 forward, float range, float stopDist,
            out Vector3 targetPos, out Transform targetTransform)
        {
            targetPos = Vector3.zero;
            targetTransform = null;

            if (LastHitTarget != null && LastHitTarget.gameObject.activeInHierarchy)
            {
                float dist = Vector3.Distance(origin, LastHitTarget.position);
                if (dist <= range)
                {
                    Vector3 dir = (LastHitTarget.position - origin).normalized;
                    if (Vector3.Angle(forward, dir) < 60f)
                    {
                        Vector3 eyePos = origin + Vector3.up * EyeHeight;
                        Vector3 targetPoint = LastHitTarget.GetComponent<Collider>()?.ClosestPoint(eyePos)
                            ?? LastHitTarget.position;

                        if (!Physics.Linecast(targetPoint, eyePos, ObstacleLayer))
                        {
                            targetTransform = LastHitTarget;
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
        /// 計算吸附位置
        /// </summary>
        public bool CalculateSnapPosition(Vector3 origin, Transform target, float stopDist, out Vector3 snapPos)
        {
            snapPos = origin;

            float characterRadius = 0.5f;
            var cc = GetComponent<CharacterController>();
            if (cc != null) characterRadius = cc.radius;

            if (target.TryGetComponent(out Collider col))
            {
                Vector3 closestPoint = col.ClosestPoint(origin);
                Vector3 direction = (closestPoint - origin).normalized;
                direction.y = 0;
                float offset = characterRadius + Mathf.Max(0, stopDist);
                snapPos = closestPoint - (direction * offset);
                snapPos.y = origin.y;
            }
            else
            {
                Vector3 direction = (target.position - origin).normalized;
                snapPos = target.position - direction * (1.0f + stopDist);
                snapPos.y = origin.y;
            }

            return true;
        }

        /// <summary>
        /// 搜索範圍內的所有敵人
        /// </summary>
        public List<Transform> FindAllTargetsInRange(Vector3 origin, float range)
        {
            var results = new List<Transform>();
            int count = Physics.OverlapSphereNonAlloc(origin, range, _overlapBuffer, EnemyLayer);

            for (int i = 0; i < count; i++)
            {
                if (_overlapBuffer[i].transform != transform)
                {
                    results.Add(_overlapBuffer[i].transform);
                }
            }

            return results;
        }

        #endregion

        #region Lock-On API

        /// <summary>
        /// 嘗試鎖定目標
        /// </summary>
        public void TryLockOn(float range, float nearPriorityRangeFromPlayer, Transform origin)
        {
            NearPriorityRange = nearPriorityRangeFromPlayer;
            SetTarget(SelectBestLockTarget(range, origin));
        }

        /// <summary>
        /// 清除鎖定
        /// </summary>
        public void ClearLockOn()
        {
            _isLocked = false;

            // 解鎖時清除標記
            ClearMarkImmediate();

            if (_currentIndicator != null) { Object.Destroy(_currentIndicator); _currentIndicator = null; }

            ResetGroup(TargetGroupA, null, 0, 0, clearAll: true); EnsurePlayerMember(TargetGroupA);
            ResetGroup(TargetGroupB, null, 0, 0, clearAll: true); EnsurePlayerMember(TargetGroupB);

            _currentEnemyRoot = null;
            _currentEnemyAnchor = null;
            _currentIndicatorAnchor = null;
            _currentConfig = null;
            _occludedTimer = 0f;

            _activeLockIdx = -1;
            SetLockOnPriorities(-1);
            if (ThirdFOV > 0f && ThirdPersonCam != null)
                ThirdPersonCam.Lens.FieldOfView = ThirdFOV;

            SetScreenPos(_rotA, Vector2.zero);
            SetScreenPos(_rotB, Vector2.zero);
        }

        /// <summary>
        /// 維護鎖定 (每幀呼叫)
        /// </summary>
        public void TickMaintain(float playerToTargetMaxRange, Transform player)
        {
            if (!_isLocked) return;

            if (_currentEnemyRoot == null || _currentEnemyRoot.gameObject == null
                || !_currentEnemyRoot.gameObject.activeInHierarchy)
            {
                if (!TryFallbackOrUnlock(playerToTargetMaxRange, player)) return;
            }

            float dist = Vector3.Distance(player.position, _currentEnemyRoot.position);
            if (dist > playerToTargetMaxRange * HardUnlockRangeMul)
            {
                ClearLockOn();
                return;
            }

            bool occluded = IsOccluded(PlayerCenterTransform != null
                ? PlayerCenterTransform.position : player.position,
                GetAimPoint(_currentEnemyRoot), out _);

            if (occluded)
            {
                _occludedTimer += Time.deltaTime;
                if (_occludedTimer >= OccludedGraceTime)
                {
                    if (!TryFallbackOrUnlock(playerToTargetMaxRange, player)) return;
                }
            }
            else
            {
                _occludedTimer = 0f;
            }

            // 垂直角度補償：當目標高低差大時，調整構圖螢幕位置避免相機穿越
            ApplyVerticalAngleCompensation(player);

            // 近距離權重調整：避免 TargetGroup 中心塌到玩家身上造成鏡頭俯視
            UpdateTargetGroupWeightsForProximity(player);

            // 更新指示器
            if (_currentIndicator != null && MainCamera != null)
            {
                Vector3 worldPos = (_currentIndicatorAnchor != null)
                    ? _currentIndicatorAnchor.position
                    : _currentEnemyRoot.position + Vector3.up * IndicatorHeight;

                Vector3 screenPos = MainCamera.WorldToScreenPoint(worldPos);
                _currentIndicator.transform.position = screenPos;
                _currentIndicator.SetActive(screenPos.z >= 0f);
            }
        }

        /// <summary>
        /// 設定目標 / 切換目標
        /// </summary>
        public void SetTarget(Transform targetRoot)
        {
            if (targetRoot == null) { ClearLockOn(); return; }

            var newAnchor = ResolveLockAnchor(targetRoot);
            var indicatorAnchor = ResolveIndicatorAnchor(targetRoot);

            _isLocked = true;
            _currentEnemyRoot = targetRoot;
            _currentEnemyAnchor = newAnchor;
            _currentIndicatorAnchor = indicatorAnchor;
            _currentConfig = targetRoot.GetComponent<EnemyLockOnConfig>();
            _occludedTimer = 0f;

            // 鎖定即標記：設定 LastHitTarget 並取消任何排定的清除
            LastHitTarget = targetRoot;
            CancelMarkClear();

            if (_currentIndicator == null && IndicatorPrefab != null)
                _currentIndicator = Instantiate(IndicatorPrefab, transform);
            if (_currentIndicator != null) _currentIndicator.SetActive(true);

            Vector2 sp = GetScreenPosition(targetRoot);

            if (_activeLockIdx == -1)
            {
                _activeLockIdx = 0;
                ResetGroup(TargetGroupA, newAnchor != null ? newAnchor : targetRoot,
                    GetGroupWeight(targetRoot), GetGroupRadius(targetRoot), clearAll: true);
                EnsurePlayerMember(TargetGroupA);

                SetScreenPos(_rotA, sp);
                SetLockOnPriorities(0);
                if (LockFOV > 0f && LockOnCamA != null)
                    LockOnCamA.Lens.FieldOfView = LockFOV;
                return;
            }

            var inactiveGroup = InactiveGroup;
            ResetGroup(inactiveGroup, newAnchor != null ? newAnchor : targetRoot,
                GetGroupWeight(targetRoot), GetGroupRadius(targetRoot), clearAll: true);
            EnsurePlayerMember(inactiveGroup);

            if (_activeLockIdx == 0) SetScreenPos(_rotB, sp);
            else SetScreenPos(_rotA, sp);

            PromoteInactiveLockCam();
            _activeLockIdx = 1 - _activeLockIdx;
        }

        /// <summary>
        /// 選擇左/右相鄰目標
        /// </summary>
        public Transform SelectSiblingTarget(bool toRight, float range, Transform player,
            Transform current, float nearPriorityRangeLocal)
        {
            if (MainCamera == null || current == null) return null;

            List<Transform> candidates = GatherLockCandidates(player.position, range);
            if (candidates.Count == 0) return null;

            Transform curAnchor = ResolveLockAnchor(current);
            Vector3 curVP3 = MainCamera.WorldToViewportPoint(curAnchor.position);

            Transform bestNear = null; float bestNearDist = Mathf.Infinity;
            float bestX = toRight ? -Mathf.Infinity : Mathf.Infinity;
            float bestCenterDist = Mathf.Infinity;
            Transform bestScreen = null;

            foreach (Transform root in candidates)
            {
                if (root == current) continue;

                Transform anchor = ResolveLockAnchor(root);

                float worldDist = Vector3.Distance(player.position, root.position);
                if (worldDist > range) continue;

                Vector3 vp = MainCamera.WorldToViewportPoint(anchor.position);
                if (vp.z <= 0f) continue;

                bool isRight = vp.x > curVP3.x;
                if ((toRight && !isRight) || (!toRight && isRight)) continue;

                if (vp.x < -ScreenMargin || vp.x > 1f + ScreenMargin
                    || vp.y < -ScreenMargin || vp.y > 1f + ScreenMargin)
                    continue;

                Vector3 playerEye = PlayerCenterTransform != null
                    ? PlayerCenterTransform.position : player.position;
                if (IsOccluded(playerEye, anchor.position, out _)) continue;

                if (worldDist <= nearPriorityRangeLocal && worldDist < bestNearDist)
                { bestNearDist = worldDist; bestNear = root; continue; }

                Vector2 center = new(0.5f, 0.5f);
                float centerDist = Vector2.Distance(center, new Vector2(vp.x, vp.y));

                if (toRight)
                {
                    if (vp.x > bestX || (Mathf.Approximately(vp.x, bestX) && centerDist < bestCenterDist))
                    { bestX = vp.x; bestCenterDist = centerDist; bestScreen = root; }
                }
                else
                {
                    if (vp.x < bestX || (Mathf.Approximately(vp.x, bestX) && centerDist < bestCenterDist))
                    { bestX = vp.x; bestCenterDist = centerDist; bestScreen = root; }
                }
            }
            return bestNear != null ? bestNear : bestScreen;
        }

        /// <summary>
        /// 2D 方向目標選擇（搖桿上下左右）
        /// 根據搖桿方向在 viewport 空間中尋找最佳候選目標
        /// </summary>
        public Transform SelectDirectionalTarget(Vector2 stickInput, float range, Transform player,
            Transform current, float nearPriorityRangeLocal)
        {
            if (MainCamera == null || current == null) return null;

            List<Transform> candidates = GatherLockCandidates(player.position, range);
            if (candidates.Count == 0) return null;

            Vector2 stickDir = stickInput.normalized;
            Transform curAnchor = ResolveLockAnchor(current);
            Vector3 curVP3 = MainCamera.WorldToViewportPoint(curAnchor.position);
            Vector2 curVP = new(curVP3.x, curVP3.y);

            Transform bestCandidate = null;
            float bestScore = Mathf.Infinity;

            foreach (Transform root in candidates)
            {
                if (root == current) continue;

                Transform anchor = ResolveLockAnchor(root);
                float worldDist = Vector3.Distance(player.position, root.position);
                if (worldDist > range) continue;

                // 垂直角度篩選
                if (!IsVerticalAngleValid(player.position, root.position)) continue;

                Vector3 vp3 = MainCamera.WorldToViewportPoint(anchor.position);
                if (vp3.z <= 0f) continue;

                Vector2 vp = new(vp3.x, vp3.y);

                // 螢幕邊界檢查
                if (vp.x < -ScreenMargin || vp.x > 1f + ScreenMargin
                    || vp.y < -ScreenMargin || vp.y > 1f + ScreenMargin)
                    continue;

                // 遮擋檢查
                Vector3 playerEye = PlayerCenterTransform != null
                    ? PlayerCenterTransform.position : player.position;
                if (IsOccluded(playerEye, anchor.position, out _)) continue;

                // 計算 viewport 中從當前目標到候選目標的方向
                Vector2 candidateDir = (vp - curVP);
                float vpDist = candidateDir.magnitude;
                if (vpDist < 0.01f) continue;

                candidateDir.Normalize();

                // 方向一致性（dot product > 0.4 才算同方向）
                float dot = Vector2.Dot(stickDir, candidateDir);
                if (dot < 0.4f) continue;

                // 綜合評分：方向越一致、距離越近 → 分數越低越好
                // 方向權重高，距離作為平衡
                float directionScore = 1f - dot; // 0 = 完全一致, 1 = 垂直
                float distanceScore = vpDist;
                float score = directionScore * 2f + distanceScore;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestCandidate = root;
                }
            }
            return bestCandidate;
        }

        #endregion

        #region Lock-On Internal

        /// <summary>
        /// 基於 EnemyLayer 在範圍內蒐集鎖定候選目標 — 取代依賴靜態註冊表的舊流程。
        /// 多 Collider 敵人以 attachedRigidbody 或 EnemyLockOnConfig 收斂至同一根物件並去重。
        /// 回傳值為內部 buffer,只能立即讀取、不可跨幀保存。
        /// </summary>
        private List<Transform> GatherLockCandidates(Vector3 origin, float range)
        {
            _candidateDedupe.Clear();
            _candidateBuffer.Clear();
            int count = Physics.OverlapSphereNonAlloc(origin, range, _overlapBuffer, EnemyLayer,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null) continue;
                Transform root = ResolveEnemyRoot(col);
                if (root == null || root == transform) continue;
                if (!root.gameObject.activeInHierarchy) continue;
                if (!_candidateDedupe.Add(root)) continue;
                _candidateBuffer.Add(root);
            }
            return _candidateBuffer;
        }

        /// <summary>
        /// 從 Collider 解析出敵人的根物件 Transform。
        /// 優先順序:attachedRigidbody → EnemyLockOnConfig 所在的父物件 → Collider 自身。
        /// </summary>
        private Transform ResolveEnemyRoot(Collider col)
        {
            if (col.attachedRigidbody != null)
            {
                return col.attachedRigidbody.transform;
            }
            EnemyLockOnConfig config = col.GetComponentInParent<EnemyLockOnConfig>();
            if (config != null)
            {
                return config.transform;
            }
            return col.transform;
        }

        private Transform SelectBestLockTarget(float range, Transform origin)
        {
            if (MainCamera == null) return null;

            List<Transform> candidates = GatherLockCandidates(origin.position, range);
            if (candidates.Count == 0) return null;

            Transform bestNear = null; float bestNearDist = Mathf.Infinity;
            Transform bestScreen = null; float bestScreenScore = Mathf.Infinity;

            Vector3 playerEye = PlayerCenterTransform != null
                ? PlayerCenterTransform.position : origin.position;

            foreach (Transform root in candidates)
            {
                Transform anchor = ResolveLockAnchor(root);

                float worldDist = Vector3.Distance(origin.position, root.position);
                if (worldDist > range) continue;

                // 垂直角度篩選
                if (!IsVerticalAngleValid(origin.position, root.position)) continue;

                Vector3 vp = MainCamera.WorldToViewportPoint(anchor.position);
                if (vp.z <= 0f || vp.x < -ScreenMargin || vp.x > 1f + ScreenMargin
                    || vp.y < -ScreenMargin || vp.y > 1f + ScreenMargin)
                    continue;

                if (IsOccluded(playerEye, anchor.position, out _)) continue;

                if (worldDist <= NearPriorityRange && worldDist < bestNearDist)
                { bestNearDist = worldDist; bestNear = root; }

                Vector2 center = new(0.5f, 0.5f);
                float screenDist = Vector2.Distance(center, new Vector2(vp.x, vp.y));
                float score = screenDist * 1.0f + Mathf.Clamp01(worldDist / range) * 0.35f;
                if (score < bestScreenScore) { bestScreenScore = score; bestScreen = root; }
            }
            return bestNear != null ? bestNear : bestScreen;
        }

        private void SetLockOnPriorities(int useIdx)
        {
            if (ThirdPersonCam != null) ThirdPersonCam.Priority = (useIdx == -1) ? 1 : 0;
            if (LockOnCamA != null) LockOnCamA.Priority = (useIdx == 0) ? 2 : 0;
            if (LockOnCamB != null) LockOnCamB.Priority = (useIdx == 1) ? 2 : 0;
        }

        private void PromoteInactiveLockCam()
        {
            if (InactiveLockCam == null || ActiveLockCam == null) return;
            if (LockFOV > 0f) InactiveLockCam.Lens.FieldOfView = LockFOV;
            InactiveLockCam.Priority = 2;
            ActiveLockCam.Priority = 1;
        }

        private void ResetGroup(CinemachineTargetGroup group, Transform enemyAnchor,
            float enemyWeight, float enemyRadius, bool clearAll)
        {
            if (group == null) return;

            if (clearAll) group.Targets.Clear();

            if (PlayerCenterTransform != null)
            {
                if (group.FindMember(PlayerCenterTransform) < 0)
                    group.AddMember(PlayerCenterTransform, GroupPlayerWeight, GroupPlayerRadius);
                else
                    UpdateMember(group, PlayerCenterTransform, GroupPlayerWeight, GroupPlayerRadius);
            }

            if (enemyAnchor != null)
            {
                if (group.FindMember(enemyAnchor) < 0)
                    group.AddMember(enemyAnchor, enemyWeight, enemyRadius);
                else
                    UpdateMember(group, enemyAnchor, enemyWeight, enemyRadius);
            }

            group.DoUpdate();
        }

        private void EnsurePlayerMember(CinemachineTargetGroup group)
        {
            if (group == null || PlayerCenterTransform == null) return;
            if (group.FindMember(PlayerCenterTransform) < 0)
                group.AddMember(PlayerCenterTransform, GroupPlayerWeight, GroupPlayerRadius);
        }

        private void UpdateMember(CinemachineTargetGroup group, Transform obj, float weight, float radius)
        {
            int idx = group.FindMember(obj);
            if (idx < 0) return;
            var list = group.Targets;
            var t = list[idx];
            t.Weight = weight;
            t.Radius = radius;
            list[idx] = t;
        }

        /// <summary>
        /// 垂直角度補償 - 當目標與玩家有較大高低差時
        /// 動態調整 CinemachineRotationComposer 的構圖位置
        /// 防止相機從頭頂或腳底穿過
        /// </summary>
        private void ApplyVerticalAngleCompensation(Transform player)
        {
            if (_currentEnemyRoot == null) return;
            var activeRot = _activeLockIdx == 0 ? _rotA : _rotB;
            if (activeRot == null) return;

            Vector3 delta = _currentEnemyRoot.position - player.position;
            float horizontalDist = new Vector2(delta.x, delta.z).magnitude;
            if (horizontalDist < 0.5f) horizontalDist = 0.5f;

            float verticalAngle = Mathf.Atan2(delta.y, horizontalDist) * Mathf.Rad2Deg;

            // 基礎螢幕位置
            Vector2 baseScreenPos = GetScreenPosition(_currentEnemyRoot);

            // 根據垂直角度調整 Y 軸構圖：目標在上方時壓低構圖，在下方時抬高構圖
            float maxAngle = Mathf.Max(MaxVerticalAngle, 1f);
            float minAngle = Mathf.Max(MaxDownwardAngle, 1f);

            float compensationY = 0f;
            if (verticalAngle > 10f)
            {
                // 目標在上方：將構圖位置向下偏移，限制相機仰角
                float t = Mathf.InverseLerp(10f, maxAngle, verticalAngle);
                compensationY = -Mathf.Lerp(0f, 0.15f, t);
            }
            else if (verticalAngle < -10f)
            {
                // 目標在下方：將構圖位置向上偏移，限制相機俯角
                float t = Mathf.InverseLerp(-10f, -minAngle, verticalAngle);
                compensationY = Mathf.Lerp(0f, 0.15f, t);
            }

            Vector2 adjustedPos = new(baseScreenPos.x, baseScreenPos.y + compensationY);
            SetScreenPos(activeRot, adjustedPos);
        }

        /// <summary>
        /// 依玩家與目標距離動態調整 TargetGroup 內兩名成員的權重。
        /// 近距離時玩家權重壓到極低、敵人權重放大,讓 Group 中心偏向敵人,
        /// 避免 Group 中心塌到玩家身上造成鏡頭從頭頂俯視;遠距離平滑回復預設權重。
        /// </summary>
        private void UpdateTargetGroupWeightsForProximity(Transform player)
        {
            if (_currentEnemyRoot == null || PlayerCenterTransform == null)
            {
                return;
            }
            CinemachineTargetGroup group = ActiveGroup;
            if (group == null)
            {
                return;
            }
            float dist = Vector3.Distance(player.position, _currentEnemyRoot.position);
            float farRange = Mathf.Max(ProximityFarRange, ProximityCloseRange + 0.01f);
            float t = Mathf.InverseLerp(ProximityCloseRange, farRange, dist);
            float configEnemyWeight = GetGroupWeight(_currentEnemyRoot);
            float playerWeight = Mathf.Lerp(ProximityCloseRangePlayerWeight, GroupPlayerWeight, t);
            float enemyWeight = Mathf.Lerp(configEnemyWeight * ProximityCloseRangeEnemyMul, configEnemyWeight, t);
            UpdateMember(group, PlayerCenterTransform, playerWeight, GroupPlayerRadius);
            Transform enemyAnchor = _currentEnemyAnchor != null ? _currentEnemyAnchor : _currentEnemyRoot;
            UpdateMember(group, enemyAnchor, enemyWeight, GetGroupRadius(_currentEnemyRoot));
            group.DoUpdate();
        }

        private bool TryFallbackOrUnlock(float range, Transform origin)
        {
            Transform fallback = SelectBestLockTarget(range, origin);
            if (fallback != null) { SetTarget(fallback); return true; }
            ClearLockOn();
            return false;
        }

        #endregion

        #region Helpers

        private EnemyLockOnConfig GetConfig(Transform root) =>
            root != null ? root.GetComponent<EnemyLockOnConfig>() : null;

        private Transform ResolveLockAnchor(Transform root)
        {
            var cfg = GetConfig(root);
            if (cfg != null && cfg.EffectiveLockAnchor != null) return cfg.EffectiveLockAnchor;
            return root;
        }

        private Transform ResolveIndicatorAnchor(Transform root)
        {
            var cfg = GetConfig(root);
            if (cfg != null) return cfg.EffectiveIndicatorAnchor;
            return null;
        }

        private float GetGroupWeight(Transform root)
        {
            var cfg = GetConfig(root);
            return (cfg != null) ? cfg.EffectiveWeight : GroupTargetWeight;
        }

        private float GetGroupRadius(Transform root)
        {
            var cfg = GetConfig(root);
            return (cfg != null) ? cfg.EffectiveRadius : GroupTargetRadius;
        }

        private Vector2 GetScreenPosition(Transform root)
        {
            var cfg = GetConfig(root);
            if (cfg != null) return cfg.EffectiveScreenPosition;
            return Vector2.zero;
        }

        private Vector3 GetAimPoint(Transform root)
        {
            Transform a = ResolveLockAnchor(root);
            return (a != null) ? a.position : root.position + Vector3.up * 1.5f;
        }

        private bool IsOccluded(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist <= 0.01f) { hit = default; return false; }
            dir /= dist;
            return Physics.SphereCast(from, 0.2f, dir, out hit, dist,
                OcclusionMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 檢查玩家到目標的垂直角度是否在限制範圍內
        /// 防止鎖定正頭頂或正腳底的目標
        /// </summary>
        private bool IsVerticalAngleValid(Vector3 playerPos, Vector3 targetPos)
        {
            Vector3 delta = targetPos - playerPos;
            float horizontalDist = new Vector2(delta.x, delta.z).magnitude;
            if (horizontalDist < 0.01f) return false; // 幾乎正上方/正下方，拒絕
            float verticalAngle = Mathf.Atan2(delta.y, horizontalDist) * Mathf.Rad2Deg;
            return verticalAngle <= MaxVerticalAngle && verticalAngle >= -MaxDownwardAngle;
        }

        private void SetScreenPos(CinemachineRotationComposer rot, Vector2 sp)
        {
            if (rot == null) return;
            var comp = rot.Composition;
            comp.ScreenPosition = sp;
            rot.Composition = comp;
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            if (!ShowDebugGizmos) return;

            foreach (var info in _debugTargets)
            {
                if (info.IsBlocked)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(info.EyePos, info.HitPoint);
                    Gizmos.DrawSphere(info.HitPoint, 0.1f);
                    Gizmos.color = new Color(1, 0, 0, 0.3f);
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

        #endregion
    }
}
