using System.Collections.Generic;
using UnityEngine;
using GAS;

/// <summary>
/// 近戰命中檢測 (混合式終極版：物理 Trigger + 射線補償 + 扇形保底)
/// </summary>
public class MeleeHitScanner : MonoBehaviour
{
    [Header("1. 物理判定 (Souls-like) - 推薦")]
    [Tooltip("請在武器上掛一個 Collider (IsTrigger=true)，並拖曳到這裡。\n這是最穩定的判定方式，不會漏掉體積內的敵人。")]
    public Collider weaponCollider;

    [Header("2. 射線補償 (Anti-Tunneling)")]
    [Tooltip("是否開啟射線補償？用於解決揮刀太快導致 Collider 穿過敵人的問題。")]
    public bool useRaycastCompensation = true;
    public Transform bladeBase; // 刀柄
    public Transform bladeTip;  // 刀尖
    [Range(2, 20)] public int raycastSamples = 8; // 刀身切幾段來掃描 (增加預設值)
    [Tooltip("射線半徑 (使用球體掃描增加判定寬度，建議 0.1~0.3)")]
    public float raycastRadius = 0.15f; // ★ 新增：射線半徑，讓判定變粗
    public LayerMask hurtboxMask;

    [Header("3. 扇形保底 (ZZZ/Action Assist)")]
    [Tooltip("是否開啟扇形保底？只要怪在面前，就算沒碰到也算中。\n適合快節奏爽遊。")]
    public bool useConeFallback = false;
    public float coneRadius = 3.0f;
    [Range(0, 180)] public float coneAngle = 120f;
    public Transform ownerRoot; // 角色根物件 (用來判斷前方)

    [Header("設定")]
    public AttackProfile attackProfile;
    public bool eventLatching = true; // 是否透過 Animation Event 開關
    [Tooltip("是否顯示除錯線條 (紅線=射線軌跡)")]
    public bool debugDraw = true;     // ★ 新增：控制 Debug 顯示

    // --- Runtime ---
    private bool _isHitboxActive = false;
    private HashSet<GameObject> _hitReceiversThisFrame = new HashSet<GameObject>();
    private Dictionary<GameObject, float> _perReceiverHitTimer = new Dictionary<GameObject, float>();

    // 射線補償用的上一幀位置
    private Vector3[] _prevRayPoints;
    private Vector3[] _currRayPoints;

    private void Awake()
    {
        // 初始化射線陣列
        if (raycastSamples < 2) raycastSamples = 2;
        _prevRayPoints = new Vector3[raycastSamples];
        _currRayPoints = new Vector3[raycastSamples];

        // 自動抓取擁有者 (用於扇形判定)
        if (ownerRoot == null)
        {
            var p = GetComponentInParent<GAS.NewGASPlayerController>();
            if (p) ownerRoot = p.transform;
            else ownerRoot = transform.root;
        }

        // 確保 Collider 是 Trigger 且預設關閉
        if (weaponCollider != null)
        {
            weaponCollider.isTrigger = true;
            weaponCollider.enabled = false;
        }
    }

    private void Update()
    {
        // 強制物理同步，確保 Collider 跟隨動畫
        Physics.SyncTransforms();

        if (_isHitboxActive)
        {
            // 先記錄上一幀的位置 (如果是剛啟動的第一幀，會在 Anim_HitboxOn 裡處理好初始化)
            CachePrevPoints();

            // 更新當前位置
            UpdateRaycastPoints();

            // A. 執行射線掃描 (補償快速揮動的空隙)
            if (useRaycastCompensation) PerformRaycastSweep();

            // B. 執行扇形保底 (可選)
            if (useConeFallback) PerformConeCheck();

            // C. 更新無敵幀計時器
            UpdateTimers();
        }
    }

    // =========================================================
    //  核心判定邏輯
    // =========================================================

    // 1. 物理 Trigger 判定 (Unity 內建，最穩)
    private void OnTriggerEnter(Collider other)
    {
        if (!_isHitboxActive) return;
        // 檢查 Layer
        if (((1 << other.gameObject.layer) & hurtboxMask) == 0) return;

        TryRegisterHit(other, other.ClosestPoint(transform.position), (transform.position - other.transform.position).normalized);
    }

    // 2. 射線掃描 (補償穿隧)
    private void PerformRaycastSweep()
    {
        if (bladeBase == null || bladeTip == null) return;

        for (int i = 0; i < raycastSamples; i++)
        {
            Vector3 start = _prevRayPoints[i];
            Vector3 end = _currRayPoints[i];
            Vector3 dir = end - start;
            float dist = dir.magnitude;

            // 只有當移動距離夠大才掃描 (省效能)
            if (dist > 0.001f) // ★ 降低門檻，避免慢動作時漏判
            {
                // ★ 改用 SphereCast (有厚度的射線)，更難漏判
                if (Physics.SphereCast(start, raycastRadius, dir.normalized, out RaycastHit hit, dist, hurtboxMask, QueryTriggerInteraction.Collide))
                {
                    TryRegisterHit(hit.collider, hit.point, hit.normal);
                }

                // ★ 繪製除錯線 (Debug.DrawLine 才能在 Game View / Scene View 即時看到)
                if (debugDraw)
                {
                    Debug.DrawLine(start, end, Color.red, 0.5f); // 保留 0.5 秒以便觀察
                }
            }
        }
    }

