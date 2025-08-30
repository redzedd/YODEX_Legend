using UnityEngine;
using Unity.Cinemachine;

public class LockOnManager : MonoBehaviour
{
    [Header("UI / Indicator")]
    public GameObject indicatorPrefab;
    public Camera mainCamera;

    [Header("Cameras")]
    public CinemachineCamera thirdPersonCam;
    public CinemachineCamera lockOnCamA;
    public CinemachineCamera lockOnCamB;

    [Header("Target Groups")]
    public CinemachineTargetGroup targetGroupA;
    public CinemachineTargetGroup targetGroupB;

    [Header("Targets / Player Anchor")]
    public Transform playerCenterTransform;  // 玩家相機定位點（胸口/頭上）

    [Header("Ranges")]
    public float screenMargin = 0.2f;
    public float nearPriorityRange = 4f;
    public float hardUnlockRangeMul = 1.5f;

    [Header("Occlusion")]
    public LayerMask occlusionMask;
    public float occludedGraceTime = 0.35f;
    public float indicatorHeight = 4.5f;

    [Header("Camera Taste (optional)")]
    public float lockFOV = 55f;   // <0 = 不動
    public float thirdFOV = 60f;  // <0 = 不動
    public float groupPlayerWeight = 1f;
    public float groupPlayerRadius = 1.2f;
    public float groupTargetWeight = 1.0f;
    public float groupTargetRadius = 1.0f;

    private GameObject currentIndicator;

    // 目前鎖定對象
    private Transform currentEnemyRoot;
    private Transform currentEnemyAnchor;
    private Transform currentIndicatorAnchor;
    private EnemyLockOnConfig currentConfig;

    private float occludedTimer = 0f;
    private bool isLocked = false;
    public bool IsLocked => isLocked;

    // 只有在目標還存在 & 啟用時才回傳，否則回傳 null
    public Transform CurrentTarget
    {
        get
        {
            if (currentEnemyRoot != null && currentEnemyRoot.gameObject.activeInHierarchy)
                return currentEnemyRoot;
            return null;
        }
    }

    // 0=A, 1=B, -1=未鎖定
    private int activeLockIdx = -1;

    private CinemachineCamera ActiveLockCam => activeLockIdx == 0 ? lockOnCamA : lockOnCamB;
    private CinemachineCamera InactiveLockCam => activeLockIdx == 0 ? lockOnCamB : lockOnCamA;
    private CinemachineTargetGroup ActiveGroup => activeLockIdx == 0 ? targetGroupA : targetGroupB;
    private CinemachineTargetGroup InactiveGroup => activeLockIdx == 0 ? targetGroupB : targetGroupA;

    // ★ 抓兩支 LockOn 的 RotationComposer，方便設定 ScreenPosition
    private CinemachineRotationComposer _rotA;
    private CinemachineRotationComposer _rotB;

    private void Awake()
    {
        // 綁 Follow / LookAt
        if (lockOnCamA != null)
        {
            if (playerCenterTransform != null) lockOnCamA.Follow = playerCenterTransform;
            if (targetGroupA != null) lockOnCamA.LookAt = targetGroupA.transform;
            _rotA = lockOnCamA.GetComponent<CinemachineRotationComposer>();   // ★
        }
        if (lockOnCamB != null)
        {
            if (playerCenterTransform != null) lockOnCamB.Follow = playerCenterTransform;
            if (targetGroupB != null) lockOnCamB.LookAt = targetGroupB.transform;
            _rotB = lockOnCamB.GetComponent<CinemachineRotationComposer>();   // ★
        }
        if (thirdPersonCam != null && playerCenterTransform != null)
        {
            thirdPersonCam.Follow = playerCenterTransform;
            thirdPersonCam.LookAt = playerCenterTransform;
        }

        // 初始化優先權
        SetLockOnPriorities(-1);
        if (thirdFOV > 0f && thirdPersonCam != null)
            thirdPersonCam.Lens.FieldOfView = thirdFOV;

        // 兩個 Group 清空，只放玩家
        ResetGroup(targetGroupA, null, 0, 0, clearAll: true);
        ResetGroup(targetGroupB, null, 0, 0, clearAll: true);
        EnsurePlayerMember(targetGroupA);
        EnsurePlayerMember(targetGroupB);

        // ★ 安全：開場就把 ScreenPosition 歸零
        SetScreenPos(_rotA, Vector2.zero);
        SetScreenPos(_rotB, Vector2.zero);
    }

