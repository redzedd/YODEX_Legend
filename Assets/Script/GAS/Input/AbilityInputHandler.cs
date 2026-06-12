using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Interaction;
using Player.Input;

namespace GAS
{
    /// <summary>
    /// 能力輸入處理器 - 整合 Input System 和能力系統
    /// 替代並擴展原有的 InputBuffer
    /// 支援武器切換與預選功能
    /// </summary>
    [RequireComponent(typeof(AbilitySystemComponent))]
    public class AbilityInputHandler : MonoBehaviour
    {
        [Header("Combat Input Actions")]
        [Tooltip("輕攻擊輸入")]
        public InputActionReference LightAttackAction;

        [Tooltip("重攻擊輸入")]
        public InputActionReference HeavyAttackAction;

        [Header("System Input Actions")]
        [Tooltip("互動輸入")]
        public InputActionReference InteractAction;

        [Header("Ranged Attack Input")]
        [Tooltip("遠程攻擊輸入（長按蓄力/瞄準）")]
        public InputActionReference RangeAttackAction;

        [Header("Weapon Switch Input Actions")]
        [Tooltip("武器切換輸入")]
        public InputActionReference WeaponSwitchAction;

        [Tooltip("預選武器輸入（循環切換下一把武器）")]
        public InputActionReference PreselectionAction;

        [Header("Buffer Settings")]
        [Tooltip("輸入緩衝時間")]
        public float BufferTime = 0.4f;

        [Tooltip("最大緩衝數量")]
        public int MaxBufferSize = 5;

        [Header("Weapon Switch Settings")]
        [Tooltip("武器切換能力標籤")]
        public GameplayTag WeaponSwitchAbilityTag;

        [Tooltip("是否在支援窗口內自動觸發支援切換")]
        public bool AutoTriggerAssistSwitch = true;

        private AbilitySystemComponent _asc;
        private WeaponManager _weaponManager;
        private readonly Queue<BufferedInput> _inputQueue = new();

        // 輸入狀態追蹤
        private readonly Dictionary<MeleeInputType, bool> _heldInputs = new();

        // 事件訂閱追蹤 — 存下每個 Action 當前綁定的 delegate,讓 UnbindInput 能正確 -= 移除。
        // 若不追蹤,lambda/匿名訂閱無法用 -= 移除,OnEnable 再次呼叫會累積訂閱,
        // 造成一次按鍵觸發多次 OnInputStarted → 多次 EnqueueInput → 連招窗口把多筆依序吃掉自動連擊。
        private readonly Dictionary<InputActionReference, Action<InputAction.CallbackContext>> _startedDelegates = new();
        private readonly Dictionary<InputActionReference, Action<InputAction.CallbackContext>> _canceledDelegates = new();

        /// <summary>
        /// 遠程攻擊按鈕是否按住中（供 GA_RangedAttack 蓄力查詢）
        /// </summary>
        public bool IsRangeAttackHeld { get; private set; }

        /// <summary>
        /// 輕攻擊鍵本幀按下旗標 — LateUpdate 自動清空。
        /// 供 GA_RangedAttack 等能力於蓄力過程中檢測「玩家是否切換為輕攻擊」用。
        /// </summary>
        public bool LightAttackTriggered { get; private set; }

        private void Awake()
        {
            _asc = GetComponent<AbilitySystemComponent>();
            _weaponManager = GetComponent<WeaponManager>();
        }

        private void OnEnable()
        {
            // 戰鬥輸入綁定
            BindInput(LightAttackAction, MeleeInputType.LightAttack);
            BindInput(HeavyAttackAction, MeleeInputType.HeavyAttack);

            // 遠程攻擊輸入綁定
            BindRangeAttackInput(RangeAttackAction);

            // 系統輸入綁定
            BindSystemInput(InteractAction, OnInteractPressed);

            // 武器切換輸入綁定
            BindWeaponSwitchInput(WeaponSwitchAction);
            BindPreselectionInput(PreselectionAction);
        }

        private void OnDisable()
        {
            // 戰鬥輸入解綁
            UnbindInput(LightAttackAction);
            UnbindInput(HeavyAttackAction);

            // 遠程攻擊輸入解綁
            UnbindInput(RangeAttackAction);

            // 系統輸入解綁
            UnbindInput(InteractAction);

            // 武器切換輸入解綁
            UnbindInput(WeaponSwitchAction);
            UnbindInput(PreselectionAction);
        }

