using System;
using System.Collections;
using System.Collections.Generic;
using Animancer;
using GAS;
using GAS.Targeting;
using UnityEngine;

namespace Enemy.AttackSystem
{
    /// <summary>
    /// 敵人攻擊執行器。
    /// 給定一份 EnemyAttackProfile，負責播放動畫並依時序廣播事件
    /// （攻擊開始、可招架窗開/關、判定窗開/關、攻擊結束、攻擊被取消）。
    /// </summary>
    [RequireComponent(typeof(AnimancerComponent))]
    public class EnemyAttackExecutor : MonoBehaviour
    {
        // ────── Inspector 設定 ──────
        [Header("元件引用")]

        [SerializeField]
        [Tooltip("Animancer 元件。留空會在 Awake 自動抓取")]
        private AnimancerComponent _animancer;

        [Header("除錯")]

        [SerializeField]
        [Tooltip("勾選後會在 Console 印出每個時間點的事件，方便除錯")]
        private bool _logEvents = true;

        // ────── 私有狀態 ──────
        private EnemyAttackProfile _currentProfile;
        private Coroutine _runningRoutine;
        private float _elapsedTime;
        private AnimancerState _currentAnimState;
        private Transform _cachedHitboxBone;
        private string _cachedHitboxBoneName;
        private GameObject _hitConfirmedTarget;
        private Vector3 _previousHitboxCenter;
        private Quaternion _previousHitboxRotation;
        private bool _hasPreviousHitboxState;
        // 自身樹的根 — Hitbox 排除自己用。Awake fallback = transform，EnemyController 會在 Awake 內呼叫 SetOwnerRoot 覆寫
        // 用意：Executor 放在子物件時，父物件的 CharacterController 也必須被排除，不能只看 IsChildOf(transform)
        private Transform _ownerRoot;
        // 快取 EnemyController（_ownerRoot 上的） — ManualLerp 位移需要透過它走 CC.Move + 重力 + A* 同步
        private EnemyAI.EnemyController _cachedEnemyController;
        // VFX 事件是否已觸發 — 攻擊開始時依 profile.VfxEvents.Count 重新配置 / reset，迴圈內逐一檢查跨過 event.Time
        private bool[] _vfxFired;
        private static readonly Collider[] _overlapBuffer = new Collider[16];
        private static readonly RaycastHit[] _sweepBuffer = new RaycastHit[16];

        // ────── 對外事件 ──────
        // 所有事件統一回傳 (executor, profile)，方便訂閱者識別來源
        public event Action<EnemyAttackExecutor, EnemyAttackProfile> OnAttackStart;
        public event Action<EnemyAttackExecutor, EnemyAttackProfile> OnAttackEnd;
        public event Action<EnemyAttackExecutor, EnemyAttackProfile> OnAttackCanceled;
        public event Action<EnemyAttackExecutor, EnemyAttackProfile> OnParryWindowOpen;
        public event Action<EnemyAttackExecutor, EnemyAttackProfile> OnParryWindowClose;
        public event Action<EnemyAttackExecutor, EnemyAttackProfile> OnHitWindowOpen;
        public event Action<EnemyAttackExecutor, EnemyAttackProfile> OnHitWindowClose;
        // hitbox 真的打到玩家時觸發（OverlapBox 命中），參數 (executor, profile, hit GameObject)
        // 跟 OnHitWindowOpen 差異：HitWindowOpen 是時間軸事件，HitConfirmed 是空間碰撞事件
        public event Action<EnemyAttackExecutor, EnemyAttackProfile, GameObject> OnHitConfirmed;

        // ────── 對外狀態查詢 ──────
        public bool IsAttacking => _runningRoutine != null;
        public EnemyAttackProfile CurrentProfile => _currentProfile;
        public float ElapsedTime => _elapsedTime;
        // 攻擊開始那一瞬間敵人的位置與面向，給玩家招架預測「HitStart 那刻位置」用
        public Vector3 AttackStartPosition { get; private set; }
        public Vector3 AttackStartForward { get; private set; }
        public bool IsInParryWindow => IsAttacking
            && _currentProfile != null
            && _currentProfile.IsInParryWindow(_elapsedTime);

        // ────── Unity 生命週期 ──────
        private void Awake()
        {
            if (_animancer == null)
            {
                _animancer = GetComponent<AnimancerComponent>();
            }
            if (_ownerRoot == null)
            {
                _ownerRoot = transform;
            }
        }