    // ===== 外部入口 =====
    public void TryLockOn(float range, float nearPriorityRangeFromPlayer, Transform origin)
    {
        nearPriorityRange = nearPriorityRangeFromPlayer;
        SetTarget(SelectBestTarget(range, origin));
    }

    public void ClearLockOn()
    {
        isLocked = false;

        if (currentIndicator != null) { Destroy(currentIndicator); currentIndicator = null; }

        // 清兩個 Group 的敵人成員（只留玩家）
        ResetGroup(targetGroupA, null, 0, 0, clearAll: true); EnsurePlayerMember(targetGroupA);
        ResetGroup(targetGroupB, null, 0, 0, clearAll: true); EnsurePlayerMember(targetGroupB);

        currentEnemyRoot = null;
        currentEnemyAnchor = null;
        currentIndicatorAnchor = null;
        currentConfig = null;
        occludedTimer = 0f;

        // 關閉 LockOn，切回第三人稱
        activeLockIdx = -1;
        SetLockOnPriorities(-1);
        if (thirdFOV > 0f && thirdPersonCam != null)
            thirdPersonCam.Lens.FieldOfView = thirdFOV;

        // ★ 退鎖時自動把 ScreenPosition 設回 0,0
        SetScreenPos(_rotA, Vector2.zero);
        SetScreenPos(_rotB, Vector2.zero);
    }

