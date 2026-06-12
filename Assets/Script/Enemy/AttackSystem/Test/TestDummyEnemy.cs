using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Enemy.AttackSystem.Test
{
    /// <summary>
    /// 測試用假人。掛在 GameObject 上、設好攻擊清單與觸發鍵，按鍵即可觸發攻擊。
    /// 用來在沒有完整 AI 邏輯前，測試 EnemyAttackProfile 與 EnemyAttackExecutor 的時序。
    /// </summary>
    [RequireComponent(typeof(EnemyAttackExecutor))]
    public class TestDummyEnemy : MonoBehaviour
    {
        // ────── Inspector 設定 ──────
        [Header("攻擊清單")]

        [SerializeField]
        [Tooltip("可使用的攻擊招式清單。按鍵觸發時會依「選擇模式」從中挑一招執行")]
        private List<EnemyAttackProfile> _attackPool = new List<EnemyAttackProfile>();

        [SerializeField]
        [Tooltip("選擇下一招的方式：\n隨機：每次按鍵隨機挑一招\n循序：依清單順序循環")]
        private AttackSelectMode _selectMode = AttackSelectMode.Random;

        [Header("觸發方式")]

        [SerializeField]
        [Tooltip("觸發攻擊的鍵盤按鍵")]
        private Key _triggerKey = Key.Q;

        [SerializeField]
        [Tooltip("勾選後會每隔指定秒數自動觸發一次攻擊，方便連續測試")]
        private bool _autoRepeat = false;

        [SerializeField]
        [Tooltip("自動觸發的間隔（秒）。建議 1~3 秒")]
        private float _autoRepeatInterval = 2f;

        // ────── 私有狀態 ──────
        private EnemyAttackExecutor _executor;
        private int _sequenceIndex;
        private float _autoRepeatTimer;

        // ────── Unity 生命週期 ──────
        private void Awake()
        {
            _executor = GetComponent<EnemyAttackExecutor>();
        }

        private void Update()
        {
            HandleKeyInput();
            HandleAutoRepeat();
        }

        // ────── 輸入處理 ──────
        private void HandleKeyInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }
            if (Keyboard.current[_triggerKey].wasPressedThisFrame)
            {
                TryExecuteAttack();
            }
        }

        private void HandleAutoRepeat()
        {
            if (!_autoRepeat)
            {
                _autoRepeatTimer = 0f;
                return;
            }
            if (_executor.IsAttacking)
            {
                _autoRepeatTimer = 0f;
                return;
            }
            _autoRepeatTimer += Time.deltaTime;
            if (_autoRepeatTimer >= _autoRepeatInterval)
            {
                _autoRepeatTimer = 0f;
                TryExecuteAttack();
            }
        }

        // ────── 攻擊選擇 ──────
        private void TryExecuteAttack()
        {
            if (_attackPool == null || _attackPool.Count == 0)
            {
                Debug.LogWarning("[測試假人] 攻擊清單為空，請在 Inspector 拖入 EnemyAttackProfile 資產", this);
                return;
            }
            EnemyAttackProfile chosen = PickNextAttack();
            if (chosen == null)
            {
                Debug.LogWarning("[測試假人] 選到的攻擊資產為空（清單中有 None 元素），跳過", this);
                return;
            }
            Debug.Log($"[測試假人] 選擇攻擊：{chosen.AttackName}", this);
            _executor.Execute(chosen);
        }

        private EnemyAttackProfile PickNextAttack()
        {
            if (_selectMode == AttackSelectMode.Random)
            {
                int index = Random.Range(0, _attackPool.Count);
                return _attackPool[index];
            }
            EnemyAttackProfile profile = _attackPool[_sequenceIndex % _attackPool.Count];
            _sequenceIndex++;
            return profile;
        }
    }

    /// <summary>
    /// 測試假人挑選下一招的方式。
    /// </summary>
    public enum AttackSelectMode
    {
        Random = 0,
        Sequential = 1,
    }
}
