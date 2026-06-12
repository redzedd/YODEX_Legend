using System.Collections.Generic;
using GAS.Targeting;
using UnityEngine;
using UnityEngine.Pool;

namespace GAS
{
    /// <summary>
    /// 投射物行為 - 負責飛行、碰撞偵測和傷害應用
    /// 由 ProjectilePoolManager 管理生命週期
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class ProjectileBehaviour : MonoBehaviour
    {
        private ProjectileData _data;
        private AbilitySystemComponent _instigator;
        private GameplayEffect _hitEffect;
        private GameplayTag _hitCueTag;
        private GameObject _hitVFXPrefab;
        private AudioClip _hitSFX;
        private float _hitVFXLifetime;
        private bool _attachHitVFXToSurface;
        private Vector3 _hitVFXScale = Vector3.one;
        private bool _hitVFXScaleAllChildren = true;
        private float _attackerScale = 1f;
        private Transform _homingTarget;
        private Vector3 _homingLocalOffset;
        private float _damage;
        private float _lifeTimer;
        private int _remainingPierces;
        private Vector3 _velocity;
        private bool _isActive;
        private Rigidbody _rigidbody;

        /// <summary>已命中的目標（避免重複命中）</summary>
        private readonly HashSet<Collider> _hitTargets = new();

        /// <summary>所屬的物件池（由 PoolManager 設定）</summary>
        public IObjectPool<ProjectileBehaviour> Pool { get; set; }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
        }

        /// <summary>
        /// 初始化投射物（從池中取出時呼叫）
        /// homingTargetRoot: 敵人 root,內部會自動解析子物件 AimAnchor 為實際追蹤錨點
        /// hitVFXPrefab/hitSFX: 命中敵人時直接生成的 Prefab/音效(不需要 Cue 系統)
        /// </summary>
        public void Initialize(
            ProjectileData data,
            AbilitySystemComponent instigator,
            Vector3 direction,
            float damage,
            GameplayEffect hitEffect,
            GameplayTag hitCueTag,
            GameObject hitVFXPrefab,
            AudioClip hitSFX,
            float hitVFXLifetime,
            bool attachHitVFXToSurface,
            Vector3 hitVFXScale,
            bool hitVFXScaleAllChildren,
            float attackerScale,
            Transform homingTargetRoot = null)
        {
            _data = data;
            _instigator = instigator;
            _damage = damage;
            _hitEffect = hitEffect;
            _hitCueTag = hitCueTag;
            _hitVFXPrefab = hitVFXPrefab;
            _hitSFX = hitSFX;
            _hitVFXLifetime = hitVFXLifetime;
            _attachHitVFXToSurface = attachHitVFXToSurface;
            _hitVFXScale = hitVFXScale;
            _hitVFXScaleAllChildren = hitVFXScaleAllChildren;
            _attackerScale = attackerScale;
            AimAnchorResolver.ResolveHomingAnchor(homingTargetRoot, out _homingTarget, out _homingLocalOffset);
            _lifeTimer = 0f;
            _remainingPierces = data.PierceCount;
            _velocity = direction.normalized * data.Speed;
            _isActive = true;
            _hitTargets.Clear();
            Debug.Log($"<color=lime>[ProjectileBehaviour]</color> 發射:{name} speed={data.Speed} HitLayers={(int)data.HitLayers} ObstacleLayers={(int)data.ObstacleLayers}");
        }

        private void Update()
        {
            if (!_isActive) return;

            float deltaTime = Time.deltaTime;
            _lifeTimer += deltaTime;

            // 壽命到期
            if (_lifeTimer >= _data.Lifetime)
            {
                ReturnToPool();
                return;
            }

            // 追蹤目標(瞄準錨點:AimAnchor → bounds 中心 fallback,避免射向腳底)
            if (_data.HomingEnabled && _homingTarget != null && _homingTarget.gameObject.activeInHierarchy)
            {
                Vector3 aimPos = _homingTarget.TransformPoint(_homingLocalOffset);
                Vector3 toTarget = (aimPos - transform.position).normalized;
                Vector3 newDir = Vector3.RotateTowards(
                    _velocity.normalized,
                    toTarget,
                    _data.HomingStrength * Mathf.Deg2Rad * deltaTime,
                    0f);
                _velocity = newDir * _data.Speed;
            }

            // 重力影響
            if (_data.Gravity > 0f)
            {
                _velocity += Vector3.down * (_data.Gravity * deltaTime);
            }

            // 掃描碰撞(防止高速子彈穿透目標) — SphereCast/Raycast 在每幀位移之間做連續偵測
            Vector3 movement = _velocity * deltaTime;
            float distance = movement.magnitude;
            if (distance > 0.0001f)
            {
                Vector3 direction = movement / distance;
                if (TrySweepHit(direction, distance, out RaycastHit sweepHit))
                {
                    transform.position = sweepHit.point;
                    UpdateRotationFromVelocity();
                    HandleHit(sweepHit.collider, sweepHit.point, sweepHit.normal);
                    return;
                }
                // 旁路:IProjectileIgnitable(爆炸桶等場景互動物件)— 無視 HitLayers/ObstacleLayers 設定,讓設計師擺什麼 layer 都能被點燃
                if (TryIgnitableSweep(direction, distance, out RaycastHit ignitableHit))
                {
                    transform.position = ignitableHit.point;
                    UpdateRotationFromVelocity();
                    HandleHit(ignitableHit.collider, ignitableHit.point, ignitableHit.normal);
                    return;
                }
                transform.position += movement;
            }

            UpdateRotationFromVelocity();
        }