    public void TickMaintain(float playerToTargetMaxRange, Transform player)
    {
        if (!isLocked) return;

        if (currentEnemyRoot == null || currentEnemyRoot.gameObject == null || !currentEnemyRoot.gameObject.activeInHierarchy)
        {
            if (!TryFallbackOrUnlock(playerToTargetMaxRange, player)) return;
        }

        float dist = Vector3.Distance(player.position, currentEnemyRoot.position);
        if (dist > playerToTargetMaxRange * hardUnlockRangeMul)
        {
            ClearLockOn(); return;
        }

        bool occluded = IsOccluded(playerCenterTransform.position, GetAimPoint(currentEnemyRoot), out _);
        if (occluded)
        {
            occludedTimer += Time.deltaTime;
            if (occludedTimer >= occludedGraceTime)
            {
                if (!TryFallbackOrUnlock(playerToTargetMaxRange, player)) return;
            }
        }
        else occludedTimer = 0f;

        if (currentIndicator != null)
        {
            Vector3 worldPos = (currentIndicatorAnchor != null)
                ? currentIndicatorAnchor.position
                : currentEnemyRoot.position + Vector3.up * indicatorHeight;

            Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPos);
            currentIndicator.transform.position = screenPos;
            currentIndicator.SetActive(screenPos.z >= 0f);
        }
    }

    // ===== 設定 / 切換 目標 =====
    public void SetTarget(Transform targetRoot)
    {
        if (targetRoot == null) { ClearLockOn(); return; }

        var newAnchor = ResolveLockAnchor(targetRoot);
        var indicatorAnchor = ResolveIndicatorAnchor(targetRoot);

        // 更新狀態
        isLocked = true;
        currentEnemyRoot = targetRoot;
        currentEnemyAnchor = newAnchor;
        currentIndicatorAnchor = indicatorAnchor;
        currentConfig = targetRoot.GetComponent<EnemyLockOnConfig>();
        occludedTimer = 0f;

        if (currentIndicator == null && indicatorPrefab != null)
            currentIndicator = Instantiate(indicatorPrefab, transform);
        if (currentIndicator != null) currentIndicator.SetActive(true);

        // 讀這隻敵人的畫面構圖位置（沒有就 0,0）
        Vector2 sp = GetScreenPosition(targetRoot);   // ★

        // 第一次進入：開 A（或 B）
        if (activeLockIdx == -1)
        {
            activeLockIdx = 0; // 先用 A
            ResetGroup(targetGroupA, newAnchor != null ? newAnchor : targetRoot, GetGroupWeight(targetRoot), GetGroupRadius(targetRoot), clearAll: true);
            EnsurePlayerMember(targetGroupA);

            // ★ 套用這隻敵人的 ScreenPosition 到「現役」相機
            SetScreenPos(_rotA, sp);

            SetLockOnPriorities(0); // Third 0, A 2, B 0
            if (lockFOV > 0f && lockOnCamA != null) lockOnCamA.Lens.FieldOfView = lockFOV;
            return;
        }

        // 切換：先把「非現役」那組設成新目標，再把 Priority 交給它（由 Blender 平滑）
        var inactiveGroup = InactiveGroup;
        ResetGroup(inactiveGroup, newAnchor != null ? newAnchor : targetRoot, GetGroupWeight(targetRoot), GetGroupRadius(targetRoot), clearAll: true);
        EnsurePlayerMember(inactiveGroup);

        // ★ 重要：在「非現役」相機上預先套用 ScreenPosition，Blend 過去會直接到正確構圖
        if (activeLockIdx == 0) SetScreenPos(_rotB, sp);
        else SetScreenPos(_rotA, sp);

        PromoteInactiveLockCam();
        activeLockIdx = 1 - activeLockIdx;
    }

    // ====== 優先權切換（-1=全關→第三人稱；0=用A；1=用B） ======
    private void SetLockOnPriorities(int useIdx)
    {
        if (thirdPersonCam != null) thirdPersonCam.Priority = (useIdx == -1) ? 1 : 0;
        if (lockOnCamA != null) lockOnCamA.Priority = (useIdx == 0) ? 2 : 0;
        if (lockOnCamB != null) lockOnCamB.Priority = (useIdx == 1) ? 2 : 0;
    }

    private void PromoteInactiveLockCam()
    {
        if (InactiveLockCam == null || ActiveLockCam == null) return;
        if (lockFOV > 0f) InactiveLockCam.Lens.FieldOfView = lockFOV;
        InactiveLockCam.Priority = 2;
        ActiveLockCam.Priority = 1;
    }

    // ====== Group 相關 ======
    private void ResetGroup(CinemachineTargetGroup group, Transform enemyAnchor, float enemyWeight, float enemyRadius, bool clearAll)
    {
        if (group == null) return;

        var list = group.Targets;
        if (clearAll) list.Clear();

        // 玩家
        if (playerCenterTransform != null)
        {
            if (group.FindMember(playerCenterTransform) < 0)
                group.AddMember(playerCenterTransform, groupPlayerWeight, groupPlayerRadius);
            else
                UpdateMember(group, playerCenterTransform, groupPlayerWeight, groupPlayerRadius);
        }

        // 敵人
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
        if (group == null || playerCenterTransform == null) return;
        if (group.FindMember(playerCenterTransform) < 0)
            group.AddMember(playerCenterTransform, groupPlayerWeight, groupPlayerRadius);
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

    // ====== 目標搜尋（沿用你的邏輯） ======
    private Transform SelectBestTarget(float range, Transform origin)
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        if (enemies == null || enemies.Length == 0) return null;

        Transform bestNear = null; float bestNearDist = Mathf.Infinity;
        Transform bestScreen = null; float bestScreenScore = Mathf.Infinity;

        foreach (var e in enemies)
        {
            if (e == null || !e.activeInHierarchy) continue;

            Transform root = e.transform;
            Transform anchor = ResolveLockAnchor(root);

            float worldDist = Vector3.Distance(origin.position, root.position);
            if (worldDist > range) continue;

            Vector3 vp = mainCamera.WorldToViewportPoint(anchor.position);
            if (vp.z <= 0f || vp.x < -screenMargin || vp.x > 1f + screenMargin || vp.y < -screenMargin || vp.y > 1f + screenMargin)
                continue;

            if (IsOccluded(playerCenterTransform.position, anchor.position, out _))
                continue;

            if (worldDist <= nearPriorityRange && worldDist < bestNearDist)
            { bestNearDist = worldDist; bestNear = root; }

            Vector2 center = new Vector2(0.5f, 0.5f);
            float screenDist = Vector2.Distance(center, new Vector2(vp.x, vp.y));
            float score = screenDist * 1.0f + Mathf.Clamp01(worldDist / range) * 0.35f;
            if (score < bestScreenScore) { bestScreenScore = score; bestScreen = root; }
        }

        return bestNear != null ? bestNear : bestScreen;
    }

    public Transform SelectSiblingTarget(bool toRight, float range, Transform player, Transform current, float nearPriorityRangeLocal)
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        if (enemies == null || enemies.Length == 0 || current == null) return null;

        Transform curAnchor = ResolveLockAnchor(current);
        Vector3 curVP3 = mainCamera.WorldToViewportPoint(curAnchor.position);

        Transform bestNear = null; float bestNearDist = Mathf.Infinity;
        float bestX = toRight ? -Mathf.Infinity : Mathf.Infinity;
        float bestCenterDist = Mathf.Infinity;
        Transform bestScreen = null;

        foreach (var e in enemies)
        {
            Transform root = e.transform;
            if (root == current) continue;

            Transform anchor = ResolveLockAnchor(root);

            float worldDist = Vector3.Distance(player.position, root.position);
            if (worldDist > range) continue;

            Vector3 vp = mainCamera.WorldToViewportPoint(anchor.position);
            if (vp.z <= 0f) continue;

            bool isRight = vp.x > curVP3.x;
            if ((toRight && !isRight) || (!toRight && isRight)) continue;

            if (vp.x < -screenMargin || vp.x > 1f + screenMargin || vp.y < -screenMargin || vp.y > 1f + screenMargin)
                continue;
            if (IsOccluded(playerCenterTransform.position, anchor.position, out _))
                continue;

            if (worldDist <= nearPriorityRangeLocal && worldDist < bestNearDist)
            { bestNearDist = worldDist; bestNear = root; continue; }

            Vector2 center = new Vector2(0.5f, 0.5f);
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

    // ====== 敵人資料存取 ======
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
        return (cfg != null) ? cfg.EffectiveWeight : groupTargetWeight;
    }

    private float GetGroupRadius(Transform root)
    {
        var cfg = GetConfig(root);
        return (cfg != null) ? cfg.EffectiveRadius : groupTargetRadius;
    }

    // ★ 讀取這隻敵人要的 ScreenPosition（若沒定義就 0,0）
    private Vector2 GetScreenPosition(Transform root)
    {
        var cfg = GetConfig(root);
        if (cfg != null) return cfg.EffectiveScreenPosition;  // 需要你在 EnemyLockOnConfig 加上該屬性
        return Vector2.zero;
    }

    private Vector3 GetAimPoint(Transform root)
    {
        Transform a = ResolveLockAnchor(root);
        return (a != null) ? a.position : root.position + Vector3.up * 1.5f;
    }

    private bool IsOccluded(Vector3 from, Vector3 to, out RaycastHit hit)
    {
        Vector3 dir = to - from; float dist = dir.magnitude;
        if (dist <= 0.01f) { hit = default; return false; }
        dir /= dist;
        return Physics.SphereCast(from, 0.2f, dir, out hit, dist, occlusionMask, QueryTriggerInteraction.Ignore);
    }

    private bool TryFallbackOrUnlock(float range, Transform origin)
    {
        Transform fallback = SelectBestTarget(range, origin);
        if (fallback != null) { SetTarget(fallback); return true; }
        ClearLockOn(); return false;
    }

    // ★ 小工具：設置相機 RotationComposer 的 ScreenPosition
    private void SetScreenPos(Unity.Cinemachine.CinemachineRotationComposer rot, Vector2 sp)
    {
        if (rot == null) return;

        // 修改 struct 後再整個回寫，避免不同版本/IL2CPP 下的寫入問題
        var comp = rot.Composition;
        comp.ScreenPosition = sp;
        rot.Composition = comp;
    }
}