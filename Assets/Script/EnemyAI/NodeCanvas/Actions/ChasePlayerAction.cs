using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — 持續追擊玩家
    /// 每幀更新 A* 目的地為玩家當前位置，並朝玩家方向轉身
    /// 進入 EnemyConfig.StopDistance 內即停下並切回 Idle，仍持續朝玩家轉身
    /// 玩家離開後自動恢復追擊
    /// </summary>
    [Category("Enemy AI/Combat")]
    [Name("Chase Player")]
    [Description("追擊玩家直到進入 EnemyConfig.StopDistance；停下後仍會持續朝玩家轉身")]
    public class ChasePlayerAction : ActionTask<EnemyController>
    {
        [Tooltip("追擊時使用的動畫類型（建議 Walk 或 Run）")]
        public EnemyAnimationType moveAnimation = EnemyAnimationType.Walk;

        private bool _isMoving;

        protected override string info => $"Chase Player ({moveAnimation})";

        protected override void OnExecute()
        {
            _isMoving = false;
            StartMoving();
        }

        protected override void OnUpdate()
        {
            if (agent.PlayerTransform == null) return;

            float dist = agent.GetDistanceToPlayer();
            float stopDist = agent.Config.StopDistance;

            if (dist > stopDist)
            {
                agent.SetDestination(agent.PlayerTransform.position);
                if (!_isMoving) StartMoving();
            }
            else
            {
                if (_isMoving) StopAtTarget();
            }

            Vector3 dir = agent.GetDirectionToPlayer();
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

        private void StartMoving()
        {
            agent.PlayAnimation(moveAnimation);
            _isMoving = true;
        }

        private void StopAtTarget()
        {
            agent.StopMovement();
            agent.PlayAnimation(EnemyAnimationType.Idle);
            _isMoving = false;
        }
    }
}