    // 3. 扇形保底 (意圖優先)
    private void PerformConeCheck()
    {
        if (ownerRoot == null) return;

        Collider[] cols = Physics.OverlapSphere(ownerRoot.position, coneRadius, hurtboxMask);
        foreach (var col in cols)
        {
            Vector3 dirToTarget = (col.transform.position - ownerRoot.position).normalized;
            // 只看水平角度
            Vector3 flatDir = new Vector3(dirToTarget.x, 0, dirToTarget.z).normalized;
            Vector3 flatFwd = new Vector3(ownerRoot.forward.x, 0, ownerRoot.forward.z).normalized;

            if (Vector3.Angle(flatFwd, flatDir) < coneAngle * 0.5f)
            {
                TryRegisterHit(col, col.bounds.center, -flatFwd);
            }
        }
    }

    // =========================================================
    //  傷害註冊與過濾
    // =========================================================

    private void TryRegisterHit(Collider col, Vector3 hitPoint, Vector3 hitNormal)
    {
        // 1. 找到接收器 (Root / Rigidbody / Component)
        GameObject receiverObj = GetReceiverObject(col);
        if (receiverObj == null) return;

        // 2. 過濾重複命中
        if (!CanRegisterHit(receiverObj)) return;

        // 3. 執行傷害
        var receiver = col.GetComponentInParent<IHitReceiver>();
        if (receiver != null)
        {
            AttackTier tier = attackProfile ? attackProfile.attackTier : AttackTier.Normal;
            HitContext ctx = new HitContext
            {
                damage = attackProfile ? attackProfile.damage : 10f,
                poiseDamage = attackProfile ? attackProfile.poiseDamage : 10f,
                knockbackForce = attackProfile ? attackProfile.knockback : 0f,
                attackTier = tier,
                isHeavyAttack = tier == AttackTier.Heavy,
                hitPoint = hitPoint,
                hitNormal = hitNormal,
                attackDirection = (hitPoint - transform.position).normalized,
                sourceProfile = attackProfile,
                skipHitEffects = attackProfile && attackProfile.skipHitEffects,
                hitStopDuration = attackProfile ? attackProfile.hitStopDuration : 0f,
                cameraShakeIntensity = attackProfile ? attackProfile.cameraShakeIntensity : 0f,
            };
            receiver.OnHit(ref ctx);

            // 觸發命中特效/頓幀 (如果 Profile 有設定)
            // 這裡可以呼叫全域 HitStop.Trigger(...)
        }

        // 4. 記錄命中
        RegisterHit(receiverObj);
    }

    private GameObject GetReceiverObject(Collider col)
    {
        if (col.attachedRigidbody != null) return col.attachedRigidbody.gameObject;
        var receiver = col.GetComponentInParent<IHitReceiver>();
        return receiver != null ? (receiver as MonoBehaviour).gameObject : col.gameObject;
    }

    private bool CanRegisterHit(GameObject target)
    {
        // 攻擊期間單次命中
        if (!attackProfile || !attackProfile.allowMultipleHitsPerTargetInOneActivation)
            return !_hitReceiversThisFrame.Contains(target);

        // 多段判定 (冷卻時間)
        return !_perReceiverHitTimer.TryGetValue(target, out float t) || t <= 0f;
    }

    private void RegisterHit(GameObject target)
    {
        if (!attackProfile || !attackProfile.allowMultipleHitsPerTargetInOneActivation)
            _hitReceiversThisFrame.Add(target);
        else
            _perReceiverHitTimer[target] = attackProfile.perTargetHitCooldown;
    }

    // =========================================================
    //  輔助方法
    // =========================================================

    private void UpdateRaycastPoints()
    {
        if (bladeBase == null || bladeTip == null) return;
        for (int i = 0; i < raycastSamples; i++)
        {
            float t = (float)i / (raycastSamples - 1);
            _currRayPoints[i] = Vector3.Lerp(bladeBase.position, bladeTip.position, t);
        }
    }

    private void CachePrevPoints()
    {
        // 將 Current 複製到 Prev
        for (int i = 0; i < raycastSamples; i++) _prevRayPoints[i] = _currRayPoints[i];
    }

    private void UpdateTimers()
    {
        if (!attackProfile || !attackProfile.allowMultipleHitsPerTargetInOneActivation) return;
        var keys = new List<GameObject>(_perReceiverHitTimer.Keys);
        foreach (var key in keys) _perReceiverHitTimer[key] -= Time.deltaTime;
    }

    // =========================================================
    //  外部呼叫 (Animation Events)
    // =========================================================

    public void Anim_HitboxOn()
    {
        _isHitboxActive = true;
        _hitReceiversThisFrame.Clear();
        _perReceiverHitTimer.Clear();

        // 開啟 Collider
        if (weaponCollider != null) weaponCollider.enabled = true;

        // ★ 關鍵修正：啟動瞬間強制同步位置，避免上一幀的殘留位置導致判定錯誤
        Physics.SyncTransforms();

        // 立即更新當前點位
        UpdateRaycastPoints();

        // ★ 關鍵：將「上一幀」的點位也設為「當前」點位
        // 這樣第一幀的移動量為 0，不會產生從遠處拉過來的錯誤紅線
        // 真正的判定會從 Update 的下一幀開始（那時就會有位移了）
        CachePrevPoints();
    }

    public void Anim_HitboxOff()
    {
        _isHitboxActive = false;

        // 關閉 Collider
        if (weaponCollider != null) weaponCollider.enabled = false;
    }

    // =========================================================
    //  Debug Gizmos
    // =========================================================
    private void OnDrawGizmos()
    {
        if (!_isHitboxActive) return;

        // 畫扇形
        if (useConeFallback && ownerRoot)
        {
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Vector3 left = Quaternion.Euler(0, -coneAngle / 2, 0) * ownerRoot.forward;
            Vector3 right = Quaternion.Euler(0, coneAngle / 2, 0) * ownerRoot.forward;
            Gizmos.DrawLine(ownerRoot.position, ownerRoot.position + left * coneRadius);
            Gizmos.DrawLine(ownerRoot.position, ownerRoot.position + right * coneRadius);
        }
    }
}