        private static readonly RaycastHit[] _ignitableSweepBuffer = new RaycastHit[8];

        /// <summary>
        /// 旁路掃描:沿位移向量找 IProjectileIgnitable 物件(無視 HitLayers/ObstacleLayers 設定)。
        /// 爆炸桶這類「設計師擺什麼 layer 都該被點燃」的場景物件靠這條路徑接事件。
        /// </summary>
        private bool TryIgnitableSweep(Vector3 direction, float distance, out RaycastHit hit)
        {
            float radius = Mathf.Max(_data.SweepRadius, 0.1f);
            int count = Physics.SphereCastNonAlloc(transform.position, radius, direction, _ignitableSweepBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
            float minDist = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < count; i++)
            {
                Collider col = _ignitableSweepBuffer[i].collider;
                if (col == null) continue;
                if (col.GetComponentInParent<IProjectileIgnitable>() == null) continue;
                if (_ignitableSweepBuffer[i].distance < minDist)
                {
                    minDist = _ignitableSweepBuffer[i].distance;
                    bestIdx = i;
                }
            }
            if (bestIdx >= 0)
            {
                hit = _ignitableSweepBuffer[bestIdx];
                return true;
            }
            hit = default;
            return false;
        }

        private static readonly RaycastHit[] _sweepBuffer = new RaycastHit[16];