        private void Update()
        {
            // 清理過期輸入
            CleanExpiredInputs();
        }

        private void LateUpdate()
        {
            // 一次性旗標清空,確保下一幀重置
            LightAttackTriggered = false;
        }


        #region Input Binding

        private void BindInput(InputActionReference actionRef, MeleeInputType inputType)
        {
            if (actionRef == null || actionRef.action == null) return;
            // 先清除舊訂閱 — 保證 OnEnable 再次被呼叫時不會累積多層 lambda 監聽器
            UnbindInput(actionRef);
            actionRef.action.Enable();
            Action<InputAction.CallbackContext> startedHandler = _ => OnInputStarted(inputType);
            Action<InputAction.CallbackContext> canceledHandler = _ => OnInputCanceled(inputType);
            _startedDelegates[actionRef] = startedHandler;
            _canceledDelegates[actionRef] = canceledHandler;
            actionRef.action.started += startedHandler;
            actionRef.action.canceled += canceledHandler;
        }

        private void UnbindInput(InputActionReference actionRef)
        {
            if (actionRef == null || actionRef.action == null) return;
            if (_startedDelegates.TryGetValue(actionRef, out Action<InputAction.CallbackContext> startedHandler))
            {
                actionRef.action.started -= startedHandler;
                _startedDelegates.Remove(actionRef);
            }
            if (_canceledDelegates.TryGetValue(actionRef, out Action<InputAction.CallbackContext> canceledHandler))
            {
                actionRef.action.canceled -= canceledHandler;
                _canceledDelegates.Remove(actionRef);
            }
            actionRef.action.Disable();
        }

        /// <summary>
        /// 綁定武器切換輸入
        /// </summary>
        private void BindWeaponSwitchInput(InputActionReference actionRef)
        {
            if (actionRef == null || actionRef.action == null) return;
            UnbindInput(actionRef);
            actionRef.action.Enable();
            _startedDelegates[actionRef] = OnWeaponSwitchPressed;
            actionRef.action.started += OnWeaponSwitchPressed;
        }

        /// <summary>
        /// 綁定預選武器輸入
        /// </summary>
        private void BindPreselectionInput(InputActionReference actionRef)
        {
            if (actionRef == null || actionRef.action == null) return;
            UnbindInput(actionRef);
            actionRef.action.Enable();
            _startedDelegates[actionRef] = OnPreselectionPressed;
            actionRef.action.started += OnPreselectionPressed;
        }

        /// <summary>
        /// 武器切換按鍵按下
        /// </summary>
        private void OnWeaponSwitchPressed(InputAction.CallbackContext context)
        {
            if (SystemInputReader.Instance == null || !SystemInputReader.Instance.IsPlayerInputEnabled) return;
            TryWeaponSwitch();
        }

        /// <summary>
        /// 預選按鍵按下
        /// </summary>
        private void OnPreselectionPressed(InputAction.CallbackContext context)
        {
            if (SystemInputReader.Instance == null || !SystemInputReader.Instance.IsPlayerInputEnabled) return;
            TryCyclePreselection();
        }

        /// <summary>
        /// 綁定系統輸入 (鎖定/互動)
        /// </summary>
        private void BindSystemInput(InputActionReference actionRef, Action<InputAction.CallbackContext> callback)
        {
            if (actionRef == null || actionRef.action == null) return;
            UnbindInput(actionRef);
            actionRef.action.Enable();
            _startedDelegates[actionRef] = callback;
            actionRef.action.started += callback;
        }

        private void OnInteractPressed(InputAction.CallbackContext context)
        {
            if (SystemInputReader.Instance == null || !SystemInputReader.Instance.IsPlayerInputEnabled) return;
            if (InteractionManager.Instance != null)
                InteractionManager.Instance.TryInteract();
        }

        /// <summary>
        /// 綁定遠程攻擊輸入（含按住追蹤和能力觸發）
        /// </summary>
        private void BindRangeAttackInput(InputActionReference actionRef)
        {
            if (actionRef == null || actionRef.action == null) return;
            UnbindInput(actionRef);
            actionRef.action.Enable();
            _startedDelegates[actionRef] = OnRangeAttackStarted;
            _canceledDelegates[actionRef] = OnRangeAttackCanceled;
            actionRef.action.started += OnRangeAttackStarted;
            actionRef.action.canceled += OnRangeAttackCanceled;
        }

