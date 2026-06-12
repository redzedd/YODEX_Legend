using System.Collections;
using System.Collections.Generic;
using GAS.Targeting;
using UnityEngine;
using Animancer;

namespace GAS
{
    /// <summary>
    /// 遠程攻擊快照(目前僅支援 ChargeMode.None 的 QuickFire)。
    /// 玩家在 QuickFire 攻擊中切武器時,把當前狀態打包交給殘影執行器接手剩餘發射 + 時間軸事件。
    /// 蓄力 / 瞄準模式(HoldToCharge / HoldToAim)目前不支援,會回傳 null 讓殘影退回純視覺。
    /// </summary>
    public class RangedAttackSnapshot
    {
        public RangedAttackData AttackData;
        public float ResumeTime;
        public AbilitySystemComponent InstigatorOwner;
        public LayerMask EnemyLayer;
        public LayerMask ObstacleLayer;

        /// <summary>玩家已發射的 FireEvents — 殘影跳過,避免單發武器發出兩顆子彈</summary>
        public HashSet<RangedFireEvent> AlreadyFiredEvents;

        /// <summary>玩家已觸發的 TimelineEvent — 殘影跳過,避免 VFX 重複生成</summary>
        public HashSet<TimelineEvent> AlreadyTriggeredEvents;

        // === 瞄準資訊 ===
        // 殘影發射時會讀 Transform 最新位置,讓敵人移動時仍能命中(對齊玩家端 Solve 的時序語意)
        /// <summary>切武器當下的鎖定目標 Transform(LockOnController);null 表示無鎖定</summary>
        public Transform LockedTarget;
        /// <summary>切武器當下的標記目標 Transform(HitTargetMemory.LastHitTarget);null 表示無標記</summary>
        public Transform MarkedTarget;
        /// <summary>切武器當下的 AutoFace 目標 Transform;null 表示無</summary>
        public Transform AutoFaceTarget;
        /// <summary>切武器當下若處於瞄準相機模式,擷取的螢幕中心射線命中點(殘影無法跟著相機,只能用 snapshot 位置)</summary>
        public Vector3 AimHitPoint;
        public bool HasAimHitPoint;
    }

    /// <summary>
    /// 遠程攻擊殘影執行器 — 在殘影 GameObject 上獨立跑剩餘 QuickFire。
    /// 不旋轉(不接玩家輸入)、不寫 HitMemory,只負責:
    /// 1) 動畫播完(或在 SheatheCancelTime 提前結束)
    /// 2) FireEvents 時間到 → 生成投射物 / AoE / Hitscan
    /// 3) TimelineEvent 時間到 → 觸發 VFX/SFX Cue
    /// 4) 結束時依 InterruptBehavior 處理 cue handlers
    /// 傷害仍掛在玩家 ASC(_snapshot.InstigatorOwner)
    /// </summary>
    public class RangedAttackGhostExecutor : MonoBehaviour
    {
        private RangedAttackSnapshot _snapshot;
        private AnimancerComponent _animancer;
        private AnimancerState _animState;
        private Transform _selfTransform;
        private float _scaleFactor = 1f;

        private readonly HashSet<RangedFireEvent> _firedEvents = new();
        private readonly HashSet<TimelineEvent> _triggeredEvents = new();
        private readonly Dictionary<TimelineEvent, TimelineEventInstance> _activeTimelineInstances = new();
        private Dictionary<string, Transform> _socketMap;

        private bool _isRunning;
        /// <summary>是否仍在執行中 — 供 WeaponRuntimeState.FadeOutAfterImage 等執行器跑完再開始淡出。</summary>
        public bool IsRunning => _isRunning;

