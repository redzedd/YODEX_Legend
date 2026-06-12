using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 近戰攻擊快照 — 玩家攻擊途中切武器時,把當前攻擊狀態打包交給殘影執行器接手。
    /// 設計成 plain class(非 ScriptableObject、非 struct,便於攜帶 HashSet/Dictionary)。
    /// </summary>
    public class MeleeAttackSnapshot
    {
        /// <summary>攻擊資料來源(讀 HitWindows / TimelineEvents / Clip)</summary>
        public MeleeAttackData AttackData;

        /// <summary>動畫接手時間點(秒,殘影從此處繼續播放)</summary>
        public float ResumeTime;

        /// <summary>傷害歸屬 — 玩家 ASC,維持仇恨值/統計一致</summary>
        public AbilitySystemComponent InstigatorOwner;

        /// <summary>敵人 Layer(沿用攻擊能力上的設定)</summary>
        public LayerMask EnemyLayer;

        /// <summary>障礙物 Layer(目前殘影未使用,保留供未來擴充)</summary>
        public LayerMask ObstacleLayer;

        /// <summary>
        /// 當前正在活動中的 HitWindow 與其已命中清單。
        /// 殘影進入這些視窗時會繼承命中清單,避免對同一敵人重複造成傷害。
        /// </summary>
        public Dictionary<MeleeHitWindow, HashSet<Collider>> InheritedActiveHits;

        /// <summary>
        /// 玩家已觸發過的 TimelineEvent — 殘影跳過,避免 VFX 重複生成。
        /// </summary>
        public HashSet<TimelineEvent> InheritedTriggeredEvents;
    }

    /// <summary>
    /// 殘影攻擊執行器 — 在殘影 GameObject 上獨立跑剩餘攻擊。
    /// 不旋轉、不位移、不取輸入、不寫 HitMemory,只負責:
    /// 1) 動畫播完
    /// 2) HitWindow 判定 + 傷害套用(傷害來源 = 玩家 ASC)
    /// 3) TimelineEvent VFX/SFX
    /// 4) 子幀取樣(高速揮擊不漏判)
    /// 5) 結束時把附在骨骼的 VFX 分離,讓殘影模型被銷毀後特效仍能播完
    /// </summary>
    public class MeleeAttackGhostExecutor : MonoBehaviour
    {
        // 子幀命中檢測門檻(等效 120fps)— 與 GA_MeleeAttack 保持一致
        private const float SUB_STEP_THRESHOLD = 1f / 120f;

        private MeleeAttackSnapshot _snapshot;
        private AnimancerComponent _animancer;
        private AnimancerState _animState;
        private Transform _selfTransform;
        private float _scaleFactor = 1f;

        // 與 MeleeAttackRuntimeData 對齊的運行時狀態
        private readonly Dictionary<MeleeHitWindow, HitWindowRuntimeState> _hitWindowStates = new();
        private readonly HashSet<TimelineEvent> _triggeredEvents = new();
        private readonly Dictionary<TimelineEvent, TimelineEventInstance> _activeTimelineInstances = new();
        private readonly RaycastHit[] _raycastBuffer = new RaycastHit[32];
        private readonly Collider[] _colliderBuffer = new Collider[32];
        private Dictionary<string, Transform> _socketMap;

        private bool _isRunning;
        /// <summary>是否仍在執行中 — 供 WeaponRuntimeState.FadeOutAfterImage 等執行器跑完再開始淡出。</summary>
        public bool IsRunning => _isRunning;

        /// <summary>初始化並啟動殘影攻擊執行(由 WeaponManager 在 CreateAfterImage 之後呼叫)</summary>
        public void Initialize(MeleeAttackSnapshot snapshot)
        {
            if (_isRunning)
            {
                Debug.LogWarning("[MeleeAttackGhostExecutor] 重複初始化,忽略");
                return;
            }
            if (snapshot == null || snapshot.AttackData == null || snapshot.AttackData.Clip?.Clip == null)
            {
                Debug.LogWarning("[MeleeAttackGhostExecutor] 快照無效,銷毀執行器");
                Destroy(this);
                return;
            }

            _snapshot = snapshot;
            _selfTransform = transform;

            // 取殘影自身的 Animancer(WeaponRuntimeState 已在 SetupAnimationCompletion 加上)
            _animancer = GetComponent<AnimancerComponent>();
            if (_animancer == null)
            {
                _animancer = GetComponentInChildren<AnimancerComponent>();
            }
            if (_animancer == null)
            {
                Debug.LogWarning("[MeleeAttackGhostExecutor] 殘影沒有 AnimancerComponent,無法接管攻擊");
                Destroy(this);
                return;
            }

            // 啟動 Animancer 自己(WeaponRuntimeState.DisableUnnecessaryComponents 已 enable=true,但保險再 set 一次)
            _animancer.enabled = true;

            // 從接手時間點重新播放,確保 hit window 判定基準與動畫時間同步
            _animState = _animancer.Play(snapshot.AttackData.Clip);
            _animState.Time = snapshot.ResumeTime;

            // 殘影自身縮放(殘影可能受 SpatialScale 影響)
            _scaleFactor = SpatialScaleUtility.GetScaleFactor(_animancer.transform);

            // 繼承「正在活動中」HitWindow 的已命中清單,避免雙重傷害
            if (snapshot.InheritedActiveHits != null)
            {
                foreach (var kvp in snapshot.InheritedActiveHits)
                {
                    // 先放佔位,真正建立 HitWindowRuntimeState 等首次進入視窗時(UpdateHitWindows 內)再做
                    // 這裡用一個 sentinel 標記:Origin = null 表示「待 lazy init,繼承命中清單」
                    var state = new HitWindowRuntimeState
                    {
                        Origin = null,
                        HitEnemies = new HashSet<Collider>(kvp.Value),
                    };
                    _hitWindowStates[kvp.Key] = state;
                }
            }

            // 繼承已觸發 TimelineEvent
            if (snapshot.InheritedTriggeredEvents != null)
            {
                foreach (var evt in snapshot.InheritedTriggeredEvents)
                {
                    _triggeredEvents.Add(evt);
                }
            }

            _isRunning = true;
            StartCoroutine(GhostAttackRoutine());
        }

        private IEnumerator GhostAttackRoutine()
        {
            MeleeAttackData attackData = _snapshot.AttackData;
            AnimationClip primaryClip = attackData.Clip.Clip;
            GameObject animTarget = _animancer.gameObject;
            float animDuration = primaryClip.length;
            // AllowInputTime >= 0 表示「玩家可接連招輸入」時間點,殘影到此就消失;
            // < 0 表示沒有此窗口,殘影跑完整段動畫
            bool hasCancelWindow = attackData.AllowInputTime >= 0f;

            float stateTimer = _animState.Time;
            float prevTimer = stateTimer;

            while (stateTimer < animDuration && _animState != null && _animancer != null)
            {
                prevTimer = stateTimer;
                stateTimer = _animState.Time;
                float frameDelta = stateTimer - prevTimer;

                if (frameDelta > SUB_STEP_THRESHOLD && HasPendingHitWindows(prevTimer, stateTimer))
                {
                    // 殘影 root 在 world space(無父層),SampleAnimation 會把 root 寫到 clip 起點(動畫空間 ~0,0,0),
                    // 子骨骼整個飄到場景原點,hit detection 找不到敵人。
                    // 對策:每次 SampleAnimation 後立刻還原 root 的 world transform,讓子骨骼維持正確世界座標。
                    // (玩家版本因為 model 是 NEWPlayer 的子物件,localPosition=0 可把它拉回父層位置,殘影沒這個 luxury)
                    Vector3 savedPos = animTarget.transform.position;
                    Quaternion savedRot = animTarget.transform.rotation;

                    int steps = Mathf.CeilToInt(frameDelta / SUB_STEP_THRESHOLD);
                    float stepSize = frameDelta / steps;
                    for (int i = 1; i <= steps; i++)
                    {
                        float subPrev = prevTimer + stepSize * (i - 1);
                        float subCurrent = prevTimer + stepSize * i;
                        primaryClip.SampleAnimation(animTarget, subCurrent);
                        animTarget.transform.SetPositionAndRotation(savedPos, savedRot);
                        UpdateHitWindows(subPrev, subCurrent);
                    }
                    primaryClip.SampleAnimation(animTarget, stateTimer);
                    animTarget.transform.SetPositionAndRotation(savedPos, savedRot);
                }
                else
                {
                    UpdateHitWindows(prevTimer, stateTimer);
                }

                UpdateTimelineEvents(stateTimer);

                // 到達 AllowInputTime → 殘影結束(視為中斷,讓 TimelineEvent 按 InterruptBehavior 處理 VFX)
                // 不主動 Destroy(gameObject) — WeaponRuntimeState.FadeOutAfterImage 偵測到 IsRunning=false 後會接手淡出 + 銷毀
                if (hasCancelWindow && stateTimer >= attackData.AllowInputTime)
                {
                    Cleanup(wasInterrupted: true);
                    yield break;
                }

                yield return null;
            }

            Cleanup(wasInterrupted: false);
        }

        private bool HasPendingHitWindows(float prevTime, float currentTime)
        {
            foreach (var window in _snapshot.AttackData.HitWindows)
            {
                if (currentTime >= window.StartTime && prevTime <= window.EndTime)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateHitWindows(float prevTime, float currentTime)
        {
            foreach (var kvp in _hitWindowStates)
            {
                kvp.Value.JustActivated = false;
            }

            foreach (var hitWindow in _snapshot.AttackData.HitWindows)
            {
                bool isInWindow = currentTime >= hitWindow.StartTime && prevTime <= hitWindow.EndTime;
                if (!isInWindow) continue;

                if (!_hitWindowStates.TryGetValue(hitWindow, out HitWindowRuntimeState state) || state.Origin == null)
                {
                    // 視窗首次進入殘影 — 初始化 Origin / Previous 位置等
                    // 若繼承自玩家(state 已存在但 Origin=null),則保留 HitEnemies 列表
                    HashSet<Collider> inheritedHits = state != null ? state.HitEnemies : new HashSet<Collider>();
                    Transform origin = ResolveSocket(hitWindow.SocketName);
                    state = new HitWindowRuntimeState
                    {
                        Origin = origin,
                        IsAttached = hitWindow.AttachToBody,
                        JustActivated = true,
                        WasFrameSkipped = prevTime < hitWindow.StartTime && currentTime > hitWindow.EndTime,
                        HitEnemies = inheritedHits,
                    };

                    Vector3 startPos = origin.TransformPoint(hitWindow.Offset);
                    state.PreviousPosition = startPos;
                    if (hitWindow.UseRaycastTrail)
                    {
                        int segments = hitWindow.TrailSegments;
                        state.PreviousTrailPositions = new Vector3[segments];
                        for (int s = 0; s < segments; s++)
                        {
                            float t = segments > 1 ? (float)s / (segments - 1) : 0f;
                            Vector3 localPos = Vector3.Lerp(hitWindow.TrailStartOffset, hitWindow.TrailEndOffset, t);
                            state.PreviousTrailPositions[s] = origin.TransformPoint(localPos);
                        }
                    }
                    if (!hitWindow.AttachToBody)
                    {
                        state.WorldLockPosition = startPos;
                        state.WorldLockRotation = origin.rotation;
                    }
                    _hitWindowStates[hitWindow] = state;

                    // 殘影不旋轉、不位移 — 略過 AutoFaceMarkedTarget 與 TriggerMovement
                }

                ProcessHitDetection(hitWindow, _hitWindowStates[hitWindow]);
            }

            // 清理過期視窗(剛啟動的保留到下一幀)
            List<MeleeHitWindow> toRemove = null;
            foreach (var kvp in _hitWindowStates)
            {
                if (currentTime > kvp.Key.EndTime && !kvp.Value.JustActivated)
                {
                    toRemove ??= new List<MeleeHitWindow>();
                    toRemove.Add(kvp.Key);
                }
            }
            if (toRemove != null)
            {
                foreach (var key in toRemove) _hitWindowStates.Remove(key);
            }
        }

        private void ProcessHitDetection(MeleeHitWindow hitWindow, HitWindowRuntimeState state)
        {
            if (hitWindow.UseRaycastTrail && state.PreviousTrailPositions != null)
            {
                ProcessHitDetectionRaycastTrail(hitWindow, state);
            }
            else
            {
                ProcessHitDetectionSweep(hitWindow, state);
            }
        }

        private void ProcessHitDetectionSweep(MeleeHitWindow hitWindow, HitWindowRuntimeState state)
        {
            Vector3 currentPos;
            Quaternion currentRot;
            if (state.IsAttached)
            {
                currentPos = state.Origin.TransformPoint(hitWindow.Offset);
                currentRot = state.Origin.rotation;
            }
            else
            {
                currentPos = state.WorldLockPosition;
                currentRot = state.WorldLockRotation;
            }
            Vector3 direction = currentPos - state.PreviousPosition;
            float distance = direction.magnitude;
            Vector3 expandedSize = hitWindow.Size * _scaleFactor;
            if (state.WasFrameSkipped)
            {
                expandedSize *= 1.5f;
                state.WasFrameSkipped = false;
            }

            HashSet<Collider> currentFrameHits = new();
            Vector3 sweepDir = distance > 0.001f ? direction.normalized : -_selfTransform.forward;
            float sweepDist = distance + 0.5f;
            Vector3 sweepStart = state.PreviousPosition - (sweepDir * 0.5f);
            int hitCount;
            if (hitWindow.Shape == HitboxShape.Box)
            {
                hitCount = Physics.BoxCastNonAlloc(sweepStart, expandedSize / 2, sweepDir,
                    _raycastBuffer, currentRot, sweepDist, _snapshot.EnemyLayer);
            }
            else
            {
                hitCount = Physics.SphereCastNonAlloc(sweepStart, expandedSize.x,
                    sweepDir, _raycastBuffer, sweepDist, _snapshot.EnemyLayer);
            }
            for (int i = 0; i < hitCount; i++) currentFrameHits.Add(_raycastBuffer[i].collider);

            int overlapCount;
            if (hitWindow.Shape == HitboxShape.Box)
            {
                overlapCount = Physics.OverlapBoxNonAlloc(currentPos, expandedSize / 2,
                    _colliderBuffer, currentRot, _snapshot.EnemyLayer);
            }
            else
            {
                overlapCount = Physics.OverlapSphereNonAlloc(currentPos, expandedSize.x,
                    _colliderBuffer, _snapshot.EnemyLayer);
            }
            for (int i = 0; i < overlapCount; i++) currentFrameHits.Add(_colliderBuffer[i]);

            foreach (var hitCollider in currentFrameHits)
            {
                if (state.HitEnemies.Contains(hitCollider)) continue;
                state.HitEnemies.Add(hitCollider);
                OnHit(hitWindow, hitCollider, currentPos);
            }
            state.PreviousPosition = currentPos;
        }

        private void ProcessHitDetectionRaycastTrail(MeleeHitWindow hitWindow, HitWindowRuntimeState state)
        {
            int segments = hitWindow.TrailSegments;
            float rayRadius = hitWindow.TrailRayRadius * _scaleFactor;
            Transform origin = state.Origin;
            HashSet<Collider> currentFrameHits = new();
            Vector3[] currentPositions = new Vector3[segments];
            for (int s = 0; s < segments; s++)
            {
                float t = segments > 1 ? (float)s / (segments - 1) : 0f;
                Vector3 localPos = Vector3.Lerp(hitWindow.TrailStartOffset, hitWindow.TrailEndOffset, t);
                currentPositions[s] = origin.TransformPoint(localPos);
            }

            // 縱向射線(上一幀 → 當前幀)
            for (int s = 0; s < segments; s++)
            {
                Vector3 from = state.PreviousTrailPositions[s];
                Vector3 to = currentPositions[s];
                Vector3 delta = to - from;
                float dist = delta.magnitude;
                if (dist < 0.001f) continue;
                Vector3 dir = delta / dist;
                AddRayHits(from, dir, dist, rayRadius, currentFrameHits);
            }
            // 橫向射線(同幀相鄰段)
            for (int s = 0; s < segments - 1; s++)
            {
                Vector3 from = currentPositions[s];
                Vector3 to = currentPositions[s + 1];
                Vector3 delta = to - from;
                float dist = delta.magnitude;
                if (dist < 0.001f) continue;
                Vector3 dir = delta / dist;
                AddRayHits(from, dir, dist, rayRadius, currentFrameHits);
            }
            // 對角線射線
            for (int s = 0; s < segments - 1; s++)
            {
                Vector3 from = state.PreviousTrailPositions[s];
                Vector3 to = currentPositions[s + 1];
                Vector3 delta = to - from;
                float dist = delta.magnitude;
                if (dist < 0.001f) continue;
                Vector3 dir = delta / dist;
                AddRayHits(from, dir, dist, rayRadius, currentFrameHits);
            }
            // 安全網 Capsule Overlap
            if (segments >= 2)
            {
                Vector3 capsuleP1 = currentPositions[0];
                Vector3 capsuleP2 = currentPositions[segments - 1];
                float maxSegmentMove = 0f;
                for (int s = 0; s < segments; s++)
                {
                    float segMove = (currentPositions[s] - state.PreviousTrailPositions[s]).sqrMagnitude;
                    if (segMove > maxSegmentMove) maxSegmentMove = segMove;
                }
                float safetyRadius = Mathf.Max(rayRadius, Mathf.Sqrt(maxSegmentMove) * 0.25f);
                int overlapCount = Physics.OverlapCapsuleNonAlloc(
                    capsuleP1, capsuleP2, safetyRadius, _colliderBuffer, _snapshot.EnemyLayer);
                for (int i = 0; i < overlapCount; i++) currentFrameHits.Add(_colliderBuffer[i]);
            }

            Vector3 hitCenter = currentPositions[segments / 2];
            foreach (var hitCollider in currentFrameHits)
            {
                if (state.HitEnemies.Contains(hitCollider)) continue;
                state.HitEnemies.Add(hitCollider);
                OnHit(hitWindow, hitCollider, hitCenter);
            }
            for (int s = 0; s < segments; s++) state.PreviousTrailPositions[s] = currentPositions[s];
            state.PreviousPosition = hitCenter;
        }

        private void AddRayHits(Vector3 from, Vector3 dir, float dist, float rayRadius, HashSet<Collider> sink)
        {
            int hitCount;
            if (rayRadius > 0f)
            {
                hitCount = Physics.SphereCastNonAlloc(from, rayRadius, dir, _raycastBuffer, dist, _snapshot.EnemyLayer);
            }
            else
            {
                hitCount = Physics.RaycastNonAlloc(from, dir, _raycastBuffer, dist, _snapshot.EnemyLayer);
            }
            for (int i = 0; i < hitCount; i++) sink.Add(_raycastBuffer[i].collider);
        }

        private void OnHit(MeleeHitWindow hitWindow, Collider hitCollider, Vector3 hitboxCenter)
        {
            AbilitySystemComponent instigator = _snapshot.InstigatorOwner;
            if (instigator == null) return; // 玩家被銷毀 → 殘影不再造成傷害

            Vector3 hitPoint = hitCollider.ClosestPoint(hitboxCenter);
            float damage = hitWindow.BaseDamage * hitWindow.DamageMultiplier;

            AbilitySystemComponent targetASC = hitCollider.GetComponent<AbilitySystemComponent>();
            bool gasApplied = false;
            if (targetASC != null && hitWindow.HitEffect != null)
            {
                instigator.ApplyEffectToTarget(targetASC, hitWindow.HitEffect, SetByCallerTags.DAMAGE, damage);
                gasApplied = true;
            }

            IHitReceiver hitReceiver = hitCollider.GetComponent<IHitReceiver>();
            if (hitReceiver != null)
            {
                // 攻擊方向以殘影為起點計算 — 視覺上來源就是殘影
                Vector3 attackDir = (hitCollider.transform.position - _selfTransform.position).normalized;
                attackDir.y = 0f;
                HitContext hitCtx = new HitContext
                {
                    damage = gasApplied ? 0f : damage,
                    poiseDamage = hitWindow.PoiseDamage,
                    knockbackForce = hitWindow.KnockbackForce,
                    attackTier = hitWindow.AttackTier,
                    isHeavyAttack = hitWindow.AttackTier == AttackTier.Heavy,
                    hitPoint = hitPoint,
                    hitNormal = (_selfTransform.position - hitPoint).normalized,
                    attackDirection = attackDir,
                    gasDamageApplied = gasApplied,
                    hitStopDuration = hitWindow.HitStopDuration,
                    hitStopTimeScale = hitWindow.HitStopSpeed,
                    cameraShakeIntensity = hitWindow.ScreenShakeForce,
                };
                hitReceiver.OnHit(ref hitCtx);
            }

            // Cue + Prefab 兩條路並列生效;Prefab 用表面法線旋轉
            Vector3 surfaceNormal = (_selfTransform.position - hitPoint).normalized;
            Quaternion surfaceRot = surfaceNormal.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(surfaceNormal)
                : Quaternion.identity;

            if (hitWindow.HitCueTag.IsValid)
            {
                instigator.ExecuteGameplayCue(hitWindow.HitCueTag, hitPoint, hitCollider.gameObject);
            }
            if (hitWindow.HitVFXPrefab != null)
            {
                HitVFXSpawner.Spawn(
                    hitWindow.HitVFXPrefab, hitPoint, surfaceRot,
                    hitWindow.HitVFXScale, _scaleFactor, hitWindow.HitVFXScaleAllChildren,
                    hitWindow.HitVFXLifetime,
                    hitWindow.AttachHitVFXToSurface ? hitCollider.transform : null);
            }
            if (hitWindow.HitSFX != null)
            {
                AudioSource.PlayClipAtPoint(hitWindow.HitSFX, hitPoint);
            }
        }

        private void UpdateTimelineEvents(float currentTime)
        {
            foreach (var evt in _snapshot.AttackData.TimelineEvents)
            {
                if (_triggeredEvents.Contains(evt)) continue;
                if (currentTime < evt.TriggerTime) continue;
                TriggerTimelineEvent(evt);
                _triggeredEvents.Add(evt);
            }
        }

        private void TriggerTimelineEvent(TimelineEvent evt)
        {
            Transform socket = ResolveSocket(evt.SocketName);
            TimelineEventInstance inst = TimelineEventSpawner.Trigger(evt, socket, _scaleFactor, _snapshot.InstigatorOwner);
            if (inst != null && (inst.SpawnedVFX != null || inst.CueHandler != null))
            {
                _activeTimelineInstances[evt] = inst;
            }
        }

        private Transform ResolveSocket(string socketName)
        {
            if (string.IsNullOrEmpty(socketName))
            {
                return _animancer != null ? _animancer.transform : _selfTransform;
            }
            _socketMap ??= new Dictionary<string, Transform>();
            if (_socketMap.TryGetValue(socketName, out Transform cached))
            {
                if (cached != null) return cached;
                _socketMap.Remove(socketName);
            }
            Transform searchRoot = _animancer != null ? _animancer.transform : _selfTransform;
            Transform found = FindChildRecursive(searchRoot, socketName);
            if (found != null)
            {
                _socketMap[socketName] = found;
                return found;
            }
            return searchRoot;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform result = FindChildRecursive(child, name);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// 結束時的 VFX 處理 — 統一交給 TimelineEventSpawner.Cleanup。
        /// wasInterrupted=true 對應「殘影在 AllowCancelTime 提前消失」;
        /// 附在骨骼上的 VFX 一律會被 detach,避免殘影 GO 銷毀時連帶破壞特效。
        /// </summary>
        private void Cleanup(bool wasInterrupted)
        {
            foreach (var kvp in _activeTimelineInstances)
            {
                TimelineEventSpawner.Cleanup(kvp.Value, wasInterrupted);
            }
            _hitWindowStates.Clear();
            _triggeredEvents.Clear();
            _activeTimelineInstances.Clear();
            _isRunning = false;
        }

        private void OnDestroy()
        {
            if (_isRunning)
            {
                // 殘影 GameObject 被外部銷毀(例如連續切換超過上限、場景切換)— 視為中斷,
                // VFX 走 InterruptBehavior;附在骨骼上的會被 detach 出去繼續播完
                Cleanup(wasInterrupted: true);
            }
        }
    }
}