        private void OnRangeAttackStarted(InputAction.CallbackContext context)
        {
            IsRangeAttackHeld = true;
            if (SystemInputReader.Instance == null || !SystemInputReader.Instance.IsPlayerInputEnabled) return;
            // 推入 combo buffer（供近戰連招中偵測跨類型輸入）
            EnqueueInput(MeleeInputType.RangedAttack);
            TryActivateRangedAbility();
        }

        private void OnRangeAttackCanceled(InputAction.CallbackContext context)
        {
            IsRangeAttackHeld = false;
        }

        /// <summary>
        /// 嘗試觸發遠程攻擊能力
        /// 優先策略：
        /// 1. 當前武器為 Ranged 類型 → 直接讀 WeaponData 的 HeavyAttackAbility（蓄力/瞄準），
        ///    未設定則退到 AttackAbility（單發）。不依賴 Tag 命名慣例,避免命名空間錯位。
        /// 2. 無 WeaponManager 時走傳統 Tag fallback chain（向後相容）。
        /// 3. 拿近戰武器時右鍵不觸發遠程能力（保留給未來副手）。
        /// </summary>
        private void TryActivateRangedAbility()
        {
            if (_asc == null) return;
            if (_weaponManager != null && _weaponManager.CurrentWeapon != null)
            {
                WeaponData weapon = _weaponManager.CurrentWeapon;
                if (weapon.Type != WeaponType.Ranged) return;
                if (weapon.HeavyAttackAbility != null
                    && _asc.TryActivateAbility(weapon.HeavyAttackAbility.AbilityTag))
                {
                    return;
                }
                if (weapon.AttackAbility != null
                    && _asc.TryActivateAbility(weapon.AttackAbility.AbilityTag))
                {
                    return;
                }
                return;
            }
            // 無 WeaponManager 的傳統 Tag fallback
            if (_asc.TryActivateAbility(GameplayTags.Ability.Attack.Ranged.Light)) return;
            _asc.TryActivateAbility(GameplayTags.Ability.Attack.Ranged.Root);
        }

        private void OnInputStarted(MeleeInputType inputType)
        {
            _heldInputs[inputType] = true;
            // UI 開啟中時（Player Action Map 停用）不處理任何戰鬥輸入
            if (SystemInputReader.Instance == null || !SystemInputReader.Instance.IsPlayerInputEnabled) return;

            if (inputType == MeleeInputType.LightAttack) LightAttackTriggered = true;

            // 添加到緩衝
            EnqueueInput(inputType);

            // 嘗試直接觸發能力
            TryTriggerAbilityFromInput(inputType);
        }

        private void OnInputCanceled(MeleeInputType inputType)
        {
            // 清除按住狀態
            _heldInputs[inputType] = false;
        }

        #endregion

        #region Input Buffer

        private void EnqueueInput(MeleeInputType inputType)
        {
            _inputQueue.Enqueue(new BufferedInput
            {
                Type = inputType,
                Timestamp = Time.time
            });

            // 限制緩衝大小
            while (_inputQueue.Count > MaxBufferSize)
            {
                _inputQueue.Dequeue();
            }
        }

        private void CleanExpiredInputs()
        {
            float now = Time.time;
            while (_inputQueue.Count > 0 && now - _inputQueue.Peek().Timestamp > BufferTime)
            {
                _inputQueue.Dequeue();
            }
        }

        /// <summary>
        /// 查看下一個緩衝輸入
        /// </summary>
        public MeleeInputType PeekInput()
        {
            CleanExpiredInputs();
            return _inputQueue.Count > 0 ? _inputQueue.Peek().Type : MeleeInputType.None;
        }

        /// <summary>
        /// 消耗緩衝輸入
        /// </summary>
        public void ConsumeInput()
        {
            CleanExpiredInputs();
            if (_inputQueue.Count > 0)
            {
                _inputQueue.Dequeue();
            }
        }

        /// <summary>
        /// 是否有緩衝輸入
        /// </summary>
        public bool HasInput()
        {
            CleanExpiredInputs();
            return _inputQueue.Count > 0;
        }

        /// <summary>
        /// 檢查指定輸入是否被按住
        /// </summary>
        public bool IsInputHeld(MeleeInputType inputType)
        {
            return _heldInputs.TryGetValue(inputType, out bool held) && held;
        }

