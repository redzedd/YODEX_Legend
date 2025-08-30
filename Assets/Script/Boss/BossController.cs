using UnityEngine;
using UnityEngine.AI;

public class BossController : MonoBehaviour
{
    public Transform target; // 玩家
    public float detectRange = 10f;
    public float chaseRange = 20f;
    public Animator animator;
    public NavMeshAgent agent;

    [Header("Chase Settings")]
    public float tooCloseDistance = 3f;

    [Header("Attack Settings")]
    public float attackRange = 2.5f;
    public float attackCooldown = 2f;
    public float maxAttackDelay = 2f; // 最多等幾秒後才攻擊
    public float lastAttackTime;
    public BossAttackSet attackSet;
    [HideInInspector] public BossAttackData currentAttackData;

    public Transform[] patrolPoints; // ✨ 新增巡邏點陣列
    public BossPatrolState patrolState; // ✨ 宣告巡邏狀態

    public BossAttackState attackState;
    public BossAttackDecider attackDecider;

    private BossStateMachine stateMachine;

    // 狀態實體
    public BossIdleState idleState;
    public BossAlertState alertState;
    public BossChaseState chaseState;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        // ✅ 關閉自動控制位置與轉向，改由動畫控制
        agent.updatePosition = false;
        agent.updateRotation = false;

        stateMachine = new BossStateMachine();

        // 初始化狀態
        idleState = new BossIdleState(this, stateMachine);
        alertState = new BossAlertState(this, stateMachine);
        chaseState = new BossChaseState(this, stateMachine);
        attackState = new BossAttackState(this, stateMachine);
        patrolState = new BossPatrolState(this, stateMachine); // ✨ 初始化
        attackDecider = new BossAttackDecider(this);

        stateMachine.ChangeState(idleState);
    }

    void Update()
    {
        stateMachine.Update();
    }

    void LateUpdate()
    {
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.nextPosition = transform.position;
        }
    }

    public void FaceTarget()
    {
        Vector3 direction = (target.position - transform.position).normalized;
        direction.y = 0f;
        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, 7.5f * Time.deltaTime);
        }
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying && target == null)
            return;

        // 感知範圍（Alert）
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);

        // 追擊範圍（Chase）
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        // 過近距離（Too Close）
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, tooCloseDistance);

        // 攻擊距離（Attack）
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }

    // 補充面向移動方向的函式
    public void FaceMovementDirection()
    {
        if (agent.desiredVelocity != Vector3.zero)
        {
            Vector3 dir = agent.desiredVelocity.normalized;
            dir.y = 0f;
            Quaternion rot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 3f);
        }
    }
}
