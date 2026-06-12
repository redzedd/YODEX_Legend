using System;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using Enemy.AttackSystem;

namespace GAS
{
    /// <summary>
    /// 武器管理器 - 管理玩家的武器切換系統
    /// 實現類似絕區零的極限支援系統中的角色切換功能
    /// </summary>
    [RequireComponent(typeof(AbilitySystemComponent))]
    public class WeaponManager : MonoBehaviour
    {
        [Header("Weapon Configuration")]
        [Tooltip("武器列表（按順序循環切換）")]
        [SerializeField] private List<WeaponData> _weapons = new();

        [Tooltip("初始武器索引")]
        [SerializeField] private int _startingWeaponIndex = 0;

        [Header("Components")]
        [Tooltip("Animancer 組件")]
        [SerializeField] private AnimancerComponent _animancer;

        [Tooltip("角色模型掛載點")]
        [SerializeField] private Transform _modelRoot;

        [Header("Settings")]
        [Tooltip("切換冷卻時間")]
        [SerializeField] private float _switchCooldown = 0.2f;

        [Tooltip("是否在切換時取消當前能力")]
        [SerializeField] private bool _cancelAbilityOnSwitch = true;

        [Header("Afterimage")]
        [Tooltip("殘影系統組件")]
        [SerializeField] private WeaponRuntimeState _runtimeState;

        [Tooltip("是否在攻擊途中切換時創建殘影")]
        [SerializeField] private bool _createAfterImageOnAttack = true;

        [Tooltip("非攻擊狀態(站立/移動/跳躍/閃避等)切武器時,是否創建凍結姿態殘影。\n" +
                 "✓ 啟用:殘影固定在切武器當下的動畫姿態,完全不動,然後淡出\n" +
                 "✗ 停用:非攻擊狀態切武器不產生殘影")]
        [SerializeField] private bool _createFrozenGhostOnNonAttack = true;

        [Tooltip("殘影是否完整接管剩餘攻擊(命中判定 + 傷害 + 特效)。\n\n" +
                 "✓ 啟用(預設):殘影把攻擊做完,有完整命中判定、傷害、VFX/SFX — 角色「分身完成攻擊」的感覺。\n" +
                 "✗ 停用:殘影只播視覺動畫,不打判定不噴特效 — 接近純視覺殘影(舊版行為)。\n\n" +
                 "需先勾選『Create After Image On Attack』本欄才會生效。\n" +
                 "目前僅近戰攻擊支援接管(蓄力/瞄準遠程待後續實作)")]
        [SerializeField] private bool _transferAttackToAfterImage = true;

        [Header("Defensive Assist (招架支援)")]

        [Tooltip("啟用招架攔截。有敵人黃光亮起時，按換武器鍵會觸發招架支援，不執行一般換人")]
        [SerializeField] private bool _enableDefensiveAssist = true;

        [Tooltip("招架支援的最大觸發距離（公尺）。超出此距離的可招架敵人不會被攔截到")]
        [SerializeField] private float _parryRange = 8f;

        [Header("Debug")]
        [SerializeField] private bool _debugMode = false;

        // === 狀態 ===
        private int _currentIndex = 0;
        private int _preselectedOffset = 1;
        private float _lastSwitchTime = -999f;
        private GameObject _currentModelInstance;
        private AnimancerState _currentAnimationState;

        // === 組件引用 ===
        private AbilitySystemComponent _asc;
        private NewGASPlayerController _newPlayerController;

        // === 事件 ===

        /// <summary>
        /// 武器切換前觸發
        /// 參數：(舊武器, 新武器)
        /// </summary>
        public event Action<WeaponData, WeaponData> OnWeaponSwitchStart;

        /// <summary>
        /// 武器切換完成後觸發
        /// 參數：(新武器)
        /// </summary>
        public event Action<WeaponData> OnWeaponSwitchComplete;

        /// <summary>
        /// 預選武器變更時觸發
        /// 參數：(預選武器)
        /// </summary>
        public event Action<WeaponData> OnPreselectionChanged;