        public void Initialize(RangedAttackSnapshot snapshot)
        {
            if (_isRunning)
            {
                Debug.LogWarning("[RangedAttackGhostExecutor] 重複初始化,忽略");
                return;
            }
            if (snapshot == null || snapshot.AttackData == null || snapshot.AttackData.FireAnimation.Clip == null)
            {
                Debug.LogWarning("[RangedAttackGhostExecutor] 快照無效,銷毀執行器");
                Destroy(this);
                return;
            }

            _snapshot = snapshot;
            _selfTransform = transform;

            _animancer = GetComponent<AnimancerComponent>();
            if (_animancer == null)
            {
                _animancer = GetComponentInChildren<AnimancerComponent>();
            }
            if (_animancer == null)
            {
                Debug.LogWarning("[RangedAttackGhostExecutor] 殘影沒有 AnimancerComponent");
                Destroy(this);
                return;
            }
            _animancer.enabled = true;

            _animState = _animancer.Play(snapshot.AttackData.FireAnimation);
            _animState.Time = snapshot.ResumeTime;

            _scaleFactor = SpatialScaleUtility.GetScaleFactor(_animancer.transform);

            if (snapshot.AlreadyFiredEvents != null)
            {
                foreach (var ev in snapshot.AlreadyFiredEvents) _firedEvents.Add(ev);
            }
            if (snapshot.AlreadyTriggeredEvents != null)
            {
                foreach (var ev in snapshot.AlreadyTriggeredEvents) _triggeredEvents.Add(ev);
            }

            _isRunning = true;
            StartCoroutine(GhostFireRoutine());
        }

        private IEnumerator GhostFireRoutine()
        {
            RangedAttackData attackData = _snapshot.AttackData;
            AnimationClip primaryClip = attackData.FireAnimation.Clip;
            float animDuration = primaryClip.length;
            // AllowInputTime >= 0 表示「玩家可接連招輸入」時間點,殘影到此就消失;
            // < 0 表示沒有此窗口,殘影跑完整段動畫
            bool hasCancelWindow = attackData.AllowInputTime >= 0f;

            while (_animState != null && _animancer != null)
            {
                float stateTimer = _animState.Time;

                FireByTime(stateTimer);
                UpdateTimelineEvents(stateTimer);

                // 不主動 Destroy(gameObject) — WeaponRuntimeState.FadeOutAfterImage 偵測到 IsRunning=false 後會接手淡出 + 銷毀
                if (hasCancelWindow && stateTimer >= attackData.AllowInputTime)
                {
                    Cleanup(wasInterrupted: true);
                    yield break;
                }
                if (stateTimer >= animDuration) break;

                yield return null;
            }

            Cleanup(wasInterrupted: false);
        }

        private void FireByTime(float currentTime)
        {
            List<RangedFireEvent> fireEvents = _snapshot.AttackData.GetResolvedFireEvents();
            foreach (var evt in fireEvents)
            {
                if (_firedEvents.Contains(evt)) continue;
                if (currentTime < evt.FireTime) continue;
                _firedEvents.Add(evt);
                FireSingle(evt);
            }
        }