        /// <summary>
        /// 沿位移向量掃描偵測命中(同時涵蓋 HitLayers + ObstacleLayers,任一命中都處理)
        /// SweepRadius > 0 用 SphereCast(寬容,推薦);≤ 0 退化為 Raycast(便宜但易擦邊不中)
        /// 無敵目標(閃避/招架中的玩家)會被跳過,投射物直接穿透飛過。
        /// </summary>
        private bool TrySweepHit(Vector3 direction, float distance, out RaycastHit hit)
        {
            int sweepMask = _data.HitLayers | _data.ObstacleLayers;
            float radius = _data.SweepRadius;
            int count = radius <= 0.001f
                ? Physics.RaycastNonAlloc(transform.position, direction, _sweepBuffer, distance, sweepMask, QueryTriggerInteraction.Ignore)
                : Physics.SphereCastNonAlloc(transform.position, radius, direction, _sweepBuffer, distance, sweepMask, QueryTriggerInteraction.Ignore);
            float minDist = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < count; i++)
            {
                if (_sweepBuffer[i].collider == null) continue;
                // 無敵目標不計入命中,讓投射物穿透過去
                if (IsInvincibleTarget(_sweepBuffer[i].collider)) continue;
                if (_sweepBuffer[i].distance < minDist)
                {
                    minDist = _sweepBuffer[i].distance;
                    bestIdx = i;
                }
            }
            if (bestIdx >= 0)
            {
                hit = _sweepBuffer[bestIdx];
                return true;
            }
            hit = default;
            return false;
        }

        /// <summary>
        /// 目標是否處於無敵(閃避/招架)— 是則投射物應穿透並略過傷害。觸發者本身不算。
        /// </summary>
        private bool IsInvincibleTarget(Collider other)
        {
            AbilitySystemComponent asc = other.GetComponentInParent<AbilitySystemComponent>();
            if (asc == null || asc == _instigator) return false;
            if (!asc.OwnedTags.HasTag(GameplayTags.State.Invincible)) return false;
            // 穿透無敵目標時通知玩家觸發閃避慢動作回饋(內部自帶單次窗口防抖)
            asc.GetComponent<NewGASPlayerController>()?.NotifyDodgeIFrameHit();
            return true;
        }

        private void UpdateRotationFromVelocity()
        {
            if (_velocity.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(_velocity);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_isActive) return;
            // 無敵目標穿透 — 不偵測、不命中,投射物繼續飛
            if (IsInvincibleTarget(other)) return;
            DetectSurfaceInfo(other, out Vector3 hitPoint, out Vector3 hitNormal);
            HandleHit(other, hitPoint, hitNormal);
        }

        /// <summary>
        /// 命中處理共用邏輯(Sweep 與 OnTriggerEnter 都會呼叫)
        /// 命中特效流向:
        ///   • 障礙物 — 只用 ProjectileData(RangedAttackData.HitVFX 是「敵人命中」概念,不適用牆)
        ///   • 單體敵人 — 優先 ProjectileData;為空時 fallback 到 RangedAttackData(避免雙特效重疊)
        ///   • AOE — 中心爆炸用 ProjectileData;每個 target 反應在 ApplyAreaDamage 內用 RangedAttackData
        /// 所有路徑都走 HitVFXSpawner,套用 RangedAttackData 的 Scale + ScaleAllChildren 與角色當下縮放
        /// </summary>
        private void HandleHit(Collider other, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (!_isActive) return;

            // 場景互動物件(爆炸桶等)— 在 layer 篩選之前優先觸發,允許物件不在 HitLayers/ObstacleLayers 內也能被引爆
            IProjectileIgnitable ignitable = other.GetComponentInParent<IProjectileIgnitable>();
            if (ignitable != null)
            {
                ignitable.OnProjectileImpact(hitPoint);
                SpawnHitFX(_data.ImpactVFXPrefab, _data.ImpactSFX, hitPoint, hitNormal, other.transform,
                    _data.ImpactVFXLifetime, _data.AttachImpactToSurface);
                ReturnToPool();
                return;
            }

            int otherLayer = 1 << other.gameObject.layer;

            // 碰到障礙物 — 只用 ProjectileData
            if ((_data.ObstacleLayers & otherLayer) != 0)
            {
                SpawnHitFX(_data.ImpactVFXPrefab, _data.ImpactSFX, hitPoint, hitNormal, other.transform,
                    _data.ImpactVFXLifetime, _data.AttachImpactToSurface);
                ReturnToPool();
                return;
            }

            // 碰到命中對象
            if ((_data.HitLayers & otherLayer) == 0) return;
            // 無敵目標 — 穿透,不命中也不引爆
            if (IsInvincibleTarget(other)) return;
            if (_hitTargets.Contains(other)) return;

            if (_data.ImpactRadius > 0f)
            {
                // AOE:不在這裡 Add 觸發者,讓 ApplyAreaDamage 的 OverlapSphere 找得到並傷害
                // ApplyAreaDamage 內部會自行 Add 每個傷害到的目標(含觸發者)— 後續穿透/重複觸發會正確 dedup
                ApplyAreaDamage(transform.position);
                // AOE 中心爆炸 — 只用 ProjectileData,per-target 反應已在 ApplyAreaDamage 內逐個生成
                SpawnHitFX(_data.ImpactVFXPrefab, _data.ImpactSFX, hitPoint, hitNormal, other.transform,
                    _data.ImpactVFXLifetime, _data.AttachImpactToSurface);
            }
            else
            {
                _hitTargets.Add(other);
                ApplyDamageToTarget(other, hitPoint, hitNormal);
                // 單體敵人 — ProjectileData 優先,空時 fallback 到 RangedAttackData
                bool useProjectile = _data.ImpactVFXPrefab != null;
                GameObject prefab = useProjectile ? _data.ImpactVFXPrefab : _hitVFXPrefab;
                AudioClip sfx = _data.ImpactSFX != null ? _data.ImpactSFX : _hitSFX;
                float lifetime = useProjectile ? _data.ImpactVFXLifetime : _hitVFXLifetime;
                bool attach = useProjectile ? _data.AttachImpactToSurface : _attachHitVFXToSurface;
                SpawnHitFX(prefab, sfx, hitPoint, hitNormal, other.transform, lifetime, attach);
            }

            if (_remainingPierces <= 0)
            {
                ReturnToPool();
            }
            else
            {
                _remainingPierces--;
            }
        }

        /// <summary>
        /// 偵測碰撞表面的法線與命中點
        /// 使用反向射線投射取得精確的表面資訊
        /// </summary>
        private void DetectSurfaceInfo(Collider other, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            float rayDist = _data.SurfaceDetectionDistance;
            Vector3 flyDir = _velocity.normalized;
            Vector3 rayOrigin = transform.position - flyDir * rayDist;
            if (Physics.Raycast(rayOrigin, flyDir, out RaycastHit hit, rayDist * 2f))
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                return;
            }
            hitPoint = other.ClosestPoint(transform.position);
            hitNormal = (transform.position - hitPoint).normalized;
            if (hitNormal.sqrMagnitude < 0.001f)
            {
                hitNormal = -flyDir;
            }
        }

        /// <summary>
        /// 對單一目標造成傷害(只處理 damage + Cue,VFX 由 HandleHit 統一生成以避免雙特效)
        /// </summary>
        private void ApplyDamageToTarget(Collider targetCollider, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (_instigator == null) return;
            var targetASC = targetCollider.GetComponentInParent<AbilitySystemComponent>();
            if (targetASC == null || targetASC == _instigator) return;
            // 無敵目標 — 不造成傷害
            if (targetASC.OwnedTags.HasTag(GameplayTags.State.Invincible)) return;
            if (_hitEffect != null)
            {
                _instigator.ApplyEffectToTarget(targetASC, _hitEffect, SetByCallerTags.DAMAGE, _damage);
            }
            if (_hitCueTag.IsValid)
            {
                Quaternion surfaceRot = hitNormal.sqrMagnitude > 0.001f
                    ? Quaternion.FromToRotation(Vector3.up, hitNormal)
                    : Quaternion.identity;
                _instigator.ExecuteGameplayCue(_hitCueTag, hitPoint, surfaceRot, targetCollider.gameObject);
            }
        }

        /// <summary>
        /// 對區域內所有目標造成傷害 — 中心爆炸由 HandleHit 處理,此處只生成 per-target 反應特效(RangedAttackData.HitVFX,無 ProjectileData fallback 避免與中心爆炸重複)
        /// </summary>
        private void ApplyAreaDamage(Vector3 center)
        {
            if (_instigator == null) return;

            Collider[] hits = Physics.OverlapSphere(center, _data.ImpactRadius, _data.HitLayers);
            foreach (Collider hit in hits)
            {
                if (_hitTargets.Contains(hit)) continue;
                _hitTargets.Add(hit);

                var targetASC = hit.GetComponentInParent<AbilitySystemComponent>();
                if (targetASC == null || targetASC == _instigator) continue;
                // 無敵目標 — 範圍傷害也略過
                if (targetASC.OwnedTags.HasTag(GameplayTags.State.Invincible)) continue;

                if (_hitEffect != null)
                {
                    _instigator.ApplyEffectToTarget(targetASC, _hitEffect, SetByCallerTags.DAMAGE, _damage);
                }

                if (_hitCueTag.IsValid)
                {
                    _instigator.ExecuteGameplayCue(_hitCueTag, hit.transform.position, hit.gameObject);
                }
                // per-target 反應特效 — 只用 RangedAttackData.HitVFX,避免與中心爆炸(ProjectileData)重複
                SpawnHitFX(_hitVFXPrefab, _hitSFX, hit.transform.position, Vector3.up, hit.transform,
                    _hitVFXLifetime, _attachHitVFXToSurface);
            }
        }

        /// <summary>
        /// 命中特效生成共用入口 — 把任一 prefab/sfx 套上 RangedAttackData 的 Scale 設定與角色縮放後丟給 HitVFXSpawner。
        /// ProjectileData 的 ImpactVFX 與 RangedAttackData 的 HitVFX 都走這條,確保兩者都享有「跟著角色放大」與「子物件粒子一起縮放」的行為
        /// </summary>
        private void SpawnHitFX(GameObject prefab, AudioClip sfx, Vector3 hitPoint, Vector3 hitNormal, Transform hitTransform, float lifetime, bool attach)
        {
            if (prefab != null)
            {
                Quaternion rotation = hitNormal.sqrMagnitude > 0.001f
                    ? Quaternion.FromToRotation(Vector3.up, hitNormal)
                    : Quaternion.identity;
                HitVFXSpawner.Spawn(prefab, hitPoint, rotation,
                    _hitVFXScale, _attackerScale, _hitVFXScaleAllChildren,
                    lifetime,
                    attach ? hitTransform : null);
            }
            if (sfx != null)
            {
                AudioSource.PlayClipAtPoint(sfx, hitPoint);
            }
        }

        /// <summary>
        /// 回收到物件池
        /// </summary>
        public void ReturnToPool()
        {
            _isActive = false;

            if (Pool != null)
            {
                Pool.Release(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 池回收時重置狀態
        /// </summary>
        public void OnReturnToPool()
        {
            _isActive = false;
            _hitTargets.Clear();
            _instigator = null;
            _hitVFXPrefab = null;
            _hitSFX = null;
            _homingTarget = null;
            _homingLocalOffset = Vector3.zero;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 池取出時啟用
        /// </summary>
        public void OnGetFromPool()
        {
            gameObject.SetActive(true);
        }
    }
}