        /// <summary>
        /// 招架支援觸發時廣播。
        /// 參數：(被招架的敵人攻擊執行器)
        /// 由 Step 7 的角色側招架反擊技能（GA_DefensiveAssist）訂閱本事件來播專屬反擊動作。
        /// </summary>
        public event Action<EnemyAttackExecutor> OnParryAssistTriggered;

        // === 屬性 ===

        /// <summary>
        /// 當前武器索引
        /// </summary>
        public int CurrentIndex => _currentIndex;

        /// <summary>
        /// 當前武器資料
        /// </summary>
        public WeaponData CurrentWeapon => _weapons.Count > 0 ? _weapons[_currentIndex] : null;

        /// <summary>
        /// 預選偏移量（1 = 下一把，2 = 跳過一把...）
        /// </summary>
        public int PreselectedOffset => _preselectedOffset;

        /// <summary>
        /// 預選的下一把武器
        /// </summary>
        public WeaponData PreselectedWeapon => GetWeaponAtOffset(_preselectedOffset);

        /// <summary>
        /// 武器數量
        /// </summary>
        public int WeaponCount => _weapons.Count;

        /// <summary>
        /// 所有武器列表（唯讀）
        /// </summary>
        public IReadOnlyList<WeaponData> Weapons => _weapons;

        /// <summary>
        /// 是否可以切換武器 — 綜合冷卻、武器數量、玩家狀態三項條件。
        /// </summary>
        public bool CanSwitch => IsOffCooldown && _weapons.Count > 1 && IsPlayerStateSwitchable;

        /// <summary>
        /// 冷卻時間是否已過。
        /// </summary>
        private bool IsOffCooldown => Time.time - _lastSwitchTime >= _switchCooldown;

        /// <summary>
        /// 玩家當前狀態是否允許切武器 — Locomotion 與 Ability(攻擊中)皆放行。
        /// 攻擊中切換由殘影系統接手完成剩餘攻擊判定 / 特效(見 Step 2 之後)。
        /// HitStun / Dead 仍一律阻擋:受擊硬直期間切武器會讓 SyncHitStunTopState 因
        /// _stateMachine.Current 不再是 Hit/Knockback 而提前解除硬直;死亡為單向狀態。
        /// 未接 NewGASPlayerController 時回傳 true(無 Controller 則無狀態守衛)。
        /// </summary>
        private bool IsPlayerStateSwitchable
        {
            get
            {
                if (_newPlayerController == null)
                {
                    return true;
                }
                TopState top = _newPlayerController.CurrentTopState;
                return top == TopState.Locomotion || top == TopState.Ability;
            }
        }

        /// <summary>
        /// 當前模型實例
        /// </summary>
        public GameObject CurrentModelInstance => _currentModelInstance;

        /// <summary>
        /// 能力系統組件
        /// </summary>
        public AbilitySystemComponent ASC => _asc;

        /// <summary>
        /// 殘影系統
        /// </summary>
        public WeaponRuntimeState RuntimeState => _runtimeState;

        #region Unity Lifecycle

        private void Awake()
        {
            _asc = GetComponent<AbilitySystemComponent>();
            _newPlayerController = GetComponent<NewGASPlayerController>();

            if (_animancer == null)
            {
                _animancer = GetComponent<AnimancerComponent>();
            }

            if (_modelRoot == null)
            {
                _modelRoot = transform;
            }

            // 獲取或創建殘影系統
            if (_runtimeState == null)
            {
                _runtimeState = GetComponent<WeaponRuntimeState>();
                if (_runtimeState == null)
                {
                    _runtimeState = gameObject.AddComponent<WeaponRuntimeState>();
                }
            }
        }