        private void FireSingle(RangedFireEvent fireEvent)
        {
            RangedAttackData attackData = _snapshot.AttackData;
            AbilitySystemComponent instigator = _snapshot.InstigatorOwner;
            if (instigator == null) return;

            float baseDamage = fireEvent.GetEffectiveBaseDamage(attackData);
            float finalDamage = baseDamage * fireEvent.DamageMultiplier;

            // 建 FireSolveContext — target 引用在發射瞬間才讀「最新位置」,敵人移動仍可命中
            string socketName = fireEvent.GetEffectiveSpawnSocketName(attackData);
            Transform socket = ResolveSocket(socketName);
            socket.GetPositionAndRotation(out Vector3 socketPos, out Quaternion socketRot);
            FireSolveContext ctx = new FireSolveContext
            {
                OwnerPosition = _selfTransform.position,
                OwnerRotation = _selfTransform.rotation,
                SocketPosition = socketPos,
                SocketRotation = socketRot,
                ApplyPitchClamp = attackData.ApplyPitchClamp,
                MaxPitchDown = attackData.MaxPitchDown,
                MarkedTargetMaxRange = attackData.AutoFaceRange,
            };
            // 依玩家端優先順序填入(Solver 自己會按 Locked > Aim > Marked > AutoFace > Forward 取最高)
            // 三個目標都解析為模型中心(AimAnchor) — 與玩家端一致,殘影子彈不會射向腳底
            if (_snapshot.LockedTarget != null && _snapshot.LockedTarget.gameObject.activeInHierarchy)
            {
                ctx.HasLockedTarget = true;
                ctx.LockedTargetPosition = AimAnchorResolver.ResolveAimPosition(_snapshot.LockedTarget);
            }
            if (_snapshot.HasAimHitPoint)
            {
                ctx.HasAimCamera = true;
                ctx.AimHitPoint = _snapshot.AimHitPoint;
            }
            if (_snapshot.MarkedTarget != null && _snapshot.MarkedTarget.gameObject.activeInHierarchy)
            {
                ctx.HasMarkedTarget = true;
                ctx.MarkedTargetPosition = AimAnchorResolver.ResolveAimPosition(_snapshot.MarkedTarget);
            }
            if (_snapshot.AutoFaceTarget != null && _snapshot.AutoFaceTarget.gameObject.activeInHierarchy)
            {
                ctx.HasAutoFaceTarget = true;
                ctx.AutoFaceTargetPosition = AimAnchorResolver.ResolveAimPosition(_snapshot.AutoFaceTarget);
            }
            FireEventInput input = new FireEventInput
            {
                SpawnOffset = fireEvent.SpawnOffset,
                DirectionOffsetEuler = fireEvent.DirectionOffset
            };
            FireDirectionSolver.Solve(in ctx, in input, out FireSolveResult solve);

            if (attackData.FireCueTag.IsValid)
            {
                instigator.ExecuteGameplayCue(attackData.FireCueTag, solve.SpawnPosition, null);
            }

            switch (attackData.AttackType)
            {
                case RangedAttackType.Projectile:
                    SpawnProjectile(finalDamage, fireEvent, in solve);
                    break;
                case RangedAttackType.AoETargeted:
                case RangedAttackType.AoEAtTarget:
                    SpawnAoE(finalDamage, fireEvent);
                    break;
                case RangedAttackType.Hitscan:
                    PerformHitscan(finalDamage, fireEvent, in solve);
                    break;
            }
        }

        private void SpawnProjectile(float damage, RangedFireEvent fireEvent, in FireSolveResult solve)
        {
            RangedAttackData attackData = _snapshot.AttackData;
            ProjectileData projData = attackData.ProjectileConfig;
            if (projData == null || projData.Prefab == null) return;

            Vector3 spawnPos = solve.SpawnPosition;
            Vector3 direction = solve.FireDirection;
            Quaternion rotation = Quaternion.LookRotation(direction);

            ProjectileBehaviour projectile;
            if (ProjectilePoolManager.Instance != null)
            {
                projectile = ProjectilePoolManager.Instance.Get(projData.Prefab, spawnPos, rotation);
            }
            else
            {
                GameObject instance = Instantiate(projData.Prefab, spawnPos, rotation);
                projectile = instance.GetComponent<ProjectileBehaviour>();
                if (projectile == null) projectile = instance.AddComponent<ProjectileBehaviour>();
            }

            if (projectile != null)
            {
                GameplayEffect effHit = fireEvent.GetEffectiveHitEffect(attackData);
                GameplayTag effCue = fireEvent.GetEffectiveHitCueTag(attackData);
                GameObject effVFX = fireEvent.GetEffectiveHitVFXPrefab(attackData);
                AudioClip effSFX = fireEvent.GetEffectiveHitSFX(attackData);
                // 殘影沒鎖定目標 → 不啟用 homing(即使 prefab 有 HomingEnabled 也沒目標可追)
                projectile.Initialize(
                    projData, _snapshot.InstigatorOwner, direction, damage,
                    effHit, effCue, effVFX, effSFX,
                    attackData.HitVFXLifetime, attackData.AttachHitVFXToSurface,
                    attackData.HitVFXScale, attackData.HitVFXScaleAllChildren, _scaleFactor,
                    null);
            }
        }