        /// <summary>
        /// 由 EnemyController.Awake 呼叫 — 設定「自身樹的根」，Hitbox 命中判定用此排除自己。
        /// Executor 跟 EnemyController 分屬父子物件時必須呼叫，不然父物件上的 CharacterController 會被誤判為攻擊目標
        /// </summary>
        public void SetOwnerRoot(Transform root)
        {
            _ownerRoot = root != null ? root : transform;
            _cachedEnemyController = null;
        }

        private void OnDisable()
        {
            // GameObject 被停用時要清掉 coroutine 與事件殘留，避免下次啟用時狀態異常
            if (IsAttacking)
            {
                Cancel();
            }
        }

        private void OnDestroy()
        {
            // 兜底：避免敵人被銷毀時還掛在 Registry 裡，造成空引用
            ParryableTargetRegistry.Unregister(this);
        }

#if UNITY_EDITOR
        // 攻擊期間在 Scene 視窗顯示 Hitbox（橘 = HitWindow 未開、紅 = HitWindow 開、綠 = 已命中）
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || _currentProfile == null)
            {
                return;
            }
            // 遠程招沒有近戰 Hitbox — 改畫子彈發射點與射出方向
            if (_currentProfile.IsRanged)
            {
                DrawProjectileAimGizmo(_currentProfile);
                return;
            }
            Transform bone = ResolveHitboxBone(_currentProfile);
            if (bone == null)
            {
                return;
            }
            Vector3 center = bone.position + bone.rotation * _currentProfile.HitboxOffset;
            Quaternion gizmoRot = bone.rotation * Quaternion.Euler(_currentProfile.HitboxRotation);
            Matrix4x4 prev = Gizmos.matrix;
            Color prevColor = Gizmos.color;
            Color hitColor = _hitConfirmedTarget != null
                ? new Color(0f, 1f, 0f, 0.6f)
                : new Color(1f, 0.4f, 0f, 0.5f);
            Gizmos.matrix = Matrix4x4.TRS(center, gizmoRot, Vector3.one);
            Gizmos.color = hitColor;
            Gizmos.DrawWireCube(Vector3.zero, _currentProfile.HitboxSize);
            // 額外 hitboxes — 用稍淺色
            IReadOnlyList<EnemyAttackHitboxData> extras = _currentProfile.ExtraHitboxes;
            if (extras != null)
            {
                for (int i = 0; i < extras.Count; i++)
                {
                    EnemyAttackHitboxData hb = extras[i];
                    if (hb == null) continue;
                    Transform hbBone = string.IsNullOrEmpty(hb.Bone)
                        ? transform
                        : (FindChildRecursive(transform, hb.Bone) ?? transform);
                    Vector3 hbCenter = hbBone.position + hbBone.rotation * hb.Offset;
                    Quaternion hbRot = hbBone.rotation * Quaternion.Euler(hb.Rotation);
                    Gizmos.matrix = Matrix4x4.TRS(hbCenter, hbRot, Vector3.one);
                    Gizmos.color = new Color(hitColor.r, hitColor.g, hitColor.b, hitColor.a * 0.6f);
                    Gizmos.DrawWireCube(Vector3.zero, hb.Size);
                }
            }
            Gizmos.matrix = prev;
            Gizmos.color = prevColor;
        }

        // 遠程招在 Scene 視窗顯示子彈發射點 + 射出方向（Forward 套用角度偏移；朝玩家模式畫實際瞄準方向）
        private void DrawProjectileAimGizmo(EnemyAttackProfile profile)
        {
            Transform spawnBone = ResolveSpawnBone(profile.ProjectileSpawnBone);
            if (spawnBone == null) return;
            Vector3 spawnPos = spawnBone.position + spawnBone.rotation * profile.ProjectileSpawnOffset;
            Vector3 dir;
            if (profile.RangedAimMode == RangedAimMode.Forward)
            {
                dir = spawnBone.rotation * Quaternion.Euler(profile.ProjectileForwardAngles) * Vector3.forward;
            }
            else
            {
                Transform playerRoot = ResolvePlayerTransform();
                dir = playerRoot != null
                    ? (AimAnchorResolver.ResolveAimPosition(playerRoot) - spawnPos)
                    : spawnBone.forward;
            }
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            Color prevColor = Gizmos.color;
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
            Gizmos.DrawSphere(spawnPos, 0.08f);
            Gizmos.DrawRay(spawnPos, dir * 2.5f);
            Gizmos.color = prevColor;
        }