        /// <summary>
        /// 清空所有緩衝輸入
        /// </summary>
        public void ClearBuffer()
        {
            _inputQueue.Clear();
        }

        #endregion

        #region Ability Triggering

        private void TryTriggerAbilityFromInput(MeleeInputType inputType)
        {
            if (_asc == null) return;
            GameplayTag abilityTag = inputType switch
            {
                MeleeInputType.LightAttack => GameplayTags.Ability.Attack.Light,
                MeleeInputType.HeavyAttack => GameplayTags.Ability.Attack.Heavy,
                _ => default
            };
            if (!abilityTag.IsValid) return;
            // 成功觸發能力就把這筆輸入消耗掉,避免被連招偵測誤判為下一段輸入
            if (_asc.TryActivateAbility(abilityTag))
            {
                ConsumeInput();
            }
        }

        /// <summary>
        /// 手動觸發能力
        /// </summary>
        public bool TriggerAbility(GameplayTag abilityTag)
        {
            if (_asc == null || !abilityTag.IsValid) return false;
            return _asc.TryActivateAbility(abilityTag);
        }

        /// <summary>
        /// 取消能力
        /// </summary>
        public void CancelAbility(GameplayTag abilityTag)
        {
            _asc?.CancelAbility(abilityTag);
        }

        #endregion

        #region Weapon Switch

        /// <summary>
        /// 嘗試執行武器切換
        /// </summary>
        public bool TryWeaponSwitch()
        {
            if (_weaponManager == null)
            {
                // 如果沒有 WeaponManager，嘗試重新獲取
                _weaponManager = GetComponent<WeaponManager>();
                if (_weaponManager == null) return false;
            }

            // 檢查是否在支援窗口內
            bool isInAssistWindow = _asc != null && _asc.OwnedTags.HasTag(GameplayTags.State.AssistWindow);

            if (isInAssistWindow && AutoTriggerAssistSwitch)
            {
                // 在支援窗口內，觸發支援切換
                return TryTriggerAssistSwitch();
            }
            else
            {
                // 普通切換
                return TryTriggerNormalSwitch();
            }
        }

        /// <summary>
        /// 嘗試觸發普通武器切換
        /// </summary>
        private bool TryTriggerNormalSwitch()
        {
            // 方法 1：使用武器切換能力
            if (WeaponSwitchAbilityTag.IsValid && _asc != null)
            {
                return _asc.TryActivateAbility(WeaponSwitchAbilityTag);
            }

            // 方法 2：直接調用 WeaponManager
            if (_weaponManager != null)
            {
                return _weaponManager.SwitchToNext();
            }

            return false;
        }

        /// <summary>
        /// 嘗試觸發支援切換（招架/迴避支援）
        /// </summary>
        private bool TryTriggerAssistSwitch()
        {
            if (_weaponManager == null || _asc == null) return false;

            // 檢查支援點數（未來實作）
            CombatAttributeSet attrSet = _asc.GetAttributeSet<CombatAttributeSet>();
            // if (attrSet != null && attrSet.AssistPoints.CurrentValue < 1f) return false;

            // 獲取下一把武器的支援能力
            WeaponData nextWeapon = _weaponManager.PreselectedWeapon;
            if (nextWeapon == null) return false;

            GameplayAbility assistAbility = nextWeapon.GetAssistAbility();
            if (assistAbility == null)
            {
                // 沒有支援能力，執行普通切換
                return TryTriggerNormalSwitch();
            }

            // 先執行武器切換
            bool switched = _weaponManager.SwitchToNext();
            if (!switched) return false;

            // 然後觸發支援能力
            return _asc.TryActivateAbility(assistAbility.AbilityTag);
        }

        /// <summary>
        /// 嘗試循環預選武器
        /// </summary>
        public bool TryCyclePreselection()
        {
            if (_weaponManager == null)
            {
                _weaponManager = GetComponent<WeaponManager>();
                if (_weaponManager == null) return false;
            }

            _weaponManager.CyclePreselection();
            return true;
        }

        /// <summary>
        /// 獲取 WeaponManager 引用
        /// </summary>
        public WeaponManager GetWeaponManager()
        {
            if (_weaponManager == null)
            {
                _weaponManager = GetComponent<WeaponManager>();
            }
            return _weaponManager;
        }

        #endregion

        private struct BufferedInput
        {
            public MeleeInputType Type;
            public float Timestamp;
        }
    }
}