        private void SpawnAoE(float damage, RangedFireEvent fireEvent)
        {
            RangedAttackData attackData = _snapshot.AttackData;
            if (attackData.AoEPrefab == null) return;

            // 殘影沒鎖定 / 瞄準相機,AoE 中心固定用「ghost 前方距離」處理(對應 PlayerForward 模式)
            Vector3 center = _selfTransform.position
                + _selfTransform.forward * attackData.AoEForwardDistance;
            Quaternion rotation = Quaternion.LookRotation(_selfTransform.forward, Vector3.up);
            GameObject aoeInstance = Instantiate(attackData.AoEPrefab, center, rotation);
            AoEBehaviour aoe = aoeInstance.GetComponent<AoEBehaviour>();
            if (aoe == null)
            {
                Destroy(aoeInstance);
                return;
            }
            GameplayEffect effHit = fireEvent.GetEffectiveHitEffect(attackData);
            GameplayTag effCue = fireEvent.GetEffectiveHitCueTag(attackData);
            aoe.Activate(_snapshot.InstigatorOwner, damage, effHit, effCue, 0f);
        }

        private void PerformHitscan(float damage, RangedFireEvent fireEvent, in FireSolveResult solve)
        {
            RangedAttackData attackData = _snapshot.AttackData;
            AbilitySystemComponent instigator = _snapshot.InstigatorOwner;
            Vector3 origin = solve.SpawnPosition;
            Vector3 direction = solve.FireDirection;

            if (!Physics.Raycast(origin, direction, out RaycastHit hit, 100f, _snapshot.EnemyLayer)) return;

            AbilitySystemComponent targetASC = hit.collider.GetComponentInParent<AbilitySystemComponent>();
            if (targetASC == null || targetASC == instigator) return;

            GameplayEffect effHit = fireEvent.GetEffectiveHitEffect(attackData);
            GameplayTag effCue = fireEvent.GetEffectiveHitCueTag(attackData);
            GameObject effVFX = fireEvent.GetEffectiveHitVFXPrefab(attackData);
            AudioClip effSFX = fireEvent.GetEffectiveHitSFX(attackData);
            if (effHit != null)
            {
                instigator.ApplyEffectToTarget(targetASC, effHit, SetByCallerTags.DAMAGE, damage);
            }
            Quaternion surfRot = Quaternion.FromToRotation(Vector3.up, hit.normal);
            if (effCue.IsValid)
            {
                instigator.ExecuteGameplayCue(effCue, hit.point, surfRot, hit.collider.gameObject);
            }
            if (effVFX != null)
            {
                HitVFXSpawner.Spawn(
                    effVFX, hit.point, surfRot,
                    attackData.HitVFXScale, _scaleFactor, attackData.HitVFXScaleAllChildren,
                    attackData.HitVFXLifetime,
                    attackData.AttachHitVFXToSurface ? hit.collider.transform : null);
            }
            if (effSFX != null)
            {
                AudioSource.PlayClipAtPoint(effSFX, hit.point);
            }
        }

        private void UpdateTimelineEvents(float currentTime)
        {
            // QuickFire 永遠用 Fire phase(沒蓄力起手/迴圈)
            foreach (var evt in _snapshot.AttackData.TimelineEvents)
            {
                if (evt.Phase != TimelineEventPhase.Fire) continue;
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
        /// 結束時 VFX 處理 — 統一交給 TimelineEventSpawner.Cleanup,規則對齊 MeleeAttackGhostExecutor.Cleanup。
        /// </summary>
        private void Cleanup(bool wasInterrupted)
        {
            foreach (var kvp in _activeTimelineInstances)
            {
                TimelineEventSpawner.Cleanup(kvp.Value, wasInterrupted);
            }
            _firedEvents.Clear();
            _triggeredEvents.Clear();
            _activeTimelineInstances.Clear();
            _isRunning = false;
        }

        private void OnDestroy()
        {
            if (_isRunning)
            {
                Cleanup(wasInterrupted: true);
            }
        }
    }
}
