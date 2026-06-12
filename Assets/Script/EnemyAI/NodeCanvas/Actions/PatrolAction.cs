using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — 持續性巡邏行為
    /// 邏輯：找最近路徑點出發 → 走到該點 → 站著等 N 秒 → 走下一點 → 循環
    /// 走動時播 Walk 動畫，等待時播 Idle 動畫；OnStop 自動停下並切回 Idle
    /// </summary>
    [Category("Enemy AI/Movement")]
    [Name("Patrol")]
    [Description("讓敵人循環巡邏 EnemyController.PatrolPoints 中的路徑點，到點後等待指定秒數再前往下一個")]
    public class PatrolAction : ActionTask<EnemyController>
    {
        [Tooltip("到達路徑點後等待的秒數 — 建議 1.5~3")]
        public float waitAtPoint = 2f;

        [Tooltip("判定為「已到達」的距離（公尺）— 過小會卡在路徑點旁不切換")]
        public float arriveDistance = 0.5f;

        [Tooltip("行走時播放的動畫類型（建議 Walk）")]
        public EnemyAnimationType walkAnimation = EnemyAnimationType.Walk;

        [Tooltip("抵達路徑點等待時播放的動畫類型（建議 PatrolWait，沒設定的話 fallback 到 Idle）")]
        public EnemyAnimationType waitAnimation = EnemyAnimationType.PatrolWait;

        private int _currentIndex;
        private float _waitTimer;
        private bool _isWaiting;

        protected override string info => $"Patrol (wait {waitAtPoint}s)";

        protected override void OnExecute()
        {
            if (agent.PatrolPoints == null || agent.PatrolPoints.Length == 0)
            {
                Debug.LogWarning($"[{agent.name}] PatrolAction：未設定 PatrolPoints，無法巡邏", agent);
                EndAction(false);
                return;
            }
            _currentIndex = FindClosestPoint();
            _isWaiting = false;
            _waitTimer = 0f;
            StartWalkingToCurrent();
        }

        protected override void OnUpdate()
        {
            if (agent.PatrolPoints == null || agent.PatrolPoints.Length == 0) return;

            Transform target = agent.PatrolPoints[_currentIndex];
            if (target == null)
            {
                AdvanceIndex();
                StartWalkingToCurrent();
                return;
            }

            if (_isWaiting)
            {
                _waitTimer -= Time.deltaTime;
                if (_waitTimer <= 0f)
                {
                    _isWaiting = false;
                    AdvanceIndex();
                    StartWalkingToCurrent();
                }
                return;
            }

            float dist = Vector3.Distance(agent.transform.position, target.position);
            if (dist <= arriveDistance)
            {
                agent.StopMovement();
                agent.PlayAnimation(waitAnimation);
                _isWaiting = true;
                _waitTimer = waitAtPoint;
                return;
            }

            Vector3 dir = agent.DesiredVelocity;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                agent.SetFacingDirection(dir);
            }
        }

        protected override void OnStop()
        {
            if (agent == null) return;
            agent.StopMovement();
            agent.ClearFacingDirection();
        }

        private void StartWalkingToCurrent()
        {
            Transform target = agent.PatrolPoints[_currentIndex];
            if (target == null) return;
            agent.SetDestination(target.position);
            agent.PlayAnimation(walkAnimation);
        }

        private void AdvanceIndex()
        {
            _currentIndex = (_currentIndex + 1) % agent.PatrolPoints.Length;
        }

        private int FindClosestPoint()
        {
            int closest = 0;
            float minSqr = float.MaxValue;
            Vector3 self = agent.transform.position;
            for (int i = 0; i < agent.PatrolPoints.Length; i++)
            {
                Transform p = agent.PatrolPoints[i];
                if (p == null) continue;
                float d = (p.position - self).sqrMagnitude;
                if (d < minSqr)
                {
                    minSqr = d;
                    closest = i;
                }
            }
            return closest;
        }
    }
}
