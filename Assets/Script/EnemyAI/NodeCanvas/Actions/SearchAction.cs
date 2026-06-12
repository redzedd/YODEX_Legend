using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — Search 狀態主迴圈，三階段流程：
    /// Phase 1 (RunToPointA) — 跑步到 PointA（丟失視線後 ShortRecordTime 秒記錄的玩家位置）
    ///   - 看到玩家 → AlertSuppression=SkipAll → 直接進 Combat（不播 VFX 不播動畫）
    /// Phase 2 (WalkToPointB) — 走路到 PointB（丟失視線後 LongRecordTime 秒記錄的玩家位置）
    ///   - 進階段時 NotifySearchPhase2Started → AlertSuppression=SkipAnimation
    ///   - 看到玩家 → 播 VFX 跳過動畫 → 進 Combat
    /// Phase 3 (LookAround) — 抵達 PointB 後播放 LookAround 動畫 lookAroundDuration 秒
    ///   - 進階段時 NotifyLookAroundReached → AlertSuppression=None
    ///   - 看到玩家 → 完整 Alert（VFX + 動畫）→ Combat
    ///   - 時間到 → EndAction → FSM On Finish → Idle/Patrol
    ///
    /// 兩個目標點在 OnExecute 時從 EnemyController 拿過來，期間視野偵測由 EnemyController 自己處理
    /// </summary>
    [Category("Enemy AI/Combat")]
    [Name("Search")]
    [Description("搜尋失去目標：跑到 PointA → 走到 PointB → 播放環顧動畫 → 超時返回 Patrol")]
    public class SearchAction : ActionTask<EnemyController>
    {
        [Tooltip("LookAround 動畫播放時長（秒）— 階段 3 持續時間，播完還沒看到玩家就 EndAction。建議 3~8")]
        public float lookAroundDuration = 5f;

        [Tooltip("整個 Search 安全上限時長（秒）— 不管在哪個 Phase，超過此時長強制 EndAction 避免敵人卡住（A* 走不到 PointA/B 等情況）。建議 20~40")]
        public float searchSafetyTimeout = 30f;

        [Tooltip("抵達各階段目標點的判定距離（公尺）— 進入此距離視為「抵達」進入下個階段。建議 0.5~1.5")]
        public float arriveDistance = 1f;

        [Tooltip("階段 1 — 跑步到 PointA 時的動畫（建議 Run，沒有 Run 動畫會 fallback 到 Walk）")]
        public EnemyAnimationType runAnimation = EnemyAnimationType.Run;

        [Tooltip("階段 2 — 走路到 PointB 時的動畫（建議 Walk）")]
        public EnemyAnimationType walkAnimation = EnemyAnimationType.Walk;

        [Tooltip("階段 3 — 環顧時播放的動畫（建議 LookAround，沒設定會 fallback 到 Idle）")]
        public EnemyAnimationType lookAroundAnimation = EnemyAnimationType.LookAround;

        [Tooltip("Console 印 Phase 切換 / 抵達距離（debug 用）— 確認流程時打開，正式版關掉避免噪音")]
        public bool logDebug = false;

        private enum Phase
        {
            RunToPointA,
            WalkToPointB,
            LookAround,
        }

        private Vector3 _pointA;
        private Vector3 _pointB;
        private bool _hasPointB;
        private Phase _phase;
        private float _searchElapsedTime;
        private float _lookAroundElapsedTime;
        private bool _hasCurrentAnim;
        private EnemyAnimationType _currentAnim;

        protected override string info
        {
            get
            {
                if (Application.isPlaying)
                {
                    return $"Search [{_phase}] elapsed {_searchElapsedTime:F1}s";
                }
                return "Search";
            }
        }

        protected override void OnExecute()
        {
            _pointA = agent.HasSearchPointA ? agent.SearchPointA : agent.transform.position;
            _hasPointB = agent.HasSearchPointB;
            _pointB = _hasPointB ? agent.SearchPointB : _pointA;

            _phase = Phase.RunToPointA;
            _searchElapsedTime = 0f;
            _lookAroundElapsedTime = 0f;
            _hasCurrentAnim = false;

            agent.NotifyEnteredSearch();
            agent.MarkUnaware();
            agent.SetDestination(_pointA);

            if (logDebug)
            {
                float distA = Vector3.Distance(agent.transform.position, _pointA);
                float distAB = Vector3.Distance(_pointA, _pointB);
                Debug.Log($"[{agent.name}] Search OnExecute — distToPointA={distA:F2}, |AB|={distAB:F2}, hasPointB={_hasPointB}", agent);
            }
        }

        protected override void OnUpdate()
        {
            _searchElapsedTime += Time.deltaTime;
            if (_searchElapsedTime >= searchSafetyTimeout)
            {
                if (logDebug) Debug.Log($"[{agent.name}] Search safety timeout ({searchSafetyTimeout}s) reached, ending", agent);
                EndAction(true);
                return;
            }

            switch (_phase)
            {
                case Phase.RunToPointA:
                    TickMoveToTarget(_pointA, runAnimation, () =>
                    {
                        if (logDebug) Debug.Log($"[{agent.name}] Search arrived PointA at elapsed {_searchElapsedTime:F1}s", agent);
                        if (_hasPointB && (_pointB - _pointA).sqrMagnitude > arriveDistance * arriveDistance)
                        {
                            EnterPhase2();
                        }
                        else
                        {
                            EnterLookAround();
                        }
                    });
                    break;
                case Phase.WalkToPointB:
                    TickMoveToTarget(_pointB, walkAnimation, EnterLookAround);
                    break;
                case Phase.LookAround:
                    TickLookAround();
                    break;
            }
        }

        protected override void OnStop()
        {
            if (agent != null)
            {
                agent.StopMovement();
                agent.ClearFacingDirection();
                agent.NotifyExitedSearch();
                agent.StopSearchQuestionVfx();
            }
            _hasCurrentAnim = false;
        }

        private void TickMoveToTarget(Vector3 target, EnemyAnimationType moveAnim, System.Action onArrived)
        {
            Vector3 selfPos = agent.transform.position;
            Vector3 toTarget = target - selfPos;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist <= arriveDistance)
            {
                onArrived?.Invoke();
                return;
            }

            EnsureAnimation(moveAnim);
            FaceAlongPath();
        }

        private void EnterPhase2()
        {
            _phase = Phase.WalkToPointB;
            agent.SetDestination(_pointB);
            agent.NotifySearchPhase2Started();
            agent.PlaySearchQuestionVfx();
            if (logDebug) Debug.Log($"[{agent.name}] Search → Phase 2 WalkToPointB", agent);
        }

        private void EnterLookAround()
        {
            _phase = Phase.LookAround;
            agent.StopMovement();
            agent.ClearFacingDirection();
            agent.NotifyLookAroundReached();
            agent.StopSearchQuestionVfx();
            _lookAroundElapsedTime = 0f;
            if (logDebug) Debug.Log($"[{agent.name}] Search → Phase 3 LookAround (elapsed {_searchElapsedTime:F1}s)", agent);
        }

        private void TickLookAround()
        {
            EnsureAnimation(lookAroundAnimation);
            _lookAroundElapsedTime += Time.deltaTime;
            if (_lookAroundElapsedTime >= lookAroundDuration)
            {
                if (logDebug) Debug.Log($"[{agent.name}] LookAround duration ({lookAroundDuration}s) reached, ending Search", agent);
                EndAction(true);
            }
        }

        private void FaceAlongPath()
        {
            Vector3 pathDir = agent.DesiredVelocity;
            pathDir.y = 0f;
            if (pathDir.sqrMagnitude > 0.01f)
            {
                agent.SetFacingDirection(pathDir);
            }
        }

        private void EnsureAnimation(EnemyAnimationType type)
        {
            if (_hasCurrentAnim && _currentAnim == type) return;
            agent.PlayAnimation(type);
            _currentAnim = type;
            _hasCurrentAnim = true;
        }
    }
}