#endif

        // ────── 公開 API ──────
        /// <summary>
        /// 開始執行一次攻擊。若目前正在攻擊中會被拒絕並回傳 false。
        /// </summary>
        public bool Execute(EnemyAttackProfile profile)
        {
            if (profile == null)
            {
                Debug.LogWarning("[攻擊執行器] 嘗試執行空的攻擊資料", this);
                return false;
            }
            if (IsAttacking)
            {
                Debug.LogWarning($"[攻擊執行器] 攻擊「{_currentProfile.AttackName}」尚未結束，無法觸發新攻擊", this);
                return false;
            }
            _runningRoutine = StartCoroutine(RunAttack(profile));
            return true;
        }

        /// <summary>
        /// 強制中斷目前攻擊（玩家招架成功、敵人被打斷時呼叫）。
        /// </summary>
        public void Cancel()
        {
            if (!IsAttacking)
            {
                return;
            }
            StopCoroutine(_runningRoutine);
            _runningRoutine = null;
            EnemyAttackProfile canceled = _currentProfile;
            _currentProfile = null;
            _currentAnimState = null;
            _elapsedTime = 0f;
            _hitConfirmedTarget = null;
            _hasPreviousHitboxState = false;
            ParryableTargetRegistry.Unregister(this);
            OnAttackCanceled?.Invoke(this, canceled);
            if (_logEvents)
            {
                Debug.Log($"[攻擊執行器] 攻擊「{canceled.AttackName}」被取消", this);
            }
        }

        // ────── 核心時序 ──────
        // 雙軌時間追蹤：
        // - playbackTime（實時間）：用於黃光熄滅，黃光時長固定不受動畫速度影響
        // - animTime（動畫時間，受 Speed 影響）：用於 HitStart / HitEnd，動畫真的到 HitStart 才砍
        // 動畫減速規則：若 HitStart < ParryWindowDuration（招架窗總時長），動畫前段減速使其等於招架窗
        // 玩家接刀（呼叫 RestoreNormalAnimSpeed）→ 動畫即時恢復 1x，HitStart 很快觸發
        private IEnumerator RunAttack(EnemyAttackProfile profile)
        {
            _currentProfile = profile;
            _elapsedTime = 0f;
            // 用 owner root 的位置/朝向當基準 — executor 在子物件時，root 才是「敵人」的真正朝向
            // 影響：DefensiveAssistResponder 招架瞬移預測 + ManualLerp 推進方向 都會更準
            Transform reference = _ownerRoot != null ? _ownerRoot : transform;
            AttackStartPosition = reference.position;
            AttackStartForward = reference.forward;
            // 計算初始動畫速度：HitStart 早於招架窗 → 減速使 HitStart 對應到招架窗結束時刻
            float parryWindow = profile.ParryWindowDuration;
            float initialAnimSpeed = 1f;
            if (profile.HitStart > 0f && profile.HitStart < parryWindow)
            {
                initialAnimSpeed = profile.HitStart / parryWindow;
            }
            if (profile.AnimationClip == null || _animancer == null)
            {
                Debug.LogWarning($"[攻擊執行器] 攻擊「{profile.AttackName}」沒有 AnimationClip 或 Animancer，直接結束", this);
                _currentProfile = null;
                _runningRoutine = null;
                yield break;
            }
            AnimancerState animState = _animancer.Play(profile.AnimationClip, profile.EntryFadeDuration);
            animState.Time = 0f;
            animState.Speed = initialAnimSpeed;
            _currentAnimState = animState;
            int vfxCount = profile.VfxEvents != null ? profile.VfxEvents.Count : 0;
            ResetVfxFiredFlags(vfxCount);
            OnAttackStart?.Invoke(this, profile);
            LogEvent(initialAnimSpeed < 1f
                ? $"攻擊開始（動畫減速 {initialAnimSpeed:F2}x，前段拉長至 {parryWindow:F2}s）"
                : "攻擊開始", profile);
            // 開頭立刻觸發黃光，Register Registry — 玩家從攻擊一開始就能按招架
            bool parryOpened = false;
            if (profile.IsParryable)
            {
                parryOpened = true;
                ParryableTargetRegistry.Register(this);
                OnParryWindowOpen?.Invoke(this, profile);
                LogEvent("可招架窗開啟（黃光亮）", profile);
            }
            bool parryClosed = false;
            bool hitOpened = false;
            bool hitClosed = false;
            float playbackTime = 0f;
            float animTime = 0f;
            // 動畫播完即退出（NormalizedTime 達到 1）— 攻擊總時長以動畫長度為準
            while (animState.NormalizedTime < 1f)
            {
                playbackTime += Time.deltaTime;
                float currentSpeed = (animState != null) ? animState.Speed : 1f;
                animTime += Time.deltaTime * currentSpeed;
                _elapsedTime = playbackTime;
                if (parryOpened && !parryClosed && playbackTime >= profile.ParryFlashDuration)
                {
                    parryClosed = true;
                    OnParryWindowClose?.Invoke(this, profile);
                    LogEvent("黃光熄滅（仍可招架直到攻擊判定）", profile);
                }
                if (!hitOpened && animTime >= profile.HitStart)
                {
                    hitOpened = true;
                    if (parryOpened)
                    {
                        ParryableTargetRegistry.Unregister(this);
                    }
                    // HitStart 觸發後恢復 1x — 否則 HitStart 之後的動畫會繼續慢動作播完
                    if (animState != null && animState.Speed < 1f)
                    {
                        animState.Speed = 1f;
                    }
                    OnHitWindowOpen?.Invoke(this, profile);
                    LogEvent("判定窗開啟（招架窗結束）", profile);
                    // 遠程攻擊：HitStart 即發射一次投射物（不再做 Hitbox 偵測）
                    if (profile.IsRanged)
                    {
                        FireProjectile(profile);
                    }
                }
                if (hitOpened && !hitClosed && animTime >= profile.HitEnd)
                {
                    hitClosed = true;
                    OnHitWindowClose?.Invoke(this, profile);
                    LogEvent("判定窗關閉", profile);
                }
                // 每幀檢查所有 hitbox（主 + 額外）— 各自的時間範圍由內部 gate；遠程攻擊整段跳過
                if (!profile.IsRanged && _hitConfirmedTarget == null)
                {
                    CheckHitOverlapAll(profile, animTime);
                }
                // ManualLerp 位移：每幀沿攻擊起始 forward 等速推進。動畫應該是「原地揮砍」型（無 root motion）
                if (profile.MoveType == AttackMoveType.ManualLerp
                    && profile.MoveDistance > 0f
                    && profile.Duration > 0f)
                {
                    ApplyManualLerpStep(profile);
                }
                if (vfxCount > 0)
                {
                    CheckVfxEvents(profile, animTime);
                }
                yield return null;
            }
            ParryableTargetRegistry.Unregister(this);
            OnAttackEnd?.Invoke(this, profile);
            LogEvent("攻擊結束", profile);
            _currentProfile = null;
            _currentAnimState = null;
            _runningRoutine = null;
            _elapsedTime = 0f;
            _hitConfirmedTarget = null;
            _hasPreviousHitboxState = false;
        }

        // ────── 多 Hitbox 偵測 ──────
        // 主 + 額外 hitbox 統一入口；各 hitbox 由自身 HitStart/HitEnd 時間窗 gate
        // 第一個命中即整招判定完成（後續 hitbox 不再嘗試）
        private void CheckHitOverlapAll(EnemyAttackProfile profile, float animTime)
        {
            // 主 Hitbox（sweep + overlap）— 高速揮砍時 sweep 兜底，避免一幀飛過玩家
            if (animTime >= profile.HitStart && animTime <= profile.HitEnd)
            {
                CheckHitOverlap(profile);
                if (_hitConfirmedTarget != null) return;
            }
            else
            {
                // 不在主窗內 — 清掉 sweep 上一幀狀態，避免下次進窗時 sweep 跨整段動畫造成誤判
                _hasPreviousHitboxState = false;
            }

            // 額外 Hitboxes（overlap-only，不做 sweep）— 設計師若有快速軌跡需求建議用主 Hitbox
            IReadOnlyList<EnemyAttackHitboxData> extras = profile.ExtraHitboxes;
            if (extras == null) return;
            for (int i = 0; i < extras.Count; i++)
            {
                EnemyAttackHitboxData hb = extras[i];
                if (hb == null) continue;
                if (animTime < hb.HitStart || animTime > hb.HitEnd) continue;
                CheckExtraHitbox(profile, hb);
                if (_hitConfirmedTarget != null) return;
            }
        }

        // 額外 Hitbox 偵測：純 OverlapBox（不做 sweep）
        private void CheckExtraHitbox(EnemyAttackProfile profile, EnemyAttackHitboxData hb)
        {
            Transform bone = ResolveExtraHitboxBone(hb.Bone);
            if (bone == null) return;
            Vector3 center = bone.position + bone.rotation * hb.Offset;
            Quaternion rot = bone.rotation * Quaternion.Euler(hb.Rotation);
            Vector3 halfExtents = hb.Size * 0.5f;
            int count = Physics.OverlapBoxNonAlloc(
                center, halfExtents, _overlapBuffer, rot,
                hb.LayerMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider c = _overlapBuffer[i];
                if (c == null) continue;
                if (_ownerRoot != null && c.transform.IsChildOf(_ownerRoot)) continue;
                _hitConfirmedTarget = c.gameObject;
                OnHitConfirmed?.Invoke(this, profile, c.gameObject);
                if (_logEvents)
                {
                    Debug.Log($"[攻擊執行器] 額外 Hitbox「{hb.Label}」命中：{c.name}", this);
                }
                return;
            }
        }

        private Transform ResolveExtraHitboxBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return transform;
            Transform found = FindChildRecursive(transform, boneName);
            return found != null ? found : transform;
        }

        // ────── ManualLerp 位移 ──────
        // 沿 AttackStartForward 等速推進 — 跟 DefensiveAssistResponder 的 DistanceAtHit 預測公式一致
        // 透過 EnemyController.ApplyAnimatorRootMotion 走 CC.Move + 重力 + A* 同步，避免敵人穿地或飛起
        private void ApplyManualLerpStep(EnemyAttackProfile profile)
        {
            float speed = profile.MoveDistance / profile.Duration;
            Vector3 forward = AttackStartForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();
            Vector3 delta = forward * (speed * Time.deltaTime);

            if (_cachedEnemyController == null && _ownerRoot != null)
            {
                _cachedEnemyController = _ownerRoot.GetComponent<EnemyAI.EnemyController>();
            }
            if (_cachedEnemyController != null)
            {
                // 累積給 OnAnimatorMove 一次性 CC.Move，避免一幀多次 Move 造成位移失效
                _cachedEnemyController.AddExternalHorizontalMovement(delta);
                return;
            }
            // Fallback：沒 EnemyController 時直接寫 transform.position（測試 / 簡化場景用，不處理重力）
            Transform target = _ownerRoot != null ? _ownerRoot : transform;
            target.position += delta;
        }

        // HitWindow 期間每幀呼叫：sweep（上幀→本幀）+ overlap（本幀）雙重檢測
        // sweep 解決揮砍速度過快時，一幀內 hitbox 從玩家前方飛到後方但檢測位置都不 overlap 的問題
        private void CheckHitOverlap(EnemyAttackProfile profile)
        {
            if (_hitConfirmedTarget != null)
            {
                return;
            }
            Transform bone = ResolveHitboxBone(profile);
            if (bone == null)
            {
                return;
            }
            Vector3 currentCenter = bone.position + bone.rotation * profile.HitboxOffset;
            // Hitbox 旋轉 = 骨骼旋轉 × Profile 額外的 local 旋轉，讓設計師能歪斜 hitbox 不受骨骼朝向限制
            Quaternion currentRot = bone.rotation * Quaternion.Euler(profile.HitboxRotation);
            Vector3 halfExtents = profile.HitboxSize * 0.5f;
            bool hit = false;
            // 1. Sweep：從上幀 hitbox 位置掃到本幀位置，覆蓋一幀內快速軌跡
            if (_hasPreviousHitboxState)
            {
                Vector3 sweepVec = currentCenter - _previousHitboxCenter;
                float sweepDist = sweepVec.magnitude;
                if (sweepDist > 0.001f)
                {
                    int sweepCount = Physics.BoxCastNonAlloc(
                        _previousHitboxCenter, halfExtents, sweepVec.normalized,
                        _sweepBuffer, _previousHitboxRotation, sweepDist,
                        profile.HitboxLayerMask, QueryTriggerInteraction.Collide);
                    for (int i = 0; i < sweepCount; i++)
                    {
                        Collider c = _sweepBuffer[i].collider;
                        if (c == null)
                        {
                            continue;
                        }
                        if (c.transform.IsChildOf(_ownerRoot))
                        {
                            continue;
                        }
                        _hitConfirmedTarget = c.gameObject;
                        OnHitConfirmed?.Invoke(this, profile, c.gameObject);
                        if (_logEvents)
                        {
                            Debug.Log($"[攻擊執行器] Hitbox sweep 命中：{c.name}", this);
                        }
                        hit = true;
                        break;
                    }
                }
            }
            // 2. Overlap：hitbox 靜止時 sweep 沒結果，這裡兜底
            if (!hit)
            {
                int count = Physics.OverlapBoxNonAlloc(
                    currentCenter, halfExtents, _overlapBuffer, currentRot,
                    profile.HitboxLayerMask, QueryTriggerInteraction.Collide);
                for (int i = 0; i < count; i++)
                {
                    Collider c = _overlapBuffer[i];
                    if (c == null)
                    {
                        continue;
                    }
                    if (c.transform.IsChildOf(_ownerRoot))
                    {
                        continue;
                    }
                    _hitConfirmedTarget = c.gameObject;
                    OnHitConfirmed?.Invoke(this, profile, c.gameObject);
                    if (_logEvents)
                    {
                        Debug.Log($"[攻擊執行器] Hitbox overlap 命中：{c.name}", this);
                    }
                    break;
                }
            }
            _previousHitboxCenter = currentCenter;
            _previousHitboxRotation = currentRot;
            _hasPreviousHitboxState = true;
        }

        // 重置 / 配置 VFX 觸發追蹤陣列。大小不夠時重新 new；夠時清零複用，避免每次攻擊 GC
        private void ResetVfxFiredFlags(int count)
        {
            if (count <= 0)
            {
                return;
            }
            if (_vfxFired == null || _vfxFired.Length < count)
            {
                _vfxFired = new bool[count];
                return;
            }
            for (int i = 0; i < _vfxFired.Length; i++)
            {
                _vfxFired[i] = false;
            }
        }

        // 每幀檢查：跨過 event.Time 即觸發一次 SpawnVfx，並標記避免重複
        private void CheckVfxEvents(EnemyAttackProfile profile, float animTime)
        {
            IReadOnlyList<EnemyAttackVfxEvent> events = profile.VfxEvents;
            for (int i = 0; i < events.Count; i++)
            {
                if (_vfxFired[i])
                {
                    continue;
                }
                EnemyAttackVfxEvent evt = events[i];
                if (evt == null)
                {
                    _vfxFired[i] = true;
                    continue;
                }
                if (animTime >= evt.Time)
                {
                    SpawnVfx(evt);
                    _vfxFired[i] = true;
                }
            }
        }

        // 生成 VFX：找骨骼 → Instantiate → 套用 offset → 依 AttachToBone 決定 parent → 依 Lifetime 排程 Destroy
        // 已生成的 VFX 是獨立 GameObject，攻擊被 Cancel 不會主動銷毀（讓 ParticleSystem 自然消逝）
        private void SpawnVfx(EnemyAttackVfxEvent evt)
        {
            if (evt.VfxPrefab == null)
            {
                if (_logEvents)
                {
                    Debug.LogWarning($"[攻擊執行器] VFX 事件「{evt.Label}」沒有 Prefab，略過", this);
                }
                return;
            }
            Transform bone;
            if (string.IsNullOrEmpty(evt.BoneName))
            {
                bone = transform;
            }
            else
            {
                bone = FindChildRecursive(transform, evt.BoneName);
                if (bone == null)
                {
                    Debug.LogWarning($"[攻擊執行器] VFX 事件「{evt.Label}」找不到骨骼「{evt.BoneName}」，改用根節點", this);
                    bone = transform;
                }
            }
            // 兩種模式：
            // - AttachToBone：parent 到骨骼後設 local transform。world scale = bone.lossyScale × prefab × multiplier，
            //   位置 / 旋轉 / 大小都會自動跟著父物件 scale 放大（透過 transform hierarchy）
            // - 不 attach：手動套用 bone.lossyScale 讓 offset 與 size 跟著父物件放大；不會跟動畫移動
            GameObject vfx = Instantiate(evt.VfxPrefab);
            if (evt.AttachToBone)
            {
                vfx.transform.SetParent(bone, false);
                vfx.transform.localPosition = evt.PositionOffset;
                vfx.transform.localRotation = Quaternion.Euler(evt.RotationOffset);
                vfx.transform.localScale = Vector3.Scale(vfx.transform.localScale, evt.ScaleMultiplier);
            }
            else
            {
                vfx.transform.position = bone.TransformPoint(evt.PositionOffset);
                vfx.transform.rotation = bone.rotation * Quaternion.Euler(evt.RotationOffset);
                vfx.transform.localScale = Vector3.Scale(
                    Vector3.Scale(vfx.transform.localScale, evt.ScaleMultiplier),
                    bone.lossyScale);
            }
            ApplyParticleScalingMode(vfx, evt.ScaleAllChildren);
            if (evt.Lifetime > 0f)
            {
                Destroy(vfx, evt.Lifetime);
            }
            if (_logEvents)
            {
                Debug.Log($"[攻擊執行器] 生成 VFX「{evt.Label}」於「{bone.name}」", this);
            }
        }

        // 套用粒子縮放模式：Hierarchy = 跟著父物件 scale 一起縮放粒子大小與發射形狀；Local = 只縮 Transform、粒子維持原尺寸
        private static void ApplyParticleScalingMode(GameObject root, bool useHierarchy)
        {
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystemScalingMode mode = useHierarchy ? ParticleSystemScalingMode.Hierarchy : ParticleSystemScalingMode.Local;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                {
                    continue;
                }
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = mode;
            }
        }

        // ────── 遠程攻擊 ──────
        /// <summary>
        /// 從發射骨骼產生投射物 — HitStart 那一刻被呼叫一次。
        /// 套用 ProjectileData + RangedDamageEffect，瞄準依 AimMode 決定。
        /// 命中傷害走 GAS 標準流程（ApplyEffectToTarget），玩家 ASC 收到後自動扣血並觸發 OnDamageTaken
        /// </summary>
        private void FireProjectile(EnemyAttackProfile profile)
        {
            ProjectilePoolManager pool = ProjectilePoolManager.Instance;
            if (pool == null)
            {
                Debug.LogWarning("[攻擊執行器] 場景中沒有 ProjectilePoolManager — 遠程攻擊無法發射，請先在場景放一個", this);
                return;
            }
            if (profile.RangedProjectile == null || profile.RangedProjectile.Prefab == null)
            {
                Debug.LogWarning($"[攻擊執行器] 攻擊「{profile.AttackName}」是遠程模式但未設 RangedProjectile / Prefab", this);
                return;
            }
            Transform spawnBone = ResolveSpawnBone(profile.ProjectileSpawnBone);
            Vector3 spawnPos = spawnBone.position + spawnBone.rotation * profile.ProjectileSpawnOffset;
            Transform playerRoot = ResolvePlayerTransform();
            Vector3 direction = ComputeAimDirection(profile.RangedAimMode, spawnBone, spawnPos, playerRoot, profile.ProjectileForwardAngles);
            Quaternion rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction)
                : spawnBone.rotation;

            AbilitySystemComponent ownerASC = (_ownerRoot != null
                ? _ownerRoot.GetComponentInParent<AbilitySystemComponent>()
                : null) ?? GetComponentInParent<AbilitySystemComponent>();
            float attackerScale = _ownerRoot != null ? Mathf.Abs(_ownerRoot.lossyScale.x) : 1f;
            bool wantsHoming = profile.RangedAimMode == RangedAimMode.TowardPlayerHoming
                && profile.RangedProjectile.HomingEnabled;

            ProjectileBehaviour proj = pool.Get(profile.RangedProjectile.Prefab, spawnPos, rotation);
            if (proj == null) return;

            proj.Initialize(
                data: profile.RangedProjectile,
                instigator: ownerASC,
                direction: direction,
                damage: profile.Damage,
                hitEffect: profile.RangedDamageEffect,
                hitCueTag: default,
                hitVFXPrefab: null,
                hitSFX: null,
                hitVFXLifetime: 0f,
                attachHitVFXToSurface: false,
                hitVFXScale: Vector3.one,
                hitVFXScaleAllChildren: true,
                attackerScale: attackerScale,
                homingTargetRoot: wantsHoming ? playerRoot : null);

            if (_logEvents)
            {
                Debug.Log($"[攻擊執行器] 發射投射物 {profile.RangedProjectile.Prefab.name} 從骨骼 {spawnBone.name}", this);
            }
        }

        private Transform ResolveSpawnBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return _ownerRoot != null ? _ownerRoot : transform;
            Transform searchRoot = _ownerRoot != null ? _ownerRoot : transform;
            Transform found = FindChildRecursive(searchRoot, boneName);
            return found != null ? found : searchRoot;
        }

        private Transform ResolvePlayerTransform()
        {
            if (_ownerRoot != null)
            {
                EnemyAI.EnemyController ec = _ownerRoot.GetComponent<EnemyAI.EnemyController>();
                if (ec != null && ec.PlayerTransform != null) return ec.PlayerTransform;
            }
            GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
            return playerGo != null ? playerGo.transform : null;
        }

        // AimMode = Forward → 用骨骼 forward
        // AimMode = TowardPlayer / TowardPlayerHoming → 用 AimAnchorResolver 解出玩家「身體中心」（避免射向腳底）
        private static Vector3 ComputeAimDirection(RangedAimMode mode, Transform spawnBone, Vector3 spawnPos, Transform playerRoot, Vector3 forwardAngles)
        {
            // Forward 模式（或找不到玩家時的 fallback）：發射骨骼 forward 再套用設計師設定的角度偏移
            Vector3 forwardDir = spawnBone.rotation * Quaternion.Euler(forwardAngles) * Vector3.forward;
            if (mode == RangedAimMode.Forward || playerRoot == null)
            {
                return forwardDir;
            }
            Vector3 aimPos = AimAnchorResolver.ResolveAimPosition(playerRoot);
            Vector3 dir = aimPos - spawnPos;
            if (dir.sqrMagnitude < 0.0001f) return forwardDir;
            return dir.normalized;
        }

        // 找名字符合的子骨骼，cache 以避免每幀 Find
        private Transform ResolveHitboxBone(EnemyAttackProfile profile)
        {
            if (string.IsNullOrEmpty(profile.HitboxBone))
            {
                return transform;
            }
            if (_cachedHitboxBone == null || _cachedHitboxBoneName != profile.HitboxBone)
            {
                _cachedHitboxBone = FindChildRecursive(transform, profile.HitboxBone);
                _cachedHitboxBoneName = profile.HitboxBone;
            }
            return _cachedHitboxBone != null ? _cachedHitboxBone : transform;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent;
            }
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // 玩家招架觸發時呼叫 — 動畫從減速狀態恢復 1x，HitStart 會很快觸發
        public void RestoreNormalAnimSpeed()
        {
            if (_currentAnimState != null && _currentAnimState.Speed < 1f)
            {
                _currentAnimState.Speed = 1f;
                if (_logEvents)
                {
                    Debug.Log("[攻擊執行器] 招架觸發，動畫恢復 1x 速度", this);
                }
            }
        }

        // 凍住當前動畫（speed = 0），讓頓幀期間武器停在命中位置
        // 由 Responder 在接刀事件回呼內呼叫；玩家無招架時不呼叫，敵人攻擊正常播完
        public void FreezeAnimation()
        {
            if (_animancer != null && _animancer.States.Current != null)
            {
                _animancer.States.Current.Speed = 0f;
                if (_logEvents)
                {
                    Debug.Log("[攻擊執行器] 動畫凍住（speed=0）", this);
                }
            }
        }

        // 命中後動畫被凍住，呼叫此恢復 1x speed（讓動畫繼續播完剩餘揮砍）
        // 不彈刀模式由 Responder 在頓幀結束時呼叫；彈刀模式不需要（PlayParryStagger 切新動畫）
        public void ResumeAnimation()
        {
            if (_animancer != null && _animancer.States.Current != null)
            {
                _animancer.States.Current.Speed = 1f;
                if (_logEvents)
                {
                    Debug.Log("[攻擊執行器] 動畫從凍住恢復 1x", this);
                }
            }
        }

        // 被彈刀時呼叫：用 ClipTransition 內建 FadeDuration 切換到 stagger 動畫
        // 設計師在 EAP 的 ParryStaggerAnimation 欄位的 ClipTransition Inspector 設定 FadeDuration（0 = 瞬切、> 0 = 過渡）
        public void PlayParryStagger(ClipTransition staggerClip)
        {
            if (staggerClip != null && staggerClip.IsValid && _animancer != null)
            {
                AnimancerState state = _animancer.Play(staggerClip);
                state.Speed = 1f;
                if (_logEvents)
                {
                    Debug.Log($"[攻擊執行器] 被彈刀，切換到 Stagger 動畫（fade={staggerClip.FadeDuration:F2}s）", this);
                }
            }
            if (IsAttacking)
            {
                Cancel();
            }
        }

        private void LogEvent(string message, EnemyAttackProfile profile)
        {
            if (!_logEvents)
            {
                return;
            }
            Debug.Log($"[攻擊執行器] {profile.AttackName} | t={_elapsedTime:F2}s | {message}", this);
        }
    }
}