        private void Start()
        {
            // 初始化武器
            if (_weapons.Count > 0)
            {
                _currentIndex = Mathf.Clamp(_startingWeaponIndex, 0, _weapons.Count - 1);
                InitializeWeapon(CurrentWeapon);
            }
            else
            {
                Debug.LogWarning("[WeaponManager] No weapons configured!");
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 在當前位置生成一個凍結姿態殘影（與非攻擊狀態切武器的殘影同款）。
        /// 供閃避無敵被攻擊等情境呼叫,做「原地定格殘影」回饋。
        /// </summary>
        public void SpawnFreezePoseAfterImage()
        {
            if (_runtimeState == null || _currentModelInstance == null)
            {
                return;
            }
            AnimancerState currentAnim = (_animancer != null && _animancer.Layers.Count > 0)
                ? _animancer.Layers[0].CurrentState
                : null;
            AfterImageInstance ghost = _runtimeState.CreateAfterImage(
                _currentModelInstance, currentAnim, CurrentWeapon, freezePose: true);
            if (ghost != null && ghost.GameObject != null)
            {
                AfterImagePositionLock posLock = ghost.GameObject.AddComponent<AfterImagePositionLock>();
                posLock.LockHere(transform.position, transform.rotation);
            }
        }

        /// <summary>
        /// 切換到下一把武器
        /// </summary>
        /// <returns>是否成功切換</returns>
        public bool SwitchToNext()
        {
            // 招架攔截：場上有可招架敵人時，觸發招架支援並阻止一般換武器流程，
            // 避免殘影系統 / 能力切換 / 模型替換等動作蓋過招架反擊
            if (TryTriggerParryAssist())
            {
                return true;
            }

            if (!CanSwitch)
            {
                if (_debugMode)
                {
                    LogSwitchBlocked();
                }
                return false;
            }

            WeaponData oldWeapon = CurrentWeapon;
            int newIndex = (_currentIndex + _preselectedOffset) % _weapons.Count;
            WeaponData newWeapon = _weapons[newIndex];

            // 觸發切換開始事件
            OnWeaponSwitchStart?.Invoke(oldWeapon, newWeapon);

            // 執行切換
            ExecuteSwitch(newIndex);

            // 重置預選
            _preselectedOffset = 1;

            // 更新冷卻時間
            _lastSwitchTime = Time.time;

            // 觸發切換完成事件
            OnWeaponSwitchComplete?.Invoke(CurrentWeapon);

            if (_debugMode)
            {
                Debug.Log($"[WeaponManager] Switched from {oldWeapon?.WeaponName} to {CurrentWeapon?.WeaponName}");
            }

            return true;
        }

        /// <summary>
        /// 切換到指定索引的武器
        /// </summary>
        public bool SwitchToIndex(int index)
        {
            if (index < 0 || index >= _weapons.Count || index == _currentIndex)
            {
                return false;
            }

            if (!CanSwitch)
            {
                if (_debugMode)
                {
                    LogSwitchBlocked();
                }
                return false;
            }

            WeaponData oldWeapon = CurrentWeapon;
            WeaponData newWeapon = _weapons[index];

            OnWeaponSwitchStart?.Invoke(oldWeapon, newWeapon);
            ExecuteSwitch(index);
            _preselectedOffset = 1;
            _lastSwitchTime = Time.time;
            OnWeaponSwitchComplete?.Invoke(CurrentWeapon);

            return true;
        }

        /// <summary>
        /// 循環切換預選武器
        /// 例如：當前使用 B，原本下一把是 C，按預選後變成 D
        /// </summary>
        public void CyclePreselection()
        {
            if (_weapons.Count <= 2)
            {
                // 只有兩把或更少武器時，預選無意義
                return;
            }

            // 循環偏移量：1 -> 2 -> 3 -> ... -> (Count-1) -> 1
            _preselectedOffset = (_preselectedOffset % (_weapons.Count - 1)) + 1;

            OnPreselectionChanged?.Invoke(PreselectedWeapon);

            if (_debugMode)
            {
                Debug.Log($"[WeaponManager] Preselection changed to: {PreselectedWeapon?.WeaponName} (offset: {_preselectedOffset})");
            }
        }

        /// <summary>
        /// 重置預選為下一把武器
        /// </summary>
        public void ResetPreselection()
        {
            if (_preselectedOffset != 1)
            {
                _preselectedOffset = 1;
                OnPreselectionChanged?.Invoke(PreselectedWeapon);
            }
        }

        /// <summary>
        /// 獲取指定偏移量的武器
        /// </summary>
        public WeaponData GetWeaponAtOffset(int offset)
        {
            if (_weapons.Count == 0) return null;
            int index = ((_currentIndex + offset) % _weapons.Count + _weapons.Count) % _weapons.Count;
            return _weapons[index];
        }

        /// <summary>
        /// 獲取指定索引的武器
        /// </summary>
        public WeaponData GetWeaponAtIndex(int index)
        {
            if (index < 0 || index >= _weapons.Count) return null;
            return _weapons[index];
        }

        /// <summary>
        /// 檢查當前是否有攻擊能力在執行
        /// </summary>
        public bool IsAttackActive()
        {
            if (_asc == null) return false;

            foreach (GameplayAbilitySpec spec in _asc.GetAllAbilities())
            {
                if (!spec.IsActive) continue;
                if (spec.AbilityDef is GA_MeleeAttack || spec.AbilityDef is GA_RangedAttack)
                {
                    return true;
                }

                if (spec.AbilityDef.AbilityTag.MatchesTagHierarchy(GameplayTags.Ability.Attack.Root))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 添加武器到列表
        /// </summary>
        public void AddWeapon(WeaponData weapon)
        {
            if (weapon != null && !_weapons.Contains(weapon))
            {
                _weapons.Add(weapon);
            }
        }

        /// <summary>
        /// 從列表移除武器
        /// </summary>
        public bool RemoveWeapon(WeaponData weapon)
        {
            int index = _weapons.IndexOf(weapon);
            if (index < 0) return false;

            // 不能移除當前武器
            if (index == _currentIndex && _weapons.Count > 1)
            {
                // 先切換到其他武器
                SwitchToNext();
            }

            _weapons.Remove(weapon);

            // 調整當前索引
            if (_currentIndex >= _weapons.Count)
            {
                _currentIndex = Mathf.Max(0, _weapons.Count - 1);
            }

            return true;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 招架攔截判斷。若 Registry 內有可招架敵人且在範圍內，觸發招架支援並回傳 true（呼叫者應停止一般換武器流程）。
        /// </summary>
        private bool TryTriggerParryAssist()
        {
            if (!_enableDefensiveAssist)
            {
                return false;
            }
            EnemyAttackExecutor target = ParryableTargetRegistry.GetClosestInRange(transform.position, _parryRange);
            if (target == null)
            {
                return false;
            }
            // 招架觸發瞬間恢復敵人動畫 1x 速度（若之前因 HitStart 過早被減速）
            // — 玩家按招架後，敵人立刻加速到 HitStart，視覺上「玩家彈刀，敵人立刻砍下來被接住」
            target.RestoreNormalAnimSpeed();
            // 不立即中斷敵人攻擊 — 由 DefensiveAssistResponder 訂閱 OnHitWindowOpen，
            // 在敵人攻擊判定真的「砍下來」那一刻才中斷（接刀時機）
            OnParryAssistTriggered?.Invoke(target);
            if (_debugMode)
            {
                Debug.Log($"[WeaponManager] ★ 招架支援觸發！目標：{target.name}");
            }
            return true;
        }

        /// <summary>
        /// 執行武器切換
        /// </summary>
        private void ExecuteSwitch(int newIndex)
        {
            WeaponData oldWeapon = CurrentWeapon;
            _currentIndex = newIndex;
            WeaponData newWeapon = CurrentWeapon;

            // 在 cancel 之前先擷取攻擊狀態 — 否則 CancelCurrentAbilities 會把 ability 標為非 active,
            // ToSnapshot 無法讀到 RuntimeData 的當前狀態與動畫時間,殘影接手就會丟失資訊
            MeleeAttackSnapshot meleeSnapshot = GA_MeleeAttack.TryCaptureSnapshot(_asc);
            RangedAttackSnapshot rangedSnapshot = GA_RangedAttack.TryCaptureSnapshot(_asc);
            bool wasAttacking = IsAttackActive();
            AnimancerState currentAnim = GetCurrentAnimationState();

            // 取消當前能力（如果配置為取消）
            if (_cancelAbilityOnSwitch)
            {
                CancelCurrentAbilities();
            }

            // 切換模型(攜帶快照,讓殘影接手剩餘攻擊)
            SwitchModel(oldWeapon, newWeapon, meleeSnapshot, rangedSnapshot, currentAnim, wasAttacking);

            // 更新能力
            UpdateAbilities(oldWeapon, newWeapon);
        }

        /// <summary>
        /// 初始化武器（遊戲開始時）
        /// </summary>
        private void InitializeWeapon(WeaponData weapon)
        {
            if (weapon == null) return;

            // 創建模型
            if (weapon.CharacterModelPrefab != null)
            {
                _currentModelInstance = Instantiate(weapon.CharacterModelPrefab, _modelRoot);
                _currentModelInstance.transform.localPosition = Vector3.zero;
                _currentModelInstance.transform.localRotation = Quaternion.identity;

                // 更新 Animancer 引用到新模型 + 交付 per-weapon SO 給新 Controller
                UpdateAnimancerReference(weapon);
            }

            // 授予能力
            GrantWeaponAbilities(weapon);

            if (_debugMode)
            {
                Debug.Log($"[WeaponManager] Initialized with weapon: {weapon.WeaponName}");
            }
        }

        /// <summary>
        /// 切換角色模型
        /// </summary>
        /// <param name="meleeSnapshot">近戰攻擊快照(已在 ExecuteSwitch 於 cancel 前擷取),null 表示非近戰攻擊中</param>
        /// <param name="rangedSnapshot">遠程 QuickFire 快照(蓄力/瞄準模式為 null,只給純視覺殘影)</param>
        /// <param name="currentAnim">cancel 前的當前動畫狀態,給殘影視覺播放用</param>
        /// <param name="wasAttacking">cancel 前是否有任何攻擊 ability 活動中(近戰或遠程)</param>
        private void SwitchModel(WeaponData oldWeapon, WeaponData newWeapon, MeleeAttackSnapshot meleeSnapshot, RangedAttackSnapshot rangedSnapshot, AnimancerState currentAnim, bool wasAttacking)
        {
            // 在銷毀舊模型前先擷取 NormalizedTime 與 slot,否則 Destroy 後 Unity fake-null 會讓 AnimancerState 無法讀取
            if (_newPlayerController != null)
            {
                _newPlayerController.PrepareForModelSwitch();
            }

            // 殘影分兩條路:
            // A. 攻擊中切換 → 動態殘影(RM Driver + 可能掛 Executor 接管攻擊判定)
            // B. 非攻擊狀態切換 → 凍結姿態殘影(SampleAnimation 烤姿態 + Animator 關掉 + PositionLock 鎖位)
            bool createAttackGhost = wasAttacking && _createAfterImageOnAttack;
            bool createFrozenGhost = !wasAttacking && _createFrozenGhostOnNonAttack;

            if ((createAttackGhost || createFrozenGhost) && _currentModelInstance != null && _runtimeState != null)
            {
                AfterImageInstance ghost = _runtimeState.CreateAfterImage(
                    _currentModelInstance, currentAnim, oldWeapon,
                    freezePose: createFrozenGhost);

                if (ghost != null && ghost.GameObject != null)
                {
                    if (createAttackGhost)
                    {
                        // 動態殘影:RM Driver + Executor
                        AfterImageRootMotionDriver rmDriver = ghost.GameObject.AddComponent<AfterImageRootMotionDriver>();
                        rmDriver.Begin(transform.position, transform.rotation);

                        bool canTransfer = _transferAttackToAfterImage;
                        if (canTransfer && meleeSnapshot != null)
                        {
                            MeleeAttackGhostExecutor executor = ghost.GameObject.AddComponent<MeleeAttackGhostExecutor>();
                            executor.Initialize(meleeSnapshot);
                            if (_debugMode)
                            {
                                Debug.Log($"[WeaponManager] Melee ghost executor attached for {oldWeapon?.WeaponName} (resume at {meleeSnapshot.ResumeTime:F2}s)");
                            }
                        }
                        else if (canTransfer && rangedSnapshot != null)
                        {
                            RangedAttackGhostExecutor executor = ghost.GameObject.AddComponent<RangedAttackGhostExecutor>();
                            executor.Initialize(rangedSnapshot);
                            if (_debugMode)
                            {
                                Debug.Log($"[WeaponManager] Ranged ghost executor attached for {oldWeapon?.WeaponName} (resume at {rangedSnapshot.ResumeTime:F2}s)");
                            }
                        }
                        else if (_debugMode)
                        {
                            string reason = !_transferAttackToAfterImage ? "TransferAttackToAfterImage disabled"
                                : "no supported snapshot (charge ranged not yet supported)";
                            Debug.Log($"[WeaponManager] Visual-only animated afterimage for {oldWeapon?.WeaponName} ({reason})");
                        }
                    }
                    else
                    {
                        // 凍結殘影:Animator/Animancer 已在 CreateAfterImage(freezePose:true) 內被 disable,
                        // PositionLock 鎖住 transform 不讓任何外力移動
                        AfterImagePositionLock posLock = ghost.GameObject.AddComponent<AfterImagePositionLock>();
                        posLock.LockHere(transform.position, transform.rotation);
                        if (_debugMode)
                        {
                            Debug.Log($"[WeaponManager] Frozen-pose afterimage for {oldWeapon?.WeaponName}");
                        }
                    }
                }
            }

            // 銷毀舊模型（殘影已經創建了副本）
            if (_currentModelInstance != null)
            {
                Destroy(_currentModelInstance);
                _currentModelInstance = null;
            }

            // 創建新模型
            if (newWeapon?.CharacterModelPrefab != null)
            {
                _currentModelInstance = Instantiate(newWeapon.CharacterModelPrefab, _modelRoot);
                _currentModelInstance.transform.localPosition = Vector3.zero;
                _currentModelInstance.transform.localRotation = Quaternion.identity;

                // 更新 Animancer 引用到新模型 + 交付 per-weapon SO 給新 Controller
                UpdateAnimancerReference(newWeapon);
            }

            // 播放切換特效
            PlaySwitchVFX(oldWeapon, newWeapon);
        }

        /// <summary>
        /// 獲取當前播放的動畫狀態
        /// </summary>
        private AnimancerState GetCurrentAnimationState()
        {
            if (_animancer == null) return null;
            return _animancer.States.Current;
        }

        /// <summary>
        /// 播放切換特效。VFX 依「最上方父物件」(transform.root) 的 Scale 等比例放大縮小，
        /// 同時把粒子系統切到 Hierarchy 模式讓發射大小一起跟著縮放、避免角色巨大化時粒子過小。
        /// </summary>
        private void PlaySwitchVFX(WeaponData oldWeapon, WeaponData newWeapon)
        {
            float scaleFactor = SpatialScaleUtility.GetScaleFactor(transform.root);

            // 退場特效
            if (oldWeapon?.SwitchOutVFXPrefab != null)
            {
                SpawnScaledSwitchVFX(oldWeapon.SwitchOutVFXPrefab, scaleFactor);
            }

            // 進場特效
            if (newWeapon?.SwitchInVFXPrefab != null)
            {
                SpawnScaledSwitchVFX(newWeapon.SwitchInVFXPrefab, scaleFactor);
            }

            // 播放音效
            if (newWeapon?.SwitchSFX != null)
            {
                AudioSource.PlayClipAtPoint(newWeapon.SwitchSFX, transform.position);
            }
        }

        private void SpawnScaledSwitchVFX(GameObject prefab, float scaleFactor)
        {
            GameObject vfx = Instantiate(prefab, transform.position, transform.rotation);
            vfx.transform.localScale *= scaleFactor;
            ApplyHierarchyScalingMode(vfx);
            Destroy(vfx, 2f);
        }

        private static void ApplyHierarchyScalingMode(GameObject root)
        {
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        /// <summary>
        /// 更新能力（移除舊武器能力，添加新武器能力）
        /// </summary>
        private void UpdateAbilities(WeaponData oldWeapon, WeaponData newWeapon)
        {
            if (_asc == null) return;

            // 移除舊武器的能力
            if (oldWeapon != null)
            {
                RevokeWeaponAbilities(oldWeapon);
            }

            // 授予新武器的能力
            if (newWeapon != null)
            {
                GrantWeaponAbilities(newWeapon);
            }
        }

        /// <summary>
        /// 授予武器的能力
        /// </summary>
        private void GrantWeaponAbilities(WeaponData weapon)
        {
            if (_asc == null || weapon == null) return;

            if (weapon.AttackAbility != null)
            {
                _asc.GiveAbility(weapon.AttackAbility);
            }

            if (weapon.HeavyAttackAbility != null)
            {
                _asc.GiveAbility(weapon.HeavyAttackAbility);
            }

            if (weapon.DodgeAbility != null)
            {
                _asc.GiveAbility(weapon.DodgeAbility);
            }

            if (weapon.ParryAssistAbility != null)
            {
                _asc.GiveAbility(weapon.ParryAssistAbility);
            }

            if (weapon.DodgeAssistAbility != null)
            {
                _asc.GiveAbility(weapon.DodgeAssistAbility);
            }
        }

        /// <summary>
        /// 撤銷武器的能力
        /// </summary>
        private void RevokeWeaponAbilities(WeaponData weapon)
        {
            if (_asc == null || weapon == null) return;

            if (weapon.AttackAbility != null)
            {
                _asc.RemoveAbility(weapon.AttackAbility.AbilityTag);
            }

            if (weapon.HeavyAttackAbility != null)
            {
                _asc.RemoveAbility(weapon.HeavyAttackAbility.AbilityTag);
            }

            if (weapon.DodgeAbility != null)
            {
                _asc.RemoveAbility(weapon.DodgeAbility.AbilityTag);
            }

            if (weapon.ParryAssistAbility != null)
            {
                _asc.RemoveAbility(weapon.ParryAssistAbility.AbilityTag);
            }

            if (weapon.DodgeAssistAbility != null)
            {
                _asc.RemoveAbility(weapon.DodgeAssistAbility.AbilityTag);
            }
        }

        /// <summary>
        /// 更新 Animancer 組件引用,並把 per-weapon 的 Locomotion / HitReaction 資料交付給 NewGASPlayerController。
        /// </summary>
        private void UpdateAnimancerReference(WeaponData weapon)
        {
            if (_currentModelInstance == null) return;

            AnimancerComponent newAnimancer = _currentModelInstance.GetComponent<AnimancerComponent>();
            if (newAnimancer == null)
            {
                Debug.LogWarning("[WeaponManager] New model doesn't have AnimancerComponent!");
                return;
            }

            _animancer = newAnimancer;

            // 新 NewGASPlayerController — 整包把 per-weapon 的四個 SO 交付,由其內部重建 Locomotion 狀態機
            if (_newPlayerController != null && weapon != null)
            {
                _newPlayerController.SetupModel(
                    newAnimancer,
                    weapon.LocomotionConfig,
                    weapon.LocomotionAnimations,
                    weapon.HitReactionData,
                    weapon.DeathData);
            }

            if (_debugMode)
            {
                Debug.Log($"[WeaponManager] Updated Animancer reference to new model (weapon={weapon?.WeaponName})");
            }
        }

        /// <summary>
        /// 輸出切武器被阻擋的原因 — 依優先級顯示第一個未通過的守門(冷卻 → 武器數 → 玩家狀態)。
        /// </summary>
        private void LogSwitchBlocked()
        {
            if (!IsOffCooldown)
            {
                float remaining = _switchCooldown - (Time.time - _lastSwitchTime);
                Debug.Log($"[WeaponManager] Cannot switch: on cooldown ({remaining:F2}s remaining)");
                return;
            }
            if (_weapons.Count <= 1)
            {
                Debug.Log("[WeaponManager] Cannot switch: only one weapon configured");
                return;
            }
            if (!IsPlayerStateSwitchable)
            {
                TopState topState = _newPlayerController != null ? _newPlayerController.CurrentTopState : TopState.Locomotion;
                Debug.Log($"[WeaponManager] Cannot switch: player in {topState} (HitStun / Dead 期間禁用切武器)");
                return;
            }
            Debug.Log("[WeaponManager] Cannot switch: unknown reason");
        }

        /// <summary>
        /// 取消當前所有能力
        /// </summary>
        private void CancelCurrentAbilities()
        {
            if (_asc == null) return;

            foreach (GameplayAbilitySpec spec in _asc.GetAllAbilities())
            {
                if (spec.IsActive) spec.CancelAbility();
            }
        }

        #endregion
    }
}
