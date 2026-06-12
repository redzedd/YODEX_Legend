using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GAS
{
    /// <summary>
    /// AoE 傷害套用模型
    /// </summary>
    public enum AoETickMode
    {
        /// <summary>釋放即一擊（震波、爆炸)</summary>
        OneShot,
        /// <summary>持續區域 — Delay → Tick × N 次(火雨、毒霧)</summary>
        Persistent,
        /// <summary>隕石雨 — ParticleSystem.OnParticleCollision 事件驅動,每顆 meteor 落點做飛濺傷害</summary>
        MeteorRain
    }

    /// <summary>
    /// AoE 區域效果行為 - prefab 一體式設計
    /// 生命週期: BeginPreview (蓄力中可視化) → UpdatePreview (每幀跟隨位置/蓄力) → Activate (釋放發射,套用傷害)
    /// 視覺分階段: _indicatorRoot 蓄力中/Delay 期間顯示;_effectRoot 攻擊生效時顯示
    /// </summary>
    public class AoEBehaviour : MonoBehaviour
    {
        [Header("Area")]
        [Tooltip("AoE 半徑(蓄力啟用時可放大)")]
        [SerializeField] private float _radius = 5f;

        [Header("Tick Mode")]
        [Tooltip("OneShot: 釋放即一擊 / Persistent: 延遲後持續 Tick")]
        [SerializeField] private AoETickMode _tickMode = AoETickMode.OneShot;

        [Header("Persistent Only (TickMode = Persistent 才生效)")]
        [Tooltip("AoE 落地延遲(秒)")]
        [SerializeField] private float _delay = 0.5f;

        [Tooltip("AoE 持續時間(秒)")]
        [SerializeField] private float _duration = 2f;

        [Tooltip("傷害間隔(秒)")]
        [SerializeField] private float _tickInterval = 0.5f;

        [Tooltip("最大傷害次數")]
        [SerializeField] private int _maxTicks = 4;

        [Header("MeteorRain Only (TickMode = MeteorRain 才生效)")]
        [Tooltip("隕石持續發射時間(秒)— 之後停止 emit,等已在飛行中的粒子全部落地")]
        [SerializeField] private float _meteorRainDuration = 3f;

        [Tooltip("每顆 meteor 落地點的飛濺傷害半徑")]
        [SerializeField] private float _meteorSplashRadius = 1f;

        [Header("Visual Phase Roots")]
        [Tooltip("蓄力預覽 + Persistent Delay 期間顯示的子物件根(放 DecalProjector / 警示圈)。留空 = 不分階段,prefab 整體一直顯示")]
        [SerializeField] private GameObject _indicatorRoot;

        [Tooltip("攻擊生效時顯示的子物件根(放爆炸/持續傷害特效)。留空 = 不分階段")]
        [SerializeField] private GameObject _effectRoot;

        [Header("Charge Scaling")]
        [Tooltip("是否依蓄力比例放大範圍(同步縮放 prefab transform.localScale + DecalProjector.size)")]
        [SerializeField] private bool _scaleRadiusWithCharge;

        [Tooltip("蓄力前期(chargeTime < MinChargeTime)的起始縮放倍率。\n" +
                 "0.5 = 從 50% 放大到 100%(MinChargeTime 剛好 100%)\n" +
                 "1.0 = 前期不縮小,直接從 100% 起跳(只後段放大)")]
        [Range(0f, 1f)]
        [SerializeField] private float _minScaleMultiplier = 0.5f;

        [Tooltip("滿蓄力(MaxChargeTime)時的半徑倍率")]
        [SerializeField] private float _radiusChargeMultiplier = 1.5f;

        [Tooltip("跟隨 root 放大的子物件清單。\n" +
                 "解決 ParticleSystem 預設 Local 模式不會跟 root 一起放大的問題:\n" +
                 "勾選 ScaleWithRange 的條目會在 Awake 時把該子物件子樹下所有 ParticleSystem 切到 Hierarchy 模式 — 粒子大小會跟著 root.lossyScale 放大。\n" +
                 "未列出的子物件保持原 ParticleSystem 設定不變。")]
        [SerializeField] private ScaledChildEntry[] _scaledChildren = System.Array.Empty<ScaledChildEntry>();

        [Header("Collision")]
        [Tooltip("命中圖層")]
        [SerializeField] private LayerMask _hitLayers;

        [Header("Lifetime")]
        [Tooltip("傷害結束後保留多久讓粒子播完才銷毀")]
        [SerializeField] private float _effectLifetime = 3f;

        [Header("Hit Reaction")]
        [Tooltip("Poise 傷害 — 擊破敵人 Poise 才會觸發 Stagger,否則只扣血(輕擊被吸收)")]
        [SerializeField] private float _poiseDamage = 50f;

        [Tooltip("攻擊類型:\nNormal — 一般攻擊（打斷 Idle/Walk）\nLight — 輕攻擊（只抖動）\nHeavy — 重攻擊（Poise 擊破走 Knockback）")]
        [SerializeField] private AttackTier _attackTier = AttackTier.Normal;

        [Tooltip("【已棄用，但保留兼容】— 寫入端依 _attackTier == Heavy 同步")]
        [SerializeField] private bool _isHeavyAttack;

        [Tooltip("擊退距離(公尺,0 = 不擊退) — 方向由 hitOrigin 指向目標")]
        [SerializeField] private float _knockbackForce;

        [Tooltip("命中時頓幀時間(秒,0 = 不頓幀) — Persistent Tick 建議保持 0,避免每 tick 都頓")]
        [SerializeField] private float _hitStopDuration;

        [Tooltip("頓幀期間的 timeScale(越接近 0 越停)")]
        [SerializeField] private float _hitStopTimeScale = 0.05f;

        [Tooltip("命中時鏡頭震動強度(0 = 不震動)")]
        [SerializeField] private float _cameraShakeIntensity;

        [Header("Audio")]
        [SerializeField] private AudioClip _castSFX;
        [SerializeField] private AudioClip _hitSFX;

        [Header("Decal Indicator (URP)")]
        [Tooltip("自動同步 Radius 到子物件 DecalProjector 的 size.x/y + LightWall transform 的 scale.x/z(深度/高度不動)")]
        [SerializeField] private bool _autoSyncDecalSize = true;

        [Tooltip("光壁 transform — 自動同步 localScale.x/z 到 Radius(假設 mesh 為單位半徑 1m 的管狀);scale.y 不動,留給 AoEIndicatorAnimator 控制。留空 = 不同步")]
        [SerializeField] private Transform _indicatorWallTransform;

        [Tooltip("地形貼合元件(掛在光壁上)— 每次 Radius/ChargeMultiplier 變動或 Activate 時呼叫 ConformToGround,讓光壁底環沿地形變形。留空 = 自動從 _indicatorWallTransform 取")]
        [SerializeField] private TerrainConformingWall _terrainConformingWall;

        [Header("Debug Visualization (MeteorRain)")]
        [Tooltip("Scene View 中顯示每顆 meteor 的落地點與飛濺範圍 — Play 模式中保留 N 秒後淡出")]
        [SerializeField] private bool _debugDrawSplashPoints = true;

        [Tooltip("Debug 落點顯示秒數")]
        [SerializeField] private float _debugSplashDisplayDuration = 2f;

        public float Radius => _radius;
        public AoETickMode TickMode => _tickMode;
        public bool ScaleRadiusWithCharge => _scaleRadiusWithCharge;
        public float MinScaleMultiplier => _minScaleMultiplier;
        public float RadiusChargeMultiplier => _radiusChargeMultiplier;
        public float Delay => _delay;
        public float Duration => _duration;
        public float TickInterval => _tickInterval;
        public int MaxTicks => _maxTicks;
        public float EffectLifetime => _effectLifetime;
        public float MeteorRainDuration => _meteorRainDuration;
        public float MeteorSplashRadius => _meteorSplashRadius;

        /// <summary>
        /// 依 chargeRatio 取得實際 scale 倍率(分段曲線):
        /// • ratio &lt; 0(蓄力前期): Lerp(_minScaleMultiplier, 1.0, ratio + 1) — 從最小起點放大到 100%
        /// • ratio &gt;= 0(蓄力後期): Lerp(1.0, _radiusChargeMultiplier, ratio) — 從 100% 放大到滿蓄
        /// 接點 ratio=0 永遠對應 1.0(MinChargeTime 時剛好 100%)
        /// _scaleRadiusWithCharge=false 永遠回傳 1.0
        /// </summary>
        public float GetEffectiveScaleMultiplier(float chargeRatio)
        {
            if (!_scaleRadiusWithCharge) return 1f;
            if (chargeRatio < 0f)
            {
                return Mathf.Lerp(_minScaleMultiplier, 1f, Mathf.Clamp01(chargeRatio + 1f));
            }
            return Mathf.Lerp(1f, _radiusChargeMultiplier, Mathf.Clamp01(chargeRatio));
        }

        private AbilitySystemComponent _instigator;
        private float _damage;
        private GameplayEffect _hitEffect;
        private GameplayTag _hitCueTag;
        private HitVFXInfo _hitVFX;

        /// <summary>
        /// 命中特效（直接拉 Prefab，不走 Cue 系統）— 由攻擊層（RangedAttackData.HitVFXPrefab 等）填入並透過 Activate 傳入。
        /// 與 _hitCueTag 並列生效，兩者皆設定時都會生成。
        /// </summary>
        public struct HitVFXInfo
        {
            public GameObject Prefab;
            public AudioClip SFX;
            public float Lifetime;
            public bool AttachToSurface;
            public Vector3 Scale;
            public bool ScaleAllChildren;
            public float AttackerScale;
        }
        private float _effectiveRadius;
        private int _tickCount;
        private bool _isActive;
        private bool _isPreview;
        private Vector3 _baseScale = Vector3.one;

        /// <summary>每 Tick 已命中的目標追蹤(每 Tick 重置)</summary>
        private readonly HashSet<AbilitySystemComponent> _tickHitTargets = new();

        /// <summary>每顆 meteor 飛濺範圍內已命中的 ASC 追蹤 — 防止同一敵人多個 collider 在單次 splash 中重複觸發 stagger</summary>
        private readonly HashSet<AbilitySystemComponent> _splashHitTargets = new();

        /// <summary>
        /// 子物件縮放跟隨設定 — 每條目對應一個要跟著 root 放大的子物件
        /// </summary>
        [System.Serializable]
        public class ScaledChildEntry
        {
            [Tooltip("要納入範圍縮放修正的子物件 Transform")]
            public Transform Child;

            [Tooltip("勾選 = Awake 時把該子物件下所有 ParticleSystem 切到 Hierarchy 縮放模式,粒子大小會跟 root 放大\n取消 = 暫時關閉(供 designer 切換比對,不刪除條目)")]
            public bool ScaleWithRange = true;
        }

        private void Awake()
        {
            // 快取 prefab 原始 scale,蓄力縮放以此為基準避免累加
            _baseScale = transform.localScale;
            // 自動從 wall transform 取地形貼合元件(設計師沒手動拖也能 work)
            if (_terrainConformingWall == null && _indicatorWallTransform != null)
            {
                _terrainConformingWall = _indicatorWallTransform.GetComponent<TerrainConformingWall>();
            }
            // 子物件 ParticleSystem 縮放模式修正 — 確保跟著 root 放大
            ApplyChildScalingModeSetup();
        }

        /// <summary>
        /// 為 _scaledChildren 中勾選 ScaleWithRange 的條目把 ParticleSystem 切到 Hierarchy 模式。
        /// 預設 Local 模式只用該 PS 自己 GameObject 的 localScale 縮放粒子,root 放大不會傳到粒子大小;
        /// Hierarchy 模式改用 lossyScale,root.localScale × multiplier 後粒子自動跟著大。
        /// </summary>
        private void ApplyChildScalingModeSetup()
        {
            if (_scaledChildren == null) return;
            for (int i = 0; i < _scaledChildren.Length; i++)
            {
                var entry = _scaledChildren[i];
                if (entry == null || entry.Child == null || !entry.ScaleWithRange) continue;
                var particles = entry.Child.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particles)
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
            }
        }

        /// <summary>
        /// 進入預覽模式(蓄力期間) — 顯示 indicator,隱藏 effect。不會套用傷害
        /// </summary>
        public void BeginPreview()
        {
            _isPreview = true;
            ShowIndicator();
            SetRoot(_effectRoot, false);
        }

        /// <summary>
        /// 預覽期間每幀更新位置/朝向/蓄力縮放
        /// </summary>
        public void UpdatePreview(Vector3 worldPos, Quaternion rotation, float chargeRatio)
        {
            transform.SetPositionAndRotation(worldPos, rotation);
            ApplyEffectiveTransform(chargeRatio);
        }

        /// <summary>
        /// 取消預覽(蓄力中斷) — 立即銷毀,不放招
        /// </summary>
        public void CancelPreview()
        {
            _isPreview = false;
            if (gameObject != null) Destroy(gameObject);
        }

        /// <summary>
        /// 啟動 AoE 實際攻擊 — 從預覽 promote 到攻擊,或無預覽時直接呼叫
        /// </summary>
        /// <param name="chargeRatio">蓄力比例 0~1(非蓄力傳 0)— 用於 ScaleRadiusWithCharge</param>
        public void Activate(
            AbilitySystemComponent instigator,
            float damage,
            GameplayEffect hitEffect,
            GameplayTag hitCueTag,
            float chargeRatio = 0f,
            HitVFXInfo hitVFX = default)
        {
            _isPreview = false;
            _instigator = instigator;
            _damage = damage;
            _hitEffect = hitEffect;
            _hitCueTag = hitCueTag;
            _hitVFX = hitVFX;
            _tickCount = 0;
            _isActive = true;
            ApplyEffectiveTransform(chargeRatio);

            switch (_tickMode)
            {
                case AoETickMode.OneShot:
                    StartCoroutine(OneShotRoutine());
                    break;
                case AoETickMode.Persistent:
                    StartCoroutine(PersistentRoutine());
                    break;
                case AoETickMode.MeteorRain:
                    StartCoroutine(MeteorRainRoutine());
                    break;
            }
        }

        /// <summary>
        /// 套用蓄力比例:更新 _effectiveRadius、transform.localScale、DecalProjector.size
        /// 冪等 — 預覽每幀呼叫不會累加
        /// 走分段曲線 GetEffectiveScaleMultiplier — chargeRatio<0 = 蓄力前期,>=0 = 蓄力後期
        /// </summary>
        private void ApplyEffectiveTransform(float chargeRatio)
        {
            float multiplier = GetEffectiveScaleMultiplier(chargeRatio);
            _effectiveRadius = _radius * multiplier;
            transform.localScale = _baseScale * multiplier;
            // DecalProjector 預設 ScaleInvariant,transform.localScale 不影響貼花 → 必須手動帶入 multiplier
            SyncDecalSize(multiplier);
        }

        private IEnumerator OneShotRoutine()
        {
            HideIndicator();
            SetRoot(_effectRoot, true);
            if (_castSFX != null)
            {
                AudioSource.PlayClipAtPoint(_castSFX, transform.position);
            }
            ApplyTickDamage();
            yield return new WaitForSeconds(_effectLifetime);
            Destroy(gameObject);
        }

        private IEnumerator PersistentRoutine()
        {
            // Delay 期間延續 indicator 顯示 — 給玩家「警示」感受
            ShowIndicator();
            SetRoot(_effectRoot, false);
            if (_castSFX != null)
            {
                AudioSource.PlayClipAtPoint(_castSFX, transform.position);
            }
            if (_delay > 0f)
            {
                yield return new WaitForSeconds(_delay);
            }
            // 切到 effect 開始 Tick
            HideIndicator();
            SetRoot(_effectRoot, true);

            float elapsed = 0f;
            while (_isActive && _tickCount < _maxTicks && elapsed < _duration)
            {
                ApplyTickDamage();
                _tickCount++;
                yield return new WaitForSeconds(_tickInterval);
                elapsed += _tickInterval;
            }

            yield return new WaitForSeconds(_effectLifetime);
            Destroy(gameObject);
        }

        /// <summary>
        /// 隕石雨流程:
        /// 1. 釋放瞬間隱藏 _indicatorRoot(警示圈消失)+ 啟用 _effectRoot(粒子開始落)
        /// 2. 掃描所有子物件 ParticleSystem,自動 attach AoEMeteorCollisionRelay 收 OnParticleCollision
        /// 3. 等 _meteorRainDuration 秒讓粒子發射期結束 → Stop(StopEmitting) 不再生新粒子
        /// 4. 等 _effectLifetime 秒讓飛行中粒子全部落地
        /// 5. 銷毀整個 AoE
        /// 傷害透過 HandleMeteorImpacts 事件驅動,Coroutine 本身不直接做傷害判定
        /// </summary>
        private IEnumerator MeteorRainRoutine()
        {
            HideIndicator();
            SetRoot(_effectRoot, true);

            if (_castSFX != null)
            {
                AudioSource.PlayClipAtPoint(_castSFX, transform.position);
            }

            // 自動接 collision relay 到子物件的所有 ParticleSystem
            ParticleSystem[] particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            if (particleSystems.Length == 0)
            {
                Debug.LogWarning($"[AoEBehaviour] MeteorRain 模式但 prefab '{name}' 內找不到 ParticleSystem,無法產生傷害", this);
            }
            foreach (var ps in particleSystems)
            {
                var relay = ps.gameObject.GetComponent<AoEMeteorCollisionRelay>();
                if (relay == null)
                {
                    relay = ps.gameObject.AddComponent<AoEMeteorCollisionRelay>();
                }
                relay.Initialize(this, ps);
                if (!ps.isPlaying) ps.Play(true);
            }

            yield return new WaitForSeconds(_meteorRainDuration);

            // 停止發射但保留飛行中粒子,等它們落地造成最後一波傷害
            foreach (var ps in particleSystems)
            {
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            yield return new WaitForSeconds(_effectLifetime);
            Destroy(gameObject);
        }

        /// <summary>
        /// 由 AoEMeteorCollisionRelay 在 OnParticleCollision 觸發時呼叫
        /// 從 ParticleSystem.GetCollisionEvents 取每顆 meteor 的落地座標,對每點做飛濺判定
        /// </summary>
        public void HandleMeteorImpacts(ParticleSystem ps, GameObject other)
        {
            if (!_isActive || _instigator == null || ps == null) return;
            int eventCount = ps.GetCollisionEvents(other, _meteorCollisionBuffer);
            for (int i = 0; i < eventCount; i++)
            {
                ApplyMeteorSplash(_meteorCollisionBuffer[i].intersection);
            }
        }

        private readonly List<ParticleCollisionEvent> _meteorCollisionBuffer = new();
        private readonly Collider[] _meteorSplashColliderBuffer = new Collider[16];

        /// <summary>Debug 用:單個 meteor 落點記錄(落地後 _debugSplashDisplayDuration 秒內可在 Scene View 看到)</summary>
        private struct DebugSplashPoint
        {
            public Vector3 Position;
            public float TimeRecorded;
            public int EnemyHits;
        }
        private readonly List<DebugSplashPoint> _debugSplashPoints = new();

        /// <summary>
        /// 在指定世界座標做 _meteorSplashRadius 飛濺傷害(範圍內所有 ASC 受傷一次)
        /// 同 splash 內單一 ASC 只命中一次(防止多 collider 角色被重複 stagger);跨 splash 獨立計算
        /// </summary>
        private void ApplyMeteorSplash(Vector3 worldPoint)
        {
            _splashHitTargets.Clear();
            int hitCount = Physics.OverlapSphereNonAlloc(worldPoint, _meteorSplashRadius, _meteorSplashColliderBuffer, _hitLayers);
            int enemyHits = 0;
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _meteorSplashColliderBuffer[i];
                if (col == null) continue;
                AbilitySystemComponent targetASC = col.GetComponentInParent<AbilitySystemComponent>();
                if (targetASC == null || targetASC == _instigator) continue;
                if (!_splashHitTargets.Add(targetASC)) continue;
                enemyHits++;
                ApplyHitToTarget(targetASC, worldPoint);
            }

            // Debug:紀錄此次落地位置 + 命中數,供 OnDrawGizmos 顯示
            if (_debugDrawSplashPoints)
            {
                _debugSplashPoints.Add(new DebugSplashPoint
                {
                    Position = worldPoint,
                    TimeRecorded = Time.time,
                    EnemyHits = enemyHits
                });
            }
        }

        private void ApplyTickDamage()
        {
            if (_instigator == null) return;

            _tickHitTargets.Clear();
            Collider[] hits = Physics.OverlapSphere(transform.position, _effectiveRadius, _hitLayers);

            foreach (Collider hit in hits)
            {
                AbilitySystemComponent targetASC = hit.GetComponentInParent<AbilitySystemComponent>();
                if (targetASC == null || targetASC == _instigator) continue;
                if (!_tickHitTargets.Add(targetASC)) continue;
                ApplyHitToTarget(targetASC, transform.position);
            }
        }

        /// <summary>
        /// AoE 對單一目標 ASC 套用完整命中流程:
        /// 1. GAS 扣血(透過 _hitEffect 注入 SetByCaller 傷害數值)
        /// 2. 構造 HitContext + 呼叫 IHitReceiver.OnHit — 觸發 Stagger / Knockback / 動畫 / HitStop / CameraShake
        /// 3. 觸發 _hitCueTag Cue,TargetObject 傳 ASC 根節點 — VFXCue.AttachToTarget=true 時全身受擊特效掛在敵人身上
        /// 4. 播放命中音效
        /// 受擊方在 OnHit 中設 wasBlocked(無敵/已死/完美閃避) → 跳過後續 Cue / SFX,避免無效特效
        /// </summary>
        /// <param name="targetASC">已過濾的目標 ASC(非 null、非 instigator)</param>
        /// <param name="hitOrigin">命中來源座標 — 一般 AoE 用 AoE 中心,MeteorRain 用該顆隕石落地點;決定 attackDirection / knockback 方向</param>
        private void ApplyHitToTarget(AbilitySystemComponent targetASC, Vector3 hitOrigin)
        {
            if (_hitEffect != null)
            {
                _instigator.ApplyEffectToTarget(targetASC, _hitEffect, SetByCallerTags.DAMAGE, _damage);
            }

            Vector3 targetPos = targetASC.transform.position;
            Vector3 toTarget = targetPos - hitOrigin;
            toTarget.y = 0f;
            Vector3 atkDir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.forward;

            HitContext hitCtx = new HitContext
            {
                damage = 0f,
                poiseDamage = _poiseDamage,
                knockbackForce = _knockbackForce,
                attackTier = _attackTier,
                isHeavyAttack = _attackTier == AttackTier.Heavy,
                hitPoint = targetPos,
                hitNormal = -atkDir,
                attackDirection = atkDir,
                gasDamageApplied = _hitEffect != null,
                hitStopDuration = _hitStopDuration,
                hitStopTimeScale = _hitStopTimeScale,
                cameraShakeIntensity = _cameraShakeIntensity,
            };

            IHitReceiver hitReceiver = targetASC.GetComponent<IHitReceiver>();
            if (hitReceiver != null)
            {
                hitReceiver.OnHit(ref hitCtx);
                if (hitCtx.wasBlocked) return;
            }

            if (_hitCueTag.IsValid)
            {
                float vfxScale = ResolveHitVFXScale(targetASC);
                _instigator.ExecuteGameplayCue(_hitCueTag, targetPos, null, targetASC.gameObject, Vector3.one * vfxScale);
            }
            // 直接拉的命中特效 Prefab（RangedAttackData.HitVFXPrefab）— 與 Cue 並列生效
            if (_hitVFX.Prefab != null)
            {
                HitVFXSpawner.Spawn(
                    _hitVFX.Prefab, targetPos, Quaternion.identity,
                    _hitVFX.Scale, _hitVFX.AttackerScale, _hitVFX.ScaleAllChildren,
                    _hitVFX.Lifetime,
                    _hitVFX.AttachToSurface ? targetASC.transform : null);
            }
            if (_hitSFX != null)
            {
                AudioSource.PlayClipAtPoint(_hitSFX, targetPos);
            }
            if (_hitVFX.SFX != null)
            {
                AudioSource.PlayClipAtPoint(_hitVFX.SFX, targetPos);
            }
        }

        /// <summary>
        /// 解析目標的受擊 VFX 縮放係數:
        /// 1. 有實作 IHitVFXSizeProvider 介面 → 用介面值(Boss / 特殊比例模型精確指定)
        /// 2. fallback 到 transform.lossyScale.x(SpatialScaleUtility 慣例,大型敵人 prefab 自動放大特效)
        /// </summary>
        private static float ResolveHitVFXScale(AbilitySystemComponent targetASC)
        {
            IHitVFXSizeProvider provider = targetASC.GetComponentInParent<IHitVFXSizeProvider>();
            if (provider != null) return provider.HitVFXScale;
            return SpatialScaleUtility.GetScaleFactor(targetASC.transform);
        }

        private static void SetRoot(GameObject root, bool active)
        {
            if (root != null && root.activeSelf != active) root.SetActive(active);
        }

        private AoEIndicatorAnimator _indicatorAnimator;
        private bool _indicatorAnimatorResolved;

        /// <summary>
        /// 取 _indicatorRoot 子樹下的 AoEIndicatorAnimator(快取後不重搜)。沒掛就回 null,走純 SetActive fallback。
        /// </summary>
        private AoEIndicatorAnimator GetIndicatorAnimator()
        {
            if (_indicatorAnimatorResolved) return _indicatorAnimator;
            if (_indicatorRoot != null)
            {
                _indicatorAnimator = _indicatorRoot.GetComponentInChildren<AoEIndicatorAnimator>(true);
            }
            _indicatorAnimatorResolved = true;
            return _indicatorAnimator;
        }

        /// <summary>
        /// 顯示指示器 — 有 animator 就播 Rise,沒就純 SetActive(true)
        /// </summary>
        private void ShowIndicator()
        {
            if (_indicatorRoot == null) return;
            _indicatorRoot.SetActive(true);
            AoEIndicatorAnimator anim = GetIndicatorAnimator();
            if (anim != null) anim.PlayRise();
        }

        /// <summary>
        /// 隱藏指示器 — 有 animator 就播 Release(animator 結束自己 SetActive(false)),沒就直接 SetActive(false)
        /// </summary>
        private void HideIndicator()
        {
            if (_indicatorRoot == null) return;
            AoEIndicatorAnimator anim = GetIndicatorAnimator();
            if (anim != null)
            {
                anim.PlayRelease();
            }
            else
            {
                _indicatorRoot.SetActive(false);
            }
        }

        /// <summary>
        /// 取消指示器 — 蓄力中斷時呼叫,無動畫直接還原狀態
        /// </summary>
        private void CancelIndicator()
        {
            if (_indicatorRoot == null) return;
            AoEIndicatorAnimator anim = GetIndicatorAnimator();
            if (anim != null)
            {
                anim.Cancel();
            }
            else
            {
                _indicatorRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            _isActive = false;
            _isPreview = false;
        }

        /// <summary>
        /// 把 _radius × chargeMultiplier 同步到所有子物件 DecalProjector.size.x/y + LightWall.localScale.x/z
        /// Decal:強制 ScaleInvariant,世界尺寸由本元件全權控制 → multiplier 直接套用
        /// Wall:走 hierarchy scaling,localScale 不套 multiplier(root 的 charge scale 會自動帶上去)
        /// </summary>
        private void SyncDecalSize(float chargeMultiplier = 1f)
        {
            if (!_autoSyncDecalSize) return;
            DecalProjector[] decals = GetComponentsInChildren<DecalProjector>(true);
            float diameter = _radius * 2f * chargeMultiplier;
            foreach (var decal in decals)
            {
                if (decal == null) continue;
                decal.scaleMode = DecalScaleMode.ScaleInvariant;
                Vector3 size = decal.size;
                size.x = diameter;
                size.y = diameter;
                decal.size = size;
            }
            // 光壁:scale.x/z = radius(unit-mesh 假設 radius=1);scale.y 不動,由 animator 控制
            if (_indicatorWallTransform != null)
            {
                Vector3 wallScale = _indicatorWallTransform.localScale;
                wallScale.x = _radius;
                wallScale.z = _radius;
                _indicatorWallTransform.localScale = wallScale;
            }
            // 半徑改變後重新採樣地形;Play Mode 才有效(Edit Mode 無 PhysicsScene 命中目標)
            if (_terrainConformingWall != null && Application.isPlaying)
            {
                _terrainConformingWall.ConformToGround();
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Inspector 改 Radius 時即時同步 Decal 大小
        /// </summary>
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                SyncDecalSize();
            };
        }

        /// <summary>
        /// Scene View 中時時刻刻畫出 AoE 範圍 — 設計時拖位置/調 Radius 立即看到圈
        /// </summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.18f);
            Gizmos.DrawSphere(transform.position, _radius);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, _radius);

            DrawMeteorDebugSplashes();
        }

        /// <summary>
        /// 繪製 Debug 落點:綠 = 命中敵人 / 藍 = 落地但無敵人 / 黃點 = 確切落點
        /// Play 模式中每顆 meteor 落地都會紀錄一筆,過 _debugSplashDisplayDuration 秒後消失
        /// </summary>
        private void DrawMeteorDebugSplashes()
        {
            if (!_debugDrawSplashPoints) return;
            if (_debugSplashPoints.Count == 0) return;
            if (!Application.isPlaying) return;

            float now = Time.time;
            for (int i = _debugSplashPoints.Count - 1; i >= 0; i--)
            {
                DebugSplashPoint p = _debugSplashPoints[i];
                float age = now - p.TimeRecorded;
                if (age > _debugSplashDisplayDuration)
                {
                    _debugSplashPoints.RemoveAt(i);
                    continue;
                }
                float alpha = Mathf.Clamp01(1f - age / _debugSplashDisplayDuration);
                // 飛濺範圍:有命中 = 紅 / 無命中 = 淡藍
                Color rangeColor = p.EnemyHits > 0
                    ? new Color(1f, 0.2f, 0.2f, alpha * 0.85f)
                    : new Color(0.4f, 0.7f, 1f, alpha * 0.55f);
                Gizmos.color = rangeColor;
                Gizmos.DrawWireSphere(p.Position, _meteorSplashRadius);
                // 落點正中黃色實心小球
                Gizmos.color = new Color(1f, 1f, 0.2f, alpha);
                Gizmos.DrawSphere(p.Position, 0.12f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_scaleRadiusWithCharge)
            {
                // 滿蓄力範圍(紅)
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
                Gizmos.DrawWireSphere(transform.position, _radius * _radiusChargeMultiplier);
                // 蓄力前期起始範圍(藍) — chargeTime=0 時的視覺大小
                Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
                Gizmos.DrawWireSphere(transform.position, _radius * _minScaleMultiplier);
            }
            // MeteorRain 模式:在 prefab 中心顯示 splash radius 大小(綠) — 設計師參考
            if (_tickMode == AoETickMode.MeteorRain)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.5f);
                Gizmos.DrawWireSphere(transform.position, _meteorSplashRadius);
            }
        }
#endif
    }
}
